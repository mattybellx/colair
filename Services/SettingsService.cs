using System.Text.Json;
using ColairShaderPainter.Models;

namespace ColairShaderPainter.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// SettingsService.cs
//
// Handles saving and loading application settings (API keys, provider config,
// rendering quality) to/from a JSON file on disk.
//
// Settings are stored at: %APPDATA%\Colair\settings.json
// On Windows, %APPDATA% is usually: C:\Users\<You>\AppData\Roaming
//
// This is a singleton service — one instance shared across the entire app.
// It keeps the settings in memory for fast access and writes to disk whenever
// the user clicks "Save & Close" in the settings panel.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Manages loading, saving, and providing access to application settings.
///
/// Think of this as a "settings locker": you put settings in, it remembers
/// them in memory, and also writes them to disk so they survive app restarts.
///
/// The settings file is JSON (a human-readable text format), so advanced
/// users can even edit it manually with Notepad if they want.
/// </summary>
public sealed class SettingsService
{
    /// <summary>
    /// Full path to the settings file on disk.
    /// %APPDATA%\Colair\settings.json
    /// </summary>
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Colair", "settings.json");

    /// <summary>
    /// Configuration for the JSON serializer/deserializer.
    ///
    /// - WriteIndented: Makes the JSON file human-readable with indentation
    /// - PropertyNameCaseInsensitive: "ssaafactor" matches "SsaaFactor"
    /// - Converters: Custom converter for our ProviderType enum
    /// </summary>
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented         = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new ProviderTypeConverter() }
    };

    /// <summary>The current in-memory settings cache</summary>
    private AppSettings _current;

    /// <summary>
    /// Constructor: loads settings from disk on startup.
    /// If the file doesn't exist yet, Load() returns default values.
    /// </summary>
    public SettingsService() => _current = Load();

    /// <summary>
    /// Provides access to the current settings from anywhere in the app.
    /// Always up-to-date — updated whenever Save() is called.
    /// </summary>
    public AppSettings Current => _current;

    /// <summary>
    /// Reads settings from the JSON file on disk.
    ///
    /// If the file exists and is valid, returns the deserialized settings.
    /// If the file doesn't exist or is corrupted, returns default settings
    /// (which include default provider configs for OpenAI, Anthropic, DeepSeek).
    ///
    /// Also merges in any new providers that were added in code updates
    /// but aren't in the user's saved file yet.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json    = File.ReadAllText(SettingsPath);
                var loaded  = JsonSerializer.Deserialize<AppSettings>(json, _json);
                if (loaded is not null)
                {
                    // Merge: add any new providers from code defaults
                    // (e.g., if we added a new provider in an app update)
                    var defaults = new AppSettings();
                    foreach (var (k, v) in defaults.Providers)
                        loaded.Providers.TryAdd(k, v);

                    _current = loaded;
                    return _current;
                }
            }
        }
        catch
        {
            // Silently fall through to defaults
            // If settings.json is corrupted, we don't want to crash the app
        }

        // No valid file found — use factory defaults
        _current = new AppSettings();
        return _current;
    }

    /// <summary>
    /// Writes settings to disk as JSON and updates the in-memory cache.
    ///
    /// Creates the directory (%APPDATA%\Colair) if it doesn't exist yet.
    /// If writing fails (e.g., disk full, permission denied), the in-memory
    /// settings still work — the app just won't persist the change.
    /// </summary>
    public void Save(AppSettings settings)
    {
        _current = settings;

        try
        {
            // Ensure the directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            // Write the JSON to disk
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, _json));
        }
        catch
        {
            // Non-fatal — in-memory settings still work for this session
        }
    }

    /// <summary>
    /// Quick way to update a single provider's configuration and save.
    /// </summary>
    public void UpdateProvider(string key, ProviderConfig config)
    {
        _current.Providers[key] = config;
        Save(_current);
    }

    /// <summary>
    /// Quick way to change the active provider and save.
    /// </summary>
    public void SetActiveProvider(string key)
    {
        _current.ActiveProvider = key;
        Save(_current);
    }
}
