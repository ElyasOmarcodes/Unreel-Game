#!/usr/bin/env bash
# Downloads the CC0 Quaternius assets used by Urban Agents into the Unity project (git sparse checkout of
# one pinned commit of a public mirror) and prepares the textures for Unity's Standard shader.
set -euo pipefail
DEST="$(realpath -m "$1")"
REPO=https://github.com/tilakpatell/tilakverse-assets
COMMIT=85064399bbdacc0e8cc50807179490314bc4ff1c
WORK="${RUNNER_TEMP:-/tmp}/cc0-assets"
rm -rf "$WORK" && mkdir -p "$WORK" && cd "$WORK"
git init -q
git remote add origin "$REPO"
git sparse-checkout set --no-cone \
  "quaternius/universal-animation-library/Unity/UAL1.fbx" \
  "quaternius/universal-animation-library/License.txt" \
  "quaternius/universal-animation-library-2/Female Mannequin/Unity/Mannequin_F.fbx" \
  "quaternius/downtown-city-megakit/Exports/FBX (Unity)/*" \
  "quaternius/downtown-city-megakit/Textures/*.png" \
  "quaternius/downtown-city-megakit/License_Standard.txt"
for i in 1 2 3; do git fetch -q --depth 1 --filter=blob:none origin "$COMMIT" && break || sleep 10; done
git checkout -q FETCH_HEAD
Q="$WORK/quaternius"
mkdir -p "$DEST/UAL" "$DEST/City/Models" "$DEST/City/Textures"
cp "$Q/universal-animation-library/Unity/UAL1.fbx" "$DEST/UAL/"
cp "$Q/universal-animation-library/License.txt" "$DEST/UAL/License.txt"
cp "$Q/universal-animation-library-2/Female Mannequin/Unity/Mannequin_F.fbx" "$DEST/UAL/" || true
cp "$Q/downtown-city-megakit/Exports/FBX (Unity)/"*.fbx "$DEST/City/Models/"
cp "$Q/downtown-city-megakit/License_Standard.txt" "$DEST/City/License.txt"
python3 -m pip install -q pillow
python3 - "$Q/downtown-city-megakit/Textures" "$DEST/City/Textures" <<'PY'
import sys, os
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
for name in sorted(os.listdir(src)):
    if not name.endswith(".png"): continue
    im = Image.open(os.path.join(src, name))
    if max(im.size) > 1024:
        im = im.resize((1024, 1024 * im.size[1] // im.size[0]), Image.LANCZOS)
    base = name[:-4]
    if base.endswith("_ORM"):
        # ORM (R occlusion, G roughness, B metal) -> Unity Standard metallic map (R metal, A smoothness)
        r, g, b = im.convert("RGB").split()
        smooth = g.point(lambda v: 255 - v)
        black = Image.new("L", im.size, 0)
        Image.merge("RGBA", (b, black, black, smooth)).save(os.path.join(dst, base[:-4] + "_MS.png"))
        continue
    im.save(os.path.join(dst, name))
    print("texture", name, im.size)
PY
du -sh "$DEST"/*
ls "$DEST/City/Models" | wc -l

# ---------------------------------------------------------------- weapons: Quaternius Ultimate Gun Pack (CC0)
cd "$WORK"
for i in 1 2 3; do curl -sfL https://opengameart.org/sites/default/files/ultimate_gun_pack_by_quaternius.zip -o guns.zip && break || sleep 10; done
mkdir -p "$DEST/Guns" && unzip -q -o guns.zip "Ultimate Gun Pack - July 2019/FBX/*" -d guns
cp guns/Ultimate*/FBX/*.fbx "$DEST/Guns/"
ls "$DEST/Guns" | wc -l

# ---------------------------------------------------------------- PBR textures: ambientCG (CC0)
mkdir -p "$DEST/Port/Textures" tex
for id in CorrugatedSteel005 CorrugatedSteel007A CorrugatedSteel009 Concrete034 Concrete042A Asphalt025C Metal032 MetalPlates013 Bricks076A PaintedPlaster017 DiamondPlate009 Wood049 Planks037A; do
  for i in 1 2 3; do curl -sfL "https://ambientcg.com/get?file=${id}_1K-JPG.zip" -o t.zip && break || sleep 5; done
  unzip -q -o t.zip -d "tex/$id" || echo "missing texture $id"
done

# ---------------------------------------------------------------- props: Poly Haven (CC0), single material models only
mkdir -p "$DEST/Props"
for id in Barrel_01 Barrel_02 barrel_03 old_military_crate concrete_road_barrier concrete_road_barrier_02 metal_trash_can plastic_crate_03 propane_tank cardboard_box_01 old_tyre utility_box_01 portable_generator street_lamp_01 small_lpg_tank ammo_box; do
  curl -sf "https://api.polyhaven.com/files/$id" -o "files_$id.json" || continue
  python3 - "$id" "files_$id.json" "$DEST/Props" <<'PY' || echo "skipped $id"
import json, sys, os, urllib.request
pid, path, dest = sys.argv[1:4]
d = json.load(open(path))
need = ["Diffuse", "nor_gl", "arm"]
if any(k not in d for k in need):
    raise SystemExit("multi material model")
def get(url, out):
    urllib.request.urlretrieve(url, out)
os.makedirs(f"{dest}/{pid}", exist_ok=True)
get(d["fbx"]["1k"]["fbx"]["url"], f"{dest}/{pid}/{pid}.fbx")
get(d["Diffuse"]["1k"]["jpg"]["url"], f"{dest}/{pid}/{pid}_BaseColor.jpg")
get(d["nor_gl"]["1k"]["jpg"]["url"], f"{dest}/{pid}/{pid}_Normal.jpg")
get(d["arm"]["1k"]["jpg"]["url"], f"{dest}/{pid}/arm.jpg")
print("prop", pid)
PY
done

python3 - "$WORK/tex" "$DEST/Port/Textures" "$DEST/Props" <<'PY'
import sys, os, glob
from PIL import Image
tex, dst, props = sys.argv[1:4]
def ms(rough, metal=None):
    r = rough.convert("L")
    m = metal.convert("L").resize(r.size) if metal else Image.new("L", r.size, 0)
    black = Image.new("L", r.size, 0)
    return Image.merge("RGBA", (m, black, black, r.point(lambda v: 255 - v)))
for folder in sorted(glob.glob(tex + "/*")):
    pid = os.path.basename(folder)
    f = lambda kind: (glob.glob(f"{folder}/*_{kind}.jpg") or [None])[0]
    if not f("Color"): continue
    Image.open(f("Color")).convert("RGB").save(f"{dst}/{pid}_BaseColor.jpg", quality=90)
    if f("NormalGL"): Image.open(f("NormalGL")).convert("RGB").save(f"{dst}/{pid}_Normal.jpg", quality=92)
    if f("Roughness"):
        ms(Image.open(f("Roughness")), Image.open(f("Metalness")) if f("Metalness") else None).save(f"{dst}/{pid}_MS.png")
    print("material", pid)
# Poly Haven ARM: R occlusion, G roughness, B metal
for arm in glob.glob(props + "/*/arm.jpg"):
    im = Image.open(arm).convert("RGB")
    r, g, b = im.split()
    ms(g, b).save(arm.replace("arm.jpg", os.path.basename(os.path.dirname(arm)) + "_MS.png"))
    os.remove(arm)
PY
du -sh "$DEST"/*
