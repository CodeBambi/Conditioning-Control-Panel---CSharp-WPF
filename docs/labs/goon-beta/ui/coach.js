/* ============================================================================
 * ui/coach.js — the one-time explainers.
 *
 * "add some nudges and explanations — the whole game feels kinda random atm
 * with no direction" (owner, 2026-08-05).
 *
 * WHAT THIS IS. A duel throws seven kinds of effect, a heat gauge, an item
 * economy, an attention check and a self-reported dial at a stranger inside the
 * first two minutes, and until today the page explained exactly one of them (the
 * six bullets on the title card, which a player reads before any of it has
 * happened and therefore cannot yet attach to anything). This module is the
 * other half: ONE short line, fired the first time each mechanic actually
 * touches the player, at the moment it touches them — and then never again.
 *
 * WHAT IT IS NOT, and every one of these is a rule rather than a preference:
 *
 *   · NOT A TUTORIAL. There is no sequence, no step counter and no "next".
 *     Hints fire in whatever order the match happens to deal them, and a hint
 *     for a mechanic a player never meets is simply never spent.
 *   · NOT MODAL, EVER. Everything here goes out through ui/toasts.js — the
 *     transient stack on #gg-toasts (z50), which is below MERCY (z60) by
 *     construction and holds no interactive element. Nothing in this file can
 *     pause the clock, cover the mercy button, eat a bubble pop or take focus.
 *     A sheet during Live is what froze the drop economy the last time one was
 *     opened, and coaching is the LEAST important thing on the desk.
 *   · NOT A GAMEPLAY CHANGE. It reads state and writes prefs. It never touches
 *     the match, the arsenal, the economy or the wire.
 *
 * ONCE-EVER, AND THE MARK GOES DOWN AT QUEUE TIME. `fire()` records the hint as
 * spent before its toast has been shown, let alone dismissed. That is
 * deliberate: a player who alt-tabs, mercies out or closes the page with a
 * coached line still queued has already been interrupted enough, and re-offering
 * it next launch would break the one promise the feature makes — that it goes
 * away. The cost is a hint that can be lost to a teardown; it is a hint, and the
 * alternative is a hint that can be shown twice.
 *
 * THE PACER. Firsts arrive in clumps — the first drop, the first throw and the
 * first inbound payload can all land inside ten seconds of a lively opening —
 * and four explainers stacked on top of each other is noise, not coaching. So
 * fires are QUEUED and drained one per COACH_GAP_MS, and a queue that is already
 * COACH_QUEUE_MAX deep drops the newest rather than growing: a line that would
 * appear a minute after the thing it explains is worse than no line. Dropped
 * that way the hint stays SPENT — see above; being shown once is the ceiling,
 * not the guarantee.
 *
 * Everything above the createCoach() line is pure and total, so
 * test/selftest-coach.js can pin the policy without a DOM, a store or a clock.
 * Node-import-safe: no DOM at import and none anywhere in this file — the toast
 * tier owns every node.
 * ==========================================================================*/

/** The pref that switches the whole feature off. */
export const COACH_PREF_ENABLED = 'coachHints';
/** The pref holding `{ [hintId]: true }` for every line already spent. */
export const COACH_PREF_SEEN = 'coachSeen';

/**
 * Every hint id, spelled once.
 *
 * The VALUES are what land in the `coachSeen` map and therefore in a player's
 * localStorage forever, so they are stable identifiers and not labels: renaming
 * one re-shows a hint to everybody who already read it, which is the single
 * worst thing this module can do. Add ids; do not rename them.
 */
export const COACH = Object.freeze({
  /** The first bubble pop that banked heat. */
  POP: 'pop',
  /** The first drop that armed a slot. */
  DROP: 'drop',
  /** The first payload this player actually fired. */
  FIRED: 'fired',
  /** The first payload of THEIRS that was accepted for this screen. */
  INCOMING: 'incoming',
  /** The first attention token to spawn. */
  CHECK: 'check',
  /** The first time the player moved the closeness dial. */
  DIAL: 'dial',
  /** The first emote received. */
  EMOTE: 'emote',
  /** Practice only: the seeded arsenal, and one concrete thing to press. */
  PRACTICE: 'practice',
});

/** The ids, as a list, in no meaningful order (there is no sequence). */
export const COACH_IDS = Object.freeze(Object.keys(COACH).map((k) => COACH[k]));

