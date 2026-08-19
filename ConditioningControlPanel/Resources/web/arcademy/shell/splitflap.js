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
    if (reduced) { root.classList.remove('play'); return; }
    root.classList.remove('play');
    void root.offsetWidth;          // reflow, or the class re-add is coalesced away
    root.classList.add('play');
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
    destroy() { root.remove(); },
  };

  build();
  if (mount) mount.appendChild(root);
  if (animate !== false) replay();
  return api;
}

export default createBoard;
