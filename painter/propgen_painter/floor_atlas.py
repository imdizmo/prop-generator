"""Paint the floor-tile atlas: one 1024 x 1024 image holding the tile's top and its sides, in our hand-painted recipe
(plank_paint.py). Four wide planks run along the tile; the top repeats in both directions.

paint_floor_atlas paints tone layers (layers.py); paint_floor_atlas_rgb composes them in v5's colours.

    python3 -m propgen_painter.floor_atlas <out.png> [--seed=1] [--carve_strength=1] [--carve_open=0] [--gap_scale=1]

Layout, in rows from the bottom of the image (textured_tile.py --atlas reads the same numbers):
    0-63     the long side of plank 1 (the tile's -x face)
    64-127   the long side of plank 4 (the +x face)
    128-223  the cut ends of the four planks (the +-y faces), lined up with the planks above
    224-1023 the top (and bottom): 4 planks of 256 px, 800 rows along the tile

Sizes 512 and 256 (the fast preview while a slider moves) are the same painting at a half or a quarter of the
texels: every number above and every size in the recipe stays in full-size pixels, and preview texel i sits on
full-size texel f*i (f = 1024 / size), so the layout scales exactly and the top still turns about texel 0. The random
draws are the full-size ones (plank_paint.py says how), so the preview shows the same marks in the same places.

The plank runs paint in WORKERS processes, started on the first paint and kept (shutdown_workers stops them). Python
starts them by importing the calling script again, so a script that paints must keep its work under
if __name__ == "__main__".
"""
import atexit
import ctypes
import os
import random
import select
import signal
import threading
from concurrent.futures import ProcessPoolExecutor
from concurrent.futures.process import BrokenProcessPool

import numpy as np

from .layers import END, SEAM, Layers, compose_v5
from .params import validate
from .plank_paint import box_taps, box_var, lattice_span, paint_piece, _blur2d, _wide

SIZES = (256, 512, 1024)
WORKERS = 4               # the plank runs paint in this many processes at once (0: all in the calling process)
N, BW, TOP0, TOPN, END0, ENDN, SIDEN = 1024, 256, 224, 800, 128, 96, 64
PAINT_CONTRAST = 0.65     # the painter's own contrast for the carving; the tone contrast setting is applied by the composer
JOINTS = {0: [610], 1: [250, 700], 2: [430], 3: [120, 560]}   # rows along the top where each plank is cut


def pieces(joints, length, tight):
    """Cyclic pieces between joints: [(a, b)] with a in [0, length), b may run past length (it wraps)."""
    j = sorted(joints)
    g = 2 if tight else 3
    return [(j[i] + g, (j[(i + 1) % len(j)] + (length if i == len(j) - 1 else 0)) - (1 if tight else 2)) for i in range(len(j))]


def paint_run(width, length, joints, x0, x1, seed0, carve_per_100, tight, scale=1, ox=0, **kw):
    """A plank run [length rows, width cols] of layers that wraps along its length, cut at the joints.
    scale=f paints every f-th texel: run column i is full-size column f*i + ox, row j is row f*j (all the arguments
    stay in full-size pixels; length must divide by f)."""
    f = scale
    assert length % f == 0, (length, f)       # the run's rows must land on the preview's texels
    ls, ws = length // f, len(range(ox, width, f))
    out = Layers.blank(ls, ws)
    tmp = Layers.blank(2 * ls, ws)     # each piece paints rows a..b, which may run past length
    c0, c1 = lattice_span(x0, x1, f, ox)
    for i, (a, b) in enumerate(pieces(joints, length, tight)):
        n = b - a + 1
        paint_piece(tmp, x0, x1, a, b, True, True, seed=seed0 + 41 * i, carve=int(carve_per_100 * n / 100) + 1,
                    scale=f, lattice=(ox, 0), **kw)
        r0, r1 = lattice_span(a, b, f)
        rows = np.arange(r0, r1 + 1)
        for o, t in zip(out.arrays(), tmp.arrays()):
            o[rows % ls, c0:c1 + 1] = t[r0:r1 + 1, c0:c1 + 1]
    return out


try:                                 # glibc only (not Windows, not musl): give freed memory back
    _LIBC = ctypes.CDLL("libc.so.6")
    _LIBC.malloc_trim
except (OSError, AttributeError):
    _LIBC = None


def _strip(c0, c1, *args, trim=False, **kw):
    """paint_run, handing back only its columns c0..c1 (what the atlas keeps), as a list of arrays. trim: give the
    freed memory back to the system afterwards, so an idle worker process doesn't sit on its last paint."""
    out = [a[:, c0:c1 + 1].copy() for a in paint_run(*args, **kw).arrays()]
    if trim and _LIBC is not None:
        _LIBC.malloc_trim(0)
    return out


