using System.Numerics;
using ColairShaderPainter.Models;

namespace ColairShaderPainter.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════════
// UniformParameterViewModel.cs
//
// ViewModel for a single dynamically-generated UI control for one AI shader uniform.
//
// When the AI generates a shader, it may declare custom uniforms like:
//   uniform float mutationSpeed;
//   uniform vec3  primaryGlow;
//   uniform bool  enableEffect;
//
// Each of these becomes a UniformParameterViewModel, which drives a matching
// UI control in the parameters panel:
//   - float → single slider
//   - vec3  → three sliders (R, G, B) like a color picker
//   - bool  → toggle switch
//   - vec2  → two sliders
//   - int   → single integer slider
//
// This is a perfect example of MVVM: the ViewModel exposes properties that
// the XAML DataTemplate binds to, and when the user adjusts a slider, the
// ViewModel fires a ValueChanged event that the parent ViewModel catches
// and forwards to the GPU viewport in real time.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// ViewModel for one shader uniform control in the parameters panel.
///
/// Properties:
///   - Name, DisplayName: Identity (e.g. "mutationSpeed" → "Mutation Speed")
///   - Type, IsFloat, IsVec3, IsBool, IsVec2, IsInt: Which UI template to use
///   - FloatValue, R, G, B, BoolValue: The actual values the user adjusts
///   - Min, Max: Slider range
///
/// The IsFloat/IsVec3/IsBool flags are called "template selectors" — the XAML
/// binds to them with IsVisible="{Binding IsFloat}" to show/hide the right
/// control type for each uniform.
/// </summary>
public sealed class UniformParameterViewModel : ViewModelBase
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>The GLSL variable name, e.g. "mutationSpeed"</summary>
    public string      Name        { get; }

    /// <summary>What type of GLSL uniform is this?</summary>
    public UniformType Type        { get; }

    /// <summary>Human-readable name, e.g. "Mutation Speed" (converted from camelCase)</summary>
    public string      DisplayName { get; }

    // ── Slider range ──────────────────────────────────────────────────────────

    /// <summary>Minimum value for the slider (from ShaderUniformParser heuristics)</summary>
    public double Min { get; }

    /// <summary>Maximum value for the slider</summary>
    public double Max { get; }

    // ── Template-selector flags (read-only, computed from Type) ────────────────

    /// <summary>True if this uniform is a float → show a single slider</summary>
    public bool IsFloat => Type == UniformType.Float;

    /// <summary>True if this uniform is a vec3 → show R/G/B sliders (color picker)</summary>
    public bool IsVec3  => Type == UniformType.Vec3;

    /// <summary>True if this uniform is a bool → show a toggle switch</summary>
    public bool IsBool  => Type == UniformType.Bool;

    /// <summary>True if this uniform is a vec2 → show two sliders</summary>
    public bool IsVec2  => Type == UniformType.Vec2;

    /// <summary>True if this uniform is an int → show an integer slider</summary>
    public bool IsInt   => Type == UniformType.Int;

    // ── Float value (bound to Slider) ─────────────────────────────────────────
    private float _floatValue;

    /// <summary>The current value of a float/int uniform (set by slider drag)</summary>
    public float FloatValue
    {
        get => _floatValue;
        set
        {
            if (!SetProperty(ref _floatValue, value)) return;
            NotifyValueChanged();  // Tell the GPU to update
        }
    }

    // ── Vec3 stored as three independent R/G/B sliders (0 → 1) ───────────────
    private float _r, _g, _b;

    /// <summary>Red channel (0.0 to 1.0) — clamped to prevent invalid GPU values</summary>
    public float R { get => _r; set { if (SetProperty(ref _r, Math.Clamp(value, 0f, 1f))) NotifyValueChanged(); } }
    /// <summary>Green channel (0.0 to 1.0)</summary>
    public float G { get => _g; set { if (SetProperty(ref _g, Math.Clamp(value, 0f, 1f))) NotifyValueChanged(); } }
    /// <summary>Blue channel (0.0 to 1.0)</summary>
    public float B { get => _b; set { if (SetProperty(ref _b, Math.Clamp(value, 0f, 1f))) NotifyValueChanged(); } }

    /// <summary>Current vec3 value for direct GPU upload (combines R, G, B into a vector)</summary>
    public Vector3 Vec3Value => new(_r, _g, _b);

    // ── Bool / toggle ─────────────────────────────────────────────────────────
    private bool _boolValue;

    /// <summary>Current value of a boolean uniform (true/false toggle)</summary>
    public bool BoolValue
    {
        get => _boolValue;
        set { if (SetProperty(ref _boolValue, value)) NotifyValueChanged(); }
    }

    // ── Change notification ───────────────────────────────────────────────────

    /// <summary>
    /// Fires whenever the user adjusts any value (slider, toggle, color picker).
    /// The parent MainWindowViewModel subscribes to this and pushes the new
    /// value to the GPU viewport in real time.
    /// </summary>
    public event EventHandler? ValueChanged;

    /// <summary>Notifies subscribers that a value changed (triggers GPU update)</summary>
    private void NotifyValueChanged() =>
        ValueChanged?.Invoke(this, EventArgs.Empty);

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a ViewModel from a UniformParameter data record.
    /// The data record comes from ShaderUniformParser.Parse().
    /// </summary>
    public UniformParameterViewModel(UniformParameter param)
    {
        Name        = param.Name;
        Type        = param.Type;
        DisplayName = ToDisplayName(param.Name);  // Convert "mutationSpeed" → "Mutation Speed"
        Min         = param.Min;
        Max         = param.Max;

        // Seed with default values
        _floatValue = param.DefaultFloat;
        _r = param.DefaultVec3.X;
        _g = param.DefaultVec3.Y;
        _b = param.DefaultVec3.Z;
        _boolValue  = true;  // Default: enabled
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts camelCase and snake_case GLSL variable names into user-friendly
    /// Title Case display labels.
    ///
    /// Examples:
    ///   "mutationSpeed"  → "Mutation Speed"
    ///   "noise_scale"    → "Noise Scale"
    ///   "primaryGlow"    → "Primary Glow"
    ///   "bloomIntensity" → "Bloom Intensity"
    ///
    /// This makes the AI-generated variable names readable in the UI without
    /// requiring the AI to provide display names.
    /// </summary>
    private static string ToDisplayName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && (char.IsUpper(c) || c == '_'))
            {
                if (c != '_') sb.Append(' ');  // Space before uppercase letter
                c = char.ToUpper(c);
            }
            else if (i == 0)
            {
                c = char.ToUpper(c);  // Capitalise first letter
            }
            if (c != '_') sb.Append(c);  // Skip underscores
        }
        return sb.ToString();
    }
}
