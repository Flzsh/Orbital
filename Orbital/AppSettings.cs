namespace Orbital;

/// <summary>All persisted user settings – serialised as JSON.</summary>
public class AppSettings
{
    // ── Appearance ───────────────────────────────────────────────
    public string Theme { get; set; } = "Dark";           // "Dark" | "Light"
    public string AccentColor { get; set; } = "#00FF88";
    public string IslandShape { get; set; } = "Pill";     // "Pill" | "Rectangle" | "Rounded"
    public double IslandCornerRadius { get; set; } = 25;
    public double IslandWidth { get; set; } = 360;
    public double IslandScale { get; set; } = 1.0;        // 0.5 – 2.0
    public string OutlineColor { get; set; } = "";         // "" = none, hex color for border accent
    public string BackgroundImagePath { get; set; } = "";  // file:// or empty

    // ── Position ────────────────────────────────────────────────
    public string IslandPosition { get; set; } = "TopCenter"; // "TopCenter" | "TopLeft" | "TopRight" | "Left" | "Right" | "Custom"
    public double CustomTop { get; set; } = 10;
    public double CustomLeft { get; set; } = -1;             // -1 = auto-center

    // ── Auto-hide ───────────────────────────────────────────────
    public bool AutoHide { get; set; }
    public string AutoHideEdge { get; set; } = "Top";        // "Top" | "Left" | "Right"

    // ── Startup ─────────────────────────────────────────────────
    public bool StartOnBoot { get; set; }

    // ── Workspaces ────────────────────────────────────────────────
    public List<SubjectConfig> Subjects { get; set; } =
    [
        new()
        {
            Name = "Workspace 1", Color = "#4A90D9",
            SplitOrientation = "LeftRight",
            Panels =
            [
                new() { Title = "Browser", Url = "https://www.google.com", LayoutGroup = 0 },
                new() { Title = "PDF Viewer", Url = "", LayoutGroup = 1 },
            ]
        },
    ];

    // ── Permanent Shortcuts ──────────────────────────────────────
    public List<ShortcutItem> PermanentShortcuts { get; set; } = [];

    // ── Schedule ────────────────────────────────────────────────
    /// <summary>1, 2, or 3 week cycle length.</summary>
    public int ScheduleCycleWeeks { get; set; } = 1;
    public List<ScheduleEntry> Schedule { get; set; } = [];

    // ── Reminders ───────────────────────────────────────────────
    public List<TimedReminder> Reminders { get; set; } = [];

    // ── System Info ─────────────────────────────────────────────
    /// <summary>Refresh interval in seconds for CPU / RAM when expanded (min 1).</summary>
    public int SysInfoRefreshSeconds { get; set; } = 3;

    // ── Workspace defaults ──────────────────────────────────
    public double WorkspaceHeaderHeight { get; set; } = 32;
    public bool BlendIslandIntoWorkspace { get; set; } = true;
}
