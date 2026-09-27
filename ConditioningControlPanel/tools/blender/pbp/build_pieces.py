# build_pieces.py - the six Piece by Piece men, pink and purple, as twelve glbs.
#   blender -b --factory-startup -P build_pieces.py -- --out DIR [--sheet PATH] [--only pawn,king]
# Every man is a closed lathe about Z with radial detail (ribs, spiral veins, suckers, beads) and a
# colour per vertex, plus any parts the lathe cannot turn (balls, a horn) in the same mesh. The king's
# crown and the queen's tiara are separate meshes in their own metal, which is how pieces.js tells
# jewellery from body: the body is the mesh carrying COLOR_0, anything else keeps its material.
# Contract (pieces.js): +Y up in the file, origin at the base centre, one board square = 1.0 and the
# man carries his own height; the loader rescales nothing.
import os, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from piecekit import *   # noqa: F401,F403

OUT = arg('--out', os.path.join(HERE, 'out'))
SHEET = arg('--sheet', '')
ONLY = arg('--only', '')
ONLY = set(ONLY.split(',')) if ONLY else None
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- the two sides
# Measured off the first-pass files: hot pink #ff69b4 as the pink body with cream marbling and a pale
# pink lift; the purple family is the rook's, #4e2273 body sinking to #222240 and lifting to a pale
# lilac #a288cd, with a lilac-white #d4c6ee where the pink side goes cream.
SIDES = {
    '':        dict(body=0xFF69B4, dark=0xE0559C, pale=0xFFBBDD, cream=0xEEDDCC, tip=0xFAECD5,
                    king=0xFE75B8, king_dark=0xF162A7, king_top=0xF95C9C,
                    queen=0xE072A5, queen_pale=0xFE76B8, queen_base=0xEFE8DA,
                    coat=None),
    '_purple': dict(body=0x4E2273, dark=0x222240, pale=0xA288CD, cream=0xD4C6EE, tip=0xBB99DD,
                    king=0x7334AA, king_dark=0x632796, king_top=0x612392,
                    queen=0x704D99, queen_pale=0x7642AB, queen_base=0xD4C6EE,
                    coat=0x7B6CFF),
}

def marble(co, a, b, scale=5.0, lo=0.18, hi=0.34, seed=0.0):
    """Cream veins through a body colour: noise on position, so the pattern wraps seamlessly."""
    m = fbm(co + Vector((seed, seed * 0.7, seed * 1.3)), 3, scale)
    return mix(a, b, smooth(lo, hi, m))

def front(th):
    """0 at the front (Blender -Y), 1 at the back."""
    return 0.5 - 0.5 * math.sin(-th) * -1 if False else (1 - math.cos(th + math.pi / 2)) / 2

FRONT = -math.pi / 2   # theta of the front, Blender -Y

# ---------------------------------------------------------------- pawn: the plug (h 0.55, r 0.172)
def pawn(P):
    H = 0.55
    prof = Profile([(0, 0.172), (0.03, 0.172), (0.05, 0.13), (0.08, 0.06), (0.13, 0.058), (0.18, 0.11),
                    (0.26, 0.148), (0.33, 0.146), (0.40, 0.125), (0.46, 0.09), (0.51, 0.05), (0.55, 0.0)])
    body_c, dark_c, pale_c = rgb(P['body']), rgb(P['dark']), rgb(P['pale'])
    purple = P['coat'] is not None
    def radius(t, th):
        return prof(t * H)
    def colour(t, th, r, co):
        if not purple:
            return body_c
        # the purple pawn: body over most of him, sinking toward the dark stop through the base disc
        # and the neck, lifting toward the pale lilac over the last third of the head
        c = mix(dark_c, body_c, smooth(0.10, 0.30, t))
        return mix(c, pale_c, smooth(0.62, 1.0, t))
    b = Body('pawn').surface(H, radius, colour, nz=80, nseg=48)
    return b.build(silicone('pawn_silicone', P['coat'])), []

