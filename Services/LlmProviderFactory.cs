using ColairShaderPainter.Models;

namespace ColairShaderPainter.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// LlmProviderFactory.cs
//
// A "factory" is a design pattern where one class is responsible for creating
// instances of other classes. Instead of scattered "new XYZProvider()" calls
// throughout the code, everything goes through this single factory.
//
// This factory reads the current settings to figure out which AI provider the
// user selected, then creates the correct provider class automatically.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Creates the right ILlmProvider for the currently active provider setting.
///
/// Think of this as a vending machine: you press a button (the active provider
/// name), and it gives you the right provider instance.
///
/// Primary constructor syntax (C# 12): "LlmProviderFactory(SettingsService settings)"
/// means the constructor parameter is automatically available as a field.
/// </summary>
/// <param name="settings">The settings service — provides the current provider config</param>
public sealed class LlmProviderFactory(SettingsService settings)
{
    /// <summary>
    /// Returns a fresh provider for the currently active configuration.
    /// Checks the settings to find which provider is selected, then creates
    /// the matching class.
    ///
    /// Called every time the AI needs to generate a shader, so the latest
    /// settings (API key, model, etc.) are always used.
    /// </summary>
    public ILlmProvider GetCurrentProvider()
    {
        var cfg     = settings.Current;        // Get the latest settings
        var key     = cfg.ActiveProvider;       // Which provider is selected?

        // Try to find that provider's configuration in the dictionary
        if (!cfg.Providers.TryGetValue(key, out var providerConfig))
            throw new InvalidOperationException(
                $"Active provider '{key}' not found in settings.");

        // Select the correct implementation based on provider type
        return providerConfig.Type switch
        {
            // Anthropic uses a different API format (v1/messages with x-api-key)
            ProviderType.Anthropic => new AnthropicProvider(providerConfig),
            // Everything else uses the OpenAI-compatible /chat/completions format
            _                      => new OpenAiCompatibleProvider(providerConfig)
        };
    }
}
