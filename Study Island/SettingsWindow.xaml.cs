using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Orbital;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<ScheduleEntry> _schedule = [];
    private readonly ObservableCollection<SubjectConfig> _subjects = [];

    private int _selectedDay; // 0=Mon..6=Sun
    private int _selectedWeek; // 0-based
    private ObservableCollection<ScheduleEntry> _filteredSchedule = [];

    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    /// <summary>Raised after the user clicks Save so the main window can refresh.</summary>
    public event Action? SettingsSaved;

    public SettingsWindow()
    {
        InitializeComponent();

        var s = SettingsManager.Current;

        // Appearance
        SelectCombo(ThemeCombo, s.Theme);
        AccentBox.Text = s.AccentColor;
        SelectCombo(ShapeCombo, s.IslandShape);
        CornerRadiusBox.Text = s.IslandCornerRadius.ToString();
        ScaleBox.Text = s.IslandScale.ToString("0.##");
        OutlineColorBox.Text = s.OutlineColor;
        BgImageBox.Text = s.BackgroundImagePath;

        // Position
        SelectCombo(PositionCombo, s.IslandPosition);
        CustomTopBox.Text = s.CustomTop.ToString("0.##");
        CustomLeftBox.Text = s.CustomLeft.ToString("0.##");
        UpdateCustomPositionVisibility();
        WidthBox.Text = s.IslandWidth.ToString();
        HeaderHeightBox.Text = s.WorkspaceHeaderHeight.ToString();
        BlendIslandCheck.IsChecked = s.BlendIslandIntoWorkspace;

        // Auto-hide
        AutoHideCheck.IsChecked = s.AutoHide;
        SelectCombo(AutoHideEdgeCombo, s.AutoHideEdge);

        // System info
        SysRefreshBox.Text = s.SysInfoRefreshSeconds.ToString();

        // Startup
        StartOnBootCheck.IsChecked = s.StartOnBoot;

        // Schedule
        foreach (var e in s.Schedule) _schedule.Add(e);

        int cycleIdx = Math.Clamp(s.ScheduleCycleWeeks - 1, 0, 2);
        CycleWeeksCombo.SelectedIndex = cycleIdx;
        BuildDayButtons();
        UpdateWeekButtonVisibility();
        _selectedDay = 0;
        _selectedWeek = 0;
        RefreshScheduleFilter();

        // Subjects
        foreach (var sub in s.Subjects) _subjects.Add(sub);
        SubjectList.ItemsSource = _subjects;
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static void SelectCombo(ComboBox cb, string value)
    {
        for (int i = 0; i < cb.Items.Count; i++)
        {
            if (cb.Items[i] is ComboBoxItem item && item.Content?.ToString() == value)
            { cb.SelectedIndex = i; return; }
        }
        cb.SelectedIndex = 0;
    }

    private static string ComboValue(ComboBox cb) =>
        (cb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

    // ── Schedule (day-based, multi-week) ─────────────────────────

    private void BuildDayButtons()
    {
        DayButtonsPanel.Children.Clear();
        for (int d = 0; d < 7; d++)
        {
            int day = d;
            var btn = new Button
            {
                Content = DayNames[d],
                Background = new SolidColorBrush(d == _selectedDay ? Color.FromRgb(74, 144, 217) : Color.FromRgb(51, 51, 51)),
                Foreground = Brushes.White,
                Tag = d,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 4, 0),
                Style = (Style)FindResource("SmallBtn"),
            };
            btn.Click += DayBtn_Click;
            DayButtonsPanel.Children.Add(btn);
        }
    }

    private void BuildWeekButtons()
    {
        WeekButtonsPanel.Children.Clear();
        int weeks = GetCycleWeeks();
        for (int w = 0; w < weeks; w++)
        {
            int week = w;
            var btn = new Button
            {
                Content = $"Week {w + 1}",
                Background = new SolidColorBrush(w == _selectedWeek ? Color.FromRgb(74, 144, 217) : Color.FromRgb(51, 51, 51)),
                Foreground = Brushes.White,
                Tag = w,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 4, 0),
                Style = (Style)FindResource("SmallBtn"),
            };
            btn.Click += WeekBtn_Click;
            WeekButtonsPanel.Children.Add(btn);
        }
    }

    private int GetCycleWeeks() => CycleWeeksCombo.SelectedIndex + 1;

    private void DayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int d })
        {
            _selectedDay = d;
            HighlightDayButton();
            RefreshScheduleFilter();
        }
    }

    private void WeekBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int w })
        {
            _selectedWeek = w;
            HighlightWeekButton();
            RefreshScheduleFilter();
        }
    }

    private void CycleWeeks_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _selectedWeek = 0;
        UpdateWeekButtonVisibility();
        RefreshScheduleFilter();
    }

    private void HighlightDayButton()
    {
        var active = new SolidColorBrush(Color.FromRgb(74, 144, 217));
        var inactive = new SolidColorBrush(Color.FromRgb(51, 51, 51));
        foreach (var child in DayButtonsPanel.Children)
        {
            if (child is Button b)
                b.Background = (int)b.Tag == _selectedDay ? active : inactive;
        }
    }

    private void HighlightWeekButton()
    {
        var active = new SolidColorBrush(Color.FromRgb(74, 144, 217));
        var inactive = new SolidColorBrush(Color.FromRgb(51, 51, 51));
        foreach (var child in WeekButtonsPanel.Children)
        {
            if (child is Button b)
                b.Background = (int)b.Tag == _selectedWeek ? active : inactive;
        }
    }

    private void UpdateWeekButtonVisibility()
    {
        int weeks = GetCycleWeeks();
        if (weeks > 1)
        {
            WeekButtonsPanel.Visibility = Visibility.Visible;
            BuildWeekButtons();
        }
        else
        {
            WeekButtonsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshScheduleFilter()
    {
        _filteredSchedule = new ObservableCollection<ScheduleEntry>(
            _schedule.Where(e => e.Day == _selectedDay && e.Week == _selectedWeek));
        ScheduleList.ItemsSource = _filteredSchedule;
    }

    private void AddSchedule_Click(object sender, RoutedEventArgs e)
    {
        var entry = new ScheduleEntry
        {
            Label = "Period " + (_filteredSchedule.Count + 1),
            Day = _selectedDay,
            Week = _selectedWeek,
            StartTime = "8:00 AM",
            EndTime = "8:50 AM",
        };
        _schedule.Add(entry);
        _filteredSchedule.Add(entry);
    }

    private void RemoveSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScheduleEntry entry })
        {
            _schedule.Remove(entry);
            _filteredSchedule.Remove(entry);
        }
    }

    // ── Subjects ─────────────────────────────────────────────────

    private void AddSubject_Click(object sender, RoutedEventArgs e) =>
        _subjects.Add(new SubjectConfig());

    private void RemoveSubject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SubjectConfig sub })
            _subjects.Remove(sub);
    }

    private void AddPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SubjectConfig sub })
        {
            sub.Panels.Add(new PanelConfig());
            SubjectList.ItemsSource = null;
            SubjectList.ItemsSource = _subjects;
        }
    }

    private void RemovePanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PanelConfig panel })
        {
            foreach (var sub in _subjects)
                sub.Panels.Remove(panel);
            SubjectList.ItemsSource = null;
            SubjectList.ItemsSource = _subjects;
        }
    }

    // ── Browse background image ──────────────────────────────────

    private void BrowseBgImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*",
            Title = "Select Background Image"
        };
        if (dlg.ShowDialog() == true)
            BgImageBox.Text = dlg.FileName;
    }

    // ── Save / Close ─────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsManager.Current;

        s.Theme = ComboValue(ThemeCombo);
        s.AccentColor = AccentBox.Text.Trim();
        s.IslandShape = ComboValue(ShapeCombo);
        double.TryParse(CornerRadiusBox.Text, out double cr); s.IslandCornerRadius = cr;
        double.TryParse(ScaleBox.Text, out double scale); s.IslandScale = scale > 0 ? scale : 1.0;
        s.OutlineColor = OutlineColorBox.Text.Trim();
        s.BackgroundImagePath = BgImageBox.Text.Trim();

        s.IslandPosition = ComboValue(PositionCombo);
        if (s.IslandPosition == "Custom")
        {
            double.TryParse(CustomTopBox.Text, out double ct); s.CustomTop = ct;
            double.TryParse(CustomLeftBox.Text, out double cl); s.CustomLeft = cl;
        }
        double.TryParse(WidthBox.Text, out double w); s.IslandWidth = w > 0 ? w : 360;
        double.TryParse(HeaderHeightBox.Text, out double hh); s.WorkspaceHeaderHeight = hh > 0 ? hh : 32;
        s.BlendIslandIntoWorkspace = BlendIslandCheck.IsChecked == true;

        s.AutoHide = AutoHideCheck.IsChecked == true;
        s.AutoHideEdge = ComboValue(AutoHideEdgeCombo);

        s.StartOnBoot = StartOnBootCheck.IsChecked == true;

        int.TryParse(SysRefreshBox.Text, out int sysRefresh);
        s.SysInfoRefreshSeconds = sysRefresh > 0 ? sysRefresh : 3;

        s.ScheduleCycleWeeks = GetCycleWeeks();
        s.Schedule = [.. _schedule];
        s.Subjects = [.. _subjects];

        SettingsManager.Save();
        SettingsSaved?.Invoke();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void PositionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateCustomPositionVisibility();
    }

    private void UpdateCustomPositionVisibility()
    {
        if (CustomPositionPanel is null) return;
        CustomPositionPanel.Visibility = ComboValue(PositionCombo) == "Custom"
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
