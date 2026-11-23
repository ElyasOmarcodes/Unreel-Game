"""
TEAM 1, made from scratch in Blender on the game's own body (Quaternius UBC male, the very mesh the agents
wear), after the reference sheet: a hexagon-camo combat jacket and cargo trousers taken off the body with room
to move and let hang under gravity in a cloth simulation (the trousers from the waist, the jacket's skirt over
the hips), a black hood shaped like a balaclava with the eyes and the brows open, a mask over the nose, the
mouth and the chin, a scarf wound round the neck with its cloth down the back and one end down the chest, a
belt with olive pouches, knee pads with their straps, high black boots, fingerless gloves and the flag on both
sleeves. Every piece takes its skin weights from the body (the same bones as the game's), the loose parts
(the jacket's skirt, the scarf) partly onto the trunk so they swing rather than stretch.
The look is baked into one atlas: each cloth tiled at its real size (the textures in Tools/team1/tex, made by
textures.py) with its weave as normals, and the shadow of the folds (ambient occlusion) laid into the colour.

Run (the bpy module):  python3 build.py <Superhero_Male_FullBody.fbx> <textures dir> <out dir> [--nosim] [--nobake]
Writes:  Team1.obj            one mesh in the game's axes (x right, y up, z forward), atlas UVs
         Team1_weights.txt    per 'v': up to four "bone weight" pairs (UBC bone names, as the game's body)
         Team1_bones.json     the body's joints in the same axes (to fit the mesh onto the game's body)
         Team1.jpg, Team1_n.png   the atlas: colour with the folds' shadow, and the normals
         Team1.blend, Team1_parts.blend   for a look (preview.py)
"""
import json
import math
import os
import sys

import bpy  # the bpy module first: it brings bmesh and mathutils
import bmesh
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

argv = [a for a in sys.argv[1:] if not a.endswith('.py')]
FBX, TEX, OUT = argv[0], argv[1], argv[2]
SIM = '--nosim' not in argv
BAKE = '--nobake' not in argv
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
scene = bpy.context.scene
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
# the body as it is at rest (the collider in the cloth simulation is the evaluated body: it must be the very
# shape the cloth was cut from)
arm.data.pose_position = 'REST'
body = bpy.data.objects['SuperHero_Male']
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o is not body:
        o.hide_render = True
BW = body.matrix_world.copy()

# ------------------------------------------------------------------ the body in world space
bmb = bmesh.new()
bmb.from_mesh(body.data)
bmb.transform(BW)
bmb.normal_update()
bmb.verts.ensure_lookup_table()
BODY_P = np.array([v.co[:] for v in bmb.verts])
body_bvh = BVHTree.FromBMesh(bmb)
gname = {g.index: g.name for g in body.vertex_groups}
DOM = []
for v in body.data.vertices:
    DOM.append(gname[max(v.groups, key=lambda g: g.weight).group] if v.groups else '')
BONE = {b.name: (arm.matrix_world @ b.head_local) for b in arm.data.bones}
TORSO = {'spine_01', 'spine_02', 'spine_03', 'clavicle_l', 'clavicle_r', 'pelvis'}
ARMS = {'upperarm_l', 'upperarm_r', 'lowerarm_l', 'lowerarm_r'}
LEGS = {'thigh_l', 'thigh_r', 'calf_l', 'calf_r'}
FEET = {'foot_l', 'foot_r', 'ball_l', 'ball_r'}
HEAD = {'Head', 'neck_01'}
HAND = {n for n in gname.values() if n.startswith(('hand_', 'thumb_', 'index_', 'middle_', 'ring_', 'pinky_'))}
FINGER_BASE = {n for n in HAND if n.split('_')[1] == '01'}
EYE_Z = (1.668, 1.738)          # the band left open in the hood (the eyes and the brows)
WAIST = 0.975                   # the belt's middle


