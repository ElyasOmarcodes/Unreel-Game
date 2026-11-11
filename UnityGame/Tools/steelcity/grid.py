"""Draws the town's 10 m grid with coordinates on a top view made by render.py: grid.py top_cx_cz_size.png"""
import sys, re
from PIL import Image, ImageDraw
p = sys.argv[1]
cx, cz, size = [float(x) for x in re.search(r'top_(-?\d+)_(-?\d+)_(\d+)', p).groups()]
im = Image.open(p).convert('RGB'); W = im.width; d = ImageDraw.Draw(im); k = W / size
X = lambda x: (x - cx) * k + W / 2
Z = lambda z: W / 2 - (z - cz) * k
x = -128.0
while x < 200:
    if abs(X(x) - W / 2) < W / 2:
        d.line([X(x), 0, X(x), W], fill=(255, 255, 0) if round(x) % 50 == 2 else (120, 120, 60), width=1); d.text((X(x) + 2, 2), '%g' % x, fill=(255, 255, 0))
    x += 10
z = -133.59
while z < 120:
    if abs(Z(z) - W / 2) < W / 2:
        d.line([0, Z(z), W, Z(z)], fill=(90, 160, 160), width=1); d.text((2, Z(z) + 1), '%.1f' % z, fill=(0, 255, 255))
    z += 10
im.save(p.replace('.png', '_grid.jpg'), quality=90)
