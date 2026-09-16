# build_props.py - The Caucus Race prop pack in the EMI hard-surface look. Builds every contract
# node from parameters (propkit.py holds the kit), exports props.glb, saves props.blend and,
# with --render 1, renders each node in the EMI pixel recipe for the contact sheet.
#   blender -b --factory-startup -P build_props.py -- --glb PATH [--render 1] [--out DIR] [--res 512 --pix 4]
# Frames: metres, Blender Z-up, prop front faces -Y (glTF +Z after export_yup), origin at the base
# centre on the ground. Shoulder props are centred on x = 0 (the loader adds the step offset).
import os, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from propkit import *   # noqa: F401,F403

GLB = arg('--glb', os.path.join(HERE, 'out', 'props.glb'))
OUT = arg('--out', os.path.join(HERE, 'out'))
RENDER = arg('--render', '0') == '1'
RES = int(arg('--res', '512'))
PIX = int(arg('--pix', '4'))
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- the kart
def kart_cup(n):
    # JS LatheGeometry profile with the rim folded into the wall as a pink glaze band
    prof = [(0, 0, 'cream'), (0.28, 0, 'cream'), (0.34, 0.06, 'cream'), (0.44, 0.30, 'cream'), (0.52, 0.58, 'cream'),
            (0.55, 0.645, 'pink'), (0.555, 0.675, 'pink'), (0.50, 0.69, 'pink'), (0.48, 0.655, 'cream'),
            (0.47, 0.60, 'cream'), (0.40, 0.30, 'cream'), (0.30, 0.10, 'cream'), (0, 0.08, 'cream')]
    n.add(lathe(prof, segs=24))
    # the handle starts INSIDE the wall at both ends and each root wears a boss half sunk into the
    # porcelain, so the C reads as grown from the cup, not floated beside it
    pts = bezier((0.36, 0, 0.24), (0.86, 0, 0.14), (0.86, 0, 0.74), (0.47, 0, 0.62), n=14)
    n.add(tube(pts, 0.05, mat='cream', sides=8))
    n.add(sphere(0.085, (0.41, 0, 0.25), mat='cream', u=8, v=4))
    n.add(sphere(0.085, (0.50, 0, 0.61), mat='cream', u=8, v=4))

def kart_saucer(n):
    prof = [(0, 0, 'cream'), (0.66, 0, 'cream'), (0.93, 0.075, 'cream'), (0.95, 0.10, 'pink'),
            (0.86, 0.105, 'pink'), (0.60, 0.105, 'cream'), (0, 0.105, 'cream')]
    n.add(lathe(prof, segs=20))
    n.add(box((0.3, 0.08, 0.02), (0.70, 0, 0.115), mat='pink', bevel=0.006))   # the glaze mark

# ---------------------------------------------------------------- road furniture
CUBE = 1.2
def item_cube(n):
    n.add(box((CUBE, CUBE, CUBE), (0, 0, CUBE / 2), mat='cream', bevel=0.07))
    for nrm in ((0, -1, 0), (0, 1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1)):
        c = Vector((0, 0, CUBE / 2)) + Vector(nrm) * (CUBE / 2)
        emboss(n, '?', c, nrm, cell=0.085, depth=0.025, mat='gold')

# the cube cut into 2 x 2 x 3 uneven pieces; each shard's mesh sits on its own centroid and the
# node's translation puts it back where it came from
SHARD_X, SHARD_Y, SHARD_Z = [-0.6, -0.08, 0.6], [-0.6, 0.12, 0.6], [0, 0.34, 0.74, 1.2]
SHARDS = []
for iz in range(3):
    for iy in range(2):
        for ix in range(2):
            sx = SHARD_X[ix + 1] - SHARD_X[ix]; sy = SHARD_Y[iy + 1] - SHARD_Y[iy]; sz = SHARD_Z[iz + 1] - SHARD_Z[iz]
            SHARDS.append(((sx, sy, sz), (SHARD_X[ix] + sx / 2, SHARD_Y[iy] + sy / 2, SHARD_Z[iz] + sz / 2)))
def item_shard(i):
    def build(n):
        size, _c = SHARDS[i]
        n.add(box(size, (0, 0, 0), mat='gold' if i in (1, 6, 10) else 'cream'))
    return build

