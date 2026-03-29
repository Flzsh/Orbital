using System.IO;
using System.Text.Json;

namespace Orbital;

/// <summary>Loads / saves <see cref="AppSettings"/> to a local JSON file.</summary>
public static class SettingsManager
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbital");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppSettings Current { get; set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new();
            }
        }
        catch
        {
            Current = new();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string json = JsonSerializer.Serialize(Current, JsonOpts);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Silently fail – settings are not critical
        }
    }
}
