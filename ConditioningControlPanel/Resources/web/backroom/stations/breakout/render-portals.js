// Both windows share one pre-portal world image. Never render or tick the world twice.
const TAU = Math.PI * 2;
export const PORTAL_BUFFER = { width: 960, height: 540 };
const valid = p => p && ['x','y','angle','halfLength'].every(k => Number.isFinite(p[k])) && p.halfLength > 0;

/** Inverse of the physical transit rotation: exit-world coordinates into entry-world coordinates. */
export function portalViewTransform(entry, exit) {
  const angle = entry.angle - exit.angle + Math.PI, a = Math.cos(angle), b = Math.sin(angle);
  return { a, b, c: -b, d: a, e: entry.x - a * exit.x + b * exit.y,
    f: entry.y - b * exit.x - a * exit.y };
}

/** A shallow lens looks beyond the collision clearance, preserving the physical view orientation. */
export function portalLensTransform(entry, exit, side = 1) {
  const n=.10,t=.55,look=220*(side < 0 ? -1 : 1),ci=Math.cos(entry.angle),si=Math.sin(entry.angle),ce=Math.cos(exit.angle),se=Math.sin(exit.angle);
  const a=-n*ci*ce-t*si*se,b=-n*si*ce+t*ci*se,c=-n*ci*se+t*si*ce,d=-n*si*se-t*ci*ce;
  const x=exit.x+ce*look,y=exit.y+se*look;
  return {a,b,c,d,e:entry.x-a*x-c*y,f:entry.y-b*x-d*y};
}

export function portalEndStyle(portal, portals = [], grey = false) {
  const pair=portals.filter(p=>p.pair===portal.pair);
  const index=Math.max(0,pair.findIndex(p=>p.id===portal.id));
  const orange=index%2===1;
  return {label:orange?'B':'A',rgb:orange?(grey?[183,133,83]:[255,145,40]):(grey?[87,140,186]:[42,163,255]),
    light:orange?(grey?'#dcc4aa':'#ffe4b5'):(grey?'#b7cddd':'#d0f1ff'),
    shade:orange?'#33200f':'#102335'};
}

/** Both faces work. Preview the side an approaching object will emerge from. */
export function portalApproachSide(portal, snapshot, previous = 1) {
  const nx=Math.cos(portal.angle),ny=Math.sin(portal.angle);
  let side=previous,nearest=320*320;
  const groups=[snapshot.balls,snapshot.colliders,snapshot.pops,snapshot.power?.drops,snapshot.power?.shots];
  for(let i=0;i<groups.length;i++) {
    for(const body of groups[i] || []) {
      if(body.lost||body.stuck)continue;
      const x=body.x-portal.x,y=body.y-portal.y,normal=x*nx+y*ny,speed=body.vx*nx+body.vy*ny;
      const distance=x*x+y*y;
      if(normal*speed>=0 || !Number.isFinite(speed) || Math.abs(-x*ny+y*nx)>portal.halfLength+(body.r||0)+60 || distance>=nearest)continue;
      nearest=distance;side=normal<0?-1:1;
    }
    if(i===0 && nearest<320*320)return side;
  }
  return side;
}

export function inverseWorldTransform(m) {
  if (!m || !['a','b','c','d','e','f'].every(k => Number.isFinite(m[k]))) return null;
  const det = m.a * m.d - m.b * m.c;
  if (Math.abs(det) < 1e-10) return null;
  return { a: m.d / det, b: -m.b / det, c: -m.c / det, d: m.a / det,
    e: (m.c * m.f - m.d * m.e) / det, f: (m.b * m.e - m.a * m.f) / det };
}

