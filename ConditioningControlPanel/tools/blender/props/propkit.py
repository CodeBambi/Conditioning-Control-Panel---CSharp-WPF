# propkit.py - the kit under build_props.py: scene reset, the EMI palette, bmesh primitives with
# one-segment bevels, node assembly (base mesh + inverted-hull outline child), glTF export and the
# pixel render recipe copied from hs/build_hs.py (copied on purpose: that file builds a scene on
# import and belongs to another agent).
# Everything is built in metres, Blender Z-up, the prop FRONT facing -Y (the glTF exporter turns
# -Y into +Z forward with export_yup). Origins sit at the base centre on the ground.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def arg(name, default):
    return argv[argv.index(name) + 1] if name in argv else default

HERE = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ---------------------------------------------------------------- palette (sRGB 8-bit, EMI family)
def lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
def rgb(h, a=1.0):
    return (lin((h >> 16) & 255), lin((h >> 8) & 255), lin(h & 255), a)

# name: (hex, roughness, metallic, emission hex or None, emission strength)
PALETTE = {
    'cream':   (0xF6E7C8, 0.40, 0.0, None, 0),
    'pink':    (0xFF69B4, 0.35, 0.0, 0xFF69B4, 0.35),
    'blush':   (0xFFB6D9, 0.45, 0.0, None, 0),
    'magenta': (0xE23C9C, 0.40, 0.0, None, 0),
    'gold':    (0xDBA830, 0.30, 0.7, None, 0),
    'navy':    (0x252542, 0.55, 0.0, None, 0),
    'ink':     (0x1B1A33, 0.60, 0.0, None, 0),
    'white':   (0xF8F4FF, 0.55, 0.0, None, 0),
    'wood':    (0x8A5A3A, 0.60, 0.0, None, 0),
    'shelf':   (0xC9B79A, 0.60, 0.0, None, 0),
    'mint':    (0xBFEBD8, 0.45, 0.0, None, 0),
    'hedge':   (0x3F9A5A, 0.65, 0.0, None, 0),
    'red':     (0xFF4D6D, 0.45, 0.0, None, 0),
    'yellow':  (0xFFD23F, 0.45, 0.0, None, 0),
    'blue':    (0x3A86FF, 0.45, 0.0, None, 0),
    'chip':    (0xA3122E, 0.45, 0.0, None, 0),
    'teal':    (0x1FA9B5, 0.50, 0.0, None, 0),
    'kelp':    (0x27B8A0, 0.50, 0.0, None, 0),
    'aqua':    (0x5BE7D8, 0.35, 0.0, None, 0),
    'silver':  (0xDDE3F0, 0.25, 0.2, None, 0),
    'steel':   (0x6D7278, 0.45, 0.4, None, 0),
    'slate':   (0x4A4F66, 0.60, 0.0, None, 0),
    'grey':    (0x9AA0A6, 0.50, 0.2, None, 0),
    'maroon':  (0x7A0F2B, 0.55, 0.0, None, 0),
    'bone':    (0xE9DCC0, 0.50, 0.0, None, 0),
    'bag':     (0xBFD7E0, 0.35, 0.0, None, 0),
    'flame':   (0xFFD696, 0.30, 0.0, 0xFFB347, 1.6),
    'boost_base':  (0x0B2A30, 0.55, 0.0, None, 0),
    'boost_strip': (0x5BE7D8, 0.30, 0.0, 0x5BE7D8, 1.8),
    'outline': (0x1B1A33, 1.00, 0.0, 0x1B1A33, 1.0),
}
MAT_NAMES = list(PALETTE.keys())
MAT_ID = {n: i for i, n in enumerate(MAT_NAMES)}
_mats = {}

