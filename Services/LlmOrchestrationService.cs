using System.Text.RegularExpressions;
using ColairShaderPainter.Models;

namespace ColairShaderPainter.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// LlmOrchestrationService.cs
//
// THE CORE AI ORCHESTRATION ENGINE — the "brain" of COLAIR.
//
// This is where the magic happens. The Self-Healing loop works like this:
//
//   1. User types: "a cosmic black hole devouring a fractal galaxy"
//   2. We inject that into a detailed system prompt that tells the AI
//      how to write GLSL shaders for our engine
//   3. We send it to the selected AI provider
//   4. We extract the GLSL code from the AI's response
//   5. We hand it to the GPU to compile
//   6a. ✅ SUCCESS → render it on screen, user is happy
//   6b. ❌ FAIL → we take the GPU error log, build a "fix this" prompt,
//       send it back to the AI, and go to step 4 (up to maxRetries times)
//
// This loop is what makes COLAIR "self-healing" — the AI fixes its own
// compilation errors without the user ever seeing them.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Core AI orchestration engine.
///
/// Implements the full Self-Healing compilation feedback loop:
///
///   User Prompt
///     ↓
///   Inject into Engine System Prompt
///     ↓
///   Send to AI provider
///     ↓
///   Extract GLSL code
///     ↓
///   GPU Compilation → ✅ Success: render it!
///     ↓  (failure)
///   Send error log back to AI → "Fix this"
///     ↓
///   Try again (up to maxRetries times)
///
/// This class is a singleton — one instance shared across the whole app.
/// </summary>
public sealed class LlmOrchestrationService : ILlmService
{
    /// <summary>Factory that creates the right LLM provider instance</summary>
    private readonly LlmProviderFactory _factory;

    /// <summary>Access to the current settings (API keys, selected provider, etc.)</summary>
    private readonly SettingsService    _settings;

    // ══════════════════════════════════════════════════════════════════════════
    // ENGINE SYSTEM PROMPT
    //
    // This is the secret sauce. When we send a user prompt to the AI, we
    // prepend a detailed "system prompt" that tells the AI EXACTLY how to
    // write shaders for our engine. Without this, the AI would write code
    // in any format and it wouldn't work with our GPU pipeline.
    //
    // The requirements are very specific:
    //   - GLSL #version 330 core (our minimum supported version)
    //   - 3D raymarching with 50+ iterations (for complex 3D scenes)
    //   - SDFs (Signed Distance Fields) — a technique for 3D shapes
    //   - fBm noise (Fractal Brownian Motion) — for organic textures
    //   - Standard uniforms: iTime (time), iResolution (screen size)
    //   - Up to 4 custom uniforms for user control sliders
    //   - ACES tonemapping + vignette + chromatic aberration + gamma
    //   - Output ONLY raw code (no explanations)
    //
    // The {userPrompt} placeholder is replaced with the actual user input.
    // ══════════════════════════════════════════════════════════════════════════
    private const string EngineSystemPrompt = """
        You generate GLSL fragment shaders (#version 330 core). Output ONLY raw code.

        REQUIRE: 3D raymarching (50+ iters), SDFs with smooth-min, fBm noise, fog, nebula background.
        POST: ACES tonemapping, vignette, chromatic aberration, gamma 2.2.
        UNIFORMS: float iTime; vec2 iResolution; + up to 4 custom (mutationSpeed, primaryGlow, secondaryGlow, bloomIntensity). NO textures.
        CAMERA: orbit smoothly. Define functions before main(). Keep output under 150 lines.

        Concept: [INSERT_USER_PROMPT_HERE]
        """;