_POOL = {}


_SYNCHRONIZE = 0x00100000       # Windows: the access right to wait on a process
_INFINITE = 0xFFFFFFFF


def _watch_parent(pid):
    """Run in each worker as it starts: end the worker the moment the process that started it is gone, however
    it went (killed, crashed), so no painter outlives it holding memory. Python's own clean-up only runs when the
    parent exits normally."""
    wait = _parent_waiter(pid)
    def watch():
        wait()
        os._exit(0)
    threading.Thread(target=watch, name="watch-parent", daemon=True).start()


def _parent_waiter(pid, windows=(os.name == "nt"), kernel32=None):
    """A function that returns once process pid has ended. Windows waits on the process's handle (os.kill(pid, 0)
    would end the process there, not test it); Linux on a pidfd, else (an old kernel) it polls for the process."""
    if windows:
        k = kernel32 if kernel32 is not None else ctypes.WinDLL("kernel32", use_last_error=True)
        def wait_windows():
            h = k.OpenProcess(_SYNCHRONIZE, False, pid)
            if not h:                     # gone already (or not ours to see): stop now
                return
            try:
                k.WaitForSingleObject(h, _INFINITE)
            finally:
                k.CloseHandle(h)
        return wait_windows
    try:
        fd = os.pidfd_open(pid)
    except (AttributeError, OSError):     # no pidfd (old kernel): poll for the parent instead
        def wait_poll():
            while os.getppid() != 1 and _alive(pid):
                threading.Event().wait(0.5)
        return wait_poll
    def wait_pidfd():
        try:
            select.select([fd], [], [])   # readable once the process has ended
        finally:
            os.close(fd)
    return wait_pidfd


def _alive(pid):
    """POSIX only: signal 0 tests that the process exists without touching it."""
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True


def _workers(n):
    if n not in _POOL:
        _POOL[n] = ProcessPoolExecutor(n, initializer=_watch_parent, initargs=(os.getpid(),))
    return _POOL[n]


def shutdown_workers():
    """Stop the painting processes (they start with the first paint and are kept for the next ones)."""
    while _POOL:
        _POOL.popitem()[1].shutdown()


atexit.register(shutdown_workers)


def exit_on_sigterm():
    """For a script that paints (the app's worker): make SIGTERM a clean exit, so the painting processes are stopped
    on the way out (Python's default for SIGTERM ends the process without its clean-up). Call it from the main
    thread. Without it the workers still end, on their own, once they see this process gone."""
    def on_term(signum, frame):
        raise SystemExit(128 + signum)      # unwinds normally; atexit then runs shutdown_workers
    signal.signal(signal.SIGTERM, on_term)


def box_down(a, f):
    """A full-size layer at 1/f of the size: the mean of the f x f box (plank_paint.box_taps) round every f-th texel,
    wrapping round the edges as the tile does."""
    if f == 1:
        return a
    h, w = a.shape
    ri, ci = np.arange(0, h, f), np.arange(0, w, f)
    rows = sum(wt * a[(ri + o) % h] for o, wt in box_taps(f))
    return sum(wt * rows[:, (ci + o) % w] for o, wt in box_taps(f))


def seam_noise(rs, f=1):
    """The seams' unevenness, drawn at full size (and averaged down at the smaller sizes)."""
    return box_down(rs.normal(0, 1, (N, N)), f)


def shade_seams(layers, noise, f=1):
    """Seam pixels: deep and crisp, a little uneven; one pixel of anti-alias on the plank side."""
    gap = layers.region == SEAM
    layers.seam[:] = np.clip(0.7 + 0.3 * _blur2d(noise, 3.0 / f) * 3, 0, 1)
    edge = ~gap & (np.roll(gap, 1, 0) | np.roll(gap, -1, 0) | np.roll(gap, 1, 1) | np.roll(gap, -1, 1))
    layers.aa[:] = np.where(edge, 0.25, 0.0)


