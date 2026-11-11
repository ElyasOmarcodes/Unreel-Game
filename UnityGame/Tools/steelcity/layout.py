"""The new yards of Steel City, laid out as numbers and written to Models/CityLord/extension.json:
the game's map builder (CityLordEast.cs) puts up exactly what is listed there, and render.py shows the
same list in Blender, so every joint can be looked at before the game is built.

The rules of the old town are kept: its own pieces on its own 10 m grid; an outline of joined
rectangles; the one road carried on with bends; yards on different levels (a raised yard stands
1.53 m over the road behind the town's retaining walls, with its concrete stairs); a road ramp of 2 m
between the low west level and the high east level; pipes that end in something.

    python3 layout.py            writes extension.json
"""
import json, math, os
import numpy as np
import town, height

E, W = -10.9, -12.9            # street levels of the old east and west ends (scene y)
RAISE = 1.53                   # a raised yard over its road, as the old inner yard
L = town.LIFT
out = {'remove': [], 'pieces': [], 'boxes': [], 'ladders': [], 'ammo': [], 'boards': [], 'spawns': [], 'yards': []}


def r3(v):
    return [round(float(x), 3) for x in v]


def put(name, x, z, yaw, ground, top=False, scale=None, collider=True):
    """A piece with the middle of its footprint at (x, z), standing on 'ground' (or its top on it)."""
    mn, mx = town.kit_bounds(name)
    c = town.yaw_matrix(yaw) @ np.array([(mn[0] + mx[0]) / 2, 0, (mn[2] + mx[2]) / 2])
    p = {'name': name, 'pos': r3([x - c[0], ground - (mx[1] if top else 0) + L, z - c[2]]), 'yaw': yaw}
    if scale: p['scale'] = scale
    if not collider: p['collider'] = False
    out['pieces'].append(p)
    return p


def at(name, pos, yaw):
    """A piece by its own pivot (scene y of its foot)."""
    out['pieces'].append({'name': name, 'pos': r3([pos[0], pos[1] + L, pos[2]]), 'yaw': round(yaw, 3)})


def box(name, c, size, mat='concrete', yaw=0.0, pitch=0.0, collider=True):
    out['boxes'].append({'name': name, 'c': r3([c[0], c[1] + L, c[2]]), 'size': r3(size), 'yaw': round(yaw, 3), 'pitch': round(pitch, 3),
                         'mat': mat, 'collider': collider})


def strut(a, b, t, mat='wood', name='Strut', collider=False):
    a = np.array(a, float); b = np.array(b, float); d = b - a; n = np.linalg.norm(d)
    box(name, (a + b) / 2, [t, t, n], mat, math.degrees(math.atan2(d[0], d[2])), -math.degrees(math.asin(d[1] / n)), collider)


def plank(a, b, width, name='Plank'):
    a = np.array(a, float); b = np.array(b, float); d = b - a; n = np.linalg.norm(d)
    box(name, (a + b) / 2 - np.array([0, 0.06, 0]), [width, 0.12, n], 'wood', math.degrees(math.atan2(d[0], d[2])), -math.degrees(math.asin(d[1] / n)))


def ladder(foot, outward, h):
    out['ladders'].append({'foot': r3([foot[0], foot[1] + L, foot[2]]), 'out': r3(outward), 'height': round(h, 3)})


def floor(level, *cells):
    for x0, z0 in cells:
        put('Road_set_v1_s_floor', x0 + 5, z0 + 5, 0, level)


def base(x0, z0, x1, z1, level, name='YardBase'):
    """The ground under a yard: down to under the plain."""
    box(name, [(x0 + x1) / 2, (level - 0.04 + -14.6) / 2, (z0 + z1) / 2], [x1 - x0, level - 0.04 + 14.6, z1 - z0], 'asphalt')


def straight(cx, cz, along_x, twenty, level):
    put('Road_set_v1_s_tile_2' if twenty else 'Road_set_v1_s_tile_1', cx, cz, 0 if along_x else 90, level, top=True)


