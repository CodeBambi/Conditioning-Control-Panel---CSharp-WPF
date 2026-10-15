/* ============================================================================
 * board/gpu.js - a plainer board for a GPU that cannot draw the full one.
 *
 * A Pixel 10 (PowerVR) showed a white screen while the game ran underneath.
 * Two doors into the lite board: the WebGL context could not be made at all
 * (boot.js retries once in lite on the spot), or it was lost while playing
 * (the page remembers lite and reloads once). A second loss in lite is the
 * honest "could not start" card, never a reload loop.
 *
 * Lite = no MSAA, pixel ratio 1, no shadow map, no glow, no reflected room.
 * The pieces, the board and every game rule are the same.
 *
 * Remembered for a week, so a desktop GPU reset after sleep does not keep a
 * good machine on the plain board forever. ?gfx=lite forces it for a test.
 * ==========================================================================*/

export const GPU = Object.freeze({
  key: 'pbp.gfx.lite',          // localStorage: ms timestamp of the last loss
  holdMs: 7 * 24 * 3600 * 1000, // how long a loss keeps the board plain
});

function store() {
  try { return window.localStorage; } catch { return null; }
}

/** True when this device should draw the lite board. */
export function gfxLite(now = Date.now()) {
  try {
    if (new URLSearchParams(window.location.search).get('gfx') === 'lite') return true;
  } catch { /* no url */ }
  const s = store();
  if (!s) return false;
  let at = 0;
  try { at = Number(s.getItem(GPU.key)) || 0; } catch { return false; }
  return at > 0 && now - at < GPU.holdMs;
}

/** Remember a loss. False when nothing can be remembered (a reload would loop). */
export function rememberLite(now = Date.now()) {
  const s = store();
  if (!s) return false;
  try { s.setItem(GPU.key, String(now)); return s.getItem(GPU.key) === String(now); } catch { return false; }
}

/**
 * Watch the canvas for a lost context. In full mode: remember lite, reload
 * once. Already lite (or nothing to remember with): `onGiveUp`.
 */
export function watchContextLoss(canvas, { lite, onGiveUp, reload = () => window.location.reload() }) {
  const onLost = (e) => {
    e.preventDefault();
    canvas.removeEventListener('webglcontextlost', onLost);
    if (!lite && rememberLite()) {
      console.warn('[pbp] the GPU dropped the board; reloading it plain');
      reload();
      return;
    }
    onGiveUp();
  };
  canvas.addEventListener('webglcontextlost', onLost);
  return () => canvas.removeEventListener('webglcontextlost', onLost);
}
