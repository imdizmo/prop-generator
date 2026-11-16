// Adapted from easy-props frontend/src/core/export/pipeline/far.ts (farFrame, the card shown at the Far away level)
// (MIT, © 2026 AHS12).
using PropGenerator.Nature.Export;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The far-away card as the view shows it at the Far away detail level: the export's two crossed cards
/// (<see cref="FarCard.Geometry"/>), Z-up, white, printed with the pictures the view's own OpenGL drew of the prop and
/// cut out where they are see-through, as an engine draws the card.
/// </summary>
internal static class FarCardMesh
{
    /// <summary>The card's box around a prop standing on its pivot, Z-up (easy-props' farFrame, the same margins).</summary>
    /// <param name="mesh">The prop at full detail.</param>
    /// <returns>The box.</returns>
    internal static FarFrame Frame(ColouredMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        double reach = 0, bottom = 0, top = 0;
        float[] p = mesh.Positions;
        for (int i = 0; i + 2 < p.Length; i += 3)
        {
            reach = Math.Max(reach, Math.Sqrt((p[i] * (double)p[i]) + (p[i + 1] * (double)p[i + 1])));
            bottom = Math.Min(bottom, p[i + 2]);
            top = Math.Max(top, p[i + 2]);
        }

        return new FarFrame(Math.Max(reach, 1e-3) * 1.01, bottom, Math.Max(top, bottom + 1e-3) * 1.01);
    }

    /// <summary>The card, printed with its pictures.</summary>
    /// <param name="frame">The card's box.</param>
    /// <param name="pictures">The pictures the view drew.</param>
    /// <returns>The mesh: eight triangles.</returns>
    internal static ColouredMesh Card(FarFrame frame, FarPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        Nature.Kernel.PropMesh card = FarCard.Geometry(frame, null);
        int count = card.Position.Length / 3;
        float[] positions = new float[count * 3], normals = new float[count * 3], colours = new float[count * 3], tex = new float[count * 3];
        for (int i = 0; i < count; i++)
        {
            // Y-up to Z-up: (x, y, z) becomes (x, -z, y).
            positions[i * 3] = card.Position[i * 3];
            positions[(i * 3) + 1] = -card.Position[(i * 3) + 2];
            positions[(i * 3) + 2] = card.Position[(i * 3) + 1];
            normals[i * 3] = card.Normal![i * 3];
            normals[(i * 3) + 1] = -card.Normal[(i * 3) + 2];
            normals[(i * 3) + 2] = card.Normal[(i * 3) + 1];
            colours[i * 3] = colours[(i * 3) + 1] = colours[(i * 3) + 2] = 1;
            tex[i * 3] = card.Uv![i * 2];
            tex[(i * 3) + 1] = card.Uv[(i * 2) + 1];
            tex[(i * 3) + 2] = 1;
        }

        return new ColouredMesh(positions, normals, colours, [.. card.Index!.Select(v => (int)v)])
        {
            TexCoords = tex,
            Texture = new MeshTexture(pictures.Size, pictures.Size, pictures.Colour, (float)FarCard.Cutoff),
        };
    }
}
