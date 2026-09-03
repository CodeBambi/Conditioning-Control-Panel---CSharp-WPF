/* ============================================================================
 * emi/heartbeat.js - THE METRONOME: EMI is never completely idle.
 *
 * Owner, 2026-08-25: "It's awfully quiet, tho we got a lot of lines. Never be
 * completely idle: something new every 10 seconds. Idle (random cycle of
 * expressions every so often, or a nudge in a direction) - a screen animation -
 * a bark - a screen animation - it asking us something (campus only, never in
 * session) - animation again. Always do something. It has to feel alive."
 *
 * This file is that clock, and it is the ONE sanctioned unattended spender in
 * EMI. The blink's own rule still stands (`voice.js onGesture`: no beat and no
 * bark from an idle blink) precisely because this thing exists instead, and
 * carries the gates a blink cannot:
 *
 *   - `document.visibilityState`. A backgrounded tab runs NO timer at all -
 *     the interval is cleared on `visibilitychange` and restarted on the way
 *     back, with the clock re-stamped so she does not fire on the first frame
 *     of a screen you have only just looked at again.
 *   - THE GEOFENCE, unchanged and absolute. The Records Office, the annex and
 *     the lab are silent screens, and the shell already says so the one way
 *     that matters: `emi.setEnabled(false)`. `emi.enabled` is therefore the
 *     whole gate here, and no data file and no dial can open it.
 *   - IT NEVER PRE-EMPTS. If she is busy, saying, asking, travelling, pressed,
 *     dragged, docked or on a live channel at fire time, the tick is SKIPPED
 *     and nothing is queued - a reaction to a moment that has passed is worse
 *     than no reaction (widget.js's own rule, and `widget.askReady()` is the
 *     one honest answer to all of it).
 *
 * WHAT IT MEASURES. `lastEventAt` is stamped by `widget.onActivity` - every
 * visible thing EMI does (a chain, a say, a raw face, a channel, a field trip)
 * AND every player verb (a pet, a drag, a hover). So the heartbeat only ever
 * fills silence: it cannot compete with a moment, with a hand on the mouse, or
 * with itself.
 *
 * WHAT IT SPENDS. Nothing of its own. Every act goes through the engine that
 * already owns it - `emi.emote` for a face or a fidget, the DECK for a channel
 * (which keeps its cooldowns and its per-session cap), `voice.onMoment` for a
 * line (which keeps the floor, the odds, the rations and the no-repeat), and
 * `asks.offer` for a question (which keeps all five ask gates and its cadence).
 * The heartbeat decides WHEN, never WHETHER, and a refusal is normal.
 *
 * NOTHING HERE MAY THROW INTO A SCREEN TRANSITION. Every entry point is one
 * try/catch, there is exactly ONE timer plus one short release timer for the
 * gaze, and `destroy()` takes both, the visibility listener and the activity
 * subscription with it.
 * ==========================================================================*/

/* THE FIDGET TABLE IS THE VOICE'S. One import, data only: the weighted chain
 * list retuned on 2026-08-25 (trap 107) is the same list here, so a fidget
 * looks like a fidget whichever clock happened to ask for it. */
import { VOICE_DIALS } from './voice.js';

/* ---------------------- dials (designer-tunable) -------------------------
 * Every number the heartbeat has, in one frozen object, VOICE_DIALS-style. */
