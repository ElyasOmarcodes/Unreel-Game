# Renders a tree model (glTF / blend) into an impostor atlas: three side views (0, 60, 120 degrees)
# and a top view, 2x2 tiles with alpha, plus its sizes in a JSON. Run: python3 impostor.py model out.png out.json [object-prefix]
import sys, json, math
sys.path.insert(0, '/tmp/claude-0/pyenv')
import bpy, mathutils
src, out_png, out_json = sys.argv[1:4]
only = sys.argv[4] if len(sys.argv) > 4 else ""
tile = int(sys.argv[5]) if len(sys.argv) > 5 else 512
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.endswith(".blend"):
    with bpy.data.libraries.load(src) as (data_from, data_to):
        data_to.objects = [n for n in data_from.objects if not only or n.startswith(only)]
    for o in data_to.objects:
        if o is not None: bpy.context.scene.collection.objects.link(o)
else:
    bpy.ops.import_scene.gltf(filepath=src)
sc = bpy.context.scene
meshes = [o for o in sc.objects if o.type == 'MESH']
if only:
    keep = [o for o in meshes if o.name.startswith(only) or (o.parent and o.parent.name.startswith(only))]
    for o in meshes:
        if o not in keep: bpy.data.objects.remove(o, do_unlink=True)
    meshes = keep
dg = bpy.context.evaluated_depsgraph_get()
mn = mathutils.Vector((1e9, 1e9, 1e9)); mx = -mn.copy()
leafmin = 1e9; low = []
for inst in dg.object_instances:
    ob = inst.object
    if ob.type != 'MESH': continue
    for c in ob.bound_box:
        w = inst.matrix_world @ mathutils.Vector(c)
        mn = mathutils.Vector(map(min, mn, w)); mx = mathutils.Vector(map(max, mx, w))
for o in meshes:
    leafy = any(m and ('leaf' in m.name.lower() or 'leaves' in m.name.lower() or 'needle' in m.name.lower() or 'twig' in m.name.lower()) for m in o.data.materials)
    me = o.data
    M = o.matrix_world
    step = max(1, len(me.vertices) // 200000)
    for i in range(0, len(me.vertices), step):
        w = M @ me.vertices[i].co
        if w.z < mn.z + 0.5: low.append((w.x, w.y))
    if leafy:
        for i in range(0, len(me.vertices), step * 5):
            leafmin = min(leafmin, (M @ me.vertices[i].co).z)
cx = cy = 0.0
if low:
    cx = sum(p[0] for p in low) / len(low); cy = sum(p[1] for p in low) / len(low)
    trunk_r = sorted(math.hypot(p[0] - cx, p[1] - cy) for p in low)[int(len(low) * 0.8)]
else:
    trunk_r = 0.2
h = mx.z - mn.z
half_w = max(abs(mx.x - cx), abs(mn.x - cx), abs(mx.y - cy), abs(mn.y - cy))
S = max(h, 2 * half_w) * 1.03
print("bounds", mn, mx, "height", h, "half width", half_w, "trunk r", trunk_r, "leaves from", leafmin - mn.z)
# lighting: a bright even sky and a sun from high up
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
bg = world.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.85, 0.88, 0.95, 1); bg.inputs[1].default_value = 1.1
sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 3.2; sun.angle = math.radians(8)
so = bpy.data.objects.new("sun", sun); sc.collection.objects.link(so)
so.rotation_euler = (math.radians(35), 0, math.radians(30))
cam = bpy.data.cameras.new("cam"); cam.type = 'ORTHO'
co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
sc.render.engine = 'CYCLES'; sc.cycles.samples = 48; sc.cycles.use_denoising = True
try: sc.cycles.denoiser = 'OPENIMAGEDENOISE'
except Exception: pass
sc.cycles.device = 'CPU'; sc.render.threads_mode = 'AUTO'
sc.render.film_transparent = True
sc.render.resolution_x = sc.render.resolution_y = tile
sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'
sc.view_settings.view_transform = 'Standard'
center_z = mn.z + S / 2
import os
tmp = out_png + ".views"; os.makedirs(tmp, exist_ok=True)
files = []
for k, az in enumerate((0, 60, 120)):
    a = math.radians(az)
    d = mathutils.Vector((math.sin(a), -math.cos(a), 0))
    co.location = mathutils.Vector((cx, cy, center_z)) - d * (S * 3)
    co.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam.ortho_scale = S; cam.clip_end = S * 10
    sun_dir = mathutils.Euler((math.radians(40), 0, a + math.radians(25)))
    so.rotation_euler = sun_dir
    sc.render.filepath = f"{tmp}/v{k}.png"; bpy.ops.render.render(write_still=True); files.append(sc.render.filepath)
# top view: the crown from above
Sw = 2 * half_w * 1.03
co.location = (cx, cy, mx.z + Sw * 3); co.rotation_euler = (0, 0, 0)
cam.ortho_scale = Sw; cam.clip_end = Sw * 10 + h
so.rotation_euler = (math.radians(20), 0, math.radians(30))
sc.render.filepath = f"{tmp}/top.png"; bpy.ops.render.render(write_still=True); files.append(sc.render.filepath)
json.dump({"height": h, "halfWidth": half_w, "side": S, "top": Sw, "trunkRadius": trunk_r, "leavesFrom": max(0.0, leafmin - mn.z)}, open(out_json, "w"), indent=1)
print("views", files)
