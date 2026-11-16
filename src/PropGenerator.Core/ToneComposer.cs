namespace PropGenerator;

/// <summary>
/// Turns the painter's tone layers into colour on the CPU (for export). The shader that draws the live preview uses
/// the same formula; this comment is the one written description of it. It is the painter's reference
/// (painter/propgen_painter/layers.py compose_v5) with the palette's fields where v5 had fixed numbers.
/// <code>
/// Per pixel, with every colour operation done channel by channel:
///   v     = marks * contrast + rest                       (the light/dark value)
///   bm    = basemul
///   plank (region 0):
///     base  = Base * bm                                     (rounded to float32)
///     tint  = v > 0 ? 1 + (LitTint - 1) * clamp(v / 0.15, 0, 1)
///                   : 1 + (ShadeTint - 1) * clamp(-v / 0.15, 0, 1)
///     h     = clamp(drift * driftSetting, -1, 1)
///     dr    = h > 0 ? 1 + (Redder - 1) * h : 1 + (Greyer - 1) * -h
///     col   = clamp(base * (1 + v) * tint * dr, 0, 1)       (rounded to float32)
///   cut end (region 1):
///     col   = Base * bm * EndMul * (1 + v)                  (not clipped yet; rounded to float32)
///   seam colour: sc = SeamLight + (SeamDeep - SeamLight) * seam
///   col = (1 - aa) * col + aa * sc                          ((1 - aa) * col in float32, then added to aa * sc)
///   seam (region 2): col = sc
///   result = byte(clamp(col, 0, 1) * 255 + 0.5), rows flipped top-down, alpha 255.
/// </code>
/// SideMul and Gap are not used here: the painter already bakes the long sides' 0.94 and each plank's jitter into
/// basemul, and the gap is painted as a seam pixel.
/// </summary>
public static class ToneComposer
{
    /// <summary>Composes the layers into an image.</summary>
    /// <param name="l">The tone layers.</param>
    /// <param name="p">The palette.</param>
    /// <param name="contrast">How strongly the marks light and darken the wood.</param>
    /// <param name="drift">How strongly the red/grey colour drift shows.</param>
    /// <returns>RGBA8, rows top-down.</returns>
    public static byte[] Compose(Layers l, Palette p, float contrast, float drift)
    {
        int size = l.Size;
        byte[] output = new byte[size * size * 4];
        double[] baseC = [p.Base.R, p.Base.G, p.Base.B];
        double[] litT = [p.LitTint.R, p.LitTint.G, p.LitTint.B];
        double[] shdT = [p.ShadeTint.R, p.ShadeTint.G, p.ShadeTint.B];
        double[] redT = [p.Redder.R, p.Redder.G, p.Redder.B];
        double[] greT = [p.Greyer.R, p.Greyer.G, p.Greyer.B];
        double[] endT = [p.EndMul.R, p.EndMul.G, p.EndMul.B];
        double[] deep = [p.SeamDeep.R, p.SeamDeep.G, p.SeamDeep.B];
        double[] light = [p.SeamLight.R, p.SeamLight.G, p.SeamLight.B];

        for (int y = 0; y < size; y++)
        {
            int outRow = size - 1 - y;
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double v = ((double)l.Marks[i] * contrast) + l.Rest[i];
                double bm = l.BaseMul[i];
                double h = Math.Clamp((double)l.Drift[i] * drift, -1, 1);
                float region = l.Region[i];
                float aa = l.Aa[i];
                double seam = l.Seam[i];
                int o = ((outRow * size) + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    double sc = light[c] + ((deep[c] - light[c]) * seam);
                    float rgb;
                    if (region == 1f)
                    {
                        rgb = (float)(baseC[c] * bm * endT[c] * (1 + v));
                    }
                    else
                    {
                        float b = (float)(baseC[c] * bm);
                        double tint = v > 0
                            ? 1 + ((litT[c] - 1) * Math.Clamp(v / 0.15, 0, 1))
                            : 1 + ((shdT[c] - 1) * Math.Clamp(-v / 0.15, 0, 1));
                        double dr = h > 0 ? 1 + ((redT[c] - 1) * h) : 1 + ((greT[c] - 1) * -h);
                        rgb = (float)Math.Clamp(b * (1 + v) * tint * dr, 0, 1);
                    }

                    float mixed = (float)(((1f - aa) * rgb) + (aa * sc));
                    if (region == 2f)
                    {
                        mixed = (float)sc;
                    }

                    output[o + c] = (byte)((Math.Clamp(mixed, 0f, 1f) * 255f) + 0.5f);
                }

                output[o + 3] = 255;
            }
        }

        return output;
    }
}
