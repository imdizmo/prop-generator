namespace PropGenerator;

/// <summary>
/// The floor tiles' shapes, as the tile script the v5 floor atlas was painted for makes them (atlas UVs, bevel 0).
/// Atlas rows, counted from the bottom of the image: long sides 0 to 64 (the -x side) and 64 to 128 (the +x side),
/// cut ends 128 to 224, top 224 to 1024.
/// </summary>
public static class TileMesh
{
    /// <summary>The tile's width and depth.</summary>
    public const float S = 2.23f;

    /// <summary>The tile's thickness.</summary>
    public const float T = 0.25f;

    /// <summary>Half a texel of the 1024 px atlas: turning the tile 180 degrees maps texel c onto texel -c.</summary>
    public const float H = 0.5f / 1024f;

    /// <summary>The first face number of the triangle tile; the square's faces are 0 to 5.</summary>
    public const int TriangleFaceOffset = 100;

    /// <summary>The square tile: bottom, top, then the four sides.</summary>
    public static Mesh Square() => Build([(-S / 2, -S / 2), (S / 2, -S / 2), (S / 2, S / 2), (-S / 2, S / 2)], 0);

    /// <summary>The triangle tile: the square cut along its diagonal. Face numbers start at 100.</summary>
    public static Mesh Triangle() => Build([(-S / 2, -S / 2), (S / 2, -S / 2), (-S / 2, S / 2)], TriangleFaceOffset);

    /// <summary>A floor tile piece whose marks cover the faces of both the square and the triangle.</summary>
    public static Piece FloorPiece()
    {
        var none = new Dictionary<string, System.Text.Json.JsonElement>();
        List<FaceMark> marks = [];
        foreach (int offset in new[] { 0, TriangleFaceOffset })
        {
            var plan = offset == 0 ? SquarePlan : TrianglePlan;
            marks.Add(new FaceMark(offset, "plank_top", none));
            marks.Add(new FaceMark(offset + 1, "plank_top", none));
            for (int i = 0; i < plan.Length; i++)
            {
                var (nx, ny) = SideNormal(plan[i], plan[(i + 1) % plan.Length]);
                marks.Add(new FaceMark(offset + 2 + i, ny >= nx ? "cut_end" : "plank_side", none));
            }
        }
        return new Piece("floor_tile", marks);
    }

    private static readonly (float, float)[] SquarePlan = [(-S / 2, -S / 2), (S / 2, -S / 2), (S / 2, S / 2), (-S / 2, S / 2)];
    private static readonly (float, float)[] TrianglePlan = [(-S / 2, -S / 2), (S / 2, -S / 2), (-S / 2, S / 2)];

    // Absolute x and y parts of a side's outward normal. The small margin makes the hypotenuse (equal parts) a cut end.
    private static (float Nx, float Ny) SideNormal((float X, float Y) a, (float X, float Y) b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        return (MathF.Abs(dy) / len - 1e-5f, MathF.Abs(dx) / len);
    }

    private static Mesh Build((float X, float Y)[] plan, int idOffset)
    {
        List<float> pos = [];
        List<float> uv = [];
        List<int> idx = [];
        List<int> ids = [];
        int n = plan.Length;

        void Face(int id, (float X, float Y, float Z)[] corners, Func<(float X, float Y, float Z), (float, float)> uvOf)
        {
            int first = pos.Count / 3;
            foreach (var c in corners)
            {
                pos.Add(c.X); pos.Add(c.Y); pos.Add(c.Z);
                var (u, v) = uvOf(c);
                uv.Add(u); uv.Add(v);
            }
            for (int k = 1; k < corners.Length - 1; k++)
            {
                idx.Add(first); idx.Add(first + k); idx.Add(first + k + 1);
                ids.Add(id);
            }
        }

        (float, float) TopUv((float X, float Y, float Z) c) =>
            (c.X / S + 0.5f + H, (224f + 800f * (c.Y / S + 0.5f) + 0.5f) / 1024f);

        // Bottom (faces down, so the plan runs backwards) and top.
        Face(idOffset, [.. plan.Reverse().Select(p => (p.X, p.Y, 0f))], TopUv);
        Face(idOffset + 1, [.. plan.Select(p => (p.X, p.Y, T))], TopUv);

        for (int i = 0; i < n; i++)
        {
            var a = plan[i];
            var b = plan[(i + 1) % n];
            var (nx, ny) = SideNormal(a, b);
            bool cutEnd = ny >= nx;
            float outwardX = b.Y - a.Y;   // the outward normal's x sign (counter-clockwise plan)
            Func<(float X, float Y, float Z), (float, float)> uvOf;
            if (cutEnd)
            {
                uvOf = c => (c.X / S + 0.5f + H, (128f + 95f * c.Z / T) / 1024f);
            }
            else
            {
                uvOf = c => (c.Y / S + 0.5f, ((outwardX < 0 ? 0f : 64f) + 63f * c.Z / T) / 1024f);
            }

            Face(idOffset + 2 + i, [(a.X, a.Y, 0), (b.X, b.Y, 0), (b.X, b.Y, T), (a.X, a.Y, T)], uvOf);
        }
        return new Mesh([.. pos], [.. uv], [.. idx], [.. ids]);
    }
}
