// Ported from V8 src/base/ieee754.cc (fdlibm, Copyright (C) 1993-2004 Sun Microsystems, via FreeBSD msun), through the
// C# port in src/PropGenerator.Nature/Js/Ieee754.cs; see NOTICE.md.
//
// Test only: the Math of the Node that dumped the goldens, for the engine's cross-check.
//
// The goldens were made by Node 26 as CachyOS builds it (GCC for x86-64-v3), whose fdlibm is compiled with fused
// multiply-adds: one rounding instead of two wherever GCC could fuse. ClearScript's V8 runs fdlibm as the C reads, so
// sin, cos, tan, atan, atan2, exp, log, log10, asin and acos differ from the goldens in the last bit for some inputs
// (pow, sqrt and hypot agree). One bit is enough to change a mesh's bits, so the cross-check would prove nothing.
// This script, loaded after shims.js and before engine.js, replaces those ten with fdlibm as that build computes it:
// the same code as the C# port (checked against a million V8 results per function), every fma() where the reference
// build has an FMA instruction. The app does not load it: it runs V8's own Math.
//
// fma(a, b, c) is a*b + c rounded once, emulated exactly: the product split into two doubles (Dekker), the sum of the
// three rounded once by rounding to odd first (Boldo and Melquiond, 2008), and exact BigInt arithmetic for the rare
// operands outside the range where that is proved.
;(function () {
    'use strict'

    const f64 = new Float64Array(1)
    const u32 = new Uint32Array(f64.buffer)
    const i32 = new Int32Array(f64.buffer)

    // ---- word access (fdlibm's EXTRACT_WORDS, GET_HIGH_WORD, SET_LOW_WORD...), little-endian ----

    const high = (x) => {
        f64[0] = x
        return i32[1]
    }
    const low = (x) => {
        f64[0] = x
        return u32[0]
    }
    const words = (hi, lo) => {
        i32[1] = hi
        u32[0] = lo
        return f64[0]
    }
    const withHigh = (x, hi) => words(hi, low(x))
    const withLow = (x, lo) => words(high(x), lo)

    /** x * 2^n, exact unless the result over- or underflows (fdlibm's scalbn as used here). */
    function scalbn(x, n) {
        while (n > 1023) {
            x *= 8.98846567431158e307 // 2^1023
            n -= 1023
        }
        while (n < -1022) {
            x *= 2.2250738585072014e-308 // 2^-1022
            n += 1022
        }
        return x * words((n + 1023) << 20, 0)
    }

    // ---- fma ----

    const SPLIT = 134217729 // 2^27 + 1
    const P1000 = 1.0715086071862673e301 // 2^1000
    const M950 = 1.0567887509362212e-286 // 2^-950
    const P995 = 3.3484643974570854e299 // 2^995

    function fma(a, b, c) {
        if (a === 0 || b === 0 || c === 0 || !Number.isFinite(a) || !Number.isFinite(b)) {
            return a * b + c
        }
        if (!Number.isFinite(c)) {
            return c
        }
        const p = a * b
        const ap = Math.abs(p)
        if (!(ap <= P1000 && ap >= M950 && Math.abs(a) < P995 && Math.abs(b) < P995 && Math.abs(c) <= P1000)) {
            return fmaExact(a, b, c)
        }

        // a*b = p + e exactly (Dekker).
        let t = SPLIT * a
        const ah = t - (t - a)
        const al = a - ah
        t = SPLIT * b
        const bh = t - (t - b)
        const bl = b - bh
        const e = ((ah * bh - p) + ah * bl + al * bh) + al * bl

        // c + p = th + tl exactly (TwoSum).
        const th = c + p
        let bb = th - c
        const tl = (c - (th - bb)) + (p - bb)

        // v = tl + e rounded to odd.
        let v = tl + e
        bb = v - tl
        const err = (tl - (v - bb)) + (e - bb)
        if (err !== 0) {
            f64[0] = v
            if ((u32[0] & 1) === 0) {
                // Inexact and even: step to the neighbour on the side of the exact sum, which is odd.
                if ((v > 0) === (err > 0)) {
                    if (u32[0] === 0xffffffff) {
                        u32[0] = 0
                        i32[1] += 1
                    } else {
                        u32[0] += 1
                    }
                } else if (u32[0] === 0) {
                    u32[0] = 0xffffffff
                    i32[1] -= 1
                } else {
                    u32[0] -= 1
                }
                v = f64[0]
            }
        }
        return th + v
    }

    /** A finite double as m * 2^e with m a BigInt. */
    function decompose(x) {
        f64[0] = x
        const hi = i32[1]
        const lo = u32[0]
        const exponent = (hi >>> 20) & 0x7ff
        let m = (BigInt(hi & 0xfffff) << 32n) | BigInt(lo)
        let e
        if (exponent === 0) {
            e = -1074
        } else {
            m |= 1n << 52n
            e = exponent - 1075
        }
        return {m: hi < 0 ? -m : m, e}
    }

    /** a*b + c rounded once, in exact arithmetic. */
    function fmaExact(a, b, c) {
        const A = decompose(a)
        const B = decompose(b)
        const C = decompose(c)
        const pm = A.m * B.m
        const pe = A.e + B.e
        const e = Math.min(pe, C.e)
        const sum = (pm << BigInt(pe - e)) + (C.m << BigInt(C.e - e))
        if (sum === 0n) {
            return 0
        }
        const negative = sum < 0n
        const mag = negative ? -sum : sum
        let shift = mag.toString(2).length - 53
        if (shift < -1074 - e) {
            shift = -1074 - e
        }
        let result
        if (shift > 0) {
            let q = mag >> BigInt(shift)
            const r = mag - (q << BigInt(shift))
            const half = 1n << BigInt(shift - 1)
            if (r > half || (r === half && (q & 1n) === 1n)) {
                q += 1n
            }
            result = scalbnExact(Number(q), e + shift)
        } else {
            result = scalbnExact(Number(mag), e)
        }
        return negative ? -result : result
    }

    /** x * 2^n where the result is representable (or overflows): never rounded twice. */
    function scalbnExact(x, n) {
        if (n >= -1022) {
            return scalbn(x, n)
        }
        // A subnormal result: an integer times 2^-1074 in one multiplication, exact as the result is representable.
        return scalbn(x, n + 1074) * 5e-324
    }

    // ---- argument reduction: __ieee754_rem_pio2 and __kernel_rem_pio2 ----

    const TWO_OVER_PI = [
        0xa2f983, 0x6e4e44, 0x1529fc, 0x2757d1, 0xf534dd, 0xc0db62, 0x95993c, 0x439041, 0xfe5163, 0xabdebb, 0xc561b7,
        0x246e3a, 0x424dd2, 0xe00649, 0x2eea09, 0xd1921c, 0xfe1deb, 0x1cb129, 0xa73ee8, 0x8235f5, 0x2ebb44, 0x84e99c,
        0x7026b4, 0x5f7e41, 0x3991d6, 0x398353, 0x39f49c, 0x845f8b, 0xbdf928, 0x3b1ff8, 0x97ffde, 0x05980f, 0xef2f11,
        0x8b5a0a, 0x6d1f6d, 0x367ecf, 0x27cb09, 0xb74f46, 0x3f669e, 0x5fea2d, 0x7527ba, 0xc7ebe5, 0xf17b3d, 0x0739f7,
        0x8a5292, 0xea6bfb, 0x5fb11f, 0x8d5d08, 0x560330, 0x46fc7b, 0x6babf0, 0xcfbc20, 0x9af436, 0x1da9e3, 0x91615e,
        0xe61b08, 0x659985, 0x5f14a0, 0x68408d, 0xffd880, 0x4d7327, 0x310606, 0x1556ca, 0x73a8c9, 0x60e27b, 0xc08c6b,
    ]
    const NPIO2_HW = [
        0x3ff921fb, 0x400921fb, 0x4012d97c, 0x401921fb, 0x401f6a7a, 0x4022d97c, 0x4025fdbb, 0x402921fb, 0x402c463a,
        0x402f6a7a, 0x4031475c, 0x4032d97c, 0x40346b9c, 0x4035fdbb, 0x40378fdb, 0x403921fb, 0x403ab41b, 0x403c463a,
        0x403dd85a, 0x403f6a7a, 0x40407e4c, 0x4041475c, 0x4042106c, 0x4042d97c, 0x4043a28c, 0x40446b9c, 0x404534ac,
        0x4045fdbb, 0x4046c6cb, 0x40478fdb, 0x404858eb, 0x404921fb,
    ]
    const PIO2 = [
        1.57079625129699707031e0, 7.54978941586159635335e-8, 5.39030252995776476554e-15, 3.28200341580791294123e-22,
        1.27065575308067607349e-29, 1.22933308981111328932e-36, 2.73370053816464559624e-44, 2.16741683877804819444e-51,
    ]
    const ZERO = 0.0
    const HALF = 0.5
    const ONE = 1.0
    const HUGE = 1.0e300
    const TINY = 1.0e-300
    const TWO24 = 1.6777216e7
    const TWON24 = 5.9604644775390625e-8
    const TWO54 = 1.8014398509481984e16
    const INVPIO2 = 6.36619772367581382433e-1
    const PIO2_1 = 1.57079632673412561417e0
    const PIO2_1T = 6.07710050650619224932e-11
    const PIO2_2 = 6.0771005063039659766e-11
    const PIO2_2T = 2.02226624879595063154e-21
    const PIO2_3 = 2.0222662487111664558e-21
    const PIO2_3T = 8.47842766036889956997e-32

    // The two halves of a reduced argument (fdlibm's y[0], y[1]).
    let Y0 = 0
    let Y1 = 0

    /** x - n*pi/2 as Y0 + Y1; returns n. */
    function remPio2(x) {
        let z = 0
        const hx = high(x)
        const ix = hx & 0x7fffffff
        if (ix <= 0x3fe921fb) {
            Y0 = x
            Y1 = 0
            return 0
        }
        if (ix < 0x4002d97c) {
            if (hx > 0) {
                z = x - PIO2_1
                if (ix !== 0x3ff921fb) {
                    Y0 = z - PIO2_1T
                    Y1 = (z - Y0) - PIO2_1T
                } else {
                    z -= PIO2_2
                    Y0 = z - PIO2_2T
                    Y1 = (z - Y0) - PIO2_2T
                }
                return 1
            }
            z = x + PIO2_1
            if (ix !== 0x3ff921fb) {
                Y0 = z + PIO2_1T
                Y1 = (z - Y0) + PIO2_1T
            } else {
                z += PIO2_2
                Y0 = z + PIO2_2T
                Y1 = (z - Y0) + PIO2_2T
            }
            return -1
        }
        if (ix <= 0x413921fb) {
            let t = Math.abs(x)
            const n = fma(t, INVPIO2, HALF) | 0
            const fn = n
            let r = fma(-fn, PIO2_1, t)
            let w = fn * PIO2_1T
            if (n < 32 && ix !== NPIO2_HW[n - 1]) {
                Y0 = r - w
            } else {
                const j = ix >> 20
                Y0 = r - w
                let i = j - ((high(Y0) >> 20) & 0x7ff)
                if (i > 16) {
                    t = r
                    r = fma(-fn, PIO2_2, t)
                    w = fma(fn, PIO2_2T, -fma(-fn, PIO2_2, t - r))
                    Y0 = r - w
                    i = j - ((high(Y0) >> 20) & 0x7ff)
                    if (i > 49) {
                        t = r
                        r = fma(-fn, PIO2_3, t)
                        w = fma(fn, PIO2_3T, -fma(-fn, PIO2_3, t - r))
                        Y0 = r - w
                    }
                }
            }
            Y1 = (r - Y0) - w
            if (hx < 0) {
                Y0 = -Y0
                Y1 = -Y1
                return -n
            }
            return n
        }
        if (ix >= 0x7ff00000) {
            Y0 = Y1 = x - x
            return 0
        }
        z = withLow(z, low(x))
        const e0 = (ix >> 20) - 1046
        z = withHigh(z, ix - (e0 << 20))
        const tx = [0, 0, 0]
        for (let i = 0; i < 2; i++) {
            tx[i] = z | 0
            z = (z - tx[i]) * TWO24
        }
        tx[2] = z
        let nx = 3
        while (tx[nx - 1] === ZERO) {
            nx--
        }
        const nn = kernelRemPio2(tx, e0, nx)
        if (hx < 0) {
            Y0 = -Y0
            Y1 = -Y1
            return -nn
        }
        return nn
    }

    /** __kernel_rem_pio2 with prec = 2; sets Y0, Y1. */
    function kernelRemPio2(x, e0, nx) {
        const iq = new Array(20).fill(0)
        const f = new Array(20).fill(0)
        const fq = new Array(20).fill(0)
        const q = new Array(20).fill(0)
        const jk = 4
        const jp = jk
        const jx = nx - 1
        let jv = ((e0 - 3) / 24) | 0
        if (jv < 0) {
            jv = 0
        }
        let q0 = e0 - 24 * (jv + 1)
        let j = jv - jx
        const m = jx + jk
        for (let i = 0; i <= m; i++, j++) {
            f[i] = j < 0 ? ZERO : TWO_OVER_PI[j]
        }
        let fw
        for (let i = 0; i <= jk; i++) {
            fw = 0.0
            for (j = 0; j <= jx; j++) {
                fw += x[j] * f[jx + i - j]
            }
            q[i] = fw
        }
        let jz = jk
        let n
        let ih
        let z
        for (;;) {
            let i
            z = q[jz]
            for (i = 0, j = jz; j > 0; i++, j--) {
                fw = (TWON24 * z) | 0
                iq[i] = (z - TWO24 * fw) | 0
                z = q[j - 1] + fw
            }
            z = scalbn(z, q0)
            z -= 8.0 * Math.floor(z * 0.125)
            n = z | 0
            z -= n
            ih = 0
            if (q0 > 0) {
                i = iq[jz - 1] >> (24 - q0)
                n += i
                iq[jz - 1] -= i << (24 - q0)
                ih = iq[jz - 1] >> (23 - q0)
            } else if (q0 === 0) {
                ih = iq[jz - 1] >> 23
            } else if (z >= 0.5) {
                ih = 2
            }
            if (ih > 0) {
                n++
                let carry = 0
                for (i = 0; i < jz; i++) {
                    j = iq[i]
                    if (carry === 0) {
                        if (j !== 0) {
                            carry = 1
                            iq[i] = 0x1000000 - j
                        }
                    } else {
                        iq[i] = 0xffffff - j
                    }
                }
                if (q0 > 0) {
                    if (q0 === 1) {
                        iq[jz - 1] &= 0x7fffff
                    } else if (q0 === 2) {
                        iq[jz - 1] &= 0x3fffff
                    }
                }
                if (ih === 2) {
                    z = ONE - z
                    if (carry !== 0) {
                        z -= scalbn(ONE, q0)
                    }
                }
            }
            if (z !== ZERO) {
                break
            }
            j = 0
            for (i = jz - 1; i >= jk; i--) {
                j |= iq[i]
            }
            if (j !== 0) {
                break
            }
            let k = 1
            while (jk >= k && iq[jk - k] === 0) {
                k++
            }
            for (i = jz + 1; i <= jz + k; i++) {
                f[jx + i] = TWO_OVER_PI[jv + i]
                fw = 0.0
                for (j = 0; j <= jx; j++) {
                    fw += x[j] * f[jx + i - j]
                }
                q[i] = fw
            }
            jz += k
        }
        if (z === 0.0) {
            jz--
            q0 -= 24
            while (iq[jz] === 0) {
                jz--
                q0 -= 24
            }
        } else {
            z = scalbn(z, -q0)
            if (z >= TWO24) {
                fw = (TWON24 * z) | 0
                iq[jz] = (z - TWO24 * fw) | 0
                jz++
                q0 += 24
                iq[jz] = fw | 0
            } else {
                iq[jz] = z | 0
            }
        }
        fw = scalbn(ONE, q0)
        for (let i = jz; i >= 0; i--) {
            q[i] = fw * iq[i]
            fw *= TWON24
        }
        for (let i = jz; i >= 0; i--) {
            fw = 0.0
            for (let k = 0; k <= jp && k <= jz - i; k++) {
                fw += PIO2[k] * q[i + k]
            }
            fq[jz - i] = fw
        }
        fw = 0.0
        for (let i = jz; i >= 0; i--) {
            fw += fq[i]
        }
        Y0 = ih === 0 ? fw : -fw
        fw = fq[0] - fw
        for (let i = 1; i <= jz; i++) {
            fw += fq[i]
        }
        Y1 = ih === 0 ? fw : -fw
        return n & 7
    }

    // ---- sin, cos, tan ----

    const S1 = -1.66666666666666324348e-1
    const S2 = 8.33333333332248946124e-3
    const S3 = -1.98412698298579493134e-4
    const S4 = 2.75573137070700676789e-6
    const S5 = -2.50507602534068634195e-8
    const S6 = 1.58969099521155010221e-10

    function kernelSin(x, y, iy) {
        const ix = high(x) & 0x7fffffff
        if (ix < 0x3e400000 && (x | 0) === 0) {
            return x
        }
        const z = x * x
        const v = z * x
        const r = fma(z, fma(z, fma(z, fma(z, S6, S5), S4), S3), S2)
        return iy === 0 ? fma(v, fma(z, r, S1), x) : x - fma(v, -S1, fma(z, fma(y, HALF, -(r * v)), -y))
    }

    const C1 = 4.16666666666666019037e-2
    const C2 = -1.38888888888741095749e-3
    const C3 = 2.48015872894767294178e-5
    const C4 = -2.75573143513906633035e-7
    const C5 = 2.0875723212981748279e-9
    const C6 = -1.13596475577881948265e-11

    function kernelCos(x, y) {
        const ix = high(x) & 0x7fffffff
        if (ix < 0x3e400000 && (x | 0) === 0) {
            return ONE
        }
        const z = x * x
        const r = fma(z, fma(z, fma(z, fma(z, fma(z, C6, C5), C4), C3), C2), C1) * z
        const zrxy = fma(z, r, -(x * y))
        if (ix < 0x3fd33333) {
            return ONE - (0.5 * z - zrxy)
        }
        const qx = ix > 0x3fe90000 ? 0.28125 : words(ix - 0x00200000, 0)
        const hz = 0.5 * z - qx
        const a = ONE - qx
        return a - (hz - zrxy)
    }

    const T = [
        3.33333333333334091986e-1, 1.33333333333201242699e-1, 5.39682539762260521377e-2, 2.18694882948595424599e-2,
        8.86323982359930005737e-3, 3.59207910759131235356e-3, 1.45620945432529025516e-3, 5.88041240820264096874e-4,
        2.46463134818469906812e-4, 7.817944429395570923e-5, 7.14072491382608190305e-5, -1.85586374855275456654e-5,
        2.59073051863633712884e-5,
    ]
    const PIO4 = 7.85398163397448278999e-1
    const PIO4LO = 3.06161699786838301793e-17

    function kernelTan(x, y, iy) {
        let z, r, v, w, s
        const hx = high(x)
        const ix = hx & 0x7fffffff
        if (ix < 0x3e300000 && (x | 0) === 0) {
            const lo = low(x)
            if ((ix | lo | (iy + 1)) === 0) {
                return ONE / Math.abs(x)
            }
            if (iy === 1) {
                return x
            }
            z = w = x + y
            z = withLow(z, 0)
            v = y - (z - x)
            const a = -ONE / w
            const t = withLow(a, 0)
            s = fma(t, z, ONE)
            return fma(a, fma(t, v, s), t)
        }
        if (ix >= 0x3fe59428) {
            if (hx < 0) {
                x = -x
                y = -y
            }
            z = PIO4 - x
            w = PIO4LO - y
            x = z + w
            y = 0.0
        }
        z = x * x
        w = z * z
        r = fma(w, fma(w, fma(w, fma(w, fma(w, T[11], T[9]), T[7]), T[5]), T[3]), T[1])
        const vpoly = fma(w, fma(w, fma(w, fma(w, fma(w, T[12], T[10]), T[8]), T[6]), T[4]), T[2])
        s = z * x
        r = fma(z, fma(s, fma(z, vpoly, r), y), y)
        r = fma(s, T[0], r)
        w = x + r
        if (ix >= 0x3fe59428) {
            v = iy
            return (1 - ((hx >> 30) & 2)) * fma(-(x - ((w * w) / (w + v) - r)), 2.0, v)
        }
        if (iy === 1) {
            return w
        }
        z = withLow(w, 0)
        v = r - (z - x)
        const a2 = -1.0 / w
        const t2 = withLow(a2, 0)
        s = fma(t2, z, 1.0)
        return fma(a2, fma(v, t2, s), t2)
    }

    function sin(x) {
        x = +x
        const ix = high(x) & 0x7fffffff
        if (ix <= 0x3fe921fb) {
            return kernelSin(x, 0.0, 0)
        }
        if (ix >= 0x7ff00000) {
            return x - x
        }
        const n = remPio2(x)
        const y0 = Y0
        const y1 = Y1
        switch (n & 3) {
            case 0:
                return kernelSin(y0, y1, 1)
            case 1:
                return kernelCos(y0, y1)
            case 2:
                return -kernelSin(y0, y1, 1)
            default:
                return -kernelCos(y0, y1)
        }
    }

    function cos(x) {
        x = +x
        const ix = high(x) & 0x7fffffff
        if (ix <= 0x3fe921fb) {
            return kernelCos(x, 0.0)
        }
        if (ix >= 0x7ff00000) {
            return x - x
        }
        const n = remPio2(x)
        const y0 = Y0
        const y1 = Y1
        switch (n & 3) {
            case 0:
                return kernelCos(y0, y1)
            case 1:
                return -kernelSin(y0, y1, 1)
            case 2:
                return -kernelCos(y0, y1)
            default:
                return kernelSin(y0, y1, 1)
        }
    }

    function tan(x) {
        x = +x
        const ix = high(x) & 0x7fffffff
        if (ix <= 0x3fe921fb) {
            return kernelTan(x, 0.0, 1)
        }
        if (ix >= 0x7ff00000) {
            return x - x
        }
        const n = remPio2(x)
        return kernelTan(Y0, Y1, 1 - ((n & 1) << 1))
    }

    // ---- atan, atan2 ----

    const ATAN_HI = [4.63647609000806093515e-1, 7.85398163397448278999e-1, 9.82793723247329054082e-1, 1.570796326794896558e0]
    const ATAN_LO = [2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17]
    const AT = [
        3.33333333333329318027e-1, -1.99999999998764832476e-1, 1.42857142725034663711e-1, -1.1111110405462355788e-1,
        9.09088713343650656196e-2, -7.69187620504482999495e-2, 6.66107313738753120669e-2, -5.83357013379057348645e-2,
        4.97687799461593236017e-2, -3.6531572744216915527e-2, 1.62858201153657823623e-2,
    ]

    function atan(x) {
        x = +x
        let id
        const hx = high(x)
        const ix = hx & 0x7fffffff
        if (ix >= 0x44100000) {
            const lo = low(x)
            if (ix > 0x7ff00000 || (ix === 0x7ff00000 && lo !== 0)) {
                return x + x
            }
            return hx > 0 ? ATAN_HI[3] + ATAN_LO[3] : -ATAN_HI[3] - ATAN_LO[3]
        }
        if (ix < 0x3fdc0000) {
            if (ix < 0x3e400000 && HUGE + x > ONE) {
                return x
            }
            id = -1
        } else {
            x = Math.abs(x)
            if (ix < 0x3ff30000) {
                if (ix < 0x3fe60000) {
                    id = 0
                    x = fma(2.0, x, -ONE) / (2.0 + x)
                } else {
                    id = 1
                    x = (x - ONE) / (x + ONE)
                }
            } else if (ix < 0x40038000) {
                id = 2
                x = (x - 1.5) / fma(x, 1.5, ONE)
            } else {
                id = 3
                x = -1.0 / x
            }
        }
        let z = x * x
        const w = z * z
        const s1Poly = fma(w, fma(w, fma(w, fma(w, fma(w, AT[10], AT[8]), AT[6]), AT[4]), AT[2]), AT[0])
        const s2 = w * fma(w, fma(w, fma(w, fma(w, AT[9], AT[7]), AT[5]), AT[3]), AT[1])
        const xs = fma(z, s1Poly, s2) * x
        if (id < 0) {
            return x - xs
        }
        z = ATAN_HI[id] - ((xs - ATAN_LO[id]) - x)
        return hx < 0 ? -z : z
    }

    const PI_O4 = 7.85398163397448279e-1
    const PI_O2 = 1.570796326794896558e0
    const PI = 3.141592653589793116e0
    const PI_LO = 1.2246467991473531772e-16

    function atan2(y, x) {
        y = +y
        x = +x
        let z
        const hx = high(x)
        const lx = low(x)
        const ix = hx & 0x7fffffff
        const hy = high(y)
        const ly = low(y)
        const iy = hy & 0x7fffffff
        if ((ix | ((lx | -lx) >>> 31)) >>> 0 > 0x7ff00000 || (iy | ((ly | -ly) >>> 31)) >>> 0 > 0x7ff00000) {
            return x + y
        }
        if (((hx - 0x3ff00000) | lx) === 0) {
            return atan(y)
        }
        let m = ((hy >> 31) & 1) | ((hx >> 30) & 2)
        if ((iy | ly) === 0) {
            switch (m) {
                case 0:
                case 1:
                    return y
                case 2:
                    return PI + TINY
                default:
                    return -PI - TINY
            }
        }
        if ((ix | lx) === 0) {
            return hy < 0 ? -PI_O2 - TINY : PI_O2 + TINY
        }
        if (ix === 0x7ff00000) {
            if (iy === 0x7ff00000) {
                switch (m) {
                    case 0:
                        return PI_O4 + TINY
                    case 1:
                        return -PI_O4 - TINY
                    case 2:
                        return 3.0 * PI_O4 + TINY
                    default:
                        return -3.0 * PI_O4 - TINY
                }
            }
            switch (m) {
                case 0:
                    return ZERO
                case 1:
                    return -ZERO
                case 2:
                    return PI + TINY
                default:
                    return -PI - TINY
            }
        }
        if (iy === 0x7ff00000) {
            return hy < 0 ? -PI_O2 - TINY : PI_O2 + TINY
        }
        const k = (iy - ix) >> 20
        if (k > 60) {
            z = fma(PI_LO, 0.5, PI_O2)
            m &= 1
        } else if (hx < 0 && k < -60) {
            z = 0.0
        } else {
            z = atan(Math.abs(y / x))
        }
        switch (m) {
            case 0:
                return z
            case 1:
                return -z
            case 2:
                return PI - (z - PI_LO)
            default:
                return (z - PI_LO) - PI
        }
    }

    // ---- exp ----

    const O_THRESHOLD = 7.09782712893383973096e2
    const U_THRESHOLD = -7.4513321910194110842e2
    const LN2_HI = 6.9314718036912381649e-1
    const LN2_LO = 1.90821492927058770002e-10
    const INVLN2 = 1.442695040888963387e0
    const P1 = 1.66666666666666019037e-1
    const P2 = -2.77777777770155933842e-3
    const P3 = 6.61375632143793436117e-5
    const P4 = -1.6533902205465251539e-6
    const P5 = 4.13813679705723846039e-8
    const E = 2.718281828459045
    const TWOM1000 = 9.3326361850321887899e-302
    const TWO1023 = 8.988465674311579539e307

    function exp(x) {
        x = +x
        let hi = 0.0
        let lo = 0.0
        let k = 0
        let hx = high(x)
        const xsb = (hx >>> 31) & 1
        hx &= 0x7fffffff
        if (hx >= 0x40862e42) {
            if (hx >= 0x7ff00000) {
                const lx = low(x)
                if (((hx & 0xfffff) | lx) !== 0) {
                    return x + x
                }
                return xsb === 0 ? x : 0.0
            }
            if (x > O_THRESHOLD) {
                return HUGE * HUGE
            }
            if (x < U_THRESHOLD) {
                return TWOM1000 * TWOM1000
            }
        }
        if (hx > 0x3fd62e42) {
            if (hx < 0x3ff0a2b2) {
                if (x === 1.0) {
                    return E
                }
                hi = x - (xsb === 0 ? LN2_HI : -LN2_HI)
                lo = xsb === 0 ? LN2_LO : -LN2_LO
                k = 1 - xsb - xsb
            } else {
                k = fma(INVLN2, x, xsb === 0 ? 0.5 : -0.5) | 0
                const tk = k
                hi = fma(-tk, LN2_HI, x)
                lo = tk * LN2_LO
            }
            x = hi - lo
        } else if (hx < 0x3e300000) {
            if (HUGE + x > ONE) {
                return ONE + x
            }
        } else {
            k = 0
        }
        const t = x * x
        const twopk = k >= -1021 ? words(0x3ff00000 + (k << 20), 0) : words(0x3ff00000 + ((k + 1000) << 20), 0)
        const c = fma(-fma(t, fma(t, fma(t, fma(t, P5, P4), P3), P2), P1), t, x)
        if (k === 0) {
            return ONE - ((x * c) / (c - 2.0) - x)
        }
        const y = ONE - ((lo - (x * c) / (2.0 - c)) - hi)
        if (k >= -1021) {
            return k === 1024 ? y * 2.0 * TWO1023 : y * twopk
        }
        return y * twopk * TWOM1000
    }

    // ---- log, log10 ----

    const LG1 = 6.66666666666673513e-1
    const LG2 = 3.999999999940941908e-1
    const LG3 = 2.857142874366239149e-1
    const LG4 = 2.222219843214978396e-1
    const LG5 = 1.818357216161805012e-1
    const LG6 = 1.531383769920937332e-1
    const LG7 = 1.479819860511658591e-1

    function log(x) {
        x = +x
        let dk
        let hx = high(x)
        const lx = low(x)
        let k = 0
        if (hx < 0x00100000) {
            if (((hx & 0x7fffffff) | lx) === 0) {
                return -Infinity
            }
            if (hx < 0) {
                return NaN
            }
            k -= 54
            x *= TWO54
            hx = high(x)
        }
        if (hx >= 0x7ff00000) {
            return x + x
        }
        k += (hx >> 20) - 1023
        hx &= 0x000fffff
        let i = (hx + 0x95f64) & 0x100000
        x = withHigh(x, hx | (i ^ 0x3ff00000))
        k += i >> 20
        const f = x - 1.0
        let r
        if ((0x000fffff & (2 + hx)) < 3) {
            if (f === ZERO) {
                if (k === 0) {
                    return ZERO
                }
                dk = k
                return fma(dk, LN2_HI, dk * LN2_LO)
            }
            r = fma(-f, 0.33333333333333333, 0.5) * (f * f)
            if (k === 0) {
                return f - r
            }
            dk = k
            return fma(dk, LN2_HI, -(fma(-dk, LN2_LO, r) - f))
        }
        const s = f / (2.0 + f)
        dk = k
        const z = s * s
        i = hx - 0x6147a
        const w = z * z
        const j = 0x6b851 - hx
        const t2 = fma(w, fma(w, fma(w, LG7, LG5), LG3), LG1) * z
        i |= j
        r = fma(fma(w, fma(w, LG6, LG4), LG2), w, t2)
        if (i > 0) {
            const hfsq = 0.5 * f * f
            const sr = s * (hfsq + r)
            return k === 0 ? f - (hfsq - sr) : fma(dk, LN2_HI, -((hfsq - fma(dk, LN2_LO, sr)) - f))
        }
        const sfr = s * (f - r)
        return k === 0 ? f - sfr : fma(dk, LN2_HI, -(fma(-dk, LN2_LO, sfr) - f))
    }

    const IVLN10 = 4.34294481903251816668e-1
    const LOG10_2HI = 3.01029995663611771306e-1
    const LOG10_2LO = 3.69423907715893078616e-13

    function log10(x) {
        x = +x
        let hx = high(x)
        let lx = low(x)
        let k = 0
        if (hx < 0x00100000) {
            if (((hx & 0x7fffffff) | lx) === 0) {
                return -Infinity
            }
            if (hx < 0) {
                return NaN
            }
            k -= 54
            x *= TWO54
            hx = high(x)
            lx = low(x)
        }
        if (hx >= 0x7ff00000) {
            return x + x
        }
        if (hx === 0x3ff00000 && lx === 0) {
            return ZERO
        }
        k += (hx >> 20) - 1023
        const i = (k >>> 31) & 1
        hx = (hx & 0x000fffff) | ((0x3ff - i) << 20)
        const y = k + i
        x = words(hx, lx)
        const z = fma(y, LOG10_2LO, log(x) * IVLN10)
        return fma(y, LOG10_2HI, z)
    }

    // ---- asin, acos ----

    const PIO2_HI = 1.570796326794896558e0
    const PIO2_LO = 6.12323399573676603587e-17
    const PIO4_HI = 7.85398163397448278999e-1
    const PS0 = 1.66666666666666657415e-1
    const PS1 = -3.25565818622400915405e-1
    const PS2 = 2.01212532134862925881e-1
    const PS3 = -4.00555345006794114027e-2
    const PS4 = 7.91534994289814532176e-4
    const PS5 = 3.4793310759602116757e-5
    const QS1 = -2.40339491173441421878e0
    const QS2 = 2.02094576023350569471e0
    const QS3 = -6.8828397160545329303e-1
    const QS4 = 7.70381505559019352791e-2

    const pPoly = (t) => fma(t, fma(t, fma(t, fma(t, fma(t, PS5, PS4), PS3), PS2), PS1), PS0)
    const qPoly = (t) => fma(t, fma(t, fma(t, QS4, QS3), QS2), QS1)

    function asin(x) {
        x = +x
        let t = 0
        let w, p, q
        const hx = high(x)
        const ix = hx & 0x7fffffff
        if (ix >= 0x3ff00000) {
            const lx = low(x)
            if (((ix - 0x3ff00000) | lx) === 0) {
                return fma(x, PIO2_HI, x * PIO2_LO)
            }
            return (x - x) / (x - x)
        }
        if (ix < 0x3fe00000) {
            if (ix < 0x3e400000) {
                if (HUGE + x > ONE) {
                    return x
                }
            } else {
                t = x * x
            }
            p = pPoly(t) * t
            q = fma(t, qPoly(t), ONE)
            w = p / q
            return fma(x, w, x)
        }
        w = ONE - Math.abs(x)
        t = w * 0.5
        p = pPoly(t) * t
        const s = Math.sqrt(t)
        q = fma(qPoly(t), t, ONE)
        const r = p / q
        if (ix >= 0x3fef3333) {
            t = PIO2_HI - fma(2.0, fma(s, r, s), -PIO2_LO)
        } else {
            w = withLow(s, 0)
            const c = fma(-w, w, t) / (s + w)
            p = fma(s + s, r, -fma(-c, 2.0, PIO2_LO))
            q = fma(-w, 2.0, PIO4_HI)
            t = PIO4_HI - (p - q)
        }
        return hx > 0 ? t : -t
    }

    function acos(x) {
        x = +x
        let z, p, q, r, w, s
        const hx = high(x)
        const ix = hx & 0x7fffffff
        if (ix >= 0x3ff00000) {
            const lx = low(x)
            if (((ix - 0x3ff00000) | lx) === 0) {
                return hx > 0 ? 0.0 : PI + 2.0 * PIO2_LO
            }
            return (x - x) / (x - x)
        }
        if (ix < 0x3fe00000) {
            if (ix <= 0x3c600000) {
                return PIO2_HI + PIO2_LO
            }
            z = x * x
            p = pPoly(z) * z
            q = fma(qPoly(z), z, ONE)
            r = p / q
            return PIO2_HI - (x - fma(-x, r, PIO2_LO))
        }
        if (hx < 0) {
            z = (ONE + x) * 0.5
            p = pPoly(z) * z
            s = Math.sqrt(z)
            q = fma(qPoly(z), z, ONE)
            r = p / q
            w = fma(r, s, -PIO2_LO)
            return fma(-(s + w), 2.0, PI)
        }
        z = (ONE - x) * 0.5
        s = Math.sqrt(z)
        const df = withLow(s, 0)
        const c = fma(-df, df, z) / (s + df)
        p = pPoly(z) * z
        q = fma(qPoly(z), z, ONE)
        r = p / q
        w = fma(s, r, c)
        return 2.0 * (df + w)
    }

    const replaced = {sin, cos, tan, atan, atan2, exp, log, log10, asin, acos}
    for (const [name, fn] of Object.entries(replaced)) {
        Object.defineProperty(Math, name, {value: fn, writable: true, enumerable: false, configurable: true})
    }
    Object.defineProperty(globalThis, '__nodeMath', {value: {fma, ...replaced}, enumerable: false})
})()
