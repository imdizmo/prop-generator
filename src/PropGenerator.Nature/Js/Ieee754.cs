// Ported from V8 src/base/ieee754.cc (fdlibm, Copyright (C) 1993-2004 Sun Microsystems, via FreeBSD msun) and V8 src/builtins/math.tq (MathHypot); see NOTICE.md.

namespace PropGenerator.Nature.Js;

/// <summary>
/// V8's own maths functions (fdlibm), ported line for line so that every result has the same bits as V8's. Only the
/// functions where .NET's libm gives different bits are here; JsMathTests checks each one against a seeded sample of
/// Node's own results (tests/Nature.Tests/Fixtures/jsmath-sample.json, cut from a million inputs). Names follow fdlibm so the code can be read beside the C.
/// </summary>
/// <remarks>
/// <para>
/// Those results come from Node 26.12.1 as CachyOS builds it: GCC for x86-64-v3, which fuses a multiply and an add into
/// one FMA instruction (one rounding instead of two) wherever it can. So the reference is fdlibm as that machine code
/// computes it, not as the C reads. Every <see cref="Fma"/> below stands where that build has an FMA instruction
/// (read from its disassembly, function by function); everything else rounds after each operation, as the C says.
/// </para>
/// <para>
/// <c>Math.FusedMultiplyAdd</c> is exact IEEE fma on every machine (an instruction, or software where the CPU
/// has none), so these results do not depend on the CPU running the port.
/// </para>
/// </remarks>
internal static class Ieee754
{
    private const double Zero = 0.0;
    private const double Half = 0.5;
    private const double One = 1.0;
    private const double Huge = 1.0e300;
    private const double Tiny = 1.0e-300;
    private const double Two24 = 1.67772160000000000000e+07;
    private const double Twon24 = 5.96046447753906250000e-08;
    private const double Two54 = 1.80143985094819840000e+16;

    /// <summary>a*b + c with one rounding: where the reference build has an FMA instruction.</summary>
    private static double Fma(double a, double b, double c)
    {
        // The one allowed use of Math.FusedMultiplyAdd (BannedSymbols.txt): V8's fdlibm, as the pinned Node build
        // compiled it, fuses exactly here, so the port must too.
#pragma warning disable RS0030
        return Math.FusedMultiplyAdd(a, b, c);
#pragma warning restore RS0030
    }

    // ---- Word access (fdlibm's EXTRACT_WORDS, GET_HIGH_WORD, SET_LOW_WORD...). ----

    private static int High(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);

    private static uint Low(double x) => unchecked((uint)BitConverter.DoubleToInt64Bits(x));

    private static double Words(int hi, uint lo) => BitConverter.Int64BitsToDouble(((long)hi << 32) | lo);

    private static double WithHigh(double x, int hi) => Words(hi, Low(x));

    private static double WithLow(double x, uint lo) => Words(High(x), lo);

    // ---- Argument reduction: __ieee754_rem_pio2 and __kernel_rem_pio2. ----

    /// <summary>2/pi in 24-bit pieces, 1584 bits.</summary>
    private static readonly int[] TwoOverPi =
    [
        0xA2F983, 0x6E4E44, 0x1529FC, 0x2757D1, 0xF534DD, 0xC0DB62, 0x95993C,
        0x439041, 0xFE5163, 0xABDEBB, 0xC561B7, 0x246E3A, 0x424DD2, 0xE00649,
        0x2EEA09, 0xD1921C, 0xFE1DEB, 0x1CB129, 0xA73EE8, 0x8235F5, 0x2EBB44,
        0x84E99C, 0x7026B4, 0x5F7E41, 0x3991D6, 0x398353, 0x39F49C, 0x845F8B,
        0xBDF928, 0x3B1FF8, 0x97FFDE, 0x05980F, 0xEF2F11, 0x8B5A0A, 0x6D1F6D,
        0x367ECF, 0x27CB09, 0xB74F46, 0x3F669E, 0x5FEA2D, 0x7527BA, 0xC7EBE5,
        0xF17B3D, 0x0739F7, 0x8A5292, 0xEA6BFB, 0x5FB11F, 0x8D5D08, 0x560330,
        0x46FC7B, 0x6BABF0, 0xCFBC20, 0x9AF436, 0x1DA9E3, 0x91615E, 0xE61B08,
        0x659985, 0x5F14A0, 0x68408D, 0xFFD880, 0x4D7327, 0x310606, 0x1556CA,
        0x73A8C9, 0x60E27B, 0xC08C6B,
    ];

    /// <summary>The high words of n*pi/2 for n = 1..32.</summary>
    private static readonly int[] Npio2Hw =
    [
        0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C,
        0x4025FDBB, 0x402921FB, 0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C,
        0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB, 0x403AB41B, 0x403C463A,
        0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C,
        0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB,
        0x404858EB, 0x404921FB,
    ];

    /// <summary>pi/2 in 24-bit pieces.</summary>
    private static readonly double[] PIo2 =
    [
        1.57079625129699707031e+00, // 0x3FF921FB, 0x40000000
        7.54978941586159635335e-08, // 0x3E74442D, 0x00000000
        5.39030252995776476554e-15, // 0x3CF84698, 0x80000000
        3.28200341580791294123e-22, // 0x3B78CC51, 0x60000000
        1.27065575308067607349e-29, // 0x39F01B83, 0x80000000
        1.22933308981111328932e-36, // 0x387A2520, 0x40000000
        2.73370053816464559624e-44, // 0x36E38222, 0x80000000
        2.16741683877804819444e-51, // 0x3569F31D, 0x00000000
    ];

    private const double Invpio2 = 6.36619772367581382433e-01; // 0x3FE45F30, 0x6DC9C883
    private const double Pio2_1 = 1.57079632673412561417e+00; // 0x3FF921FB, 0x54400000
    private const double Pio2_1t = 6.07710050650619224932e-11; // 0x3DD0B461, 0x1A626331
    private const double Pio2_2 = 6.07710050630396597660e-11; // 0x3DD0B461, 0x1A600000
    private const double Pio2_2t = 2.02226624879595063154e-21; // 0x3BA3198A, 0x2E037073
    private const double Pio2_3 = 2.02226624871116645580e-21; // 0x3BA3198A, 0x2E000000
    private const double Pio2_3t = 8.47842766036889956997e-32; // 0x397B839A, 0x252049C1

