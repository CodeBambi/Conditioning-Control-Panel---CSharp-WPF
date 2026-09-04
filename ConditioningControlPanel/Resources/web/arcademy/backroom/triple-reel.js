/* ============================================================================
 * backroom/triple-reel.js - the three reels, and nothing else.
 *
 * The window of TRIPLE TRIGGER: three columns of words that turn, and stop
 * where they are told to stop. It knows nothing about chips, stakes, holds as
 * a PRICE, odds or the dealer. Hand it a list of trigger rows and it paints
 * three reels; hand it three indices and it lands on them. That is the whole
 * surface, and keeping it that small is what makes the cabinet on top of it
 * readable and this half testable on a bench with no cashier anywhere near it.
 *
 * FOUR THINGS THIS FILE IS BUILT AROUND.
 *
 *  - THE REELS ARE THEATRE (LEDGER TRUTH). The server drew the outcome before
 *    the first frame turned. Every road through `settle()` ends on the indices
 *    it was given, and no road anywhere in here picks one.
 *
 *  - IT ALWAYS COMES TO REST. The ceiling is armed BEFORE the first frame, the
 *    way kit/dropbeat.js arms its own: a frame clock that stops, a tab that
 *    sleeps, an answer that never lands, and the reels still park and the
 *    cabinet still gets told. A slot machine that spins forever has eaten
 *    somebody's stake and is lying about it.
 *
 *  - REDUCED MOTION IS A DIFFERENT MACHINE, NOT A SLOWER ONE. Still mode never
 *    starts the loop at all: the reels dim, and then they CROSSFADE onto the
 *    answer. The global freeze at the bottom of styles.css cannot reach a rAF
 *    (trap 92), so the gate is here in the JS and the crossfade is an opacity
 *    class this module times rather than a transition the freeze would flatten.
 *
 *  - NO PIXEL IS EVER MEASURED. A reel is parked by writing
 *    `translateY(calc(var(--bk-tt-cell) * -n))`, so the sheet owns the cell
 *    height, the phone diet can shorten it, and nothing here has to know or
 *    care. Measuring would also mean measuring during a roll, which is the
 *    classic way to make a phone drop frames.
 * ==========================================================================*/

import { REEL_GLYPH } from './kit/triggers.js';

/** Three reels. It is in the name. */
export const REELS = 3;
/** Copies of the strip stacked in each column, so there is always one above
 *  and one below the window and the wrap is never seen. */
const COPIES = 3;
/** Cells a reel travels per second while it turns. Each one is a hair quicker
 *  than the last, so three reels never read as one wide reel. */
const SPEED = Object.freeze([15.5, 17, 18.5]);
/** The roll is never shorter than this, however fast the server answers. A
 *  slot that resolves instantly reads as a button, not as a machine. */
export const MIN_ROLL_MS = 620;
/** Reel to reel, on the way down. Long enough to count, short enough to want. */
const SETTLE_GAP_MS = 300;
/** The ceiling. Nothing in this file may turn for longer, on any path. */
export const ROLL_CEILING_MS = 9500;

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

function raf(fn) {
  if (typeof requestAnimationFrame === 'function') return requestAnimationFrame(fn);
  return setTimeout(() => fn(Date.now()), 16);
}
function unraf(h) {
  if (typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(h); return; } catch { /* noop */ } }
  try { clearTimeout(h); } catch { /* noop */ }
}
const clock = () => ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now());

/**
 * createReelWindow({ reduced, lite, sfx, log, labels, onCeiling }) ->
 *   { el, setRows, rows, at, wordAt, sentence, rolling,
 *     startRoll, settle, park, stop, destroy }
 *
 * `labels` is `{ window, reel(i) }` and it is passed IN rather than looked up
 * here on purpose: the strings of this machine live in the cabinet's own table
 * (they are `bk_tt_*` rows the host mirrors), and a component with its own
 * private lexicon would be a second place for a translator to find.
 *
 * `onCeiling()` fires when the ceiling parks the reels without an answer. The
 * cabinet uses it to hand the buttons back, which is the only thing that can
 * still be done for a player at that point.
 */
