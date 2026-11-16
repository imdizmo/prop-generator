// Approach from Groundwork's ModelThumb.
using System.Numerics;

namespace PropGenerator.Desktop.Pictures;

/// <summary>
/// A coloured mesh's picture for the objects drawer: drawn on the CPU with a depth buffer, seen from the front right
/// and a little above, lit from the upper left, on a clear background. A printed triangle (a leaf card) is multiplied by
/// its picture, nearest texel, and left out where the picture's alpha is under its cut.
/// </summary>
internal static class MeshThumb
{
    private const float Yaw = 0.6f;
    private const float Tilt = 0.35f;
    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.4f, 0.75f, 0.55f));

    /// <summary>Draws the mesh.</summary>
    /// <param name="m">The mesh: Z-up, linear colours.</param>
    /// <param name="size">The picture's side in pixels.</param>
    /// <returns>A PNG, <paramref name="size"/> pixels square, the mesh filling about 90 percent of it.</returns>
    public static byte[] Png(ColouredMesh m, int size = 128)
    {
        ArgumentNullException.ThrowIfNull(m);
        int n = size * 2;
        int count = m.Positions.Length / 3;

        // Each vertex in view space: x to the right, y up, z towards the viewer.
        float cy = MathF.Cos(Yaw), sy = MathF.Sin(Yaw), ct = MathF.Cos(Tilt), st = MathF.Sin(Tilt);
        var view = new Vector3[count];
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (int i = 0; i < count; i++)
        {
            float x = m.Positions[i * 3], y = m.Positions[(i * 3) + 1], z = m.Positions[(i * 3) + 2];
            float right = (x * cy) + (y * sy);
            float away = (-x * sy) + (y * cy);
            var v = new Vector3(right, (z * ct) + (away * st), (z * st) - (away * ct));
            view[i] = v;
            lo = Vector3.Min(lo, v);
            hi = Vector3.Max(hi, v);
        }

        float[] rgba = new float[n * n * 4];
        if (count > 0)
        {
            float[] depth = new float[n * n];
            Array.Fill(depth, float.MinValue);
            float scale = n * 0.9f / MathF.Max(1e-4f, MathF.Max(hi.X - lo.X, hi.Y - lo.Y));
            Vector3 mid = (lo + hi) / 2;
            var s = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                s[i] = new Vector3(((view[i].X - mid.X) * scale) + (n / 2f), (n / 2f) - ((view[i].Y - mid.Y) * scale), view[i].Z);
            }

            Draw(m, view, s, n, rgba, depth);
        }

        return PngEncoder.Encode(Shrink(rgba, n, size), size, size);
    }

    private static void Draw(ColouredMesh m, Vector3[] view, Vector3[] s, int n, float[] rgba, float[] depth)
    {
        int[] idx = m.Indices;
        float[]? tex = m.Texture is null ? null : m.TexCoords;
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            bool printed = tex is not null && tex[(a * 3) + 2] > 0.5f;
            Vector3 p0 = s[a], p1 = s[b], p2 = s[c];
            float area = ((p1.X - p0.X) * (p2.Y - p0.Y)) - ((p2.X - p0.X) * (p1.Y - p0.Y));
            if (MathF.Abs(area) < 1e-6f)
            {
                continue;
            }

            // Flat light: one normal per triangle, turned to face the viewer.
            var normal = Vector3.Cross(view[b] - view[a], view[c] - view[a]);
            if (normal.LengthSquared() < 1e-20f)
            {
                continue;
            }

            normal = Vector3.Normalize(normal);
            if (normal.Z < 0)
            {
                normal = -normal;
            }

            float lit = 0.45f + (0.65f * MathF.Max(0, Vector3.Dot(normal, Light)));
            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
            int x1 = Math.Min(n - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
            int y1 = Math.Min(n - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = (((p1.X - px) * (p2.Y - py)) - ((p2.X - px) * (p1.Y - py))) / area;
                    float w1 = (((p2.X - px) * (p0.Y - py)) - ((p0.X - px) * (p2.Y - py))) / area;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0)
                    {
                        continue;
                    }

                    int p = (y * n) + x;
                    float z = (w0 * p0.Z) + (w1 * p1.Z) + (w2 * p2.Z);
                    if (z <= depth[p])
                    {
                        continue;
                    }

                    int texel = -1;
                    if (printed)
                    {
                        MeshTexture picture = m.Texture!;
                        float u = (w0 * tex![a * 3]) + (w1 * tex[b * 3]) + (w2 * tex[c * 3]);
                        float v = (w0 * tex[(a * 3) + 1]) + (w1 * tex[(b * 3) + 1]) + (w2 * tex[(c * 3) + 1]);
                        int tx = Math.Clamp((int)(u * picture.Width), 0, picture.Width - 1);
                        int ty = Math.Clamp((int)(v * picture.Height), 0, picture.Height - 1);
                        texel = ((ty * picture.Width) + tx) * 4;
                        if (picture.Rgba[texel + 3] < picture.AlphaCut * 255)
                        {
                            continue;
                        }
                    }

                    depth[p] = z;
                    for (int k = 0; k < 3; k++)
                    {
                        float col = (w0 * m.Colours[(a * 3) + k]) + (w1 * m.Colours[(b * 3) + k]) + (w2 * m.Colours[(c * 3) + k]);
                        if (texel >= 0)
                        {
                            col *= Linear(m.Texture!.Rgba[texel + k]);
                        }

                        rgba[(p * 4) + k] = col * lit;
                    }

                    rgba[(p * 4) + 3] = 1;
                }
            }
        }
    }

    /// <summary>Halves the picture with a box filter (colours weighted by coverage) and encodes linear to sRGB.</summary>
    private static byte[] Shrink(float[] big, int n, int size)
    {
        byte[] bytes = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int q = ((((y * 2) + dy) * n) + (x * 2) + dx) * 4;
                        r += big[q];
                        g += big[q + 1];
                        b += big[q + 2];
                        a += big[q + 3];
                    }
                }

                int o = ((y * size) + x) * 4;
                if (a > 0)
                {
                    bytes[o] = Encode(r / a);
                    bytes[o + 1] = Encode(g / a);
                    bytes[o + 2] = Encode(b / a);
                    bytes[o + 3] = (byte)((a / 4 * 255) + 0.5f);
                }
            }
        }

        return bytes;
    }

    private static float Linear(byte srgb)
    {
        float c = srgb / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static byte Encode(float linear)
    {
        float c = Math.Clamp(linear, 0, 1);
        float s = c <= 0.0031308f ? c * 12.92f : (1.055f * MathF.Pow(c, 1 / 2.4f)) - 0.055f;
        return (byte)((s * 255) + 0.5f);
    }
}