    /// <summary>x - n*pi/2 as y0 + y1; returns n.</summary>
    private static int RemPio2(double x, out double y0, out double y1)
    {
        double z = 0;
        int hx = High(x);
        int ix = hx & 0x7FFFFFFF;
        if (ix <= 0x3FE921FB)
        {
            // |x| ~<= pi/4, no need for reduction.
            y0 = x;
            y1 = 0;
            return 0;
        }

        if (ix < 0x4002D97C)
        {
            // |x| < 3pi/4, special case with n=+-1.
            if (hx > 0)
            {
                z = x - Pio2_1;
                if (ix != 0x3FF921FB)
                {
                    // 33+53 bit pi is good enough.
                    y0 = z - Pio2_1t;
                    y1 = (z - y0) - Pio2_1t;
                }
                else
                {
                    // Near pi/2, use 33+33+53 bit pi.
                    z -= Pio2_2;
                    y0 = z - Pio2_2t;
                    y1 = (z - y0) - Pio2_2t;
                }

                return 1;
            }

            z = x + Pio2_1;
            if (ix != 0x3FF921FB)
            {
                y0 = z + Pio2_1t;
                y1 = (z - y0) + Pio2_1t;
            }
            else
            {
                z += Pio2_2;
                y0 = z + Pio2_2t;
                y1 = (z - y0) + Pio2_2t;
            }

            return -1;
        }

        if (ix <= 0x413921FB)
        {
            // |x| ~<= 2^19*(pi/2), medium size.
            double t = Math.Abs(x);
            int n = (int)Fma(t, Invpio2, Half);
            double fn = n;
            double r = Fma(-fn, Pio2_1, t);
            double w = fn * Pio2_1t;
            if (n < 32 && ix != Npio2Hw[n - 1])
            {
                // Quick check: no cancellation.
                y0 = r - w;
            }
            else
            {
                int j = ix >> 20;
                y0 = r - w;
                int i = j - ((High(y0) >> 20) & 0x7FF);
                if (i > 16)
                {
                    // 2nd iteration needed, good to 118 bits.
                    t = r;
                    r = Fma(-fn, Pio2_2, t);
                    w = Fma(fn, Pio2_2t, -Fma(-fn, Pio2_2, t - r));
                    y0 = r - w;
                    i = j - ((High(y0) >> 20) & 0x7FF);
                    if (i > 49)
                    {
                        // 3rd iteration needed, 151 bits, covers all possible cases.
                        t = r;
                        r = Fma(-fn, Pio2_3, t);
                        w = Fma(fn, Pio2_3t, -Fma(-fn, Pio2_3, t - r));
                        y0 = r - w;
                    }
                }
            }

            y1 = (r - y0) - w;
            if (hx < 0)
            {
                y0 = -y0;
                y1 = -y1;
                return -n;
            }

            return n;
        }

        // All other (large) arguments.
        if (ix >= 0x7FF00000)
        {
            // x is inf or NaN.
            y0 = y1 = x - x;
            return 0;
        }

        // Set z = scalbn(|x|, ilogb(x) - 23).
        z = WithLow(z, Low(x));
        int e0 = (ix >> 20) - 1046;
        z = WithHigh(z, ix - (e0 << 20));
        Span<double> tx = stackalloc double[3];
        for (int i = 0; i < 2; i++)
        {
            tx[i] = (int)z;
            z = (z - tx[i]) * Two24;
        }

        tx[2] = z;
        int nx = 3;
        while (tx[nx - 1] == Zero)
        {
            nx--;
        }

        int nn = KernelRemPio2(tx, out y0, out y1, e0, nx);
        if (hx < 0)
        {
            y0 = -y0;
            y1 = -y1;
            return -nn;
        }

        return nn;
    }

