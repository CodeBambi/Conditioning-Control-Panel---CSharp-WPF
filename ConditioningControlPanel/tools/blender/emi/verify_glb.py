# verify_glb.py - re-import race/assets/emi.glb into a fresh Blender and prove the contract:
# node names and parents, animation names and lengths, triangle count after export, bounding
# box, the glass UV frame and its atlas. Then the check renders: a turntable of the game LOD
# with idle playing, and one still per clip at its most expressive frame, each with a different
# atlas frame slid in the way the runtime does it (map.offset.x = i / 5).
#   blender -b --factory-startup -P verify_glb.py -- --glb X.glb --out DIR [--res 768] [--pix 5]
#       [--frames 48] [--still 1024] [--stillpix 6] [--fps 30] [--norender 1]
# Low-res frames come out at res // pix; run pixup.py on DIR afterwards (run_game.cmd does).
import bpy, math, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default
GLB = arg('--glb')
OUT = arg('--out', os.path.join(os.path.dirname(os.path.abspath(GLB or '.')), 'verify'))
RES = int(arg('--res', '768')); PIX = int(arg('--pix', '5')); FRAMES = int(arg('--frames', '48'))
STILL = int(arg('--still', '1024')); STILLPIX = int(arg('--stillpix', '6'))
FPS = int(arg('--fps', '30')); NORENDER = arg('--norender', '0') == '1'
CAM = arg('--cam', 'menu')        # stills: 'menu' = the race's character-select camera, 'studio' = the approved angle
if not GLB:
    raise SystemExit('usage: verify_glb.py -- --glb X.glb --out DIR')
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS
scene.render.fps_base = 1.0
bpy.ops.import_scene.gltf(filepath=GLB)

objs = list(bpy.data.objects)
print('GLB_BYTES', os.path.getsize(GLB))
print('NODES', len(objs))
for o in sorted(objs, key=lambda o: o.name):
    print('  node %-14s %-6s parent=%s' % (o.name, o.type, o.parent.name if o.parent else '-'))

CONTRACT = ['EMI_root', 'EMI_case', 'EMI_glass', 'ant0', 'ant1', 'ant2', 'ballpiv',
            'shoulderL', 'shoulderR', 'footL', 'footR', 'button']
missing = [n for n in CONTRACT if n not in bpy.data.objects]
print('CONTRACT_NODES', 'OK' if not missing else 'MISSING %s' % missing)
print('CONTRACT_MATERIAL_outline', 'OK' if 'outline' in bpy.data.materials else 'MISSING',
      sorted(m.name for m in bpy.data.materials))
STRAY = [n for n in ('cutter', 'floor', 'target', 'cam', 'mcam', 'mtarget', 'key', 'fill', 'rim', 'glow') if n in bpy.data.objects]
print('STRAY_NODES', STRAY or 'none')

def chain(o):
    names = []
    while o.parent:
        o = o.parent
        names.append(o.name)
    return names
for n in ('armL', 'handL', 'thumbR', 'legL', 'shoeR', 'soleL', 'antseg2', 'antjoint0', 'ball', 'btn_h', 'EMI_glass', 'EMI_case'):
    o = bpy.data.objects.get(n)
    print('  under %-10s %s' % (n, ' > '.join(chain(o)) if o else 'ABSENT'))

for a in bpy.data.actions:
    fr = a.frame_range
    print('ANIM %-6s frames %g..%g  %.3f s  slots %d' % (a.name, fr[0], fr[1], (fr[1] - fr[0]) / FPS, len(a.slots)))
tracks = {}
for o in objs:
    if o.animation_data:
        for t in o.animation_data.nla_tracks:
            tracks.setdefault(t.name, []).append(o.name)
for t, names in sorted(tracks.items()):
    print('TRACK %-6s on %d nodes: %s' % (t, len(names), ' '.join(sorted(names))))

deps = bpy.context.evaluated_depsgraph_get()
tris = 0
mn, mx = Vector((1e9,) * 3), Vector((-1e9,) * 3)
per = []
for o in objs:
    if o.type != 'MESH':
        continue
    ev = o.evaluated_get(deps)
    me = ev.to_mesh()
    t = sum(len(p.vertices) - 2 for p in me.polygons)
    tris += t
    per.append((t, o.name))
    for v in me.vertices:
        w = ev.matrix_world @ v.co
        mn = Vector((min(mn.x, w.x), min(mn.y, w.y), min(mn.z, w.z)))
        mx = Vector((max(mx.x, w.x), max(mx.y, w.y), max(mx.z, w.z)))
    ev.to_mesh_clear()
