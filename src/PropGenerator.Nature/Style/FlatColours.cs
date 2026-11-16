// Ported from easy-props frontend/src/assets/style/flat-colours.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;
using PropGenerator.Nature.Textures;

namespace PropGenerator.Nature.Style;

/// <summary>
/// A part's painted colours cut to its few main colours, for the Comic look. The view does this as it shows a part, and
/// a Comic export bakes it into the file, so both come out the same.
/// </summary>
public static class FlatColours
{
    /// <summary>
    /// A part's painted colours, each moved to the nearest of the part's few main colours (easy-props'
    /// <c>flattenColours</c>). Groups closer than <see cref="ToonLooks.FlatSameColour"/> are joined, unless
    /// <paramref name="join"/> is false (a picture of a whole prop).
    /// </summary>
    /// <param name="colours">Linear colours, r, g, b each.</param>
    /// <param name="join">Whether tones that are one colour with grain are joined.</param>
    /// <returns>The flat colours, as float32.</returns>
    public static float[] Flatten(ReadOnlySpan<double> colours, bool join = true)
    {
        int count = colours.Length / 3;
        float[] flat = new float[count * 3];

        if (count == 0)
        {
            return flat;
        }

        // Grouped by colour, with brightness counting for little. A Float32Array in easy-props.
        float[] features = new float[count * 4];
        for (int index = 0; index < count; index++)
        {
            double r = colours[index * 3];
            double g = colours[(index * 3) + 1];
            double b = colours[(index * 3) + 2];
            double sum = r + g + b + 1e-4;
            features[index * 4] = (float)(r / sum);
            features[(index * 4) + 1] = (float)(g / sum);
            features[(index * 4) + 2] = (float)(b / sum);
            features[(index * 4) + 3] = (float)(sum / 3 * ToonLooks.FlatBrightnessWeight);
        }

        double FeatureGap(int index, float[] centre) =>
            JsMath.Pow(features[index * 4] - (double)centre[0], 2)
            + JsMath.Pow(features[(index * 4) + 1] - (double)centre[1], 2)
            + JsMath.Pow(features[(index * 4) + 2] - (double)centre[2], 2)
            + JsMath.Pow(features[(index * 4) + 3] - (double)centre[3], 2);

        // Start from the colours most unlike each other, so a few flowers among many leaves still get a tone of their own.
        var centres = new List<float[]> { features[0..4] };
        float[] gap = new float[count];
        Array.Fill(gap, float.PositiveInfinity);

        while (centres.Count < ToonLooks.FlatMaxTones)
        {
            float[] latest = centres[^1];
            int farthest = -1;
            double farthestGap = 1e-6;

            for (int index = 0; index < count; index++)
            {
                gap[index] = (float)Math.Min(gap[index], FeatureGap(index, latest));
                if (gap[index] > farthestGap)
                {
                    farthestGap = gap[index];
                    farthest = index;
                }
            }

            if (farthest < 0)
            {
                break;
            }

            centres.Add(features[(farthest * 4)..((farthest * 4) + 4)]);
        }

        int NearestCentre(int index)
        {
            int best = 0;
            double bestDistance = double.PositiveInfinity;
            for (int centre = 0; centre < centres.Count; centre++)
            {
                double d = FeatureGap(index, centres[centre]);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = centre;
                }
            }

            return best;
        }

        var tones = new List<double[]>();

        for (int round = 0; round < ToonLooks.FlatRounds; round++)
        {
            // Each group's colour, its place in the grouping, and how much of the part it covers.
            var sums = centres.Select(_ => new double[8]).ToList();

            for (int index = 0; index < count; index++)
            {
                double[] sum = sums[NearestCentre(index)];
                for (int k = 0; k < 3; k++)
                {
                    sum[k] += colours[(index * 3) + k];
                }

                for (int k = 0; k < 4; k++)
                {
                    sum[3 + k] += features[(index * 4) + k];
                }

                sum[7] += 1;
            }

            var used = sums.Where(sum => sum[7] > 0).ToList();
            centres = [.. used.Select(sum => new[] { (float)(sum[3] / sum[7]), (float)(sum[4] / sum[7]), (float)(sum[5] / sum[7]), (float)(sum[6] / sum[7]) })];
            tones = [.. used.Select(sum => new[] { sum[0] / sum[7], sum[1] / sum[7], sum[2] / sum[7], sum[7] })];
        }

