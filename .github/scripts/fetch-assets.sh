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
  "quaternius/downtown-city-megakit/Exports/FBX (Unity)/*" \
  "quaternius/downtown-city-megakit/Textures/*.png" \
  "quaternius/downtown-city-megakit/License_Standard.txt"
for i in 1 2 3; do git fetch -q --depth 1 --filter=blob:none origin "$COMMIT" && break || sleep 10; done
git checkout -q FETCH_HEAD
Q="$WORK/quaternius"
mkdir -p "$DEST/UAL" "$DEST/City/Models" "$DEST/City/Textures"
cp "$Q/universal-animation-library/Unity/UAL1.fbx" "$DEST/UAL/"
cp "$Q/universal-animation-library/License.txt" "$DEST/UAL/License.txt"
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
