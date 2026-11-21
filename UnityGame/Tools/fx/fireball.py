"""Renders the explosion flipbooks of the game with Blender (Cycles volumes), headless:
   python3 fireball.py out_dir
Fireball: 16 frames of a turbulent fire ball that swells, cools and turns to dark smoke.
Smoke: 16 frames of a billowing grey puff (lit from above). Each frame on a transparent background."""
import sys, os, math
import bpy

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)


def scene(res):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.render.engine = 'CYCLES'
    s.cycles.samples = 48
    s.cycles.use_denoising = True
    try:
        s.cycles.denoiser = 'OPENIMAGEDENOISE'
    except TypeError:
        pass
    s.cycles.volume_step_rate = 0.6
    s.cycles.volume_bounces = 2
    s.render.resolution_x = s.render.resolution_y = res
    s.render.film_transparent = True
    s.view_settings.view_transform = 'Standard'
    s.render.image_settings.file_format = 'PNG'
    s.render.image_settings.color_mode = 'RGBA'
    w = bpy.data.worlds.new('w'); s.world = w
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[1].default_value = 0.25
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.3
    cam.location = (0, -6, 0); cam.rotation_euler = (math.pi / 2, 0, 0)
    s.collection.objects.link(cam); s.camera = cam
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 3.5; sun.rotation_euler = (math.radians(35), math.radians(-25), 0)
    s.collection.objects.link(sun)
    bpy.ops.mesh.primitive_cube_add(size=2.2)
    dom = bpy.context.active_object
    return s, dom


def node(nt, kind, **inputs):
    n = nt.nodes.new(kind)
    for k, v in inputs.items():
        n.inputs[k].default_value = v
    return n


def volume_material(fire):
    m = bpy.data.materials.new('vol')
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    vol = nt.nodes.new('ShaderNodeVolumePrincipled')
    coord = nt.nodes.new('ShaderNodeTexCoord')
    # turbulence: two noises (big billows, small curls) displace the radius of the ball
    n1 = nt.nodes.new('ShaderNodeTexNoise'); n1.noise_dimensions = '4D'
    n1.inputs['Scale'].default_value = 1.6; n1.inputs['Detail'].default_value = 6; n1.inputs['Roughness'].default_value = 0.6
    n2 = nt.nodes.new('ShaderNodeTexNoise'); n2.noise_dimensions = '4D'
    n2.inputs['Scale'].default_value = 4.5; n2.inputs['Detail'].default_value = 4; n2.inputs['Roughness'].default_value = 0.55
    warp = nt.nodes.new('ShaderNodeTexNoise'); warp.noise_dimensions = '4D'; warp.name = 'warp'
    warp.inputs['Scale'].default_value = 1.1; warp.inputs['Detail'].default_value = 2
    nt.links.new(coord.outputs['Object'], warp.inputs['Vector'])
    wsub = nt.nodes.new('ShaderNodeVectorMath'); wsub.operation = 'SUBTRACT'
    nt.links.new(warp.outputs['Color'], wsub.inputs[0]); wsub.inputs[1].default_value = (0.5, 0.5, 0.5)
    wsc = nt.nodes.new('ShaderNodeVectorMath'); wsc.operation = 'SCALE'
    nt.links.new(wsub.outputs[0], wsc.inputs[0]); wsc.inputs['Scale'].default_value = 0.7
    wadd = nt.nodes.new('ShaderNodeVectorMath'); wadd.operation = 'ADD'
    nt.links.new(coord.outputs['Object'], wadd.inputs[0]); nt.links.new(wsc.outputs[0], wadd.inputs[1])
    nt.links.new(wadd.outputs[0], n1.inputs['Vector']); nt.links.new(wadd.outputs[0], n2.inputs['Vector'])
    length = nt.nodes.new('ShaderNodeVectorMath'); length.operation = 'LENGTH'
    nt.links.new(coord.outputs['Object'], length.inputs[0])
    # squash a little: smoke rises, the ball is a bit taller than wide
    mix = nt.nodes.new('ShaderNodeMath'); mix.operation = 'MULTIPLY_ADD'
    nt.links.new(n1.outputs['Fac'], mix.inputs[0]); mix.inputs[1].default_value = 1.05; mix.inputs[2].default_value = -0.52
    mix2 = nt.nodes.new('ShaderNodeMath'); mix2.operation = 'MULTIPLY_ADD'
    nt.links.new(n2.outputs['Fac'], mix2.inputs[0]); mix2.inputs[1].default_value = 0.34; mix2.inputs[2].default_value = -0.17
    disp = nt.nodes.new('ShaderNodeMath'); disp.operation = 'ADD'
    nt.links.new(mix.outputs[0], disp.inputs[0]); nt.links.new(mix2.outputs[0], disp.inputs[1])
    r = nt.nodes.new('ShaderNodeMath'); r.operation = 'SUBTRACT'; r.name = 'radius'
    r.inputs[0].default_value = 0.6
    nt.links.new(disp.outputs[0], r.inputs[1])     # radius - disp
    edge = nt.nodes.new('ShaderNodeMath'); edge.operation = 'SUBTRACT'
    nt.links.new(r.outputs[0], edge.inputs[0]); nt.links.new(length.outputs['Value'], edge.inputs[1])   # >0 inside
    dens = nt.nodes.new('ShaderNodeMath'); dens.operation = 'MULTIPLY'; dens.use_clamp = True
    nt.links.new(edge.outputs[0], dens.inputs[0]); dens.inputs[1].default_value = 9.0
    dscale = nt.nodes.new('ShaderNodeMath'); dscale.operation = 'MULTIPLY'; dscale.name = 'density'
    nt.links.new(dens.outputs[0], dscale.inputs[0]); dscale.inputs[1].default_value = 18.0
    nt.links.new(dscale.outputs[0], vol.inputs['Density'])
    vol.inputs['Color'].default_value = (0.11, 0.1, 0.095, 1) if fire else (0.42, 0.41, 0.4, 1)
    vol.inputs['Absorption Color'].default_value = (0.05, 0.05, 0.05, 1)
    if fire:
        # heat: hottest in the middle; the colour from white-yellow through orange to deep red
        heat = nt.nodes.new('ShaderNodeMath'); heat.operation = 'MULTIPLY'; heat.use_clamp = True
        nt.links.new(edge.outputs[0], heat.inputs[0]); heat.inputs[1].default_value = 2.2; heat.name = 'heatgain'
        ramp = nt.nodes.new('ShaderNodeValToRGB')
        cr = ramp.color_ramp
        cr.elements[0].position = 0.0; cr.elements[0].color = (0.25, 0.02, 0.0, 1)
        cr.elements[1].position = 1.0; cr.elements[1].color = (1.0, 0.92, 0.6, 1)
        e = cr.elements.new(0.35); e.color = (0.9, 0.22, 0.02, 1)
        e = cr.elements.new(0.7); e.color = (1.0, 0.6, 0.12, 1)
        nt.links.new(heat.outputs[0], ramp.inputs['Fac'])
        nt.links.new(ramp.outputs['Color'], vol.inputs['Emission Color'])
        est = nt.nodes.new('ShaderNodeMath'); est.operation = 'MULTIPLY'; est.name = 'emission'
        nt.links.new(heat.outputs[0], est.inputs[0]); est.inputs[1].default_value = 12.0
        nt.links.new(est.outputs[0], vol.inputs['Emission Strength'])
    nt.links.new(vol.outputs['Volume'], out.inputs['Volume'])
    return m, n1, n2


