# A Canary / date palm built leaf by leaf (trunk with leaf scars, ~50 arching fronds of narrow leaflets,
# a skirt of dry fronds), or a tuft of dry grass; saved as a .blend for impostor.py.
# python3 palmgen.py palm|grass seed out.blend bark.jpg
import sys, math, random
sys.path.insert(0, '/tmp/claude-0/pyenv')
import bpy, bmesh, mathutils
from mathutils import Vector, Matrix
kind, seed, out, bark = sys.argv[1], int(sys.argv[2]), sys.argv[3], sys.argv[4]
rnd = random.Random(seed)
bpy.ops.wm.read_factory_settings(use_empty=True)

def material(name, use_color=True, rough=0.6, trans=0.25, tex=None, tint=(1, 1, 1)):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    if tex:
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(tex)
        mul = nt.nodes.new("ShaderNodeMixRGB"); mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 1
        mul.inputs[2].default_value = (*tint, 1)
        nt.links.new(t.outputs[0], mul.inputs[1]); nt.links.new(mul.outputs[0], b.inputs["Base Color"])
        uv = nt.nodes.new("ShaderNodeTexCoord"); mp = nt.nodes.new("ShaderNodeMapping")
        mp.inputs["Scale"].default_value = (2.0, 6.0, 1)
        nt.links.new(uv.outputs["UV"], mp.inputs[0]); nt.links.new(mp.outputs[0], t.inputs[0])
    elif use_color:
        a = nt.nodes.new("ShaderNodeVertexColor"); a.layer_name = "Col"
        nt.links.new(a.outputs[0], b.inputs["Base Color"])
    if trans > 0:
        # thin leaves let light through: a little translucency on the shaded side
        tr = nt.nodes.new("ShaderNodeBsdfTranslucent"); mix = nt.nodes.new("ShaderNodeMixShader")
        mix.inputs[0].default_value = trans
        if use_color and not tex:
            nt.links.new(nt.nodes["Color Attribute"].outputs[0] if "Color Attribute" in nt.nodes else a.outputs[0], tr.inputs[0])
        outn = nt.nodes["Material Output"]
        nt.links.new(b.outputs[0], mix.inputs[1]); nt.links.new(tr.outputs[0], mix.inputs[2]); nt.links.new(mix.outputs[0], outn.inputs[0])
    return m