    /// <summary>
    /// __kernel_rem_pio2 with prec = 2 (53 + 53 bits), the only precision fdlibm's double functions use. The reference
    /// build fuses three multiply-adds here (z - two24*fw twice, z - 8*floor(z/8)); all three are exact either way, so
    /// the code keeps fdlibm's plain arithmetic.
    /// </summary>
    private static int KernelRemPio2(ReadOnlySpan<double> x, out double y0, out double y1, int e0, int nx)
    {
        Span<int> iq = stackalloc int[20];
        Span<double> f = stackalloc double[20];
        Span<double> fq = stackalloc double[20];
        Span<double> q = stackalloc double[20];

        // init_jk[prec] for prec 2.
        const int jk = 4;
        const int jp = jk;

        // Determine jx, jv, q0; note that 3 > q0.
        int jx = nx - 1;
        int jv = (e0 - 3) / 24;
        if (jv < 0)
        {
            jv = 0;
        }

        int q0 = e0 - (24 * (jv + 1));

        // Set up f[0] to f[jx+jk] where f[jx+jk] = ipio2[jv+jk].
        int j = jv - jx;
        int m = jx + jk;
        for (int i = 0; i <= m; i++, j++)
        {
            f[i] = j < 0 ? Zero : TwoOverPi[j];
        }

        double fw;

        // Compute q[0], q[1], ... q[jk].
        for (int i = 0; i <= jk; i++)
        {
            fw = 0.0;
            for (j = 0; j <= jx; j++)
            {
                fw += x[j] * f[jx + i - j];
            }

            q[i] = fw;
        }

        int jz = jk;
        int n;
        int ih;
        double z;
        while (true)
        {
            // Distill q[] into iq[] reversingly.
            int i;
            z = q[jz];
            for (i = 0, j = jz; j > 0; i++, j--)
            {
                fw = (int)(Twon24 * z);
                iq[i] = (int)(z - (Two24 * fw));
                z = q[j - 1] + fw;
            }

            // Compute n.
            z = Math.ScaleB(z, q0);
            z -= 8.0 * Math.Floor(z * 0.125);
            n = (int)z;
            z -= n;
            ih = 0;
            if (q0 > 0)
            {
                // Need iq[jz-1] to determine n.
                i = iq[jz - 1] >> (24 - q0);
                n += i;
                iq[jz - 1] -= i << (24 - q0);
                ih = iq[jz - 1] >> (23 - q0);
            }
            else if (q0 == 0)
            {
                ih = iq[jz - 1] >> 23;
            }
            else if (z >= 0.5)
            {
                ih = 2;
            }

            if (ih > 0)
            {
                // q > 0.5.
                n++;
                int carry = 0;
                for (i = 0; i < jz; i++)
                {
                    // Compute 1-q.
                    j = iq[i];
                    if (carry == 0)
                    {
                        if (j != 0)
                        {
                            carry = 1;
                            iq[i] = 0x1000000 - j;
                        }
                    }
                    else
                    {
                        iq[i] = 0xFFFFFF - j;
                    }
                }

                if (q0 > 0)
                {
                    // Rare case: chance is 1 in 12.
                    if (q0 == 1)
                    {
                        iq[jz - 1] &= 0x7FFFFF;
                    }
                    else if (q0 == 2)
                    {
                        iq[jz - 1] &= 0x3FFFFF;
                    }
                }

                if (ih == 2)
                {
                    z = One - z;
                    if (carry != 0)
                    {
                        z -= Math.ScaleB(One, q0);
                    }
                }
            }

            // Check if recomputation is needed.
            if (z != Zero)
            {
                break;
            }

            j = 0;
            for (i = jz - 1; i >= jk; i--)
            {
                j |= iq[i];
            }

            if (j != 0)
            {
                break;
            }

            // Need recomputation: k = number of terms needed.
            int k = 1;
            while (jk >= k && iq[jk - k] == 0)
            {
                k++;
            }

            for (i = jz + 1; i <= jz + k; i++)
            {
                // Add q[jz+1] to q[jz+k].
                f[jx + i] = TwoOverPi[jv + i];
                fw = 0.0;
                for (j = 0; j <= jx; j++)
                {
                    fw += x[j] * f[jx + i - j];
                }

                q[i] = fw;
            }

            jz += k;
        }

        // Chop off zero terms.
        if (z == 0.0)
        {
            jz--;
            q0 -= 24;
            while (iq[jz] == 0)
            {
                jz--;
                q0 -= 24;
            }
        }
        else
        {
            // Break z into 24-bit if necessary.
            z = Math.ScaleB(z, -q0);
            if (z >= Two24)
            {
                fw = (int)(Twon24 * z);
                iq[jz] = (int)(z - (Two24 * fw));
                jz++;
                q0 += 24;
                iq[jz] = (int)fw;
            }
            else
            {
                iq[jz] = (int)z;
            }
        }

        // Convert integer "bit" chunk to floating-point value.
        fw = Math.ScaleB(One, q0);
        for (int i = jz; i >= 0; i--)
        {
            q[i] = fw * iq[i];
            fw *= Twon24;
        }

        // Compute PIo2[0,...,jp]*q[jz,...,0].
        for (int i = jz; i >= 0; i--)
        {
            fw = 0.0;
            for (int k = 0; k <= jp && k <= jz - i; k++)
            {
                fw += PIo2[k] * q[i + k];
            }

            fq[jz - i] = fw;
        }

        // Compress fq[] into y[] (prec 2).
        fw = 0.0;
        for (int i = jz; i >= 0; i--)
        {
            fw += fq[i];
        }

        y0 = ih == 0 ? fw : -fw;
        fw = fq[0] - fw;
        for (int i = 1; i <= jz; i++)
        {
            fw += fq[i];
        }

        y1 = ih == 0 ? fw : -fw;
        return n & 7;
    }

    // ---- sin, cos, tan: __kernel_sin, __kernel_cos, __kernel_tan. ----

    private const double S1 = -1.66666666666666324348e-01; // 0xBFC55555, 0x55555549
    private const double S2 = 8.33333333332248946124e-03; // 0x3F811111, 0x1110F8A6
    private const double S3 = -1.98412698298579493134e-04; // 0xBF2A01A0, 0x19C161D5
    private const double S4 = 2.75573137070700676789e-06; // 0x3EC71DE3, 0x57B1FE7D
    private const double S5 = -2.50507602534068634195e-08; // 0xBE5AE5E6, 0x8A2B9CEB
    private const double S6 = 1.58969099521155010221e-10; // 0x3DE5D93A, 0x5ACFD57C

    private static double KernelSin(double x, double y, int iy)
    {
        int ix = High(x) & 0x7FFFFFFF;
        if (ix < 0x3E400000 && (int)x == 0)
        {
            // |x| < 2**-27.
            return x;
        }

        double z = x * x;
        double v = z * x;
        double r = Fma(z, Fma(z, Fma(z, Fma(z, S6, S5), S4), S3), S2);
        return iy == 0
            ? Fma(v, Fma(z, r, S1), x)
            : x - Fma(v, -S1, Fma(z, Fma(y, Half, -(r * v)), -y));
    }

    private const double C1 = 4.16666666666666019037e-02; // 0x3FA55555, 0x5555554C
    private const double C2 = -1.38888888888741095749e-03; // 0xBF56C16C, 0x16C15177
    private const double C3 = 2.48015872894767294178e-05; // 0x3EFA01A0, 0x19CB1590
    private const double C4 = -2.75573143513906633035e-07; // 0xBE927E4F, 0x809C52AD
    private const double C5 = 2.08757232129817482790e-09; // 0x3E21EE9E, 0xBDB4B1C4
    private const double C6 = -1.13596475577881948265e-11; // 0xBDA8FAE9, 0xBE8838D4

    private static double KernelCos(double x, double y)
    {
        int ix = High(x) & 0x7FFFFFFF;
        if (ix < 0x3E400000 && (int)x == 0)
        {
            // |x| < 2**-27.
            return One;
        }

        double z = x * x;
        double r = Fma(z, Fma(z, Fma(z, Fma(z, Fma(z, C6, C5), C4), C3), C2), C1) * z;
        double zrxy = Fma(z, r, -(x * y));
        if (ix < 0x3FD33333)
        {
            // |x| < 0.3.
            return One - ((0.5 * z) - zrxy);
        }

        // x > 0.78125: qx = 0.28125; otherwise qx = x/4.
        double qx = ix > 0x3FE90000 ? 0.28125 : Words(ix - 0x00200000, 0);
        double hz = (0.5 * z) - qx;
        double a = One - qx;
        return a - (hz - zrxy);
    }