export function createReelWindow(opts) {
  const o = opts || {};
  const reduced = !!o.reduced;
  const sfx = (typeof o.sfx === 'function') ? o.sfx : () => {};
  const note = (typeof o.log === 'function') ? o.log : () => {};
  const labels = o.labels || {};
  const onCeiling = (typeof o.onCeiling === 'function') ? o.onCeiling : () => {};

  let dead = false;
  let rows = [];                       // the reel list, from kit/triggers.js
  let at = [0, 0, 0];                  // where each reel is parked, an index into `rows`
  let rollingFlags = [false, false, false];
  let loop = null;
  let ceiling = null;
  let rollT0 = 0;
  const timers = new Set();

  const win = el('div', 'bk-tt-window');
  win.setAttribute('role', 'group');
  win.setAttribute('aria-label', String(labels.window || 'the three reels'));

  const cols = [];
  for (let i = 0; i < REELS; i++) {
    const reel = el('div', 'bk-tt-reel');
    reel.setAttribute('role', 'img');
    reel.setAttribute('aria-label', String((typeof labels.reel === 'function' ? labels.reel(i + 1) : '') || ('reel ' + (i + 1))));
    const strip = el('div', 'bk-tt-strip');
    reel.appendChild(strip);
    win.appendChild(reel);
    cols.push({ reel, strip, off: 0 });
  }

  function later(fn, ms) {
    const h = setTimeout(() => { timers.delete(h); if (!dead) fn(); }, ms);
    timers.add(h);
    return h;
  }

  /* ----------------------------------------------------------------- paint */

  /** The word reel `r` shows for row `i`. The server's blank is `-1` and a
   *  padded row carries its own glyph flag, so both roads reach the same cell. */
  function wordAt(r, i) {
    if (i < 0 || i >= rows.length) return null;
    const row = rows[i];
    if (!row || row.glyph) return null;
    const parts = String(row.word || '').split(' ');
    return parts[r] || null;
  }

  /** Every cell of one strip, COPIES times over. The extra row on the end is
   *  the server's `-1`: a blank needs somewhere to stop, and a blank is the
   *  house's spiral rather than an empty box. */
  function buildStrip(r) {
    const strip = cols[r].strip;
    strip.textContent = '';
    const n = rows.length + 1;
    for (let copy = 0; copy < COPIES; copy++) {
      for (let i = 0; i < n; i++) {
        const w = (i === rows.length) ? null : wordAt(r, i);
        strip.appendChild(el('div', 'bk-tt-cell' + (w ? '' : ' bk-tt-glyph'), w || REEL_GLYPH));
      }
    }
  }

  /** The offset that puts row `i` on the payline, inside the middle copy. The
   *  window is three cells tall and the payline is the middle one, hence the
   *  minus one: `off` counts the cells that have gone past the TOP. */
  function posFor(i) {
    const n = rows.length + 1;
    const idx = (i < 0 || i >= rows.length) ? rows.length : i;
    return n + idx - 1;
  }

  function place(col, off) {
    col.off = off;
    try { col.strip.style.transform = 'translateY(calc(var(--bk-tt-cell) * ' + (-off).toFixed(3) + '))'; }
    catch (e) { /* a strip that will not move still reports its parked index */ }
  }

  /** New words on the reels. The opening positions are deliberately spread, so
   *  a cabinet that has never been pulled does not open on three of a kind and
   *  read as a machine that has already paid. */
  function setRows(list) {
    rows = Array.isArray(list) ? list : [];
    if (!rows.length) return;
    at = [0, 1 % rows.length, 2 % rows.length];
    for (let i = 0; i < REELS; i++) { buildStrip(i); place(cols[i], posFor(at[i])); }
  }

  /* ------------------------------------------------------------------ roll */

  /** `held` is three booleans. A held reel does not turn: that is exactly what
   *  the player paid five chips for, and it has to be visible. */
  function startRoll(held) {
    if (dead || !rows.length) return;
    const keep = Array.isArray(held) ? held : [];
    rollT0 = clock();
    for (let i = 0; i < REELS; i++) {
      rollingFlags[i] = !keep[i];
      cols[i].reel.classList.remove('bk-tt-land');
      if (rollingFlags[i]) cols[i].reel.classList.add(reduced ? 'bk-tt-fade' : 'bk-tt-rolling');
    }
    // ARMED BEFORE THE FIRST FRAME, always, on both roads.
    ceiling = later(() => { stop(); park(); onCeiling(); }, ROLL_CEILING_MS);
    if (reduced) return;

    let last = clock();
    const step = () => {
      if (dead) return;
      const now = clock();
      const dt = Math.min(0.05, Math.max(0, (now - last) / 1000));
      last = now;
      const n = rows.length + 1;
      for (let i = 0; i < REELS; i++) {
        if (!rollingFlags[i]) continue;
        let off = cols[i].off + (SPEED[i] * dt);
        while (off >= n * 2) off -= n;
        place(cols[i], off);
      }
      loop = raf(step);
    };
    loop = raf(step);
  }

  function stop() {
    if (loop != null) { unraf(loop); loop = null; }
    if (ceiling != null) {
      try { clearTimeout(ceiling); } catch (e) { /* noop */ }
      timers.delete(ceiling);
      ceiling = null;
    }
    for (let i = 0; i < REELS; i++) {
      rollingFlags[i] = false;
      cols[i].reel.classList.remove('bk-tt-rolling', 'bk-tt-fade');
    }
  }

  /** Every reel back where it was. The road out of a refusal and out of the
   *  ceiling: a machine that could not take a pull must at least look like it
   *  never took one. */
  function park() {
    for (let i = 0; i < REELS; i++) place(cols[i], posFor(at[i]));
  }

  function land(i, idx) {
    const moved = rollingFlags[i];
    at[i] = (idx < 0 || idx >= rows.length) ? -1 : idx;
    rollingFlags[i] = false;
    const col = cols[i];
    col.reel.classList.remove('bk-tt-rolling', 'bk-tt-fade');
    place(col, posFor(idx));
    // The parked index, on the element, because a headless probe reading a
    // dumped DOM cannot see a transform but can always read an attribute.
    try { col.reel.dataset.at = String(at[i]); } catch (e) { /* noop */ }
    // Three thuds, each a semitone up, so the ear counts the reels down. A reel
    // that never turned makes no sound: you paid for it to stay still.
    if (!moved) return;
    sfx('pip', 0.34, { pitch: 1 + (i * 0.12) });
    if (reduced) return;
    col.reel.classList.add('bk-tt-land');
    later(() => col.reel.classList.remove('bk-tt-land'), 220);
  }

  /**
   * settle([i,i,i]) -> Promise, resolved when the third reel is down.
   *
   * Left to right with a beat between, and never before the roll has had its
   * minimum, however quickly the server answered. It resolves even when the
   * window is torn down underneath it, so the cabinet's `.then` is never the
   * thing left hanging.
   */
  function settle(list) {
    const want = Array.isArray(list) ? list : [];
    const wait = Math.max(0, MIN_ROLL_MS - (clock() - rollT0));
    return new Promise((done) => {
      let i = 0;
      const next = () => {
        if (dead) { done(); return; }
        if (i >= REELS) { stop(); done(); return; }
        const which = i;
        i += 1;
        land(which, Math.round(Number(want[which])));
        later(next, SETTLE_GAP_MS);
      };
      later(wait > 0 ? next : next, wait);
    });
  }

  return {
    el: win,
    setRows,
    rows: () => rows,
    at: () => at.slice(),
    wordAt,
    /** The three words on the payline right now, in reel order. On a scramble
     *  this is a sentence nobody wrote, which is the point of the machine. */
    sentence() {
      const out = [];
      for (let i = 0; i < REELS; i++) out.push(wordAt(i, at[i]) || REEL_GLYPH);
      return out.join(' ').toUpperCase();
    },
    /** The whole trigger row a reel is sitting on, for reading a royal out. */
    phraseAt(i) {
      const row = rows[at[i]];
      return (row && !row.glyph) ? String(row.word || '') : '';
    },
    rolling: () => rollingFlags.some(Boolean),
    startRoll,
    settle,
    park,
    stop,
    /** Safe from any road, and safe twice: mid roll, mid settle, mid nothing. */
    destroy() {
      if (dead) return;
      dead = true;
      stop();
      for (const h of timers) { try { clearTimeout(h); } catch (e) { /* noop */ } }
      timers.clear();
      try { win.remove(); } catch (e) { note('backroom: reel window would not lift'); }
    },
  };
}

export default createReelWindow;
