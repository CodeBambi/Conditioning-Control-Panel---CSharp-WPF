/* ============================================================================
 * stations/breakout/render.js - canvas 2D. Everything visual keys on the
 * snapshot's `rungs` (the juice ladder) and `state` (COLOUR / GREY). The
 * letterbox maps the 480x720 field into whatever the canvas is.
 *
 * createRenderer(canvas, { reduced, media, rng }) ->
 *   { resize, draw(snap, extras | now, dt, word), onEvent, onGameEvent, toField, dispose }
 * draw accepts the CONTRACT v2 form draw(snap, { words, media, now, reduced, dt, word }) and the older
 * draw(snap, now, dt, word). onGameEvent is onEvent (same function, both names exported on the object).
 * Cosmetic-only state (debris, stamps, shake, post) lives in render-fx.js.
 * The payload bubbles (colliders and the whirlwind well) wear the OG soap
 * bubble skin (assets/bubble.png) over the picture; until it loads, the old
 * circle. A broken GIF brick's face (snap.pops) tumbles until it bursts.
 * ==========================================================================*/

import { W, H } from './game.js';
import { drawSpiral } from './payloads.js';
import { makeCrack, makeShatterWeb, strokeLines, createDebris, createStamps, createShake, makeNoiseTile, makeScanTile, postProcess, clamp, lerp } from './render-fx.js';
import { createParticles } from './particles.js';
import { createWellFx } from './render-well.js';