    private static readonly double[] T =
    [
        3.33333333333334091986e-01, // 3FD55555, 55555563
        1.33333333333201242699e-01, // 3FC11111, 1110FE7A
        5.39682539762260521377e-02, // 3FABA1BA, 1BB341FE
        2.18694882948595424599e-02, // 3F9664F4, 8406D637
        8.86323982359930005737e-03, // 3F8226E3, E96E8493
        3.59207910759131235356e-03, // 3F6D6D22, C9560328
        1.45620945432529025516e-03, // 3F57DBC8, FEE08315
        5.88041240820264096874e-04, // 3F4344D8, F2F26501
        2.46463134818469906812e-04, // 3F3026F7, 1A8D1068
        7.81794442939557092300e-05, // 3F147E88, A03792A6
        7.14072491382608190305e-05, // 3F12B80F, 32F0A7E9
        -1.85586374855275456654e-05, // BEF375CB, DB605373
        2.59073051863633712884e-05, // 3EFB2A70, 74BF7AD4
    ];

    private const double Pio4 = 7.85398163397448278999e-01; // 3FE921FB, 54442D18
    private const double Pio4lo = 3.06161699786838301793e-17; // 3C81A626, 33145C07

    private static double KernelTan(double x, double y, int iy)
    {
        double z, r, v, w, s;
        int hx = High(x);
        int ix = hx & 0x7FFFFFFF;
        if (ix < 0x3E300000 && (int)x == 0)
        {
            // x < 2**-28.
            uint low = Low(x);
            if ((((uint)ix | low) | (uint)(iy + 1)) == 0)
            {
                return One / Math.Abs(x);
            }

            if (iy == 1)
            {
                return x;
            }

            // Compute -1 / (x+y) carefully.
            z = w = x + y;
            z = WithLow(z, 0);
            v = y - (z - x);
            double a = -One / w;
            double t = WithLow(a, 0);
            s = Fma(t, z, One);
            return Fma(a, Fma(t, v, s), t);
        }

        if (ix >= 0x3FE59428)
        {
            // |x| >= 0.6744.
            if (hx < 0)
            {
                x = -x;
                y = -y;
            }

            z = Pio4 - x;
            w = Pio4lo - y;
            x = z + w;
            y = 0.0;
        }

        z = x * x;
        w = z * z;

        // Break x^5*(T[1]+x^2*T[2]+...) into x^5(T[1]+x^4*T[3]+...+x^20*T[11]) + x^5(x^2*(T[2]+x^4*T[4]+...+x^22*[T12])).
        r = Fma(w, Fma(w, Fma(w, Fma(w, Fma(w, T[11], T[9]), T[7]), T[5]), T[3]), T[1]);
        double vpoly = Fma(w, Fma(w, Fma(w, Fma(w, Fma(w, T[12], T[10]), T[8]), T[6]), T[4]), T[2]);
        s = z * x;

        // r + v, with v = z*vpoly fused; then y + z*(s*(r+v) + y); then r += T[0]*s.
        r = Fma(z, Fma(s, Fma(z, vpoly, r), y), y);
        r = Fma(s, T[0], r);
        w = x + r;
        if (ix >= 0x3FE59428)
        {
            v = iy;
            return (1 - ((hx >> 30) & 2)) * Fma(-(x - (((w * w) / (w + v)) - r)), 2.0, v);
        }

        if (iy == 1)
        {
            return w;
        }

        // Compute -1.0 / (x+r) accurately.
        z = WithLow(w, 0);
        v = r - (z - x);
        double a2 = -1.0 / w;
        double t2 = WithLow(a2, 0);
        s = Fma(t2, z, 1.0);
        return Fma(a2, Fma(v, t2, s), t2);
    }

    /// <summary>fdlibm sin.</summary>
    public static double Sin(double x)
    {
        int ix = High(x) & 0x7FFFFFFF;
        if (ix <= 0x3FE921FB)
        {
            return KernelSin(x, 0.0, 0);
        }

        if (ix >= 0x7FF00000)
        {
            return x - x;
        }

        int n = RemPio2(x, out double y0, out double y1);
        return (n & 3) switch
        {
            0 => KernelSin(y0, y1, 1),
            1 => KernelCos(y0, y1),
            2 => -KernelSin(y0, y1, 1),
            _ => -KernelCos(y0, y1),
        };
    }

    /// <summary>fdlibm cos.</summary>
    public static double Cos(double x)
    {
        int ix = High(x) & 0x7FFFFFFF;
        if (ix <= 0x3FE921FB)
        {
            return KernelCos(x, 0.0);
        }

        if (ix >= 0x7FF00000)
        {
            return x - x;
        }

        int n = RemPio2(x, out double y0, out double y1);
        return (n & 3) switch
        {
            0 => KernelCos(y0, y1),
            1 => -KernelSin(y0, y1, 1),
            2 => -KernelCos(y0, y1),
            _ => KernelSin(y0, y1, 1),
        };
    }

    /// <summary>fdlibm tan.</summary>
    public static double Tan(double x)
    {
        int ix = High(x) & 0x7FFFFFFF;
        if (ix <= 0x3FE921FB)
        {
            return KernelTan(x, 0.0, 1);
        }

        if (ix >= 0x7FF00000)
        {
            return x - x;
        }

        int n = RemPio2(x, out double y0, out double y1);

        // 1: n even, -1: n odd.
        return KernelTan(y0, y1, 1 - ((n & 1) << 1));
    }

    // ---- atan, atan2. ----