# ---------------------------------------------------------------- knight: shaft and balls (h 1.0, r 0.254)
def knight(P):
    H = 1.0
    prof = Profile([(0, 0.25), (0.02, 0.25), (0.05, 0.20), (0.10, 0.14), (0.30, 0.135), (0.60, 0.13),
                    (0.78, 0.128), (0.83, 0.14), (0.87, 0.15), (0.92, 0.135), (0.97, 0.08), (1.0, 0.0)])
    body_c, cream_c, pale_c = rgb(P['body']), rgb(P['cream']), rgb(P['pale'])
    def radius(t, th):
        z = t * H
        r = prof(z)
        # the coronal ridge sits higher at the back, so the glans reads as a glans from the side
        r += 0.012 * smooth(0.80, 0.86, z) * (1 - smooth(0.88, 0.94, z)) * (0.5 + 0.5 * math.cos(th - FRONT + math.pi))
        # a soft vein up the back of the shaft
        r += helix(th, z, 0.35, 1, 0.010, 0.15, 0.80, power=4.0, phase=math.pi)
        return r
    def colour(t, th, r, co):
        c = body_c
        # cream marbling creeps in over the top third
        c = mix(c, marble(co, body_c, cream_c, 6.0, 0.22, 0.40, 3.0), smooth(0.55, 0.85, t))
        return mix(c, pale_c, 0.35 * smooth(0.88, 1.0, t))
    b = Body('knight').surface(H, radius, colour, nz=120, nseg=64)
    def ball_col(co, n):
        return marble(co, body_c, cream_c, 7.0, 0.30, 0.42, 9.0)
    for sx in (-1, 1):
        b.sphere((sx * 0.115, -0.10, 0.125), 0.115, ball_col, u=28, v=14, scale=(1, 1, 1.05),
                 bump=lambda n: 0.006 * fbm(n * 2.0 + Vector((sx, 0, 0)), 2, 2.0))
    return b.build(silicone('knight_silicone', P['coat'])), []

# ---------------------------------------------------------------- bishop: the tentacle (h 1.0, r 0.235)
def bishop(P):
    H = 1.0
    prof = Profile([(0, 0.235), (0.03, 0.235), (0.07, 0.19), (0.12, 0.155), (0.30, 0.125), (0.55, 0.085),
                    (0.80, 0.045), (0.95, 0.018), (1.0, 0.0)])
    body_c, tip_c, pale_c = rgb(P['body']), rgb(P['tip']), rgb(P['pale'])
    # two rows of suckers up the front, shrinking with the taper
    spots = []
    z = 0.16
    k = 0
    while z < 0.93:
        size = 0.052 * (1 - 0.62 * z) + 0.010
        for sgn in (-1, 1):
            spots.append((FRONT + sgn * (0.48 - 0.12 * z), z + (0.025 if sgn > 0 else 0), size, size * 1.5))
        z += size * 2.4 + 0.010
        k += 1
    def centre(t):
        # a gentle lean toward the front over the top half
        return Vector((0, -0.09 * t * t))
    def radius(t, th):
        z = t * H
        r = prof(z)
        # sucker positions are in surface space: angular distance scaled by the local radius
        r += bumps(th, z, r, spots, cup=0.7)
        return r
    def colour(t, th, r, co):
        c = mix(body_c, tip_c, smooth(0.35, 0.95, t))
        # the sucker rims read a shade paler
        d = bumps(th, t * H, prof(t * H), spots, cup=0.0)
        return mix(c, pale_c, smooth(0.006, 0.03, d) * 0.6)
    b = Body('bishop').surface(H, radius, colour, nz=150, nseg=72, centre=centre)
    return b.build(silicone('bishop_silicone', P['coat'])), []

