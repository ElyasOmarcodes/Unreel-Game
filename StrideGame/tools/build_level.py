#!/usr/bin/env python3
"""
Builds the "Tropical Factory" level for the ArenaRush Stride project.

What it does:
  1. Downloads CC0 assets from Poly Haven (models, PBR textures, HDRI sky) into
     ArenaRush.Game/Resources/Level (skipped for files that already exist).
  2. Writes Stride asset files (.sdtex / .sdmat / .sdm3d / .sdpromodel) into
     ArenaRush.Game/Assets/Level.
  3. Writes ArenaRush.Game/Assets/MainScene.sdscene: beach, palm trees, an old
     factory (furnace tower, chimneys, tanks, pipes, conveyors, fly-wheel, sheds),
     Poly Haven props with physics colliders, a physics-driven player character
     (mannequin with idle/walk/run/jump animations) and a third-person camera.

Usage:  python3 tools/build_level.py [--offline]
--offline skips downloads (props whose files are missing are left out).
All Poly Haven content is CC0 (https://polyhaven.com/license).
"""
import json
import math
import os
import random
import sys
import urllib.request
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = os.path.join(ROOT, "ArenaRush.Game")
ASSETS = os.path.join(GAME, "Assets")
LEVEL_ASSETS = os.path.join(ASSETS, "Level")
LEVEL_RES = os.path.join(GAME, "Resources", "Level")
OFFLINE = "--offline" in sys.argv

NS = uuid.UUID("6f1c2c43-6a43-4d55-9a3c-9d7c0e2b7a11")
random.seed(1234)

# Fixed ids that live in hand-written assets
COMPOSITOR_ID = "f96b754e-e14f-43b1-94a9-4e676dd26fe4"
CAMERA_SLOT_ID = "f1ef5817-0725-4b1a-abc9-845d101ec0e6"
SCENE_ID = "d472907d-661e-40bf-820d-0eecd1599699"
MANNEQUIN_MODEL = "f68ea240-695a-4de5-9884-60d77544fcd6:/mannequinModel/Models/mannequinModel"
ANIMS = {
    "AnimationIdle": "5f286f7e-7d38-44cf-9bc1-3aad7be4f0fa:/mannequinModel/Animations/Idle",
    "AnimationWalk": "9d61b261-66aa-40ed-88ed-3cc448f890c0:/mannequinModel/Animations/Walk",
    "AnimationRun": "448ca260-c8b7-4f63-9bf6-e58a5970a830:/mannequinModel/Animations/Run",
    "AnimationJumpStart": "8e192377-a8d5-4e4d-8b88-21f921be0683:/mannequinModel/Animations/Jump_Start",
    "AnimationJumpMid": "94d6acea-a230-438d-83b3-b99c99a41a53:/mannequinModel/Animations/Jump_Loop",
    "AnimationJumpEnd": "f5d57cf5-c062-4e82-abcc-3ca315f86363:/mannequinModel/Animations/Jump_End",
}


def gid(*parts):
    return str(uuid.uuid5(NS, "/".join(str(p) for p in parts)))


def key(*parts):
    return uuid.uuid5(NS, "key/" + "/".join(str(p) for p in parts)).hex


def f(v):
    return f"{float(v):.6g}" if abs(float(v)) >= 1e-6 else "0.0"


def vec3(v):
    return f"{{X: {f(v[0])}, Y: {f(v[1])}, Z: {f(v[2])}}}"


def quat(q):
    return f"{{X: {f(q[0])}, Y: {f(q[1])}, Z: {f(q[2])}, W: {f(q[3])}}}"


def q_axis(axis, deg):
    a = math.radians(deg) / 2
    s = math.sin(a)
    return (axis[0] * s, axis[1] * s, axis[2] * s, math.cos(a))


def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def q_rot(yaw=0.0, pitch=0.0, roll=0.0):
    """Roll (Z) first, then pitch (X), then yaw (Y)."""
    return q_mul(q_axis((0, 1, 0), yaw), q_mul(q_axis((1, 0, 0), pitch), q_axis((0, 0, 1), roll)))


def q_apply(q, v):
    x, y, z, w = q
    qv = (v[0], v[1], v[2], 0.0)
    r = q_mul(q_mul(q, qv), (-x, -y, -z, w))
    return (r[0], r[1], r[2])


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", newline="\n") as fh:
        fh.write(text)


# ---------------------------------------------------------------- downloads
UA = {"User-Agent": "ArenaRush-LevelBuilder/1.0 (CC0 asset fetch)"}


def http_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
        return json.load(r)


