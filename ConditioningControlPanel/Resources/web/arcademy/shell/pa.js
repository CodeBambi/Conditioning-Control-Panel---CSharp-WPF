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
 *     t        (key, fallback)=>string   the lexicon. THE PA HAS A CAPTION NOW
 *                                (owner, 2026-08-28), and the thirty-six lines
 *                                live in `core/lexicon.js` DEFAULT_LEXICON as
 *                                `pa_line_01`..`pa_line_36`, in FILE order. This
 *                                file still renders NOTHING - it hands the text
 *                                to whoever asks (`pa.caption(name)`) and seeds
 *                                it onto the cue it already fires;
 *                                `shell/pacaption.js` owns the surface, which is
 *                                what keeps trap 36 satisfied.
 *     log      (msg)=>void       the shell's `say`.
 *     lite     bool | ()=>bool   the performance cap.
 *     reduced  bool | ()=>bool   prefers-reduced-motion.
 *     inClass  bool | ()=>bool   optional SECOND opinion on law 2. The shell
 *                                knows whether a class is up (`!!active`) and
 *                                pa.js must never guess from a screen, so if it
 *                                is handed that answer it asks it as well as its
 *                                own notify-fed flag. Either one saying yes is a
 *                                no.
 *     duckDepth number | ()=>number  the player's `caps.duckDepth`, 0..1. Scales
 *                                LINE_DUCK the way engine/index.js scales every
 *                                other duck, so the PA is not the one cue in the
 *                                school that ignores the cap. Absent = 1
 *                                (uncapped), which is caps.js's own default.
 *     daySeed  string            the UTC day seed the shell already computes.
 *
 *   pa.notify(kind)   'campusReveal' | 'classStart' | 'classEnd' (also spelled
 *                     'classEnded', which is the shell's teardown funnel's
 *                     word) | 'campusUnmount'. Unknown kinds are ignored.
 *   pa.plan()         a copy of tonight's two-entry plan.
 *   pa.spoken()       how many lines this session has actually sounded.
 *   pa.caption(name)  the line's TEXT ('' for a name that is not one of the 36).
 *   pa.sayNow(reason) one line right now, OUTSIDE the two-a-session cap. Also
 *                     reachable as the document event `arcademy-pa-request`
 *                     with `detail.reason`, which is how the prize counter asks
 *                     after a `pa_pack` purchase. Every other law still holds.
 *   pa.debug()        {session, spoken, requests, inClass, pending, plan, last}
 *   pa.destroy()      cancels the timers, drops the ear, forgets the plan.
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

/** THE CAPTION KEY for a line: `pa_03` (or `3`, or `'03'`) -> `pa_line_03`.
 *  `null` for anything that is not one of the thirty-six, which is what stops
 *  the caption surface answering a bell or a page turn with `humanize()`'s
 *  best guess at a key it has never heard of.
 *  THE NUMBER IS THE FILE'S, NOT THE SCRIPT'S. See THE FILES AS SHIPPED above:
 *  the script's Closing block lives at 31-36 on disk and its "Mostly" block at
 *  25-30, and `core/lexicon.js` carries the strings in FILE order so that this
 *  function is the only place the two numberings ever have to meet - and it
 *  does not have to know about the swap at all, because the lexicon already
 *  did. Verified by `pa.test.mjs`. */
export function captionKey(name) {
  const s = String(name == null ? '' : name).trim();
  const m = /^(?:pa_)?(\d{1,2})$/.exec(s);
  if (!m) return null;
  const n = Number(m[1]);
  if (!(n >= 1 && n <= PA_COUNT)) return null;
  return 'pa_line_' + String(n).padStart(2, '0');
}

function range(from, to) {
  const out = [];
  for (let i = from; i <= to; i += 1) out.push(paName(i));
  return Object.freeze(out);
}

/* THE FILES AS SHIPPED (round 3, 2026-08-27, voice Jessica). The script was
 * written in six groups and the shelves below are four, so the numbers on
 * disk are the script's numbers with ONE swap: the script's Closing (its
 * 25-30) sits at 31-36 here, on the shelf law 6 keeps out of rotation, and
 * its "Mostly the schedule, mostly" (its 31-36) sits at 25-30, on the ASIDE
 * shelf where it belongs. Everything else is one to one:
 *   01-06 Arrival        07-14 Class calls     15-18 Payday nights
 *   19-24 Streaks/grades 25-30 Mostly          31-36 Closing (shelved)
 * Fourteen of the thirty-six carry a seed; which ones is not this file's
 * business and is not written down anywhere the page ships. */

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

