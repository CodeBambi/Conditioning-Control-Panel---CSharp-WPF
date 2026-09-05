# export_glb.py - EMI's game glb for The Caucus Race (race/assets/emi.glb).
# Opens the game rig written by  build_hs.py --game 1 --mode game  (see run_game.cmd), authors the
# five character-select clips as one slotted action each (idle, wave, hop, peek, drum: one slot per
# pivot), parks every action on an NLA track named after it, exports the glb (Y up, she faces +Z,
# metres, atlas packed, keys as authored, never sampled) and copies the face atlas beside it.
#   blender -b --factory-startup -P export_glb.py -- --blend IN.blend --glb OUT.glb [--atlas emi-faces.png] [--fps 30]
# Contract (race/assets/README.md): pivots EMI_root, ant0..ant2, ballpiv, shoulderL/R, footL/R,
# button; meshes EMI_case, EMI_glass; material 'outline' = the inverted hull. Keys land on the
# pivots and the root only. No face frame is baked; the runtime slides the atlas.
import bpy, math, os, sys, shutil
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default
BLEND = arg('--blend')
GLB = arg('--glb')
ATLAS = arg('--atlas', '')
FPS = int(arg('--fps', '30'))
if not BLEND or not GLB:
    raise SystemExit('usage: export_glb.py -- --blend IN.blend --glb OUT.glb [--atlas png] [--fps 30]')

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
scene.render.fps = FPS
scene.render.fps_base = 1.0
prefs = bpy.context.preferences.edit
prefs.keyframe_new_interpolation_type = 'BEZIER'
prefs.keyframe_new_handle_type = 'AUTO_CLAMPED'

PIVOTS = ['EMI_root', 'ant0', 'ant1', 'ant2', 'ballpiv', 'shoulderL', 'shoulderR', 'footL', 'footR']
O = {n: bpy.data.objects[n] for n in PIVOTS}
REST = {n: (o.location.copy(), Vector(o.rotation_euler), o.scale.copy()) for n, o in O.items()}

# Sign sheet (Blender axes, she faces -Y which exports as glTF +Z):
#   root rot x +  : top tilts toward the camera        root rot y + : leans toward her +X side
#   shoulder rot y: L + / R - swings the arm outward   shoulder rot x - : arm swings forward
#   foot rot x +  : toes down                          ant rot x + : the stem droops forward
#   shoulderL sits at -X (her right hand, the viewer's left when she faces you)

def key(name, frame, loc=None, rot=None, scale=None):
    """One pivot at one frame. loc = metres added to rest, rot = degrees added to rest (XYZ),
    scale = multipliers on rest. Each transform is keyed as a whole vector, so every channel keeps
    one keyframe count and the exporter can ship the curves unsampled."""
    o = O[name]
    rl, rr, rs = REST[name]
    if loc is not None:
        o.location = rl + Vector(loc)
        o.keyframe_insert('location', frame=frame)
    if rot is not None:
        o.rotation_euler = [rr[i] + math.radians(rot[i]) for i in range(3)]
        o.keyframe_insert('rotation_euler', frame=frame)
    if scale is not None:
        o.scale = [rs[i] * scale[i] for i in range(3)]
        o.keyframe_insert('scale', frame=frame)

def rest(frame):
    """Every pivot at rest on every channel a clip may touch, so each clip fully owns the pose and
    a crossfade never inherits a stray arm from the clip before."""
    key('EMI_root', frame, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1))
    key('ant0', frame, rot=(0, 0, 0), scale=(1, 1, 1))
    key('ant1', frame, rot=(0, 0, 0))
    key('ant2', frame, rot=(0, 0, 0))
    key('ballpiv', frame, scale=(1, 1, 1))
    key('shoulderL', frame, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1))
    key('shoulderR', frame, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1))
    key('footL', frame, rot=(0, 0, 0))
    key('footR', frame, rot=(0, 0, 0))

def begin_clip(name):
    act = bpy.data.actions.new(name)
    for n, o in O.items():
        ad = o.animation_data or o.animation_data_create()
        slot = act.slots.new('OBJECT', n)
        ad.action = act
        ad.action_slot = slot
    return act

def end_clip(act, length):
    for n, o in O.items():
        ad = o.animation_data
        slot = ad.action_slot
        track = ad.nla_tracks.new()
        track.name = act.name
        strip = track.strips.new(act.name, 0, act)
        strip.action_slot = slot
        strip.frame_end = length
        ad.action = None
        o.location, o.rotation_euler, o.scale = REST[n][0].copy(), REST[n][1].copy(), REST[n][2].copy()
    print('CLIP', act.name, 'frames', 0, length, 'sec', length / FPS, 'slots', len(act.slots), flush=True)

