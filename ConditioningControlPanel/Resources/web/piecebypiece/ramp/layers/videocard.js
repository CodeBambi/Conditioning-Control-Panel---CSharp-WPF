/* ============================================================================
 * layers/videocard.js - the wash that rides in front of the POV.
 *
 * Was a framed video card parked in the middle of the screen (the DtRH
 * payloadFx.videoCard in a red electric cage). Owner, 2026-09-29: a still frame
 * in the middle of the board made the board impossible to read. It is now a
 * FULLSCREEN picture that fades in, holds semi-transparent over everything,
 * and fades out: the board stays readable through it, it just moves.
 *
 * It rides the THINK (owner, 2026-10-01): it rises once the mover has sat on a
 * move for a while (RAMP_TUNING.think.cardAtMs) and drops away fast the moment
 * the move is made (clear({ fast: true }), the snap). On a long think the next
 * wash follows the last one. Hold time is scaled by the meter
 * (RAMP_TUNING.videoCard, 4s to 13s). Click-through like every other layer. The
 * kind is still called `videoCard` and the element still wears `pbp-card`, so
 * the schedule, the veil damping and the open/close sounds keep working.
 *
 * Picture: a gif from the pool (an online gif is a clip and plays as a muted
 * looping <video> child through clip.js, which unloads it on removal); a still
 * when there is no gif; the pink noise tile when there is nothing at all.
 * ==========================================================================*/

import { dressBox, undressBox } from './clip.js';

const FADE_IN_MS = 700;    // matches .pbp-card's opacity transition
const FADE_OUT_MS = 900;   // matches .pbp-card.is-out
const SNAP_OUT_MS = 220;   // matches .pbp-card.is-out.is-snap: the move was made

export function createVideoCard(ctx) {
  let card = null;      // the one live wash
  let disposed = false;
  let timers = [];

  const track = (id) => { timers.push(id); return id; };
  const untrack = () => { for (const id of timers) clearTimeout(id); timers = []; };

  function show(opts = {}) {
    if (disposed || card) return;                    // one at a time
    const wrap = ctx.el('div', 'pbp-card');
    if (!wrap) return;
    // every picture already up somewhere: skip this turn rather than wear a copy
    const url = dressBox(ctx, wrap, ctx.tile(), ctx.image);
    if (!url) { undressBox(wrap); return; }
    ctx.mountFront(wrap);
    ctx.hold(url);
    card = wrap;

    let removed = false;
    function remove(fast = false) {
      if (removed) return;
      removed = true;
      const outMs = fast ? SNAP_OUT_MS : FADE_OUT_MS;
      try { wrap.classList.remove('is-in'); wrap.classList.add('is-out'); if (fast) wrap.classList.add('is-snap'); } catch { /* gone */ }
      // the picture is on screen until the fade out is over; untracked, so a clear cannot drop it
      ctx.releaseLater(url, outMs + 60);
      // untracked too: a clear that lands mid-fade must not strand the wash on screen
      setTimeout(() => {
        undressBox(wrap);
        try { wrap.remove(); } catch { /* gone */ }
        if (card === wrap) card = null;
      }, outMs + 60);
    }

    // fade in on the next frame, hold, fade out
    try { requestAnimationFrame(() => { if (!disposed && !removed) wrap.classList.add('is-in'); }); } catch { wrap.classList.add('is-in'); }
    const holdMs = Math.max(1200, opts.holdMs || 6000);
    track(setTimeout(remove, FADE_IN_MS + holdMs));
    card._remove = remove;
    card._url = url;
  }

  /** Take the wash down. `fast` is the snap: the move was made, it leaves in a blink. */
  function clear(opts = {}) {
    untrack();
    if (card) {
      const c = card;
      try { if (c._remove) c._remove(!!opts.fast); else c.remove(); } catch { /* gone */ }
      if (card === c) card = null;
    }
  }

  function dispose() {
    disposed = true;
    untrack();
    if (card) { ctx.release(card._url); undressBox(card); try { card.remove(); } catch { /* gone */ } card = null; }
  }

  return { show, clear, dispose, get live() { return !!card; }, set() {}, fire() {}, grab() {}, move() {}, drop() {} };
}

export default createVideoCard;