def bend(cx, cz, yaw, level):
    """0 joins north and east, 90 east and south, 180 south and west, -90 west and north."""
    put('Road_set_v1_s_bend_1', cx, cz, yaw, level, top=True)
    put('Road_set_v1_s_bend_1f', cx, cz, yaw, level, top=True)


def fence(ax, az, bx, bz, level):
    """Wall panels between columns along a side of the grid, 10 m each."""
    along_x = abs(bx - ax) > abs(bz - az)
    n = max(1, round((abs(bx - ax) if along_x else abs(bz - az)) / 10))
    for i in range(n + 1):
        t = i / n
        put('Concrete_fence_v1_column', ax + (bx - ax) * t, az + (bz - az) * t, 0, level - 0.05)
        if i == n: break
        t = (i + 0.5) / n
        put('Concrete_fence_v1_wall_set_v1', ax + (bx - ax) * t, az + (bz - az) * t, 90 if along_x else 0, level - 0.05)


def retain(x, z, along_x, low):
    """A 10 m retaining wall (its middle at x, z) holding a raised yard over the level 'low'."""
    put('UNIConcrete_wall_v1_W_tile_1', x, z, 90 if along_x else 0, low)


def stack(x, z, yaw, level, two=False):
    put('Cargo_container_v1_LD2close', x, z, yaw, level)
    if two: put('Cargo_container_v1_LD2close', x, z, yaw + 5, level + 2.6)


def crates(x, z, level, k=0):
    put('Wooden_box_v1_LD2square', x, z, (x * 7 + k) % 40, level)
    put('Wooden_box_v1_LD1square', x + 1.45, z + 0.3, (z * 5 + k) % 30, level)


def tower(cx, cz, deck, size, foot_of, ladder_out, name='Tower'):
    """A wooden look-out: four legs from whatever is under them, a deck with rails open where the
    ladder comes up (ladder_out: the side the ladder leans out to), a roof of planks."""
    h = size / 2
    top = deck + 2.35
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = cx + sx * h, cz + sz * h
            f = foot_of(x, z)
            box(name + 'Leg', [x, (f - 0.1 + top) / 2, z], [0.26, top - f + 0.1, 0.26], 'wood')
    box(name + 'Deck', [cx, deck - 0.1, cz], [size + 0.7, 0.2, size + 0.7], 'wood')
    box(name + 'Roof', [cx, top + 0.1, cz], [size + 1.2, 0.14, size + 1.2], 'wood')
    for sx, sz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        side = np.array([sx, 0, sz], float)
        if abs(side[0] - ladder_out[0]) < 0.1 and abs(side[2] - ladder_out[2]) < 0.1:
            continue
        c = np.array([cx, 0, cz]) + side * (h + 0.22)
        long = [0.1, 0.1, size + 0.6] if sx else [size + 0.6, 0.1, 0.1]
        box(name + 'Rail', [c[0], deck + 1.05, c[2]], long, 'wood', collider=False)
        box(name + 'Rail', [c[0], deck + 0.55, c[2]], [0.14 if sx else size + 0.6, 1.1, size + 0.6 if sx else 0.14], 'none')
        box(name + 'Board', [c[0], deck + 0.35, c[2]], [0.06 if sx else size + 0.6, 0.6, size + 0.6 if sx else 0.06], 'wood', collider=False)
    # braces under the deck on two sides
    for s in (-1, 1):
        f = min(foot_of(cx + s * h, cz - h), foot_of(cx + s * h, cz + h))
        strut([cx + s * h, max(f + 0.3, deck - 3.2), cz - h], [cx + s * h, deck - 0.3, cz + h], 0.12, 'wood', name + 'Brace')
        f = min(foot_of(cx - h, cz + s * h), foot_of(cx + h, cz + s * h))
        strut([cx - h, deck - 0.3, cz + s * h], [cx + h, max(f + 0.3, deck - 3.2), cz + s * h], 0.12, 'wood', name + 'Brace')


