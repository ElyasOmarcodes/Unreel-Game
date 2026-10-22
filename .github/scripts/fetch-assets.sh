#!/usr/bin/env bash
# Downloads the CC0 Quaternius assets used by Agent Warfare into the Unity project (git sparse checkout of
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
  "quaternius/universal-animation-library-2/Unity/UAL2.fbx" \
  "quaternius/downtown-city-megakit/Exports/FBX (Unity)/*" \
  "quaternius/downtown-city-megakit/Textures/*.png" \
  "quaternius/downtown-city-megakit/License_Standard.txt" \
  "quaternius/stylized-nature-megakit/FBX (Unity)/CommonTree_*.fbx" \
  "quaternius/stylized-nature-megakit/FBX (Unity)/Pine_*.fbx" \
  "quaternius/stylized-nature-megakit/FBX (Unity)/Bush_*.fbx" \
  "quaternius/stylized-nature-megakit/FBX (Unity)/Fern_1.fbx" \
  "quaternius/stylized-nature-megakit/FBX (Unity)/Grass_Common_Tall.fbx" \
  "quaternius/stylized-nature-megakit/FBX (Unity)/Grass_Wispy_Tall.fbx" \
  "quaternius/stylized-nature-megakit/FBX (Unity)/Plant_1_Big.fbx" \
  "quaternius/stylized-nature-megakit/FBX (Unity)/Rock_Medium_*.fbx" \
  "quaternius/stylized-nature-megakit/glTF/Bark_NormalTree*.png" \
  "quaternius/stylized-nature-megakit/glTF/Bark_TwistedTree.png" \
  "quaternius/stylized-nature-megakit/glTF/Leaves_NormalTree_C.png" \
  "quaternius/stylized-nature-megakit/glTF/Leaves_TwistedTree_C.png" \
  "quaternius/stylized-nature-megakit/glTF/Leaf_Pine_C.png" \
  "quaternius/stylized-nature-megakit/glTF/Grass.png" \
  "quaternius/stylized-nature-megakit/glTF/Flowers.png" \
  "quaternius/stylized-nature-megakit/glTF/Rocks_Diffuse.png" \
  "quaternius/stylized-nature-megakit/License_Source.txt"
for i in 1 2 3; do git fetch -q --depth 1 --filter=blob:none origin "$COMMIT" && break || sleep 10; done
git checkout -q FETCH_HEAD
Q="$WORK/quaternius"
mkdir -p "$DEST/UAL" "$DEST/City/Models" "$DEST/City/Textures"
cp "$Q/universal-animation-library/Unity/UAL1.fbx" "$DEST/UAL/"
cp "$Q/universal-animation-library/License.txt" "$DEST/UAL/License.txt"
cp "$Q/universal-animation-library-2/Female Mannequin/Unity/Mannequin_F.fbx" "$DEST/UAL/" || true
# eight-direction walks (same skeleton)
cp "$Q/universal-animation-library-2/Unity/UAL2.fbx" "$DEST/UAL/" || true
cp "$Q/downtown-city-megakit/Exports/FBX (Unity)/"*.fbx "$DEST/City/Models/"
cp "$Q/downtown-city-megakit/License_Standard.txt" "$DEST/City/License.txt"
mkdir -p "$DEST/Nature"
cp "$Q/stylized-nature-megakit/FBX (Unity)/"*.fbx "$DEST/Nature/"
cp "$Q/stylized-nature-megakit/glTF/"*.png "$DEST/Nature/"
python3 -m pip install -q pillow
python3 - "$DEST/Nature" <<'PY'
import sys, glob
from PIL import Image
for f in glob.glob(sys.argv[1] + "/*.png"):
    im = Image.open(f)
    if max(im.size) > 1024:
        im.resize((1024, 1024), Image.LANCZOS).save(f)
