/* ============================================================================
 * stations/breakout/render.js - canvas 2D. Everything visual keys on the
 * snapshot's `rungs` (the juice ladder) and `state` (COLOUR / GREY). The
 * letterbox maps the 480x720 field into whatever the canvas is.
 *
 * createRenderer(canvas, { reduced, media }) -> { resize, draw(snap, now, dt, word), onEvent, toField, dispose }
 * ==========================================================================*/

import { W, H } from './game.js';
import { drawSpiral } from './payloads.js';

const FONT = '"Arial Rounded MT Bold", "Trebuchet MS", Arial, sans-serif';
const BG = [26, 26, 46], PINK = [255, 105, 180], VIOLET = [165, 108, 255], MINT = [120, 230, 200], GOLD = [255, 207, 107];
const ROWS = [[255, 105, 180], [255, 140, 200], [214, 120, 255], [165, 108, 255], [120, 180, 255], [120, 230, 200]];
const lerp = (a, b, t) => a + (b - a) * t;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const easeOutBack = (t) => { const c = 1.7; t = clamp(t, 0, 1) - 1; return 1 + t * t * ((c + 1) * t + c); };

/** rgb mixed toward its own grey by `mix` (0 = grey, 1 = full colour). */
function col(rgb, mix, a = 1) {
  const l = 0.3 * rgb[0] + 0.59 * rgb[1] + 0.11 * rgb[2];
  const r = lerp(l, rgb[0], mix) | 0, g = lerp(l, rgb[1], mix) | 0, b = lerp(l, rgb[2], mix) | 0;
  return a >= 1 ? `rgb(${r},${g},${b})` : `rgba(${r},${g},${b},${a})`;
}
function roundRect(g, x, y, w, h, r) {
  g.beginPath(); g.moveTo(x + r, y); g.arcTo(x + w, y, x + w, y + h, r); g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r); g.arcTo(x, y, x + w, y, r); g.closePath();
}
/** A hairline fracture: a random walk across the field with a few branches. */
function makeCrack(rng) {
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

export function createRenderer(canvas, { reduced = false, media = null, rng = Math.random } = {}) {
  const g = canvas.getContext('2d');
  let cw = 0, ch = 0, scale = 1, ox = 0, oy = 0;
  let last = null, shake = 0, glitch = 0, wipe = 0, crackFlash = 0, spin = 0;
  const particles = [], shards = [], shockwaves = [], drifters = [], pops = [], bigWords = [];
  const crack = makeCrack(rng);

  function resize(w, h) {
    cw = Math.max(1, w | 0); ch = Math.max(1, h | 0);
    if (canvas.width !== cw || canvas.height !== ch) { canvas.width = cw; canvas.height = ch; }
    scale = Math.min(cw / W, ch / H); ox = (cw - W * scale) / 2; oy = (ch - H * scale) / 2;
  }
  const toField = (px, py) => ({ x: (px - ox) / scale, y: (py - oy) / scale });
  const rungs = (i) => !!(last && last.rungs[i]);

  function burst(x, y, rgb, n, speed, life) {
    for (let i = 0; i < n; i++) {
      const a = rng() * Math.PI * 2, s = speed * (0.4 + rng());
      particles.push({ x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s - 40, life, max: life, rgb, r: 2 + rng() * 3 });
    }
  }
  function onEvent(name, d) {
    const colour = last && last.state === 'colour';
    if (name === 'brick') {
      if (rungs(3) && colour) burst(d.x, d.y, ROWS[d.row % ROWS.length], 12, 160, 0.6);
      if (rungs(5) && colour && !reduced) shake = Math.max(shake, 4);
    } else if (name === 'gif') {
      if (!reduced) { shake = Math.max(shake, 7); if (rungs(8)) glitch = 0.14; }
      burst(d.x, d.y, VIOLET, 18, 220, 0.7);
    } else if (name === 'spiral') {
      burst(d.x, d.y, MINT, 24, 200, 0.9);
    } else if (name === 'relapse') {
      wipe = 0.1; particles.length = 0; shockwaves.length = 0; drifters.length = 0; glitch = 0; shake = 0;
      bigWords.push({ text: 'RELAPSE', kind: 'cross', at: 0, life: 1.6 });
    } else if (name === 'breakout') {
      for (let i = 0; i < 26; i++) {
        const a = rng() * Math.PI * 2, s = 120 + rng() * 260;
        shards.push({ x: d.x, y: d.y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, rot: rng() * 6, vr: (rng() - 0.5) * 12, life: 1.1, max: 1.1, size: 3 + rng() * 6 });
      }
      shockwaves.push({ x: d.x, y: d.y, at: 0, life: 0.7 });
      bigWords.push({ text: 'BREAKOUT', kind: 'stamp', at: 0, life: 1.1 });
      if (!reduced) shake = 10;
    } else if (name === 'split') {
      drifters.push({ x: d.x, y: d.y, at: 0, life: 2.2 });
      burst(d.x, d.y, PINK, 14, 140, 0.6);
    } else if (name === 'wall') {
      pops.push({ text: '+1 SP', at: 0, life: 1.4 });
    } else if (name === 'crack') {
      crackFlash = 1; if (!reduced) shake = 12;
    }
  }

  /* ------------------------------------------------------------ pieces */
  function drawBricks(s, mix, now) {
    const tween = s.wallAge < 0.5 && rungs(1) ? easeOutBack(s.wallAge / 0.5) : 1;
    for (const br of s.bricks) {
      if (!br.alive) continue;
      const jelly = rungs(4) ? br.jelly : 0;
      const sx = 1 + jelly * 0.18 * Math.sin(jelly * 9), sy = 1 - jelly * 0.22 * Math.sin(jelly * 9);
      const cx = br.x + br.w / 2, cy = br.y + br.h / 2 - (1 - tween) * 140;
      g.save();
      g.globalAlpha = clamp(tween, 0, 1);
      g.translate(cx, cy); g.scale(sx, sy);
      const frame = br.gif >= 0 && rungs(1) && s.state === 'colour' && media ? media.frame(br.gif) : null;
      if (frame) {
        roundRect(g, -br.w / 2, -br.h / 2, br.w, br.h, 3); g.clip();
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1;
        const k = Math.max(br.w / fw, br.h / fh) * 1.05;
        g.drawImage(frame, -fw * k / 2, -fh * k / 2, fw * k, fh * k);
        g.strokeStyle = col(PINK, mix, 0.7); g.lineWidth = 1; g.strokeRect(-br.w / 2 + 0.5, -br.h / 2 + 0.5, br.w - 1, br.h - 1);
      } else {
        const rgb = ROWS[br.row % ROWS.length];
        roundRect(g, -br.w / 2, -br.h / 2, br.w, br.h, 3);
        g.fillStyle = col(rgb, mix); g.fill();
        g.fillStyle = 'rgba(255,255,255,.18)'; g.fillRect(-br.w / 2 + 2, -br.h / 2 + 2, br.w - 4, 3);
        if (br.split) { g.fillStyle = col(GOLD, mix); g.beginPath(); g.arc(0, 0, 3, 0, 7); g.fill(); }
      }
      g.restore();
    }
  }
  function drawWell(s, mix, now) {
    const well = s.well; if (!well) return;
    const a = clamp(well.fade, 0, 1) * clamp(well.age * 3, 0, 1);
    g.save();
    g.fillStyle = col(VIOLET, mix, 0.08 * a); g.beginPath(); g.arc(well.x, well.y, well.pull, 0, 7); g.fill();
    drawSpiral(g, well.x, well.y, well.r, well.rot, col(PINK, mix), a);
    drawSpiral(g, well.x, well.y, well.r * 0.6, -well.rot * 1.4, col(MINT, mix), a * 0.7);
    g.restore();
  }
  function drawColliders(s, mix) {
    for (const c of s.colliders) {
      const r = c.r * (1 + c.pulse * 0.25), frame = media ? media.frame(c.gif) : null;
      g.save(); g.globalAlpha = clamp(c.alpha, 0, 1);
      g.beginPath(); g.arc(c.x, c.y, r, 0, 7); g.closePath();
      g.shadowColor = col(PINK, mix, 0.8); g.shadowBlur = 14 + c.pulse * 20;
      g.fillStyle = col(VIOLET, mix); g.fill(); g.shadowBlur = 0;
      if (frame) {
        g.clip();
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1, k = Math.max(2 * r / fw, 2 * r / fh);
        g.drawImage(frame, c.x - fw * k / 2, c.y - fh * k / 2, fw * k, fh * k);
      }
      g.restore();
      g.strokeStyle = col(PINK, mix, 0.9); g.lineWidth = 2 + c.pulse * 3; g.beginPath(); g.arc(c.x, c.y, r, 0, 7); g.stroke();
    }
  }
  function drawBall(s, b, mix, now) {
    if (b.ghost || s.state === 'grey') {
      g.save(); g.globalAlpha = 0.72;
      g.fillStyle = '#9a9a9a'; g.beginPath(); g.arc(b.x, b.y, b.r, 0, 7); g.fill();
      g.strokeStyle = '#d0d0d0'; g.lineWidth = 1; g.stroke();
      g.fillStyle = '#2a2a2a'; g.font = `700 4.2px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillText('OLD SELF', b.x, b.y + 0.3);
      g.restore();
      return;
    }
    if (rungs(2) && b.trail.length >= 4) {
      const n = Math.min(b.trail.length / 2, Math.max(2, Math.round(30 * s.sat)));
      for (let i = 0; i < n; i++) {
        const k = b.trail.length - 2 - i * 2; if (k < 0) break;
        const t = 1 - i / n;
        g.fillStyle = col(PINK, mix, 0.35 * t); g.beginPath(); g.arc(b.trail[k], b.trail[k + 1], b.r * (0.2 + 0.8 * t), 0, 7); g.fill();
      }
    }
    if (rungs(3)) {
      const grd = g.createRadialGradient(b.x, b.y, b.r, b.x, b.y, b.r * 3.2);
      grd.addColorStop(0, col(PINK, mix, 0.45)); grd.addColorStop(1, col(PINK, mix, 0));
      g.fillStyle = grd; g.beginPath(); g.arc(b.x, b.y, b.r * 3.2, 0, 7); g.fill();
    }
    g.fillStyle = col(PINK, mix); g.beginPath(); g.arc(b.x, b.y, b.r, 0, 7); g.fill();
    g.save(); g.beginPath(); g.arc(b.x, b.y, b.r - 0.5, 0, 7); g.clip();
    drawSpiral(g, b.x, b.y, b.r * 1.1, spin, col([255, 255, 255], 1), 0.85, 2);
    g.restore();
  }
  function drawPaddle(s, mix) {
    const p = s.paddle, st = rungs(2) ? p.stretch : 0;
    const w = p.w * (1 + st * 0.25), h = p.h * (1 - st * 0.3);
    roundRect(g, p.x - w / 2, p.y - h / 2, w, h, 7);
    g.fillStyle = col(VIOLET, mix); g.fill();
    g.fillStyle = 'rgba(255,255,255,.22)'; roundRect(g, p.x - w / 2 + 3, p.y - h / 2 + 2, w - 6, 3, 2); g.fill();
    if (rungs(6) && s.state === 'colour' && s.balls[0]) {
      const b = s.balls[0], dx = b.x - p.x, dy = b.y - p.y, d = Math.hypot(dx, dy) || 1;
      const lx = dx / d * 1.6, ly = dy / d * 1.2;
      for (const ex of [-9, 9]) {
        g.fillStyle = '#fff'; g.beginPath(); g.arc(p.x + ex, p.y, 3.6, 0, 7); g.fill();
        g.fillStyle = '#1a1a2e'; g.beginPath(); g.arc(p.x + ex + lx, p.y + ly, 1.7, 0, 7); g.fill();
      }
      g.strokeStyle = '#1a1a2e'; g.lineWidth = 1.4; g.beginPath(); g.arc(p.x, p.y + 1.5, 3.5, 0.25, Math.PI - 0.25); g.stroke();
    }
  }
  function drawWalls(s, mix) {
    const wob = rungs(4) ? s.wobble : null, amp = wob ? wob.t * 6 * Math.sin(wob.t * 22) : 0;
    g.strokeStyle = col(VIOLET, mix, 0.55); g.lineWidth = 2;
    for (const [side, x0] of [['left', 1], ['right', W - 1]]) {
      const a = wob && wob.side === side ? amp : 0;
      g.beginPath(); g.moveTo(x0, 0);
      for (let y = 0; y <= H; y += 24) g.lineTo(x0 + a * Math.sin(y / H * Math.PI * 3), y);
      g.stroke();
    }
    const a = wob && wob.side === 'top' ? amp : 0;
    g.beginPath(); g.moveTo(0, 1); for (let x = 0; x <= W; x += 24) g.lineTo(x, 1 + a * Math.sin(x / W * Math.PI * 3)); g.stroke();
  }
  function drawParticles(dt, mix) {
    for (let i = particles.length - 1; i >= 0; i--) {
      const p = particles[i]; p.life -= dt; if (p.life <= 0) { particles.splice(i, 1); continue; }
      p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 300 * dt;
      g.fillStyle = col(p.rgb, mix, p.life / p.max); g.beginPath(); g.arc(p.x, p.y, p.r * (p.life / p.max), 0, 7); g.fill();
    }
    for (let i = shards.length - 1; i >= 0; i--) {
      const p = shards[i]; p.life -= dt; if (p.life <= 0) { shards.splice(i, 1); continue; }
      p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 260 * dt; p.rot += p.vr * dt;
      g.save(); g.translate(p.x, p.y); g.rotate(p.rot); g.globalAlpha = p.life / p.max;
      g.fillStyle = '#b8b8b8'; g.beginPath(); g.moveTo(-p.size, 0); g.lineTo(0, -p.size * 0.6); g.lineTo(p.size, 0); g.lineTo(0, p.size * 0.5); g.fill();
      g.restore();
    }
  }
  function drawOverlays(s, dt, mix, word) {
    if (word) {
      g.save(); g.font = `800 30px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillStyle = col(MINT, mix, 0.9); g.fillText(word.text, word.x + 1.5, word.y + 1.5);
      g.fillStyle = col(PINK, mix, 0.95); g.fillText(word.text, word.x, word.y);
      g.restore();
    }
    for (let i = drifters.length - 1; i >= 0; i--) {
      const d = drifters[i]; d.at += dt; if (d.at >= d.life) { drifters.splice(i, 1); continue; }
      const t = d.at / d.life, y = d.y - t * 320, x = d.x + Math.sin(t * 5) * 12;
      g.save(); g.globalAlpha = 0.6 * (1 - t);
      g.fillStyle = '#9a9a9a'; g.beginPath(); g.arc(x, y, 12, 0, 7); g.fill();
      g.fillStyle = '#2a2a2a'; g.font = `700 5px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText('OLD SELF', x, y);
      g.restore();
    }
    for (let i = shockwaves.length - 1; i >= 0; i--) {
      const sw = shockwaves[i]; sw.at += dt; if (sw.at >= sw.life) { shockwaves.splice(i, 1); continue; }
      const t = sw.at / sw.life;
      g.strokeStyle = col(PINK, mix, 1 - t); g.lineWidth = 10 * (1 - t) + 1; g.beginPath(); g.arc(sw.x, sw.y, 20 + t * 520, 0, 7); g.stroke();
      g.strokeStyle = col(MINT, mix, 0.6 * (1 - t)); g.lineWidth = 3; g.beginPath(); g.arc(sw.x, sw.y, 10 + t * 380, 0, 7); g.stroke();
    }
    for (let i = bigWords.length - 1; i >= 0; i--) {
      const bw = bigWords[i]; bw.at += dt; if (bw.at >= bw.life) { bigWords.splice(i, 1); continue; }
      const t = bw.at / bw.life;
      g.save(); g.textAlign = 'center'; g.textBaseline = 'middle';
      if (bw.kind === 'cross') {
        g.font = `900 96px ${FONT}`; g.fillStyle = `rgba(210,210,210,${0.16 * Math.sin(t * Math.PI)})`;
        g.fillText(bw.text, lerp(-W * 0.6, W * 1.6, t), H * 0.46);
      } else {
        const k = t < 0.14 ? lerp(1.9, 1, t / 0.14) : 1, a = t > 0.7 ? (1 - t) / 0.3 : 1;
        g.translate(W / 2, H * 0.44); g.scale(k, k); g.globalAlpha = a;
        g.font = `900 68px ${FONT}`; g.lineWidth = 6; g.strokeStyle = col(VIOLET, mix); g.strokeText(bw.text, 0, 0);
        g.fillStyle = col(GOLD, mix); g.fillText(bw.text, 0, 0);
      }
      g.restore();
    }
    if (rungs(9) && s.state === 'colour') {
      crackFlash = Math.max(0, crackFlash - dt * 1.5);
      g.save(); g.strokeStyle = `rgba(255,255,255,${0.35 + crackFlash * 0.6})`; g.lineWidth = 0.8 + crackFlash * 2; g.lineJoin = 'round';
      for (const pts of crack) { g.beginPath(); for (let i = 0; i < pts.length; i++) i ? g.lineTo(pts[i][0], pts[i][1]) : g.moveTo(pts[i][0], pts[i][1]); g.stroke(); }
      if (crackFlash > 0) { g.fillStyle = `rgba(255,255,255,${crackFlash * 0.5})`; g.fillRect(0, 0, W, H); }
      g.restore();
    }
    if (wipe > 0) {                                    // the RELAPSE wipe: 100 ms sweep of grey
      wipe -= dt; const t = clamp(1 - wipe / 0.1, 0, 1);
      g.fillStyle = 'rgba(120,120,120,.9)'; g.fillRect(0, 0, W * t, H);
    }
  }
  function drawHud(s, dt, mix) {
    g.fillStyle = 'rgba(0,0,0,.35)'; g.fillRect(0, 0, W, 4);
    g.fillStyle = col(PINK, mix); g.fillRect(0, 0, W * (s.state === 'grey' ? 0 : s.sat), 4);
    g.font = `700 12px ${FONT}`; g.textBaseline = 'top'; g.fillStyle = 'rgba(255,255,255,.72)';
    g.textAlign = 'left'; g.fillText(`bricks ${s.stats.bricks}   walls ${s.stats.walls}`, 12, 12);
    g.textAlign = 'right'; g.fillText(`SP ${s.stats.sp}`, W - 12, 12);
    if (s.state === 'grey') { g.textAlign = 'center'; g.fillStyle = 'rgba(200,200,200,.5)'; g.fillText(`${s.greyBricks} / ${s.breakoutN}`, W / 2, 12); }
    for (let i = pops.length - 1; i >= 0; i--) {
      const p = pops[i]; p.at += dt; if (p.at >= p.life) { pops.splice(i, 1); continue; }
      const t = p.at / p.life; g.save(); g.globalAlpha = 1 - t; g.textAlign = 'right'; g.font = `800 20px ${FONT}`; g.fillStyle = col(GOLD, mix);
      g.fillText(p.text, W - 12, 30 - t * 30); g.restore();
    }
  }

  /* ------------------------------------------------------------ frame */
  function draw(snap, now, dt, word) {
    last = snap; dt = Math.min(dt, 0.05);
    const s = snap, grey = s.state === 'grey';
    const mix = grey ? 0 : (rungs(1) ? clamp(0.2 + s.sat, 0, 1) : 0);
    if (s.freeze <= 0) spin += dt * (2 + 6 * s.sat);
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.fillStyle = '#000'; g.fillRect(0, 0, cw, ch);
    g.save();
    let sx = 0, sy = 0;
    if (shake > 0 && !reduced) { sx = (rng() - 0.5) * shake; sy = (rng() - 0.5) * shake; shake = Math.max(0, shake - dt * 40); }
    g.translate(ox + sx * scale, oy + sy * scale); g.scale(scale, scale);
    g.beginPath(); g.rect(0, 0, W, H); g.clip();
    g.fillStyle = col(BG, mix); g.fillRect(0, 0, W, H);
    if (!grey && mix > 0) {
      const grd = g.createRadialGradient(W / 2, H * 0.55, 40, W / 2, H * 0.55, H * 0.8);
      grd.addColorStop(0, col(VIOLET, mix, 0.16 * s.sat)); grd.addColorStop(1, col(BG, mix, 0));
      g.fillStyle = grd; g.fillRect(0, 0, W, H);
    }
    drawWalls(s, mix);
    if (!grey) drawWell(s, mix, now);
    drawBricks(s, mix, now);
    if (!grey) drawColliders(s, mix);
    if (grey) particles.length = 0;
    drawParticles(dt, mix);
    for (const b of s.balls) drawBall(s, b, mix, now);
    drawPaddle(s, mix);
    drawOverlays(s, dt, mix, grey ? null : word);
    drawHud(s, dt, mix);
    g.restore();
    if (glitch > 0 && !reduced) {
      glitch -= dt;
      for (let i = 0; i < 5; i++) {
        const y = (rng() * ch) | 0, hgt = 4 + (rng() * 24) | 0, dx = ((rng() - 0.5) * 40) | 0;
        g.drawImage(canvas, 0, y, cw, hgt, dx, y, cw, hgt);
      }
      g.globalCompositeOperation = 'screen'; g.globalAlpha = 0.25; g.drawImage(canvas, 3, 0); g.globalAlpha = 1; g.globalCompositeOperation = 'source-over';
    }
  }

  return { resize, draw, onEvent, toField, dispose() { particles.length = shards.length = 0; } };
}
