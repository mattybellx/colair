using ColairShaderPainter.Graphics;

namespace ColairShaderPainter.Models;

// ═══════════════════════════════════════════════════════════════════════════════
// ShaderCompilationResult.cs
//
// This is the "result envelope" that wraps every shader compilation attempt.
// Instead of returning a bare ShaderProgram (which would be null on failure),
// or throwing exceptions for every error, this class gives us a clean way to
// say "here's what happened" — either success with a program, or failure with
// an error log that the AI can read to fix its own code.
//
// This is an example of a "discriminated union" pattern: one object that can
// hold either a success result OR a failure result, with no ambiguity.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Wraps the outcome of trying to compile an AI-generated GLSL shader on the GPU.
///
/// Think of this as a "safe return envelope":
/// - If compilation worked → Success=true, Program has the live GPU object
/// - If compilation failed → Success=false, ErrorLog has the driver's error message
///
/// This clean separation lets the AI orchestration layer inspect the error log
/// and ask the LLM to fix its own code (self-healing loop).
/// </summary>
public sealed class ShaderCompilationResult
{
    /// <summary>
    /// True = the shader compiled and linked on the GPU successfully.
    /// False = something went wrong — check ErrorMessage / ErrorLog.
    /// </summary>
    public bool Success { get; private init; }

    /// <summary>
    /// The live GPU program handle. Only set when Success is true.
    /// This is what the viewport uses to draw the shader on screen.
    /// </summary>
    public ShaderProgram? Program { get; private init; }

    /// <summary>
    /// A short, human-readable error message (e.g. "Line 42: syntax error").
    /// Only set when Success is false.
    /// </summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>
    /// The FULL driver error log with line numbers — this gets fed back to the
    /// AI so it can see exactly what went wrong and fix its own code.
    /// Only set when Success is false.
    /// </summary>
    public string? ErrorLog { get; private init; }

    // Private constructor forces callers to use the static factory methods below
    private ShaderCompilationResult() { }

    /// <summary>
    /// Factory method: creates a "success" result wrapping a compiled GPU program.
    ///
    /// Usage: ShaderCompilationResult.Ok(myProgram)
    /// </summary>
    public static ShaderCompilationResult Ok(ShaderProgram program) =>
        new() { Success = true, Program = program };

    /// <summary>
    /// Factory method: creates a "failure" result with error details for the AI to fix.
    ///
    /// Usage: ShaderCompilationResult.Fail("syntax error", driverLog)
    /// </summary>
    public static ShaderCompilationResult Fail(string message, string log) =>
        new() { Success = false, ErrorMessage = message, ErrorLog = log };
}