def sh(frame, l=None, r=None):
    """Both shoulders at once; r defaults to the mirror of l (y negated)."""
    if l is not None:
        key('shoulderL', frame, rot=l)
    if r is None and l is not None:
        r = (l[0], -l[1], l[2])
    if r is not None:
        key('shoulderR', frame, rot=r)

_arm_prev = {}
def arm(side, frame, out, back=0.0, twist=0.0, stretch=1.0, lift=0.0):
    """One arm as out (degrees from hanging, swung outward about the forward axis), back (degrees
    swung behind her), twist (degrees about the arm's own axis, + turns the thumb toward the
    camera), stretch (cartoon scale along the arm) and lift (metres the shoulder shrugs up the
    case side). Composed as back * out * twist and handed to key() as an XYZ Euler picked nearest
    the previous key so the curve never wraps."""
    sx = 1 if side == 'R' else -1
    m = (Matrix.Rotation(math.radians(back), 3, 'X') @ Matrix.Rotation(math.radians(-sx * out), 3, 'Y')
         @ Matrix.Rotation(math.radians(sx * twist), 3, 'Z'))
    name = 'shoulder' + side
    prev = _arm_prev.get(name) or Euler(REST[name][1], 'XYZ')
    e = m.to_euler('XYZ', prev)
    _arm_prev[name] = e
    key(name, frame, loc=(0, 0, lift), rot=[math.degrees(e[i] - REST[name][1][i]) for i in range(3)],
        scale=(1, 1, stretch))

# ---------------------------------------------------------------- idle: 4.0 s seamless loop
# one breath per loop (Law III: the one breath on screen), a lazy weight shift, the antenna
# swaying a beat behind, the bead pulsing once. Ends equal the start with flat tangents.
act = begin_clip('idle')
L = 4.0 * FPS
rest(0); rest(L)
key('EMI_root', L / 2, loc=(0, 0, 0.004), scale=(0.992, 0.992, 1.014))
key('EMI_root', L / 3, rot=(0, 0.9, -0.6))
key('EMI_root', 2 * L / 3, rot=(0, -0.9, 0.6))
key('ant0', L / 3, rot=(2, 5, 0)); key('ant0', 2 * L / 3, rot=(-2, -5, 0))
key('ant1', L / 3 + 10, rot=(2, 4, 0)); key('ant1', 2 * L / 3 + 10, rot=(-2, -4, 0))
key('ant2', L / 2, rot=(3, 0, 0))
key('ballpiv', L / 2, scale=(1.05, 1.05, 1.05))
sh(L / 2, (0, 1.5, 0))
end_clip(act, L)