export function createPortalRenderer({ software = false } = {}) {
  let world = null, context = null, clock = 0;
  const pulses = new Map(), viewSides = new Map(), viewHold = new Map();
  function event(name, d) {
    if (name === 'wall') { pulses.clear(); viewSides.clear(); viewHold.clear(); }
    if (name !== 'portalTransit') return;
    if(d.entrySide){viewSides.set(d.entryId,d.entrySide);viewHold.set(d.entryId,clock+.55);}
    if(d.exitSide){viewSides.set(d.exitId,d.exitSide);viewHold.set(d.exitId,clock+.55);}
    for (const id of [d.entryId, d.exitId]) {
      if (pulses.size >= 8 && !pulses.has(id)) pulses.delete(pulses.keys().next().value);
      pulses.set(id, 1);
    }
  }
  function draw(g, source, s, { now = 0, dt = 0, reduced = false } = {}) {
    clock = now;
    const portals = (s.portals || []).filter(valid).slice(0, 8);
    const paired = portals.map(p => [p, portals.find(q => q !== p && q.pair === p.pair)]).filter(([,q]) => q);
    if (!paired.length) return;
    const w = s.w || 1280, h = s.h || 720, inverse = inverseWorldTransform(g.getTransform());
    if (!inverse) return;
    const scale = Math.min(1, PORTAL_BUFFER.width / w, PORTAL_BUFFER.height / h);
    if (!world) { world = document.createElement('canvas'); context = world.getContext('2d', { willReadFrequently: software }); }
    const bw = Math.max(1, Math.round(w * scale)), bh = Math.max(1, Math.round(h * scale));
    if (world.width !== bw || world.height !== bh) { world.width = bw; world.height = bh; }
    context.setTransform(1,0,0,1,0,0); context.clearRect(0,0,bw,bh);
    context.setTransform(scale*inverse.a,scale*inverse.b,scale*inverse.c,scale*inverse.d,scale*inverse.e,scale*inverse.f);
    context.drawImage(source,0,0);
    for (const [id, value] of pulses) { const next = Math.max(0, value - dt * 3); if (next) pulses.set(id,next); else pulses.delete(id); }
    const grey = s.state === 'grey';
    const pairIds = [...new Set(paired.map(([p]) => p.pair))];
    for (const [entry, exit] of paired) {
      const depth = Math.max(14, Math.min(18, entry.depth || 16));
      const length = entry.halfLength, pulse = pulses.get(entry.id) || 0;
      const style=portalEndStyle(entry,portals,grey),tint='rgb('+style.rgb.join(',')+')',light=style.light;
      let side=viewSides.get(entry.id)||1;
      if(now>=(viewHold.get(entry.id)||0)) {
        const next=portalApproachSide(entry,s,side);
        if(next!==side){side=next;viewSides.set(entry.id,side);viewHold.set(entry.id,now+.35);}
      }
      // Clip in the entry frame, then transform the already drawn exit world into it.
      g.save(); g.translate(entry.x,entry.y); g.rotate(entry.angle);
      g.beginPath(); g.ellipse(0,0,depth,length,0,0,TAU); g.clip();
      g.fillStyle = grey ? '#24242c' : style.shade; g.fillRect(-depth,-length,depth*2,length*2);
      g.rotate(-entry.angle); g.translate(-entry.x,-entry.y);
      const m = portalLensTransform(entry,exit,side); g.transform(m.a,m.b,m.c,m.d,m.e,m.f);
      g.drawImage(world,0,0,w,h); g.restore();
      g.save(); g.translate(entry.x,entry.y); g.rotate(entry.angle);
      const ring = (rx,ry,start=0,end=TAU) => { g.beginPath(); g.ellipse(0,0,rx,ry,0,start,end); g.stroke(); };
      g.strokeStyle = '#10111eee'; g.lineWidth = 8; ring(depth+2,length+2);
      g.strokeStyle = tint; g.globalAlpha = .13 + pulse*.15; g.lineWidth = 12; ring(depth+4,length+4);
      g.globalAlpha = .95; g.lineWidth = 3.5; ring(depth+2,length+2);
      g.strokeStyle = light; g.lineWidth = 1; ring(depth-1,length-1);
      // Opposing traces and cut-glass tips make the mouths read as a connected mechanism.
      const phase = reduced ? .35 : now * .7;
      for (let i=0;i<3;i++) {
        const a=phase+i*TAU/3;
        g.strokeStyle = tint; g.lineWidth=2.5; g.globalAlpha=.8;
        ring(depth+6,length+6,a,a+.52);
      }
      for (const sign of [-1,1]) {
        g.globalAlpha=1; g.fillStyle=light; g.beginPath();
        g.moveTo(0,sign*(length+11)); g.lineTo(4,sign*(length+4)); g.lineTo(0,sign*(length-2)); g.lineTo(-4,sign*(length+4));g.closePath();g.fill();
      }
      if (!reduced && !grey) for (let i=0;i<12;i++) {
        const a=i*2.39996+now*.55, drift=3+5*(.5+.5*Math.sin(now*1.5+i));
        const x=Math.cos(a)*(depth+drift+5),y=Math.sin(a)*(length+drift+5);
        g.globalAlpha=.25+.45*Math.pow(.5+.5*Math.sin(now*2+i),2);g.fillStyle=i%3? tint:light;
        g.fillRect(x-1,y-1,2,2);
      }
      // A lone blue/orange pair needs no labels. Multiple pairs share a letter at both ends.
      if (pairIds.length > 1) {
        g.save();g.translate(0,-length-19);g.rotate(-entry.angle);
        g.globalAlpha=.9;g.fillStyle=grey?'#24242e':style.shade;g.beginPath();g.arc(0,0,9,0,TAU);g.fill();
        g.fillStyle=light;g.textAlign='center';g.textBaseline='middle';g.font='700 11px system-ui';
        g.fillText(String.fromCharCode(65+pairIds.indexOf(entry.pair)),0,.5);g.restore();
      }
      if (pulse && !reduced) { g.strokeStyle=light;g.globalAlpha=pulse*.8;g.lineWidth=2;ring(depth+4+(1-pulse)*15,length+4+(1-pulse)*15); }
      g.restore();
    }
    if (s.balls?.some(b => b.stuck && !b.lost)) {
      g.save();g.globalAlpha=.85;g.fillStyle=grey?'#dedee4':'#cfefe4';
      g.textAlign='center';g.font='700 13px system-ui';
      g.fillText('HIT EITHER SIDE. COME OUT THE OTHER.',w/2,h-80);g.restore();
    }
  }
  return { draw, event, dispose() { world = context = null; pulses.clear(); viewSides.clear(); viewHold.clear(); } };
}
