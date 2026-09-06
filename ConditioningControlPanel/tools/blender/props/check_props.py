# check_props.py - re-import props.glb in a fresh Blender and print the contract audit:
# every top-level node with its triangle count (base + outline child), bbox in glTF Y-up metres,
# the shard translations and their reassembled bbox, missing contract names, file size.
#   blender -b --factory-startup -P check_props.py -- --glb PATH
import bpy, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
GLB = argv[argv.index('--glb') + 1]
CONTRACT = ['kart_cup', 'kart_saucer', 'item_cube'] + ['item_shard_%02d' % i for i in range(12)] + [
    'boost_pad', 'ramp_lip', 'air_marker', 'gantry', 'podium', 'floor_tile',
    'teagarden_wall', 'teagarden_shoulder', 'toybox_wall', 'toybox_shoulder', 'toybox_extra',
    'casino_shoulder', 'casino_extra', 'casino_sign', 'undertow_shoulder', 'undertow_porthole',
    'mirrors_wall', 'mirrors_shoulder', 'chapel_shoulder', 'chapel_frame',
    'greyward_shoulder', 'greyward_iv', 'greyward_fluoro', 'coronation_wall', 'coronation_shoulder', 'coronation_extra']

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)

def yup(v):          # Blender Z-up back to glTF Y-up
    return Vector((v.x, v.z, -v.y))

def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons) if ob.type == 'MESH' else 0

top = [o for o in bpy.data.objects if o.parent is None]
found = {}
total = 0
for ob in sorted(top, key=lambda o: o.name):
    kids = [c for c in ob.children]
    t = tris(ob) + sum(tris(c) for c in kids)
    total += t
    vs = [yup(ob.matrix_world @ v.co) for v in ob.data.vertices] if ob.type == 'MESH' else []
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    mats = [m.name for m in ob.data.materials] if ob.type == 'MESH' else []
    ol = [c.name for c in kids if c.type == 'MESH' and c.data.materials and c.data.materials[0].name == 'outline']
    found[ob.name] = (t, lo, hi)
    print('CHECK %-22s tris %5d  bbox x %.2f..%.2f y %.2f..%.2f z %.2f..%.2f  loc %s  mats %s  outline %s' % (
        ob.name, t, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z, tuple(round(c, 3) for c in yup(ob.location)), ','.join(mats), ol), flush=True)

missing = [n for n in CONTRACT if n not in found]
print('MISSING', missing if missing else 'none', flush=True)
extra = [n for n in found if n not in CONTRACT]
print('EXTRA', extra if extra else 'none', flush=True)
shards = [n for n in found if n.startswith('item_shard')]
if shards:
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for n in shards:
        _t, a, b = found[n]
        lo = Vector(tuple(min(x, y) for x, y in zip(lo, a))); hi = Vector(tuple(max(x, y) for x, y in zip(hi, b)))
    print('SHARDS reassembled bbox x %.2f..%.2f y %.2f..%.2f z %.2f..%.2f (cube is -0.6..0.6 / 0..1.2 / -0.6..0.6)' % (lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
print('TOTAL tris', total, 'nodes', len(top), 'materials', sorted(m.name for m in bpy.data.materials))
print('SIZE', os.path.getsize(GLB), 'bytes')
print('CHECK_DONE', flush=True)
