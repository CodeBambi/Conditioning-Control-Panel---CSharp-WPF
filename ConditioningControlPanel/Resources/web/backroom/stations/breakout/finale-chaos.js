// Deterministic, bounded finale choreography shared by collision and rendering.
export const FINALE_REBUILD_SECONDS = 4.8;
export const FINALE_FEED_LIFE = 36;
export const FINALE_BRICK_LIMIT = 154;
export const FINALE_PULSE_PERIOD = 5.8;
export const FINALE_APPROACH_FRACTION = .2;
export const FINALE_GALAXY_TURN = 3.8;
export const FINALE_THREAD_COUNT = 5, FINALE_GATE_COUNT = 34;
const TAU = Math.PI * 2;
const motifs = ['tide', 'spell', 'pendulum', 'iris', 'rhythm', 'dome'];
export function finalePulse(age, radius, reduced = false) {
  if (reduced) return 0;
  const waveRadius = ((age % FINALE_PULSE_PERIOD) - .8) * 240;
  const distance = radius - waveRadius;
  return Math.abs(distance) < 100 ? Math.sin(distance * Math.PI / 100) * Math.cos(distance * Math.PI / 200) * 4 : 0;
}
export function finaleChaosMetadata(id) {
  const slot=id%12, cluster=Math.floor(id/10), motif=motifs[cluster%motifs.length];
  const finaleHinge=id%37===0;
  const width=finaleHinge?26:id%11===0?26:id%7===0?23:id%5===0?18:20;
  return {chaosId:id,finaleCluster:cluster,finaleMotif:motif,finaleHinge,
    finaleLetter:motif==='spell'?'LETGORELAX'[slot%10]:null,
    w:width*1.38,h:(finaleHinge?26:width*.55)*1.38};
}
// The approach continues the curve outside the screen instead of forming radial rows.
function flowPoint(arm,q,f,w,h) {
  const angle=arm*TAU/FINALE_THREAD_COUNT+.25+q*FINALE_GALAXY_TURN;
  const c=Math.cos(angle),s=Math.sin(angle),rx=Math.min(550,w*.45),ry=Math.min(280,h*.39);
  const outer=1/Math.sqrt(c*c/(rx*rx)+s*s/(ry*ry));
  const radius=q<0?outer+(Math.hypot(w,h)-outer)*Math.pow(-q/.25,1.4):126+(Math.max(160,outer)-126)*(1-q);
  return {x:f.centreX+c*radius,y:f.centreY+s*radius,angle,radius};
}
export function finaleChaosPose(br, f, w, h, reduced = false) {
  const id=br.chaosId,progress=Math.max(0,Math.min(1,br.feedAge/FINALE_FEED_LIFE));
  const baseQ=(progress-FINALE_APPROACH_FRACTION)/(1-FINALE_APPROACH_FRACTION),time=reduced?0:f.stageAge;
  // Occasional close pairs interrupt the cadence without making solid walls.
  const q=baseQ-(Math.floor(id/FINALE_THREAD_COUNT)%6===1?.016:0)*Math.sin(Math.max(0,baseQ)*Math.PI);
  const point=flowPoint(id%FINALE_THREAD_COUNT,q,f,w,h),next=flowPoint(id%FINALE_THREAD_COUNT,Math.min(1,q+.001),f,w,h);
  const tangent=Math.atan2(next.y-point.y,next.x-point.x);
  const envelope=Math.sin(Math.max(0,q)*Math.PI);
  const drift=reduced?0:Math.sin(time*(.45+(id%4)*.13)+id*1.73)*8;
  // Small motif motions recall earlier walls without adding emitters or render surfaces.
  const motif=br.finaleMotif, phase=time*1.4+br.finaleCluster;
  const legacyRadial=reduced?0:motif==='dome'?Math.sin(phase*.6)*6:motif==='rhythm'?Math.pow(Math.max(0,Math.cos(time*3.2)),8)*7:0;
  const legacySide=reduced?0:motif==='tide'?Math.sin(phase+id*.3)*9:motif==='iris'?Math.sin(time*2.2+id)*4:0;
  const radial=envelope*(Math.sin(id*2.399)*22+drift+legacyRadial+finalePulse(time,point.radius,reduced));
  const sideways=envelope*(Math.sin(id*5.17)*18+legacySide+(reduced?0:Math.cos(time*.7+id)*6));
  const radius=Math.max(126,point.radius+radial);
  let angle=point.angle+sideways/radius;
  // Keep one ball-width approach through the loose field below the circular defenses.
  const gap=Math.atan2(Math.sin(angle-Math.PI/2),Math.cos(angle-Math.PI/2));
  const clearance=(Math.hypot(br.w,br.h)/2+12)/radius;
  if(Math.abs(gap)<clearance)angle+=Math.sign(gap||1)*(clearance-Math.abs(gap));
  const tilt=id%9===0?Math.PI/2:id%7===0?0:tangent+Math.sin(id*2.1)*.8;
  return {x:f.centreX+Math.cos(angle)*radius-br.w/2,y:f.centreY+Math.sin(angle)*radius-br.h/2,
    angle:tilt+(reduced?0:Math.sin(time*(.65+(id%3)*.2)+id)*.18)};
}
export function finaleWhirlPose(from, target, f, progress, width, height) {
  const p=Math.min(1,Math.max(0,progress)), ease=p*p*(3-2*p);
  const sx=from.x+width/2-f.centreX,sy=from.y+height/2-f.centreY;
  const tx=target.x+width/2-f.centreX,ty=target.y+height/2-f.centreY;
  const start=Math.atan2(sy,sx), end=Math.atan2(ty,tx);
  // Keep the moving destination on the same angular branch through +/- PI.
  if(from.whirlEnd===undefined) from.whirlEnd=start+((end-start)%TAU+TAU)%TAU+TAU;
  else from.whirlEnd+=Math.atan2(Math.sin(end-from.whirlEnd),Math.cos(end-from.whirlEnd));
  const delta=from.whirlEnd-start;
  const angle=start+delta*ease;
  const radius=Math.hypot(sx,sy)*(1-ease)+Math.hypot(tx,ty)*ease+Math.sin(p*Math.PI)*100;
  return {x:f.centreX+Math.cos(angle)*radius-width/2,y:f.centreY+Math.sin(angle)*radius-height/2,
    angle:(from.angle||0)*(1-ease)+target.angle*ease+Math.sin(p*Math.PI)*Math.PI};
}
