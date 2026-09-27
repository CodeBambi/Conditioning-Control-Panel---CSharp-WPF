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
export function portalLensTransform(entry, exit) {
  const n=.20,t=.55,look=220,ci=Math.cos(entry.angle),si=Math.sin(entry.angle),ce=Math.cos(exit.angle),se=Math.sin(exit.angle);
  const a=-n*ci*ce-t*si*se,b=-n*si*ce+t*ci*se,c=-n*ci*se+t*si*ce,d=-n*si*se-t*ci*ce;
  const x=exit.x+ce*look,y=exit.y+se*look;
  return {a,b,c,d,e:entry.x-a*x-c*y,f:entry.y-b*x-d*y};
}

export function inverseWorldTransform(m) {
  if (!m || !['a','b','c','d','e','f'].every(k => Number.isFinite(m[k]))) return null;
  const det = m.a * m.d - m.b * m.c;
  if (Math.abs(det) < 1e-10) return null;
  return { a: m.d / det, b: -m.b / det, c: -m.c / det, d: m.a / det,
    e: (m.c * m.f - m.d * m.e) / det, f: (m.b * m.e - m.a * m.f) / det };
}

export function createPortalRenderer({ software = false } = {}) {
  let world = null, context = null;
  const pulses = new Map();
  function event(name, d) {
    if (name === 'wall') pulses.clear();
    if (name !== 'portalTransit') return;
    for (const id of [d.entryId, d.exitId]) {
      if (pulses.size >= 8 && !pulses.has(id)) pulses.delete(pulses.keys().next().value);
      pulses.set(id, 1);
    }
  }
  function draw(g, source, s, { now = 0, dt = 0, reduced = false } = {}) {
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
    const pairs = [...new Set(paired.map(([p]) => p.pair))];
    for (const [entry, exit] of paired) {
      const depth = Math.min(34, Math.max(20, entry.halfLength * .38));
      const length = entry.halfLength, pulse = pulses.get(entry.id) || 0;
      const tint = grey ? '#c7cad4' : '#82f1e0', light = grey ? '#eff0f3' : '#ffe3ae';
      // Clip in the entry frame, then transform the already drawn exit world into it.
      g.save(); g.translate(entry.x,entry.y); g.rotate(entry.angle);
      g.beginPath(); g.ellipse(0,0,depth,length,0,0,TAU); g.clip();
      g.fillStyle = grey ? '#24242c' : '#101a24'; g.fillRect(-depth,-length,depth*2,length*2);
      g.rotate(-entry.angle); g.translate(-entry.x,-entry.y);
      const m = portalLensTransform(entry,exit); g.transform(m.a,m.b,m.c,m.d,m.e,m.f);
      g.drawImage(world,0,0,w,h); g.restore();
      g.save(); g.translate(entry.x,entry.y); g.rotate(entry.angle);
      const ring = (rx,ry,start=0,end=TAU) => { g.beginPath(); g.ellipse(0,0,rx,ry,0,start,end); g.stroke(); };
      g.strokeStyle = '#10111eee'; g.lineWidth = 12; ring(depth+2,length+2);
      g.strokeStyle = tint; g.globalAlpha = .13 + pulse*.15; g.lineWidth = 20; ring(depth+4,length+4);
      g.globalAlpha = .95; g.lineWidth = 3.5; ring(depth+2,length+2);
      g.strokeStyle = light; g.lineWidth = 1; ring(depth-1,length-1);
      // Opposing traces and cut-glass tips make the mouths read as a connected mechanism.
      const phase = reduced ? .35 : now * .7;
      for (let i=0;i<3;i++) {
        const a=phase+i*TAU/3;
        g.strokeStyle = grey ? '#8b909e' : '#f6a5d5'; g.lineWidth=2.5; g.globalAlpha=.8;
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
      // The shared letter identifies a pair; the chevron reads its outward normal.
      g.globalAlpha=.85;g.strokeStyle=light;g.lineWidth=1.5;g.beginPath();
      g.moveTo(depth+12,-5);g.lineTo(depth+18,0);g.lineTo(depth+12,5);g.stroke();
      g.save();g.translate(depth+32,0);g.rotate(-entry.angle);
      g.fillStyle=grey?'#24242e':'#142831';g.beginPath();g.arc(0,0,9,0,TAU);g.fill();
      g.fillStyle=light;g.textAlign='center';g.textBaseline='middle';g.font='700 11px system-ui';
      g.fillText(String.fromCharCode(65+pairs.indexOf(entry.pair)),0,.5);g.restore();
      if (pulse && !reduced) { g.strokeStyle=light;g.globalAlpha=pulse*.8;g.lineWidth=2;ring(depth+4+(1-pulse)*15,length+4+(1-pulse)*15); }
      g.restore();
    }
    if (s.balls?.some(b => b.stuck && !b.lost)) {
      g.save();g.globalAlpha=.85;g.fillStyle=grey?'#dedee4':'#cfefe4';
      g.textAlign='center';g.font='700 13px system-ui';
      g.fillText('EITHER END. SAME MOMENTUM.',w/2,h-80);g.restore();
    }
  }
  return { draw, event, dispose() { world = context = null; pulses.clear(); } };
}