def boost_pad(n):
    n.add(box((5.75, 3.0, 0.06), (0, 0, 0.03), mat='boost_base', bevel=0.03))
    for sx in (-1, 1):
        n.add(box((0.12, 3.0, 0.08), (sx * 2.80, 0, 0.04), mat='boost_strip'))
    # three chevrons, apex forward (-Y), raised ribs in the emissive strip material
    for y0 in (-0.75, 0.05, 0.85):
        for sx in (-1, 1):
            n.add(box((2.66, 0.26, 0.05), (sx * 1.25, y0, 0.085), mat='boost_strip', rot=(0, 0, -sx * 19.8)))

def ramp_lip(n):
    n.add(box((7.1, 0.34, 0.12), (0, 0, 0.06), mat='cream', bevel=0.03))
    n.add(cyl(0.16, 0.16, 7.1, (0, 0, 0.16), mat='pink', rot=(0, 90, 0), segs=10))

def air_marker(n):
    n.add(box((0.35, 0.35, 0.35), (0, 0, 0.175), mat='gold', bevel=0.06))

def gantry(n):
    for sx in (-1, 1):
        x = sx * 3.75
        n.add(box((0.9, 0.9, 0.3), (x, 0, 0.15), mat='cream', bevel=0.04))
        n.add(fluted(0.30, 0.25, 8, 3.6, (x, 0, 2.1), mat='bone'))
        n.add(box((0.8, 0.8, 0.25), (x, 0, 4.02), mat='gold', bevel=0.04))
    n.add(box((8.4, 0.5, 0.5), (0, 0, 4.4), mat='cream', bevel=0.06))
    n.add(box((8.42, 0.52, 0.12), (0, 0, 4.4), mat='pink', bevel=0.02))
    n.add(box((3.2, 0.12, 1.0), (0, 0, 3.55), mat='blush', bevel=0.04))    # the sign plate (text is the loader's)
    n.add(box((3.3, 0.14, 0.1), (0, 0, 4.1), mat='gold', bevel=0.02))
    # bunting: a sagging string with fourteen flags in the three party colours
    pts = [Vector((-3.75 + 7.5 * i / 14, -0.32, 3.95 - 0.55 * math.sin(math.pi * i / 14))) for i in range(15)]
    for i in range(14):
        a, b = pts[i], pts[i + 1]
        mid = (a + b) / 2
        n.add(box(((b - a).length, 0.025, 0.025), mid, mat='cream', rot=(0, -math.degrees(math.atan2(b.z - a.z, b.x - a.x)), 0)))
        n.add(flag(a, b, mid + Vector((0, 0, -0.45)), mat=('pink', 'gold', 'cream')[i % 3]), outline=False)
    # the teapot crowning the beam
    n.add(sphere(0.45, (0, 0, 5.1), mat='pink', u=10, v=5))
    n.add(cyl(0.28, 0.28, 0.1, (0, 0, 5.55), mat='cream', segs=8))
    n.add(sphere(0.08, (0, 0, 5.65), mat='gold', u=6, v=3))
    n.add(cyl(0.09, 0.05, 0.55, (0.55, 0, 5.25), mat='pink', rot=(0, 60, 0), segs=6))
    for i, (dx, dz, rz) in enumerate(((-0.55, 5.35, 0), (-0.68, 5.15, 90), (-0.55, 4.95, 0))):
        n.add(box((0.26 if rz == 0 else 0.08, 0.08, 0.08 if rz == 0 else 0.4), (dx, 0, dz), mat='pink'))

def podium(n):
    prof = [(0, 0, 'cream'), (0.75, 0, 'cream'), (1.05, 0.26, 'cream'), (1.10, 0.31, 'pink'), (1.10, 0.35, 'pink'),
            (0.98, 0.35, 'pink'), (0.92, 0.31, 'cream'), (0, 0.31, 'cream')]
    n.add(lathe(prof, segs=24))
    n.add(box((0.45, 0.12, 0.02), (0.72, 0, 0.32), mat='pink', bevel=0.006))

def floor_tile(n):
    for i, (x, y) in enumerate(((-0.5, -0.5), (0.5, -0.5), (-0.5, 0.5), (0.5, 0.5))):
        n.add(box((1.0, 1.0, 0.08), (x, y, 0.04), mat='cream' if i in (0, 3) else 'blush', bevel=0.03))

