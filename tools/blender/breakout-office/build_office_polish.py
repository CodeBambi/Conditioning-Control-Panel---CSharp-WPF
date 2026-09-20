"""Office polish pass. Preserve the approved camera and scene silhouette."""
from pathlib import Path
import bpy,math,json,sys
from mathutils import Vector
OUT=Path(__file__).resolve().parent
exec(compile((OUT/'build_office_motion.py').read_text(encoding='utf-8-sig').rsplit('bpy.ops.wm.save_as_mainfile',1)[0],str(OUT/'build_office_motion.py'),'exec'))
# Use actual units for surface variation, with restrained worn-plastic roughness.
def surface_variation(material,scale,lo,hi,rough_lo,rough_hi):
 n=material.node_tree.nodes;l=material.node_tree.links;p=next(v for v in n if v.type=='BSDF_PRINCIPLED')
 tex=n.new('ShaderNodeTexNoise');tex.inputs['Scale'].default_value=scale;tex.inputs['Detail'].default_value=2.5
 coord=n.new('ShaderNodeTexCoord');l.new(coord.outputs['Object'],tex.inputs['Vector'])
 ramp=n.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].color=(*lo,1);ramp.color_ramp.elements[1].color=(*hi,1)
 l.new(tex.outputs['Fac'],ramp.inputs[0]);l.new(ramp.outputs[0],p.inputs['Base Color'])
 r=n.new('ShaderNodeMapRange');r.inputs['From Min'].default_value=0;r.inputs['From Max'].default_value=1;r.inputs['To Min'].default_value=rough_lo;r.inputs['To Max'].default_value=rough_hi
 l.new(tex.outputs['Fac'],r.inputs['Value']);l.new(r.outputs['Result'],p.inputs['Roughness'])
surface_variation(plastic,19,(.08,.084,.082),(.14,.145,.138),.38,.58)
surface_variation(desk,38,(.25,.255,.24),(.31,.315,.295),.40,.58)
surface_variation(metal,26,(.13,.138,.135),(.19,.20,.191),.42,.62)
chrome=mat('dull steel fittings',(.24,.25,.245),.28);next(n for n in chrome.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Metallic'].default_value=.8
# Old monitor has layered bezel and button legends; retain image fit.
for x in (-.314,.314):box('inner bezel vertical',(x,.128,1.22),(.008,.006,.362),trim,.002)
for z in (1.043,1.397):box('inner bezel horizontal',(0,.128,z),(.636,.006,.008),trim,.002)
text('monitor button labels','-  +  MENU',(.182,.133,1.031),.0055,keys)
box('monitor stand seam',(0,.185,.84),(.085,.080,.012),trim,.003)
# A less box-like office phone: sloped base, recessed LCD and curved receiver.
old=bpy.data.objects.get('office telephone');bpy.data.objects.remove(old,do_unlink=True)
x0,x1=.535,.745;y0,y1=-.295,-.105;z0=.782
verts=[(x0,y0,z0),(x1,y0,z0),(x1,y1,z0),(x0,y1,z0),(x0,y0,.841),(x1,y0,.841),(x1,y1,.872),(x0,y1,.872)]
me=bpy.data.meshes.new('telephone wedge mesh');me.from_pydata(verts,[],[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)])
o=bpy.data.objects.new('sloped office telephone',me);bpy.context.collection.objects.link(o);me.materials.append(plastic);mod=o.modifiers.new('case radius','BEVEL');mod.width=.009;mod.segments=4;o.modifiers.new('normals','WEIGHTED_NORMAL')
for o in bpy.data.objects:
 if o.name.startswith('phone button'):o.location.z=.845+(o.location.y+.295)*.16
bpy.data.objects.remove(bpy.data.objects['telephone receiver'],do_unlink=True)
box('receiver handle',(.567,-.20,.895),(.03,.122,.020),trim,.009)
for y in (-.28,-.12):
 box('receiver earpiece',(.567,y,.884),(.051,.053,.044),trim,.015)
 for i in range(3):box('receiver grille',(.567,y-.01+i*.008,.907),(.027,.003,.001),plastic,.001)
box('phone LCD recess',(.676,-.132,.875),(.097,.040,.006),trim,.003)
box('phone LCD glass',(.676,-.132,.879),(.080,.026,.002),mat('unlit LCD',(.21,.23,.20),.3),.001)
text('phone LCD text','17:42   00',(.644,-.140,.881),.008,ink,(0,0,0))
# Binder spine seams, pull rings and meaningful labels.
for x in (-1.02,-.965,-.91):
 for z in (.795,1.163):box('binder worn spine edge',(x,.27,z),(.042,.004,.006),keys,.001)
 bpy.ops.mesh.primitive_torus_add(major_radius=.008,minor_radius=.0017,major_segments=24,minor_segments=8,location=(x,.268,.861),rotation=(math.pi/2,0,0));bpy.context.object.name='binder pull ring';bpy.context.object.data.materials.append(chrome)