def render_sheet(name, fire, res, frames=16):
    s, dom = scene(res)
    m, n1, n2 = volume_material(fire)
    dom.data.materials.append(m)
    nt = m.node_tree
    paths = []
    for f in range(frames):
        t = f / (frames - 1)
        ease = 1 - (1 - t) ** 2.2
        n1.inputs['W'].default_value = t * 1.6
        n2.inputs['W'].default_value = t * 2.4
        nt.nodes['warp'].inputs['W'].default_value = t * 0.9
        if fire:
            boom = 1 - (1 - min(1.0, t / 0.35)) ** 3          # the first third: the fast swell
            nt.nodes['radius'].inputs[0].default_value = 0.3 + 0.42 * boom + 0.22 * t
            # thin bright fire first, thick smoke after; the last frames thin out to nothing
            fade = min(1.0, (1 - t) / 0.3)
            nt.nodes['density'].inputs[1].default_value = (5 + 22 * t) * fade
            # the fire dies quickly: by 55% of the life only embers in the folds, then smoke
            nt.nodes['emission'].inputs[1].default_value = 75.0 * max(0.0, 1 - t / 0.55) ** 1.4
            nt.nodes['heatgain'].inputs[1].default_value = 2.4 * (1 - 0.7 * t)
        else:
            nt.nodes['radius'].inputs[0].default_value = 0.42 + 0.45 * ease
            nt.nodes['density'].inputs[1].default_value = 7 * min(1.0, (1 - t) / 0.45)
        s.render.filepath = os.path.join(OUT, f'{name}_{f:02d}.png')
        bpy.ops.render.render(write_still=True)
        paths.append(s.render.filepath)
    return paths


if __name__ == '__main__':
    which = sys.argv[2] if len(sys.argv) > 2 else 'both'
    if which in ('fire', 'both'):
        render_sheet('fire', True, 256)
    if which in ('smoke', 'both'):
        render_sheet('smoke', False, 128)
    print('done')
