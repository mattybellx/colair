namespace ColairShaderPainter.Models;

// ═══════════════════════════════════════════════════════════════════════════════
// AppSettings.cs
//
// This file defines the ROOT settings object for the entire application.
// All user preferences (which AI provider to use, API keys, rendering quality)
// are stored in a single JSON file at: %APPDATA%\Colair\settings.json
//
// AppSettings contains:
//   - A dictionary of AI provider configurations (OpenAI, Anthropic, DeepSeek)
//   - Which provider is currently active
//   - Rendering quality settings (SSAA factor)
//   - Self-healing retry count
//   - A custom JSON converter that knows how to read/write ProviderType enums
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Custom JSON converter for the ProviderType enum.
///
/// JSON stores data as text, not C# objects. When we read settings from disk,
/// this converter tells the JSON library: "When you see 'OpenAi' in the file,
/// convert it to ProviderType.OpenAi". And when saving, it does the reverse.
///
/// This is needed because the default JSON converter might not handle our
/// custom enum naming (e.g. "OpenAiCompatible").
/// </summary>
public sealed class ProviderTypeConverter : System.Text.Json.Serialization.JsonConverter<ProviderType>
{
    /// <summary>
    /// Reads the enum value from JSON text. Called automatically when loading settings.
    /// </summary>
    public override ProviderType Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return str switch
        {
            "OpenAi" => ProviderType.OpenAi,
            "Anthropic" => ProviderType.Anthropic,
            "OpenAiCompatible" => ProviderType.OpenAiCompatible,
            _ => ProviderType.OpenAi  // Default fallback if unknown
        };
    }

    /// <summary>
    /// Writes the enum value as JSON text. Called automatically when saving settings.
    /// </summary>
    public override void Write(System.Text.Json.Utf8JsonWriter writer, ProviderType value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

/// <summary>
/// Root settings class — this is the ENTIRE configuration for COLAIR.
/// Everything the app needs to remember between runs lives here.
///
/// The SettingsService loads this from disk on startup and saves it when
/// the user clicks "Save & Close" in the settings panel.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Dictionary of all configured AI providers. The KEY is a short name
    /// like "OpenAi", "Anthropic", or "DeepSeek". The VALUE is the full
    /// ProviderConfig with URL, API key, model, etc.
    ///
    /// Dictionary = a collection of key-value pairs, like a real dictionary
    /// where you look up a word (key) to find its definition (value).
    /// </summary>
    public Dictionary<string, ProviderConfig> Providers { get; set; } = BuildDefaults();

    /// <summary>
    /// Which provider is currently selected for shader generation.
    /// This matches one of the keys in the Providers dictionary.
    /// Default: "OpenAi"
    /// </summary>
    public string ActiveProvider { get; set; } = "OpenAi";

    /// <summary>
    /// Super-Sampling Anti-Aliasing factor.
    /// - 1.0 = Native resolution (fastest)
    /// - 1.5 = 1.5x resolution (balanced)
    /// - 2.0 = 2x resolution, 4x the pixels (best quality, heavy GPU load)
    ///
    /// Higher values give smoother edges but use more GPU power.
    /// </summary>
    public float SsaaFactor { get; set; } = 1.0f;

    /// <summary>
    /// Maximum number of self-healing retries.
    /// If the AI generates a shader with errors, the app sends the error
    /// log back to the AI and asks it to fix the code. This is the limit
    /// on how many times we'll try before giving up.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Builds the default provider configurations. These are the starting
    /// values before the user customizes anything. They provide sensible
    /// defaults so the app works out of the box (once you add API keys).
    ///
    /// Each entry has:
    ///   - Type: Which API format to use
    ///   - Name: Display name in the UI
    ///   - Icon: A fun emoji
    ///   - BaseUrl: Where the API lives
    ///   - Model: Default AI model
    ///   - Models: List of models the user can choose from
    /// </summary>
    private static Dictionary<string, ProviderConfig> BuildDefaults() => new()
    {
        ["OpenAi"] = new ProviderConfig
        {
            Type    = ProviderType.OpenAi,
            Name    = "OpenAI",
            Icon    = "🤖",
            BaseUrl = "https://api.openai.com/v1",
            Model   = "gpt-4o",
            Models  = ["gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-4.5-preview", "o3", "o4-mini"]
        },
        ["Anthropic"] = new ProviderConfig
        {
            Type    = ProviderType.Anthropic,
            Name    = "Anthropic Claude",
            Icon    = "🔮",
            BaseUrl = "https://api.anthropic.com",
            Model   = "claude-opus-4-5",
            Models  = ["claude-opus-4-5", "claude-sonnet-4-5", "claude-haiku-4-5", "claude-opus-4", "claude-sonnet-4"]
        },
        ["DeepSeek"] = new ProviderConfig
        {
            Type    = ProviderType.OpenAiCompatible,
            Name    = "DeepSeek",
            Icon    = "🧠",
            BaseUrl = "https://api.deepseek.com/v1",
            Model   = "deepseek-chat",
            Models  = ["deepseek-chat", "deepseek-reasoner"]
        }
    };
}
