/* ============================================================================
 * shell/ceremonies.js - the shared reward ceremonies (SYNTHESIS #10).
 *
 * Four dossiers invented their own success stamp and Composure invented an
 * 8-segment streak meter; those are now ONE shell primitive that games skin and
 * never fork. This module is the shell's side of them:
 *
 *   stamp({text, tone, target})        the success stamp
 *   streakMeter({target, filled})      the 10-segment meter (always 10)
 *   reward(kind, opts)                 'jackpot' | 'near_miss' beats
 *
 * DELEGATION: visual flair belongs to the Distraction Engine, so every call
 * first asks engine.ceremony(kind, opts) (BUILD-CONTRACT §5). The DOM built here
 * is the floor, not the ceiling - if the engine is missing, still loading, or
 * throws, the CSS-only version stands and the beat still lands. That is the
 * whole point: a ceremony must never be the thing that fails.
 * ==========================================================================*/

const SEGMENTS = 10;              // SYNTHESIS #10 - ten, everywhere, forever
const STAMP_MS = 1600;
const STAMP_MS_REDUCED = 900;

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/**
 * @param {Object} o
 * @param {Object=} o.engine        the Distraction Engine facade (may be null)
 * @param {HTMLElement=} o.layer    fixed-position ceremony layer (#arc-ceremony)
 * @param {boolean=} o.reducedMotion
 * @param {Function=} o.log
 */
export function createCeremonies({ engine, layer, reducedMotion, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const reduced = !!reducedMotion;
  const timers = new Set();
  let engineWarned = false;

  /** Ask the engine first; return true if it took the beat. */
  function delegate(kind, opts) {
    if (!engine || typeof engine.ceremony !== 'function') return false;
    try {
      const r = engine.ceremony(kind, opts || {});
      return r !== false;                       // undefined = "handled"
    } catch (e) {
      if (!engineWarned) {
        engineWarned = true;                    // once per class, not per beat
        say('engine.ceremony(' + kind + ') threw, degrading to CSS: ' + ((e && e.message) || e));
      }
      return false;
    }
  }

  function later(fn, ms) {
    const t = setTimeout(() => { timers.delete(t); try { fn(); } catch (e) { /* noop */ } }, ms);
    timers.add(t);
    return t;
  }

  const api = {
    /**
     * The success stamp. `target` may be any element (the class root, a report
     * card cell); it falls back to the fixed ceremony layer.
     * @returns {HTMLElement|null} the stamp node (already scheduled to fade)
     */
    stamp({ text, tone, target, hold } = {}) {
      const opts = { text, tone, target };
      const engineTook = delegate('stamp', opts);
      const host = target || layer;
      if (!host) return null;

      const node = el('div', 'arc-stamp' + (tone === 'pink' ? ' pink' : '') + (reduced ? '' : ' pop'),
        String(text == null ? '' : text));
      // The fixed layer is pointer-events:none; an in-flow target needs the same
      // promise or a stamp could eat a click on the button underneath it.
      node.style.pointerEvents = 'none';
      if (host === layer) {
        node.style.position = 'absolute';
        node.style.left = '50%';
        node.style.top = '38%';
        node.style.transform = 'translate(-50%,-50%) rotate(-6deg)';
      }
      host.appendChild(node);

      const ms = hold || (reduced ? STAMP_MS_REDUCED : STAMP_MS);
      later(() => node.remove(), ms);
      if (!engineTook) say('stamp (css): ' + String(text || ''));
      return node;
    },

    /**
     * The 10-segment streak meter. Renders into `target` (replacing its content)
     * or returns a detached node the caller can place.
     * @param {number} filled 0..10 (values above 10 clamp - a 12-streak still
     *                shows ten lit segments; the number lives in the chip)
     */
    streakMeter({ target, filled, gold } = {}) {
      const lit = Math.max(0, Math.min(SEGMENTS, Math.round(Number(filled) || 0)));
      delegate('streak_meter', { filled: lit, total: SEGMENTS, target });

      const meter = el('span', 'arc-meter' + (reduced ? '' : ' fill'));
      meter.setAttribute('role', 'img');
      meter.setAttribute('aria-label', lit + ' of ' + SEGMENTS);
      for (let i = 0; i < SEGMENTS; i++) {
        const seg = el('i', i < lit ? (gold ? 'on gold' : 'on') : '');
        seg.style.setProperty('--i', String(i));
        meter.appendChild(seg);
      }
      if (target) { target.textContent = ''; target.appendChild(meter); }
      return meter;
    },

    /**
     * A reward beat. 'jackpot' and 'near_miss' are engine-owned spectacle; the
     * CSS floor is a stamp so the moment is still legible without the engine.
     */
    reward(kind, opts) {
      const k = String(kind || '');
      if (k !== 'jackpot' && k !== 'near_miss') {
        say('unknown ceremony kind: ' + k);
        return false;
      }
      if (delegate(k, opts)) return true;
      const o = opts || {};
      api.stamp({
        text: o.text || (k === 'jackpot' ? 'JACKPOT' : 'SO CLOSE'),
        tone: k === 'jackpot' ? undefined : 'pink',
        target: o.target,
      });
      return true;
    },

    /** How many segments the meter has - games must not hardcode 8 or 12. */
    get segments() { return SEGMENTS; },

    destroy() {
      for (const t of Array.from(timers)) clearTimeout(t);
      timers.clear();
      if (layer) layer.textContent = '';
    },
  };

  return api;
}

export default createCeremonies;
