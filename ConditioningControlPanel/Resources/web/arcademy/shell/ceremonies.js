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

/* HOUSE RULES additions (Deck V / Deck VI, shell level so all ten classes get
 * them): `gradeObject({grade,...})` mints the grade as a physical stamp instead
 * of a bare letter string, and `payoff({grade, capped, ...})` guarantees that a
 * C - or a class that dropped a hard gate - still gets a scaled-down beat rather
 * than silence. Both ride the same delegate-to-the-engine floor as everything
 * above them. */

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

    /* ---------------------------------------------------------------------
     * DECK VI - GRADES AS OBJECTS. The grade arrives as a physical thing, never
     * as a bare letter printed into a <span>: it is minted through the stamp
     * ceremony above (so the engine gets first refusal, exactly like every other
     * beat) and only then dressed with its grade class. Callers hand this an
     * OBJECT - {grade, zen, target} - which is the whole point: nothing upstream
     * gets to render the letter itself first.
     * @returns {HTMLElement|null} the stamp node
     * ------------------------------------------------------------------- */
    gradeObject({ grade, zen, target, hold, label } = {}) {
      const raw = String(grade == null ? '' : grade);
      const g = raw.toLowerCase();
      const text = label != null ? String(label)
        : (g === 'pass' || zen === true ? 'PASS' : raw.toUpperCase());
      // 'a' and 'pass' wear the pink seal, everything else the gold one - the
      // stamp primitive's only two tones, unchanged.
      const node = api.stamp({
        text, target, hold,
        tone: (g === 'a' || g === 'pass') ? 'pink' : undefined,
      });
      if (!node) return null;
      try {
        if (node.classList) {
          node.classList.add('arc-gradeobj');
          if (g) node.classList.add('g-' + g.replace(/[^a-z]/g, ''));
        }
        if (node.setAttribute) {
          node.setAttribute('role', 'img');
          node.setAttribute('aria-label', text);
          node.setAttribute('data-grade', g);
        }
      } catch (e) { /* a decoration must never be the thing that throws */ }
      return node;
    },

    /* ---------------------------------------------------------------------
     * DECK V - LOSSES DISGUISED. Every finished class pays SOMETHING. A top
     * grade gets the jackpot, the middle gets its stamp, and a C (or any class
     * that dropped a hard gate) gets a scaled-down near-miss beat instead of
     * silence - because silence is where people stand up. Never returns false:
     * the worst case is the CSS stamp floor.
     * @returns {'jackpot'|'near_miss'|'stamp'} which beat was played
     * ------------------------------------------------------------------- */
    payoff({ grade, zen, gated, capped, target, text, scale } = {}) {
      const g = String(grade || '').toLowerCase();
      const capList = Array.isArray(capped) ? capped.map((c) => String(c)) : [];
      const failedGate = !!gated || capList.some((c) => c.indexOf('hard') >= 0);
      const opts = { target, scale: scale == null ? 1 : scale };
      if (!failedGate && (g === 's' || g === 'a')) {
        api.reward('jackpot', Object.assign({ text: text || 'JACKPOT' }, opts));
        return 'jackpot';
      }
      if (g === 'c' || failedGate) {
        // Scaled DOWN, never off: half the spectacle, all of the acknowledgement.
        api.reward('near_miss', Object.assign({ text: text || 'SO CLOSE' },
          opts, { scale: scale == null ? 0.5 : scale }));
        return 'near_miss';
      }
      api.stamp({ text: text || (g === 'pass' || zen === true ? 'PASS' : 'MARKED'), target });
      return 'stamp';
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
