# A character rigged on Mixamo (FBX, T-pose) into the game's pack format:
#   <out>/<key>.obj            the mesh at rest (metres, feet at 0, facing +Z), one 'v' per FBX vertex
#   <out>/<key>_weights.txt    per 'v': up to 4 "MixamoBone weight" pairs (Mixamo's own skinning)
#   <out>/<key>_skeleton.json  every Mixamo joint where it is (the pack's skeleton format, x mirrored)
# Loose crumbs (small pieces standing off the body) are dropped.
#   python3 mx2pack.py <fbx> <key> <out> [height]
import bpy, bmesh, sys, os, json
import numpy as np
from mathutils import Vector
fbx, key, out = sys.argv[-4], sys.argv[-3], sys.argv[-2]
H = float(sys.argv[-1])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
arm.data.pose_position = 'REST'
if arm.animation_data: arm.animation_data.action = None
bpy.context.view_layer.update()
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
dg = bpy.context.evaluated_depsgraph_get()
P, UV, N, W, F = [], [], [], [], []
for o in meshes:
    e = o.evaluated_get(dg); me = e.to_mesh()
    base = len(P)
    names = {g.index: g.name for g in o.vertex_groups}
    for v in o.data.vertices:
        ws = sorted(((names[g.group], g.weight) for g in v.groups if g.weight > 1e-4), key=lambda x: -x[1])[:4]
        W.append(ws)
    for v in me.vertices: P.append(e.matrix_world @ v.co)
    me.calc_loop_triangles()
    uvl = me.uv_layers.active.data if me.uv_layers else None
    for t in me.loop_triangles:
        F.append([(base + vi, tuple(uvl[li].uv) if uvl else (0, 0), tuple((e.matrix_world.to_3x3() @ me.corner_normals[li].vector).normalized())) for li, vi in zip(t.loops, t.vertices)])
    e.to_mesh_clear()
P = np.array([p[:] for p in P])
J = {b.name.split(':')[-1]: np.array((arm.matrix_world @ b.head_local)[:]) for b in arm.data.bones}
parent = {b.name.split(':')[-1]: (b.parent.name.split(':')[-1] if b.parent else None) for b in arm.data.bones}
# frame: Z up, facing -Y (Mixamo's front), feet at 0, the given height, the hips over the origin
lo = P[:, 2].min(); s = H / (P[:, 2].max() - lo)
c = np.array([J['Hips'][0], J['Hips'][1], lo])
P = (P - c) * s
J = {k: (v - c) * s for k, v in J.items()}
# loose crumbs: pieces under 60 vertices standing more than 4 cm off every other piece
par = list(range(len(P)))
def find(a):
    while par[a] != a: par[a] = par[par[a]]; a = par[a]
    return a
for f in F:
    for x in f[1:]:
        a, b = find(f[0][0]), find(x[0])
        if a != b: par[a] = b
roots = np.array([find(i) for i in range(len(P))])
ids, counts = np.unique(roots, return_counts=True)
big = ids[counts >= 60]
bigpts = P[np.isin(roots, big)][::3]
drop = set()
for r, n in zip(ids, counts):
    if n >= 60: continue
    pts = P[roots == r]
    d = np.min(np.linalg.norm(bigpts[None, :, :] - pts[:, None, :], axis=2))
    if d > 0.04: drop.add(r)
F = [f for f in F if roots[f[0][0]] not in drop]
print(key, 'crumbs dropped', len(drop), 'faces', len(F), 'verts', len(P))
# OBJ: Blender (x, y, z) -> OBJ (x, z, -y)
uvs, vts, ns, vns, lines = {}, [], {}, [], []
for f in F:
    idx = []
    for vi, uv, n in f:
        u = (round(uv[0], 5), round(uv[1], 5))
        if u not in uvs: uvs[u] = len(vts) + 1; vts.append(u)
        nn = (round(n[0], 3), round(n[2], 3), round(-n[1], 3))
        if nn not in ns: ns[nn] = len(vns) + 1; vns.append(nn)
        idx.append(f'{vi + 1}/{uvs[u]}/{ns[nn]}')
    lines.append('f ' + ' '.join(idx))
os.makedirs(out, exist_ok=True)
with open(os.path.join(out, key + '.obj'), 'w') as fo:
    fo.write(f'# {key}: rigged on Mixamo (T-pose, metres, feet at 0, facing +Z); its weights in {key}_weights.txt\n')
    fo.write('\n'.join(f'v {p[0]:.5f} {p[2]:.5f} {-p[1]:.5f}' for p in P) + '\n')
    fo.write('\n'.join(f'vt {u[0]:.5f} {u[1]:.5f}' for u in vts) + '\n')
    fo.write('\n'.join(f'vn {n[0]} {n[1]} {n[2]}' for n in vns) + '\n')
    fo.write('\n'.join(lines) + '\n')
with open(os.path.join(out, key + '_weights.txt'), 'w') as fw:
    fw.write('# one line per OBJ vertex: Mixamo bone and weight, up to four\n')
    for ws in W:
        t = sum(w for _, w in ws) or 1
        fw.write(' '.join(f'{n.split(":")[-1]} {w / t:.4f}' for n, w in ws) + '\n')
bones = []
for b in arm.data.bones:
    n = b.name.split(':')[-1]; p = J[n]
    x, y, z = p[0], p[2], -p[1]; x = -x       # OBJ space, then mirrored as the pack loads it
    bones.append({'name': 'mixamorig:' + n, 'parent': 'mixamorig:' + parent[n] if parent[n] else None, 'local_position': [0, 0, 0], 'local_rotation': [0, 0, 0, 1], 'local_scale': [1, 1, 1],
                  'bind_pose': [1, 0, 0, -x, 0, 1, 0, -y, 0, 0, 1, -z, 0, 0, 0, 1]})
json.dump({'character': key, 'bone_count': len(bones), 'bones': bones}, open(os.path.join(out, key + '_skeleton.json'), 'w'), indent=1)
# the textures of the FBX (a new character): its colour and normal maps
for o in meshes:
    for m in o.data.materials:
        if not (m and m.node_tree): continue
        for nd in m.node_tree.nodes:
            if nd.type == 'TEX_IMAGE' and nd.image:
                links = [l.to_socket.name for l in nd.outputs[0].links]
                print('TEX', nd.image.name, nd.image.size[:], links, nd.image.filepath)