def pipe_axis(name):
    """Both ends of the pipe of an old horizontal run (scene coordinates), west first."""
    for n, p, Wv, T, M in town.town_objects():
        if n == name:
            V = Wv - np.array([0, L, 0])
            t = V[V[:, 1] > V[:, 1].max() - 1.2]
            c = t.mean(0); ax = np.linalg.svd(t - c)[2][0]
            if ax[0] < 0: ax = -ax
            s = (t - c) @ ax
            return c + ax * s.min(), c + ax * s.max()
    raise KeyError(name)


# ===================================================================== what goes of the old town
out['remove'] += [
    {'name': 'Oil_tank_v2', 'x': 98, 'z': -2, 'r': 7},                       # the four small tanks in the old east gate
    {'name': 'Concrete_fence_v1_wall_set_v2', 'x': 99.28, 'z': -3.3},
    {'name': 'Road_set_v1_s_bend_1', 'x': 87, 'z': 11.41},
    {'name': 'Road_set_v1_s_tile_2', 'x': 87, 'z': 26.41},
    {'name': 'Road_set_v1_b_floor', 'x': 92, 'z': -3.69},
    {'name': 'Cargo_container_v1_LD2close', 'x': 83.71, 'z': -4.47},
    {'name': 'Concrete_fence_v1_wall_set_v1', 'x': -22.79, 'z': -53.54},
    {'name': 'Concrete_fence_v1_wall_set_v1', 'x': 20.52, 'z': -40.54},     # the old fence across the top of the new way between the halls   # the fence across the old west end of the road
]

# ===================================================================== the road on through the old east gate
bend(87, 11.41, 180, E)
bend(87, 1.41, 0, E)
straight(97, 1.41, True, False, E)
floor(E, (82, -13.59), (92, -13.59), (82, 16.41), (82, 26.41))
box('FloorJoint', [92, E - 0.05, -13.64], [20, 0.1, 0.14], 'asphalt', collider=False)

# ===================================================================== the East Yard
# outline: the court at the gate (x 102-122, seven rows), a narrower part east of the road and a short east wing
EY = [(102, z) for z in (-53.59, -43.59, -33.59, -23.59, -13.59, -3.59, 6.41)] + [(112, z) for z in (-53.59, -43.59, -33.59, -23.59, -13.59, -3.59, 6.41)] \
     + [(122, z) for z in (-53.59, -43.59, -33.59, -23.59, -13.59, -3.59)] + [(132, z) for z in (-43.59, -33.59, -23.59, -13.59, -3.59)]
PA = [(132, -23.59), (132, -13.59), (132, -3.59)]            # the raised yard east of the road
road_e = {(102, -3.59), (112, -3.59), (122, -3.59), (122, -13.59), (122, -23.59), (122, -33.59), (132, -33.59)}
base(102, -53.59, 122, 16.41, E)
base(122, -53.59, 132, 6.41, E)
base(132, -43.59, 142, -23.59, E)
straight(112, 1.41, True, True, E)
bend(127, 1.41, 180, E)
straight(127, -13.59, False, True, E)
bend(127, -28.59, 0, E)
straight(137, -28.59, True, False, E)
floor(E, *[c for c in EY if c not in road_e and c not in PA])
# the raised yard: its ground, retaining walls to the road, the stairs in the middle of its west side
PE = E + RAISE
base(132, -23.59, 142, -13.59, PE)
base(134.4, -13.59, 142, -3.59, PE)       # the stairs are cut into this one
base(132, -13.59, 134.4, -3.59, E + 0.02, 'StairBase')
base(132, -3.59, 142, 6.41, PE)
floor(PE, (132, -23.59), (132, -3.59))
put('UNIConcrete_wall_v1_S_R_2f', 137, -8.59, 180, PE)
put('UNIConcrete_wall_v1_S_R_2', 132.9, -8.59, 180, E)
retain(131.85, -18.59, False, E)
retain(131.85, 1.41, False, E)
retain(137, -23.74, True, E)
for px, pz in ((131.85, -23.74), (131.85, 6.3), (141.9, -23.74)):
    box('RetainPost', [px, E + 0.86, pz], [0.36, 1.72, 0.36], 'concrete')
