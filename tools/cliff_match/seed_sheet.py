"""Render cliff GLBs for a seed contact sheet in headless Blender: each from the three-quarter camera, auto-framed, and
from a close camera at a fixed scale (the same metres per pixel for every cliff), so detail can be compared.

    blender -b --factory-startup -P seed_sheet.py -- <outdir> <tile-px> <reference.glb> <cliff.glb>...

Writes <outdir>/quarter_ref.png and close_ref.png for the reference, and quarter_<k>.png and close_<k>.png for the
k-th cliff (from 0). Each GLB's largest mesh is rendered on its own, in one plain stone colour under a studio light with
a fill (so walls facing away from the camera do not read as holes), cavity and shadows, so the shapes and the surface
detail read rather than the palette.
"""
import math
import os
import sys

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector

# The three-quarter camera looks from the front right, a little from above (match_views.py's persp camera).
QUARTER = Vector((16.0, -20.0, 5.5)).normalized()
LENS = 50.0
# The close camera stands this far from its look point, so it spans about 11.5 m across at the look point.
CLOSE_DISTANCE = 16.0
# The close camera's look point is no higher than this above the cliff's base, in metres.
CLOSE_HEIGHT = 4.5
# The framed cliff fills this share of the tile.
FILL = 0.92
# The studio light the sheets are lit with: one with a fill (see scene_setup).
STUDIO_LIGHT = "paint.sl"


def args():
    a = sys.argv[sys.argv.index("--") + 1:]
    if len(a) < 4:
        sys.exit("usage: -- <outdir> <tile-px> <reference.glb> <cliff.glb>...")
    return a[0], int(a[1]), a[2], a[3:]


def scene_setup(tile):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.color_type = "STUDIO", "SINGLE"
    # Blender's "paint" studio light has a fill: under the default one, which follows the camera, an upright wall
    # facing away from it at a grazing angle (a step or bite wall on a block top) renders near black and reads as a hole.
    sh.studio_light = STUDIO_LIGHT
    sh.single_color = (0.72, 0.70, 0.66)
    sh.show_cavity, sh.cavity_type = True, "BOTH"
    sh.show_shadows = True
    sc.render.resolution_x = sc.render.resolution_y = tile
    sc.render.resolution_percentage = 100
    world = bpy.data.worlds.new("sheet")
    world.color = (0.13, 0.13, 0.14)
    sc.world = world
    cam = bpy.data.objects.new("sheet_cam", bpy.data.cameras.new("sheet_cam"))
    cam.data.lens = LENS
    cam.data.clip_start, cam.data.clip_end = 0.1, 1000
    sc.collection.objects.link(cam)
    sc.camera = cam
    return sc, cam


def load(path):
    """Imports a GLB, keeps its largest mesh with no material, and returns it with its world vertex positions."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    added = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in added if o.type == "MESH"]
    if not meshes:
        sys.exit(f"no mesh in {path}")
    keep = max(meshes, key=lambda o: len(o.data.polygons))
    for o in added:
        if o is not keep:
            bpy.data.objects.remove(o)
    keep.data.materials.clear()
    bpy.context.view_layer.update()
    points = [keep.matrix_world @ v.co for v in keep.data.vertices]
    return keep, points


def aim(cam, eye, look):
    cam.location = eye
    cam.rotation_euler = (look - eye).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()


def frame(sc, cam, points):
    """Places the camera along the three-quarter direction so the points fill FILL of the square tile."""
    lo = Vector((min(p[i] for p in points) for i in range(3)))
    hi = Vector((max(p[i] for p in points) for i in range(3)))
    middle = (lo + hi) / 2
    distance = (hi - lo).length * 1.5
    sample = points[:: max(1, len(points) // 4000)]
    for _ in range(6):
        aim(cam, middle + QUARTER * distance, middle)
        views = [world_to_camera_view(sc, cam, p) for p in sample]
        # The widest reach from the tile's middle, in shares of the half tile; the look point is re-centred too.
        xs = [v.x for v in views]
        ys = [v.y for v in views]
        reach = max(max(xs) - 0.5, 0.5 - min(xs), max(ys) - 0.5, 0.5 - min(ys)) * 2
        shift_x = (max(xs) + min(xs)) / 2 - 0.5
        shift_y = (max(ys) + min(ys)) / 2 - 0.5
        right = cam.matrix_world.to_quaternion() @ Vector((1, 0, 0))
        up = cam.matrix_world.to_quaternion() @ Vector((0, 1, 0))
        span = 2 * distance * math.tan(cam.data.angle / 2)
        middle = middle + right * (shift_x * span) + up * (shift_y * span)
        distance *= reach / FILL
    aim(cam, middle + QUARTER * distance, middle)


def close(cam, points):
    """Places the camera CLOSE_DISTANCE from a point on the cliff's front, at most CLOSE_HEIGHT up."""
    lo = Vector((min(p[i] for p in points) for i in range(3)))
    hi = Vector((max(p[i] for p in points) for i in range(3)))
    look = Vector(((lo.x + hi.x) / 2, lo.y + 0.25 * (hi.y - lo.y), lo.z + min((hi.z - lo.z) / 2, CLOSE_HEIGHT)))
    aim(cam, look + QUARTER * CLOSE_DISTANCE, look)


def render(sc, path):
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    outdir, tile, reference, cliffs = args()
    os.makedirs(outdir, exist_ok=True)
    for tag, path in [("ref", reference)] + [(str(k), p) for k, p in enumerate(cliffs)]:
        sc, cam = scene_setup(tile)
        _, points = load(path)
        frame(sc, cam, points)
        render(sc, os.path.join(outdir, f"quarter_{tag}.png"))
        close(cam, points)
        render(sc, os.path.join(outdir, f"close_{tag}.png"))
        print(f"SHEET rendered {path}")


main()
