using System.Numerics;
using System.Text.RegularExpressions;
using ColairShaderPainter.Models;

namespace ColairShaderPainter.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// ShaderUniformParser.cs
//
// Parses AI-generated GLSL shader code to detect "uniform" variables.
//
// In GLSL (OpenGL Shading Language), a "uniform" is a variable that the
// CPU can set before the shader runs, allowing real-time control of the
// shader's behaviour. For example:
//   uniform float mutationSpeed;   ← user controls this with a slider
//   uniform vec3  primaryGlow;     ← user controls this with a color picker
//
// This parser scans the shader source code with a regular expression (regex)
// to find every "uniform" declaration. For each one, it creates a
// UniformParameter record describing the variable, which the ViewModel
// then converts into a slider/color-picker/toggle UI control.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Scans GLSL shader source code to find all uniform variable declarations.
///
/// When the AI generates a shader, it may include custom uniforms like:
///   uniform float mutationSpeed;
///   uniform vec3  primaryGlow;
///   uniform bool  enableEffect;
///
/// This parser finds those declarations and creates a UniformParameter for
/// each one. The UI then automatically creates matching controls (sliders,
/// color pickers, toggles) so the user can tweak the shader in real time.
///
/// Built-in uniforms like "iTime" and "iResolution" are skipped — they're
/// handled automatically by the GPU viewport.
/// </summary>
public sealed partial class ShaderUniformParser
{
    /// <summary>
    /// Regular expression that matches GLSL uniform declarations.
    ///
    /// A "regular expression" (regex) is a pattern-matching language for text.
    /// This one finds lines like:
    ///   uniform float speed;
    ///   uniform vec3  neonColor;
    ///   uniform int   layers;
    ///
    /// Pattern breakdown:
    ///   ^\s*uniform\s+           — line starts with "uniform"
    ///   (float|int|bool|vec2|vec3) — capture the type (group 1)
    ///   \s+                      — whitespace between type and name
    ///   ([a-zA-Z_]\w*)           — capture the variable name (group 2)
    ///   \s*;                     — ends with semicolon
    ///
    /// The [GeneratedRegex] attribute (C# 11) makes this a compile-time regex,
    /// which is faster than creating it at runtime.
    /// </summary>
    [GeneratedRegex(
        @"^\s*uniform\s+(float|int|bool|vec2|vec3)\s+([a-zA-Z_]\w*)\s*;",
        RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex UniformPattern();

    /// <summary>
    /// Built-in uniforms that the GPU engine manages automatically.
    /// These should NOT appear as user-controllable UI controls.
    ///
    /// - iTime: Elapsed time in seconds (drives animation)
    /// - iResolution: Viewport size in pixels
    /// </summary>
    private static readonly HashSet<string> BuiltInNames =
        ["iTime", "iResolution"];

    /// <summary>
    /// Scans shader source code and returns metadata about each uniform found.
    ///
    /// For each uniform declaration, this method:
    /// 1. Reads the type (float, vec3, bool, etc.)
    /// 2. Reads the variable name
    /// 3. Skips built-in uniforms (iTime, iResolution)
    /// 4. Checks if the name suggests a color variable
    /// 5. Picks sensible default values and slider ranges based on name
    /// 6. Creates a UniformParameter record with all this info
    /// </summary>
    public IReadOnlyList<UniformParameter> Parse(string shaderSource)
    {
        var results = new List<UniformParameter>();

        // Match every "uniform ... ;" declaration in the source
        foreach (Match m in UniformPattern().Matches(shaderSource))
        {
            string typeName = m.Groups[1].Value;  // e.g. "float", "vec3"
            string varName  = m.Groups[2].Value;  // e.g. "mutationSpeed"

            // Skip built-in uniforms — these are set by the viewport
            if (BuiltInNames.Contains(varName)) continue;

            // Map the GLSL type string to our UniformType enum
            UniformType type = typeName switch
            {
                "float" => UniformType.Float,
                "int"   => UniformType.Int,
                "bool"  => UniformType.Bool,
                "vec2"  => UniformType.Vec2,
                "vec3"  => UniformType.Vec3,
                _       => UniformType.Float  // fallback
            };

            // Check if the name sounds like a color (for default value heuristics)
            bool isColor = ContainsAny(varName,
                ["color", "colour", "glow", "hue", "tint", "rgb", "tone"]);

            // Pick sensible slider defaults based on the variable name
            (float def, float min, float max) = GetFloatDefaults(type, varName);

            // Default color: violet glow for color-ish, gray for others
            Vector3 defVec3 = isColor
                ? new Vector3(0.4f, 0.2f, 0.8f)   // Default violet glow
                : new Vector3(0.5f, 0.5f, 0.5f);   // Default medium gray

            results.Add(new UniformParameter(
                Name:         varName,
                Type:         type,
                DefaultFloat: def,
                Min:          min,
                Max:          max,
                DefaultVec3:  defVec3,
                IsColorHint:  isColor));
        }

        return results;
    }

    // ── Heuristic defaults based on common naming conventions ─────────────────

    /// <summary>
    /// Returns sensible default values and slider ranges based on the variable name.
    ///
    /// For example:
    ///   - "speed" → default 1.0, range 0-5
    ///   - "intensity" → default 1.5, range 0-4
    ///   - "frequency" → default 2.0, range 0-10
    ///
    /// These heuristics make the sliders feel "right" without the user needing
    /// to manually configure ranges for every AI-generated shader.
    /// </summary>
    private static (float def, float min, float max) GetFloatDefaults(
        UniformType type, string name)
    {
        if (type != UniformType.Float) return (0.5f, 0f, 1f);

        if (ContainsAny(name, ["speed", "rate", "velocity"]))  return (1.0f, 0f, 5f);
        if (ContainsAny(name, ["scale", "size", "radius"]))    return (1.0f, 0f, 4f);
        if (ContainsAny(name, ["intensity", "strength"]))      return (1.5f, 0f, 4f);
        if (ContainsAny(name, ["frequency", "freq"]))          return (2.0f, 0f, 10f);
        if (ContainsAny(name, ["power", "exponent"]))          return (2.0f, 0.1f, 8f);
        if (ContainsAny(name, ["mutation", "warp", "twist"]))  return (1.0f, 0f, 3f);
        if (ContainsAny(name, ["bloom", "glow", "halo"]))      return (1.5f, 0f, 4f);
        return (0.5f, 0f, 2f);  // Generic fallback
    }

    /// <summary>
    /// Checks if a string contains any of the given keywords (case-insensitive).
    ///
    /// Used to detect color variables and to classify uniform names for
    /// default value heuristics.
    /// </summary>
    private static bool ContainsAny(string name, IEnumerable<string> keywords) =>
        keywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
}
