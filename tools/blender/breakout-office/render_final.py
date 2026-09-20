"""Render the shipping clip from the polished scene; source never touches the UI."""
import bpy,json
from pathlib import Path
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view
OUT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(OUT/'office-ending-polished.blend'))
s=bpy.context.scene;s.frame_set(1)
for pos,expected in [((-.308,.131,1.04675),(0,0)),((.308,.131,1.39325),(1,1))]:
 p=world_to_camera_view(s,s.camera,Vector(pos));assert max(abs(p.x-expected[0]),abs(p.y-expected[1]))<.001
assert all(i.packed_file for i in bpy.data.images if i.source=='FILE')
s.cycles.device='CPU';backend='CPU'
try:
 p=bpy.context.preferences.addons['cycles'].preferences
 for mode in ('OPTIX','CUDA'):
  try:
   p.compute_device_type=mode;p.get_devices();available=[d for d in p.devices if d.type==mode]
   if available:
    for d in p.devices:d.use=d in available
    s.cycles.device='GPU';backend=mode;break
  except Exception:pass
except Exception:pass
s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100;s.cycles.samples=24;s.cycles.use_denoising=True
frames=OUT/'final-frames';frames.mkdir(exist_ok=True)
for frame in range(1,193):
 target=frames/f'{frame-1:04d}.png'
 s.frame_set(frame);s.render.filepath=str(target);bpy.ops.render.render(write_still=True)
(OUT/'final-render.json').write_text(json.dumps({'blender':bpy.app.version_string,'engine':'Cycles','device':backend,'samples':24,'denoise':True,'resolution':[1280,720],'fps':24,'frames':192,'seconds':8,'screen_fit_verified':True,'packed_images':True,'colour_management':'AgX exposure -0.9','screen':'same ending-card.js artwork as runtime'},indent=2))
