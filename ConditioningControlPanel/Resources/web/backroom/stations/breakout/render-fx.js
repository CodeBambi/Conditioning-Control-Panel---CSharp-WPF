/* ============================================================================
 * stations/breakout/render-fx.js - cosmetic helpers for render.js: pooled
 * debris (tumbling bricks), stamps (combo, PERFECT, rings, big words), the
 * shake/rotate/zoom camera, crack + shatter geometry, noise and scanline
 * tiles, and the post pass (aberration, bloom, glitch, scanlines). No sim
 * state lives here; render.js feeds it the snapshot-derived numbers.
 * ==========================================================================*/

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;

/** A hairline fracture: a random walk across the field with a few branches. */
export function makeCrack(rng, W, H) {
  const lines = [];
  const walk = (x, y, ang, len, depth) => {
    const pts = [[x, y]];
    for (let i = 0; i < len; i++) {
      ang += (rng() - 0.5) * 0.9; x += Math.cos(ang) * 22; y += Math.sin(ang) * 22; pts.push([x, y]);
      if (depth < 2 && rng() < 0.12) walk(x, y, ang + (rng() < 0.5 ? -1 : 1) * (0.6 + rng() * 0.6), 6 + (rng() * 10) | 0, depth + 1);
    }
    lines.push(pts);
  };
  walk(-10, 120 + rng() * 200, 0.45, 42, 0);
  walk(W + 10, 420 + rng() * 200, Math.PI + 0.5, 30, 0);
  return lines;
}

/** A broken pane: rays from an impact point with kinks, plus a few concentric rings. */
export function makeShatterWeb(rng, W, H) {
  const cx = W * (0.35 + rng() * 0.3), cy = H * (0.3 + rng() * 0.3), lines = [];
  const n = 11 + (rng() * 5) | 0;
  for (let k = 0; k < n; k++) {
    let ang = (k / n) * Math.PI * 2 + (rng() - 0.5) * 0.3, x = cx, y = cy;
    const pts = [[x, y]];
    for (let i = 0; i < 40; i++) {
      ang += (rng() - 0.5) * 0.35; x += Math.cos(ang) * 26; y += Math.sin(ang) * 26; pts.push([x, y]);
      if (x < -20 || x > W + 20 || y < -20 || y > H + 20) break;
    }
    lines.push(pts);
  }
  for (let r = 40; r < 260; r += 60 + rng() * 40) {
    const pts = [], m = 18;
    for (let i = 0; i <= m; i++) { const a = (i / m) * Math.PI * 2, rr = r * (0.85 + rng() * 0.3); pts.push([cx + Math.cos(a) * rr, cy + Math.sin(a) * rr]); }
    lines.push(pts);
  }
  return { cx, cy, lines };
}

export function strokeLines(g, lines, frac) {
  for (const pts of lines) {
    const n = frac >= 1 ? pts.length : Math.max(2, Math.ceil(pts.length * frac));
    g.beginPath();
    for (let i = 0; i < n; i++) i ? g.lineTo(pts[i][0], pts[i][1]) : g.moveTo(pts[i][0], pts[i][1]);
    g.stroke();
  }
}

