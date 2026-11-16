// Ported from easy-props frontend/src/core/export/pipeline/far.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Style;
using PropGenerator.Nature.Textures;

namespace PropGenerator.Nature.Export;

/// <summary>
/// The far-away card: two crossed upright cards with pictures of the prop on them, for an engine to show in place of
/// the prop once it is far off. Each side of each card shows the view from that side, drawn level and without
/// perspective, so the card is the prop's real size and stands where the prop does.
/// </summary>
public static class FarCard
{
    /// <summary>Faces in a far-away card: two cards, two sides each, two triangles a side.</summary>
    public const int Faces = 8;

    /// <summary>Pixels across each view.</summary>
    public const int ViewSize = 256;

    /// <summary>Where an engine cuts the picture out, as glTF's alpha cut-off.</summary>
    public const double Cutoff = 0.5;

    // How much the see-through amount is thickened, so cut-out crowns don't thin out with distance.
    private const double EdgeThickening = 1.3;

    // How many pixels deep edge colours spread into the see-through ones.
    private const int FillDepth = 8;

    private static readonly int[][] Neighbours = [[1, 0], [-1, 0], [0, 1], [0, -1]];

    private static readonly int[][] Corners = [[0, 0], [1, 0], [1, 1], [0, 1]];

    /// <summary>Gets the four views: the way each side faces, right in its picture, and its cell (column, row from the bottom).</summary>
    public static IReadOnlyList<(string Name, double[] Facing, double[] Right, int[] Cell)> Views { get; } =
    [
        ("front", [0, 0, 1], [1, 0, 0], [0, 0]),
        ("right", [1, 0, 0], [0, 0, -1], [1, 0]),
        ("back", [0, 0, -1], [-1, 0, 0], [0, 1]),
        ("left", [-1, 0, 0], [0, 0, 1], [1, 1]),
    ];

    /// <summary>The box around merged meshes, which already stand on the pivot (easy-props' <c>farFrame</c>).</summary>
    /// <param name="meshes">The merged meshes.</param>
    public static FarFrame Frame(IReadOnlyList<MergedMesh> meshes)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        double reach = 0;
        double bottom = 0;
        double top = 0;

        foreach (MergedMesh mesh in meshes)
        {
            float[] position = mesh.Mesh.Position;
            for (int i = 0; i < position.Length / 3; i++)
            {
                reach = Math.Max(reach, JsMath.Hypot(position[i * 3], position[(i * 3) + 2]));
                bottom = Math.Min(bottom, position[(i * 3) + 1]);
                top = Math.Max(top, position[(i * 3) + 1]);
            }
        }

