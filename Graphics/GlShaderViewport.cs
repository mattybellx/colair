using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using ColairShaderPainter.Exceptions;
using ColairShaderPainter.Models;
using Silk.NET.OpenGL;

namespace ColairShaderPainter.Graphics;

// ═══════════════════════════════════════════════════════════════════════════════
// GlShaderViewport.cs
//
// THE HEART OF THE GPU PIPELINE — this is the OpenGL viewport that renders
// AI-generated shaders in real time inside the Avalonia window.
//
// Key capabilities:
//   - Full-screen quad rendering (the shader draws on a rectangle covering
//     the entire viewport — standard technique for fragment-shader art)
//   - Continuous render loop at monitor refresh rate (up to ~144 Hz)
//   - Frame-rate throttling to ~1 FPS during AI inference (frees VRAM)
//   - GPU cross-fade between old and new shaders (smooth transitions!)
//   - SSAA (Super-Sampling Anti-Aliasing) for crisp, high-quality output
//   - Real-time uniform updates from the UI controls
//   - Zoom in/out capability
//
// Bridging Avalonia + Silk.NET + OpenGL:
// Avalonia's OpenGlControlBase provides a raw OpenGL context inside an
// Avalonia window. Silk.NET is a modern .NET binding for OpenGL. We bridge
// them by passing Avalonia's function-pointer loader (GetProcAddress) to
// Silk.NET's GL.GetApi() factory method.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hardware-accelerated OpenGL viewport for rendering AI-generated GLSL shaders.
///
/// This is a custom Avalonia control that:
/// 1. Creates an OpenGL context inside the window
/// 2. Runs a continuous render loop (every frame ~16ms at 60fps)
/// 3. Compiles and switches between shader programs on the fly
/// 4. Supports smooth cross-fade transitions between shaders
/// 5. Provides SSAA for high-quality anti-aliased rendering
/// 6. Accepts real-time uniform parameter updates from the UI
///
/// The control extends OpenGlControlBase, which is Avalonia's built-in
/// control for hosting raw OpenGL content inside a cross-platform UI.
/// </summary>
public sealed class GlShaderViewport : OpenGlControlBase
{
    // ══════════════════════════════════════════════════════════════════════════
    // PRIVATE FIELDS
    // ══════════════════════════════════════════════════════════════════════════

    // ── Silk.NET API handle ───────────────────────────────────────────────────
    /// <summary>The Silk.NET OpenGL API wrapper — all GL calls go through this.</summary>
    private GL? _gl;

    // ── Fullscreen quad geometry ──────────────────────────────────────────────
    // We render a single rectangle (two triangles) that fills the entire viewport.
    // The fragment shader then draws on every pixel of that rectangle.
    // VAO = Vertex Array Object (describes how vertex data is laid out)
    // VBO = Vertex Buffer Object (the actual vertex data in GPU memory)
    private uint _vao, _vbo;

    // ── Shader programs ───────────────────────────────────────────────────────
    private ShaderProgram? _currentProgram;   // The shader currently being displayed
    private ShaderProgram? _nextProgram;      // A newly compiled shader waiting to cross-fade in
    private ShaderProgram? _compositeProgram; // Helper shader for alpha-blending two textures
    private ShaderProgram? _downsampleProgram; // Helper shader for SSAA downsampling

    // ── FBO (Framebuffer Object) handles for cross-fading ─────────────────────
    // FBO A holds the old shader's output, FBO B holds the new shader's output.
    // The composite shader blends them together with a smooth alpha transition.
    private uint _fboA, _texA;   // FBO A + its texture attachment
    private uint _fboB, _texB;   // FBO B + its texture attachment
    private float  _fadeAlpha;   // 0.0 = old shader only, 1.0 = new shader only
    private bool   _isFading;    // True while cross-fade is active
    private DateTime _fadeStart; // When the fade started (for timing)
    private const float FadeSecs = 0.55f; // Duration of the cross-fade in seconds

    // ── SSAA fields ──────────────────────────────────────────────────────────
    private float  _ssaaFactor = 1.0f;       // 1.0 = native, 2.0 = 2x supersampling
    private uint   _renderFbo, _renderTex;   // SSAA render target (higher resolution)
    
    // ── Frame-rate throttle ───────────────────────────────────────────────────
    // When generating, we throttle to ~1 FPS to free GPU memory for local AI models.
    private bool  _throttled;
    private Timer? _throttleTimer;

    // ── Pending shader compilation ────────────────────────────────────────────
    // Only one shader can be pending compilation at a time. If a new request
    // comes in, it replaces the previous pending one ("latest wins").
    private record PendingShader(string Source, TaskCompletionSource<ShaderCompilationResult> Tcs);
    private volatile PendingShader? _pendingShader;

    // ── Uniform state ─────────────────────────────────────────────────────────
    // Written from the UI thread (when user moves a slider), read on the GL
    // thread (during rendering). Lock (_uniformLock) ensures thread safety.
    private readonly Dictionary<string, float>   _floatUniforms = new(16);
    private readonly Dictionary<string, Vector3> _vec3Uniforms  = new(8);
    private readonly Dictionary<string, Vector2> _vec2Uniforms  = new(4);
    private readonly Lock _uniformLock = new();

    // ── Zoom ─────────────────────────────────────────────────────────────────
    private float _zoom = 1.0f;

