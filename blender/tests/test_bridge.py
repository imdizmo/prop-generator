"""Tests for the Prop Generator bridge add-on.

Run:  blender -b --factory-startup -P blender/tests/test_bridge.py
Exit code is non-zero when a test fails. Files go to ~/.cache/prop-generator/scratch/ (never /tmp, which is RAM).
"""
import json
import math
import os
import shutil
import struct
import sys
import time
import traceback

import bmesh
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

SCRATCH = os.path.join(os.path.expanduser("~"), ".cache", "prop-generator", "scratch", "blender-bridge-%d" % os.getpid())
INBOX = os.path.join(SCRATCH, "inbox")
OUTBOX = os.path.join(SCRATCH, "outbox")

import prop_generator_bridge as bridge  # noqa: E402  (fails until the add-on exists)


bridge.register()


def fresh_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bridge.set_root(SCRATCH)
    shutil.rmtree(SCRATCH, ignore_errors=True)


def make_cube(name="Crate"):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    select_only(ob)
    return ob


def select_only(ob):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def face_ids(ob):
    return [d.value for d in ob.data.attributes["pg_face_id"].data]


def read_glb(path):
    """Returns (positions, indices, TEXCOORD_1, json) of the first primitive, in glTF (Y up) space."""
    data = open(path, "rb").read()
    jlen = struct.unpack("<I", data[12:16])[0]
    j = json.loads(data[20:20 + jlen])
    binchunk = data[20 + jlen + 8:]
    prim = j["meshes"][0]["primitives"][0]

    def accessor(i):
        a = j["accessors"][i]
        bv = j["bufferViews"][a["bufferView"]]
        n = {"SCALAR": 1, "VEC2": 2, "VEC3": 3}[a["type"]]
        fmt = {5126: "f", 5123: "H", 5125: "I"}[a["componentType"]]
        off = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        vals = struct.unpack("<%d%s" % (a["count"] * n, fmt), binchunk[off:off + a["count"] * n * struct.calcsize(fmt)])
        return [vals[k:k + n] for k in range(0, len(vals), n)] if n > 1 else list(vals)

    return accessor(prim["attributes"]["POSITION"]), accessor(prim["indices"]), accessor(prim["attributes"]["TEXCOORD_1"]), j


def test_cube_sent_twice_keeps_six_face_ids():
    fresh_scene()
    ob = make_cube()
    bpy.ops.prop_generator.send()
    first = face_ids(ob)
    assert sorted(first) == list(range(6)), first
    bpy.ops.prop_generator.send()
    assert face_ids(ob) == first, (face_ids(ob), first)
    assert os.path.isfile(os.path.join(INBOX, "Crate.glb"))
    assert not os.path.exists(os.path.join(INBOX, "Crate.faces.json"))


def test_second_uv_set_gives_each_triangle_its_face_id():
    fresh_scene()
    ob = make_cube()
    bpy.ops.prop_generator.send()
    pos, idx, uv1, _ = read_glb(os.path.join(INBOX, "Crate.glb"))
    assert len(idx) == 36
    for t in range(12):
        corners = [idx[3 * t + k] for k in range(3)]
        ids = {uv1[c][0] for c in corners}
        assert len(ids) == 1 and all(uv1[c][1] == 1.0 for c in corners), (t, ids)
        face = int(ids.pop())
        a, b, c = [pos[i] for i in corners]
        # glTF (x, y, z) is Blender (x, -z, y)
        a, b, c = [(p[0], -p[2], p[1]) for p in (a, b, c)]
        u = [b[i] - a[i] for i in range(3)]
        v = [c[i] - a[i] for i in range(3)]
        n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
        ln = math.sqrt(sum(x * x for x in n))
        n = [x / ln for x in n]
        want = ob.data.polygons[face].normal
        assert sum(n[i] * want[i] for i in range(3)) > 0.99, (t, face, n, tuple(want))


