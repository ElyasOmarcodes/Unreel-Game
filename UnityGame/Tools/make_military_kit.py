#!/usr/bin/env python3
"""
Turns "Military Character Kit 1.1" by britdawgmasterfunk (Sketchfab, CC-BY-4.0, credited in the
game) into light OBJ models for phones: the kit lies laid out as a flat display, so each item is cut
out by its place on the display, merged per material, reduced to a few thousand triangles, moved to
its own origin and scaled to meters (front +Z, up +Y).

Run:  python3 UnityGame/Tools/make_military_kit.py <scene.gltf>   (needs trimesh, fast-simplification)
"""
import json
import os
import sys
import numpy as np
import trimesh
import fast_simplification

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Models", "MilitaryKit")   # outside Assets: read by Wardrobe.cs, not imported by Unity
SCALE = 0.2   # kit units to meters

# name: (x range, y range on the display), triangle budget, nodes to leave out
ITEMS = {
    "Helmet": (((-8.3, -6.8), (5.9, 7.6)), 2600, {"Object_90", "Object_92", "Object_104", "Object_106", "Object_108", "Object_110"}),
    "HelmetNVG": (((-8.3, -6.8), (5.9, 7.6)), 3600, set()),
    "HelmetHeadset": (((-10.0, -8.7), (5.9, 7.6)), 3400, set()),
    "PlateCarrier": (((-8.6, -6.3), (3.3, 5.75)), 3800, set()),
    "Belt": (((-10.9, -8.85), (3.35, 4.1)), 1200, set()),
    "KneePad": (((-4.5, -3.55), (1.8, 2.85)), 500, set()),
    "Boot": (((-9.55, -8.75), (1.85, 2.95)), 1400, set()),
    "BootBrown": (((-10.4, -9.6), (1.85, 2.95)), 1400, set()),
    "PatrolCap": (((-11.2, -10.05), (6.55, 7.5)), 1200, set()),
    "Backpack": (((-3.3, -1.9), (2.2, 4.1)), 1800, set()),
    "ChestRig": (((-5.2, -3.8), (3.2, 5.6)), 3000, set()),
}


def main():
    gltf = json.load(open(sys.argv[1]))
    colors = {}
    for m in gltf.get("materials", []):
        pbr = m.get("pbrMetallicRoughness", {})
        colors[m.get("name", "")] = (pbr.get("baseColorFactor", [0.5, 0.5, 0.5, 1])[:3], pbr.get("roughnessFactor", 0.8))
    scene = trimesh.load(sys.argv[1], force="scene")
    parts = {}
    for node in scene.graph.nodes_geometry:
        transform, name = scene.graph[node]
        mesh = scene.geometry[name].copy()
        mesh.apply_transform(transform)
        mesh.merge_vertices()   # split UV seams stop the simplifier at a few thousand triangles
        parts[node] = mesh
    os.makedirs(OUT, exist_ok=True)
    for item, (((x0, x1), (y0, y1)), budget, leave) in ITEMS.items():
        chosen = {n: m for n, m in parts.items() if n not in leave and x0 <= m.bounds.mean(0)[0] <= x1 and y0 <= m.bounds.mean(0)[1] <= y1}
        total = sum(len(m.faces) for m in chosen.values())
        lo = np.min([m.bounds[0] for m in chosen.values()], axis=0)
        hi = np.max([m.bounds[1] for m in chosen.values()], axis=0)
        origin = np.array([(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2])
        by_material = {}
        for name, mesh in chosen.items():
            material = getattr(mesh.visual, "material", None)
            key = getattr(material, "name", None) or "default"
            faces = len(mesh.faces)
            want = max(12, int(budget * faces / total))
            v, f = np.asarray(mesh.vertices, dtype=np.float32), np.asarray(mesh.faces)
            if faces > want * 1.2 and faces > 60:
                v, f = fast_simplification.simplify(v, f, target_reduction=1 - want / faces)
            by_material.setdefault(key, []).append(((v - origin) * SCALE, f))
        write_obj(item, by_material, colors)
        print(f"{item}: {total} -> {sum(len(f) for parts_ in by_material.values() for _, f in parts_)} triangles, size {np.round((hi - lo) * SCALE, 3)} m")


def write_obj(item, by_material, colors):
    with open(os.path.join(OUT, item + ".mtl"), "w") as mtl:
        for key in by_material:
            color, rough = colors.get(key, ([0.5, 0.5, 0.5], 0.8))
            mtl.write(f"newmtl {item}_{key}\nKd {color[0]:.4f} {color[1]:.4f} {color[2]:.4f}\nNs {max(2, (1 - rough) * 200):.1f}\n\n")
    with open(os.path.join(OUT, item + ".obj"), "w") as obj:
        obj.write(f"# {item} from Military Character Kit 1.1 by britdawgmasterfunk, CC-BY-4.0\nmtllib {item}.mtl\n")
        base = 1
        for key, chunks in by_material.items():
            obj.write(f"o {item}_{key}\nusemtl {item}_{key}\n")
            for v, f in chunks:
                for p in v:
                    # glTF is right-handed: -x and reversed corners give Unity's left-handed space (read as-is by Outfit.LoadKit)
                    obj.write(f"v {-p[0]:.5f} {p[1]:.5f} {p[2]:.5f}\n")
                for a, b, c in f + base:
                    obj.write(f"f {a} {c} {b}\n")
                base += len(v)


if __name__ == "__main__":
    main()
