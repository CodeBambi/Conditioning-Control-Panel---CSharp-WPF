/* ============================================================================
 * mediaLane.js - the media lane governor for The Caucus Race.
 *
 * game/payloadFx.js scatters its cards at random over the whole viewport:
 * `.sf-pfx-flash` lands at (14..86vw, 16..80vh), `.sf-pfx-cascade` rains from
 * the top through the full height, `.sf-pfx-sub` blips dead centre. In the
 * tube that is the point. On the race road it is not: four of them at once sit
 * on the road, the cup and the cornering line, and the player is steering.
 *
 * So we do not change payloadFx. We watch `.sf-hud` and RE-HOME every card
 * that lands, into one of three lanes:
 *
 *      ceiling strip (top 2..24vh, centre x)
 *   +---------------------------------------+
 *   | [score]        [ceiling]              |
 *   | [left ]                       [right] |   <- the two rails
 *   | [rail ]      THE ROAD BAND    [rail  ] |      (2..21vw / 79..99vw)
 *   | [item ]     30..70vw below 26vh [gauge]|
 *   +---------------------------------------+
 *
 * The lanes alternate left / right / ceiling so a dense wave fans out instead
 * of piling on one spot, and the card is clamped to the lane's box with its
 * aspect kept. Nothing is ever placed over the road band.
 *
 * What is NOT re-homed: the full-screen overlays (spiral, pink, braindrain,
 * the pinned spiral, BAMBI FREEZE), the centred subliminal WORDS, the drifting
 * bounce text and the mandatory video card. Those are meant to fill the frame;
 * race.css caps their opacity instead.
 *
 * The left rail also yields while the room ribbon is up: on a short window the
 * ribbon reaches into the rail's top slot, and the ribbon is the one thing that
 * has to be readable for its 2.6 s.
 *
 * The geometry lives in race.css (`.rh-laned`) so it wins over payloadFx's
 * inline left/top with `!important`; this module only picks the lane and sets
 * the custom properties. Placement runs on insertion AND again on the next
 * frame, because payloadFx sometimes writes style after the append.
 * ==========================================================================*/

/** Cards we take: the payloadFx transients that scatter. */
const ALLOW = ['sf-pfx-flash', 'sf-pfx-cascade', 'sf-pfx-sub'];

/** Layers we never touch: full-screen washes, centred words, the tape. */
const DENY = [
  'sf-pfx', 'sf-pfx-front', 'sf-pfx-layer', 'sf-pfx-spiral', 'sf-pfx-pink',
  'sf-pfx-drain', 'sf-pfx-pinned', 'sf-pfx-freeze', 'sf-pfx-word',
  'sf-pfx-bounce', 'sf-pfx-videocard', 'sf-pfx-vidframe', 'rh-laned',
];

/** The three lanes, in the order they are handed out. x/y are the card CENTRE
 * (the payloadFx cards carry a translate(-50%, -50%)); w/h are the clamp box. */
const LANES = [
  // the rails sit below the score plate / the room ribbon and above the item
  // slot and the speed gauge; two slots each so a pair staggers instead of
  // landing on the same spot
  { name: 'left', xs: [11], ys: [42, 60], w: 20, h: 26 },
  { name: 'right', xs: [89], ys: [42, 60], w: 20, h: 26 },
  // the ceiling starts right of the score plate, so it clears it at 900px wide
  { name: 'ceiling', xs: [38, 50, 62], ys: [13], w: 22, h: 20 },
];

const DEFAULT_DUR = 2600;
const hasClass = (node, list) => list.some((c) => node.classList.contains(c));

/** Read the life payloadFx gave the card so the lane animation ends with it. */
function lifeMs(node) {
  let v = 0;
  try {
    const s = node.style;
    const dur = s.getPropertyValue('--pfx-dur').trim();
    const fall = s.getPropertyValue('--pfx-fall').trim();
    if (dur) v = dur.endsWith('ms') ? parseFloat(dur) : parseFloat(dur) * 1000;
    else if (fall) v = fall.endsWith('ms') ? parseFloat(fall) : parseFloat(fall) * 1000;
  } catch (e) { v = 0; }
  if (!isFinite(v) || v <= 0) v = DEFAULT_DUR;
  return Math.min(6000, Math.max(600, Math.round(v)));
}

/**
 * Governs where payloadFx media lands inside `sfHud`.
 * @param {Element} sfHud the `.sf-hud` div payloadFx draws into
 * @returns {{ dispose(): void, place(node: Element): boolean, taken: number }}
 */
