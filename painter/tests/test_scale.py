"""The preview paints (256 and 512 px) show the same marks in the same places as the full 1024 paint, only softer;
the full paint is still v5."""
import hashlib
import random
import struct

import numpy as np
import pytest

from propgen_painter.floor_atlas import TOP0, TOPN, paint_floor_atlas, paint_floor_atlas_rgb
from propgen_painter.layers import LAYER_NAMES
from test_golden import V5, want_v5

_FULL = []


def full():
    if not _FULL:
        _FULL.append(paint_floor_atlas(V5, 1024))
    return _FULL[0]


def box_taps(f):
    """The offsets and weights of a box f texels wide, centred on a texel: [-f/2 .. f/2], half weight at both ends."""
    if f == 1:
        return [(0, 1.0)]
    h = f // 2
    return [(o, (0.5 if abs(o) == h else 1.0) / f) for o in range(-h, h + 1)]


def box_down(a, f):
    """The full-size layer seen at 1/f of the size: the mean of the f x f box of full-size texels round each
    preview texel. The atlas turns about texel 0 at every size, so preview texel (i, j) sits on full-size texel
    (f*i, f*j), and the box is centred there (the image wraps, as the tile does)."""
    out = np.zeros(a.shape, dtype=np.float64)
    for oy, wy in box_taps(f):
        for ox, wx in box_taps(f):
            out += wy * wx * np.roll(a, (-oy, -ox), axis=(0, 1))
    return out[::f, ::f]


@pytest.mark.parametrize("size", [256, 512])
def test_preview_has_the_same_marks_in_the_same_places(size):
    prev = paint_floor_atlas(V5, size)
    for name in ("marks", "rest"):
        a = box_down(getattr(full(), name), 1024 // size); b = getattr(prev, name)
        corr = np.corrcoef(a.ravel(), b.ravel())[0, 1]; diff = np.abs(a - b).mean()
        print(f"size {size} {name}: correlation {corr:.4f}, mean difference {diff:.4f}")
        assert corr > 0.9
        assert diff < 0.02


@pytest.mark.parametrize("size", [256, 512])
def test_preview_layers_have_the_preview_size(size):
    prev = paint_floor_atlas(V5, size)
    for name in LAYER_NAMES:
        lay = getattr(prev, name)
        assert lay.shape == (size, size) and lay.dtype == np.float32 and np.isfinite(lay).all()


@pytest.mark.parametrize("size", [256, 512, 1024])
def test_top_is_exactly_symmetric_at_every_size(size):
    """The top turned 180 degrees about texel 0 is itself, at every size, so a turned tile still joins."""
    L = paint_floor_atlas(V5, size) if size != 1024 else full()
    f = 1024 // size
    n, t0, tn = size, TOP0 // f, TOPN // f
    cols, rows = (n - np.arange(n)) % n, (tn - np.arange(tn)) % tn
    for name in LAYER_NAMES:
        top = getattr(L, name)[t0:]
        turned = top[rows][:, cols]
        assert np.array_equal(top[:, n // 2 + 1:], turned[:, n // 2 + 1:]), name


def test_preview_is_deterministic():
    a, b = paint_floor_atlas(V5, 256), paint_floor_atlas(V5, 256)
    for name in LAYER_NAMES:
        assert np.array_equal(getattr(a, name), getattr(b, name)), name


def test_other_sizes_are_refused():
    with pytest.raises(ValueError):
        paint_floor_atlas(V5, 300)


def test_full_size_still_v5():   # same as Task 2's test, kept here so this task can't break it silently
    got = paint_floor_atlas_rgb(V5, 1024)
    assert got.shape == (1024, 1024, 3)
    assert np.abs(got.astype(int) - want_v5()).max() == 0


def test_worker_processes_paint_the_same():
    a, b = paint_floor_atlas(V5, 256, workers=0), paint_floor_atlas(V5, 256, workers=4)
    for name in LAYER_NAMES:
        assert np.array_equal(getattr(a, name), getattr(b, name)), name


class _NumpyStream:
    """A numpy Generator that keeps a digest of every call made on it and everything it returned."""
    def __init__(self, g):
        self._g, self.calls, self._h = g, 0, hashlib.sha256()

    def __getattr__(self, name):
        f = getattr(self._g, name)
        if not callable(f):
            return f

        def call(*a, **k):
            out = f(*a, **k)
            self.calls += 1
            self._h.update(repr((name, a, k)).encode()); self._h.update(np.asarray(out).tobytes())
            return out
        return call

    def digest(self):
        return self.calls, self._h.hexdigest()


class _RandomStream(random.Random):
    """random.Random keeping a digest of every number it hands out (all its methods draw through these two)."""
    def __init__(self, *a):
        self.calls, self._h = 0, hashlib.sha256()
        super().__init__(*a)

    def random(self):
        x = super().random()
        self.calls += 1; self._h.update(struct.pack("<d", x))
        return x

    def getrandbits(self, k):
        x = super().getrandbits(k)
        self.calls += 1; self._h.update(repr((k, x)).encode())
        return x

    def digest(self):
        return self.calls, self._h.hexdigest()


def _streams(size, monkeypatch):
    """Paint at size (in this process) and hand back every random stream the paint made, in the order it made them:
    (kind and seed, (number of draws, digest of every draw))."""
    made = []
    real_rng = np.random.default_rng

    def rng(*a, **k):
        g = _NumpyStream(real_rng(*a, **k))
        made.append((("numpy",) + a, g))
        return g

    def rand(*a):
        g = _RandomStream(*a)
        made.append((("random",) + a, g))
        return g

    with monkeypatch.context() as m:
        m.setattr(np.random, "default_rng", rng)
        m.setattr(random, "Random", rand)
        paint_floor_atlas(V5, size, workers=0)
    return [(seed, g.digest()) for seed, g in made]


@pytest.mark.parametrize("size", [256, 512])
def test_preview_draws_exactly_the_full_size_randomness(size, monkeypatch):
    """Every random stream (each piece's and the atlas's own) hands out the very same numbers, in the same order,
    at the preview size as at full size, so not one mark moves. (Comparing only each stream's final state isn't
    enough: an extra draw at 256 once ended on the very same state, the shifted numbers after it happening to
    use up the same amount of the stream.)"""
    full_streams, prev_streams = _streams(1024, monkeypatch), _streams(size, monkeypatch)
    assert [s for s, _ in prev_streams] == [s for s, _ in full_streams]
    assert len(full_streams) > 9 * 5
    for (seed, d1), (_, d2) in zip(full_streams, prev_streams):
        assert d1 == d2, f"stream {seed} drew differently at size {size}: {d1[0]} draws at 1024, {d2[0]} at {size}"


@pytest.mark.parametrize("size, floor", [(256, (0.98, 0.004, 0.95, 0.006)), (512, (0.995, 0.0015, 0.99, 0.002))])
def test_preview_stays_as_close_as_measured(size, floor):
    """A tighter floor than the brief's test, from what the preview measures (256: marks 0.9956 / 0.0014, rest
    0.9665 / 0.0030), so a smaller slip shows: re-randomising one piece drops 256's marks to about 0.94 / 0.006."""
    prev = paint_floor_atlas(V5, size)
    r_marks, d_marks, r_rest, d_rest = floor
    for name, r_min, d_max in (("marks", r_marks, d_marks), ("rest", r_rest, d_rest)):
        a = box_down(getattr(full(), name), 1024 // size); b = getattr(prev, name)
        assert np.corrcoef(a.ravel(), b.ravel())[0, 1] > r_min, name
        assert np.abs(a - b).mean() < d_max, name
