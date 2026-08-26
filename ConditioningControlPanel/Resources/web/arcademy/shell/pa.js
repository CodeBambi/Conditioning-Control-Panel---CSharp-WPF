/* ============================================================================
 * shell/pa.js - THE PUBLIC ADDRESS SYSTEM. Counter Stock prize `pa_pack`.
 *
 * The morning announcements get a voice. She mostly reads the schedule.
 * Mostly.
 *
 * This module is a SCHEDULER, not a speaker. It owns no Audio node, no
 * element, no class on any root - it owns two timers and a seeded plan, and
 * everything it wants to be heard leaves through the one audio door as an
 * `arcademy-sfx` request on `document` (trap 18). That is the whole of it.
 *
 * ---------------------------------------------------------------------------
 * THE LAWS (read these before touching the schedule)
 * ---------------------------------------------------------------------------
 *  1. AT MOST TWO LINES A SESSION. One shortly after the campus reveals
 *     itself, one after a class lets out. A session is one campus mount; the
 *     count resets on the next reveal and NEVER carries across.
 *  2. NEVER DURING A CLASS. `classStart` cancels anything in flight. A line
 *     that was cancelled does not come back - the moment passed, and the PA
 *     talking over a game is worse than the PA saying nothing.
 *  3. NEVER UNDER LITE. The lite rung is a cap the shell hands down and it is
 *     re-read at fire time, not at build time, because a class can leave one
 *     behind.
 *  4. REDUCED MOTION GETS ONE LINE, not two. Motion is not the objection here;
 *     the objection is being startled twice, and a player who asked the school
 *     to calm down has asked once for everything.
 *  5. THE PLAN IS SEEDED, NOT ROLLED LIVE. Same UTC day + same session index =
 *     same two lines in the same order at the same offsets. A reload inside a
 *     sitting replays the sitting; a new night is a new tape.
 *  6. CLOSING LINES ARE NOT IN ROTATION. The shell has no closing moment yet.
 *     Six of the 36 samples are written for one and they stay on the shelf
 *     until there is a door for them - see CLOSING below.
 *
 * ---------------------------------------------------------------------------
 * THE PUBLIC SURFACE
 * ---------------------------------------------------------------------------
 *   createPa({ owned, sfx, t, log, lite, reduced, daySeed }) -> pa
 *
 *     owned    ()=>bool | bool   does the player own `pa_pack`? READ AT FIRE
 *                                TIME, so a mid-session purchase speaks tonight
 *                                and a lapse goes quiet without a rebuild.
 *     sfx      (name, level, extra)  optional. Omit it and the module dispatches
 *                                the `arcademy-sfx` CustomEvent itself, in the
 *                                exact defensive shape every other shell module
 *                                uses. Supplying it is for tests and for a host
 *                                that already owns a cue helper.
 *     t        (key, fallback)=>string   the lexicon. PA is AUDIO ONLY by
 *                                design - there is no caption surface and
 *                                inventing one would be a new render surface
 *                                (trap 36). `t` is held for `pa.caption()`,
 *                                which answers '' unless a host ships a key.
 *                                No lexicon key is required by this file.
 *     log      (msg)=>void       the shell's `say`.
 *     lite     bool | ()=>bool   the performance cap.
 *     reduced  bool | ()=>bool   prefers-reduced-motion.
 *     inClass  bool | ()=>bool   optional SECOND opinion on law 2. The shell
 *                                knows whether a class is up (`!!active`) and
 *                                pa.js must never guess from a screen, so if it
 *                                is handed that answer it asks it as well as its
 *                                own notify-fed flag. Either one saying yes is a
 *                                no.
 *     daySeed  string            the UTC day seed the shell already computes.
 *
 *   pa.notify(kind)   'campusReveal' | 'classStart' | 'classEnd' (also spelled
 *                     'classEnded', which is the shell's teardown funnel's
 *                     word) | 'campusUnmount'. Unknown kinds are ignored.
 *   pa.plan()         a copy of tonight's two-entry plan.
 *   pa.spoken()       how many lines this session has actually sounded.
 *   pa.caption(name)  reserved text seam; '' unless the host ships the key.
 *   pa.debug()        {session, spoken, inClass, pending, plan, last}
 *   pa.destroy()      cancels the timers and forgets the plan.
 *
 * ---------------------------------------------------------------------------
 * THE SAMPLES
 * ---------------------------------------------------------------------------
 * 36 lines, `pa_01` .. `pa_36`, registered in `shell/audio.js` SAMPLES as FLAT
 * files in `assets/sfx/` (the host's `BuildSfxSamples` scans TopDirectoryOnly,
 * so `assets/sfx/pa/` would never be seen). All 36 are SAMPLE_ONLY - a missing
 * file is silence, never the 660Hz unknown-cue blip (trap 115) - and all 36 are
 * NEVER_BUFFERED, because 36 spoken lines decoded to PCM is tens of megabytes
 * held for at most two plays a night.
 *
 * The four categories below are a PARTITION of the 36 and the numbering is the
 * contract with whoever records them. Move a line between categories only by
 * moving it here; never by renaming a file.
 * ========================================================================= */