export function createMediaLane(sfHud) {
  let n = 0;                       // lane cursor: left, right, ceiling, left, ...
  const slot = [0, 0, 0];          // per-lane cursor so two cards never stack exactly
  let raf = 0;
  let disposed = false;
  let banner = null;               // the .rh-banner ribbon, looked up lazily
  const pending = new Set();
  const api = { taken: 0 };

  /** True for a payloadFx card that scatters over the road. */
  function isMedia(node) {
    if (!node || node.nodeType !== 1 || !node.classList) return false;
    if (hasClass(node, DENY)) return false;
    if (typeof node.closest === 'function' && node.closest('.sf-pfx-videocard')) return false;
    if (hasClass(node, ALLOW)) return true;
    const tag = (node.tagName || '').toUpperCase();
    if (tag === 'IMG' || tag === 'VIDEO' || tag === 'PICTURE') return true;
    return /card/i.test(node.className || '');
  }

  /** The room ribbon (hud.js MARQUEE) shares the top of the left rail on a
   * short window, and it only lives 2.6 s, so the rail yields to it. */
  function ribbonUp() {
    try {
      if (!banner && sfHud && sfHud.ownerDocument) banner = sfHud.ownerDocument.querySelector('.rh-banner');
      return !!(banner && banner.classList.contains('is-on'));
    } catch (e) { return false; }
  }

  /** Move one card into the next lane. Returns true if it took it. */
  function place(node) {
    if (disposed || !isMedia(node)) return false;
    const fresh = !node.classList.contains('rh-laned');
    if (fresh && LANES[n % LANES.length].name === 'left' && ribbonUp()) n++;
    const lane = fresh ? LANES[n++ % LANES.length] : LANES[(node.__rhLane | 0) % LANES.length];
    if (fresh) {
      const i = LANES.indexOf(lane);
      node.__rhLane = i;
      const k = slot[i]++;
      // jitter inside the lane so a wave reads as scattered, not as a column
      node.__rhX = lane.xs[k % lane.xs.length] + (Math.random() * 4 - 2);
      node.__rhY = lane.ys[k % lane.ys.length] + (Math.random() * 6 - 3);
      node.__rhDur = lifeMs(node);
      api.taken++;
    }
    const s = node.style;
    s.setProperty('--rh-lane-x', `${node.__rhX.toFixed(2)}vw`);
    s.setProperty('--rh-lane-y', `${node.__rhY.toFixed(2)}vh`);
    s.setProperty('--rh-lane-w', `${lane.w}vw`);
    s.setProperty('--rh-lane-h', `${lane.h}vh`);
    s.setProperty('--rh-lane-dur', `${node.__rhDur}ms`);
    node.classList.add('rh-laned', `rh-lane-${lane.name}`);
    return true;
  }

  // payloadFx writes some styles AFTER the append, so every card is placed
  // again on the next frame; re-placing is idempotent (the lane is remembered).
  function sweep() {
    raf = 0;
    if (disposed) return;
    for (const node of pending) place(node);
    pending.clear();
  }
  function queue(node) {
    pending.add(node);
    if (!raf && typeof requestAnimationFrame === 'function') raf = requestAnimationFrame(sweep);
  }

  function take(node) {
    if (!node || node.nodeType !== 1) return;
    if (place(node)) queue(node);
    // a card can arrive wrapped (a frame div): check one level in
    const kids = node.children;
    if (kids && kids.length && kids.length < 8) {
      for (let i = 0; i < kids.length; i++) { if (place(kids[i])) queue(kids[i]); }
    }
  }

  let obs = null;
  if (sfHud && typeof MutationObserver === 'function') {
    obs = new MutationObserver((records) => {
      for (const r of records) {
        const added = r.addedNodes;
        if (!added) continue;
        for (let i = 0; i < added.length; i++) take(added[i]);
      }
    });
    obs.observe(sfHud, { childList: true, subtree: true });
  }
  // anything already in flight when the lane opens
  if (sfHud && typeof sfHud.querySelectorAll === 'function') {
    const now = sfHud.querySelectorAll(ALLOW.map((c) => `.${c}`).join(','));
    for (let i = 0; i < now.length; i++) take(now[i]);
  }

  api.place = place;
  api.dispose = function dispose() {
    if (disposed) return;
    disposed = true;
    if (obs) obs.disconnect();
    if (raf && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(raf);
    raf = 0;
    pending.clear();
  };
  return api;
}

// self-check: node --check is the bar; _p2check.html and the DOM-stub test in
// the PR body exercise the placement itself.
