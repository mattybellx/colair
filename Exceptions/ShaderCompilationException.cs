namespace ColairShaderPainter.Exceptions;

// ═══════════════════════════════════════════════════════════════════════════════
// ShaderCompilationException.cs
//
// A custom exception type specifically for GPU shader compilation failures.
//
// When the AI generates a GLSL shader that has errors (syntax mistake,
// wrong function name, type mismatch, etc.), the GPU driver returns an
// error message with line numbers. This exception wraps that error so
// we can pass it back to the AI for self-healing.
//
// Instead of the app crashing, the LlmOrchestrationService catches this
// exception, reads the ErrorLog, and sends it back to the LLM with:
//   "Fix this error: [ErrorLog]"
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Thrown when an AI-generated GLSL shader fails to compile on the GPU.
///
/// This is NOT a crash — it's a recoverable error that triggers the
/// self-healing loop. The LlmOrchestrationService catches this exception,
/// extracts the driver error log, and asks the AI to fix its own code.
///
/// Primary constructor syntax (C# 12): the parameters after the class name
/// automatically become properties. So "string message" becomes the base
/// Exception.Message, and "string errorLog" becomes this.ErrorLog.
/// </summary>
/// <param name="message">Short human-readable error description</param>
/// <param name="errorLog">
///   The FULL OpenGL driver error log — includes line numbers, error codes,
///   and detailed messages. This is fed directly into the AI's fix-prompt.
/// </param>
public sealed class ShaderCompilationException(string message, string errorLog)
    : Exception(message)
{
    /// <summary>
    /// The verbatim OpenGL info-log from the GPU driver.
    /// Contains line numbers and error codes like:
    ///   "ERROR: 0:42: 'undefinedFunction' : no matching overloaded function found"
    ///
    /// This string is sent back to the AI so it can see exactly what went wrong
    /// and fix the code accordingly.
    /// </summary>
    public string ErrorLog { get; } = errorLog;
}
