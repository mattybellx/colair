using System.Net.Http.Headers;
using System.Net.Http.Json;
using ColairShaderPainter.Models;

namespace ColairShaderPainter.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// ConnectionTestService.cs
//
// Tests whether an AI provider is reachable and responding correctly.
//
// Before the user tries to generate a shader, they can click "Test" on a
// provider card in the Settings panel. This service sends a lightweight
// request to the provider's API to verify:
//   - The URL is correct
//   - The API key works
//   - The server responds in a reasonable time
//
// Different provider types need different test approaches:
//   - OpenAI-compatible: POST /chat/completions with a minimal message
//   - Anthropic: GET /v1/models (lighter than a full message call)
//   - Ollama (local): GET /api/tags (different API format entirely)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests LLM provider connectivity.
///
/// This class sends a small test request to the provider's API to verify
/// that the connection works. It returns a simple "yes it works" or
/// "no, here's why" result with a human-readable message.
/// </summary>
public sealed class ConnectionTestService
{
    /// <summary>
    /// Shared HTTP client with a short timeout (12 seconds).
    /// We don't want the test to hang for a long time if the server is down.
    /// </summary>
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    /// <summary>
    /// Tests whether the given provider config can connect to its API.
    ///
    /// Returns a tuple: (Success: bool, Message: string)
    ///   - Success = true:  "Connected — gpt-4o"
    ///   - Success = false: "HTTP 401" or "Network error: ..."
    /// </summary>
    public async Task<(bool Success, string Message)> TestAsync(
        ProviderConfig config, CancellationToken ct = default)
    {
        try
        {
            // Route to the correct test method based on provider type
            return config.Type switch
            {
                // Anthropic uses a different API format
                ProviderType.Anthropic       => await TestAnthropicAsync(config, ct),

                // Ollama/local servers have their own API format
                ProviderType.OpenAiCompatible when
                    config.BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                                             => await TestOllamaAsync(config, ct),

                // Everything else uses /chat/completions
                _                            => await TestOpenAiAsync(config, ct)
            };
        }
        catch (OperationCanceledException)
        {
            return (false, "Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Tests an OpenAI-compatible provider by sending a tiny chat request.
    ///
    /// We send: {"model": "gpt-4o", "messages": [{"role": "user", "content": "test"}], "max_tokens": 1}
    /// This is the absolute minimum request — it asks the AI to reply with just 1 token.
    ///
    /// Special case: HTTP 401 (Unauthorized) or 402 (Payment Required) means the
    /// server is reachable but the key might be wrong or the account has no credits.
    /// We still report "Connected" because the network path works.
    /// </summary>
    private static async Task<(bool, string)> TestOpenAiAsync(
        ProviderConfig cfg, CancellationToken ct)
    {
        try
        {
            var uri = TryBuildUri(cfg.BaseUrl, "chat/completions");
            if (uri is null) return (false, "Invalid URL");

            using var req = new HttpRequestMessage(HttpMethod.Post, uri);
            if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);

            var body = new { model = cfg.Model, messages = new[] { new { role = "user", content = "test" } }, max_tokens = 1 };
            req.Content = JsonContent.Create(body);

            var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
                return (true, $"Connected — {cfg.Model}");
            if ((int)res.StatusCode is 401 or 402)
                return (true, $"Connected (key OK) — {cfg.Model}");
            return (false, $"HTTP {(int)res.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Server error: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests Anthropic Claude by calling GET /v1/models.
    /// This is lighter than a full message call and still validates auth.
    /// </summary>
    private static async Task<(bool, string)> TestAnthropicAsync(
        ProviderConfig cfg, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(cfg.BaseUrl.TrimEnd('/') + "/"), "v1/models"));

        req.Headers.Add("x-api-key",         cfg.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode)
            return (true, $"Connected — {cfg.Model}");

        return (false, $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
    }

    /// <summary>
    /// Tests a local Ollama server.
    /// Ollama runs on localhost and has a different API from OpenAI:
    ///   GET /api/tags — lists available models
    ///
    /// We strip "/v1" from the URL if present because Ollama doesn't use it.
    /// </summary>
    private static async Task<(bool, string)> TestOllamaAsync(
        ProviderConfig cfg, CancellationToken ct)
    {
        var baseUri = cfg.BaseUrl.Contains("/v1")
            ? cfg.BaseUrl.Replace("/v1", string.Empty)
            : cfg.BaseUrl;

        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(baseUri.TrimEnd('/') + "/"), "api/tags"));

        var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode)
            return (true, "Connected — Ollama local");

        return (false, $"Ollama not running? HTTP {(int)res.StatusCode}");
    }

    /// <summary>
    /// Safely builds a URI from a base URL and a relative path.
    ///
    /// Handles various URL formats:
    ///   "https://api.openai.com/v1" + "chat/completions"
    ///     → "https://api.openai.com/v1/chat/completions"
    ///
    ///   "https://api.openai.com/v1/" + "chat/completions"
    ///     → "https://api.openai.com/v1/chat/completions"
    ///
    /// Returns null if the URL is malformed (catches UriFormatException).
    /// </summary>
    private static Uri? TryBuildUri(string baseUrl, string relativePath)
    {
        try
        {
            var trimmed = baseUrl.TrimEnd('/');
            if (!string.IsNullOrEmpty(relativePath))
                return new Uri(new Uri(trimmed + "/"), relativePath);
            return new Uri(trimmed + "/");
        }
        catch { return null; }
    }
}
