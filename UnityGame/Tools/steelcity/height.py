"""Height of the old town from above (scene y, without the lift), on a 0.25 m grid: height.top(x, z)."""
import os, numpy as np, town
STEP = 0.25
X0, Z0, NX, NZ = -60.0, -100.0, 920, 800
_cache = os.path.join(os.path.dirname(os.path.abspath(__file__)), '.height.npy')


def _build():
    H = np.full((NX, NZ), -14.14)
    for name, path, W, T, M in town.town_objects():
        if name == 'Plane':
            continue
        V = W - np.array([0, town.LIFT, 0])
        for a, b, c in V[T]:
            mn = np.minimum(np.minimum(a, b), c); mx = np.maximum(np.maximum(a, b), c)
            i0 = max(0, int((mn[0] - X0) / STEP)); i1 = min(NX - 1, int((mx[0] - X0) / STEP) + 1)
            k0 = max(0, int((mn[2] - Z0) / STEP)); k1 = min(NZ - 1, int((mx[2] - Z0) / STEP) + 1)
            if i1 < i0 or k1 < k0:
                continue
            xs = X0 + np.arange(i0, i1 + 1) * STEP; zs = Z0 + np.arange(k0, k1 + 1) * STEP
            PX, PZ = np.meshgrid(xs, zs, indexing='ij')
            d = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
            if abs(d) < 1e-9:
                continue
            l1 = ((b[2] - c[2]) * (PX - c[0]) + (c[0] - b[0]) * (PZ - c[2])) / d
            l2 = ((c[2] - a[2]) * (PX - c[0]) + (a[0] - c[0]) * (PZ - c[2])) / d
            l3 = 1 - l1 - l2
            inside = (l1 >= -1e-6) & (l2 >= -1e-6) & (l3 >= -1e-6)
            y = l1 * a[1] + l2 * b[1] + l3 * c[1]
            sub = H[i0:i1 + 1, k0:k1 + 1]
            np.maximum(sub, np.where(inside, y, -1e9), out=sub)
    np.save(_cache, H)
    return H


_H = None


def grid():
    global _H
    if _H is None:
        _H = np.load(_cache) if os.path.exists(_cache) else _build()
    return _H


def top(x, z):
    return float(grid()[int(round((x - X0) / STEP)), int(round((z - Z0) / STEP))])


if __name__ == '__main__':
    import sys
    H = grid()
    for a in sys.argv[1:]:
        x, z = [float(v) for v in a.split(',')]
        print(a, round(top(x, z), 2))
