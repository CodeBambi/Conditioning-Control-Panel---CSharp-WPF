"""Second scene study: material refinement and authored camera pullback."""
import bpy, math, json, sys, random
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parent
# Keep the first study reproducible. Reuse its construction without rendering it.
exec(compile((OUT/'build_office.py').read_text(encoding='utf-8-sig').split('bpy.ops.wm.save_as_mainfile')[0],str(OUT/'build_office.py'),'exec'))
random.seed(17)
for m in bpy.data.materials:
 if m.use_nodes:
  for n in m.node_tree.nodes:
   if n.type=='BUMP':n.inputs['Distance'].default_value=.0004
# A photographed linen weave replaces the large procedural noise on partitions.
asset=OUT/'assets'/'rough-linen'
diff=next(asset.glob('*diffuse*.jpg'),None)
if diff:
 n=fabric.node_tree.nodes;l=fabric.node_tree.links;p=n.get('Principled BSDF')
 uv=n.new('ShaderNodeTexCoord');mapping=n.new('ShaderNodeVectorMath');mapping.operation='SCALE';mapping.inputs[3].default_value=7
 l.new(uv.outputs['UV'],mapping.inputs[0])
 t=n.new('ShaderNodeTexImage');t.image=bpy.data.images.load(str(diff));l.new(mapping.outputs[0],t.inputs['Vector'])
 bw=n.new('ShaderNodeRGBToBW');l.new(t.outputs['Color'],bw.inputs[0])
 ramp=n.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].color=(.065,.07,.068,1);ramp.color_ramp.elements[1].color=(.24,.25,.242,1);l.new(bw.outputs[0],ramp.inputs[0]);l.new(ramp.outputs[0],p.inputs['Base Color'])
 bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.22;bump.inputs['Distance'].default_value=.0008;l.new(bw.outputs[0],bump.inputs['Height']);l.new(bump.outputs[0],p.inputs['Normal'])
# Shared ending card artwork, packed into the blend. Static screen
# matches the runtime completion screen.
for o in bpy.data.objects:
 if o.name.startswith('placeholder') or o.name=='SCREEN_GAME_REPLACE_ME':o.hide_render=True
local=OUT/'assets'/'ending-screen.png'
if not local.exists():raise FileNotFoundError('Export the ending card before building the office scene.')
mesh=bpy.data.meshes.new('screen UV surface');mesh.from_pydata([(-.308,.131,1.04675),(.308,.131,1.04675),(.308,.131,1.39325),(-.308,.131,1.39325)],[],[(0,1,2,3)]);mesh.uv_layers.new()
for i,uv in enumerate([(0,0),(1,0),(1,1),(0,1)]):mesh.uv_layers.active.data[i].uv=uv
screen=bpy.data.objects.new('SCREEN_UV_FINAL_FRAME',mesh);bpy.context.collection.objects.link(screen)
m=bpy.data.materials.new('live game replacement');m.use_nodes=True;n=m.node_tree.nodes;n.clear();out=n.new('ShaderNodeOutputMaterial');emit=n.new('ShaderNodeEmission');emit.inputs['Strength'].default_value=1.2;t=n.new('ShaderNodeTexImage');t.image=bpy.data.images.load(str(local));m.node_tree.links.new(t.outputs['Color'],emit.inputs['Color']);m.node_tree.links.new(emit.outputs[0],out.inputs[0]);mesh.materials.append(m)
# A hollow mug: closed bottom, inner wall and rounded lip.
for name in ('mug body','mug dark opening'):bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
profile=[(0,.783),(.033,.783),(.039,.790),(.040,.88),(.0385,.889),(.034,.889),(.032,.881),(.030,.796),(0,.796)]
verts=[];faces=[];N=64
for r,z in profile:
 for j in range(N):verts.append((-.52+r*math.cos(j*math.tau/N),-.22+r*math.sin(j*math.tau/N),z))
for k in range(len(profile)-1):
 for j in range(N):a=k*N+j;b=k*N+(j+1)%N;faces.append((a,b,b+N,a+N))
mesh=bpy.data.meshes.new('hollow ceramic mesh');mesh.from_pydata(verts,[],faces);o=bpy.data.objects.new('hollow ceramic mug',mesh);bpy.context.collection.objects.link(o);mesh.materials.append(ceramic)
for p in mesh.polygons:p.use_smooth=True
# Little asymmetry and physical office details, kept below the monitor.
for o in bpy.data.objects:
 if o.name.startswith('paper in tray'):
  o.rotation_euler.z=random.uniform(-.055,.055);o.location.x+=random.uniform(-.004,.004)
