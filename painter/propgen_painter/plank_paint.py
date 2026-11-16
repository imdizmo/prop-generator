"""Paint one hand-painted plank piece into tone layers (layers.Layers, rows bottom to top).

Our recipe, built up with the user over many passes: straight grain with soft creases and lit lips, the odd
gentle kink, a big lit zone along the plank, soft shadow from the gaps, a chunky bevel with a crisp crest, a
wobbly outline with worn bites and chipped corners, short forked cracks at ends and edges, brushwork dabs
along the grain, and colour that drifts in small soft patches (a bit redder, a bit greyer).

The piece is painted as tone, not colour: layers.compose_v5 (or the app's shader) turns it into colour.

    paint_piece(layers, x0, x1, a, b, end_bot, end_top, seed=1, **opts)

Options (defaults are the recipe): basemul (multiplier on the base colour), creases (count), crease_strength (lo,
hi), dominant (count of extra-strong creases), kink_p, lit (lit-zone strength), crest (bevel crest strength), brush
(dab amplitude), brush_n (dabs per 1000 rows), knot (bool), contrast (only decides where the cracks cut, see crack();
the composer applies the real contrast to the marks layer), carve (count of broad carved facets), carve_strength,
carve_open (0-1: how much of each facet's outline fades away instead of closing into a loop), gap_scale (scales how
far the outline sits in: smaller gaps).

Smaller paints (the fast preview). Every position and size in here is in full-size (1024 px atlas) pixels, and stays
so: scale=f paints only every f-th full-size pixel (layers is 1/f of the size), on the lattice given by lattice=(ox,
oy): preview column i is full-size column f*i + ox, row j is f*j + oy. Every random draw is made exactly as at full
size, in the same order and count (per-row and per-column noise is drawn and smoothed at full length, then sampled),
so the preview has the same marks in the same places. What a full-size texel can hold but a preview texel can't (a
crease 2 px wide, the crisp crest of a bevel) is spread over the preview texel the way averaging the f x f full-size
texels round it would: per-row and per-column arrays are averaged over that box, the narrow strokes are widened by the
box's spread (keeping their area), and the creases and the edge profile are sampled at the box's offsets and
averaged. At scale=1 every step is the original one, so the full-size paint doesn't change by a bit.
"""
import numpy as np, random

from .layers import PLANK, SEAM


def box_taps(f):
    """A box f full-size texels wide centred on a texel, as (offset, weight): [-f/2 .. f/2], half weight at both
    ends. f is 1, 2 or 4; f=1 is the texel itself."""
    if f == 1:
        return [(0, 1.0)]
    h = f // 2
    return [(o, (0.5 if abs(o) == h else 1.0) / f) for o in range(-h, h + 1)]


def box_var(f):
    """The spread (variance, in full-size pixels squared) of that box: 0, 0.5, 1.5 for f = 1, 2, 4."""
    return sum(w * o * o for o, w in box_taps(f))