        // Join tones that are really one colour with grain, weighted by how much of the part each covers.
        bool joined = join;
        while (joined && tones.Count > 1)
        {
            joined = false;

            for (int a = 0; a < tones.Count && !joined; a++)
            {
                for (int b = a + 1; b < tones.Count && !joined; b++)
                {
                    if (ToneDistance(tones[a], tones[b]) < ToonLooks.FlatSameColour)
                    {
                        double wa = tones[a][3];
                        double wb = tones[b][3];
                        double w = wa + wb;
                        double sumA = tones[a][0] + tones[a][1] + tones[a][2];
                        double sumB = tones[b][0] + tones[b][1] + tones[b][2];
                        double[] lighter = sumA >= sumB ? tones[a] : tones[b];

                        // The joined tone keeps the lighter half's brightness, and leans to its colour.
                        double[] mixed = new double[3];
                        for (int k = 0; k < 3; k++)
                        {
                            mixed[k] = (((tones[a][k] * wa) + (tones[b][k] * wb)) / w * (1 - ToonLooks.FlatLean)) + (lighter[k] * ToonLooks.FlatLean);
                        }

                        double lift = Math.Max(sumA, sumB) / Math.Max(mixed[0] + mixed[1] + mixed[2], 1e-4);
                        tones[a] = [Math.Min(mixed[0] * lift, 1), Math.Min(mixed[1] * lift, 1), Math.Min(mixed[2] * lift, 1), w];
                        tones.RemoveAt(b);
                        joined = true;
                    }
                }
            }
        }

        // Matched by colour as the joining was, not by plain distance.
        for (int index = 0; index < count; index++)
        {
            double[] colour = [colours[index * 3], colours[(index * 3) + 1], colours[(index * 3) + 2]];
            double[] best = tones[0];
            double bestDistance = double.PositiveInfinity;
            foreach (double[] tone in tones)
            {
                double d = ToneDistance(colour, tone);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = tone;
                }
            }

            flat[index * 3] = (float)best[0];
            flat[(index * 3) + 1] = (float)best[1];
            flat[(index * 3) + 2] = (float)best[2];
        }

        return flat;
    }

    /// <summary>
    /// A copy of a picture with each shown pixel moved to one of its few main colours, found as for a part's painted
    /// colours (easy-props' <c>flattenPicture</c>); the cut-away pixels keep theirs.
    /// </summary>
    /// <param name="picture">The picture, sRGB bytes with alpha.</param>
    /// <param name="cutoff">The alpha share below which a pixel is cut away.</param>
    /// <returns>The flattened copy.</returns>
    public static RgbaImage FlattenPicture(RgbaImage picture, double cutoff)
    {
        ArgumentNullException.ThrowIfNull(picture);
        byte[] data = picture.Rgba;
        var shown = new List<int>();
        var colours = new List<double>();

        for (int index = 0; index < picture.W * picture.H; index++)
        {
            if (data[(index * 4) + 3] >= cutoff * 255)
            {
                shown.Add(index);
                colours.Add(ToLinear(data[index * 4]));
                colours.Add(ToLinear(data[(index * 4) + 1]));
                colours.Add(ToLinear(data[(index * 4) + 2]));
            }
        }

        float[] flat = Flatten(colours.ToArray(), join: false);
        byte[] pixels = (byte[])data.Clone();

        for (int at = 0; at < shown.Count; at++)
        {
            int index = shown[at];
            pixels[index * 4] = ToByte(flat[at * 3]);
            pixels[(index * 4) + 1] = ToByte(flat[(at * 3) + 1]);
            pixels[(index * 4) + 2] = ToByte(flat[(at * 3) + 2]);
        }

        return new RgbaImage(picture.W, picture.H, pixels) { Name = picture.Name };
    }

    // How different two tones look as colours; brightness counts for little.
    private static double ToneDistance(double[] a, double[] b)
    {
        double sumA = a[0] + a[1] + a[2] + 1e-4;
        double sumB = b[0] + b[1] + b[2] + 1e-4;
        double hue = JsMath.Hypot((a[0] / sumA) - (b[0] / sumB), (a[1] / sumA) - (b[1] / sumB), (a[2] / sumA) - (b[2] / sumB));
        return JsMath.Hypot(hue, (sumA - sumB) / 3 * ToonLooks.FlatBrightnessWeight);
    }

    private static double ToLinear(byte value)
    {
        double v = value / 255.0;
        return v <= 0.04045 ? v / 12.92 : JsMath.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static byte ToByte(double linear)
    {
        double value = linear <= 0.0031308 ? linear * 12.92 : (1.055 * JsMath.Pow(linear, 1 / 2.4)) - 0.055;
        return (byte)JsMath.Round(Math.Min(Math.Max(value, 0), 1) * 255);
    }
}