/** Minimum spacing between two coached lines. See THE PACER. */
export const COACH_GAP_MS = 2800;
/** How long a coached toast dwells — longer than a status toast, it is a sentence. */
export const COACH_TOAST_MS = 6400;
/** Queue depth past which a new fire is dropped (but still marked spent). */
export const COACH_QUEUE_MAX = 3;
/** The glyph every coached toast carries, so the stack reads as one voice. */
export const COACH_ICON = '✳';

/* ---------------------------------------------------------------------------
 * THE POLICY, pure. Nothing below reads a store, a clock or a document.
 * ------------------------------------------------------------------------ */

/** Is this a hint this module knows about? Total: any input, boolean out. */
export function isCoachId(id) {
  return typeof id === 'string' && COACH_IDS.indexOf(id) >= 0;
}

/**
 * Has `id` already been spent?
 *
 * ANY truthy member counts, not `=== true`: the map round-trips through JSON and
 * through ui/prefs.js's `coerce`, and a store written by an older or a
 * hand-edited build must not be able to resurrect a hint by holding a 1 where
 * this file writes a true.
 */
export function hasSeen(seen, id) {
  if (!seen || typeof seen !== 'object') return false;
  return !!seen[id];
}

/**
 * `seen` + `id` -> a NEW map with `id` spent. Never mutates its argument (the
 * caller's copy came out of prefs.get, which hands out clones on purpose) and
 * never records an id this module does not own, so a typo at a call site cannot
 * quietly grow the stored blob.
 */
export function withSeen(seen, id) {
  const base = (seen && typeof seen === 'object') ? seen : {};
  if (!isCoachId(id) || hasSeen(base, id)) return base;
  const out = {};
  for (const k of Object.keys(base)) out[k] = base[k];
  out[id] = true;
  return out;
}

/**
 * The whole gate, in one pure function: may `id` be shown?
 *
 * Order matters and is the order a reader would ask it in — is it a real hint,
 * is the feature on, has it already been spent. `enabled` defaults to true so a
 * caller with no store still coaches; a page with no prefs is a dev harness or a
 * browser with storage disabled, and neither is a reason to go silent.
 *
 * @param {string} id
 * @param {{enabled?:boolean, seen?:object}} [state]
 */
export function shouldFire(id, { enabled = true, seen = null } = {}) {
  if (!isCoachId(id)) return false;
  if (enabled === false) return false;
  return !hasSeen(seen, id);
}

/* ---------------------------------------------------------------------------
 * THE HANDLE
 * ------------------------------------------------------------------------ */

/**
 * @param {object}   [o]
 * @param {object}   [o.prefs]  ui/prefs.js store. Absent: hints fire, once per
 *                              page, and nothing is remembered across a reload.
 * @param {object}   [o.toasts] ui/toasts.js handle. Absent: every fire is a
 *                              silent no-op that still spends the hint — a page
 *                              with no toast layer must not bank them all up.
 * @param {object}   [o.logger]
 * @param {Function} [o.now]    () => ms, for the suite. Defaults to Date.now.
 * @param {Function} [o.schedule] (fn, ms) => handle, for the suite.
 * @param {Function} [o.cancel]   (handle) => void, for the suite.
 */