def download(url, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return True
    if OFFLINE:
        return False
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r, open(dest + ".part", "wb") as out:
            out.write(r.read())
        os.replace(dest + ".part", dest)
        return True
    except Exception as e:  # keep building with what we have
        print(f"  ! download failed {url}: {e}")
        return False


def fetch_model(asset_id):
    """Downloads the 1k glTF of a Poly Haven model. Returns the .gltf path or None."""
    folder = os.path.join(LEVEL_RES, "Models", asset_id)
    gltf_path = os.path.join(folder, f"{asset_id}_1k.gltf")
    if os.path.exists(gltf_path):
        return gltf_path
    if OFFLINE:
        return None
    try:
        files = http_json(f"https://api.polyhaven.com/files/{asset_id}")
        entry = files["gltf"]["1k"]["gltf"]
    except Exception as e:
        print(f"  ! no glTF for {asset_id}: {e}")
        return None
    ok = True
    for rel, inc in entry.get("include", {}).items():
        ok &= download(inc["url"], os.path.join(folder, rel))
    ok &= download(entry["url"], gltf_path)
    return gltf_path if ok else None


def fetch_texture_set(asset_id, maps=("diff", "nor_gl", "arm"), res="1k"):
    out = {}
    for m in maps:
        dest = os.path.join(LEVEL_RES, "Textures", asset_id, f"{asset_id}_{m}_{res}.jpg")
        url = f"https://dl.polyhaven.org/file/ph-assets/Textures/jpg/{res}/{asset_id}/{asset_id}_{m}_{res}.jpg"
        if download(url, dest):
            out[m] = dest
    return out


def fetch_hdri(asset_id, res="1k"):
    dest = os.path.join(LEVEL_RES, "Sky", f"{asset_id}_{res}.hdr")
    url = f"https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/{res}/{asset_id}_{res}.hdr"
    return dest if download(url, dest) else None


def hdr_to_png(hdr_path, exposure=1.0):
    """Converts a Radiance .hdr (RGBE, RLE) to an 8-bit sRGB PNG (pure Python; Stride on Linux cannot load .hdr)."""
    import struct, zlib
    png_path = os.path.splitext(hdr_path)[0] + ".png"
    if os.path.exists(png_path):
        return png_path
    data = open(hdr_path, "rb").read()
    pos = 0
    while True:
        end = data.index(b"\n", pos)
        line = data[pos:end]
        pos = end + 1
        if line.startswith(b"-Y") or line.startswith(b"+Y"):
            parts = line.split()
            h, w = int(parts[1]), int(parts[3])
            break
    out = bytearray()
    for y in range(h):
        if data[pos] == 2 and data[pos + 1] == 2:
            pos += 4
            chans = []
            for c in range(4):
                row = bytearray()
                while len(row) < w:
                    n = data[pos]; pos += 1
                    if n > 128:
                        row += bytes([data[pos]]) * (n - 128); pos += 1
                    else:
                        row += data[pos:pos + n]; pos += n
                chans.append(row)
            px = zip(chans[0], chans[1], chans[2], chans[3])
        else:
            px = [tuple(data[pos + 4 * i: pos + 4 * i + 4]) for i in range(w)]
            pos += 4 * w
        out.append(0)
        for r, g, b, e in px:
            sc = math.ldexp(1.0, e - 136) * exposure if e else 0.0
            for v in (r * sc, g * sc, b * sc):
                v = v / (1.0 + v)  # Reinhard
                out.append(min(255, int(255 * (v ** (1 / 2.2)) + 0.5)))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress(bytes(out), 9)) + chunk(b"IEND", b"")
    open(png_path, "wb").write(png)
    return png_path


# ---------------------------------------------------------------- stride assets
def rel_from(asset_file, target):
    return os.path.relpath(target, os.path.dirname(asset_file)).replace(os.sep, "/")


def asset_url(path):
    return os.path.relpath(os.path.splitext(path)[0], ASSETS).replace(os.sep, "/")


def ref(path):
    """Asset reference '<id>:<url>' for an asset written by this script."""
    return f"{gid('asset', asset_url(path))}:{asset_url(path)}"


TEX_CACHE = {}


def texture_asset(src, kind, scale_pct=100):
    """kind: 'color' (sRGB), 'linear', 'normal'."""
    k = (src, kind, scale_pct)
    if k in TEX_CACHE:
        return TEX_CACHE[k]
    name = os.path.splitext(os.path.basename(src))[0]
    path = os.path.join(LEVEL_ASSETS, "Textures", f"{name}_{kind}_{scale_pct}.sdtex")
    if kind == "normal":
        ttype = "Type: !NormalMapTextureType {}"
    elif kind == "linear":
        ttype = "Type: !ColorTextureType\n    UseSRgbSampling: false\n    ColorKeyColor: {R: 255, G: 0, B: 255, A: 255}"
    else:
        ttype = "Type: !ColorTextureType\n    ColorKeyColor: {R: 255, G: 0, B: 255, A: 255}"
    size = "" if scale_pct == 100 else f"Width: {scale_pct}.0\nHeight: {scale_pct}.0\n"
    write(path, f"""!Texture
Id: {gid('asset', asset_url(path))}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
Source: {rel_from(path, src)}
{size}{ttype}
""")
    TEX_CACHE[k] = ref(path)
    return TEX_CACHE[k]


def tex_color(tref, uv=(1, 1)):
    return f"""!ComputeTextureColor
            Texture: {tref}
            FallbackValue:
                Value: {{R: 1.0, G: 1.0, B: 1.0, A: 1.0}}
            Scale: {{X: {f(uv[0])}, Y: {f(uv[1])}}}
            Offset: {{X: 0.0, Y: 0.0}}
            Swizzle: null"""


def tex_scalar(tref, channel, uv=(1, 1), indent="            "):
    return f"""!ComputeTextureScalar
{indent}Texture: {tref}
{indent}FallbackValue:
{indent}    Value: 1.0
{indent}Scale: {{X: {f(uv[0])}, Y: {f(uv[1])}}}
{indent}Offset: {{X: 0.0, Y: 0.0}}
{indent}Channel: {channel}"""