import { makeTaggedRoll } from '../core/rng.js';

/* ----------------------------------------------------------------------------
 * THE LINE BOOK
 * -------------------------------------------------------------------------- */

/** How many PA samples exist. Must agree with `PA_COUNT` in shell/audio.js -
 *  that file registers the rows, this one decides when they are asked for. */
export const PA_COUNT = 36;

/** `1` -> `'pa_01'`. Same recipe as audio.js `paName`, deliberately duplicated
 *  rather than imported: pa.js must stay importable without dragging the whole
 *  mixer into a bare-node test. */
export function paName(n) {
  const i = Math.max(1, Math.min(PA_COUNT, Math.round(Number(n) || 0)));
  return 'pa_' + String(i).padStart(2, '0');
}

function range(from, to) {
  const out = [];
  for (let i = from; i <= to; i += 1) out.push(paName(i));
  return Object.freeze(out);
}

/** Doors opening. Good morning, roll call, the day's shape. Early lines. */
export const ARRIVAL = range(1, 8);
/** The schedule proper - rooms, periods, reminders, the notice board. */
export const SCHEDULE = range(9, 20);
/** The "mostly" half. She reads the schedule, and then she reads you. */
export const ASIDE = range(21, 30);
/** Lights out, last bell, go home. NOT IN ROTATION - law 6. */
export const CLOSING = range(31, 36);

export const CATEGORIES = Object.freeze({
  arrival: ARRIVAL,
  schedule: SCHEDULE,
  aside: ASIDE,
  closing: CLOSING,
});

/** Which shelf a line sits on, or `null` for a name that is not a PA line. */
export function categoryOf(name) {
  const n = String(name == null ? '' : name);
  for (const k of Object.keys(CATEGORIES)) {
    if (CATEGORIES[k].indexOf(n) !== -1) return k;
  }
  return null;
}

/* ----------------------------------------------------------------------------
 * THE DIALS
 * -------------------------------------------------------------------------- */

/** Law 1. Two, and reduced motion takes one (law 4). */
export const MAX_LINES_PER_SESSION = 2;

/** How long after the campus reveal the first line opens. Long enough that the
 *  reveal's own audio has finished landing and the player is looking around. */
export const REVEAL_MS = 6000;
export const REVEAL_JITTER_MS = 3000;

/** How long after a class lets out the second line opens. Short - the player is
 *  standing in the corridor with nothing to do, which is the whole point. */
export const CLASS_END_MS = 2600;
export const CLASS_END_JITTER_MS = 1800;

/** The voice bus, half level, and the longest a line may run. audio.js caps an
 *  element clip at CLIP_MAX_MS (1200ms) unless the cue ASKS for more, and it
 *  will sell up to CLIP_REQ_MAX_MS (8000ms). Every spoken line must ask, or it
 *  is cut off after a second and a fifth. */
export const LINE_LEVEL = 0.5;
export const LINE_MAX_MS = 8000;
/** She talks over the school, not under it. `voice` pulls fx/music/drops down. */
export const LINE_DUCK = Object.freeze({ target: 'voice', mult: 0.45, ms: 3600 });

/** Category weights per slot. "Arrival lines early" is law, the rest is taste:
 *  the corridor after a class is where she stops reading the schedule. */
export const SLOT_WEIGHTS = Object.freeze({
  reveal: Object.freeze([['arrival', 7], ['schedule', 3]]),
  classEnd: Object.freeze([['schedule', 6], ['aside', 4]]),
});

/* ----------------------------------------------------------------------------
 * THE PLAN - pure, seeded, and the whole of the decision-making
 * -------------------------------------------------------------------------- */

