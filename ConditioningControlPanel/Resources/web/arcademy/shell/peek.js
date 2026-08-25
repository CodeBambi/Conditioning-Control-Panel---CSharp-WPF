/* ============================================================================
 * shell/peek.js - the shared hold-to-reveal verb (SYNTHESIS #6).
 *
 * Lost & Found's Peek, Composure's Peek and Deja Vu's Cram Assist were three
 * designs for one verb. This is the verb: hold (pointer or the game's declared
 * `peek` keybind) to reveal, release to hide, and ONE grading rule -
 *
 *      using peek caps the class grade at A.
 *
 * The cap is a FLAT grade cap, never a time surcharge (Composure Q4 closed). The
 * shell reads peek.used at endClass and puts `assists.peek` into core/grades.js;
 * a game never grades its own peek and cannot opt out of the cap.
 *
 * The reveal itself is the game's business: peek only owns the verb, the timing
 * and the honesty. onReveal/onHide do the drawing.
 * ==========================================================================*/

/** Hold this long before the reveal starts - kills accidental taps. */
const ARM_MS = 110;
/** Safety ceiling on a single hold, in case a pointerup is lost (alt-tab). */
const MAX_HOLD_MS = 4000;

/* ----------------------------------------------------------------------------
 * THE SHUTTER. One cue, on the reveal - never on the release.
 * shell/audio.js holds the only audio node on the page (trap 18), so this is a
 * REQUEST on `document` and never a sound - the exact defensive shape
 * shell/ceremonies.js sfx() set. A dropped cue is not an error.
 * -------------------------------------------------------------------------- */
/* The three games that wire peek all draw their reveal in `onReveal` and none
 * of them makes a sound doing it, so this is the verb speaking for itself
 * rather than a second voice over theirs.
 */
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

/**
 * @param {Object} o
 * @param {Function=} o.onReveal    () => void   start showing
 * @param {Function=} o.onHide      () => void   stop showing
 * @param {Function=} o.onFirstUse  () => void   fired once, when the cap engages
 * @param {number=} o.maxHoldMs
 * @param {Function=} o.log
 */
export function createPeek({ onReveal, onHide, onFirstUse, maxHoldMs, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  // Mutable so the SHELL can build the verb before the class exists and the GAME
  // can say what a reveal actually shows (ctx.peek.setHandlers) once it does.
  let reveal = onReveal, hide = onHide, firstUse = onFirstUse;
  let used = false;
  let holding = false;
  let armTimer = 0;
  let capTimer = 0;
  let holdStartedAt = 0;
  let totalMs = 0;
  let count = 0;
  let destroyed = false;
  const bound = [];

  function clearTimers() {
    if (armTimer) { clearTimeout(armTimer); armTimer = 0; }
    if (capTimer) { clearTimeout(capTimer); capTimer = 0; }
  }

  function begin() {
    if (destroyed || holding || armTimer) return;
    armTimer = setTimeout(() => {
      armTimer = 0;
      holding = true;
      holdStartedAt = Date.now();
      count++;
      if (!used) {
        used = true;
        say('peek used - class capped at A');
        /* W3 P1-18: THE COST, SAID OUT LOUD, ONCE. This is the frame the class
         * is capped at an A in, and it used to pass without a sound - the
         * player paid the whole grade and heard the shutter, which is the part
         * they got for free. A bad stamp, quiet: it is a loss cue, and loss
         * cues are the quietest things in the building (owner's rule). */
        sfx('stamp_bad', 0.12);
        try { if (firstUse) firstUse(); } catch (e) { /* never fatal */ }
      }
      // The shutter opens. Quiet on purpose: peek costs the player a grade cap
      // already - it does not also need to announce itself to the room.
      sfx('shutter', 0.25);
      try { if (reveal) reveal(); } catch (e) { say('peek onReveal threw: ' + ((e && e.message) || e)); }
      capTimer = setTimeout(end, maxHoldMs || MAX_HOLD_MS);
    }, ARM_MS);
  }

  function end() {
    clearTimers();
    if (!holding) return;
    holding = false;
    totalMs += Math.max(0, Date.now() - holdStartedAt);
    // W3 P1-18: the shutter has opened since the verb existed and never once
    // closed. Lower and quieter than the open - a thing shutting, not opening.
    sfx('shutter', 0.18, { pitch: 0.9 });
    try { if (hide) hide(); } catch (e) { say('peek onHide threw: ' + ((e && e.message) || e)); }
  }

  const api = {
    /** True once the player has actually revealed something. */
    get used() { return used; },
    get holding() { return holding; },
    /** Diagnostics for the report card ("2 peeks, 1.4s"). */
    get stats() { return { used, count, totalMs }; },

    /**
     * Wire a hold surface: a button, the board, anything. Pointer + keyboard
     * (Enter/Space on a focused button) both work; pointer capture means a drag
     * off the element still releases.
     */
    attach(node) {
      if (!node || destroyed) return () => {};
      const down = (e) => {
        if (e.button != null && e.button !== 0) return;
        if (e.pointerId != null && node.setPointerCapture) {
          try { node.setPointerCapture(e.pointerId); } catch (_e) { /* fine */ }
        }
        e.preventDefault();
        node.classList.add('held');
        begin();
      };
      const up = () => { node.classList.remove('held'); if (used) node.classList.add('spent'); end(); };
      const keyDown = (e) => {
        if (e.repeat) return;
        if (e.key !== 'Enter' && e.key !== ' ' && e.code !== 'Space') return;
        e.preventDefault();
        node.classList.add('held');
        begin();
      };
      const keyUp = (e) => {
        if (e.key !== 'Enter' && e.key !== ' ' && e.code !== 'Space') return;
        up();
      };
      node.addEventListener('pointerdown', down);
      node.addEventListener('pointerup', up);
      node.addEventListener('pointercancel', up);
      node.addEventListener('pointerleave', up);
      node.addEventListener('keydown', keyDown);
      node.addEventListener('keyup', keyUp);
      const off = () => {
        node.removeEventListener('pointerdown', down);
        node.removeEventListener('pointerup', up);
        node.removeEventListener('pointercancel', up);
        node.removeEventListener('pointerleave', up);
        node.removeEventListener('keydown', keyDown);
        node.removeEventListener('keyup', keyUp);
      };
      bound.push(off);
      return off;
    },

    /**
     * Wire the game's declared `peek` keybind (ctx.keys). Kept separate from
     * attach() so a game can offer the button, the key, or both.
     */
    bindKeys(keys, verb) {
      if (!keys || typeof keys.on !== 'function') return () => {};
      const v = verb || 'peek';
      const offDown = keys.on(v, () => begin());
      const offUp = keys.on(v + ':up', () => end());
      const off = () => { offDown(); offUp(); };
      bound.push(off);
      return off;
    },

    /**
     * Install what a reveal shows. The game calls this in start(); the shell
     * built the verb (and owns the A-cap) before the game existed.
     */
    setHandlers({ onReveal: r, onHide: h, onFirstUse: f } = {}) {
      if (typeof r === 'function') reveal = r;
      if (typeof h === 'function') hide = h;
      if (typeof f === 'function') firstUse = f;
    },

    /** Programmatic hold (a game with its own gesture layer). */
    hold() { begin(); },
    release() { end(); },

    /** Suspend/teardown must always end the reveal - never leave one latched. */
    forceHide() { end(); },

    destroy() {
      destroyed = true;
      end();
      clearTimers();
      while (bound.length) { try { bound.pop()(); } catch (e) { /* noop */ } }
    },
  };

  return api;
}

export default createPeek;
