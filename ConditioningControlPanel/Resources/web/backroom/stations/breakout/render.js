import { durabilityColour, cometSegments, paddleMood } from './feedback.js';
import { createEndingCard } from './ending-card.js';
import {drawPowerIcon,drawPowerups} from './powerups-render.js';
import { junctionProtected } from './junction-shield.js';
import { shieldY as junctionShieldY } from './words/let-go.js';
import {metalActive} from './grey-metal.js';
import {createRewardPicker, rewardBounds} from './bubble-rewards.js';
import { FINALE_PULSE_PERIOD } from './finale-chaos.js';
import { drawMetronome } from './reform.js';
import { createLevelIntro } from './level-intro.js';
/* ============================================================================
 * stations/breakout/render.js - canvas 2D. Everything visual keys on the
 * snapshot's `rungs` (the juice ladder) and `state` (COLOUR / GREY). The
 * letterbox maps the 1280x720 field into whatever the canvas is.
 *
 * createRenderer(canvas, { reduced, media, rng }) ->
 *   { resize, draw(snap, extras | now, dt, word), onEvent, onGameEvent, toField, dispose }
 * draw accepts the CONTRACT v2 form draw(snap, { words, media, now, reduced, dt, word }) and the older
 * draw(snap, now, dt, word). onGameEvent is onEvent (same function, both names exported on the object).
 * Cosmetic-only state (debris, stamps, shake, post) lives in render-fx.js.
 * The word bricks wear their word on a dark plate (gradient fill, glow) and
 * glitch when the sim swaps it (brick.glitch: slices, chromatic split, scramble).
 * The spiral bricks show their Loom field in a small disc (render-well.js tile)
 * and their pop grows into the well; the well itself has no bubble skin.
 * The payload bubbles (the colliders) wear the OG soap
 * bubble skin (assets/bubble.png) over the picture; until it loads, the old
 * circle. A broken GIF brick's face (snap.pops) tumbles until it bursts.
 * ==========================================================================*/

import { W, H, gifScaleForWall } from './game.js';
import { drawSpiral } from './payloads.js';
import { makeCrack, makeShatterWeb, strokeLines, createDebris, createStamps, createShake, makeNoiseTile, postProcess, clamp, lerp } from './render-fx.js';
import { createParticles } from './particles.js';
import { createLandscape } from './landscape.js';
import { createSpellRender } from './spell-render.js';
import { createWellFx } from './render-well.js';
import { WORD_FX } from './word-fx.js';

const FONT = '"Arial Rounded MT Bold", "Trebuchet MS", Arial, sans-serif';
const BG = [26, 26, 46], PINK = [255, 105, 180], VIOLET = [165, 108, 255], MINT = [120, 230, 200], GOLD = [255, 207, 107], WHITE = [255, 255, 255], GREY = [150, 150, 150];
const ROWS = [[255, 105, 180], [255, 140, 200], [214, 120, 255], [165, 108, 255], [120, 180, 255], [120, 230, 200]];
const easeOutBack = (t) => { const c = 1.7; t = clamp(t, 0, 1) - 1; return 1 + t * t * ((c + 1) * t + c); };
const INFLATE_S = 0.25;
/* The OG bubble, loaded once per module. import.meta.url keeps it right under play.html's <base> and in dev.html. */
let BUBBLE = null, softBubble = null;
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