# ---------------------------------------------------------------- rook: the ribbed dome (h 1.0, r 0.274)
def rook(P):
    H = 1.0
    prof = Profile([(0, 0.274), (0.04, 0.274), (0.07, 0.23), (0.11, 0.19), (0.20, 0.195), (0.60, 0.20),
                    (0.78, 0.195), (0.88, 0.16), (0.95, 0.09), (1.0, 0.0)])
    body_c, dark_c, pale_c, cream_c = rgb(P['body']), rgb(P['dark']), rgb(P['pale']), rgb(P['cream'])
    beads = [(FRONT, z, 0.032, 0.034) for z in (0.24, 0.34, 0.44, 0.54, 0.64, 0.74)]
    def radius(t, th):
        z = t * H
        r = prof(z)
        r += ribs(z, 0.085, 0.020, 0.14, 0.84)
        r += bumps(th, z, r, beads)
        return r
    def colour(t, th, r, co):
        z = t * H
        if z < 0.10:
            return marble(co, body_c, cream_c, 6.0, 0.10, 0.30, 5.0)
        # rib valleys a shade darker, the cap a shade paler
        rib = ribs(z, 0.085, 1.0, 0.14, 0.84)
        c = mix(dark_c, body_c, 0.55 + 0.45 * rib)
        return mix(c, pale_c, 0.6 * smooth(0.84, 1.0, t))
    b = Body('rook').surface(H, radius, colour, nz=140, nseg=64)
    return b.build(silicone('rook_silicone', P['coat'])), []

# ---------------------------------------------------------------- queen: the veined shaft on a knobbly base (h 1.15, r 0.171)
def queen(P):
    H = 1.15
    prof = Profile([(0, 0.171), (0.03, 0.171), (0.10, 0.155), (0.18, 0.13), (0.24, 0.10), (0.30, 0.095),
                    (0.70, 0.095), (0.95, 0.085), (1.05, 0.075), (1.12, 0.04), (1.15, 0.0)])
    body_c, pale_c, base_c = rgb(P['queen']), rgb(P['queen_pale']), rgb(P['queen_base'])
    def radius(t, th):
        z = t * H
        r = prof(z)
        # the base is lumpy stone
        r += 0.012 * fbm(Vector((math.cos(th), math.sin(th), z * 3)), 2, 3.0) * (1 - smooth(0.16, 0.26, z))
        # two spiral veins, opposite hands, over the shaft
        r += helix(th, z, 1.6, 2, 0.011, 0.28, 1.08, power=2.5)
        r += helix(th, z, -1.1, 3, 0.007, 0.28, 1.08, power=3.0, phase=1.0)
        return r
    def colour(t, th, r, co):
        z = t * H
        base = mix(base_c, mix(base_c, body_c, 0.5), smooth(0.10, 0.20, z) * 0.3)
        c = mix(body_c, pale_c, 0.5 * smooth(0.5, 1.0, math.sin(2 * th - TAU * 1.6 * z) ** 2))
        c = mix(c, pale_c, 0.4 * smooth(1.02, 1.15, z))
        return mix(base, c, smooth(0.20, 0.27, z))
    b = Body('queen').surface(H, radius, colour, nz=140, nseg=64)
    # a small horn out of the front of the base
    def horn_col(co, n):
        return base_c
    b.cone((0, -0.12, 0.08), (0, -0.20, 0.22), 0.035, horn_col, segs=14)
    body = b.build(silicone('queen_silicone', P['coat']))
    # the tiara: a slim ring with five points
    t = Body('tiara')
    silver_c = rgb(0xDDE3F0)
    def sc(co, n):
        return silver_c
    t.torus((0, 0, 1.115), 0.062, 0.008, sc, segs=40, sides=8)
    for i in range(5):
        a = TAU * i / 5 + math.pi / 2
        x, y = 0.062 * math.cos(a), 0.062 * math.sin(a)
        h = 0.055 if i == 0 else 0.038
        t.cone((x, y, 1.118), (x * 1.05, y * 1.05, 1.118 + h), 0.009, sc, segs=10)
    tiara = t.build(metal('silver', 0xDDE3F0, 0.28))
    tiara.data.color_attributes.remove(tiara.data.color_attributes[0])
    return body, [tiara]

