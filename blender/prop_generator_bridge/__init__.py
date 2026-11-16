"""Prop Generator bridge: send a mesh from Blender to the Prop Generator app, and bring painted results back.

In Blender we shape things, in the app we paint them. This add-on only moves files; it starts no processes. The app
watches the inbox folder and shows what you send; it writes painted models to the outbox folder.

Folders (the root is a preference of this add-on; the app uses the same default):
    <root>/inbox/    Blender -> app    <object>.glb
    <root>/outbox/   app -> Blender    pg_<name>_floor_tile.glb and so on
The default root is ~/.cache/prop-generator.

Face ids: so that the painting can later be done face by face, every face of a sent mesh has a stable number, kept in
the integer face attribute "pg_face_id" on the mesh. A face without one gets the next free number (one more than the
largest in use); sending again keeps every number. A face copied by splitting keeps the number of its original, and
the later copy gets a new one, so the numbers on one mesh are always unique. The numbers ride in a float, so ids
up to 16,777,215 are exact; a mesh with a larger id is refused when sent.

How the ids reach the app: Blender's glTF exporter cannot carry a per-face attribute. It averages face and corner
attributes onto the vertices, which mixes the ids of neighbouring faces, and it drops degenerate triangles, so a list
of ids by triangle number would drift. What it does keep exactly is every corner of a UV map, splitting vertices where
the corners differ. So the add-on exports a copy of the mesh (modifiers applied, triangulated, no materials) with its
first UV map plus a second one named pg_face_id: u is the face id, v is 0. In the GLB that is TEXCOORD_1 (u = id,
v = 1, because glTF counts v downwards). The app reads the ids from there. Your own UV maps are not touched: the
copy keeps only the first one, so TEXCOORD_0 is yours.
"""
import os
import re

import bmesh
import bpy
from mathutils import Matrix
from bpy.props import StringProperty
from bpy.types import AddonPreferences, Operator, Panel

bl_info = {
    "name": "Prop Generator bridge",
    "author": "Prop Generator",
    "version": (0, 1, 0),
    "blender": (5, 2, 0),
    "location": "3D Viewport > Sidebar (N) > Prop Generator",
    "description": "Send meshes to the Prop Generator app and bring its painted models back",
    "category": "Import-Export",
}

DEFAULT_ROOT = os.path.join("~", ".cache", "prop-generator")
FACE_ATTRIBUTE = "pg_face_id"
MAX_FACE_ID = 2**24 - 1
GROUP_PATTERN = re.compile(r"^(pg_.+_)(floor_tile|floor_tri)$")
GROUP_ORDER = ("floor_tile", "floor_tri")
GAP = 0.5

_root_override = None


def _prefs():
    entry = bpy.context.preferences.addons.get(__package__ or __name__)
    return entry.preferences if entry else None


def get_root():
    """The folder holding inbox/ and outbox/."""
    prefs = _prefs()
    root = _root_override or (prefs.root if prefs and prefs.root else DEFAULT_ROOT)
    return os.path.abspath(os.path.expanduser(bpy.path.abspath(root)))


def set_root(path):
    """Sets the root in the add-on's preferences (or in memory when the add-on is not enabled, as in tests)."""
    global _root_override
    prefs = _prefs()
    if prefs is not None:
        prefs.root = path
        _root_override = None
    else:
        _root_override = path


class PG_Preferences(AddonPreferences):
    bl_idname = __package__ or __name__

    root: StringProperty(
        name="Folder",
        description="Folder with the inbox and outbox subfolders that Blender and the Prop Generator app share",
        subtype="DIR_PATH",
        default=DEFAULT_ROOT,
    )

    def draw(self, context):
        self.layout.prop(self, "root")


def _write_face_id_uv(mesh):
    """Keeps the first UV map (making an empty one if there is none) and adds the pg_face_id map after it."""
    layers = mesh.uv_layers
    if len(layers) == 0:
        layers.new(name="UVMap")
    while len(layers) > 1:
        layers.remove(layers[-1])
    ids = [item.value for item in mesh.attributes[FACE_ATTRIBUTE].data]
    layer = layers.new(name=FACE_ATTRIBUTE)
    for polygon in mesh.polygons:
        for loop in polygon.loop_indices:
            layer.data[loop].uv = (float(ids[polygon.index]), 0.0)