/** The voice bus, FULL level, and the longest a line may run. audio.js caps an
 *  element clip at CLIP_MAX_MS (1200ms) unless the cue ASKS for more, and it
 *  sells a `pa_NN` name up to PA_REQ_MAX_MS (12000ms; everything else stops at
 *  8000). Every spoken line must ask, or it is cut off after a second and a
 *  fifth. The longest line on disk runs 10.9s under the tannoy's tail.
 *
 *  WHY 1.0 (owner, 2026-08-28: "the announcer should be higher in volume").
 *  Half level is 0.707 through the mixer's sqrt curve, i.e. three decibels
 *  given away for nothing on the ONE cue in the school that is a person
 *  speaking. It buys back three of the eleven the recordings were made under;
 *  the other eight are `PA_MAKEUP` in audio.js, because they are a fact about
 *  the FILES and belong next to the files. This number is a fact about the
 *  MOMENT: she is the loudest thing in the building while she is talking. */
export const LINE_LEVEL = 1.0;
export const LINE_MAX_MS = 12000;

/** THE DUCK, and it is not the duck it was.
 *
 *  Three faults, one report ("duck everything else more, rn the soundtrack is
 *  super loud compared to the announcer"):
 *
 *   1. ONE DEPTH FOR EVERY BUS. `voice` pulls fx, music AND drops, and the old
 *      0.45 was a compromise between two different jobs - clearing a 140-second
 *      soundtrack out from under a spoken line, and not deafening the player's
 *      own click. `mults` names them separately now. The soundtrack is the one
 *      she is fighting, so the soundtrack takes the deep cut.
 *   2. IT LET GO FOUR SECONDS IN. audio.js clamped an unkeyed duck's `ms` to
 *      TWO SECONDS, silently, so the 3600 asked for here was never honoured and
 *      the music PUMPED back to full volume in the middle of a nine-second
 *      announcement. `key` makes it a HELD duck: it stays down until the cue's
 *      own `onEnded` releases it, so the room comes back when she stops talking
 *      and not one word before. `ms` is now the dead man's handle - the longest
 *      it MAY hold if the mixer never answers - which is why it is LINE_MAX_MS.
 *   3. IT DUCKED FOR LINES THAT NEVER PLAYED. A missing file is silence (the
 *      lines are SAMPLE_ONLY), and audio.js used to pull the school down for it
 *      anyway. A keyed duck is not applied to a cue that dropped.
 *
 *  These are the POLICY numbers - the depth at a full duckDepth cap. `duckFor`
 *  below scales them by the player's cap the same way engine/index.js does, so
 *  a player who has turned ducking down still gets a shallower duck and one who
 *  has turned it off still gets none. The law is the law; the PA does not get
 *  to be the one cue that ignores it. */
export const LINE_DUCK = Object.freeze({
  target: 'voice',
  key: 'pa_line',
  /** fx and drops: out of her way, still audible. A click is feedback. */
  mult: 0.35,
  mults: Object.freeze({ music: 0.20, fx: 0.35, drops: 0.35 }),
  ms: LINE_MAX_MS,
});

/** THE duckDepth CAP, applied the way the engine applies it (engine/index.js:
 *  `1 - (1 - policy) * clamp01(duckDepth)`). Depth 1 gives the policy exactly,
 *  depth 0 gives no duck at all, and everything between is a straight line
 *  between them. Returns a NEW spec; LINE_DUCK is frozen and stays the policy.
 *  @param {number} depth 0..1, the player's cap. Junk reads as 1 (uncapped).
 *  @returns {{target:string, key:string, mult:number, mults:Object, ms:number}} */
