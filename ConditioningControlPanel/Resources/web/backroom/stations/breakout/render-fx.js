/* ============================================================================
 * stations/breakout/render-fx.js - cosmetic helpers for render.js: pooled
 * debris (tumbling bricks), stamps (combo, PERFECT, rings, big words), the
 * shake/rotate/zoom camera, crack + shatter geometry, noise and scanline
 * tiles, and the post pass (aberration, bloom, glitch, scanlines). No sim
 * state lives here; render.js feeds it the snapshot-derived numbers.
 * ==========================================================================*/

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;

/** Edge damage: irregular angular faults, sparse forks and fine refractive edges. */
export function makeCrack(rng, W, H) {
  const lines = [];
  const walk = (x, y, heading, length, depth = 0, start = 0) => {
    const pts = [[x, y]];
    pts.fracture = true; pts.depth = depth; pts.start = start;
    let travelled = 0;
    while (travelled < length) {
      const step = Math.min(length - travelled, 7 + rng() * 25);
      const ang = heading + (rng() - 0.5) * 0.52;
      x = clamp(x + Math.cos(ang) * step, 0, W);
      y = clamp(y + Math.sin(ang) * step, 0, H);
      travelled += step; pts.push([x, y]);
      if (depth === 0 && pts.length > 3 && rng() < 0.19) {
        walk(x, y, heading + (rng() < 0.5 ? -1 : 1) * (0.4 + rng() * 0.55),
          18 + rng() * 48, 1, travelled / length * 0.8);
      }
      if (x === 0 || x === W || y === 0 || y === H) break;
    }
    lines.push(pts);
  };
  walk(0, H * (0.18 + rng() * 0.2), 0.35 + rng() * 0.3, W * (0.4 + rng() * 0.22));
  walk(W, H * (0.58 + rng() * 0.18), Math.PI + 0.25 + rng() * 0.35, W * (0.28 + rng() * 0.2));
  return lines;
}

/** Uneven impact fractures: short splinters, branching faults and sparse connecting seams. */
export function makeShatterWeb(rng, W, H) {
  const cx = W * (.25 + rng() * .5), cy = H * (.25 + rng() * .4), lines = [], rays = [];
  const branch = (x, y, angle, length, depth = 0) => {
    const pts = [[x, y]];
    pts.fracture = true; pts.depth = depth; pts.start = depth ? .12 : 0;
    for (let distance = 0; distance < length;) {
      const step = 5 + rng() * 19;
      angle += (rng() - .5) * .28;
      x += Math.cos(angle) * step; y += Math.sin(angle) * step; distance += step;
      pts.push([x, y]);
      if (!depth && distance > 35 && rng() < .12)
        branch(x, y, angle + (rng() < .5 ? -1 : 1) * (.3 + rng() * .5), 18 + rng() * 65, 1);
      if (x < 0 || x > W || y < 0 || y > H) break;
    }
    lines.push(pts); return pts;
  };
  for (let i = 0; i < 9; i++) {
    const angle = i / 9 * Math.PI * 2 + (rng() - .5) * .5;
    rays.push(branch(cx + (rng() - .5) * 7, cy + (rng() - .5) * 7, angle,
      (i % 3 === 0 ? W * .65 : H * (.12 + rng() * .3))));
  }
  for (let i = 0; i < 18; i++) branch(cx, cy, rng() * Math.PI * 2, 5 + rng() * 26, 1);
  for (let i = 0; i < rays.length - 1; i++) {
    if (rng() > .5) continue;
    const a = rays[i][Math.min(3, rays[i].length - 1)], b = rays[i + 1][Math.min(4, rays[i + 1].length - 1)];
    const seam = [a, [(a[0] + b[0]) / 2 + rng() * 9, (a[1] + b[1]) / 2], b];
    seam.fracture = true; seam.depth = 1; seam.start = .2; lines.push(seam);
  }
  return { cx, cy, lines };
}