# buildings: the long hall of the court, a shed in the north corner, a shed on the raised yard
HALL = (110.5, -15.5)
put('Hangar_v4', HALL[0], HALL[1], 0, E)
put('Hangar_v1_full', 111, 11.2, 90, E)
put('Hangar_v1_full', 137.6, 1.3, 90, PE)

# the pipes: out of the hall's south wall, south along the court, into a riser, the riser against its tank.
# The old run (hall wall - long run - short run - riser) is turned a quarter and moved as one, so its own
# joints stay; the riser is set on the end of the run and the tank against the riser as the old tank stands.
a0, a1 = pipe_axis('Pipes_set_v1_H_set_v1')
b0, b1 = pipe_axis('Pipes_set_v1_H_set_v2 (3)')
R90 = town.yaw_matrix(90)
start = np.array([115.4, 0, HALL[1] - 11.31 + 1.9])        # the mouth of the run, inside the hall's south wall
O = np.array([a0[0], 0, a0[2]])


def moved(p):
    q = R90 @ (np.array([p[0], 0, p[2]]) - O) + start
    return [q[0], p[1], q[2]]


at('Pipes_set_v1_H_set_v1', moved([79.64, -10.94, -10.23]), -80.3 + 90)
at('Pipes_set_v1_H_set_v2', moved([88.68, -10.94, -11.78]), -80.3 + 90)
riser_old = np.array([b1[0] + 0.38, -10.94, b1[2] + 0.05])   # the riser's pair of pipes astride the end of the run
at('Pipes_set_v1_V_set_v2', moved(riser_old), 90 + 90)
tank_old = riser_old + np.array([75.4 - 75.69, 0, -32.52 + 26.87])
TANK = moved([tank_old[0], -10.91, tank_old[2]])
at('Oil_tank_v1', TANK, 90)
PIPE_END = moved(b1)

# things in the court and the low yard
put('Industrial_pipe_v1', 127.6, -49.4, 0, E)
stack(106.3, -32.2, 90, E, True)
stack(136.5, -39.2, 90, E, False)
stack(117.2, 11.5, 0, E, False)
put('Cargo_container_v1_LD2through', 118.2, -45.5, 90, E)
for i, (x, z) in enumerate([(104.5, -1.2), (119, -8.5), (117.5, -21), (119.5, -30.5), (120.5, -50.5), (129.5, -40), (139, -37.5), (125, -5.2), (105, 8.5)]):
    crates(x, z, E, i)
for x, z in [(134.5, -21.5), (140.2, -10.5), (133.4, 4.6)]:
    crates(x, z, PE, 3)
put('Palet_v1_set', 116.5, -27.5, 20, E)
put('Palet_v1_set', 123.6, -45.5, 80, E)
put('Barrel_v3_quadro', 120.6, -12.5, 0, E)
put('Barrel_v3_quadro', 140.6, -21.8, 90, PE)
put('Barrel_v2_quadro', 119.6, -24.0, 0, E)
put('Generator_v1', 119.6, 7.6, 90, E)
put('Road_block_v1', 139.6, -27.0, 80, E)
put('Road_block_v1', 139.8, -30.2, 95, E)
# the wooden look-out on the raised yard, a ladder up it from the yard
TW = (137.2, -19.0)
deck = PE + 5.0
tower(TW[0], TW[1], deck, 3.0, lambda x, z: PE, (0, 0, 1))
ladder([TW[0], PE, TW[1] + 1.85], [0, 0, 1], 5.0)
out['ammo'].append({'pos': r3([139.6, PE + L, -1.0]), 'yaw': 20})
# a ladder up the shed by the gate; from its roof one looks down the road
ladder([111 - 3.62, E, 11.2], [-1, 0, 0], 3.08)
# the fences: every outer side of the yard
for (ax, az, bx, bz, lv) in [
    (102, 16.41, 122, 16.41, E), (122, 6.41, 122, 16.41, E), (122, 6.41, 132, 6.41, E), (132, 6.41, 142, 6.41, PE),
    (142, -23.59, 142, 6.41, PE), (142, -43.59, 142, -23.59, E), (132, -43.59, 142, -43.59, E), (132, -53.59, 132, -43.59, E),
    (102, -53.59, 132, -53.59, E),
    (72, -53.59, 102, -53.59, E),      # the open south edge of the old yard behind the factories
]:
    fence(ax, az, bx, bz, lv)