export function duckFor(depth) {
  const d = Number.isFinite(+depth) ? Math.max(0, Math.min(1, +depth)) : 1;
  const scale = (p) => 1 - (1 - Math.max(0, Math.min(1, Number(p) || 0))) * d;
  const mults = {};
  for (const b of Object.keys(LINE_DUCK.mults)) mults[b] = scale(LINE_DUCK.mults[b]);
  return {
    target: LINE_DUCK.target,
    key: LINE_DUCK.key,
    mult: scale(LINE_DUCK.mult),
    mults,
    ms: LINE_DUCK.ms,
  };
}

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
  let cues = 0;           // every cue this handle has ever fired, numbered
  let dead = false;

  function ownedNow() { return flag(c.owned); }
  function liteNow() { return flag(c.lite); }
  function reducedNow() { return flag(c.reduced); }
  /** Law 2, both opinions. `inClass` (the notify-fed flag) and the shell's own
   *  `active`, if it was handed over. Either one is enough to stay quiet. */
  function classNow() { return inClass || (c.inClass !== undefined && flag(c.inClass)); }

  /** Law 4 lives here and nowhere else. */
  function capNow() { return reducedNow() ? 1 : MAX_LINES_PER_SESSION; }

  /** The player's duckDepth cap, read AT FIRE TIME like every other gate. A
   *  host that hands over nothing is uncapped (1), which is the caps.js default
   *  and the shape of the harness. */
  function depthNow() {
    const v = (typeof c.duckDepth === 'function') ? (() => {
      try { return c.duckDepth(); } catch (e) { return 1; }
    })() : c.duckDepth;
    return Number.isFinite(+v) ? Math.max(0, Math.min(1, +v)) : 1;
  }

  function clearTimer() { cancel(timer); timer = 0; pending = null; }

  /** ONE LINE, OUT LOUD, NOW. Every road that speaks goes through here, so the
   *  cue's shape - level, ceiling, duck, and the caption's own seed - is
   *  written once. The GATES are the callers' business: `speak()` re-asks all
   *  five, `sayNow()` re-asks four and skips the session cap on purpose. */
  function utter(name, why) {
    last = { name, slot: why, at: spoken };
    speakingUntil = nowMs() + LINE_MAX_MS;
    cues += 1;
    const cueId = cues;
    try {
      fire(name, LINE_LEVEL, {
        bus: 'voice',
        maxMs: LINE_MAX_MS,
        duck: duckFor(depthNow()),
        /* THE CAPTION'S SEED. shell/pacaption.js listens on the SAME cue every
         * line already sends (contract: `arcademy-sfx` with a `pa_NN` name) and
         * needs no other channel; this only saves it a second lookup and lets a
         * host with no lexicon at all still be told there is nothing to show.
         * A consumer that has never heard of captions ignores an extra field,
         * which is the whole reason it can ride here. */
        caption: captionFor(name),
        /* THE CUE'S OWN NUMBER. It exists for one reason and it is a hard one:
         * audio.js's consumer is installed at BOOT and the caption's at SHELL
         * BUILD, so on a shared `document` listener list the mixer always runs
         * first - and a cue it drops answers 'dropped' SYNCHRONOUSLY, inside
         * the dispatch, before the caption listener has even been reached. The
         * end therefore arrives before the beginning, and a purely
         * chronological caption would put a box on screen for a line that was
         * already dead. The id lets the caption recognise the ending it has
         * already been told about, deterministically, with no timing window. */
        cueId,
        /* THE END OF THE LINE, re-broadcast. audio.js answers this exactly once
         * on every road out of a clip ('ended', the maxMs governor, a re-fire,
         * the voice cap, teardown, a file that will not load), and it is the
         * ONLY thing in the page that knows how long a given announcement
         * actually was. pa.js does not consume it - it turns it back into a
         * document event, because the caption surface is not the mixer's
         * business and pa.js may hold a handle on neither of them. */
        onEnded: (reason) => {
          speakingUntil = 0;
          const d2 = doc();
          if (!d2 || typeof d2.dispatchEvent !== 'function') return;
          const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
          if (!Ctor) return;
          try {
            d2.dispatchEvent(new Ctor('arcademy-pa-ended', {
              detail: { name, cueId, reason: String(reason || 'ended') },
            }));
          } catch (e2) { /* an ending must never be the thing that throws */ }
        },
      });
    } catch (e) { /* the mixer's problem, never the scheduler's */ }
    try { say('[pa] ' + why + ' -> ' + name); }
    catch (e) { /* noop */ }
  }

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
    utter(entry.name, entry.slot + ' (' + entry.category + ')');
  }

  /* --------------------------------------------------------------------------
   * THE ONE LINE THAT IS NOT ON THE PLAN (`arcademy-pa-request`, 2026-08-28)
   *
   * The player has just BOUGHT the pack at the counter. Law 1 says two lines a
   * session and the reveal's slot is long spent by then, so the thing they paid
   * three hundred tickets for would say nothing at all until the next campus
   * mount - which is the one moment it most needs to speak. This is the door
   * for that, and it is deliberately narrow:
   *
   *   - IT DOES NOT COUNT. `spoken` is not incremented and the session cap is
   *     not consulted, because the cap exists to stop the school NAGGING and a
   *     line the player just bought is not nagging. It also cannot spend one of
   *     tonight's two: the plan is untouched, so both scheduled lines still get
   *     their turn.
   *   - EVERY OTHER LAW STILL HOLDS. Not under lite (law 3), not during a class
   *     (law 2, both opinions), not without the pack (a preview of something you
   *     do not own is a lie), and never while a line is already in the air.
   *   - IT IS SEEDED, NOT ROLLED. Same day, same session, same request index =
   *     the same line, so a reload does not re-roll the purchase moment. Draws
   *     from arrival or schedule - the shelves that read as the school
   *     acknowledging you, never a closing line and never an aside.
   * ------------------------------------------------------------------------ */

  /** How many requests this session has honoured. Seeds the roll; not a cap. */
  let requests = 0;
  /** Rough "she is still talking" guard - the only state pa.js keeps about the
   *  mixer, and it is a clock, not a handle. */
  let speakingUntil = 0;

  function nowMs() { try { return Date.now(); } catch (e) { return 0; } }

  /**
   * @param {string=} reason 'purchase' | 'preview' (log only; the line is the same)
   * @returns {string|null} the name spoken, or null if a law said no
   */
  function sayNow(reason) {
    if (dead) return null;
    if (!ownedNow()) return null;
    if (liteNow()) return null;
    if (classNow()) return null;
    // Two announcements at once is a fault in the building, not a feature.
    if (nowMs() < speakingUntil) return null;
    requests += 1;
    const roll = makeTaggedRoll(daySeed + '|pa-req|' + session + '|' + requests);
    const cat = pickWeighted(SLOT_WEIGHTS.reveal, roll('cat')) || 'arrival';
    const name = pickFrom(CATEGORIES[cat] || ARRIVAL, roll('line'));
    if (!name) return null;
    utter(name, 'request:' + String(reason || 'preview'));
    return name;
  }

  /** The document ear for `arcademy-pa-request`. The ONLY thing pa.js listens
   *  to; everything else still arrives through `notify()` (the module is a
   *  scheduler and the shell is its clock). Detached by `destroy()`. */
  function onRequest(e) {
    const detail = (e && e.detail) || {};
    try { sayNow(detail.reason); } catch (err) { /* an ear must never throw */ }
  }
  try {
    const d0 = doc();
    if (d0 && typeof d0.addEventListener === 'function') {
      d0.addEventListener('arcademy-pa-request', onRequest);
    }
  } catch (e) { /* a host with no document simply never hears one */ }

  /** The line's text, or ''. `t(key)` with NO fallback on purpose: a fallback
   *  wins over DEFAULT_LEXICON in core/lexicon.js, so passing one here would
   *  hide the very rows this feature ships. A name that is not one of the
   *  thirty-six answers '' rather than letting `humanize()` invent a sentence
   *  out of the key. */
  function captionFor(name) {
    const key = captionKey(name);
    if (!key || !tr) return '';
    let s = '';
    try { s = String(tr(key) || ''); } catch (e) { return ''; }
    /* THE LAST GUARD. `t()`'s floor is `humanize(key)`, so a row that has been
     * deleted out of DEFAULT_LEXICON would come back as the sentence "Pa Line
     * 03" and be rendered as if it were an announcement. Better nothing. */
    if (/^Pa Line \d+$/.test(s)) return '';
    return s;
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
    /** The line's TEXT. `pa_03`, `3` and `'03'` all mean the same line; anything
     *  else answers ''. shell/pacaption.js renders what comes out of here. */
    caption: captionFor,
    /** Speak one line right now, outside the session cap. The
     *  `arcademy-pa-request` event is the road everything else takes; this is
     *  the same door with a handle on the inside, for a caller that already
     *  holds the handle (and for the test). */
    sayNow,
    debug: () => ({
      session,
      spoken,
      requests,
      inClass: classNow(),
      pending,
      cap: capNow(),
      owned: ownedNow(),
      duckDepth: depthNow(),
      plan: plan.map((e) => Object.assign({}, e)),
      last: last ? Object.assign({}, last) : null,
    }),
    destroy: () => {
      dead = true;
      clearTimer();
      plan = [];
      try {
        const d1 = doc();
        if (d1 && typeof d1.removeEventListener === 'function') {
          d1.removeEventListener('arcademy-pa-request', onRequest);
        }
      } catch (e) { /* noop */ }
    },
  };
}

export default createPa;
