"""Steel City as numbers: the town's objects from Models/CityLord/scene.json (Unity coordinates, y up),
the kit pieces as CityLordBuilder.Kit makes them (turned back to their own axes, foot at y 0), and the
placing of a piece as CityLordBuilder.Place does it. Used to lay out the new yards and to look at
them in Blender before the game is built."""
import json, struct, os, math
import numpy as np

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Models', 'CityLord')
LIFT = 12.922
_scene = None
_meshes = {}
_kit = {}


def scene():
    global _scene
    if _scene is None:
        _scene = json.load(open(os.path.join(BASE, 'scene.json')))
    return _scene


def mesh(key):
    if key in _meshes:
        return _meshes[key]
    info = scene()['meshes'].get(key)
    if not info:
        _meshes[key] = None
        return None
    f = open(os.path.join(BASE, info['file']), 'rb')
    assert f.read(4) == b'CLM1'
    n, subs, flags = struct.unpack('<III', f.read(12))
    P = np.frombuffer(f.read(12 * n), dtype='<f4').reshape(n, 3).astype(np.float64)
    for bit, size in ((1, 12), (2, 8), (4, 8), (8, 16), (16, 16)):
        if flags & bit:
            f.read(size * n)
    S = []
    for _ in range(subs):
        c, = struct.unpack('<I', f.read(4))
        S.append(np.frombuffer(f.read(4 * c), dtype='<u4').astype(np.int64))
    _meshes[key] = (P, S)
    return _meshes[key]


def _inactive_paths():
    d = scene()
    return {o['path'] for o in d['objects'] if not o.get('active', True)}


def rendered(o):
    """World vertices and triangles (n,3 indices) of an object's renderer, or None."""
    for c in o['components']:
        if c['type'] != 'MeshRenderer':
            continue
        m = mesh(c['mesh'])
        if m is None:
            return None
        P, S = m
        M = np.array(o['world_matrix'], dtype=np.float64)
        cnt = c.get('static_batch_submesh_count', 0)
        if cnt > 0:
            first = c['static_batch_first_submesh']
            idx = np.concatenate([S[i] for i in range(first, first + cnt)])
            used, inv = np.unique(idx, return_inverse=True)
            return P[used], inv.reshape(-1, 3), M, True
        idx = np.concatenate(S) if S else np.zeros(0, dtype=np.int64)
        W = (M[:3, :3] @ P.T).T + M[:3, 3]
        return W, idx.reshape(-1, 3), M, False
    return None


def town_objects():
    """(name, path, world verts, tris, matrix) of every drawn object of the town."""
    d = scene()
    off = _inactive_paths()
    out = []
    for o in d['objects']:
        p = o['path']
        if not p.startswith('Map_v1'):
            continue
        if any(p == q or p.startswith(q + '/') for q in off):
            continue
        r = rendered(o)
        if r is None:
            continue
        W, T, M, _ = r
        out.append((o['name'], p, W + np.array([0, LIFT, 0]), T, M))
    return out


def base(name):
    i = name.find(' (')
    return name if i < 0 else name[:i]


def kit(name):
    """Vertices (own axes, own scale, foot at y 0) and triangles of the first piece of that name."""
    if name in _kit:
        return _kit[name]
    d = scene()
    off = _inactive_paths()
    for o in d['objects']:
        p = o['path']
        if not p.startswith('Map_v1') or any(p == q or p.startswith(q + '/') for q in off):
            continue
        if not (o['name'] == name or o['name'].startswith(name + ' (')):
            continue
        r = rendered(o)
        if r is None:
            continue
        W, T, M, _ = r
        R = M[:3, :3].copy()
        for k in range(3):
            R[:, k] /= np.linalg.norm(R[:, k])
        V = (R.T @ (W - M[:3, 3]).T).T
        V[:, 1] -= V[:, 1].min()
        _kit[name] = (V, T)
        return _kit[name]
    raise KeyError(name)


def yaw_matrix(yaw):
    a = math.radians(yaw)
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def place(name, pos, yaw, scale=(1, 1, 1)):
    V, T = kit(name)
    return (yaw_matrix(yaw) @ (V * np.array(scale)).T).T + np.array(pos, dtype=np.float64), T


def kit_bounds(name):
    V, _ = kit(name)
    return V.min(0), V.max(0)
