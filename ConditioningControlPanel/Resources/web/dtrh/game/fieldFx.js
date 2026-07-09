/* ============================================================================
 * fieldFx.js - the run's FX layer (a faithful port of the WPF ChaosSkiaFxOverlay):
 * Bound tether threads, E-Stim lightning bolts, the VibePopping pointer trail,
 * Aftermath residue zones and rabbit sparkle trails (Tail-Plug). One fullscreen
 * click-through layer UNDER the bubbles, redrawn from chaosField's rAF.
 *
 * HOW THE DESKTOP APP GETS ITS GLOW (and how we copy it here):
 * ChaosSkiaFxOverlay does NOT use GPU Skia - it rasters CPU Skia with
 * SKBlendMode.Plus (ADDITIVE) and builds every glow from a soft white
 * radial-gradient sprite drawn in LAYERS: a wide dim bloom halo, a bright
 * body, and a hot core. Bolts = a blurred glow stroke + a white-blue core +
 * endpoint strike-flashes + 0-2 forks. That composites identically to a plain
 * 2D canvas with `globalCompositeOperation = 'lighter'` + a pre-baked radial
 * sprite. So that's exactly what this does - additive soft-sprite bloom in the
 * SAME client-pixel space the bubbles live in (no second WebGL context, no
 * coordinate offset, no WASM). Palettes mirror the desktop overlay:
 *   E-Stim  electric cyan 0x42DCE6 glow + white-blue 0xBFECFF core
 *   Vibe    warm amber 0xFFB03A + gold 0xFFE9A0 core
 *   Sparkle rabbit pink 0xFF4DC4 + gold core
 *   Residue crackle cyan 0x7AE0FF
 * ==========================================================================*/

/** hsl -> rgb (0..255), kept for the rabbit sparkles' shifting hue. */
function hsl2rgb(h, s, l) {
  h /= 360;
  const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
  const p = 2 * l - q;
  const hue = (t) => {
    if (t < 0) t += 1; if (t > 1) t -= 1;
    if (t < 1 / 6) return p + (q - p) * 6 * t;
    if (t < 1 / 2) return q;
    if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
    return p;
  };
  return [hue(h + 1 / 3) * 255, hue(h) * 255, hue(h - 1 / 3) * 255];
}

// ---- soft radial sprite (the WPF `Dot()`), one per colour, cached ----------
// White->transparent radial gradient, alpha stops 1.0 / 0.62 / 0 at 0/0.32/1,
// matching ChaosSkiaFxOverlay.Dot(). Additive `lighter` blitting of this sprite
// IS the bloom. Tinted variants are baked on demand (the WPF TintFor cache).
const SPRITE_SIZE = 128;
const _spriteCache = new Map();
function spriteFor(r, g, b) {
  const key = (r << 16) | (g << 8) | b;
  let c = _spriteCache.get(key);
  if (c) return c;
  c = document.createElement('canvas');
  c.width = c.height = SPRITE_SIZE;
  const g2 = c.getContext('2d');
  const rr = SPRITE_SIZE / 2;
  const grad = g2.createRadialGradient(rr, rr, 0, rr, rr, rr);
  grad.addColorStop(0.0, `rgba(${r},${g},${b},1)`);
  grad.addColorStop(0.32, `rgba(${r},${g},${b},0.62)`);
  grad.addColorStop(1.0, `rgba(${r},${g},${b},0)`);
  g2.fillStyle = grad;
  g2.fillRect(0, 0, SPRITE_SIZE, SPRITE_SIZE);
  _spriteCache.set(key, c);
  return c;
}
const SPR_WHITE = () => spriteFor(255, 255, 255);

