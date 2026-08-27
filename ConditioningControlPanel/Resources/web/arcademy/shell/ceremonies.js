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

/* ----------------------------------------------------------------------------
 * THE FLOOR HAS A VOICE (W0, 2026-08-24). The delegate below asks the engine
 * first, but this module also serves the two places that have NO engine to ask:
 * the shell's own instance (end card, report card - built engine:null, the
 * engine is per class) and a class whose engine failed to load. Both used to
 * fall to a CSS-only floor that was completely SILENT - the grade stamp, the
 * payoff jackpot and the near-miss all landed mute, and silence is where people
 * stand up. shell/audio.js is still the one audio owner (trap 18): this is a
 * REQUEST on `document`, never a node - the exact precedent punchcard.js's
 * thud() set. Levels mirror the engine's own numbers so a beat sounds the same
 * whichever path served it.
 * -------------------------------------------------------------------------- */
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
 * @param {Function=} o.confetti    () -> true when the player owns the Prize
 *                                  Counter's confetti stamp. A GETTER, not a
 *                                  flag: the thing can be bought mid-night and
 *                                  the ceremonies live for the whole class.
 * @param {Function=} o.log
 */
export function createCeremonies({ engine, layer, reducedMotion, confetti, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const reduced = !!reducedMotion;
  /* THE GARNISH. One class on the stamp node, and prizecounter's CSS-free
   * cousin in styles.css does the rest. It is deliberately not a new ceremony:
   * a cosmetic that changed the BEAT would be a cosmetic that changed the
   * game, and the three-tier law says the shop sells looks and nothing else. */
  const hasConfetti = () => {
    try { return typeof confetti === 'function' ? !!confetti() : false; }
    catch (e) { return false; }
  };
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
    stamp({ text, tone, target, hold, pitch, quiet } = {}) {
      // `label` beside `text`: the engine's stamp reads opts.label and was
      // rendering BLANK on every delegated beat (W0 audit). Both ride so either
      // reader is served; the engine's own tone names (good/bad/gild) differ
      // from ours ('pink') on purpose - a wrong tone is a default, never a throw.
      // `dom:false` (2026-08-26): the delegate carries the BEAT (fx + cue);
      // the ONE stamp node is built below, at the caller's target. Before
      // this, every delegated stamp rendered twice - the engine's ae-stamp in
      // its fx layer AND our arc-stamp here.
      const opts = { text, label: text, tone, target, dom: false };
      const engineTook = delegate('stamp', opts);
      // The engine path plays its own stamp cue; the floor plays the same one.
      // `pitch` (gradeObject's rank ladder) rides only the floor - the engine's
      // ceremony sfx takes no pitch, and a doubled cue would be worse.
      if (!engineTook && !quiet) {
        sfx(tone === 'bad' ? 'stamp_bad' : 'stamp', 0.55, pitch ? { pitch } : null);
      }
      const host = target || layer;
      if (!host) return null;

      const node = el('div', 'arc-stamp' + (tone === 'pink' ? ' pink' : '') + (reduced ? '' : ' pop')
        + (hasConfetti() && !reduced ? ' arc-stamp-confetti' : ''),
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
      /* NO DELEGATE, ON PURPOSE (W0, 2026-08-24). The engine's streak_meter
       * ceremony reads {streak} - this call sent {filled, total}, which parsed
       * as streak 0, so the CHIME LADDER never once played (the audit's headline
       * bug) AND the engine quietly mounted its own invisible meter node in the
       * fx layer on the first call. Fixing the field name would have surfaced
       * that second meter over this one. The shell draws the ONE meter; the
       * chime is requested directly: level and pitch climb with the lit count
       * (a semitone per segment, capped at 7 - the House Book's ladder), hidden
       * under 2 exactly like the engine's own spec. */
      if (lit >= 2) {
        sfx('streak', Math.min(1, 0.25 + 0.05 * lit),
          { pitch: Math.pow(2, Math.min(lit - 2, 7) / 12) });
      }

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
      /* The CSS floor sounds like the engine path would have (W0): the engine
       * plays jackpot at 0.6+0.4*intensity (default .8 -> .92) and near_miss at
       * 0.2+0.3*intensity (default .4 -> .32). payoff()'s scaled-down loss beat
       * (scale .5) scales the LEVEL the same way it scales the spectacle. */
      const scale = o.scale == null ? 1 : Math.max(0, Math.min(1, Number(o.scale) || 0));
      sfx(k, (k === 'jackpot' ? 0.92 : 0.32) * scale,
        k === 'jackpot' ? { duck: { target: 'spotlight', mult: 0.25, ms: 600 } } : null);
      api.stamp({
        text: o.text || (k === 'jackpot' ? 'JACKPOT' : 'SO CLOSE'),
        tone: k === 'jackpot' ? undefined : 'pink',
        target: o.target,
        quiet: true,               // the reward cue above IS this beat's sound
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
      // THE GRADE IS RANK-PITCHED (W0): the House Book's reference beat is the
      // punch-card thud and this is its ladder verbatim (punchcard.js
      // 0.78/0.92/1/1.18) - a C lands low, an S rings. PASS sits at 1.
      // S+ RINGS HIGHER THAN S and it has to be spelled out: the key here is
      // the LOWERCASED grade, so 'S+' arrives as 's+' and would have fallen
      // through to the flat 1 a PASS gets. One rung above the S, no further -
      // the ladder is the punch card's and this is the top of it.
      const GRADE_PITCH = { c: 0.78, b: 0.92, a: 1, s: 1.18, 's+': 1.28 };
      // 'a' and 'pass' wear the pink seal, everything else the gold one - the
      // stamp primitive's only two tones, unchanged.
      const node = api.stamp({
        text, target, hold,
        tone: (g === 'a' || g === 'pass') ? 'pink' : undefined,
        pitch: GRADE_PITCH[g] || 1,
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
      // 's+' is the lowercased honours letter and it is the BEST result the
      // school hands out, so it takes the jackpot rung it would otherwise have
      // missed entirely - `g === 's'` is a string compare and 's+' is not 's'.
      if (!failedGate && (g === 's+' || g === 's' || g === 'a')) {
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

    /**
     * THE CEREMONY'S OWN CLOCK, LENT OUT (W3 P0-28). Sequencing a beat needs a
     * timer, and a timer needs an owner: this one is already swept by
     * `destroy()` below, so a caller that borrows it cannot leave a stamp
     * falling on a screen that has gone. The end card uses it to put air
     * between its whoosh, its stamp and its payoff.
     * @returns {number} the handle, for a caller that wants to cancel early
     */
    later(fn, ms) {
      return later(typeof fn === 'function' ? fn : () => {}, Math.max(0, Number(ms) || 0));
    },

    destroy() {
      for (const t of Array.from(timers)) clearTimeout(t);
      timers.clear();
      if (layer) layer.textContent = '';
    },
  };

  return api;
}

export default createCeremonies;
