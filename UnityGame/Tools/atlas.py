# The four views into one 2x2 atlas; the colour of the leaves bled into the transparent pixels (no dark
# fringes in the mip maps). python3 atlas.py views_dir out.png tile
import sys, os
from PIL import Image, ImageFilter
d, out, tile = sys.argv[1], sys.argv[2], int(sys.argv[3])
names = ["v0.png", "v1.png", "v2.png", "top.png"]
A = Image.new("RGBA", (tile * 2, tile * 2), (0, 0, 0, 0))
for i, n in enumerate(names):
    im = Image.open(os.path.join(d, n)).convert("RGBA").resize((tile, tile), Image.LANCZOS)
    A.paste(im, ((i % 2) * tile, (i // 2) * tile))
rgb = A.convert("RGB"); alpha = A.split()[3]
# bleed: repeatedly spread colour from opaque pixels into transparent ones
filled = Image.new("RGB", A.size, (0, 0, 0)); mask = alpha.point(lambda v: 255 if v > 8 else 0)
filled.paste(rgb, mask=mask)
cur = filled; m = mask
for _ in range(10):
    blur = cur.filter(ImageFilter.BoxBlur(2)); mb = m.filter(ImageFilter.BoxBlur(2))
    import numpy as np
    b = np.asarray(blur, dtype=np.float32); w = np.asarray(mb, dtype=np.float32)[..., None] / 255.0
    c = np.asarray(cur, dtype=np.float32); mm = np.asarray(m, dtype=np.float32)[..., None] / 255.0
    spread = np.where(w > 0.01, b / np.maximum(w, 1e-3), 0)
    c = np.where(mm > 0.5, c, spread)
    cur = Image.fromarray(np.clip(c, 0, 255).astype("uint8")); m = Image.fromarray((np.maximum(mm[..., 0], (w[..., 0] > 0.01)) * 255).astype("uint8"))
out_im = cur.convert("RGBA"); out_im.putalpha(alpha)
out_im.save(out, optimize=True)
print(out, os.path.getsize(out) // 1024, "KB")