export function strokeLines(g, lines, frac) {
  for (const pts of lines) {
    if (!pts.fracture) {
      const n = frac >= 1 ? pts.length : Math.max(2, Math.ceil(pts.length * frac));
      g.beginPath();
      for (let i = 0; i < n; i++) i ? g.lineTo(pts[i][0], pts[i][1]) : g.moveTo(pts[i][0], pts[i][1]);
      g.stroke();
      continue;
    }
    if (pts.length < 2) continue;
    const progress = clamp((frac - pts.start) / (1 - pts.start), 0, 1);
    if (progress <= 0) continue;
    const end = clamp(progress, 0, 1) * (pts.length - 1), n = Math.floor(end);
    const trace = () => {
      g.beginPath(); g.moveTo(pts[0][0], pts[0][1]);
      for (let i = 1; i <= n; i++) g.lineTo(pts[i][0], pts[i][1]);
      if (n + 1 < pts.length) g.lineTo(lerp(pts[n][0], pts[n + 1][0], end - n), lerp(pts[n][1], pts[n + 1][1], end - n));
      g.stroke();
    };
    g.save();
    g.lineJoin = 'bevel'; g.lineCap = 'butt';
    g.globalAlpha *= pts.depth ? 0.45 : 0.8;
    g.lineWidth = pts.depth ? 0.4 : 0.65;
    trace();
    // A subpixel dark lip gives the line thickness without a painted white seam.
    g.translate(0.55, 0.45); g.strokeStyle = 'rgba(0,0,0,.22)'; g.lineWidth = 0.55; trace();
    g.translate(-0.55, -0.45);
    if (progress < 1 && !pts.depth) {
      // Tiny travelling reflections follow the advancing tip, never a screen flash.
      const next = Math.min(n + 1, pts.length - 1);
      const x = lerp(pts[n][0], pts[next][0], end - n), y = lerp(pts[n][1], pts[next][1], end - n);
      g.fillStyle = 'rgba(230,244,255,.28)'; g.fillRect(x - 0.6, y - 0.6, 1.2, 1.2);
    }
    g.restore();
  }
}