def M(name):
    """The Blender material for a palette entry, made once. Flat Principled colour, no textures."""
    if name in _mats:
        return _mats[name]
    h, rough, metal, emit, es = PALETTE[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = rgb(h)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    if emit is not None:
        b.inputs['Emission Color'].default_value = rgb(emit)
        b.inputs['Emission Strength'].default_value = es
    if name == 'outline':
        m.use_backface_culling = True
        if 'Specular IOR Level' in b.inputs:
            b.inputs['Specular IOR Level'].default_value = 0.0
    _mats[name] = m
    return m

# ---------------------------------------------------------------- bmesh primitives
def _xf(loc, rot=(0, 0, 0), scale=None):
    m = Matrix.Translation(Vector(loc)) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4()
    if scale is not None:
        m = m @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
    return m

def finish(bm, mat, loc=(0, 0, 0), rot=(0, 0, 0), smooth=False, sharp=40.0, matrix=None):
    """Recalc normals, tag the material layer, set smooth/sharp, move into place."""
    lay = bm.faces.layers.int.get('mat') or bm.faces.layers.int.new('mat')
    if mat is not None:
        gid = MAT_ID[mat]
        for f in bm.faces:
            f[lay] = gid
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    for f in bm.faces:
        f.smooth = smooth
    if smooth:
        lim = math.radians(sharp)
        for e in bm.edges:
            if e.is_manifold and e.calc_face_angle(0.0) > lim:
                e.smooth = False
    bmesh.ops.transform(bm, matrix=matrix if matrix is not None else _xf(loc, rot), verts=bm.verts)
    return bm

def box(size, loc=(0, 0, 0), mat='cream', rot=(0, 0, 0), bevel=0.0, drop=None, matrix=None):
    """Axis box. bevel = one-segment chamfer width. drop = local axis whose face is removed (plates)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bm.normal_update()
    if drop is not None:
        d = Vector(drop)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.dot(d) > 0.5], context='FACES')
    elif bevel > 0:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, offset_type='OFFSET', segments=1,
                        profile=0.5, affect='EDGES', clamp_overlap=True)
    return finish(bm, mat, loc, rot, matrix=matrix)

def cyl(r1, r2, h, loc=(0, 0, 0), mat='cream', rot=(0, 0, 0), segs=8, caps=True, smooth=True, cap_mat=None):
    """Cylinder/cone along local Z, centred at loc. cap_mat colours the top cap (tea in a cup)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segs, radius1=r1, radius2=max(r2, 0.0), depth=h)
    finish(bm, mat, smooth=smooth)
    if cap_mat is not None:
        lay = bm.faces.layers.int['mat']
        for f in bm.faces:
            if f.normal.z > 0.9 and len(f.verts) > 4:
                f[lay] = MAT_ID[cap_mat]
    bmesh.ops.transform(bm, matrix=_xf(loc, rot), verts=bm.verts)
    return bm

def sphere(r, loc=(0, 0, 0), mat='cream', u=8, v=4, scale=(1, 1, 1)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    return finish(bm, mat, smooth=True, sharp=80, matrix=_xf(loc, (0, 0, 0), scale))

def torus(R, r, loc=(0, 0, 0), mat='cream', rot=(0, 0, 0), segs=16, sides=6):
    """Ring in the local XY plane (axis Z)."""
    bm = bmesh.new()
    rings = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        ca, sa = math.cos(a), math.sin(a)
        ring = []
        for j in range(sides):
            b = 2 * math.pi * j / sides
            rr = R + r * math.cos(b)
            ring.append(bm.verts.new((rr * ca, rr * sa, r * math.sin(b))))
        rings.append(ring)
    for i in range(segs):
        a, b = rings[i], rings[(i + 1) % segs]
        for j in range(sides):
            bm.faces.new((a[j], b[j], b[(j + 1) % sides], a[(j + 1) % sides]))
    return finish(bm, mat, loc, rot, smooth=True, sharp=80)

def lathe(prof, segs=24, loc=(0, 0, 0), mat='cream', rot=(0, 0, 0), smooth=True, sharp=40.0):
    """Spin a profile of (r, z, mat) about Z. Each entry's mat colours the band that FOLLOWS it;
    r < 1e-4 makes a pole. Closed at both ends when both ends are poles."""
    bm = bmesh.new()
    lay = bm.faces.layers.int.new('mat')
    rings = []
    for r, z, _m in prof:
        if r < 1e-4:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z)) for k in range(segs)])
    for i in range(len(prof) - 1):
        a, b = rings[i], rings[i + 1]
        gid = MAT_ID[prof[i][2] or mat]
        for k in range(segs):
            k1 = (k + 1) % segs
            if len(a) == 1:
                f = bm.faces.new((a[0], b[k], b[k1]))
            elif len(b) == 1:
                f = bm.faces.new((a[k], a[k1], b[0]))
            else:
                f = bm.faces.new((a[k], a[k1], b[k1], b[k]))
            f[lay] = gid
    return finish(bm, None, loc, rot, smooth=smooth, sharp=sharp)

