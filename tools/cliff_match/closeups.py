"""Render cliff close-ups at one fixed framing in headless Blender, so a generated cliff's joins and edges can be laid
beside the hand-made pieces' at the same scale (the same metres per pixel for every piece).

    blender -b --factory-startup -P closeups.py -- <outdir> <tile-px> <name>=<glb>[#<object>][@<x>[,<z>]] ...

Each item is rendered on its own (its named mesh, or the GLB's largest), shaded and as a wireframe overlay, to
<outdir>/<name>.png and <outdir>/<name>_wire.png. The camera looks from the front right, a little from above, at
CLOSE_DISTANCE from a point on the piece's front (-y) face: at x (default: the middle of the piece) and z above its
base (default: half its height, at most CLOSE_HEIGHT), so the view spans about 6 m whatever the piece's size.
Made from the hand-made renders' script, whose cameras stood a share of each piece's size away and so framed each
piece differently, and too close.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

# The camera looks from the front right, a little from above (seed_sheet.py's three-quarter direction).
DIRECTION = Vector((16.0, -20.0, 5.5)).normalized()
LENS = 50.0
# The camera stands this far from its look point: with a 50 mm lens the view spans about 6 m across there
# (CLOSEUP_DISTANCE in the environment sets another, for a closer look at one piece).
CLOSE_DISTANCE = float(os.environ.get("CLOSEUP_DISTANCE", "8.5"))
# The look point is no higher than this above the piece's base, in metres.
CLOSE_HEIGHT = 4.0


def items():
    a = sys.argv[sys.argv.index("--") + 1:]
    if len(a) < 3:
        sys.exit("usage: -- <outdir> <tile-px> <name>=<glb>[#<object>][@<x>[,<z>]] ...")
    parsed = []
    for spec in a[2:]:
        name, rest = spec.split("=", 1)
        look = None
        if "@" in rest:
            rest, at = rest.rsplit("@", 1)
            look = [float(v) for v in at.split(",")]
        obj = None
        if "#" in rest:
            rest, obj = rest.split("#", 1)
        parsed.append((name, rest, obj, look))
    return a[0], int(a[1]), parsed


def scene_setup(tile):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    # In one plain stone colour, so the shapes read rather than the palette; CLOSEUP_COLOUR=1 shows the vertex colours.
    sh.light, sh.color_type = "STUDIO", "VERTEX" if os.environ.get("CLOSEUP_COLOUR") == "1" else "SINGLE"
    # A studio light with a fill, as the seed sheets use: under the default one, which follows the camera, an upright
    # wall facing away from it at a grazing angle renders near black and reads as a hole.
    sh.studio_light = "paint.sl"
    sh.single_color = (0.72, 0.70, 0.66)
    sh.show_cavity, sh.cavity_type = True, "BOTH"
    sh.show_shadows = True
    sc.render.resolution_x = sc.render.resolution_y = tile
    sc.render.resolution_percentage = 100
    world = bpy.data.worlds.new("closeups")
    world.color = (0.13, 0.13, 0.14)
    sc.world = world
    cam = bpy.data.objects.new("closeups_cam", bpy.data.cameras.new("closeups_cam"))
    cam.data.lens = LENS
    cam.data.clip_start, cam.data.clip_end = 0.1, 1000
    sc.collection.objects.link(cam)
    sc.camera = cam
    return sc, cam


def load(path, name):
    """Imports a GLB and keeps one mesh (by name, else the largest), with no material."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    added = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in added if o.type == "MESH"]
    if name is not None:
        meshes = [o for o in meshes if o.name.startswith(name)]
    if not meshes:
        sys.exit(f"no mesh {name or ''} in {path}")
    keep = max(meshes, key=lambda o: len(o.data.polygons))
    for o in added:
        if o is not keep:
            bpy.data.objects.remove(o)
    keep.data.materials.clear()
    bpy.context.view_layer.update()
    return keep


def main():
    outdir, tile, specs = items()
    os.makedirs(outdir, exist_ok=True)
    for name, path, obj, look in specs:
        sc, cam = scene_setup(tile)
        target = load(path, obj)
        points = [target.matrix_world @ Vector(c) for c in target.bound_box]
        lo = Vector([min(p[i] for p in points) for i in range(3)])
        hi = Vector([max(p[i] for p in points) for i in range(3)])
        x = (lo.x + hi.x) / 2 if look is None else look[0]
        z = lo.z + (min((hi.z - lo.z) / 2, CLOSE_HEIGHT) if look is None or len(look) < 2 else look[1])
        at = Vector((x, lo.y + 0.25 * (hi.y - lo.y), z))
        cam.location = at + DIRECTION * CLOSE_DISTANCE
        cam.rotation_euler = (at - cam.location).to_track_quat("-Z", "Y").to_euler()
        for wire in (False, True):
            if wire:
                m = target.modifiers.new("wire", "WIREFRAME")
                m.thickness, m.use_replace = 0.008, False
            sc.render.filepath = os.path.join(outdir, f"{name}{'_wire' if wire else ''}.png")
            bpy.ops.render.render(write_still=True)
            if wire:
                target.modifiers.remove(m)
        print(f"CLOSEUP rendered {name} from {path}")


main()