    private static readonly double[] AtanHi =
    [
        4.63647609000806093515e-01, // atan(0.5)hi 0x3FDDAC67, 0x0561BB4F
        7.85398163397448278999e-01, // atan(1.0)hi 0x3FE921FB, 0x54442D18
        9.82793723247329054082e-01, // atan(1.5)hi 0x3FEF730B, 0xD281F69B
        1.57079632679489655800e+00, // atan(inf)hi 0x3FF921FB, 0x54442D18
    ];

    private static readonly double[] AtanLo =
    [
        2.26987774529616870924e-17, // atan(0.5)lo 0x3C7A2B7F, 0x222F65E2
        3.06161699786838301793e-17, // atan(1.0)lo 0x3C81A626, 0x33145C07
        1.39033110312309984516e-17, // atan(1.5)lo 0x3C700788, 0x7AF0CBBD
        6.12323399573676603587e-17, // atan(inf)lo 0x3C91A626, 0x33145C07
    ];

    private static readonly double[] AT =
    [
        3.33333333333329318027e-01, // 0x3FD55555, 0x5555550D
        -1.99999999998764832476e-01, // 0xBFC99999, 0x9998EBC4
        1.42857142725034663711e-01, // 0x3FC24924, 0x920083FF
        -1.11111104054623557880e-01, // 0xBFBC71C6, 0xFE231671
        9.09088713343650656196e-02, // 0x3FB745CD, 0xC54C206E
        -7.69187620504482999495e-02, // 0xBFB3B0F2, 0xAF749A6D
        6.66107313738753120669e-02, // 0x3FB10D66, 0xA0D03D51
        -5.83357013379057348645e-02, // 0xBFADDE2D, 0x52DEFD9A
        4.97687799461593236017e-02, // 0x3FA97B4B, 0x24760DEB
        -3.65315727442169155270e-02, // 0xBFA2B444, 0x2C6A6C2F
        1.62858201153657823623e-02, // 0x3F90AD3A, 0xE322DA11
    ];

    /// <summary>fdlibm atan.</summary>
    public static double Atan(double x)
    {
        int id;
        int hx = High(x);
        int ix = hx & 0x7FFFFFFF;
        if (ix >= 0x44100000)
        {
            // |x| >= 2^66.
            uint low = Low(x);
            if (ix > 0x7FF00000 || (ix == 0x7FF00000 && low != 0))
            {
                return x + x;
            }

            return hx > 0 ? AtanHi[3] + AtanLo[3] : -AtanHi[3] - AtanLo[3];
        }

        if (ix < 0x3FDC0000)
        {
            // |x| < 0.4375.
            if (ix < 0x3E400000 && Huge + x > One)
            {
                // |x| < 2^-27.
                return x;
            }

            id = -1;
        }
        else
        {
            x = Math.Abs(x);
            if (ix < 0x3FF30000)
            {
                // |x| < 1.1875.
                if (ix < 0x3FE60000)
                {
                    // 7/16 <= |x| < 11/16.
                    id = 0;
                    x = Fma(2.0, x, -One) / (2.0 + x);
                }
                else
                {
                    // 11/16 <= |x| < 19/16.
                    id = 1;
                    x = (x - One) / (x + One);
                }
            }
            else if (ix < 0x40038000)
            {
                // |x| < 2.4375.
                id = 2;
                x = (x - 1.5) / Fma(x, 1.5, One);
            }
            else
            {
                // 2.4375 <= |x| < 2^66.
                id = 3;
                x = -1.0 / x;
            }
        }

        // End of argument reduction.
        double z = x * x;
        double w = z * z;

        // Break the sum from i=0 to 10 of aT[i]z**(i+1) into odd and even polynomials.
        double s1Poly = Fma(w, Fma(w, Fma(w, Fma(w, Fma(w, AT[10], AT[8]), AT[6]), AT[4]), AT[2]), AT[0]);
        double s2 = w * Fma(w, Fma(w, Fma(w, Fma(w, AT[9], AT[7]), AT[5]), AT[3]), AT[1]);

        // x*(s1 + s2), with s1 = z*s1Poly fused into the sum.
        double xs = Fma(z, s1Poly, s2) * x;
        if (id < 0)
        {
            return x - xs;
        }

        z = AtanHi[id] - ((xs - AtanLo[id]) - x);
        return hx < 0 ? -z : z;
    }

    private const double PiO4 = 7.8539816339744827900E-01; // 0x3FE921FB, 0x54442D18
    private const double PiO2 = 1.5707963267948965580E+00; // 0x3FF921FB, 0x54442D18
    private const double Pi = 3.1415926535897931160E+00; // 0x400921FB, 0x54442D18
    private const double PiLo = 1.2246467991473531772E-16; // 0x3CA1A626, 0x33145C07

    /// <summary>fdlibm atan2.</summary>
    public static double Atan2(double y, double x)
    {
        double z;
        int hx = High(x);
        uint lx = Low(x);
        int ix = hx & 0x7FFFFFFF;
        int hy = High(y);
        uint ly = Low(y);
        int iy = hy & 0x7FFFFFFF;
        if (((uint)ix | ((lx | (0u - lx)) >> 31)) > 0x7FF00000 || ((uint)iy | ((ly | (0u - ly)) >> 31)) > 0x7FF00000)
        {
            // x or y is NaN.
            return x + y;
        }

        if (((uint)(hx - 0x3FF00000) | lx) == 0)
        {
            // x = 1.0.
            return Atan(y);
        }

        // 2*sign(x) + sign(y).
        int m = ((hy >> 31) & 1) | ((hx >> 30) & 2);

        // When y = 0.
        if (((uint)iy | ly) == 0)
        {
            // atan(+-0, +anything) = +-0; atan(+0, -anything) = pi; atan(-0, -anything) = -pi.
            return m switch
            {
                0 or 1 => y,
                2 => Pi + Tiny,
                _ => -Pi - Tiny,
            };
        }

        // When x = 0.
        if (((uint)ix | lx) == 0)
        {
            return hy < 0 ? -PiO2 - Tiny : PiO2 + Tiny;
        }

        // When x is INF.
        if (ix == 0x7FF00000)
        {
            if (iy == 0x7FF00000)
            {
                return m switch
                {
                    0 => PiO4 + Tiny,
                    1 => -PiO4 - Tiny,
                    2 => (3.0 * PiO4) + Tiny,
                    _ => (-3.0 * PiO4) - Tiny,
                };
            }

            return m switch
            {
                0 => Zero,
                1 => -Zero,
                2 => Pi + Tiny,
                _ => -Pi - Tiny,
            };
        }

        // When y is INF.
        if (iy == 0x7FF00000)
        {
            return hy < 0 ? -PiO2 - Tiny : PiO2 + Tiny;
        }

        // Compute y/x.
        int k = (iy - ix) >> 20;
        if (k > 60)
        {
            // |y/x| > 2**60.
            z = Fma(PiLo, 0.5, PiO2);
            m &= 1;
        }
        else if (hx < 0 && k < -60)
        {
            // 0 > |y|/x > -2**-60.
            z = 0.0;
        }
        else
        {
            // Safe to do y/x.
            z = Atan(Math.Abs(y / x));
        }

        return m switch
        {
            0 => z,
            1 => -z,
            2 => Pi - (z - PiLo),
            _ => (z - PiLo) - Pi,
        };
    }

