"""Looks at Steel City (and the new yards of extension.json) in Blender, headless:
   python3 render.py out_dir [view ...]     (views: top, or name=cx,cy,cz:tx,ty,tz in Unity coordinates)"""
import sys, os, json, math, colorsys, zlib
import numpy as np
import bpy
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import town

PALETTE = [('Road_set_v1_s_floor', (0.16, 0.16, 0.17)), ('Road_set_v1_b_floor', (0.16, 0.16, 0.17)), ('Road_set', (0.33, 0.33, 0.35)),
           ('Hangar', (0.78, 0.82, 0.86)), ('Concrete_fence', (0.62, 0.6, 0.55)), ('Oil_tank', (0.9, 0.9, 0.88)), ('Cargo', (0.2, 0.42, 0.68)),
           ('Wooden', (0.6, 0.42, 0.25)), ('TallLadder', (0.75, 0.5, 0.2)), ('Pipes', (0.55, 0.6, 0.62)), ('Industrial_pipe', (0.75, 0.5, 0.5)),
           ('Stairs', (0.8, 0.75, 0.3)), ('Platform', (0.7, 0.7, 0.3)), ('NEW_wood', (0.7, 0.45, 0.2)), ('NEW_iron', (0.45, 0.25, 0.15)),
           ('NEW_asphalt', (0.2, 0.2, 0.22)), ('NEW_concrete', (0.7, 0.68, 0.62)), ('NEW_mark', (1, 0.1, 0.1))]


def colour(name):
    for k, c in PALETTE:
        if name.startswith(k):
            return c
    h = (zlib.crc32(town.base(name).encode()) % 1000) / 1000.0
    return colorsys.hsv_to_rgb(h, 0.35, 0.75)


def to_blender(V):
    return np.stack([V[:, 0], V[:, 2], V[:, 1]], axis=1)


_mats = {}


def material(c):
    key = tuple(round(x, 3) for x in c)
    if key not in _mats:
        m = bpy.data.materials.new('m%d' % len(_mats))
        m.diffuse_color = (*c, 1)
        m.use_nodes = True
        for n in m.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                n.inputs['Base Color'].default_value = (*c, 1)
                n.inputs['Roughness'].default_value = 0.85
        _mats[key] = m
    return _mats[key]


def add(name, V, T, c):
    me = bpy.data.meshes.new(name)
    me.from_pydata(to_blender(V).tolist(), [], T[:, ::-1].tolist())
    me.materials.append(material(c))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def box(c, size, yaw=0.0, pitch=0.0):
    """A box (Unity: centre, size, yaw about y, then pitch about its own x) as verts, tris."""
    h = np.array(size) / 2.0
    V = np.array([[sx * h[0], sy * h[1], sz * h[2]] for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)])
    T = np.array([[0, 1, 3], [0, 3, 2], [4, 6, 7], [4, 7, 5], [0, 4, 5], [0, 5, 1], [2, 3, 7], [2, 7, 6], [0, 2, 6], [0, 6, 4], [1, 5, 7], [1, 7, 3]])
    a = math.radians(pitch)
    Rx = np.array([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]])
    V = (town.yaw_matrix(yaw) @ Rx @ V.T).T + np.array(c)
    return V, T


def build(ext=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    removed = ext.get('remove', []) if ext else []

    def gone(name, at):
        for r in removed:
            if name.startswith(r['name']) and abs(at[0] - r['x']) < r.get('r', 1.0) and abs(at[2] - r['z']) < r.get('r', 1.0):
                return True
        return False
    for name, path, W, T, M in town.town_objects():
        if gone(name, M[:3, 3]):
            continue
        add(name, W, T, colour(name))
    if not ext:
        return
    for p in ext.get('pieces', []):
        V, T = town.place(p['name'], p['pos'], p.get('yaw', 0.0), p.get('scale', (1, 1, 1)))
        add('X_' + p['name'], V, T, colour(p['name']))
    for b in ext.get('boxes', []):
        V, T = box(b['c'], b['size'], b.get('yaw', 0.0), b.get('pitch', 0.0))
        add('B_' + b.get('name', 'box'), V, T, colour('NEW_' + b.get('mat', 'concrete')))
    for l in ext.get('ladders', []):
        for seg in ladder_shape(l):
            add('L', *seg, colour('TallLadder'))


def ladder_shape(l):
    """The ladder as PlaceLadder lays it when there is room: 53.4 degrees from upright."""
    lean = 53.4
    foot = np.array(l['foot'], float); out = np.array(l['out'], float); out /= np.linalg.norm(out)
    h = l['height']; reach = h * math.tan(math.radians(lean)); length = h / math.cos(math.radians(lean)) + 0.35
    yaw = math.degrees(math.atan2(out[0], out[2]))
    at = foot + out * (0.2 + reach)
    V, T = box([0, length / 2, 0], [1.1, length, 0.16])
    a = math.radians(-lean)
    Rx = np.array([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]])
    V = (town.yaw_matrix(yaw) @ Rx @ V.T).T + at
    return [(V, T)]


def setup_render(w=1280, h=720):
    s = bpy.context.scene
    s.render.engine = 'CYCLES'
    s.cycles.samples = 12
    s.cycles.use_denoising = False
    s.cycles.max_bounces = 2
    s.render.resolution_x, s.render.resolution_y = w, h
    s.view_settings.view_transform = 'Standard'
    wd = bpy.data.worlds.new('w'); s.world = wd; wd.use_nodes = True
    wd.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.82, 0.95, 1)
    wd.node_tree.nodes['Background'].inputs[1].default_value = 0.9
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    s.collection.objects.link(sun)


def shot(path, cam_pos, target, ortho=None, fov=60):
    s = bpy.context.scene
    cd = bpy.data.cameras.new('c'); ob = bpy.data.objects.new('c', cd); s.collection.objects.link(ob)
    cd.clip_end = 2000
    if ortho:
        cd.type = 'ORTHO'; cd.ortho_scale = ortho
    else:
        cd.sensor_fit = 'VERTICAL'; cd.angle = math.radians(fov)
    from mathutils import Vector
    p = Vector((cam_pos[0], cam_pos[2], cam_pos[1])); t = Vector((target[0], target[2], target[1]))
    ob.location = p
    ob.rotation_euler = (t - p).to_track_quat('-Z', 'Y').to_euler()
    s.camera = ob
    s.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(ob)


if __name__ == '__main__':
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    extp = os.path.join(town.BASE, 'extension.json')
    ext = json.load(open(extp)) if os.path.exists(extp) and os.environ.get('NOEXT') != '1' else None
    build(ext)
    setup_render()
    for v in sys.argv[2:]:
        if v.startswith('top'):
            # top=cx,cz,size
            a = v.split('=')[1].split(',') if '=' in v else ['50', '-20', '260']
            cx, cz, size = float(a[0]), float(a[1]), float(a[2])
            bpy.context.scene.render.resolution_x, bpy.context.scene.render.resolution_y = 1600, 1600
            shot(os.path.join(out, 'top_%d_%d_%d.png' % (cx, cz, size)), (cx, 300, cz - 0.01), (cx, 0, cz), ortho=size)
            bpy.context.scene.render.resolution_x, bpy.context.scene.render.resolution_y = 1280, 720
        else:
            name, rest = v.split('=')
            c, t = rest.split(':')
            shot(os.path.join(out, name + '.png'), [float(x) for x in c.split(',')], [float(x) for x in t.split(',')])
    print('rendered', out)