def lattice_span(lo, hi, f, off=0):
    """The preview indices i whose full-size texel f*i + off lies in lo..hi (inclusive), as (i0, i1)."""
    return -(-(lo - off) // f), (hi - off) // f


def _down(v, idx, f):
    """A full-length 1D array (per row or per column) at the preview's texels idx (indices into v), averaged over
    the box round each one. At f=1 it is v itself."""
    if f == 1:
        return v
    h = f // 2
    p = np.pad(v, h, mode="edge")
    return sum(w * p[idx + h + o] for o, w in box_taps(f))


def _wide(w, var):
    """A stroke exp(-(d/w)^2) seen through a box of spread var: (wider w, amplitude that keeps its area)."""
    if var == 0:
        return w, 1.0
    w2 = np.sqrt(w * w + 2 * var)
    return w2, w / w2


def _smooth1d(v, k, mode="wrap"):
    ker = np.hanning(k); ker /= ker.sum()
    return np.convolve(np.pad(v, k, mode=mode), ker, mode="same")[k:-k]


def _blur2d(m, s):
    if s < 0.8:     # under a texel (preview sizes): 3 taps with the same spread s^2, which a sampled Gaussian loses
        r = 1; k = np.array([s * s / 2, 1 - s * s, s * s / 2])
    else:
        r = max(1, int(3 * s)); k = np.exp(-0.5 * (np.arange(-r, r + 1) / s) ** 2); k /= k.sum()
    t = np.apply_along_axis(lambda c: np.convolve(np.pad(c, r, mode="edge"), k, mode="valid"), 0, m)
    return np.apply_along_axis(lambda c: np.convolve(np.pad(c, r, mode="edge"), k, mode="valid"), 1, t)


def paint_piece(layers, x0, x1, a, b, end_bot, end_top, seed=1, basemul=1.0, creases=7,
                crease_strength=(0.7, 1.6), dominant=0, kink_p=0.35, lit=0.09, crest=0.22, brush=0.03, brush_n=290,
                knot=False, one_sided=True, end_style="irregular", side_wobble=1.5, edge_line=0.12,
                edge_light=1.0, meander=0.0, broken_edge=False, contrast=1.0, carve=0,
                carve_strength=1.0, carve_open=0.0, gap_scale=1.0, scale=1, lattice=(0, 0)):
    n, W = b - a + 1, x1 - x0 + 1
    rs = np.random.default_rng(seed); rng = random.Random(seed); gr = random.Random(seed * 7 + 3)
    F, (ox, oy) = scale, lattice
    i0, i1 = lattice_span(x0, x1, F, ox); j0, j1 = lattice_span(a, b, F, oy)
    if i1 < i0 or j1 < j0:
        return
    ci = np.arange(i0, i1 + 1) * F + ox - x0         # the painted texels, as indices along the full-size piece
    ri = np.arange(j0, j1 + 1) * F + oy - a
    X = (ci + x0)[None, :].astype(np.float32)
    Y = (ri + a)[:, None].astype(np.float32)
    taps, var = box_taps(F), box_var(F)
    at_rows, at_cols = (lambda v: _down(v, ri, F)), (lambda v: _down(v, ci, F))   # per-row / per-column arrays

    def sub(lo, hi, idx):    # the painted texels inside full-size indices lo..hi-1
        return slice(lo, hi) if F == 1 else slice(int(np.searchsorted(idx, lo)), int(np.searchsorted(idx, hi)))
    sm = _smooth1d
    bl = lambda m, s: _blur2d(m, s / F)
    # outline: wobbly sides, irregular joint ends, bites, chipped and rounded corners
    BIG = 1e4
    wl = side_wobble * (1 + np.clip(sm(rs.normal(0, 1, n), 121) * 2.5, -1, 1))
    wr = side_wobble * (1 + np.clip(sm(rs.normal(0, 1, n), 121) * 2.5, -1, 1))
    if meander:
        ml = meander * np.sin(np.arange(n) / 160.0 + seed % 7) * np.sin(np.arange(n) / 420.0 + seed % 5)
        mr = meander * np.sin(np.arange(n) / 180.0 + seed % 11) * np.sin(np.arange(n) / 380.0 + seed % 3)
        wl = np.clip(wl + ml, 0, None); wr = np.clip(wr + mr, 0, None)
    wl, wr = wl * gap_scale, wr * gap_scale     # gap_scale: how far the outline sits in from the piece's box
    el = X - (x0 + at_rows(wl)[:, None]); er = (x1 - at_rows(wr)[:, None]) - X
    def endw():
        raw = rs.normal(0, 1, W)                       # drawn as before, so everything after keeps its randomness
        if end_style != "irregular":
            return at_cols(4 + 5 * np.clip(sm(raw, 41, "edge") * 3, -1, 1))[None, :]
        prof = np.full(W, 2.5)
        x_ = 0
        while x_ < W:                                  # flat stretches with an occasional small step
            L_ = int(abs(raw[x_ % W]) * 40 + 30)
            prof[x_:x_ + L_] += (raw[(x_ + 7) % W] > 0.9) * 2.5
            x_ += L_
        nx = int((raw[3] * 0.5 + 0.5) % 1 * (W - 40)) + 20          # a notch
        prof += 5 * np.clip(1 - np.abs(np.arange(W) - nx) / 9.0, 0, 1)
        return at_cols(sm(prof, 5, "edge"))[None, :]
    eb = ((Y - a) - gap_scale * endw()) if end_bot else np.full((len(ri), len(ci)), BIG)
    et = ((b - Y) - gap_scale * endw()) if end_top else np.full((len(ri), len(ci)), BIG)

    def bite(ex, cx, cy, r, depth):
        return ex - depth * np.clip(1 - np.sqrt((X - cx) ** 2 + (Y - cy) ** 2) / r, 0, 1) ** 0.7
    if end_bot:
        eb = bite(eb, x0 + rng.uniform(10, 30), a + 4, rng.uniform(16, 24), 8)
    if end_top:
        et = bite(et, x1 - rng.uniform(10, 30), b - 4, rng.uniform(16, 24), 8)
    el = bite(el, x0, a + rng.uniform(0.25, 0.6) * n, rng.uniform(18, 28), 6)
    er = bite(er, x1, a + rng.uniform(0.15, 0.45) * n, rng.uniform(18, 28), 6)
    def outline(el, er, eb, et):
        side, endd = np.minimum(el, er), np.minimum(eb, et)
        R = 14
        e = np.minimum(side, endd)
        for ex in (el, er):
            for en in (eb, et):
                m = (ex < R) & (en < R)
                e = np.where(m, np.minimum(e, R - np.sqrt((R - np.clip(ex, 0, R)) ** 2 + (R - np.clip(en, 0, R)) ** 2)), e)
        return side, endd, e
    side, endd, e = outline(el, er, eb, et)
    # grain: straight creases, slight drift, the odd gentle kink; soft value steps across each crease
    rows = np.arange(n)
    val = np.zeros((len(ri), len(ci)), dtype=np.float32)
    kx, ky = x0 + rng.uniform(0.3, 0.7) * (x1 - x0), a + rng.uniform(0.3, 0.7) * n
    xs = sorted(gr.uniform(x0 + 22, x1 - 22) for _ in range(creases))
    strong = set(gr.sample(range(creases), min(dominant, creases)))
    for i, xs0 in enumerate(xs):
        x = np.empty(n); cur = xs0; r_ = 0
        while r_ < n:
            if gr.random() < kink_p:
                L = gr.randint(25, 60); s = gr.choice((-1, 1)) * gr.uniform(0.12, 0.25)
            else:
                L = gr.randint(160, 420); s = gr.uniform(-0.02, 0.02)
            for k in range(L):
                if r_ >= n: break
                x[r_] = cur; cur += s; r_ += 1
        x = sm(np.clip(x, x0 + 12, x1 - 12), 9, "edge")
        if knot:   # grain bends round the knot
            x = x + 16 * np.exp(-((rows + a - ky) / 80.0) ** 2) * np.sign(x - kx)
        y0 = gr.uniform(-0.2, 0.4) * n; y1 = y0 + gr.uniform(0.45, 1.0) * n
        if i in strong:
            y0, y1 = -0.1 * n, 1.1 * n
        fade = np.clip(np.minimum(rows - y0, y1 - rows) / 140.0, 0, 1)
        if y0 < 0.05 * n:
            fade[: n // 2] = np.maximum(fade[: n // 2], np.clip(rows[: n // 2] / 40.0, 0.6, 1))   # reaches the lower end
        if y1 > 0.95 * n:
            fade[n // 2:] = np.maximum(fade[n // 2:], 0.6)                                       # reaches the upper end
        st = gr.uniform(1.7, 2.1) if i in strong else gr.uniform(*crease_strength)
        for end_ok, row_i, earr in ((end_bot, 0, "b"), (end_top, n - 1, "t")):
            if end_ok and fade[row_i] > 0.3:                    # a small notch where the crease meets the cut
                xi = int(np.clip(x[row_i] - x0, 0, W - 1))
                notch = at_cols(3.0 * np.clip(1 - np.abs(np.arange(W) - xi) / 4.0, 0, 1))[None, :]
                if earr == "b": eb = eb - notch
                else: et = et - notch
        x, fade, step = at_rows(x), at_rows(fade), gr.uniform(-0.05, 0.05)
        for o, w in taps:      # across the crease: the box's offsets, averaged (one offset, weight 1, at full size)
            dx = X + o - x[:, None]
            core = np.exp(-(dx / (2.2 * np.sqrt(st))) ** 2)
            lipm = np.exp(-((dx - 3.2) / 1.8) ** 2)
            across = 1 / (1 + np.exp(-dx / 3.0))
            val += w * fade[:, None] * (st * (-0.11 * core + 0.05 * np.clip(lipm - core, 0, 1)) + step * across)
    side, endd, e = outline(el, er, eb, et)     # again, now that the creases have notched the ends
    if knot:   # a soft dish, lit on its right, with a gentle ring
        kr = np.sqrt(((X - kx) / 14.0) ** 2 + ((Y - ky) / 24.0) ** 2)
        val += np.where(kr < 1.6, -0.15 * np.exp(-(kr / 0.7) ** 2) + 0.06 * np.exp(-((kr - 1.15) / 0.25) ** 2)
                        + 0.05 * np.clip((X - kx) / 14, -1, 1) * np.exp(-(kr / 0.8) ** 2), 0)
    cr = random.Random(seed * 31 + 9)
    cv = np.zeros((len(ri), len(ci)), dtype=np.float32)
    for _ in range(carve):   # broad carved facets: flat scoops and raised planes, rims lit toward the top left
        cx, cy2 = cr.uniform(x0 + 20, x1 - 20), cr.uniform(a, b)
        L, Wd, ang, sgn = cr.uniform(110, 260), cr.uniform(25, 0.35 * W), cr.gauss(0, 0.12), cr.choice((-1, -1, 1))
        wob = cr.uniform(0, 6)
        def plate(dx, dy):
            al = (Y - cy2 + dy) * np.cos(ang) + (X - cx + dx) * np.sin(ang)
            ac = (X - cx + dx) * np.cos(ang) - (Y - cy2 + dy) * np.sin(ang)
            r0_ = np.sqrt((al / L) ** 2 + (ac / Wd) ** 2)
            r = r0_ + 0.06 * np.sin(np.arctan2(al / L, ac / Wd) * 3 + wob)
            if not var:
                return 1 / (1 + np.exp((r - 1) / 0.02))
            # the rim through the box: a soft step of spread s^2 pi^2/3 gains the box's spread, measured along r
            g2 = ((al / L ** 2) ** 2 + (ac / Wd ** 2) ** 2) / np.maximum(r0_, 1e-3) ** 2
            return 1 / (1 + np.exp((r - 1) / np.sqrt(0.02 ** 2 + 3 * var * g2 / np.pi ** 2)))
        p0 = plate(0, 0)
        near = np.clip(p0 - plate(-3, 3), 0, 1)       # the rim on the side facing the light (left, up the rows)
        far = np.clip(p0 - plate(3, -3), 0, 1)
        g = carve_strength
        if carve_open:   # only part of the outline is cut: the rest fades into the plank, so no closed loops
            th = np.arctan2((Y - cy2) * np.cos(ang) + (X - cx) * np.sin(ang), ((X - cx) * np.cos(ang) - (Y - cy2) * np.sin(ang)) * L / Wd)
            win = np.clip(np.cos(th - cr.uniform(-np.pi, np.pi)), 0, 1) ** 1.5
            g = carve_strength * (1 - carve_open * (1 - win))
        if sgn > 0:      # raised: lit near rim, dark far rim
            cv += g * (0.05 * p0 + 0.24 * near - 0.15 * far)
        else:            # scooped: shadow under the near rim, light catches the far rim
            cv += g * (-0.05 * p0 - 0.20 * near + 0.18 * far)
    if F == 1:
        fib = sm(rs.normal(0, 1, W), 5)[None, :] * (0.6 + 0.4 * np.sin(Y / 140.0 + sm(rs.normal(0, 1, W), 15)[None, :] * 6))
        val += 0.035 * fib / (np.abs(fib).max() + 1e-6)
    else:      # the same fibres averaged over each texel's box, scaled by the full-size paint's largest fibre
        fa = sm(rs.normal(0, 1, W), 5); ph = sm(rs.normal(0, 1, W), 15) * 6
        fib = (0.6 * at_cols(fa)[None, :] + 0.4 * (np.sin(Y / 140.0) * at_cols(fa * np.cos(ph))[None, :]
                                               + np.cos(Y / 140.0) * at_cols(fa * np.sin(ph))[None, :]))
        lo, hi = a / 140.0 + ph, b / 140.0 + ph       # the sine's range down each full-size column
        peak = np.where(np.pi / 2 + 2 * np.pi * np.ceil((lo - np.pi / 2) / (2 * np.pi)) <= hi, 1.0,
                        np.maximum(np.sin(lo), np.sin(hi)))
        val += 0.035 * fib / ((np.abs(fa) * (0.6 + 0.4 * peak)).max() + 1e-6)
    cy = a + rng.uniform(0.35, 0.65) * n                    # one big lit zone along the plank
    val += lit * np.exp(-((Y - cy) / (0.33 * n)) ** 2) - lit / 3
    for i in range(int(brush_n * n / 1000)):                 # brushwork: bigger soft dabs along the grain
        cx, cy2 = rs.uniform(x0, x1), rs.uniform(a, b)
        L, Wd = rs.uniform(30, 80), rs.uniform(4, 9)
        amp = rs.normal(0, brush)
        r0, r1 = max(int(cy2 - a - L), 0), min(int(cy2 - a + L), n)
        c0, c1 = max(int(cx - x0 - 3 * Wd), 0), min(int(cx - x0 + 3 * Wd), W)
        if r0 >= r1 or c0 >= c1: continue
        ang = rs.normal(0, 0.05)
        rw, cw = sub(r0, r1, ri), sub(c0, c1, ci)
        if rw.start >= rw.stop or cw.start >= cw.stop: continue
        (L, kl), (Wd, kw) = _wide(L, var), _wide(Wd, var)
        dxs, dys = X[:, cw] - cx, Y[rw] - cy2
        along = dys * np.cos(ang) + dxs * np.sin(ang); acr = dxs * np.cos(ang) - dys * np.sin(ang)
        val[rw, cw] += amp * kl * kw * np.exp(-(along / L) ** 2 - (acr / Wd) ** 2)
    hue = np.zeros((len(ri), len(ci)), dtype=np.float32)                # colour drift: many small soft patches, not strips
    for i in range(int(14 * n / 1000) + 4):
        cx, cy2 = rs.uniform(x0, x1), rs.uniform(a, b)
        hue += rs.uniform(-1, 1) * np.exp(-((X - cx) / rs.uniform(25, 55)) ** 2 - ((Y - cy2) / rs.uniform(80, 220)) ** 2)
    # the value is marks * contrast + rest: contrast scales the surface marks only, the carving keeps its strength
    marks = bl(val, 1.1); rest = bl(cv, 1.0); hue = bl(hue, 2.0)
    del val
    bw = at_rows(14 * (0.5 + 1.0 * np.clip(sm(rs.random(n), 151), 0, 1)))[:, None]
    gate = 1.0
    if one_sided:   # the edges facing the top-left light get the lit bevel; the far edges only darken gently
        lit_e0 = np.minimum(el, et); far_e0 = np.minimum(er, eb)
        if broken_edge:    # the light on the edge comes and goes along its length
            g = np.sin(np.arange(n) / 37.0 + seed % 13) + 0.7 * np.sin(np.arange(n) / 91.0 + seed % 17)
            gate = at_rows(np.clip(g * 0.9 + 0.2, 0, 1))[:, None]
    # the profile across the edge, at the box's offsets along it (all the outline's distances move together)
    e0 = e
    for o, w in taps:
        e = e0 + o if o else e0
        rest -= w * 0.10 * np.exp(-np.clip(e, 0, None) / 12.0)        # soft shadow from the gaps
        if one_sided:
            lit_e, far_e = (lit_e0 + o, far_e0 + o) if o else (lit_e0, far_e0)
            bev_l = np.sin(np.pi * np.clip(lit_e / bw, 0, 1)) * (lit_e < bw)
            bev_f = np.clip(1 - far_e / bw, 0, 1) * (far_e < bw)
            rest += w * edge_light * gate * (0.15 * bev_l * (e > 0) + crest * np.exp(-((lit_e - bw * 0.35) / 1.4) ** 2) * (e > 0) * (lit_e < bw))
            rest -= w * 0.07 * bev_f * (e > 0)
        else:
            bev = np.sin(np.pi * np.clip(e / bw, 0, 1)) * (e < bw)
            rest += w * 0.15 * bev * (e > 0)
            rest += w * crest * np.exp(-((e - bw * 0.35) / 1.4) ** 2) * (e > 0)
        rest -= w * edge_line * np.exp(-np.clip(e, 0, None) / 1.5) * (e > 0)
    e = e0

    def crack(x, y, dx, dy, L, wid, fork):
        cx, cy3 = x, y
        for s in range(int(L)):
            cx += dx + rs.normal(0, 0.3); cy3 += dy + rs.normal(0, 0.3)
            ww = wid * (1 - s / L) ** 0.8 + 0.8
            r0, r1 = max(int(cy3 - a - 12), 0), min(int(cy3 - a + 12), n)
            c0, c1 = max(int(cx - x0 - 12), 0), min(int(cx - x0 + 12), W)
            if r0 >= r1 or c0 >= c1: break
            rw, cw = sub(r0, r1, ri), sub(c0, c1, ci)
            if rw.start < rw.stop and cw.start < cw.stop:
                stamp(rw, cw, cx, cy3, ww)
            if fork and s == int(L * 0.45):
                crack(cx, cy3, dx * 0.6 + 0.45 * rng.choice((-1, 1)), dy, L * 0.5, wid * 0.65, False)

    def stamp(rw, cw, cx, cy3, ww):     # one step of a crack; rw, cw: the rows and columns it reaches
        dd = np.sqrt((X[:, cw] - cx) ** 2 + (Y[rw] - cy3) ** 2)
        core = np.exp(-(dd / ww) ** 2)
        # v5 did v = min(v, -0.17 * core + v * (1 - core)) where core > 0.05. That is linear in the marks, so it
        # is split into marks *= (1 - core) and rest = rest * (1 - core) - 0.17 * core, applied where it darkens
        # the value at this piece's contrast (v5's 0.65). Fallback at another contrast: the crack keeps the same
        # footprint and blend, but the min() isn't redone, so where v sits near -0.17 the crack can come out a
        # touch lighter (never darker) than painting again at that contrast would.
        mk, rt = marks[rw, cw], rest[rw, cw]
        v = mk * contrast + rt
        cut = (core > 0.05) & (-0.17 * core + v * (1 - core) < v)
        marks[rw, cw] = np.where(cut, mk * (1 - core), mk)
        rest[rw, cw] = np.where(cut, rt * (1 - core) - 0.17 * core, rt)
        dl = np.sqrt((X[:, cw] - cx - 3.0) ** 2 + (Y[rw] - cy3) ** 2)
        rest[rw, cw] += 0.03 * np.exp(-(dl / (ww + 0.8)) ** 2) * (core < 0.3)
    # damage: each piece its own plan, from clean to four marks of different kinds, places and sizes
    dr = random.Random(seed * 13 + 5)
    ends = [e_ for e_, ok in (("bot", end_bot), ("top", end_top)) if ok]
    kinds = ["edge", "check", "notch", "pair"] + (["end_fork"] * 2 if ends else [])
    for _ in range(dr.choice((0, 1, 1, 2, 2, 2, 3, 4))):
        kind = dr.choice(kinds)
        if kind in ("end_fork", "pair", "check") and ends:
            which = dr.choice(ends)
            ys, dy = (a + 1, 1.0) if which == "bot" else (b - 1, -1.0)
        elif kind in ("end_fork", "pair", "check"):
            kind = "edge"
        if kind == "end_fork":
            crack(x0 + dr.uniform(0.2, 0.8) * (x1 - x0), ys, dr.uniform(-0.25, 0.25), dy, dr.uniform(70, 140), dr.uniform(3.8, 5.4), True)
        elif kind == "check":      # a long thin check along the grain
            crack(x0 + dr.uniform(0.2, 0.8) * (x1 - x0), ys, dr.uniform(-0.06, 0.06), dy, dr.uniform(150, 280), dr.uniform(2.2, 3.0), False)
        elif kind == "pair":       # two short parallel splits
            cx0 = x0 + dr.uniform(0.25, 0.65) * (x1 - x0)
            for off in (0, dr.uniform(14, 26)):
                crack(cx0 + off, ys, dr.uniform(-0.1, 0.1), dy, dr.uniform(40, 75), dr.uniform(3.0, 4.2), False)
        elif kind == "edge":       # a crack in from one side
            left = dr.random() < 0.5
            yy = a + dr.uniform(0.12, 0.88) * n
            crack(x0 + 1 if left else x1 - 1, yy, (1.0 if left else -1.0), dr.uniform(-0.45, 0.45),
                  dr.uniform(45, 100), dr.uniform(3.6, 5.0), dr.random() < 0.6)
        else:                      # a tiny notch
            left = dr.random() < 0.5
            yy = a + dr.uniform(0.1, 0.9) * n
            crack(x0 + 1 if left else x1 - 1, yy, (1.0 if left else -1.0), dr.uniform(-0.3, 0.3), dr.uniform(15, 30), dr.uniform(3.0, 4.0), False)
    # colour (tints, drift, the gap colour) is the composer's job now; the piece hands over its tone
    win = (slice(j0, j1 + 1), slice(i0, i1 + 1))
    layers.marks[win] = marks
    layers.rest[win] = rest
    layers.drift[win] = hue
    layers.region[win] = np.where(e <= 0, SEAM, PLANK)
    layers.basemul[win] = basemul


def pieces_of(px, x0, x1, thresh=0.12):
    """The pieces of one board between its end joints: [(a, b, end_bot, end_top)], longest first."""
    h = px.shape[0]
    lum = px[:, (x0 + x1) // 2, :3] @ np.array([0.3, 0.55, 0.15])
    dark = lum < thresh
    runs, y = [], 0
    while y < h:
        if dark[y]:
            s = y
            while y < h and dark[y]: y += 1
            runs.append((s, y - 1))
        y += 1
    cuts = [-1] + [v for r in runs for v in r] + [h]
    out = [(cuts[i] + 1, cuts[i + 1] - 1) for i in range(0, len(cuts) - 1, 2)]
    return sorted([(a_, b_, a_ > 0, b_ < h - 1) for a_, b_ in out], key=lambda p: -(p[1] - p[0]))
