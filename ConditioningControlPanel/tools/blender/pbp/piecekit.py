# piecekit.py - the kit under build_pieces.py: colour-carrying surface builders for the Piece by
# Piece men, the silicone and jewellery materials, glTF export and the contact-sheet camera.
# Everything is built in board units (one square = 1.0), Blender Z-up, origin at the centre of the
# base on the ground. The exporter writes +Y up, so Blender +Y becomes glTF -Z: a man's FRONT (the
# side white sees looking down the board) is Blender -Y. Each builder appends one colour per vertex
# it makes, in creation order, and Body.build() turns that list into the COLOR_0 pieces.js reads.
import bpy, bmesh, math, os, sys
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def arg(name, default):
    return argv[argv.index(name) + 1] if name in argv else default

HERE = os.path.dirname(os.path.abspath(__file__))
TAU = 2 * math.pi

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ---------------------------------------------------------------- colour helpers (sRGB hex in, linear out)
def _lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
def rgb(h):
    return Vector((_lin((h >> 16) & 255), _lin((h >> 8) & 255), _lin(h & 255)))
def mix(a, b, t):
    t = min(1.0, max(0.0, t))
    return a * (1 - t) + b * t
def smooth(e0, e1, x):
    if e1 == e0:
        return 1.0 if x >= e1 else 0.0
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)
def fbm(p, octaves=3, scale=1.0):
    """Sum of value noise over octaves, roughly in [-1, 1]. p is a Vector."""
    out, amp, tot = 0.0, 1.0, 0.0
    for i in range(octaves):
        out += amp * noise.noise(p * (scale * (2 ** i)))
        tot += amp
        amp *= 0.5
    return out / tot

# ---------------------------------------------------------------- monotone profile spline
class Profile:
    """r(z) through (z, r) control points with a monotone cubic (Fritsch-Carlson), so a flat base
    disc stays flat and a sharp shoulder does not ring. Outside the points it clamps."""
    def __init__(self, pts):
        self.z = [p[0] for p in pts]
        self.r = [p[1] for p in pts]
        n = len(pts)
        h = [self.z[i + 1] - self.z[i] for i in range(n - 1)]
        d = [(self.r[i + 1] - self.r[i]) / h[i] for i in range(n - 1)]
        m = [0.0] * n
        m[0], m[-1] = d[0], d[-1]
        for i in range(1, n - 1):
            if d[i - 1] * d[i] <= 0:
                m[i] = 0.0
            else:
                w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
                m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
        self.m, self.h = m, h
    def __call__(self, z):
        if z <= self.z[0]:
            return self.r[0]
        if z >= self.z[-1]:
            return self.r[-1]
        i = 0
        while self.z[i + 1] < z:
            i += 1
        h = self.h[i]
        t = (z - self.z[i]) / h
        t2, t3 = t * t, t * t * t
        return ((2 * t3 - 3 * t2 + 1) * self.r[i] + (t3 - 2 * t2 + t) * h * self.m[i]
                + (-2 * t3 + 3 * t2) * self.r[i + 1] + (t3 - t2) * h * self.m[i + 1])

# ---------------------------------------------------------------- detail functions (radial displacement)
def ribs(z, period, amp, z0, z1, feather=0.06):
    """Horizontal ribs: amp * a raised cosine along z, faded in over feather at both ends."""
    w = smooth(z0, z0 + feather, z) * (1 - smooth(z1 - feather, z1, z))
    return amp * 0.5 * (1 - math.cos(TAU * (z - z0) / period)) * w

def helix(th, z, turns_per_unit, count, amp, z0, z1, power=3.0, phase=0.0, feather=0.08):
    """Spiral ridges: `count` ridges winding turns_per_unit about the axis, sharpened by power."""
    w = smooth(z0, z0 + feather, z) * (1 - smooth(z1 - feather, z1, z))
    s = math.sin(count * th - TAU * turns_per_unit * z + phase)
    return amp * (max(s, 0.0) ** power) * w

def angdist(a, b):
    d = (a - b) % TAU
    return min(d, TAU - d)

def bumps(th, z, r, spots, cup=0.0):
    """Gaussian bumps on the surface. spots = [(theta, z, size, amp)]; cup > 0 dents the centre so a
    bump reads as a sucker instead of a bead."""
    out = 0.0
    for tc, zc, size, amp in spots:
        dz = z - zc
        if abs(dz) > size * 3:
            continue
        da = angdist(th, tc) * r
        d2 = da * da + dz * dz
        out += amp * (math.exp(-d2 / (2 * size * size)) - cup * math.exp(-d2 / (2 * (size / 2.6) ** 2)))
    return out