def smooth01(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


# ------------------------------------------------------------------ mesh helpers

def new_object(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    return ob


def shell(name, keep, offset):
    """The body's faces whose corners all satisfy keep(i), pushed out along the skin's normal by offset(i)."""
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.transform(BW)
    bm.normal_update()
    bm.verts.ensure_lookup_table()
    k = [keep(i) for i in range(len(bm.verts))]
    off = [offset(i) if k[i] else 0.0 for i in range(len(bm.verts))]
    for v in bm.verts:
        v.co = v.co + v.normal * off[v.index]
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not all(k[v.index] for v in f.verts)], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    return new_object(name, bm)


def clear_of(bm, clearance, targets, keep_boundary=False):
    """Every vertex at least clearance(v) off the target surfaces: moved out along its own normal (never onto
    the same spot as a neighbour, as moving to the nearest surface point would), at most 2.5 cm a time."""
    bm.normal_update()
    for v in bm.verts:
        if keep_boundary and v.is_boundary:
            continue
        c = clearance(v.co)
        for bvh in targets:
            loc, nrm, _i, d = bvh.find_nearest(v.co)
            if loc is None:
                continue
            sd = (v.co - loc).dot(nrm)                # signed: below zero inside
            if sd >= c or sd < -0.03:
                continue                               # clear, or "deep inside": a hollow's face seen from outside
            n = v.normal if v.normal.length > 0.5 else nrm
            along = max(0.5, n.dot(nrm))
            v.co = v.co + n * min(0.025, (c - sd) / along)


def clear_of_obj(ob, c, targets):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    clear_of(bm, lambda co: c, targets)
    bm.to_mesh(ob.data)
    bm.free()


def relax(ob, iterations, clearance, targets=None, factor=0.5):
    """Cloth bridges the body's hollows: smoothed, then kept clear of the skin."""
    targets = targets or [body_bvh]
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    inner = [v for v in bm.verts if not v.is_boundary]
    for _ in range(iterations):
        bmesh.ops.smooth_vert(bm, verts=inner, factor=factor, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        clear_of(bm, clearance, targets)
    bm.to_mesh(ob.data)
    bm.free()


def boundary_loops(bm, pick=None):
    """The open edges in closed loops (lists of verts), optionally only the loops whose middle satisfies pick."""
    edges = [e for e in bm.edges if e.is_boundary]
    left = set(edges)
    loops = []
    while left:
        e = left.pop()
        loop = [e.verts[0], e.verts[1]]
        grew = True
        while grew:
            grew = False
            for e2 in list(left):
                a, b = e2.verts
                if a == loop[-1] and b != loop[-2]:
                    loop.append(b); left.discard(e2); grew = True
                elif b == loop[-1] and a != loop[-2]:
                    loop.append(a); left.discard(e2); grew = True
        if loop[0] == loop[-1]:
            loop.pop()
        mid = sum((v.co for v in loop), Vector()) / len(loop)
        if pick is None or pick(mid, loop):
            loops.append(loop)
    return loops


def extend_loop(bm, loop, rows, step):
    """New rows of faces off an open loop: each vertex moved by step(v_co, row) (returns the offset)."""
    prev = loop
    for r in range(rows):
        new = [bm.verts.new(v.co + step(v.co, r)) for v in prev]
        n = len(prev)
        for i in range(n):
            a, b = prev[i], prev[(i + 1) % n]
            c, d = new[(i + 1) % n], new[i]
            try:
                bm.faces.new((a, b, c, d))
            except ValueError:
                pass
        prev = new
    bm.normal_update()
    return prev


def rim(ob, depth=0.007, pick=None):
    """Thickness at the open edges (hems, cuffs, openings): each open loop turned in by depth."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.normal_update()
    for loop in boundary_loops(bm, pick):
        normals = [v.normal.copy() for v in loop]
        n = len(loop)
        new = [bm.verts.new(v.co - nv * depth) for v, nv in zip(loop, normals)]
        for i in range(n):
            a, b = loop[i], loop[(i + 1) % n]
            try:
                bm.faces.new((b, a, new[i], new[(i + 1) % n]))
            except ValueError:
                pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()


def tidy(ob):
    """Points closer than a millimetre merged, faces and edges with no size dissolved (a cloth solver fails on them)."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0012)
    bmesh.ops.dissolve_degenerate(bm, dist=0.0008, edges=bm.edges[:])
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(ob.data)
    bm.free()


def subdivide(ob, cuts=1):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts, use_grid_fill=True, smooth=0.6)
    bm.to_mesh(ob.data)
    bm.free()


def convex_hull(points):
    """2D convex hull (monotone chain), counter-clockwise."""
    pts = sorted(set(points))
    if len(pts) < 3:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def ray_to_hull(cx, cy, dx, dy, hull):
    """How far from (cx, cy) along (dx, dy) the ray leaves the convex polygon."""
    best = None
    n = len(hull)
    for i in range(n):
        ax, ay = hull[i]
        bx, by = hull[(i + 1) % n]
        ex, ey = bx - ax, by - ay
        den = dx * ey - dy * ex
        if abs(den) < 1e-12:
            continue
        t = ((ax - cx) * ey - (ay - cy) * ex) / den
        u = ((ax - cx) * dy - (ay - cy) * dx) / den
        if t > 0 and -1e-6 <= u <= 1 + 1e-6:
            best = t if best is None else max(best, t)
    return best


def despike(ob, centre, iterations=3, limit=0.006):
    """A point standing out from its neighbours (an ear's tip under the hood) put back to their mean distance
    from the centre."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for _ in range(iterations):
        moves = {}
        for v in bm.verts:
            nb = [e.other_vert(v) for e in v.link_edges]
            if len(nb) < 3:
                continue
            r = (v.co - centre).length
            mean = sum((w.co - centre).length for w in nb) / len(nb)
            if r > mean + limit:
                moves[v] = centre + (v.co - centre).normalized() * mean
        for v, p in moves.items():
            v.co = p
    bm.to_mesh(ob.data)
    bm.free()


def smooth_boundary(ob, iterations=6, factor=0.5):
    """The open edges evened out along themselves (a cut through the body's triangles is a zig-zag)."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for _ in range(iterations):
        moves = {}
        for v in bm.verts:
            if not v.is_boundary:
                continue
            nb = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(nb) == 2:
                moves[v] = (nb[0].co + nb[1].co) / 2
        for v, target in moves.items():
            v.co = v.co.lerp(target, factor)
    bm.to_mesh(ob.data)
    bm.free()


def bvh_of(*objs):
    bm = bmesh.new()
    for o in objs:
        m = bmesh.new()
        m.from_mesh(o.data)
        m.transform(o.matrix_world)
        tmp = bpy.data.meshes.new('tmp')
        m.to_mesh(tmp)
        m.free()
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    bm.normal_update()
    t = BVHTree.FromBMesh(bm)
    bm.free()
    return t


def drape(ob, frames, pin, colliders, mass=0.04, tension=25.0, bending=1.0, shrink=0.0):
    """Classic cloth: the pinned verts (pin(co) -> 0..1) hold, the rest falls on the colliders and settles."""
    if not SIM:
        return
    tidy(ob)
    me = ob.data
    # nothing may start inside a collider's margin (the push out of it would throw the cloth about)
    bm = bmesh.new()
    bm.from_mesh(me)
    clear_of(bm, lambda co: 0.009, [bvh_of(c) if c is not body else body_bvh for c in colliders])
    bm.to_mesh(me)
    bm.free()
    vg = ob.vertex_groups.new(name='Pin')
    for v in me.vertices:
        w = pin(ob.matrix_world @ v.co)
        if w > 0:
            vg.add([v.index], w, 'REPLACE')
    cl = ob.modifiers.new('Drape', 'CLOTH')
    s = cl.settings
    s.quality, s.mass, s.tension_stiffness, s.compression_stiffness = 10, mass, tension, tension
    s.bending_stiffness, s.shrink_min = bending, shrink
    s.vertex_group_mass = 'Pin'
    s.pin_stiffness = 1.0
    s.air_damping = 2.0
    cl.collision_settings.distance_min = 0.003
    cl.collision_settings.collision_quality = 4
    cl.collision_settings.impulse_clamp = 0.4        # no collision may throw a point far in one step
    cl.collision_settings.use_self_collision = False
    added = []
    for c in colliders:
        if not any(m.type == 'COLLISION' for m in c.modifiers):
            c.modifiers.new('Collision', 'COLLISION')
            added.append(c)
        c.collision.thickness_outer = 0.002
        c.collision.cloth_friction = 6.0
    cl.point_cache.frame_start, cl.point_cache.frame_end = 1, frames + 1
    rest = np.array([v.co[:] for v in me.vertices])
    for f in range(1, frames + 2):
        scene.frame_set(f)
        ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get()).data
        now = np.array([v.co[:] for v in ev.vertices])
        moved = np.linalg.norm(now - rest, axis=1).max() if len(now) == len(rest) else -1
        if moved > 0.45:
            print(f'  drape {ob.name}: unstable at frame {f}')
            break
    print(f'  drape {ob.name}: furthest move {moved:.3f} m')
    bpy.context.view_layer.objects.active = ob
    with bpy.context.temp_override(object=ob, active_object=ob):
        bpy.ops.object.modifier_apply(modifier=cl.name)
    scene.frame_set(1)
    for c in added:
        c.modifiers.remove(c.modifiers['Collision'])
    ob.vertex_groups.remove(ob.vertex_groups['Pin'])


def patch(name, target, center, out, up, size, res, lift, bulge=0.0):
    """A flat piece (pocket, flap, flag, pad) laid on a surface: a grid round center, each point dropped along
    -out onto the target (or to its nearest point) and lifted by lift (plus a bulge in the middle); its own UVs
    in metres across it."""
    tb = bvh_of(target)
    out = out.normalized()
    right = up.cross(out).normalized()
    up = out.cross(right).normalized()
    bm = bmesh.new()
    grid = {}
    nx, ny = res
    for i in range(nx + 1):
        for j in range(ny + 1):
            a, b = i / nx - 0.5, j / ny - 0.5
            p = center + right * (a * size[0]) + up * (b * size[1])
            hit, nrm, _i, _d = tb.ray_cast(p + out * 0.25, -out, 0.6)
            if hit is None:
                hit, nrm, _i, _d = tb.find_nearest(p)
                if hit is None:
                    hit, nrm = p, out
            bl = bulge * math.cos(a * math.pi) * math.cos(b * math.pi)
            grid[i, j] = bm.verts.new(hit + nrm * (lift + bl))
    uvl = bm.loops.layers.uv.new('Tile')
    for i in range(nx):
        for j in range(ny):
            f = bm.faces.new((grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]))
            for loop, (a, b) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                loop[uvl].uv = (a / nx * size[0], b / ny * size[1])    # metres across the patch
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    if bm.faces[0].normal.dot(out) < 0:                # facing out
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    return new_object(name, bm)


