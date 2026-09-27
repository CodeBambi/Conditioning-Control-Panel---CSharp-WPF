// Three curtains share visible geometry with their collision faces.
export const SWEEP_SECONDS = 2.4, BUMPER_SECONDS = 10;
export const CURTAIN_ROWS = 8, CURTAIN_COLS = 6, ANCHOR_GUARDS = 8;
export function createPendulums(w, h) {
  return [0, 1, 2].map(id => ({ id, pivotX:w*(.22+id*.28), pivotY:h*.095,
    length:h*.39, phase:id*2.1, angle:0, energy:0, mode:'hung', age:0,
    x:0,y:0,r:30,pulse:0, trail:[], struck:new Set() }));
}
export function curtainPose(p, row, col) {
  const dx=(col-(CURTAIN_COLS-1)/2)*35,dy=86+row*22,c=Math.cos(p.angle),s=Math.sin(p.angle);
  return {x:p.pivotX+dx*c+dy*s-15.5,y:p.pivotY-dx*s+dy*c-9,w:31,h:18,angle:-p.angle};
}
export function releasePendulum(p,w) {
  if(p.mode!=='hung')return;
  p.mode='sweep';p.age=0;p.fromX=p.x;p.fromY=p.y;
  p.toX=p.x<w/2?w-90:90;p.toY=p.pivotY+50;p.collapseClock=0;
  p.trail.length=0;
}
export function advancePendulum(p,dt,w,h,reduced) {
  p.age+=dt;p.pulse=Math.max(0,p.pulse-dt*3);
  if(p.mode==='hung') {
    p.energy=Math.max(0,p.energy-dt*.07);
    p.phase+=dt*1.25;
    p.angle=reduced?0:Math.sin(p.phase)*(.42+p.energy*.48);
    p.x=p.pivotX+Math.sin(p.angle)*p.length;
    p.y=p.pivotY+Math.cos(p.angle)*p.length;
  } else if(p.mode==='sweep') {
    const t=Math.min(1,p.age/SWEEP_SECONDS),u=t*t*(3-2*t);
    p.x=p.fromX+(p.toX-p.fromX)*u;
    p.y=p.fromY+(p.toY-p.fromY)*u-Math.sin(Math.PI*t)*150;
    if(t===1){p.mode='bumper';p.age=0;}
  } else if(p.mode==='bumper' && p.age>=BUMPER_SECONDS) p.mode='spent';
  if(!reduced && p.mode!=='spent') {
    p.trail.push({x:p.x,y:p.y});if(p.trail.length>18)p.trail.shift();
  } else p.trail.length=0;
}
export function collidePendulum(ball,p,speed) {
  if(p.mode==='spent')return false;
  const dx=ball.x-p.x,dy=ball.y-p.y,d=Math.hypot(dx,dy),rr=ball.r+p.r;
  if(d>=rr)return false;
  const nx=d>0?dx/d:0,ny=d>0?dy/d:1,dot=ball.vx*nx+ball.vy*ny;
  ball.x=p.x+nx*(rr+.1);ball.y=p.y+ny*(rr+.1);
  if(dot>=0)return false;
  ball.vx-=2*dot*nx;ball.vy-=2*dot*ny;
  const k=speed/(Math.hypot(ball.vx,ball.vy)||1);ball.vx*=k;ball.vy*=k;
  p.energy=Math.min(1,p.energy+.34);p.pulse=1;
  return true;
}
