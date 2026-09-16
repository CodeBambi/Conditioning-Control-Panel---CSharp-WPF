/* ============================================================================
 * race/subliminal.js - THE WHISPER, COMING AT YOU (race page only).
 *
 *   createSubliminal(hudRoot, { reducedMotion }) -> { show, live, clear, dispose }
 *
 * WHAT THIS REPLACES. A subliminal pop used to draw game/payloadFx.js's whisper blip:
 * white Poppins at 9vmin, dead centre, up and gone in 420 ms. On the race page that is
 * the HUD's own face at the HUD's own size in the HUD's own white, so the owner drove it
 * and could not tell a PAYLOAD from a label: "the subliminals should be shown BIGGER and
 * should feel different from the HUD text, maybe they get bigger and go towards the pov
 * before fading or they have an animation."
 *
 * So the race gets its own card and the tube keeps the blip. payloadFx.js has exactly one
 * seam for this (`subliminalFx`), run.js is the only caller that passes it, and nothing in
 * game/ or styles.css changes: `.sf-pfx-sub` is untouched and dtrh.html looks the same.
 *
 * THE LOOK, and why each half of it is the OPPOSITE of the chrome:
 *   SIZE   the chrome's labels are 12-24 px and its score reads about 40; the card is sized
 *          off the viewport (fitPx below) and lands near 175 px on a desktop, so it is not
 *          a bigger label, it is a different order of thing.
 *   FACE   the same family as the chrome (nothing new is loaded, and a WebView2 with no
 *          network still has to draw this) but at 900 with wide tracking and no shadow-box:
 *          heavy and soft, not the chrome's small tight 800. NEVER a book face.
 *   INK    cream on a hot-pink bloom over a dark vignette. The chrome is white-on-teal
 *          plates and pink accents; the card owns the cream and owns the vignette, so a
 *          payload dims the room and a label never does.
 *   MOTION it starts small and blurred DEEP in the scene and rushes the POV: 0.62 -> 1.0
 *          by 320 ms, readable through a ~460 ms hold, then past the camera to 1.7 while
 *          it lets go. transform / opacity / filter only, on one will-change'd element.
 *
 * THE QUEUE. One card, ever. A second pop inside MIN_SHOW_MS is held and painted when the
 * first has had its readable moment (newest wins, depth one: a wave of pops is one card
 * after another, never two fighting over the middle of the screen). Past MIN_SHOW_MS the
 * new phrase simply takes the card over, restarting the rush.
 *
 * REDUCED MOTION. The card is BORN at full size and only fades (`is-still`); race.css also
 * answers `prefers-reduced-motion` for a browser whose player never opened the race menu.
 *
 * Z. The layer lives in `.race-hud` at z14: over the canvas (z0), the DOM walls (z1), the
 * chrome (z3), the caption band (z4) and every payloadFx layer (inside `.sf-hud`, z10), and
 * UNDER the pause veil (z20), the countdown (z21), the menu and the End card (z25) and the
 * shutter (z40). It never takes a pointer.
 * ==========================================================================*/

/** The whole card, in ms: rush in, hold, then past the POV. race.css owns the same number. */
export const LIFE_MS = 1250;
/** A card cannot be pushed off before this; a pop inside it is queued instead. */
export const MIN_SHOW_MS = 700;
/** How long a phrase stays worth echoing after the voice said it (run.js reads this). */
export const ECHO_SEC = 7;
/** The size ladder: how much of the one-or-two-word size a phrase of N words gets. */
const STEP = [1, 1, 1, 0.78, 0.68, 0.58, 0.54, 0.47, 0.44];
/** Floor and ceiling in px, so a phone never gets a whisper and a 4K never gets a wall. */
const MIN_PX = 21, MAX_PX = 210;

/** Words of a phrase, lowercased and squeezed (the house style for UI copy is lowercase). */
function wordsOf(text) {
  return String(text || '').toLowerCase().trim().split(/\s+/).filter(Boolean);
}

