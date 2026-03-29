using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace Orbital;

/// <summary>
/// A single browser tab inside a workspace panel.
/// </summary>
public class BrowserTab
{
    public string Title { get; set; } = "Tab";
    public string Url { get; set; } = "";
    public WebView2? WebView { get; set; }
    public Button? HeaderButton { get; set; }
    public bool HasNavigated { get; set; }
}

/// <summary>
/// Runtime state for one workspace panel (may contain multiple tabs).
/// </summary>
public class WorkspacePanel
{
    public string Title { get; set; } = "";
    public List<BrowserTab> Tabs { get; } = [];
    public int ActiveTabIndex { get; set; }
    public bool IsFolded { get; set; }
    public int LayoutGroup { get; set; }
    public Border? Container { get; set; }
    public Grid? ContentGrid { get; set; }
    public StackPanel? TabBar { get; set; }
    /// <summary>True = fold to narrow column (LeftRight), False = fold to narrow row (TopBottom/Mixed)</summary>
    public bool FoldVertical { get; set; }
    /// <summary>The ColumnDefinition or RowDefinition controlling this panel's size in the grid.</summary>
    public DefinitionBase? SizingDefinition { get; set; }
}

public partial class WorkspaceWindow : Window
{
    private readonly SubjectConfig _subject;
    private readonly List<WorkspacePanel> _panels = [];
    private readonly DispatcherTimer _headerClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _isLight;

    // Theme colors
    private Color _panelBg;
    private Color _panelHeaderBg;
    private Color _fgPrimary;
    private Color _fgMuted;
    private Color _accentColor;

    public WorkspaceWindow(SubjectConfig subject)
    {
        InitializeComponent();
        _subject = subject;
        SubjectTitle.Text = subject.Name;

        var s = SettingsManager.Current;
        var hh = s.WorkspaceHeaderHeight;
        HeaderRow.Height = new GridLength(Math.Max(24, hh));

        ApplyThemeColors();

        foreach (var p in subject.Panels)
        {
            var panel = new WorkspacePanel { Title = p.Title, IsFolded = p.IsFolded, LayoutGroup = p.LayoutGroup };
            panel.Tabs.Add(new BrowserTab { Title = p.Title, Url = p.Url });
            _panels.Add(panel);
        }

        // Header clock for blended island info
        _headerClock.Tick += (_, _) => UpdateHeaderInfo();
        _headerClock.Start();
        UpdateHeaderInfo();

        RebuildLayout();
    }

    private void ApplyThemeColors()
    {
        var s = SettingsManager.Current;
        _isLight = s.Theme == "Light";
        bool light = _isLight;

        _panelBg = light ? Color.FromRgb(245, 245, 245) : Color.FromRgb(26, 26, 26);
        _panelHeaderBg = light ? Color.FromRgb(230, 230, 230) : Color.FromRgb(34, 34, 34);
        _fgPrimary = light ? Colors.Black : Colors.White;
        _fgMuted = light ? Color.FromRgb(120, 120, 120) : Color.FromRgb(170, 170, 170);

        try { _accentColor = (Color)ColorConverter.ConvertFromString(s.AccentColor)!; }
        catch { _accentColor = Color.FromRgb(0, 255, 136); }

        var windowBg = light ? Color.FromRgb(235, 235, 235) : Color.FromRgb(13, 13, 13);
        var titleBg = light ? Color.FromRgb(225, 225, 225) : Color.FromRgb(21, 21, 21);

        Background = new SolidColorBrush(windowBg);
        TitleBarBorder.Background = new SolidColorBrush(titleBg);
        SubjectTitle.Foreground = new SolidColorBrush(_fgPrimary);
        HeaderClockText.Foreground = new SolidColorBrush(light ? Color.FromRgb(80, 80, 80) : Color.FromRgb(204, 204, 204));
        HeaderTimerText.Foreground = new SolidColorBrush(_fgMuted);

        // Title bar buttons
        var titleBtnBg = new SolidColorBrush(light ? Color.FromRgb(200, 200, 200) : Color.FromRgb(51, 51, 51));
        AddPanelBtn.Background = titleBtnBg;
        AddPanelBtn.Foreground = new SolidColorBrush(_fgPrimary);
        MaximizeBtn.Background = titleBtnBg;
        MaximizeBtn.Foreground = new SolidColorBrush(_fgPrimary);
    }