    // ---- exp. ----

    private const double OThreshold = 7.09782712893383973096e+02; // 0x40862E42, 0xFEFA39EF
    private const double UThreshold = -7.45133219101941108420e+02; // 0xC0874910, 0xD52D3051
    private const double Ln2Hi = 6.93147180369123816490e-01; // 0x3FE62E42, 0xFEE00000
    private const double Ln2Lo = 1.90821492927058770002e-10; // 0x3DEA39EF, 0x35793C76
    private const double Invln2 = 1.44269504088896338700e+00; // 0x3FF71547, 0x652B82FE
    private const double P1 = 1.66666666666666019037e-01; // 0x3FC55555, 0x5555553E
    private const double P2 = -2.77777777770155933842e-03; // 0xBF66C16C, 0x16BEBD93
    private const double P3 = 6.61375632143793436117e-05; // 0x3F11566A, 0xAF25DE2C
    private const double P4 = -1.65339022054652515390e-06; // 0xBEBBBD41, 0xC5D26BF1
    private const double P5 = 4.13813679705723846039e-08; // 0x3E663769, 0x72BEA4D0
    private const double E = 2.718281828459045; // 0x4005BF0A, 0x8B145769
    private const double Twom1000 = 9.33263618503218878990e-302; // 2**-1000 = 0x01700000, 0
    private const double Two1023 = 8.988465674311579539e307; // 0x1p1023

    /// <summary>fdlibm exp (FreeBSD's, with V8's exp(1) = E).</summary>
    public static double Exp(double x)
    {
        double hi = 0.0, lo = 0.0;
        int k = 0;
        uint hx = (uint)High(x);
        int xsb = (int)((hx >> 31) & 1);
        hx &= 0x7FFFFFFF;

        // Filter out non-finite argument.
        if (hx >= 0x40862E42)
        {
            // |x| >= 709.78...
            if (hx >= 0x7FF00000)
            {
                uint lx = Low(x);
                if (((hx & 0xFFFFF) | lx) != 0)
                {
                    return x + x;
                }

                // exp(+-inf) = {inf, 0}.
                return xsb == 0 ? x : 0.0;
            }

            if (x > OThreshold)
            {
                return Huge * Huge;
            }

            if (x < UThreshold)
            {
                return Twom1000 * Twom1000;
            }
        }

        // Argument reduction.
        if (hx > 0x3FD62E42)
        {
            // |x| > 0.5 ln2.
            if (hx < 0x3FF0A2B2)
            {
                // And |x| < 1.5 ln2. V8 returns E for exp(1), which the general path gets wrong in the last bit.
                if (x == 1.0)
                {
                    return E;
                }

                hi = x - (xsb == 0 ? Ln2Hi : -Ln2Hi);
                lo = xsb == 0 ? Ln2Lo : -Ln2Lo;
                k = 1 - xsb - xsb;
            }
            else
            {
                k = (int)Fma(Invln2, x, xsb == 0 ? 0.5 : -0.5);
                double tk = k;

                // t*ln2HI is exact here.
                hi = Fma(-tk, Ln2Hi, x);
                lo = tk * Ln2Lo;
            }

            x = hi - lo;
        }
        else if (hx < 0x3E300000)
        {
            // |x| < 2**-28.
            if (Huge + x > One)
            {
                return One + x;
            }
        }
        else
        {
            k = 0;
        }

        // x is now in primary range.
        double t = x * x;
        double twopk = k >= -1021 ? Words(0x3FF00000 + (k << 20), 0) : Words(0x3FF00000 + ((k + 1000) << 20), 0);
        double c = Fma(-Fma(t, Fma(t, Fma(t, Fma(t, P5, P4), P3), P2), P1), t, x);
        if (k == 0)
        {
            return One - (((x * c) / (c - 2.0)) - x);
        }

        double y = One - ((lo - ((x * c) / (2.0 - c))) - hi);
        if (k >= -1021)
        {
            return k == 1024 ? y * 2.0 * Two1023 : y * twopk;
        }

        return y * twopk * Twom1000;
    }

    // ---- log, log10. ----

    private const double Lg1 = 6.666666666666735130e-01; // 3FE55555 55555593
    private const double Lg2 = 3.999999999940941908e-01; // 3FD99999 9997FA04
    private const double Lg3 = 2.857142874366239149e-01; // 3FD24924 94229359
    private const double Lg4 = 2.222219843214978396e-01; // 3FCC71C5 1D8E78AF
    private const double Lg5 = 1.818357216161805012e-01; // 3FC74664 96CB03DE
    private const double Lg6 = 1.531383769920937332e-01; // 3FC39A09 D078C69F
    private const double Lg7 = 1.479819860511658591e-01; // 3FC2F112 DF3E5244