def material(name, diffuse=None, normal=None, arm=None, color=(0.6, 0.6, 0.6), rough=0.8, metal=0.0,
             uv=(1, 1), cutout=False, two_sided=False, tex_scale=50, emissive=None):
    """PBR material. diffuse/normal/arm are image paths (Poly Haven 'arm' = AO, roughness, metalness)."""
    path = os.path.join(LEVEL_ASSETS, "Materials", f"{name}.sdmat")
    lines = ["Attributes:"]
    if normal:
        lines.append(f"""    Surface: !MaterialNormalMapFeature
        NormalMap: {tex_color(texture_asset(normal, 'normal', tex_scale), uv)}
        IsXYNormal: true""")
    if arm:
        lines.append(f"""    MicroSurface: !MaterialGlossinessMapFeature
        GlossinessMap: {tex_scalar(texture_asset(arm, 'linear', tex_scale), 'G', uv)}
        Invert: true""")
    else:
        lines.append(f"""    MicroSurface: !MaterialGlossinessMapFeature
        GlossinessMap: !ComputeFloat
            Value: {f(1 - rough)}""")
    if diffuse:
        lines.append(f"""    Diffuse: !MaterialDiffuseMapFeature
        DiffuseMap: {tex_color(texture_asset(diffuse, 'color', tex_scale), uv)}""")
    else:
        lines.append(f"""    Diffuse: !MaterialDiffuseMapFeature
        DiffuseMap: !ComputeColor
            Value: {{R: {f(color[0])}, G: {f(color[1])}, B: {f(color[2])}, A: 1.0}}""")
    lines.append("    DiffuseModel: !MaterialDiffuseLambertModelFeature {}")
    if arm:
        lines.append(f"""    Specular: !MaterialMetalnessMapFeature
        MetalnessMap: {tex_scalar(texture_asset(arm, 'linear', tex_scale), 'B', uv)}""")
    else:
        lines.append(f"""    Specular: !MaterialMetalnessMapFeature
        MetalnessMap: !ComputeFloat
            Value: {f(metal)}""")
    lines.append("""    SpecularModel: !MaterialSpecularMicrofacetModelFeature
        Fresnel: !MaterialSpecularMicrofacetFresnelSchlick {}
        Visibility: !MaterialSpecularMicrofacetVisibilitySmithSchlickGGX {}
        NormalDistribution: !MaterialSpecularMicrofacetNormalDistributionGGX {}
        Environment: !MaterialSpecularMicrofacetEnvironmentGGXLUT {}""")
    if arm:
        lines.append(f"""    Occlusion: !MaterialOcclusionMapFeature
        AmbientOcclusionMap: {tex_scalar(texture_asset(arm, 'linear', tex_scale), 'R', uv)}
        DirectLightingFactor: !ComputeFloat
            Value: 0.0
        CavityMap: !ComputeFloat
            Value: 1.0
        DiffuseCavity: !ComputeFloat
            Value: 1.0
        SpecularCavity: !ComputeFloat
            Value: 1.0""")
    if emissive:
        lines.append(f"""    Emissive: !MaterialEmissiveMapFeature
        EmissiveMap: !ComputeColor
            Value: {{R: {f(emissive[0])}, G: {f(emissive[1])}, B: {f(emissive[2])}, A: 1.0}}
        Intensity: !ComputeFloat
            Value: {f(emissive[3])}""")
    if cutout:
        lines.append("""    Transparency: !MaterialTransparencyCutoffFeature
        Alpha: !ComputeFloat
            Value: 0.5""")
    lines.append("""    Overrides:
        UVScale: {X: 1.0, Y: 1.0}""")
    if two_sided:
        lines.append("    CullMode: None")
    write(path, f"""!MaterialAsset
Id: {gid('asset', asset_url(path))}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
{chr(10).join(lines)}
Layers: {{}}
""")
    return ref(path)


PROC_CACHE = {}


def proc_model(kind, dims, mat_ref, uv=(1, 1), tess=None, offset=(0, 0, 0)):
    """Stride procedural model (Cube/Cylinder/Plane/Torus/Cone) with a material."""
    k = (kind, tuple(dims), mat_ref, tuple(uv), tess, tuple(offset))
    if k in PROC_CACHE:
        return PROC_CACHE[k]
    name = f"{kind}_{len(PROC_CACHE):03d}"
    path = os.path.join(LEVEL_ASSETS, "Shapes", f"{name}.sdpromodel")
    if kind == "Cube":
        body = f"    Size: {vec3(dims)}\n"
    elif kind == "Cylinder":
        body = f"    Height: {f(dims[1])}\n    Radius: {f(dims[0])}\n    Tessellation: {tess or 24}\n"
    elif kind == "Cone":
        body = f"    Height: {f(dims[1])}\n    Radius: {f(dims[0])}\n    Tessellation: {tess or 24}\n"
    elif kind == "Torus":
        body = f"    Radius: {f(dims[0])}\n    Thickness: {f(dims[1])}\n    Tessellation: {tess or 32}\n"
    elif kind == "Plane":
        body = (f"    Size: {{X: {f(dims[0])}, Y: {f(dims[1])}}}\n    Tessellation: {{X: 1, Y: 1}}\n"
                f"    Normal: UpY\n    GenerateBackFace: {'true' if (len(dims) > 2 and dims[2]) else 'false'}\n")
    else:
        raise ValueError(kind)
    write(path, f"""!ProceduralModelAsset
Id: {gid('asset', asset_url(path))}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
Type: !{kind}ProceduralModel
{body}    Scale: {{X: 1.0, Y: 1.0, Z: 1.0}}
    UvScale: {{X: {f(uv[0])}, Y: {f(uv[1])}}}
    LocalOffset: {vec3(offset)}
    NumberOfTextureCoordinates: 10
    MaterialInstance:
        Material: {mat_ref}
""")
    PROC_CACHE[k] = ref(path)
    return PROC_CACHE[k]


MODEL_CACHE = {}


def gltf_model(asset_id):
    """Imports a Poly Haven glTF: materials from its PBR textures, model asset, and local AABB."""
    if asset_id in MODEL_CACHE:
        return MODEL_CACHE[asset_id]
    gltf_path = fetch_model(asset_id)
    if not gltf_path:
        MODEL_CACHE[asset_id] = None
        return None
    folder = os.path.dirname(gltf_path)
    g = json.load(open(gltf_path))
    images = [os.path.join(folder, im.get("uri", "")) for im in g.get("images", [])]

    def tex_path(info):
        if not info:
            return None
        src = g["textures"][info["index"]].get("source")
        p = images[src] if src is not None else None
        return p if p and os.path.exists(p) else None

    mats = []
    for i, m in enumerate(g.get("materials", [])):
        pbr = m.get("pbrMetallicRoughness", {})
        mname = m.get("name") or f"material_{i}"
        mref = material(
            f"{asset_id}__{i}",
            diffuse=tex_path(pbr.get("baseColorTexture")),
            normal=tex_path(m.get("normalTexture")),
            arm=tex_path(pbr.get("metallicRoughnessTexture")),
            color=tuple(pbr.get("baseColorFactor", [0.6, 0.6, 0.6, 1])[:3]),
            cutout=m.get("alphaMode") == "MASK",
            two_sided=bool(m.get("doubleSided")),
            tex_scale=25,
        )
        mats.append((mname, mref))

    # AABB from POSITION accessors of every mesh used by a node (node transforms ignored; Poly Haven models are baked)
    lo, hi = [1e9] * 3, [-1e9] * 3
    for mesh in g.get("meshes", []):
        for prim in mesh.get("primitives", []):
            acc = g["accessors"][prim["attributes"]["POSITION"]]
            if "min" in acc and "max" in acc:
                lo = [min(a, b) for a, b in zip(lo, acc["min"])]
                hi = [max(a, b) for a, b in zip(hi, acc["max"])]
    if lo[0] > hi[0]:
        lo, hi = [-0.5, 0, -0.5], [0.5, 1, 0.5]

    path = os.path.join(LEVEL_ASSETS, "Models", f"{asset_id}.sdm3d")
    mat_lines = "".join(
        f"    {key(asset_id, i)}:\n        Name: {json.dumps(n)}\n        MaterialInstance:\n            Material: {r}\n"
        for i, (n, r) in enumerate(mats))
    write(path, f"""!Model
Id: {gid('asset', asset_url(path))}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
Source: {rel_from(path, gltf_path)}
PivotPosition: {{X: 0.0, Y: 0.0, Z: 0.0}}
Materials:
{mat_lines if mat_lines else '    {}'}
Skeleton: null
""".replace("Materials:\n    {}", "Materials: {}"))
    MODEL_CACHE[asset_id] = (ref(path), lo, hi)
    return MODEL_CACHE[asset_id]