export function createFieldFx(hud) {
  let disposed = false;

  const canvas = document.createElement('canvas');
  canvas.className = 'cf-fieldfx';
  hud.insertBefore(canvas, hud.firstChild);
  const ctx = canvas.getContext('2d');

  let dpr = Math.min(window.devicePixelRatio || 1, 2);
  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    const cssW = window.innerWidth, cssH = window.innerHeight;
    canvas.width = Math.max(1, Math.round(cssW * dpr));
    canvas.height = Math.max(1, Math.round(cssH * dpr));
    // CRITICAL: a <canvas> is a replaced element - with only `inset:0` and no CSS
    // width/height it renders at its raw BACKING size (cssW*dpr), not stretched to
    // the container. We must pin the CSS display size to the viewport so the *dpr
    // backing composites down 1:1; then ctx.scale(dpr) + drawing in CSS px lands
    // exactly on the bubbles (same client-px space). Omitting this double-applies
    // dpr and shifts everything toward bottom-right by a factor of dpr.
    canvas.style.width = cssW + 'px';
    canvas.style.height = cssH + 'px';
  }
  window.addEventListener('resize', resize);
  resize();

  const bolts = [];      // { pts:[{x,y}..], forks:[[{x,y}..]..], ex,ey, ax,ay, life, total }
  const residues = [];   // { x, y, r, life, total }
  const trail = [];      // vibe pointer trail: { x, y, life }
  const sparkles = [];   // rabbit tail-plug sparkles: { x, y, life, hue }
  let tethers = [];      // per-frame: [{ ax, ay, bx, by, fraying }]
  let vibeOn = false;

  // ---- jagged bolt builder (perpendicular midpoint displacement, WPF BuildBolt) ----
  function buildBolt(ax, ay, bx, by) {
    const dx = bx - ax, dy = by - ay;
    const len = Math.hypot(dx, dy);
    const mids = Math.max(2, (len / 55) | 0);
    const px = len > 0.001 ? -dy / len : 0, py = len > 0.001 ? dx / len : 0;
    const pts = [{ x: ax, y: ay }];
    for (let m = 1; m <= mids; m++) {
      const f = m / (mids + 1);
      const off = (Math.random() * 2 - 1) * 16;
      pts.push({ x: ax + dx * f + px * off, y: ay + dy * f + py * off });
    }
    pts.push({ x: bx, y: by });
    return pts;
  }

  /** A jagged lightning bolt from a to b (E-Stim / Body Buzz / Electrified Rabbits). */
  function addBolt(ax, ay, bx, by) {
    const pts = buildBolt(ax, ay, bx, by);
    const forks = [];
    const nForks = (Math.random() * 3) | 0;
    for (let k = 0; k < nForks && pts.length > 2; k++) {
      const o = pts[1 + ((Math.random() * (pts.length - 2)) | 0)];
      const ang = Math.random() * Math.PI * 2;
      const flen = 20 + Math.random() * 55;
      forks.push(buildBolt(o.x, o.y, o.x + Math.cos(ang) * flen, o.y + Math.sin(ang) * flen));
    }
    bolts.push({ pts, forks, ax, ay, ex: bx, ey: by, life: 0.22, total: 0.22 });
  }

  /** Aftermath: 2s of crackling residue at a brink-snap's pop point (170px zone). */
  function addResidue(x, y, r = 170, lifeSec = 2.0) {
    residues.push({ x, y, r, life: lifeSec, total: lifeSec });
  }

  function addSparkle(x, y) {
    sparkles.push({ x: x + (Math.random() - 0.5) * 14, y: y + (Math.random() - 0.5) * 14,
      life: 0.5, hue: 315 + Math.random() * 30 });
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

  /** Age + cull the timed buffers. Tethers are re-fed each frame, not aged. */
  function step(dt) {
    for (let i = residues.length - 1; i >= 0; i--) { if ((residues[i].life -= dt) <= 0) residues.splice(i, 1); }
    for (let i = bolts.length - 1; i >= 0; i--) {
      const b = bolts[i];
      if ((b.life -= dt) <= 0) { bolts.splice(i, 1); continue; }
      // re-jitter the bolt while it's hot, so the current dances (WPF Rejitter)
      if (b.life > b.total * 0.4) {
        b._j = (b._j || 0) + dt;
        if (b._j >= 0.04) {
          b._j = 0;
          b.pts = buildBolt(b.ax, b.ay, b.ex, b.ey);
          b.forks = [];
          const nF = (Math.random() * 3) | 0;
          for (let k = 0; k < nF && b.pts.length > 2; k++) {
            const o = b.pts[1 + ((Math.random() * (b.pts.length - 2)) | 0)];
            const ang = Math.random() * Math.PI * 2, flen = 20 + Math.random() * 55;
            b.forks.push(buildBolt(o.x, o.y, o.x + Math.cos(ang) * flen, o.y + Math.sin(ang) * flen));
          }
        }
      }
    }
    for (let i = trail.length - 1; i >= 0; i--) { if ((trail[i].life -= dt) <= 0) trail.splice(i, 1); }
    for (let i = sparkles.length - 1; i >= 0; i--) { if ((sparkles[i].life -= dt) <= 0) sparkles.splice(i, 1); }
  }

  function anyActive() {
    return bolts.length || residues.length || trail.length || sparkles.length || tethers.length;
  }

  // ---- additive soft-sprite blit (the WPF DrawDot): tinted bloom, `lighter` ----
  function dot(cx, cy, radius, alpha, r, g, b) {
    if (radius <= 0.2 || alpha <= 0) return;
    ctx.globalAlpha = Math.min(1, alpha);
    const s = spriteFor(r | 0, g | 0, b | 0);
    ctx.drawImage(s, cx - radius, cy - radius, radius * 2, radius * 2);
  }

  // ---- stroked glow polyline (the WPF blurred glow stroke), `lighter` ----
  function glowLine(pts, width, r, g, b, alpha, blur) {
    if (pts.length < 2) return;
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i].x, pts[i].y);
    ctx.lineWidth = width;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.strokeStyle = `rgba(${r},${g},${b},${alpha})`;
    if (blur) { ctx.shadowColor = `rgba(${r},${g},${b},${alpha})`; ctx.shadowBlur = blur; }
    ctx.stroke();
    if (blur) { ctx.shadowBlur = 0; ctx.shadowColor = 'transparent'; }
  }

  function draw(dt) {
    step(dt);
    const any = anyActive();
    if (!any && !canvas._dirty) return;

    // clear at 1:1, then draw in CSS px (backing is *dpr for crispness).
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    canvas._dirty = any;
    if (!any) return;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.globalCompositeOperation = 'lighter';   // ADDITIVE = the glow

    // --- Bound tethers: an elastic thread, glowing, reddening as it frays. ---
    for (const t of tethers) {
      const midX = (t.ax + t.bx) / 2, midY = (t.ay + t.by) / 2 + 26;
      ctx.beginPath();
      ctx.moveTo(t.ax, t.ay);
      ctx.quadraticCurveTo(midX, midY, t.bx, t.by);
      const col = t.fraying ? [255, 80, 80] : [200, 180, 255];
      ctx.lineWidth = t.fraying ? 4 : 2.6;
      ctx.lineCap = 'round';
      ctx.strokeStyle = `rgba(${col[0]},${col[1]},${col[2]},0.85)`;
      ctx.shadowColor = `rgba(${col[0]},${col[1]},${col[2]},0.85)`;
      ctx.shadowBlur = t.fraying ? 10 : 7;
      ctx.stroke();
      ctx.shadowBlur = 0; ctx.shadowColor = 'transparent';
    }

    // --- Residue zones: a soft additive disc + a dashed glowing ring + motes. ---
    for (const z of residues) {
      const a = 0.32 * (z.life / z.total);
      dot(z.x, z.y, z.r, a * 0.9, 122, 224, 255);            // soft bloom disc
      ctx.beginPath();
      ctx.arc(z.x, z.y, z.r, 0, Math.PI * 2);
      ctx.setLineDash([6, 10]);
      ctx.lineWidth = 2;
      ctx.strokeStyle = `rgba(122,224,255,${a * 1.4})`;
      ctx.shadowColor = `rgba(122,224,255,${a * 1.4})`;
      ctx.shadowBlur = 8;
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.shadowBlur = 0; ctx.shadowColor = 'transparent';
      if (Math.random() < 0.35) {
        const ang = Math.random() * Math.PI * 2, rr = Math.random() * z.r;
        dot(z.x + Math.cos(ang) * rr, z.y + Math.sin(ang) * rr, 5, a * 1.5, 190, 245, 255);
      }
    }

    // --- Lightning bolts: blurred cyan glow stroke + white-blue core + flashes + forks. ---
    for (const b of bolts) {
      const t = b.life / b.total;
      const a = t > 0.4 ? 1 : t / 0.4;                       // hold hot, then fade
      glowLine(b.pts, 6.5, 66, 220, 230, 0.55 * a, 8);       // cyan glow halo
      for (const fk of b.forks) glowLine(fk, 5, 66, 220, 230, 0.4 * a, 6);
      glowLine(b.pts, 1.8, 191, 236, 255, 0.95 * a, 0);      // white-blue core
      for (const fk of b.forks) glowLine(fk, 1.4, 191, 236, 255, 0.62 * a, 0);
      const fr = 16 * a + 6;
      dot(b.ex, b.ey, fr, 0.8 * a, 66, 220, 230);            // strike flash
      dot(b.ax, b.ay, fr * 0.7, 0.6 * a, 66, 220, 230);      // source spark
    }

    // --- Vibe trail: a warm additive ribbon (bloom + gold core) behind the pointer. ---
    for (const p of trail) {
      const a = p.life / 0.45;
      const rad = 10 * a + 3;
      dot(p.x, p.y, rad * 1.8, 0.28 * a, 255, 176, 58);      // bloom
      dot(p.x, p.y, rad, 0.6 * a, 255, 176, 58);             // body
      dot(p.x, p.y, rad * 0.5, 0.7 * a, 255, 233, 160);      // gold core
    }

    // --- Rabbit sparkles (Tail-Plug): pink soft sprite + gold core. ---
    for (const s of sparkles) {
      const a = s.life / 0.5;
      const [r, g, bch] = hsl2rgb(s.hue, 0.95, 0.72);
      dot(s.x, s.y, 7 * a + 2, 0.35 * a, r, g, bch);         // bloom
      dot(s.x, s.y, 3.5 * a + 1, 0.85 * a, r, g, bch);       // body
      dot(s.x, s.y, 2 * a, 0.7 * a, 255, 233, 160);          // gold core
    }

    ctx.globalCompositeOperation = 'source-over';
    ctx.globalAlpha = 1;
  }

  return {
    addBolt, addResidue, addSparkle, setTethers, setVibe, vibePoint, inResidue, draw,
    clear() {
      bolts.length = 0; residues.length = 0; trail.length = 0; sparkles.length = 0; tethers = [];
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      canvas._dirty = false;
    },
    dispose() {
      disposed = true;
      window.removeEventListener('resize', resize);
      canvas.remove();
      _spriteCache.clear();   // release the tinted sprite canvases (one field per page; safe to drop the shared module cache on teardown)
    },
  };
}
