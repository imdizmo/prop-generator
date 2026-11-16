// Ported from easy-props frontend/src/core/export/godot-toon.ts (MIT, © 2026 AHS12).
// The shader texts are copied from that file as they are; tools that reformat this file must leave them alone.
using PropGenerator.Nature.Js;
using PropGenerator.Nature.Style;

namespace PropGenerator.Nature.Export;

/// <summary>
/// Godot materials for the Cartoon and Comic looks, written from the numbers the view draws them with. Each keeps the
/// wind of the lit material it is made from and replaces how light falls; lines come from a separate full-screen effect.
/// </summary>
public static class GodotToon
{
    private static readonly string CartoonLight = "\n" + $$"""
// The share of the light a surface turned this far toward it gets: flat bands
// joined by short soft steps.
float cartoon_level(float facing) {
	float level = {{N(ToonLooks.CartoonLevels[0])}};
{{CartoonSteps()}}
	return level;
}

void light() {
	DIFFUSE_LIGHT += cartoon_level(dot(NORMAL, LIGHT) * 0.5 + 0.5) * ATTENUATION * LIGHT_COLOR / PI;
}
""" + "\n";

    private static readonly string CartoonWood = "\n" + $$"""
void fragment() {
	ALBEDO = COLOR.rgb * texture(bark_texture, UV).rgb;
	NORMAL_MAP = texture(relief_texture, UV).rgb;
	NORMAL_MAP_DEPTH = relief_strength;
}
""" + "\n";

    private static readonly string CartoonLeaves = "\n" + $$"""
void fragment() {
	vec4 dab = texture(dab_texture, UV);
	ALBEDO = COLOR.rgb * dab.rgb;
	ALPHA = dab.a;
	ALPHA_SCISSOR_THRESHOLD = cut_off;
}
""" + "\n";

    private static readonly string ComicSettings = "\n" + $$"""
uniform float colour_boost : hint_range(1.0, 2.0) = {{N(ToonLooks.ComicVivid)}};
uniform float shadow_darkness : hint_range(0.0, 1.0) = {{N(ToonLooks.ComicShadowDarkness)}};

const vec3 WEIGHTS = vec3(0.2126, 0.7152, 0.0722);

// One colour per face, so a part's flat colours meet at a crisp edge.
varying flat vec3 face_colour;
// The shadow colour as a share of the lit one, handed from fragment() to light().
varying vec3 shade_share;
""" + "\n";

    private static readonly string ComicColours = "\n" + $$"""
// Colours are made richer, and shade deepens them rather than greying them, as a
// painter would. A grey only darkens: richer colour would turn a faint cast blue.
float colourful(vec3 base) {
	float top = max(max(base.r, base.g), base.b);
	float saturation = (top - min(min(base.r, base.g), base.b)) / max(top, 0.0001);
	return smoothstep({{N(ToonLooks.ComicColourful[0])}}, {{N(ToonLooks.ComicColourful[1])}}, saturation);
}

vec3 rich_colour(vec3 base) {
	float luma = dot(base, WEIGHTS);
	return max(mix(vec3(luma), base, mix(1.0, colour_boost, colourful(base))), 0.0);
}

vec3 shadow_colour(vec3 rich) {
	float luma = max(dot(rich, WEIGHTS), 0.0001);
	vec3 grey = rich * pow(luma, {{N(ToonLooks.ComicGreyShadowDepth)}});
	return mix(grey, pow(rich, vec3({{N(ToonLooks.ComicShadowDepth)}})), colourful(rich)) * shadow_darkness;
}

void light() {
	vec3 sun = LIGHT_COLOR / PI;
	float strength = dot(sun, WEIGHTS);
	vec3 tinted = mix(vec3(strength), sun, {{N(ToonLooks.ComicTint)}});
	// One sharp step from shadow to lit, where the surface turns away or a shadow falls.
	float lit = smoothstep(-{{N(ToonLooks.ComicStepSoftness)}}, {{N(ToonLooks.ComicStepSoftness)}}, dot(NORMAL, LIGHT)) * step(0.5, ATTENUATION);
	if (LIGHT_IS_DIRECTIONAL) {
		DIFFUSE_LIGHT += mix(shade_share * strength, tinted, lit);
	} else {
		DIFFUSE_LIGHT += tinted * lit * ATTENUATION;
	}
}
""" + "\n";