/** Tumbling brick chunks, pooled (no per-frame allocation), oldest recycled past `max`. */
export function createDebris(max = 60) {
  const pool = [];
  for (let i = 0; i < max; i++) pool.push({ on: false, x: 0, y: 0, w: 0, h: 0, vx: 0, vy: 0, rot: 0, vr: 0, rgb: null, born: 0 });
  let seq = 0;
  function spawn(x, y, w, h, rgb, rng) {
    let slot = null, oldest = null;
    for (const d of pool) { if (!d.on) { slot = d; break; } if (!oldest || d.born < oldest.born) oldest = d; }
    const d = slot || oldest;
    d.on = true; d.x = x; d.y = y; d.w = w; d.h = h; d.rgb = rgb; d.born = ++seq;
    d.vx = (rng() - 0.5) * 140; d.vy = -60 - rng() * 120; d.rot = 0; d.vr = (rng() - 0.5) * 14;
  }
  function draw(g, dt, mix, col, H) {
    for (const d of pool) {
      if (!d.on) continue;
      d.x += d.vx * dt; d.y += d.vy * dt; d.vy += 760 * dt; d.rot += d.vr * dt;
      if (d.y > H + 40) { d.on = false; continue; }
      g.save(); g.translate(d.x, d.y); g.rotate(d.rot);
      g.fillStyle = col(d.rgb, mix, 0.95); g.fillRect(-d.w / 2, -d.h / 2, d.w, d.h);
      g.fillStyle = 'rgba(255,255,255,.16)'; g.fillRect(-d.w / 2 + 1, -d.h / 2 + 1, d.w - 2, 2);
      g.restore();
    }
  }
  return { spawn, draw, clear() { for (const d of pool) d.on = false; } };
}

/**
 * Stamps: short-lived text/ring feedback. kinds: 'text' (rise + fade, pops in), 'ring' (expanding stroke),
 * 'stamp' (big centre word, scales down then fades), 'cross' (huge faint word crossing the screen),
 * 'pop' (HUD +SP chip, top right).
 */
export function createStamps(FONT, W, H) {
  const list = [];
  function push(o) { o.at = 0; list.push(o); if (list.length > 24) list.shift(); }
  function draw(g, dt, col, mix) {
    for (let i = list.length - 1; i >= 0; i--) {
      const s = list[i]; s.at += dt; if (s.at >= s.life) { list.splice(i, 1); continue; }
      const t = s.at / s.life;
      g.save(); g.textAlign = 'center'; g.textBaseline = 'middle';
      if (s.kind === 'ring') {
        g.strokeStyle = col(s.rgb, mix, 0.9 * (1 - t)); g.lineWidth = 2.5 * (1 - t) + 0.5;
        g.beginPath(); g.arc(s.x, s.y, s.r0 + (s.r1 - s.r0) * t, 0, 7); g.stroke();
      } else if (s.kind === 'text') {
        const k = t < 0.12 ? lerp(1.8, 1, t / 0.12) : 1, a = t > 0.55 ? (1 - t) / 0.45 : 1;
        g.translate(s.x, s.y - t * 26); g.scale(k, k); g.globalAlpha = a;
        g.font = `900 ${s.size || 18}px ${FONT}`; g.lineWidth = 3; g.strokeStyle = 'rgba(20,20,40,.75)'; g.strokeText(s.text, 0, 0);
        g.fillStyle = col(s.rgb, mix); g.fillText(s.text, 0, 0);
      } else if (s.kind === 'cross') {
        g.font = `900 96px ${FONT}`; g.fillStyle = `rgba(210,210,210,${0.16 * Math.sin(t * Math.PI)})`;
        g.fillText(s.text, lerp(-W * 0.6, W * 1.6, t), H * 0.46);
      } else if (s.kind === 'pop') {
        g.globalAlpha = 1 - t; g.textAlign = 'right'; g.font = `800 20px ${FONT}`; g.fillStyle = col(s.rgb, mix);
        g.fillText(s.text, W - 12, 30 - t * 30);
      } else {
        const k = t < 0.14 ? lerp(1.9, 1, t / 0.14) : 1, a = t > 0.7 ? (1 - t) / 0.3 : 1;
        g.translate(s.x || W / 2, s.y || H * 0.44); g.scale(k, k); g.globalAlpha = a;
        g.font = `900 ${s.size || 68}px ${FONT}`; g.lineWidth = 6; g.strokeStyle = col(s.rgb2, mix); g.strokeText(s.text, 0, 0);
        g.fillStyle = col(s.rgb, mix); g.fillText(s.text, 0, 0);
      }
      g.restore();
    }
  }
  return { push, draw, clear() { list.length = 0; } };
}