def tube(points, r, mat='cream', sides=6, caps=True, loc=(0, 0, 0), rot=(0, 0, 0)):
    """Sweep a circle along a polyline (parallel-transport frames). Handles and coils."""
    pts = [Vector(p) for p in points]
    n = len(pts)
    bm = bmesh.new()
    tangents = []
    for i in range(n):
        a = pts[max(i - 1, 0)]; b = pts[min(i + 1, n - 1)]
        tangents.append((b - a).normalized())
    nrm = Vector((0, 0, 1)) if abs(tangents[0].z) < 0.9 else Vector((1, 0, 0))
    nrm = (nrm - tangents[0] * nrm.dot(tangents[0])).normalized()
    rings = []
    for i in range(n):
        t = tangents[i]
        nrm = (nrm - t * nrm.dot(t)).normalized()
        bn = t.cross(nrm)
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides
            ring.append(bm.verts.new(pts[i] + nrm * (r * math.cos(a)) + bn * (r * math.sin(a))))
        rings.append(ring)
    for i in range(n - 1):
        a, b = rings[i], rings[i + 1]
        for j in range(sides):
            bm.faces.new((a[j], a[(j + 1) % sides], b[(j + 1) % sides], b[j]))
    if caps:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    return finish(bm, mat, loc, rot, smooth=True, sharp=60)

def bezier(p0, p1, p2, p3, n=14):
    p0, p1, p2, p3 = (Vector(p) for p in (p0, p1, p2, p3))
    out = []
    for i in range(n + 1):
        t = i / n; u = 1 - t
        out.append(p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t))
    return out

def helix(r, z0, z1, turns, per_turn=7):
    n = int(turns * per_turn)
    return [((r * math.cos(2 * math.pi * turns * i / n)), (r * math.sin(2 * math.pi * turns * i / n)), z0 + (z1 - z0) * i / n) for i in range(n + 1)]

def fluted(r_out, r_in, n, h, loc=(0, 0, 0), mat='bone', rot=(0, 0, 0)):
    """A star-section column: 2n-gon with alternating radii, flat shaded so the flutes read."""
    bm = bmesh.new()
    bot, top = [], []
    for i in range(2 * n):
        a = 2 * math.pi * i / (2 * n)
        rr = r_out if i % 2 == 0 else r_in
        bot.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), -h / 2)))
        top.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), h / 2)))
    for i in range(2 * n):
        j = (i + 1) % (2 * n)
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bm.faces.new(list(reversed(bot)))
    bm.faces.new(top)
    return finish(bm, mat, loc, rot)

def prism(poly, depth, loc=(0, 0, 0), mat='cream', rot=(0, 0, 0)):
    """Extrude a 2D polygon (x, z) along local Y by depth (front face at -depth/2). Banners, gables."""
    bm = bmesh.new()
    f = [bm.verts.new((x, -depth / 2, z)) for x, z in poly]
    b = [bm.verts.new((x, depth / 2, z)) for x, z in poly]
    bm.faces.new(f); bm.faces.new(list(reversed(b)))
    for i in range(len(poly)):
        j = (i + 1) % len(poly)
        bm.faces.new((f[i], b[i], b[j], f[j]))
    return finish(bm, mat, loc, rot)

def quad(size, mat='cream', matrix=None, loc=(0, 0, 0), rot=(0, 0, 0)):
    """A single quad in the local XY plane facing +Z (decals: letters, pips, labels)."""
    bm = bmesh.new()
    w, h = size[0] / 2, size[1] / 2
    vs = [bm.verts.new(p) for p in ((-w, -h, 0), (w, -h, 0), (w, h, 0), (-w, h, 0))]
    bm.faces.new(vs)
    return finish(bm, mat, loc, rot, matrix=matrix)

def flag(a, b, c, mat='pink'):
    """A two-sided triangle (bunting)."""
    bm = bmesh.new()
    va, vb, vc = (bm.verts.new(Vector(p)) for p in (a, b, c))
    bm.faces.new((va, vb, vc))
    wa, wb, wc = (bm.verts.new(Vector(p)) for p in (a, b, c))
    bm.faces.new((wc, wb, wa))
    lay = bm.faces.layers.int.new('mat')
    for f in bm.faces:
        f[lay] = MAT_ID[mat]
    bm.normal_update()
    return bm