PY
cp "$Q/stylized-nature-megakit/License_Source.txt" "$DEST/Nature/License.txt"
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
for id in CorrugatedSteel005 CorrugatedSteel007A CorrugatedSteel009 Concrete034 Concrete042A Asphalt025C Metal032 MetalPlates013 Bricks076A PaintedPlaster017 DiamondPlate009 Wood049 Planks037A Grass001 Grass004 PavingStones070 Road007 Tiles101 WoodFloor051 Plaster001 Clay002 Clay007 Ground104 Gravel036L Rock055 RoofingTiles006; do
  rm -f t.zip
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
# ---------------------------------------------------------------- characters: Quaternius Universal Base Characters (CC0)
# Free (pay what you want, $0) download from itch.io: the same skeleton as the animation library,
# realistic skin with a face, eyes, eyebrows, hair and beard. Skipped (mannequin fallback) if itch.io fails.
cd "$WORK"
# free (pay what you want, $0) itch.io download: itch <page url> <zip> <unzip dir>
itch() {
  local P=$1
  local UA="Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36"
  rm -f jar
  curl -sf -A "$UA" -c jar -b jar "$P" -o page.html || return 1
  local CSRF URL SEG UP FILE
  CSRF=$(grep -o 'name="csrf_token" value="[^"]*' page.html | head -1 | sed 's/.*value="//')
  curl -sf -A "$UA" -c jar -b jar -X POST "$P/download_url" -H "X-Requested-With: XMLHttpRequest" -H "Referer: $P" --data-urlencode "csrf_token=$CSRF" -o r1.json || return 1
  URL=$(python3 -c "import json;print(json.load(open('r1.json'))['url'])") || return 1
  SEG=${URL##*/download/}
  curl -sf -A "$UA" -c jar -b jar -L "$URL" -o dl.html || return 1
  CSRF=$(grep -o 'name="csrf_token" value="[^"]*' dl.html | head -1 | sed 's/.*value="//')
  UP=$(grep -o 'data-upload_id="[0-9]*"' dl.html | grep -o '[0-9]*' | head -1)
  curl -sf -A "$UA" -c jar -b jar -X POST "$P/file/$UP?source=game_download&download_key=$SEG" -H "X-Requested-With: XMLHttpRequest" -H "Referer: $URL" --data-urlencode "csrf_token=$CSRF" -o r2.json || return 1
  FILE=$(python3 -c "import json;print(json.load(open('r2.json'))['url'])") || return 1
  curl -sfL "$FILE" -o "$2" && unzip -q -o "$2" -d "$3"
}
ubc() { itch https://quaternius.itch.io/universal-base-characters ubc.zip ubc; }
for i in 1 2 3; do ubc && break || { echo "UBC download attempt $i failed"; sleep 10; }; done
if [ -d ubc ]; then
  U=$(find ubc -maxdepth 1 -type d -name "Universal*" | head -1)
  mkdir -p "$DEST/UBC/Hair" "$DEST/UBC/Textures"
  cp "$U/Base Characters/Unity/"Superhero_*_FullBody.fbx "$DEST/UBC/"
  for h in Hair_Buzzed Hair_BuzzedFemale Hair_Buns Hair_Beard Eyebrows_Regular Eyebrows_Female; do
    cp "$U/Hairstyles/Rigged to Head Bone/FBX (Unity)/$h.fbx" "$DEST/UBC/Hair/" || true
  done
  cp "$U/License_Standard.txt" "$DEST/UBC/License.txt"
  python3 - "$U" "$DEST/UBC/Textures" <<'PY'
import sys, os
from PIL import Image
src, dst = sys.argv[1:3]
T = src + "/Base Characters/Textures"
N = T + "/Normals Unity - Godot"
H = src + "/Hairstyles/Textures"
def out(img, name, size=1024):
    img = img.resize((size, size), Image.LANCZOS) if img.size[0] > size else img
    img.save(f"{dst}/{name}")
    print("ubc texture", name)
def ms(rough):
    r = rough.convert("L").resize((1024, 1024), Image.LANCZOS)
    black = Image.new("L", r.size, 0)
    return Image.merge("RGBA", (black, black, black, r.point(lambda v: 255 - v)))
jobs = [
    (T + "/T_Superhero_Male_Ligh.png", "T_Male_Light_BaseColor.png"),
    (T + "/T_Superhero_Male_Dark.png", "T_Male_Dark_BaseColor.png"),
    (T + "/T_Superhero_Female_Light_BaseColor.png", "T_Female_Light_BaseColor.png"),
    (T + "/T_Superhero_Female_Dark_BaseColor.png", "T_Female_Dark_BaseColor.png"),
    (N + "/T_Superhero_Male_Normal.png", "T_Male_Normal.png"),
    (N + "/T_Superhero_Female_Normal.png", "T_Female_Normal.png"),
    (T + "/T_Eye_Brown.png", "T_Eye_BaseColor.png"),
    (N + "/T_Eye_Normal.png", "T_Eye_Normal.png"),
    (H + "/T_Hair_1_BaseColor.png", "T_Hair_1_BaseColor.png"),
    (H + "/T_Hair_2_BaseColor.png", "T_Hair_2_BaseColor.png"),
    (H + "/Normals Unity - Godot/T_Hair_1_Normal.png", "T_Hair_1_Normal.png"),
    (H + "/Normals Unity - Godot/T_Hair_2_Normal.png", "T_Hair_2_Normal.png"),
]
for path, name in jobs:
    if os.path.exists(path):
        img = Image.open(path)
        out(img.convert("RGBA" if "Normal" in name or "Eye" in name else "RGB"), name, 512 if "Hair" in name else 1024)
    else:
        print("missing", path)
for who in ("Male", "Female"):
    p = f"{T}/T_Superhero_{who}_Roughness.png"
    if os.path.exists(p):
        ms(Image.open(p)).save(f"{dst}/T_{who}_MS.png")
PY
  ls -la "$DEST/UBC" "$DEST/UBC/Hair" "$DEST/UBC/Textures"
else
  echo "::warning::Universal Base Characters could not be downloaded; the mannequins are used"
fi

# ---------------------------------------------------------------- cars: Quaternius Realistic Car Pack (CC0, itch.io)
cd "$WORK"
for i in 1 2 3; do itch https://quaternius.itch.io/lowpoly-cars cars.zip cars && break || { echo "car pack attempt $i failed"; sleep 10; }; done
if [ -d cars ]; then
  mkdir -p "$DEST/Cars"
  find cars -iname "*.fbx" ! -iname "*cop*" ! -iname "*police*" -exec cp {} "$DEST/Cars/" \;
  find cars -iname "*.png" -exec cp {} "$DEST/Cars/" \; || true
  find cars -iname "*license*" -exec cp {} "$DEST/Cars/License.txt" \; || true
  ls -la "$DEST/Cars"
else
  echo "::warning::the car pack could not be downloaded; simple cars are used"
fi

# ---------------------------------------------------------------- sounds (OpenGameArt): footsteps recorded on sand/grass
# and stone by Fantozzi (CC0), pain and death voices by Michel Baradari (CC-BY 3.0, credited in the game)
cd "$WORK"
mkdir -p snd && cd snd
command -v ffmpeg >/dev/null && command -v 7z >/dev/null || { sudo apt-get update -qq && sudo apt-get install -y -qq ffmpeg p7zip-full >/dev/null; } || true
oga() {  # every archive or sound file linked from an OpenGameArt page
  curl -sfL "https://opengameart.org/content/$1" | grep -oE 'https://opengameart.org/sites/default/files/[^"<> ]+\.(7z|zip|ogg|wav|flac)' | sort -u |
    while read -r u; do curl -sfL "$u" -o "$(basename "$u")" || echo "could not download $u"; done
}
oga fantozzis-footsteps-grasssand-stone || true
oga 11-male-human-paindeath-sounds || true
# weapon handling (all CC0): rifle and pistol reloads, a shotgun pump (SpringySpringo), a handgun
# reload (zer0_sol), clicks recorded from a real bolt-action rifle (LFA)
oga gun-reload-sounds || true
oga handgun-reload-sound-effect || true
oga equipment-clicks-iii || true
for a in *.7z *.zip; do [ -f "$a" ] && 7z x -y -o"x_${a%.*}" "$a" >/dev/null || true; done
find . -type f \( -iname "*.ogg" -o -iname "*.wav" -o -iname "*.flac" \) | sort
mkdir -p "$DEST/Sounds/Resources/Audio"
python3 - "$DEST/Sounds/Resources/Audio" <<'PY' || echo "::warning::the sound packs could not be prepared; the made-up sounds are used"
import array, os, subprocess, sys, wave
out = sys.argv[1]
files = sorted(os.path.join(d, f) for d, _, fs in os.walk(".") for f in fs if f.lower().endswith((".ogg", ".wav", ".flac")))
def stem(f): return os.path.splitext(os.path.basename(f))[0].lower()
def pick(f):
    # one file per sound: prefer the lossless copy of a pack that ships both
    return not (f.lower().endswith(".ogg") and any(g != f and stem(g) == stem(f) for g in files))
files = [f for f in files if pick(f)]
def convert(src, name, peak, keep):
    """mono 44.1 kHz 16 bit, leading silence cut, at most `keep` seconds, peak at `peak` of full scale"""
    tmp = os.path.join(out, name + ".tmp.wav")
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", src, "-ac", "1", "-ar", "44100",
                    "-af", f"silenceremove=start_periods=1:start_threshold=-50dB,atrim=0:{keep},afade=t=out:st={keep - 0.04}:d=0.04",
                    "-sample_fmt", "s16", tmp], check=True)
    with wave.open(tmp) as w:
        data = array.array("h", w.readframes(w.getnframes()))
    os.remove(tmp)
    top = max(1, max(abs(x) for x in data))
    data = array.array("h", (int(x * peak * 32767 / top) for x in data))
    with wave.open(os.path.join(out, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(44100); w.writeframes(data.tobytes())
    print("sound", name, "<-", src, f"{len(data) / 44100:.2f}s")
def duration(f):
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", f], capture_output=True, text=True)
    try: return float(r.stdout.strip())
    except ValueError: return 0.0
steps = [f for f in files if "fantozzi" in f.lower()]
for surface, key in (("stone", "stone"), ("sand", "sand")):
    for i, f in enumerate(f for f in steps if key in os.path.basename(f).lower()):
        convert(f, f"fs_{surface}_{i}", 0.8, 0.5)
voices = [f for f in files if "baradari" in f.lower() or "human" in f.lower()]
voices = [f for f in voices if f not in steps]
dying = [f for f in voices if any(k in os.path.basename(f).lower() for k in ("die", "death", "dying"))]
if not dying and voices:
    # no names to go by: the longest cries are the deaths
    dying = sorted(voices, key=duration)[-max(1, len(voices) // 3):]
for i, f in enumerate(dying): convert(f, f"death_{i}", 0.85, 2.0)
for i, f in enumerate(f for f in voices if f not in dying): convert(f, f"pain_{i}", 0.8, 1.0)
def find(*keys):
    for f in files:
        n = os.path.basename(f).lower()
        if all(k in n for k in keys): return f
    return None
for name, keys, keep in (("reload_rifle", ("assaultriflereload",), 3.0), ("reload_pistol", ("reload.wav",), 2.5),
                         ("reload_pistol2", ("gunreload",), 2.5), ("reload_shotgun", ("shotguncock",), 1.2)):
    f = find(*keys)
    if f: convert(f, name, 0.8, keep)
# the bolt-action recording: the loudest moments, each cut out with a little time around it
f = find("equipment_clicks3")
if f:
    tmp = "clicks.wav"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", f, "-ac", "1", "-ar", "44100", "-sample_fmt", "s16", tmp], check=True)
    with wave.open(tmp) as w:
        data = array.array("h", w.readframes(w.getnframes()))
    win = 2205
    loud = sorted(((max(abs(x) for x in data[i:i + win]), i) for i in range(0, len(data) - win, win)), reverse=True)
    picked = []
    for level, i in loud:
        if all(abs(i - j) > 44100 for j in picked): picked.append(i)
        if len(picked) == 4: break
    for k, i in enumerate(sorted(picked)):
        for name, before, length in ((f"bolt_{k}", 0.15, 0.7), (f"dry_{k}", 0.02, 0.14)):
            if name.startswith("dry") and k > 1: continue
            a = max(0, i - int(before * 44100)); seg = data[a:a + int(length * 44100)]
            top = max(1, max(abs(x) for x in seg))
            seg = array.array("h", (int(x * 0.8 * 32767 / top) for x in seg))
            with wave.open(os.path.join(out, name + ".wav"), "wb") as w:
                w.setnchannels(1); w.setsampwidth(2); w.setframerate(44100); w.writeframes(seg.tobytes())
            print("sound", name, "<-", f, f"at {i / 44100:.2f}s")
PY
ls -la "$DEST/Sounds/Resources/Audio" || true

du -sh "$DEST"/*