// Owner verified this path fixes a black Firefox playfield on the desktop GPU.
export const prefersSoftwareCanvas = (agent = globalThis.navigator?.userAgent || '') => /Firefox\//i.test(agent);
export function createRenderer(canvas, { reduced = false, media = null, rng = Math.random, software = prefersSoftwareCanvas(), brickHue = 270 } = {}) {
  let g = canvas.getContext('2d', { willReadFrequently: software }), foreground = null;
  let cw = 0, ch = 0, scale = 1, ox = 0, oy = 0, off = null;
  let last = null, glitch = 0, wipe = 0, crackFlash = 0, spin = 0, aberr = 0, recoil = 0, lastNow = 0, lastCombo = 0, comboPop = 0;
  let happy = 0;
  const smoke = createParticles({ max: 120, rng });
  let blinkAt = 3 + rng() * 2, blink = 0, wordIdx = 0, wordAt = 0, flash = 0, lastFlashAt = -1;
  const shockwaves = [], drifters = [];
  const rewardPick = createRewardPicker(rng);
  const levelIntro = createLevelIntro();
  const landscape = createLandscape(W, H, rng, reduced);
  let breakoutFlash = 0, breakoutGif = -1, irisMelt = 0, irisBuffer = null;
  const P = createParticles({ max: 600, rng });
  const spellFx = createSpellRender({ W, H, FONT, reduced, particles: P });
  const crack = makeCrack(rng, W, H), web = makeShatterWeb(rng, W, H);
  const debris = createDebris(144), stamps = createStamps(FONT, W, H), cam = createShake(reduced);
  const noise = makeNoiseTile(128);
  const wellFx = createWellFx({ reduced, rng, noiseTile: noise, software });
  const irisFx = createWellFx({ reduced, rng, noiseTile: noise, software });
  const finaleFx = createWellFx({ reduced, rng, noiseTile: noise, software });
  // Stable identity lets the Loom interpolate its recipes instead of restarting each frame.
  const finaleEye = { x: 0, y: 0, r: 40, pull: 44, persistent: true, age: 0, born: 1, fade: .7, rot: 0 };
  let finaleTarget=null, pupilOffsetX=0, pupilOffsetY=0;
  let finaleOwner = null, finaleFreezeFrame = null, finaleFreezeAge = -1;
  let finaleOutroFrame = null, finaleOutroOwner = null; const endingCard = createEndingCard();
  let frameNo = 0;
  let noisePat = null, vignette = null, ambient = null;

  function resize(w, h, resetSurface = false) {
    cw = Math.max(1, w | 0); ch = Math.max(1, h | 0);
    if (resetSurface || canvas.width !== cw || canvas.height !== ch) { canvas.width = cw; canvas.height = ch; }
    if (!off) off = document.createElement('canvas');
    // The optical pass owns its smaller backing buffer.
    scale = Math.min(cw / W, ch / H); ox = (cw - W * scale) / 2; oy = (ch - H * scale) / 2;
    landscape.resize(scale);
    noisePat = g.createPattern(noise, 'repeat');
    vignette = g.createRadialGradient(W / 2, H / 2, H * 0.25, W / 2, H / 2, H * 0.72);
    vignette.addColorStop(0, 'rgba(0,0,0,0)'); vignette.addColorStop(1, 'rgba(0,0,0,.55)');
  }
  const toField = (px, py) => ({ x: (px - ox) / scale, y: (py - oy) / scale });
  const rungs = (i) => !!(last && last.rungs[i]);

  const bubbleRewards = [];
  function onEvent(name, d) {
    d = d || {};
    spellFx.event(name, d);
    if (name === 'paddle') happy = .9;
    if (!reduced && (name === 'brick' || name === 'brickDamage' || name === 'metalHit' ||
        name === 'irisHit' || name === 'pendulumAnchorHit' || (name === 'hit' && d.kind === 'paddle'))) {
      smoke.smoke(d.x, d.y, [205, 195, 215], 10);
    }
    if (name === 'relapse' || name === 'wall') { smoke.clear(); happy = 0; }
    if(name==='irisCore') {
      irisMelt=4;
      stamps.push({kind:'ring',x:d.x,y:d.y,r0:12,r1:100,life:.65,rgb:GOLD});
      if(!reduced) {
        P.burst(d.x,d.y,GOLD,42,210,.85,{gv:100,r0:1,r1:3});
        P.rects(d.x,d.y,PINK,18,{speed:170,life:.8,size:4});
      }
    }
    if(name==='brickDamage' && !reduced) {
      P.burst(d.x,d.y,d.ghost?[165,165,165]:tierColour(d.hp),7,75,.25,{gv:90,r0:.6,r1:1.2});
    }
    if(name==='irisHit') {
      stamps.push({kind:'ring',x:d.x,y:d.y,r0:8,r1:38,life:.25,rgb:WHITE});
      if(!reduced) P.burst(d.x,d.y,GOLD,10,110,.3,{gv:40,r0:1,r1:2});
    }
    if(name==='metalHit' && !reduced) P.burst(d.x,d.y,[215,225,235],12,100,.22,{gv:30,r0:.7,r1:1.8});
    if(name==='pendulumRelease' || name==='pendulumHit' || name==='pendulumAnchorHit') {
      const release=name==='pendulumRelease';
      stamps.push({kind:'ring',x:d.x,y:d.y,r0:10,r1:release?110:45,life:release?.6:.25,rgb:GOLD});
      if(!reduced) {P.burst(d.x,d.y,GOLD,release?65:15,release?250:120,.7,{gv:130,r0:1,r1:3});}
    }
    if(name==='wall') irisMelt=0;
    const colour = !last || last.state === 'colour';
    const sat = last ? clamp(last.sat || 0, 0, 1) : 0;
    if (name === 'bubblePop') {
      const keys = media ? media.keys() : [];
      const first = rewardPick(keys);
      const other = d.tier === 2 && new Set(keys).size > 1 ? rewardPick(keys) : -1;
      const indices = other >= 0 ? [first, other] : [first];
      bubbleRewards.push({ ...d, indices, t: 0, life: d.tier === 3 ? 3.8 : 1.6 });
    } else if (name === 'relapse' || name === 'relapseStart') {
      bubbleRewards.length = 0;
    }
    if (name === 'brick') {
      const rgb = toRgb(d.color) || ROWS[(d.row || 0) % ROWS.length];
      const grey = rgb[0] * .3 + rgb[1] * .59 + rgb[2] * .11;
      const dust = rgb.map(v => Math.round(grey * .75 + v * .25));
      if (!reduced) {
        P.burst(d.x, d.y, dust, Math.round(20 + 20 * sat), 120 + 70 * sat, .45, { r0: .7, r1: 1.5 });
        P.rects(d.x, d.y, dust, 9, { speed: 145, life: .55, size: 3 });
        for (let i = 0; i < 10; i++) {
          const x = d.x + ((i % 5) / 4 - .5) * (d.w || 82) * .8;
          const y = d.y + (Math.floor(i / 5) - .5) * (d.h || 27) * .6;
          debris.spawn(x, y, (d.w || 82) * (.08 + rng() * .09), (d.h || 27) * (.2 + rng() * .22), dust, rng);
        }
      }
      if (rungs(5) && colour && !last?.iris) cam.kick(4);
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
      if (d.kind === 'brick' && (d.combo | 0) >= 5 && colour && !last?.iris) { cam.kick(6 + Math.min(8, d.combo * 0.5), 1, 0.012); aberr = Math.max(aberr, 0.8); }
      else if (d.kind === 'brick' && colour && rungs(5) && !last?.iris) aberr = Math.max(aberr, 0.35);
    } else if (name === 'paddle') {
      recoil = 1;
      if (colour && rungs(3) && last) {
        const ang = -Math.PI / 2 + clamp(d.t || 0, -1, 1) * (Math.PI / 3);
        P.spray(d.x, last.paddle.y - last.paddle.h, ang, 0.8, PINK, 6 + Math.round(4 * sat), 180, 0.45);
      }
    } else if (name === 'wordSwap') {
      if (colour) { P.rects(d.x, d.y, MINT, 2, { speed: 70, life: 0.35, size: 3 }); P.burst(d.x, d.y, PINK, 3, 60, 0.4, { rise: 20, gv: 30, r0: 1, r1: 1.5 }); }
    } else if (name === 'gif') {
      P.burst(d.x, d.y, VIOLET, d.popped ? 24 : 8, 150, 0.45);
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
      landscape.shatter();
      P.shatter(web.cx, web.cy, 44);
      P.after(0.12, () => P.burst(web.cx, web.cy, WHITE, 26, 300, 0.8));
      cam.kick(10, 1, 0.015); flash = 0.6;
    } else if (name === 'relapseStart') {
      stamps.push({ kind: 'ring', x: d.x || W / 2, y: d.y || H - 40, r0: 10, r1: 140, life: 0.6, rgb: GREY });
    } else if (name === 'lost') {
      stamps.push({ kind: 'ring', x: d.x || W / 2, y: H - 20, r0: 8, r1: 80, life: 0.5, rgb: GREY });
    } else if (name === 'relapse') {
      landscape.reset();
      wipe = 0.1; breakoutFlash = 0; P.clear(); shockwaves.length = 0; drifters.length = 0; glitch = 0; aberr = 0; cam.reset(); wellFx.reset();

    } else if (name === 'breakoutStart') {
      if (!reduced) flash = Math.max(flash, 0.3);
    } else if (name === 'breakout') {
      drifters.push({ x: d.x, y: d.y, at: 0, life: 3.6, r: 8 });
      breakoutFlash = reduced ? 0 : 0.65;
      breakoutGif = Number.isInteger(d.gifIndex) ? d.gifIndex : -1;
      if (!reduced) {
        P.shatter(d.x, d.y, 44);
        P.burst(d.x, d.y, WHITE, 24, 240, 0.8);
        shockwaves.push({ x: d.x, y: d.y, at: 0, life: 0.7 });
        P.after(0.12, () => { P.burst(d.x, d.y, GOLD, 34, 320, 0.9); shockwaves.push({ x: d.x, y: d.y, at: 0, life: 0.6 }); });
        stamps.push({ kind: 'stamp', text: d.count>=2?'RELAPSE':'BREAKOUT', life: 1.1, rgb: GOLD, rgb2: VIOLET });
        cam.kick(10, 1.2, 0.02);
      }
    } else if (name === 'split') {
      drifters.push({ x: d.x, y: d.y, at: 0, life: 2.2, r: 8 });
      P.burst(d.x, d.y, PINK, 18, 140, 0.6);
    } else if (name === 'wall') {
      flash = Math.max(flash, 0.2);                                       // the SP chip in the DOM strip pops; no second counter here
      if (colour && rungs(3)) {                                           // two beats: the clear, then the echo
        P.burst(W / 2, H * 0.35, GOLD, 40, 260, 0.9);
        P.after(0.12, () => P.burst(W / 2, H * 0.35, MINT, 30, 330, 0.8));
      }
    } else if (name === 'crack') {
      crackFlash = 1;
    } else if (name === 'word') {
      // The word leaves its brick: a stamp, big when the effect fired, small when it was a stamp only.
      if (colour) {
        const x = d.x || W / 2, y = (d.y || 200) - 6;
        stamps.push({ kind: 'word', text: String(d.word || '').toUpperCase(), x, y, life: d.fired ? 1.2 : 0.8, rgb: d.fired ? PINK : WHITE, rgb2: d.fired ? MINT : PINK, size: d.fired ? 40 : 26 });
        stamps.push({ kind: 'ring', x, y, r0: 6, r1: d.fired ? 90 : 50, life: 0.45, rgb: d.fired ? PINK : MINT });
        P.burst(x, y, PINK, d.fired ? 22 : 10, 220, 0.7, { rise: 60, gv: 160 });
        P.burst(x, y, WHITE, d.fired ? 12 : 6, 120, 0.8, { rise: 90, gv: 60, r0: 1.5, r1: 2.5 });
        P.rects(x, y, MINT, d.fired ? 6 : 3, { speed: 200, life: 0.6, size: 5 });
        if (d.fired && d.key !== 'DROP' && d.key !== 'BLANK') cam.kick(3);
      }
    }
  }

  /* ------------------------------------------------------------ word effects (word-fx.js) */
  /** A copy of the canvas as it is now (post hooks only). Pass a canvas to reuse it. */
  function copyFrame(target) {
    const c = target || document.createElement('canvas');
    if (c.width !== cw || c.height !== ch) { c.width = cw; c.height = ch; }
    const x = c.getContext('2d', { willReadFrequently: software }); x.setTransform(1, 0, 0, 1, 0, 0); x.clearRect(0, 0, cw, ch); x.drawImage(canvas, 0, 0);
    return c;
  }
  function wordHooks(hook, s, R) {
    const list = s.fx && Array.isArray(s.fx.active) ? s.fx.active : null;
    if (!list || !list.length) return;
    for (const fx of list) {
      const d = WORD_FX[fx.key];
      if (!d || !d.render || typeof d.render[hook] !== 'function') continue;
      const wrap = hook !== 'world';                    // world hooks move the whole world, so their transform must survive
      if (wrap) g.save();
      try { d.render[hook](g, s, fx, R); } catch (e) { /* a word's bug never breaks the frame */ }
      if (wrap) g.restore();
    }
  }
  const rInfo = (s, mix, dt, fxDt) => ({ W, H, cw, ch, scale, ox, oy, mix, dt, fxDt, P, stamps, cam, col, toRgb, PINK, MINT, VIOLET, WHITE, BG, GOLD, GREY, FONT, reduced, rng, media, frame: copyFrame, sat: s.sat });

  /* ------------------------------------------------------------ pieces */
  const tierColour = tier => tier === 3 ? [128, 62, 230] : tier === 2 ? [199, 100, 240] : [255, 145, 204];
  const tierGlows = new Map();
  function tierAura(tier, x, y, width, height, alpha = 1) {
    let glow = tierGlows.get(tier);
    if (!glow) {
      glow = document.createElement('canvas'); glow.width = glow.height = 128;
      const ctx = glow.getContext('2d'), rgb = tierColour(tier);
      const gradient = ctx.createRadialGradient(64, 64, 0, 64, 64, 64);
      gradient.addColorStop(0, col(rgb, 1, 0.65));
      gradient.addColorStop(0.58, col(rgb, 1, 0.55));
      gradient.addColorStop(1, col(rgb, 1, 0));
      ctx.fillStyle = gradient; ctx.fillRect(0, 0, 128, 128);
      tierGlows.set(tier, glow);
    }
    const pulse = reduced ? 1 : 0.84 + 0.16 * Math.sin(lastNow * 2.1 + tier);
    g.save(); g.globalAlpha *= alpha * pulse;
    g.drawImage(glow, x - width / 2, y - height / 2, width, height);
    g.restore();
  }
  function drawPendulums(s,mix,front=false) {
    if(!s.pendulums || s.wallAge<1.9)return;
    for(const p of s.pendulums) {
      if(p.mode==='spent')continue;
      g.save();
      if(p.mode==='bumper')g.globalAlpha=Math.min(1,(10-p.age)/1.2);
      if(!front) {
        if(p.mode==='hung') {
          g.strokeStyle=col(GOLD,mix,.55);g.lineWidth=3;g.setLineDash([4,6]);
          g.beginPath();g.moveTo(p.pivotX,p.pivotY);g.lineTo(p.x,p.y);g.stroke();g.setLineDash([]);
        }
        if(!reduced && p.trail.length>1) {
          g.strokeStyle=col(p.mode==='sweep'?GOLD:PINK,mix,.2+p.energy*.3);
          g.lineWidth=p.mode==='sweep'?16:5;g.beginPath();
          p.trail.forEach((v,i)=>i?g.lineTo(v.x,v.y):g.moveTo(v.x,v.y));g.stroke();
        }
      } else {
        g.translate(p.x,p.y);
        g.fillStyle=col([28,20,38],mix);g.beginPath();g.arc(0,0,p.r,0,Math.PI*2);g.fill();
        const rot=reduced?0:-s.time*(1+p.energy),tile=wellFx.tile('whirl',rot,0,mix,56,frameNo);
        g.save();g.beginPath();g.arc(0,0,p.r-5,0,Math.PI*2);g.clip();
        if(tile){g.rotate(rot);g.drawImage(tile,-p.r,-p.r,p.r*2,p.r*2);}g.restore();
        g.strokeStyle=col(GOLD,mix);g.lineWidth=3;g.beginPath();g.arc(0,0,p.r,0,Math.PI*2);g.stroke();
        g.strokeStyle=col(WHITE,mix,.35+p.pulse*.65);g.lineWidth=1;
        g.beginPath();g.arc(0,0,p.r-4,0,Math.PI*2);g.stroke();
      }
      g.restore();
    }
    if(front && s.wallAge<9) {
      g.save();g.globalAlpha=Math.min(1,9-s.wallAge);g.fillStyle=col(GOLD,mix,.75);
      g.font=`600 13px ${FONT}`;g.textAlign='center';
      g.fillText('HIT THE HINGES. FREE THE SWING.',W/2,H-106);g.restore();
    }
  }
  // Baked bevels separate faces from busy backgrounds without per-frame filters.
  function finishBrick(x,w,h) {
    x.save();roundRect(x,2,2,w-4,h-4,5);x.clip();
    const glaze=x.createLinearGradient(0,0,0,h);
    glaze.addColorStop(0,'rgba(255,255,255,.42)');
    glaze.addColorStop(.36,'rgba(255,255,255,.07)');
    glaze.addColorStop(.62,'rgba(0,0,0,0)');
    glaze.addColorStop(1,'rgba(10,5,24,.38)');
    x.fillStyle=glaze;x.fillRect(0,0,w,h);x.restore();
    roundRect(x,2,2,w-4,h-4,5);x.strokeStyle='rgba(12,7,23,.95)';x.lineWidth=4;x.stroke();
    roundRect(x,4,4,w-8,h-8,3);x.strokeStyle='rgba(255,232,255,.48)';x.lineWidth=1.5;x.stroke();
    x.beginPath();x.moveTo(8,6);x.lineTo(w-8,6);x.strokeStyle='rgba(255,255,255,.65)';x.lineWidth=2;x.stroke();
    x.beginPath();x.moveTo(7,h-6);x.lineTo(w-7,h-6);x.strokeStyle='rgba(10,5,24,.42)';x.lineWidth=2;x.stroke();
  }
  let brickFinish=null;
  function finishTile() {
    if(brickFinish)return brickFinish;
    const c=document.createElement('canvas');c.width=96;c.height=56;
    finishBrick(c.getContext('2d',{willReadFrequently:true}),96,56);
    return brickFinish=c;
  }
  const plainFaces = new Map();
  function plainFace(br, mix, grey) {
    const key = `${grey ? 'grey' : br.row % ROWS.length}:${Math.round(mix * 16)}`;
    if (plainFaces.has(key)) return plainFaces.get(key);
    const c = document.createElement('canvas'); c.width = 84; c.height = 52;
    const x = c.getContext('2d', { willReadFrequently: true });
    roundRect(x, 1, 1, 82, 50, 6);
    x.fillStyle = col(durabilityColour(1, grey, brickHue), Math.round(mix * 16) / 16); x.fill();
    finishBrick(x,84,52);
    plainFaces.set(key, c); return c;
  }
  // Downsample each picture once per frame, shared by every small brick using it.
  // Full-size media remains available to bubbles and fullscreen rewards.
  const brickPictures = new Map();
  function brickPicture(frame) {
    if (!software || !frame) return frame;
    let entry = brickPictures.get(frame);
    if (!entry) {
      if (brickPictures.size >= 16) brickPictures.delete(brickPictures.keys().next().value);
      const c = document.createElement('canvas');
      const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1;
      const k = Math.min(1, 96 / Math.max(fw, fh));
      c.width = Math.max(1, Math.round(fw * k)); c.height = Math.max(1, Math.round(fh * k));
      entry = { canvas: c, context: c.getContext('2d', { willReadFrequently: true }), frame: -1, at:-Infinity };
      brickPictures.set(frame, entry);
    }
    if (entry.frame !== frameNo && lastNow-entry.at+1e-6>=1/30) {
      entry.context.clearRect(0, 0, entry.canvas.width, entry.canvas.height);
      entry.context.drawImage(frame, 0, 0, entry.canvas.width, entry.canvas.height);
      entry.frame = frameNo;entry.at=lastNow;
    }
    return entry.canvas;
  }
  let greyMetalFace = null;
  function metalFace() {
    if(greyMetalFace) return greyMetalFace;
    const c=document.createElement('canvas');c.width=96;c.height=40;
    const x=c.getContext('2d',{willReadFrequently:software});
    const steel=x.createLinearGradient(0,0,0,40);
    for(const [at,colour] of [[0,'#edf0f2'],[.16,'#a1a8b0'],[.46,'#505963'],[.52,'#bbc1c7'],[1,'#424951']])steel.addColorStop(at,colour);
    x.fillStyle=steel;x.fillRect(0,0,96,40);
    x.strokeStyle='#e0e5eb';x.lineWidth=2;x.strokeRect(1,1,94,38);
    x.strokeStyle='rgba(255,255,255,.15)';x.lineWidth=1;
    for(let y=5;y<38;y+=4){x.beginPath();x.moveTo(4,y);x.lineTo(92,y);x.stroke();}
    x.fillStyle='rgba(255,255,255,.18)';x.beginPath();x.moveTo(32,0);x.lineTo(44,0);x.lineTo(64,40);x.lineTo(52,40);x.closePath();x.fill();
    x.fillStyle='#262b30';for(const px of [8,88]){x.beginPath();x.arc(px,20,2,0,7);x.fill();}
    return greyMetalFace=c;
  }
  const toughFaces = new Map();
  function toughFace(br, grey, mix) {
    const damage=br.strength-br.hp;
    const tint=grey?0:Math.round(Math.max(.55,mix)*16)/16;
    const key=`${br.strength}:${damage}:${grey}:${tint}`;
    if(toughFaces.has(key)) return toughFaces.get(key);
    const c=document.createElement('canvas');c.width=96;c.height=56;
    const x=c.getContext('2d',{willReadFrequently:true});
    const rgb=durabilityColour(br.hp, grey, brickHue);
    roundRect(x,1,1,94,54,6);x.fillStyle=col(rgb,tint);x.fill();
    finishBrick(x,96,56);
    if(damage>0) {
      // Clip highlights as well as the dark fissures inside the brick face.
      x.save();roundRect(x,4,4,88,48,4);x.clip();
      const cracks=()=>{
        x.beginPath();x.moveTo(49,6);x.lineTo(43,17);x.lineTo(51,25);x.lineTo(44,35);x.lineTo(47,49);
        x.moveTo(43,17);x.lineTo(31,21);x.lineTo(27,28);
        if(damage>1){x.moveTo(88,34);x.lineTo(76,29);x.lineTo(68,38);x.lineTo(60,34);x.lineTo(55,48);}
      };
      x.save();x.translate(1,1);cracks();x.strokeStyle='rgba(255,255,255,.4)';x.lineWidth=2;x.stroke();x.restore();
      cracks();x.strokeStyle='rgba(12,10,19,.85)';x.lineWidth=2;x.stroke();x.restore();
    }
    if(toughFaces.size>=256)toughFaces.delete(toughFaces.keys().next().value);
    toughFaces.set(key,c);return c;
  }
  function drawBricks(s, mix, coresOnly = false, outro = null) {
    const landing = !reduced && s.wallAge < 1.9;
    const hide = s.mod && s.state === 'colour' ? clamp(s.mod.hideBricks || 0, 0, 1) : 0;   // BLANK: the wall fades from sight, still there
    if (hide >= 1) return;
    for (const br of s.bricks) {
      if (!br.alive || !!br.irisCore !== coresOnly) continue;
      const letter=br.letter || br.finaleLetter;
      // Stable per-brick timing, independent of moving formations and frame rate.
      const seed = ((br.row + 1) * 73 + (br.col + 1) * 151) % 97;
      const delay = seed / 97 * .28;
      const arrival = landing ? clamp((s.wallAge - delay) / 1.6, 0, 1) : 1;
      const fall = Math.min(1, arrival / .35);
      const spring = clamp((arrival - .35) / .65, 0, 1);
      const bounce = landing && arrival > .35 ? Math.abs(Math.sin(spring * Math.PI * 2)) * 85 * (1 - spring) : 0;
      const squash = landing && arrival > .35 ? Math.cos(spring * Math.PI * 4) * .07 * (1 - spring) : 0;
      const offsetY = landing ? -(Math.max(0, br.y) + br.h + 28) * (1 - fall * fall) - bounce : 0;
      const jelly = rungs(4) ? (br.jelly || 0) : 0;
      const sx = 1 + jelly * 0.18 * Math.sin(jelly * 9) + squash, sy = 1 - jelly * 0.22 * Math.sin(jelly * 9) - squash;
      const px = br.push ? br.push.dx || 0 : 0, py = br.push ? br.push.dy || 0 : 0;
      const cx = br.x + br.w / 2 + px, cy = br.y + br.h / 2 + py + offsetY;
      const extent=Math.hypot(br.w*sx,br.h*sy)/2+30;
      if(!outro&&(cx+extent<0||cx-extent>W||cy+extent<0||cy-extent>H))continue;
      g.save();
      g.globalAlpha = (landing ? Math.min(1, arrival * 12) : 1) * (1 - hide) * (br.irisAlpha ?? 1);
      if (outro) pullFinalePiece(cx, cy, outro);
      g.translate(cx, cy); if (br.angle) g.rotate(br.angle); g.scale(sx, sy);
      if(metalActive(br,s.state)) {
        g.drawImage(metalFace(),-br.w/2,-br.h/2,br.w,br.h);
        g.restore();continue;
      }
      if(br.strength && !br.finaleHinge) {
        g.drawImage(toughFace(br,s.state==='grey'||br.finaleGrey,mix),-br.w/2,-br.h/2,br.w,br.h);
        if(letter && s.state==='colour') {
          g.save();if(br.angle)g.rotate(-br.angle);g.fillStyle='#192930';
          drawLetter(letter,(s.spell||br.finaleMotif==='spell')?Math.min(25,Math.min(br.w,br.h)*.65):11,2);g.restore();
        }
        g.restore();continue;
      }
      const gifOn = br.gif === true || (typeof br.gif === 'number' && br.gif >= 0);
      const frame = gifOn && rungs(1) && s.state === 'colour' && media ? brickPicture(media.frame(typeof br.gif === 'number' ? br.gif : br.col)) : null;
      if (gifOn && s.state === 'colour') tierAura(br.tier || 1, 0, 0, br.w + 26, br.h + 24);
      if (!gifOn && !br.powerup && !br.spiral && !br.split && !br.jackpot && !br.word && !letter && !br.finaleWord && !br.irisCore && !br.pendulumAnchor && !br.finaleHinge && !(br.finaleRing && s.finale?.phase === 'locked')) {
        g.drawImage(plainFace(br, mix, s.state === 'grey'), -br.w/2, -br.h/2, br.w, br.h);
        g.restore(); continue;
      }
      if (br.finaleWord) {
        const fade=Math.min(1,br.ttl/.6,((br.wordLife||6)-br.ttl)/.35);
        g.globalAlpha *= Math.max(.1,fade);
        // Disguised as a quiet HUD notice; the collision target stays invisible.
        g.font='500 14px ui-monospace, Consolas, monospace';g.textAlign='center';g.textBaseline='middle';
        g.fillStyle='rgba(0,0,0,.55)';g.fillText(br.finaleWord,0,1);
        g.fillStyle='rgba(220,220,228,.85)';g.fillText(br.finaleWord,0,0);
      } else if (br.finaleRing && s.finale?.phase==='locked') {
        // A hard silver bevel remains legible in grey. No colour-rung gate.
        roundRect(g,-br.w/2,-br.h/2,br.w,br.h,2);
        const steel=g.createLinearGradient(0,-br.h/2,0,br.h/2);
        steel.addColorStop(0,'#edf0f2');steel.addColorStop(.16,'#a1a8b0');
        steel.addColorStop(.46,'#505963');steel.addColorStop(.52,'#bbc1c7');steel.addColorStop(1,'#424951');
        g.fillStyle=steel;g.fill();g.strokeStyle='#e0e5eb';g.lineWidth=1;g.stroke();
        g.save();g.clip();
        const shift=reduced?0:Math.sin(s.time*.8+br.col*.18)*br.w*.25;
        g.fillStyle=`rgba(255,255,255,${.12+(br.pushT||0)*.55})`;
        g.beginPath();g.moveTo(-9+shift,-br.h/2);g.lineTo(-3+shift,-br.h/2);
        g.lineTo(9+shift,br.h/2);g.lineTo(3+shift,br.h/2);g.closePath();g.fill();
        g.strokeStyle='rgba(255,255,255,.14)';g.lineWidth=.5;
        for(let y=-br.h/2+3;y<br.h/2;y+=3){g.beginPath();g.moveTo(-br.w/2+2,y);g.lineTo(br.w/2-2,y);g.stroke();}
        g.restore();
        g.fillStyle='#262b30';for(const x of [-br.w/2+3,br.w/2-3]) {g.beginPath();g.arc(x,0,1.1,0,7);g.fill();}
      } else if (br.pendulumAnchor || br.finaleHinge) {
        const remaining=br.hp||1,r=br.w/2,tint=tierColour(remaining);
        if(s.state==='colour')tierAura(remaining,0,0,br.w+30,br.h+30,.75);
        g.beginPath();g.arc(0,0,r,0,7);g.fillStyle=col([24,18,31],mix);g.fill();
        g.save();g.clip();
        const rot=reduced?0:-s.time,tile=wellFx.tile('whirl',rot,0,mix,56,frameNo);
        if(tile){g.save();g.rotate(rot);g.drawImage(tile,-r*.83,-r*.83,r*1.66,r*1.66);g.restore();}
        const edge=g.createRadialGradient(0,0,r*.65,0,0,r);
        edge.addColorStop(0,col(tint,mix,0));edge.addColorStop(.86,col(tint,mix,.4));edge.addColorStop(1,col(tint,mix,.85));
        g.fillStyle=edge;g.fillRect(-r,-r,2*r,2*r);g.restore();
        drawBubble(0,0,r,.55);
        g.strokeStyle=col(tint,mix);g.lineWidth=2;g.beginPath();g.arc(0,0,r-1,0,7);g.stroke();
        // Three ring segments and the bubble's purple-to-pink gradient count remaining hits.
        for(let i=0;i<remaining;i++) {g.beginPath();g.arc(0,0,r+3,-Math.PI/2+i*(Math.PI*2)/3+.12,-Math.PI/2+(i+1)*(Math.PI*2)/3-.12);g.stroke();}
      } else if (s.state === 'grey') {
        const special = gifOn || br.spiral || br.split || br.jackpot || br.word;
        roundRect(g, -br.w / 2, -br.h / 2, br.w, br.h, 3);
        g.fillStyle = col(durabilityColour(1, true), 0); g.fill();
        g.fillStyle = special ? '#666' : '#7b7b7b';
        g.fillRect(-br.w / 2 + 2, -br.h / 2 + 2, br.w - 4, special ? 2 : 1);
        g.fillStyle = '#383838'; g.fillRect(-br.w / 2 + 1, br.h / 2 - 3, br.w - 2, 2);
      } else if (br.irisCore) {
        const pulse=reduced?1:1+.035*Math.sin(s.time*4);
        g.scale(pulse,pulse);
        roundRect(g,-br.w/2,-br.h/2,br.w,br.h,9);
        g.fillStyle='#20192d';g.fill();g.strokeStyle='#bca184';g.lineWidth=2;g.stroke();
        roundRect(g,-br.w/2+4,-br.h/2+4,br.w-8,br.h-8,6);
        g.fillStyle='#090c18';g.fill();g.strokeStyle='rgba(255,225,183,.35)';g.lineWidth=1;g.stroke();
        // Vinyl grooves rotate under a fixed play label.
        const radius=Math.min(br.w,br.h)*.34;
        g.fillStyle='#151323';g.beginPath();g.arc(0,0,radius,0,7);g.fill();
        g.strokeStyle='#807083';g.lineWidth=.8;
        for(const band of [.62,.8,1]) {g.beginPath();g.arc(0,0,radius*band,0,7);g.stroke();}
        g.save();g.rotate(reduced?0:s.time*.7);
        g.strokeStyle='rgba(255,225,183,.65)';g.lineWidth=1.5;
        g.beginPath();g.arc(0,0,radius*.88,-.6,.25);g.stroke();g.restore();
        g.fillStyle='#bd84b4';g.beginPath();g.arc(0,0,radius*.46,0,7);g.fill();
        g.fillStyle='#fff0ca';g.beginPath();g.moveTo(-radius*.13,-radius*.25);
        g.lineTo(radius*.27,0);g.lineTo(-radius*.13,radius*.25);g.closePath();g.fill();
        for(let i=0;i<3;i++) {
          g.fillStyle=i<(br.hp||3)?'#ffe2a6':'#51434c';
          g.fillRect(-10+i*8,br.h/2-5,5,2);
        }
      } else if (frame) {
        roundRect(g, -br.w / 2, -br.h / 2, br.w, br.h, 3); g.clip();
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1;
        const k = Math.max(br.w / fw, br.h / fh) * 1.05;
        g.drawImage(frame, -fw * k / 2, -fh * k / 2, fw * k, fh * k);
      } else if (br.spiral) {
        drawSpiralBrick(br, s, mix);
      } else {
        const rgb = durabilityColour(1, false, brickHue);
        roundRect(g, -br.w / 2, -br.h / 2, br.w, br.h, 3);
        g.fillStyle = col(rgb, mix); g.fill();
        g.fillStyle = 'rgba(255,255,255,.18)'; g.fillRect(-br.w / 2 + 2, -br.h / 2 + 2, br.w - 4, 3);
        if (br.split) {
          // Twin pearls and an orbit distinguish extra balls without a live blur pass.
          const orbit = reduced ? 0 : spin * .8;
          g.save();
          g.strokeStyle = col(MINT, mix, .85); g.lineWidth = 1.2;
          g.beginPath(); g.ellipse(0, 0, 12, 6, 0, 0, Math.PI * 2); g.stroke();
          for (const x of [-4, 4]) {
            g.fillStyle = col(VIOLET, mix); g.strokeStyle = col(PINK, mix); g.lineWidth = 1.5;
            g.beginPath(); g.arc(x, 0, 4.5, 0, 7); g.fill(); g.stroke();
            g.fillStyle = '#fffdf5'; g.beginPath(); g.arc(x - 1, -1.5, 1.5, 0, 7); g.fill();
          }
          const sx = Math.cos(orbit) * 12, sy = Math.sin(orbit) * 6;
          g.strokeStyle = col(GOLD, mix); g.lineWidth = 1.5;
          g.beginPath(); g.moveTo(sx - 2.5, sy); g.lineTo(sx + 2.5, sy);
          g.moveTo(sx, sy - 2.5); g.lineTo(sx, sy + 2.5); g.stroke();
          g.restore();
        }
        if (br.word) { g.save(); roundRect(g, -br.w / 2, -br.h / 2, br.w, br.h, 3); g.clip(); drawWordLabel(br, s, mix); g.restore(); }
        else if (letter) { g.save(); if (br.angle) g.rotate(-br.angle); drawLetter(letter,(s.spell || br.finaleMotif === 'spell') ? Math.min(25, Math.min(br.w, br.h) * .72) : 11,.5); g.restore(); }
      }
      if(s.state==='colour'&&br.powerup)drawPowerIcon(g,br.powerup,0,0,Math.min(9,br.h*.36),s.state==='grey');
      if (!br.finaleWord && !br.irisCore && !br.pendulumAnchor && !br.finaleHinge) {
        g.drawImage(finishTile(),-br.w/2,-br.h/2,br.w,br.h);
      }
      g.restore();
    }
  }
  // Text outlines are expensive on Firefox's software canvas. Keep the glyphs,
  // but draw the moving plate, sheen and glitch live in brick space.
  const letterFaces=new Map();
  function drawLetter(letter,size,y=0) {
    let face=letterFaces.get(letter);
    if(!face){
      face=document.createElement('canvas');face.width=face.height=64;
      const x=face.getContext('2d',{willReadFrequently:true});
      x.font=`900 48px ${FONT}`;x.textAlign='center';x.textBaseline='middle';
      x.fillStyle=x.strokeStyle='rgba(20,20,40,.8)';x.lineWidth=1.3;
      x.strokeText(letter,32,32);x.fillText(letter,32,32);
      if(letterFaces.size>=64)letterFaces.delete(letterFaces.keys().next().value);
      letterFaces.set(letter,face);
    }
    const edge=size*64/48;g.drawImage(face,-edge/2,y-edge/2,edge,edge);
  }
  const wordFaces = new Map();
  function wordFace(text, mix, colour) {
    const tone = Math.round(mix * 16) / 16, key = text + '|' + tone + '|' + colour;
    let face = wordFaces.get(key);
    if (face) return face;
    const c = document.createElement('canvas'), x = c.getContext('2d', { willReadFrequently: true });
    x.font = '900 10px ' + FONT;
    if ('letterSpacing' in x) x.letterSpacing = '0.5px';
    const width = Math.max(1, x.measureText(text).width);
    c.width = Math.ceil(width + 12); c.height = 24;
    x.font = '900 10px ' + FONT; x.textAlign = 'center'; x.textBaseline = 'middle';
    if ('letterSpacing' in x) x.letterSpacing = '0.5px';
    x.translate(c.width / 2, 12);
    if (colour) {
      const grad = x.createLinearGradient(0, -5, 0, 5);
      grad.addColorStop(0, col(WHITE, tone)); grad.addColorStop(.5, col([255,214,238], tone)); grad.addColorStop(1, col(PINK, tone));
      x.lineWidth = 3; x.strokeStyle = col(PINK, tone, .24); x.strokeText(text, 0, .5);
      x.lineJoin = 'round'; x.lineWidth = 1.6; x.strokeStyle = 'rgba(16,6,26,.8)'; x.strokeText(text, 0, .5);
      x.fillStyle = grad;
    } else x.fillStyle = 'rgba(190,190,190,.7)';
    x.fillText(text, 0, .5);
    face = { canvas: c, width };
    if (wordFaces.size >= 192) wordFaces.delete(wordFaces.keys().next().value);
    wordFaces.set(key, face); return face;
  }
  const SCRAMBLE = '#%&@!?<>/=+*XZKQ0173';
  /** A cheap per-letter hash for the glitch scramble: stable within a frame pair, different across bricks. */
  const gh = (a, b, c) => { let h = (a * 374761393 + b * 668265263 + c * 2246822519) | 0; h = Math.imul(h ^ (h >>> 13), 1274126177); return ((h ^ (h >>> 16)) >>> 0) / 4294967296; };
  /** The word on a word brick, drawn in brick space (0,0 = centre): a dark plate, gradient text with a glow and a
   *  slow sheen; while brick.glitch runs down the plate tears into offset slices, the text splits into colours and
   *  its letters scramble before the new word settles (owner, 2026-09-19: the bare white text was underwhelming). */
  function drawWordLabel(br, s, mix) {
    const colour = s.state === 'colour', gl = clamp(br.glitch || 0, 0, 1);
    const pw = br.w - 4, ph = br.h - 4, hx = pw / 2, hy = ph / 2;
    const text = String(br.word || '').toUpperCase().slice(0, 9);
    let shown = text;
    if (gl > 0.45) {                                                       // the old word tearing into the new one
      const p = gl > 0.75 ? 0.65 : 0.3, fr = frameNo >> 1;
      shown = [...text].map((ch, i) => (ch !== ' ' && gh(fr, i, br.row * 31 + br.col) < p ? SCRAMBLE[(gh(fr + 7, i, br.col) * SCRAMBLE.length) | 0] : ch)).join('');
    }
    g.font = `900 10px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
    if ('letterSpacing' in g) g.letterSpacing = '0.5px';
    const angle = br.angle || 0, ca = Math.abs(Math.cos(angle)), sa = Math.abs(Math.sin(angle));
    const textWidth = Math.max(1, Math.min(ca > .001 ? (pw - 5 - 10 * sa) / ca : Infinity, sa > .001 ? (ph - 5 - 10 * ca) / sa : Infinity));
    const face = gl <= 0 ? wordFace(shown, mix, colour) : null;
    const tw = face ? face.width : Math.max(1, g.measureText(shown).width), k = Math.min(1, textWidth / tw);
    const label = (dx, dy) => {
      g.save(); g.translate(dx, dy);
      roundRect(g, -hx, -hy, pw, ph, 2.5);
      g.fillStyle = colour ? 'rgba(22,9,34,.66)' : 'rgba(34,34,34,.55)'; g.fill();
      g.clip();
      g.fillStyle = 'rgba(255,255,255,.10)'; g.fillRect(-hx + 1, -hy + 1, pw - 2, 1);
      if (colour) {                                                        // the sheen: a slanted band crossing the plate every few seconds
        const ph2 = ((s.time || 0) * 0.3 + (br.row * 3 + br.col) * 0.137) % 1;
        const sx = -hx - 8 + ph2 * (pw + 16);
        g.fillStyle = 'rgba(255,255,255,.13)'; g.beginPath(); g.moveTo(sx, -hy); g.lineTo(sx + 5, -hy); g.lineTo(sx + 1, hy); g.lineTo(sx - 4, hy); g.closePath(); g.fill();
      }
      if (angle) g.rotate(-angle);
      g.scale(k, 1);
      if (face) {
        g.drawImage(face.canvas, -face.canvas.width / 2, -12);
      } else if (colour) {
        const grad = g.createLinearGradient(0, -5, 0, 5);
        grad.addColorStop(0, col(WHITE, mix)); grad.addColorStop(0.5, col([255, 214, 238], mix)); grad.addColorStop(1, col(PINK, mix));
        g.lineWidth = 3 + gl * 2; g.strokeStyle = col(PINK, mix, .24); g.strokeText(shown, 0, .5);
        if (gl > 0) {                                                      // chromatic split, widening with the glitch
          g.fillStyle = col(MINT, mix, 0.85 * gl); g.fillText(shown, -2.4 * gl, 0.5);
          g.fillStyle = col(VIOLET, mix, 0.85 * gl); g.fillText(shown, 2.4 * gl, 0.5);
        }
        g.lineJoin = 'round'; g.lineWidth = 1.6; g.strokeStyle = 'rgba(16,6,26,.8)'; g.strokeText(shown, 0, 0.5);
        g.fillStyle = grad; g.fillText(shown, 0, 0.5);
        g.shadowBlur = 0;
      } else {
        g.fillStyle = 'rgba(190,190,190,.7)'; g.fillText(shown, 0, 0.5);
      }
      g.restore();
    };
    if (gl <= 0 || reduced) { label(0, 0); return; }
    // Torn into three horizontal slices, each shoved sideways by its own amount that dies with the glitch.
    const fr = frameNo >> 1, cuts = [-hy, -hy + ph * (0.25 + 0.2 * gh(fr, 1, br.col)), -hy + ph * (0.6 + 0.2 * gh(fr, 2, br.col)), hy];
    for (let i = 0; i < 3; i++) {
      g.save(); g.beginPath(); g.rect(-hx - 6, cuts[i], pw + 12, cuts[i + 1] - cuts[i]); g.clip();
      label((gh(fr, 3 + i, br.row * 31 + br.col) - 0.5) * 7 * gl, 0);
      g.restore();
    }
  }
  /** A spiral brick in brick space: its Loom field in a disc a little larger than the brick, clipped to the brick, a pink rim. */
  function drawSpiralBrick(br, s, mix) {
    const rot = -(s.time || 0) * 1.4 * (br.spin || 1);                     // counter-clockwise, the way the well turns
    const tile = wellFx.tile(br.spiral, rot, br.hue || 0, mix, 56, frameNo);
    roundRect(g, -br.w / 2, -br.h / 2, br.w, br.h, 3);
    g.fillStyle = col([30, 14, 44], mix); g.fill();
    g.save(); g.clip();
    if (tile) { const d = Math.max(br.w, br.h) * 1.3; g.save(); if (!reduced) g.rotate(rot); g.drawImage(tile, -d / 2, -d / 2, d, d); g.restore(); }
    else drawSpiral(g, 0, 0, br.w * 0.5, rot, col(PINK, mix), 0.8, 2);
    g.fillStyle = 'rgba(255,255,255,.10)'; g.fillRect(-br.w / 2 + 2, -br.h / 2 + 2, br.w - 4, 2);
    g.restore();
    tierAura(1, 0, 0, br.w + 12, br.h + 12, .35);
    g.strokeStyle = col(PINK, mix, 0.75); g.lineWidth = 1; g.strokeRect(-br.w / 2 + 0.5, -br.h / 2 + 0.5, br.w - 1, br.h - 1);
    g.shadowBlur = 0;
  }
  /** The OG soap bubble over a picture of radius r: the rim highlights sit on top, the picture shows through. */
  function drawBubble(x, y, r, a = 1) {
    if (!bubbleReady()) return false;
    if (!softBubble) {
      softBubble = document.createElement('canvas'); softBubble.width = softBubble.height = 256;
      const ctx = softBubble.getContext('2d');
      ctx.filter = 'blur(2px)'; ctx.drawImage(BUBBLE, 8, 8, 240, 240);
    }
    const d = r * 2.30;                                                   // the PNG's bubble sits a little inside its square
    g.save(); g.globalAlpha = clamp(a, 0, 1); g.drawImage(softBubble, x - d / 2, y - d / 2, d, d); g.restore();
    return true;
  }
  /** A broken GIF brick's face, tumbling out of the wall until it bursts. */
  function drawPops(s, mix) {
    for (const p of s.pops || []) {
      if (p.spiral) {                                                      // the spiral face swells into a disc on its way to becoming the well
        const k = clamp((p.t || 0) / 0.7, 0, 1), R = lerp(11, 34 * 1.33, k), rot = -(s.time || 0) * 1.4 * (p.spin || 1);
        const tile = wellFx.tile(p.spiral, rot, p.hue || 0, mix, 56, frameNo);
        g.save(); g.translate(p.x, p.y); g.globalAlpha *= 0.9;
        tierAura(1, 0, 0, R * 2 + 30, R * 2 + 30, .5);
        g.beginPath(); g.arc(0, 0, R, 0, 7); g.fillStyle = col([30, 14, 44], mix); g.fill(); g.shadowBlur = 0;
        g.clip();
        if (tile) { g.save(); if (!reduced) g.rotate(rot); g.drawImage(tile, -R * 1.08, -R * 1.08, R * 2.16, R * 2.16); g.restore(); }
        else drawSpiral(g, 0, 0, R, rot, col(PINK, mix), 0.8, 2);
        g.restore();
        continue;
      }
      const frame = media && rungs(1) ? media.frame(p.gif) : null;
      g.save(); g.translate(p.x, p.y); g.rotate(p.rot || 0);
      g.globalAlpha *= 0.72;                                              // dimmed a little while it falls (owner, 2026-09-18)
      tierAura(1, 0, 0, p.w + 24, p.h + 24, .4);
      roundRect(g, -p.w / 2, -p.h / 2, p.w, p.h, 3);
      if (frame) {
        g.fillStyle = col(VIOLET, mix); g.fill(); g.shadowBlur = 0; g.clip();
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1, k = Math.max(p.w / fw, p.h / fh) * 1.05;
        g.drawImage(frame, -fw * k / 2, -fh * k / 2, fw * k, fh * k);
        g.fillStyle = 'rgba(10,4,16,.28)'; g.fillRect(-p.w / 2, -p.h / 2, p.w, p.h);
        g.strokeStyle = col(PINK, mix, 0.8); g.lineWidth = 1; g.strokeRect(-p.w / 2 + 0.5, -p.h / 2 + 0.5, p.w - 1, p.h - 1);
      } else {
        g.fillStyle = col(toRgb(p.color) || PINK, mix); g.fill();
        g.fillStyle = 'rgba(255,255,255,.18)'; g.fillRect(-p.w / 2 + 2, -p.h / 2 + 2, p.w - 4, 3);
      }
      g.restore();
    }
  }
  /** The whirlwind: one of the Loom's fields (render-well.js), its preset, spin and hue the spiral brick's it came out of. */
  function drawWell(s, mix, dt, extras) {
    const well = s.well; if (!well) return;
    const m = (extras && extras.media) || media;
    const inflate = Math.max(0.02, easeOutBack((typeof well.born === 'number' ? well.born : 1) / INFLATE_S));
    g.save();
    // Keep the Loom buffer fixed: growth and breathing are cheap display transforms.
    const cleared = well.persistent && s.bricks.length
      ? 1 - s.bricks.reduce((n, b) => n + (b.alive ? 1 : 0), 0) / s.bricks.length : 0;
    const breath = well.persistent && !reduced ? .03 * Math.sin((well.born || 0) * Math.PI / 2) : 0;
    const size = inflate * (1 + .3 * cleared + breath);
    g.translate(well.x, well.y); g.scale(size, size); g.translate(-well.x, -well.y);
    wellFx.draw(g, well, { mix, col, media: m, dt, sat: s.sat, particles: P,
      pink: PINK, violet: VIOLET, mint: MINT, spiral: drawSpiral });
    // No bubble skin on the well (owner, 2026-09-19): the field is the well, its own torn rim is the edge.
    g.restore();
  }
  function drawColliders(s, mix) {
    for (const c of s.colliders) {
      const inflate = easeOutBack((typeof c.age === 'number' ? c.age : 1) / INFLATE_S);
      const r = c.r * (1 + c.pulse * 0.25) * Math.max(0.02, inflate), frame = media ? media.frame(c.gif) : null;
      const a = clamp(c.alpha, 0, 1), remaining = Math.max(1, (c.tier || 1) - c.hits), tint = tierColour(remaining);
      if (s.state === 'colour') tierAura(remaining, c.x, c.y, r * 2 + 30, r * 2 + 30, a * 0.60);
      g.save(); g.globalAlpha = a;
      g.beginPath(); g.arc(c.x, c.y, r, 0, 7); g.closePath();
      const body = g.createRadialGradient(c.x, c.y, r * .82, c.x, c.y, r * 1.04);
      body.addColorStop(0, col(tint, mix, .65)); body.addColorStop(.86, col(tint, mix, .4)); body.addColorStop(1, col(tint, mix, 0));
      g.fillStyle = body; g.fill();
      if (frame) {
        // The picture sits in a smaller circle inside the bubble and its edge fades into the bubble's body, so
        // there is visible room between the picture and the rim (owner, 2026-09-18).
        const inner = r * 0.93;                                          // a slim band only, the picture nearly at the rim (owner, 2026-09-19)
        g.beginPath(); g.arc(c.x, c.y, inner, 0, 7); g.closePath(); g.clip();
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1, k = Math.max(2 * inner / fw, 2 * inner / fh);
        g.drawImage(frame, c.x - fw * k / 2, c.y - fh * k / 2, fw * k, fh * k);
        const fade = g.createRadialGradient(c.x, c.y, inner * 0.84, c.x, c.y, inner);
        fade.addColorStop(0, col(tint, mix, 0)); fade.addColorStop(1, col(tint, mix, .85));
        g.fillStyle = fade; g.fillRect(c.x - r, c.y - r, 2 * r, 2 * r);
      }
      g.restore();
      drawBubble(c.x, c.y, r, a * .55);
      for (let skin = 1; skin < remaining; skin++) {
        drawBubble(c.x, c.y, r * (1 - skin * .18), a * .25);
      }
    }
  }
  function drawBall(s, b, mix, words) {
    const hgt = clamp((H - 40 - b.y) / (H - 40), 0, 1);                        // soft shadow, grows with height
    g.fillStyle = `rgba(0,0,0,${0.28 - hgt * 0.16})`;
    g.beginPath(); g.ellipse(b.x, b.y + b.r + 3 + hgt * 10, b.r * (1 + hgt * 1.2), b.r * 0.45 * (1 + hgt * 0.6), 0, 0, 7); g.fill();
    if (!reduced) {
      g.save(); g.lineCap = 'round';
      let trailIndex = 0;
      for (const segment of cometSegments(b)) {
        g.strokeStyle = col(b.ghost ? GREY : VIOLET, mix, segment.alpha * .6);
        g.lineWidth = Math.max(.5, b.r * 1.7 * segment.alpha);
        g.beginPath(); g.moveTo(segment.x, segment.y); g.lineTo(segment.nx, segment.ny); g.stroke();
        if (s.state === 'colour' && !b.ghost && s.sat >= .7 && words?.length && trailIndex++ % 5 === 0) {
          g.font = '700 7px ' + FONT; g.textAlign = 'center'; g.textBaseline = 'middle';
          g.fillStyle = col(PINK, mix, segment.alpha * .5);
          g.fillText(words[(wordIdx + trailIndex) % words.length], segment.x, segment.y);
        }
      }
      g.restore();
    }
    if (b.ghost || s.state === 'grey') {
      g.save(); g.globalAlpha = 0.72;
      g.fillStyle = '#9a9a9a'; g.beginPath(); g.arc(b.x, b.y, b.r, 0, 7); g.fill();
      g.strokeStyle = '#d0d0d0'; g.lineWidth = 1; g.stroke();
      g.fillStyle = '#2a2a2a'; g.font = `700 4.2px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillText('OLD SELF', b.x, b.y + 0.3);
      g.restore();
      return;
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
    // A solid light face and dark rim keep the playable ball readable over pictures.
    g.fillStyle = '#fffdf5'; g.strokeStyle = '#21162f'; g.lineWidth = 2.5;
    g.beginPath(); g.arc(0, 0, b.r, 0, 7); g.fill(); g.stroke();
    g.beginPath(); g.arc(0, 0, b.r - 0.5, 0, 7); g.clip();
    drawSpiral(g, 0, 0, b.r * .85, rot, col(VIOLET, mix), 0.55, 2);
    g.fillStyle = '#ffffff'; g.beginPath(); g.arc(0, 0, b.r * .32, 0, 7); g.fill();
    g.restore();
  }
  let junctionFade=0;
  function drawJunctionShield(s,mix,dt) {
    const active=junctionProtected(s);
    junctionFade=active?.65:Math.max(0,junctionFade-dt);
    if(!junctionFade)return;
    const fade=active?1:junctionFade/.65;
    const flicker=active||reduced?1:(Math.cos((.65-junctionFade)*Math.PI*12)>0?1:.2);
    const y=junctionShieldY(s.paddle,H),alpha=fade*flicker;
    g.save();g.lineCap='round';
    for(const [width,colour] of [[10,col(MINT,mix,.15*alpha)],[3,col(MINT,mix,.8*alpha)],[1,col(WHITE,1,.8*alpha)]]) {
      g.lineWidth=width;g.strokeStyle=colour;g.beginPath();g.moveTo(5,y);g.lineTo(W-5,y);g.stroke();
    }
    g.restore();
  }
  function drawPaddle(s, mix, dt) {
    const p = s.paddle, st = rungs(2) ? p.stretch : 0;
    const w = p.w * (1 + st * 0.25 + recoil * 0.1), h = p.h * (1 - st * 0.3), py = p.y + recoil * 5;
    roundRect(g, p.x - w / 2, py - h / 2, w, h, 7);
    g.fillStyle = col(VIOLET, mix); g.fill();
    g.fillStyle = 'rgba(255,255,255,.22)'; roundRect(g, p.x - w / 2 + 3, py - h / 2 + 2, w - 6, 3, 2); g.fill();
    if (s.balls[0]) {
      const b = s.balls[0], dx = b.x - p.x, dy = b.y - py, d = Math.hypot(dx, dy) || 1;
      const lx = dx / d * 1.6, ly = dy / d * 1.2;
      blinkAt -= dt; if (blinkAt <= 0) { blink = 0.12; blinkAt = 3 + rng() * 2; } blink = Math.max(0, blink - dt);
      for (const ex of [-12, 12]) {
        if (blink > 0) { g.strokeStyle = '#fff'; g.lineWidth = 1.6; g.beginPath(); g.moveTo(p.x + ex - 3.5, py); g.lineTo(p.x + ex + 3.5, py); g.stroke(); continue; }
        g.fillStyle = '#fff'; g.beginPath(); g.arc(p.x + ex, py - 1, 4.6, 0, 7); g.fill();
        g.fillStyle = '#1a1a2e'; g.beginPath(); g.arc(p.x + ex + lx, py - 1 + ly, 2.2, 0, 7); g.fill();
      }
      const mood = paddleMood(s.balls, p, happy);
      g.strokeStyle = '#1a1a2e'; g.lineWidth = 1.6; g.beginPath(); g.moveTo(p.x - 5, py + 2);
      g.quadraticCurveTo(p.x, py + (mood === 'happy' ? 9 : mood === 'sad' ? -4 : 3), p.x + 5, py + 2); g.stroke();
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
  function drawOverlays(s, dt, mix, word, now) {
    if (word) {
      // The flash near the ball: a chromatic triple that lands from large to size, plus one burst of sparkles and an
      // afterglow stamp the first frame it shows (owner, 2026-09-19: the bare text was underwhelming).
      if (word.at !== lastFlashAt) {
        lastFlashAt = word.at;
        P.burst(word.x, word.y, PINK, 10, 150, 0.5, { rise: 40, gv: 120, r0: 1.5, r1: 2 });
        P.burst(word.x, word.y, MINT, 6, 90, 0.6, { rise: 60, gv: 40, r0: 1, r1: 1.5 });
        stamps.push({ kind: 'ring', x: word.x, y: word.y, r0: 4, r1: 46, life: 0.3, rgb: MINT });
        stamps.push({ kind: 'word', text: String(word.text).toUpperCase(), x: word.x, y: word.y, life: 0.32, rgb: PINK, rgb2: MINT, size: 30, alpha: 0.4 });
      }
      const span = Math.max(0.016, (word.until || 0) - (word.at || 0)), tt = clamp(((now || 0) - (word.at || 0)) / span, 0, 1);
      const k = 1.5 - 0.5 * tt;
      g.save(); g.translate(word.x, word.y); g.scale(k, k); g.font = `900 30px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.lineWidth = 6; g.strokeStyle = col(PINK, mix, .2); g.strokeText(String(word.text).toUpperCase(), 0, 0);
      g.fillStyle = col(MINT, mix, 0.9); g.fillText(word.text, 2.5, 0);
      g.fillStyle = col(VIOLET, mix, 0.9); g.fillText(word.text, -2.5, 0);
      g.shadowBlur = 0;
      g.fillStyle = col(PINK, mix, 0.95); g.fillText(word.text, 0, 0);
      g.fillStyle = 'rgba(255,255,255,.6)'; g.fillText(word.text, 0, -1.5);
      g.restore();
    }
    for (let i = shockwaves.length - 1; i >= 0; i--) {
      const sw = shockwaves[i]; sw.at += dt; if (sw.at >= sw.life) { shockwaves.splice(i, 1); continue; }
      const t = sw.at / sw.life;
      g.strokeStyle = col(PINK, mix, 1 - t); g.lineWidth = 10 * (1 - t) + 1; g.beginPath(); g.arc(sw.x, sw.y, 20 + t * 520, 0, 7); g.stroke();
      g.strokeStyle = col(MINT, mix, 0.6 * (1 - t)); g.lineWidth = 3; g.beginPath(); g.arc(sw.x, sw.y, 10 + t * 380, 0, 7); g.stroke();
    }
    stamps.draw(g, dt, col, mix);
    if (s.state === 'grey' && s.fractures > 0) {
      g.save(); g.strokeStyle = 'rgba(222,235,245,.32)';
      strokeLines(g, web.lines, clamp(s.fractures, 0, 1));
      strokeLines(g, crack, clamp(s.fractures, 0, 1));
      g.restore();
    }
    if (breakoutFlash > 0) {
      const a = breakoutFlash / 0.65, frame = breakoutGif >= 0 && media && media.frame ? media.frame(breakoutGif) : null;
      g.save(); g.globalAlpha = a * 0.55;
      g.fillStyle = '#ff5fa2'; g.fillRect(0, 0, W, H);
      if (frame) {
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1;
        const k = Math.min(W * 0.8 / fw, H * 0.65 / fh);
        g.globalAlpha = a * 0.7; g.drawImage(frame, (W - fw * k) / 2, (H - fh * k) / 2, fw * k, fh * k);
      }
      g.restore(); breakoutFlash = Math.max(0, breakoutFlash - dt);
    }
    if (flash > 0) { flash = Math.max(0, flash - dt * 2.5); g.fillStyle = `rgba(255,255,255,${flash * 0.175})`; g.fillRect(0, 0, W, H); }
    if (wipe > 0) {                                    // the RELAPSE wipe: 100 ms sweep of grey
      wipe -= dt; const t = clamp(1 - wipe / 0.1, 0, 1);
      g.fillStyle = 'rgba(120,120,120,.9)'; g.fillRect(0, 0, W * t, H);
    }
  }
  // Last canvas pass: the shell stays crisp above the picture wash and post effects.
  function drawDrifters(dt) {
    g.save(); g.setTransform(scale, 0, 0, scale, ox, oy);
    g.globalCompositeOperation = 'source-over'; g.filter = 'none'; g.shadowBlur = 0;
    g.beginPath(); g.rect(0, 0, W, H); g.clip();
    for (let i = drifters.length - 1; i >= 0; i--) {
      const d = drifters[i]; d.at += dt;
      if (d.at >= d.life) { drifters.splice(i, 1); continue; }
      const t = d.at / d.life;
      const x = d.x + (reduced ? 0 : Math.sin(t * Math.PI * 4) * 8);
      const y = reduced ? d.y : d.y - t * (d.y + 48);
      g.globalAlpha = 0.42 * (1 - Math.max(0, (t - 0.5) / 0.5));
      g.fillStyle = '#9a9a9a'; g.beginPath(); g.arc(x, y, d.r, 0, 7); g.fill();
      g.strokeStyle = '#b8b8b8'; g.lineWidth = 0.7; g.stroke();
      g.fillStyle = '#252525'; g.font = '700 ' + (d.r * 0.42) + 'px ' + FONT;
      g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText('OLD SELF', x, y);
    }
    g.restore();
  }
  function drawGreyPost(s) {
    g.save();
    if (s.smear) { const sm = s.smear, a = clamp(sm.a, 0, 1);            // the last hit, burning away
      const grd = g.createRadialGradient(sm.x, sm.y, 0, sm.x, sm.y, 70 + (1 - a) * 60);
      grd.addColorStop(0, `rgba(190,190,190,${0.22 * a})`); grd.addColorStop(1, 'rgba(190,190,190,0)');
      g.fillStyle = grd; g.fillRect(0, 0, W, H); }
    g.fillStyle = vignette; g.fillRect(0, 0, W, H);
    if (!reduced && noisePat) { g.globalAlpha = 0.045; g.fillStyle = noisePat;
      g.translate((rng() * 128) | 0, (rng() * 128) | 0); g.fillRect(-128, -128, W + 256, H + 256); }
    g.restore();
  }
  function drawHud(s, mix) {                                              // only the saturation bar; the counters live in the DOM strip
    g.fillStyle = 'rgba(0,0,0,.35)'; g.fillRect(0, 0, W, 4);
    g.fillStyle = col(PINK, mix); g.fillRect(0, 0, W * (s.state === 'grey' ? 0 : s.sat), 4);
  }

  /* ------------------------------------------------------------ frame */
  function drawBubbleRewards(dt) {
    for (let i = bubbleRewards.length - 1; i >= 0; i--) {
      const reward = bubbleRewards[i]; reward.t += dt;
      if (reward.t >= reward.life) { bubbleRewards.splice(i, 1); continue; }
      const full = reward.tier === 3;
      const fade = full ? Math.pow(Math.min(1, reward.t / 0.4, (reward.life - reward.t) / 0.4), .8)
        : Math.min(1, reward.t / 0.12) * Math.min(1, (reward.life - reward.t) / 0.6) * 0.7;
      reward.indices.forEach((index, j) => {
        if (media) media.pin(index);
        const frame = media ? media.frame(index) : null;
        if (!frame) return;
        const duo = reward.indices.length === 2 && !full;
        const progress = reduced ? 1 : Math.min(1, reward.t / (duo ? reward.life : 0.8));
        const fw = frame.width || frame.naturalWidth || 1, fh = frame.height || frame.naturalHeight || 1;
        const {w:targetW,h:targetH}=rewardBounds(cw,ch,fw,fh,duo,full);
        const width = reward.r * 2 * scale + (targetW - reward.r * 2 * scale) * progress;
        const height = reward.r * 2 * scale + (targetH - reward.r * 2 * scale) * progress;
        const tx = full ? cw / 2 : cw * (reward.indices.length === 2 ? (j ? 0.75 : 0.25) : 0.5);
        let x = full ? tx : ox + reward.x * scale + (tx - ox - reward.x * scale) * progress;
        let y = full ? ch / 2 : oy + reward.y * scale + (ch / 2 - oy - reward.y * scale) * progress;
        let rotation = 0;
        if (duo && !reduced) {
          const travel = Math.min(1, reward.t / 0.7);
          const angle = reward.t / reward.life * Math.PI * 2 + j * Math.PI;
          const radius = Math.min(cw * 0.24, ch * 0.19) * Math.sin(progress * Math.PI / 2);
          x = ox + reward.x * scale + (cw / 2 - ox - reward.x * scale) * travel + Math.cos(angle) * radius;
          y = oy + reward.y * scale + (ch / 2 - oy - reward.y * scale) * travel + Math.sin(angle) * radius;
          rotation = Math.sin(angle) * 0.35 + (j ? -1 : 1) * progress * 0.3;
        }
        g.save(); g.setTransform(1, 0, 0, 1, 0, 0); g.globalAlpha = fade * (full ? 0.6 : 1);
        g.translate(x, y); g.rotate(rotation);
        roundRect(g, -width / 2, -height / 2, width, height, Math.min(width, height) * 0.075); g.clip();
        const k = Math.max(width / fw, height / fh);
        g.drawImage(frame, -fw * k / 2, -fh * k / 2, fw * k, fh * k);
        g.restore();
      });
    }
  }

  // Three thin water traces share the formation's pulse clock, with no surfaces or blur.
  function drawFinaleRipples(f) {
    if (reduced || f.phase !== 'released' || f.stage !== 2) return;
    const waveRadius = (((f.stageAge || 0) % FINALE_PULSE_PERIOD) - .8) * 240;
    if (waveRadius <= 0) return;
    const cx=f.centreX ?? W/2, cy=f.centreY ?? H*.28;
    const reach=Math.hypot(W,H), fade=clamp(1-waveRadius/reach,0,1);
    g.save();g.lineWidth=1.4;
    for(let ring=0;ring<3;ring++) {
      const radius=waveRadius-ring*24;
      if(radius<16)continue;
      g.strokeStyle=ring===0?'#d5c7e6':'#ad8cce';
      g.globalAlpha=fade*(ring===0?.25:.11)*Math.min(1,radius/60);
      g.beginPath();
      for(let point=0;point<=64;point++) {
        const a=point*Math.PI/32;
        const r=radius+Math.sin(a*7-waveRadius*.025+ring)*3;
        const x=cx+Math.cos(a)*r, y=cy+Math.sin(a)*r;
        if(point===0)g.moveTo(x,y);else g.lineTo(x,y);
      }
      g.closePath();g.stroke();
    }
    g.restore();
  }

  function finaleBackgroundProgress(s) {
    const f=s.finale;if(!f?.mixed)return 0;
    if(f.phase==='outro')return .97+.03*clamp((f.outroAge||0)/3.8,0,1);
    if(f.stage===2)return .85+.12*clamp(f.stageAge/60,0,1);
    const alive=s.bricks.reduce((n,b)=>n+(b.alive&&!b.finaleWord?1:0),0);
    return .33+.52*clamp((1-alive/Math.max(1,f.initialBricks))/.8,0,1);
  }
  function drawFinaleCore(s, dt) {
    const f = s.finale;
    if (!f) { finaleOwner = null; return; }
    if (finaleOwner !== f) {
      finaleOwner = f; finaleTarget=null;pupilOffsetX=pupilOffsetY=0; finaleEye.age = 0; finaleFx.reset(); finaleFreezeFrame=null; finaleFreezeAge=-1;
    }
    if (f.phase !== 'interrupt') finaleEye.age += dt;
    finaleEye.x = f.centreX ?? W / 2;
    finaleEye.y = f.centreY ?? H * .28;
    if(!finaleTarget||!s.balls.includes(finaleTarget)||finaleTarget.lost||finaleTarget.falling)
      finaleTarget=s.balls.find(b=>!b.lost&&!b.falling)||null;
    const target=finaleTarget;
    finaleEye.r = f.coreRadius || 40;
    finaleEye.pull = finaleEye.r * 1.1;
    finaleEye.rot = reduced ? 0 : finaleEye.age * .19;
    finaleEye.fade = f.phase === 'forming' ? .7 * clamp(f.age / 1.4, 0, 1) : .7;
    const tick = Math.floor(finaleEye.age * 12);
    const tear = !reduced && tick % 47 === 0;
    g.save();
    if (tear) g.translate(Math.sin(tick * 7) * 2.5, 0);
    // The hostile eye stays monochrome against the mixed world.
    const eyeMix=.001;
    g.save();
    finaleFx.draw(g, finaleEye, { mix: eyeMix, col, dt, particles: null,
      pink: GREY, violet: GREY, mint: GREY, spiral: drawSpiral });
    g.restore();
    // Only the pupil tracks the ball. The spiral housing stays centred.
    let pupilX=finaleEye.x,pupilY=finaleEye.y;
    if(target&&!reduced){
      const dx=target.x-pupilX,dy=target.y-pupilY,d=Math.hypot(dx,dy)||1;
      const blend=1-Math.exp(-Math.max(0,dt)*6);
      pupilOffsetX+=(dx/d*4-pupilOffsetX)*blend;pupilOffsetY+=(dy/d*4-pupilOffsetY)*blend;
      pupilX+=pupilOffsetX;pupilY+=pupilOffsetY;
    }
    g.fillStyle='#29272d';g.beginPath();g.arc(pupilX,pupilY,4.5,0,Math.PI*2);g.fill();
    g.fillStyle='#bbb8c1';g.beginPath();g.arc(pupilX-1,pupilY-1,1.4,0,Math.PI*2);g.fill();
    if (tear) {
      g.fillStyle = 'rgba(18,18,22,.65)';
      for (let i = 0; i < 3; i++) {
        const y = finaleEye.y + Math.sin(tick + i * 4) * finaleEye.r * .7;
        g.fillRect(finaleEye.x - finaleEye.r * .8, y, finaleEye.r * 1.6, 1.5);
      }
    }
    // The incoming stage is announced locally, leaving the paddle and ball unobscured.
    if (f.stage === 2 && f.stageAge < 1.2) {
      const t = clamp(f.stageAge / 1.2, 0, 1);
      g.strokeStyle = `rgba(218,205,236,${(1 - t) * .35})`;
      g.lineWidth = 1.5;
      g.beginPath(); g.arc(finaleEye.x, finaleEye.y, finaleEye.r + (reduced ? 12 : t * 115), 0, Math.PI * 2); g.stroke();
    }
    g.restore();
  }

  // Visual-only suction keeps frozen collision geometry untouched.
  function pullFinalePiece(x, y, f) {
    if (reduced) return;
    const t=clamp((f.outroAge || 0)/2.8,0,1), cx=f.centreX ?? W/2, cy=f.centreY ?? H*.28;
    const dx=x-cx,dy=y-cy,turn=t*t*8, radius=Math.pow(1-t,2.3);
    const px=cx+(dx*Math.cos(turn)-dy*Math.sin(turn))*radius;
    const py=cy+(dx*Math.sin(turn)+dy*Math.cos(turn))*radius;
    g.translate(px,py);g.rotate(turn);g.scale(Math.max(.01,1-t*t),Math.max(.01,1-t*t));g.translate(-x,-y);
  }

  function drawFinaleOutro(s, mix, dt) {
    const f=s.finale,t=Math.max(0,f.outroAge || 0);
    if(finaleOutroOwner!==f){finaleOutroOwner=f;finaleOutroFrame=null;}
    if(foreground){foreground.remove();foreground=null;}
    g.setTransform(1,0,0,1,0,0);g.globalAlpha=1;g.globalCompositeOperation='source-over';
    g.fillStyle='#000';g.fillRect(0,0,cw,ch);
    if(t>=3.8){endingCard.draw(g,cw,ch,t,reduced);return;}
    if(t<2.9 || !finaleOutroFrame) {
      g.save();g.translate(ox,oy);g.scale(scale,scale);
      const cx=f.centreX ?? W/2,cy=f.centreY ?? H*.28,p=clamp(t/2.9,0,1);
      if(!reduced){
        const zoom=1+p*p*5;
        g.translate(cx+(W/2-cx)*p,cy+(H/2-cy)*p);g.scale(zoom,zoom);g.translate(-cx,-cy);
      }
      landscape.draw(g,false,0,true,s.beatPhase,finaleBackgroundProgress(s));
      drawBricks(s,mix,false,f);
      for(const ball of s.balls){g.save();pullFinalePiece(ball.x,ball.y,f);drawBall(s,ball,mix,null);g.restore();}
      g.save();pullFinalePiece(s.paddle.x,s.paddle.y,f);drawPaddle(s,mix,0);g.restore();
      drawFinaleCore(s,dt);g.restore();
      if(reduced){g.fillStyle=`rgba(0,0,0,${clamp(t/3.8,0,1)})`;g.fillRect(0,0,cw,ch);}
      if(t>=2.9){
        finaleOutroFrame=document.createElement('canvas');finaleOutroFrame.width=640;finaleOutroFrame.height=360;
        finaleOutroFrame.getContext('2d',{willReadFrequently:software}).drawImage(canvas,0,0,640,360);
      }
    }
    if(t>=2.9){
      const p=clamp((t-2.9)/.9,0,1);
      g.fillStyle='#000';g.fillRect(0,0,cw,ch);
      if(reduced){g.globalAlpha=1-p;g.drawImage(finaleOutroFrame,0,0,cw,ch);g.globalAlpha=1;}
      else {
        const height=Math.max(1,ch*Math.pow(1-Math.min(1,p/.7),3));
        const width=cw*(1-Math.max(0,(p-.7)/.3));
        g.drawImage(finaleOutroFrame,(cw-width)/2,(ch-height)/2,width,height);
        g.fillStyle=`rgba(230,235,245,${(1-p)*.85})`;g.fillRect((cw-width)/2,ch/2-1,width,2);
      }
    }
  }

  function drawFinale(s, transform) {
    const f=s.finale;if(!f)return;
    g.save();g.setTransform(transform);g.globalAlpha=1;g.globalCompositeOperation='source-over';
    for(const burst of f.bursts) {
      const t=burst.age/.55;g.font='500 14px ui-monospace, Consolas, monospace';g.textAlign='center';g.textBaseline='middle';
      g.globalAlpha=1-t;g.fillStyle='#dddde2';
      if(reduced)g.fillText(burst.text,burst.x,burst.y);
      else for(let stripe=0;stripe<4;stripe++) {
        g.save();g.beginPath();g.rect(burst.x-90,burst.y-14+stripe*7,180,7);g.clip();
        g.fillText(burst.text,burst.x+Math.sin(stripe*9+t*24)*30*t,burst.y);g.restore();
      }
    }
    g.globalAlpha=1;
    if(f.phase==='interrupt') {
      const t=f.age;
      if(!finaleFreezeFrame || t<finaleFreezeAge) {
        finaleFreezeFrame=document.createElement('canvas');
        finaleFreezeFrame.width=640;finaleFreezeFrame.height=360;
        finaleFreezeFrame.getContext('2d').drawImage(canvas,0,0,640,360);
      }
      finaleFreezeAge=t;
      g.fillStyle='#000';g.fillRect(0,0,W,H);
      if(t<1) {
        const jitter=reduced?0:Math.sin(t*79)*1.8;
        g.drawImage(finaleFreezeFrame,jitter,reduced?0:Math.cos(t*61)*1.2,W,H);
        if(!reduced) for(const ball of s.balls) {
          if(ball.lost)continue;
          for(let i=0;i<4;i++) {
            const dx=Math.sin(Math.floor(t*24)*7+i*11)*(4+t*9);
            g.fillStyle=i%2?'rgba(113,232,255,.65)':'rgba(255,117,195,.65)';
            g.fillRect(ball.x-ball.r+dx,ball.y-ball.r+i*ball.r/2,ball.r*2,2);
          }
        }
      } else if(t<1.35) {
        const p=(t-1)/.35;
        if(reduced) {g.globalAlpha=1-p;g.drawImage(finaleFreezeFrame,0,0,W,H);g.globalAlpha=1;}
        else {
          const height=Math.max(1,H*Math.pow(1-Math.min(1,p/.7),3));
          const width=W*(1-Math.max(0,(p-.7)/.3));
          g.drawImage(finaleFreezeFrame,(W-width)/2,(H-height)/2,width,height);
          g.fillStyle=`rgba(225,240,255,${(1-p)*.8})`;g.fillRect((W-width)/2,H/2-1,width,2);
        }
      } else {
        const textAge=t-1.35;
        g.fillStyle='#f0eef0';g.textAlign='center';g.textBaseline='middle';
        g.font=`900 ${textAge<1.1?70:44}px ${FONT}`;
        g.fillText(textAge<1.1?'ENOUGH':'YOU GOTTA STOP',W/2,H/2);
      }
    } else if(f.phase==='locked' && f.age<1) {
      finaleFreezeFrame=null;finaleFreezeAge=-1;
      g.fillStyle=`rgba(0,0,0,${1-f.age})`;g.fillRect(0,0,W,H);
    }
    if(s.balls.some(b=>b.stuck)&&f.phase!=='forming'&&f.phase!=='interrupt') {
      g.globalAlpha=.8;g.fillStyle='#eee';g.textAlign='center';g.font=`700 13px ${FONT}`;
      g.fillText('TAP / CLICK / SPACE TO LAUNCH',W/2,H-80);
    }
    g.restore();
  }
  function draw(snap, a, b, c) {
    let now, dt, word, extras = null;
    if (a && typeof a === 'object') { extras = a; now = extras.now != null ? extras.now : performance.now() / 1000; if (now > 1e6) now /= 1000;
      dt = extras.dt != null ? extras.dt : (lastNow ? now - lastNow : 1 / 60); word = extras.word || null; }
    else { now = a || 0; dt = b != null ? b : 1 / 60; word = c || null; }
    lastNow = now; dt = clamp(dt, 0, 0.05); frameNo++;
    let stageAt = extras?.timings ? performance.now() : 0;
    const mark = name => { if (extras?.timings) { const t = performance.now(); extras.timings[name] = +(t-stageAt).toFixed(2); stageAt=t; } };
    last = snap;
    const s = snap, grey = s.state === 'grey', tr = s.transition || null, ts = typeof s.timeScale === 'number' ? s.timeScale : 1;
    const fxDt = dt * ts;
    let mix = grey ? 0 : (rungs(1) ? clamp(0.2 + s.sat, 0, 1) : 0);
    if(s.finale?.phase==='outro'){drawFinaleOutro(s,mix,dt);return;}
    finaleOutroFrame=null;finaleOutroOwner=null;
    if (!reduced && tr && tr.kind === 'breakout' && grey && tr.t > 0.3 && (((tr.t * 14) | 0) % 3) === 0) mix = clamp(0.2 + (s.savedSat || s.sat), 0, 1);
    if (s.freeze <= 0) spin += fxDt * (2 + 6 * s.sat);
    happy = Math.max(0, happy - dt);
    recoil = Math.max(0, recoil - dt * 6); aberr = Math.max(0, aberr - dt * 3);
    wordAt += dt; if (wordAt > 0.35) { wordAt = 0; wordIdx++; }
    const words = extras && Array.isArray(extras.words) ? extras.words : null;
    const beat = typeof s.beatPhase === 'number' ? Math.pow(1 - s.beatPhase, 3) : 0;
    const camera = cam.step(dt, rng);
    const dropping = s.fx && s.fx.active.some(f => f.key === 'DROP');
    // Keep the level ramp, with all camera shake at half its previous strength.
    const shakeGain = dropping ? 0 : .175 + .175 * clamp((s.stats?.walls || 0) / 7, 0, 1);
    const shk = { sx: camera.sx * shakeGain, sy: camera.sy * shakeGain,
      rot: camera.rot * shakeGain, zoom: 1 + (camera.zoom - 1) * shakeGain };

    g.setTransform(1, 0, 0, 1, 0, 0);
    g.fillStyle = '#000'; g.fillRect(0, 0, cw, ch);
    g.save();
    g.translate(ox + W * scale / 2, oy + H * scale / 2); g.rotate(shk.rot); g.scale(scale * shk.zoom, scale * shk.zoom);
    g.translate(-W / 2 + shk.sx, -H / 2 + shk.sy);
    g.beginPath(); g.rect(0, 0, W, H); g.clip();
    const R = rInfo(s, mix, dt, fxDt);
    const zm = s.mod && Number.isFinite(s.mod.zoom) ? s.mod.zoom : 1;
    if (zm !== 1 && !reduced) {                                              // DEEPER and friends: the field zooms around the ball
      const zb = s.balls[0], zx = zb ? clamp(zb.x, W * 0.3, W * 0.7) : W / 2, zy = zb ? clamp(zb.y, H * 0.3, H * 0.7) : H / 2;
      g.translate(zx, zy); g.scale(zm, zm); g.translate(-zx, -zy);
    }
    wordHooks('world', s, R);
    g.fillStyle = col(BG, mix); g.fillRect(0, 0, W, H);
    landscape.draw(g, grey && !s.finale, dt, !!s.finale, s.beatPhase, finaleBackgroundProgress(s)); mark("backgroundMs");
    if (!grey && mix > 0) {
      if (!ambient) {
        ambient = document.createElement('canvas'); ambient.width = ambient.height = 256;
        const ag = ambient.getContext('2d'), grd = ag.createRadialGradient(128,128,0,128,128,128);
        grd.addColorStop(0, col(VIOLET,1)); grd.addColorStop(1, col(VIOLET,1,0));
        ag.fillStyle = grd; ag.fillRect(0,0,256,256);
      }
      const radius = H * .8;
      g.save(); g.globalAlpha = clamp(.025 * s.sat,0,.025);
      g.drawImage(ambient,W/2-radius,H*.55-radius,radius*2,radius*2); g.restore();
    }
    drawWalls(s, mix);
    if (s.mantra && !grey) {                                                // the mantra, large and faint behind the bricks
      g.save(); g.font = `900 62px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillStyle = col(PINK, mix, 0.09);
      g.fillText(String(s.mantra).toUpperCase(), W / 2, 150); g.restore();
    }
    mark("worldMs");
    if (!grey) drawWell(s, mix, fxDt, extras);
    mark("wellMs");
    if(s.iris) irisFx.draw(g,s.iris.eye,{mix,col,media,dt:fxDt,sat:s.sat,particles:P,
      pink:PINK,violet:VIOLET,mint:MINT,spiral:drawSpiral});
    if(s.finale) drawFinaleRipples(s.finale);
    if(s.finale?.phase==='released'&&s.finale.stage===2) {
      // Sparse dust follows the actual arms without a fullscreen blur or extra surface.
      g.save();
      for(const br of s.bricks)if(br.alive&&br.finaleFeed&&br.chaosId%4===0){
        const a=(br.angle||0),x=br.x+br.w/2,y=br.y+br.h/2;
        g.fillStyle=br.chaosId%2?'rgba(247,164,218,.17)':'rgba(170,129,236,.20)';
        for(let j=-1;j<=1;j+=2){
          const offset=12+(br.chaosId%7)*2;
          g.beginPath();g.arc(x+Math.cos(a)*offset*j,y+Math.sin(a)*offset*j,1+(br.chaosId%3)*.4,0,Math.PI*2);g.fill();
        }
      }
      g.restore();
    }
    drawFinaleCore(s, fxDt); mark("coreMs");
    drawMetronome(g, s, reduced);
    spellFx.background(g, s);
    drawPendulums(s,mix); drawBricks(s, mix); drawPendulums(s,mix,true); if(s.iris) drawBricks(s, mix, true); mark("bricksMs");
    if (!grey) { drawPops(s, mix); drawColliders(s, mix); }
    if (grey) P.clear(); else ballSparkle(s);
    debris.draw(g, fxDt, mix, col, H);
    drawParticles(fxDt, mix);
    smoke.step(g, fxDt, mix, col);
    spellFx.front(g, s, dt);
    const breakoutOnTop = breakoutFlash > 0 || bubbleRewards.length > 0 || irisMelt > 0;
    const ballTransform = g.getTransform();
    for (const bl of s.balls) drawBall(s, bl, mix, words);
    drawPowerups(g,s);
    drawJunctionShield(s,mix,dt);
    drawPaddle(s, mix, dt); mark("particlesBallMs");
    if (!extras) drawCombo(s, mix, dt);                                   // the v2 station shows the combo in its strip
    drawOverlays(s, dt, mix, grey ? null : word, now);
    if (tr && tr.kind === 'relapse' && !grey) {                             // slow-mo drain: colour leaves from the bottom up
      const y0 = H * (1 - clamp(tr.t, 0, 1));
      g.save(); g.globalCompositeOperation = 'saturation'; g.fillStyle = '#808080'; g.fillRect(0, y0, W, H - y0); g.restore();
      g.fillStyle = `rgba(120,120,120,${0.25 * tr.t})`; g.fillRect(0, y0, W, H - y0);
    }
    if (!grey) wordHooks('over', s, R);
    if (!grey && s.breakoutShield) WORD_FX['LET GO'].render.over(g, s, s.breakoutShield, R);
    drawHud(s, mix);
    if (grey) drawGreyPost(s);
    g.restore();

    mark("overlaysMs");
    if (!extras?.skipPost) postProcess(g, canvas, off, {
      aberr: grey || reduced ? 0 : aberr,
      // Local cached glows supply bloom; avoid copying/blending the whole frame continuously.
      bloom: 0,
      glitch: glitch > 0 && !reduced, rng,
    });
    mark("postMs");
    if (glitch > 0) glitch -= dt;
    if (!grey) { g.setTransform(1, 0, 0, 1, 0, 0); wordHooks('post', s, R); }
    mark('wordPostMs');
    if(irisMelt>0) {
      irisMelt=Math.max(0,irisMelt-dt);
      const envelope=Math.min(1,(4-irisMelt)/.35,irisMelt/.7);
      if(!reduced) {
        if(!irisBuffer){irisBuffer=document.createElement('canvas');irisBuffer.width=480;irisBuffer.height=270;}
        const mg=irisBuffer.getContext('2d', { willReadFrequently: software });mg.drawImage(canvas,0,0,480,270);
        g.save();g.setTransform(1,0,0,1,0,0);g.globalAlpha=.5*envelope;
        for(let i=0;i<24;i++) {
          const bend=Math.sin(i*.63+lastNow*1.8)*12*envelope;
          g.drawImage(irisBuffer,i*20,0,20,270,i*cw/24,bend*ch/720,cw/24+1,ch);
        }
        g.restore();
      }
    }
    mark('meltMs');
    drawDrifters(dt);
    if (!grey) drawBubbleRewards(dt);
    // The web host paints fullscreen spirals outside the station's canvas stacking context.
    const spiralAbove = document.querySelector?.('#__fx canvas.fxfull');
    if (spiralAbove) {
      if (!foreground) {
        foreground = document.createElement('canvas');
        foreground.style.cssText = 'position:fixed;pointer-events:none;z-index:2147483001';
        document.body.appendChild(foreground);
      }
      const bounds = canvas.getBoundingClientRect();
      foreground.style.left = bounds.left + 'px'; foreground.style.top = bounds.top + 'px';
      foreground.style.width = bounds.width + 'px'; foreground.style.height = bounds.height + 'px';
      if (foreground.width !== cw || foreground.height !== ch) { foreground.width = cw; foreground.height = ch; }
      const mainContext = g;
      try {
        g = foreground.getContext('2d', { willReadFrequently: software }); g.setTransform(1, 0, 0, 1, 0, 0); g.clearRect(0, 0, cw, ch);
        g.save(); g.setTransform(ballTransform);
        for (const bl of s.balls) {
          if (bl.lost) continue;
          g.strokeStyle = '#19121e'; g.lineWidth = 3;
          g.beginPath(); g.arc(bl.x, bl.y, bl.r + 1, 0, 7); g.stroke();
          drawBall(s, bl, mix, null);
        }
        g.restore();
      } finally { g = mainContext; }
    } else if (foreground) { foreground.remove(); foreground = null; }
    // Keep the playable ball above the breakout wash, shell and post effects.
    if (breakoutOnTop) {
      g.save(); g.setTransform(ballTransform); g.globalAlpha = 1;
      g.globalCompositeOperation = 'source-over'; g.filter = 'none';
      for (const bl of s.balls) {
        g.strokeStyle = '#19121e'; g.lineWidth = 3;
        g.beginPath(); g.arc(bl.x, bl.y, bl.r + 1, 0, 7); g.stroke();
        drawBall(s, bl, mix, null);
      }
      if (bubbleRewards.length || irisMelt>0) drawPaddle(s, mix, 0);
      g.restore();
    }
    drawFinale(s, ballTransform);
    g.save(); g.setTransform(scale,0,0,scale,ox,oy);
    levelIntro.draw(g, s.stats.walls, s.wallAge, W, H, reduced); g.restore();
    mark('foregroundMs');
  }

  const r = { resize, draw, onEvent, onGameEvent: onEvent, toField,
    dispose() { smoke.clear(); endingCard.reset();letterFaces.clear();greyMetalFace=null; toughFaces.clear(); brickPictures.clear(); wordFaces.clear(); plainFaces.clear(); tierGlows.clear(); foreground?.remove(); foreground = null; spellFx.reset(); bubbleRewards.length = 0; P.clear(); shockwaves.length = drifters.length = 0; debris.clear(); stamps.clear(); wellFx.reset(); wellFx.dispose(); irisFx.dispose(); finaleFx.dispose(); finaleFreezeFrame=null; finaleOutroFrame=null; finaleOutroOwner=null; irisBuffer=null; off = null; },
    particleCount: () => P.count() };
  return r;
}