# ---------------------------------------------------------------- scene building
ENTITIES = []   # (id, yaml)
ROOTS = []


class Ent:
    def __init__(self, name, pos=(0, 0, 0), rot=(0, 0, 0, 1), scale=(1, 1, 1)):
        self.id = gid("entity", name, len(ENTITIES))
        self.name = name
        self.pos, self.rot, self.scale = pos, rot, scale
        self.components = []
        self.children = []   # transform ids
        self.transform_id = gid("transform", self.id)

    def add(self, text):
        self.components.append(text)
        return self

    def child(self, ent):
        self.children.append(ent.transform_id)
        return ent

    def emit(self, root=True):
        ch = "".join(f"\n                            {key(self.id, 'child', i)}: ref!! {c}" for i, c in enumerate(self.children))
        comps = f"""                    {key(self.id, 'transform')}: !TransformComponent
                        Id: {self.transform_id}
                        Position: {vec3(self.pos)}
                        Rotation: {quat(self.rot)}
                        Scale: {vec3(self.scale)}
                        Children:{ch if ch else ' {}'}
"""
        for i, c in enumerate(self.components):
            comps += c.replace("@KEY@", key(self.id, "c", i)).replace("@ID@", gid("component", self.id, i))
        ENTITIES.append((self.id, f"""        -   Entity:
                Id: {self.id}
                Name: {json.dumps(self.name)}
                Components:
{comps}"""))
        if root:
            ROOTS.append(self.id)
        return self


def model_comp(model_ref, cast_shadows=True):
    return f"""                    @KEY@: !ModelComponent
                        Id: @ID@
                        Model: {model_ref}
                        IsShadowCaster: {'true' if cast_shadows else 'false'}
                        Materials: {{}}
"""


def box_shape(size, offset=(0, 0, 0)):
    return f"""                            {uuid.uuid4().hex}: !BoxColliderShapeDesc
                                Is2D: false
                                Size: {vec3(size)}
                                LocalOffset: {vec3(offset)}
                                LocalRotation: {{X: 0.0, Y: 0.0, Z: 0.0, W: 1.0}}
"""


def cyl_shape(radius, height, offset=(0, 0, 0)):
    return f"""                            {uuid.uuid4().hex}: !CylinderColliderShapeDesc
                                Height: {f(height)}
                                Radius: {f(radius)}
                                Orientation: UpY
                                LocalOffset: {vec3(offset)}
                                LocalRotation: {{X: 0.0, Y: 0.0, Z: 0.0, W: 1.0}}
"""


def static_collider(shapes, friction=0.6):
    return f"""                    @KEY@: !StaticColliderComponent
                        Id: @ID@
                        CanSleep: true
                        Restitution: 0.0
                        Friction: {f(friction)}
                        RollingFriction: 0.0
                        CcdMotionThreshold: 0.0
                        CcdSweptSphereRadius: 0.0
                        IsTrigger: false
                        AlwaysUpdateNaviMeshCache: false
                        ColliderShapes:
{''.join(shapes)}"""


def rigidbody(shapes, mass):
    return f"""                    @KEY@: !RigidbodyComponent
                        Id: @ID@
                        CanSleep: true
                        Restitution: 0.1
                        Friction: 0.7
                        RollingFriction: 0.05
                        CcdMotionThreshold: 0.0
                        CcdSweptSphereRadius: 0.0
                        IsTrigger: false
                        IsKinematic: false
                        Mass: {f(mass)}
                        LinearDamping: 0.05
                        AngularDamping: 0.1
                        OverrideGravity: false
                        Gravity: {{X: 0.0, Y: 0.0, Z: 0.0}}
                        NodeName: null
                        ColliderShapes:
{''.join(shapes)}"""


def shape(name, kind, dims, mat_ref, pos, rot=(0, 0, 0, 1), uv=(1, 1), collide=True, tess=None, offset=(0, 0, 0), shadows=True):
    """A procedural mesh placed in the world. Cylinders/cones are Y-up, cubes are centred."""
    e = Ent(name, pos, rot).add(model_comp(proc_model(kind, dims, mat_ref, uv, tess, offset), shadows))
    if collide:
        if kind == "Cube":
            e.add(static_collider([box_shape(dims, offset)]))
        elif kind in ("Cylinder", "Cone"):
            e.add(static_collider([cyl_shape(dims[0], dims[1], offset)]))
    return e.emit()


def prop(asset_id, pos, yaw=0.0, scale=1.0, dynamic_mass=0.0, collide=True, name=None):
    m = gltf_model(asset_id)
    if not m:
        return None
    mref, lo, hi = m
    size = [(hi[i] - lo[i]) * scale for i in range(3)]
    center = [(hi[i] + lo[i]) / 2 * scale for i in range(3)]
    e = Ent(name or asset_id, pos, q_rot(yaw), (scale, scale, scale)).add(model_comp(mref))
    if collide and max(size) > 0.15:
        # collider shapes are expressed in the scaled entity space
        sh = [box_shape([max(s, 0.05) / scale for s in size], [c / scale for c in center])]
        e.add(rigidbody(sh, dynamic_mass) if dynamic_mass > 0 else static_collider(sh))
    return e.emit()


