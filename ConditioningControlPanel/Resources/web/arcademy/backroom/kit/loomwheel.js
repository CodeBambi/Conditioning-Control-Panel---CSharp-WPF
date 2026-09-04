/* ============================================================================
 * backroom/kit/loomwheel.js - the wheel face.
 *
 * One canvas, two jobs. The FACE is wedges: flat colour, a label along each
 * spoke, a brass rim, drawn once into an offscreen buffer and thereafter only
 * rotated, because redrawing twenty-four labels every frame is how a wheel
 * turns a phone into a hand warmer. The HUB is a Loom spiral, which is the one
 * place on the wheel where the room's own art shows through.
 *
 * THE POINTER DOES NOT MOVE AND THE WHEEL DOES. That is not a style choice: a
 * wheel whose pointer swings is a wheel a player cannot read at a glance, and
 * a player who cannot read the wheel cannot tell whether the house is honest.
 *
 * WHO DECIDES WHERE IT STOPS: NOT THIS FILE. `spinTo(index)` is told the index
 * by the caller, which was told it by the server. There is no rng anywhere in
 * the landing, and the only random thing on the whole wheel is the hub's
 * recipe. A wheel that picked its own winner would be a wheel with a thumb on
 * it, and it would be this module's thumb.
 *
 * REDUCED MOTION GETS A CROSSFADE, NOT A SLOW SPIN. Nothing in engine/loom
 * gates itself on reduced motion, and the CSS freeze cannot reach a JS loop
 * (trap 92), so every loop here is gated in this file: the hub does not turn,
 * and spinTo lands the wheel on its answer behind a short dissolve instead of
 * travelling there.
 *
 * .ae-lite gets a STILL hub. The phone diet's whole argument is that a
 * decorative loop is the first thing to cut, and the hub is decoration.
 * ==========================================================================*/

import { drawSpiral } from '../../engine/loom/loomSpiral.js';
import { makeRng } from '../../core/rng.js';

const TAU = Math.PI * 2;
/** How many whole turns a spin makes before it starts hunting for its wedge. */
const TURNS = 4;

function raf(fn) {
  if (typeof requestAnimationFrame === 'function') return requestAnimationFrame(fn);
  return setTimeout(() => fn(Date.now()), 16);
}
function unraf(h) {
  if (typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(h); return; } catch { /* noop */ } }
  try { clearTimeout(h); } catch { /* noop */ }
}
const clock = () => ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now());

/** The settle. Quintic out: it arrives fast, then spends most of the last
 *  third slowing down, which is the part that reads as a wheel with weight. */
const settle = (t) => 1 - Math.pow(1 - t, 5);

/** A Loom recipe for the hub, seeded so a given wheel always wears the same
 *  spiral. Not the drop's spiral: the hub is furniture, the drop is an event. */
function hubRecipe(seed) {
  const rng = makeRng(String(seed || 'backroom-wheel'));
  const arms = 3 + Math.floor(rng() * 4);
  return {
    arms,
    turns: 1.2 + rng() * 1.6,
    style: ['log', 'arch', 'ribbon'][Math.floor(rng() * 3)],
    duty: 0.42 + rng() * 0.2,
    speed: 3,
    direction: rng() < 0.5 ? -1 : 1,
    colors: ['#ff69b4', '#b8a6e8'],
    bg: '#14142b',
  };
}

/**
 * createLoomWheel({ host, wedges, seed, reduced, lite, sfx, log }) ->
 *   { el, spinTo(index, ms) -> Promise<number>, setWedges(list), at(), destroy() }
 *
 * `wedges` is `[{ label, color, drop }]`. `spinTo` resolves with the index it
 * landed on, which is always the index it was given: the promise is a "the
 * ceremony is over" signal, never an outcome.
 */