def new_obj(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    me.materials.append(mat)
    for p in me.polygons: p.use_smooth = True
    return o

def leaf_quad(bm, col, base, tip, width, up, color, bend=0.0):
    # a narrow leaflet: base -> middle -> tip, folded a little along its length (V section)
    d = tip - base; L = d.length
    side = d.cross(up).normalized() * width / 2
    mid = base + d * 0.5 + up * (bend * L)
    vs = [bm.verts.new(base + side * 0.4), bm.verts.new(base - side * 0.4), bm.verts.new(mid + side), bm.verts.new(mid - side), bm.verts.new(tip)]
    fs = [bm.faces.new((vs[0], vs[2], vs[3], vs[1])), bm.faces.new((vs[2], vs[4], vs[3]))]
    for f in fs:
        for l in f.loops: l[col] = (*color, 1)

if kind == "palm":
    H = rnd.uniform(6.5, 9.0)
    lean = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized() * rnd.uniform(0.2, 0.7)
    def trunk_at(t):
        # a slight curve: the lean grows with height
        return Vector((0, 0, t * H)) + lean * (t * t)
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UV")
    rings, seg = 48, 18
    verts = []
    for i in range(rings + 1):
        t = i / rings; c = trunk_at(t)
        r0 = 0.42 - 0.1 * t + (0.12 if t < 0.04 else 0)   # a wider foot
        row = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            # leaf scars: diamond knobs in rows
            knob = 0.035 * max(0, math.sin(a * 6 + i * 1.7)) * max(0, math.sin(t * H * 9))
            r = r0 + knob
            row.append(bm.verts.new(c + Vector((math.cos(a) * r, math.sin(a) * r, 0))))
        verts.append(row)
    for i in range(rings):
        for k in range(seg):
            f = bm.faces.new((verts[i][k], verts[i][(k + 1) % seg], verts[i + 1][(k + 1) % seg], verts[i + 1][k]))
            for l, (u, v) in zip(f.loops, ((k, i), (k + 1, i), (k + 1, i + 1), (k, i + 1))):
                l[uvl].uv = (u / seg, v / rings)
    trunk = new_obj("palm_trunk", bm, material("trunk", use_color=False, rough=0.9, trans=0, tex=bark, tint=(0.95, 0.85, 0.72)))
    top = trunk_at(1.0)
    bm = bmesh.new(); col = bm.loops.layers.color.new("Col")
    n = 54
    for j in range(n):
        az = j * 2.39996 + rnd.uniform(-0.2, 0.2)          # golden angle around the crown
        age = j / n                                       # 0 the young upright ones, 1 the old low ones
        elev = math.radians(70 - 110 * age + rnd.uniform(-8, 8))
        L = rnd.uniform(3.4, 4.6) * (0.75 if age < 0.15 else 1)
        dirh = Vector((math.cos(az), math.sin(az), 0))
        green = Vector((0.11, 0.2, 0.05)) * rnd.uniform(0.85, 1.25)
        dry = Vector((0.45, 0.36, 0.18))
        dead = age > 0.93
        base_col = dry * rnd.uniform(0.7, 1.0) if dead else green.lerp(Vector((0.3, 0.32, 0.1)), max(0, age - 0.6) * 1.2)
        steps = 24
        pts = [top + Vector((0, 0, 0.15))]
        bendk = 1.2 if not dead else 2.2
        for q in range(steps):
            e = elev - bendk * (q / steps) ** 2      # the frond arches down along its length
            pts.append(pts[-1] + (dirh * math.cos(e) + Vector((0, 0, math.sin(e)))) * (L / steps))
        for s in range(1, steps):
            t = s / steps
            if t < 0.18: continue
            p = pts[s]; fwd = (pts[s + 1] - pts[s - 1]).normalized()
            sidev = fwd.cross(Vector((0, 0, 1))).normalized()
            upv = sidev.cross(fwd).normalized()
            ll = 0.55 * math.sin(math.pi * min(1, (t - 0.1) / 0.95)) + 0.08
            for k in range(3):
                pp = p + fwd * (k / 3) * (L / steps)
                for sgn in (-1, 1):
                    leaf_dir = (fwd * 0.75 + sidev * sgn * 1.0 + upv * (0.35 - 0.6 * t)).normalized()
                    if dead: leaf_dir = (fwd * 0.9 + sidev * sgn * 0.3 - upv * 0.8).normalized()
                    c = base_col * rnd.uniform(0.85, 1.15)
                    leaf_quad(bm, col, pp, pp + leaf_dir * ll * rnd.uniform(0.85, 1.1), 0.045, upv, tuple(c), bend=0.05)
        # the rachis itself
        for s in range(steps):
            leaf_quad(bm, col, pts[s], pts[s + 1], 0.06 * (1 - s / steps) + 0.015, Vector((0, 0, 1)), tuple(base_col * 0.8))
    new_obj("palm_crown", bm, material("leaf", rough=0.55, trans=0.3))
else:
    # a tuft of dry grass: a few hundred thin curved blades, straw to grey green
    bm = bmesh.new(); col = bm.loops.layers.color.new("Col")
    for b in range(260):
        a = rnd.uniform(0, 2 * math.pi); r = abs(rnd.gauss(0, 0.12))
        base = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        h = rnd.uniform(0.25, 0.75)
        out_dir = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.15, 0.7)
        straw = Vector((0.62, 0.53, 0.32)); grey = Vector((0.36, 0.38, 0.22))
        c = straw.lerp(grey, rnd.random() * 0.7) * rnd.uniform(0.75, 1.1)
        prev = base
        segs = 4
        for s in range(1, segs + 1):
            t = s / segs
            p = base + Vector((0, 0, h * t)) + out_dir * h * t * t
            w = 0.012 * (1 - t) + 0.002
            leaf_quad(bm, col, prev, p, w * 2, Vector((0, 0, 1)), tuple(c * (0.7 + 0.3 * t)))
            prev = p
        # seed heads on some
        if rnd.random() < 0.25:
            leaf_quad(bm, col, prev, prev + (prev - base).normalized() * 0.08, 0.02, Vector((0, 0, 1)), tuple(Vector((0.7, 0.62, 0.42))))
    new_obj("grass_tuft", bm, material("grass", rough=0.8, trans=0.35))
bpy.ops.wm.save_as_mainfile(filepath=out)
print("saved", out)
