using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CacheCleanup.Settings;

public sealed class AppSettings
{
    /// <summary>"auto" or a language code from <see cref="Localization.LocalizationManager.Languages"/>.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>"dark", "light" or "system".</summary>
    public string Theme { get; set; } = "dark";

    public bool UseRecycleBin { get; set; } = true;
    public int SkipRecentHours { get; set; } = 24;
    public bool Animations { get; set; } = true;
    public bool Backdrop { get; set; } = true;
    public bool ScanOnStartup { get; set; }

    public Dictionary<string, bool> TargetStates { get; set; } = new(StringComparer.Ordinal);
    public List<string> CustomFolders { get; set; } = [];

    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public long LifetimeBytesFreed { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // CACHECLEANUP_SETTINGS may point at an alternative settings file (portable use, testing).
    public static string FilePath { get; } =
        Environment.GetEnvironmentVariable("CACHECLEANUP_SETTINGS") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CacheCleanup", "settings.json");

    public static string Directory { get; } = Path.GetDirectoryName(FilePath)!;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch
        {
            // corrupt settings fall back to defaults
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // never crash because of settings
        }
    }
}