/**
 * The size this phrase gets at this viewport, in px. Two caps, whichever is smaller:
 *
 *   the LADDER  a one or two word phrase gets the full base, and a longer one steps down
 *               the STEP table. PORTRAIT gets a fatter share of its width (0.2 against
 *               0.145): a phone is narrow, and a short phrase that wraps to two enormous
 *               lines reads far better there than one timid line that fits.
 *   the WORD     whatever happens, the LONGEST word sits inside 86% of the glass. 0.56em
 *               per character is a fair read of this family at 900, and this is the cap
 *               that keeps an eight word line on a 390 px phone instead of off both sides.
 *
 * The card wraps (max-width in race.css), so the ladder only has to be honest about how
 * much room a phrase wants, not about how many lines it will take.
 */
export function fitPx(text, vw, vh) {
  const w = wordsOf(text);
  if (!w.length) return 0;
  const base = Math.min(vw * (vw < vh ? 0.2 : 0.145), vh * 0.245);
  const step = STEP[Math.min(w.length, STEP.length - 1)];
  const longest = w.reduce((n, s) => Math.max(n, s.length), 1);
  const byWord = (vw * 0.86) / (0.56 * longest);
  return Math.max(MIN_PX, Math.min(MAX_PX, Math.min(base * step, byWord)));
}

export function createSubliminal(hudRoot, { reducedMotion = false } = {}) {
  if (!hudRoot) return null;
  const layer = document.createElement('div');
  layer.className = 'rh-sub-layer';
  const veil = document.createElement('div');
  veil.className = 'rh-sub-veil';
  const card = document.createElement('div');
  card.className = 'rh-sub-card';
  if (reducedMotion) { card.classList.add('is-still'); veil.classList.add('is-still'); }
  layer.appendChild(veil);
  layer.appendChild(card);
  hudRoot.appendChild(layer);

  let shownAt = -1e9, endTimer = 0, queueTimer = 0, pending = null, shown = 0, queued = 0, disposed = false;
  const now = () => (typeof performance === 'object' && performance.now ? performance.now() : Date.now());

  /** Restart both animations on the SAME frame: strip the class, force a reflow, put it back. */
  function paint(text) {
    const w = wordsOf(text);
    if (!w.length) return false;
    const vw = window.innerWidth || 1280, vh = window.innerHeight || 720;
    card.textContent = w.join(' ');
    card.style.fontSize = Math.round(fitPx(text, vw, vh)) + 'px';
    layer.classList.remove('is-on');
    void layer.offsetWidth;            // the reflow that makes a re-fire actually re-fire
    layer.classList.add('is-on');
    shownAt = now(); shown++;
    if (endTimer) clearTimeout(endTimer);
    endTimer = setTimeout(() => {
      endTimer = 0;
      if (!disposed) layer.classList.remove('is-on');
    }, LIFE_MS);
    return true;
  }

  /** Fire one. `{ text }` is the phrase run.js resolved; strength is taken but not used yet. */
  function show({ text } = {}) {
    if (disposed) return false;
    const w = wordsOf(text);
    if (!w.length) return false;
    const since = now() - shownAt;
    if (layer.classList.contains('is-on') && since < MIN_SHOW_MS) {
      pending = w.join(' '); queued++;   // depth one, newest wins
      if (!queueTimer) queueTimer = setTimeout(() => {
        queueTimer = 0;
        const next = pending; pending = null;
        if (!disposed && next) paint(next);
      }, Math.max(16, MIN_SHOW_MS - since));
      return true;
    }
    return paint(w.join(' '));
  }

  /** True while a card is on the glass (the run's own book-keeping; nothing reads it yet). */
  const live = () => layer.classList.contains('is-on');

  /** Take the glass back at once: the run ended, or the page went home. */
  function clear() {
    pending = null;
    if (queueTimer) { clearTimeout(queueTimer); queueTimer = 0; }
    if (endTimer) { clearTimeout(endTimer); endTimer = 0; }
    layer.classList.remove('is-on');
  }

  function dispose() {
    disposed = true;
    clear();
    layer.remove();
  }

  return {
    show, live, clear, dispose,
    /** race/smoke/subliminal-check.mjs: how many were painted and how many the queue held. */
    stats: () => ({ shown, queued }),
    el: card,
  };
}