/** Weighted choice over `[[key, weight], ...]` with one supplied 0..1 roll. */
export function pickWeighted(pairs, r) {
  const list = (pairs || []).filter((p) => p && Number(p[1]) > 0);
  if (!list.length) return null;
  let total = 0;
  for (const p of list) total += Number(p[1]);
  const x = (r == null ? 0 : (r < 0 ? 0 : r > 0.999999 ? 0.999999 : r)) * total;
  let seen = 0;
  for (const p of list) {
    seen += Number(p[1]);
    if (x < seen) return p[0];
  }
  return list[list.length - 1][0];
}

function pickFrom(list, r) {
  if (!list || !list.length) return null;
  const x = (r == null ? 0 : (r < 0 ? 0 : r > 0.999999 ? 0.999999 : r));
  return list[Math.floor(x * list.length)];
}

/**
 * Tonight's two lines. PURE: same `(daySeed, session)` in, same array out,
 * forever. Nothing here reads a clock, a store or `Math.random`.
 *
 * @param {string|number} daySeed  the shell's UTC day seed
 * @param {number} session         which campus mount this is, 1-based
 * @returns {Array<{slot:string, name:string, category:string, delayMs:number}>}
 *          exactly MAX_LINES_PER_SESSION entries, distinct names, in the order
 *          they may be spoken. The runtime may speak fewer; it never speaks
 *          more and never reorders.
 */
export function planLines(daySeed, session) {
  const s = Math.max(1, Math.round(Number(session) || 1));
  const roll = makeTaggedRoll(String(daySeed == null ? '' : daySeed) + '|pa|' + s);

  const catA = pickWeighted(SLOT_WEIGHTS.reveal, roll('cat-a')) || 'arrival';
  const first = pickFrom(CATEGORIES[catA] || ARRIVAL, roll('line-a'));

  const catB = pickWeighted(SLOT_WEIGHTS.classEnd, roll('cat-b')) || 'schedule';
  /* A session never says the same line twice, so the second draw is over the
   * shelf MINUS whatever the first took. The filter can only ever remove one
   * entry, and both shelves are long enough that it can never empty. */
  const poolB = (CATEGORIES[catB] || SCHEDULE).filter((n) => n !== first);
  const second = pickFrom(poolB, roll('line-b'));

  return [
    {
      slot: 'reveal',
      name: first,
      category: catA,
      delayMs: REVEAL_MS + Math.floor(roll('gap-a') * REVEAL_JITTER_MS),
    },
    {
      slot: 'classEnd',
      name: second,
      category: catB,
      delayMs: CLASS_END_MS + Math.floor(roll('gap-b') * CLASS_END_JITTER_MS),
    },
  ];
}

/* ----------------------------------------------------------------------------
 * PLUMBING - guarded for the node DOM double, every one of them
 * -------------------------------------------------------------------------- */

function doc() { return (typeof document !== 'undefined' && document) ? document : null; }

/** A cap that may be a value or a getter. Read it EVERY time; never cache. */
function flag(v) {
  if (typeof v === 'function') { try { return v() === true; } catch (e) { return false; } }
  return v === true;
}