/** Tumbling brick chunks, pooled (no per-frame allocation), oldest recycled past `max`. */
export function createDebris(max = 60) {
  const pool = [];
  for (let i = 0; i < max; i++) pool.push({ on: false, x: 0, y: 0, w: 0, h: 0, vx: 0, vy: 0, rot: 0, vr: 0, rgb: null, born: 0, age: 0, life: 1, cut: .2 });
  let seq = 0;
  function spawn(x, y, w, h, rgb, rng) {
    let slot = null, oldest = null;
    for (const d of pool) { if (!d.on) { slot = d; break; } if (!oldest || d.born < oldest.born) oldest = d; }
    const d = slot || oldest;
    d.on = true; d.x = x; d.y = y; d.w = w; d.h = h; d.rgb = rgb; d.born = ++seq; d.age = 0; d.life = .55 + rng() * .35; d.cut = .15 + rng() * .35;
    d.vx = (rng() - 0.5) * 140; d.vy = -60 - rng() * 120; d.rot = 0; d.vr = (rng() - 0.5) * 14;
  }
  function draw(g, dt, mix, col, H) {
    for (const d of pool) {
      if (!d.on) continue;
      d.x += d.vx * dt; d.y += d.vy * dt; d.vy += 760 * dt; d.rot += d.vr * dt;
      d.age += dt;
      if (d.age >= d.life || d.y > H + 40) { d.on = false; continue; }
      g.save(); g.translate(d.x, d.y); g.rotate(d.rot);
      g.globalAlpha = .65 * Math.min(1, (d.life - d.age) / .3);
      g.fillStyle = col(d.rgb, mix * .35);
      g.beginPath(); g.moveTo(-d.w / 2, -d.h / 2);
      g.lineTo(d.w * d.cut, -d.h * .4); g.lineTo(d.w / 2, d.h * .2);
      g.lineTo(d.w * .15, d.h / 2); g.lineTo(-d.w / 2, d.h * .3); g.closePath(); g.fill();
      g.strokeStyle = 'rgba(220,220,225,.22)'; g.lineWidth = .6; g.stroke();
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
      } else if (s.kind === 'word') {
        // A subliminal leaving its brick (or its afterglow near the ball): pops in with an overshoot, wobbles,
        // each letter riding its own wave, a pink glow with a mint chromatic twin, then it lifts and thins out.
        const pop = t < 0.18 ? 0.3 + 0.7 * (1 - (1 - t / 0.18) ** 3) * (1 + 0.35 * Math.sin(t / 0.18 * Math.PI)) : 1;
        const a = (t > 0.6 ? (1 - t) / 0.4 : 1) * (s.alpha == null ? 1 : s.alpha);
        const size = s.size || 34, text = String(s.text || '');
        g.translate(s.x, s.y - t * 34); g.rotate(Math.sin(s.at * 9) * 0.06 * (1 - t)); g.scale(pop, pop);
        g.globalAlpha = a; g.font = `900 ${size}px ${FONT}`;
        const widths = s.widths || (s.widths = [...text].map(ch => g.measureText(ch).width)), total = widths.reduce((p, w) => p + w, 0);
        let x = -total / 2;
        for (let i = 0; i < text.length; i++) {
          const ch = text[i], w = widths[i], cx = x + w / 2, cy = Math.sin(s.at * 12 + i * 0.9) * size * 0.08 * (1 - t);
          g.lineWidth = 5; g.strokeStyle = col(s.rgb, mix, 0.22); g.strokeText(ch, cx + 2, cy + 2);
          g.fillStyle = col(s.rgb2 || [120, 230, 200], mix, 0.7); g.fillText(ch, cx + 2, cy + 2);
          g.shadowBlur = 0;
          g.lineWidth = 3; g.strokeStyle = 'rgba(20,20,40,.7)'; g.strokeText(ch, cx, cy);
          g.fillStyle = col(s.rgb, mix); g.fillText(ch, cx, cy);
          g.fillStyle = 'rgba(255,255,255,.55)'; g.fillText(ch, cx - 0.5, cy - 1.5);
          x += w;
        }
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
// Build the soft optical pass at quarter resolution. Only one blend touches the full frame.
const opticalBuffers = new WeakMap();
export function postProcess(g, canvas, off, { aberr = 0, bloom = 0, glitch = false, scan = null, rng = Math.random }) {
  const cw = canvas.width, ch = canvas.height;
  g.setTransform(1, 0, 0, 1, 0, 0);
  if (aberr > 0 || bloom > 0 || glitch) {
    const ow = Math.max(1, Math.ceil(cw / 4)), oh = Math.max(1, Math.ceil(ch / 4));
    if (off.width !== ow || off.height !== oh) { off.width = ow; off.height = oh; }
    const og = off.getContext('2d');
    og.setTransform(1, 0, 0, 1, 0, 0); og.globalCompositeOperation = 'copy';
    og.drawImage(canvas, 0, 0, ow, oh); og.globalCompositeOperation = 'source-over';
    if (glitch) {
      g.save(); g.globalAlpha *= .5;
      for (let i = 0; i < 5; i++) {
        const y = (rng() * oh) | 0, hgt = Math.min(oh - y, 1 + (rng() * 6) | 0), dx = ((rng() - 0.5) * 20) | 0;
        g.drawImage(off, 0, y, ow, hgt, dx, y * ch / oh, cw, hgt * ch / oh);
      }
      g.restore();
    }
    if (aberr > 0 || bloom > 0) {
      let layer = opticalBuffers.get(off);
      if (!layer) { layer = document.createElement('canvas'); opticalBuffers.set(off, layer); }
      if (layer.width !== ow || layer.height !== oh) { layer.width = ow; layer.height = oh; }
      const x = layer.getContext('2d'); x.clearRect(0, 0, ow, oh);
      x.globalCompositeOperation = 'lighter';
      if (aberr > 0) {
        const dx = Math.max(.125, aberr * 3 * cw / 480 / 4);
        x.globalAlpha = .08 + aberr * .1;
        x.drawImage(off, dx, 0); x.drawImage(off, -dx, 0);
      }
      if (bloom > 0) {
        const k = 1 + .018 * bloom;
        x.globalAlpha = .08 + .1 * bloom;
        x.drawImage(off, ow * (1-k)/2, oh * (1-k)/2, ow*k, oh*k);
      }
      x.globalAlpha = 1;
      g.globalCompositeOperation = 'screen'; g.drawImage(layer, 0, 0, cw, ch);
      g.globalCompositeOperation = 'source-over';
    }
  }
  if (scan) { g.globalAlpha = .07; g.fillStyle = scan; g.fillRect(0, 0, cw, ch); g.globalAlpha = 1; }
}

export { clamp, lerp };
