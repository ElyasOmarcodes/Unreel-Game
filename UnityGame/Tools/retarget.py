# Mixamo animations onto the game's skeleton (Quaternius UAL), by world-space turn of each bone from its
# T-pose: the same turn the Mixamo bone makes is given to the matching game bone. Writes, per clip, every
# frame's turns (in Unity's axes, as the game reads them) and the hips' move; renders frames to check.
#   python3 retarget.py <ual.fbx> <mixamo rest fbx> <out json> <render dir|-> <anim fbx>...
import bpy, sys, os, json, math
from mathutils import Matrix, Quaternion, Vector
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
ual_path, rest_path, out_json, render = argv[0], argv[1], argv[2], argv[3]
anims = argv[4:]
MAP = {'Hips': 'pelvis', 'Spine': 'spine_01', 'Spine1': 'spine_02', 'Spine2': 'spine_03', 'Neck': 'neck_01', 'Head': 'Head'}
for side, s in (('Left', 'l'), ('Right', 'r')):
    MAP.update({side + 'Shoulder': 'clavicle_' + s, side + 'Arm': 'upperarm_' + s, side + 'ForeArm': 'lowerarm_' + s, side + 'Hand': 'hand_' + s,
                side + 'UpLeg': 'thigh_' + s, side + 'Leg': 'calf_' + s, side + 'Foot': 'foot_' + s, side + 'ToeBase': 'ball_' + s})
    for f, g in (('Thumb', 'thumb'), ('Index', 'index'), ('Middle', 'middle'), ('Ring', 'ring'), ('Pinky', 'pinky')):
        for i in (1, 2, 3): MAP[f'{side}Hand{f}{i}'] = f'{g}_0{i}_{s}'
