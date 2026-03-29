using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Orbital
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        [DllImport("powrprof.dll")]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        [DllImport("dxva2.dll")]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint num);

        [DllImport("dxva2.dll")]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count,
            [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll")]
        private static extern bool GetMonitorBrightness(IntPtr hPhysicalMonitor,
            ref uint min, ref uint cur, ref uint max);

        [DllImport("dxva2.dll")]
        private static extern bool SetMonitorBrightness(IntPtr hPhysicalMonitor, uint brightness);

        [DllImport("dxva2.dll")]
        private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszProgressTitle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        // ── State ────────────────────────────────────────────────
        private bool _isExpanded;
        private bool _isHidden;

        // Clock
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        // Manual timer
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private TimeSpan _timeRemaining;
        private bool _timerRunning;

        // Stopwatch
        private readonly DispatcherTimer _swTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private DateTime _swStart;
        private TimeSpan _swElapsed;
        private bool _swRunning;

        // Reminder checker
        private readonly DispatcherTimer _reminderChecker = new() { Interval = TimeSpan.FromSeconds(15) };

        // Reminder toast auto-dismiss
        private readonly DispatcherTimer _toastDismissTimer = new() { Interval = TimeSpan.FromSeconds(6) };

        // Auto-hide edge detection
        private readonly DispatcherTimer _edgeDetector = new() { Interval = TimeSpan.FromMilliseconds(150) };

        // Data
        private readonly ObservableCollection<TimedReminder> _reminders = [];

        // DPI
        private double _dpiX = 1.0, _dpiY = 1.0;

        // Audio spectrum
        private WasapiLoopbackCapture? _audioCapture;
        private readonly float[] _smoothSpectrum = new float[64];
        private readonly float[] _rawSpectrum = new float[64];
        private readonly float[] _bandPeaks = new float[64];
        private readonly object _spectrumLock = new();
        private readonly DispatcherTimer _spectrumTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private DateTime _audioDetectedTime;
        private DateTime _lastAudioTime;
        private bool _audioActive;
        private bool _spectrumVisible;
        private bool _expandedSpectrumShowing;

        // Drag support (collapsed mode)
        private Point _mouseDownPos;
        private bool _isDragging;

        // Temporary shelf (cleared on restart)
        private readonly ObservableCollection<ShortcutItem> _tempShelfItems = [];

        // Auto-collapse when cursor leaves expanded view
        private readonly DispatcherTimer _autoCollapseTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private DateTime? _autoCollapseLeaveTime;

        // Collapsed reminder toast auto-dismiss
        private readonly DispatcherTimer _collapsedToastDismissTimer = new() { Interval = TimeSpan.FromSeconds(8) };

        // Grace period after slide-in to prevent instant re-hide
        private DateTime _slideInTime;

        // System controls
        private MMDevice? _audioDevice;
        private IntPtr _physicalMonitor;
        private bool _brightnessAvailable;
        private bool _suppressVolumeEvent;
        private bool _suppressBrightnessEvent;

        // Dynamic Island animation
        private bool _isAnimating;
        private double _cachedCollapsedWidth;
        private double _cachedCollapsedHeight;
        private double _cachedCollapsedLeft;
        private double _cachedCollapsedTop;
        private Action? _collapseCallback;

        // System info (fetched on-demand when expanded)
        private readonly DispatcherTimer _sysInfoTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private long _prevIdleTime, _prevKernelTime, _prevUserTime;

        // ── Constructor ──────────────────────────────────────────

        public MainWindow()
        {
            InitializeComponent();

            SettingsManager.Load();
            ApplySettings();

            _clockTimer.Tick += (_, _) => UpdateClock();
            _clockTimer.Start();
            UpdateClock();

            _timer.Tick += Timer_Tick;

            _swTimer.Tick += (_, _) =>
            {
                var total = _swElapsed + (DateTime.UtcNow - _swStart);
                StopwatchDisplay.Text = $"{(int)total.TotalMinutes:D2}:{total.Seconds:D2}.{total.Milliseconds / 100}";
            };

            // Load persisted reminders
            foreach (var r in SettingsManager.Current.Reminders.Where(r => !r.Fired))
                _reminders.Add(r);
            ReminderList.ItemsSource = _reminders;
            _reminders.CollectionChanged += (_, _) => UpdateReminderBadge();

            _reminderChecker.Tick += ReminderChecker_Tick;
            _reminderChecker.Start();

            _toastDismissTimer.Tick += (_, _) => { _toastDismissTimer.Stop(); DismissToast(); };

            _collapsedToastDismissTimer.Tick += (_, _) => { _collapsedToastDismissTimer.Stop(); DismissCollapsedToast(); };

            // Auto-hide edge detection
            _edgeDetector.Tick += EdgeDetector_Tick;

            // Auto-collapse when cursor leaves expanded view
            _autoCollapseTimer.Tick += AutoCollapse_Tick;

            // System info timer (CPU/RAM refresh when expanded)
            _sysInfoTimer.Tick += SysInfoTimer_Tick;

            BuildSubjectButtons();
            BuildShortcutButtons();
            UpdateReminderBadge();
            UpdateScheduleCountdown();

            Loaded += (_, _) =>
            {
                PositionIsland();
                InitAudioSpectrum();
                InitSystemControls();
            };
            ApplyStartOnBoot();
        }

        // ── Settings application ─────────────────────────────────

        public void ApplySettings()
        {
            var s = SettingsManager.Current;

            // Shape
            double cr = s.IslandShape switch
            {
                "Rectangle" => 4,
                "Rounded" => 14,
                _ => s.IslandCornerRadius > 0 ? s.IslandCornerRadius : 25,
            };
            MainBorder.CornerRadius = new CornerRadius(cr);

            // Scale
            double scale = Math.Clamp(s.IslandScale, 0.5, 2.0);
            IslandScaleTransform.ScaleX = scale;
            IslandScaleTransform.ScaleY = scale;

            // Theme
            bool light = s.Theme == "Light";
            var bg = light ? Color.FromRgb(240, 240, 240) : Color.FromRgb(30, 30, 30);
            var border = light ? Color.FromRgb(200, 200, 200) : Color.FromRgb(51, 51, 51);
            var fgPrimary = light ? Colors.Black : Colors.White;
            var fgSecondary = light ? Color.FromRgb(100, 100, 100) : Color.FromRgb(204, 204, 204);
            var fgMuted = light ? Color.FromRgb(140, 140, 140) : Color.FromRgb(136, 136, 136);
            var panelBg = light ? Color.FromRgb(225, 225, 225) : Color.FromRgb(42, 42, 42);

            MainBorder.Background = new SolidColorBrush(bg);
            MainBorder.BorderBrush = new SolidColorBrush(border);

            // Outline color override
            if (!string.IsNullOrWhiteSpace(s.OutlineColor))
            {
                try
                {
                    MainBorder.BorderBrush = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(s.OutlineColor)!);
                    MainBorder.BorderThickness = new Thickness(2);
                }
                catch { }
            }

            // Background image
            if (!string.IsNullOrWhiteSpace(s.BackgroundImagePath) && System.IO.File.Exists(s.BackgroundImagePath))
            {
                try
                {
                    var imgBrush = new ImageBrush(new BitmapImage(new Uri(s.BackgroundImagePath, UriKind.Absolute)))
                    {
                        Stretch = Stretch.UniformToFill, Opacity = 0.3
                    };
                    MainBorder.Background = imgBrush;
                }
                catch { }
            }

            // Apply colors to collapsed view text
            ClockText.Foreground = new SolidColorBrush(fgPrimary);
            DateText.Foreground = new SolidColorBrush(fgMuted);
            CollapsedTimerText.Foreground = new SolidColorBrush(fgSecondary);

            // Apply colors to expanded view text
            ExpandedClockText.Foreground = new SolidColorBrush(fgPrimary);
            ExpandedDateText.Foreground = new SolidColorBrush(fgMuted);
            ExpandedTimerText.Foreground = new SolidColorBrush(fgPrimary);
            NoRemindersText.Foreground = new SolidColorBrush(fgMuted);

            // Apply panel backgrounds for expanded view
            var panelBrush = new SolidColorBrush(panelBg);
            ScheduleBar.Background = panelBrush;
            TimerStopwatchBorder.Background = panelBrush;
            ExpandedSpectrumBorder.Background = panelBrush;

            // Update icon/pill button backgrounds in expanded view
            var btnBg = new SolidColorBrush(panelBg);
            SettingsBtn.Background = btnBg;
            HideBtn.Background = btnBg;
            CollapseBtn.Background = btnBg;
            TimerTabBtn.Background = new SolidColorBrush(light ? Color.FromRgb(200, 200, 200) : Color.FromRgb(68, 68, 68));
            StopwatchTabBtn.Background = new SolidColorBrush(light ? Color.FromRgb(210, 210, 210) : Color.FromRgb(51, 51, 51));

            // Button foreground for light mode visibility
            var btnFg = new SolidColorBrush(light ? Colors.Black : Colors.White);
            SettingsBtn.Foreground = btnFg;
            HideBtn.Foreground = btnFg;
            CollapseBtn.Foreground = btnFg;
            AddReminderBtn.Foreground = btnFg;
            TimerTabBtn.Foreground = btnFg;
            StopwatchTabBtn.Foreground = btnFg;

            // Timer input text
            TimerMinutesInput.Foreground = new SolidColorBrush(fgPrimary);
            TimerMinutesInput.CaretBrush = new SolidColorBrush(fgPrimary);
            StopwatchDisplay.Foreground = new SolidColorBrush(fgPrimary);

            // Separator line
            SeparatorLine.Background = panelBrush;

            // Section labels and add button
            var sectionLabelBrush = new SolidColorBrush(light ? Color.FromRgb(130, 130, 130) : Color.FromRgb(85, 85, 85));
            SubjectsLabel.Foreground = sectionLabelBrush;
            RemindersLabel.Foreground = sectionLabelBrush;
            ControlsLabel.Foreground = sectionLabelBrush;
            TempShelfLabel.Foreground = sectionLabelBrush;
            ShortcutsLabel.Foreground = sectionLabelBrush;
            AddReminderBtn.Background = panelBrush;
            NewWorkspaceBtn.Background = panelBrush;
            AddShortcutBtn.Background = panelBrush;
            ControlsBorder.Background = panelBrush;
            LockBtn.Background = new SolidColorBrush(light ? Color.FromRgb(210, 210, 210) : Color.FromRgb(51, 51, 51));
            HibernateBtn.Background = new SolidColorBrush(light ? Color.FromRgb(210, 210, 210) : Color.FromRgb(51, 51, 51));
            ShutdownBtn.Background = new SolidColorBrush(light ? Color.FromRgb(210, 210, 210) : Color.FromRgb(51, 51, 51));
            RestartBtn.Background = new SolidColorBrush(light ? Color.FromRgb(210, 210, 210) : Color.FromRgb(51, 51, 51));
            LockBtn.Foreground = btnFg;
            HibernateBtn.Foreground = btnFg;
            ShutdownBtn.Foreground = btnFg;
            RestartBtn.Foreground = btnFg;
            NewWorkspaceBtn.Foreground = btnFg;
            AddShortcutBtn.Foreground = btnFg;
            VolumeIcon.Foreground = btnFg;
            BrightnessIcon.Foreground = btnFg;
            TimerIcon.Foreground = btnFg;

            // System info section
            SystemLabel.Foreground = sectionLabelBrush;
            SystemInfoBorder.Background = panelBrush;
            BatteryText.Foreground = new SolidColorBrush(fgMuted);
            ExpandedBatteryText.Foreground = new SolidColorBrush(fgMuted);
            SysInfoBattery.Foreground = new SolidColorBrush(fgSecondary);
            SysInfoCpu.Foreground = new SolidColorBrush(fgSecondary);
            SysInfoRam.Foreground = new SolidColorBrush(fgSecondary);

            // Drop zone backgrounds for light/dark mode
            var dropZoneBg = new SolidColorBrush(light ? Color.FromRgb(215, 215, 215) : Color.FromRgb(34, 34, 34));
            TempShelfDropZone.Background = dropZoneBg;
            ShortcutDropZone.Background = dropZoneBg;
            DeletionZone.Background = new SolidColorBrush(light ? Color.FromRgb(235, 210, 210) : Color.FromRgb(51, 17, 17));
            DeletionZoneText.Foreground = new SolidColorBrush(light ? Color.FromRgb(180, 60, 60) : Color.FromRgb(255, 107, 107));

            // Auto-hide
            if (s.AutoHide)
                _edgeDetector.Start();
            else if (!_isHidden)
                _edgeDetector.Stop();

            // Rebuild workspaces and shortcuts from settings
            BuildSubjectButtons();
            BuildShortcutButtons();
        }

        private void PositionIsland()
        {
            var (left, top) = ComputeIslandPosition(ActualWidth, ActualHeight);
            Left = left;
            Top = top;
        }

        private (double left, double top) ComputeIslandPosition(double width, double height)
        {
            var s = SettingsManager.Current;
            var screenW = SystemParameters.PrimaryScreenWidth;
            var screenH = SystemParameters.PrimaryScreenHeight;
            return s.IslandPosition switch
            {
                "TopLeft" => (20, s.CustomTop),
                "TopRight" => (screenW - width - 20, s.CustomTop),
                "Left" => (5, (screenH - height) / 2),
                "Right" => (screenW - width - 5, (screenH - height) / 2),
                "Custom" when s.CustomLeft >= 0 => (s.CustomLeft, s.CustomTop),
                _ => ((screenW - width) / 2, s.CustomTop),
            };
        }

        // ── Clock ────────────────────────────────────────────────

        private void UpdateClock()
        {
            string time = DateTime.Now.ToString("h:mm tt");
            string date = DateTime.Now.ToString("ddd, MMM d");
            ClockText.Text = time;
            DateText.Text = date;
            ExpandedClockText.Text = time;
            ExpandedDateText.Text = date;

            UpdateBattery();
            UpdateScheduleCountdown();
        }

        // ── Schedule auto-countdown (day-based, multi-week) ───────

        private void UpdateScheduleCountdown()
        {
            var nowDt = DateTime.Now;
            var now = TimeOnly.FromDateTime(nowDt);
            int cycleWeeks = Math.Max(1, SettingsManager.Current.ScheduleCycleWeeks);

            // Map DayOfWeek to 0=Monday..6=Sunday
            int dayIndex = nowDt.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)nowDt.DayOfWeek - 1;
            int weekInCycle = (ISOWeek.GetWeekOfYear(nowDt) - 1) % cycleWeeks;

            var todaySchedule = SettingsManager.Current.Schedule
                .Where(e => e.Day == dayIndex && e.Week == weekInCycle
                         && e.ParsedStart.HasValue && e.ParsedEnd.HasValue)
                .OrderBy(e => e.ParsedStart!.Value)
                .ToList();

            var current = todaySchedule
                .FirstOrDefault(e => e.ParsedStart!.Value <= now && now < e.ParsedEnd!.Value);

            if (current is not null)
            {
                var left = current.ParsedEnd!.Value.ToTimeSpan() - now.ToTimeSpan();
                // Collapsed: schedule info replaces clock area (frees spectrum column)
                ClockText.Text = $"{current.Label}  {(int)left.TotalMinutes}:{left.Seconds:D2}";
                DateText.Text = $"ends {current.EndTime}";
                // Expanded
                ExpandedTimerText.Text = $"{current.Label}  {(int)left.TotalMinutes}:{left.Seconds:D2} left";
                ScheduleLabel.Text = $"ends {current.EndTime}";
                ScheduleBar.Visibility = Visibility.Visible;
            }
            else if (!_timerRunning && _timeRemaining <= TimeSpan.Zero)
            {
                bool hasAny = SettingsManager.Current.Schedule.Count > 0;
                ScheduleBar.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;

                var next = todaySchedule
                    .FirstOrDefault(e => e.ParsedStart!.Value > now);
                if (next is not null)
                {
                    ExpandedTimerText.Text = $"Next: {next.Label} at {next.StartTime}";
                    ScheduleLabel.Text = "";
                }
                else
                {
                    ExpandedTimerText.Text = "";
                    ScheduleLabel.Text = "";
                }
            }
        }

        // ── Expand / Collapse with animation ─────────────────────

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            if (_isExpanded)
            {
                DragMove();
                return;
            }

            // In collapsed mode: capture mouse to distinguish click vs drag
            _mouseDownPos = e.GetPosition(this);
            _isDragging = false;
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isExpanded || !IsMouseCaptured) return;

            var pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _mouseDownPos.X) > 5 || Math.Abs(pos.Y - _mouseDownPos.Y) > 5)
            {
                _isDragging = true;
                ReleaseMouseCapture();
                DragMove();
                // Save custom position after drag
                SaveCustomPosition();
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (!_isDragging && !_isExpanded && !_isAnimating)
            {
                Expand();
            }
            _isDragging = false;
        }

        private void SaveCustomPosition()
        {
            SettingsManager.Current.IslandPosition = "Custom";
            SettingsManager.Current.CustomTop = Top;
            SettingsManager.Current.CustomLeft = Left;
            SettingsManager.Save();
        }

        private void Expand()
        {
            if (_isExpanded || _isAnimating) return;
            _isExpanded = true;
            _isAnimating = true;

            RefreshVolumeSlider();

            // Start system info fetching
            GetSystemTimes(out _prevIdleTime, out _prevKernelTime, out _prevUserTime);
            _sysInfoTimer.Interval = TimeSpan.FromSeconds(
                Math.Max(1, SettingsManager.Current.SysInfoRefreshSeconds));
            _sysInfoTimer.Start();
            UpdateMemory();
            UpdateCpuUsage();

            // Cache collapsed geometry
            _cachedCollapsedWidth = ActualWidth;
            _cachedCollapsedHeight = ActualHeight;
            _cachedCollapsedLeft = Left;
            _cachedCollapsedTop = Top;

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // Phase 1: Fade out collapsed content quickly
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (_, _) =>
            {
                // Phase 2: Lock size BEFORE swapping to prevent flash
                SizeToContent = SizeToContent.Manual;
                Width = _cachedCollapsedWidth;
                Height = _cachedCollapsedHeight;
                Left = _cachedCollapsedLeft;
                Top = _cachedCollapsedTop;

                // Now swap content — window stays at collapsed size since it's locked
                CollapsedView.Visibility = Visibility.Collapsed;
                ExpandedView.Visibility = Visibility.Visible;
                ExpandedView.Opacity = 0;

                // Measure the expanded content's natural size without changing window
                Dispatcher.BeginInvoke(() =>
                {
                    MainBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double targetW = MainBorder.DesiredSize.Width;
                    double targetH = MainBorder.DesiredSize.Height;

                    // Center the expanded view around the island's center point
                    var screenW = SystemParameters.PrimaryScreenWidth;
                    var screenH = SystemParameters.PrimaryScreenHeight;
                    double centerX = _cachedCollapsedLeft + _cachedCollapsedWidth / 2;
                    double targetLeft = Math.Max(0, Math.Min(centerX - targetW / 2, screenW - targetW));
                    double targetTop = _cachedCollapsedTop;
                    if (targetTop + targetH > screenH)
                        targetTop = Math.Max(0, screenH - targetH);

                    // Animate the island shape growing
                    var dur = TimeSpan.FromMilliseconds(380);
                    var wAnim = new DoubleAnimation(targetW, dur) { EasingFunction = ease };
                    var hAnim = new DoubleAnimation(targetH, dur) { EasingFunction = ease };
                    var lAnim = new DoubleAnimation(targetLeft, dur) { EasingFunction = ease };
                    var tAnim = new DoubleAnimation(targetTop, dur) { EasingFunction = ease };

                    hAnim.Completed += (_, _) =>
                    {
                        // Phase 3: Restore auto-size and fade in content
                        BeginAnimation(WidthProperty, null);
                        BeginAnimation(HeightProperty, null);
                        BeginAnimation(LeftProperty, null);
                        BeginAnimation(TopProperty, null);
                        SizeToContent = SizeToContent.WidthAndHeight;
                        ClearValue(WidthProperty);
                        ClearValue(HeightProperty);
                        Left = targetLeft;
                        Top = targetTop;

                        ExpandedView.BeginAnimation(OpacityProperty,
                            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });

                        var doneAnim = new DoubleAnimation(0, 0, TimeSpan.FromMilliseconds(200));
                        doneAnim.Completed += (_, _) => _isAnimating = false;
                        ExpandedTranslate.BeginAnimation(TranslateTransform.YProperty, doneAnim);
                    };

                    BeginAnimation(WidthProperty, wAnim);
                    BeginAnimation(HeightProperty, hAnim);
                    BeginAnimation(LeftProperty, lAnim);
                    BeginAnimation(TopProperty, tAnim);
                }, DispatcherPriority.Loaded);
            };
            CollapsedView.BeginAnimation(OpacityProperty, fadeOut);

            _autoCollapseTimer.Start();
        }

        private void Collapse()
        {
            if (!_isExpanded || _isAnimating) return;
            _isExpanded = false;
            _isAnimating = true;
            _autoCollapseTimer.Stop();
            _autoCollapseLeaveTime = null;
            _sysInfoTimer.Stop();

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            double expandedW = ActualWidth;
            double expandedH = ActualHeight;
            double expandedLeft = Left;
            double expandedTop = Top;

            // Phase 1: Fade out expanded content
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (_, _) =>
            {
                // Phase 2: Lock size BEFORE swapping to prevent flash
                SizeToContent = SizeToContent.Manual;
                Width = expandedW;
                Height = expandedH;
                Left = expandedLeft;
                Top = expandedTop;

                // Now swap content — window stays at expanded size since it's locked
                ExpandedView.Visibility = Visibility.Collapsed;
                CollapsedView.Visibility = Visibility.Visible;
                CollapsedView.Opacity = 0;

                // Measure the collapsed content's natural size without changing window
                Dispatcher.BeginInvoke(() =>
                {
                    MainBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double targetW = MainBorder.DesiredSize.Width;
                    double targetH = MainBorder.DesiredSize.Height;

                    // Compute correct collapsed position for collapsed size
                    var (targetLeft, targetTop) = ComputeIslandPosition(targetW, targetH);

                    // Animate shrink
                    var dur = TimeSpan.FromMilliseconds(350);
                    var wAnim = new DoubleAnimation(targetW, dur) { EasingFunction = ease };
                    var hAnim = new DoubleAnimation(targetH, dur) { EasingFunction = ease };
                    var lAnim = new DoubleAnimation(targetLeft, dur) { EasingFunction = ease };
                    var tAnim = new DoubleAnimation(targetTop, dur) { EasingFunction = ease };

                    hAnim.Completed += (_, _) =>
                    {
                        // Phase 3: Restore auto-size and fade in collapsed
                        BeginAnimation(WidthProperty, null);
                        BeginAnimation(HeightProperty, null);
                        BeginAnimation(LeftProperty, null);
                        BeginAnimation(TopProperty, null);
                        SizeToContent = SizeToContent.WidthAndHeight;
                        ClearValue(WidthProperty);
                        ClearValue(HeightProperty);
                        PositionIsland();

                        CollapsedView.BeginAnimation(OpacityProperty,
                            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });

                        var doneAnim = new DoubleAnimation(0, 0, TimeSpan.FromMilliseconds(180));
                        doneAnim.Completed += (_, _) =>
                        {
                            _isAnimating = false;
                            _collapseCallback?.Invoke();
                            _collapseCallback = null;
                        };
                        CollapsedTranslate.BeginAnimation(TranslateTransform.YProperty, doneAnim);
                    };

                    BeginAnimation(WidthProperty, wAnim);
                    BeginAnimation(HeightProperty, hAnim);
                    BeginAnimation(LeftProperty, lAnim);
                    BeginAnimation(TopProperty, tAnim);
                }, DispatcherPriority.Loaded);
            };
            ExpandedView.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void CollapseButton_Click(object sender, RoutedEventArgs e) => Collapse();

        private void HideButton_Click(object sender, RoutedEventArgs e)
        {
            _collapseCallback = () =>
            {
                SlideOut();
                _edgeDetector.Start();
            };
            Collapse();
        }

        // ── Auto-hide ────────────────────────────────────────────

        private void EdgeDetector_Tick(object? sender, EventArgs e)
        {
            GetCursorPos(out POINT pos);

            // Convert physical pixels to DIPs
            double cx = pos.X / _dpiX;
            double cy = pos.Y / _dpiY;
            var screenW = SystemParameters.PrimaryScreenWidth;
            string edge = SettingsManager.Current.AutoHideEdge;

            bool atEdge = edge switch
            {
                "Left" => cx <= 3,
                "Right" => cx >= screenW - 3,
                _ => cy <= 3,
            };

            // Coordinate check is more reliable than IsMouseOver with transparent windows
            bool overIsland = cx >= Left && cx <= Left + ActualWidth &&
                              cy >= Top && cy <= Top + ActualHeight;

            if (_isHidden && atEdge)
            {
                SlideIn();
                _slideInTime = DateTime.UtcNow;
                if (!SettingsManager.Current.AutoHide)
                    _edgeDetector.Stop();
            }
            else if (!_isHidden && !overIsland && !atEdge && SettingsManager.Current.AutoHide
                     && (DateTime.UtcNow - _slideInTime).TotalSeconds >= 5)
            {
                SlideOut();
            }
        }

        private void SlideOut()
        {
            _isHidden = true;
            string edge = SettingsManager.Current.AutoHideEdge;
            double target = edge switch
            {
                "Left" => -ActualWidth - 20,
                "Right" => ActualWidth + 20,
                _ => -ActualHeight - 20,
            };

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            var prop = edge is "Left" or "Right" ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            IslandSlideTransform.BeginAnimation(prop,
                new DoubleAnimation(target, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
            MainBorder.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        }

        private void SlideIn()
        {
            _isHidden = false;
            string edge = SettingsManager.Current.AutoHideEdge;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var prop = edge is "Left" or "Right" ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            IslandSlideTransform.BeginAnimation(prop,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
            MainBorder.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
        }

        // ── Auto-collapse when cursor leaves expanded island ─────

        private void AutoCollapse_Tick(object? sender, EventArgs e)
        {
            if (!_isExpanded) { _autoCollapseTimer.Stop(); _autoCollapseLeaveTime = null; return; }

            GetCursorPos(out POINT pos);
            double cx = pos.X / _dpiX;
            double cy = pos.Y / _dpiY;

            const double margin = 30;
            bool overIsland = cx >= Left - margin && cx <= Left + ActualWidth + margin &&
                              cy >= Top - margin && cy <= Top + ActualHeight + margin;

            if (overIsland)
            {
                _autoCollapseLeaveTime = null;
            }
            else
            {
                _autoCollapseLeaveTime ??= DateTime.UtcNow;
                if ((DateTime.UtcNow - _autoCollapseLeaveTime.Value).TotalSeconds >= 5)
                {
                    _autoCollapseLeaveTime = null;
                    Collapse();
                }
            }
        }

        // ── Timer / Stopwatch tabs ───────────────────────────────

        private void TimerTab_Click(object sender, RoutedEventArgs e)
        {
            TimerPanel.Visibility = Visibility.Visible;
            StopwatchPanel.Visibility = Visibility.Collapsed;
            bool light = SettingsManager.Current.Theme == "Light";
            TimerTabBtn.Background = new SolidColorBrush(light ? Color.FromRgb(190, 190, 190) : Color.FromRgb(68, 68, 68));
            StopwatchTabBtn.Background = new SolidColorBrush(light ? Color.FromRgb(210, 210, 210) : Color.FromRgb(51, 51, 51));
        }

        private void StopwatchTab_Click(object sender, RoutedEventArgs e)
        {
            TimerPanel.Visibility = Visibility.Collapsed;
            StopwatchPanel.Visibility = Visibility.Visible;
            bool light = SettingsManager.Current.Theme == "Light";
            StopwatchTabBtn.Background = new SolidColorBrush(light ? Color.FromRgb(190, 190, 190) : Color.FromRgb(68, 68, 68));
            TimerTabBtn.Background = new SolidColorBrush(light ? Color.FromRgb(210, 210, 210) : Color.FromRgb(51, 51, 51));
        }

        // ── Manual timer ─────────────────────────────────────────

        private void StartTimer_Click(object sender, RoutedEventArgs e)
        {
            if (_timerRunning)
            {
                _timer.Stop(); _timerRunning = false;
                StartPauseButton.Content = "▶ Start";
            }
            else
            {
                if (_timeRemaining <= TimeSpan.Zero
                    && int.TryParse(TimerMinutesInput.Text, out int min) && min > 0)
                    _timeRemaining = TimeSpan.FromMinutes(min);
                if (_timeRemaining <= TimeSpan.Zero) return;

                _timer.Start(); _timerRunning = true;
                StartPauseButton.Content = "⏸ Pause";
            }
            SyncTimerDisplay();
        }

        private void ResetTimer_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop(); _timerRunning = false;
            _timeRemaining = TimeSpan.Zero;
            StartPauseButton.Content = "▶ Start";
            SyncTimerDisplay();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _timeRemaining -= TimeSpan.FromSeconds(1);
            if (_timeRemaining <= TimeSpan.Zero)
            {
                _timeRemaining = TimeSpan.Zero;
                _timer.Stop(); _timerRunning = false;
                StartPauseButton.Content = "▶ Start";
                MessageBox.Show("⏰ Time's up!", "Orbital",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            SyncTimerDisplay();
        }

        private void SyncTimerDisplay()
        {
            if (_timerRunning || _timeRemaining > TimeSpan.Zero)
                CollapsedTimerText.Text = $"{(int)_timeRemaining.TotalMinutes}:{_timeRemaining.Seconds:D2}";
            else
                CollapsedTimerText.Text = "";
        }

        // ── Stopwatch ────────────────────────────────────────────

        private void StopwatchStart_Click(object sender, RoutedEventArgs e)
        {
            if (_swRunning)
            {
                _swTimer.Stop();
                _swElapsed += DateTime.UtcNow - _swStart;
                _swRunning = false;
                StopwatchStartBtn.Content = "▶ Start";
            }
            else
            {
                _swStart = DateTime.UtcNow;
                _swTimer.Start();
                _swRunning = true;
                StopwatchStartBtn.Content = "⏸ Pause";
            }
        }

        private void StopwatchReset_Click(object sender, RoutedEventArgs e)
        {
            _swTimer.Stop(); _swRunning = false; _swElapsed = TimeSpan.Zero;
            StopwatchDisplay.Text = "00:00.0";
            StopwatchStartBtn.Content = "▶ Start";
        }

        // ── Workspaces ───────────────────────────────────────────

        private void BuildSubjectButtons()
        {
            SubjectPanel.Children.Clear();
            foreach (var sub in SettingsManager.Current.Subjects)
            {
                var captured = sub;
                var btn = new Button
                {
                    Content = captured.Name,
                    Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(captured.Color)!),
                    Tag = captured,
                    Style = (Style)FindResource("SubjectButtonStyle"),
                };
                btn.Click += SubjectButton_Click;

                // Right-click context menu for rename / delete
                bool light = SettingsManager.Current.Theme == "Light";
                var menuBg = new SolidColorBrush(light ? Color.FromRgb(240, 240, 240) : Color.FromRgb(42, 42, 42));
                var menuFg = new SolidColorBrush(light ? Colors.Black : Colors.White);
                var menuBorder = new SolidColorBrush(light ? Color.FromRgb(200, 200, 200) : Color.FromRgb(68, 68, 68));
                var ctx = new ContextMenu
                {
                    Background = menuBg,
                    Foreground = menuFg,
                    BorderBrush = menuBorder,
                    BorderThickness = new Thickness(1),
                };
                var renameItem = new MenuItem { Header = "✏  Rename", Foreground = menuFg };
                renameItem.Click += (_, _) => RenameWorkspace(captured);
                var deleteItem = new MenuItem { Header = "🗑  Delete", Foreground = menuFg };
                deleteItem.Click += (_, _) => DeleteWorkspace(captured);
                ctx.Items.Add(renameItem);
                ctx.Items.Add(deleteItem);
                btn.ContextMenu = ctx;

                SubjectPanel.Children.Add(btn);
            }
        }

        private void RenameWorkspace(SubjectConfig sub)
        {
            var dlg = new InputDialog("Rename workspace:", sub.Name) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
            {
                sub.Name = dlg.ResultText;
                SettingsManager.Save();
                BuildSubjectButtons();
            }
        }

        private void DeleteWorkspace(SubjectConfig sub)
        {
            var result = MessageBox.Show(
                $"Delete workspace \"{sub.Name}\"?",
                "Orbital", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                SettingsManager.Current.Subjects.Remove(sub);
                SettingsManager.Save();
                BuildSubjectButtons();
            }
        }

        private void SubjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: SubjectConfig sub })
            {
                var win = new WorkspaceWindow(sub);
                if (SettingsManager.Current.BlendIslandIntoWorkspace)
                {
                    win.Closed += (_, _) => { Show(); };
                    Hide();
                }
                win.Show();
            }
        }

        private void NewWorkspace_Click(object sender, RoutedEventArgs e)
        {
            var ws = new SubjectConfig
            {
                Name = $"Workspace {SettingsManager.Current.Subjects.Count + 1}",
                Color = "#4A90D9",
                SplitOrientation = "LeftRight",
                Panels =
                [
                    new() { Title = "Browser", Url = "https://www.google.com", LayoutGroup = 0 },
                    new() { Title = "PDF Viewer", Url = "", LayoutGroup = 1 },
                ],
            };
            SettingsManager.Current.Subjects.Add(ws);
            SettingsManager.Save();
            BuildSubjectButtons();

            // Open immediately
            var win = new WorkspaceWindow(ws);
            if (SettingsManager.Current.BlendIslandIntoWorkspace)
            {
                win.Closed += (_, _) => { Show(); };
                Hide();
            }
            win.Show();
        }

        // ── Reminders with timed alerts ──────────────────────────

        private void AddReminder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ReminderDialog { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ReminderText))
            {
                var r = new TimedReminder { Text = dlg.ReminderText, AlertTime = dlg.AlertTime };
                _reminders.Add(r);
                SettingsManager.Current.Reminders.Add(r);
                SettingsManager.Save();
            }
        }

        private void RemoveReminder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: TimedReminder r })
            {
                _reminders.Remove(r);
                SettingsManager.Current.Reminders.Remove(r);
                SettingsManager.Save();
            }
        }

        private void ReminderChecker_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            foreach (var r in _reminders.Where(r => !r.Fired && r.AlertTime <= now).ToList())
            {
                r.Fired = true;
                ShowReminderToast(r.Text);
            }
            SettingsManager.Save();
            UpdateReminderBadge();
        }

        private void ShowReminderToast(string text)
        {
            if (_isExpanded)
            {
                // Show in expanded view toast
                ReminderToastText.Text = text;
                ReminderToast.Visibility = Visibility.Visible;
                ReminderToast.Opacity = 0;
                ReminderToast.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                _toastDismissTimer.Stop();
                _toastDismissTimer.Start();
            }
            else
            {
                // Show inline in collapsed view (don't expand the island)
                CollapsedReminderText.Text = text;
                CollapsedReminderToast.Visibility = Visibility.Visible;
                CollapsedReminderToast.Opacity = 0;
                CollapsedReminderToast.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                _collapsedToastDismissTimer.Stop();
                _collapsedToastDismissTimer.Start();
            }

            SystemSounds.Asterisk.Play();
        }

        private void DismissToast()
        {
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            anim.Completed += (_, _) => ReminderToast.Visibility = Visibility.Collapsed;
            ReminderToast.BeginAnimation(OpacityProperty, anim);
        }

        private void DismissToast_Click(object sender, RoutedEventArgs e)
        {
            _toastDismissTimer.Stop();
            DismissToast();
        }

        private void DismissCollapsedToast()
        {
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            anim.Completed += (_, _) => CollapsedReminderToast.Visibility = Visibility.Collapsed;
            CollapsedReminderToast.BeginAnimation(OpacityProperty, anim);
        }

        private void DismissCollapsedToast_Click(object sender, RoutedEventArgs e)
        {
            _collapsedToastDismissTimer.Stop();
            DismissCollapsedToast();
        }

        private void UpdateReminderBadge()
        {
            int pending = _reminders.Count(r => !r.Fired);
            ReminderBadge.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
            ReminderCountText.Text = pending.ToString();
            NoRemindersText.Visibility = _reminders.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── Settings ─────────────────────────────────────────────

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow { Owner = this };
            win.SettingsSaved += () => ApplySettings();
            win.ShowDialog();
        }

        // ── Start on boot ────────────────────────────────────────

        private static void ApplyStartOnBoot()
        {
            const string keyName = "Orbital";
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key is null) return;

                if (SettingsManager.Current.StartOnBoot)
                {
                    string exePath = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue(keyName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(keyName, false);
                }
            }
            catch { }
        }

        // ── Audio spectrum ────────────────────────────────────────

        private void InitAudioSpectrum()
        {
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget is not null)
            {
                _dpiX = src.CompositionTarget.TransformToDevice.M11;
                _dpiY = src.CompositionTarget.TransformToDevice.M22;
            }

            var stroke = new SolidColorBrush(Color.FromRgb(0, 255, 136));
            var fill = new LinearGradientBrush(
                Color.FromArgb(70, 0, 255, 136),
                Color.FromArgb(70, 0, 204, 255), 0);
            SpectrumLine.Stroke = stroke;
            SpectrumLine.StrokeThickness = 1.5;
            SpectrumFill.Fill = fill;

            ExpandedSpectrumLine.Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 136));
            ExpandedSpectrumLine.StrokeThickness = 1.5;
            ExpandedSpectrumFill.Fill = new LinearGradientBrush(
                Color.FromArgb(70, 0, 255, 136),
                Color.FromArgb(70, 0, 204, 255), 0);

            try
            {
                _audioCapture = new WasapiLoopbackCapture();
                _audioCapture.DataAvailable += OnAudioData;
                _audioCapture.StartRecording();
            }
            catch { _audioCapture = null; }

            _spectrumTimer.Tick += SpectrumTick;
            _spectrumTimer.Start();
        }

        private void OnAudioData(object? sender, WaveInEventArgs e)
        {
            if (_audioCapture?.WaveFormat is null || e.BytesRecorded == 0) return;

            var wf = _audioCapture.WaveFormat;
            int channels = wf.Channels;
            int bps = wf.BitsPerSample / 8;
            int frames = e.BytesRecorded / (bps * channels);
            if (frames == 0) return;

            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    int off = (i * channels + ch) * bps;
                    if (off + bps <= e.BytesRecorded)
                        sum += wf.Encoding == WaveFormatEncoding.IeeeFloat
                            ? BitConverter.ToSingle(e.Buffer, off)
                            : BitConverter.ToInt16(e.Buffer, off) / 32768f;
                }
                mono[i] = sum / channels;
            }

            float rms = 0;
            for (int i = 0; i < mono.Length; i++) rms += mono[i] * mono[i];
            rms = MathF.Sqrt(rms / mono.Length);

            if (rms > 0.001f)
            {
                _lastAudioTime = DateTime.UtcNow;
                if (!_audioActive) _audioDetectedTime = DateTime.UtcNow;
                _audioActive = true;
            }
            else if (_audioActive && (DateTime.UtcNow - _lastAudioTime).TotalSeconds > 0.5)
            {
                _audioActive = false;
            }

            if (_audioActive && (DateTime.UtcNow - _audioDetectedTime).TotalSeconds >= 3 && mono.Length >= 1024)
            {
                int N = mono.Length >= 2048 ? 2048 : 1024;
                int m = (int)Math.Log2(N);
                var cplx = new NAudio.Dsp.Complex[N];
                int start = mono.Length - N;
                for (int i = 0; i < N; i++)
                {
                    float w = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (N - 1)));
                    cplx[i].X = mono[start + i] * w;
                    cplx[i].Y = 0;
                }
                FastFourierTransform.FFT(true, m, cplx);

                const int bands = 64;
                float sr = wf.SampleRate;
                float binW = sr / N;
                float melMin = 2595f * MathF.Log10(1 + 60f / 700f);
                float melMax = 2595f * MathF.Log10(1 + Math.Min(18000f, sr / 2f) / 700f);
                var spec = new float[bands];
                for (int bd = 0; bd < bands; bd++)
                {
                    float mel0 = melMin + (melMax - melMin) * bd / bands;
                    float mel1 = melMin + (melMax - melMin) * (bd + 1) / bands;
                    float f0 = 700f * (MathF.Pow(10, mel0 / 2595f) - 1);
                    float f1 = 700f * (MathF.Pow(10, mel1 / 2595f) - 1);
                    int b0 = Math.Max(1, (int)(f0 / binW));
                    int b1 = Math.Min(N / 2 - 1, Math.Max(b0, (int)(f1 / binW)));
                    float mx = 0;
                    for (int k = b0; k <= b1; k++)
                    {
                        float mag = MathF.Sqrt(cplx[k].X * cplx[k].X + cplx[k].Y * cplx[k].Y);
                        mx = Math.Max(mx, mag);
                    }
                    spec[bd] = mx;
                }
                lock (_spectrumLock) Array.Copy(spec, _rawSpectrum, bands);
            }
            else if (!_audioActive)
            {
                lock (_spectrumLock) Array.Clear(_rawSpectrum);
            }
        }

        private void SpectrumTick(object? sender, EventArgs e)
        {
            const int bands = 64;

            float[] raw;
            lock (_spectrumLock) raw = (float[])_rawSpectrum.Clone();

            // Per-band peak tracking and normalization
            for (int i = 0; i < bands; i++)
            {
                _bandPeaks[i] = Math.Max(raw[i], _bandPeaks[i] * 0.995f);
                _bandPeaks[i] = Math.Max(_bandPeaks[i], 1e-6f);

                float normalized = raw[i] / _bandPeaks[i];

                _smoothSpectrum[i] = normalized > _smoothSpectrum[i]
                    ? _smoothSpectrum[i] * 0.3f + normalized * 0.7f
                    : _smoothSpectrum[i] * 0.75f + normalized * 0.25f;
            }

            bool audioReady = _audioActive
                && (DateTime.UtcNow - _audioDetectedTime).TotalSeconds >= 3;

            // ── Collapsed view ──
            bool shouldShowCollapsed = audioReady
                && string.IsNullOrEmpty(CollapsedTimerText.Text)
                && !_isExpanded;

            if (shouldShowCollapsed)
            {
                if (!_spectrumVisible)
                {
                    SpectrumBorder.Visibility = Visibility.Visible;
                    SpectrumBorder.BeginAnimation(WidthProperty,
                        new DoubleAnimation(0, 140, TimeSpan.FromMilliseconds(350))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                    _spectrumVisible = true;
                }
                UpdateSpectrumPoints(SpectrumLine, SpectrumFill, 140, 18);
            }
            else if (_spectrumVisible && (!shouldShowCollapsed || _isExpanded))
            {
                bool any = false;
                for (int i = 0; i < bands; i++)
                {
                    _smoothSpectrum[i] *= 0.8f;
                    if (_smoothSpectrum[i] > 0.001f) any = true;
                }
                UpdateSpectrumPoints(SpectrumLine, SpectrumFill, 140, 18);
                if (!any)
                {
                    SpectrumBorder.BeginAnimation(WidthProperty,
                        new DoubleAnimation(0, TimeSpan.FromMilliseconds(250))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
                    _spectrumVisible = false;
                }
            }

            // ── Expanded view ──
            bool shouldShowExpanded = audioReady && _isExpanded;

            if (shouldShowExpanded)
            {
                if (!_expandedSpectrumShowing)
                {
                    ExpandedSpectrumBorder.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                    _expandedSpectrumShowing = true;
                }
                UpdateSpectrumPoints(ExpandedSpectrumLine, ExpandedSpectrumFill, 420, 22);
            }
            else if (_expandedSpectrumShowing && !shouldShowExpanded)
            {
                bool any = false;
                for (int i = 0; i < bands; i++)
                {
                    _smoothSpectrum[i] *= 0.8f;
                    if (_smoothSpectrum[i] > 0.001f) any = true;
                }
                UpdateSpectrumPoints(ExpandedSpectrumLine, ExpandedSpectrumFill, 420, 22);
                if (!any)
                {
                    ExpandedSpectrumBorder.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
                    _expandedSpectrumShowing = false;
                }
            }
        }

        private void UpdateSpectrumPoints(Polyline line, Polygon fill, double width, double height)
        {
            const int bands = 64;
            var lp = new PointCollection(bands);
            var fp = new PointCollection(bands + 2);
            fp.Add(new Point(0, height));
            for (int i = 0; i < bands; i++)
            {
                float val = MathF.Pow(Math.Clamp(_smoothSpectrum[i], 0, 1), 0.6f);
                double x = i * width / (bands - 1);
                double y = height - val * height * 0.95;
                lp.Add(new Point(x, y));
                fp.Add(new Point(x, y));
            }
            fp.Add(new Point(width, height));
            line.Points = lp;
            fill.Points = fp;
        }

        // ── System Controls ──────────────────────────────────────

        private void InitSystemControls()
        {
            // Volume via NAudio CoreAudioApi
            try
            {
                var enumerator = new MMDeviceEnumerator();
                _audioDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _suppressVolumeEvent = true;
                VolumeSlider.Value = _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100;
                VolumeLabel.Text = $"{(int)VolumeSlider.Value}%";
                _suppressVolumeEvent = false;
            }
            catch { _audioDevice = null; }

            // Brightness via DDC/CI (dxva2)
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var hMon = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
                uint numMonitors = 0;
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, ref numMonitors) && numMonitors > 0)
                {
                    var monitors = new PHYSICAL_MONITOR[numMonitors];
                    if (GetPhysicalMonitorsFromHMONITOR(hMon, numMonitors, monitors))
                    {
                        _physicalMonitor = monitors[0].hPhysicalMonitor;
                        uint min = 0, cur = 0, max = 0;
                        if (GetMonitorBrightness(_physicalMonitor, ref min, ref cur, ref max) && max > min)
                        {
                            _brightnessAvailable = true;
                            _suppressBrightnessEvent = true;
                            BrightnessSlider.Minimum = min;
                            BrightnessSlider.Maximum = max;
                            BrightnessSlider.Value = cur;
                            BrightnessLabel.Text = $"{(int)(100.0 * (cur - min) / (max - min))}%";
                            _suppressBrightnessEvent = false;
                        }
                    }
                }
            }
            catch { }

            if (!_brightnessAvailable)
            {
                BrightnessSlider.IsEnabled = false;
                BrightnessLabel.Text = "N/A";
            }
        }

        private void VolumeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressVolumeEvent || VolumeLabel is null) return;
            VolumeLabel.Text = $"{(int)e.NewValue}%";
            try
            {
                if (_audioDevice is not null)
                    _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(e.NewValue / 100.0);
            }
            catch { }
        }

        private void RefreshVolumeSlider()
        {
            try
            {
                if (_audioDevice is not null)
                {
                    _suppressVolumeEvent = true;
                    double vol = _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100;
                    VolumeSlider.Value = vol;
                    VolumeLabel.Text = $"{(int)vol}%";
                    _suppressVolumeEvent = false;
                }
            }
            catch { _suppressVolumeEvent = false; }
        }

        private void BrightnessSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressBrightnessEvent || !_brightnessAvailable || BrightnessLabel is null) return;
            double pct = (e.NewValue - BrightnessSlider.Minimum) / (BrightnessSlider.Maximum - BrightnessSlider.Minimum);
            BrightnessLabel.Text = $"{(int)(pct * 100)}%";
            try { SetMonitorBrightness(_physicalMonitor, (uint)e.NewValue); } catch { }
        }

        private void LockScreen_Click(object sender, RoutedEventArgs e)
        {
            LockWorkStation();
        }

        private void HibernatePC_Click(object sender, RoutedEventArgs e)
        {
            SetSuspendState(true, false, false);
        }

        private void ShutdownPC_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "/s /t 0")
                { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }
        }

        private void RestartPC_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 0")
                { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }
        }

        // ── Temporary Shelf ──────────────────────────────────────

        private void TempShelf_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files)
                {
                    string name = System.IO.Path.GetFileName(f);
                    _tempShelfItems.Add(new ShortcutItem { Name = name, Path = f, Kind = "File" });
                }
                RebuildTempShelfUI();
            }
        }

        private void ShelfDrag_Enter(object sender, DragEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
        }

        private void ShelfDrag_Leave(object sender, DragEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromRgb(34, 34, 34));
        }

        private void RebuildTempShelfUI()
        {
            TempShelfList.Items.Clear();
            foreach (var item in _tempShelfItems)
            {
                var si = item; // capture for lambda
                var tile = BuildShortcutTile(
                    si.Name, si.Path, si.Kind,
                    onClick: () => OpenPath(si.Path),
                    onRemove: () => { _tempShelfItems.Remove(si); RebuildTempShelfUI(); });
                TempShelfList.Items.Add(tile);
            }
        }

        // ── Permanent Shortcuts ──────────────────────────────────

        private void BuildShortcutButtons()
        {
            ShortcutList.Items.Clear();
            foreach (var item in SettingsManager.Current.PermanentShortcuts)
            {
                var si = item; // capture for lambda
                var tile = BuildShortcutTile(
                    si.Name, si.Path, si.Kind,
                    onClick: () => OpenPath(si.Path),
                    onRemove: () =>
                    {
                        SettingsManager.Current.PermanentShortcuts.Remove(si);
                        SettingsManager.Save();
                        BuildShortcutButtons();
                    });
                ShortcutList.Items.Add(tile);
            }
        }

        private void AddShortcut_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "All Files|*.*|Applications|*.exe;*.lnk",
                Title = "Select file or application for shortcut"
            };
            if (dlg.ShowDialog() == true)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                string kind = ext is ".exe" or ".lnk" ? "App" : "File";
                SettingsManager.Current.PermanentShortcuts.Add(
                    new ShortcutItem { Name = name, Path = dlg.FileName, Kind = kind });
                SettingsManager.Save();
                BuildShortcutButtons();
            }
        }

        private void ShortcutZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files)
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(f);
                    string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    string kind = ext is ".exe" or ".lnk" ? "App" : "File";
                    SettingsManager.Current.PermanentShortcuts.Add(
                        new ShortcutItem { Name = name, Path = f, Kind = kind });
                }
                SettingsManager.Save();
                BuildShortcutButtons();
            }
        }

        // ── Deletion Zone ────────────────────────────────────────

        private void DeletionZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files)
                    SendToRecycleBin(f);
            }
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromRgb(51, 17, 17));
        }

        private void DeletionDrag_Enter(object sender, DragEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromRgb(80, 20, 20));
        }

        private void DeletionDrag_Leave(object sender, DragEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromRgb(51, 17, 17));
        }

        private static void SendToRecycleBin(string path)
        {
            try
            {
                var shf = new SHFILEOPSTRUCT
                {
                    wFunc = 0x0003, // FO_DELETE
                    pFrom = path + '\0' + '\0',
                    fFlags = 0x0040 | 0x0010 | 0x0004, // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
                };
                SHFileOperation(ref shf);
            }
            catch { }
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>Extract the shell icon for a file/folder path and return as an ImageSource.</summary>
        private static ImageSource? GetFileIcon(string path)
        {
            const uint SHGFI_ICON = 0x000000100;
            const uint SHGFI_LARGEICON = 0x000000000;
            const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

            try
            {
                var shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;
                if (!System.IO.File.Exists(path) && !Directory.Exists(path))
                    flags |= SHGFI_USEFILEATTRIBUTES;

                var result = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
                if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;

                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    shfi.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                DestroyIcon(shfi.hIcon);
                return source;
            }
            catch { return null; }
        }

        /// <summary>Build a desktop-shortcut-style button: icon on top, name below.</summary>
        private static UIElement BuildShortcutTile(string name, string path, string kind,
            Action onClick, Action? onRemove)
        {
            var outer = new Grid { Width = 72, Margin = new Thickness(4) };
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Icon
            var iconImage = GetFileIcon(path);
            UIElement iconElement;
            if (iconImage is not null)
            {
                iconElement = new Image
                {
                    Source = iconImage,
                    Width = 32,
                    Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 2),
                };
            }
            else
            {
                string emoji = kind switch { "App" => "🚀", "Url" => "🌐", _ => "📄" };
                iconElement = new TextBlock
                {
                    Text = emoji,
                    FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 2),
                };
            }

            var btn = new Button
            {
                Content = iconElement,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = path,
            };
            btn.Click += (_, _) => onClick();
            Grid.SetRow(btn, 0);
            outer.Children.Add(btn);

            // Name label
            var label = new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 70,
                MaxHeight = 30,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetRow(label, 1);
            outer.Children.Add(label);

            // Remove button
            if (onRemove is not null)
            {
                var removeBtn = new Button
                {
                    Content = "✕",
                    Background = Brushes.Transparent,
                    Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                    FontSize = 8,
                    Cursor = Cursors.Hand,
                    Padding = new Thickness(2, 0, 2, 0),
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Opacity = 0.6,
                };
                removeBtn.Click += (_, _) => onRemove();
                Grid.SetRow(removeBtn, 2);
                outer.Children.Add(removeBtn);
            }

            return outer;
        }

        // ── System Info ──────────────────────────────────────────

        private void UpdateBattery()
        {
            if (GetSystemPowerStatus(out var ps) && ps.BatteryLifePercent != 255)
            {
                string icon = ps.ACLineStatus == 1 ? "⚡" : "🔋";
                BatteryText.Text = $"{icon}{ps.BatteryLifePercent}%";
                ExpandedBatteryText.Text = $"{icon}{ps.BatteryLifePercent}%";
                SysInfoBattery.Text = $"🔋 {ps.BatteryLifePercent}% {(ps.ACLineStatus == 1 ? "(Charging)" : "")}".TrimEnd();
            }
            else
            {
                BatteryText.Text = "⚡AC";
                ExpandedBatteryText.Text = "⚡AC";
                SysInfoBattery.Text = "🔋 AC Power";
            }
        }

        private void UpdateCpuUsage()
        {
            if (GetSystemTimes(out long idle, out long kernel, out long user))
            {
                long idleDelta = idle - _prevIdleTime;
                long totalDelta = (kernel - _prevKernelTime) + (user - _prevUserTime);
                double usage = totalDelta > 0 ? (1.0 - (double)idleDelta / totalDelta) * 100.0 : 0;
                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                SysInfoCpu.Text = $"💻 CPU: {usage:F0}%";
            }
        }

        private void UpdateMemory()
        {
            var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref ms))
            {
                double totalGB = ms.ullTotalPhys / (1024.0 * 1024 * 1024);
                double usedGB = (ms.ullTotalPhys - ms.ullAvailPhys) / (1024.0 * 1024 * 1024);
                SysInfoRam.Text = $"💾 RAM: {usedGB:F1}/{totalGB:F1} GB ({ms.dwMemoryLoad}%)";
            }
        }

        private void SysInfoTimer_Tick(object? sender, EventArgs e)
        {
            UpdateCpuUsage();
            UpdateMemory();
        }

        // ── Close ────────────────────────────────────────────────

        private void CloseApp_Click(object sender, RoutedEventArgs e)
        {
            _spectrumTimer.Stop();
            _sysInfoTimer.Stop();
            try { _audioCapture?.StopRecording(); } catch { }
            _audioCapture?.Dispose();
            Application.Current.Shutdown();
        }
    }
}