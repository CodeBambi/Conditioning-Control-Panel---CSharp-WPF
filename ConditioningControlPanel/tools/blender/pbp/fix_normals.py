# fix_normals.py - the first-pass men, turned the right way out. Imports each sculpted glb, makes
# every mesh's faces point away from the body (recalc, then a whole-mesh flip if the majority
# still faces in), and re-exports with the vertex colours, materials and node names untouched.
#   blender -b --factory-startup -P fix_normals.py -- --in DIR --out DIR
import bpy, bmesh, os, sys
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
def arg(n, d):
    return argv[argv.index(n) + 1] if n in argv else d
IN, OUT = arg('--in', '.'), arg('--out', './fixed')
os.makedirs(OUT, exist_ok=True)

def outward_fraction(ob):
    me = ob.data
    zs = [(ob.matrix_world @ v.co).z for v in me.vertices]
    cz = (max(zs) + min(zs)) / 2
    out = tot = 0
    for p in me.polygons:
        c = ob.matrix_world @ p.center
        n = ob.matrix_world.to_3x3() @ p.normal
        v = Vector((c.x, c.y, c.z - cz))
        if v.length < 1e-6:
            continue
        tot += 1
        out += n.dot(v) > 0
    return out / max(tot, 1)

for f in sorted(os.listdir(IN)):
    if not f.endswith('.glb'):
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(IN, f))
    objs = [o for o in bpy.data.objects if o.type == 'MESH']
    for o in objs:
        before = outward_fraction(o)
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(o.data); bm.free()
        mid = outward_fraction(o)
        if mid < 0.5:
            bm = bmesh.new(); bm.from_mesh(o.data)
            bmesh.ops.reverse_faces(bm, faces=bm.faces)
            bm.to_mesh(o.data); bm.free()
        # custom split normals from the import would keep pointing the old way; drop them so the
        # exporter writes normals from the corrected faces
        try: o.data.shade_smooth()
        except Exception: pass
        after = outward_fraction(o)
        print(f'[fix] {f} {o.name}: outward {before:.2f} -> {mid:.2f} -> {after:.2f} colours={[c.name for c in o.data.color_attributes]}')
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    kw = dict(filepath=os.path.join(OUT, f), export_format='GLB', export_apply=True, export_yup=True, use_selection=True,
              export_animations=False, export_cameras=False, export_lights=False, export_image_format='NONE',
              export_texcoords=False, export_skins=False, export_morph=False, export_extras=False)
    try:
        bpy.ops.export_scene.gltf(**kw, export_vertex_color='ACTIVE', export_all_vertex_colors=False)
    except TypeError:
        bpy.ops.export_scene.gltf(**kw)
print('[fix] DONE')
