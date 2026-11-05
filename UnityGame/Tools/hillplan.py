# Plan of the hill village from the same numbers as HillVillageBuilder.cs: heights (shaded), the levels,
# the walls, streets, houses, mosque, square. python3 hillplan.py out.png
import math, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
Half, Edge = 100.0, 140.0
def ss(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t)
def smoothstep(a, b, t):
    t = min(1.0, max(0.0, t)); t = t * t * (3 - 2 * t); return a + (b - a) * t
def gauss(x, z, cx, cz, s): return math.exp(-((x - cx) ** 2 + (z - cz) ** 2) / (2 * s * s))
def raw(x, z):
    h = 0.07 * z + 1.8 * math.sin(x / 29 + 0.5) * math.cos(z / 23) + 0.9 * math.sin((x - z) / 17)
    h += 26 * gauss(x, z, 10, 128, 46) + 11 * gauss(x, z, -122, 40, 34) + 9 * gauss(x, z, 118, 64, 30) - 3.5 * gauss(x, z, 30, -112, 40)
    e = max(abs(x), abs(z))
    h += ss(Half - 8, Half + 50, e) * (16 + 6 * math.sin(x / 21 + z / 27))
    return h
pads = []
def flat(x0, z0, x1, z1, y, w, e, s, n): pads.append((x0, z0, x1, z1, 0, 0, y, 0, y, w, e, s, n))
def ramp(x0, z0, x1, z1, axis, a0, y0, a1, y1, w, e, s, n): pads.append((x0, z0, x1, z1, axis, a0, y0, a1, y1, w, e, s, n))
def side(out, m):
    if out <= 0: return 1.0
    if m <= 0: return 0.0
    return smoothstep(1, 0, out / m)
def weight(p, x, z):
    x0, z0, x1, z1, ax, a0, y0, a1, y1, W, E, S, N = p
    return min(side(x0 - x, W), side(x - x1, E), side(z0 - z, S), side(z - z1, N))
def pheight(p, x, z):
    x0, z0, x1, z1, ax, a0, y0, a1, y1 = p[:9]
    if ax == 0: return y0
    a = x if ax == 1 else z
    t = min(1, max(0, (a - a0) / (a1 - a0))); return y0 + (y1 - y0) * t
def ground(x, z):
    h = raw(x, z)
    for p in pads:
        w = weight(p, x, z)
        if w > 0: h = h + (pheight(p, x, z) - h) * w
    return h
def built(x, z): return max([weight(p, x, z) for p in pads] + [0])
flat(-26, -18, 26, 16, 0, 6, 6, 0, 0)
ramp(-96, -18, -26, 16, 1, -96, 3, -26, 0, 10, 0, 0, 0)
flat(-96, 16, 24, 50, 4.5, 10, 0, 0, 12)
ramp(24, -6, 42, 52, 2, 16, 0, 40, 4.5, 0, 10, 6, 10)
flat(-96, -56, 26, -18, -2.5, 12, 0, 12, 0)
ramp(26, -56, 42, -6, 2, -40, -2.5, -18, 0, 0, 10, 12, 0)
ramp(42, -8, 104, 10, 1, 42, 0, 104, 7, 0, 0, 6, 6)
houses = []
src = open(sys.argv[2]).read()
import re
for m in re.finditer(r'new House\(([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f, (\d), (\d), "([^"]*)"\)', src):
    x, z, yaw, w, d = map(float, m.groups()[:5]); houses.append((x, z, yaw, w, d, int(m.group(6)), int(m.group(7)), m.group(8)))
for (x, z, yaw, w, d, fl, st, ex) in houses:
    if built(x, z) < 0.95:
        r = math.radians(yaw); ew = abs(math.cos(r) * w) + abs(math.sin(r) * d); ed = abs(math.sin(r) * w) + abs(math.cos(r) * d)
        ex2, ez2 = ew / 2 + 1.5, ed / 2 + 1.5
        y = ground(x, z); flat(x - ex2, z - ez2, x + ex2, z + ez2, y, 3, 3, 3, 3)