def ring(target, z, center_xy, radius_guess, lift, samples=48):
    """Points round a body part at height z, on the target surface (rays from the middle outwards)."""
    tb = bvh_of(target)
    pts = []
    c = Vector((center_xy[0], center_xy[1], z))
    for k in range(samples):
        a = k / samples * math.tau
        d = Vector((math.cos(a), math.sin(a), 0.0))
        hit, nrm, _i, _d = tb.ray_cast(c + d * (radius_guess * 2.5), -d, radius_guess * 2.5)
        if hit is None:
            hit = c + d * radius_guess
        pts.append(hit + d * lift)
    return pts


def band(name, rings_pts, close=True):
    """A strip between rings of points (each a closed loop of the same count)."""
    bm = bmesh.new()
    rows = [[bm.verts.new(p) for p in r] for r in rings_pts]
    n = len(rows[0])
    for a, b in zip(rows, rows[1:]):
        for i in range(n if close else n - 1):
            bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
    bm.normal_update()
    return new_object(name, bm)


def box(name, center, size, rot=Matrix.Identity(3), bevel=0.004):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = rot @ Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + center
    if bevel:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=1, affect='EDGES')
    return new_object(name, bm)


def join(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs):
        bpy.ops.object.join()
    objs[0].name = name
    return objs[0]


# ------------------------------------------------------------------ the pieces

def trousers():
    def keep(i):
        z = BODY_P[i][2]
        return (DOM[i] in LEGS or DOM[i] == 'pelvis' or (DOM[i] == 'spine_01' and z < 1.04)) and 0.2 < z < 1.04

    def offset(i):
        z = BODY_P[i][2]
        # roomier towards the knee and bloused over the boots
        return 0.012 + 0.008 * smooth01((0.75 - z) / 0.25) + 0.008 * smooth01((0.4 - z) / 0.15)
    t = shell('Trousers', keep, offset)
    relax(t, 4, lambda co: 0.01)
    subdivide(t)
    relax(t, 1, lambda co: 0.01)
    drape(t, 28, lambda co: 1.0 if co.z > 0.95 else 0.0, [body], mass=0.035, tension=25, bending=1.0)
    clear_of_obj(t, 0.008, [body_bvh])
    rim(t, 0.008)
    return t