# ---------------------------------------------------------------- room props
def teagarden_wall(n):
    n.add(box((1.4, 0.5, 0.08), (0, -0.25, 0.16), mat='shelf', bevel=0.02))
    for sx in (-1, 1):
        n.add(box((0.06, 0.42, 0.12), (sx * 0.6, -0.21, 0.06), mat='wood'))
    for sx, col in ((-1, 'blush'), (1, 'mint')):
        x = sx * 0.42
        n.add(cyl(0.24, 0.24, 0.03, (x, -0.25, 0.215), mat='cream', segs=6))
        n.add(cyl(0.14, 0.18, 0.26, (x, -0.25, 0.36), mat=col, segs=8, cap_mat='wood'))
        n.add(box((0.08, 0.06, 0.14), (x + sx * 0.20, -0.25, 0.36), mat=col))
    n.add(sphere(0.20, (0, -0.25, 0.42), mat='cream', u=6, v=4))
    n.add(cyl(0.12, 0.12, 0.05, (0, -0.25, 0.63), mat='blush', segs=6))
    n.add(cyl(0.05, 0.03, 0.26, (0.22, -0.25, 0.50), mat='cream', rot=(0, 55, 0), segs=6))
    n.add(box((0.06, 0.06, 0.22), (-0.26, -0.25, 0.42), mat='cream'))
    n.add(box((0.12, 0.06, 0.06), (-0.22, -0.25, 0.53), mat='cream'))

def teagarden_shoulder(n):
    n.add(cyl(0.22, 0.20, 0.12, (0, 0, 0.06), mat='wood', segs=8))
    n.add(sphere(0.25, (0, 0, 0.36), mat='hedge', u=8, v=4, scale=(1, 1, 0.85)))
    n.add(cyl(0.11, 0.11, 0.05, (0, 0, 0.58), mat='cream', segs=6))
    n.add(box((0.07, 0.07, 0.07), (0, 0, 0.63), mat='cream'))
    n.add(cyl(0.05, 0.03, 0.26, (0, -0.25, 0.42), mat='hedge', rot=(-55, 0, 0), segs=6))
    n.add(box((0.06, 0.06, 0.22), (0, 0.27, 0.36), mat='hedge'))
    n.add(box((0.06, 0.14, 0.06), (0, 0.23, 0.47), mat='hedge'))
    for x, y, z in ((-0.14, -0.14, 0.50), (0.17, -0.09, 0.44), (0.03, 0.18, 0.52)):
        n.add(box((0.1, 0.1, 0.1), (x, y, z), mat='blush'))

def toybox_wall(n):
    n.add(box((1.3, 0.5, 0.08), (0, -0.25, 0.04), mat='wood', bevel=0.02))
    for x, col, g in ((-0.42, 'red', 'E'), (0, 'yellow', 'M'), (0.42, 'blue', 'I')):
        n.add(box((0.34, 0.34, 0.34), (x, -0.25, 0.25), mat=col, bevel=0.03))
        emboss(n, g, (x, -0.42, 0.25), (0, -1, 0), cell=0.04, depth=0.012, mat='cream', flat=True)
    n.add(box((0.26, 0.26, 0.26), (0, -0.25, 0.55), mat='blush', bevel=0.02))

def toybox_shoulder(n):
    for (x, y, z, s, rz, col, g) in ((0, 0, 0.20, 0.40, 0, 'red', 'E'), (0.03, 0.03, 0.58, 0.36, 14, 'yellow', 'M'),
                                     (-0.02, -0.02, 0.91, 0.30, -18, 'blue', 'I')):
        n.add(box((s, s, s), (x, y, z), mat=col, bevel=0.03, rot=(0, 0, rz)))
        r = math.radians(rz)
        emboss(n, g, (x + math.sin(r) * s / 2, y - math.cos(r) * s / 2, z), (math.sin(r), -math.cos(r), 0),
               cell=s * 0.11, depth=0.012, mat='cream', flat=True)

def toybox_extra(n):
    n.add(box((0.5, 0.5, 0.5), (0, 0, 0.25), mat='blue', bevel=0.03))
    n.add(box((0.5, 0.5, 0.06), (0, 0.125, 0.716), mat='yellow', bevel=0.02, rot=(-60, 0, 0)))
    n.add(tube(helix(0.09, 0.5, 0.95, 2.5, per_turn=7), 0.025, mat='grey', sides=4, caps=False))
    n.add(sphere(0.16, (0, 0, 1.10), mat='blush', u=8, v=4))

