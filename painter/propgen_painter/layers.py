"""Tone layers: the painting before it has a colour, and the composer that gives it v5's colour.

The painter (floor_atlas.paint_floor_atlas) no longer makes colour. It returns seven float32 maps, rows bottom-up as
the painter works, and the app turns them into colour on the GPU, so colour, contrast and drift can change every frame
without painting again. compose_v5 is the reference composition with v5's fixed palette: the C# ToneComposer and the
shader must give the same result.

    marks    the part of the light/dark value that contrast scales (grain, creases, brushwork, the lit zone, knots)
    rest     everything else in the value (carving, gap shadow, bevels, edge lines, cracks, the cut ends' value)
    drift    the raw red/grey colour drift, before it is multiplied by the drift setting and clipped to -1..1
    region   0 plank top or long side, 1 cut end, 2 seam (the gaps between and around the pieces)
    basemul  multiplier on the region's base colour (each plank's +-3 %, 0.94 for the long sides, 1 for the ends)
    seam     how deep the seam is, 0-1 (light to deep); set everywhere because the anti-alias needs it too
    aa       how far a plank pixel next to a seam blends toward the seam's colour (0 or 0.25)

The value of a plank or end pixel is v = marks * contrast + rest.
"""
from dataclasses import dataclass, fields

import numpy as np

LAYER_NAMES = ("marks", "rest", "drift", "region", "basemul", "seam", "aa")
PLANK, END, SEAM = 0, 1, 2

# v5's palette. Plank colour: BASE * basemul * (1 + v), tinted toward LIT_T where lit and SHD_T where shadowed (full at
# |v| = 0.15), then toward REDDER or GREYER by the clipped drift. Cut ends: BASE * END_T * (1 + v), no tints. Seams:
# LIGHT mixing to DEEP by the seam depth.
BASE = np.array([0.272, 0.188, 0.170])
LIT_T, SHD_T = np.array([1.05, 1.0, 0.9]), np.array([0.94, 0.9, 1.0])
REDDER, GREYER = np.array([1.07, 0.985, 0.955]), np.array([0.955, 0.99, 1.045])
END_T = np.array([1.16, 1.12, 1.06])
DEEP, LIGHT = np.array([0.085, 0.055, 0.043]), np.array([0.135, 0.088, 0.07])


@dataclass
class Layers:
    marks: np.ndarray
    rest: np.ndarray
    drift: np.ndarray
    region: np.ndarray
    basemul: np.ndarray
    seam: np.ndarray
    aa: np.ndarray

    @classmethod
    def blank(cls, h, w):
        """h x w layers that are all seam: whatever the painter doesn't paint stays gap."""
        start = {"region": SEAM, "basemul": 1.0}
        return cls(**{n: np.full((h, w), start.get(n, 0.0), dtype=np.float32) for n in LAYER_NAMES})

    def arrays(self):
        return [getattr(self, f.name) for f in fields(self)]


def compose_v5(layers, contrast, drift):
    """The layers in v5's colours: a uint8 rgb image, rows top-down as in the PNG.

    The steps and their float32 roundings follow v5's painter, which kept each piece's colour in a float32 image,
    so v5's settings give v5's texture."""
    f64 = np.float64
    v = (layers.marks.astype(f64) * contrast + layers.rest.astype(f64))[..., None]
    bm = layers.basemul.astype(f64)[..., None]
    # planks: v5 held each plank's base colour in float32
    base = (BASE * bm).astype(np.float32)
    col = base * (1 + v) * np.where(v > 0, 1 + (LIT_T - 1) * np.clip(v / 0.15, 0, 1),
                                    1 + (SHD_T - 1) * np.clip(-v / 0.15, 0, 1))
    hh = np.clip(layers.drift.astype(f64) * drift, -1, 1)[..., None]
    col = col * np.where(hh > 0, 1 + (REDDER - 1) * hh, 1 + (GREYER - 1) * -hh)
    plank = np.clip(col, 0, 1).astype(np.float32)
    del col, hh, base
    # cut ends: no tints, no drift, and (as in v5) not clipped until the very end
    end = (BASE * bm * END_T * (1 + v)).astype(np.float32)
    del v, bm
    region = layers.region[..., None]
    rgb = np.where(region == END, end, plank)
    del end, plank
    # seams, and the one pixel of anti-alias on the plank side of them. v5 did 0.75 * rgb in float32, then added the
    # seam part in float64.
    seamc = LIGHT + (DEEP - LIGHT) * layers.seam.astype(f64)[..., None]
    aa = layers.aa[..., None]
    rgb = ((1 - aa) * rgb + aa * seamc).astype(np.float32)
    rgb = np.where(region == SEAM, seamc.astype(np.float32), rgb)
    return (np.clip(np.flipud(rgb), 0, 1) * 255 + 0.5).astype(np.uint8)
