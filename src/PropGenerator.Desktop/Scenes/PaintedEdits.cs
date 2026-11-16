// Ported from easy-props frontend/src/assets/trees/painted/edits.ts (PaintedEdits, parseEdits) (MIT, © 2026 AHS12).
using System.Text.Json;
using PropGenerator.Nature.Rocks;

namespace PropGenerator.Desktop.Scenes;

/// <summary>
/// The hand edits to single parts of a painted tree, keyed by each part's stable name. The tree is still built from one
/// plan: an edit is an instruction to it, so the wood re-blends around a moved limb and children follow their parent.
/// </summary>
/// <param name="Limbs">Limb edits by limb key.</param>
/// <param name="Clumps">Clump edits by clump key.</param>
public sealed record PaintedEdits(IReadOnlyDictionary<string, LimbEdit> Limbs, IReadOnlyDictionary<string, ClumpEdit> Clumps)
{
    /// <summary>No edits.</summary>
    public static readonly PaintedEdits None = new(new Dictionary<string, LimbEdit>(), new Dictionary<string, ClumpEdit>());

    /// <summary>A limb's edit, or <see cref="LimbEdit.None"/>.</summary>
    /// <param name="key">The limb's key.</param>
    /// <returns>Its edit.</returns>
    public LimbEdit Limb(string key) => Limbs.TryGetValue(key, out LimbEdit? edit) ? edit : LimbEdit.None;

    /// <summary>A clump's edit, or <see cref="ClumpEdit.None"/>.</summary>
    /// <param name="key">The clump's key.</param>
    /// <returns>Its edit.</returns>
    public ClumpEdit Clump(string key) => Clumps.TryGetValue(key, out ClumpEdit? edit) ? edit : ClumpEdit.None;

    /// <summary>
    /// The edits in a tree's <c>edits</c> text (easy-props' <c>parseEdits</c>). Never throws: a damaged edit list means an
    /// unedited tree, not a broken one. A field that is not the kind of value it should be counts as not edited.
    /// </summary>
    /// <param name="text">The JSON text, or empty.</param>
    /// <returns>The edits.</returns>
    public static PaintedEdits Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            // JSON.parse('null').limbs throws, which the catch turns into no edits.
            if (root.ValueKind == JsonValueKind.Null)
            {
                return None;
            }

            var limbs = new Dictionary<string, LimbEdit>(StringComparer.Ordinal);
            var clumps = new Dictionary<string, ClumpEdit>(StringComparer.Ordinal);

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("limbs", out JsonElement limbsJson) && limbsJson.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in limbsJson.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        limbs[property.Name] = ParseLimb(property.Value);
                    }
                }
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("clumps", out JsonElement clumpsJson) && clumpsJson.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in clumpsJson.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        clumps[property.Name] = ParseClump(property.Value);
                    }
                }
            }

            return new PaintedEdits(limbs, clumps);
        }
        catch (JsonException)
        {
            return None;
        }
    }

    private static LimbEdit ParseLimb(JsonElement json)
    {
        IReadOnlyList<Vec3>? offsets = null;
        if (json.TryGetProperty("offsets", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            offsets = [.. list.EnumerateArray().Select(item => VectorOf(item) ?? new Vec3(0, 0, 0))];
        }

        return new LimbEdit(
            offsets,
            NumberOf(json, "lengthScale"),
            NumberOf(json, "thicknessScale"),
            NumberOf(json, "sizeScale"),
            json.TryGetProperty("turn", out JsonElement turn) ? VectorOf(turn) : null,
            Truthy(json, "removed"));
    }

    private static ClumpEdit ParseClump(JsonElement json) => new(
        json.TryGetProperty("offset", out JsonElement offset) ? VectorOf(offset) : null,
        NumberOf(json, "sizeScale"),
        NumberOf(json, "flattenScale"),
        NumberOf(json, "fullnessScale"),
        NumberOf(json, "leafSizeScale"),
        NumberOf(json, "lumpinessScale"),
        json.TryGetProperty("colour", out JsonElement colour) && colour.ValueKind == JsonValueKind.String ? colour.GetString() : null,
        Truthy(json, "removed"));

    private static double? NumberOf(JsonElement json, string name) =>
        json.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static Vec3? VectorOf(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() < 3)
        {
            return null;
        }

        double At(int index) => json[index].ValueKind == JsonValueKind.Number ? json[index].GetDouble() : 0;
        return new Vec3(At(0), At(1), At(2));
    }

    // JavaScript truthiness of a JSON value: true, a number other than 0, a non-empty string, any object or array.
    private static bool Truthy(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.GetDouble() != 0,
            JsonValueKind.String => value.GetString()!.Length > 0,
            JsonValueKind.Object or JsonValueKind.Array => true,
            _ => false,
        };
    }
}
