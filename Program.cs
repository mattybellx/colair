using Avalonia;
using Avalonia.Win32;

namespace ColairShaderPainter;

// ═══════════════════════════════════════════════════════════════════════════════
// Program.cs — Application Entry Point
//
// This is where the app starts. The Main() method is the very first code
// that runs when the user launches COLAIR.
//
// What happens here:
//   1. A global crash handler is registered (writes to a log file)
//   2. The Avalonia UI framework is configured and started
//   3. The Win32 rendering mode is set to prefer WGL (OpenGL) first,
//      then Angle/Egl (fallback), then Software (last resort)
//
// The [STAThread] attribute is required for any Windows GUI app — it sets
// the thread to "Single Threaded Apartment" mode, which is necessary for
// COM interop (handles native window creation properly).
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Application entry point. This is the Main method — the first code that runs.
///
/// [STAThread] = Single Threaded Apartment. Required for Windows GUI apps.
/// It ensures the main thread is configured for proper window and clipboard handling.
/// </summary>
internal sealed class Program
{
    /// <summary>
    /// The Main method — where everything begins.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        // ── Global exception handler ─────────────────────────────────────────
        // Catches ANY unhandled exception that would otherwise crash the app
        // silently. Writes the error details to a log file in the temp folder.
        //
        // This is a safety net — in a well-designed app, most exceptions are
        // caught closer to where they happen. But bugs happen, and this ensures
        // we have a record of what went wrong.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            try
            {
                var dir = System.IO.Path.GetTempPath();
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "colair_crash.log"),
                    $"[{DateTime.Now:HH:mm:ss}] UNHANDLED: {(ex?.ToString() ?? "unknown")}\n");
            }
            catch { }
        };

        // ── Configure and start Avalonia ─────────────────────────────────────
        return AppBuilder.Configure<App>()           // Use our App class
            .UsePlatformDetect()                      // Auto-detect OS (Windows/Mac/Linux)
            .WithInterFont()                          // Use the Inter font family
            .LogToTrace()                             // Log diagnostics to debug output
            .With(new Win32PlatformOptions
            {
                // Rendering mode priority:
                // 1. Wgl = native OpenGL (best performance, what we want)
                // 2. AngleEgl = OpenGL via ANGLE (good fallback)
                // 3. Software = CPU rendering (slow, last resort)
                RenderingMode =
                [
                    Win32RenderingMode.Wgl,
                    Win32RenderingMode.AngleEgl,
                    Win32RenderingMode.Software
                ]
            })
            .StartWithClassicDesktopLifetime(args);  // Start the main window loop
    }
}
