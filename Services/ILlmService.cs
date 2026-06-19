using ColairShaderPainter.Models;

namespace ColairShaderPainter.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// ILlmService.cs
//
// Defines the public contract (interface) for the AI orchestration layer.
//
// An "interface" is like a promise: it says "any class that claims to be
// an ILlmService MUST have a method called GenerateAndCompileAsync with
// these exact parameters." This lets the rest of the app talk to the AI
// without caring about which specific implementation is being used.
//
// Think of it like a power socket: you don't care which power plant is
// behind the wall — you just need the socket to work the same way every time.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Contract for the AI shader generation service.
///
/// Any class that implements this interface promises to:
/// 1. Take a user's text description ("a cosmic black hole")
/// 2. Send it to an AI along with instructions for writing GLSL shaders
/// 3. Take the AI's code and try to compile it on the GPU
/// 4. If compilation fails, send the error back to the AI to fix itself
/// 5. Repeat step 3-4 until success or max retries exhausted
///
/// This is the "self-healing loop" — the core innovation of COLAIR.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// The main entry point: takes a user's prompt, runs it through the AI,
    /// compiles on GPU, and self-heals on failure.
    ///
    /// This method is async (returns a Task), meaning it runs in the background
    /// without freezing the UI. The UI stays responsive while the AI thinks.
    /// </summary>
    /// <param name="userPrompt">
    /// What the user typed — their visual concept description.
    /// Example: "a neon cyberpunk city with volumetric fog and rain"
    /// </param>
    /// <param name="compileShader">
    /// A function (delegate) that sends GLSL source code to the GPU and returns
    /// whether it compiled. The ViewModel provides this from the viewport.
    ///
    /// Think of this as a "remote control" for the GPU — we give it source code
    /// and it tells us if the GPU liked it or found errors.
    /// </param>
    /// <param name="onStatusUpdate">
    /// A function that gets called with status messages like "Compiling on GPU..."
    /// so the UI can display progress without needing to know our internals.
    /// </param>
    /// <param name="cancellationToken">
    /// A way for the user to cancel mid-generation (clicking the Cancel button).
    /// </param>
    /// <returns>
    /// The GLSL source code that finally compiled successfully, or null if
    /// all self-healing attempts failed.
    /// </returns>
    Task<string?> GenerateAndCompileAsync(
        string userPrompt,
        Func<string, Task<ShaderCompilationResult>> compileShader,
        Action<string> onStatusUpdate,
        CancellationToken cancellationToken = default);
}
