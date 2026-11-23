"""
A look at TEAM 1 as built by build.py: four renders (front, three-quarter, side, back) side by side.
Run (the bpy module):  python3 preview.py <Team1.blend or Team1_parts.blend> <out.png> [--atlas] [--head]
  --atlas  the baked atlas (Team1.jpg, Team1_n.png next to the blend) on the joined mesh, as the game shows it
  --head   close on the head and the shoulders
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

argv = [a for a in sys.argv[1:] if not a.endswith('.py')]
BLEND, OUT = argv[0], argv[1]
ATLAS = '--atlas' in argv
HEAD = '--head' in argv
bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
base = os.path.dirname(os.path.abspath(BLEND))

if ATLAS and 'Team1' in bpy.data.objects:
    team = bpy.data.objects['Team1']
    m = bpy.data.materials.new('AtlasLook')
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes['Principled BSDF']
    uv = nt.nodes.new('ShaderNodeUVMap')
    uv.uv_map = 'Atlas'
    col = nt.nodes.new('ShaderNodeTexImage')
    col.image = bpy.data.images.load(os.path.join(base, 'Team1.jpg'))
    nt.links.new(uv.outputs['UV'], col.inputs['Vector'])
    nt.links.new(col.outputs['Color'], bsdf.inputs['Base Color'])
    nimg = bpy.data.images.load(os.path.join(base, 'Team1_n.png'))
    nimg.colorspace_settings.name = 'Non-Color'
    nrm = nt.nodes.new('ShaderNodeTexImage')
    nrm.image = nimg
    nt.links.new(uv.outputs['UV'], nrm.inputs['Vector'])
    nm = nt.nodes.new('ShaderNodeNormalMap')
    nm.uv_map = 'Atlas'
    nt.links.new(nrm.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
    bsdf.inputs['Roughness'].default_value = 0.8
    team.data.materials.clear()
    team.data.materials.append(m)

# the body: plain skin, the rest of the pack's meshes hidden
for o in bpy.data.objects:
    if o.type == 'MESH' and o.name.startswith('SuperHero') and o.name != 'SuperHero_Male':
        o.hide_render = True
body = bpy.data.objects.get('SuperHero_Male')
if body is not None:
    body.hide_render = False
    skin = bpy.data.materials.new('Skin')
    skin.use_nodes = True
    skin.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.62, 0.45, 0.36, 1)
    skin.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.6
    body.data.materials.clear()
    body.data.materials.append(skin)

scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 560, 1000
scene.view_settings.view_transform = 'Standard'
world = scene.world or bpy.data.worlds.new('World')
scene.world = world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.32, 0.34, 0.37, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.6


def light(name, loc, power):
    ld = bpy.data.lights.new(name, 'AREA')
    ld.energy = power
    ld.size = 2.0
    lo = bpy.data.objects.new(name, ld)
    lo.location = loc
    scene.collection.objects.link(lo)
    d = Vector((0, 0, 1.0)) - Vector(loc)
    lo.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


light('Key', (2.2, 3.0, 2.6), 900)
light('Fill', (-2.6, 2.0, 1.6), 350)
light('Back', (0.0, -3.0, 2.8), 600)

cam_data = bpy.data.cameras.new('Cam')
cam_data.lens = 80
cam = bpy.data.objects.new('Cam', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
target = Vector((0, 0, 1.6 if HEAD else 0.92))
dist = 1.6 if HEAD else 5.2

tiles = []
for i, ang in enumerate((90, 55, 0, -90)):     # front (the face looks along +y), three-quarter, side, back
    a = math.radians(ang)
    cam.location = target + Vector((math.cos(a) * dist, math.sin(a) * dist, 0.05 if HEAD else 0.25))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    path = OUT + f'.{i}.png'
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    w, h = img.size
    tiles.append(np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4))
    os.remove(path)
sheet = np.concatenate(tiles, axis=1)
out = bpy.data.images.new('Sheet', sheet.shape[1], sheet.shape[0])
out.pixels = sheet.ravel()
out.filepath_raw = OUT
out.file_format = 'PNG'
out.save()
print('preview', OUT)