def jacket(under):
    def keep0(i):
        z, x = BODY_P[i][2], abs(BODY_P[i][0])
        d = DOM[i]
        if d in ARMS:
            return x < 0.682                           # to the wrist
        if d in TORSO:
            return z >= 0.975
        if d == 'neck_01':
            return z < 1.5
        return False

    def keep(i):
        x, y, z = BODY_P[i]
        # under the scarf's roll round the neck there is no collar to poke through it
        return keep0(i) and not (z > 1.455 and abs(x) < 0.15)

    def offset(i):
        x = abs(BODY_P[i][0])
        return 0.014 + (0.006 if 0.25 < x else 0.0)    # sleeves a little looser
    j = shell('Jacket', keep, offset)
    relax(j, 5, lambda co: 0.012)
    # the skirt: from the waist down over the hips and the trousers' seat, flaring a little, the hem level all round
    bm = bmesh.new()
    bm.from_mesh(j.data)
    bm.normal_update()
    for loop in boundary_loops(bm, lambda mid, loop: mid.z < 1.1):
        def step(co, r):
            radial = Vector((co.x, co.y + 0.02, 0.0))
            radial = radial.normalized() if radial.length > 1e-6 else Vector((0, 0, 0))
            target = (0.935, 0.895, 0.855, 0.815)[r]
            return Vector((0, 0, target - co.z)) + radial * 0.007
        extend_loop(bm, loop, 4, step)
    bm.to_mesh(j.data)
    bm.free()
    clear_of_obj(j, 0.012, [body_bvh, bvh_of(under)])
    subdivide(j)
    relax(j, 1, lambda co: 0.012, [body_bvh, bvh_of(under)])

    # the sleeves and the trunk held (the T-pose), the skirt hangs over the hips and folds
    def pin(co):
        if abs(co.x) > 0.17:
            return 1.0
        return 1.0 if co.z > 1.0 else smooth01((co.z - 0.96) / 0.04)
    drape(j, 30, pin, [body, under], mass=0.04, tension=25, bending=1.2)
    clear_of_obj(j, 0.01, [body_bvh, bvh_of(under)])
    rim(j, 0.008)
    return j


def hood(under):
    """The hood: a sphere's even net shrunk onto the head 2 cm out and smoothed (no ears or jaw showing through),
    down round the neck, an opening for the eyes and the brows; the mask over the nose, the mouth and the
    chin is a thin layer of its own (mask())."""
    head_pts = BODY_P[[i for i in range(len(DOM)) if DOM[i] == 'Head']]
    c = Vector(((head_pts[:, 0].min() + head_pts[:, 0].max()) / 2, (head_pts[:, 1].min() + head_pts[:, 1].max()) / 2,
                (head_pts[:, 2].min() + head_pts[:, 2].max()) / 2 + 0.01))
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=40, v_segments=28, radius=0.2)
    for v in bm.verts:
        v.co += c
    # each point onto the head (or the neck) along the ray from the middle, then 2 cm out
    for v in bm.verts:
        d = (v.co - c).normalized()
        if d.z < -0.55:                                 # under the jaw: straight in towards the neck's axis
            flat = Vector((d.x, d.y, 0))
            d = flat.normalized() if flat.length > 1e-4 else Vector((0, 1, 0))
            origin = Vector((c.x, c.y - 0.03, v.co.z))
        else:
            origin = c
        hit, _nrm, _i, _d = body_bvh.ray_cast(origin + d * 0.4, -d, 0.4)
        if hit is None:
            hit = origin + d * 0.1
        v.co = hit + d * 0.02
        # the ears do not show through: no wider than the skull
        if abs(v.co.x - c.x) > 0.098:
            v.co.x = c.x + math.copysign(0.098, v.co.x - c.x)
    # below the neck's middle it goes no further (the scarf covers the rest)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < 1.49], context='VERTS')
    # the face opening: the eyes and the brows (the mask covers its lower part)
    ex, ez = EYE_Z

    def in_face(co):
        return co.y > c.y and abs(co.x) < 0.082 and ex - 0.1 < co.z < ez + 0.004
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(in_face(v.co) for v in f.verts) or in_face(f.calc_center_median())], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    h = new_object('Hood', bm)
    despike(h, c, 4)
    smooth_boundary(h, 6, 0.5)
    relax(h, 8, lambda co: 0.018, factor=0.5)
    despike(h, c, 2)
    rim(h, 0.006)
    return h


def mask():
    """The face mask: from under the eyes over the nose, the mouth and the chin, close to the skin."""
    ex, _ez = EYE_Z

    def keep(i):
        x, y, z = BODY_P[i]
        return DOM[i] in HEAD and y > -0.005 and 1.55 < z < ex + 0.004 and abs(x) < 0.095
    m = shell('Mask', keep, lambda i: 0.007)
    smooth_boundary(m, 6, 0.5)
    relax(m, 3, lambda co: 0.006, factor=0.35)
    rim(m, 0.004)
    return m


def scarf_wrap(under):
    """The scarf wound round the neck: a thick soft roll lying on the shoulders (the turns a little uneven),
    and its cloth down the back to the shoulder blades."""
    neck = BONE['neck_01']
    centre = Vector((0.0, neck.y, 1.49))
    bm = bmesh.new()
    seg, around = 48, 12
    rings = []
    for i in range(seg):
        a = i / seg * math.tau
        radial = Vector((math.sin(a), math.cos(a), 0))
        # wider over the shoulders than front and back, a turn's lump here and there
        big = 0.105 + 0.03 * abs(math.sin(a)) + 0.008 * math.sin(a * 3 + 0.7)
        tube = 0.034 + 0.008 * math.sin(a * 2 + 1.3) + 0.005 * math.sin(a * 5)
        row = []
        for k in range(around):
            b = k / around * math.tau
            p = centre + radial * (big + tube * math.cos(b)) + Vector((0, 0, tube * math.sin(b) * 0.85 - 0.012 * abs(math.sin(a))))
            row.append(bm.verts.new(p))
        rings.append(row)
    for i in range(seg):
        for k in range(around):
            a, b = rings[i][k], rings[i][(k + 1) % around]
            c2, d = rings[(i + 1) % seg][(k + 1) % around], rings[(i + 1) % seg][k]
            bm.faces.new((a, b, c2, d))
    bm.normal_update()
    roll = new_object('ScarfWrap', bm)
    clear_of_obj(roll, 0.01, [body_bvh, bvh_of(under)])
    relax(roll, 5, lambda co: 0.01, [body_bvh, bvh_of(under)], factor=0.5)

    # the cloth down the back
    def keep(i):
        x, y, z = BODY_P[i]
        if DOM[i] not in ('spine_03', 'spine_02', 'neck_01', 'clavicle_l', 'clavicle_r'):
            return False
        return y < -0.03 and abs(x) < 0.17 and 1.26 + 0.7 * abs(x) ** 1.3 < z < 1.5
    back = shell('ScarfBack', keep, lambda i: 0.03)
    smooth_boundary(back, 12, 0.6)
    relax(back, 8, lambda co: 0.026, [body_bvh, bvh_of(under)], factor=0.55)
    rim(back, 0.006)
    return join('ScarfWrap', [roll, back])