print('TRIS', tris)
for t, n in sorted(per, reverse=True)[:10]:
    print('  tris %-12s %d' % (n, t))
r4 = lambda v: tuple(round(c, 4) for c in v)
print('BBOX min', r4(mn), 'max', r4(mx), 'size', r4(mx - mn), '(Blender Z up; glTF Y = this Z)')
case = bpy.data.objects.get('EMI_case')
if case:
    ev = case.evaluated_get(deps); me = ev.to_mesh()
    top = max((ev.matrix_world @ v.co).z for v in me.vertices)
    ev.to_mesh_clear()
    print('CASE_TOP_Z', round(top, 4), '(target 1.0 m above the sole)')

glass = bpy.data.objects.get('EMI_glass')
if glass and glass.data.uv_layers.active:
    us = [d.uv.x for d in glass.data.uv_layers.active.data]
    vs = [d.uv.y for d in glass.data.uv_layers.active.data]
    print('GLASS_UV U %.4f..%.4f V %.4f..%.4f (want U inside [0, 0.2])' % (min(us), max(us), min(vs), max(vs)))
    mat = glass.active_material
    texn = [n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE'] if mat and mat.node_tree else []
    print('GLASS_TEX', [(n.image.name if n.image else None, tuple(n.image.size) if n.image else None,
                         n.interpolation, n.extension) for n in texn])
    bsdf = [n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'] if mat and mat.node_tree else []
    if bsdf:
        e = bsdf[0].inputs['Emission Strength'].default_value
        print('GLASS_EMISSIVE_STRENGTH', e, 'linked', bsdf[0].inputs['Emission Color'].is_linked)
print('VERIFY_DONE', flush=True)
if NORENDER:
    sys.exit(0)

# ---------------------------------------------------------------- the check renders
# The approved studio (build_hs.py) scaled from 6.7 units of EMI to her 1.0 m game height, and
# shifted for the game origin (sole centre between the feet). The rig empty carries camera and
# lights together, so turning it is the same as turning her under fixed lights.
K = 1.0 / 6.7
SHIFT = Vector((0, -0.075, -0.0045))
def studio_pos(x, y, z):
    return Vector((x, y, z)) * K + SHIFT

scene.render.engine = 'BLENDER_EEVEE'
scene.eevee.taa_render_samples = 64
scene.eevee.use_shadows = True
scene.eevee.use_raytracing = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.film_transparent = False
scene.view_settings.view_transform = 'AgX'
scene.view_settings.look = 'AgX - Medium High Contrast'
world = bpy.data.worlds.new('W'); scene.world = world; world.use_nodes = True
bg = world.node_tree.nodes['Background']
bg.inputs[0].default_value = (0.012, 0.011, 0.030, 1); bg.inputs[1].default_value = 1.0

def lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
fm = bpy.data.materials.new('floor'); fm.use_nodes = True
fb = fm.node_tree.nodes['Principled BSDF']
fb.inputs['Base Color'].default_value = (lin(9), lin(8), lin(18), 1)
fb.inputs['Roughness'].default_value = 0.55; fb.inputs['Coat Weight'].default_value = 0.06
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, -0.025))
floor = bpy.context.active_object; floor.name = 'floor'; floor.scale = (100, 100, 0.05)
floor.data.materials.append(fm)

rig = bpy.data.objects.new('rig', None); scene.collection.objects.link(rig)
target = bpy.data.objects.new('target', None); scene.collection.objects.link(target)
target.location = studio_pos(0, 0.42, 3.9); target.parent = rig

def light(name, loc, energy, color, size, track=True):
    ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy * K * K; ld.color = color
    ld.shape = 'SQUARE'; ld.size = size * K
    lo = bpy.data.objects.new(name, ld); lo.location = loc; lo.parent = rig
    scene.collection.objects.link(lo)
    if track:
        c = lo.constraints.new('TRACK_TO'); c.target = target
        c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'
    return lo
