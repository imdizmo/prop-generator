"""Render a hand-made cliff piece and our GLB in headless Blender from cameras framed on the piece; score the match.

    blender -b --factory-startup -P match_views.py -- <original.glb> <object-prefix> <ours.glb> <outdir>

Both pieces are centred on their bounding boxes in x/y with their base on z = 0. Writes sheet.png (original on top,
ours below; front, side, top, three-quarter), overlay.png (front, side, top: red = only the original, blue = only
ours, white = both) and the per-view PNGs, and prints one JSON line: overlap per view (best over sideways shifts of
+-12 px, and +-12 px front to back in the top view), the shift, shading difference and the ten-band width ratios
(ours / original, top to bottom).
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

RES = 400
FRAME = 1.15  # the ortho views span 1.15 x the original's largest extent


def args():
    a = sys.argv[sys.argv.index("--") + 1:]
    if len(a) != 4:
        sys.exit("usage: -- <original.glb> <object-prefix> <ours.glb> <outdir>")
    return a


def import_glb(path):
    """Imports a GLB and returns the objects it added."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def world_bounds(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    return lo, hi


def ground(objs):
    """Moves the meshes so the bounding box is centred on the origin in x/y and rests on z = 0."""
    bpy.context.view_layer.update()
    lo, hi = world_bounds(objs)
    shift = Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
    for o in objs:
        o.matrix_world = shift @ o.matrix_world
    bpy.context.view_layer.update()
    return world_bounds(objs)


def make_camera(name, location, ortho_scale, rotation=None, look_at=None):
    cam = bpy.data.cameras.new(name)
    if ortho_scale:
        cam.type = "ORTHO"
        cam.ortho_scale = ortho_scale
    else:
        cam.type = "PERSP"
        cam.angle = math.radians(35)
    cam.clip_start, cam.clip_end = 0.1, 500
    obj = bpy.data.objects.new(name, cam)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    if look_at is not None:
        obj.rotation_euler = (Vector(look_at) - Vector(location)).to_track_quat("-Z", "Y").to_euler()
    else:
        obj.rotation_euler = rotation
    return obj


def setup_cameras(size):
    """Frames the views on the original's size: the ortho views span 1.15 x its largest extent, the front and side
    cameras sit at half its height, and the three-quarter view keeps the proportions it had at a 15 m frame."""
    scale = FRAME * max(size)
    z = size.z / 2
    far = 40 + scale
    k = scale / 15
    h = math.radians(90)
    return {
        "front": make_camera("MatchCam_front", (0, -far, z), scale, (h, 0, 0)),
        "side": make_camera("MatchCam_side", (far, 0, z), scale, (h, 0, h)),
        "top": make_camera("MatchCam_top", (0, 0, far), scale, (0, 0, 0)),
        "persp": make_camera("MatchCam_persp", (16 * k, -20 * k, 11 * k), None, look_at=(0, 0, 5.5 * k)),
    }


def render_views(tag, rock, cams, outdir):
    sc = bpy.context.scene
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in rock
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.color_type = "STUDIO", "SINGLE"
    sh.single_color = (0.8, 0.8, 0.8)
    sh.show_cavity, sh.cavity_type = True, "BOTH"
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = RES
    sc.render.resolution_percentage = 100
    out = {}
    for v, cam in cams.items():
        sc.camera = cam
        path = os.path.join(outdir, f"{tag}_{v}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(path, check_existing=False)
        a = np.empty(RES * RES * 4, np.float32)
        im.pixels.foreach_get(a)
        bpy.data.images.remove(im)
        out[v] = a.reshape(RES, RES, 4)[::-1]
    return out


def metrics(T, C):
    """Overlap (best over small shifts), shading difference and width-band ratios of ours (C) against the original (T)."""
    r = {}
    shifted = {}
    for v in ("front", "side", "top"):
        ta = T[v][..., 3] > 0.5
        ca = C[v][..., 3] > 0.5
        iou, best = 0.0, (0, 0)
        for dx in range(-12, 13, 2):
            for dy in (range(-12, 13, 2) if v == "top" else (0,)):
                cs = np.roll(np.roll(ca, dx, axis=1), dy, axis=0)
                score = (ta & cs).sum() / max(1, (ta | cs).sum())
                if score > iou:
                    iou, best = score, (dx, dy)
        ca = np.roll(np.roll(ca, best[0], axis=1), best[1], axis=0)
        shifted[v] = (ta, ca)
        bands = []
        if v != "top":
            ys = np.where(ta.any(1))[0]
            y0, y1 = ys.min(), ys.max()
            for i in range(10):
                a = y0 + (y1 - y0) * i // 10
                b = y0 + (y1 - y0) * (i + 1) // 10
                bands.append(round(float(ca[a:b].sum(1).mean() / max(ta[a:b].sum(1).mean(), 1)), 2))
        both = ta & ca
        shade = float(np.abs(T[v][..., 0] - np.roll(np.roll(C[v], best[0], axis=1), best[1], axis=0)[..., 0])[both].mean()) if both.any() else 1.0
        r[v] = {"iou": round(float(iou), 3), "shift_px": list(best), "shade_diff": round(shade, 3),
                "band_ratio_top_to_bottom": bands}
    return r, shifted


def save_image(rgb, path):
    h, w = rgb.shape[:2]
    out = np.concatenate([rgb, np.ones((h, w, 1), np.float32)], 2)[::-1].ravel()
    im = bpy.data.images.new("out", w, h)
    im.pixels.foreach_set(out)
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)


def sheet(T, C, path):
    rows = [np.concatenate([X[v] for v in ("front", "side", "top", "persp")], 1) for X in (T, C)]
    img = np.concatenate(rows, 0)
    bg = np.array([0.23, 0.23, 0.25], np.float32)
    save_image(img[..., :3] * img[..., 3:4] + bg * (1 - img[..., 3:4]), path)


def overlay(shifted, path):
    panels = []
    for v in ("front", "side", "top"):
        ta, ca = shifted[v]
        img = np.full(ta.shape + (3,), 0.12, np.float32)
        img[ta & ca] = (0.9, 0.9, 0.9)
        img[ta & ~ca] = (0.9, 0.1, 0.1)
        img[~ta & ca] = (0.15, 0.3, 0.95)
        panels.append(img)
    save_image(np.concatenate(panels, 1), path)


def main():
    orig, prefix, ours, outdir = args()
    os.makedirs(outdir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)

    added = import_glb(orig)
    pieces = [o for o in added if o.type == "MESH" and o.name.startswith(prefix)]
    if not pieces:
        sys.exit(f"no mesh object starting with {prefix!r} in {orig}")
    for o in added:
        if o not in pieces and o.type == "MESH":
            bpy.data.objects.remove(o)
    lo, hi = ground(pieces)
    mine = [o for o in import_glb(ours) if o.type == "MESH"]
    ground(mine)

    cams = setup_cameras(hi - lo)
    T = render_views("orig", pieces, cams, outdir)
    C = render_views("ours", mine, cams, outdir)
    result, shifted = metrics(T, C)
    sheet(T, C, os.path.join(outdir, "sheet.png"))
    overlay(shifted, os.path.join(outdir, "overlay.png"))
    result["original_size_m"] = [round(x, 3) for x in (world_bounds(pieces)[1] - world_bounds(pieces)[0])]
    result["ours_size_m"] = [round(x, 3) for x in (world_bounds(mine)[1] - world_bounds(mine)[0])]
    print("MATCH " + json.dumps(result))


main()