short = lambda n: n.split(':')[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
def load(path):
    before = set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    return next(o for o in new if o.type == 'ARMATURE'), new
ual, ual_objs = load(ual_path)
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
if ual.animation_data: ual.animation_data.action = None
rest_arm, rest_objs = load(rest_path)
def rot(m): return m.to_3x3().normalized().to_quaternion()
src_rest = {short(b.name): rot(rest_arm.matrix_world @ b.matrix_local) for b in rest_arm.data.bones}
src_rest_hips = rest_arm.matrix_world @ rest_arm.data.bones['mixamorig:Hips'].head_local
src_foot = min((rest_arm.matrix_world @ b.head_local).z for b in rest_arm.data.bones if 'Toe' in b.name)
for o in rest_objs: bpy.data.objects.remove(o)
dst_rest = {b.name: rot(ual.matrix_world @ b.matrix_local) for b in ual.data.bones}
pel = ual.data.bones['pelvis']
dst_rest_hips = ual.matrix_world @ pel.head_local
scale = (dst_rest_hips.z) / (src_rest_hips.z - src_foot)
A = Quaternion((0, 0, 1), math.pi)          # Mixamo faces -Y, the UAL +Y (Blender's axes)
def to_unity(q):
    # Blender (x right, y forward, z up) -> Unity (x right, y up, z forward): a mirror, so the axis turns too
    q = q.normalized()
    return [round(q.w, 4), round(-q.x, 4), round(-q.z, 4), round(-q.y, 4)]
order = [b for b in ual.data.bones]          # parents before children

# ---- the game's stance: upright and steady -------------------------------------------------------------
# Mixamo's rifle clips stand bent (knees, hips, back) and rock the hips from side to side. Of each bone of the
# trunk and legs, part of its mean turn over the clip is taken out (STRAIGHT: idle and walks more, runs less,
# crouches not at all) and the trunk's swing round its mean is made smaller (STEADY); the hips sway less to the
# sides; then the hips are set so the lowest foot is as high as before in every frame (no foot in the ground).
TRUNK = ['pelvis', 'spine_01', 'spine_02', 'spine_03']
LEGS = [f'{b}_{s}' for b in ('thigh', 'calf', 'foot') for s in 'lr']
def amounts(name):
    if 'crouch' in name: return 0.0, 0.3, 1.0, 0.0
    if 'idle' in name: return 0.75, 0.75, 1.0, 0.0
    if 'walk' in name: return 0.6, 0.85, 0.75, 0.5
    if 'run' in name or 'sprint' in name: return 0.4, 0.7, 0.45, 0.45
    return 0.0, 0.0, 1.0, 0.0
def mean(qs):
    acc = Quaternion((0, 0, 0, 0)); ref = qs[0]
    for q in qs:
        q = q if ref.dot(q) >= 0 else -q
        acc = Quaternion((acc.w + q.w, acc.x + q.x, acc.y + q.y, acc.z + q.z))
    return acc.normalized()
def slerp_id(q, t): return Quaternion().slerp(q, t)
def feet_low(fr, hip):
    world = {}
    low = 1e9
    for b in order:
        rest_rel = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
        parent_w = world[b.parent.name] if b.parent else ual.matrix_world
        base = parent_w @ rest_rel
        if b.name in fr:
            pos = dst_rest_hips + hip if b.name == 'pelvis' else base.translation
            world[b.name] = Matrix.Translation(pos) @ (fr[b.name] @ dst_rest[b.name]).to_matrix().to_4x4()
        else:
            world[b.name] = base
        if b.name.startswith(('foot_', 'ball_')): low = min(low, world[b.name].translation.z)
    return low
def shape(name, frames, hips):
    straight, straight_trunk, steady, sway = amounts(name)
    if straight == 0 and straight_trunk == 0 and steady == 1: return
    before = [feet_low(fr, h) for fr, h in zip(frames, hips)]
    for b in TRUNK + LEGS:
        if b not in frames[0]: continue
        m = mean([fr[b] for fr in frames])
        cut = straight_trunk if b in TRUNK else straight
        swing = steady if b in TRUNK else 1.0
        for fr in frames:
            r = m.inverted() @ fr[b]
            if r.w < 0: r = -r
            fr[b] = slerp_id(m, 1 - cut) @ slerp_id(r, swing)
    if sway > 0:
        mx = sum(h.x for h in hips) / len(hips)
        for h in hips: h.x = mx + (h.x - mx) * (1 - sway)
    for fr, h, low in zip(frames, hips, before):
        h.z += low - feet_low(fr, h)
result = {}
if render != '-':
    w = bpy.data.worlds.new('w'); bpy.context.scene.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[1].default_value = 2.0
for path in anims:
    name = os.path.splitext(os.path.basename(path))[0]
    src, src_objs = load(path)
    act = src.animation_data.action
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    frames, hips = [], []
    sc = bpy.context.scene
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        turns = {}
        for pb in src.pose.bones:
            n = short(pb.name)
            if n not in MAP: continue
            w = rot(src.matrix_world @ pb.matrix)
            turns[MAP[n]] = A @ (w @ src_rest[n].inverted()) @ A.inverted()
        h = src.matrix_world @ src.pose.bones['mixamorig:Hips'].head
        hips.append(A @ (h - src_rest_hips) * scale)
        frames.append(turns)
    for o in src_objs: bpy.data.objects.remove(o)
    shape(name, frames, hips)
    # in place: the hips' drift over the clip taken out along the ground (the game moves the body)
    n = len(hips); drift = hips[-1] - hips[0]
    speed = Vector((drift.x, drift.y, 0)).length / max(1e-6, (n - 1) / sc.render.fps)
    for i in range(n):
        k = i / max(1, n - 1)
        hips[i] = hips[i] - Vector((drift.x * k + hips[0].x, drift.y * k + hips[0].y, 0))
    result[name] = {'fps': sc.render.fps, 'speed': round(speed, 3),
                    'bones': sorted(frames[0].keys()),
                    'turns': [[x for b in sorted(fr.keys()) for x in to_unity(fr[b])] for fr in frames],
                    'hips': [[round(v.x, 4), round(v.z, 4), round(v.y, 4)] for v in hips]}
    print('CLIP', name, 'frames', n, 'speed', round(speed, 2), 'm/s')
    # the game skeleton posed at a few frames (check pictures)
    if render != '-':
        for fi in (0, n // 3, 2 * n // 3):
            for pb in ual.pose.bones:
                pb.rotation_mode = 'QUATERNION'
            world = {}
            for b in order:
                pb = ual.pose.bones[b.name]
                rest_rel = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
                parent_w = world[b.parent.name] if b.parent else ual.matrix_world
                base = parent_w @ rest_rel
                if b.name in frames[fi]:
                    wq = frames[fi][b.name] @ dst_rest[b.name]
                    target = Matrix.Translation(base.translation) @ wq.to_matrix().to_4x4()
                    if b.name == 'pelvis':
                        target = Matrix.Translation(dst_rest_hips + hips[fi]) @ wq.to_matrix().to_4x4()
                    basis = base.inverted() @ target
                    pb.rotation_quaternion = basis.to_quaternion(); pb.location = basis.translation
                    world[b.name] = base @ basis
                else:
                    pb.rotation_quaternion = Quaternion(); pb.location = (0, 0, 0)
                    world[b.name] = base
            bpy.context.view_layer.update()
            cam = bpy.data.cameras.new('c'); cam.type = 'ORTHO'; cam.ortho_scale = 2.3
            for vi, ang in enumerate((180, 90)):
                co = bpy.data.objects.new('c', cam); sc.collection.objects.link(co)
                a = math.radians(ang); co.location = Vector((6 * math.sin(a), -6 * math.cos(a), 0.95)); co.rotation_euler = (math.radians(90), 0, a)
                sc.camera = co; sc.render.engine = 'BLENDER_WORKBENCH' if False else 'CYCLES'; sc.cycles.samples = 4
                sc.render.resolution_x = 300; sc.render.resolution_y = 400
                sc.render.filepath = os.path.join(render, f'{name}_{fi}_{vi}.png'); bpy.ops.render.render(write_still=True)
# the game reads a plain text file: a header, then per clip its bones and one line per frame (each bone's turn
# w x y z in Unity's axes, then the hips' move x y z in metres for a body whose hips stand ual_hips high)
with open(out_json, 'w') as fo:
    fo.write('# Mixamo animations turned onto the game skeleton (tools: retarget.py)\n')
    fo.write(f'ual_hips {dst_rest_hips.z:.4f}\n')
    for name, r in result.items():
        fo.write(f"clip {name.replace(' ', '_')} {r['fps']} {r['speed']} {len(r['turns'])}\n")
        fo.write('bones ' + ' '.join(r['bones']) + '\n')
        for t, h in zip(r['turns'], r['hips']):
            fo.write(' '.join(f'{x:g}' for x in t) + ' ' + ' '.join(f'{x:g}' for x in h) + '\n')