def casino_shoulder(n):
    def stack(x, y, count, cols):
        prof = [(0.17, i * 0.065, cols[i % len(cols)]) for i in range(count)] + [(0.17, count * 0.065, 'chip'), (0, count * 0.065, None)]
        n.add(lathe(prof, segs=8, loc=(x, y, 0), mat='chip'))
    stack(-0.10, -0.18, 6, ['chip', 'white']); stack(0.13, 0.16, 4, ['gold', 'ink']); stack(-0.13, 0.22, 3, ['white', 'chip'])

PIPS = {1: [(0, 0)], 2: [(-1, 1), (1, -1)], 3: [(-1, 1), (0, 0), (1, -1)], 4: [(-1, 1), (1, 1), (-1, -1), (1, -1)],
        5: [(-1, 1), (1, 1), (0, 0), (-1, -1), (1, -1)], 6: [(-1, 1), (1, 1), (-1, 0), (1, 0), (-1, -1), (1, -1)]}
def casino_extra(n):
    S = 0.36
    n.add(box((S, S, S), (0, 0, S / 2), mat='white', bevel=0.04))
    for val, nrm in ((1, (0, -1, 0)), (6, (0, 1, 0)), (2, (0, 0, 1)), (5, (0, 0, -1)), (3, (1, 0, 0)), (4, (-1, 0, 0))):
        u, v = face_basis(nrm)
        c = Vector((0, 0, S / 2)) + Vector(nrm) * (S / 2)
        for px, py in PIPS[val]:
            m = Matrix.Translation(c + u * (px * 0.10) + v * (py * 0.10) + Vector(nrm) * 0.006) @ Matrix((u, v, Vector(nrm))).transposed().to_4x4()
            n.add(box((0.07, 0.07, 0.012), mat='ink', drop=(0, 0, -1), matrix=m), outline=False)

def casino_sign(n):
    # frame around a 1.6 x 0.8 opening centred at z = 0.5 (the neon canvas quad is the loader's)
    n.add(box((1.8, 0.1, 0.1), (0, -0.05, 0.05), mat='maroon', bevel=0.02))
    n.add(box((1.8, 0.1, 0.1), (0, -0.05, 0.95), mat='maroon', bevel=0.02))
    for sx in (-1, 1):
        n.add(box((0.1, 0.1, 1.0), (sx * 0.85, -0.05, 0.5), mat='maroon', bevel=0.02))
    for x, z in ((-0.85, 0.05), (0.85, 0.05), (-0.85, 0.95), (0.85, 0.95)):
        n.add(sphere(0.06, (x, -0.12, z), mat='gold', u=6, v=3))

def undertow_shoulder(n):
    n.add(box((0.5, 0.45, 0.18), (0, 0, 0.09), mat='slate', bevel=0.05))
    for (x, y, rz, cols, hs) in ((-0.12, -0.08, 12, ('teal', 'kelp', 'aqua'), (0.55, 0.55, 0.5)),
                                  (0.12, 0.10, -16, ('kelp', 'teal', 'aqua'), (0.5, 0.5, 0.4)),
                                  (0.02, -0.14, 5, ('teal', 'kelp', 'aqua'), (0.45, 0.45, 0.35))):
        z = 0.18
        for i, (col, h) in enumerate(zip(cols, hs)):
            w = 0.22 - i * 0.04
            n.add(box((w, 0.09, h), (x + i * 0.02, y - i * 0.02, z + h / 2), mat=col, rot=(i * 6, 0, rz)))
            z += h
    n.add(box((0.16, 0.06, 0.3), (-0.26, -0.02, 0.9), mat='kelp', rot=(0, 20, 0)))
    n.add(box((0.16, 0.06, 0.3), (0.26, 0.08, 1.3), mat='kelp', rot=(0, -20, 0)))

def undertow_porthole(n):
    n.add(torus(0.69, 0.09, (0, -0.09, 0.78), mat='steel', rot=(90, 0, 0), segs=16, sides=6))
    for i in range(8):
        a = 2 * math.pi * i / 8 + math.pi / 8
        n.add(box((0.07, 0.05, 0.07), (0.69 * math.cos(a), -0.19, 0.78 + 0.69 * math.sin(a)), mat='grey', rot=(0, math.degrees(a), 0)))