    /// <summary>fdlibm log (FreeBSD's).</summary>
    public static double Log(double x)
    {
        double dk;
        int hx = High(x);
        uint lx = Low(x);
        int k = 0;
        if (hx < 0x00100000)
        {
            // x < 2**-1022.
            if (((uint)(hx & 0x7FFFFFFF) | lx) == 0)
            {
                // log(+-0) = -inf.
                return double.NegativeInfinity;
            }

            if (hx < 0)
            {
                // log(-#) = NaN.
                return double.NaN;
            }

            // Subnormal number, scale up x.
            k -= 54;
            x *= Two54;
            hx = High(x);
        }

        if (hx >= 0x7FF00000)
        {
            return x + x;
        }

        k += (hx >> 20) - 1023;
        hx &= 0x000FFFFF;
        int i = (hx + 0x95F64) & 0x100000;

        // Normalize x or x/2.
        x = WithHigh(x, hx | (i ^ 0x3FF00000));
        k += i >> 20;
        double f = x - 1.0;
        double r;
        if ((0x000FFFFF & (2 + hx)) < 3)
        {
            // -2**-20 <= f < 2**-20.
            if (f == Zero)
            {
                if (k == 0)
                {
                    return Zero;
                }

                dk = k;
                return Fma(dk, Ln2Hi, dk * Ln2Lo);
            }

            r = Fma(-f, 0.33333333333333333, 0.5) * (f * f);
            if (k == 0)
            {
                return f - r;
            }

            dk = k;
            return Fma(dk, Ln2Hi, -(Fma(-dk, Ln2Lo, r) - f));
        }

        double s = f / (2.0 + f);
        dk = k;
        double z = s * s;
        i = hx - 0x6147A;
        double w = z * z;
        int j = 0x6B851 - hx;
        double t2 = Fma(w, Fma(w, Fma(w, Lg7, Lg5), Lg3), Lg1) * z;
        i |= j;

        // t2 + t1, with t1 = w*(Lg2 + w*(Lg4 + w*Lg6)) fused into the sum.
        r = Fma(Fma(w, Fma(w, Lg6, Lg4), Lg2), w, t2);
        if (i > 0)
        {
            double hfsq = 0.5 * f * f;
            double sr = s * (hfsq + r);
            return k == 0
                ? f - (hfsq - sr)
                : Fma(dk, Ln2Hi, -((hfsq - Fma(dk, Ln2Lo, sr)) - f));
        }

        double sfr = s * (f - r);
        return k == 0
            ? f - sfr
            : Fma(dk, Ln2Hi, -(Fma(-dk, Ln2Lo, sfr) - f));
    }

    private const double Ivln10 = 4.34294481903251816668e-01; // 0x3FDBCB7B, 0x1526E50E
    private const double Log10_2hi = 3.01029995663611771306e-01; // 0x3FD34413, 0x509F6000
    private const double Log10_2lo = 3.69423907715893078616e-13; // 0x3D59FEF3, 0x11F12B36

    /// <summary>fdlibm log10.</summary>
    public static double Log10(double x)
    {
        int hx = High(x);
        uint lx = Low(x);
        int k = 0;
        if (hx < 0x00100000)
        {
            // x < 2**-1022.
            if (((uint)(hx & 0x7FFFFFFF) | lx) == 0)
            {
                // log(+-0) = -inf.
                return double.NegativeInfinity;
            }

            if (hx < 0)
            {
                // log(-#) = NaN.
                return double.NaN;
            }

            // Subnormal number, scale up x.
            k -= 54;
            x *= Two54;
            hx = High(x);
            lx = Low(x);
        }

        if (hx >= 0x7FF00000)
        {
            return x + x;
        }

        if (hx == 0x3FF00000 && lx == 0)
        {
            // log(1) = +0.
            return Zero;
        }

        k += (hx >> 20) - 1023;
        int i = (int)(((uint)k & 0x80000000) >> 31);
        hx = (hx & 0x000FFFFF) | ((0x3FF - i) << 20);
        double y = k + i;
        x = Words(hx, lx);
        double z = Fma(y, Log10_2lo, Log(x) * Ivln10);
        return Fma(y, Log10_2hi, z);
    }

    // ---- asin, acos. ----

    private const double Pio2Hi = 1.57079632679489655800e+00; // 0x3FF921FB, 0x54442D18
    private const double Pio2Lo = 6.12323399573676603587e-17; // 0x3C91A626, 0x33145C07
    private const double Pio4Hi = 7.85398163397448278999e-01; // 0x3FE921FB, 0x54442D18
    private const double PS0 = 1.66666666666666657415e-01; // 0x3FC55555, 0x55555555
    private const double PS1 = -3.25565818622400915405e-01; // 0xBFD4D612, 0x03EB6F7D
    private const double PS2 = 2.01212532134862925881e-01; // 0x3FC9C155, 0x0E884455
    private const double PS3 = -4.00555345006794114027e-02; // 0xBFA48228, 0xB5688F3B
    private const double PS4 = 7.91534994289814532176e-04; // 0x3F49EFE0, 0x7501B288
    private const double PS5 = 3.47933107596021167570e-05; // 0x3F023DE1, 0x0DFDF709
    private const double QS1 = -2.40339491173441421878e+00; // 0xC0033A27, 0x1C8A2D4B
    private const double QS2 = 2.02094576023350569471e+00; // 0x40002AE5, 0x9C598AC8
    private const double QS3 = -6.88283971605453293030e-01; // 0xBFE6066C, 0x1B8D0159
    private const double QS4 = 7.70381505559019352791e-02; // 0x3FB3B8C5, 0xB12E9282

    /// <summary>pS0 + t*(pS1 + ... + t*pS5), all fused: R(x^2)'s numerator without its leading t.</summary>
    private static double PPoly(double t) => Fma(t, Fma(t, Fma(t, Fma(t, Fma(t, PS5, PS4), PS3), PS2), PS1), PS0);

    /// <summary>qS1 + t*(qS2 + t*(qS3 + t*qS4)), all fused: the denominator without its leading 1 + t*.</summary>
    private static double QPoly(double t) => Fma(t, Fma(t, Fma(t, QS4, QS3), QS2), QS1);

