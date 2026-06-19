using System.Numerics;

namespace ColairShaderPainter.Models;

// ═══════════════════════════════════════════════════════════════════════════════
// UniformParameter.cs
//
// This file defines:
//   1. UniformType — an enum that lists all the kinds of GLSL uniform variables
//      the AI shader might declare (float, int, bool, vec2, vec3).
//   2. UniformParameter — an immutable record that describes ONE uniform variable
//      detected inside an AI-generated shader. The ViewModel layer reads these
//      records and creates matching UI controls (sliders, color pickers, toggles)
//      so the user can tweak values in real time without touching any code.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// GLSL uniform type categories. Each one maps to a different UI control:
///   - Float / Int → single slider
///   - Bool → toggle switch
///   - Vec2 → two sliders (R, G)
///   - Vec3 → three sliders (R, G, B) like a color picker
/// </summary>
public enum UniformType
{
    Float,   // A single decimal number (e.g. uniform float speed;)
    Int,     // A whole number (e.g. uniform int layers;)
    Bool,    // true/false (e.g. uniform bool enableGlow;)
    Vec2,    // Two numbers grouped together (e.g. uniform vec2 offset;)
    Vec3     // Three numbers — usually an RGB color (e.g. uniform vec3 color;)
}

/// <summary>
/// An immutable record that holds ALL the metadata about ONE uniform variable
/// found in a shader. "Record" means it's a data-only object (like a row in a
/// spreadsheet) — once created, its values never change.
///
/// Think of this as the "blueprint" for a UI control. The ViewModel reads this
/// and builds a slider, toggle, or color picker for each UniformParameter.
///
/// The RUNTIME VALUE (what the user adjusts via sliders) is stored in a separate
/// class called UniformParameterViewModel, not here. This keeps the data model
/// clean and separate from the UI behavior.
/// </summary>
/// <param name="Name">The GLSL variable name, e.g. "mutationSpeed" or "primaryGlow"</param>
/// <param name="Type">What kind of uniform is it? (float, vec3, bool, etc.)</param>
/// <param name="DefaultFloat">Starting value for float/int sliders</param>
/// <param name="Min">Minimum slider value</param>
/// <param name="Max">Maximum slider value</param>
/// <param name="DefaultVec3">Starting color (R,G,B) for vec3 uniforms — defaults to violet/blue glow</param>
/// <param name="IsColorHint">True if the variable name sounds like a color (e.g. "glow", "tint")</param>
public sealed record UniformParameter(
    string Name,
    UniformType Type,
    float DefaultFloat,
    double Min,
    double Max,
    Vector3 DefaultVec3,
    bool IsColorHint
);
