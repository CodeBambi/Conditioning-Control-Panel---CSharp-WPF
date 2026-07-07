/* ============================================================================
 * fieldFx.js - the run's 2D canvas FX layer (the WPF ChaosFieldFxOverlay port):
 * Bound tether threads, E-Stim lightning bolts, the VibePopping pointer trail,
 * Aftermath residue zones and rabbit sparkle trails (Tail-Plug). One fullscreen
 * click-through canvas UNDER the bubbles, redrawn from chaosField's rAF.
 * ==========================================================================*/

export function createFieldFx(hud) {
  const canvas = document.createElement('canvas');
  canvas.className = 'cf-fieldfx';
  hud.insertBefore(canvas, hud.firstChild);
  const ctx = canvas.getContext('2d');

  function resize() {
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
  }
  resize();
  window.addEventListener('resize', resize);

  const bolts = [];      // { pts:[{x,y}..], life, total }
  const residues = [];   // { x, y, r, life, total }
  const trail = [];      // vibe pointer trail: { x, y, life }
  const sparkles = [];   // rabbit tail-plug sparkles: { x, y, life, hue }
  let tethers = [];      // per-frame: [{ ax, ay, bx, by, fraying }]
  let vibeOn = false;

  /** A jagged lightning bolt from a to b (E-Stim / Body Buzz / Electrified Rabbits). */
  function addBolt(ax, ay, bx, by) {
    const pts = [{ x: ax, y: ay }];
    const segs = 6;
    for (let i = 1; i < segs; i++) {
      const t = i / segs;
      pts.push({
        x: ax + (bx - ax) * t + (Math.random() - 0.5) * 34,
        y: ay + (by - ay) * t + (Math.random() - 0.5) * 34,
      });
    }
    pts.push({ x: bx, y: by });
    bolts.push({ pts, life: 0.28, total: 0.28 });
  }

  /** Aftermath: 2s of crackling residue at a brink-snap's pop point (170px zone). */
  function addResidue(x, y, r = 170, lifeSec = 2.0) {
    residues.push({ x, y, r, life: lifeSec, total: lifeSec });
  }

  function addSparkle(x, y) {
    sparkles.push({ x: x + (Math.random() - 0.5) * 14, y: y + (Math.random() - 0.5) * 14,
      life: 0.5, hue: 300 + Math.random() * 40 });
  }

  /** The Bound's threads, re-fed every frame by chaosField. */
  function setTethers(list) { tethers = list; }

  function setVibe(on) { vibeOn = !!on; if (!on) trail.length = 0; }
  function vibePoint(x, y) { if (vibeOn) trail.push({ x, y, life: 0.45 }); }

  /** Any residue zone covering (x,y)? chaosField polls per bubble per frame. */
  function inResidue(x, y) {
    for (const z of residues) {
      const dx = z.x - x, dy = z.y - y;
      if (dx * dx + dy * dy <= z.r * z.r) return true;
    }
    return false;
  }

  function draw(dt) {
    const any = bolts.length || residues.length || trail.length || sparkles.length || tethers.length;
    if (!any && !canvas._dirty) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    canvas._dirty = any;

    // Bound tethers: an elastic thread, reddening as the window frays.
    for (const t of tethers) {
      const midX = (t.ax + t.bx) / 2, midY = (t.ay + t.by) / 2 + 26;
      ctx.beginPath();
      ctx.moveTo(t.ax, t.ay);
      ctx.quadraticCurveTo(midX, midY, t.bx, t.by);
      ctx.strokeStyle = t.fraying ? 'rgba(255,80,80,0.85)' : 'rgba(200,180,255,0.55)';
      ctx.lineWidth = t.fraying ? 3 : 2;
      ctx.stroke();
    }

    // Residue zones: a crackling translucent disc that fades out.
    for (let i = residues.length - 1; i >= 0; i--) {
      const z = residues[i];
      z.life -= dt;
      if (z.life <= 0) { residues.splice(i, 1); continue; }
      const a = 0.28 * (z.life / z.total);
      ctx.beginPath();
      ctx.arc(z.x, z.y, z.r, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(122,224,255,${a * 0.35})`;
      ctx.fill();
      ctx.strokeStyle = `rgba(122,224,255,${a})`;
      ctx.setLineDash([6, 10]);
      ctx.lineWidth = 2;
      ctx.stroke();
      ctx.setLineDash([]);
      // stray crackle spark
      if (Math.random() < 0.3) {
        const a2 = Math.random() * Math.PI * 2, rr = Math.random() * z.r;
        ctx.fillStyle = `rgba(180,240,255,${a})`;
        ctx.fillRect(z.x + Math.cos(a2) * rr, z.y + Math.sin(a2) * rr, 2, 2);
      }
    }

    // Lightning bolts.
    for (let i = bolts.length - 1; i >= 0; i--) {
      const b = bolts[i];
      b.life -= dt;
      if (b.life <= 0) { bolts.splice(i, 1); continue; }
      const a = b.life / b.total;
      ctx.beginPath();
      ctx.moveTo(b.pts[0].x, b.pts[0].y);
      for (let j = 1; j < b.pts.length; j++) ctx.lineTo(b.pts[j].x, b.pts[j].y);
      ctx.strokeStyle = `rgba(156,92,255,${0.9 * a})`;
      ctx.lineWidth = 2.5;
      ctx.stroke();
      ctx.strokeStyle = `rgba(230,210,255,${0.7 * a})`;
      ctx.lineWidth = 1;
      ctx.stroke();
    }

    // Vibe trail: a warm fading ribbon behind the pointer.
    for (let i = trail.length - 1; i >= 0; i--) {
      const p = trail[i];
      p.life -= dt;
      if (p.life <= 0) { trail.splice(i, 1); continue; }
      const a = p.life / 0.45;
      ctx.beginPath();
      ctx.arc(p.x, p.y, 10 * a + 3, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(255,176,58,${0.35 * a})`;
      ctx.fill();
    }

    // Rabbit sparkles (Tail-Plug).
    for (let i = sparkles.length - 1; i >= 0; i--) {
      const s = sparkles[i];
      s.life -= dt;
      if (s.life <= 0) { sparkles.splice(i, 1); continue; }
      const a = s.life / 0.5;
      ctx.fillStyle = `hsla(${s.hue},95%,75%,${0.8 * a})`;
      ctx.fillRect(s.x, s.y, 3, 3);
    }
  }

  return {
    addBolt, addResidue, addSparkle, setTethers, setVibe, vibePoint, inResidue, draw,
    clear() { bolts.length = 0; residues.length = 0; trail.length = 0; sparkles.length = 0; tethers = []; ctx.clearRect(0, 0, canvas.width, canvas.height); },
    dispose() { window.removeEventListener('resize', resize); canvas.remove(); },
  };
}