export const HB_DIALS = Object.freeze({
  /* --- the clock -------------------------------------------------------- */
  TICK_MS: 2500,             // the ONE interval; the period is measured, not slept
  BEAT_MS: 10000,            // campus: "something new every 10 seconds"
  CLASS_BEAT_MS: 14000,      // in class the player is looking at the board
  JITTER_MS: 2500,           // +- , re-rolled after every act (never a metronome tick)
  AFTER_BARK_MULT: 0.7,      // after a line, the next act comes sooner: it ANSWERS the line

  /* --- the starvation floor --------------------------------------------- */
  SPEAK_STARVE_MS: 75000,    // campus: this long with no words = the next act is a bark
  CLASS_STARVE_MS: 90000,    // ...and in class

  /* --- the acts --------------------------------------------------------- */
  FACE_HOLD_MIN_MS: 1200,
  FACE_HOLD_MAX_MS: 2000,
  NUDGE_HOLD_MS: 900,        // a body move wears the face she is already wearing
  GAZE_MS: 1500,             // how long a gaze nudge is held before it eases home
  TRIES_PER_ACT: 3,          // a refused kind is re-drawn (bounded), never queued

  /* THE WHEEL. Weights, not odds: the sequence law below removes kinds before
   * the draw and the remaining weights are re-normalised by the draw itself.
   * `screen` was 22 (face 22 / fidget 18 / nudge 18) until the owner played
   * the shipped heartbeat, 2026-08-25: "we need to see the animations wayy
   * more often". The deck's own cooldowns (SL_DIALS) were loosened with it -
   * a heavier wheel slot alone would mostly have drawn refusals. */
  WEIGHTS: Object.freeze({ face: 18, fidget: 14, nudge: 14, screen: 34, bark: 14, ask: 6 }),
  /* IN CLASS THERE IS NO SCREEN AND NO ASK. The deck already refuses a channel
   * while a class owns the screen and an ask is barred mid-class by its own
   * gate - the zero weights here mean she does not waste a beat finding out. */
  WEIGHTS_CLASS: Object.freeze({ face: 30, fidget: 25, nudge: 25, bark: 20, screen: 0, ask: 0 }),

  /* THE IDLE FACES. Curated from chains.js FACES - CLOSED SETS ONLY - and
   * curated for one property: every one of them is a face she could plausibly
   * be wearing while nothing at all is happening. No tears, no rage, no shock,
   * no love: those are REACTIONS and they belong to a moment. */
  IDLE_FACES: Object.freeze([
    '^_^', '._.', '0_0', '-_-', '=_=', '¬_¬', 'o_o', '^_~', '@_@',
    '(◠‿◠)', '(◕‿◕)', '(¬‿¬)', ':3', ':P', 'B)', 'ZzZ', '???', '*_*', '(◔_◔)',
  ]),
  /** The body moves a `nudge` may use (widget.css owns the keyframes). */
  NUDGE_BODIES: Object.freeze(['bounce', 'nod', 'shiver', 'droop']),
  /** Where a gaze nudge may look: eight directions, -1..1 each. */
  GAZE_DIRS: Object.freeze([
    [-1, 0], [1, 0], [0, -1], [0, 1],
    [-1, -1], [1, -1], [-1, 1], [1, 1],
  ]),
});

/** The kinds that cost WORDS. One of these is always followed by a wordless one. */
const SPOKEN = Object.freeze({ bark: true, ask: true });

/* ---------------------- small helpers ---------------------------------- */
function isObj(v) { return !!v && typeof v === 'object' && !Array.isArray(v); }
/** An injected handle may be the thing itself or a getter for it (index.js
 *  mounts this file while `asks` and `trips` are still one import away). */
function resolve(v) {
  if (typeof v === 'function') { try { return v(); } catch (e) { return null; } }
  return v || null;
}

/**
 * @param {Object} o
 * @param {Object} o.widget    the widget handle (askReady / onActivity / nudgeGaze / pulseChannel)
 * @param {Object} o.emi       the emi/index.js controller (emote / enabled)
 * @param {Object=} o.voice    emi/voice.js - the line channel and the class latch
 * @param {Object|Function=} o.asks   emi/asks.js, or a getter for it
 * @param {Object|Function=} o.trips  emi/fieldtrips.js, or a getter for it (unused today;
 *                                    taken so a later act can reach it without a re-mount)
 * @param {Function=} o.rng
 * @param {Function=} o.now
 * @param {Object=} o.dials    TEST seam: compress the clock, never retune it here
 * @param {Object=} o.doc      TEST seam: the document the visibility gate reads
 * @param {Function=} o.log
 * @returns {?Object} {start, stop, tick, destroy, state, get running} or null
 */