const FONT = '"Arial Rounded MT Bold", "Trebuchet MS", Arial, sans-serif';
const BG = [26, 26, 46], PINK = [255, 105, 180], VIOLET = [165, 108, 255], MINT = [120, 230, 200], GOLD = [255, 207, 107], WHITE = [255, 255, 255], GREY = [150, 150, 150];
const ROWS = [[255, 105, 180], [255, 140, 200], [214, 120, 255], [165, 108, 255], [120, 180, 255], [120, 230, 200]];
const easeOutBack = (t) => { const c = 1.7; t = clamp(t, 0, 1) - 1; return 1 + t * t * ((c + 1) * t + c); };
const INFLATE_S = 0.25;
/* The OG bubble, loaded once per module. import.meta.url keeps it right under play.html's <base> and in dev.html. */
let BUBBLE = null;
try { if (typeof Image !== 'undefined') { BUBBLE = new Image(); BUBBLE.decoding = 'async'; BUBBLE.src = new URL('./assets/bubble.png', import.meta.url).href; } } catch (e) { BUBBLE = null; }
const bubbleReady = () => !!(BUBBLE && BUBBLE.complete && BUBBLE.naturalWidth > 0);
/** '#rrggbb' (the sim's brick.color) or an [r,g,b] array -> [r,g,b]; anything else -> null. */
function toRgb(c) {
  if (Array.isArray(c)) return c;
  if (typeof c === 'string' && /^#[0-9a-f]{6}$/i.test(c)) return [parseInt(c.slice(1, 3), 16), parseInt(c.slice(3, 5), 16), parseInt(c.slice(5, 7), 16)];
  return null;
}

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

export function createRenderer(canvas, { reduced = false, media = null, rng = Math.random } = {}) {
  const g = canvas.getContext('2d');
  let cw = 0, ch = 0, scale = 1, ox = 0, oy = 0, off = null, scanPat = null;
  let last = null, glitch = 0, wipe = 0, crackFlash = 0, spin = 0, aberr = 0, recoil = 0, lastNow = 0, lastCombo = 0, comboPop = 0;
  let blinkAt = 3 + rng() * 2, blink = 0, wordIdx = 0, wordAt = 0, flash = 0;
  const shockwaves = [], drifters = [];
  const P = createParticles({ max: 600, rng });
  const crack = makeCrack(rng, W, H), web = makeShatterWeb(rng, W, H);
  const debris = createDebris(60), stamps = createStamps(FONT, W, H), cam = createShake(reduced);
  const noise = makeNoiseTile(128), scanTile = makeScanTile();
  const wellFx = createWellFx({ reduced, rng, noiseTile: noise });
  let frameNo = 0;
  let noisePat = null, vignette = null;

  function resize(w, h) {
    cw = Math.max(1, w | 0); ch = Math.max(1, h | 0);
    if (canvas.width !== cw || canvas.height !== ch) { canvas.width = cw; canvas.height = ch; }
    if (!off) off = document.createElement('canvas');
    if (off.width !== cw || off.height !== ch) { off.width = cw; off.height = ch; }
    scale = Math.min(cw / W, ch / H); ox = (cw - W * scale) / 2; oy = (ch - H * scale) / 2;
    scanPat = g.createPattern(scanTile, 'repeat'); noisePat = g.createPattern(noise, 'repeat');
    vignette = g.createRadialGradient(W / 2, H / 2, H * 0.25, W / 2, H / 2, H * 0.72);
    vignette.addColorStop(0, 'rgba(0,0,0,0)'); vignette.addColorStop(1, 'rgba(0,0,0,.55)');
  }
  const toField = (px, py) => ({ x: (px - ox) / scale, y: (py - oy) / scale });
  const rungs = (i) => !!(last && last.rungs[i]);

  function onEvent(name, d) {
    d = d || {};
    const colour = !last || last.state === 'colour';
    const sat = last ? clamp(last.sat || 0, 0, 1) : 0;
    if (name === 'brick') {
      const rgb = toRgb(d.color) || ROWS[(d.row || 0) % ROWS.length];
      if (rungs(3) && colour) {
        P.burst(d.x, d.y, rgb, Math.round(8 + 20 * sat), 160 + 70 * sat, 0.6);
        P.rects(d.x, d.y, rgb, 4 + Math.round(2 * sat), { speed: 190, life: 0.7, size: 6 });
      }
      debris.spawn(d.x, d.y, (d.w || 42) * 0.55, (d.h || 18) * 0.7, rgb, rng);
      debris.spawn(d.x, d.y, (d.w || 42) * 0.35, (d.h || 18) * 0.6, rgb, rng);
      if (rungs(5) && colour) cam.kick(4);
      if (d.jackpot) cam.kick(6, 1, 0.01);
      if (d.ghost && (d.plus | 0) > 1) stamps.push({ kind: 'text', text: '+' + (d.plus | 0), x: d.x, y: d.y - 8, life: 0.8, rgb: WHITE, size: 18 });
    } else if (name === 'popOut') {
      // The picture leaves the wall: a few shards of the brick and a puff along the kick.
      const rgb = toRgb(d.color) || PINK;
      P.rects(d.x, d.y, rgb, 5, { speed: 150, life: 0.5, size: 5 });
      if (colour) P.spray(d.x, d.y, Math.atan2(d.vy || -1, d.vx || 0), 0.7, WHITE, 8, 170, 0.35);
    } else if (name === 'burst') {
      // The bubble inflating: a soap-flavoured burst, light and slow, plus a ring.
      if (colour) {
        const rgb = toRgb(d.color) || PINK;
        P.burst(d.x, d.y, rgb, 22, 190, 0.6, { rise: 30, gv: 120 });
        P.burst(d.x, d.y, WHITE, 14, 110, 0.7, { rise: 50, gv: 40, r0: 1.5, r1: 3 });
        stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 8, r1: d.kind === 'well' ? 96 : 60, life: 0.4, rgb: d.kind === 'well' ? MINT : PINK });
        cam.kick(d.kind === 'well' ? 5 : 3);
      }
    } else if (name === 'hit') {
      if (d.kind === 'paddle') recoil = 1;
      if (d.kind === 'gif') { cam.kick(7); if (rungs(8)) { glitch = 0.14; aberr = 1; } }
      if ((d.combo | 0) >= 5 && colour) { cam.kick(6 + Math.min(8, d.combo * 0.5), 1, 0.012); aberr = Math.max(aberr, 0.8); }
      else if (d.kind === 'brick' && colour && rungs(5)) aberr = Math.max(aberr, 0.35);
    } else if (name === 'paddle') {
      recoil = 1;
      if (colour && rungs(3) && last) {
        const ang = -Math.PI / 2 + clamp(d.t || 0, -1, 1) * (Math.PI / 3);
        P.spray(d.x, last.paddle.y - last.paddle.h, ang, 0.8, PINK, 6 + Math.round(4 * sat), 180, 0.45);
      }
    } else if (name === 'gif') {
      cam.kick(7); if (rungs(8)) { glitch = 0.14; aberr = 1; }
      P.burst(d.x, d.y, VIOLET, 24, 220, 0.7);
      P.rects(d.x, d.y, VIOLET, 4, { speed: 150, life: 0.6, size: 5 });
    } else if (name === 'capture') {
      // The well swallowing the ball: everything falls inward, then the release throws it back out.
      if (colour) P.implode(d.x, d.y, MINT, 30, 190, 0.6, { from: 115 });
    } else if (name === 'spiral') {
      P.burst(d.x, d.y, MINT, 30, 220, 0.9);
      P.after(0.1, () => P.burst(d.x, d.y, PINK, 18, 160, 0.7));
    } else if (name === 'perfect') {
      stamps.push({ kind: 'text', text: 'PERFECT', x: d.x || W / 2, y: (d.y || H / 2) - 18, life: 0.8, rgb: GOLD, size: 20 });
      stamps.push({ kind: 'ring', x: d.x || W / 2, y: d.y || H / 2, r0: 6, r1: 60, life: 0.45, rgb: GOLD });
      P.spray(d.x || W / 2, d.y || H / 2, -Math.PI / 2, 1.5, GOLD, 16, 230, 0.6);
      flash = Math.max(flash, 0.25);
    } else if (name === 'nearMiss') {
      stamps.push({ kind: 'ring', x: d.x || W / 2, y: d.y || H - 40, r0: 4, r1: 48, life: 0.4, rgb: WHITE });
    } else if (name === 'jackpot') {
      stamps.push({ kind: 'stamp', text: 'JACKPOT', x: W / 2, y: H * 0.4, life: 1.3, rgb: GOLD, rgb2: VIOLET, size: 60 });
      stamps.push({ kind: 'stamp', text: '+5 SP', x: W / 2, y: H * 0.4 + 52, life: 1.3, rgb: WHITE, rgb2: VIOLET, size: 30 });
      P.burst(d.x || W / 2, d.y || H / 2, GOLD, 46, 260, 1);
      P.after(0.12, () => P.burst(d.x || W / 2, d.y || H / 2, VIOLET, 30, 320, 0.9));
      cam.kick(9, 1.5, 0.02); flash = 0.5;
    } else if (name === 'shatterWall') {
      P.shatter(web.cx, web.cy, 44);
      P.after(0.12, () => P.burst(web.cx, web.cy, WHITE, 26, 300, 0.8));
      cam.kick(10, 1, 0.015); flash = 0.6;
    } else if (name === 'relapseStart') {
      stamps.push({ kind: 'ring', x: d.x || W / 2, y: d.y || H - 40, r0: 10, r1: 140, life: 0.6, rgb: GREY });
    } else if (name === 'lost') {
      stamps.push({ kind: 'ring', x: d.x || W / 2, y: H - 20, r0: 8, r1: 80, life: 0.5, rgb: GREY });
    } else if (name === 'relapse') {
      wipe = 0.1; P.clear(); shockwaves.length = 0; drifters.length = 0; glitch = 0; aberr = 0; cam.reset(); wellFx.reset();
      stamps.push({ kind: 'cross', text: 'RELAPSE', life: 1.6 });
    } else if (name === 'breakoutStart') {
      flash = Math.max(flash, 0.3);
    } else if (name === 'breakout') {
      P.shatter(d.x, d.y, 44);
      P.burst(d.x, d.y, WHITE, 24, 240, 0.8);
      shockwaves.push({ x: d.x, y: d.y, at: 0, life: 0.7 });
      P.after(0.12, () => { P.burst(d.x, d.y, GOLD, 34, 320, 0.9); shockwaves.push({ x: d.x, y: d.y, at: 0, life: 0.6 }); });
      stamps.push({ kind: 'stamp', text: 'BREAKOUT', life: 1.1, rgb: GOLD, rgb2: VIOLET });
      cam.kick(10, 1.2, 0.02);
    } else if (name === 'split') {
      drifters.push({ x: d.x, y: d.y, at: 0, life: 2.2 });
      P.burst(d.x, d.y, PINK, 18, 140, 0.6);
    } else if (name === 'wall') {
      flash = Math.max(flash, 0.2);                                       // the SP chip in the DOM strip pops; no second counter here
      if (colour && rungs(3)) {                                           // two beats: the clear, then the echo
        P.burst(W / 2, H * 0.35, GOLD, 40, 260, 0.9);
        P.after(0.12, () => P.burst(W / 2, H * 0.35, MINT, 30, 330, 0.8));
      }
    } else if (name === 'crack') {
      crackFlash = 1; cam.kick(12, 1, 0.01);
    }
  }

  /* ------------------------------------------------------------ pieces */
  function drawBricks(s, mix) {
    const landing = s.wallAge < 0.7 && rungs(1);
    for (const br of s.bricks) {
      if (!br.alive) continue;
      const tween = landing ? easeOutBack((s.wallAge - br.row * 0.04) / 0.45) : 1;
      const jelly = rungs(4) ? (br.jelly || 0) : 0;
      const sx = 1 + jelly * 0.18 * Math.sin(jelly * 9), sy = 1 - jelly * 0.22 * Math.sin(jelly * 9);
      const px = br.push ? br.push.dx || 0 : 0, py = br.push ? br.push.dy || 0 : 0;
      const cx = br.x + br.w / 2 + px, cy = br.y + br.h / 2 + py - (1 - tween) * 140;
      g.save();
      g.globalAlpha = clamp(tween, 0, 1);
      g.translate(cx, cy); g.scale(sx, sy);
      const gifOn = br.gif === true || (typeof br.gif === 'number' && br.gif >= 0);
      const frame = gifOn && rungs(1) && s.state === 'colour' && media ? media.frame(typeof br.gif === 'number' ? br.gif : br.col) : null;
      if (frame) {
        roundRect(g, -br.w / 2, -br.h / 2, br.w, br.h, 3); g.clip();
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1;
        const k = Math.max(br.w / fw, br.h / fh) * 1.05;
        g.drawImage(frame, -fw * k / 2, -fh * k / 2, fw * k, fh * k);
        g.strokeStyle = col(PINK, mix, 0.7); g.lineWidth = 1; g.strokeRect(-br.w / 2 + 0.5, -br.h / 2 + 0.5, br.w - 1, br.h - 1);
      } else {
        const rgb = br.jackpot ? GOLD : ROWS[br.row % ROWS.length];
        roundRect(g, -br.w / 2, -br.h / 2, br.w, br.h, 3);
        g.fillStyle = col(rgb, mix); g.fill();
        g.fillStyle = 'rgba(255,255,255,.18)'; g.fillRect(-br.w / 2 + 2, -br.h / 2 + 2, br.w - 4, 3);
        if (br.split) { g.fillStyle = col(GOLD, mix); g.beginPath(); g.arc(0, 0, 3, 0, 7); g.fill(); }
        if (br.letter) { g.fillStyle = 'rgba(20,20,40,.8)'; g.font = `800 11px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText(br.letter, 0, 0.5); }
      }
      g.restore();
    }
  }
  /** The OG soap bubble over a picture of radius r: the rim highlights sit on top, the picture shows through. */
  function drawBubble(x, y, r, a = 1) {
    if (!bubbleReady()) return false;
    const d = r * 2.16;                                                   // the PNG's bubble sits a little inside its square
    g.save(); g.globalAlpha = clamp(a, 0, 1); g.drawImage(BUBBLE, x - d / 2, y - d / 2, d, d); g.restore();
    return true;
  }
  /** A broken GIF brick's face, tumbling out of the wall until it bursts. */
  function drawPops(s, mix) {
    for (const p of s.pops || []) {
      const frame = media && rungs(1) ? media.frame(p.gif) : null;
      g.save(); g.translate(p.x, p.y); g.rotate(p.rot || 0);
      g.shadowColor = col(PINK, mix, 0.6); g.shadowBlur = 10;
      roundRect(g, -p.w / 2, -p.h / 2, p.w, p.h, 3);
      if (frame) {
        g.fillStyle = col(VIOLET, mix); g.fill(); g.shadowBlur = 0; g.clip();
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1, k = Math.max(p.w / fw, p.h / fh) * 1.05;
        g.drawImage(frame, -fw * k / 2, -fh * k / 2, fw * k, fh * k);
        g.strokeStyle = col(PINK, mix, 0.8); g.lineWidth = 1; g.strokeRect(-p.w / 2 + 0.5, -p.h / 2 + 0.5, p.w - 1, p.h - 1);
      } else {
        g.fillStyle = col(toRgb(p.color) || PINK, mix); g.fill();
        g.fillStyle = 'rgba(255,255,255,.18)'; g.fillRect(-p.w / 2 + 2, -p.h / 2 + 2, p.w - 4, 3);
      }
      g.restore();
    }
  }
  /** The whirlwind: a dealt picture cut into wedges and wound into a spiral (render-well.js), inside the bubble. */
  function drawWell(s, mix, dt, extras) {
    const well = s.well; if (!well) return;
    const m = (extras && extras.media) || media;
    const inflate = Math.max(0.02, easeOutBack((typeof well.born === 'number' ? well.born : 1) / INFLATE_S));
    g.save();
    g.translate(well.x, well.y); g.scale(inflate, inflate); g.translate(-well.x, -well.y);
    wellFx.draw(g, well, { mix, col, media: m, dt, sat: s.sat, particles: P,
      pink: PINK, violet: VIOLET, mint: MINT, spiral: drawSpiral });
    if (mix > 0) drawBubble(well.x, well.y, well.r || 70, clamp(well.fade, 0, 1) * 0.95);
    g.restore();
  }
  function drawColliders(s, mix) {
    for (const c of s.colliders) {
      const inflate = easeOutBack((typeof c.age === 'number' ? c.age : 1) / INFLATE_S);
      const r = c.r * (1 + c.pulse * 0.25) * Math.max(0.02, inflate), frame = media ? media.frame(c.gif) : null;
      const a = clamp(c.alpha, 0, 1);
      g.save(); g.globalAlpha = a;
      g.beginPath(); g.arc(c.x, c.y, r, 0, 7); g.closePath();
      g.shadowColor = col(PINK, mix, 0.8); g.shadowBlur = 14 + c.pulse * 20;
      g.fillStyle = col(VIOLET, mix); g.fill(); g.shadowBlur = 0;
      if (frame) {
        g.clip();
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1, k = Math.max(2 * r / fw, 2 * r / fh);
        g.drawImage(frame, c.x - fw * k / 2, c.y - fh * k / 2, fw * k, fh * k);
      }
      g.restore();
      if (!drawBubble(c.x, c.y, r, a)) { g.strokeStyle = col(PINK, mix, 0.9 * a); g.lineWidth = 2 + c.pulse * 3; g.beginPath(); g.arc(c.x, c.y, r, 0, 7); g.stroke(); }
      else if (c.pulse > 0) { g.strokeStyle = col(PINK, mix, 0.8 * c.pulse * a); g.lineWidth = 1 + c.pulse * 3; g.beginPath(); g.arc(c.x, c.y, r * 1.04, 0, 7); g.stroke(); }
    }
  }
  function drawBall(s, b, mix, words) {
    const hgt = clamp((H - 40 - b.y) / (H - 40), 0, 1);                        // soft shadow, grows with height
    g.fillStyle = `rgba(0,0,0,${0.28 - hgt * 0.16})`;
    g.beginPath(); g.ellipse(b.x, b.y + b.r + 3 + hgt * 10, b.r * (1 + hgt * 1.2), b.r * 0.45 * (1 + hgt * 0.6), 0, 0, 7); g.fill();
    if (b.ghost || s.state === 'grey') {
      g.save(); g.globalAlpha = 0.72;
      g.fillStyle = '#9a9a9a'; g.beginPath(); g.arc(b.x, b.y, b.r, 0, 7); g.fill();
      g.strokeStyle = '#d0d0d0'; g.lineWidth = 1; g.stroke();
      g.fillStyle = '#2a2a2a'; g.font = `700 4.2px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillText('OLD SELF', b.x, b.y + 0.3);
      g.restore();
      return;
    }
    const trail = b.trail || [];
    if (rungs(2) && trail.length >= 4) {
      const n = Math.min(trail.length / 2, Math.max(2, Math.round(30 * s.sat)));
      const useWords = s.sat >= 0.7 && words && words.length;
      if (useWords) { g.font = `700 7px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle'; }
      for (let i = 0; i < n; i++) {
        const k = trail.length - 2 - i * 2; if (k < 0) break;
        const t = 1 - i / n;
        if (useWords) {
          if (i % 3) continue;
          g.fillStyle = col(i % 2 ? MINT : PINK, mix, 0.5 * t);
          g.fillText(words[(wordIdx + (i / 3 | 0)) % words.length], trail[k], trail[k + 1]);
        } else {
          g.fillStyle = col(PINK, mix, 0.35 * t); g.beginPath(); g.arc(trail[k], trail[k + 1], b.r * (0.2 + 0.8 * t), 0, 7); g.fill();
        }
      }
    }
    if (rungs(3)) {
      const grd = g.createRadialGradient(b.x, b.y, b.r, b.x, b.y, b.r * 3.2);
      grd.addColorStop(0, col(PINK, mix, 0.45)); grd.addColorStop(1, col(PINK, mix, 0));
      g.fillStyle = grd; g.beginPath(); g.arc(b.x, b.y, b.r * 3.2, 0, 7); g.fill();
    }
    const sp = Math.hypot(b.vx || 0, b.vy || 0), ref = s.speed || 420;
    const k = rungs(2) ? clamp(sp / ref, 0, 1.6) * 0.16 : 0, ang = sp > 1 ? Math.atan2(b.vy, b.vx) : 0;
    const rot = typeof b.spin === 'number' ? b.spin : spin;
    g.save(); g.translate(b.x, b.y); g.rotate(ang); g.scale(1 + k, 1 - k * 0.8); g.rotate(-ang);
    g.fillStyle = col(PINK, mix); g.beginPath(); g.arc(0, 0, b.r, 0, 7); g.fill();
    g.beginPath(); g.arc(0, 0, b.r - 0.5, 0, 7); g.clip();
    drawSpiral(g, 0, 0, b.r * 1.1, rot, col(WHITE, 1), 0.85, 2);
    g.restore();
  }
  function drawPaddle(s, mix, dt) {
    const p = s.paddle, st = rungs(2) ? p.stretch : 0;
    const w = p.w * (1 + st * 0.25 + recoil * 0.1), h = p.h * (1 - st * 0.3), py = p.y + recoil * 5;
    roundRect(g, p.x - w / 2, py - h / 2, w, h, 7);
    g.fillStyle = col(VIOLET, mix); g.fill();
    g.fillStyle = 'rgba(255,255,255,.22)'; roundRect(g, p.x - w / 2 + 3, py - h / 2 + 2, w - 6, 3, 2); g.fill();
    if (rungs(6) && s.state === 'colour' && s.balls[0]) {
      const b = s.balls[0], dx = b.x - p.x, dy = b.y - py, d = Math.hypot(dx, dy) || 1;
      const lx = dx / d * 1.6, ly = dy / d * 1.2;
      blinkAt -= dt; if (blinkAt <= 0) { blink = 0.12; blinkAt = 3 + rng() * 2; } blink = Math.max(0, blink - dt);
      for (const ex of [-9, 9]) {
        if (blink > 0) { g.strokeStyle = '#fff'; g.lineWidth = 1.6; g.beginPath(); g.moveTo(p.x + ex - 3.5, py); g.lineTo(p.x + ex + 3.5, py); g.stroke(); continue; }
        g.fillStyle = '#fff'; g.beginPath(); g.arc(p.x + ex, py, 3.6, 0, 7); g.fill();
        g.fillStyle = '#1a1a2e'; g.beginPath(); g.arc(p.x + ex + lx, py + ly, 1.7, 0, 7); g.fill();
      }
      const smile = 3.5 + clamp(s.combo || 0, 0, 12) * 0.35;
      g.strokeStyle = '#1a1a2e'; g.lineWidth = 1.4; g.beginPath(); g.arc(p.x, py + 1, smile, 0.25, Math.PI - 0.25); g.stroke();
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
  const drawParticles = (dt, mix) => P.step(g, dt, mix, col);
  /** A mote shed behind the ball every other frame once the field is bright. */
  function ballSparkle(s) {
    if (reduced || s.state !== 'colour' || s.sat <= 0.5 || (frameNo & 1)) return;
    const b = s.balls.find(x => !x.ghost && !x.lost); if (!b) return;
    const sp = Math.hypot(b.vx || 0, b.vy || 0) || 1;
    P.sparkle(b.x - (b.vx / sp) * b.r * 1.4 + (rng() - 0.5) * 6, b.y - (b.vy / sp) * b.r * 1.4 + (rng() - 0.5) * 6,
      rng() < 0.5 ? MINT : WHITE, 0.35 + rng() * 0.3);
  }
  function drawShatterWall(s, mix, extras) {
    const m = (extras && extras.media) || media, frame = m && m.frame ? m.frame(0) : null;
    if (frame) {
      const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1, k = Math.max(W / fw, H / fh);
      g.save(); g.globalAlpha = 0.4; g.drawImage(frame, W / 2 - fw * k / 2, H / 2 - fh * k / 2, fw * k, fh * k); g.restore();
      g.fillStyle = col(BG, mix, 0.45); g.fillRect(0, 0, W, H);
    }
    g.save(); g.lineJoin = 'round'; g.strokeStyle = 'rgba(255,255,255,.5)'; g.lineWidth = 1.2; strokeLines(g, web.lines, 1);
    g.strokeStyle = 'rgba(0,0,0,.35)'; g.lineWidth = 0.6; g.translate(1, 1); strokeLines(g, web.lines, 1); g.restore();
  }
  function drawCombo(s, mix, dt) {
    const c = s.combo | 0, b = s.balls[0];
    if (c !== lastCombo) { comboPop = c > lastCombo ? 1 : 0; lastCombo = c; }
    comboPop = Math.max(0, comboPop - dt * 5);
    if (c < 3 || !b || s.state === 'grey') return;
    g.save(); g.translate(b.x + 16, b.y - 16); g.scale(1 + comboPop * 0.6, 1 + comboPop * 0.6);
    g.font = `900 ${14 + Math.min(c, 20) * 0.6}px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
    g.lineWidth = 3; g.strokeStyle = 'rgba(20,20,40,.8)'; g.strokeText(String(c), 0, 0);
    g.fillStyle = col(c >= 10 ? GOLD : c >= 5 ? MINT : WHITE, mix); g.fillText(String(c), 0, 0);
    g.restore();
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
    stamps.draw(g, dt, col, mix);
    const cracked = rungs(9) || s.crackFired;
    if (cracked && s.state === 'colour') {                                  // fractures grow after the crack
      crackFlash = Math.max(0, crackFlash - dt * 1.5);
      const frac = typeof s.fractures === 'number' ? clamp(s.fractures, 0, 1) : 1;
      g.save(); g.strokeStyle = `rgba(255,255,255,${0.35 + crackFlash * 0.6})`; g.lineWidth = 0.8 + crackFlash * 2 + frac * 0.6; g.lineJoin = 'round';
      strokeLines(g, crack, 0.35 + frac * 0.65);
      if (crackFlash > 0) { g.fillStyle = `rgba(255,255,255,${crackFlash * 0.5})`; g.fillRect(0, 0, W, H); }
      g.restore();
    }
    if (flash > 0) { flash = Math.max(0, flash - dt * 2.5); g.fillStyle = `rgba(255,255,255,${flash * 0.35})`; g.fillRect(0, 0, W, H); }
    if (wipe > 0) {                                    // the RELAPSE wipe: 100 ms sweep of grey
      wipe -= dt; const t = clamp(1 - wipe / 0.1, 0, 1);
      g.fillStyle = 'rgba(120,120,120,.9)'; g.fillRect(0, 0, W * t, H);
    }
  }
  function drawGreyPost(s) {
    g.save();
    if (s.smear) { const sm = s.smear, a = clamp(sm.a, 0, 1);            // the last hit, burning away
      const grd = g.createRadialGradient(sm.x, sm.y, 0, sm.x, sm.y, 70 + (1 - a) * 60);
      grd.addColorStop(0, `rgba(190,190,190,${0.22 * a})`); grd.addColorStop(1, 'rgba(190,190,190,0)');
      g.fillStyle = grd; g.fillRect(0, 0, W, H); }
    g.fillStyle = vignette; g.fillRect(0, 0, W, H);
    if (!reduced && noisePat) { g.globalAlpha = 0.12; g.globalCompositeOperation = 'overlay'; g.fillStyle = noisePat;
      g.translate((rng() * 128) | 0, (rng() * 128) | 0); g.fillRect(-128, -128, W + 256, H + 256); }
    g.restore();
  }
  function drawHud(s, mix) {                                              // only the saturation bar; the counters live in the DOM strip
    g.fillStyle = 'rgba(0,0,0,.35)'; g.fillRect(0, 0, W, 4);
    g.fillStyle = col(PINK, mix); g.fillRect(0, 0, W * (s.state === 'grey' ? 0 : s.sat), 4);
  }

  /* ------------------------------------------------------------ frame */
  function draw(snap, a, b, c) {
    let now, dt, word, extras = null;
    if (a && typeof a === 'object') { extras = a; now = extras.now != null ? extras.now : performance.now() / 1000; if (now > 1e6) now /= 1000;
      dt = extras.dt != null ? extras.dt : (lastNow ? now - lastNow : 1 / 60); word = extras.word || null; }
    else { now = a || 0; dt = b != null ? b : 1 / 60; word = c || null; }
    lastNow = now; dt = clamp(dt, 0, 0.05); frameNo++;
    last = snap;
    const s = snap, grey = s.state === 'grey', tr = s.transition || null, ts = typeof s.timeScale === 'number' ? s.timeScale : 1;
    const fxDt = dt * ts;
    let mix = grey ? 0 : (rungs(1) ? clamp(0.2 + s.sat, 0, 1) : 0);
    if (tr && tr.kind === 'breakout' && grey && tr.t > 0.3 && (((tr.t * 14) | 0) % 3) === 0) mix = clamp(0.2 + (s.savedSat || s.sat), 0, 1);
    if (s.freeze <= 0) spin += fxDt * (2 + 6 * s.sat);
    recoil = Math.max(0, recoil - dt * 6); aberr = Math.max(0, aberr - dt * 3);
    wordAt += dt; if (wordAt > 0.35) { wordAt = 0; wordIdx++; }
    const words = extras && Array.isArray(extras.words) ? extras.words : null;
    const beat = typeof s.beatPhase === 'number' ? Math.pow(1 - s.beatPhase, 3) : 0;
    const shk = cam.step(dt, rng);

    g.setTransform(1, 0, 0, 1, 0, 0);
    g.fillStyle = '#000'; g.fillRect(0, 0, cw, ch);
    g.save();
    g.translate(ox + W * scale / 2, oy + H * scale / 2); g.rotate(shk.rot); g.scale(scale * shk.zoom, scale * shk.zoom);
    g.translate(-W / 2 + shk.sx, -H / 2 + shk.sy);
    g.beginPath(); g.rect(0, 0, W, H); g.clip();
    g.fillStyle = col(BG, mix); g.fillRect(0, 0, W, H);
    if (s.shatterWall) drawShatterWall(s, mix, extras);
    if (!grey && mix > 0) {
      const pulse = 1 + beat * 0.9 * s.sat;
      const grd = g.createRadialGradient(W / 2, H * 0.55, 40, W / 2, H * 0.55, H * (0.8 + beat * 0.15 * s.sat));
      grd.addColorStop(0, col(VIOLET, mix, clamp(0.16 * s.sat * pulse, 0, 0.4))); grd.addColorStop(1, col(BG, mix, 0));
      g.fillStyle = grd; g.fillRect(0, 0, W, H);
    }
    drawWalls(s, mix);
    if (s.mantra && !grey) {                                                // the mantra, large and faint behind the bricks
      g.save(); g.font = `900 62px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillStyle = col(PINK, mix, 0.09);
      g.fillText(String(s.mantra).toUpperCase(), W / 2, 150); g.restore();
    }
    if (!grey) drawWell(s, mix, fxDt, extras);
    drawBricks(s, mix);
    if (!grey) { drawPops(s, mix); drawColliders(s, mix); }
    if (grey) P.clear(); else ballSparkle(s);
    debris.draw(g, fxDt, mix, col, H);
    drawParticles(fxDt, mix);
    for (const bl of s.balls) drawBall(s, bl, mix, words);
    drawPaddle(s, mix, dt);
    if (!extras) drawCombo(s, mix, dt);                                   // the v2 station shows the combo in its strip
    drawOverlays(s, dt, mix, grey ? null : word);
    if (tr && tr.kind === 'relapse' && !grey) {                             // slow-mo drain: colour leaves from the bottom up
      const y0 = H * (1 - clamp(tr.t, 0, 1));
      g.save(); g.globalCompositeOperation = 'saturation'; g.fillStyle = '#808080'; g.fillRect(0, y0, W, H - y0); g.restore();
      g.fillStyle = `rgba(120,120,120,${0.25 * tr.t})`; g.fillRect(0, y0, W, H - y0);
    }
    drawHud(s, mix);
    if (grey) drawGreyPost(s);
    g.restore();

    const big = !grey && (s.combo | 0) >= 5;
    postProcess(g, canvas, off, {
      aberr: grey || reduced ? 0 : Math.max(aberr, big ? 0.4 : 0),
      bloom: grey || !rungs(5) ? 0 : 0.6 + 0.4 * s.sat,
      glitch: glitch > 0 && !reduced, scan: scanPat, rng,
    });
    if (glitch > 0) glitch -= dt;
  }

  const r = { resize, draw, onEvent, onGameEvent: onEvent, toField,
    dispose() { P.clear(); shockwaves.length = drifters.length = 0; debris.clear(); stamps.clear(); wellFx.reset(); off = null; },
    particleCount: () => P.count() };
  return r;
}