def mirrors_wall(n):
    n.add(box((1.2, 0.1, 0.1), (0, -0.05, 0.05), mat='gold', bevel=0.02))
    n.add(box((1.2, 0.1, 0.1), (0, -0.05, 1.65), mat='gold', bevel=0.02))
    for sx in (-1, 1):
        n.add(box((0.1, 0.1, 1.7), (sx * 0.55, -0.05, 0.85), mat='gold'))
        for z in (0.05, 1.65):
            n.add(sphere(0.08, (sx * 0.55, -0.09, z), mat='gold', u=6, v=3))
    n.add(box((0.3, 0.08, 0.12), (0, -0.05, 1.76), mat='gold', bevel=0.02))
    n.add(sphere(0.07, (0, -0.06, 1.86), mat='blush', u=6, v=3))
    n.add(box((1.0, 0.03, 1.5), (0, -0.015, 0.85), mat='silver'))

def mirrors_shoulder(n):
    n.add(box((0.5, 0.5, 0.1), (0, 0, 0.05), mat='slate', bevel=0.03))
    for (x, y, z, sx, sz, rot, col) in ((-0.08, 0.05, 0.38, 0.32, 0.6, (0, 22, 10), 'silver'), (0.14, -0.14, 0.30, 0.26, 0.44, (0, -40, -8), 'silver'),
                                         (-0.14, -0.18, 0.24, 0.2, 0.3, (0, 70, 15), 'aqua'), (0.10, 0.18, 0.22, 0.18, 0.28, (0, -15, 40), 'grey'),
                                         (0.0, 0.0, 0.14, 0.22, 0.12, (0, 0, 50), 'silver')):
        n.add(box((sx, 0.04, sz), (x, y, z), mat=col, rot=rot))

def chapel_shoulder(n):
    n.add(box((0.5, 0.7, 0.06), (0, 0, 0.03), mat='magenta', bevel=0.02))
    for (x, y, h) in ((-0.12, -0.20, 0.5), (0.06, 0.05, 0.7), (-0.10, 0.26, 0.6)):
        n.add(cyl(0.07, 0.07, h, (x, y, 0.06 + h / 2), mat='white', segs=8))
        n.add(box((0.05, 0.04, 0.12), (x - 0.07, y - 0.02, 0.06 + h - 0.05), mat='white'))
        n.add(box((0.04, 0.05, 0.09), (x + 0.03, y + 0.07, 0.06 + h - 0.03), mat='white'))
        n.add(cyl(0.05, 0.0, 0.14, (x, y, 0.06 + h + 0.07), mat='flame', segs=6))

def chapel_frame(n):
    # 1.2 x 1.6 opening centred at z = 0.9; the stained-glass canvas is the loader's
    n.add(box((1.4, 0.1, 0.1), (0, -0.05, 0.05), mat='bone', bevel=0.02))
    n.add(box((1.4, 0.1, 0.1), (0, -0.05, 1.75), mat='bone', bevel=0.02))
    for sx in (-1, 1):
        n.add(box((0.1, 0.1, 1.8), (sx * 0.65, -0.05, 0.9), mat='bone', bevel=0.02))
        n.add(box((0.9, 0.1, 0.1), (sx * 0.36, -0.05, 2.05), mat='bone', rot=(0, sx * 38, 0)))
    n.add(box((0.06, 0.06, 0.3), (0, -0.05, 2.45), mat='gold'))
    n.add(box((0.2, 0.06, 0.06), (0, -0.05, 2.5), mat='gold'))

def greyward_shoulder(n):
    for sx in (-1, 1):
        for sy in (-1, 1):
            n.add(box((0.05, 0.05, 0.2), (sx * 0.22, sy * 0.38, 0.1), mat='steel'))
    n.add(box((0.54, 0.9, 0.06), (0, 0, 0.23), mat='steel', bevel=0.015))
    n.add(box((0.5, 0.88, 0.14), (0, 0, 0.33), mat='silver', bevel=0.03))
    n.add(box((0.3, 0.2, 0.08), (0, -0.30, 0.44), mat='white', bevel=0.02))
    for sx in (-1, 1):
        n.add(box((0.03, 0.6, 0.03), (sx * 0.27, 0.05, 0.62), mat='grey'))
        for dy in (-0.25, 0.25):
            n.add(box((0.03, 0.03, 0.22), (sx * 0.27, 0.05 + dy, 0.51), mat='grey'))

