using System.Numerics;
using ColairShaderPainter.Exceptions;
using Silk.NET.OpenGL;

namespace ColairShaderPainter.Graphics;

// ═══════════════════════════════════════════════════════════════════════════════
// ShaderProgram.cs
//
// Wraps a compiled OpenGL shader program — a GPU program that runs on your
// graphics card. This is the low-level interface between our C# code and
// the graphics hardware.
//
// Think of it like this:
//   - A "shader" is a small program that runs on the GPU (not the CPU)
//   - It's written in GLSL (OpenGL Shading Language) — a C-like language
//   - The CPU sends the GLSL source code to the GPU driver
//   - The GPU driver compiles it into machine code for the graphics card
//   - The resulting "program" can be used to draw things on screen
//
// This class handles:
//   1. Compiling vertex and fragment shader source code
//   2. Linking them into a complete GPU program
//   3. Setting uniform values (parameters that control the shader)
//   4. Caching uniform locations for performance
//   5. Cleaning up GPU memory when the program is no longer needed
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a compiled GPU shader program that can be used for rendering.
///
/// A shader program has two parts:
///   - Vertex Shader: Processes the shape's vertices (positions in 3D space)
///   - Fragment Shader: Colors each pixel on screen — this is what the AI generates
///
/// This class wraps Silk.NET's OpenGL bindings to compile, link, and use
/// shader programs. It also caches uniform locations so we don't have to
/// ask the GPU "where is variable X?" on every frame (that would be slow).
///
/// Implements IDisposable because GPU resources need to be explicitly freed
/// (unlike normal C# objects, the garbage collector doesn't handle GPU memory).
/// </summary>
public sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;           // The Silk.NET OpenGL API handle
    private readonly uint _handle;     // GPU-side handle to this program (an integer ID)

    /// <summary>
    /// Cache of uniform locations. On the GPU, uniform variables are identified
    /// by integer "locations" rather than names. Looking up a location by name
    /// (glGetUniformLocation) is slow, so we do it once and cache the result.
    ///
    /// Dictionary<string, int> = look up uniform name → get its GPU location number.
    /// StringComparer.Ordinal = case-sensitive comparison (GLSL is case-sensitive).
    /// </summary>
    private readonly Dictionary<string, int> _locationCache = new(16, StringComparer.Ordinal);

    private bool _disposed;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles a vertex shader and fragment shader, links them into a program.
    ///
    /// Step by step:
    /// 1. Compile the vertex shader source → GPU shader object
    /// 2. Compile the fragment shader source → GPU shader object
    /// 3. Create a program and attach both shaders to it
    /// 4. Link the program (GPU resolves cross-references between shaders)
    /// 5. Check if linking succeeded
    /// 6. Clean up the intermediate shader objects
    /// 7. If linking failed, throw ShaderCompilationException with the error log
    ///
    /// Throws ShaderCompilationException if compilation or linking fails.
    /// This exception is caught by the AI self-healing loop.
    /// </summary>
    public ShaderProgram(GL gl, string vertSrc, string fragSrc)
    {
        _gl = gl;

        // Compile both shader stages separately
        uint vert = CompileStage(ShaderType.VertexShader,   vertSrc);
        uint frag = CompileStage(ShaderType.FragmentShader, fragSrc);

        // Create the program and attach the compiled shaders
        _handle = gl.CreateProgram();
        gl.AttachShader(_handle, vert);
        gl.AttachShader(_handle, frag);

        // Link — this resolves references between vertex and fragment shaders
        gl.LinkProgram(_handle);

        // Check if linking succeeded
        gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int linked);
        string linkLog = gl.GetProgramInfoLog(_handle);

        // Clean up: detach and delete the intermediate shader objects
        // (they're already compiled and linked into the program)
        gl.DetachShader(_handle, vert);
        gl.DetachShader(_handle, frag);
        gl.DeleteShader(vert);
        gl.DeleteShader(frag);

        if (linked == 0)
        {
            // Linking failed — clean up and throw
            gl.DeleteProgram(_handle);
            throw new ShaderCompilationException(
                $"Program link failed: {linkLog}", linkLog);
        }
    }

    // ── Activation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates this shader program on the GPU. After calling Use(),
    /// all subsequent draw calls will use this program to render.
    ///
    /// Think of it like selecting a tool before using it: "GPU, use THIS shader now."
    /// </summary>
    public void Use()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.UseProgram(_handle);
    }

    // ── Uniform setters ───────────────────────────────────────────────────────
    // These methods send values from the CPU to the GPU's shader variables.
    // If the uniform was optimised out by the driver (not used in the shader),
    // the call silently does nothing — no harm done.

    /// <summary>Sets a float uniform (e.g. uniform float speed;)</summary>
    public void SetUniform(string name, float value)
    {
        if (_disposed) return;
        int loc = Locate(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    /// <summary>Sets an int uniform (e.g. uniform int layers;)</summary>
    public void SetUniform(string name, int value)
    {
        if (_disposed) return;
        int loc = Locate(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    /// <summary>Sets a bool uniform (OpenGL uses 0 or 1 for false/true)</summary>
    public void SetUniform(string name, bool value)
    {
        if (_disposed) return;
        int loc = Locate(name);
        if (loc >= 0) _gl.Uniform1(loc, value ? 1 : 0);
    }

    /// <summary>Sets a vec2 uniform (two floats, e.g. uniform vec2 offset;)</summary>
    public void SetUniform(string name, Vector2 value)
    {
        if (_disposed) return;
        int loc = Locate(name);
        if (loc >= 0) _gl.Uniform2(loc, value.X, value.Y);
    }

    /// <summary>Sets a vec3 uniform (three floats, e.g. uniform vec3 color;)</summary>
    public void SetUniform(string name, Vector3 value)
    {
        if (_disposed) return;
        int loc = Locate(name);
        if (loc >= 0) _gl.Uniform3(loc, value.X, value.Y, value.Z);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Frees the GPU resources held by this program.
    /// Must be called when the shader is no longer needed (e.g., when switching
    /// to a new AI-generated shader), otherwise we leak GPU memory.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteProgram(_handle);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Compiles one stage (vertex or fragment) of a shader program.
    ///
    /// Sends the source code to the GPU driver and asks it to compile it.
    /// If compilation fails, throws ShaderCompilationException with the
    /// driver's error log (which includes line numbers).
    /// </summary>
    /// <param name="type">VertexShader or FragmentShader</param>
    /// <param name="source">The GLSL source code</param>
    /// <returns>A GPU handle to the compiled shader object</returns>
    private uint CompileStage(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        // Check if compilation succeeded
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new ShaderCompilationException(
                $"[{type}] compilation failed: {log}", log);
        }

        return shader;
    }

    /// <summary>
    /// Gets the GPU location for a uniform variable, using a cache to avoid
    /// repeated calls to glGetUniformLocation (which is relatively slow).
    ///
    /// Returns -1 if the uniform doesn't exist in this program (the driver
    /// may have optimised it away if it's not used).
    /// </summary>
    private int Locate(string name)
    {
        if (!_locationCache.TryGetValue(name, out int loc))
        {
            loc = _gl.GetUniformLocation(_handle, name);
            _locationCache[name] = loc;
        }
        return loc;
    }
}