export function createLoomWheel(opts) {
  const o = opts || {};
  const reduced = !!o.reduced;
  const lite = !!o.lite;
  const sfx = (typeof o.sfx === 'function') ? o.sfx : () => {};
  const note = (typeof o.log === 'function') ? o.log : () => {};

  const size = Math.max(180, Math.min(420, Math.round(Number(o.size) || 320)));
  const wrap = document.createElement('div');
  wrap.className = 'bk-wheel';
  const cv = document.createElement('canvas');
  cv.width = size;
  cv.height = size;
  cv.className = 'bk-wheel-face';
  cv.setAttribute('role', 'img');
  wrap.appendChild(cv);
  const pin = document.createElement('i');
  pin.className = 'bk-wheel-pin';
  pin.setAttribute('aria-hidden', 'true');
  wrap.appendChild(pin);

  let ctx2d = null;
  try { ctx2d = cv.getContext('2d'); } catch (e) { ctx2d = null; }

  let wedges = [];
  let face = null;        // the drawn face, rotated but never redrawn
  let rot = 0;            // current rotation, radians
  let landed = 0;
  let hubHandle = null;
  let spinHandle = null;
  let dead = false;
  const recipe = hubRecipe(o.seed);

  /** The face, drawn ONCE into a buffer. Every frame after this is a rotate. */
  function buildFace() {
    if (!ctx2d || !wedges.length) return;
    let buf = null;
    try {
      buf = document.createElement('canvas');
      buf.width = size;
      buf.height = size;
    } catch (e) { face = null; return; }
    const b = buf.getContext('2d');
    if (!b) { face = null; return; }
    const c = size / 2;
    const r = c - 6;
    const span = TAU / wedges.length;
    for (let i = 0; i < wedges.length; i++) {
      const w = wedges[i] || {};
      // -PI/2 puts wedge zero under the pin at twelve o'clock, which is where
      // a player looks first and therefore where zero has to be.
      const a0 = (i * span) - Math.PI / 2 - span / 2;
      b.beginPath();
      b.moveTo(c, c);
      b.arc(c, c, r, a0, a0 + span);
      b.closePath();
      b.fillStyle = w.color || (i % 2 ? '#2e2e55' : '#252542');
      b.fill();
      b.strokeStyle = 'rgba(240,194,75,.35)';
      b.lineWidth = 1;
      b.stroke();
      const label = String(w.label == null ? '' : w.label);
      if (!label) continue;
      b.save();
      b.translate(c, c);
      b.rotate(a0 + span / 2);
      b.fillStyle = w.ink || '#F2EBDD';
      b.font = '600 ' + Math.max(9, Math.round(size / 26)) + 'px ' + (w.font || 'system-ui, sans-serif');
      b.textAlign = 'right';
      b.textBaseline = 'middle';
      // Labels ride the spoke outward. A wheel with horizontal labels needs
      // twice the face to fit half the words.
      b.fillText(label.slice(0, 14), r - 10, 0);
      b.restore();
    }
    b.beginPath();
    b.arc(c, c, r, 0, TAU);
    b.strokeStyle = '#F0C24B';
    b.lineWidth = 3;
    b.stroke();
    face = buf;
  }

  /** One composited frame: the face at `rot`, the hub on top and upright. */
  function paint(hubPhase) {
    if (!ctx2d) return;
    const c = size / 2;
    ctx2d.clearRect(0, 0, size, size);
    if (face) {
      ctx2d.save();
      ctx2d.translate(c, c);
      ctx2d.rotate(rot);
      ctx2d.drawImage(face, -c, -c);
      ctx2d.restore();
    }
    const hubR = Math.round(size * 0.17);
    ctx2d.save();
    ctx2d.beginPath();
    ctx2d.arc(c, c, hubR, 0, TAU);
    ctx2d.clip();
    ctx2d.translate(c - hubR, c - hubR);
    try { drawSpiral(ctx2d, hubR * 2, recipe, hubPhase || 0); }
    catch (e) { note('backroom: wheel hub would not draw'); }
    ctx2d.restore();
    ctx2d.beginPath();
    ctx2d.arc(c, c, hubR, 0, TAU);
    ctx2d.strokeStyle = '#F0C24B';
    ctx2d.lineWidth = 2;
    ctx2d.stroke();
  }

  /** The hub's own slow turn, when the room can afford one. */
  function startHub() {
    if (hubHandle != null || reduced || lite) { paint(0); return; }
    const t0 = clock();
    const loop = () => {
      if (dead) return;
      paint(((clock() - t0) / 2400) % 1);
      hubHandle = raf(loop);
    };
    hubHandle = raf(loop);
  }

  function setWedges(list) {
    wedges = Array.isArray(list) ? list.slice(0, 48) : [];
    try { cv.setAttribute('aria-label', wedges.map((w) => w && w.label).filter(Boolean).join(', ')); }
    catch (e) { /* noop */ }
    buildFace();
    paint(0);
  }

  /**
   * spinTo(index, ms) -> Promise<index>
   * The index came from the server. This only travels there.
   */
  function spinTo(index, ms) {
    const n = wedges.length;
    if (dead || !n || !ctx2d) return Promise.resolve(landed);
    const i = ((Math.round(Number(index) || 0) % n) + n) % n;
    const span = TAU / n;
    // Where the wheel must end up for wedge i to sit under the pin, carried
    // forward from where it is so a second spin never rewinds.
    const target = rot + (TURNS * TAU) + (((-i * span) - (rot % TAU)) % TAU + TAU) % TAU;

    if (reduced) {
      /* THE CROSSFADE. The answer is the information and it is not withheld:
       * the wheel simply arrives at it behind a short dissolve rather than
       * travelling there for three seconds. */
      landed = i;
      rot = target % TAU;
      try { wrap.classList.add('bk-wheel-fade'); } catch (e) { /* noop */ }
      paint(0);
      sfx('chime', 0.3);
      return new Promise((res) => setTimeout(() => {
        try { wrap.classList.remove('bk-wheel-fade'); } catch (e) { /* noop */ }
        res(i);
      }, 240));
    }

    const dur = Math.max(900, Math.min(6000, Math.round(Number(ms) || 3200)));
    const from = rot;
    const t0 = clock();
    if (spinHandle != null) { unraf(spinHandle); spinHandle = null; }
    sfx('whoosh', 0.34);
    return new Promise((res) => {
      let over = false;
      const finish = () => {
        if (over) return;
        over = true;
        if (spinHandle != null) { unraf(spinHandle); spinHandle = null; }
        rot = target % TAU;
        landed = i;
        paint(0);
        sfx('chime', 0.34);
        res(i);
      };
      // The ceiling, armed before the first frame: a stalled clock or a lost
      // compositor must never leave a wheel spinning with a stake on it.
      const ceiling = setTimeout(finish, dur + 400);
      const step = () => {
        if (dead || over) return;
        const t = Math.max(0, Math.min(1, (clock() - t0) / dur));
        rot = from + (target - from) * settle(t);
        paint(0);
        if (t >= 1) { clearTimeout(ceiling); finish(); return; }
        spinHandle = raf(step);
      };
      spinHandle = raf(step);
    });
  }

  setWedges(o.wedges);
  startHub();
  if (o.host) { try { o.host.appendChild(wrap); } catch (e) { /* noop */ } }

  return {
    el: wrap,
    spinTo,
    setWedges,
    at: () => landed,
    destroy() {
      dead = true;
      if (hubHandle != null) { unraf(hubHandle); hubHandle = null; }
      if (spinHandle != null) { unraf(spinHandle); spinHandle = null; }
      try { wrap.remove ? wrap.remove() : wrap.parentNode && wrap.parentNode.removeChild(wrap); }
      catch (e) { /* noop */ }
    },
  };
}

export default createLoomWheel;