def test_a_mesh_with_its_own_uv_maps_keeps_the_first_as_texcoord_0():
    fresh_scene()
    ob = make_cube()
    ob.data.uv_layers.new(name="A")
    ob.data.uv_layers.new(name="B")
    bpy.ops.prop_generator.send()
    _, _, _, j = read_glb(os.path.join(INBOX, "Crate.glb"))
    attrs = j["meshes"][0]["primitives"][0]["attributes"]
    assert "TEXCOORD_0" in attrs and "TEXCOORD_1" in attrs and "TEXCOORD_2" not in attrs, attrs
    assert len(ob.data.uv_layers) == 2, "the source mesh must not change"


def test_every_triangle_of_a_monkey_has_a_known_face_id():
    fresh_scene()
    me = bpy.data.meshes.new("Monkey")
    bm = bmesh.new()
    bmesh.ops.create_monkey(bm)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("Monkey", me)
    bpy.context.scene.collection.objects.link(ob)
    select_only(ob)
    bpy.ops.prop_generator.send()
    ids = set(face_ids(ob))
    assert len(ids) == len(me.polygons)
    pos, idx, uv1, _ = read_glb(os.path.join(INBOX, "Monkey.glb"))
    for t in range(len(idx) // 3):
        corner_ids = {uv1[idx[3 * t + k]][0] for k in range(3)}
        assert len(corner_ids) == 1 and int(corner_ids.pop()) in ids


def test_added_face_gets_the_next_id_and_old_ids_stay():
    fresh_scene()
    ob = make_cube()
    bpy.ops.prop_generator.send()
    before = face_ids(ob)
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    vs = [bm.verts.new(p) for p in ((5, 0, 0), (6, 0, 0), (6, 1, 0))]
    bm.faces.new(vs)
    bm.to_mesh(me)
    bm.free()
    assert len(me.polygons) == 7
    bpy.ops.prop_generator.send()
    after = face_ids(ob)
    assert after[:6] == before, (after, before)
    assert after[6] == 6, after


def test_deleted_face_ids_are_not_reused():
    fresh_scene()
    ob = make_cube()
    bpy.ops.prop_generator.send()
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[5]], context="FACES_ONLY")
    bm.faces.new(bm.verts[:3])
    bm.to_mesh(me)
    bm.free()
    bpy.ops.prop_generator.send()
    ids = face_ids(ob)
    assert len(set(ids)) == len(ids), ids