# ---------------------------------------------------------------- level content
def build():
    os.makedirs(LEVEL_ASSETS, exist_ok=True)
    print("Fetching textures ...")
    T = {name: fetch_texture_set(name) for name in [
        "sand_01", "sandstone_cracks", "worn_corrugated_iron", "rusty_metal_02", "rusty_metal_sheet",
        "stone_wall_03", "worn_concrete_floor", "rusty_painted_metal", "weathered_planks",
        "stacked_stone_wall", "rocky_terrain_02"]}

    def mat_from(name, tex, uv=(1, 1), **kw):
        t = T.get(tex, {})
        return material(name, t.get("diff"), t.get("nor_gl"), t.get("arm"), uv=uv, **kw)

    sand = mat_from("Sand", "sand_01", rough=0.95)
    cracked = mat_from("CrackedGround", "sandstone_cracks", rough=0.9)
    corrugated = mat_from("CorrugatedIron", "worn_corrugated_iron")
    rusty = mat_from("RustyMetal", "rusty_metal_02")
    rusty_sheet = mat_from("RustySheet", "rusty_metal_sheet")
    concrete = mat_from("Concrete", "stone_wall_03")
    concrete_floor = mat_from("ConcreteFloor", "worn_concrete_floor")
    painted = mat_from("PaintedMetal", "rusty_painted_metal")
    planks = mat_from("Planks", "weathered_planks")
    stone = mat_from("StoneWall", "stacked_stone_wall")
    dark_metal = material("DarkMetal", color=(0.08, 0.08, 0.085), rough=0.45, metal=1.0)
    nature = os.path.join(GAME, "Resources", "Nature")
    bark = material("PalmBark", os.path.join(nature, "palm_bark.jpg"), rough=0.9)
    frond = material("PalmFrond", os.path.join(nature, "palm_frond.png"), rough=0.7, cutout=True, two_sided=True)
    glow = material("FurnaceGlow", color=(1.0, 0.45, 0.1), rough=0.6, emissive=(1.0, 0.4, 0.08, 6.0))

    # ---- environment: sky, sun, ground
    hdri = fetch_hdri("kloofendal_48d_partly_cloudy_puresky")
    sky_tex = texture_asset(hdr_to_png(hdri, exposure=1.6), "color") if hdri else None
    sky_path = os.path.join(LEVEL_ASSETS, "Sky.sdsky")
    if sky_tex:
        write(sky_path, f"""!SkyboxAsset
Id: {gid('asset', asset_url(sky_path))}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
CubeMap: {sky_tex}
""")
        sky = Ent("Sky", (0, 0, 0))
        sky.add(f"""                    @KEY@: !BackgroundComponent
                        Id: @ID@
                        Texture: {sky_tex}
""")
        sky.add(f"""                    @KEY@: !LightComponent
                        Id: @ID@
                        Type: !LightSkybox
                            Skybox: {ref(sky_path)}
                        Intensity: 2.5
""")
        sky.emit()

    sun = Ent("Sun", (0, 30, 0), q_rot(yaw=-35, pitch=-52))
    sun.add("""                    @KEY@: !LightComponent
                        Id: @ID@
                        Type: !LightDirectional
                            Color: !ColorRgbProvider
                                Value: {R: 1.0, G: 0.95, B: 0.86}
                            Shadow:
                                Enabled: true
                                Filter: !LightShadowMapFilterTypePcf
                                    FilterSize: Filter3x3
                                Size: Medium
                                CascadeCount: TwoCascades
                                DepthRange:
                                    IsAutomatic: false
                                    ManualMinDistance: 0.0
                                    ManualMaxDistance: 45.0
                                PartitionMode: !LightDirectionalShadowMap.PartitionLogarithmic {}
                                ComputeTransmittance: false
                                BiasParameters: {}
                        Intensity: 12.0
""").emit()

    # Big sand beach + cracked factory yard
    shape("Beach", "Plane", (240, 240), sand, (0, 0, 0), uv=(60, 60), collide=False)
    Ent("GroundCollider", (0, -0.5, 0)).add(static_collider([box_shape((240, 1, 240))], friction=0.8)).emit()
    shape("FactoryYard", "Plane", (56, 46), cracked, (0, 0.02, -18), uv=(10, 8), collide=False)
    shape("ConcretePad", "Cube", (22, 0.3, 14), concrete_floor, (6, 0.15, -30), uv=(4, 3))

    # ---- furnace tower (like the big grey kiln with chimneys)
    fx, fz = 10, -30
    shape("FurnaceBase", "Cube", (11, 4.5, 9), concrete, (fx, 2.25 + 0.3, fz), uv=(3, 1.5))
    shape("FurnaceTower", "Cone", (5.2, 9.0), concrete, (fx, 4.8 + 4.5, fz), uv=(3, 2))
    shape("FurnaceRing", "Cylinder", (5.4, 0.6), dark_metal, (fx, 5.0, fz), collide=False)
    for i, (dx, dz) in enumerate([(-1.4, -0.8), (1.2, -1.0), (0.1, 1.3)]):
        shape(f"Chimney{i}", "Cylinder", (0.65, 6.0), rusty, (fx + dx, 12.5, fz + dz), uv=(2, 2))
        shape(f"ChimneyCap{i}", "Torus", (0.7, 0.12), dark_metal, (fx + dx, 15.5, fz + dz), collide=False)
    for i, dx in enumerate((-2.8, 0.0, 2.8)):  # glowing furnace doors
        shape(f"FurnaceDoor{i}", "Cube", (1.6, 1.8, 0.1), glow, (fx + dx, 1.6, fz + 4.55), collide=False)
        shape(f"FurnaceFrame{i}", "Cube", (2.0, 2.2, 0.2), dark_metal, (fx + dx, 1.6, fz + 4.62 - 0.12), collide=False)

    # ---- storage tanks with pipe runs
    for i, (tx, tz) in enumerate([(-16, -32), (-10, -36), (-20, -24)]):
        shape(f"Tank{i}", "Cylinder", (2.6, 7.5), rusty_sheet, (tx, 3.75, tz), uv=(3, 2))
        shape(f"TankRoof{i}", "Cone", (2.7, 1.2), rusty, (tx, 8.1, tz), collide=False)
        for ly in (1.5, 4.0, 6.5):
            shape(f"TankBand{i}_{ly}", "Torus", (2.62, 0.08), dark_metal, (tx, ly, tz), collide=False)
    pipe_y = 5.2
    shape("PipeA", "Cylinder", (0.32, 26.0), rusty, (-3, pipe_y, -32), q_rot(roll=90), uv=(1, 8), collide=False)
    shape("PipeB", "Cylinder", (0.25, 12.0), rusty, (-15, pipe_y - 0.8, -28), q_rot(pitch=90), uv=(1, 4), collide=False)
    for x in range(-14, 10, 4):
        shape(f"PipeSupport{x}", "Cube", (0.25, pipe_y, 0.25), dark_metal, (x, pipe_y / 2, -32))

    # ---- conveyor lines with crates riding on them
    for row, (cz, length, cx) in enumerate([(-14, 26, -2), (-8, 18, 6)]):
        shape(f"ConveyorBed{row}", "Cube", (length, 0.25, 1.4), painted, (cx, 1.1, cz), uv=(length / 2, 1))
        shape(f"ConveyorRailL{row}", "Cube", (length, 0.2, 0.08), dark_metal, (cx, 1.3, cz - 0.72), collide=False)
        shape(f"ConveyorRailR{row}", "Cube", (length, 0.2, 0.08), dark_metal, (cx, 1.3, cz + 0.72), collide=False)
        for i in range(int(length // 3) + 1):
            x = cx - length / 2 + i * 3
            shape(f"ConveyorLeg{row}_{i}", "Cube", (0.12, 1.0, 1.3), dark_metal, (x, 0.5, cz), collide=False)
        for i in range(int(length // 2.2)):
            prop("wooden_crate_02", (cx - length / 2 + 1.2 + i * 2.2, 1.23, cz), yaw=random.uniform(-6, 6), scale=0.55, collide=False,
                 name=f"ConveyorCrate{row}_{i}")

    # ---- big fly-wheel (like the iron wheel in the reference)
    wx, wz = -8, -20
    wheel_rot = q_rot(yaw=20, pitch=90)
    shape("FlyWheel", "Torus", (2.6, 0.18), dark_metal, (wx, 3.2, wz), wheel_rot, collide=False, tess=48)
    for i in range(8):
        a = i * 22.5
        shape(f"WheelSpoke{i}", "Cube", (5.1, 0.12, 0.12), dark_metal, (wx, 3.2, wz), q_mul(q_rot(yaw=20), q_axis((0, 0, 1), a)), collide=False)
    shape("WheelAxle", "Cylinder", (0.25, 1.2), rusty, (wx, 3.2, wz), q_mul(q_rot(yaw=20), q_axis((1, 0, 0), 90)), collide=False)
    shape("WheelStandL", "Cube", (0.4, 3.2, 0.4), painted, (wx - 0.2, 1.6, wz - 0.55))
    shape("WheelStandR", "Cube", (0.4, 3.2, 0.4), painted, (wx + 0.2, 1.6, wz + 0.55))
    shape("EngineBlock", "Cube", (3.5, 2.2, 2.0), painted, (wx + 3.0, 1.1, wz + 1.2), q_rot(yaw=20))

    # ---- steel lattice derrick
    dx0, dz0, h = 18, -14, 12
    for i, (ox, oz) in enumerate([(-1.6, -1.6), (1.6, -1.6), (1.6, 1.6), (-1.6, 1.6)]):
        lean = q_rot(pitch=-math.degrees(math.atan2(oz * 0.7, h)), roll=math.degrees(math.atan2(ox * 0.7, h)))
        shape(f"DerrickLeg{i}", "Cube", (0.22, h, 0.22), dark_metal, (dx0 + ox * 0.65, h / 2, dz0 + oz * 0.65), lean, collide=False)
    for lvl in range(1, 6):
        y = lvl * 2.1
        w = 3.2 * (1 - y / h * 0.7)
        for j, (sx, sz, rx) in enumerate([(w, 0.12, 0), (0.12, w, 0)]):
            for side in (-1, 1):
                off = (0, side * w / 2) if j == 0 else (side * w / 2, 0)
                shape(f"DerrickBrace{lvl}_{j}_{side}", "Cube", (sx, 0.12, sz), dark_metal, (dx0 + off[0], y, dz0 + off[1]), collide=False)
    shape("DerrickCollider", "Cube", (3.4, 1.0, 3.4), dark_metal, (dx0, 0.5, dz0))
    shape("DerrickWreckingBall", "Cylinder", (0.03, 4.0), dark_metal, (dx0 + 1.5, h - 2.5, dz0), collide=False)

    # ---- corrugated sheds
    for i, (sx, sz, yaw, w, d, hh) in enumerate([(-24, -8, 10, 9, 6, 4.2), (26, -32, -15, 8, 7, 4.8), (22, 2, 80, 7, 5, 3.6)]):
        rot = q_rot(yaw=yaw)
        shape(f"Shed{i}", "Cube", (w, hh, d), corrugated, (sx, hh / 2, sz), rot, uv=(w / 2, hh / 2))
        shape(f"ShedRoof{i}", "Cube", (w + 0.8, 0.15, d + 0.8), rusty_sheet, (sx, hh + 0.1, sz), rot, uv=(w / 2, d / 2), collide=False)
        shape(f"ShedBase{i}", "Cube", (w + 0.4, 0.3, d + 0.4), concrete_floor, (sx, 0.15, sz), rot, collide=False)
    shape("StoneWall", "Cube", (18, 2.0, 0.6), stone, (-6, 1.0, -44), uv=(6, 1))
    shape("Walkway", "Cube", (12, 0.15, 2.0), planks, (2, 1.9, -20), uv=(4, 1))
    for x in (-3.5, 0.5, 4.5, 7.5):
        shape(f"WalkwayPost{x}", "Cube", (0.2, 1.9, 0.2), dark_metal, (x, 0.95, -20.8), collide=False)
        shape(f"WalkwayPostB{x}", "Cube", (0.2, 1.9, 0.2), dark_metal, (x, 0.95, -19.2), collide=False)

    # ---- Poly Haven props (static and physics-driven)
    statics = [
        ("portable_generator", (3, 0, -2), 30), ("old_military_compressor", (-12, 0, -12), -20),
        ("overhead_crane", (2, 0, -40), 0), ("exterior_aircon_unit", (-20, 0, -4), 90),
        ("utility_box_01", (14, 0, -6), -90), ("utility_box_02", (15.2, 0, -6), -90),
        ("small_lpg_tank", (-17, 0, -16), 15), ("propane_tank", (-14, 0, -18), 0),
        ("covered_car", (-26, 0, 6), 35), ("concrete_road_barrier_02", (-4, 0, 6), 0),
        ("concrete_road_barrier_02", (0, 0, 6), 0), ("steel_frame_shelves_01", (-22, 0, -11.5), 10),
        ("street_lamp_02", (-6, 0, 2), 0), ("street_lamp_02", (12, 0, 2), 180), ("tool_cart", (6, 0, -12), 60),
        ("hand_truck", (8, 0, -4), -30), ("metal_trash_can", (-9, 0, 0), 0), ("stone_fire_pit", (-30, 0, 18), 0),
        ("rock_07", (-38, 0, -20), 40), ("rock_09", (34, 0, -24), 120), ("rock_07", (30, 0, 22), 200),
        ("rock_face_01", (-44, 0, -42), 30), ("rock_face_02", (40, 0, -46), -40),
    ]
    for aid, p, yaw in statics:
        prop(aid, p, yaw)
    for aid, p, yaw in [("fern_02", (-30, 0, 12), 0), ("fern_02", (24, 0, 14), 60), ("shrub_03", (-36, 0, 2), 0),
                        ("shrub_03", (36, 0, 6), 90), ("shrub_02", (-12, 0, 20), 0), ("fern_02", (10, 0, 22), 120)]:
        prop(aid, p, yaw, collide=False)

    # physics: stacks of crates and barrels you can push around
    crate_ids = ["wooden_crate_01", "wooden_crate_02", "old_military_crate", "wooden_military_crate"]
    for s, (bx, bz) in enumerate([(-4, -4), (10, -10), (-16, 2), (4, 10)]):
        for i in range(3):
            for j in range(2 if i < 2 else 1):
                prop(random.choice(crate_ids), (bx + i * 1.1 - 1.1 + j * 0.5, 0.02 + j * 1.4, bz + random.uniform(-0.2, 0.2)),
                     yaw=random.uniform(-15, 15), scale=1.0, dynamic_mass=25, name=f"Crate{s}_{i}_{j}")
    for i in range(10):
        a = random.uniform(0, 360)
        r = random.uniform(4, 12)
        prop(random.choice(["Barrel_01", "barrel_03"]), (2 + math.cos(a) * r, 0.02, -24 + math.sin(a) * r),
             yaw=random.uniform(0, 360), dynamic_mass=40, name=f"Barrel{i}")
    for i, p in enumerate([(-6, 0.02, 12), (16, 0.02, 8), (-18, 0.02, -2)]):
        prop("old_tyre", p, yaw=random.uniform(0, 360), dynamic_mass=15, name=f"Tyre{i}")
    prop("metal_jerrycan_green", (-1, 0.02, -2), 20, dynamic_mass=8)
    prop("ammo_box", (1.5, 0.02, -1), 70, dynamic_mass=6)
    prop("wooden_barrels_01", (-24, 0.0, -14), 0)

    # ---- energy cells to collect (first gameplay goal)
    cell_mat = material("EnergyCell", color=(0.1, 0.9, 1.0), rough=0.3, metal=0.2, emissive=(0.2, 0.9, 1.0, 5.0))
    cell_cap = material("EnergyCellCap", color=(0.15, 0.15, 0.16), rough=0.4, metal=1.0)
    cell_spots = [(-6, -8), (8, -18), (-14, -26), (16, -26), (2, -36), (-22, -18), (24, -12), (-10, 4),
                  (12, 6), (-26, 8), (28, 14), (0, 18)]
    for i, (x, z) in enumerate(cell_spots):
        cell = Ent(f"EnergyCell{i}", (x, 1.0, z))
        core = Ent(f"EnergyCellCore{i}", (0, 0, 0))
        cap_top = Ent(f"EnergyCellCapTop{i}", (0, 0.32, 0))
        cap_bottom = Ent(f"EnergyCellCapBottom{i}", (0, -0.32, 0))
        core.add(model_comp(proc_model("Cylinder", (0.16, 0.55), cell_mat, tess=16), False))
        cap_top.add(model_comp(proc_model("Cylinder", (0.2, 0.1), cell_cap, tess=16), False))
        cap_bottom.add(model_comp(proc_model("Cylinder", (0.2, 0.1), cell_cap, tess=16), False))
        for c in (core, cap_top, cap_bottom):
            cell.child(c)
        cell.emit()
        for c in (core, cap_top, cap_bottom):
            c.emit(root=False)

    # ---- palm trees ringing the yard
    def palm(name, x, z, height, lean_deg, lean_dir):
        segs = 3
        top = [x, 0.0, z]
        tilt = 0.0
        for s in range(segs):
            seg_h = height / segs
            r = 0.26 - s * 0.022
            tilt += lean_deg / segs
            rot = q_mul(q_rot(yaw=lean_dir), q_rot(pitch=tilt))
            d = q_apply(rot, (0, seg_h, 0))
            center = [top[0] + d[0] / 2, top[1] + d[1] / 2, top[2] + d[2] / 2]
            shape(f"{name}_trunk{s}", "Cylinder", (r, seg_h * 1.04), bark, center, rot, uv=(1, 1.5), tess=10,
                  collide=(s == 0))
            top = [top[0] + d[0], top[1] + d[1], top[2] + d[2]]
        n = 6
        for k in range(n):
            yaw = k * 360 / n + random.uniform(-12, 12)
            inner_p = random.uniform(-8, 6)
            inner = q_rot(yaw=yaw, pitch=inner_p)
            shape(f"{name}_frondA{k}", "Plane", (1.3, 2.4, True), frond, top, inner, collide=False, offset=(0, 0, 1.2), shadows=False)
            end = q_apply(inner, (0, 0, 2.3))
            outer = q_rot(yaw=yaw, pitch=inner_p + random.uniform(35, 55))
            shape(f"{name}_frondB{k}", "Plane", (1.2, 2.2, True), frond, [top[0] + end[0], top[1] + end[1], top[2] + end[2]], outer,
                  collide=False, offset=(0, 0, 1.1), shadows=False)

    spots = []
    for i in range(12):
        for _ in range(40):
            a = random.uniform(0, 2 * math.pi)
            r = random.uniform(30, 70)
            x, z = math.cos(a) * r, math.sin(a) * r - 10
            if all((x - sx) ** 2 + (z - sz) ** 2 > 36 for sx, sz in spots):
                spots.append((x, z))
                break
    for i, (x, z) in enumerate(spots):
        palm(f"Palm{i}", x, z, random.uniform(7, 12), random.uniform(6, 22), random.uniform(0, 360))
    for i, (x, z) in enumerate([(-12, 14), (14, 16), (-28, -2), (28, -6), (-2, 24)]):
        palm(f"YardPalm{i}", x, z, random.uniform(6, 9), random.uniform(5, 15), random.uniform(0, 360))

    # ---- player: physics character + mannequin + third-person camera
    player = Ent("Player", (0, 0.1, 8))
    model = Ent("PlayerModel", (0, 0, 0))
    cam_target = Ent("CameraTarget", (0, 1.65, 0), q_rot(yaw=0, pitch=-12))
    camera = Ent("Camera", (0, 0, 5.0))
    player.child(model)
    player.child(cam_target)
    cam_target.child(camera)
    player.add("""                    @KEY@: !CharacterComponent
                        Id: @ID@
                        CollisionGroup: KinematicFilter
                        CanSleep: false
                        Restitution: 0.0
                        Friction: 0.5
                        RollingFriction: 0.0
                        CcdMotionThreshold: 0.0
                        CcdSweptSphereRadius: 0.0
                        FallSpeed: 10.0
                        MaxSlope: {Radians: 0.7853982}
                        JumpSpeed: 9.0
                        Gravity: {X: 0.0, Y: -25.0, Z: 0.0}
                        ColliderShapes:
                            56a5239435c0e54188f913fd5d2f5dee: !CapsuleColliderShapeDesc
                                Length: 1.2
                                Radius: 0.4
                                LocalOffset: {X: 0.0, Y: 0.95, Z: 0.0}
                                LocalRotation: {X: 0.0, Y: 0.0, Z: 0.0, W: 1.0}
""")
    player.add("""                    @KEY@: !ArenaRush.Player.PlayerController,ArenaRush.Game
                        Id: @ID@
                        MaxRunSpeed: 6.5
                        JumpReactionThreshold: 0.15
""")
    player.add(f"""                    @KEY@: !ArenaRush.Player.PlayerInput,ArenaRush.Game
                        Id: @ID@
                        MouseSensitivity: 1.0
                        DeadZone: 0.15
                        Camera: ref!! {gid('component', camera.id, 0)}
                        KeysLeft:
                            {key('kl1')}: A
                            {key('kl2')}: Left
                        KeysRight:
                            {key('kr1')}: D
                            {key('kr2')}: Right
                        KeysUp:
                            {key('ku1')}: W
                            {key('ku2')}: Up
                        KeysDown:
                            {key('kd1')}: S
                            {key('kd2')}: Down
                        KeysJump:
                            {key('kj1')}: Space
""")
    player.add("""                    @KEY@: !ArenaRush.Player.PushBodies,ArenaRush.Game
                        Id: @ID@
                        PushStrength: 6.0
""")
    anim_comp_id = gid("component", model.id, 2)
    model.add(model_comp(MANNEQUIN_MODEL))
    anim_lines = "".join(f"                        {k}: {v}\n" for k, v in ANIMS.items())
    model.add(f"""                    @KEY@: !ArenaRush.Player.AnimationController,ArenaRush.Game
                        Id: @ID@
                        AnimationComponent: ref!! {anim_comp_id}
{anim_lines}                        WalkThreshold: 0.25
                        TimeFactor: 1.0
""")
    model.add("""                    @KEY@: !AnimationComponent
                        Id: @ID@
                        Animations: {}
""")
    camera.add(f"""                    @KEY@: !CameraComponent
                        Id: @ID@
                        Name: null
                        Projection: Perspective
                        VerticalFieldOfView: 55.0
                        FarClipPlane: 400.0
                        Slot: {CAMERA_SLOT_ID}
""")
    camera.add("""                    @KEY@: !ArenaRush.Camera.ThirdPersonCamera,ArenaRush.Game
                        Id: @ID@
                        DefaultDistance: 5.0
                        MinimumDistance: 0.4
                        ConeRadius: 1.25
                        InvertX: false
                        MinVerticalAngle: -20.0
                        MaxVerticalAngle: 70.0
                        InvertY: false
                        RotationSpeed: 360.0
                        VerticalSpeed: 45.0
""")
    player.emit()
    model.emit(root=False)
    cam_target.emit(root=False)
    camera.emit(root=False)

    Ent("TouchControls").add("""                    @KEY@: !ArenaRush.TouchHud,ArenaRush.Game
                        Id: @ID@
                        Font: 3c1d7a52-9b0e-4f6a-8d21-6e5f0a9c4b11:HudFont
""").emit()
    Ent("GameManager").add("""                    @KEY@: !ArenaRush.CellHunt,ArenaRush.Game
                        Id: @ID@
                        PickupRadius: 1.4
""").emit()

    # ---- scene file
    roots = "".join(f"        - ref!! {r}\n" for r in ROOTS)
    parts = "".join(y for _, y in ENTITIES)
    write(os.path.join(ASSETS, "MainScene.sdscene"), f"""!SceneAsset
Id: {SCENE_ID}
SerializedVersion: {{Stride: 3.1.0.1}}
Tags: []
ChildrenIds: []
Offset: {{X: 0.0, Y: 0.0, Z: 0.0}}
Hierarchy:
    RootParts:
{roots}    Parts:
{parts}""")
    print(f"Level written: {len(ENTITIES)} entities, {len(PROC_CACHE)} shapes, "
          f"{sum(1 for m in MODEL_CACHE.values() if m)} Poly Haven models, {len(TEX_CACHE)} textures")


if __name__ == "__main__":
    build()
