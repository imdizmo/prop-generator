from pathlib import Path

import numpy as np
from PIL import Image

from propgen_painter.floor_atlas import paint_floor_atlas, paint_floor_atlas_rgb
from propgen_painter.layers import compose_v5
from propgen_painter.params import defaults

GOLDEN = Path(__file__).parent / "golden" / "floor_atlas_v5.png"
V5 = defaults()


def want_v5():
    return np.asarray(Image.open(GOLDEN))[..., :3].astype(int)


def _report(name, got):
    d = np.abs(got.astype(int) - want_v5())
    print(f"{name}: max channel difference {d.max()}, {int((d.max(axis=-1) > 0).sum())} pixels differ")
    return d


def assert_v5(d):
    """The v5 atlas, give or take float rounding: numpy builds and CPUs differ in the last bit, which moves a few
    pixels by one level (2 of a million on GitHub's runners). Anything more is a real change to the recipe."""
    assert d.max() <= 1
    assert int((d.max(axis=-1) > 0).sum()) <= 16


def test_v5_reproduces_exactly():
    got = paint_floor_atlas_rgb(V5)
    assert got.shape == (1024, 1024, 3)
    assert_v5(_report("paint_floor_atlas_rgb", got))


def test_layers_compose_to_v5():
    L = paint_floor_atlas(V5)
    got = compose_v5(L, contrast=0.65, drift=0.6)
    assert _report("compose_v5", got).max() <= 1
