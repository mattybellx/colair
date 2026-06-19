using ColairShaderPainter.Models;

namespace ColairShaderPainter.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// ILlmProvider.cs
//
// Defines the contract for talking to ANY AI provider (OpenAI, Anthropic, etc.).
//
// Just like ILlmService is the high-level "generate a shader" contract,
// ILlmProvider is the low-level "send a message to an AI and get a reply" contract.
//
// The ProviderFactory creates the right implementation based on which provider
// the user selected in settings.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Abstraction over any AI back-end API.
///
/// Different providers (OpenAI, Anthropic Claude, DeepSeek) have different
/// API formats and authentication methods. This interface hides those
/// differences behind a single method: "send messages, get text back."
///
/// Implementations:
///   - OpenAiCompatibleProvider → for OpenAI, DeepSeek, Ollama, etc.
///   - AnthropicProvider → for Claude (different message format)
/// </summary>
public interface ILlmProvider
{
    /// <summary>Which provider type is this? (OpenAI, Anthropic, etc.)</summary>
    ProviderType ProviderType { get; }

    /// <summary>
    /// Sends a list of chat messages to the AI and returns the text response.
    ///
    /// This is like typing to ChatGPT and getting a reply — but done
    /// programmatically from code instead of through a web page.
    /// </summary>
    /// <param name="messages">
    /// The conversation history: system instructions + user prompts + AI replies.
    /// Each message has a "role" (who said it) and "content" (what they said).
    /// </param>
    /// <param name="ct">Cancellation token to stop waiting for a response.</param>
    /// <returns>The AI's text response (the generated shader code).</returns>
    Task<string> CompleteChatAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken ct = default);
}
