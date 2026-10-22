using System.Text.Json;
using System.Text.Json.Serialization;

using TaskbarDino.Core.Settings;

namespace TaskbarDino;

/// <summary>Persists <see cref="AppSettings"/> as indented JSON in <c>%LOCALAPPDATA%\TaskbarDino\settings.json</c>.</summary>
internal sealed class SettingsStore
{
    private AppSettings _current;

    public SettingsStore()
    {
        Location = Path.Combine(Log.Directory, "settings.json");
        _current = Load();
    }

    public AppSettings Current => _current;

    public string Location { get; }

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        _current = mutate(_current);
        Save();
    }

    /// <summary>Restores defaults (keeping only the high score) and rewrites the file.</summary>
    public void Reset()
    {
        _current = new AppSettings { HighScore = _current.HighScore };
        Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(Location))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(Location), SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Settings file unreadable ({ex.Message}); using defaults.");
            try
            {
                File.Move(Location, Location + ".bad", overwrite: true);
            }
            catch (Exception)
            {
            }
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Location)!);
            File.WriteAllText(Location, JsonSerializer.Serialize(_current, SettingsJsonContext.Default.AppSettings));
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to persist settings: {ex.Message}");
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
