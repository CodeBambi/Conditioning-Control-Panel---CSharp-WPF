/* ============================================================================
 * layers/videocard.js - the tape that rides in front of the POV.
 *
 * The DtRH video card (payloadFx.videoCard), re-choreographed for a board game:
 * the tape RUSHES UP AT THE POV from the top of the screen, HOLDS there
 * semi-transparent over the board, then SLIDES OUT THE BOTTOM. One card at a
 * time, on the FRONT plane, in a vibrating electric cage.
 *
 * It fires on every `turn`, for the side that just moved and is now WAITING, so
 * the tape covers exactly the stretch where the player has nothing to do but
 * look. Hold time is scaled by that side's meter (RAMP_TUNING.videoCard, 4s to
 * 13s). Semi-transparent, so the board is legible THROUGH it - and the card is
 * click-through like every other layer, so the player can still reach a piece
 * underneath. Having to play through it is the point; being unable to is not.
 *
 * Mechanics carried over from DtRH because they were paid for in bug reports:
 *   - a plain <video> with NO crossOrigin, so a remote pool url still plays;
 *   - born muted, since a card that talks over the game is a different feature;
 *   - a random start offset, so the same clip is never the same card;
 *   - an explicit unload on removal (removeAttribute('src') + load()), because
 *     Chromium counts a merely removed element's decoder against the per-page
 *     media cap until GC.
 * ==========================================================================*/

export function createVideoCard(ctx) {
  const t = (ctx.tuning && ctx.tuning.videoCard) || { riseMs: 620, startJitter: 0.7 };
  let card = null;      // the one live card
  let disposed = false;
  let timers = [];

  const track = (id) => { timers.push(id); return id; };
  const untrack = () => { for (const id of timers) clearTimeout(id); timers = []; };

  function show(opts = {}) {
    if (disposed || card) return;                    // one card at a time
    const url = ctx.video();
    if (!url) return;                                // no clips in the pool: skip
    const wrap = ctx.el('div', 'pbp-card');
    const frame = ctx.el('div', 'pbp-card-frame');
    const vid = ctx.el('video', null);
    if (!wrap || !frame || !vid) return;

    vid.loop = true; vid.playsInline = true; vid.autoplay = true; vid.muted = true;
    vid.preload = 'auto';
    // a random start offset so the same clip never opens on the same frame
    vid.addEventListener('loadedmetadata', () => {
      try {
        const d = vid.duration;
        if (Number.isFinite(d) && d > 1) vid.currentTime = ctx.rand(0, d * (t.startJitter || 0.7));
      } catch { /* seeking is best effort */ }
    }, { once: true });
    vid.src = url;
    frame.appendChild(vid);
    wrap.appendChild(frame);
    ctx.mountFront(wrap);
    card = wrap;

    const play = () => { try { const p = vid.play(); if (p && p.catch) p.catch(() => {}); } catch { /* blocked */ } };
    play();

    let removed = false;
    function remove() {
      if (removed) return;
      removed = true;
      try { wrap.classList.remove('is-in'); wrap.classList.add('is-out'); } catch { /* gone */ }
      try { vid.pause(); } catch { /* gone */ }
      // explicit unload: a removed element's decoder counts against Chromium's
      // per-page media cap until GC, and this card fires every single turn
      track(setTimeout(() => {
        try { vid.removeAttribute('src'); vid.load(); } catch { /* gone */ }
        try { wrap.remove(); } catch { /* gone */ }
        if (card === wrap) card = null;
      }, 760));
    }

    // rush at the face on the next frame, hold, then slide out the bottom
    try { requestAnimationFrame(() => { if (!disposed && !removed) wrap.classList.add('is-in'); }); } catch { wrap.classList.add('is-in'); }
    const holdMs = Math.max(1200, opts.holdMs || 6000);
    track(setTimeout(remove, holdMs));
    card = wrap;
    card._remove = remove;
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
    if (card) { try { card.remove(); } catch { /* gone */ } card = null; }
  }

  return { show, clear, dispose, get live() { return !!card; }, set() {}, fire() {}, grab() {}, move() {}, drop() {} };
}

export default createVideoCard;