# ---------------------------------------------------------------- body: one mesh, one colour per vertex
class Body:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.cols = []

    def _v(self, co, col):
        v = self.bm.verts.new(co)
        self.cols.append(col)
        return v

    def surface(self, height, radius, colour, nz=120, nseg=64, centre=None, top_pole=True):
        """A closed lathe about Z from z = 0 to height. radius(t, th) and colour(t, th, r, co) are
        sampled on a nz x nseg grid; centre(t) may offset a ring in XY for a lean. Both ends close
        with a pole, so the shell is manifold and recalc_face_normals can orient it."""
        rings = []
        last = nz - 1 if top_pole else nz
        for i in range(last + 1):
            t = i / nz
            z = t * height
            c = centre(t) if centre else Vector((0, 0))
            ring = []
            for k in range(nseg):
                th = TAU * k / nseg
                r = max(radius(t, th), 1e-4)
                co = Vector((c.x + r * math.cos(th), c.y + r * math.sin(th), z))
                ring.append(self._v(co, colour(t, th, r, co)))
            rings.append(ring)
        for i in range(len(rings) - 1):
            a, b = rings[i], rings[i + 1]
            for k in range(nseg):
                k1 = (k + 1) % nseg
                self.bm.faces.new((a[k], a[k1], b[k1], b[k]))
        c0 = centre(0) if centre else Vector((0, 0))
        p0 = self._v((c0.x, c0.y, 0.0), colour(0, 0, 0, Vector((c0.x, c0.y, 0))))
        for k in range(nseg):
            self.bm.faces.new((rings[0][(k + 1) % nseg], rings[0][k], p0))
        if top_pole:
            c1 = centre(1) if centre else Vector((0, 0))
            p1 = self._v((c1.x, c1.y, height), colour(1, 0, 0, Vector((c1.x, c1.y, height))))
            top = rings[-1]
            for k in range(nseg):
                self.bm.faces.new((top[k], top[(k + 1) % nseg], p1))
        return self

    def sphere(self, centre, r, colour, u=32, v=16, scale=(1, 1, 1), bump=None):
        """UV sphere. colour(co, n) gets the world position and the unit normal; bump(n) can push a
        vertex along its normal for a lumpy ball."""
        c = Vector(centre)
        def at(n):
            rr = r + (bump(n) if bump else 0.0)
            return c + Vector((n.x * rr * scale[0], n.y * rr * scale[1], n.z * rr * scale[2]))
        rings = []
        for j in range(1, v):
            ph = math.pi * j / v
            ring = []
            for i in range(u):
                th = TAU * i / u
                n = Vector((math.sin(ph) * math.cos(th), math.sin(ph) * math.sin(th), math.cos(ph)))
                co = at(n)
                ring.append(self._v(co, colour(co, n)))
            rings.append(ring)
        nt, nb = Vector((0, 0, 1)), Vector((0, 0, -1))
        top = self._v(at(nt), colour(at(nt), nt))
        bot = self._v(at(nb), colour(at(nb), nb))
        for i in range(u):
            i1 = (i + 1) % u
            self.bm.faces.new((top, rings[0][i], rings[0][i1]))
            for j in range(len(rings) - 1):
                self.bm.faces.new((rings[j][i], rings[j + 1][i], rings[j + 1][i1], rings[j][i1]))
            self.bm.faces.new((rings[-1][i1], rings[-1][i], bot))
        return self

    def torus(self, centre, R, r, colour, segs=48, sides=12):
        c = Vector(centre)
        rings = []
        for i in range(segs):
            a = TAU * i / segs
            ca, sa = math.cos(a), math.sin(a)
            ring = []
            for j in range(sides):
                b = TAU * j / sides
                rr = R + r * math.cos(b)
                co = c + Vector((rr * ca, rr * sa, r * math.sin(b)))
                ring.append(self._v(co, colour(co, Vector((ca * math.cos(b), sa * math.cos(b), math.sin(b))))))
            rings.append(ring)
        for i in range(segs):
            a, b = rings[i], rings[(i + 1) % segs]
            for j in range(sides):
                self.bm.faces.new((a[j], b[j], b[(j + 1) % sides], a[(j + 1) % sides]))
        return self

    def cone(self, base, tip, r, colour, segs=16, r_tip=0.0):
        """A closed cone (r_tip = 0) or frustum from the base point to the tip point."""
        b, t = Vector(base), Vector(tip)
        axis = (t - b).normalized()
        u = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
        u = (u - axis * u.dot(axis)).normalized()
        w = axis.cross(u)
        ring0, ring1 = [], []
        for i in range(segs):
            a = TAU * i / segs
            d = u * math.cos(a) + w * math.sin(a)
            ring0.append(self._v(b + d * r, colour(b + d * r, d)))
            if r_tip > 0:
                ring1.append(self._v(t + d * r_tip, colour(t + d * r_tip, d)))
        pb = self._v(b, colour(b, -axis))
        pt = self._v(t, colour(t, axis))
        for i in range(segs):
            i1 = (i + 1) % segs
            self.bm.faces.new((ring0[i1], ring0[i], pb))
            if r_tip > 0:
                self.bm.faces.new((ring0[i], ring0[i1], ring1[i1], ring1[i]))
                self.bm.faces.new((ring1[i], ring1[i1], pt))
            else:
                self.bm.faces.new((ring0[i], ring0[i1], pt))
        return self

    def build(self, material, loc=(0, 0, 0)):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        for f in bm.faces:
            f.smooth = True
        bm.verts.index_update()
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        ca = me.color_attributes.new(name='Color', type='FLOAT_COLOR', domain='POINT')
        for i, c in enumerate(self.cols):
            ca.data[i].color = (c.x, c.y, c.z, 1.0)
        me.color_attributes.active_color = ca
        me.color_attributes.render_color_index = 0
        me.materials.append(material)
        ob = bpy.data.objects.new(self.name, me)
        scene.collection.objects.link(ob)
        ob.location = loc
        bm.free()
        self.ob = ob
        return ob

