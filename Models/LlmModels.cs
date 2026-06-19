using System.Text.Json.Serialization;

namespace ColairShaderPainter.Models;

// ═══════════════════════════════════════════════════════════════════════════════
// LlmModels.cs
//
// Data Transfer Objects (DTOs) for the OpenAI-compatible Chat Completions API.
//
// When COLAIR talks to an AI provider, it sends a JSON request over HTTP and
// gets a JSON response back. These C# classes model exactly what that JSON
// looks like, so the JSON deserializer can automatically convert between
// raw JSON and typed C# objects.
//
// Compatible with: OpenAI, DeepSeek, Groq, Ollama, LM Studio, Azure OpenAI,
// and any other service that speaks the OpenAI chat format.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Holds the connection details for an LLM API endpoint.
/// This is read from the appsettings.json config file.
///
/// Note: This class is separate from ProviderConfig. LlmSettings was the
/// original single-provider config; the newer ProviderConfig system supports
/// multiple providers. LlmSettings is kept for backwards compatibility.
/// </summary>
public sealed class LlmSettings
{
    /// <summary>The API server URL (e.g. "https://api.openai.com/v1")</summary>
    public string BaseUrl      { get; set; } = "https://api.openai.com/v1";

    /// <summary>Your secret API key / authentication token</summary>
    public string ApiKey       { get; set; } = string.Empty;

    /// <summary>Which model to use (e.g. "gpt-4o", "claude-sonnet-4-5")</summary>
    public string Model        { get; set; } = "gpt-4o";

    /// <summary>How many times to retry before giving up on compilation</summary>
    public int    MaxRetries   { get; set; } = 5;

    /// <summary>How long to wait for a response before timing out (seconds)</summary>
    public int    TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// Represents ONE message in a chat conversation with an AI.
///
/// Messages have a "role" that tells the AI who's speaking:
///   - "system": Instructions that set the AI's behaviour
///   - "user": What the human says
///   - "assistant": What the AI replies
///
/// The JsonPropertyName attributes tell the JSON library what field names
/// to use when converting this to/from JSON (e.g. {"role": "user", "content": "Hello"})
/// </summary>
/// <param name="Role">Who said this? ("system", "user", or "assistant")</param>
/// <param name="Content">The actual text of the message</param>
public sealed record LlmMessage(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content);

/// <summary>
/// The request body sent to the AI's /chat/completions endpoint.
///
/// This is what the HTTP request looks like:
/// {
///   "model": "gpt-4o",
///   "messages": [...],
///   "temperature": 0.85,
///   "max_tokens": 4096
/// }
/// </summary>
public sealed class LlmChatRequest
{
    /// <summary>Which AI model to use (e.g. "gpt-4o", "deepseek-chat")</summary>
    [JsonPropertyName("model")]       public string Model       { get; init; } = string.Empty;

    /// <summary>The conversation history — a list of messages (system + user + assistant)</summary>
    [JsonPropertyName("messages")]    public List<LlmMessage> Messages { get; init; } = [];

    /// <summary>
    /// Controls randomness: 0 = very deterministic (always picks the most likely word),
    /// 1 = very creative (picks less likely words). We use 0.85 for artistic shaders.
    /// </summary>
    [JsonPropertyName("temperature")] public float Temperature  { get; init; } = 0.85f;

    /// <summary>Maximum number of words/tokens the AI is allowed to generate</summary>
    [JsonPropertyName("max_tokens")]  public int   MaxTokens   { get; init; } = 4096;
}

/// <summary>
/// The response we get back from the AI's /chat/completions endpoint.
/// We only extract the fields we actually need (the rest are ignored).
/// </summary>
public sealed class LlmChatResponse
{
    /// <summary>
    /// The AI can suggest multiple different replies ("choices").
    /// We always take the first one (index 0).
    /// </summary>
    [JsonPropertyName("choices")] public List<LlmChoice> Choices { get; init; } = [];
}

/// <summary>
/// A single "choice" (reply) from the AI. Contains the message text.
/// </summary>
public sealed class LlmChoice
{
    /// <summary>The actual message content (role + text) from the AI's reply</summary>
    [JsonPropertyName("message")] public LlmMessage? Message { get; init; }
}