    private static readonly string ComicWood = "\n" + $$"""
vec3 two_tone(vec3 print) {
	float top = max(max(print.r, print.g), print.b);
	if (top - min(min(print.r, print.g), print.b) < {{N(ToonLooks.ComicGreyPrint)}}) {
		return vec3(top < {{N(ToonLooks.ComicStrokeLevel)}} ? {{N(ToonLooks.ComicStrokeTone)}} : 1.0);
	}
	return print;
}

void fragment() {
	vec3 rich = rich_colour(face_colour * two_tone(texture(bark_texture, UV).rgb));
	ALBEDO = rich;
	shade_share = shadow_colour(rich) / max(rich, vec3(0.001));
	SPECULAR = 0.0;
}
""" + "\n";

    private static readonly string ComicLeaves = "\n" + $$"""
void fragment() {
	vec3 rich = rich_colour(face_colour);
	ALBEDO = rich;
	ALPHA = texture(dab_texture, UV).a;
	ALPHA_SCISSOR_THRESHOLD = cut_off;
	shade_share = shadow_colour(rich) / max(rich, vec3(0.001));
	SPECULAR = 0.0;
}
""" + "\n";

    private static readonly string Outline = $$"""
// easy-props outline: dark lines around shapes, for the Cartoon and Comic looks.
// Put it on a 2 by 2 QuadMesh that is a child of the camera. Needs the Forward+ renderer.
shader_type spatial;
render_mode unshaded, fog_disabled, depth_draw_never, cull_disabled;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform vec3 ink_colour : source_color = {{LinearColour(ToonLooks.InkColour)}};
// In pixels. Comic is {{N(ToonLooks.InkOuter * ToonLooks.ComicLineWeight)}} and Cartoon {{N(ToonLooks.InkOuter * ToonLooks.CartoonLineWeight)}}.
uniform float line_width : hint_range(0.5, 8.0) = {{N(ToonLooks.InkOuter * ToonLooks.ComicLineWeight)}};
// How dark, from 0 for none to 1 for solid ink. Comic is {{N(ToonLooks.ComicLineStrength)}} and Cartoon {{N(ToonLooks.CartoonLineStrength)}}.
uniform float line_strength : hint_range(0.0, 1.0) = {{N(ToonLooks.ComicLineStrength)}};
// How far behind, as a share of its distance, a surface must be to be outlined.
uniform float depth_gap : hint_range(0.001, 0.5) = 0.1;
// Gaps narrower than this many pixels, such as among leaves, go without a line.
uniform float gap_size : hint_range(0.0, 16.0) = {{N(ToonLooks.InkHoleReach)}};

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float distance_at(vec2 uv, mat4 inverse_projection) {
	// Kept on the screen: past its edge the depth reads as near, which would line the border.
	uv = clamp(uv, vec2(0.001), vec2(0.999));
	float depth = texture(depth_texture, uv).r;
	vec4 view = inverse_projection * vec4(uv * 2.0 - 1.0, depth, 1.0);
	return -view.z / view.w;
}

void fragment() {
	vec2 texel = 1.0 / VIEWPORT_SIZE;
	float here = distance_at(SCREEN_UV, INV_PROJECTION_MATRIX);
	float hits = 0.0;
	for (int i = 0; i < 12; i++) {
		float angle = float(i) * PI / 6.0;
		float other = distance_at(SCREEN_UV + vec2(cos(angle), sin(angle)) * line_width * texel, INV_PROJECTION_MATRIX);
		// Only the farther side takes the line, so it sits beside a shape, not on it.
		if (here - other > depth_gap * other) {
			hits += 1.0;
		}
	}
	if (hits == 0.0) {
		discard;
	}
	// A gap with something nearer all round it is a hole among leaves, not an edge.
	float around = 0.0;
	for (int i = 0; i < 8; i++) {
		float angle = float(i) * PI / 4.0;
		float other = distance_at(SCREEN_UV + vec2(cos(angle), sin(angle)) * gap_size * texel, INV_PROJECTION_MATRIX);
		if (here - other > depth_gap * other) {
			around += 1.0;
		}
	}
	if (around >= 7.0) {
		discard;
	}
	ALBEDO = ink_colour;
	ALPHA = clamp(hits / 6.0, 0.0, 1.0) * line_strength;
}
""" + "\n";

