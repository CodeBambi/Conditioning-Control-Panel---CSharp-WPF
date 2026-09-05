# Hard-surface EMI: a real CRT case (deep tapered back, vents), a curved glass the faces
# project onto, cylindrical arms with mitten hands, stub legs with sneakers, and a jointed
# antenna that poses per emotion.
#   blender -b --factory-startup -P build_hs.py -- --mode test|poses|turn --out DIR [--res N] [--frames N]
# Game LOD (The Caucus Race glb, see export_glb.py): add --game 1. That switches to 16-segment
# rounds, 1-segment bevels (2 on the case, bezel and screen cut), metre units (sole to case top
# = 1.0 m, origin at the sole centre between the feet), the export names EMI_case / EMI_glass /
# outline, a glTF-portable screen material driven by the five-frame face atlas, no floor,
# lights or camera. --mode game saves the posed rig to OUT/emi_game.blend and stops.
import bpy, bmesh, math, os, sys
from mathutils import Vector
# ---------------------------------------------------------------- args
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def arg(name, default):
    return argv[argv.index(name) + 1] if name in argv else default
MODE = arg('--mode', 'test')
OUT = arg('--out', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out'))
RES = int(arg('--res', '1024' if MODE != 'test' else '640'))
PIX = int(arg('--pix', '0'))          # >0: render at RES // PIX for a pixel-art look (upscale with pixup.py)
if PIX > 1:
    RES = RES // PIX
FRAMES = int(arg('--frames', '40'))
OUTLINE = arg('--outline', '1') == '1'
GAME = arg('--game', '0') == '1'      # game LOD, see the header
HERE = os.path.dirname(os.path.abspath(__file__))
FACES = os.path.join(HERE, 'faces')
ATLAS = arg('--atlas', os.path.join(FACES, 'emi-faces.png'))   # game only: face2.py --game output
os.makedirs(OUT, exist_ok=True)

S = 0.1  # one sprite cell = 0.1 blender units (the game LOD re-derives S in metres below)
SOLE_BOTTOM = 0.3          # cells: where the sole's outline hull meets the floor
# ---------------------------------------------------------------- palette (sprite-sampled, sRGB 8-bit)
def lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
def rgb(r, g, b, a=1.0):
    return (lin(r), lin(g), lin(b), a)

C_CASE = (40, 35, 62)
C_BEZEL = (46, 41, 71)
C_SIDE_DARK = (16, 19, 48)
C_SCREEN = (45, 33, 61)
C_PINK = (255, 105, 180)
C_SHOE = (232, 85, 143)
C_SOLE = (238, 232, 240)
C_LACE = (250, 205, 222)
C_GOLD = (219, 168, 48)
C_BALL = (241, 169, 190)
C_BALL_CORE = (255, 105, 180)
# ---------------------------------------------------------------- scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.eevee.taa_render_samples = 64
scene.eevee.use_shadows = True
scene.eevee.use_raytracing = True
scene.render.resolution_x = RES
scene.render.resolution_y = RES
if PIX > 1:
    scene.render.filter_size = 0.6      # crisper pixel edges, minecraft-ish stair steps
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.film_transparent = False
scene.view_settings.view_transform = 'AgX'
scene.view_settings.look = 'AgX - Medium High Contrast'

world = bpy.data.worlds.new('W')
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes['Background']
bg.inputs[0].default_value = (0.012, 0.011, 0.030, 1)
bg.inputs[1].default_value = 1.0
# ---------------------------------------------------------------- helpers
def mat(name, c, rough=0.45, coat=0.0, emit=None, emit_str=0.0, metallic=0.0, spec=0.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = rgb(*c)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metallic
    b.inputs['Coat Weight'].default_value = coat
    if 'Specular IOR Level' in b.inputs:
        b.inputs['Specular IOR Level'].default_value = spec
    if emit is not None:
        b.inputs['Emission Color'].default_value = rgb(*emit)
        b.inputs['Emission Strength'].default_value = emit_str
    return m

M_CASE = mat('emi_case', C_CASE, rough=0.42, coat=0.2)
M_BEZEL = mat('emi_bezel', C_BEZEL, rough=0.38, coat=0.25)
M_DARK = mat('emi_dark', C_SIDE_DARK, rough=0.6)
M_SHOE = mat('emi_shoe', C_SHOE, rough=0.5, coat=0.1)
M_SOLE = mat('emi_sole', C_SOLE, rough=0.7)
M_LACE = mat('emi_lace', C_LACE, rough=0.6)
M_GOLD = mat('emi_gold', C_GOLD, rough=0.3, metallic=0.7)
M_BALL = mat('emi_ball', C_BALL, rough=0.25, coat=0.5, emit=C_BALL_CORE, emit_str=0.9)

M_OUTLINE = bpy.data.materials.new('outline' if GAME else 'emi_outline')   # 'outline' is a loader contract
M_OUTLINE.use_nodes = True
nt = M_OUTLINE.node_tree
for n in list(nt.nodes):
    nt.nodes.remove(n)
em = nt.nodes.new('ShaderNodeEmission')
em.inputs[0].default_value = (0.004, 0.004, 0.010, 1)
em.inputs[1].default_value = 1.0
outn = nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(em.outputs[0], outn.inputs[0])
M_OUTLINE.use_backface_culling = True

def smooth(obj):
    me = obj.data
    me.polygons.foreach_set('use_smooth', [True] * len(me.polygons))
    me.update()

def bevel(obj, width, segments=4, angle=None, harden=True, game_segments=1):
    mod = obj.modifiers.new('bevel', 'BEVEL')
    mod.width = width * S
    mod.segments = game_segments if GAME else segments
    mod.harden_normals = harden
    if angle is not None:
        mod.limit_method = 'ANGLE'
        mod.angle_limit = math.radians(angle)
    else:
        mod.limit_method = 'NONE'
    return mod

def outline(obj, thick=0.55):
    if not OUTLINE:
        return
    obj.data.materials.append(M_OUTLINE)
    mod = obj.modifiers.new('outline', 'SOLIDIFY')
    mod.thickness = thick * S
    mod.offset = 1.0
    mod.use_flip_normals = True
    mod.material_offset = 1
    mod.use_rim = False

def link(obj, parent=None):
    scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj

def place(o, loc, parent):
    # cells -> units; the game LOD moves the origin from the case's front-bottom line to the
    # sole centre between the feet, so every direct child of the root shifts by ORIGIN
    o.location = Vector(loc) * S
    if GAME and parent is not None and parent is root:
        o.location -= Vector(ORIGIN) * S

def lod_segs(n, r=None):
    # game LOD: 16 segments on anything round, 8 on beads under a cell in radius
    if not GAME:
        return n
    return 8 if (r is not None and r < 1.0) else min(n, 16)

def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.05
    place(e, loc, parent)
    return link(e, parent)

def mesh_obj(name, verts, faces, material, parent=None, loc=(0, 0, 0)):
    me = bpy.data.meshes.new(name)
    me.from_pydata([Vector(v) * S for v in verts], [], faces)
    me.update()
    me.materials.append(material)
    o = bpy.data.objects.new(name, me)
    place(o, loc, parent)
    return link(o, parent)

def box(name, size, loc, material, parent=None, rot=(0, 0, 0)):
    sx, sy, sz = size
    v = [(x * sx / 2, y * sy / 2, z * sz / 2) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    o = mesh_obj(name, v, f, material, parent, loc)
    o.rotation_euler = [math.radians(a) for a in rot]
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()
    return o

def cylinder(name, r, depth, loc, material, parent=None, rot=(0, 0, 0), segs=48):
    segs = lod_segs(segs, r)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs,
                          radius1=r * S, radius2=r * S, depth=depth * S)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    me.materials.append(material)
    o = bpy.data.objects.new(name, me)
    place(o, loc, parent)
    o.rotation_euler = [math.radians(a) for a in rot]
    link(o, parent)
    smooth(o)
    return o

def sphere(name, r, loc, material, parent=None, scale=(1, 1, 1), segs=48):
    segs = lod_segs(segs, r)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=segs // 2, radius=r * S)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    me.materials.append(material)
    o = bpy.data.objects.new(name, me)
    place(o, loc, parent)
    o.scale = scale
    link(o, parent)
    smooth(o)
    return o

# ---------------------------------------------------------------- dimensions (cells)
W, H = 54.0, 55.0          # case front width / height
Z0 = 12.0                  # case bottom (shoes sit under it, floor at z=0)
ZC = Z0 + H / 2            # case centre height
D1 = 14.0                  # front box depth (y 0..D1, front face at y=0, back is +y)
D2 = 30.0                  # tapered hump depth
BACK_SCALE = 0.60
BACK_LIFT = 1.5
SW, SH = 36.0, 33.0        # screen opening
SZ = ZC + 2.0              # screen centre (sits a touch high, like the sprite)
BULGE = 2.4                # glass bulge forward of the bezel (apex sits ~1.2 ahead of the bezel face)
BEZEL_T = 2.4              # bezel plate thickness (protrudes in front of the case)
if GAME:
    S = 1.0 / (Z0 + H - SOLE_BOTTOM)     # 1 unit = 1 metre: sole (outline hull) to case top = 1.0 m
ORIGIN = (0, D1 * 0.5 - 2.0, SOLE_BOTTOM)   # cells: sole centre between the feet (game origin)

root = None
root = empty('EMI_root', (0, 0, 0))

# ---------------------------------------------------------------- CRT case (one tapered mesh)
def ring(y, sx, sz, zc):
    return [(-W / 2 * sx, y, zc - H / 2 * sz), (W / 2 * sx, y, zc - H / 2 * sz),
            (W / 2 * sx, y, zc + H / 2 * sz), (-W / 2 * sx, y, zc + H / 2 * sz)]
rings = [ring(0, 1, 1, ZC), ring(D1, 1, 1, ZC), ring(D1 + 4, 0.93, 0.93, ZC + 0.5),
         ring(D1 + D2, BACK_SCALE, BACK_SCALE, ZC + BACK_LIFT)]
verts = [v for r in rings for v in r]
faces = [(0, 3, 2, 1)]                       # front cap (normal -y)
for i in range(len(rings) - 1):
    a = i * 4
    for k in range(4):
        faces.append((a + k, a + (k + 1) % 4, a + 4 + (k + 1) % 4, a + 4 + k))
b = (len(rings) - 1) * 4
faces.append((b, b + 1, b + 2, b + 3))      # back cap
case = mesh_obj('EMI_case' if GAME else 'case', verts, faces, M_CASE, root)   # EMI_case is a loader contract
bm = bmesh.new(); bm.from_mesh(case.data)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(case.data); bm.free()
bevel(case, 3.2, segments=8, game_segments=2)
smooth(case)
outline(case)

# back vents: dark slats on the back plate
yb = D1 + D2 + 0.15
for i in range(6):
    z = ZC + BACK_LIFT - 7 + i * 2.6
    box('vent%d' % i, (W * BACK_SCALE * 0.62, 0.5, 1.0), (0, yb, z), M_DARK, root)
# a small dark power inlet on the lower back
box('inlet', (5, 0.8, 3), (W * BACK_SCALE * 0.28, yb, ZC + BACK_LIFT - 12), M_DARK, root)

# ---------------------------------------------------------------- bezel plate with the screen cut out
bez = box('bezel', (W - 3.0, BEZEL_T + 1.0, H - 3.0), (0, -BEZEL_T / 2 + 0.5, ZC), M_BEZEL, root)
bevel(bez, 3.6, segments=8, game_segments=2)
cutter = box('cutter', (SW, 12.0, SH), (0, -BEZEL_T / 2, SZ), M_DARK, root)   # MUST ride the root, or the hole stays behind when she turns
bevel(cutter, 3.0, segments=8, game_segments=2)   # the screen's rounded corners are part of her face
cutter.hide_render = True
cutter.display_type = 'WIRE'
boo = bez.modifiers.new('cut', 'BOOLEAN')
boo.operation = 'DIFFERENCE'
boo.object = cutter
boo.solver = 'EXACT'
bevel(bez, 0.5, segments=3, angle=40)
smooth(bez)
outline(bez, 0.5)
# dark recess behind the glass
box('recess', (SW - 0.4, 3.0, SH - 0.4), (0, 1.2, SZ), M_DARK, root)

# ---------------------------------------------------------------- curved glass (superellipse dome) + face material
def glass_mesh():
    a, bb, n = SW / 2 + 2.5, SH / 2 + 2.5, 5.0   # rim hides inside the bezel plate, so the cut-out alone shapes the glass
    ua, ub = SW / 2 - 0.35, SH / 2 - 0.35           # face UVs keep the opening size, so glyphs are not clipped
    R, N = (6, 32) if GAME else (18, 72)
    def face_uv(x, z):
        u, v = 0.5 + x / (2 * ua), 0.5 + z / (2 * ub)
        if GAME:
            # the atlas is five 152x137 frames side by side; the glass spans frame 0 only
            # (U in [0, 0.2]) and the runtime picks frame i with map.offset.x = i / 5. The hidden
            # rim would run past the frame, so clamp to the edge texel centres of the frame.
            u = min(max(u, 0.5 / 152), 1 - 0.5 / 152) / 5.0
            v = min(max(v, 0.5 / 137), 1 - 0.5 / 137)
        return (u, v)
    vs, uv = [], []
    y0 = -BEZEL_T + 1.2             # glass rim recessed 1.2 cells behind the bezel face
    vs.append((0, y0 - BULGE, 0)); uv.append(face_uv(0, 0))
    for r in range(1, R + 1):
        s = r / R
        z_b = BULGE * (1 - s * s) ** 1.1
        for k in range(N):
            t = 2 * math.pi * k / N
            c, sn = math.cos(t), math.sin(t)
            x = a * math.copysign(abs(c) ** (2 / n), c) * s
            z = bb * math.copysign(abs(sn) ** (2 / n), sn) * s
            vs.append((x, y0 - z_b, z)); uv.append(face_uv(x, z))
    # rim going back into the recess
    for k in range(N):
        x, y, z = vs[1 + (R - 1) * N + k]
        vs.append((x, y + 3.0, z)); uv.append(face_uv(x, z))
    fs = []
    for k in range(N):
        fs.append((0, 1 + k, 1 + (k + 1) % N))
    for r in range(R - 1):
        for k in range(N):
            i0 = 1 + r * N + k; i1 = 1 + r * N + (k + 1) % N
            fs.append((i0, i0 + N, i1 + N, i1))
    base = 1 + (R - 1) * N
    for k in range(N):
        i0 = base + k; i1 = base + (k + 1) % N
        fs.append((i0, i0 + N, i1 + N, i1))
    return vs, fs, uv

gv, gf, guv = glass_mesh()
glass_mat = bpy.data.materials.new('emi_glass')
glass_mat.use_nodes = True
glass_mat.surface_render_method = 'DITHERED'
glass = mesh_obj('EMI_glass' if GAME else 'glass', gv, gf, glass_mat, root, (0, 0, SZ))   # EMI_glass is a loader contract
bm = bmesh.new(); bm.from_mesh(glass.data)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
# make sure the dome faces the camera (-y)
front = [f for f in bm.faces if abs(f.calc_center_median().y - (-BEZEL_T + 1.2 - BULGE) * S) < 0.5 * S]
if front and front[0].normal.y > 0:
    for f in bm.faces:
        f.normal_flip()
bm.to_mesh(glass.data); bm.free()
uvl = glass.data.uv_layers.new(name='UVMap')
for poly in glass.data.polygons:
    for li in poly.loop_indices:
        vi = glass.data.loops[li].vertex_index
        uvl.data[li].uv = guv[vi]
smooth(glass)

# glass node tree: dark scanlined screen + pink emissive face from the texture alpha
nt = glass_mat.node_tree
nodes, links = nt.nodes, nt.links
bsdf = nodes['Principled BSDF']
bsdf.inputs['Roughness'].default_value = 0.40
bsdf.inputs['Coat Weight'].default_value = 0.08
bsdf.inputs['Coat Roughness'].default_value = 0.18
if 'Specular IOR Level' in bsdf.inputs:
    bsdf.inputs['Specular IOR Level'].default_value = 0.18
tex = nodes.new('ShaderNodeTexImage'); tex.name = 'FACE'
tex.interpolation = 'Closest'
tex.extension = 'CLIP'
if GAME:
    # glTF-portable screen: near-black base, the face atlas straight into Emission. The atlas
    # already carries the dim screen purple behind the pink glyph (face2.py --game), and the race
    # renders with no tone mapping, so the pink ships at strength 1.0 rather than the 2.6 that
    # the AgX studio render wants. The scanlines below do not survive glTF and are left to the game.
    tex.extension = 'EXTEND'
    bsdf.inputs['Base Color'].default_value = rgb(9, 7, 15)
    links.new(tex.outputs['Color'], bsdf.inputs['Emission Color'])
    bsdf.inputs['Emission Strength'].default_value = 1.0
    atlas_img = bpy.data.images.load(ATLAS, check_existing=True)
    atlas_img.colorspace_settings.name = 'sRGB'
    tex.image = atlas_img
else:
    uvn = nodes.new('ShaderNodeUVMap')
    links.new(uvn.outputs['UV'], tex.inputs['Vector'])
    # scanlines from uv.v
    sep = nodes.new('ShaderNodeSeparateXYZ')
    links.new(uvn.outputs['UV'], sep.inputs[0])
    m1 = nodes.new('ShaderNodeMath'); m1.operation = 'MULTIPLY'; m1.inputs[1].default_value = 70.0
    links.new(sep.outputs['Y'], m1.inputs[0])
    m2 = nodes.new('ShaderNodeMath'); m2.operation = 'FRACT'
    links.new(m1.outputs[0], m2.inputs[0])
    m3 = nodes.new('ShaderNodeMath'); m3.operation = 'GREATER_THAN'; m3.inputs[1].default_value = 0.45
    links.new(m2.outputs[0], m3.inputs[0])
    scan = nodes.new('ShaderNodeMapRange'); scan.inputs['From Min'].default_value = 0; scan.inputs['From Max'].default_value = 1
    scan.inputs['To Min'].default_value = 0.72; scan.inputs['To Max'].default_value = 1.0
    links.new(m3.outputs[0], scan.inputs['Value'])
    # base colour: screen dark, pink where the face is
    mixc = nodes.new('ShaderNodeMix'); mixc.data_type = 'RGBA'
    mixc.inputs[6].default_value = rgb(9, 7, 15)   # near-black: screen is self-lit, so the bezel shadow cannot step across it
    mixc.inputs[7].default_value = rgb(*C_PINK)
    links.new(tex.outputs['Alpha'], mixc.inputs['Factor'])
    mulc = nodes.new('ShaderNodeMix'); mulc.data_type = 'RGBA'; mulc.blend_type = 'MULTIPLY'; mulc.inputs['Factor'].default_value = 1.0
    links.new(mixc.outputs[2], mulc.inputs[6])
    sc3 = nodes.new('ShaderNodeCombineColor')
    links.new(scan.outputs[0], sc3.inputs[0]); links.new(scan.outputs[0], sc3.inputs[1]); links.new(scan.outputs[0], sc3.inputs[2])
    links.new(sc3.outputs[0], mulc.inputs[7])
    links.new(mulc.outputs[2], bsdf.inputs['Base Color'])
    # emission: faint purple glow everywhere, strong pink on the face
    mixe = nodes.new('ShaderNodeMix'); mixe.data_type = 'RGBA'
    mixe.inputs[6].default_value = rgb(62, 46, 92)
    mixe.inputs[7].default_value = rgb(*C_PINK)
    links.new(tex.outputs['Alpha'], mixe.inputs['Factor'])
    mule = nodes.new('ShaderNodeMix'); mule.data_type = 'RGBA'; mule.blend_type = 'MULTIPLY'; mule.inputs['Factor'].default_value = 1.0
    links.new(mixe.outputs[2], mule.inputs[6]); links.new(sc3.outputs[0], mule.inputs[7])
    links.new(mule.outputs[2], bsdf.inputs['Emission Color'])
    stre = nodes.new('ShaderNodeMapRange'); stre.inputs['To Min'].default_value = 1.0; stre.inputs['To Max'].default_value = 2.6
    links.new(tex.outputs['Alpha'], stre.inputs['Value'])
    links.new(stre.outputs[0], bsdf.inputs['Emission Strength'])

def set_face(name):
    if GAME:
        return      # the game screen is the fixed atlas; the runtime swaps frames
    p = os.path.join(FACES, name + '.png')
    if not os.path.exists(p):
        p = os.path.join(FACES, 'idle.png')
    img = bpy.data.images.load(p, check_existing=True)
    img.colorspace_settings.name = 'sRGB'
    tex.image = img

# ---------------------------------------------------------------- gold cross button (lower-left of the bezel)
bx, bz = -W / 2 + 12.0, Z0 + 6.5
bpiv = empty('button', (bx, -BEZEL_T + 0.3, bz), root)
b1 = box('btn_h', (6.0, 2.2, 2.0), (0, -0.6, 0), M_GOLD, bpiv)
b2 = box('btn_v', (2.0, 2.2, 6.0), (0, -0.6, 0), M_GOLD, bpiv)
for o in (b1, b2):
    bevel(o, 0.45, segments=3); smooth(o); outline(o, 0.4)

# ---------------------------------------------------------------- antenna: three jointed segments + ball
ANT_Y = 6.5
ANT_L = 3.2
ANT_R = 0.9
ant_pivs = []
parent = root
loc = (0, ANT_Y, Z0 + H - 0.4)
for i in range(3):
    piv = empty('ant%d' % i, loc, parent)
    ant_pivs.append(piv)
    seg = cylinder('antseg%d' % i, ANT_R, ANT_L + 0.2, (0, 0, ANT_L / 2), M_DARK, piv, segs=24)
    outline(seg, 0.35)
    j = sphere('antjoint%d' % i, ANT_R, (0, 0, 0), M_DARK, piv, segs=20)
    outline(j, 0.35)
    parent = piv
    loc = (0, 0, ANT_L)
ball_piv = empty('ballpiv', (0, 0, ANT_L), parent)
ball = sphere('ball', 4.0, (0, 0, 3.3), M_BALL, ball_piv, segs=48)
outline(ball, 0.5)

def pose_antenna(angles, stretch=1.0):
    for piv, (rx, ry) in zip(ant_pivs, angles):
        piv.rotation_euler = (math.radians(rx), math.radians(ry), 0)
    for piv in ant_pivs:
        piv.scale = (1, 1, 1)
    ant_pivs[0].scale = (1, 1, stretch)

# ---------------------------------------------------------------- arms: shoulder pivot -> cylinder -> mitten
ARM_R, ARM_L = 3.2, 14.0
HAND_R = 4.9
SH_Z = Z0 + 21.0
SH_Y = D1 * 0.5
arm_pivs = {}
for side, sx in (('L', -1), ('R', 1)):
    piv = empty('shoulder' + side, (sx * (W / 2 - 1.0), SH_Y, SH_Z), root)
    arm_pivs[side] = piv
    sphere('shoulder_cap' + side, ARM_R * 1.05, (0, 0, 0), M_CASE, piv, segs=24)
    a = cylinder('arm' + side, ARM_R, ARM_L, (0, 0, -ARM_L / 2), M_CASE, piv, segs=36)
    outline(a, 0.45)
    # sock mitten: one rounded block for the four fingers, a fatter rounded block for the thumb
    HW, HT, HL = 6.6, 3.6, 5.6            # wider than the arm, flat like a sock over four fingers
    hand = box('hand' + side, (HW, HT, HL), (0, -0.2, -ARM_L - HL / 2 + 0.4), M_CASE, piv)
    bevel(hand, 1.75, segments=6); smooth(hand); outline(hand, 0.45)
    thumb = box('thumb' + side, (2.4, 2.7, 3.8), (-sx * (HW / 2 + 0.5), -0.7, -ARM_L - 1.0), M_CASE, piv,
                rot=(0, -sx * 32, 0))
    bevel(thumb, 1.0, segments=5); smooth(thumb); outline(thumb, 0.4)

def pose_arms(kind):
    table = {
        'down': (14, 0, 14, 0), 'down_in': (-2, -4, -2, -4), 'out': (82, -6, 82, -6),
        'up': (152, -8, 152, -8), 'wave': (150, -6, 16, 0), 'hip': (26, 0, 10, -6),
    }
    lo, lf, ro, rf = table[kind]
    arm_pivs['L'].rotation_euler = (math.radians(lf), math.radians(lo), 0)
    arm_pivs['R'].rotation_euler = (math.radians(rf), math.radians(-ro), 0)

# ---------------------------------------------------------------- legs + sneakers
LEG_R, LEG_X = 2.4, 9.5
LEG_Y = D1 * 0.5
FOOT_Z = 6.2
for side, sx in (('L', -1), ('R', 1)):
    fp = empty('foot' + side, (sx * LEG_X, LEG_Y, FOOT_Z), root)
    fp.rotation_euler = (0, 0, math.radians(sx * 9))
    # the leg rides the foot pivot, so a foot key swings leg and sneaker together
    leg = cylinder('leg' + side, LEG_R, 8.0, (0, 0, Z0 - 2.5 - FOOT_Z), M_CASE, fp, segs=32)
    outline(leg, 0.45)
    shoe = box('shoe' + side, (6.4, 11.6, 4.6), (0, -2.0, -2.7), M_SHOE, fp)   # sinks 0.3 into the sole
    bevel(shoe, 1.5, segments=6); smooth(shoe); outline(shoe, 0.5)   # squarer sneaker, no toe bump
    # slim sole, barely wider than the upper
    sole = box('sole' + side, (6.5, 11.8, 0.8), (0, -2.0, -5.1), M_SOLE, fp)
    bevel(sole, 0.3, segments=3); smooth(sole); outline(sole, 0.4)
    # laces: two thin cross strips on the vamp and a small bow
    for i, ly in enumerate((-4.3, -5.3)):
        st = box('lace%d' % i + side, (2.8, 0.45, 0.3), (0, ly, -0.05), M_LACE, fp)
        bevel(st, 0.12, segments=2); smooth(st)
    for k, sxk in ((0, -1), (1, 1)):
        loop = box('bow%d' % k + side, (1.3, 0.9, 0.55), (sxk * 0.75, -3.4, 0.3), M_LACE, fp, rot=(0, 0, sxk * 32))
        bevel(loop, 0.28, segments=3); smooth(loop)
    sphere('knot' + side, 0.42, (0, -3.4, 0.38), M_LACE, fp, segs=16)
    cuff = cylinder('cuff' + side, LEG_R * 1.25, 1.4, (0, 0, 0.2), M_DARK, fp, segs=32)

# ---------------------------------------------------------------- floor, lights, camera
def light(name, kind, loc, energy, color, size=None, target=None):
    ld = bpy.data.lights.new(name, kind)
    ld.energy = energy
    ld.color = color
    if size is not None:
        ld.shape = 'SQUARE' if kind == 'AREA' else ld.shape
        ld.size = size
    lo = bpy.data.objects.new(name, ld)
    lo.location = loc
    scene.collection.objects.link(lo)
    if target is not None:
        c = lo.constraints.new('TRACK_TO')
        c.target = target
        c.track_axis = 'TRACK_NEGATIVE_Z'
        c.up_axis = 'UP_Y'
    return lo

def studio():
    # the render set: never part of the game LOD export
    box('floor', (900, 900, 0.5), (0, 0, -0.25), mat('emi_floor', (9, 8, 18), rough=0.55, coat=0.06))
    target = empty('target', (0, D1 * 0.3, 39.0))
    light('key', 'AREA', (-7, -9, 11), 3300, (1.0, 0.86, 0.92), 4, target)
    light('fill', 'AREA', (9, -8, 5), 1000, (0.78, 0.72, 1.0), 6, target)
    light('rim', 'AREA', (4, 9, 9), 1300, (1.0, 0.45, 0.75), 5, target)
    glow = light('glow', 'AREA', (0, (-BEZEL_T - BULGE - 2.5) * S, SZ * S), 22, (1.0, 0.41, 0.71), 3.0)
    glow.rotation_euler = (math.radians(90), 0, 0)   # shine +y, back onto the bezel and hands

    cam_d = bpy.data.cameras.new('cam')
    cam_d.lens = 55
    cam = bpy.data.objects.new('cam', cam_d)
    cam.location = (0, -16.8, (Z0 + H / 2 + 9) * S)
    scene.collection.objects.link(cam)
    c = cam.constraints.new('TRACK_TO'); c.target = target; c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'
    scene.camera = cam
    # close-up overrides (cells): --cam x,y,z --tgt x,y,z --lens N
    if '--cam' in argv:
        cx, cy, cz = [float(v) for v in arg('--cam', '0,0,0').split(',')]
        cam.location = (cx * S, cy * S, cz * S)
    if '--tgt' in argv:
        tx, ty, tz = [float(v) for v in arg('--tgt', '0,0,0').split(',')]
        target.location = (tx * S, ty * S, tz * S)
    cam_d.lens = float(arg('--lens', '55'))

if not GAME:
    studio()

# ---------------------------------------------------------------- poses
POSES = {
    #        face     antenna joints (rx, ry) x3           stretch  arms      root rot (x, y, z deg)  lift
    'idle':  ('idle',  [(6, 0), (4, 0), (2, 0)],            1.00, 'down',    (0, 0, 28),   0.0),
    'glee':  ('glee',  [(-10, 14), (-8, 12), (-4, 8)],      1.05, 'up',      (-3, 0, 28),  1.2),
    'sad':   ('sad',   [(30, 0), (40, 0), (38, 0)],         0.96, 'down_in', (7, 0, 28),   -0.6),
    'shock': ('shock', [(-14, 0), (-2, 0), (0, 0)],         1.14, 'out',     (-6, 0, 28),  1.6),
    'smug':  ('smug',  [(0, 26), (0, 22), (0, 12)],         1.00, 'hip',     (0, -4, 28),  0.0),
}

def apply_pose(name):
    face, ant, stretch, arms, rrot, lift = POSES[name]
    set_face(face)
    pose_antenna(ant, stretch)
    pose_arms(arms)
    root.rotation_euler = [math.radians(a) for a in rrot]
    root.location = (0, 0, lift * S)

def render(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print('RENDERED', path, flush=True)

if MODE == 'game':
    # rest pose for the glb: the idle antenna lean, arms down, no turn (she faces glTF +Z).
    # export_glb.py opens this file, authors the clips on the pivots and writes the glb.
    pose_antenna(POSES['idle'][1], 1.0)
    pose_arms('down')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'emi_game.blend'))
    print('GAME_DONE', flush=True)
elif MODE == 'test':
    apply_pose(arg('--pose', 'idle'))
    render(os.path.join(OUT, 'test_%s.png' % arg('--pose', 'idle')))
elif MODE == 'poses':
    for name in POSES:
        apply_pose(name)
        render(os.path.join(OUT, 'emi_hs_%s.png' % name))
elif MODE == 'turn':
    apply_pose('idle')
    scene.render.resolution_x = scene.render.resolution_y = RES
    bpy.context.preferences.edit.keyframe_new_interpolation_type = 'LINEAR'
    scene.frame_start, scene.frame_end = 1, FRAMES
    root.rotation_euler = (0, 0, math.radians(-30))
    root.keyframe_insert('rotation_euler', frame=1)
    root.rotation_euler = (0, 0, math.radians(330))
    root.keyframe_insert('rotation_euler', frame=FRAMES + 1)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'emi_hs.blend'))
    scene.render.filepath = os.path.join(OUT, 'turn_')
    bpy.ops.render.render(animation=True)
    print('TURN_DONE', flush=True)
print('BUILD_DONE', flush=True)