for x,label in zip((-1.02,-.965,-.91),('2019','2020','2021')):text('binder archive year',label,(x-.012,.266,1.05),.007,ink)
# Two stacked trays support the papers instead of a floating stack.
for z in (.783,.820):
 box('document tray base',(-.65,.15,z),(.277,.332,.004),trim,.003)
 for x in (-.789,-.511):box('document tray rim',(x,.15,z+.014),(.004,.33,.032),metal,.002)
 box('document tray back',(-.65,.314,z+.014),(.28,.004,.032),metal,.002)
for o in bpy.data.objects:
 if o.name.startswith('paper in tray') or o.name.startswith('printed rows'):o.location.z+=.041
text('invoice title','EXPENSE REPORT',(-.750,.255,.851),.012,ink,(0,0,0))
# A steel paper clip rests on the loose note.
cable('paper clip',[(.162,-.358,.785),(.175,-.358,.785),(.177,-.347,.785),(.177,-.33,.785),(.169,-.326,.785),(.163,-.332,.785),(.163,-.350,.785),(.170,-.350,.785),(.171,-.334,.785)],.0009)
bpy.data.objects['paper clip'].data.materials.clear();bpy.data.objects['paper clip'].data.materials.append(chrome)
# Subtle dried coffee ring. Slightly uneven curve, not a flat decal.
stain=mat('old coffee ring',(.16,.145,.12),.9)
pts=[]
for i in range(90):
 a=i/89*math.tau;rr=.041+.001*math.sin(a*7);pts.append((-.46+rr*math.cos(a),-.20+rr*math.sin(a),.7783))
cable('old mug ring',pts,.0008);bpy.data.objects['old mug ring'].data.materials.clear();bpy.data.objects['old mug ring'].data.materials.append(stain)
# Slightly warped notice corners and smaller print replace abstract dark bars.
for o in list(bpy.data.objects):
 if o.name.startswith('notice rule'):bpy.data.objects.remove(o,do_unlink=True)
for x,z in [(-.58,1.43),(.56,1.50)]:
 for j,line in enumerate(['DEPARTMENT 04','REVIEW / APPROVAL','-----------------------','MON  08:30 - 17:30','TUE  08:30 - 17:30','WED  08:30 - 17:30','THU  08:30 - 17:30','FRI  08:30 - 17:30','PLEASE KEEP ON FILE']):text('notice small print',line,(x-.106,.598,z+.065-j*.023),.0085,ink)
# Wall clock: matte bezel, paper dial and steel hands, motionless at 17:42.
clockx,clockz=.78,1.73
for name,r,depth,y,material in [('clock case',.105,.024,.586,trim),('clock face',.091,.004,.570,paper)]:
 o=cyl(name,(clockx,y,clockz),r,depth,material);o.rotation_euler=(math.pi/2,0,0)
for i in range(12):
 a=i*math.tau/12;o=box('clock tick',(clockx+math.sin(a)*.079,.565,clockz+math.cos(a)*.079),(.002,.002,.009),ink,.0005);o.rotation_euler.y=a
for name,a,length,width in [('minute',42/60*math.tau,.071,.0025),('hour',(5+42/60)/12*math.tau,.047,.004)]:
 o=box('clock '+name,(clockx+math.sin(a)*length/2,.561,clockz+math.cos(a)*length/2),(width,.003,length),ink,.001);o.rotation_euler.y=a
# Thin lateral trim explains partition construction; base remains anchored.
for x in (-1.208,1.208):
 box('side panel top trim',(x,-.30,2.10),(.023,1.9,.032),trim,.004)
 box('side panel rail',(x,-.30,.63),(.022,1.9,.020),trim,.003)
# The game task can supply a control-free end card at this stable path.
ending=OUT/'assets'/'ending-screen.png'
if ending.exists():
 material=bpy.data.materials['live game replacement'];node=next(n for n in material.node_tree.nodes if n.type=='TEX_IMAGE');node.image=bpy.data.images.load(str(ending));node.image.pack()
scene.frame_set(192);scene.cycles.samples=48;scene.render.resolution_percentage=100
for image in bpy.data.images:
 if image.source=='FILE' and not image.packed_file:image.pack()
scene.render.filepath=str(OUT/'office-polished.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'office-ending-polished.blend'))
bpy.ops.render.render(write_still=True)
if '--preview' in sys.argv:
 scene.render.resolution_percentage=50;scene.cycles.samples=16;frames=OUT/'polished-frames';frames.mkdir(exist_ok=True)
 for frame in range(1,193):
  scene.frame_set(frame);scene.render.filepath=str(frames/f'{frame-1:04d}.png');bpy.ops.render.render(write_still=True)
(OUT/'polish-manifest.json').write_text(json.dumps({'source':'build_office_polish.py','blend':'office-ending-polished.blend','hero':[1280,720,48],'engine':'Cycles','device':backend,'screen':'ending-screen.png' if ending.exists() else 'missing ending card','integration':'not integrated','duration':8,'fps':24},indent=2))