        // A hair of room, so the outermost leaf isn't cut by the frame's edge.
        return new FarFrame(Math.Max(reach, 1e-3) * 1.01, bottom, Math.Max(top, bottom + 1e-3) * 1.01);
    }

    /// <summary>
    /// Spreads the colour of solid pixels into see-through ones, one cell at a time; whatever is still empty after a few
    /// pixels takes the cell's average (easy-props' <c>fillGaps</c>).
    /// </summary>
    /// <param name="pixels">RGBA bytes, changed in place.</param>
    /// <param name="size">Pixels along each edge.</param>
    /// <param name="cells">Cells along each edge.</param>
    /// <param name="depth">How many pixels deep colours spread.</param>
    public static void FillGaps(byte[] pixels, int size, int cells = 2, int depth = FillDepth)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        int cell = size / cells;
        byte[] filled = new byte[size * size];

        for (int at = 0; at < size * size; at++)
        {
            filled[at] = pixels[(at * 4) + 3] > 0 ? (byte)1 : (byte)0;
        }

        for (int row = 0; row < cells; row++)
        {
            for (int column = 0; column < cells; column++)
            {
                int x0 = column * cell;
                int y0 = row * cell;
                double[] average = [0, 0, 0];
                int solid = 0;

                for (int y = y0; y < y0 + cell; y++)
                {
                    for (int x = x0; x < x0 + cell; x++)
                    {
                        int at = (y * size) + x;
                        if (filled[at] != 0)
                        {
                            for (int channel = 0; channel < 3; channel++)
                            {
                                average[channel] += pixels[(at * 4) + channel];
                            }

                            solid++;
                        }
                    }
                }

                for (int step = 0; step < depth; step++)
                {
                    var reached = new List<(int At, byte[] Colour)>();

                    for (int y = y0; y < y0 + cell; y++)
                    {
                        for (int x = x0; x < x0 + cell; x++)
                        {
                            if (filled[(y * size) + x] != 0)
                            {
                                continue;
                            }

                            double[] sum = [0, 0, 0];
                            int count = 0;

                            foreach (int[] d in Neighbours)
                            {
                                int nx = x + d[0];
                                int ny = y + d[1];

                                if (nx >= x0 && nx < x0 + cell && ny >= y0 && ny < y0 + cell && filled[(ny * size) + nx] != 0)
                                {
                                    int near = ((ny * size) + nx) * 4;
                                    sum[0] += pixels[near];
                                    sum[1] += pixels[near + 1];
                                    sum[2] += pixels[near + 2];
                                    count++;
                                }
                            }

                            if (count > 0)
                            {
                                reached.Add(((y * size) + x, [Byte(sum[0] / count), Byte(sum[1] / count), Byte(sum[2] / count)]));
                            }
                        }
                    }

                    foreach (var (at, colour) in reached)
                    {
                        colour.CopyTo(pixels, at * 4);
                        filled[at] = 1;
                    }
                }

                byte[] rest = solid > 0 ? [Byte(average[0] / solid), Byte(average[1] / solid), Byte(average[2] / solid)] : [128, 128, 128];

                for (int y = y0; y < y0 + cell; y++)
                {
                    for (int x = x0; x < x0 + cell; x++)
                    {
                        if (filled[(y * size) + x] == 0)
                        {
                            rest.CopyTo(pixels, ((y * size) + x) * 4);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Gives the relief the colour picture's see-through amount, then fills both (easy-props' <c>finishPictures</c>).</summary>
    /// <param name="colour">The colour picture, changed in place.</param>
    /// <param name="relief">The relief, changed in place.</param>
    /// <param name="size">Pixels along each edge.</param>
    public static void FinishPictures(byte[] colour, byte[] relief, int size)
    {
        ArgumentNullException.ThrowIfNull(colour);
        ArgumentNullException.ThrowIfNull(relief);
        for (int at = 3; at < colour.Length; at += 4)
        {
            colour[at] = (byte)Math.Min(255, JsMath.Round(colour[at] * EdgeThickening));
            relief[at] = colour[at];
        }

        FillGaps(colour, size);
        FillGaps(relief, size);

        // A relief image has no see-through part.
        for (int at = 3; at < relief.Length; at += 4)
        {
            relief[at] = 255;
        }
    }

    /// <summary>
    /// Two upright cards through the pivot, crossed at right angles, each side with its own corners (easy-props'
    /// <c>farCardGeometry</c>). With wind, each corner sways as the prop does at that point and never flutters.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="wind">The prop's wind extent, or null.</param>
    public static PropMesh Geometry(FarFrame frame, WindExtent? wind)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var positions = new List<double>();
        var normals = new List<double>();
        var uvs = new List<double>();
        var index = new List<uint>();

        foreach (var (_, facing, right, cell) in Views)
        {
            uint first = (uint)(positions.Count / 3);

            foreach (int[] corner in Corners)
            {
                int across = corner[0];
                int up = corner[1];
                double outward = ((across * 2) - 1) * frame.Reach;

                positions.Add(right[0] * outward);
                positions.Add(up != 0 ? frame.Top : frame.Bottom);
                positions.Add(right[2] * outward);
                normals.AddRange(facing);
                uvs.Add((cell[0] + across) / 2.0);
                uvs.Add((cell[1] + up) / 2.0);
            }

            index.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }

        var mesh = new PropMesh
        {
            Position = [.. positions.Select(v => (float)v)],
            Normal = [.. normals.Select(v => (float)v)],
            Uv = [.. uvs.Select(v => (float)v)],
        };
        mesh.SetIndex([.. index]);

        if (wind is not null)
        {
            float[] sway = new float[positions.Count / 3 * 2];
            for (int at = 0; at < positions.Count; at += 3)
            {
                sway[at / 3 * 2] = (float)Wind.Weight(positions[at], positions[at + 1], positions[at + 2], "solid", wind).Sway;
            }

            mesh.Uv1 = sway;
        }

        return mesh;
    }

    /// <summary>The card for merged full-detail meshes, or null when there are none or the pictures can't be drawn (easy-props' <c>buildFarCard</c>).</summary>
    /// <param name="meshes">The merged meshes at full detail.</param>
    /// <param name="wind">The prop's wind extent, or null.</param>
    /// <param name="painter">What draws the views.</param>
    public static FarCardData? Build(IReadOnlyList<MergedMesh> meshes, WindExtent? wind, IFarPainter painter)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(painter);
        if (meshes.Count == 0)
        {
            return null;
        }

        FarFrame frame = Frame(meshes);
        FarPictures? pictures = painter.Paint(meshes, frame, ViewSize);
        return pictures is null ? null : new FarCardData(frame, pictures, Geometry(frame, wind));
    }

    /// <summary>The card as a file wants it; a Comic card's picture is cut to flat colours (easy-props' <c>farCardMesh</c>).</summary>
    /// <param name="card">The card.</param>
    /// <param name="name">The prop's name.</param>
    /// <param name="comic">Whether the export is in the Comic look.</param>
    public static GlbNode Mesh(FarCardData card, string name, bool comic)
    {
        ArgumentNullException.ThrowIfNull(card);
        int size = card.Pictures.Size;
        var picture = new RgbaImage(size, size, card.Pictures.Colour) { Name = $"{name}_far" };
        RgbaImage map = comic ? FlatColours.FlattenPicture(picture, Cutoff) : picture;
        RgbaImage relief = FileRelief.Build(new RgbaImage(size, size, card.Pictures.Relief), true);
        string[] attributes = card.Geometry.Uv1 is null ? ["position", "normal", "uv"] : ["position", "normal", "uv", "uv1"];
        return new GlbNode($"{name}_far", card.Geometry, attributes, [new FarMaterial($"{name}_far", map, relief)], false);
    }

    // JavaScript's Math.round, for a byte.
    private static byte Byte(double value) => (byte)JsMath.Round(value);
}