def ensure_face_ids(mesh):
    """Gives every face a unique id; faces without a usable one get the next numbers after the largest."""
    attribute = mesh.attributes.get(FACE_ATTRIBUTE)
    if attribute is None or attribute.domain != "FACE" or attribute.data_type != "INT":
        if attribute is not None:
            mesh.attributes.remove(attribute)
        attribute = mesh.attributes.new(FACE_ATTRIBUTE, "INT", "FACE")
        for item in attribute.data:
            item.value = -1
    values = [item.value for item in attribute.data]
    seen = set()
    lacking = []
    for i, v in enumerate(values):
        # A new face has no value (-1) or the attribute's default (0), which an older face already holds.
        if v < 0 or v in seen:
            lacking.append(i)
        else:
            seen.add(v)
    next_id = max(seen) + 1 if seen else 0
    for i in lacking:
        attribute.data[i].value = next_id
        next_id += 1
    return len(lacking)


def _safe_name(name):
    return re.sub(r'[\\/:*?"<>|\x00-\x1f]', "_", name).strip(". ") or "object"


def _source_mesh_object(context):
    ob = context.active_object
    if ob is not None and ob.type == "MESH" and ob.select_get():
        return ob
    for ob in context.selected_objects:
        if ob.type == "MESH":
            return ob
    return None