# ---------------------------------------------------------------- wave: 2.0 s
# the right-hand-side arm (shoulderR, +X) swings OUT and UP beside the case about the forward
# axis, a little back, thumb turned to the camera; four waggles with the hand above the case's
# top corner; comes down. She leans into it, the antenna perks back and the bead swells.
# The shoulder pivot sits at 0.49 m and the arm reaches 0.29 m, so a rotation alone tops out
# at 0.78 m against the 1.0 m case top. The hand gets above the case by a cartoon stretch
# along the arm (WAVE_STRETCH) and a shrug of the shoulder up the case side (WAVE_LIFT): at
# 165 degrees out the hand bottom meets the case top line and the waggle sweeps the mitten
# above and beside the corner. Both are single constants to dial.
WAVE_STRETCH, WAVE_LIFT = 2.0, 0.09
act = begin_clip('wave')
L = 2.0 * FPS
rest(0); rest(L)
_arm_prev.clear()
arm('R', 5, out=6, back=-8, stretch=0.94)                                    # anticipation: a dip inward
arm('R', 11, out=172, back=12, twist=60, stretch=WAVE_STRETCH * 1.06, lift=WAVE_LIFT * 1.15)   # fly-up, overshoot
arm('R', 15, out=160, back=10, twist=60, stretch=WAVE_STRETCH, lift=WAVE_LIFT)
arm('R', 20, out=178, back=10, twist=60, stretch=WAVE_STRETCH, lift=WAVE_LIFT)   # waggle above the corner
arm('R', 25, out=148, back=10, twist=60, stretch=WAVE_STRETCH, lift=WAVE_LIFT)
arm('R', 30, out=178, back=10, twist=60, stretch=WAVE_STRETCH, lift=WAVE_LIFT)
arm('R', 35, out=148, back=10, twist=60, stretch=WAVE_STRETCH, lift=WAVE_LIFT)
arm('R', 40, out=168, back=10, twist=60, stretch=WAVE_STRETCH, lift=WAVE_LIFT)
arm('R', 46, out=148, back=6, twist=40, stretch=1.5, lift=WAVE_LIFT * 0.5)
arm('R', 54, out=30, back=0, twist=6, stretch=1.0, lift=0.0)                 # down
key('shoulderL', 12, rot=(0, 10, 0)); key('shoulderL', 42, rot=(0, 10, 0))
key('ant0', 10, rot=(-16, 0, 0), scale=(1, 1, 1.10)); key('ant0', 40, rot=(-14, 0, 0), scale=(1, 1, 1.08))
key('ant1', 12, rot=(-10, 0, 0)); key('ant1', 42, rot=(-8, 0, 0))
key('ant2', 14, rot=(-6, 0, 0)); key('ant2', 44, rot=(-5, 0, 0))
key('ballpiv', 12, scale=(1.15, 1.15, 1.15)); key('ballpiv', 40, scale=(1.10, 1.10, 1.10))
key('EMI_root', 6, loc=(0, 0, 0.02), scale=(1.02, 1.02, 0.97))
key('EMI_root', 12, loc=(0, 0, 0), scale=(1, 1, 1), rot=(0, 5, 0))
key('EMI_root', 44, rot=(0, 4, 0))
end_clip(act, L)

# ---------------------------------------------------------------- hop: 1.6 s
# anticipation squash, launch with stretch, apex, land with squash, overshoot, settle. The
# antenna whips a beat behind, the arms flare in the air, the toes point down.
act = begin_clip('hop')
L = 1.6 * FPS
rest(0); rest(L)
key('EMI_root', 9, loc=(0, 0, 0), scale=(1.09, 1.09, 0.84))
key('EMI_root', 15, loc=(0, 0, 0.08), scale=(0.92, 0.92, 1.14), rot=(-3, 0, 0))
key('EMI_root', 22, loc=(0, 0, 0.24), scale=(0.98, 0.98, 1.03), rot=(-4, 0, 0))
key('EMI_root', 29, loc=(0, 0, 0.08), scale=(0.94, 0.94, 1.10))
key('EMI_root', 33, loc=(0, 0, 0), scale=(1.15, 1.15, 0.80), rot=(3, 0, 0))
key('EMI_root', 38, scale=(0.97, 0.97, 1.05))
key('EMI_root', 43, scale=(1.02, 1.02, 0.985))
for f, x, sz in ((9, 14, 0.90), (15, -14, 1.15), (22, -2, 1.0), (29, 6, 1.0), (33, 22, 0.85), (39, -8, 1.05), (44, 3, 1.0)):
    key('ant0', f, rot=(x, 0, 0), scale=(1, 1, sz))
for f, x in ((11, 10), (17, -10), (24, 0), (35, 16), (41, -6)):
    key('ant1', f, rot=(x, 0, 0))
for f, x in ((13, 8), (19, -8), (26, 0), (37, 12), (43, -4)):
    key('ant2', f, rot=(x, 0, 0))
for f, s in ((15, 1.15), (33, 0.90), (40, 1.05)):
    key('ballpiv', f, scale=(s, s, s))
for f, y in ((9, -6), (15, 50), (22, 62), (29, 55), (33, 10), (39, 20)):
    sh(f, (0, y, 0))
for f, x in ((9, 0), (15, 18), (22, 24), (29, 12), (33, 0)):
    key('footL', f, rot=(x, 0, 0)); key('footR', f, rot=(x, 0, 0))
end_clip(act, L)