    private static readonly Dictionary<string, string> Notes = new(StringComparer.Ordinal)
    {
        ["cartoonwood"] = "// easy-props wood, Cartoon look: light falls in soft bands. Wind as in wood.gdshader.",
        ["cartoonleaves"] = "// easy-props leaves, Cartoon look: light falls in soft bands. Wind as in leaves.gdshader.",
        ["celwood"] = "// easy-props wood, Comic look: flat colours and one hard step to a deeper shadow colour.\n// Use it with props exported with Look: Comic. Wind as in wood.gdshader.",
        ["celleaves"] = "// easy-props leaves, Comic look: flat colours and one hard step to a deeper shadow colour.\n// Use it with props exported with Look: Comic. Wind as in leaves.gdshader.",
    };

    /// <summary>A Cartoon or Comic material for wood or leaves, as Godot shader text (easy-props' <c>godotToonShader</c>).</summary>
    /// <param name="look"><see cref="ExportLook.Cartoon"/> or <see cref="ExportLook.Cel"/>.</param>
    /// <param name="material"><c>wood</c> or <c>leaves</c>.</param>
    public static string Shader(string look, string material)
    {
        if (look is not (ExportLook.Cartoon or ExportLook.Cel) || material is not ("wood" or "leaves"))
        {
            throw new ArgumentException($"No toon shader for {look} {material}");
        }

        string header = Notes[look + material];
        if (look == ExportLook.Cartoon)
        {
            return $"{header}\n{WindPart(material)}\n{(material == "wood" ? CartoonWood : CartoonLeaves)}{CartoonLight}";
        }

        // The settings go before vertex(), which hands each face its colour.
        string part = WindPart(material);
        int at = part.IndexOf("void vertex() {", StringComparison.Ordinal);
        string wind = at < 0 ? part : part[..at] + $"{ComicSettings.Trim()}\n\nvoid vertex() {{\n\tface_colour = COLOR.rgb;" + part[(at + "void vertex() {".Length)..];
        return $"{header}\n{wind}\n{ComicColours}{(material == "wood" ? ComicWood : ComicLeaves)}";
    }

    /// <summary>Dark lines around shapes, drawn over the whole picture (easy-props' <c>godotOutlineShader</c>).</summary>
    public static string OutlineShader() => Outline;

    // A number as Godot's shading language wants it: always with a point, so it is never read as whole.
    private static string N(double value)
    {
        string text = JsNumber.ToJsString(JsMath.Round(value * 10000) / 10000);
        return text.Contains('.', StringComparison.Ordinal) ? text : $"{text}.0";
    }

    private static string LinearColour(string hex)
    {
        string Channel(int at)
        {
            double value = Convert.ToInt32(hex.Substring(at, 2), 16) / 255.0;
            return N(value <= 0.04045 ? value / 12.92 : JsMath.Pow((value + 0.055) / 1.055, 2.4));
        }

        return $"vec3({Channel(1)}, {Channel(3)}, {Channel(5)})";
    }

    private static string CartoonSteps()
    {
        var lines = new List<string>();
        for (int index = 0; index < ToonLooks.CartoonSteps.Count; index++)
        {
            double step = ToonLooks.CartoonSteps[index];
            double rise = ToonLooks.CartoonLevels[index + 1] - ToonLooks.CartoonLevels[index];
            double half = ToonLooks.CartoonSoftness / 2;
            lines.Add($"\tlevel += {N(rise)} * smoothstep({N(step - half)}, {N(step + half)}, facing);");
        }

        return string.Join("\n", lines);
    }

    // The lit material's settings and wind, without its opening note or its colour.
    private static string WindPart(string material)
    {
        string body = string.Join("\n", EngineKits.Text($"godot/{material}.gdshader").Split('\n').Where(line => !line.StartsWith("//", StringComparison.Ordinal)));
        return body[..body.IndexOf("void fragment()", StringComparison.Ordinal)].TrimEnd();
    }
}
