# check_pieces.py - audit the twelve piece glbs in a fresh Blender, the way pieces.js will read them.
#   blender -b --factory-startup -P check_pieces.py -- --dir DIR
# For every file: the body node is named after the piece, carries COLOR_0, has outward normals (the
# first-pass files had five of six bodies inside-out), stands on z = 0 at the height the recipe
# promised, and any other node is jewellery with a material of its own and no colours. Exits 1 on
# the first file that breaks the contract, so run_pieces.cmd can stop before copying anything.
import bpy, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def arg(name, default):
    return argv[argv.index(name) + 1] if name in argv else default
DIR = arg('--dir', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out'))

EXPECT = {   # height, max radius (a little over the recipe's own), jewellery node
    'pawn': (0.55, 0.20, None), 'knight': (1.00, 0.30, None), 'bishop': (1.00, 0.30, None),
    'rook': (1.00, 0.32, None), 'queen': (1.15, 0.22, 'tiara'), 'king': (1.25, 0.31, 'crown'),
}
bad = 0
def fail(msg):
    global bad
    bad += 1
    print(f'[check] FAIL {msg}')

for name, (h, rmax, jewel) in EXPECT.items():
    for suffix in ('', '_purple'):
        f = os.path.join(DIR, f'{name}{suffix}.glb')
        bpy.ops.wm.read_factory_settings(use_empty=True)
        if not os.path.exists(f):
            fail(f'{name}{suffix}.glb missing')
            continue
        bpy.ops.import_scene.gltf(filepath=f)
        meshes = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}
        body = meshes.get(name)
        if body is None:
            fail(f'{name}{suffix}: no node named {name} (got {sorted(meshes)})')
            continue
        me = body.data
        if not me.color_attributes:
            fail(f'{name}{suffix}: body has no COLOR_0')
        co = [body.matrix_world @ v.co for v in me.vertices]
        zmin, zmax = min(c.z for c in co), max(c.z for c in co)
        rr = max((c.x * c.x + c.y * c.y) ** 0.5 for c in co)
        if abs(zmin) > 1e-3:
            fail(f'{name}{suffix}: base at z={zmin:.3f}, not 0')
        if abs(zmax - h) > 0.01:
            fail(f'{name}{suffix}: height {zmax:.3f}, expected {h}')
        if rr > rmax:
            fail(f'{name}{suffix}: radius {rr:.3f} over {rmax}')
        cz = (zmax + zmin) / 2
        out = tot = 0
        for p in me.polygons:
            c = body.matrix_world @ p.center
            n = (body.matrix_world.to_3x3() @ p.normal)
            v = Vector((c.x, c.y, c.z - cz))
            if v.length < 1e-6:
                continue
            tot += 1
            out += n.dot(v) > 0
        frac = out / max(tot, 1)
        if frac < 0.75:
            fail(f'{name}{suffix}: only {frac:.2f} of faces point outward')
        others = sorted(n for n in meshes if n != name)
        if jewel is None and others:
            fail(f'{name}{suffix}: unexpected extra nodes {others}')
        if jewel is not None:
            if others != [jewel]:
                fail(f'{name}{suffix}: expected jewellery [{jewel}], got {others}')
            else:
                j = meshes[jewel]
                if j.data.color_attributes:
                    fail(f'{name}{suffix}: {jewel} carries vertex colours; it should keep its own metal')
                if not j.data.materials or j.data.materials[0].name == me.materials[0].name:
                    fail(f'{name}{suffix}: {jewel} shares the body material')
        print(f'[check] {name}{suffix}: verts={len(me.vertices)} h={zmax:.3f} r={rr:.3f} outward={frac:.2f} '
              f'mats={[m.name for m in me.materials]} extra={others}')

print(f'[check] {"OK" if bad == 0 else str(bad) + " problems"}')
sys.exit(1 if bad else 0)