out['boards'].append({'at': [118.5, E, 4.9], 'ps': 'ختیځ انګړ', 'en': 'EAST YARD'})
out['spawns'].append(r3([116, E + L + 0.1, -44]))
out['yards'] += [[102, -53.59, 122, 16.41], [122, -53.59, 132, 6.41], [132, -43.59, 142, 6.41], [72, -53.59, 102, -40]]

# ===================================================================== the South Yard and the back court
# the road goes on south from its old west end, east along the south side, north, and up the ramp to the
# east level behind the two factories. A raised yard north of the road, the back court on the slope.
PW = W + RAISE
base(-28, -83.59, 32, -53.59, W)
base(32, -73.59, 52, -53.59, W - 0.25)
base(52, -73.59, 72, -53.59, E)
straight(-23, -63.59, False, True, W)
bend(-23, -78.59, 0, W)
straight(-8, -78.59, True, True, W)
straight(12, -78.59, True, True, W)
bend(27, -78.59, -90, W)
bend(27, -68.59, 90, W)
put('Road_set_v1_road_up_2m_HD', 42, -68.59, 180, W - 0.18)
straight(62, -68.59, True, True, E)
put('Road_set_v1_road_up_2m_HD_f', 42, -58.59, 180, W)
floor(E, (52, -63.59), (62, -63.59))
floor(W, (-18, -63.59), (-18, -73.59), (-8, -63.59), (2, -63.59), (12, -73.59), (12, -63.59), (22, -63.59))
# the raised yard (x -8..12, z -73.59..-63.59) with the tank; stairs down to the road on its south side
base(-8, -73.59, 12, -63.59, PW)
floor(PW, (-8, -73.59), (2, -73.59))
put('UNIConcrete_wall_v1_S_middle', -3, -73.59 - 1.12, 90, W)
retain(7, -73.74, True, W)
retain(-8.15, -68.59, False, W)
retain(12.15, -68.59, False, W)
retain(-3, -63.44, True, W)
retain(7, -63.44, True, W)
for px, pz in ((-8.15, -73.74), (12.15, -73.74), (-8.15, -63.44), (12.15, -63.44)):
    box('RetainPost', [px, W + 0.86, pz], [0.36, 1.72, 0.36], 'concrete')