def scarf_tail(under, extra=()):
    """The scarf's end down the right of the chest (to the belt), a little twisted."""
    bm = bmesh.new()
    tb = bvh_of(under, *extra)
    rows = []
    for j in range(13):
        z = 1.47 - j * 0.04
        w = 0.17 - 0.01 * math.sin(j * 0.5)
        row = []
        for i in range(5):
            x = 0.035 + (i / 4) * w + 0.012 * math.sin(j * 0.7)
            p = Vector((x, 0.3, z))
            hit, nrm, _i, _d = tb.ray_cast(p, Vector((0, -1, 0)), 0.6)
            if hit is None:
                hit, nrm = Vector((x, 0.12, z)), Vector((0, 1, 0))
            row.append(bm.verts.new(hit + nrm * (0.02 + 0.008 * math.sin(i / 4 * math.pi))))
        rows.append(row)
    for a, b in zip(rows, rows[1:]):
        for i in range(4):
            bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    if bm.faces[0].normal.y < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    t = new_object('ScarfTail', bm)
    rim(t, 0.006)
    return t


def boots():
    def keep(i):
        return (DOM[i] in FEET or DOM[i] in ('calf_l', 'calf_r')) and BODY_P[i][2] < 0.27
    b = shell('Boots', keep, lambda i: 0.012)
    relax(b, 4, lambda co: 0.01, factor=0.6)
    bm = bmesh.new()
    bm.from_mesh(b.data)
    # each foot's slices made convex: the leather spans the gaps between the toes and over the arch (every
    # vertex inside its slice's convex hull is moved out along the ray from the slice's middle to the hull)
    for side in (-1, 1):
        vs = [v for v in bm.verts if v.co.x * side > 0]
        bins = {}
        for v in vs:
            bins.setdefault(int(math.floor(v.co.z / 0.012)), []).append(v)
        for k, members in bins.items():
            pts = [(w.co.x, w.co.y) for w in members] + [(w.co.x, w.co.y) for w in bins.get(k - 1, []) + bins.get(k + 1, [])]
            hull = convex_hull(pts)
            if len(hull) < 3:
                continue
            cx = sum(p[0] for p in hull) / len(hull)
            cy = sum(p[1] for p in hull) / len(hull)
            for v in members:
                dx, dy = v.co.x - cx, v.co.y - cy
                r = math.hypot(dx, dy)
                if r < 1e-6:
                    continue
                reach = ray_to_hull(cx, cy, dx / r, dy / r, hull)
                if reach is not None and reach > r:
                    v.co.x, v.co.y = cx + dx / r * reach, cy + dy / r * reach
    for v in bm.verts:
        if v.co.z < 0.03:
            v.co.z = -0.008 + max(0.0, v.co.z) * 0.25     # the sole: flat
    bm.to_mesh(b.data)
    bm.free()
    relax(b, 2, lambda co: 0.01, factor=0.4)
    rim(b, 0.008)
    return b


def gloves():
    def keep(i):
        d = DOM[i]
        return (d.startswith('hand_') or d in FINGER_BASE) and abs(BODY_P[i][0]) > 0.66
    g = shell('Gloves', keep, lambda i: 0.0035)
    rim(g, 0.004)
    return g


def belt_and_pouches(jacket_ob):
    parts = []
    rings_pts = [ring(jacket_ob, WAIST + dz, (0.0, -0.02), 0.17, 0.006) for dz in (-0.024, 0.024)]
    b = band('Belt', rings_pts)
    rim(b, 0.006)
    parts.append(b)
    front = max(rings_pts[0], key=lambda p: p.y)
    parts.append(box('Buckle', Vector((0.0, front.y + 0.012, WAIST)), (0.075, 0.016, 0.055)))
    # pouches round the front and the sides (double magazine pouches)
    for ang in (-62, -36, 36, 62):
        a = math.radians(90 + ang)
        d = Vector((math.cos(a), math.sin(a), 0))
        hit = max(rings_pts[0], key=lambda p: (p - Vector((0, -0.02, p.z))).normalized().dot(d))
        rot = Matrix.Rotation(math.atan2(d.y, d.x) - math.pi / 2, 3, 'Z')
        center = Vector((hit.x, hit.y, WAIST - 0.035)) + d * 0.026
        parts.append(box('Pouch', center, (0.075, 0.04, 0.115), rot, 0.006))
        parts.append(box('PouchFlap', center + d * 0.004 + Vector((0, 0, 0.05)), (0.08, 0.044, 0.025), rot, 0.004))
    return parts


def knee_pads(trousers_ob):
    parts = []
    for side in ('_l', '_r'):
        k = BONE['calf' + side]
        center = Vector((k.x, k.y + 0.1, k.z - 0.01))
        pad = patch('KneePad', trousers_ob, center, Vector((0, 1, 0)), Vector((0, 0, 1)), (0.11, 0.15), (6, 8), 0.016, bulge=0.02)
        rim(pad, 0.012)
        parts.append(pad)
        for dz in (0.06, -0.07):
            pts = ring(trousers_ob, k.z + dz, (k.x, k.y), 0.08, 0.004, 32)
            strap = band('KneeStrap', [[p - Vector((0, 0, 0.012)) for p in pts], [p + Vector((0, 0, 0.012)) for p in pts]])
            rim(strap, 0.003)
            parts.append(strap)
    return parts


