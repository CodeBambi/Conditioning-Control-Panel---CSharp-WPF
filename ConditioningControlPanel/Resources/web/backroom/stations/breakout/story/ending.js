/* ============================================================================
 * story/ending.js - the story's own ending. Owned by the act 5 lane.
 *
 * story/CONTRACT.md section 7: playStoryEnding(host, opts) -> boolean.
 *
 * The beat, after the office clip has finished and before the Play again card:
 *   1. the 0.9 "crack" from the juice ladder runs across the grey office still
 *      (the same makeCrack geometry render.js strokes at `fractures`),
 *   2. BREAK OUT stamps ONCE over the real world, with one frame of shake,
 *   3. a beat of hold, then a cut to black,
 *   4. hand back: the office ending's own Play again / Menu buttons come up.
 *
 * Reduced motion gets the whole crack at once, no growth and no shake, in the
 * same order. Story mode only: the house game's ending is untouched and this
 * file never runs for it.
 *
 * The pure half (crackPath, endingTimeline, beatAt) carries no DOM and no clock.
 * ==========================================================================*/

import { makeCrack } from '../render-fx.js';

/* ------------------------------------------------------------------ pure */

/** The seed the ending draws with. Picked because it forks: the test pins it. */
export const CRACK_SEED = 3;

/** Tiny deterministic rng, so one seed is always the same fracture. */
export function crackRng(seed) {
  let a = (Math.floor(seed) || 0) >>> 0;
  return function next() {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * The fracture, as polyline branches: `[[x, y], ...]` arrays carrying
 * `.fracture`, `.depth` (0 = a trunk, 1 = a fork) and `.start` (when in the
 * growth the fork begins). Every point sits inside 0..w by 0..h.
 */
export function crackPath(seed = CRACK_SEED, w = 1280, h = 720) {
  return makeCrack(crackRng(seed), Math.max(1, w), Math.max(1, h));
}

/**
 * The beat times, in seconds from the moment the office clip ended.
 * Reduced motion keeps the order and drops the growth and the shake.
 */
export function endingTimeline(reduced = false) {
  if (reduced) {
    return { crackAt: 0, crackGrow: 0, stampAt: 0.4, stampShake: 0, stampPunch: 0,
      blackAt: 1.4, blackFade: 0, cardAt: 2.2 };
  }
  return { crackAt: 0, crackGrow: 1.2, stampAt: 1.2, stampShake: 0.09, stampPunch: 0.14,
    blackAt: 2.3, blackFade: 0.12, cardAt: 3.1 };
}

/** Which beat `t` seconds is in. One word, so a test can read the order back. */
export function beatAt(t, tl) {
  if (t >= tl.cardAt) return 'card';
  if (t >= tl.blackAt) return 'black';
  if (t >= tl.stampAt) return 'stamp';
  return 'crack';
}

/* ------------------------------------------------------------------- DOM */

const STAMP = 'BREAK OUT';
const POLL_MS = 50;
const OFFICE_WAIT_MS = 45000;

/** The office ending is done when its clip has played out (or been skipped). */
function officeDone(office) {
  try { return !office || office.state === 'done' || office.state === 'idle'; } catch (e) { return true; }
}

/** Hold the Play again buttons down until the beat has run. */
function hideActions(actions) { try { if (actions) actions.hidden = true; } catch (e) { /* noop */ } }
function showActions(actions) {
  try {
    if (!actions) return;
    actions.hidden = false;
    actions.querySelector('button')?.focus({ preventScroll: true });
  } catch (e) { /* noop */ }
}

/** Wait for the clip, keeping the buttons down the whole time. */
function waitForOffice(office, actions, sleep) {
  return new Promise((resolve) => {
    let waited = 0;
    const tick = () => {
      hideActions(actions);
      if (officeDone(office) || waited >= OFFICE_WAIT_MS) { resolve(); return; }
      waited += POLL_MS;
      sleep(tick, POLL_MS);
    };
    tick();
  });
}

/** One trunk or fork, traced up to `frac` of its own growth. */
function traceLine(g, pts, frac) {
  const start = pts.start || 0;
  const progress = Math.max(0, Math.min(1, (frac - start) / Math.max(1e-6, 1 - start)));
  if (progress <= 0 || pts.length < 2) return;
  const end = progress * (pts.length - 1), n = Math.floor(end);
  g.beginPath();
  g.moveTo(pts[0][0], pts[0][1]);
  for (let i = 1; i <= n; i++) g.lineTo(pts[i][0], pts[i][1]);
  if (n + 1 < pts.length) {
    const k = end - n;
    g.lineTo(pts[n][0] + (pts[n + 1][0] - pts[n][0]) * k, pts[n][1] + (pts[n + 1][1] - pts[n][1]) * k);
  }
  g.stroke();
}

/** The fracture, in the look of render.js's 0.9 rung: a pale line over a dark lip. */
function drawCrack(g, lines, frac, scale) {
  g.save();
  g.lineJoin = 'bevel'; g.lineCap = 'butt';
  for (const pts of lines) {
    const trunk = !pts.depth;
    g.save();
    g.translate(1.4 * scale, 1.1 * scale);
    g.strokeStyle = 'rgba(0,0,0,.55)';
    g.lineWidth = (trunk ? 2.6 : 1.5) * scale;
    traceLine(g, pts, frac);
    g.restore();
    g.globalAlpha = trunk ? 0.92 : 0.6;
    g.strokeStyle = 'rgba(238,233,247,1)';
    g.lineWidth = (trunk ? 2.2 : 1.2) * scale;
    traceLine(g, pts, frac);
    g.globalAlpha = 1;
  }
  g.restore();
}

/** BREAK OUT, once, heavy, over the real world. */
function drawStamp(g, w, h, since, tl) {
  const punch = tl.stampPunch > 0 ? Math.max(1, 1.16 - 0.16 * Math.min(1, since / tl.stampPunch)) : 1;
  const size = Math.round(Math.min(w / 8.2, h / 5.2));
  g.save();
  g.translate(w / 2, h * 0.47);
  g.scale(punch, punch);
  g.textAlign = 'center'; g.textBaseline = 'middle';
  g.font = '900 ' + size + 'px ui-monospace, Consolas, monospace';
  g.lineJoin = 'round';
  g.lineWidth = Math.max(4, size * 0.14);
  g.strokeStyle = 'rgba(8,6,14,.92)';
  g.strokeText(STAMP, 0, 0);
  g.fillStyle = '#f4ecff';
  g.fillText(STAMP, 0, 0);
  g.restore();
}

/** The beat itself, on its own transparent layer over the office still. */
function runBeat(host, doc, now, raf) {
  return new Promise((resolve) => {
    const el = host.el;
    const tl = endingTimeline(!!host.reduced);
    const layer = doc.createElement('canvas');
    layer.className = 'bo-story-ending';
    layer.setAttribute('aria-hidden', 'true');
    layer.style.cssText = 'position:absolute;inset:0;z-index:11;width:100%;height:100%;pointer-events:none;';
    const box = el.getBoundingClientRect ? el.getBoundingClientRect() : { width: 0, height: 0 };
    const w = Math.max(320, Math.round(box.width || el.clientWidth || 1280));
    const h = Math.max(200, Math.round(box.height || el.clientHeight || 720));
    layer.width = w; layer.height = h;
    el.append(layer);
    const g = layer.getContext ? layer.getContext('2d') : null;
    const lines = crackPath(CRACK_SEED, w, h);
    const scale = Math.max(0.8, Math.min(2.4, w / 900));
    const t0 = now();
    let done = false;
    const finish = () => { if (done) return; done = true; try { layer.remove(); } catch (e) { /* noop */ } resolve(); };
    const frame = () => {
      if (done) return;
      if (!g) { finish(); return; }
      const t = (now() - t0) / 1000;
      if (t >= tl.cardAt) { finish(); return; }
      g.clearRect(0, 0, w, h);
      const beat = beatAt(t, tl);
      const grown = tl.crackGrow > 0 ? Math.min(1, (t - tl.crackAt) / tl.crackGrow) : 1;
      // One frame of shake as the stamp lands, and never under reduced motion.
      const since = t - tl.stampAt;
      const shaking = tl.stampShake > 0 && beat === 'stamp' && since < tl.stampShake;
      g.save();
      if (shaking) {
        const k = 1 - since / tl.stampShake;
        g.translate(Math.round(7 * scale * k), Math.round(-4 * scale * k));
      }
      drawCrack(g, lines, beat === 'crack' ? grown : 1, scale);
      if (beat === 'stamp') drawStamp(g, w, h, Math.max(0, since), tl);
      g.restore();
      if (beat === 'black') {
        const a = tl.blackFade > 0 ? Math.min(1, (t - tl.blackAt) / tl.blackFade) : 1;
        g.globalAlpha = a; g.fillStyle = '#000'; g.fillRect(0, 0, w, h); g.globalAlpha = 1;
      }
      raf(frame);
    };
    raf(frame);
  });
}

/**
 * @param host { el, canvas, reduced, actions, officeEnding, paintCard }
 * @param opts { wall, house }  `house` = the run ended on the house finale and
 *              the office pan out is already playing behind this call.
 * @returns true when this function owns the screen from here; false to let
 *          station.js run its ordinary ending card path.
 */
export async function playStoryEnding(host, opts = {}) {
  if (!host || !host.el || !host.canvas) return false;
  // An authored last wall has no office still behind it: the ordinary card is right.
  if (!opts.house) return false;
  const doc = host.el.ownerDocument || (typeof document !== 'undefined' ? document : null);
  if (!doc || typeof doc.createElement !== 'function') return false;
  const sleep = (fn, ms) => (typeof setTimeout === 'function' ? setTimeout(fn, ms) : fn());
  const raf = (fn) => (typeof requestAnimationFrame === 'function' ? requestAnimationFrame(fn) : sleep(fn, 16));
  const now = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());
  try {
    hideActions(host.actions);
    await waitForOffice(host.officeEnding, host.actions, sleep);
    await runBeat(host, doc, now, raf);
  } catch (e) { /* a missed beat still owes the player the buttons */ }
  showActions(host.actions);
  return true;
}

export default playStoryEnding;