# ---------------------------------------------------------------- pixel glyphs as geometry
GLYPHS = {
    '?': ['.####.', '##..##', '....##', '...##.', '..##..', '......', '..##..', '..##..'],
    'E': ['####', '#...', '###.', '#...', '####'],
    'M': ['#...#', '##.##', '#.#.#', '#...#', '#...#'],
    'I': ['###', '.#.', '.#.', '.#.', '###'],
}

def glyph_rects(rows):
    """Greedy rectangle cover of a bitmap: (x, y, w, h) in cells, y down from the top row."""
    h, w = len(rows), len(rows[0])
    seen = [[False] * w for _ in range(h)]
    out = []
    for y in range(h):
        for x in range(w):
            if rows[y][x] != '#' or seen[y][x]:
                continue
            ww = 0
            while x + ww < w and rows[y][x + ww] == '#' and not seen[y][x + ww]:
                ww += 1
            hh = 1
            while y + hh < h and all(rows[y + hh][x + k] == '#' and not seen[y + hh][x + k] for k in range(ww)):
                hh += 1
            for yy in range(y, y + hh):
                for xx in range(x, x + ww):
                    seen[yy][xx] = True
            out.append((x, y, ww, hh))
    return out

def face_basis(n):
    """(u, v) for a face normal so u reads left-to-right and v up for someone looking at it."""
    n = Vector(n)
    v = Vector((0, 0, 1)) if abs(n.z) < 0.5 else Vector((0, -n.z, 0))
    u = v.cross(n)
    return u.normalized(), v.normalized()

def emboss(node, glyph, centre, normal, cell, depth, mat, flat=False):
    """Raise a glyph off a face as plates (5 faces each) or, flat=True, as single quads."""
    rows = GLYPHS[glyph]
    gw, gh = len(rows[0]), len(rows)
    n = Vector(normal).normalized()
    u, v = face_basis(n)
    c = Vector(centre)
    for x, y, w, h in glyph_rects(rows):
        uo = (x + w / 2 - gw / 2) * cell
        vo = (gh / 2 - (y + h / 2)) * cell
        basis = Matrix((u, v, n)).transposed().to_4x4()
        if flat:
            m = Matrix.Translation(c + u * uo + v * vo + n * depth) @ basis
            node.add(quad((w * cell, h * cell), mat=mat, matrix=m), outline=False)
        else:
            m = Matrix.Translation(c + u * uo + v * vo + n * (depth / 2)) @ basis
            node.add(box((w * cell, h * cell, depth), mat=mat, drop=(0, 0, -1), matrix=m), outline=False)

# ---------------------------------------------------------------- node assembly
NODES = []

class Node:
    """One contract node: parts merge into a single mesh; the outline hull is a child mesh."""
    def __init__(self, name, thick=None):
        self.name = name
        self.bm = bmesh.new()
        self.ol = bmesh.new()
        self.mats = []
        self.thick = thick
        self.last = None
        NODES.append(self)

    def add(self, bm, outline=True):
        lay = bm.faces.layers.int['mat']
        for f in bm.faces:
            nm = MAT_NAMES[f[lay]]
            if nm not in self.mats:
                self.mats.append(nm)
            f.material_index = self.mats.index(nm)
        tmp = bpy.data.meshes.new('tmp')
        bm.to_mesh(tmp)
        self.bm.from_mesh(tmp)
        if outline:
            self.ol.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
        bm.free()
        return self

    def bbox(self):
        vs = [v.co for v in self.bm.verts]
        lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        return lo, hi

    def tris(self):
        return sum(len(f.verts) - 2 for f in self.bm.faces) + sum(len(f.verts) - 2 for f in self.ol.faces)

    def build(self, loc=(0, 0, 0)):
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        for nm in self.mats:
            me.materials.append(M(nm))
        ob = bpy.data.objects.new(self.name, me)
        scene.collection.objects.link(ob)
        ob.location = loc
        lo, hi = self.bbox()
        diag = (hi - lo).length
        t = self.thick if self.thick is not None else min(0.05, max(0.008, 0.012 * diag))
        self.ol.normal_update()
        for v in self.ol.verts:
            v.co += v.normal * t
        bmesh.ops.reverse_faces(self.ol, faces=self.ol.faces)
        for f in self.ol.faces:
            f.material_index = 0
            f.smooth = True
        me2 = bpy.data.meshes.new(self.name + '_outline')
        self.ol.to_mesh(me2)
        me2.materials.append(M('outline'))
        ob2 = bpy.data.objects.new(self.name + '_outline', me2)
        scene.collection.objects.link(ob2)
        ob2.parent = ob
        self.ob, self.ob_ol, self.thick_used = ob, ob2, t
        return ob

