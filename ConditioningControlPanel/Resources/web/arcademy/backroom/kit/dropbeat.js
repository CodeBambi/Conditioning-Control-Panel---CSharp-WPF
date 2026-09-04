/* ============================================================================
 * backroom/kit/dropbeat.js - THE DROP.
 *
 * One wedge on the Spiral is not a payout. The room dims, a spiral turns over
 * the felt for a moment, the dealer says one thing, and then the lights come
 * back up and you are still standing at the cabinet. That is the whole beat.
 *
 * FOUR HARD LIMITS, and every one of them is a promise to the player rather
 * than a taste.
 *  - IT NEVER LASTS LONGER THAN 1.6 SECONDS. There is a ceiling in code, not a
 *    convention in a comment: `run()` always ends, and it ends on a timer that
 *    was armed before the first frame was drawn.
 *  - IT NEVER BLOCKS THE EXIT (Law VI). The veil is pointer-events:none and
 *    takes no focus, so the way out stays clickable underneath it the whole
 *    time. Nothing here is a modal, and nothing here waits for a click.
 *  - IT NEVER ARRIVES WHILE ONE IS RUNNING. A second drop over the first is
 *    two celebrations talking over each other, so a call during a drop is
 *    dropped, not queued.
 *  - REDUCED MOTION GETS THE SAME EVENT WITHOUT THE SPIN. One still frame, one
 *    line, out. The Loom modules carry no reduced-motion handling of their own
 *    (nothing in engine/loom does), so the rAF loop is gated HERE.
 *
 * The dim is deliberately gentle. This room already asks for a lot of a
 * player's attention and the drop should read as a hand on the shoulder, not
 * as the screen being taken away.
 * ==========================================================================*/

import { drawSpiral } from '../../engine/loom/loomSpiral.js';

/** The ceiling. Nothing in this file may take longer, on any path. */
export const DROP_MAX_MS = 1600;
/** The floor, so a drop is felt at all rather than flickering past. */
const DROP_MIN_MS = 700;

function raf(fn) {
  if (typeof requestAnimationFrame === 'function') return requestAnimationFrame(fn);
  return setTimeout(() => fn(Date.now()), 16);
}
function unraf(h) {
  if (typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(h); return; } catch { /* noop */ } }
  try { clearTimeout(h); } catch { /* noop */ }
}
const clock = () => ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now());

/** The spiral the drop wears. Fixed rather than seeded: the drop should be the
 *  same shape every time so that seeing it is recognising it. */
const DROP_LOOM = Object.freeze({
  arms: 5, turns: 2.4, style: 'log', duty: 0.55, speed: 4, direction: 1,
  colors: ['#ff69b4', '#b8a6e8'], bg: '#14142b',
});

/**
 * createDropBeat({ host, voice, sfx, reduced, lite, log }) ->
 *   { run(opts) -> Promise<void>, running(), destroy() }
 *
 * `host` is the element the veil is laid over, normally the machine's stage.
 * `run()` resolves when the beat is over, always, including when it refused to
 * play at all, so a caller can always chain the next thing onto it.
 */
export function createDropBeat(opts) {
  const o = opts || {};
  const host = o.host || null;
  const sfx = (typeof o.sfx === 'function') ? o.sfx : () => {};
  const note = (typeof o.log === 'function') ? o.log : () => {};
  const reduced = !!o.reduced;
  const lite = !!o.lite;

  let dead = false;
  let live = false;

  function makeVeil() {
    const veil = document.createElement('div');
    veil.className = 'bk-drop';
    veil.setAttribute('aria-hidden', 'true');   // the LINE is the announcement, not the veil
    const cv = document.createElement('canvas');
    cv.className = 'bk-drop-eye';
    // A small canvas scaled up by CSS. The spiral is a blur behind a dim, so
    // pixels spent on it are pixels wasted, and a phone notices.
    const px = lite ? 96 : 168;
    cv.width = px;
    cv.height = px;
    veil.appendChild(cv);
    const line = document.createElement('div');
    line.className = 'bk-drop-line';
    line.setAttribute('role', 'status');
    veil.appendChild(line);
    return { veil, cv, line };
  }

  /**
   * run({ ms, text }) -> Promise<void>
   * `text` overrides the dealer's line, for a machine with something specific
   * to say. Otherwise she picks one from her drop bucket.
   */
  function run(runOpts) {
    const r = runOpts || {};
    // A drop over a drop is two ceremonies talking over each other.
    if (dead || live || !host || typeof document === 'undefined') return Promise.resolve();
    live = true;

    const ms = Math.max(DROP_MIN_MS, Math.min(DROP_MAX_MS, Math.round(Number(r.ms) || 1200)));
    let text = r.text;
    if (text == null) { try { text = o.voice ? o.voice.line('drop') : ''; } catch (e) { text = ''; } }

    const { veil, cv, line } = makeVeil();
    if (text) line.textContent = String(text);
    let ctx2d = null;
    try { ctx2d = cv.getContext('2d'); } catch (e) { ctx2d = null; }
    try { host.appendChild(veil); } catch (e) { live = false; return Promise.resolve(); }
    sfx('spiral_hum', 0.3);

    return new Promise((resolve) => {
      let handle = null;
      let over = false;
      const end = () => {
        if (over) return;
        over = true;
        live = false;
        if (handle != null) { unraf(handle); handle = null; }
        try { veil.remove ? veil.remove() : veil.parentNode && veil.parentNode.removeChild(veil); }
        catch (e) { note('backroom: drop veil would not lift'); }
        sfx('lift', 0.22);
        resolve();
      };

      // THE CEILING IS ARMED BEFORE THE FIRST FRAME. Whatever happens to the
      // frame clock, to the canvas, to the tab, the room comes back up.
      const ceiling = setTimeout(end, ms);

      if (!ctx2d) { /* no canvas: the dim and the line are still the beat */ }
      else if (reduced) {
        // ONE STILL FRAME. Phase 0, drawn once, never animated. The player is
        // told the same thing; they are simply not spun while being told it.
        try { drawSpiral(ctx2d, cv.width, DROP_LOOM, 0); } catch (e) { note('backroom: still spiral failed'); }
      } else {
        const t0 = clock();
        let frames = 0;
        const step = () => {
          if (over) return;
          frames += 1;
          const elapsed = Math.max(clock() - t0, frames * 16);
          if (elapsed >= ms) { clearTimeout(ceiling); end(); return; }
          // phase 0..1 twice over the beat: two turns reads as motion, more
          // reads as a machine, fewer reads as a stutter.
          try { drawSpiral(ctx2d, cv.width, DROP_LOOM, ((elapsed / ms) * 2) % 1); }
          catch (e) { clearTimeout(ceiling); end(); return; }
          handle = raf(step);
        };
        handle = raf(step);
      }
    });
  }

  return {
    run,
    running: () => live,
    destroy() { dead = true; },
  };
}

export default createDropBeat;