class PG_OT_send(Operator):
    bl_idname = "prop_generator.send"
    bl_label = "Send to Prop Generator"
    bl_description = "Send the selected mesh to the Prop Generator app, which shows it. Every face gets a number that stays the same when you send again."
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        source = _source_mesh_object(context)
        if source is None:
            self.report({"ERROR"}, "Select a mesh object first, then send it.")
            return {"CANCELLED"}
        inbox = os.path.join(get_root(), "inbox")
        name = _safe_name(source.name)
        glb_path = os.path.join(inbox, name + ".glb")
        mode = source.mode
        try:
            if mode != "OBJECT":
                bpy.ops.object.mode_set(mode="OBJECT")
            os.makedirs(inbox, exist_ok=True)
            ensure_face_ids(source.data)
            largest = max((item.value for item in source.data.attributes[FACE_ATTRIBUTE].data), default=0)
            if largest > MAX_FACE_ID:
                self.report({"ERROR"}, "Face number %s is too big to send: the largest that can be sent is %s." % (format(largest, ","), format(MAX_FACE_ID, ",")))
                return {"CANCELLED"}
            self._export(context, source, glb_path)
        except OSError as e:
            self.report({"ERROR"}, "Could not write to %s: %s" % (inbox, e.strerror or e))
            return {"CANCELLED"}
        except RuntimeError as e:
            self.report({"ERROR"}, "Could not export %s: %s" % (source.name, e))
            return {"CANCELLED"}
        finally:
            if mode != "OBJECT" and context.view_layer.objects.active is source:
                try:
                    bpy.ops.object.mode_set(mode=mode)
                except RuntimeError:
                    pass
        self.report({"INFO"}, "Sent %s to %s" % (source.name, glb_path))
        return {"FINISHED"}

    @staticmethod
    def _export(context, source, glb_path):
        depsgraph = context.evaluated_depsgraph_get()
        tmp_mesh = bpy.data.meshes.new_from_object(source.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
        tmp_object = None
        saved_active = context.view_layer.objects.active
        saved_selection = list(context.selected_objects)
        try:
            ids = tmp_mesh.attributes.get(FACE_ATTRIBUTE)
            if ids is None or len(ids.data) != len(tmp_mesh.polygons):
                raise RuntimeError("its modifiers drop the face numbers; apply them first")
            bm = bmesh.new()
            bm.from_mesh(tmp_mesh)
            bmesh.ops.triangulate(bm, faces=bm.faces[:])
            bm.to_mesh(tmp_mesh)
            bm.free()
            tmp_mesh.materials.clear()
            _write_face_id_uv(tmp_mesh)
            tmp_object = bpy.data.objects.new(source.name + "_pg_send", tmp_mesh)
            context.scene.collection.objects.link(tmp_object)
            for ob in context.selected_objects:
                ob.select_set(False)
            tmp_object.select_set(True)
            context.view_layer.objects.active = tmp_object
            bpy.ops.export_scene.gltf(
                filepath=glb_path, export_format="GLB", use_selection=True, export_apply=False,
                export_materials="NONE", export_yup=True, export_normals=True, export_animations=False)
        finally:
            if tmp_object is not None:
                bpy.data.objects.remove(tmp_object)
            bpy.data.meshes.remove(tmp_mesh)
            for ob in context.scene.objects:
                ob.select_set(ob in saved_selection)
            context.view_layer.objects.active = saved_active


class PG_OT_get_painted(Operator):
    bl_idname = "prop_generator.get_painted"
    bl_label = "Get painted"
    bl_description = "Bring the newest send from the Prop Generator app into the scene, at the 3D cursor: what was sent, either the two floor tiles side by side or one rock. A mesh you sent from Blender comes back beside it, with the original hidden."
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        outbox = os.path.join(get_root(), "outbox")
        if not os.path.isdir(outbox):
            self.report({"ERROR"}, "The outbox folder %s does not exist yet. In the app, use Send to Blender first." % outbox)
            return {"CANCELLED"}
        files = [os.path.join(outbox, n) for n in os.listdir(outbox) if n.lower().endswith(".glb")]
        if not files:
            self.report({"ERROR"}, "There is no .glb file in %s. In the app, use Send to Blender first." % outbox)
            return {"CANCELLED"}
        group = _newest_send(files)
        if len(group) == 1 and _sent_object(group[0]) is not None:
            return self._bring_beside_source(context, group[0])
        return self._bring_in_group(context, group)

    def _import(self, context, path):
        """Imports one GLB as one mesh object without parent; returns it, or None after reporting the problem."""
        before = set(bpy.data.objects)
        try:
            bpy.ops.import_scene.gltf(filepath=path)
        except RuntimeError as e:
            self.report({"ERROR"}, "Could not read %s: %s" % (os.path.basename(path), e))
            return None
        created = [o for o in bpy.data.objects if o not in before]
        meshes = [o for o in created if o.type == "MESH"]
        if not meshes:
            for o in created:
                bpy.data.objects.remove(o)
            self.report({"ERROR"}, "%s holds no mesh." % os.path.basename(path))
            return None
        for o in meshes:
            matrix = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = matrix
        for o in created:
            if o.type != "MESH":
                bpy.data.objects.remove(o)
        result = meshes[0]
        if len(meshes) > 1:
            for ob in context.selected_objects:
                ob.select_set(False)
            for ob in meshes:
                ob.select_set(True)
            context.view_layer.objects.active = result
            bpy.ops.object.join()
        return result

    def _bring_in_group(self, context, group):
        """Every file of one send, side by side from the 3D cursor; nothing is hidden."""
        cursor = context.scene.cursor.location.copy()
        edge = None
        made = []
        for path in group:
            ob = self._import(context, path)
            if ob is None:
                for done in made:
                    bpy.data.objects.remove(done)
                return {"CANCELLED"}
            ob.name = os.path.splitext(os.path.basename(path))[0]
            context.view_layer.update()
            half = ob.dimensions.x / 2
            x = cursor.x if edge is None else edge + GAP + half
            ob.matrix_world = Matrix.Translation((x, cursor.y, cursor.z))
            edge = x + half
            made.append(ob)
        for ob in context.selected_objects:
            ob.select_set(False)
        for ob in made:
            ob.select_set(True)
        context.view_layer.objects.active = made[0]
        self.report({"INFO"}, "Got %s" % ", ".join(ob.name for ob in made))
        return {"FINISHED"}

    def _bring_beside_source(self, context, path):
        """A painted version of a mesh sent from Blender: at its place, the original hidden."""
        source = _sent_object(path)
        painted = self._import(context, path)
        if painted is None:
            return {"CANCELLED"}
        painted.name = source.name + " painted"
        painted.matrix_world = source.matrix_world
        source.hide_set(True)
        for ob in context.selected_objects:
            ob.select_set(False)
        painted.select_set(True)
        context.view_layer.objects.active = painted
        self.report({"INFO"}, "Got %s from %s" % (painted.name, os.path.basename(path)))
        return {"FINISHED"}


def _newest_send(files):
    """The newest file and the others of the same send (same pg_<name>_ prefix), the tile first.

    Files written one after the other can share a modification time, so the time is never what tells them apart.
    """
    newest = max(files, key=lambda f: (os.path.getmtime(f), f))
    match = GROUP_PATTERN.match(os.path.splitext(os.path.basename(newest))[0])
    if match is None:
        return [newest]
    group = []
    for f in files:
        m = GROUP_PATTERN.match(os.path.splitext(os.path.basename(f))[0])
        if m and m.group(1) == match.group(1):
            group.append((GROUP_ORDER.index(m.group(2)), f))
    return [f for _, f in sorted(group)]


def _sent_object(path):
    """The object this GLB is the painted version of: one that was sent from Blender (it has face ids) and is named like the file."""
    ob = bpy.data.objects.get(os.path.splitext(os.path.basename(path))[0])
    if ob is not None and ob.type == "MESH" and FACE_ATTRIBUTE in ob.data.attributes:
        return ob
    return None


class PG_PT_panel(Panel):
    bl_label = "Prop Generator"
    bl_idname = "PG_PT_panel"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Prop Generator"

    def draw(self, context):
        col = self.layout.column(align=True)
        col.operator(PG_OT_send.bl_idname, icon="EXPORT")
        col.operator(PG_OT_get_painted.bl_idname, icon="IMPORT")
        prefs = _prefs()
        if prefs is not None:
            self.layout.prop(prefs, "root")


CLASSES = (PG_Preferences, PG_OT_send, PG_OT_get_painted, PG_PT_panel)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
