"""Draws the textures of the ADVANCED game's things (fuel drum, C4, supply crate, vests) into
Assets/Textures/Loot. Run: python3 textures.py"""
import math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Textures', 'Loot')
os.makedirs(OUT, exist_ok=True)
FONT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Resources', 'Fonts', 'UAFont.ttf')
STENCIL = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'


def font(size, f=None):
    for p in (f, FONT, STENCIL):
        if p and os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def noise(w, h, cells, seed):
    g = np.random.default_rng(seed).random((max(2, cells * h // w), cells))
    return np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), dtype=np.float32) / 255.0


def fbm(w, h, seed, octaves=((6, .5), (18, .3), (60, .2))):
    return sum(a * noise(w, h, c, seed + i) for i, (c, a) in enumerate(octaves))


def to_img(rgb):
    return Image.fromarray(np.clip(rgb * 255, 0, 255).astype(np.uint8), 'RGB')


# ----------------------------------------------------------------- fuel drum body (u around, v up)
def skull(d, cx, cy, s, ink=(15, 15, 15)):
    """Skull and crossbones in a box of size s round (cx, cy)."""
    w = max(2, int(s * 0.09))
    for sgn in (-1, 1):
        x0, y0 = cx - sgn * s * 0.42, cy + s * 0.06
        x1, y1 = cx + sgn * s * 0.42, cy + s * 0.46
        d.line([x0, y0, x1, y1], fill=ink, width=int(s * 0.11))
        for (x, y) in ((x0, y0), (x1, y1)):
            for o in (-1, 1):
                d.ellipse([x - s * 0.07 + o * s * 0.05, y - s * 0.07 - o * s * 0.03 * sgn, x + s * 0.07 + o * s * 0.05, y + s * 0.07 - o * s * 0.03 * sgn], fill=ink)
    d.ellipse([cx - s * 0.3, cy - s * 0.48, cx + s * 0.3, cy + s * 0.08], fill=ink)            # cranium
    d.rounded_rectangle([cx - s * 0.17, cy - s * 0.02, cx + s * 0.17, cy + s * 0.22], radius=s * 0.05, fill=ink)   # jaw
    bg = (245, 242, 230)
    for sgn in (-1, 1):
        d.ellipse([cx + sgn * s * 0.13 - s * 0.085, cy - s * 0.26, cx + sgn * s * 0.13 + s * 0.085, cy - s * 0.09], fill=bg)
    d.polygon([(cx, cy - s * 0.07), (cx - s * 0.04, cy + s * 0.01), (cx + s * 0.04, cy + s * 0.01)], fill=bg)
    for i in range(-2, 3):
        d.line([cx + i * s * 0.06, cy + s * 0.08, cx + i * s * 0.06, cy + s * 0.2], fill=bg, width=w // 2 + 1)


def drum():
    W, H = 1024, 512
    n = fbm(W, H, 11)
    base = np.array([0.93, 0.72, 0.05])
    rgb = base[None, None, :] * (0.86 + 0.22 * n[..., None])
    # rust and dirt towards the bottom and at the rims, running down in streaks
    streak = noise(W, H, 90, 5) * noise(W, H, 4, 6)
    v = np.linspace(1, 0, H)[:, None]                 # v=1 top row of the image
    dirt = np.clip((0.25 - v) / 0.25, 0, 1) * 0.6 + streak * 0.35 * (1 - v) + np.clip((v - 0.93) / 0.07, 0, 1) * 0.5
    rust = np.array([0.36, 0.17, 0.07])
    rgb = rgb * (1 - dirt[..., None] * 0.7) + rust * dirt[..., None] * 0.7
    # scratches showing dark metal
    rng = np.random.default_rng(3)
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    for _ in range(90):
        x, y = rng.uniform(0, W), rng.uniform(0, H)
        a, L = rng.uniform(0, math.pi), rng.uniform(6, 30)
        d.line([x, y, x + math.cos(a) * L, y + math.sin(a) * L], fill=(70, 60, 45), width=1)
    # the rolling ribs: darker shaded lines
    for vr in (0.33, 0.67):
        y = int(H * (1 - vr))
        d.rectangle([0, y - 6, W, y + 6], fill=(150, 110, 10))
        d.line([0, y - 6, W, y - 6], fill=(250, 215, 90), width=2)
        d.line([0, y + 6, W, y + 6], fill=(90, 65, 10), width=2)
    # hazard band (black and yellow diagonals) near the top
    y0, y1 = int(H * 0.07), int(H * 0.17)
    band = Image.new('RGB', (W, y1 - y0), (240, 185, 10))
    bd = ImageDraw.Draw(band)
    for x in range(-60, W + 60, 56):
        bd.polygon([(x, 0), (x + 28, 0), (x + 28 - (y1 - y0), y1 - y0), (x - (y1 - y0), y1 - y0)], fill=(20, 20, 20))
    img.paste(band, (0, y0))
    # the warning signs, front and back: a white diamond with the skull, the words under it
    for cu in (0.25, 0.75):
        cx, cy, r = int(W * cu), int(H * 0.47), 92
        d.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=(20, 20, 20))
        r2 = r - 10
        d.polygon([(cx, cy - r2), (cx + r2, cy), (cx, cy + r2), (cx - r2, cy)], fill=(245, 242, 230))
        skull(d, cx, cy + 4, 96)
        f = font(34)
        for text, yy in (('FLAMMABLE', cy + r + 30), ('DANGER', cy - r - 36)):
            tw = d.textlength(text, font=f)
            d.text((cx - tw / 2, yy - 17), text, font=f, fill=(20, 20, 20))
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    img.save(os.path.join(OUT, 'DrumBody.png'), optimize=True)
    # the lid: grey-yellow, two bungs, a rim
    L = 256
    n = fbm(L, L, 21)
    lid = to_img(np.array([0.85, 0.66, 0.08])[None, None, :] * (0.8 + 0.25 * n[..., None]))
    ld = ImageDraw.Draw(lid)
    ld.ellipse([4, 4, L - 4, L - 4], outline=(110, 80, 10), width=10)
    for (x, y, rr) in ((70, 90, 22), (180, 160, 14)):
        ld.ellipse([x - rr, y - rr, x + rr, y + rr], fill=(90, 90, 85), outline=(40, 40, 40), width=3)
    lid.save(os.path.join(OUT, 'DrumLid.png'), optimize=True)


# ----------------------------------------------------------------- C4 wrapper
def c4():
    W, H = 512, 256
    n = fbm(W, H, 31)
    rgb = np.array([0.33, 0.36, 0.21])[None, None, :] * (0.85 + 0.25 * n[..., None])
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    big = font(110, STENCIL)
    d.text((58, 50), 'C4', font=big, fill=(18, 18, 14))
    small = font(17, STENCIL)
    for i, line in enumerate(('DEMOLITION M112', '1-1/4 LB  COMP C-4', 'LOT  MA-87-0317')):
        d.text((222, 70 + i * 34), line, font=small, fill=(20, 20, 16))
    # grey tape round both ends
    for x in (0, W - 40):
        d.rectangle([x, 0, x + 40, H], fill=(95, 95, 92))
        for k in range(0, H, 9):
            d.line([x, k, x + 40, k + 4], fill=(110, 110, 106), width=1)
    img.filter(ImageFilter.GaussianBlur(0.5)).save(os.path.join(OUT, 'C4.png'), optimize=True)


# ----------------------------------------------------------------- supply crate
def crate():
    S = 512
    n = fbm(S, S, 41)
    grain = noise(S, S, 140, 43) * 0.15
    rgb = np.array([0.24, 0.3, 0.17])[None, None, :] * (0.8 + 0.3 * n[..., None] + grain[..., None])
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    for y in range(0, S, 102):
        d.line([0, y, S, y], fill=(30, 36, 20), width=5)                 # gaps between the planks
    d.rectangle([0, 0, S - 1, S - 1], outline=(40, 40, 38), width=22)    # the steel edges
    for (x, y) in ((22, 22), (S - 22, 22), (22, S - 22), (S - 22, S - 22)):
        d.ellipse([x - 8, y - 8, x + 8, y + 8], fill=(70, 70, 66))
    f = font(84, STENCIL)
    t = 'SUPPLY'
    tw = d.textlength(t, font=f)
    d.text(((S - tw) / 2, S / 2 - 70), t, font=f, fill=(225, 222, 205))
    f2 = font(34, STENCIL)
    t2 = 'WEAPONS  -  KEEP DRY'
    tw = d.textlength(t2, font=f2)
    d.text(((S - tw) / 2, S / 2 + 40), t2, font=f2, fill=(225, 222, 205))
    # wear on the stencil
    arr = np.asarray(img).astype(np.float32) / 255
    wear = (noise(S, S, 50, 47) > 0.62)[..., None]
    arr = np.where(wear & (arr.mean(-1, keepdims=True) > 0.6), arr * 0.55 + rgb * 0.45, arr)
    to_img(arr).save(os.path.join(OUT, 'Crate.png'), optimize=True)


# ----------------------------------------------------------------- vest fabric
def vest(name, color, seed):
    S = 256
    n = fbm(S, S, seed)
    y, x = np.mgrid[0:S, 0:S]
    rip = (((x % 16) < 1) | ((y % 16) < 1)) * 0.08                       # ripstop grid
    weave = (np.sin(x * 1.6) * np.sin(y * 1.6)) * 0.03
    rgb = np.array(color)[None, None, :] * (0.82 + 0.25 * n[..., None] - rip[..., None] + weave[..., None])
    to_img(rgb).save(os.path.join(OUT, name + '.png'), optimize=True)


# ----------------------------------------------------------------- military crate (hot spots)
def military_crate():
    S = 512
    n = fbm(S, S, 61)
    y, x = np.mgrid[0:S, 0:S]
    rgb = np.array([0.11, 0.13, 0.1])[None, None, :] * (0.85 + 0.3 * n[..., None])
    # moulded ribs across the case
    rib = ((x % 64) < 6) * 0.06
    rgb = rgb + rib[..., None]
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    # a hazard band along the top, yellow and black
    for i in range(-2, S // 28 + 2):
        x0 = i * 28
        d.polygon([(x0, 0), (x0 + 14, 0), (x0 + 14 + 40, 40), (x0 + 40, 40)], fill=(214, 170, 30))
    d.line([0, 42, S, 42], fill=(20, 20, 18), width=4)
    f = font(70, STENCIL)
    t = 'MILITARY'
    tw = d.textlength(t, font=f)
    d.text(((S - tw) / 2, S / 2 - 70), t, font=f, fill=(222, 196, 92))
    f2 = font(30, STENCIL)
    for k, t2 in enumerate(('WEAPON SYSTEMS', 'HANDLE WITH CARE')):
        tw = d.textlength(t2, font=f2)
        d.text(((S - tw) / 2, S / 2 + 20 + k * 40), t2, font=f2, fill=(200, 196, 180))
    # a purple marking stripe (the grade of what is inside)
    d.rectangle([0, S - 46, S, S - 30], fill=(130, 70, 200))
    arr = np.asarray(img).astype(np.float32) / 255
    wear = (noise(S, S, 60, 63) > 0.66)[..., None]
    arr = np.where(wear & (arr.mean(-1, keepdims=True) > 0.45), arr * 0.6 + rgb * 0.4, arr)
    to_img(arr).save(os.path.join(OUT, 'MilitaryCrate.png'), optimize=True)


# ----------------------------------------------------------------- supply drop crate panels
def airdrop():
    S = 512
    n = fbm(S, S, 71)
    rgb = np.array([0.27, 0.33, 0.36])[None, None, :] * (0.82 + 0.3 * n[..., None])
    img = to_img(rgb)
    d = ImageDraw.Draw(img)
    # pressed panel lines and rivets
    d.rectangle([18, 18, S - 19, S - 19], outline=(48, 56, 60), width=6)
    for k in range(1, 4):
        d.line([18, k * S // 4, S - 19, k * S // 4], fill=(52, 60, 64), width=3)
    for xx in range(30, S, 60):
        for yy in (30, S - 30):
            d.ellipse([xx - 5, yy - 5, xx + 5, yy + 5], fill=(95, 104, 108))
    # the parachute pictogram and the words
    cx, cy = S // 2, 150
    d.pieslice([cx - 90, cy - 70, cx + 90, cy + 70], 180, 360, fill=(240, 236, 224))
    for k in range(-2, 3):
        d.line([cx + k * 42, cy, cx, cy + 90], fill=(240, 236, 224), width=5)
    d.rectangle([cx - 22, cy + 88, cx + 22, cy + 122], fill=(240, 236, 224))
    t = 'SUPPLY DROP'
    size = 74
    while True:   # as big as fits between the frame's edges
        f = font(size, STENCIL)
        tw = d.textlength(t, font=f)
        if tw < S - 80 or size < 30: break
        size -= 2
    d.text(((S - tw) / 2, 300), t, font=f, fill=(244, 240, 226))
    f2 = font(32, STENCIL)
    t2 = 'THIS SIDE UP'
    tw = d.textlength(t2, font=f2)
    d.text(((S - tw) / 2, 400), t2, font=f2, fill=(225, 70, 50))
    for sx in (cx - tw / 2 - 40, cx + tw / 2 + 18):
        d.polygon([(sx, 432), (sx + 22, 432), (sx + 11, 402)], fill=(225, 70, 50))
    arr = np.asarray(img).astype(np.float32) / 255
    wear = (noise(S, S, 70, 73) > 0.68)[..., None]
    arr = np.where(wear & (arr.mean(-1, keepdims=True) > 0.55), arr * 0.6 + rgb * 0.4, arr)
    to_img(arr).save(os.path.join(OUT, 'Airdrop.png'), optimize=True)


# ----------------------------------------------------------------- parachute gores (u: 4 gores across the tile, v: rim to top)
def parachute():
    W, H = 256, 128
    y, x = np.mgrid[0:H, 0:W]
    gore = (x * 4 // W) % 2
    orange = np.array([0.95, 0.42, 0.08]); white = np.array([0.93, 0.92, 0.88])
    rgb = np.where(gore[..., None] == 0, orange[None, None, :], white[None, None, :])
    seam = ((x % (W // 4)) < 2) | ((y % 32) < 1)
    rgb = rgb * (1 - 0.18 * seam[..., None])
    rgb = rgb * (0.9 + 0.1 * fbm(W, H, 81)[..., None])
    to_img(rgb).save(os.path.join(OUT, 'Parachute.png'), optimize=True)


def parachute_icon():
    """A white parachute over a crate for the HUD (Resources/Icons, 96 px)."""
    S = 96
    img = Image.new('RGBA', (S * 4, S * 4), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S * 2
    d.pieslice([c - 150, 30, c + 150, 250], 180, 360, fill=(255, 255, 255, 255))
    for k in (-3, -1, 1, 3):
        d.pieslice([c + k * 37 - 37, 120, c + k * 37 + 37, 160], 0, 180, fill=(0, 0, 0, 0))
    for k in (-150, -75, 0, 75, 150):
        d.line([c + k, 140, c, 280], fill=(255, 255, 255, 255), width=10)
    d.rounded_rectangle([c - 48, 275, c + 48, 360], radius=10, fill=(255, 255, 255, 255))
    img.resize((S, S), Image.LANCZOS).save(os.path.join(OUT, '..', '..', 'Resources', 'Icons', 'parachute.png'), optimize=True)


if __name__ == '__main__':
    drum(); c4(); crate(); military_crate(); airdrop(); parachute(); parachute_icon()
    vest('VestMilitary', (0.48, 0.38, 0.24), 53)
    vest('VestLight', (0.4, 0.38, 0.26), 51)
    vest('VestHeavy', (0.12, 0.13, 0.14), 52)
    print('textures in', os.path.abspath(OUT))
