import bpy, math, json
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)

def mat(name,c,rough=.7,noise=False):
 m=bpy.data.materials.new(name); m.diffuse_color=(*c,1); m.use_nodes=True
 n=m.node_tree.nodes; p=n.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*c,1); p.inputs['Roughness'].default_value=rough
 if noise:
  t=n.new('ShaderNodeTexNoise'); t.inputs['Scale'].default_value=160
  b=n.new('ShaderNodeBump'); b.inputs['Strength'].default_value=.16; b.inputs['Distance'].default_value=.008
  m.node_tree.links.new(t.outputs['Fac'],b.inputs['Height']); m.node_tree.links.new(b.outputs['Normal'],p.inputs['Normal'])
 return m
fabric=mat('grey partition fabric',(.19,.20,.195),.95,True)
trim=mat('aged charcoal trim',(.075,.08,.078),.65)
desk=mat('grey laminate',(.29,.29,.27),.62,True)
plastic=mat('old grey PC plastic',(.12,.13,.125),.5,True)
keys=mat('worn keycaps',(.23,.235,.22),.6)
paper=mat('old paperwork',(.49,.49,.45),.95)
ink=mat('toner',(.11,.115,.11))
metal=mat('filing cabinet',(.17,.18,.175),.5)

def box(name,loc,dim,material,bevel=.008):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.name=name; o.dimensions=dim
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 o.data.materials.append(material)
 if bevel:
  m=o.modifiers.new('manufactured rounded edges','BEVEL'); m.width=bevel; m.segments=3
  o.modifiers.new('weighted surface normals','WEIGHTED_NORMAL')
 return o

def cyl(name,loc,r,depth,material):
 bpy.ops.mesh.primitive_cylinder_add(vertices=48,radius=r,depth=depth,location=loc)
 o=bpy.context.object; o.name=name; o.data.materials.append(material)
 m=o.modifiers.new('rim bevel','BEVEL');m.width=.003;m.segments=3
 o.modifiers.new('surface normals','WEIGHTED_NORMAL');return o

def cable(name,pts,r=.005):
 c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=r;c.bevel_resolution=3
 s=c.splines.new('BEZIER');s.bezier_points.add(len(pts)-1)
 for p,co in zip(s.bezier_points,pts):p.co=co;p.handle_left_type='AUTO';p.handle_right_type='AUTO'
 o=bpy.data.objects.new(name,c);bpy.context.collection.objects.link(o);o.data.materials.append(trim)

def text(name,value,loc,size,material,rotation=(math.pi/2,0,0)):
 c=bpy.data.curves.new(name,'FONT');c.body=value;c.size=size;c.extrude=.0001
 o=bpy.data.objects.new(name,c);bpy.context.collection.objects.link(o);o.location=loc;o.rotation_euler=rotation;o.data.materials.append(material)
 return o

# Metres. Front faces point toward negative Y. No person or chair in camera path.
box('floor',(0,0,-.05),(5,5,.10),mat('office carpet',(.065,.07,.065),1,True))
box('back fabric partition',(0,.65,1.05),(2.55,.075,2.10),fabric)
box('left fabric partition',(-1.25,-.30,1.05),(.075,1.9,2.10),fabric)
box('right fabric partition',(1.25,-.30,1.05),(.075,1.9,2.10),fabric)
for x in (-1.25,0,1.25):box('partition upright',(x,.596,1.05),(.024,.03,2.10),trim,.003)
box('partition top',(0,.65,2.10),(2.58,.10,.035),trim)
box('desktop',(0,-.03,.75),(2.30,.95,.055),desk)
for x in (-.98,.98):
 for y in (-.36,.29):box('desk metal leg',(x,y,.36),(.045,.045,.72),metal)
box('cable modesty panel',(0,.32,.48),(2.02,.035,.42),metal)
# Monitor chassis, visible bezel and separate future game texture surface.
box('monitor pedestal',(0,.18,.801),(.32,.22,.045),plastic)
box('monitor neck',(0,.23,.94),(.08,.08,.25),plastic)
box('monitor shell',(0,.19,1.21),(.68,.09,.43),plastic,.018)
screen=box('SCREEN_GAME_REPLACE_ME',(0,.138,1.22),(.616,.008,.3465),plastic,.002)
em=bpy.data.materials.new('temporary violet game screen');em.use_nodes=True
p=em.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(.025,.002,.06,1);p.inputs['Emission Color'].default_value=(.19,.018,.4,1);p.inputs['Emission Strength'].default_value=.65
screen.data.materials.clear();screen.data.materials.append(em)
# Simple colourful game marks are placeholders, not a captured final frame.
colours=[mat('screen violet',(.42,.07,.7)),mat('screen pink',(.8,.09,.43))]
for row in range(3):
 for i in range(9):box('placeholder game brick',(-.245+i*.061,.131,1.32-row*.033),(.05,.003,.022),colours[(i+row)%2],.001)