def pockets(jacket_ob, trousers_ob):
    parts = []
    # the chest pockets' flaps, slanting a little
    for sx in (-1, 1):
        parts.append(patch('ChestFlap', jacket_ob, Vector((0.085 * sx, 0.2, 1.33)), Vector((0, 1, 0)), Vector((0.12 * sx, 0, 1)), (0.12, 0.05), (4, 2), 0.004))
        parts.append(patch('ChestPocket', jacket_ob, Vector((0.085 * sx, 0.2, 1.27)), Vector((0, 1, 0)), Vector((0.12 * sx, 0, 1)), (0.12, 0.13), (4, 4), 0.002, bulge=0.006))
    # the sleeves' pockets with the flag patch on them (the top of the arm in the T-pose is its outer side;
    # the patch's top towards the shoulder)
    for sx in (-1, 1):
        at = Vector((0.31 * sx, BONE['upperarm_l'].y - 0.01, 1.56))
        parts.append(patch('SleevePocket', jacket_ob, at, Vector((0, -0.3, 1)), Vector((-sx, 0, 0)), (0.085, 0.12), (4, 5), 0.003, bulge=0.004))
        parts.append(patch('Flag', jacket_ob, at + Vector((-0.012 * sx, 0, 0)), Vector((0, -0.3, 1)), Vector((-sx, 0, 0)), (0.07, 0.047), (3, 2), 0.0085))
    # the cargo pockets on the thighs, with their flaps
    for sx in (-1, 1):
        th = BONE['thigh' + ('_l' if sx < 0 else '_r')]
        at = Vector((th.x + 0.2 * sx, th.y, 0.66))
        parts.append(patch('CargoPocket', trousers_ob, at, Vector((sx, 0.15, 0)), Vector((0, 0, 1)), (0.13, 0.18), (5, 6), 0.004, bulge=0.012))
        parts.append(patch('CargoFlap', trousers_ob, at + Vector((0, 0, 0.085)), Vector((sx, 0.15, 0)), Vector((0, 0, 1)), (0.14, 0.05), (5, 2), 0.018))
    return parts


# ------------------------------------------------------------------ materials (tiled at their real size)

def image(name):
    path = os.path.join(TEX, name + '.jpg')
    if not os.path.exists(path):
        path = os.path.join(TEX, name + '.png')
    return bpy.data.images.load(path, check_existing=True)