export function createHeartbeat(o = {}) {
  const say = typeof o.log === 'function' ? o.log : () => {};
  const widget = o.widget;
  const emi = o.emi;
  if (!widget || !emi) return null;
  if (typeof widget.askReady !== 'function' || typeof emi.emote !== 'function') return null;

  const D = Object.assign({}, HB_DIALS, isObj(o.dials) ? o.dials : {});
  const rng = typeof o.rng === 'function' ? o.rng : Math.random;
  const clock = typeof o.now === 'function' ? o.now : Date.now;
  const now = () => { const n = Number(clock()); return Number.isFinite(n) ? n : 0; };
  const doc = o.doc || (typeof document !== 'undefined' ? document : null);

  /* ---------------------- the state ------------------------------------- */
  const S = {
    /** When anything visible last happened - hers or the player's. */
    lastEventAt: now(),
    /** When she last SPOKE (the starvation clock). */
    lastSpokeAt: now(),
    /** The kind of the last act that actually landed (the sequence law). */
    lastKind: null,
    /** The face she is currently wearing, so a `nudge` does not change it. */
    face: D.IDLE_FACES[0],
    /** This beat's period, re-rolled after every act. */
    period: 0,
    acts: 0,
    skipped: 0,
    counts: Object.create(null),
  };
  S.period = rollPeriod(null);

  let timer = null;
  let unActivity = null;
  let destroyed = false;

  /* ---------------------- the clock ------------------------------------- */
  function rollPeriod(afterKind) {
    const base = inClass() ? D.CLASS_BEAT_MS : D.BEAT_MS;
    const jitter = (rng() * 2 - 1) * D.JITTER_MS;
    const mult = afterKind === 'bark' ? D.AFTER_BARK_MULT : 1;
    /* A FLOOR OF ONE SECOND, and it is a sanity rail rather than a dial: the
     * shipped numbers never come near it (the shortest possible beat is
     * (10000 - 2500) x 0.7 = 5250ms). It is deliberately NOT TICK_MS - a suite
     * that lengthens the tick to keep the interval out of its way would
     * otherwise find the PERIOD lengthened underneath it. */
    return Math.max(1000, Math.round((base + jitter) * mult));
  }

  /** No timer runs while the document is hidden. Not "a tick that returns
   *  early" - no timer: a backgrounded tab is not a place to spend a beat. */
  function pageVisible() {
    try {
      if (!doc) return true;
      if (doc.hidden === true) return false;
      if (typeof doc.visibilityState !== 'string') return true;
      return doc.visibilityState === 'visible';
    } catch (e) { return true; }
  }

  function arm() {
    if (destroyed || timer !== null) return;
    if (typeof setInterval !== 'function') return;
    timer = setInterval(() => { try { tick(); } catch (e) { /* never into a transition */ } }, D.TICK_MS);
  }
  function disarm() {
    if (timer === null) return;
    clearInterval(timer);
    timer = null;
  }

  function onVisibility() {
    try {
      if (destroyed) return;
      if (!pageVisible()) { disarm(); return; }
      /* COMING BACK IS AN EVENT. Without this re-stamp the whole absence
       * counts as silence and she performs on the first frame of a screen the
       * player has only just looked at again. */
      S.lastEventAt = now();
      arm();
    } catch (e) { /* noop */ }
  }

  /* ---------------------- eligibility ----------------------------------- */
  /** Is a class up? The voice owns the latch; with no voice, assume campus. */
  function inClass() {
    try { const v = o.voice; return !!(v && v.inClass); } catch (e) { return false; }
  }

  /**
   * THE CONJUNCTIVE GATE. Every leg has to hold. `widget.askReady()` is doing
   * most of the work on purpose - it is the file that owns the verbs answering
   * "is she free right now" once, instead of this file guessing at it twice.
   */
  function eligible() {
    try {
      if (destroyed) return false;
      if (!pageVisible()) return false;
      // THE GEOFENCE. A silent screen (Records / annex / lab) is `enabled:false`.
      if (emi.enabled === false) return false;
      if (emi.hidden === true) return false;
      // ...and the intro owns the screen until it does not.
      if (emi.introHeld === true) return false;
      /* NO LIVE VERB: no say, no chain, no press, no drag, no field trip, no
       * live channel, no strip already up, not dismissed, not disabled. */
      if (!widget.askReady()) return false;
      return true;
    } catch (e) { return false; }
  }

  /* ---------------------- the wheel ------------------------------------- */
  /**
   * THE SEQUENCE LAW (the owner's rhythm, and it is the feature):
   *  - never the same KIND twice in a row (a second face still counts as a
   *    repeat even when the face itself differs - the rhythm is about what
   *    kind of thing happened, not about which glyph);
   *  - a SPOKEN kind is always followed by at least one wordless one;
   *  - a screen is never followed directly by a screen (the first rule
   *    already says so; it is written down because it is the one people try
   *    to "optimise" away);
   *  - and if every kind has been removed, the law yields rather than the
   *    beat being lost: she makes a face.
   */
  function wheel(exclude) {
    const table = inClass() ? D.WEIGHTS_CLASS : D.WEIGHTS;
    const skip = exclude || {};
    const kinds = [];
    let total = 0;
    for (const k of Object.keys(table)) {
      const w = Number(table[k]) || 0;
      if (w <= 0) continue;
      if (skip[k]) continue;
      if (k === S.lastKind) continue;                 // no kind twice in a row
      if (SPOKEN[k] && SPOKEN[S.lastKind]) continue;  // a line is answered, not doubled
      kinds.push([k, w]);
      total += w;
    }
    if (!kinds.length) return skip.face ? null : 'face';
    let r = rng() * total;
    for (const [k, w] of kinds) { r -= w; if (r < 0) return k; }
    return kinds[kinds.length - 1][0];
  }

  /** Words have been absent long enough that the next act is forced to be one. */
  function starving() {
    const floor = inClass() ? D.CLASS_STARVE_MS : D.SPEAK_STARVE_MS;
    return (now() - spokeAt()) >= floor;
  }

  /** The last time a LINE landed, hers or anyone's - the voice's clock wins
   *  when it has one, because a bark fired by a moment is words too. */
  function spokeAt() {
    let v = 0;
    try { v = Number(o.voice && o.voice.lastSayAt) || 0; } catch (e) { v = 0; }
    return Math.max(S.lastSpokeAt, v);
  }

  /** Is the voice's bark floor clear? A forced bark that the floor would
   *  refuse is a wasted beat; ask before spending it. */
  function floorClear() {
    try {
      const v = o.voice;
      if (!v) return false;
      const last = Number(v.lastBarkAt) || 0;
      if (!last) return true;
      const dials = v.dials || {};
      const floor = inClass()
        ? (Number(dials.CLASS_BARK_FLOOR_MS) || 20000)
        : (Number(dials.BARK_FLOOR_MS) || 40000);
      return (now() - last) >= floor;
    } catch (e) { return false; }
  }

  /* ---------------------- the acts -------------------------------------- */
  function pickFace() {
    const list = D.IDLE_FACES;
    if (!list.length) return null;
    let i = Math.floor(rng() * list.length) % list.length;
    // NEVER THE SAME FACE TWICE. (The wheel already refuses face-after-face;
    // this is the other half - a repeat that arrived some other way.)
    if (list[i] === S.face && list.length > 1) i = (i + 1) % list.length;
    return list[i];
  }

  function actFace() {
    const f = pickFace();
    if (!f) return false;
    const hold = Math.round(D.FACE_HOLD_MIN_MS
      + rng() * Math.max(0, D.FACE_HOLD_MAX_MS - D.FACE_HOLD_MIN_MS));
    if (!emi.emote(f, { hold })) return false;
    S.face = f;
    return true;
  }

  /** The voice's own weighted table, so the two clocks fidget alike. */
  function pickFidget() {
    const table = (VOICE_DIALS && VOICE_DIALS.FIDGET_CHAINS) || [];
    if (!table.length) return null;
    let total = 0;
    for (const e of table) total += Number(e[0]) || 0;
    if (total <= 0) return null;
    let r = rng() * total;
    for (const e of table) { r -= Number(e[0]) || 0; if (r < 0) return e[1]; }
    return table[table.length - 1][1];
  }

  function actFidget() {
    const chain = pickFidget();
    if (!chain) return false;
    return !!emi.emote(chain);
  }

  /** A body move and a look, with NO face change - the smallest sign of life
   *  she has. The gaze half is refused outright under reduced motion by the
   *  widget; the body move is a CSS keyframe and rides the same policy the
   *  rest of them do. */
  function actNudge() {
    const bodies = D.NUDGE_BODIES;
    const body = bodies[Math.floor(rng() * bodies.length) % bodies.length];
    const okay = !!emi.emote(S.face, { hold: D.NUDGE_HOLD_MS, body });
    if (!okay) return false;
    try {
      if (typeof widget.nudgeGaze === 'function') {
        const dirs = D.GAZE_DIRS;
        const d = dirs[Math.floor(rng() * dirs.length) % dirs.length];
        widget.nudgeGaze(d[0], d[1], D.GAZE_MS);
      }
    } catch (e) { /* a lean may never break a beat */ }
    return true;
  }

  /** ONE WHEEL TICK ON THE DECK, with the player-silence floor lifted and
   *  every other deck refusal intact (takeover.js `pulse`). Never in class:
   *  the deck refuses there, and the class wheel does not offer it. */
  function actScreen() {
    if (inClass()) return false;
    try {
      if (typeof widget.pulseChannel !== 'function') return false;
      return !!widget.pulseChannel();
    } catch (e) { return false; }
  }

  /** A LINE, through the ordinary ladder. Everything that rations her words -
   *  the floor, the ceiling, the odds, the no-repeat, the doubles slot, the
   *  danger gate - is voice.js's and stays voice.js's. */
  function actBark() {
    const v = o.voice;
    if (!v || typeof v.onMoment !== 'function') return false;
    try {
      const cls = inClass();
      const p = { inClass: cls, source: 'heartbeat' };
      /* THE CLASS'S KEY, and ONLY in class. A `gameIs:` gate that could match
       * on the campus because the last class left its key lying about is a
       * per-game colour bleeding onto the quad. */
      if (cls) { const k = gameKeyNow(); if (k) p.gameKey = k; }
      return !!v.onMoment('heartbeat', p);
    } catch (e) { return false; }
  }

  /** Which class is up, per the voice's own latch. Null is normal (and null
   *  simply means the `hbClass` fallback pool answers instead of `hbClass_x`). */
  function gameKeyNow() {
    try {
      const v = o.voice;
      const k = v && v.gameKey;
      return typeof k === 'string' && k ? k : null;
    } catch (e) { return null; }
  }

  /** CAMPUS ONLY, and every ask gate still runs. The heartbeat gives the ask
   *  engine a SLOT; it does not give it permission. */
  function actAsk() {
    if (inClass()) return false;
    const asks = resolve(o.asks);
    if (!asks || typeof asks.offer !== 'function') return false;
    try { return !!asks.offer('heartbeat', {}); } catch (e) { return false; }
  }

  const ACTS = { face: actFace, fidget: actFidget, nudge: actNudge, screen: actScreen, bark: actBark, ask: actAsk };

  /* ---------------------- the tick -------------------------------------- */
  /**
   * THE ONE THING THE INTERVAL DOES. Exported as a test seam so a suite can
   * drive the clock by hand rather than sleeping through a cadence.
   * @returns {?string} the kind that landed, or null (which is most ticks)
   */
  function tick() {
    try { return runTick(); }
    catch (e) {
      /* A MASCOT MAY NEVER BREAK A SCREEN TRANSITION, and this one runs on a
       * timer nobody is watching. The interval wraps this too; the second net
       * is here so a DIRECT caller (index.js, a suite) cannot be hurt either. */
      say('emi heartbeat: tick threw (ignored) - ' + ((e && e.message) || e));
      return null;
    }
  }

  function runTick() {
    if (destroyed) return null;
    if (!eligible()) { S.skipped += 1; return null; }
    if ((now() - S.lastEventAt) < S.period) return null;

    /* STARVATION. A campus (or a class) that has had no words for long enough
     * forces the next act to be a bark - but only when the voice's own floor
     * would actually let one through, or the beat is spent on a refusal. */
    let forced = null;
    if (starving() && floorClear() && !SPOKEN[S.lastKind]) forced = 'bark';

    const tried = Object.create(null);
    let landed = null;
    for (let i = 0; i < Math.max(1, D.TRIES_PER_ACT | 0) && !landed; i++) {
      const kind = (i === 0 && forced) ? forced : wheel(tried);
      if (!kind) break;
      tried[kind] = true;
      const fn = ACTS[kind];
      if (!fn) continue;
      let okay = false;
      try { okay = !!fn(); } catch (e) { okay = false; }
      if (okay) landed = kind;
    }

    if (!landed) {
      /* NOTHING TOOK. Do not re-stamp the clock: the next tick (2.5s) tries
       * again with a different draw, which is how a cooling deck or a spent
       * ask stops being the whole beat. */
      S.skipped += 1;
      return null;
    }

    S.acts += 1;
    S.counts[landed] = (S.counts[landed] || 0) + 1;
    S.lastKind = landed;
    if (SPOKEN[landed]) S.lastSpokeAt = now();
    /* The activity tap has usually stamped this already (every act above goes
     * through a widget verb); stamping it here too is what keeps a refused-but-
     * true act - an ask that mounted a strip, say - from firing again in 2.5s. */
    S.lastEventAt = now();
    S.period = rollPeriod(landed);
    return landed;
  }

  /* ---------------------- wiring ---------------------------------------- */
  try {
    if (typeof widget.onActivity === 'function') {
      unActivity = widget.onActivity(() => { S.lastEventAt = now(); });
    }
  } catch (e) { unActivity = null; say('emi heartbeat: activity tap refused'); }

  const visHandler = () => onVisibility();
  try {
    if (doc && typeof doc.addEventListener === 'function') {
      doc.addEventListener('visibilitychange', visHandler);
    }
  } catch (e) { /* a platform with no visibility API simply always runs */ }

  return {
    /** Arm the interval. Idempotent, and a hidden page arms nothing. */
    start() {
      try {
        if (destroyed) return false;
        S.lastEventAt = now();
        if (!pageVisible()) return false;
        arm();
        return timer !== null;
      } catch (e) { return false; }
    },
    /** Take the interval down. `start()` brings it back. */
    stop() { try { disarm(); } catch (e) { /* noop */ } },
    /** TEST SEAM: one tick, by hand. */
    tick,
    /** Debug/read-only view. Nothing outside may mutate the state. */
    state() {
      return {
        running: timer !== null,
        visible: pageVisible(),
        inClass: inClass(),
        eligible: eligible(),
        lastKind: S.lastKind,
        period: S.period,
        acts: S.acts,
        skipped: S.skipped,
        sinceEvent: now() - S.lastEventAt,
        sinceSpoke: now() - spokeAt(),
        counts: Object.assign({}, S.counts),
      };
    },
    get running() { return timer !== null; },
    dials: D,
    destroy() {
      destroyed = true;
      disarm();
      try { if (unActivity) unActivity(); } catch (e) { /* noop */ }
      unActivity = null;
      try {
        if (doc && typeof doc.removeEventListener === 'function') {
          doc.removeEventListener('visibilitychange', visHandler);
        }
      } catch (e) { /* noop */ }
    },
  };
}

export default createHeartbeat;