# ---------------------------------------------------------------- export
def export_glb(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_apply=True, export_yup=True,
                              export_animations=False, use_selection=False, export_cameras=False,
                              export_lights=False, export_image_format='NONE', export_texcoords=False,
                              export_skins=False, export_morph=False, export_extras=False)
    return os.path.getsize(path)

# ---------------------------------------------------------------- render recipe (from build_hs.py)
_rig = {}

def render_res(res, pix):
    """Pixel look: render at res // pix and let sheet.py blow it back up with NEAREST."""
    if pix > 1:
        res = res // pix
        scene.render.filter_size = 0.6
    scene.render.resolution_x = scene.render.resolution_y = res
    scene.render.resolution_percentage = 100

def render_setup(res, pix):
    render_res(res, pix)
    scene.render.engine = 'BLENDER_EEVEE'
    scene.eevee.taa_render_samples = 64
    scene.eevee.use_shadows = True
    scene.eevee.use_raytracing = True
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
    fm = bpy.data.materials.new('floor')
    fm.use_nodes = True
    b = fm.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = rgb(0x090812)
    b.inputs['Roughness'].default_value = 0.55
    fl = bpy.data.meshes.new('floor')
    bmf = bmesh.new(); bmesh.ops.create_cube(bmf, size=1.0)
    bmesh.ops.scale(bmf, vec=Vector((900, 900, 0.5)), verts=bmf.verts)
    bmesh.ops.transform(bmf, matrix=Matrix.Translation((0, 0, -0.25)), verts=bmf.verts)
    bmf.to_mesh(fl); bmf.free()
    fl.materials.append(fm)
    fo = bpy.data.objects.new('floor', fl); scene.collection.objects.link(fo)
    tgt = bpy.data.objects.new('target', None); scene.collection.objects.link(tgt)
    def light(name, energy, color, size):
        ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy; ld.color = color; ld.shape = 'SQUARE'; ld.size = size
        lo = bpy.data.objects.new(name, ld); scene.collection.objects.link(lo)
        c = lo.constraints.new('TRACK_TO'); c.target = tgt; c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'
        return lo
    cam_d = bpy.data.cameras.new('cam'); cam_d.lens = 55
    cam = bpy.data.objects.new('cam', cam_d); scene.collection.objects.link(cam)
    c = cam.constraints.new('TRACK_TO'); c.target = tgt; c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'
    scene.camera = cam
    _rig.update(tgt=tgt, cam=cam, floor=fo,
                key=light('key', 3300, (1.0, 0.86, 0.92), 4), fill=light('fill', 1000, (0.78, 0.72, 1.0), 6),
                rim=light('rim', 1300, (1.0, 0.45, 0.75), 5))
    _rig['base'] = {'key': (3300, 4), 'fill': (1000, 6), 'rim': (1300, 5)}

def frame(lo, hi, yaw=35.0, fill=1.0):
    """Aim the EMI rig at a bbox: offsets are the build_hs recipe scaled by the bbox radius
    (EMI reads as R = 4.4 m) and swung round by yaw degrees for the three-quarter view."""
    c = (lo + hi) / 2
    R = max((hi - lo).length / 2, 0.15) / fill
    k = R / 4.4
    flat = (hi.z - lo.z) < 0.3 * max(hi.x - lo.x, hi.y - lo.y)
    rz = Matrix.Rotation(math.radians(yaw), 4, 'Z')
    def place(ob, off):
        ob.location = c + rz @ (Vector(off) * k)
    _rig['tgt'].location = c
    place(_rig['cam'], (0, -14.0, 10.0) if flat else (0, -17.2, 4.6))
    place(_rig['key'], (-7, -9.4, 7.1)); place(_rig['fill'], (9, -8.4, 1.1)); place(_rig['rim'], (4, 8.6, 5.1))
    for nm in ('key', 'fill', 'rim'):
        e, s = _rig['base'][nm]
        _rig[nm].data.energy = e * k * k
        _rig[nm].data.size = s * k

def render(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print('RENDERED', path, flush=True)
