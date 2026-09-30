/* shared/host-escape.js - the Escape the panel kept reaches the game once.
 *
 * Escape is the default panic key and the panel's keyboard hook sees every
 * press. With Breakout in front it keeps a first Escape as the game's pause
 * (PanicPolicy.GameClaimsEscapeAsPause; a second within 2 s is a full panic).
 * The game pauses on its own keydown, and while its WebView2 is out of keyboard
 * focus (the title bar clicked) that keydown never comes: the press was kept,
 * and nothing paused and nothing panicked. So the host hands the kept press over
 * as { type: 'kept-escape' } (BackRoomHostService.PostKeptEscape) and this plays
 * it as the page's own Escape: one keydown at the focused element, through
 * every listener a real one meets.
 *
 * A focused game hears the real key too, in either order: the hook runs before
 * the key reaches WebView2, and the frame is posted after it. One press is
 * handled once (twice would pause and then leave), so the frame is dropped when
 * a real Escape came
 *   before it, within LOOK_BACK_MS (the usual order; a stalled panel posts late),
 *   after it, within LOOK_AHEAD_MS (the frame outran the key), so it waits that
 *   long before it plays.
 * The panel never keeps two presses closer than 2 s, so no other press fits in.
 *
 * The same shape as piecebypiece/ui/host-escape.js (the chess board) and
 * dtrh/race/hostEscape.js; each tree ships alone on the web, so each keeps its
 * own copy. Unhosted (the web build) no frame ever comes and this only listens.
 * Make it before the game's own listeners: its capture listener hears every
 * real Escape before any part of the page keeps one for itself.
 */

export const HOST_ESCAPE = 'kept-escape';
export const LOOK_BACK_MS = 1500;
export const LOOK_AHEAD_MS = 300;

const escapeKeydown = () => new KeyboardEvent('keydown', {
  key: 'Escape', code: 'Escape', keyCode: 27, which: 27, bubbles: true, cancelable: true, composed: true,
});

export function createHostEscape({
  win = globalThis.window,
  doc = globalThis.document,
  now = () => performance.now(),
  later = setTimeout,
  cancel = clearTimeout,
  makeEvent = escapeKeydown,
} = {}) {
  let lastReal = -Infinity;   // when a real Escape last reached the page
  let waiting = 0;            // the kept press, until LOOK_AHEAD_MS says no real key is coming
  let playing = false;        // our own keydown is not a real one

  const onKey = (e) => {
    if (playing || !e || e.key !== 'Escape') return;
    lastReal = now();
    if (waiting) { cancel(waiting); waiting = 0; }   // the real key got here after all: it is this press
  };
  win?.addEventListener?.('keydown', onKey, true);

  function play() {
    const target = doc?.activeElement || doc?.body;
    if (!target) return;
    playing = true;
    try { target.dispatchEvent(makeEvent()); }
    catch (e) { console.warn('[host-escape] kept Escape', e); }
    finally { playing = false; }
  }

  /** The host kept an Escape as the game's pause. True when it will play, false when dropped. */
  function kept() {
    const at = now();
    if (at - lastReal < LOOK_BACK_MS) return false;   // the page already had this press
    if (waiting) return false;                          // one kept press at a time
    waiting = later(() => {
      waiting = 0;
      if (lastReal >= at) return;
      play();
    }, LOOK_AHEAD_MS);
    return true;
  }

  function dispose() {
    if (waiting) { cancel(waiting); waiting = 0; }
    win?.removeEventListener?.('keydown', onKey, true);
  }

  return { kept, dispose };
}