if __name__ == "__main__" and sys.argv[1] != "-":
    S = 3.6; R = int(2 * Edge * S)
    def px(x, z): return ((x + Edge) * S, (Edge - z) * S)
    H = np.zeros((R, R))
    for j in range(R):
        for i in range(R):
            H[j, i] = ground(-Edge + (i + 0.5) / S, Edge - (j + 0.5) / S)
    gy, gx = np.gradient(H, 1 / S)
    shade = np.clip(0.75 + (-gx * 0.5 + gy * 0.6) * 0.35, 0.3, 1.2)
    lo, hi = H.min(), H.max()
    t = (H - lo) / (hi - lo)
    col = np.stack([0.55 + 0.25 * t, 0.6 + 0.2 * t, 0.42 + 0.15 * t], -1) * shade[..., None]
    img = Image.fromarray((np.clip(col, 0, 1) * 255).astype('uint8'))
    d = ImageDraw.Draw(img)
    # contour lines every metre
    for j in range(0, R - 1):
        for i in range(0, R - 1):
            if math.floor(H[j, i]) != math.floor(H[j, i + 1]) or math.floor(H[j, i]) != math.floor(H[j + 1, i]):
                img.putpixel((i, j), (90, 90, 70))
    streets = [((29.5, -6), (29.5, 53), 9), ((30, -6), (30, -57), 8), ((30, 1), (105, 1), 7), ((-26, 0), (-99, 0), 6), ((-99, 19.3), (24, 19.3), 4.4),
               ((-97, 34.5), (-17, 34.5), 5), ((-99, -21.6), (27, -21.6), 4.6), ((-99, -36.2), (27, -36.2), 9)]
    for a, b, w in streets: d.line([px(*a), px(*b)], fill=(150, 140, 125), width=int(w * S))
    d.rectangle([px(-26, 15), px(26, -17)], fill=(215, 210, 198), outline=(120, 110, 100))
    for (a, b, c) in [((-100, 16), (24, 16), 'x'), ((24, 16), (24, 42), 'z'), ((-100, -18), (26, -18), 'x'), ((26, -42), (26, -18), 'z')]:
        d.line([px(*a), px(*b)], fill=(120, 100, 85), width=int(2.1 * S))
    d.rectangle([px(-12.5, -1), px(0.5, -9)], fill=(160, 150, 140), outline=(80, 60, 40))
    d.rectangle([px(-17, 47.5), px(6, 21.5)], outline=(120, 100, 85), width=3)
    d.rectangle([px(-12, 43), px(0, 30)], fill=(180, 170, 160), outline=(60, 50, 40)); d.ellipse([px(0.6, 32.4), px(2.6, 30.4)], fill=(90, 90, 100))
    for (x, z, yaw, w, dd, fl, st, ex) in houses:
        r = math.radians(yaw); c, s_ = math.cos(r), math.sin(r)
        pts = [(x + c * u + s_ * v, z - s_ * u + c * v) for u, v in ((-w / 2, -dd / 2), (w / 2, -dd / 2), (w / 2, dd / 2), (-w / 2, dd / 2))]
        fill = (196, 98, 60) if 'ruin' not in ex else (150, 140, 130)
        d.polygon([px(*p) for p in pts], fill=fill, outline=(70, 40, 30))
        fx, fz = x + s_ * dd / 2, z + c * dd / 2
        d.ellipse([px(fx - 0.5, fz + 0.5), px(fx + 0.5, fz - 0.5)], fill=(30, 60, 160))
    for xx, zz in [(-82, 19.5), (18, 19.5), (31, 47), (70, 2), (32, -50), (-40, -36), (-88, 0.5), (-6, 58), (-18, -12)]:
        d.ellipse([px(xx - 2, zz + 2), px(xx + 2, zz - 2)], fill=(30, 200, 60), outline=(0, 0, 0))
    d.rectangle([px(-Half, Half), px(Half, -Half)], outline=(200, 40, 40), width=2)
    for x in range(-140, 141, 20):
        d.text(px(x, -Edge + 6), str(x), fill=(0, 0, 0))
    for z in range(-140, 141, 20):
        d.text(px(-Edge + 2, z), str(z), fill=(0, 0, 0))
    img.save(sys.argv[1])
    print("plan", lo, hi)