def export_cube_glb(path, size=2.0, mtime=None):
    """A stand-in for a GLB from the app: a cube of this size exported by Blender itself."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    other = make_cube("tmp")
    other.scale = (size / 2,) * 3
    bpy.context.view_layer.update()
    select_only(other)
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True)
    bpy.data.objects.remove(other)
    if mtime is not None:
        os.utime(path, (mtime, mtime))


def test_get_painted_brings_in_both_floor_tiles_side_by_side_at_the_cursor():
    fresh_scene()
    stamp = time.time()
    # the app writes the tile, then the triangle; the same modification time makes the file order arbitrary
    export_cube_glb(os.path.join(OUTBOX, "pg_oak_floor_tile.glb"), 2.0, stamp)
    export_cube_glb(os.path.join(OUTBOX, "pg_oak_floor_tri.glb"), 2.0, stamp)
    export_cube_glb(os.path.join(OUTBOX, "pg_old_floor_tile.glb"), 2.0, stamp - 100)
    bpy.context.scene.cursor.location = (10, 20, 30)
    bystander = make_cube("Bystander")
    select_only(bystander)

    bpy.ops.prop_generator.get_painted()

    tile = bpy.data.objects.get("pg_oak_floor_tile")
    tri = bpy.data.objects.get("pg_oak_floor_tri")
    assert tile is not None and tri is not None, [o.name for o in bpy.data.objects]
    assert bpy.data.objects.get("pg_old_floor_tile") is None
    assert tuple(round(v, 4) for v in tile.matrix_world.translation) == (10, 20, 30), tuple(tile.matrix_world.translation)
    # the triangle is 0.5 m to the +X side of the tile (edge to edge, both 2 m wide)
    assert abs(tri.matrix_world.translation.x - (10 + 1.0 + 0.5 + 1.0)) < 1e-4, tuple(tri.matrix_world.translation)
    assert abs(tri.matrix_world.translation.y - 20) < 1e-4 and abs(tri.matrix_world.translation.z - 30) < 1e-4
    assert not bystander.hide_get(), "nothing is hidden for pieces made by the app"
    assert [o.name for o in bpy.data.objects if o.type == "EMPTY"] == []


def test_get_painted_for_a_piece_sent_from_blender_goes_beside_it_and_hides_it():
    fresh_scene()
    src = make_cube("Crate")
    src.location = (3, 2, 1)
    bpy.context.view_layer.update()
    select_only(src)
    bpy.ops.prop_generator.send()
    export_cube_glb(os.path.join(OUTBOX, "Crate.glb"))
    select_only(src)

    bpy.ops.prop_generator.get_painted()

    painted = bpy.data.objects.get("Crate painted")
    assert painted is not None, [o.name for o in bpy.data.objects]
    assert painted.type == "MESH" and len(painted.data.polygons) == 12
    assert src.hide_get(), "source is not hidden"
    assert not painted.hide_get()
    for i in range(3):
        assert abs(painted.matrix_world.translation[i] - src.matrix_world.translation[i]) < 1e-5


def test_a_single_unknown_file_is_named_after_the_file():
    fresh_scene()
    export_cube_glb(os.path.join(OUTBOX, "something.glb"))
    bpy.ops.prop_generator.get_painted()
    assert bpy.data.objects.get("something") is not None, [o.name for o in bpy.data.objects]


def test_ids_beyond_what_a_float_holds_are_refused():
    fresh_scene()
    ob = make_cube()
    bpy.ops.prop_generator.send()
    ob.data.attributes["pg_face_id"].data[0].value = 16777216
    expect_error(bpy.ops.prop_generator.send, "16,777,216")


def test_send_can_be_undone():
    assert "UNDO" in bridge.PG_OT_send.bl_options


def expect_error(op, needle):
    try:
        op()
    except RuntimeError as e:
        assert needle.lower() in str(e).lower(), str(e)
        return
    raise AssertionError("no error reported")


def test_errors_are_reported_not_raised_as_tracebacks():
    fresh_scene()
    expect_error(bpy.ops.prop_generator.send, "mesh")
    expect_error(bpy.ops.prop_generator.get_painted, "outbox")
    bridge.set_root(os.path.join(SCRATCH, "x", "y"))
    os.makedirs(os.path.join(SCRATCH, "x", "y", "outbox"), exist_ok=True)
    expect_error(bpy.ops.prop_generator.get_painted, ".glb")


def test_operators_have_tooltips_and_the_panel_exists():
    fresh_scene()
    for cls in (bridge.PG_OT_send, bridge.PG_OT_get_painted):
        assert len(cls.bl_description) >= 40, cls
    assert bridge.PG_PT_panel.bl_label == "Prop Generator"
    assert bridge.PG_PT_panel.bl_space_type == "VIEW_3D" and bridge.PG_PT_panel.bl_region_type == "UI"
    assert bridge.bl_info["blender"] >= (5, 2, 0)


def main():
    tests = [(n, f) for n, f in sorted(globals().items()) if n.startswith("test_") and callable(f)]
    failed = 0
    for name, fn in tests:
        try:
            fn()
            print("PASS", name)
        except Exception:
            failed += 1
            print("FAIL", name)
            traceback.print_exc()
    shutil.rmtree(SCRATCH, ignore_errors=True)
    print("%d passed, %d failed" % (len(tests) - failed, failed))
    sys.stdout.flush()
    os._exit(1 if failed else 0)


main()