# ---------------------------------------------------------------- peek: 2.0 s
# she tilts toward the camera with a curious roll, holds a beat, the antenna leans in after her,
# then everything settles back through a small overshoot.
act = begin_clip('peek')
L = 2.0 * FPS
rest(0); rest(L)
key('EMI_root', 14, rot=(16, 7, 0), loc=(0, -0.05, -0.012))
key('EMI_root', 22, rot=(14, 8, 0))
key('EMI_root', 38, rot=(14, 8, 0), loc=(0, -0.05, -0.012))
key('EMI_root', 50, rot=(-2, -1, 0), loc=(0, 0.004, 0))
key('ant0', 18, rot=(22, 0, 0), scale=(1, 1, 1.06)); key('ant0', 40, rot=(18, 0, 0), scale=(1, 1, 1.04)); key('ant0', 52, rot=(-4, 0, 0), scale=(1, 1, 1))
key('ant1', 21, rot=(16, 3, 0)); key('ant1', 42, rot=(12, 3, 0)); key('ant1', 54, rot=(-3, 0, 0))
key('ant2', 24, rot=(12, 4, 0)); key('ant2', 44, rot=(8, 4, 0)); key('ant2', 56, rot=(-2, 0, 0))
key('ballpiv', 22, scale=(1.12, 1.12, 1.12)); key('ballpiv', 40, scale=(1.10, 1.10, 1.10))
sh(16, (6, 4, 0)); sh(40, (6, 4, 0))
end_clip(act, L)

# ---------------------------------------------------------------- drum: 2.4 s
# both hands tap an invisible rim in front of her, alternating every 6 frames; she rocks toward
# each hit, the antenna nods on the beat, the bead pops with it.
act = begin_clip('drum')
L = 2.4 * FPS
rest(0); rest(L)
UP, HIT, RAISE = -70, -58, -78
hitsL = [12, 24, 36, 48, 60]
hitsR = [18, 30, 42, 54, 66]
for side, hits, y in (('shoulderL', hitsL, 24), ('shoulderR', hitsR, -24)):
    key(side, 8, rot=(UP, y, 0))
    for h in hits:
        key(side, h, rot=(HIT, y, 0))
        key(side, h + 2, rot=(HIT, y, 0))
        if h + 7 < L - 4:
            key(side, h + 7, rot=(RAISE, y, 0))
for h in sorted(hitsL + hitsR):
    left = h in hitsL
    key('EMI_root', h, loc=(0, 0, -0.006), rot=(0, -1.5 if left else 1.5, 0))
    key('EMI_root', h + 3, loc=(0, 0, 0))
    key('ant0', h, rot=(8, -4 if left else 4, 0)); key('ant0', h + 3, rot=(-2, 0, 0))
    key('ant1', h + 2, rot=(6, -3 if left else 3, 0)); key('ant1', h + 5, rot=(-1, 0, 0))
    key('ballpiv', h, scale=(1.08, 1.08, 1.08)); key('ballpiv', h + 3, scale=(1, 1, 1))
key('ant2', 8, rot=(4, 0, 0)); key('ant2', L - 6, rot=(4, 0, 0))
end_clip(act, L)

scene.frame_start, scene.frame_end = 0, int(4.0 * FPS)
scene.frame_set(0)
anim_blend = os.path.splitext(BLEND)[0] + '_anim.blend'
bpy.ops.wm.save_as_mainfile(filepath=anim_blend)
print('SAVED', anim_blend, flush=True)

# ---------------------------------------------------------------- export
os.makedirs(os.path.dirname(os.path.abspath(GLB)), exist_ok=True)
opts = dict(
    filepath=GLB, export_format='GLB', export_apply=True, export_yup=True,
    export_animations=True, export_animation_mode='ACTIONS', export_merge_animation='ACTION',
    export_force_sampling=False, export_optimize_animation_size=True,
    export_optimize_animation_keep_anim_object=True, export_frame_range=False,
    export_anim_slide_to_zero=True, export_bake_animation=False, export_current_frame=False,
    export_image_format='AUTO', export_texcoords=True, export_normals=True, export_tangents=False,
    export_materials='EXPORT', export_lights=False, export_cameras=False, export_extras=False,
    export_skins=False, export_morph=False, export_def_bones=False, export_attributes=False,
    export_hierarchy_full_collections=False, use_renderable=True, use_visible=False,
)
known = {p.identifier for p in bpy.ops.export_scene.gltf.get_rna_type().properties}
dropped = [k for k in opts if k not in known]
for k in dropped:
    opts.pop(k)
if dropped:
    print('EXPORT_OPTS_UNKNOWN', dropped, flush=True)
bpy.ops.export_scene.gltf(**opts)
print('EXPORTED', GLB, os.path.getsize(GLB), 'bytes', flush=True)
if ATLAS:
    dst = os.path.join(os.path.dirname(os.path.abspath(GLB)), 'emi-faces.png')
    shutil.copyfile(ATLAS, dst)
    print('ATLAS', dst, os.path.getsize(dst), 'bytes', flush=True)
print('EXPORT_DONE', flush=True)