/** ONE AUDIO DOOR (trap 18): a cue is a REQUEST on `document`, never a node. */
function cue(name, level, extra) {
  try {
    const d = doc();
    if (!d || typeof d.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    d.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign({ name: String(name), level: Number(level) || 0.5, bus: 'voice' }, extra || {}),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

function later(fn, ms) {
  if (typeof setTimeout !== 'function') return 0;
  try { return setTimeout(fn, Math.max(0, Number(ms) || 0)); } catch (e) { return 0; }
}
function cancel(id) { try { if (id && typeof clearTimeout === 'function') clearTimeout(id); } catch (e) { /* noop */ } }

/* ----------------------------------------------------------------------------
 * THE ANNOUNCER
 * -------------------------------------------------------------------------- */

/**
 * Build the PA. Always returns a handle - ownership is asked at fire time, not
 * at build time, so the shell may build this once and never think about it
 * again while the player buys, lapses and re-buys the pack.
 *
 * @param {{owned?:(boolean|Function), sfx?:Function, t?:Function, log?:Function,
 *          lite?:(boolean|Function), reduced?:(boolean|Function),
 *          daySeed?:(string|number)}=} caps
 */
export function createPa(caps) {
  const c = caps || {};
  const say = (typeof c.log === 'function') ? c.log : function () {};
  const tr = (typeof c.t === 'function') ? c.t : null;
  const fire = (typeof c.sfx === 'function') ? c.sfx : cue;
  const daySeed = c.daySeed == null ? '' : String(c.daySeed);

  let session = 0;        // which campus mount we are inside; 0 = none yet
  let plan = [];          // tonight's two entries for THIS session
  let used = [false, false]; // has each slot had its chance? (spoken OR passed)
  let spoken = 0;         // how many actually sounded
  let inClass = false;
  let timer = 0;
  let pending = null;     // the slot name the timer is holding, or null
  let last = null;        // {name, slot, at} of the last line that sounded
  let dead = false;

  function ownedNow() { return flag(c.owned); }
  function liteNow() { return flag(c.lite); }
  function reducedNow() { return flag(c.reduced); }
  /** Law 2, both opinions. `inClass` (the notify-fed flag) and the shell's own
   *  `active`, if it was handed over. Either one is enough to stay quiet. */
  function classNow() { return inClass || (c.inClass !== undefined && flag(c.inClass)); }

  /** Law 4 lives here and nowhere else. */
  function capNow() { return reducedNow() ? 1 : MAX_LINES_PER_SESSION; }

  function clearTimer() { cancel(timer); timer = 0; pending = null; }

  function speak(i) {
    const entry = plan[i];
    timer = 0; pending = null;
    if (dead || !entry) return;
    used[i] = true;
    /* Every gate is re-asked at the moment of speaking. Between the plan and
     * now the player may have started a class, dropped to lite, or stopped
     * owning the pack, and each of those is a reason to stay quiet. */
    if (!ownedNow()) return;
    if (liteNow()) return;
    if (classNow()) return;
    if (spoken >= capNow()) return;
    spoken += 1;
    last = { name: entry.name, slot: entry.slot, at: spoken };
    try {
      fire(entry.name, LINE_LEVEL, { bus: 'voice', maxMs: LINE_MAX_MS, duck: LINE_DUCK });
    } catch (e) { /* the mixer's problem, never the scheduler's */ }
    try { say('[pa] ' + entry.slot + ' -> ' + entry.name + ' (' + entry.category + ')'); }
    catch (e) { /* noop */ }
  }

  function arm(i) {
    const entry = plan[i];
    if (dead || !entry || used[i]) return;
    /* Nothing is armed for a player who cannot hear it. Ownership is asked
     * again in speak(), so a purchase mid-timer still lands; these early exits
     * only keep a timer off the pile. They do not mark the slot used - arm()
     * is only ever called from notify(), so an un-armed slot is already a slot
     * that will not fire, and pretending otherwise would lie to debug(). */
    if (liteNow()) return;
    if (spoken >= capNow()) return;
    clearTimer();
    pending = entry.slot;
    timer = later(() => speak(i), entry.delayMs);
  }

  /**
   * The shell's ear. Four moments, and the PA has no other way to learn
   * anything - it never polls, never listens on `document`, never reads a store.
   * @param {string} kind
   */
  function notify(kind) {
    if (dead) return;
    const k = String(kind == null ? '' : kind);

    if (k === 'campusReveal') {
      session += 1;
      spoken = 0;
      used = [false, false];
      inClass = false;
      plan = planLines(daySeed, session);
      arm(0);
      return;
    }

    if (k === 'classStart') {
      inClass = true;
      /* Law 2. A line caught in flight is SPENT, not deferred - the moment it
       * belonged to is over. Retire its slot before the timer dies so a later
       * classEnd cannot resurrect it. */
      if (pending !== null) {
        const i = plan.findIndex((e) => e && e.slot === pending);
        if (i >= 0) used[i] = true;
      }
      clearTimer();
      return;
    }

    /* `classEnded` is the shell's word for the teardown funnel EVERY leave path
     * ends in - finished, abandoned, Esc - and that funnel is exactly the moment
     * this wants, so both spellings mean the same thing here. */
    if (k === 'classEnd' || k === 'classEnded') {
      inClass = false;
      arm(1);
      return;
    }

    if (k === 'campusUnmount') {
      inClass = false;
      clearTimer();
      return;
    }
    /* anything else: ignored, on purpose */
  }

  return {
    notify,
    plan: () => plan.map((e) => Object.assign({}, e)),
    spoken: () => spoken,
    /** Reserved text seam. Audio-only by design; '' unless a host ships a key. */
    caption: (name) => (tr ? String(tr('pa_line_' + String(name || ''), '') || '') : ''),
    debug: () => ({
      session,
      spoken,
      inClass: classNow(),
      pending,
      cap: capNow(),
      owned: ownedNow(),
      plan: plan.map((e) => Object.assign({}, e)),
      last: last ? Object.assign({}, last) : null,
    }),
    destroy: () => {
      dead = true;
      clearTimer();
      plan = [];
    },
  };
}

export default createPa;