def material(name, tex, tile, tint=(1, 1, 1), rough=0.8, normal_strength=1.0, metallic=0.0, tile_y=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes['Principled BSDF']
    uv = nt.nodes.new('ShaderNodeUVMap')
    uv.uv_map = 'Tile'
    mapping = nt.nodes.new('ShaderNodeMapping')
    mapping.inputs['Scale'].default_value = (1.0 / tile, 1.0 / (tile_y or tile), 1)
    nt.links.new(uv.outputs['UV'], mapping.inputs['Vector'])
    it = nt.nodes.new('ShaderNodeTexImage')
    it.image = image(tex)
    nt.links.new(mapping.outputs['Vector'], it.inputs['Vector'])
    mix = nt.nodes.new('ShaderNodeMix')
    mix.data_type = 'RGBA'
    mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 1.0
    mix.inputs['B'].default_value = (*tint, 1)
    nt.links.new(it.outputs['Color'], mix.inputs['A'])
    nt.links.new(mix.outputs['Result'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = rough
    bsdf.inputs['Metallic'].default_value = metallic
    nrm_path = os.path.join(TEX, tex + '_n.png')
    if os.path.exists(nrm_path):
        nimg = bpy.data.images.load(nrm_path, check_existing=True)
        nimg.colorspace_settings.name = 'Non-Color'
        nt2 = nt.nodes.new('ShaderNodeTexImage')
        nt2.image = nimg
        nt.links.new(mapping.outputs['Vector'], nt2.inputs['Vector'])
        nm = nt.nodes.new('ShaderNodeNormalMap')
        nm.uv_map = 'Tile'
        nm.inputs['Strength'].default_value = normal_strength
        nt.links.new(nt2.outputs['Color'], nm.inputs['Color'])
        nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
    return m


MATS = {}
PATCHES = {'ChestFlap', 'ChestPocket', 'SleevePocket', 'Flag', 'CargoPocket', 'CargoFlap', 'KneePad'}


def setup_materials():
    # the camo a little darker and more olive, as on the reference sheet
    MATS['camo'] = material('Camo', 'camo', 0.42, tint=(0.84, 0.86, 0.76), rough=0.85)
    MATS['knit'] = material('Knit', 'knit', 0.16, tint=(0.75, 0.75, 0.78), rough=0.95)
    MATS['cordura'] = material('Cordura', 'cordura', 0.18, rough=0.8)
    MATS['leather'] = material('Leather', 'leather', 0.2, rough=0.55)
    MATS['plastic'] = material('Plastic', 'plastic', 0.2, rough=0.6)
    MATS['webbing'] = material('Webbing', 'cordura', 0.12, tint=(0.22, 0.22, 0.2), rough=0.85)
    MATS['flag'] = material('Flag', 'flag', 0.07, rough=0.7, tile_y=0.047)    # its picture over the 7 x 4.7 cm patch
    MATS['metal'] = material('Metal', 'plastic', 0.2, tint=(0.25, 0.25, 0.25), rough=0.35, metallic=0.8)


def tile_uv(ob):
    """UVs at real size (1 unit = 1 m) for the tiled cloth (a patch brings its own)."""
    me = ob.data
    if 'Tile' in me.uv_layers and len(me.uv_layers) == 1 and ob.name.split('.')[0] in PATCHES:
        return
    if 'Tile' not in me.uv_layers:
        me.uv_layers.new(name='Tile')
    me.uv_layers.active = me.uv_layers['Tile']
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # scale to metres: the UV area against the mesh area
    area3 = sum(p.area for p in me.polygons)
    uvl = me.uv_layers['Tile'].data
    area2 = 0.0
    for p in me.polygons:
        pts = [uvl[li].uv for li in p.loop_indices]
        for k in range(1, len(pts) - 1):
            a, b, c = pts[0], pts[k], pts[k + 1]
            area2 += abs((b - a).cross(c - a)) / 2
    s = math.sqrt(area3 / max(area2, 1e-9))
    for d in uvl:
        d.uv = d.uv * s


def dress(ob, mat_key):
    ob.data.materials.clear()
    ob.data.materials.append(MATS[mat_key])
    tile_uv(ob)


# ------------------------------------------------------------------ weights (from the body), loose parts onto the trunk

def weights_from_body(ob, blend=None):
    """The body's weights on every vertex (nearest face, interpolated); blend(co) -> [(bone, share)] mixes some
    of the trunk's in (0..1 of the vertex) where the cloth hangs free."""
    for vg in body.vertex_groups:
        if vg.name not in ob.vertex_groups:
            ob.vertex_groups.new(name=vg.name)
    dt = ob.modifiers.new('W', 'DATA_TRANSFER')
    dt.object, dt.use_object_transform = body, True
    dt.use_vert_data, dt.data_types_verts = True, {'VGROUP_WEIGHTS'}
    dt.vert_mapping = 'POLYINTERP_NEAREST'
    dt.layers_vgroup_select_src, dt.layers_vgroup_select_dst = 'ALL', 'NAME'
    bpy.context.view_layer.objects.active = ob
    with bpy.context.temp_override(object=ob, active_object=ob):
        bpy.ops.object.modifier_apply(modifier='W')
    if blend:
        for v in ob.data.vertices:
            mix = blend(ob.matrix_world @ v.co)
            if not mix:
                continue
            share = sum(s for _b, s in mix)
            for g in v.groups:
                g.weight *= (1 - share)
            for bone, s in mix:
                ob.vertex_groups[bone].add([v.index], s, 'ADD')


def skirt_blend(co):
    """The jacket below the waist: mostly the pelvis (it swings with the hips, not stretched by the thighs)."""
    if co.z > WAIST:
        return None
    k = smooth01((WAIST - co.z) / 0.12)
    return [('pelvis', 0.7 * k)]


def cowl_blend(co):
    if co.z > 1.6:
        return None
    k = smooth01((1.6 - co.z) / 0.12)
    return [('spine_03', 0.55 * k), ('neck_01', 0.2 * k)]


def tail_blend(co):
    return [('spine_03', 0.6), ('spine_02', 0.3)]


# ------------------------------------------------------------------ one mesh, the atlas, the bake, the files

BUDGET = 17000          # triangles for the whole outfit (the head and the hands are the body's own)


def finish(pieces):
    """All the pieces in one mesh (one material in the game), cut to the budget where it shows least."""
    team = join('Team1', pieces)
    me = team.data
    # soft cloth: normals smoothed across the faces, kept sharp only at real edges (the pouches' and the
    # buckle's corners, the rims of the hems)
    bpy.ops.object.select_all(action='DESELECT')
    team.select_set(True)
    bpy.context.view_layer.objects.active = team
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(55))
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    if tris > BUDGET:
        dec = team.modifiers.new('Budget', 'DECIMATE')
        dec.decimate_type = 'COLLAPSE'
        dec.ratio = BUDGET / tris
        dec.use_collapse_triangulate = True
        dec.delimit = {'UV', 'MATERIAL', 'SEAM'}
        bpy.context.view_layer.objects.active = team
        with bpy.context.temp_override(object=team, active_object=team):
            bpy.ops.object.modifier_apply(modifier='Budget')
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print('outfit triangles', tris, 'vertices', len(me.vertices))
    # the atlas: the tiled UVs' islands packed into one square, the same size on the cloth everywhere
    me.uv_layers.active = me.uv_layers['Tile']
    atlas = me.uv_layers.new(name='Atlas', do_init=True)
    me.uv_layers.active = atlas
    bpy.ops.object.select_all(action='DESELECT')
    team.select_set(True)
    bpy.context.view_layer.objects.active = team
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=0.0035)
    bpy.ops.object.mode_set(mode='OBJECT')
    return team


def bake(team):
    """The atlas: the cloths' colours, the shadow in the folds and under the straps (ambient occlusion, the
    body casting it too), and the weave's normals."""
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    body.hide_render = False
    col = bpy.data.images.new('Team1_col', 2048, 2048)
    ao = bpy.data.images.new('Team1_ao', 1024, 1024)
    nrm = bpy.data.images.new('Team1_n', 1024, 1024)
    nrm.colorspace_settings.name = 'Non-Color'

    def target(img):
        for m in team.data.materials:
            nt = m.node_tree
            node = nt.nodes.get('BakeTarget') or nt.nodes.new('ShaderNodeTexImage')
            node.name = 'BakeTarget'
            node.image = img
            for n in nt.nodes:
                n.select = False
            node.select = True
            nt.nodes.active = node
    bpy.ops.object.select_all(action='DESELECT')
    team.select_set(True)
    bpy.context.view_layer.objects.active = team
    target(col)
    scene.cycles.samples = 1
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=6)
    target(ao)
    scene.cycles.samples = 48
    scene.world = scene.world or bpy.data.worlds.new('W')
    bpy.ops.object.bake(type='AO', margin=6)
    target(nrm)
    scene.cycles.samples = 1
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', margin=6)
    # the colour darkened by the occlusion (softly: the game lights it as well)
    c = np.array(col.pixels[:], dtype=np.float32).reshape(2048, 2048, 4)
    a = np.array(ao.pixels[:], dtype=np.float32).reshape(1024, 1024, 4)[..., 0]
    a = np.kron(a, np.ones((2, 2), dtype=np.float32))
    c[..., :3] *= (0.42 + 0.58 * a)[..., None]
    out = bpy.data.images.new('Team1_out', 2048, 2048)
    out.pixels = c.ravel()
    out.filepath_raw = os.path.join(OUT, 'Team1.jpg')
    out.file_format = 'JPEG'
    scene.render.image_settings.quality = 92
    out.save()
    nrm.filepath_raw = os.path.join(OUT, 'Team1_n.png')
    nrm.file_format = 'PNG'
    nrm.save()
    print('baked', os.path.join(OUT, 'Team1.jpg'))