    /// <summary>
    /// Regex to extract code from fenced code blocks in the AI's response.
    ///
    /// AI models often wrap code in triple backticks like:
    ///   ```glsl
    ///   void main() { ... }
    ///   ```
    ///
    /// This regex finds anything between ``` and ```, handling optional
    /// language identifiers like "glsl", "frag", or "c".
    ///
    /// Regex breakdown:
    ///   ```          — literal backtick fence
    ///   (?:glsl|...) — optional language identifier (non-capturing group)
    ///   \s*\n        — whitespace + newline after the opening fence
    ///   (.*?)        — the actual code (lazy capture)
    ///   ```          — closing fence
    /// </summary>
    private static readonly Regex CodeFenceRe = new(
        @"```(?:glsl|frag|hlsl|c)?\s*\n(.*?)```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LlmOrchestrationService(LlmProviderFactory factory, SettingsService settings)
    {
        _factory  = factory;
        _settings = settings;
    }

    /// <summary>
    /// THE MAIN ENTRY POINT — generates and compiles a shader from a prompt.
    ///
    /// This method orchestrates the entire self-healing loop:
    ///
    /// 1. Find the active provider (or auto-detect one with an API key)
    /// 2. Build the system prompt + user prompt
    /// 3. Loop: call AI → extract code → compile → success? done! → fail? fix and repeat
    /// 4. Return the working shader source, or null if all attempts failed
    /// </summary>
    public async Task<string?> GenerateAndCompileAsync(
        string userPrompt,
        Func<string, Task<ShaderCompilationResult>> compileShader,
        Action<string> onStatusUpdate,
        CancellationToken cancellationToken = default)
    {
        // Read the latest settings
        var settings = _settings.Current;

        // ── Step 1: Find the AI provider to use ─────────────────────────────
        // First try the user's selected active provider. If that has no API key,
        // automatically find the first provider that does have one.
        string activeKey = settings.ActiveProvider;
        ProviderConfig? activeCfg = null;

        if (!string.IsNullOrWhiteSpace(activeKey) &&
            settings.Providers.TryGetValue(activeKey, out var ap) &&
            !string.IsNullOrWhiteSpace(ap.ApiKey))
        {
            activeCfg = ap;
        }
        else
        {
            // Auto-detect: scan all providers for one with a non-empty API key
            foreach (var (key, cfg) in settings.Providers)
            {
                if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
                {
                    activeKey = key;
                    activeCfg = cfg;
                    break;
                }
            }
        }

        // No provider has an API key configured — tell the user
        if (activeCfg is null)
        {
            onStatusUpdate("❌ No API key found — open Settings ⚙, type key in a provider, then Generate again.");
            return null;
        }

        // Save the active provider selection
        _settings.SetActiveProvider(activeKey);
        onStatusUpdate($"⚡ {activeCfg.Name} / {activeCfg.Model} generating...");

        // ── Step 2: Build the prompts ───────────────────────────────────────
        // Replace the placeholder in the system prompt with the user's concept
        string systemContent = EngineSystemPrompt.Replace(
            "[INSERT_USER_PROMPT_HERE]", userPrompt, StringComparison.Ordinal);

        var messages = new List<LlmMessage>
        {
            new("system", systemContent),  // Instructions for the AI
            new("user",   $"Create a spectacular ultra-complex shader for: {userPrompt}")
        };

        // ── Step 3: The self-healing loop ───────────────────────────────────
        int maxRetries = Math.Max(1, settings.MaxRetries);
        var providerName = $"{activeCfg.Name} / {activeCfg.Model}";

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            onStatusUpdate(attempt == 1
                ? $"⚡ Generating with {providerName}..."
                : $"🔧 Self-healing attempt {attempt}/{maxRetries} via {providerName}...");

            // ── Call the AI ──────────────────────────────────────────────
            string rawResponse;
            try
            {
                var provider = _factory.GetCurrentProvider();
                rawResponse = await provider.CompleteChatAsync(messages, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                onStatusUpdate($"❌ API error: {ex.Message}");
                return null;
            }

            // Add the AI's response to the conversation history
            // (so on the next retry, the AI sees what it wrote before)
            messages.Add(new LlmMessage("assistant", rawResponse));

            // ── Extract the GLSL code ─────────────────────────────────────
            // Strip markdown code fences, if present
            string shaderCode = ExtractShaderCode(rawResponse);
            onStatusUpdate("🔵 Compiling on GPU...");

            // ── Send to GPU for compilation ───────────────────────────────
            ShaderCompilationResult result = await compileShader(shaderCode);

            // ✅ Success! Return the working shader
            if (result.Success)
            {
                onStatusUpdate("✅ Shader compiled — AI art is live.");
                return shaderCode;
            }

            // ❌ Compilation failed — build a fix prompt and loop back
            string fixPrompt = BuildFixPrompt(result.ErrorLog ?? result.ErrorMessage ?? "Unknown error");
            onStatusUpdate("⚠️ GPU error detected — AI is rewriting...");
            messages.Add(new LlmMessage("user", fixPrompt));
        }

        // All retries exhausted
        onStatusUpdate("❌ All retries exhausted. Check your API key in Settings.");
        return null;
    }

    /// <summary>
    /// Extracts GLSL code from the AI's response.
    ///
    /// AI models often wrap code in Markdown fenced blocks:
    ///   ```glsl
    ///   void main() { ... }
    ///   ```
    ///
    /// This method strips those fences and returns just the raw code.
    /// If no fenced block is found, it returns the entire response as-is
    /// (the AI might have output raw code without fences).
    /// </summary>
    private static string ExtractShaderCode(string llmResponse)
    {
        var match = CodeFenceRe.Match(llmResponse);
        return match.Success ? match.Groups[1].Value.Trim() : llmResponse.Trim();
    }

    /// <summary>
    /// Builds a "fix this" prompt from the GPU driver's error log.
    ///
    /// When the shader fails to compile, we take the driver's error message
    /// (which includes line numbers and error descriptions) and format it
    /// into a clear instruction for the AI: "Here's exactly what went wrong.
    /// Fix only the syntax issues, keep the visual design the same."
    /// </summary>
    private static string BuildFixPrompt(string errorLog) => $"""
        The shader failed GPU compilation. Exact driver error log:

        ── GLSL COMPILER OUTPUT ──
        {errorLog}
        ── END ──

        Fix every error above (check line numbers carefully).
        Keep the same visual design — only fix syntax/semantic issues.
        Output ONLY the corrected raw GLSL. No explanations.
        """;
}