/** Camera: translational shake + a little roll + a tiny zoom, all decaying. Nothing when reduced. */
export function createShake(reduced) {
  let amp = 0, roll = 0, zoom = 0, rollSign = 1;
  const out = { sx: 0, sy: 0, rot: 0, zoom: 1 };
  return {
    kick(a, r = 0, z = 0) { if (reduced) return; amp = Math.max(amp, a); roll = Math.max(roll, r); zoom = Math.max(zoom, z); rollSign = -rollSign; },
    step(dt, rng) {
      if (reduced || (amp <= 0 && roll <= 0 && zoom <= 0)) { out.sx = out.sy = out.rot = 0; out.zoom = 1; return out; }
      out.sx = (rng() - 0.5) * amp; out.sy = (rng() - 0.5) * amp;
      out.rot = roll * rollSign * 0.012 * Math.sin(amp * 3); out.zoom = 1 + zoom;
      amp = Math.max(0, amp - dt * 40); roll = Math.max(0, roll - dt * 30); zoom = Math.max(0, zoom - dt * 0.25);
      return out;
    },
    reset() { amp = roll = zoom = 0; },
  };
}

export function makeNoiseTile(size = 128) {
  const c = document.createElement('canvas'); c.width = c.height = size;
  const x = c.getContext('2d'), img = x.createImageData(size, size), d = img.data;
  for (let i = 0; i < d.length; i += 4) { const v = 90 + (Math.random() * 130) | 0; d[i] = d[i + 1] = d[i + 2] = v; d[i + 3] = 255; }
  x.putImageData(img, 0, 0);
  return c;
}
export function makeScanTile() {
  const c = document.createElement('canvas'); c.width = 2; c.height = 3;
  const x = c.getContext('2d'); x.fillStyle = 'rgba(0,0,0,1)'; x.fillRect(0, 2, 2, 1);
  return c;
}

/**
 * Post pass on the device-space canvas. `off` is the single offscreen copy (sized like the canvas by the caller).
 * aberr: 0..1 chromatic split; bloom: 0..1 additive re-draw; glitch: true for row tearing; scan: the scanline pattern.
 */
export function postProcess(g, canvas, off, { aberr = 0, bloom = 0, glitch = false, scan = null, rng = Math.random }) {
  const cw = canvas.width, ch = canvas.height;
  g.setTransform(1, 0, 0, 1, 0, 0);
  if (aberr > 0 || bloom > 0 || glitch) {
    const og = off.getContext('2d');
    og.setTransform(1, 0, 0, 1, 0, 0); og.globalCompositeOperation = 'copy'; og.drawImage(canvas, 0, 0); og.globalCompositeOperation = 'source-over';
    if (glitch) {
      for (let i = 0; i < 5; i++) {
        const y = (rng() * ch) | 0, hgt = 4 + (rng() * 24) | 0, dx = ((rng() - 0.5) * 40) | 0;
        g.drawImage(off, 0, y, cw, hgt, dx, y, cw, hgt);
      }
    }
    if (aberr > 0) {
      const dx = Math.max(1, Math.round(aberr * 6 * (cw / 480)));
      g.globalCompositeOperation = 'screen'; g.globalAlpha = 0.16 + aberr * 0.2;
      g.drawImage(off, dx, 0); g.drawImage(off, -dx, 0);
    }
    if (bloom > 0) {
      const k = 1 + 0.018 * bloom;
      g.globalCompositeOperation = 'lighter'; g.globalAlpha = 0.08 + 0.1 * bloom;
      g.drawImage(off, cw / 2 - cw * k / 2, ch / 2 - ch * k / 2, cw * k, ch * k);
    }
    g.globalAlpha = 1; g.globalCompositeOperation = 'source-over';
  }
  if (scan) { g.globalAlpha = 0.07; g.fillStyle = scan; g.fillRect(0, 0, cw, ch); g.globalAlpha = 1; }
}

export { clamp, lerp };