    private void UpdateHeaderInfo()
    {
        HeaderClockText.Text = DateTime.Now.ToString("h:mm tt");

        var nowDt = DateTime.Now;
        var now = TimeOnly.FromDateTime(nowDt);
        int cycleWeeks = Math.Max(1, SettingsManager.Current.ScheduleCycleWeeks);
        int dayIndex = nowDt.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)nowDt.DayOfWeek - 1;
        int weekInCycle = (System.Globalization.ISOWeek.GetWeekOfYear(nowDt) - 1) % cycleWeeks;

        var todaySchedule = SettingsManager.Current.Schedule
            .Where(e => e.Day == dayIndex && e.Week == weekInCycle
                     && e.ParsedStart.HasValue && e.ParsedEnd.HasValue)
            .OrderBy(e => e.ParsedStart!.Value);

        var current = todaySchedule
            .FirstOrDefault(e => e.ParsedStart!.Value <= now && now < e.ParsedEnd!.Value);
        if (current is not null)
        {
            var left = current.ParsedEnd!.Value.ToTimeSpan() - now.ToTimeSpan();
            HeaderTimerText.Text = $"{current.Label} · {(int)left.TotalMinutes}:{left.Seconds:D2} left";
        }
        else
        {
            var next = todaySchedule
                .FirstOrDefault(e => e.ParsedStart!.Value > now);
            HeaderTimerText.Text = next is not null ? $"Next: {next.Label}" : "";
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  DYNAMIC LAYOUT – supports pure LeftRight, TopBottom, and
    //  Mixed (columns where panels share a LayoutGroup stack vertically)
    // ══════════════════════════════════════════════════════════════

    private void RebuildLayout()
    {
        DetachWebViews();
        PanelHost.Children.Clear();
        PanelHost.ColumnDefinitions.Clear();
        PanelHost.RowDefinitions.Clear();

        string orientation = _subject.SplitOrientation;

        if (orientation == "Mixed")
        {
            BuildMixedLayout();
        }
        else if (orientation == "TopBottom")
        {
            BuildLinearLayout(horizontal: false);
        }
        else
        {
            BuildLinearLayout(horizontal: true);
        }
    }

    private void BuildLinearLayout(bool horizontal)
    {
        int visibleCount = 0;
        for (int i = 0; i < _panels.Count; i++)
        {
            var panel = _panels[i];
            panel.FoldVertical = horizontal;

            if (visibleCount > 0)
            {
                if (horizontal)
                {
                    PanelHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
                    var splitter = new GridSplitter
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Background = Brushes.Transparent
                    };
                    Grid.SetColumn(splitter, PanelHost.ColumnDefinitions.Count - 1);
                    PanelHost.Children.Add(splitter);
                }
                else
                {
                    PanelHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
                    var splitter = new GridSplitter
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Background = Brushes.Transparent
                    };
                    Grid.SetRow(splitter, PanelHost.RowDefinitions.Count - 1);
                    PanelHost.Children.Add(splitter);
                }
            }

            var border = BuildPanelUI(panel);

            if (horizontal)
            {
                var colDef = panel.IsFolded
                    ? new ColumnDefinition { Width = new GridLength(36), MinWidth = 36 }
                    : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 };
                PanelHost.ColumnDefinitions.Add(colDef);
                panel.SizingDefinition = colDef;
                Grid.SetColumn(border, PanelHost.ColumnDefinitions.Count - 1);
            }
            else
            {
                var rowDef = panel.IsFolded
                    ? new RowDefinition { Height = new GridLength(32), MinHeight = 32 }
                    : new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 };
                PanelHost.RowDefinitions.Add(rowDef);
                panel.SizingDefinition = rowDef;
                Grid.SetRow(border, PanelHost.RowDefinitions.Count - 1);
            }

            PanelHost.Children.Add(border);
            visibleCount++;
        }
    }

    /// <summary>
    /// Mixed layout: panels are grouped by LayoutGroup into columns.
    /// Panels sharing the same group stack vertically within that column.
    /// </summary>
    private void BuildMixedLayout()
    {
        var groups = _panels.GroupBy(p => p.LayoutGroup).OrderBy(g => g.Key).ToList();
        int colIdx = 0;

        foreach (var group in groups)
        {
            if (colIdx > 0)
            {
                PanelHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
                var splitter = new GridSplitter
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = Brushes.Transparent
                };
                Grid.SetColumn(splitter, PanelHost.ColumnDefinitions.Count - 1);
                PanelHost.Children.Add(splitter);
            }

            var panelsInGroup = group.ToList();

            if (panelsInGroup.Count == 1)
            {
                var panel = panelsInGroup[0];
                panel.FoldVertical = true;
                var colDef = panel.IsFolded
                    ? new ColumnDefinition { Width = new GridLength(36), MinWidth = 36 }
                    : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 };
                PanelHost.ColumnDefinitions.Add(colDef);
                panel.SizingDefinition = colDef;
                int thisCol = PanelHost.ColumnDefinitions.Count - 1;

                var border = BuildPanelUI(panel);
                Grid.SetColumn(border, thisCol);
                PanelHost.Children.Add(border);
            }
            else
            {
                PanelHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
                int thisCol = PanelHost.ColumnDefinitions.Count - 1;

                var innerGrid = new Grid();
                int rowIdx = 0;
                foreach (var panel in panelsInGroup)
                {
                    panel.FoldVertical = false;

                    if (rowIdx > 0)
                    {
                        innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
                        var rowSplitter = new GridSplitter
                        {
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Background = Brushes.Transparent
                        };
                        Grid.SetRow(rowSplitter, innerGrid.RowDefinitions.Count - 1);
                        innerGrid.Children.Add(rowSplitter);
                    }

                    var rowDef = panel.IsFolded
                        ? new RowDefinition { Height = new GridLength(32), MinHeight = 32 }
                        : new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 };
                    innerGrid.RowDefinitions.Add(rowDef);
                    panel.SizingDefinition = rowDef;

                    var border = BuildPanelUI(panel);
                    Grid.SetRow(border, innerGrid.RowDefinitions.Count - 1);
                    innerGrid.Children.Add(border);
                    rowIdx++;
                }

                Grid.SetColumn(innerGrid, thisCol);
                PanelHost.Children.Add(innerGrid);
            }
            colIdx++;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  BUILD ONE PANEL (header bar + tab strip + WebView area)
    // ══════════════════════════════════════════════════════════════

    private Border BuildPanelUI(WorkspacePanel panel)
    {
        var container = new Border
        {
            Background = new SolidColorBrush(_panelBg),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Margin = new Thickness(2),
        };

        // Accent outline if configured
        if (!string.IsNullOrWhiteSpace(SettingsManager.Current.OutlineColor))
        {
            try
            {
                container.BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(SettingsManager.Current.OutlineColor)!);
                container.BorderThickness = new Thickness(1);
            }
            catch { }
        }

        panel.Container = container;

        if (panel.IsFolded)
        {
            container.Child = BuildFoldedContent(panel);
            return container;
        }

        var outerGrid = new Grid();
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header

        // ── Panel header (28px) ──
        var header = new Border
        {
            Background = new SolidColorBrush(_panelHeaderBg),
            Padding = new Thickness(8, 3, 8, 3),
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Title + editable URL bar
        var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var titleTb = new TextBlock
        {
            Text = panel.Title,
            Foreground = new SolidColorBrush(_fgMuted),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        headerLeft.Children.Add(titleTb);

        // URL box
        bool light = SettingsManager.Current.Theme == "Light";
        var urlBorderBg = light ? Color.FromRgb(210, 210, 210) : Color.FromRgb(51, 51, 51);
        var urlBorder = new Border
        {
            Background = new SolidColorBrush(urlBorderBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
        };
        var urlFg = light ? Brushes.Black : Brushes.White;
        var urlBox = new TextBox
        {
            Width = 200, Height = 20, FontSize = 10,
            Background = Brushes.Transparent, Foreground = urlFg,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0, 4, 0),
            CaretBrush = urlFg,
            Text = panel.Tabs.Count > 0 ? panel.Tabs[Math.Min(panel.ActiveTabIndex, panel.Tabs.Count - 1)].Url : "",
        };
        urlBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                string url = urlBox.Text.Trim();
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    url = "https://" + url;
                var tab = panel.Tabs[panel.ActiveTabIndex];
                tab.Url = url;
                tab.WebView?.CoreWebView2?.Navigate(url);
            }
        };
        urlBorder.Child = urlBox;
        headerLeft.Children.Add(urlBorder);
        Grid.SetColumn(headerLeft, 0);
        headerGrid.Children.Add(headerLeft);

        // Buttons: + tab, fold, open file, remove
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var addTabBtn = MakeThemedButton("+");
        addTabBtn.ToolTip = "Add tab";
        addTabBtn.Click += (_, _) =>
        {
            panel.Tabs.Add(new BrowserTab { Title = $"Tab {panel.Tabs.Count + 1}", Url = "https://www.google.com" });
            panel.ActiveTabIndex = panel.Tabs.Count - 1;
            RebuildSinglePanel(panel);
        };
        btnPanel.Children.Add(addTabBtn);

        var openBtn = MakeThemedButton("📂");
        openBtn.ToolTip = "Open PDF / file";
        openBtn.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "PDF Files|*.pdf|All Files|*.*", Title = "Open File" };
            if (dlg.ShowDialog() == true)
            {
                var tab = panel.Tabs[panel.ActiveTabIndex];
                tab.Url = new Uri(dlg.FileName).AbsoluteUri;
                tab.WebView?.CoreWebView2?.Navigate(tab.Url);
                urlBox.Text = tab.Url;
            }
        };
        btnPanel.Children.Add(openBtn);

        var launchAppBtn = MakeThemedButton("🚀");
        launchAppBtn.ToolTip = "Embed external application";
        launchAppBtn.Click += async (_, _) =>
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Applications|*.exe;*.lnk|All Files|*.*",
                Title = "Select application to embed"
            };
            if (dlg.ShowDialog() == true)
                await EmbedAppInPanel(panel, dlg.FileName);
        };
        btnPanel.Children.Add(launchAppBtn);

        var shortcutsBtn = MakeThemedButton("📌");
        shortcutsBtn.ToolTip = "Open from shortcuts";
        shortcutsBtn.Click += (_, _) =>
        {
            var menu = new System.Windows.Controls.ContextMenu();
            foreach (var sc in SettingsManager.Current.PermanentShortcuts)
            {
                var item = new System.Windows.Controls.MenuItem { Header = $"{sc.Name} ({sc.Kind})", Tag = sc };
                item.Click += async (s, _) =>
                {
                    if (s is System.Windows.Controls.MenuItem { Tag: ShortcutItem si })
                    {
                        if (si.Kind == "App" || si.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            await EmbedAppInPanel(panel, si.Path);
                        else if (si.Kind == "Url" || si.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            panel.Tabs.Add(new BrowserTab { Title = si.Name, Url = si.Path });
                            panel.ActiveTabIndex = panel.Tabs.Count - 1;
                            RebuildSinglePanel(panel);
                            var tab2 = panel.Tabs[panel.ActiveTabIndex];
                            if (tab2.WebView is not null) await InitSingleWebViewAsync(tab2);
                        }
                        else
                        {
                            string fileUrl = new Uri(si.Path).AbsoluteUri;
                            panel.Tabs.Add(new BrowserTab { Title = si.Name, Url = fileUrl });
                            panel.ActiveTabIndex = panel.Tabs.Count - 1;
                            RebuildSinglePanel(panel);
                            var tab2 = panel.Tabs[panel.ActiveTabIndex];
                            if (tab2.WebView is not null) await InitSingleWebViewAsync(tab2);
                        }
                    }
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
                menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "(no shortcuts saved)", IsEnabled = false });
            shortcutsBtn.ContextMenu = menu;
            menu.IsOpen = true;
        };
        btnPanel.Children.Add(shortcutsBtn);

        var foldBtn = MakeThemedButton("◀");
        foldBtn.ToolTip = "Fold";
        foldBtn.Click += (_, _) => ToggleFold(panel);
        btnPanel.Children.Add(foldBtn);

        var splitBtn = MakeThemedButton("↕");
        splitBtn.ToolTip = "Split vertically (add panel below in same column)";
        splitBtn.Click += (_, _) =>
        {
            var newPanel = new WorkspacePanel
            {
                Title = "New Panel",
                LayoutGroup = panel.LayoutGroup,
                Tabs = { new BrowserTab { Title = "Google", Url = "https://www.google.com" } }
            };
            int idx = _panels.IndexOf(panel);
            _panels.Insert(idx + 1, newPanel);
            // Auto-switch to Mixed if currently linear
            if (_subject.SplitOrientation is "LeftRight" or "TopBottom")
                _subject.SplitOrientation = "Mixed";
            RebuildLayout();
            InitAllWebViews();
        };
        btnPanel.Children.Add(splitBtn);

        var removeBtn = MakeThemedButton("✕", isDestructive: true);
        removeBtn.ToolTip = "Remove panel";
        removeBtn.Click += (_, _) =>
        {
            if (_panels.Count <= 1) return;
            _panels.Remove(panel);
            RebuildLayout();
            InitAllWebViews();
        };
        btnPanel.Children.Add(removeBtn);

        Grid.SetColumn(btnPanel, 1);
        headerGrid.Children.Add(btnPanel);
        header.Child = headerGrid;
        Grid.SetRow(header, 0);
        outerGrid.Children.Add(header);

        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Tab strip + content ──
        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // tab bar
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // webview
        panel.ContentGrid = contentGrid;

        // Tab bar
        var tabBarBg = light ? Color.FromRgb(235, 235, 235) : Color.FromRgb(28, 28, 28);
        var tabBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(tabBarBg),
        };
        panel.TabBar = tabBar;

        for (int t = 0; t < panel.Tabs.Count; t++)
        {
            int idx = t;
            var tab = panel.Tabs[t];
            bool isActive = idx == panel.ActiveTabIndex;

            var tabGrid = new Grid();
            tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var activeBg = light ? Color.FromRgb(245, 245, 245) : Color.FromRgb(42, 42, 42);
            var inactiveBg = tabBarBg;

            var tabBtn = new Button
            {
                Content = tab.Title,
                Background = new SolidColorBrush(isActive ? activeBg : inactiveBg),
                Foreground = new SolidColorBrush(isActive ? _fgPrimary : _fgMuted),
                FontSize = 10, Cursor = Cursors.Hand, Padding = new Thickness(8, 3, 4, 3),
                BorderThickness = new Thickness(0),
            };
            tabBtn.Template = MakeTabBtnTemplate(isActive);
            tab.HeaderButton = tabBtn;
            tabBtn.Click += (_, _) =>
            {
                panel.ActiveTabIndex = idx;
                RebuildSinglePanel(panel);
            };
            Grid.SetColumn(tabBtn, 0);
            tabGrid.Children.Add(tabBtn);

            if (panel.Tabs.Count > 1)
            {
                var closeTab = new Button
                {
                    Content = "✕", FontSize = 8, Cursor = Cursors.Hand,
                    Background = Brushes.Transparent, Foreground = new SolidColorBrush(_fgMuted),
                    BorderThickness = new Thickness(0), Padding = new Thickness(3, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                closeTab.Click += (_, _) =>
                {
                    panel.Tabs.RemoveAt(idx);
                    if (panel.ActiveTabIndex >= panel.Tabs.Count) panel.ActiveTabIndex = panel.Tabs.Count - 1;
                    RebuildSinglePanel(panel);
                };
                Grid.SetColumn(closeTab, 1);
                tabGrid.Children.Add(closeTab);
            }

            var tabContainer = new Border { Margin = new Thickness(1, 0, 0, 0) };
            tabContainer.Child = tabGrid;
            tabBar.Children.Add(tabContainer);
        }

        Grid.SetRow(tabBar, 0);
        contentGrid.Children.Add(tabBar);

        // Active WebView — reuse existing instance to avoid flash
        var activeTab = panel.Tabs[Math.Min(panel.ActiveTabIndex, panel.Tabs.Count - 1)];
        var wv = activeTab.WebView ?? new WebView2();
        activeTab.WebView = wv;
        Grid.SetRow(wv, 1);
        contentGrid.Children.Add(wv);

        Grid.SetRow(contentGrid, 1);
        outerGrid.Children.Add(contentGrid);

        container.Child = outerGrid;
        return container;
    }

    // ══════════════════════════════════════════════════════════════
    //  WEBVIEW INIT – called after layout rebuild
    // ══════════════════════════════════════════════════════════════

    private async void InitAllWebViews()
    {
        foreach (var panel in _panels)
        {
            if (panel.IsFolded) continue;
            var tab = panel.Tabs[Math.Min(panel.ActiveTabIndex, panel.Tabs.Count - 1)];
            if (tab.WebView is null || tab.HasNavigated) continue;
            await InitSingleWebViewAsync(tab);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Detach all WebView2 controls from their parent containers before rebuilding layout.
    /// This prevents them from being destroyed and recreated.
    /// </summary>
    private void DetachWebViews()
    {
        foreach (var panel in _panels)
        {
            foreach (var tab in panel.Tabs)
            {
                if (tab.WebView?.Parent is Panel p)
                    p.Children.Remove(tab.WebView);
                else if (tab.WebView?.Parent is Decorator d)
                    d.Child = null;
                else if (tab.WebView?.Parent is ContentControl cc)
                    cc.Content = null;
            }
        }
    }

    private void ToggleFold(WorkspacePanel panel)
    {
        foreach (var tab in panel.Tabs)
        {
            if (tab.WebView?.Parent is Panel p) p.Children.Remove(tab.WebView);
            else if (tab.WebView?.Parent is Decorator d) d.Child = null;
        }

        panel.IsFolded = !panel.IsFolded;

        var existing = panel.Container;
        var rebuilt = BuildPanelUI(panel);
        var innerContent = rebuilt.Child;
        if (existing is not null && innerContent is not null)
        {
            rebuilt.Child = null;
            existing.Child = null;
            existing.Child = innerContent;
            panel.Container = existing;
        }

        // Resize the column/row definition so other panels stretch to fill
        if (panel.FoldVertical && panel.SizingDefinition is ColumnDefinition cd)
        {
            cd.Width = panel.IsFolded ? new GridLength(36) : new GridLength(1, GridUnitType.Star);
            cd.MinWidth = panel.IsFolded ? 36 : 120;
        }
        else if (!panel.FoldVertical && panel.SizingDefinition is RowDefinition rd)
        {
            rd.Height = panel.IsFolded ? new GridLength(32) : new GridLength(1, GridUnitType.Star);
            rd.MinHeight = panel.IsFolded ? 32 : 80;
        }

        if (!panel.IsFolded)
        {
            var tab = panel.Tabs[Math.Min(panel.ActiveTabIndex, panel.Tabs.Count - 1)];
            if (tab.WebView is not null) _ = InitSingleWebViewAsync(tab);
        }
    }

    private void RebuildSinglePanel(WorkspacePanel panel)
    {
        foreach (var tab in panel.Tabs)
        {
            if (tab.WebView?.Parent is Panel p) p.Children.Remove(tab.WebView);
            else if (tab.WebView?.Parent is Decorator d) d.Child = null;
        }

        var existing = panel.Container;
        var rebuilt = BuildPanelUI(panel);
        var innerContent = rebuilt.Child;
        if (existing is not null && innerContent is not null)
        {
            rebuilt.Child = null;
            existing.Child = null;
            existing.Child = innerContent;
            panel.Container = existing;
        }

        if (!panel.IsFolded)
        {
            var tab = panel.Tabs[Math.Min(panel.ActiveTabIndex, panel.Tabs.Count - 1)];
            if (tab.WebView is not null) _ = InitSingleWebViewAsync(tab);
        }
    }

    private UIElement BuildFoldedContent(WorkspacePanel panel)
    {
        if (panel.FoldVertical)
        {
            // Vertical sidebar strip for horizontal layouts
            var grid = new Grid { Background = new SolidColorBrush(_panelHeaderBg) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var unfoldBtn = MakeThemedButton("▶");
            unfoldBtn.ToolTip = "Unfold";
            unfoldBtn.Margin = new Thickness(3, 6, 3, 4);
            unfoldBtn.Click += (_, _) => ToggleFold(panel);
            grid.Children.Add(unfoldBtn);

            var title = new TextBlock
            {
                Text = panel.Title,
                Foreground = new SolidColorBrush(_fgMuted),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 4),
            };
            title.LayoutTransform = new RotateTransform(90);
            Grid.SetRow(title, 1);
            grid.Children.Add(title);
            return grid;
        }
        else
        {
            // Horizontal strip for vertical layouts
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush(_panelHeaderBg),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var unfoldBtn = MakeThemedButton("▼");
            unfoldBtn.ToolTip = "Unfold";
            unfoldBtn.Margin = new Thickness(6, 3, 4, 3);
            unfoldBtn.Click += (_, _) => ToggleFold(panel);
            sp.Children.Add(unfoldBtn);

            var title = new TextBlock
            {
                Text = panel.Title,
                Foreground = new SolidColorBrush(_fgMuted),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            sp.Children.Add(title);
            return sp;
        }
    }

    private async Task InitSingleWebViewAsync(BrowserTab tab)
    {
        if (tab.WebView is null || tab.HasNavigated) return;
        try
        {
            await tab.WebView.EnsureCoreWebView2Async();
            if (!string.IsNullOrWhiteSpace(tab.Url) && tab.WebView.CoreWebView2 is not null)
            {
                string url = tab.Url;
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("file", StringComparison.OrdinalIgnoreCase))
                {
                    try { url = new Uri(url).AbsoluteUri; } catch { url = "https://" + url; }
                }
                tab.WebView.CoreWebView2.Navigate(url);
                tab.HasNavigated = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView2 init error: {ex.Message}");
        }
    }

    private Button MakeThemedButton(string text, bool isDestructive = false)
    {
        var bgColor = isDestructive
            ? Color.FromRgb(153, 68, 68)
            : (_isLight ? Color.FromRgb(210, 210, 210) : Color.FromRgb(68, 68, 68));
        var fgBrush = _isLight && !isDestructive
            ? new SolidColorBrush(Colors.Black)
            : Brushes.White;

        var btn = new Button
        {
            Content = text,
            Background = new SolidColorBrush(bgColor),
            Foreground = fgBrush,
            FontSize = 10, Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 0, 0),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(5, 2, 5, 2),
        };
        btn.Template = CreateRoundedButtonTemplate();
        return btn;
    }

    private static ControlTemplate CreateRoundedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.PaddingProperty, new Thickness(5, 2, 5, 2));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        template.VisualTree = border;
        return template;
    }

    private ControlTemplate MakeTabBtnTemplate(bool active)
    {
        bool light = SettingsManager.Current.Theme == "Light";
        var activeBg = light ? Color.FromRgb(245, 245, 245) : Color.FromRgb(42, 42, 42);
        var inactiveBg = light ? Color.FromRgb(235, 235, 235) : Color.FromRgb(28, 28, 28);

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty,
            new SolidColorBrush(active ? activeBg : inactiveBg));
        border.SetValue(Border.PaddingProperty, new Thickness(8, 3, 4, 3));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4, 4, 0, 0));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(cp);
        template.VisualTree = border;
        return template;
    }

    // ══════════════════════════════════════════════════════════════
    //  EMBED EXTERNAL APPLICATION
    // ══════════════════════════════════════════════════════════════

    private async Task EmbedAppInPanel(WorkspacePanel panel, string exePath)
    {
        // Replace the current WebView content with an embedded app host
        if (panel.ContentGrid is null) return;

        // Remove existing content in row 1
        var toRemove = panel.ContentGrid.Children.Cast<UIElement>()
            .Where(c => Grid.GetRow(c) == 1).ToList();
        foreach (var c in toRemove) panel.ContentGrid.Children.Remove(c);

        var host = new NativeAppHost { MinHeight = 100, MinWidth = 100 };
        Grid.SetRow(host, 1);
        panel.ContentGrid.Children.Add(host);

        bool success = await host.EmbedProcessAsync(exePath);
        if (!success)
        {
            panel.ContentGrid.Children.Remove(host);
            // Rebuild WebView content as fallback
            RebuildSinglePanel(panel);
            if (!panel.IsFolded)
            {
                var tab = panel.Tabs[Math.Min(panel.ActiveTabIndex, panel.Tabs.Count - 1)];
                if (tab.WebView is not null) _ = InitSingleWebViewAsync(tab);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  WINDOW EVENTS
    // ══════════════════════════════════════════════════════════════

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void AddPanel_Click(object sender, RoutedEventArgs e)
    {
        int maxGroup = _panels.Count > 0 ? _panels.Max(p => p.LayoutGroup) : 0;
        _panels.Add(new WorkspacePanel
        {
            Title = "Reference",
            LayoutGroup = maxGroup + 1,
            Tabs = { new BrowserTab { Title = "Google", Url = "https://www.google.com" } }
        });
        RebuildLayout();
        InitAllWebViews();
    }

    private async void PanelHost_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            string file = files[0];
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();

            // If it's an exe, embed it in a new panel
            if (ext is ".exe" or ".lnk")
            {
                int maxGroup = _panels.Count > 0 ? _panels.Max(p => p.LayoutGroup) : 0;
                var panel = new WorkspacePanel
                {
                    Title = System.IO.Path.GetFileNameWithoutExtension(file),
                    LayoutGroup = maxGroup + 1,
                    Tabs = { new BrowserTab { Title = "App", Url = "" } }
                };
                _panels.Add(panel);
                RebuildLayout();
                InitAllWebViews();
                await EmbedAppInPanel(panel, file);
            }
            else
            {
                // Open as a file URL in a new panel tab
                string fileUrl = new Uri(file).AbsoluteUri;
                int maxGroup = _panels.Count > 0 ? _panels.Max(p => p.LayoutGroup) : 0;
                _panels.Add(new WorkspacePanel
                {
                    Title = System.IO.Path.GetFileName(file),
                    LayoutGroup = maxGroup + 1,
                    Tabs = { new BrowserTab { Title = System.IO.Path.GetFileName(file), Url = fileUrl } }
                });
                RebuildLayout();
                InitAllWebViews();
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  LOADED – initialize WebViews
    // ══════════════════════════════════════════════════════════════

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InitAllWebViews();
    }
}
