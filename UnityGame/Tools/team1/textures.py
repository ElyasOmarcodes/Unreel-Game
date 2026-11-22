"""TEAM 1's cloth textures, made seamless for tiling, with height-based normal maps:
  camo.jpg      the hexagon camo of the uniform (a Higgsfield picture made from the reference sheet)
  knit.jpg      black cotton fleece of the hood, the mask and the scarf      (quarter of the material sheet)
  cordura.jpg   olive 1000D nylon of the pouches and the belt                (quarter of the material sheet)
  leather.jpg   black boot and glove leather                                 (quarter of the material sheet)
  plastic.jpg   olive knee pad plastic                                       (quarter of the material sheet)
  flag.jpg      the arm patch (drawn)
Each cloth also gets <name>_n.png (tangent-space normal map from its brightness: the weave and the creases).
Run: python3 -I textures.py <camo_raw.png> <sheet_raw.png> <out dir>
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

camo_src, sheet_src, out = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(out, exist_ok=True)


def seamless(a, blend=0.18):
    """Tile without seams: the picture shifted by half both ways is blended in where the original's own edges
    would meet (a soft cross), the original kept elsewhere."""
    h, w = a.shape[:2]
    s = np.roll(np.roll(a, h // 2, 0), w // 2, 1)
    y = np.abs(np.arange(h) - h / 2) / (h / 2)          # 0 in the middle, 1 at the edges
    x = np.abs(np.arange(w) - w / 2) / (w / 2)
    wy = np.clip((y - (1 - blend)) / blend, 0, 1)
    wx = np.clip((x - (1 - blend)) / blend, 0, 1)
    m = np.maximum(wy[:, None], wx[None, :])
    m = m * m * (3 - 2 * m)
    return a * (1 - m[..., None]) + s * m[..., None]


def normal_from(gray, strength):
    gy, gx = np.gradient(gray)
    nx, ny, nz = -gx * strength, gy * strength, np.ones_like(gray)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return Image.fromarray(np.clip((n * 0.5 + 0.5) * 255, 0, 255).astype(np.uint8), 'RGB')


def save(name, rgb, size, strength, detail_blur=0.6):
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), 'RGB').resize((size, size), Image.LANCZOS)
    img.save(os.path.join(out, name + '.jpg'), quality=92)
    g = np.asarray(img.convert('L').filter(ImageFilter.GaussianBlur(detail_blur)), dtype=np.float32) / 255.0
    g = g - np.asarray(img.convert('L').filter(ImageFilter.GaussianBlur(12)), dtype=np.float32) / 255.0   # detail only
    normal_from(g, strength).save(os.path.join(out, name + '_n.png'), optimize=True)
    print(name, img.size)


camo = np.asarray(Image.open(camo_src).convert('RGB'), dtype=np.float32)
save('camo', seamless(camo), 1024, 6.0)
sheet = np.asarray(Image.open(sheet_src).convert('RGB'), dtype=np.float32)
H, W = sheet.shape[:2]
pad = 24   # keep off the joins of the quarters
quarters = {'knit': (0, 0), 'cordura': (0, 1), 'leather': (1, 0), 'plastic': (1, 1)}
strengths = {'knit': 5.0, 'cordura': 7.0, 'leather': 4.0, 'plastic': 2.5}
for name, (r, c) in quarters.items():
    q = sheet[r * H // 2 + pad:(r + 1) * H // 2 - pad, c * W // 2 + pad:(c + 1) * W // 2 - pad]
    save(name, seamless(q), 512, strengths[name])

# the arm patch: green over white over black, three red stars, a dark stitched border (3 : 2)
w, h = 384, 256
flag = Image.new('RGB', (w, h), (40, 40, 34))
d = ImageDraw.Draw(flag)
b = 14
bands = [(0, 122, 61), (238, 238, 232), (12, 12, 12)]
for i, col in enumerate(bands):
    d.rectangle([b, b + i * (h - 2 * b) / 3, w - b, b + (i + 1) * (h - 2 * b) / 3], fill=col)


def star(cx, cy, R):
    pts = []
    for k in range(10):
        a = -math.pi / 2 + k * math.pi / 5
        r = R if k % 2 == 0 else R * 0.382
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    d.polygon(pts, fill=(206, 17, 38))


for k in (-1, 0, 1):
    star(w / 2 + k * 92, h / 2, 34)
# stitches along the border
for x in range(b // 2, w, 10):
    d.line([x, b // 2, x + 5, b // 2], fill=(90, 90, 80), width=2)
    d.line([x, h - b // 2, x + 5, h - b // 2], fill=(90, 90, 80), width=2)
for y in range(b // 2, h, 10):
    d.line([b // 2, y, b // 2, y + 5], fill=(90, 90, 80), width=2)
    d.line([w - b // 2, y, w - b // 2, y + 5], fill=(90, 90, 80), width=2)
fl = np.asarray(flag, dtype=np.float32)
weave = (np.sin(np.arange(w)[None, :] * 1.7) * np.sin(np.arange(h)[:, None] * 1.7)) * 6
flag = Image.fromarray(np.clip(fl + weave[..., None], 0, 255).astype(np.uint8), 'RGB')
flag.save(os.path.join(out, 'flag.jpg'), quality=95)
print('flag', flag.size)