light('key', studio_pos(-7, -9, 11), 3300, (1.0, 0.86, 0.92), 4)
light('fill', studio_pos(9, -8, 5), 1000, (0.78, 0.72, 1.0), 6)
light('rim', studio_pos(4, 9, 9), 1300, (1.0, 0.45, 0.75), 5)
glow = light('glow', studio_pos(0, -0.73, 4.15), 22, (1.0, 0.41, 0.71), 3.0, track=False)
glow.rotation_euler = (math.radians(90), 0, 0)
cam_d = bpy.data.cameras.new('cam'); cam_d.lens = 55
cam = bpy.data.objects.new('cam', cam_d); cam.location = studio_pos(0, -16.8, 4.85); cam.parent = rig
scene.collection.objects.link(cam)
c = cam.constraints.new('TRACK_TO'); c.target = target; c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'
scene.camera = cam
# The race's menu camera: glTF (0, 0.9, 3.2) looking at (0, 0.55, 0), straight on and a little
# above eye line, here in Blender axes with a 40 degree vertical field of view.
mtarget = bpy.data.objects.new('mtarget', None); scene.collection.objects.link(mtarget)
mtarget.location = (0, 0, 0.55)
mcam_d = bpy.data.cameras.new('mcam'); mcam_d.sensor_fit = 'VERTICAL'; mcam_d.lens_unit = 'FOV'
mcam_d.angle = math.radians(40)
mcam = bpy.data.objects.new('mcam', mcam_d); mcam.location = (0, -3.2, 0.9)
scene.collection.objects.link(mcam)
c = mcam.constraints.new('TRACK_TO'); c.target = mtarget; c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'

def solo(clip):
    """Play one imported clip: its NLA track alone, everything else muted, no active action."""
    for o in objs:
        ad = o.animation_data
        if not ad:
            continue
        ad.action = None
        for t in ad.nla_tracks:
            t.mute = (t.name != clip)

def face_frame(i):
    """Slide the atlas the way the runtime does (offset.x = i / 5) through a Mapping node."""
    mat = glass.active_material
    nt = mat.node_tree
    mp = nt.nodes.get('FRAME')
    if mp is None:
        tex = [n for n in nt.nodes if n.type == 'TEX_IMAGE'][0]
        uvn = nt.nodes.new('ShaderNodeUVMap')
        mp = nt.nodes.new('ShaderNodeMapping'); mp.name = 'FRAME'
        nt.links.new(uvn.outputs['UV'], mp.inputs['Vector'])
        nt.links.new(mp.outputs['Vector'], tex.inputs['Vector'])
    mp.inputs['Location'].default_value = (i / 5.0, 0, 0)

def render(path, res, pix):
    scene.render.resolution_x = scene.render.resolution_y = res // pix if pix > 1 else res
    scene.render.filter_size = 0.6 if pix > 1 else 1.5
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print('RENDERED', path, flush=True)

# stills: clip, its most expressive frame, the atlas frame, studio rig yaw, studio camera lift (m).
# With --cam menu (the default) every still comes from the fixed menu camera at rig yaw 0, which
# is the view the clips must read from. With --cam studio, -28 is the approved emi_hs_idle.png
# angle; the wave turns the other way so the waving arm (shoulderR, +X) faces the camera, and
# the hop lifts the frame so the bead stays in shot.
STILLS = [('idle', 60, 0, -28, 0.0), ('wave', 22, 1, 28, 0.0), ('hop', 22, 2, -28, 0.14),
          ('peek', 22, 3, -28, 0.0), ('drum', 24, 4, -28, 0.0)]
cam_home, target_home = cam.location.copy(), target.location.copy()
for clip, frame, face, yaw, lift in STILLS:
    solo(clip); face_frame(face)
    if CAM == 'menu':
        scene.camera = mcam; rig.rotation_euler = (0, 0, 0)
    else:
        scene.camera = cam; rig.rotation_euler = (0, 0, math.radians(yaw))
        cam.location = cam_home + Vector((0, 0, lift)); target.location = target_home + Vector((0, 0, lift))
    scene.frame_set(frame)
    render(os.path.join(OUT, 'emi_game_%s.png' % clip), STILL, STILLPIX)
scene.camera = cam; cam.location, target.location = cam_home, target_home

# turntable: idle plays, atlas frame 0, the rig turns a full circle the way the approved one did
solo('idle'); face_frame(0)
bpy.context.preferences.edit.keyframe_new_interpolation_type = 'LINEAR'
scene.frame_start, scene.frame_end = 1, FRAMES
rig.rotation_euler = (0, 0, math.radians(30)); rig.keyframe_insert('rotation_euler', frame=1)
rig.rotation_euler = (0, 0, math.radians(-330)); rig.keyframe_insert('rotation_euler', frame=FRAMES + 1)
scene.render.resolution_x = scene.render.resolution_y = RES // PIX if PIX > 1 else RES
scene.render.filter_size = 0.6 if PIX > 1 else 1.5
scene.render.filepath = os.path.join(OUT, 'turn_')
bpy.ops.render.render(animation=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'emi_game_verify.blend'))
print('RENDER_DONE', flush=True)