# ---------------------------------------------------------------- materials
def silicone(name, coat_tint=None):
    """The silicone body: base colour from the vertex colours, a hard clearcoat. pieces.js builds its
    own material in the game, so what matters here is that the exporter writes COLOR_0 and the
    KHR_materials_clearcoat block the first-pass files carried."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nodes, links = m.node_tree.nodes, m.node_tree.links
    b = nodes['Principled BSDF']
    vc = nodes.new('ShaderNodeVertexColor')
    vc.layer_name = 'Color'
    links.new(vc.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.35
    b.inputs['Metallic'].default_value = 0.0
    b.inputs['Coat Weight'].default_value = 0.6
    b.inputs['Coat Roughness'].default_value = 0.12
    if coat_tint is not None:
        t = rgb(coat_tint)
        b.inputs['Coat Tint'].default_value = (t.x, t.y, t.z, 1.0)
    return m

def metal(name, h, rough):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    c = rgb(h)
    b.inputs['Base Color'].default_value = (c.x, c.y, c.z, 1.0)
    b.inputs['Metallic'].default_value = 1.0
    b.inputs['Roughness'].default_value = rough
    return m

# ---------------------------------------------------------------- export
def export_glb(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    kw = dict(filepath=path, export_format='GLB', export_apply=True, export_yup=True, use_selection=True,
              export_animations=False, export_cameras=False, export_lights=False, export_image_format='NONE',
              export_texcoords=False, export_skins=False, export_morph=False, export_extras=False)
    try:
        bpy.ops.export_scene.gltf(**kw, export_vertex_color='ACTIVE', export_all_vertex_colors=False)
    except TypeError:
        bpy.ops.export_scene.gltf(**kw)
    return os.path.getsize(path)

# ---------------------------------------------------------------- contact sheet
def sheet(path, cols, rows, pitch=1.0, res=(1800, 900)):
    """Workbench, studio light, vertex colours, backface culling ON so an inside-out shell shows up
    as a hollow man instead of hiding behind two-sided shading."""
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'VERTEX'
    sh.show_backface_culling = True
    sh.show_specular_highlight = True
    scene.render.resolution_x, scene.render.resolution_y = res
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = cols * pitch + 0.6
    cx = (cols - 1) * pitch / 2
    cy = (rows - 1) * 2.2 / 2
    cam.location = (cx, cy - 8.0, 6.0)
    cam.rotation_euler = (math.radians(54), 0, 0)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
