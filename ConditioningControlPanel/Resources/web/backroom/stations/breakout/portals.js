// Each mouth faces its outward normal. A pair transports front-to-back crossings.
const EPS = 1e-7, CLEARANCE = 2;
const basis = p => ({nx:Math.cos(p.angle),ny:Math.sin(p.angle),tx:-Math.sin(p.angle),ty:Math.cos(p.angle)});

/** Mutates position/velocity only after a swept crossing. Returns its event, or null.
 * Rotation is exit.angle-entry.angle+PI: local tangent and offset both reverse.
 * Radius must fit BOTH mouths. Carrier age, ownership and rewards stay untouched.
 */
export function transitPortal(body, previous, portals, {radius=body.r||0,kind='ball',emit=()=>{}}={}) {
  if (!Array.isArray(portals) || portals.length < 2 || !(radius >= 0) ||
      ![body.x,body.y,body.vx,body.vy,previous?.x,previous?.y].every(Number.isFinite)) return null;
  if (body.portalExit != null) {
    const blocked=portals.find(p=>p.id===body.portalExit);
    if (!blocked) delete body.portalExit;
    else {
      const f=basis(blocked),dx=previous.x-blocked.x,dy=previous.y-blocked.y;
      if (dx*f.nx+dy*f.ny > radius+CLEARANCE+.01 || Math.abs(dx*f.tx+dy*f.ty)>blocked.halfLength+radius)
        delete body.portalExit;
    }
  }
  let first=null;
  for (const entry of portals) {
    if (entry.id===body.portalExit || !Number.isFinite(entry.angle) || !(entry.halfLength>=radius)) continue;
    const pair=portals.filter(p=>p.pair===entry.pair);
    if(pair.length!==2)continue;
    const exit=pair.find(p=>p!==entry);
    if(!exit || !(exit.halfLength>=radius) || !Number.isFinite(exit.angle))continue;
    const a=basis(entry),d0=(previous.x-entry.x)*a.nx+(previous.y-entry.y)*a.ny;
    const d1=(body.x-entry.x)*a.nx+(body.y-entry.y)*a.ny;
    if (!(d0>EPS && d1<=EPS && d1<d0)) continue;
    const t=d0/(d0-d1),x=previous.x+(body.x-previous.x)*t,y=previous.y+(body.y-previous.y)*t;
    const offset=(x-entry.x)*a.tx+(y-entry.y)*a.ty;
    if(Math.abs(offset)+radius>Math.min(entry.halfLength,exit.halfLength)+EPS)continue;
    if(!first || t<first.t)first={entry,exit,a,t,x,y,offset};
  }
  if(!first)return null;
  const {entry,exit,a,t,x,y,offset}=first,b=basis(exit);
  const normal=body.vx*a.nx+body.vy*a.ny,tangent=body.vx*a.tx+body.vy*a.ty;
  const dx=body.x-x,dy=body.y-y,rn=dx*a.nx+dy*a.ny,rt=dx*a.tx+dy*a.ty;
  body.x=exit.x+b.nx*(radius+CLEARANCE-rn)-b.tx*(offset+rt);
  body.y=exit.y+b.ny*(radius+CLEARANCE-rn)-b.ty*(offset+rt);
  body.vx=-normal*b.nx-tangent*b.tx;body.vy=-normal*b.ny-tangent*b.ty;
  body.portalExit=exit.id;
  if(Array.isArray(body.trail))body.trail.length=0;
  const event={kind,entryId:entry.id,exitId:exit.id,pair:entry.pair,x:body.x,y:body.y,
    fromX:x,fromY:y,vx:body.vx,vy:body.vy};
  emit('portalTransit',event);
  return event;
}
