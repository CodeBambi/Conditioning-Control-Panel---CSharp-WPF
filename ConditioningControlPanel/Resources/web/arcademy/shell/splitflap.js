/* ============================================================================
 * shell/splitflap.js - the departure-board reveal.
 *
 * A clean reimplementation of the mockup's board + replayFlaps(): the whole
 * board carries `.play`, each row publishes `--r` (row index) and each flap
 * `--i` (character index), and ONE CSS keyframe (arc-flap in styles.css)
 * staggers off those two variables into a departure-board cascade. Re-flip is
 * remove .play -> force reflow -> add .play; no JS animation loop exists, which
 * is why the whole thing costs nothing on a weak GPU and why
 * prefers-reduced-motion can kill it with a media query.
 *
 * Every visible string arrives already lexicon-resolved: this module renders
 * text, it never looks a display string up (SYNTHESIS #9 - one lexicon, and the
 * board is not allowed to be a second one).
 * ==========================================================================*/

/** Character budget per row so a long mod-skinned name cannot blow the board. */
const MAX_FLAPS = 18;

/* ----------------------------------------------------------------------------
 * THE BOARD HAS A VOICE (AV Club, 2026-08-24). A departure board that deals in
 * silence is a departure board nobody looks up at. Three cues, and every one of
 * them rides the CASCADE, never the build: `animate:false` is the shell's
 * repaint flag (trap 4) and the campus hangs its board behind the plaque that
 * way, so a build that does not flip stays DEAD SILENT. The flaps only speak
 * when they actually turn - the first open of the night, and a replay.
 *
 * shell/audio.js is the one holder of an audio node on this page (trap 18), so
 * this is a REQUEST on `document` and never a sound - the exact defensive shape
 * shell/ceremonies.js sfx() and shell/punchcard.js thud() already set.
 * -------------------------------------------------------------------------- */

/** The row stagger baked into styles.css's arc-flap (`--r * .4s`). If that
 *  number moves, this one moves with it or the ticks drift off the picture. */
const ROW_STEP_MS = 400;
/** A row's meta chip lands at `--r * .4s + 1s` and fades for .5s: the last row's
 *  fade ending IS the board settling. */
const SETTLE_MS = 1500;
/** HARD CAP. Four rows tonight; this is what stops a twenty-row wall one day
 *  from machine-gunning the mixer with twenty ticks. */
const MAX_TICKS = 12;

function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/** One flap strip: 'DAILY TRIGGER' -> flaps, spaces as .gap spacers. */
function flapStrip(text) {
  const strip = el('span', 'bl');
  const chars = String(text == null ? '' : text).toUpperCase().slice(0, MAX_FLAPS).split('');
  chars.forEach((ch, i) => {
    const isGap = ch === ' ';
    const f = el('span', isGap ? 'fl gap' : 'fl', isGap ? '' : ch);
    f.style.setProperty('--i', String(i));
    strip.appendChild(f);
  });
  return strip;
}

/**
 * Render the board.
 *
 * @param {Object} o
 * @param {HTMLElement} o.mount
 * @param {Array<Object>} o.rows       [{ id, time, label, chips:[{text,kind}],
 *                                       disabled, done, ariaLabel }]
 * @param {boolean=} o.reducedMotion   skip the cascade, render composed
 * @param {boolean=} o.animate         false = build without flipping (a repaint
 *                                     that is not a reveal must not re-flap)
 * @param {Function=} o.onSelect       (rowId, row) => void
 * @returns {{replay:Function, setRows:Function, root:HTMLElement, destroy:Function}}
 */
export function createBoard({ mount, rows, reducedMotion, animate, onSelect } = {}) {
  const root = el('div', 'board');
  let current = Array.isArray(rows) ? rows : [];
  let reduced = !!reducedMotion;

  /* The cascade's cues, in flight. A re-flip drops the old ones rather than
   * letting two deals tick over each other, and destroy() takes them with it -
   * a board that has left the document must not still be knocking. */
  const cueTimers = new Set();

  function dropCues() {
    for (const id of Array.from(cueTimers)) {
      try { clearTimeout(id); } catch (e) { /* noop */ }
    }
    cueTimers.clear();
  }

  function cueLater(fn, ms) {
    if (typeof setTimeout !== 'function') return;
    const id = setTimeout(() => {
      cueTimers.delete(id);
      // A BOARD THAT HAS LEFT THE PAGE STOPS TALKING. The shell wipes the screen
      // rather than destroying its board (clearScreen empties dom.screen), so a
      // room clicked mid-cascade must not land its settle thunk inside a class.
      // `isConnected` is undefined in the node DOM double, which reads as "fine".
      if (root.isConnected === false) return;
      fn();
    }, Math.max(0, ms));
    cueTimers.add(id);
  }

  /** The sound of a deal: the whole board announces, each row turns, it settles. */
  function cueCascade() {
    dropCues();
    const n = current.length;
    if (!n) return;
    /* SAMPLE-ONLY (no recipe fallback): silent until assets/sfx/flap_deal.mp3
     * ships, and silent by design rather than by accident. */
    sfx('flap_deal', 0.5);
    const ticks = Math.min(n, MAX_TICKS);
    for (let r = 0; r < ticks; r++) cueLater(() => sfx('flap', 0.2), r * ROW_STEP_MS);
    cueLater(() => sfx('commit', 0.3), (n - 1) * ROW_STEP_MS + SETTLE_MS);
  }

  function build() {
    root.textContent = '';
    current.forEach((row, r) => {
      // A row is a <button> so keyboard focus, Enter/Space and screen readers all
      // work for free - a <div role=button> would need three more handlers.
      const brow = el('button', 'brow' + (row.done ? ' done' : ''));
      brow.type = 'button';
      brow.style.setProperty('--r', String(r));
      if (row.disabled) brow.disabled = true;
      if (row.ariaLabel) brow.setAttribute('aria-label', row.ariaLabel);

      brow.appendChild(flapStrip(row.time || ''));
      brow.appendChild(flapStrip(row.label || ''));

      const meta = el('span', 'meta');
      (row.chips || []).forEach((c) => {
        if (!c) return;
        meta.appendChild(el('span', 'chip' + (c.kind ? ' ' + c.kind : ''), c.text));
      });
      brow.appendChild(meta);

      brow.addEventListener('click', () => {
        if (row.disabled) return;
        try { if (onSelect) onSelect(row.id, row); }
        catch (e) { /* a bad handler must not brick the board */ }
      });
      root.appendChild(brow);
    });
  }

  /** The mockup's replayFlaps(): drop .play, force reflow, re-add. */
  function replay() {
    // Reduced motion has no cascade, so it has nothing to sound: the cue rides
    // the picture or it does not happen.
    if (reduced) { dropCues(); root.classList.remove('play'); return; }
    root.classList.remove('play');
    void root.offsetWidth;          // reflow, or the class re-add is coalesced away
    root.classList.add('play');
    cueCascade();
  }

  const api = {
    root,
    replay,
    setRows(next, opts) {
      current = Array.isArray(next) ? next : [];
      if (opts && typeof opts.reducedMotion === 'boolean') reduced = opts.reducedMotion;
      build();
      if (!opts || opts.animate !== false) replay();
    },
    destroy() { dropCues(); root.remove(); },
  };

  build();
  if (mount) mount.appendChild(root);
  if (animate !== false) replay();
  return api;
}

export default createBoard;
