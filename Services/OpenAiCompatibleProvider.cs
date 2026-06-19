using System.Net.Http.Headers;
using System.Net.Http.Json;
using ColairShaderPainter.Models;

namespace ColairShaderPainter.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// OpenAiCompatibleProvider.cs
//
// Implementation of ILlmProvider for ANY service that speaks the OpenAI
// /chat/completions protocol. This covers the vast majority of AI APIs:
//
//   ✅ OpenAI (api.openai.com)
//   ✅ DeepSeek (api.deepseek.com)
//   ✅ Groq (api.groq.com)
//   ✅ Ollama (localhost:11434 — local LLMs)
//   ✅ LM Studio (localhost:1234)
//   ✅ Azure OpenAI (.openai.azure.com)
//   ✅ Together AI, Fireworks AI, OpenRouter, etc.
//
// All of these use the same pattern:
//   POST /chat/completions
//   Authorization: Bearer <key>
//   Body: { model, messages, temperature, max_tokens }
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// LLM provider for any OpenAI-compatible API endpoint.
///
/// This is the "universal adapter" — it speaks the most common AI API format,
/// so it works with dozens of different providers and local LLM servers.
///
/// The only difference between providers is:
///   - The base URL (where to send requests)
///   - The API key (who you are)
///   - The model name (which AI to talk to)
/// Everything else is identical.
/// </summary>
public sealed class OpenAiCompatibleProvider : ILlmProvider
{
    /// <summary>
    /// Shared HTTP client. Static = one connection pool for the whole app,
    /// which prevents socket exhaustion (a common bug in .NET apps that
    /// create too many HTTP clients).
    /// </summary>
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Configuration for this specific provider (URL, key, model, etc.)</summary>
    private readonly ProviderConfig _config;

    /// <summary>
    /// Returns the provider type. For standard OpenAI calls this is ProviderType.OpenAi;
    /// for OpenAI-compatible providers (DeepSeek, Groq, Ollama, etc.) it returns OpenAiCompatible.
    /// </summary>
    public ProviderType ProviderType => _config.Type;

    public OpenAiCompatibleProvider(ProviderConfig config) => _config = config;

    /// <summary>
    /// Sends a list of messages to the AI and returns the text response.
    ///
    /// Step by step:
    /// 1. Build the HTTP POST request to {baseUrl}/chat/completions
    /// 2. Add the Bearer token (API key) to the Authorization header
    /// 3. Format the body as an LlmChatRequest (model, messages, etc.)
    /// 4. Send it over the network
    /// 5. Parse the response and extract the assistant's message
    /// </summary>
    public async Task<string> CompleteChatAsync(
        IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
    {
        // Build the POST request to the /chat/completions endpoint
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_config.BaseUrl.TrimEnd('/') + "/"), "chat/completions"));

        // Add the API key as a Bearer token in the Authorization header
        // Bearer auth: "I have a token that proves I'm allowed to use this API"
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

        // Build the request body using our standard LlmChatRequest model
        var body = new LlmChatRequest
        {
            Model       = _config.Model,
            Messages    = [.. messages],   // Spread the list into an array
            Temperature = 0.8f,            // 0.8 = moderately creative
            MaxTokens   = 1024             // Allow up to ~1000 words output
        };

        request.Content = JsonContent.Create(body);

        // Send the request — this is the slow part (network I/O)
        var response = await _http.SendAsync(request, ct);

        // Throw if we got an error status code
        response.EnsureSuccessStatusCode();

        // Parse the JSON response into our LlmChatResponse model
        var result = await response.Content.ReadFromJsonAsync<LlmChatResponse>(
            cancellationToken: ct);

        // Extract the text from the first choice (the AI's main reply)
        return result?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("Provider returned empty response.");
    }
}
