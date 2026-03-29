namespace Orbital;

/// <summary>A single panel inside a workspace.</summary>
public class PanelConfig
{
    public string Title { get; set; } = "New Panel";
    public string Url { get; set; } = "https://www.google.com";
    public bool IsFolded { get; set; }
    /// <summary>Column group index for mixed layouts. Panels sharing the same group stack vertically.</summary>
    public int LayoutGroup { get; set; }
    /// <summary>Path to a native executable to embed instead of a URL.</summary>
    public string AppPath { get; set; } = "";
}

/// <summary>A workspace preset with an arbitrary number of panels.</summary>
public class SubjectConfig
{
    public string Name { get; set; } = "New Workspace";
    public string Color { get; set; } = "#4A90D9";
    public List<PanelConfig> Panels { get; set; } =
    [
        new() { Title = "Browser", Url = "https://www.google.com", LayoutGroup = 0 },
        new() { Title = "PDF Viewer", Url = "", LayoutGroup = 1 },
    ];
    /// <summary>"LeftRight", "TopBottom", or "Mixed"</summary>
    public string SplitOrientation { get; set; } = "LeftRight";
}

/// <summary>A file/app shortcut stored in the island.</summary>
public class ShortcutItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>"File", "App", or "Url"</summary>
    public string Kind { get; set; } = "File";
}

/// <summary>A class period in a day-based, multi-week schedule.</summary>
public class ScheduleEntry
{
    public string Label { get; set; } = "";
    /// <summary>0 = Monday … 6 = Sunday</summary>
    public int Day { get; set; }
    /// <summary>0-based week number in the cycle (for multi-week schedules).</summary>
    public int Week { get; set; }
    /// <summary>Start time in 12-hour format, e.g. "8:00 AM".</summary>
    public string StartTime { get; set; } = "8:00 AM";
    /// <summary>End time in 12-hour format, e.g. "8:50 AM".</summary>
    public string EndTime { get; set; } = "8:50 AM";

    [System.Text.Json.Serialization.JsonIgnore]
    public TimeOnly? ParsedStart => TimeOnly.TryParse(StartTime, out var t) ? t : null;
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeOnly? ParsedEnd => TimeOnly.TryParse(EndTime, out var t) ? t : null;
}

/// <summary>A timed reminder (to-do) that fires a notification.</summary>
public class TimedReminder
{
    public string Text { get; set; } = "";
    public DateTime AlertTime { get; set; } = DateTime.Now;
    public bool Fired { get; set; }
}