def export(team):
    """The mesh in the game's axes (x right, y up, z forward: Blender's x, z, y), atlas UVs, per corner normals;
    the weights per 'v'; the joints."""
    me = team.data
    W = team.matrix_world
    me.calc_loop_triangles()
    uv = me.uv_layers['Atlas'].data
    names = {g.index: g.name for g in team.vertex_groups}
    bones = set(b.name for b in arm.data.bones)

    def axes(p):
        return (p[0], p[2], p[1])
    with open(os.path.join(OUT, 'Team1.obj'), 'w') as f:
        f.write('# TEAM 1 (Tools/team1/build.py): x right, y up, z forward, metres\n')
        for v in me.vertices:
            p = axes(W @ v.co)
            f.write(f'v {p[0]:.5f} {p[1]:.5f} {p[2]:.5f}\n')
        normals = me.corner_normals
        # each distinct uv and normal once
        uvs, uv_index, nrms, n_index = {}, {}, {}, {}
        for t in me.loop_triangles:
            for li in t.loops:
                u = uv[li].uv
                ku = (round(u[0], 5), round(u[1], 5))
                uv_index[li] = uvs.setdefault(ku, len(uvs))
                n = axes((W.to_3x3() @ normals[li].vector).normalized())
                kn = (round(n[0], 3), round(n[1], 3), round(n[2], 3))
                n_index[li] = nrms.setdefault(kn, len(nrms))
        for ku in uvs:
            f.write(f'vt {ku[0]:.5f} {ku[1]:.5f}\n')
        for kn in nrms:
            f.write(f'vn {kn[0]:.3f} {kn[1]:.3f} {kn[2]:.3f}\n')
        for t in me.loop_triangles:
            a, b, c = t.vertices
            la, lb, lc = t.loops
            f.write(f'f {a + 1}/{uv_index[la] + 1}/{n_index[la] + 1} {b + 1}/{uv_index[lb] + 1}/{n_index[lb] + 1} {c + 1}/{uv_index[lc] + 1}/{n_index[lc] + 1}\n')
    with open(os.path.join(OUT, 'Team1_weights.txt'), 'w') as f:
        for v in me.vertices:
            ws = sorted(((names[g.group], g.weight) for g in v.groups if names[g.group] in bones and g.weight > 1e-3), key=lambda x: -x[1])[:4]
            total = sum(w for _n, w in ws) or 1.0
            f.write(' '.join(f'{n} {w / total:.4f}' for n, w in ws) + '\n')
    joints = {b.name: [round(x, 5) for x in axes(arm.matrix_world @ b.head_local)] for b in arm.data.bones}
    with open(os.path.join(OUT, 'Team1_bones.json'), 'w') as f:
        json.dump(joints, f, indent=0)
    print('exported', len(me.vertices), 'vertices', len(me.loop_triangles), 'triangles')


# ------------------------------------------------------------------ build

def main():
    trousers_ob = trousers()
    jacket_ob = jacket(trousers_ob)
    hood_ob = hood(jacket_ob)
    mask_ob = mask()
    wrap_ob = scarf_wrap(jacket_ob)
    pocket_parts = pockets(jacket_ob, trousers_ob)
    tail_ob = scarf_tail(jacket_ob, [p for p in pocket_parts if p.name.startswith('Chest')])
    boots_ob = boots()
    gloves_ob = gloves()
    belt_parts = belt_and_pouches(jacket_ob)
    pad_parts = knee_pads(trousers_ob)

    setup_materials()
    dress(trousers_ob, 'camo')
    dress(jacket_ob, 'camo')
    for ob in (hood_ob, mask_ob, wrap_ob, tail_ob):
        dress(ob, 'knit')
    dress(boots_ob, 'leather')
    dress(gloves_ob, 'leather')
    for p in belt_parts:
        dress(p, 'webbing' if p.name.startswith('Belt') else 'metal' if p.name.startswith('Buckle') else 'cordura')
    for p in pad_parts:
        dress(p, 'plastic' if p.name.startswith('KneePad') else 'webbing')
    for p in pocket_parts:
        dress(p, 'flag' if p.name.startswith('Flag') else 'camo')

    weights_from_body(trousers_ob)
    weights_from_body(jacket_ob, skirt_blend)
    weights_from_body(hood_ob, lambda co: [('Head', 0.6)] if co.z > 1.56 else [('neck_01', 0.5)])
    weights_from_body(mask_ob)
    weights_from_body(wrap_ob, cowl_blend)
    weights_from_body(tail_ob, tail_blend)
    weights_from_body(boots_ob)
    weights_from_body(gloves_ob)
    for p in belt_parts:
        weights_from_body(p, lambda co: [('pelvis', 0.85)])
    for p in pad_parts + pocket_parts:
        weights_from_body(p, (lambda co: [('pelvis', 0.6)]) if 'Cargo' in p.name else None)

    pieces = [trousers_ob, jacket_ob, hood_ob, mask_ob, wrap_ob, tail_ob, boots_ob, gloves_ob] + belt_parts + pad_parts + pocket_parts
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in pieces)
    print('pieces', len(pieces), 'triangles', tris)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'Team1_parts.blend'))
    team = finish(pieces)
    if BAKE:
        bake(team)
    export(team)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'Team1.blend'))


if __name__ == '__main__':
    main()