box('placeholder paddle',(0,.13,1.095),(.14,.003,.012),colours[0],.003)
text('monitor label','OFFICE SYSTEMS',(-.10,.139,1.015),.009,keys)
# Keyboard, mouse and wires.
box('keyboard case',(-.10,-.22,.803),(.49,.165,.03),plastic)
for row in range(5):
 for k in range(15):box('keyboard key',(-.322+k*.0305,-.276+row*.027,.823),(.025,.022,.009),keys,.002)
box('spacebar',(-.10,-.29,.833),(.16,.018,.008),keys,.002)
box('mousepad',(.38,-.22,.783),(.24,.24,.006),trim,.005)
bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=(.38,-.22,.809));o=bpy.context.object;o.name='wired mouse';o.scale=(.033,.057,.025);o.data.materials.append(plastic)
cable('mouse wire',[(.38,-.17,.80),(.43,-.04,.79),(.39,.26,.79),(.70,.33,.79)])
cable('keyboard wire',[(-.10,-.13,.805),(-.17,-.02,.79),(-.10,.35,.79)])
# PC tower and drawers.
box('PC tower',(.88,.23,1.01),(.22,.38,.47),plastic,.013)
for z in (1.12,1.18):box('drive bay',(.88,.031,z),(.18,.008,.025),trim,.002)
for z in range(8):box('tower ventilation',(.88,.029,.87+z*.012),(.15,.006,.004),trim,.001)
box('drawer pedestal',(-.89,-.01,.36),(.43,.63,.70),metal)
for z in (.20,.46,.65):
 box('drawer front',(-.89,-.335,z),(.40,.018,.17 if z<.6 else .12),desk,.003)
 box('drawer handle',(-.89,-.357,z+.025),(.14,.025,.013),trim,.003)
# Paper stacks, print lines and folders.
for j in range(7):box('paper in tray',(-.65,.15,.791+j*.0025),(.25,.31,.002),paper,.0002)
for j in range(12):box('printed rows',(-.65,.04+j*.020,.808),(.19,.0018,.0005),ink,0)
for x in (-1.02,-.965,-.91):
 box('binder',(x,.38,.98),(.044,.21,.40),trim,.004)
 box('binder label',(x,.27,1.03),(.026,.002,.09),paper,.001)
# Telephone handset and keypad.
box('office telephone',(.64,-.20,.817),(.21,.19,.07),plastic,.014)
for r in range(4):
 for c in range(3):box('phone button',(.62+c*.025,-.25+r*.023,.858),(.018,.015,.005),keys,.002)
box('telephone receiver',(.567,-.20,.885),(.037,.18,.033),trim,.012)
# Empty mug with a dark cavity and handle.
ceramic=mat('grey ceramic',(.27,.28,.265),.25)
cyl('mug body',(-.52,-.22,.835),.039,.105,ceramic)
cyl('mug dark opening',(-.52,-.22,.889),.031,.001,trim)
bpy.ops.mesh.primitive_torus_add(major_radius=.032,minor_radius=.008,major_segments=32,minor_segments=12,location=(-.566,-.22,.843),rotation=(math.pi/2,0,0));bpy.context.object.name='mug handle';bpy.context.object.data.materials.append(ceramic)
# Uninspiring notices pinned to rear wall.
for x,z,heading in [(-.58,1.43,'MONTHLY TARGETS'),(.56,1.50,'REVISED SCHEDULE')]:
 box('pinned office notice',(x,.603,z),(.25,.004,.32),paper,.001)
 text('notice heading',heading,(x-.11,.599,z+.11),.013,ink)
 for j in range(9):box('notice rule',(x,.598,z+.06-j*.025),(.205,.002,.0015),ink,0)
 cyl('pin',(x,.596,z+.147),.005,.006,trim).rotation_euler=(math.pi/2,0,0)

def area(name,loc,power,color,size,target):
 d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=color;d.shape='DISK';d.size=size
 o=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
area('cold overhead fluorescent',(0,-.10,2.6),28,(.78,.85,.83),2.0,(0,0,.7))
area('weak corridor spill',(-.5,-2.0,1.8),5,(.70,.75,.78),2,(0,0,1))
area('monitor violet spill',(0,.10,1.22),5,(.5,.12,1),.5,(0,-.5,.8))
world=bpy.data.worlds.new('unlit office');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.03,.035,.032,1);world.node_tree.nodes['Background'].inputs[1].default_value=.05;bpy.context.scene.world=world

def camera(name,loc,target,lens):
 d=bpy.data.cameras.new(name);d.lens=lens;o=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();return o
hero=camera('END_PULLBACK_WIDE',(0,-2.65,1.62),(0,.15,1.10),39)
close=camera('START_SCREEN_MATCH',(0,-.55,1.22),(0,.14,1.22),40)
scene=bpy.context.scene;scene.camera=hero;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=75
scene.view_settings.view_transform='AgX';scene.view_settings.exposure=-.9
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(OUT/'office-first-look.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'office-ending.blend'))
bpy.ops.render.render(write_still=True)
(OUT/'manifest.json').write_text(json.dumps({'stage':'first scene study, not final','blender':bpy.app.version_string,'engine':'Cycles','samples':24,'resolution':[960,540],'camera':hero.name,'screen_object':screen.name,'source':'build_office.py','animation':'not authored; start and end cameras provided','integration':'not integrated'},indent=2))