    /// <summary>fdlibm asin.</summary>
    public static double Asin(double x)
    {
        double t = 0, w, p, q;
        int hx = High(x);
        int ix = hx & 0x7FFFFFFF;
        if (ix >= 0x3FF00000)
        {
            // |x| >= 1.
            uint lx = Low(x);
            if (((uint)(ix - 0x3FF00000) | lx) == 0)
            {
                // asin(1) = +-pi/2 with inexact.
                return Fma(x, Pio2Hi, x * Pio2Lo);
            }

            // asin(|x|>1) is NaN.
            return (x - x) / (x - x);
        }

        if (ix < 0x3FE00000)
        {
            // |x| < 0.5.
            if (ix < 0x3E400000)
            {
                // |x| < 2**-27: return x with inexact if x != 0.
                if (Huge + x > One)
                {
                    return x;
                }
            }
            else
            {
                t = x * x;
            }

            p = PPoly(t) * t;
            q = Fma(t, QPoly(t), One);
            w = p / q;
            return Fma(x, w, x);
        }

        // 1 > |x| >= 0.5.
        w = One - Math.Abs(x);
        t = w * 0.5;
        p = PPoly(t) * t;
        double s = Math.Sqrt(t);
        q = Fma(QPoly(t), t, One);
        double r = p / q;
        if (ix >= 0x3FEF3333)
        {
            // |x| > 0.975.
            t = Pio2Hi - Fma(2.0, Fma(s, r, s), -Pio2Lo);
        }
        else
        {
            w = WithLow(s, 0);
            double c = Fma(-w, w, t) / (s + w);
            p = Fma(s + s, r, -Fma(-c, 2.0, Pio2Lo));
            q = Fma(-w, 2.0, Pio4Hi);
            t = Pio4Hi - (p - q);
        }

        return hx > 0 ? t : -t;
    }

    /// <summary>fdlibm acos.</summary>
    public static double Acos(double x)
    {
        double z, p, q, r, w, s;
        int hx = High(x);
        int ix = hx & 0x7FFFFFFF;
        if (ix >= 0x3FF00000)
        {
            // |x| >= 1.
            uint lx = Low(x);
            if (((uint)(ix - 0x3FF00000) | lx) == 0)
            {
                // acos(1) = 0, acos(-1) = pi.
                return hx > 0 ? 0.0 : Pi + (2.0 * Pio2Lo);
            }

            // acos(|x|>1) is NaN.
            return (x - x) / (x - x);
        }

        if (ix < 0x3FE00000)
        {
            // |x| < 0.5.
            if (ix <= 0x3C600000)
            {
                // |x| < 2**-57.
                return Pio2Hi + Pio2Lo;
            }

            z = x * x;
            p = PPoly(z) * z;
            q = Fma(QPoly(z), z, One);
            r = p / q;
            return Pio2Hi - (x - Fma(-x, r, Pio2Lo));
        }

        if (hx < 0)
        {
            // x < -0.5.
            z = (One + x) * 0.5;
            p = PPoly(z) * z;
            s = Math.Sqrt(z);
            q = Fma(QPoly(z), z, One);
            r = p / q;
            w = Fma(r, s, -Pio2Lo);
            return Fma(-(s + w), 2.0, Pi);
        }

        // x > 0.5.
        z = (One - x) * 0.5;
        s = Math.Sqrt(z);
        double df = WithLow(s, 0);
        double c = Fma(-df, df, z) / (s + df);
        p = PPoly(z) * z;
        q = Fma(QPoly(z), z, One);
        r = p / q;
        w = Fma(s, r, c);
        return 2.0 * (df + w);
    }

    // ---- hypot: V8's MathHypot (src/builtins/math.tq). ----

    /// <summary>Math.hypot of two numbers as V8's builtin computes it: scaled by the largest, Kahan-summed.</summary>
    public static double Hypot(double a, double b)
    {
        bool oneArgIsNaN = false;
        double max = 0;
        double absA = 0, absB = 0;
        if (double.IsNaN(a))
        {
            oneArgIsNaN = true;
        }
        else
        {
            absA = Math.Abs(a);
            if (absA > max)
            {
                max = absA;
            }
        }

        if (double.IsNaN(b))
        {
            oneArgIsNaN = true;
        }
        else
        {
            absB = Math.Abs(b);
            if (absB > max)
            {
                max = absB;
            }
        }

        if (max == double.PositiveInfinity)
        {
            return double.PositiveInfinity;
        }

        if (oneArgIsNaN)
        {
            return double.NaN;
        }

        if (max == 0)
        {
            return 0;
        }

        double sum = 0, compensation = 0;
        KahanAdd(ref sum, ref compensation, absA / max);
        KahanAdd(ref sum, ref compensation, absB / max);
        return Math.Sqrt(sum) * max;
    }

    /// <summary>Math.hypot of three numbers as V8's builtin computes it: scaled by the largest, Kahan-summed.</summary>
    public static double Hypot(double a, double b, double c)
    {
        bool oneArgIsNaN = false;
        double max = 0;
        double absA = 0, absB = 0, absC = 0;
        if (double.IsNaN(a))
        {
            oneArgIsNaN = true;
        }
        else
        {
            absA = Math.Abs(a);
            if (absA > max)
            {
                max = absA;
            }
        }

        if (double.IsNaN(b))
        {
            oneArgIsNaN = true;
        }
        else
        {
            absB = Math.Abs(b);
            if (absB > max)
            {
                max = absB;
            }
        }

        if (double.IsNaN(c))
        {
            oneArgIsNaN = true;
        }
        else
        {
            absC = Math.Abs(c);
            if (absC > max)
            {
                max = absC;
            }
        }

        if (max == double.PositiveInfinity)
        {
            return double.PositiveInfinity;
        }

        if (oneArgIsNaN)
        {
            return double.NaN;
        }

        if (max == 0)
        {
            return 0;
        }

        double sum = 0, compensation = 0;
        KahanAdd(ref sum, ref compensation, absA / max);
        KahanAdd(ref sum, ref compensation, absB / max);
        KahanAdd(ref sum, ref compensation, absC / max);
        return Math.Sqrt(sum) * max;
    }

    private static void KahanAdd(ref double sum, ref double compensation, double n)
    {
        double summand = (n * n) - compensation;
        double preliminary = sum + summand;
        compensation = (preliminary - sum) - summand;
        sum = preliminary;
    }
}