def greyward_iv(n):
    n.add(box((0.5, 0.06, 0.04), (0, 0, 0.02), mat='steel'))
    n.add(box((0.06, 0.5, 0.04), (0, 0, 0.02), mat='steel'))
    n.add(cyl(0.02, 0.02, 1.5, (0, 0, 0.77), mat='grey', segs=6))
    n.add(box((0.26, 0.03, 0.03), (0.11, 0, 1.52), mat='grey'))
    n.add(box((0.03, 0.03, 0.08), (0.22, 0, 1.48), mat='grey'))
    n.add(box((0.16, 0.06, 0.26), (0.22, 0, 1.31), mat='bag', bevel=0.02))
    n.add(quad((0.1, 0.08), mat='blush', loc=(0.22, -0.032, 1.31), rot=(90, 0, 0)), outline=False)
    n.add(box((0.015, 0.015, 0.6), (0.22, 0, 0.88), mat='silver'))

def greyward_fluoro(n):
    # 1.6 x 0.4 opening centred at z = 0.3 (the fluoro canvas quad is the loader's)
    n.add(box((1.8, 0.12, 0.1), (0, -0.06, 0.05), mat='steel', bevel=0.015))
    n.add(box((1.8, 0.12, 0.1), (0, -0.06, 0.55), mat='steel', bevel=0.015))
    for sx in (-1, 1):
        n.add(box((0.1, 0.12, 0.6), (sx * 0.85, -0.06, 0.3), mat='steel', bevel=0.015))

def coronation_wall(n):
    n.add(prism([(-0.45, 0.5), (-0.45, 2.1), (0.45, 2.1), (0.45, 0.5), (0, 0.85)], 0.05, (0, -0.045, 0), mat='maroon'))
    n.add(box((1.0, 0.07, 0.07), (0, -0.05, 2.12), mat='gold', bevel=0.015))
    n.add(cyl(0.05, 0.05, 1.2, (0, -0.07, 2.2), mat='gold', rot=(0, 90, 0), segs=8))
    for sx in (-1, 1):
        n.add(sphere(0.08, (sx * 0.62, -0.07, 2.2), mat='gold', u=6, v=3))
    for x in (-0.2, 0, 0.2):
        n.add(quad((0.14, 0.14), mat='gold', loc=(x, -0.075, 1.25), rot=(90, 0, 0)), outline=False)
    n.add(quad((0.5, 0.1), mat='gold', loc=(0, -0.075, 1.05), rot=(90, 0, 0)), outline=False)
    n.add(quad((0.5, 0.12), mat='gold', loc=(0, -0.075, 1.7), rot=(90, 0, 0)), outline=False)
    for x in (-0.38, 0, 0.38):
        n.add(box((0.02, 0.02, 0.2), (x, -0.05, 0.42 if x else 0.75), mat='gold'))
        n.add(cyl(0.045, 0.03, 0.25, (x, -0.05, 0.2 if x else 0.53), mat='gold', segs=6))

def coronation_shoulder(n):
    n.add(box((0.56, 0.56, 0.12), (0, 0, 0.06), mat='cream', bevel=0.03))
    n.add(fluted(0.22, 0.19, 8, 1.9, (0, 0, 1.07), mat='bone'))
    n.add(box((0.5, 0.5, 0.06), (0, 0, 2.05), mat='cream'))
    n.add(box((0.6, 0.6, 0.16), (0, 0, 2.16), mat='gold', bevel=0.03))

def coronation_extra(n):
    n.add(cyl(0.24, 0.24, 0.1, (0, 0, 0.05), mat='gold', segs=12))
    n.add(sphere(0.20, (0, 0, 0.16), mat='magenta', u=8, v=4, scale=(1, 1, 0.7)))
    for i in range(6):
        a = 2 * math.pi * i / 6
        n.add(cyl(0.06, 0.0, 0.16, (0.2 * math.cos(a), 0.2 * math.sin(a), 0.18), mat='gold', segs=6))
        n.add(box((0.07, 0.03, 0.07), (0.245 * math.cos(a), 0.245 * math.sin(a), 0.05), mat='magenta', rot=(0, 0, math.degrees(a) + 90)))
    n.add(box((0.03, 0.03, 0.14), (0, 0, 0.33), mat='gold'))
    n.add(box((0.1, 0.03, 0.03), (0, 0, 0.36), mat='gold'))

