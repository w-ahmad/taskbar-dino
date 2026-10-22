using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Settings;

namespace TaskbarDino.App;

/// <summary>Persists <see cref="AppSettings"/> as indented JSON in %LOCALAPPDATA%\TaskbarDino.</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger<JsonSettingsService> _logger;
    private AppSettings _current;

    public JsonSettingsService(ILogger<JsonSettingsService> logger)
    {
        _logger = logger;
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarDino");
        _path = Path.Combine(directory, "settings.json");
        _current = Load();
    }

    public AppSettings Current => _current;

    public string Location => _path;

    public event EventHandler<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        _current = mutate(_current);
        Save();
        Changed?.Invoke(this, _current);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read settings; using defaults.");
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_current, Options));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist settings.");
        }
    }
}