# a tank with its riser as in the old yard, on the raised yard; a shed in the corner by the court
d = np.array([-73.2, 0, -36.2])
at('Oil_tank_v1', [75.4 + d[0], PW, -32.52 + d[2]], 0)
at('Pipes_set_v1_V_set_v2', [75.69 + d[0], PW, -26.87 + d[2]], 90)
put('Hangar_v1_full', 27.1, -59.3, 0, W)
# under the factories that stand out over the court, and the old yard's edge: a concrete footing
box('Footing', [44.7, (-10.6 + W - 0.3) / 2, -54.2], [45.9, -10.6 - W + 0.3, 1.25], 'concrete')
box('Footing', [69.75, (E + W - 0.3) / 2, -53.44], [4.5, E - W + 0.3, 0.3], 'concrete')
# the way through to the old yard: a concrete ramp up between the two halls (x 14.8-21.8), west of the pipe factory
ramp_a = np.array([18.3, W, -53.59]); ramp_b = np.array([18.3, -11.37, -42.8])
n = np.linalg.norm(ramp_b - ramp_a)
box('PassRamp', (ramp_a + ramp_b) / 2 - np.array([0, 0.2, 0]), [7.0, 0.4, n + 0.3], 'concrete', 0, -math.degrees(math.asin((ramp_b[1] - ramp_a[1]) / n)))
box('PassBase', [18.3, -13.6, -48.2], [7.0, 1.6, 10.8], 'concrete')
# and a narrow door at the east end of the court into the yard behind the factories
box('DoorWall', [70.6, E + 1.65, -53.59], [2.8, 3.3, 0.5], 'concrete')
box('DoorLintel', [68.35, E + 2.85, -53.59], [1.7, 0.9, 0.5], 'concrete')
# the look-out on the roof of the pipe factory (D-4): a ladder from the ridge, where the roof pipe comes over
D4 = (31.0, -47.8)
ridge = height.top(D4[0], D4[1])
deck4 = ridge + 3.3
tower(D4[0], D4[1], deck4, 3.0, lambda x, z: height.top(x, z), (1, 0, 0), 'RoofTower')
ladder([D4[0] + 1.85, ridge, D4[1]], [1, 0, 0], 3.3)
out['ammo'].append({'pos': r3([D4[0] - 0.7, deck4 + L, D4[1] - 0.6]), 'yaw': 90})
# things
stack(-13.5, -78.2 + 9.2, 90, W, False)
stack(2.5, -58.6, 90, W, True)
stack(58, -57.8, 90, E, False)
for i, (x, z) in enumerate([(-20, -57), (-25.5, -80.5), (14.5, -70.5), (23.5, -65.2), (9.5, -81.6), (34, -62.5), (47, -56.8), (55, -62), (66, -57), (17.5, -47.5)]):
    lv = W if x < 32 else E
    if 32 <= x < 52: lv = W + (x - 32) / 10.0 + 0.0
    if x == 17.5: lv = W + (z + 53.59) / 10.79 * 1.53
    crates(x, z, lv, i)
for x, z in [(-6, -65.5), (10, -72)]:
    crates(x, z, PW, 5)
put('Palet_v1_set', -24.5, -72, 20, W)
put('Barrel_v3_quadro', 20.5, -81.8, 0, W)
put('Barrel_v2_quadro', 60.5, -62.6, 0, E)
put('Road_block_v1', 69.6, -66.8, 85, E)
put('Road_block_v1', 69.8, -70.3, 97, E)
out['ammo'].append({'pos': r3([13.6, W + L, -66.5]), 'yaw': -70})
for (ax, az, bx, bz, lv) in [
    (-28, -83.59, -28, -53.59, W), (-28, -83.59, 32, -83.59, W), (32, -83.59, 32, -73.59, W), (32, -73.59, 52, -73.59, W + 1.0),
    (52, -73.59, 72, -73.59, E), (72, -73.59, 72, -53.59, E), (-8, -53.59, 12, -53.59, W),
]:
    fence(ax, az, bx, bz, lv)
box('FenceEnd', [13.45, W + 1.6, -53.59], [2.9, 3.3, 0.5], 'concrete')
out['boards'].append({'at': [-16, W, -76], 'ps': 'جنوبي انګړ', 'en': 'SOUTH YARD'})
out['spawns'].append(r3([-3, W + L + 0.1, -81]))
out['spawns'].append(r3([60, E + L + 0.1, -60]))
out['yards'] += [[-28, -83.59, 32, -53.59], [32, -73.59, 72, -53.59], [14.8, -53.59, 21.8, -42]]

json.dump(out, open(os.path.join(town.BASE, 'extension.json'), 'w'), ensure_ascii=False, indent=0)
print({k: len(v) for k, v in out.items()}, 'pipe end', np.round(PIPE_END, 2), 'tank', np.round(TANK, 2))