# ---------------------------------------------------------------- king: the big one (h 1.25, r 0.262)
def king(P):
    H = 1.25
    prof = Profile([(0, 0.262), (0.02, 0.262), (0.05, 0.20), (0.12, 0.155), (0.32, 0.15), (0.70, 0.152),
                    (0.98, 0.15), (1.04, 0.165), (1.08, 0.17), (1.13, 0.155), (1.20, 0.10), (1.25, 0.0)])
    body_c, dark_c, top_c = rgb(P['king']), rgb(P['king_dark']), rgb(P['king_top'])
    def radius(t, th):
        z = t * H
        r = prof(z)
        r += helix(th, z, 1.1, 2, 0.016, 0.36, 1.02, power=2.2)
        r += helix(th, z, -0.8, 1, 0.010, 0.30, 1.00, power=3.0, phase=2.0)
        r += 0.010 * smooth(1.00, 1.05, z) * (1 - smooth(1.08, 1.14, z)) * (0.5 + 0.5 * math.cos(th - FRONT + math.pi))
        return r
    def colour(t, th, r, co):
        z = t * H
        c = mix(dark_c, body_c, smooth(0.0, 0.40, z))
        v = helix(th, z, 1.1, 2, 1.0, 0.36, 1.02, power=2.2)
        c = mix(c, dark_c, 0.35 * smooth(0.2, 0.9, v))
        return mix(c, top_c, smooth(1.02, 1.25, z))
    b = Body('king').surface(H, radius, colour, nz=150, nseg=64)
    def ball_col(co, n):
        return mix(dark_c, body_c, 0.5 + 0.5 * n.z)
    for sx in (-1, 1):
        b.sphere((sx * 0.13, -0.07, 0.16), 0.145, ball_col, u=28, v=14, scale=(1, 1, 1.05),
                 bump=lambda n: 0.008 * fbm(n * 2.0 + Vector((0, sx, 0)), 2, 2.0))
    body = b.build(silicone('king_silicone', P['coat']))
    # the crown: a ring at the top of the glans with six points
    c = Body('crown')
    gold_c = rgb(0xFFA030)
    def gc(co, n):
        return gold_c
    c.torus((0, 0, 1.205), 0.095, 0.010, gc, segs=48, sides=8)
    for i in range(6):
        a = TAU * i / 6
        x, y = 0.095 * math.cos(a), 0.095 * math.sin(a)
        c.cone((x, y, 1.208), (x * 1.08, y * 1.08, 1.285), 0.014, gc, segs=10)
        c.sphere((x * 1.08, y * 1.08, 1.283), 0.011, gc, u=10, v=6)
    crown = c.build(metal('gold', 0xFFA030, 0.25))
    crown.data.color_attributes.remove(crown.data.color_attributes[0])
    return body, [crown]

# ---------------------------------------------------------------- build, export, sheet
RECIPES = [('pawn', pawn), ('knight', knight), ('bishop', bishop), ('rook', rook), ('queen', queen), ('king', king)]
built = []
for col, (name, fn) in enumerate(RECIPES):
    if ONLY and name not in ONLY:
        continue
    for row, (suffix, P) in enumerate(SIDES.items()):
        body, jewels = fn(P)
        path = os.path.join(OUT, f'{name}{suffix}.glb')
        size = export_glb([body] + jewels, path)
        me = body.data
        zmax = max(v.co.z for v in me.vertices)
        print(f'[pieces] {name}{suffix}.glb {size // 1024}KB verts={len(me.vertices)} tris={sum(len(p.vertices) - 2 for p in me.polygons)} height={zmax:.3f}' + (f' +{",".join(j.name for j in jewels)}' if jewels else ''))
        # park the built men on the sheet grid and free their names, so the purple king's crown is
        # also called `crown` in its own file rather than `crown.001`
        for o in [body] + jewels:
            o.location = (col * 1.0, row * 2.2, 0)
            o.name = o.data.name = f'{o.name}{suffix}_built'
        for m in body.data.materials:
            m.name = f'{m.name}{suffix}_built'
        built.append(body)

if SHEET:
    sheet(SHEET, cols=len(RECIPES), rows=len(SIDES), pitch=1.0)
    print(f'[pieces] sheet {SHEET}')
print('[pieces] DONE')
