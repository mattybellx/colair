using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ColairShaderPainter.Models;

namespace ColairShaderPainter.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// AnthropicProvider.cs
//
// Implementation of ILlmProvider for Anthropic Claude.
//
// Anthropic's API is different from OpenAI's:
//   - Uses a different endpoint: /v1/messages (not /chat/completions)
//   - Authenticates with x-api-key header (not Bearer token)
//   - Has a separate "system" field instead of a system message in the list
//   - Uses "anthropic-version" header to specify API version
//
// The "file sealed class" types below are DTOs (Data Transfer Objects) —
// they exist ONLY to model the JSON that goes over the network. The "file"
// keyword means they're only visible within this file (C# 11 feature).
// ═══════════════════════════════════════════════════════════════════════════════

// ── Anthropic-specific DTOs ──────────────────────────────────────────────────

/// <summary>
/// The JSON body sent to Anthropic's /v1/messages endpoint.
/// Note the different format: "system" is separate from "messages".
/// </summary>
file sealed class AnthropicRequest
{
    /// <summary>Which Claude model to use (e.g. "claude-opus-4-5")</summary>
    [JsonPropertyName("model")]      public string Model     { get; init; } = string.Empty;

    /// <summary>Maximum output length in tokens (words ≈ ¾ of a token)</summary>
    [JsonPropertyName("max_tokens")] public int    MaxTokens { get; init; } = 8192;

    /// <summary>System prompt — sets Claude's behaviour (separate from messages)</summary>
    [JsonPropertyName("system")]     public string System    { get; init; } = string.Empty;

    /// <summary>The conversation history (user + assistant turns)</summary>
    [JsonPropertyName("messages")]   public List<AnthropicMessage> Messages { get; init; } = [];
}

/// <summary>A single message in the Anthropic conversation format</summary>
file sealed record AnthropicMessage(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content);

/// <summary>The response from Anthropic's API — contains a list of content blocks</summary>
file sealed class AnthropicResponse
{
    /// <summary>Content blocks (can be text, tool_use, etc.)</summary>
    [JsonPropertyName("content")] public List<AnthropicContent> Content { get; init; } = [];
}

/// <summary>A single content block from the response</summary>
file sealed class AnthropicContent
{
    /// <summary>Type of content: "text" is what we want</summary>
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;

    /// <summary>The actual text content</summary>
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
}

// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// LLM provider implementation for Anthropic Claude.
///
/// This class knows how to:
/// 1. Take our internal LlmMessage format and convert it to Anthropic's format
/// 2. Send HTTP requests with the correct headers (x-api-key, anthropic-version)
/// 3. Parse the response and extract the generated text
///
/// The static HttpClient is shared across all instances (recommended practice
/// to avoid socket exhaustion — opening too many network connections).
/// </summary>
public sealed class AnthropicProvider : ILlmProvider
{
    /// <summary>
    /// Shared HTTP client. Static = one instance for the entire app.
    /// Timeout of 20 seconds means if the API doesn't respond in time,
    /// we give up instead of waiting forever.
    /// </summary>
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Configuration for this specific provider (URL, key, model, etc.)</summary>
    private readonly ProviderConfig _config;

    public ProviderType ProviderType => Models.ProviderType.Anthropic;

    public AnthropicProvider(ProviderConfig config) => _config = config;

    /// <summary>
    /// Sends a list of messages to Claude and returns the text response.
    ///
    /// Step by step:
    /// 1. Find the system message (instructions for behaviour) — Claude handles
    ///    this differently from OpenAI, so we extract it from the message list.
    /// 2. Build the HTTP request with Anthropic-specific headers
    /// 3. Send the request and wait for the response
    /// 4. Parse the JSON response and extract the text
    /// </summary>
    public async Task<string> CompleteChatAsync(
        IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
    {
        // Anthropic puts the system prompt in a separate field, not in the
        // messages array. So we need to find it and pull it out.
        var systemMsg = messages.FirstOrDefault(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));

        // The rest of the messages (user + assistant) go in the messages array
        var conversationMsgs = messages
            .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new AnthropicMessage(m.Role, m.Content))
            .ToList();

        // Build the POST request to /v1/messages
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_config.BaseUrl.TrimEnd('/') + "/"), "v1/messages"));

        // Anthropic auth: x-api-key header (not Bearer token like OpenAI)
        request.Headers.Add("x-api-key",         _config.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var body = new AnthropicRequest
        {
            Model     = _config.Model,
            MaxTokens = 1024,
            System    = systemMsg?.Content ?? string.Empty,
            Messages  = conversationMsgs
        };

        request.Content = JsonContent.Create(body);

        // Send the request — this is the actual network call
        var response = await _http.SendAsync(request, ct);

        // Throw if we got an HTTP error (4xx or 5xx)
        response.EnsureSuccessStatusCode();

        // Parse the JSON response body
        var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(
            cancellationToken: ct);

        // Extract the text from the first content block that has type "text"
        return result?.Content?.FirstOrDefault(c => c.Type == "text")?.Text
            ?? throw new InvalidOperationException("Anthropic returned empty response.");
    }
}