# Desk edge band and circular grommet at back right.
box('laminate edge band',(0,-.507,.75),(2.30,.012,.047),trim,.002)
cyl('cable grommet',(.66,.36,.780),.034,.005,trim)
cable('tower cable bundle',[(.88,.41,.92),(.69,.40,.80),(.66,.36,.79),(.66,.36,.60)],.008)
# Telephone coiled cord lies on the tabletop beside the receiver.
pts=[]
for i in range(160):
 t=i/159;pts.append((.535+.010*math.sin(t*math.tau*13),-.28+t*.18,.799+.009*math.cos(t*math.tau*13)))
cable('telephone coil',pts,.002)
# Fine monitor hardware and indicator, no extra coloured lights.
for x in (-.313,.313):
 for z in (1.026,1.396):
  o=cyl('monitor case screw',(x,.142,z),.003,.002,trim);o.rotation_euler=(math.pi/2,0,0)
for x in (.21,.235,.26):box('monitor button',(x,.138,1.020),(.013,.005,.003),keys,.001)
# A few notes, a clipped pen, and a calendar sheet convey routine without gore.
box('desk memo',(.13,-.36,.782),(.13,.105,.001),paper,.001).rotation_euler.z=-.12
text('memo print','CALL BACK\nEXT. 041',(.080,-.388,.784),.010,ink,(0,0,-.12))
pen=box('pen',(.27,-.34,.788),(.009,.16,.009),plastic,.004);pen.rotation_euler.z=-.21
# Existing evenly spaced keyboard keys gain restrained legends.
for r in range(3):
 for k,ch in enumerate(['QWERTYUIOP','ASDFGHJKL','ZXCVBNM'][r]):
  text('key legend',ch,(-.305+k*.0305,-.219-r*.027,.829),.005,paper,(0,0,0))
# Keep panel edges grounded and make the office lighting more directional.
bpy.data.lights['cold overhead fluorescent'].shape='RECTANGLE';bpy.data.lights['cold overhead fluorescent'].size=1.1;bpy.data.lights['cold overhead fluorescent'].size_y=.32
bpy.data.objects['cold overhead fluorescent'].location=(-.32,.04,2.5)
bpy.data.lights['cold overhead fluorescent'].energy=29
bpy.data.lights['monitor violet spill'].energy=2
# 24 fps camera animation. Hold .5s, ease out for 6.5s, hold 1s.
FPS=24;END=192;LENS=39.0;WIDTH=.616
cam=camera('CAMERA_PULLBACK',(0,0,1.22),(0,.131,1.22),LENS);cam.data.sensor_fit='HORIZONTAL';cam.data.sensor_width=36
start_y=.131-WIDTH*LENS/36
for frame in range(1,END+1):
 t=max(0,min(1,(frame-13)/(168-13)));q=t*t*(3-2*t)
 cam.location=(.055*q,start_y+(-2.65-start_y)*q,1.22+.40*q)
 target=Vector((0,.131,1.22-.12*q));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
 cam.keyframe_insert(data_path='location',frame=frame);cam.keyframe_insert(data_path='rotation_euler',frame=frame)
scene.camera=cam;scene.render.fps=FPS;scene.frame_start=1;scene.frame_end=END;scene.frame_set(END)
scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100
scene.cycles.samples=32;scene.cycles.use_denoising=True
# Select a supported GPU when available, retain CPU fallback.
backend='CPU'
try:
 prefs=bpy.context.preferences.addons['cycles'].preferences
 for candidate in ('OPTIX','CUDA'):
  try:
   prefs.compute_device_type=candidate;prefs.get_devices()
   devices=[d for d in prefs.devices if d.type==candidate]
   if devices:
    for d in prefs.devices:d.use=d in devices
    scene.cycles.device='GPU';backend=candidate;break
  except Exception:pass
except Exception:pass
for image in bpy.data.images:
 if image.source=='FILE':image.pack()
scene.render.filepath=str(OUT/'office-refined.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'office-ending-motion.blend'))
bpy.ops.render.render(write_still=True)
# Low-cost full-duration timing preview, every other authored frame at 12 fps.
if '--preview' in sys.argv:
 scene.render.resolution_percentage=50;scene.cycles.samples=12
 frames=OUT/'motion-frames';frames.mkdir(exist_ok=True)
 for index,frame in enumerate(range(1,END+1,2)):
  scene.frame_set(frame);scene.render.filepath=str(frames/f'{index:04d}.png');bpy.ops.render.render(write_still=True)
(OUT/'motion-manifest.json').write_text(json.dumps({'stage':'refined scene and camera timing preview','blender':bpy.app.version_string,'engine':'Cycles','device':backend,'hero_resolution':[1280,720],'hero_samples':32,'preview_resolution':[640,360],'preview_samples':12,'preview_fps':12,'authored_fps':24,'frames':[1,192],'duration_seconds':8,'screen':'shared ending card','integration':'not integrated','textures':'Poly Haven rough_linen CC0, packed'},indent=2))