export function createCoach({ prefs = null, toasts = null, logger = null,
  now = null, schedule = null, cancel = null } = {}) {
  const clock = typeof now === 'function' ? now : () => Date.now();
  const setLater = typeof schedule === 'function'
    ? schedule
    : (fn, ms) => { try { return setTimeout(fn, ms); } catch (_e) { return 0; } };
  const clearLater = typeof cancel === 'function'
    ? cancel
    : (h) => { try { clearTimeout(h); } catch (_e) { /* gone */ } };

  /* THE PAGE-LOCAL LEDGER. A second copy of the spent set, kept because a page
   * with no store at all (private mode, the node harness) still owes the "once"
   * half of "once ever" — without it a mechanic that fires twice a minute would
   * coach twice a minute. With a store it simply agrees with it. */
  const spentHere = Object.create(null);

  const queue = [];
  let drainAt = 0;
  let timer = 0;
  let disposed = false;

  function readSeen() {
    try {
      if (prefs && typeof prefs.get === 'function') {
        const v = prefs.get(COACH_PREF_SEEN);
        if (v && typeof v === 'object') return v;
      }
    } catch (_e) { /* a corrupt store coaches rather than throws */ }
    return null;
  }

  function readEnabled() {
    try {
      if (prefs && typeof prefs.get === 'function') return prefs.get(COACH_PREF_ENABLED) !== false;
    } catch (_e) { /* fall through */ }
    return true;
  }

  function markSpent(id) {
    spentHere[id] = true;
    try {
      if (prefs && typeof prefs.set === 'function') {
        const next = withSeen(readSeen(), id);
        prefs.set(COACH_PREF_SEEN, next);
      }
    } catch (e) {
      try { logger?.warn?.('[GG coach] could not record "' + id + '": ' + ((e && e.message) || e)); }
      catch (_e) { /* a logger is never load-bearing */ }
    }
  }

  function show(entry) {
    if (!entry) return;
    try {
      if (toasts && typeof toasts.show === 'function') {
        toasts.show(entry.text, { kind: 'info', icon: COACH_ICON, ms: COACH_TOAST_MS });
      }
    } catch (_e) { /* a coached line must never break the desk */ }
  }

  function drain() {
    timer = 0;
    if (disposed) return;
    const entry = queue.shift();
    if (!entry) return;
    show(entry);
    drainAt = clock() + COACH_GAP_MS;
    if (queue.length) timer = setLater(drain, COACH_GAP_MS);
  }

  function pump() {
    if (disposed || timer || !queue.length) return;
    const wait = Math.max(0, drainAt - clock());
    if (wait <= 0) { drain(); return; }
    timer = setLater(drain, wait);
  }

  const api = {
    /** The master switch, live — the drawer can flip it mid-match. */
    get enabled() { return readEnabled(); },
    /** Has this line already been spent, on this page or on this machine? */
    seen(id) { return !!spentHere[id] || hasSeen(readSeen(), id); },

    /**
     * Offer one hint. Returns true when it was accepted (i.e. queued and marked
     * spent), false when the gate refused it — so a caller can tell "I coached"
     * from "already coached" without reaching into the store.
     *
     * Callers pass the TEXT, not a key: the copy lives in ui/strings.js S.coach
     * and several lines are interpolators (a label, a key number) that only the
     * call site holds the arguments for.
     *
     * @param {string} id   one of COACH
     * @param {string} text the finished sentence
     */
    fire(id, text) {
      if (disposed) return false;
      if (spentHere[id]) return false;
      if (!shouldFire(id, { enabled: readEnabled(), seen: readSeen() })) return false;
      // SPENT FIRST, SHOWN SECOND. See the banner: a queue drop, a teardown or a
      // missing toast layer must all leave the hint used up.
      markSpent(id);
      const line = String(text || '');
      if (!line) return false;
      if (queue.length >= COACH_QUEUE_MAX) return false;
      queue.push({ id, text: line });
      pump();
      return true;
    },

    /** The options drawer's setter, so callers never spell the pref key. */
    setEnabled(on) {
      try { prefs?.set?.(COACH_PREF_ENABLED, !!on); } catch (_e) { /* no store, no problem */ }
      // Switching OFF empties what has not been said yet. A hint that arrives
      // after the toggle would read as the switch not working.
      if (!on) queue.length = 0;
    },

    /**
     * Drop everything still queued, keeping every mark. The desk calls this when
     * a HUD comes down: a match is over, so a line explaining a mechanic of it
     * arriving over the recap is a leftover, not coaching. The hints stay SPENT
     * — they were offered, the match simply ended first, and re-offering them
     * next duel is the "never re-show" rule broken by a technicality.
     */
    clearPending() {
      queue.length = 0;
      if (timer) { clearLater(timer); timer = 0; }
    },

    /** Test/dev hook: forget everything, on this page and in the store. */
    reset() {
      for (const k of Object.keys(spentHere)) delete spentHere[k];
      queue.length = 0;
      try { prefs?.set?.(COACH_PREF_SEEN, {}); } catch (_e) { /* ignore */ }
    },

    /** Queued-but-unsaid count. The suite reads it; nothing else should. */
    get pending() { return queue.length; },

    dispose() {
      if (disposed) return;
      disposed = true;
      queue.length = 0;
      if (timer) { clearLater(timer); timer = 0; }
    },
  };
  return api;
}

export default createCoach;
