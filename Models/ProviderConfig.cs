using System.Text.Json.Serialization;

namespace ColairShaderPainter.Models;

// ═══════════════════════════════════════════════════════════════════════════════
// ProviderConfig.cs
//
// Defines the data models for LLM (Large Language Model) provider configuration.
//
// COLAIR can talk to multiple AI providers: OpenAI, Anthropic Claude, DeepSeek,
// or any API that speaks the same protocol (OpenAI-compatible). Each provider
// needs a name, an API key, a model name, and a server URL.
//
// The ProviderType enum categorises providers so we know HOW to talk to them
// (the API format differs between Anthropic and OpenAI-compatible endpoints).
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Categories of AI providers. This tells the app which API format to use.
///
/// - OpenAi: Uses /chat/completions with Bearer token auth (standard OpenAI)
/// - Anthropic: Uses /v1/messages with x-api-key header (Claude-specific)
/// - OpenAiCompatible: Same format as OpenAI, but hosted elsewhere (DeepSeek,
///   Groq, Ollama, LM Studio, Azure OpenAI, etc.)
/// </summary>
public enum ProviderType
{
    OpenAi,            // Standard OpenAI API
    Anthropic,         // Anthropic Claude API (different message format)
    OpenAiCompatible   // Any server that speaks the OpenAI chat format
}

/// <summary>
/// Stores everything needed to communicate with ONE AI provider.
///
/// Think of this as an "address book entry" for an AI service: it has the
/// server URL, your API key (like a password), and which model to use.
///
/// These configs are stored in %APPDATA%\Colair\settings.json so your keys
/// and preferences persist between app restarts.
/// </summary>
public sealed class ProviderConfig
{
    /// <summary>Which API format does this provider use? (OpenAI, Anthropic, etc.)</summary>
    [JsonPropertyName("type")]    public ProviderType Type    { get; set; }

    /// <summary>Display name shown in the UI (e.g. "OpenAI", "Anthropic Claude")</summary>
    [JsonPropertyName("name")]    public string       Name    { get; set; } = string.Empty;

    /// <summary>An emoji icon shown next to the provider name in the settings panel</summary>
    [JsonPropertyName("icon")]    public string       Icon    { get; set; } = "🤖";

    /// <summary>
    /// The server URL where the AI API lives.
    /// Examples: "https://api.openai.com/v1", "https://api.anthropic.com"
    /// </summary>
    [JsonPropertyName("baseUrl")] public string       BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Your secret API key. This is like a password — it proves you're allowed
    /// to use the AI service. Stored in settings.json.
    /// </summary>
    [JsonPropertyName("apiKey")]  public string       ApiKey  { get; set; } = string.Empty;

    /// <summary>Which model to use (e.g. "gpt-4o", "claude-opus-4-5", "deepseek-chat")</summary>
    [JsonPropertyName("model")]   public string       Model   { get; set; } = string.Empty;

    /// <summary>
    /// List of available models the user can choose from in the dropdown.
    /// This comes from hardcoded defaults in AppSettings.cs.
    /// </summary>
    [JsonPropertyName("models")]  public List<string> Models  { get; set; } = [];
}

/// <summary>
/// Tracks the state of a connection test for a provider.
/// Used by ProviderCardViewModel to show test progress in the UI.
///
/// - Idle: No test has been run yet
/// - Testing: Currently checking the connection
/// - Connected: The API responded successfully
/// - Failed: Something went wrong (wrong key, network error, etc.)
/// </summary>
public enum ConnectionStatus { Idle, Testing, Connected, Failed }
