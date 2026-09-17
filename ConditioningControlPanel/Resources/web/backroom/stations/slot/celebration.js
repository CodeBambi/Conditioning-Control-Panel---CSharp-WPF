// A single bounded canvas for win glitter, confetti and reel portraits. No extra WebGL context.
export const ECHO_MS = 400;
export function celebrationCounts(tier, pay, still = false) {
  if (still || !(pay > 0)) return { sparks: 0, confetti: 0 };
  const t = Math.max(1, Math.min(4, Math.floor(tier) || 1));
  return { sparks: [0, 48, 100, 170, 240][t], confetti: [0, 0, 0, 72, 160][t] };
}
export function echoReels(symbols = [], effects = []) {
  const ids = effects.map(f => f.id || f);
  return symbols.flatMap((symbol, index) => {
    const kind = String(symbol).replace(/[0-9]+$/, '');
    const matches = ids.some(id => id.startsWith('fx.gif_') ? kind === 'gif'
      : id.startsWith('fx.spiral_') ? kind === 'spiral'
      : id.startsWith('fx.sub_') ? kind === 'sub'
      : id === 'fx.melt' ? kind === 'melt' : id === 'fx.jackpot' ? kind === 'emi' : false);
    return matches ? [index] : [];
  }).slice(0, 3);
}
const COLORS = ['#ffe6a0', '#ffcc63', '#ff7ddb', '#bd9aff', '#83f9ed', '#ffffff'];
export function createCelebration({ mount, still, portrait, point }) {
  const canvas = document.createElement('canvas'), g = canvas.getContext('2d');
  canvas.className = 'slot-celebration'; canvas.setAttribute('aria-hidden', 'true');
  Object.assign(canvas.style, { position: 'absolute', inset: '0', width: '100%', height: '100%', pointerEvents: 'none', zIndex: '3' });
  mount.append(canvas);
  let particles = [], echoes = [], raf = 0, disposed = false;
  function clear() {
    cancelAnimationFrame(raf); raf = 0; particles = []; echoes = [];
    g.clearRect(0, 0, canvas.width, canvas.height);
  }
  function wake() { if (!raf && !disposed) raf = requestAnimationFrame(frame); }
  function frame(now) {
    raf = 0;
    if (disposed || still() || document.hidden) { clear(); return; }
    const rect = mount.getBoundingClientRect(), dpr = Math.min(1.5, devicePixelRatio || 1);
    const w = Math.max(1, rect.width), h = Math.max(1, rect.height);
    if (canvas.width !== Math.round(w*dpr) || canvas.height !== Math.round(h*dpr)) {
      canvas.width = Math.round(w*dpr); canvas.height = Math.round(h*dpr);
    }
    g.setTransform(dpr, 0, 0, dpr, 0, 0); g.clearRect(0, 0, w, h);
    particles = particles.filter(p => now - p.at < p.life);
    for (const p of particles) {
      const age = (now-p.at)/1000, q = (now-p.at)/p.life;
      const x = p.x + p.vx*age, y = p.y + p.vy*age + (p.confetti ? 105 : 190)*age*age;
      g.save(); g.translate(x,y); g.rotate(p.angle+age*p.turn);
      g.globalAlpha = Math.min(1, (1-q)*3); g.fillStyle = p.color;
      if (p.confetti) { g.scale(Math.cos(age*8+p.angle),1); g.fillRect(-p.size,-p.size*.4,p.size*2,p.size*.8); }
      else {
        g.globalCompositeOperation = 'lighter';
        const s = p.size*(1-q*.7);
        g.beginPath(); g.moveTo(0,-s*2); g.lineTo(s*.3,-s*.3); g.lineTo(s*2,0);
        g.lineTo(s*.3,s*.3); g.lineTo(0,s*2); g.lineTo(-s*.3,s*.3);
        g.lineTo(-s*2,0); g.lineTo(-s*.3,-s*.3); g.closePath(); g.fill();
      }
      g.restore();
    }
    echoes = echoes.filter(e => now-e.at < ECHO_MS);
    for (const e of echoes) {
      const q = Math.min(1,(now-e.at)/ECHO_MS);
      for (let trail=4; trail>=0; trail--) {
        const u = Math.max(0,q-trail*.055), zoom = 1 + 5*u*u;
        const x = e.x + (w*.5-e.x)*u*.65, y=e.y+(h*.48-e.y)*u*.65;
        g.save(); g.globalAlpha = (trail ? .15 : .9)*Math.min(1,(1-q)*5);
        g.translate(x,y); g.rotate(e.tilt*u); g.scale(zoom,zoom);
        g.drawImage(e.canvas,-e.w/2,-e.h/2,e.w,e.h); g.restore();
      }
    }
    if (particles.length || echoes.length) wake();
  }
  return {
    burst(tier, pay) {
      if (disposed) return;
      const count=celebrationCounts(tier,pay,still());
      const rect=mount.getBoundingClientRect(), origin=point?.() || {x:rect.left+rect.width/2,y:rect.top+rect.height*.5};
      const now=performance.now(), spread=Math.min(1,rect.width/800);
      for (let i=0; i<count.sparks+count.confetti; i++) {
        const confetti=i>=count.sparks, angle=Math.random()*Math.PI*2;
        particles.push({ at:now, life:confetti?1800+Math.random()*1000:650+Math.random()*900,
          x:origin.x-rect.left+(Math.random()-.5)*rect.width*.35, y:origin.y-rect.top,
          vx:Math.cos(angle)*(70+Math.random()*260)*spread, vy:-(70+Math.random()*300),
          angle, turn:(Math.random()-.5)*12, size:confetti?3+Math.random()*3:1+Math.random()*2.5,
          confetti, color:COLORS[i%COLORS.length] });
      }
      particles=particles.slice(-420); wake();
    },
    echo(indices) {
      if (disposed || still()) return;
      const rect=mount.getBoundingClientRect();
      echoes=indices.map(i => {
        const p=portrait(i); if (!p) return null;
        return {...p, x:p.x-rect.left, y:p.y-rect.top, at:performance.now(), tilt:(i-1)*.08};
      }).filter(Boolean); wake();
    },
    clear,
    dispose() { if(disposed)return; disposed=true; clear(); canvas.remove(); },
    debug() { return { particles:particles.length, echoes:echoes.length }; },
  };
}