def paint_floor_atlas(settings, size=1024, workers=WORKERS):
    """Paint the atlas as tone layers (layers.Layers, size x size, rows bottom-up as the painter works).
    settings: any of params.validate's keys (missing ones take their defaults); the tone keys are ignored here.
    size: 1024, or 512 / 256 for a preview with the same marks. workers: processes for the plank runs (0: none);
    the result is the same either way."""
    if size not in SIZES:
        raise ValueError(f"size must be one of {SIZES}, got {size!r}")
    f = N // size
    assert all(v % 4 == 0 for v in (N, BW, TOP0, TOPN, END0, ENDN, SIDEN)), "the layout must divide by 4"
    t0, ls = TOP0 // f, SIDEN // f      # the layout's rows at this size (all multiples of 4)
    s = validate(settings)
    seed = s["seed"]
    gap_scale = s["gap_scale"]
    tight = gap_scale < 1      # tighter joints and 1 px board seams go with smaller gaps
    rng = random.Random(seed)
    recipe = dict(creases=s["creases"], crease_strength=(s["crease_min"], s["crease_max"]), lit=s["lit"],
                  crest=s["crest"], side_wobble=s["side_wobble"], meander=s["meander"], edge_light=s["edge_light"],
                  broken_edge=s["broken_edge"], edge_line=s["edge_line"], contrast=PAINT_CONTRAST,
                  carve_strength=s["carve_strength"], carve_open=s["carve_open"], gap_scale=gap_scale)
    density = s["carve_density"]
    atlas = Layers.blank(size, size)
    rs = np.random.default_rng(seed)
    # The six plank runs (the top's four, the two long sides) don't share any randomness: they are painted at once
    # in the worker processes while this one paints the cut ends, and are put in place after.
    runs = []
    # ---- top: four planks, each its own run
    for k in range(4):
        x0, x1 = (BW * k + 1, BW * (k + 1) - 1) if tight else (BW * k + 2, BW * (k + 1) - 2)
        basemul = 1 + rng.uniform(-0.03, 0.03)
        runs.append((lattice_span(x0, x1, f), (N, TOPN, JOINTS[k], x0, x1, 3000 + 97 * k + seed, density * 1.0, tight),
                     dict(basemul=basemul, knot=(k == 2), dominant=(1 if k in (0, 2) else 0), scale=f, **recipe)))
    # ---- long sides of planks 1 and 4: the plank turned on its edge, its lit edge at the top corner
    for r0, k in ((0, 0), (SIDEN, 3)):
        j = [int(round(v * N / TOPN)) for v in JOINTS[k]]
        # rows = along the tile, cols = across the thickness. Columns 4..67 turn into the strip's rows the other
        # way up, so at the smaller sizes the run is painted on the columns that land on the strip's texels
        ox = (4 + SIDEN - 1) % f
        runs.append((lattice_span(4, 4 + SIDEN - 1, f, ox), (80, N, j, 0, 71, 5000 + 97 * k + seed, density * 0.4, tight),
                     dict(basemul=0.94, knot=False, dominant=0, scale=f, ox=ox, **recipe)))
    if workers:
        pool = _workers(workers)
        strips = [pool.submit(_strip, c0, c1, *args, trim=True, **kw) for (c0, c1), args, kw in runs]
    else:
        strips = [_strip(c0, c1, *args, **kw) for (c0, c1), args, kw in runs]
    noise = seam_noise(rs, f)
    # ---- cut ends: growth rings round a heart below each plank, soft and lit like the top, a check or two. The
    # composer makes them lighter and warmer than the top; contrast and drift leave them alone, as in v5.
    Y = np.arange(0, ENDN, f, dtype=np.float32)[:, None]
    var = box_var(f)        # at the smaller sizes the thin ring creases are spread over the texel's box
    for k in range(4):
        c0, c1 = (BW * k + 1, BW * (k + 1) - 1) if tight else (BW * k + 2, BW * (k + 1) - 2)
        i0, i1 = lattice_span(c0, c1, f)
        X = np.arange(i0 * f, i1 * f + 1, f, dtype=np.float32)[None, :]
        hx, hy = (c0 + c1) / 2 + rng.uniform(-60, 60), -rng.uniform(60, 200)
        r = np.hypot(X - hx, Y - hy); ang = np.arctan2(Y - hy, X - hx)
        rr = r + 4 * np.sin(3 * ang + rng.uniform(0, 6)) + 2.5 * np.sin(7 * ang + rng.uniform(0, 6))
        sp = rng.uniform(16, 22)
        ph = (rr % sp) / sp
        (wc, kc), (wl, kl) = _wide(0.09, var / sp ** 2), _wide(0.07, var / sp ** 2)
        v = 0.04 * np.cos(2 * np.pi * rr / sp)                                   # soft alternating bands
        if var:
            v *= np.exp(-2 * np.pi ** 2 * var / sp ** 2)
        v += -0.07 * kc * np.exp(-((ph - 0.5) / wc) ** 2) + 0.03 * kl * np.exp(-((ph - 0.62) / wl) ** 2)   # crease + lit lip
        for _ in range(rng.choice((1, 1, 2))):                                    # a wedge check down from the top
            sx = rng.uniform(c0 + 30, c1 - 30); d = np.array([hx - sx, hy - ENDN]); d /= np.linalg.norm(d)
            L, w0 = rng.uniform(25, 55), rng.uniform(2.8, 3.8)
            for s in range(int(L)):
                ww = w0 * (1 - s / L) ** 0.8 + 0.8
                dd = np.hypot(X - (sx + d[0] * s), Y - (ENDN - 2 + d[1] * s))
                v = np.minimum(v, -0.17 * np.exp(-(dd / ww) ** 2) + v * (1 - np.exp(-(dd / ww) ** 2)))
        v = _blur2d(v, 1.0 / f) * 0.8
        top = ENDN - 1 - Y
        wt, kt = _wide(2.5, var)
        v += 0.15 * kt * np.exp(-((top - 4) / wt) ** 2) - 0.06 * np.exp(-top / 10)   # lit crest at the top corner
        v += 0.07 * np.exp(-(X - c0) / 6) - 0.05 * np.exp(-(c1 - X) / 6)          # lit left edge, darker right
        v -= 0.07 * np.clip(1 - Y / 25, 0, 1)                                      # darker toward the underside
        win = (slice(END0 // f, (END0 + ENDN) // f), slice(i0, i1 + 1))
        atlas.marks[win] = 0
        atlas.rest[win] = v
        atlas.drift[win] = 0
        atlas.region[win] = END
        atlas.basemul[win] = 1
        for c in ((BW * k,) if tight else (BW * k - 1, BW * k, BW * k + 1)):
            if c % f == 0:
                atlas.region[END0 // f:(END0 + ENDN) // f, (c % N) // f] = SEAM
    for i, strip in enumerate(strips):
        try:
            strip = strip.result() if workers else strip
        except BrokenProcessPool:     # a worker died (killed, out of memory): start afresh on the next paint
            broken = _POOL.pop(workers, None)
            if broken is not None:
                broken.shutdown(wait=False, cancel_futures=True)
            raise
        if i < 4:            # a plank of the top, and the seam before it
            (c0, c1), k = runs[i][0], i
            for dst, src in zip(atlas.arrays(), strip):
                dst[t0:, c0:c1 + 1] = src
            for c in ((BW * k,) if tight else (BW * k - 1, BW * k, BW * k + 1)):
                if c % f == 0:
                    atlas.region[t0:, (c % N) // f] = SEAM
        else:                # a long side
            r0 = (i - 4) * SIDEN
            for dst, src in zip(atlas.arrays(), strip):
                dst[r0 // f:r0 // f + ls, :] = src[:, ::-1].T
        strips[i] = None
    shade_seams(atlas, noise, f)
    del noise
    # Symmetric: planks 3 and 4 become planks 2 and 1 turned round, so the top looks the same turned 180 degrees. A
    # triangle and its turned partner then make one whole square, and any tile can turn 180 degrees and still join.
    # Every layer is turned, the seam depth and anti-alias too, so the composed colour turns with them.
    n, tn, e0, en = size, TOPN // f, END0 // f, ENDN // f
    cols, rows = (n - np.arange(n)) % n, (tn - np.arange(tn)) % tn   # about texel 0: textured_tile shifts UVs half a texel
    for lay in atlas.arrays():
        top = lay[t0:].copy()
        lay[t0:, n // 2 + 1:] = top[rows][:, cols][:, n // 2 + 1:]
        for c in (0, n // 2):        # the two seams that turn onto themselves: their lower half mirrors the upper
            lay[t0 + tn // 2 + 1:, c] = top[rows[tn // 2 + 1:], c]
        lay[ls:2 * ls] = lay[0:ls][:, cols]
        ends = lay[e0:e0 + en].copy()
        lay[e0:e0 + en, n // 2 + 1:] = ends[:, cols][:, n // 2 + 1:]
    return atlas


def paint_floor_atlas_rgb(settings, size=1024):
    """Paint the atlas and return it in v5's colours as a uint8 array (size x size x 3), rows top-down as in the PNG.
    settings: as paint_floor_atlas; contrast and drift (v5's 0.65 and 0.6 by default) go to the composer."""
    s = validate(settings)
    return compose_v5(paint_floor_atlas(s, size), contrast=s["contrast"], drift=s["drift"])


def main(argv):
    from PIL import Image
    opt = dict(a[2:].partition("=")[::2] for a in argv[1:] if a.startswith("--"))
    settings = {k: float(v) for k, v in opt.items()}
    if "seed" in settings:
        settings["seed"] = int(settings["seed"])
    Image.fromarray(paint_floor_atlas_rgb(settings)).save(argv[0])
    print("ATLAS", argv[0])


if __name__ == "__main__":
    import sys
    main(sys.argv[1:])