# ---------------------------------------------------------------- the table
PROPS = [('kart_cup', kart_cup), ('kart_saucer', kart_saucer), ('item_cube', item_cube)]
PROPS += [('item_shard_%02d' % i, item_shard(i)) for i in range(12)]
PROPS += [('boost_pad', boost_pad), ('ramp_lip', ramp_lip), ('air_marker', air_marker), ('gantry', gantry),
          ('podium', podium), ('floor_tile', floor_tile),
          ('teagarden_wall', teagarden_wall), ('teagarden_shoulder', teagarden_shoulder),
          ('toybox_wall', toybox_wall), ('toybox_shoulder', toybox_shoulder), ('toybox_extra', toybox_extra),
          ('casino_shoulder', casino_shoulder), ('casino_extra', casino_extra), ('casino_sign', casino_sign),
          ('undertow_shoulder', undertow_shoulder), ('undertow_porthole', undertow_porthole),
          ('mirrors_wall', mirrors_wall), ('mirrors_shoulder', mirrors_shoulder),
          ('chapel_shoulder', chapel_shoulder), ('chapel_frame', chapel_frame),
          ('greyward_shoulder', greyward_shoulder), ('greyward_iv', greyward_iv), ('greyward_fluoro', greyward_fluoro),
          ('coronation_wall', coronation_wall), ('coronation_shoulder', coronation_shoulder), ('coronation_extra', coronation_extra)]
BUDGET = {'kart_cup': 3000, 'gantry': 2000, 'podium': 2000}

built = {}
for name, fn in PROPS:
    node = Node(name, thick=0.01 if name.startswith('item_shard') else None)
    fn(node)
    loc = SHARDS[int(name[-2:])][1] if name.startswith('item_shard') else (0, 0, 0)
    node.build(loc)
    built[name] = node
    lo, hi = node.bbox()
    cap = BUDGET.get(name, 40 if name.startswith('item_shard') else 600)
    print('NODE %-22s tris %5d /%5d  bbox %.2f x %.2f x %.2f  outline %.3f %s' % (
        name, node.tris(), cap, hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, node.thick_used, '' if node.tris() <= cap else 'OVER'), flush=True)
print('TOTAL tris', sum(n.tris() for n in built.values()), flush=True)

size = export_glb(GLB)
print('GLB', GLB, size, 'bytes', flush=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'props.blend'))

# ---------------------------------------------------------------- contact-sheet renders
if RENDER:
    render_setup(RES, PIX)
    objs = [(n.ob, n.ob_ol) for n in built.values()]
    def show(names):
        for ob, ol in objs:
            ob.hide_render = ol.hide_render = ob.name not in names
    def bbox_of(names):
        lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
        for nm in names:
            a, b = built[nm].bbox(); off = Vector(built[nm].ob.location)
            lo = Vector(tuple(min(x, y) for x, y in zip(lo, a + off))); hi = Vector(tuple(max(x, y) for x, y in zip(hi, b + off)))
        return lo, hi
    for name in built:
        if name.startswith('item_shard'):
            continue
        show({name}); frame(*bbox_of([name]))
        render(os.path.join(OUT, name + '.png'))
    shards = [nm for nm in built if nm.startswith('item_shard')]
    for nm in shards:
        built[nm].ob.location = Vector(SHARDS[int(nm[-2:])][1]) * 1.6 + Vector((0, 0, 0.3))
    show(set(shards)); frame(*bbox_of(shards))
    render(os.path.join(OUT, 'item_shards.png'))
    # hero: the cup riding its saucer (the loader lifts the cup 0.09 like the JS rig)
    built['kart_cup'].ob.location = (0, 0, 0.09)
    show({'kart_cup', 'kart_saucer'})
    render_res(1024, 6)
    frame(*bbox_of(['kart_cup', 'kart_saucer']), fill=1.1)
    render(os.path.join(OUT, 'hero_kart.png'))
print('BUILD_DONE', flush=True)
