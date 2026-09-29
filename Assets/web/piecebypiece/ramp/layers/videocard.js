/* ============================================================================
 * layers/videocard.js - the wash that rides in front of the POV.
 *
 * Was a framed video card parked in the middle of the screen (the DtRH
 * payloadFx.videoCard in a red electric cage). Owner, 2026-09-29: a still frame
 * in the middle of the board made the board impossible to read. It is now a
 * FULLSCREEN picture that fades in, holds semi-transparent over everything,
 * and fades out: the board stays readable through it, it just moves.
 *
 * It fires on every `turn`, for the side that just moved and is now WAITING, so
 * the wash covers exactly the stretch where the player has nothing to do but
 * look. Hold time is scaled by that side's meter (RAMP_TUNING.videoCard, 4s to
 * 13s). Click-through like every other layer. The kind is still called
 * `videoCard` and the element still wears `pbp-card`, so the schedule, the veil
 * damping and the open/close sounds keep working untouched.
 *
 * Picture: a gif from the pool (an online gif is a clip and plays as a muted
 * looping <video> child through clip.js, which unloads it on removal); a still
 * when there is no gif; the pink noise tile when there is nothing at all.
 * ==========================================================================*/

import { dressBox, undressBox } from './clip.js';

const FADE_IN_MS = 700;    // matches .pbp-card's opacity transition
const FADE_OUT_MS = 900;   // matches .pbp-card.is-out

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
    function remove() {
      if (removed) return;
      removed = true;
      try { wrap.classList.remove('is-in'); wrap.classList.add('is-out'); } catch { /* gone */ }
      // the picture is on screen until the fade out is over; untracked, so a clear cannot drop it
      ctx.releaseLater(url, FADE_OUT_MS + 60);
      // untracked too: a clear that lands mid-fade must not strand the wash on screen
      setTimeout(() => {
        undressBox(wrap);
        try { wrap.remove(); } catch { /* gone */ }
        if (card === wrap) card = null;
      }, FADE_OUT_MS + 60);
    }

    // fade in on the next frame, hold, fade out
    try { requestAnimationFrame(() => { if (!disposed && !removed) wrap.classList.add('is-in'); }); } catch { wrap.classList.add('is-in'); }
    const holdMs = Math.max(1200, opts.holdMs || 6000);
    track(setTimeout(remove, FADE_IN_MS + holdMs));
    card._remove = remove;
    card._url = url;
  }

  function clear() {
    untrack();
    if (card) {
      const c = card;
      try { if (c._remove) c._remove(); else c.remove(); } catch { /* gone */ }
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