    // ── Framerate cap (60 FPS target to avoid tearing / flickering) ──────────
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / 60.0);
    private long _lastFrameTimestamp;

    // ── Render scaling cache (avoids constant FBO recreation from float drift) ──
    private double _cachedRenderScaling = 1.0;

    // ── Misc ──────────────────────────────────────────────────────────────────
    private PixelSize _lastSize;       // Last known viewport size (for resize detection)
    private bool      _initialized;    // True once OpenGL is set up
    private readonly DateTime _appStart = DateTime.UtcNow; // App start time (for iTime)

    // ══════════════════════════════════════════════════════════════════════════
    // EMBEDDED GLSL SHADER SOURCE CODE
    //
    // These are small helper shaders compiled alongside the AI-generated ones.
    // They handle the "plumbing" of the GPU pipeline:
    //
    // 1. VsSrc (Vertex Shader):
    //    The simplest possible vertex shader. It just passes vertex positions
    //    through unchanged. Since we're rendering a full-screen quad, the
    //    positions are already in the right coordinate system (-1 to +1).
    //
    // 2. CompositeFsSrc (Fragment Shader for cross-fading):
    //    Blends two textures (old shader output, new shader output) using
    //    a lerp (linear interpolation) controlled by uAlpha (0→1).
    //    This creates the smooth cross-fade when switching shaders.
    //
    // 3. DownsampleFsSrc (Fragment Shader for SSAA):
    //    Averages 4 pixels into 1 (box filter) to downsample the high-res
    //    SSAA render target down to screen resolution, giving smooth edges.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Vertex shader: the simplest possible pass-through shader.
    /// It takes 2D positions (aPos) and passes them to gl_Position unchanged.
    /// Since we're drawing a full-screen quad covering NDC (-1 to +1),
    /// no transformation is needed.
    ///
    /// GLSL breakdown:
    ///   #version 330 core     — GLSL version (OpenGL 3.3)
    ///   layout(location=0)    — this input comes from VBO attribute index 0
    ///   in vec2 aPos;          — input: 2D position from our vertex buffer
    ///   gl_Position            — built-in: where the vertex ends up on screen
    /// </summary>
    private const string VsSrc = """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        void main() { gl_Position = vec4(aPos, 0.0, 1.0); }
        """;

    /// <summary>
    /// Composite fragment shader: blends two textures (old → new) using alpha.
    ///
    /// Used during cross-fade transitions between shaders.
    /// The mix() function does: result = A * (1-alpha) + B * alpha
    ///
    /// Uniforms:
    ///   uTexA   — The old shader's output (frozen at start of fade)
    ///   uTexB   — The new shader's output (incoming)
    ///   uAlpha  — Blend factor: 0 = old only, 1 = new only
    /// </summary>
    private const string CompositeFsSrc = """
        #version 330 core
        out vec4 fragColor;
        uniform sampler2D uTexA;
        uniform sampler2D uTexB;
        uniform float     uAlpha;
        void main()
        {
            vec2 uv = gl_FragCoord.xy / vec2(textureSize(uTexA, 0));
            fragColor = mix(texture(uTexA, uv), texture(uTexB, uv), uAlpha);
        }
        """;

    /// <summary>
    /// Downsample fragment shader: 2x2 box filter for SSAA resolution reduction.
    ///
    /// When SSAA is enabled, the shader renders at a higher resolution (e.g.,
    /// 2x width, 2x height = 4x the pixels). This shader then averages each
    /// 2x2 block of pixels into a single pixel at the target resolution.
    ///
    /// This gives smooth, anti-aliased edges at the cost of rendering 4x pixels.
    ///
    /// Uniforms:
    ///   uTex        — The high-res SSAA render target
    ///   uTexelSize  — Size of one texel: 1/width, 1/height
    /// </summary>
    private const string DownsampleFsSrc = """
        #version 330 core
        out vec4 fragColor;
        uniform sampler2D uTex;
        uniform vec2      uTexelSize;
        void main()
        {
            vec2 uv = gl_FragCoord.xy * uTexelSize;
            vec4 c;
            c  = 0.25 * texture(uTex, uv);
            c += 0.25 * texture(uTex, uv + vec2(uTexelSize.x, 0.0));
            c += 0.25 * texture(uTex, uv + vec2(0.0,        uTexelSize.y));
            c += 0.25 * texture(uTex, uv + uTexelSize);
            fragColor = c;
        }
        """;

    // ══════════════════════════════════════════════════════════════════════════
    // ULTRA DEFAULT "WAKE" SHADER
    //
    // This is the default shader that shows when the app starts — before the
    // user types a prompt and generates their own. It's a "screensaver" level
    // procedural art piece that demonstrates what the GPU engine can do:
    //
    //   - 3D raymarching with Signed Distance Fields (SDFs)
    //   - Dual gyroid surfaces (triply periodic minimal surfaces — think
    //     mathematically beautiful organic shapes)
    //   - fBm noise (Fractal Brownian Motion) for organic texture
    //   - Domain warping — noise distorting the space itself
    //   - Volumetric lighting with soft shadows
    //   - Ambient Occlusion (AO) for depth
    //   - ACES filmic tone mapping (cinematic color grading)
    //   - Subtle chromatic aberration (color fringing like a lens)
    //   - Vignette (darker corners like a real camera)
    //   - Gamma correction (2.2 standard)
    //   - Nebula background
    //
    // This shader is intentionally spectacular to make a great first impression
    // when the user launches the app for the first time.
    //
    // Uniforms that the user can control via the UI panel:
    //   mutationSpeed   — Speed of animation
    //   primaryGlow     — Main color (vec3 = RGB)
    //   secondaryGlow   — Secondary/rim color (vec3)
    //   bloomIntensity  — Glow brightness
    //   noiseScale      — Texture scale
    // ══════════════════════════════════════════════════════════════════════════
    internal const string DefaultFragSrc = """
        #version 330 core
        out vec4 fragColor;

        uniform float iTime;
        uniform vec2  iResolution;
        uniform float uZoom;
        uniform float mutationSpeed;
        uniform vec3  primaryGlow;
        uniform vec3  secondaryGlow;
        uniform float bloomIntensity;
        uniform float noiseScale;

        #define PI  3.14159265359
        #define TAU 6.28318530718

        // ── Hash / noise ─────────────────────────────────────────────────────
        float hash21(vec2 p) {
            p = fract(p * vec2(234.34, 435.345));
            p += dot(p, p + 19.19);
            return fract(p.x * p.y);
        }

        float vnoise(vec3 x) {
            vec3 i = floor(x), f = fract(x);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = hash21(i.xy + i.z * 7.3);
            float n100 = hash21(i.xy + vec2(1,0) + i.z * 7.3);
            float n010 = hash21(i.xy + vec2(0,1) + i.z * 7.3);
            float n110 = hash21(i.xy + vec2(1,1) + i.z * 7.3);
            float n001 = hash21(i.xy + (i.z+1.0) * 7.3);
            float n101 = hash21(i.xy + vec2(1,0) + (i.z+1.0) * 7.3);
            float n011 = hash21(i.xy + vec2(0,1) + (i.z+1.0) * 7.3);
            float n111 = hash21(i.xy + vec2(1,1) + (i.z+1.0) * 7.3);
            return mix(mix(mix(n000,n100,f.x),mix(n010,n110,f.x),f.y),
                       mix(mix(n001,n101,f.x),mix(n011,n111,f.x),f.y), f.z);
        }

        float fbm(vec3 p) {
            float v = 0.0, a = 0.5;
            mat3 R = mat3(0.8,-0.6,0.0, 0.6,0.8,0.0, 0.0,0.0,1.0);
            for (int i = 0; i < 4; i++) {
                v += a * vnoise(p * noiseScale);
                p  = R * p * 2.1;
                a *= 0.48;
            }
            return v;
        }

        // ── Gyroid SDF ───────────────────────────────────────────────────────
        float gyroid(vec3 p, float s) {
            p *= s;
            return (abs(dot(sin(p), cos(p.zxy))) - 0.05) / s;
        }

        float smin(float a, float b, float k) {
            float h = clamp(0.5 + 0.5*(b-a)/k, 0.0, 1.0);
            return mix(b, a, h) - k*h*(1.0-h);
        }

        float smax(float a, float b, float k) { return -smin(-a, -b, k); }

        // ── Scene SDF: double-warped gyroids ─────────────────────────────────
        float map(vec3 p) {
            float t  = iTime * mutationSpeed * 0.25;
            // Double domain warp
            vec3 w1  = vec3(fbm(p + t), fbm(p.yzx + t*0.7), fbm(p.zxy + t*0.5));
            vec3 q   = p + 0.35 * w1;
            vec3 w2  = vec3(fbm(q + t*0.3), fbm(q.yzx + t*0.2), fbm(q.zxy + t*0.25));
            vec3 r   = q + 0.2 * w2;

            float g1 = gyroid(r, 2.8 + sin(t*0.18)*0.5);
            float g2 = gyroid(r * 1.35, 4.6);
            float sp = length(p) - 1.8;
            float g  = smin(g1, g2, 0.15);
            return smax(sp, g, 0.12);
        }

        // ── Normal ────────────────────────────────────────────────────────────
        vec3 calcNormal(vec3 p) {
            vec2 e = vec2(0.002, 0.0);
            return normalize(vec3(
                map(p + e.xyy) - map(p - e.xyy),
                map(p + e.yxy) - map(p - e.yxy),
                map(p + e.yyx) - map(p - e.yyx)));
        }

        // ── Soft shadow (8-step) ────────────────────────────────────────────
        float softShadow(vec3 ro, vec3 rd, float tMin, float tMax) {
            float res = 1.0;
            for (int i = 0; i < 8; i++) {
                float d = map(ro + rd * tMin);
                if (d < 0.001) return 0.35;
                res = min(res, 8.0 * d / max(tMin, 0.01));
                tMin += d;
                if (tMin > tMax) break;
            }
            return res;
        }

        // ── AO (3-tap) ────────────────────────────────────────────────────────
        float calcAO(vec3 p, vec3 n) {
            float ao = 0.0, scale = 1.0;
            for (int i = 0; i < 3; i++) {
                float d = 0.03 + 0.08 * float(i);
                ao += max(0.0, (d - map(p + n * d)) / (d * scale));
                scale *= 1.8;
            }
            return clamp(1.0 - ao * 0.12, 0.0, 1.0);
        }

        // ── Raymarcher ───────────────────────────────────────────────────────
        vec4 march(vec3 ro, vec3 rd) {
            float t = 0.0;
            for (int i = 0; i < 50; i++) {
                vec3  p = ro + rd * t;
                float d = map(p);
                if (d < 0.0006) {
                    vec3  n   = calcNormal(p);
                    float ao  = calcAO(p, n);
                    float rim = pow(1.0 - clamp(dot(n, -rd), 0.0, 1.0), 3.0);
                    float sh  = softShadow(p + n*0.03, -rd, 0.03, 4.0);
                    float t2  = iTime * mutationSpeed;

                    // Multi-light
                    vec3  l1  = normalize(vec3(1.0, 2.0, 1.5));
                    vec3  l2  = normalize(vec3(-1.5, -1.0, 1.0));
                    float dif1 = max(dot(n, l1), 0.0) * sh;
                    float dif2 = max(dot(n, l2), 0.0) * 0.3;
                    float spc  = pow(max(dot(reflect(-l1, n), -rd), 0.0), 16.0);

                    // Smooth colored lighting — deep rich tones, never white
                    vec3 gc = primaryGlow * 0.8
                            + vec3(sin(t2*0.6 + p.x)*0.10,
                                   cos(t2*0.45 + p.y)*0.08,
                                   sin(t2*0.3 + p.z)*0.10);
                    vec3 sc = secondaryGlow * 0.8
                            + vec3(cos(t2*0.3)*0.06, sin(t2*0.4)*0.06, cos(t2*0.5)*0.06);

                    vec3 col  = gc * (0.15 + 0.6 * dif1 + 0.25 * dif2) * ao;
                    col += sc * rim * bloomIntensity * 0.2;
                    col += vec3(0.90, 0.55, 0.30) * spc * 0.08;
                    col += vec3(0.03, 0.01, 0.08) * (1.0 - ao) * 0.3;
                    col = clamp(col, 0.0, 0.80);

                    return vec4(col, 1.0);
                }
                t += d;
                if (t > 10.0) break;
            }
            // Nebula background
            float bg = fbm(rd * 2.3 + iTime * 0.025);
            vec3 neb = primaryGlow * bg * 0.12 + secondaryGlow * bg * 0.06;
            return vec4(neb + vec3(0.012, 0.006, 0.03), 1.0);
        }

        void main()
        {
            vec2 uv = (gl_FragCoord.xy - 0.5 * iResolution.xy) / iResolution.y;

            float t  = iTime * mutationSpeed * 0.12;
            vec3  ro = vec3(cos(t) * 4.2, sin(t * 0.6) * 1.5, sin(t) * 4.2);
            vec3  fwd = normalize(vec3(0.0) - ro);
            vec3  right = normalize(cross(vec3(0,1,0), fwd));
            vec3  up   = cross(fwd, right);
            vec3  rd   = normalize(fwd + (uv.x * right + uv.y * up) / uZoom);

            vec4 col = march(ro, rd);

            // ── Subtle chromatic aberration ─────────────────────────────────
            col.r *= 1.0 + 0.008 * sin(iTime * 1.3);
            col.b *= 1.0 + 0.008 * cos(iTime * 1.1);

            // ── Vignette ─────────────────────────────────────────────────────
            vec2 vigUv = gl_FragCoord.xy / iResolution.xy;
            float vig = 1.0 - 0.35 * dot(vigUv - 0.5, vigUv - 0.5);
            col.rgb *= vig;

            // ── ACES filmic tone mapping ─────────────────────────────────────
            vec3 aces = col.rgb * (2.51 * col.rgb + 0.03);
            aces = aces / (aces * (1.08 * col.rgb + 0.43) + 0.59);
            col.rgb = clamp(aces, 0.0, 1.0);

            // ── Gamma ────────────────────────────────────────────────────────
            col.rgb = pow(col.rgb, vec3(1.0 / 2.2));

            fragColor = col;
        }
        """;

    // ══════════════════════════════════════════════════════════════════════════
    // PUBLIC API
    //
    // These methods are called from the ViewModel and Views layer to control
    // the viewport from outside. They're designed to be thread-safe so the
    // UI thread can call them without worrying about the GL thread.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fires on the UI thread whenever a shader successfully compiles.
    /// The argument is the raw GLSL source code.
    ///
    /// The ViewModel subscribes to this event and uses it to:
    /// 1. Parse the shader for uniform declarations
    /// 2. Populate the parameters panel with matching UI controls
    /// </summary>
    public event Action<string>? ShaderCompiled;

    /// <summary>
    /// Sets the SSAA (Super-Sampling Anti-Aliasing) factor.
    /// Thread-safe — can be called from any thread.
    ///
    /// How it works:
    ///   - 1.0 = Render at native resolution (fast, no anti-aliasing)
    ///   - 2.0 = Render at 2x width and height (4x pixels), then downsample
    ///
    /// Higher values give smoother edges but use much more GPU power
    /// (4x the pixels for 2x SSAA, 9x for 3x, etc.).
    ///
    /// Setting _lastSize to zero forces the FBO to be recreated on the
    /// next frame at the new scale.
    /// </summary>
    public void SetSsaaFactor(float factor)
    {
        _ssaaFactor = Math.Clamp(factor, 1.0f, 4.0f);
        _lastSize = new PixelSize(0, 0);  // Force FBO recreation
    }

    /// <summary>
    /// Sets the zoom factor for the shader view.
    /// 1.0 = normal view, 2.0 = 2x zoomed in.
    /// The zoom value is passed to the shader as the 'uZoom' uniform.
    ///
    /// Min 0.5 prevents the shader's raymarcher from producing NaN ray
    /// directions at wide angles (which manifests as black flickering
    /// pixels at the viewport edges when very zoomed out).
    /// Thread-safe.
    /// </summary>
    public void SetZoom(float zoom)
    {
        _zoom = Math.Clamp(zoom, 0.5f, 10.0f);
    }

    /// <summary>
    /// Controls render-loop throttling.
    ///
    /// When the AI is generating a shader (especially if using a local model
    /// that shares GPU memory), we throttle the render loop to ~1 FPS to
    /// free up GPU resources (VRAM) for the AI model.
    ///
    /// When throttling is turned off, the full framerate render loop resumes.
    /// </summary>
    public bool IsThrottled
    {
        get => _throttled;
        set
        {
            _throttled = value;
            _throttleTimer?.Dispose();
            _throttleTimer = null;

            if (value)
            {
                // ~1 Hz keep-alive timer — renders one frame per second
                // so the viewport doesn't go completely black
                _throttleTimer = new Timer(OnThrottleTick, null,
                    TimeSpan.Zero, TimeSpan.FromSeconds(1));
            }
            else
            {
                // Resume full framerate immediately
                SafePost(RequestNextFrameRendering);
            }
        }
    }

    /// <summary>
    /// Timer callback for the 1 Hz throttle tick.
    /// Uses a captured reference to detect stale callbacks after disposal.
    /// </summary>
    private void OnThrottleTick(object? state)
    {
        try
        {
            if (_throttled && _initialized)
                Avalonia.Threading.Dispatcher.UIThread.Post(RequestNextFrameRendering);
        }
        catch
        {
            // App shutting down — ignore
        }
    }

    /// <summary>
    /// Safely dispatches an action to the UI thread, swallowing any errors.
    /// Used when the app might be shutting down (the dispatcher might be gone).
    /// </summary>
    private static void SafePost(Action action)
    {
        try { Avalonia.Threading.Dispatcher.UIThread.Post(action); }
        catch { /* App shutting down or dispatcher unavailable — ignore */ }
    }

    /// <summary>
    /// Converts a DIP (device-independent pixel) value to a physical pixel value
    /// by multiplying with the visual root's RenderScaling.
    /// Caps at 1 minimum to avoid zero-sized viewports.
    ///
    /// Caches RenderScaling to avoid constant FBO recreation from float drift.
    /// RenderScaling typically stays stable within a session (changes only on
    /// DPI changes / monitor moves).
    /// </summary>
    private int Px(double dip)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        // Smooth the scaling value — if it changed by less than 1%, keep the cached value.
        // This prevents 1-pixel FBO size fluctuations that cause constant FBO recreation
        // (which manifests as flickering).
        if (Math.Abs(scaling - _cachedRenderScaling) > 0.01)
            _cachedRenderScaling = scaling;
        return Math.Max(1, (int)(dip * _cachedRenderScaling));
    }

    /// <summary>
    /// Schedules a new GLSL fragment shader for compilation on the GPU.
    ///
    /// This is thread-safe — can be called from any thread. The actual
    /// compilation happens on the GL rendering thread (on the next frame).
    ///
    /// The returned Task completes when the GPU has finished compiling.
    /// If a previous shader is still pending, it gets replaced (latest wins),
    /// and the PREVIOUS caller's task is immediately cancelled with no result.
    /// </summary>
    /// <param name="fragmentSource">The GLSL fragment shader source code</param>
    /// <returns>A task that resolves to ShaderCompilationResult (success or failure)</returns>
    public Task<ShaderCompilationResult> CompileShaderAsync(string fragmentSource)
    {
        // TaskCompletionSource creates a "promise" — an object that will later
        // hold the result of the asynchronous operation.
        var tcs = new TaskCompletionSource<ShaderCompilationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // If a previous shader is still pending, complete its TCS with a failure
        // so the previous caller doesn't wait forever.
        Interlocked.Exchange(ref _pendingShader, new PendingShader(fragmentSource, tcs))
            ?.Tcs.TrySetResult(ShaderCompilationResult.Fail("Superseded by newer request", ""));

        // Ask Avalonia to render the next frame (which will process the pending shader)
        Avalonia.Threading.Dispatcher.UIThread.Post(RequestNextFrameRendering);
        return tcs.Task;
    }

    /// <summary>
    /// Updates a float uniform value. Thread-safe — uses a lock.
    /// The next frame render will pick up the new value.
    /// </summary>
    public void SetUniform(string name, float value)
    {
        lock (_uniformLock) _floatUniforms[name] = value;
    }

    /// <summary>
    /// Updates a vec3 uniform (RGB color). Thread-safe.
    /// </summary>
    public void SetUniform(string name, Vector3 value)
    {
        lock (_uniformLock) _vec3Uniforms[name] = value;
    }

    /// <summary>
    /// Updates a vec2 uniform (2D coordinate). Thread-safe.
    /// </summary>
    public void SetUniform(string name, Vector2 value)
    {
        lock (_uniformLock) _vec2Uniforms[name] = value;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // OpenGlControlBase LIFECYCLE
    //
    // These methods are called by Avalonia's OpenGlControlBase at specific
    // points in the control's lifetime:
    //
    //   OnOpenGlInit   — Called ONCE when the OpenGL context is created
    //   OnOpenGlRender — Called EVERY FRAME to draw the current shader
    //   OnOpenGlDeinit — Called ONCE when the OpenGL context is destroyed
    //
    // This is the bridge between Avalonia's UI framework and raw OpenGL.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called once when the OpenGL context is first created.
    /// This is where we set up all GPU resources.
    ///
    /// Wrapped in try-catch because if GL init fails, we don't want to crash
    /// the whole app — we log the error and let the user see a blank viewport.
    /// </summary>
    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            InitGl(gl);
        }
        catch (Exception ex)
        {
            var glLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "colair_gl.txt");
            try { System.IO.File.AppendAllText(glLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} OnOpenGlInit CRASHED: {ex}\n"); } catch { }
        }
    }

    /// <summary>
    /// Initialises OpenGL state and creates all GPU resources.
    ///
    /// Step by step:
    /// 1. Bridge Avalonia's GL proc-address loader → Silk.NET GL API
    /// 2. Create the full-screen quad geometry (VAO/VBO)
    /// 3. Compile the helper shaders (composite, downsample)
    /// 4. Compile the default "WAKE" shader
    /// 5. Set up framebuffer objects (FBOs) for cross-fading
    /// 6. Set up SSAA render target
    /// 7. Start the render loop
    /// </summary>
    private void InitGl(GlInterface gl)
    {
        // ── Diagnostic log ───────────────────────────────────────────────
        // Writes to a debug log file. Useful for troubleshooting GL issues
        // without cluttering the app UI.
        // Write to system temp folder instead of hardcoded C:\Temp (may not exist)
        var glLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "colair_gl.txt");
        try { System.IO.File.AppendAllText(glLogPath,
            $"{DateTime.Now:HH:mm:ss.fff} OnOpenGlInit called. GL Version: {gl.ContextInfo?.Version}\n"); }
        catch { }

        // Bridge Avalonia's GlInterface proc-address loader → Silk.NET GL API.
        // GL.GetApi(Func<string,IntPtr>) is the simplest cross-context factory.
        _gl = GL.GetApi(gl.GetProcAddress);

        SetupQuad();
        _compositeProgram = CompileProgram(VsSrc, CompositeFsSrc);
        _downsampleProgram = CompileProgram(VsSrc, DownsampleFsSrc);

        // Compile the default wake shader and start rendering immediately
        var defaultResult = CompileSafe(DefaultFragSrc);
        if (defaultResult.Success && defaultResult.Program is not null)
        {
            _currentProgram = defaultResult.Program;

            lock (_uniformLock)
            {
                _floatUniforms["mutationSpeed"]  = 1.0f;
                _floatUniforms["noiseScale"]      = 1.0f;
                _floatUniforms["bloomIntensity"]  = 1.5f;
                _vec3Uniforms["primaryGlow"]      = new Vector3(0.4f, 0.2f, 0.8f);
                _vec3Uniforms["secondaryGlow"]    = new Vector3(0.1f, 0.5f, 0.7f);
            }

            // Notify the ViewModel so it can populate the uniform panel
            SafePost(() => ShaderCompiled?.Invoke(DefaultFragSrc));
        }

        SetupFbos(new PixelSize(1280, 720));
        SetupRenderFbo(new PixelSize(1280, 720));
        _initialized = true;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Called EVERY FRAME to render the current shader.
    /// This is the hot path — performance here is critical.
    ///
    /// The 'fb' parameter is the OpenGL framebuffer handle that Avalonia
    /// provides for us to render into. We draw our shader into this buffer,
    /// and Avalonia composites it into the window.
    ///
    /// Wrapped in try-catch so a crash in one frame doesn't kill the app.
    /// The render loop continues despite errors (self-healing at the GL level).
    /// </summary>
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        try
        {
            RenderFrame(fb);
        }
        catch (Exception ex)
        {
            var glLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "colair_gl.txt");
            try { System.IO.File.AppendAllText(glLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} Render CRASHED: {ex.Message}\n"); } catch { }
            // Keep the render loop alive despite errors
            if (!_throttled)
                Avalonia.Threading.Dispatcher.UIThread.Post(RequestNextFrameRendering);
        }
    }

    /// <summary>
    /// The main render function — called every frame.
    ///
    /// This function is the heart of the GPU pipeline. Each frame:
    ///
    /// 1. Check viewport size — recreate FBOs if window was resized
    /// 2. Process any pending shader compilation request
    /// 3. Decide: SSAA render or normal render?
    ///    - SSAA enabled: render to high-res FBO, then downsample
    ///    - Cross-fade active: render old + new to separate FBOs, blend
    ///    - Normal: render current shader directly to screen
    /// 4. Request the next frame (unless throttled)
    /// </summary>
    private void RenderFrame(int fb)
    {
        if (_gl is null || !_initialized) return;

        // ═══════════════════════════════════════════════════════════════════
        // RESET OpenGL state that Avalonia may have changed
        // ═══════════════════════════════════════════════════════════════════
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);

        // ═══════════════════════════════════════════════════════════════════
        // Query the actual framebuffer size from OpenGL.
        //
        // This is more reliable than computing from Bounds because:
        //   - On high-DPI, Bounds is in DIPs but the FBO is in physical pixels
        //   - Computing physical = Bounds * RenderScaling can be off by 1 pixel
        //     due to floating-point rounding, causing viewport > FBO = memory
        //     corruption (flickering black mesh at edges).
        //
        // For FBOs (fb != 0), we query the color attachment 0 texture size.
        // For the default framebuffer (fb = 0), we fall back to Bounds.
        // ═══════════════════════════════════════════════════════════════════
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);

        int fboW, fboH;
        if (fb != 0)
        {
            // Get the type of the color attachment (texture vs renderbuffer)
            _gl.GetFramebufferAttachmentParameter(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                FramebufferAttachmentParameterName.ObjectType,
                out int attachType);

            _gl.GetFramebufferAttachmentParameter(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                FramebufferAttachmentParameterName.ObjectName,
                out int attachName);

            // GL_TEXTURE = 0x8CD0, GL_RENDERBUFFER = 0x8D41
            if (attachType == 0x8CD0 && // GL_TEXTURE
                attachName != 0 && _gl.IsTexture((uint)attachName))
            {
                _gl.BindTexture(TextureTarget.Texture2D, (uint)attachName);
                _gl.GetTexLevelParameter(TextureTarget.Texture2D, 0,
                    GetTextureParameter.TextureWidth, out fboW);
                _gl.GetTexLevelParameter(TextureTarget.Texture2D, 0,
                    GetTextureParameter.TextureHeight, out fboH);
                _gl.BindTexture(TextureTarget.Texture2D, 0);
            }
            else if (attachType == 0x8D41 && // GL_RENDERBUFFER
                     attachName != 0)
            {
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (uint)attachName);
                _gl.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer,
                    RenderbufferParameterName.Width, out fboW);
                _gl.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer,
                    RenderbufferParameterName.Height, out fboH);
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            }
            else
            {
                // Fallback: multiply Bounds by RenderScaling to get physical pixels
                fboW = Px(Bounds.Width);
                fboH = Px(Bounds.Height);
            }
        }
        else
        {
            // Default framebuffer — multiply Bounds by RenderScaling
            fboW = Px(Bounds.Width);
            fboH = Px(Bounds.Height);
        }

        var size = new PixelSize(fboW, fboH);

        // Clear color = deep violet. This helps distinguish:
        //   "GL context active, shader not yet ready" (deep violet)
        //   from "GL context not active" (pure black)
        _gl.ClearColor(0.06f, 0.02f, 0.14f, 1.0f);

        if (size != _lastSize)
        {
            SetupFbos(size);
            SetupRenderFbo(size);
            _lastSize = size;
        }

        // ── Process any pending shader compilation request ────────────────────
        if (_pendingShader is { } pending)
        {
            _pendingShader = null;
            var result = CompileSafe(pending.Source);

            if (result.Success && result.Program is not null)
            {
                _nextProgram = result.Program;
                BeginFade();
                string src = pending.Source;
                SafePost(() => ShaderCompiled?.Invoke(src));
            }

            pending.Tcs.SetResult(result);
        }

        float time = (float)(DateTime.UtcNow - _appStart).TotalSeconds;

        bool useSsaa = _ssaaFactor > 1.0f && _renderFbo != 0;

        // ── SSAA path: render to high-res FBO, then downsample ───────────────
        // Now also handles cross-fades correctly by rendering both old and new
        // to the high-res FBO and compositing before the downsample pass.
        if (useSsaa && _currentProgram is not null)
        {
            var ssaaW = (uint)(size.Width * _ssaaFactor);
            var ssaaH = (uint)(size.Height * _ssaaFactor);
            var ssaaSize = new Vector2(ssaaW, ssaaH);

            if (_isFading && _nextProgram is not null)
            {
                // SSAA cross-fade: render old+new at high res, composite into
                // renderFbo, then downsample to screen.
                RenderCrossFadeSsr((uint)fb, time, size, ssaaW, ssaaH, ssaaSize);
            }
            else
            {
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _renderFbo);
                _gl.Viewport(0, 0, ssaaW, ssaaH);
                _gl.Clear(ClearBufferMask.ColorBufferBit);
                Render(_currentProgram, time, ssaaSize);

                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
                _gl.Viewport(0, 0, (uint)size.Width, (uint)size.Height);
                RenderDownsample();
            }
        }
        else
        {
            // ── Non-SSAA path ────────────────────────────────────────────────
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
            _gl.Viewport(0, 0, (uint)size.Width, (uint)size.Height);
            _gl.Clear(ClearBufferMask.ColorBufferBit);

            if (_isFading && _currentProgram is not null && _nextProgram is not null)
            {
                RenderCrossFade(fb, time, size, (uint)size.Width, (uint)size.Height,
                    new Vector2(size.Width, size.Height));
            }
            else if (_currentProgram is not null)
            {
                Render(_currentProgram, time, new Vector2(size.Width, size.Height));
            }
        }

        if (!_throttled)
        {
            // ── Framerate cap: target ~60 FPS ──────────────────────────────
            // Without this cap, the render loop runs as fast as possible,
            // causing tearing (the image updates mid-screen-scanout) and
            // excessive GPU usage. A 60 FPS cap matches typical monitor refresh
            // and gives smooth animation without unnecessary GPU load.
            var now = Stopwatch.GetTimestamp();
            var elapsed = (double)(now - _lastFrameTimestamp) / Stopwatch.Frequency;
            var remaining = FrameInterval.TotalSeconds - elapsed;
            if (remaining > 0)
                Thread.Sleep((int)(remaining * 1000));
            _lastFrameTimestamp = now;

            RequestNextFrameRendering();
        }
    }

    /// <summary>
    /// Called ONCE when the OpenGL context is being destroyed.
    /// This is where we clean up all GPU resources to prevent memory leaks.
    ///
    /// GPU resources (shader programs, framebuffers, textures, vertex buffers)
    /// are NOT automatically garbage collected like normal C# objects — they
    /// live in GPU memory and must be explicitly deleted.
    /// </summary>
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        // Dispose all shader programs
        _throttleTimer?.Dispose();
        _currentProgram?.Dispose();
        _nextProgram?.Dispose();
        _compositeProgram?.Dispose();
        _downsampleProgram?.Dispose();

        if (_gl is not null)
        {
            // Delete GPU resources: VAO, VBO, FBOs, textures
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_fboA != 0) { _gl.DeleteFramebuffer(_fboA); _gl.DeleteTexture(_texA); }
            if (_fboB != 0) { _gl.DeleteFramebuffer(_fboB); _gl.DeleteTexture(_texB); }
            if (_renderFbo != 0) { _gl.DeleteFramebuffer(_renderFbo); _gl.DeleteTexture(_renderTex); }
            _gl.Dispose();
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PRIVATE — GPU SETUP HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates the full-screen quad geometry.
    ///
    /// We render a rectangle made of two triangles that covers the entire
    /// normalized device coordinate (NDC) space from -1 to +1:
    ///
    ///   (-1,+1) ┌──────┐ (+1,+1)
    ///           │  ↙  ↘ │
    ///           │ ↘  ↙ │
    ///   (-1,-1) └──────┘ (+1,-1)
    ///
    /// The 6 vertices are: bottom-left, bottom-right, top-left (triangle 1),
    /// bottom-right, top-right, top-left (triangle 2).
    ///
    /// Each vertex has 2 components (x, y) — a 2D position.
    /// The fragment shader then draws on every pixel of this rectangle.
    ///
    /// Uses unsafe code because Silk.NET's BufferData expects a raw pointer.
    /// </summary>
    private void SetupQuad()
    {
        if (_gl is null) return;

        // Two triangles forming a full-screen quad (6 vertices × 2 coords = 12 floats)
        ReadOnlySpan<float> verts = [ -1f, -1f,  1f, -1f,  -1f, 1f,  1f, -1f,  1f, 1f,  -1f, 1f ];

        _vao = _gl.GenVertexArray();   // Vertex Array Object — describes vertex layout
        _vbo = _gl.GenBuffer();         // Vertex Buffer Object — stores the actual data

        // Bind VAO, then VBO, then upload data
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        unsafe
        {
            fixed (float* ptr = verts)
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                               (nuint)(verts.Length * sizeof(float)),
                               ptr,
                               BufferUsageARB.StaticDraw);  // StaticDraw = data never changes
        }

        // Tell OpenGL: attribute 0 has 2 floats per vertex, no gap between vertices
        unsafe
        {
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false,
                                    (uint)(2 * sizeof(float)), (void*)0);
        }
        _gl.EnableVertexAttribArray(0);
        _gl.BindVertexArray(0);  // Unbind VAO
    }

    /// <summary>
    /// Creates (or recreates) the two FBOs used for cross-fade transitions.
    /// FBO A holds the old shader's last frame, FBO B holds the new shader's frame.
    /// </summary>
    private void SetupFbos(PixelSize size)
    {
        if (_gl is null || size.Width <= 0 || size.Height <= 0) return;

        // Delete previous FBOs before reallocating
        if (_fboA != 0) { _gl.DeleteFramebuffer(_fboA); _gl.DeleteTexture(_texA); }
        if (_fboB != 0) { _gl.DeleteFramebuffer(_fboB); _gl.DeleteTexture(_texB); }

        (_fboA, _texA) = CreateFbo(size);
        (_fboB, _texB) = CreateFbo(size);
    }

    /// <summary>
    /// Creates (or recreates) the SSAA render target FBO.
    /// This FBO is larger than the viewport (size × ssaaFactor) so the shader
    /// renders at higher resolution for super-sampling.
    /// </summary>
    private void SetupRenderFbo(PixelSize screenSize)
    {
        if (_gl is null) return;

        // Delete previous render FBO
        if (_renderFbo != 0) { _gl.DeleteFramebuffer(_renderFbo); _gl.DeleteTexture(_renderTex); }

        var ssaaSize = new PixelSize(
            (int)(screenSize.Width  * _ssaaFactor),
            (int)(screenSize.Height * _ssaaFactor));

        (_renderFbo, _renderTex) = CreateFbo(ssaaSize);
    }

    /// <summary>
    /// Creates a Framebuffer Object (FBO) with an attached color texture.
    ///
    /// An FBO is an "offscreen canvas" — instead of drawing directly to the
    /// screen, we draw to this buffer first, then composite it to the screen.
    /// This is essential for cross-fading and SSAA.
    ///
    /// The texture uses RGBA16f format (16-bit floating point per channel),
    /// which gives high color precision (HDR-like quality).
    /// </summary>
    /// <param name="size">Width and height of the FBO in pixels</param>
    /// <returns>A tuple of (framebuffer handle, texture handle)</returns>
    private (uint fbo, uint tex) CreateFbo(PixelSize size)
    {
        if (_gl is null) return (0, 0);

        // Create and configure the texture
        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f,
                           (uint)size.Width, (uint)size.Height,
                           0, PixelFormat.Rgba, PixelType.Float, null);
        }
        // Linear filtering for smooth scaling
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        // ═══════════════════════════════════════════════════════════════════
        // CRITICAL: Set texture wrapping to CLAMP_TO_EDGE.
        //
        // Default OpenGL wrapping is GL_REPEAT, which causes the downsample
        // and composite shaders to WRAP AROUND when sampling near the edge of
        // the texture (texture coords ≈ 1.0). This manifests as a flickering
        // black "mesh" pattern at the top and right edges of the viewport.
        //
        // CLAMP_TO_EDGE ensures sampling at/above 1.0 clamps to the last
        // valid pixel instead of wrapping to the opposite side.
        // ═══════════════════════════════════════════════════════════════════
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // Create the FBO and attach the texture as color attachment 0
        uint fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                                  FramebufferAttachment.ColorAttachment0,
                                  TextureTarget.Texture2D, tex, 0);

        // Unbind
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return (fbo, tex);
    }

    /// <summary>
    /// Renders a shader program into the currently bound framebuffer.
    ///
    /// Sets the standard uniforms (iTime, iResolution, uZoom) and all
    /// user-controlled uniforms (from the UI panel). Then draws the
    /// full-screen quad (6 vertices = 2 triangles) to trigger the fragment shader.
    /// </summary>
    private void Render(ShaderProgram prog, float time, Vector2 resolution)
    {
        prog.Use();

        // Set standard uniforms that every shader expects
        prog.SetUniform("iTime",       time);          // Elapsed time in seconds
        prog.SetUniform("iResolution", resolution);     // Viewport size in pixels
        prog.SetUniform("uZoom",       _zoom);          // Zoom factor

        // Set user-controlled uniforms from the UI panel (thread-safe via lock)
        lock (_uniformLock)
        {
            foreach (var (k, v) in _floatUniforms) prog.SetUniform(k, v);
            foreach (var (k, v) in _vec3Uniforms)  prog.SetUniform(k, v);
            foreach (var (k, v) in _vec2Uniforms)  prog.SetUniform(k, v);
        }

        // Draw the full-screen quad (6 vertices = 2 triangles)
        _gl!.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// Renders the cross-fade composite: blends FBO A (old) into FBO B (new).
    /// Uses the composite shader with mix() = A × (1-alpha) + B × alpha.
    /// </summary>
    private void RenderComposite(float alpha)
    {
        if (_gl is null || _compositeProgram is null) return;

        _compositeProgram.Use();

        // Bind old shader output to texture unit 0
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _texA);
        _compositeProgram.SetUniform("uTexA", 0);

        // Bind new shader output to texture unit 1
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _texB);
        _compositeProgram.SetUniform("uTexB", 1);

        // Set blend factor
        _compositeProgram.SetUniform("uAlpha", alpha);

        // Draw full-screen quad
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// Renders the SSAA downsample pass.
    /// Takes the high-res render target and downsamples it to screen resolution
    /// using a 2x2 box filter (averages each 2x2 block of pixels into one).
    /// </summary>
    private void RenderDownsample()
    {
        if (_gl is null || _downsampleProgram is null || _renderTex == 0) return;

        _downsampleProgram.Use();

        // Bind the high-res SSAA texture
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _renderTex);
        _downsampleProgram.SetUniform("uTex", 0);

        // Calculate the size of one texel in the high-res buffer
        _downsampleProgram.SetUniform("uTexelSize",
            new Vector2(1.0f / (_lastSize.Width * _ssaaFactor),
                        1.0f / (_lastSize.Height * _ssaaFactor)));

        // Draw full-screen quad
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// Renders a cross-fade transition between the current and next shader.
    /// Non-SSAA path: renders old/new to separate FBOs at native resolution,
    /// then composites directly to the main framebuffer.
    /// </summary>
    private void RenderCrossFade(int fb, float time, PixelSize size,
        uint renderW, uint renderH, Vector2 renderRes)
    {
        float elapsed = (float)(DateTime.UtcNow - _fadeStart).TotalSeconds;
        _fadeAlpha = Math.Clamp(elapsed / FadeSecs, 0f, 1f);

        // Old shader → FBO A
        _gl!.BindFramebuffer(FramebufferTarget.Framebuffer, _fboA);
        _gl.Viewport(0, 0, renderW, renderH);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        Render(_currentProgram!, time, renderRes);

        // New shader → FBO B
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fboB);
        _gl.Viewport(0, 0, renderW, renderH);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        Render(_nextProgram!, time, renderRes);

        // Composite back to main framebuffer
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        _gl.Viewport(0, 0, (uint)size.Width, (uint)size.Height);
        RenderComposite(_fadeAlpha);

        EndFadeIfComplete();
    }

    /// <summary>
    /// SSAA cross-fade: renders old/new at SSAA resolution into separate FBOs,
    /// composites into the SSAA render FBO, then downsamples to screen.
    /// </summary>
    private void RenderCrossFadeSsr(uint fb, float time, PixelSize size,
        uint ssaaW, uint ssaaH, Vector2 ssaaSize)
    {
        float elapsed = (float)(DateTime.UtcNow - _fadeStart).TotalSeconds;
        _fadeAlpha = Math.Clamp(elapsed / FadeSecs, 0f, 1f);

        // Old shader → FBO A at SSAA resolution
        _gl!.BindFramebuffer(FramebufferTarget.Framebuffer, _fboA);
        _gl.Viewport(0, 0, ssaaW, ssaaH);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        Render(_currentProgram!, time, ssaaSize);

        // New shader → FBO B at SSAA resolution
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fboB);
        _gl.Viewport(0, 0, ssaaW, ssaaH);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        Render(_nextProgram!, time, ssaaSize);

        // Composite A+B → SSAA render FBO
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _renderFbo);
        _gl.Viewport(0, 0, ssaaW, ssaaH);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        // Bind texA and texB and render the composite shader into renderFbo
        _compositeProgram!.Use();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _texA);
        _compositeProgram.SetUniform("uTexA", 0);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _texB);
        _compositeProgram.SetUniform("uTexB", 1);
        _compositeProgram.SetUniform("uAlpha", _fadeAlpha);
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);

        // Downsample renderFbo → main framebuffer
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fb);
        _gl.Viewport(0, 0, (uint)size.Width, (uint)size.Height);
        RenderDownsample();

        EndFadeIfComplete();
    }

    /// <summary>
    /// Checks if the cross-fade is complete and cleans up if so.
    /// The old _currentProgram is disposed and _nextProgram becomes the new current.
    /// </summary>
    private void EndFadeIfComplete()
    {
        if (_fadeAlpha >= 1.0f)
        {
            _currentProgram?.Dispose();
            _currentProgram = _nextProgram;
            _nextProgram    = null;
            _isFading       = false;
        }
    }

    /// <summary>Starts a cross-fade transition by resetting fade timing.</summary>
    private void BeginFade()
    {
        _fadeAlpha = 0f;
        _fadeStart = DateTime.UtcNow;
        _isFading  = true;
    }

    /// <summary>
    /// Safely compiles a fragment shader, catching any compilation errors.
    ///
    /// This is the entry point for the AI self-healing loop at the GPU level.
    /// Instead of letting exceptions crash the render loop, we catch them and
    /// return a ShaderCompilationResult that the orchestration layer can inspect.
    /// </summary>
    private ShaderCompilationResult CompileSafe(string fragSrc)
    {
        try
        {
            var prog = CompileProgram(VsSrc, EnsureVersion(fragSrc));
            var glLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "colair_gl.txt");
            try { System.IO.File.AppendAllText(glLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} Shader compiled OK\n"); } catch { }
            return ShaderCompilationResult.Ok(prog);
        }
        catch (ShaderCompilationException ex)
        {
            // Expected: shader had syntax errors — the AI will fix these
            var glLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "colair_gl.txt");
            try { System.IO.File.AppendAllText(glLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} COMPILE FAIL:\n{ex.ErrorLog}\n---\n"); } catch { }
            return ShaderCompilationResult.Fail(ex.Message, ex.ErrorLog);
        }
        catch (Exception ex)
        {
            // Unexpected error — still return a result instead of crashing
            var glLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "colair_gl.txt");
            try { System.IO.File.AppendAllText(glLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} COMPILE EXCEPTION: {ex}\n---\n"); } catch { }
            return ShaderCompilationResult.Fail(ex.Message, ex.ToString());
        }
    }

    /// <summary>Helper: compiles vertex + fragment shader into a ShaderProgram.</summary>
    private ShaderProgram CompileProgram(string vert, string frag)
    {
        if (_gl is null) throw new InvalidOperationException("GL not initialised.");
        return new ShaderProgram(_gl, vert, frag);
    }

    /// <summary>
    /// Pre-processes AI-generated GLSL source to ensure it compiles correctly.
    ///
    /// Two fixes:
    /// 1. Strip non-ASCII characters — AI models sometimes include fancy
    ///    Unicode characters (em-dashes, box-drawing characters) in comments.
    ///    These can corrupt Silk.NET's native string marshalling, causing the
    ///    GLSL string to arrive empty on the GPU.
    /// 2. Add #version directive — if the AI forgot to include a version
    ///    directive, we prepend #version 330 core so the shader still works.
    /// </summary>
    private static string EnsureVersion(string src)
    {
        // Strip non-ASCII characters before any processing — GLSL only uses ASCII
        // and non-ASCII in comment decorators (em-dashes, box-drawing chars) will
        // produce an empty/corrupted string when marshalled to a native char*.
        src = System.Text.RegularExpressions.Regex.Replace(src, @"[^\x00-\x7F]", "");

        string trimmed = src.TrimStart();
        if (trimmed.StartsWith("#version", StringComparison.OrdinalIgnoreCase))
            return src; // Already has a version directive — trust it

        // Inject minimal header without overriding the AI's own declarations
        return "#version 330 core\nout vec4 fragColor;\n\n" + src;
    }


}
