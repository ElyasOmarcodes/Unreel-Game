import json, math, sys
import numpy as np
from PIL import Image, ImageDraw
d = json.load(open(sys.argv[1]))
S = 4.0; E = 165
R = int(2 * E * S)
H = np.array(d["height"])  # rows z=140..-140, cols x=-140..140
gy, gx = np.gradient(H)
shade = np.clip(0.75 + 0.6 * (gx - gy) * 0.5, 0.3, 1.2)
hn = (H - H.min()) / (H.max() - H.min())
base = np.stack([0.55 + 0.25 * hn, 0.62 + 0.2 * hn, 0.45 + 0.1 * hn], -1) * shade[..., None]
# contour lines every 1 m
cont = (np.abs(H - np.round(H)) < 0.06)
base[cont] *= 0.8
img = Image.fromarray((np.clip(base, 0, 1) * 255).astype(np.uint8)).resize((R, R), Image.BILINEAR)
dr = ImageDraw.Draw(img, "RGBA")
def px(x, z): return ((x + E) * S, (E - z) * S)
for t in d["tracks"]:
    dr.line([px(*p) for p in t["p"]], fill=(170, 130, 80, 255), width=int(t["w"] * S))
for l in d["lanes"]:
    dr.line([px(*p) for p in l["p"]], fill=(205, 200, 190, 255), width=int(l["w"] * S), joint="curve")
for k in d["keep"]:
    dr.rectangle([px(k[0], k[3]), px(k[2], k[1])], outline=(255, 0, 0, 90))
cols = {"Classic": (230, 120, 70), "Tower": (200, 60, 40), "Flat": (150, 150, 160), "Shop": (90, 140, 220), "Cottage": (210, 170, 90), "Shed": (120, 90, 60), "Wing": (170, 80, 170)}
def turn(yaw, x, z):
    a = math.radians(yaw); c, s = math.cos(a), math.sin(a); return (x * c + z * s, -x * s + z * c)
def rect(x, z, yaw, w, dd):
    return [turn(yaw, sx * w / 2, sz * dd / 2) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
for q in d.get("plots", []):
    x, z, yaw, w, dd, big = q
    pts = rect(x, z, yaw, w, dd)
    dr.polygon([px(x + p[0], z + p[1]) for p in pts], fill=(150, 175, 110, 255), outline=(95, 85, 70, 255), width=3)
for h in d["houses"]:
    x, z, yaw, w, dd, fl, kind, roof, col, yard, ydep, gal = h[:12]
    pts = [turn(yaw, sx * w / 2, sz * dd / 2) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    c = cols[kind]; c = tuple(int(v * (0.75 + 0.12 * fl)) for v in c)
    dr.polygon([px(x + p[0], z + p[1]) for p in pts], fill=c + (255,), outline=(40, 30, 20, 255))
    f = turn(yaw, 0, dd / 2 + 0.6)
    dr.line([px(x, z), px(x + f[0], z + f[1])], fill=(0, 0, 0, 160), width=2)
    if len(h) > 12 and h[12]:
        lp = turn(yaw, -w / 2 - 1.2, 0)
        dr.ellipse([px(x + lp[0] - 0.9, z + lp[1] + 0.9), px(x + lp[0] + 0.9, z + lp[1] - 0.9)], fill=(255, 255, 0, 255), outline=(0, 0, 0, 255))
    if yard:
        yp = [turn(yaw, sx * (w / 2 + 1.2), dd / 2 + (ydep if sz else 0)) for sx, sz in ((-1, 0), (-1, 1), (1, 1), (1, 0))]
        dr.line([px(x + p[0], z + p[1]) for p in yp], fill=(110, 100, 90, 255), width=3)
for g in d["gardens"]:
    dr.line([px(g[0], g[1]), px(g[2], g[3])], fill=(110, 100, 90, 255), width=4)
    dr.ellipse([px(g[4] - 1.5, g[5] + 1.5), px(g[4] + 1.5, g[5] - 1.5)], fill=(60, 120, 50, 255))
for c in d["compounds"]:
    x, z, yaw, w, dd, gate = c
    dr.rectangle([px(x - w / 2, z + dd / 2), px(x + w / 2, z - dd / 2)], outline=(90, 60, 40, 255), width=4)
for w in d["windmills"]:
    dr.ellipse([px(w[0] - 3, w[2] + 3), px(w[0] + 3, w[2] - 3)], fill=(240, 240, 230, 255), outline=(0, 0, 0, 255))
for p in d["poles"]:
    dr.ellipse([px(p[0] - 0.6, p[1] + 0.6), px(p[0] + 0.6, p[1] - 0.6)], fill=(0, 0, 0, 255))
crop = sys.argv[3] if len(sys.argv) > 3 else None
if crop:
    x0, z0, x1, z1 = map(float, crop.split(","))
    img = img.crop((int((x0 + E) * S), int((E - z1) * S), int((x1 + E) * S), int((E - z0) * S)))
img.save(sys.argv[2])
print(len(d["houses"]), "houses", {k: sum(1 for h in d["houses"] if h[6] == k) for k in cols}, len(d["gardens"]), "gardens")
