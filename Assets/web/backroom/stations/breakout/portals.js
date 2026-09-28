// A touch on either face or the rounded rim transports the moving carrier.
const EPS = 1e-7, CLEARANCE = 2;
export const PORTAL_DEPTH = 16;
const basis = p => ({nx:Math.cos(p.angle),ny:Math.sin(p.angle),tx:-Math.sin(p.angle),ty:Math.cos(p.angle)});
const clamp = (v,a,b) => Math.max(a,Math.min(b,v));
export const portalDepth = p => clamp(Number.isFinite(p.depth)?p.depth:PORTAL_DEPTH,1,p.halfLength);
const valid = p => p && [p.x,p.y,p.angle,p.halfLength].every(Number.isFinite) && p.halfLength>=1;
function local(p,point) {
  const b=basis(p),x=point.x-p.x,y=point.y-p.y;
  return {n:x*b.nx+y*b.ny,t:x*b.tx+y*b.ty};
}
function distance(p,point) {
  const q=local(p,point),inner=Math.max(0,p.halfLength-portalDepth(p));
  return Math.hypot(q.n,Math.max(0,Math.abs(q.t)-inner));
}
// Exact swept point against the union of a rectangle and its two circular caps.
function contact(p,from,to,radius) {
  const q=local(p,from),end=local(p,to),dn=end.n-q.n,dt=end.t-q.t;
  const depth=portalDepth(p),inner=Math.max(0,p.halfLength-depth),r=depth+radius;
  if(Math.hypot(q.n,Math.max(0,Math.abs(q.t)-inner))<=r+EPS)return 0;
  let best=Infinity,lo=0,hi=1;
  for(const [start,delta,min,max] of [[q.n,dn,-r,r],[q.t,dt,-inner,inner]]) {
    if(Math.abs(delta)<EPS){if(start<min||start>max){lo=Infinity;break;}}
    else {let a=(min-start)/delta,b=(max-start)/delta;if(a>b)[a,b]=[b,a];lo=Math.max(lo,a);hi=Math.min(hi,b);}
  }
  if(lo<=hi&&lo>=0&&lo<=1)best=lo;
  const a=dn*dn+dt*dt;
  for(const centre of [-inner,inner]) {
    const y=q.t-centre,b=2*(q.n*dn+y*dt),c=q.n*q.n+y*y-r*r,disc=b*b-4*a*c;
    if(disc<0)continue;
    const t=(-b-Math.sqrt(disc))/(2*a);
    if(t>=-EPS&&t<=1+EPS)best=Math.min(best,Math.max(0,t));
  }
  return best<=1?best:null;
}

/** Swept circle/capsule touch, both faces and rim. Returns event or null.
 * halfLength is total visible tangent extent; depth is normal half-thickness.
 * Front approaches emerge from the exit front, back approaches from its back.
 * Rotation remains exit.angle-entry.angle+PI. No carrier age or reward changes.
 */
export function transitPortal(body, previous, portals, {radius=body.r||0,kind='ball',emit=()=>{}}={}) {
  if (!Array.isArray(portals) || portals.length<2 || !(radius>=0) ||
      ![radius,body.x,body.y,body.vx,body.vy,previous?.x,previous?.y].every(Number.isFinite) ||
      Math.hypot(body.x-previous.x,body.y-previous.y)<EPS) return null;
  if (body.portalExit != null) {
    const blocked=portals.find(p=>p.id===body.portalExit);
    if (!valid(blocked) || distance(blocked,previous)>portalDepth(blocked)+radius+CLEARANCE+.01)
      delete body.portalExit;
  }
  let first=null;
  for(const entry of portals) {
    if(!valid(entry)||entry.id===body.portalExit)continue;
    const pair=portals.filter(p=>p.pair===entry.pair);
    if(pair.length!==2)continue;
    const exit=pair.find(p=>p!==entry);if(!valid(exit))continue;
    const t=contact(entry,previous,body,radius);
    if(t!=null&&(!first||t<first.t))first={entry,exit,t};
  }
  if(!first)return null;
  const {entry,exit,t}=first,a=basis(entry),b=basis(exit);
  const x=previous.x+(body.x-previous.x)*t,y=previous.y+(body.y-previous.y)*t;
  const normal=body.vx*a.nx+body.vy*a.ny,tangent=body.vx*a.tx+body.vy*a.ty;
  const approach=(body.x-previous.x)*a.nx+(body.y-previous.y)*a.ny;
  const side=Math.abs(approach)>EPS?(approach<0?1:-1):((x-entry.x)*a.nx+(y-entry.y)*a.ny<0?-1:1);
  const offset=clamp((x-entry.x)*a.tx+(y-entry.y)*a.ty,-exit.halfLength,exit.halfLength);
  const dx=body.x-x,dy=body.y-y,rn=dx*a.nx+dy*a.ny,rt=dx*a.tx+dy*a.ty;
  const clearance=side*(radius+portalDepth(exit)+CLEARANCE);
  body.x=exit.x+b.nx*(clearance-rn)-b.tx*(offset+rt);
  body.y=exit.y+b.ny*(clearance-rn)-b.ty*(offset+rt);
  body.vx=-normal*b.nx-tangent*b.tx;body.vy=-normal*b.ny-tangent*b.ty;
  body.portalExit=exit.id;
  if(Array.isArray(body.trail))body.trail.length=0;
  const event={kind,entryId:entry.id,exitId:exit.id,pair:entry.pair,entrySide:side,exitSide:side,
    x:body.x,y:body.y,fromX:x,fromY:y,vx:body.vx,vy:body.vy};
  emit('portalTransit',event);return event;
}
