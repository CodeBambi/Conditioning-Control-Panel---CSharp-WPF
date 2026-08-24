/* ============================================================================
 * emi/voice.js - THE DECISION ENGINE: does EMI say anything, and what.
 *
 * `emi/moments.js` is the WORDLESS table and it still is: a moment maps to a
 * face, a chain, a body move. This file sits one step in front of it and asks a
 * different question - is this particular moment, for this particular player,
 * on this particular night, worth a LINE. Almost always the answer is no, and
 * that "almost always" IS the design: EMI mostly reacts with her face, and the
 * rarity is the whole reason a sentence lands when one arrives.
 *
 * THE ORDER OF RESOLUTION (one thing per event, never two)
 *   1. the GEOFENCE  - a refused Records / lab door is total silence, always
 *   2. the BEATS     - story.js one-shots; highest priority wins
 *   3. the BARKS     - barks.js pools, gated by odds, a floor and the rations
 *   4. the CHANNELS  - the exit flinch and the idle glitch, wordless and
 *                      engine-owned (the flinch is asked last, and only for
 *                      `exitIntent`, which boot.js fires 450ms into an Esc
 *                      hold - the app's one true door)
 *   5. nothing       - fireMoment falls through to the wordless table
 *
 * THE DATA IS SOMEBODY ELSE'S. `story.js` and `barks.js` are pure frozen data
 * with no imports; they are pulled in with a dynamic import inside a try/catch
 * (the `loadOptional` discipline shell.js uses for engine/ and provider/), so a
 * missing or broken data module costs EMI that channel and nothing else. Both
 * can also be INJECTED, which is how the suite drives this file without either
 * of them existing. An unknown field, an unknown trigger and an unknown
 * predicate are all silent: a shape this file does not understand must never
 * throw, and a gate nobody implements CLOSES the line rather than opening it.
 *
 * NOTHING HERE MAY BLOCK. A mascot may never break a screen transition, so
 * every entry point is one try/catch, every timer is tracked, and every failure
 * is a silent false. The exit flinch in particular is fire-and-forget: the door
 * is already opening while she shakes.
 *
 * ---------------------------------------------------------------------------
 * PREDICATE VOCABULARY (the spec's, plus what the two data files ask for)
 *
 * shared spec   firstSession day2 daysAtLeast:N sessionAtLeast:N sessionIs:N
 *               streakAtLeast:N gradeIs:s|a|b|c|pass perfect postLoss:MS
 *               lateNight longAbsence:DAYS petsAtLeast:N masteredAtLeast:N
 *               labSeen notLabSeen gradeUp seen:beatId notSeen:beatId
 * added here    lastSessionBad     the PREVIOUS session closed on a loss
 *               firstOfDay         this greet is the day's first board-up
 *               notFirstOfDay      ...and its inverse (alias: repeatToday)
 *               seenThisSession:id a beat that fired in THIS sitting
 *               evening            local hour >= 18
 *               streakIs:N         exact, so milestones cannot cascade
 *               masteredIs:N       exact mastered count
 *               firstHole          this stamp is hole 1 of a fresh card
 *               punchesAtLeast:N   the stamped card is now at N/10
 *               punchesTodayAtLeast:N  holes minted today, all cards
 *               cardsBelow:N       fewer than N cards enrolled
 *               deckFull           a card for every active class
 *               afterBadDay        the previous play-day's best was C or worse
 *               awayCountAtLeast:N the tabAway/suspend escalation count
 *               hoursAtLeast:N     lifetime msVisible, in hours
 *               flingsAtLeast:N / bubblesAtLeast:N   the widget's own counters
 * perception    calendarDaysAtLeast:N  calendar days since her first day (the
 *               anniversary gate; `days` only counts days PLAYED)
 *               hidesAtLeast:N     lifetime x-dismissals
 *               zoneCountAtLeast:N / zoneRowIs:top|mid|bottom   spot memory,
 *               off the dropAt payload (zone, zoneRow, zoneCount)
 *
 * FREQUENCY FIELDS honoured on a pool or a line: odds, ceremony, priority,
 * noRepeat, cooldownMs, maxPerSession, maxPerClass, oncePerStreak, onceEver,
 * oncePerGamePerDay. Anything else on a pool is ignored, silently.
 * ==========================================================================*/

/* THE SAY CADENCE IS THE WIDGET'S. One import, no decision logic: the ladder
 * needs to know how long a spoken line actually takes so a `tail` chain does not
 * land on a bubble that is still up. */
import { sayHoldMs, SAY_LEAD_MS } from './widget.js';

/* ---------------------- dials (designer-tunable) -------------------------
 * Every number the voice has, in one frozen object, so the owner can retune the
 * rarity without reading the machinery around it. */
export const VOICE_DIALS = Object.freeze({
  /* --- the seldom channel ---------------------------------------------- */
  BARK_ODDS: 0.25,           // default chance a pool speaks instead of staying wordless
  BARK_FLOOR_MS: 90000,      // no two barks closer than this (ceremony pools exempt)
  FRESH_WEIGHT: 3,           // an unheard line is this many times likelier than a heard one
  DOUBLES_PER_SESSION: 1,    // the suspiciously-human register, capped across ALL pools

  /* --- performing ------------------------------------------------------- */
  CHAIN_LEAD_MS: 1200,       // a lead chain of unknown length gets this much room
  HELD_MS: 1400,             // an event string ("4/10", "GG") after the reveal
  // SAY_MS was a constant 3400 until the hold became length-driven (widget.js
  // sayHoldMs, owner 2026-08-24). The ladder measures the real line instead, or a
  // `tail` scheduled at the old number lands on a bubble that is still up - and a
  // SAY is protected, so the tail would simply be refused and lost.

  /* --- the exit flinch (owner rec 1) ----------------------------------- */
  FLINCH_ODDS: 1 / 3,        // ...of the exits that are armed at all
  FLINCH_PETS: 10,           // lifetime pets before it arms
  FLINCH_DAYS: 2,            // ...and the day-2 return must have happened
  FLINCH_SAD_MS: 500,        // ;_; + the shake
  FLINCH_HOLD_MS: 1600,      // ...then T_T, held, as the window goes

  /* --- the glitch (owner rec 5) ---------------------------------------- */
  GLITCH_BLINK_ODDS: 1 / 40, // of idle blinks, once labSeen
  GLITCH_HEX_ODDS: 1 / 5,    // ...of those, 0x1F instead of #ERR
  GLITCH_HOLD_MS: 260,
  GLITCH_PER_SESSION: 1,
  POST_LAB_QUIET_SESSIONS: 3, // no glitch in the first minute of these many sessions
  POST_LAB_QUIET_MS: 60000,   // ...this being the minute

  /* --- predicates ------------------------------------------------------- */
  POST_LOSS_MS: 10000,       // `postLoss` with no argument
  EVENING_HOUR: 18,          // `evening`
  LATE_FROM: 23,             // `lateNight` (local hour >= this...)
  LATE_TO: 5,                // ...or < this

  /* --- plumbing --------------------------------------------------------- */
  DATA_GRACE_MS: 2500,       // replay ONE moment fired before the data landed
  SAVE_DEBOUNCE_MS: 600,     // one write per interaction, like widget.js
});

/** The voice's OWN key in the C# meta store. NEVER the widget's `emi` blob. */
export const VOICE_STORE_KEY = 'emiVoice';

/** Bump only for a shape change; an unreadable blob starts clean, never throws. */
const BLOB_VERSION = 1;

/* THE GEOFENCE (owner rec 3). EMI has no reaction to the Records door, and none
 * to the lab door that is coming. Not a quiet one - none. It is enforced HERE,
 * in the engine, so no future data file can open it by accident. */
const SILENT_TARGETS = Object.freeze({ records: true, lab: true });

/** The seen-flag for the forced post-lab greet (owner rec 4). */
const POST_LAB_GREET_FLAG = 'postLabGreet';

const GRADE_RANK = Object.freeze({ pass: 1, c: 2, b: 3, a: 4, s: 5 });

/* ---------------------- small helpers --------------------------------- */
function isObj(v) { return !!v && typeof v === 'object' && !Array.isArray(v); }
function plain(v) { return isObj(v) ? v : {}; }
function intOf(v) { const n = Number(v); return Number.isFinite(n) && n > 0 ? Math.round(n) : 0; }
function strOf(v) { return typeof v === 'string' && v ? v : null; }
function listOf(v) { return Array.isArray(v) ? v : (v == null ? [] : [v]); }

/** LOCAL date key (trap 8: dates on this page are local, never UTC). */
function dayKeyOf(d) {
  const dt = d || new Date();
  const m = String(dt.getMonth() + 1).padStart(2, '0');
  const day = String(dt.getDate()).padStart(2, '0');
  return dt.getFullYear() + '-' + m + '-' + day;
}

/** Whole days between two `yyyy-mm-dd` keys; 0 when either is unreadable. */
function daysBetween(a, b) {
  try {
    const pa = String(a).split('-').map(Number);
    const pb = String(b).split('-').map(Number);
    if (pa.length !== 3 || pb.length !== 3) return 0;
    const ta = Date.UTC(pa[0], pa[1] - 1, pa[2]);
    const tb = Date.UTC(pb[0], pb[1] - 1, pb[2]);
    if (!Number.isFinite(ta) || !Number.isFinite(tb)) return 0;
    return Math.max(0, Math.round((tb - ta) / 86400000));
  } catch (e) { return 0; }
}

function gradeKey(g) {
  const k = String(g == null ? '' : g).toLowerCase();
  if (k === 's' || k === 'a' || k === 'b' || k === 'c') return k;
  if (k === 'pass' || k === 'zen') return 'pass';
  return '';
}

/** A line's identity for the heard counter, the no-repeat guard and onceEver. */
function lineKey(l) { return (l && (l.id || l.t)) ? String(l.id || l.t) : ''; }

/* ============================================================================
 * createVoice
 * ==========================================================================*/
/**
 * @param {Object} o
 * @param {Object=} o.store       core/store.js (get/set) - the SAME handle the widget has
 * @param {Object} o.emi          the emi/index.js controller (say/emote/stats)
 * @param {Function=} o.stats     lifetime telemetry accessor (defaults to emi.stats)
 * @param {Function=} o.onGesture widget.onGesture - subscribe to the pointer verbs
 * @param {Object=} o.data        {BEATS, POOLS, RARE_DORK, TELEMETRY, CHAINS} - INJECTED,
 *                                which is how the suite runs without the data modules.
 *                                Absent = story.js / barks.js are dynamic-imported.
 * @param {Function=} o.rng       0..1, injectable so a suite never flakes
 * @param {Function=} o.now       ms clock, injectable for the same reason
 * @param {Function=} o.log
 * @returns {?Object} the voice, or null when there is nothing to speak through
 */
export function createVoice(o) {
  const opts = o || {};
  const emi = opts.emi;
  if (!emi || typeof emi.say !== 'function' || typeof emi.emote !== 'function') return null;

  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const store = opts.store && typeof opts.store.get === 'function' ? opts.store : null;
  const statsOf = typeof opts.stats === 'function'
    ? opts.stats
    : (typeof emi.stats === 'function' ? () => emi.stats() : () => ({}));

  /* CAN SHE ACTUALLY PERFORM RIGHT NOW. The renderer attaches a tick or two
   * after the mount and a `say` before that returns false - so without this
   * gate a one-shot beat would be spent on a bubble nobody ever saw. The
   * controller injects the real probe; a direct caller gets "always". */
  const canPerform = typeof opts.canPerform === 'function' ? opts.canPerform : () => true;
  let rng = typeof opts.rng === 'function' ? opts.rng : Math.random;
  let clock = typeof opts.now === 'function' ? opts.now : Date.now;
  const now = () => { const n = Number(clock()); return Number.isFinite(n) ? n : 0; };

  /* THE DIALS, per instance. `VOICE_DIALS` is still the one table the owner
   * tunes; `opts.dials` is a TEST seam that shortens a hold so a suite does not
   * have to wait out a locked cadence in real time. */
  const D = Object.assign({}, VOICE_DIALS, isObj(opts.dials) ? opts.dials : {});

  /* ---------------------- the persisted blob ----------------------------
   * v:1 exactly as the spec writes it, plus four documented extensions the two
   * data files' predicates need: `once` (the onceEver / oncePerStreak /
   * oncePerGamePerDay ledger), `punchDay`/`punchesToday`, and
   * `dayBest`/`prevDayBest` (afterBadDay). An unreadable or older blob starts
   * clean - a mascot's memory is worth less than a boot. */
  let blob = readBlob();
  let dirty = false;
  let saveTimer = null;

  function readBlob() {
    let raw = null;
    try { raw = store ? store.get(VOICE_STORE_KEY) : null; }
    catch (e) { say('emi voice: store read failed - ' + ((e && e.message) || e)); }
    const b = (isObj(raw) && raw.v === BLOB_VERSION) ? raw : {};
    return {
      v: BLOB_VERSION,
      seen: Object.assign({}, plain(b.seen)),
      sessions: intOf(b.sessions),
      days: intOf(b.days),
      lastDayKey: strOf(b.lastDayKey),
      lastSessionEnd: b.lastSessionEnd === 'bad' ? 'bad' : 'ok',
      heard: Object.assign({}, plain(b.heard)),
      lastLine: Object.assign({}, plain(b.lastLine)),
      flinchLast: b.flinchLast === true,
      best: Object.assign({}, plain(b.best)),
      labSeen: b.labSeen === true,
      postLabSessions: intOf(b.postLabSessions),
      once: Object.assign({}, plain(b.once)),
      punchDay: strOf(b.punchDay),
      punchesToday: intOf(b.punchesToday),
      dayBest: strOf(b.dayBest),
      prevDayBest: strOf(b.prevDayBest),
      /* The CALENDAR anchor (perception wave): the local day EMI first spoke on
       * this install, for `calendarDaysAtLeast`. `days` counts days PLAYED and
       * cannot answer "a year since we met"; this can. A pre-wave blob gets it
       * stamped on its next boot, which starts the clock late and never early. */
      firstDayKey: strOf(b.firstDayKey),
    };
  }

  /** Write-through, DEBOUNCED (widget.js's rule: one write per interaction). */
  function save(immediate) {
    if (!store || typeof store.set !== 'function') { dirty = false; return; }
    if (saveTimer !== null) { clearTimeout(saveTimer); saveTimer = null; }
    const doIt = () => {
      saveTimer = null;
      dirty = false;
      try { store.set(VOICE_STORE_KEY, JSON.parse(JSON.stringify(blob))); }
      catch (e) { say('emi voice: store write failed - ' + ((e && e.message) || e)); }
    };
    if (immediate) doIt();
    else saveTimer = setTimeout(doIt, D.SAVE_DEBOUNCE_MS);
  }
  function touch() { dirty = true; save(false); }

  /* ---------------------- session-only state ----------------------------
   * Deliberately NOT persisted: a floor, a cap and a cooldown are about THIS
   * sitting. Tomorrow starts quiet again. */
  const S = {
    startedAt: now(),
    lastBarkAt: 0,
    glitchCount: 0,
    lastLossAt: 0,
    doubleSpent: false,
    poolCount: {},        // poolId -> barks this session
    lineCount: {},        // poolId|lineKey -> the same, per line
    poolLast: {},         // poolId -> when it last spoke (cooldownMs)
    classCount: {},       // poolId -> barks in the CURRENT class (maxPerClass)
    channel: {},          // 'rare'|'telemetry' -> spent this session
    seenThisSession: {},
    absenceDays: 0,
    newDay: false,
    greetSpent: false,
    openedBad: false,
    /* ONE exit's worth of suppression after the lab is first seen (D.5): a
     * flinch that session would read as "it knows", the one thing it must never
     * read as. Session-only on purpose - entering the lab and the app exit that
     * follows it are the same sitting. */
    flinchSuppress: false,
  };
  S.openedBad = blob.lastSessionEnd === 'bad';

  const timers = new Set();
  function later(fn, ms) {
    const id = setTimeout(() => { timers.delete(id); try { fn(); } catch (e) { /* noop */ } },
      Math.max(0, ms | 0));
    timers.add(id);
    return id;
  }
  function killTimers() { for (const id of timers) clearTimeout(id); timers.clear(); }

  /* ---------------------- the data, late and optional ------------------- */
  let BEATS = [];
  let POOLS = [];
  let CHAINS = null;          // durations only, so a lead can run back to back
  let ready = false;
  let pending = null;

  function normalizeBeats(list) {
    const src = Array.isArray(list) ? list : (isObj(list) ? Object.keys(list).map((k) => list[k]) : []);
    const out = [];
    for (const b of src) {
      if (!isObj(b) || typeof b.on !== 'string' || !b.id) continue;
      out.push({
        id: String(b.id),
        on: b.on,
        when: Array.isArray(b.when) ? b.when : [],
        whenAny: Array.isArray(b.whenAny) ? b.whenAny : [],
        priority: Number(b.priority) || 0,
        once: b.once !== false,
        requires: Array.isArray(b.requires) ? b.requires : [],
        lead: b.lead == null ? null : b.lead,
        held: typeof b.held === 'string' ? b.held : null,
        fx: typeof b.fx === 'string' ? b.fx : null,
        tail: typeof b.tail === 'string' ? b.tail : null,
        nod: b.nod === true,
        say: typeof b.say === 'string' ? b.say : null,
        face: typeof b.face === 'string' ? b.face : null,
        emote: isObj(b.emote) ? b.emote : null,
        report: b.report === true,
        double: b.double === true,
      });
    }
    // Highest priority first, then declaration order - a stable deal every time.
    return out.map((b, i) => Object.assign(b, { _i: i }))
      .sort((a, c) => (c.priority - a.priority) || (a._i - c._i));
  }

  function normalizeLine(l, pool) {
    if (!isObj(l)) return null;
    if (typeof l.t !== 'string' && typeof l.chain !== 'string') return null;
    return {
      t: typeof l.t === 'string' ? l.t : null,
      face: typeof l.face === 'string' ? l.face : null,
      chain: typeof l.chain === 'string' ? l.chain : (pool && pool.chain) || null,
      nod: l.nod === true,
      double: l.double === true,
      when: Array.isArray(l.when) ? l.when : [],
      id: typeof l.id === 'string' ? l.id : null,
      maxPerSession: Number(l.maxPerSession) || 0,
      onceEver: l.onceEver === true,
    };
  }

  function makePool(id, p, channel, on, lines) {
    return {
      id: String(id),
      on: on.filter((x) => typeof x === 'string' && x),
      when: Array.isArray(p.when) ? p.when : [],
      odds: typeof p.odds === 'number' ? p.odds : D.BARK_ODDS,
      ceremony: p.ceremony === true,
      priority: Number(p.priority) || 0,
      noRepeat: p.noRepeat !== false,
      cooldownMs: Number(p.cooldownMs) || 0,
      maxPerSession: Number(p.maxPerSession) || 0,
      maxPerClass: Number(p.maxPerClass) || 0,
      oncePerStreak: p.oncePerStreak === true,
      oncePerGamePerDay: p.oncePerGamePerDay === true,
      channel: channel || 'bark',
      lines,
    };
  }

  /**
   * POOLS is a map of poolId -> pool. `RARE_DORK` and `TELEMETRY` are ONE
   * container each whose LINES carry their own `on`/`when`, so every one of
   * those becomes a one-line pool wearing the container's odds and cap - which
   * is what keeps the routing (and the channel ration) identical for all three.
   */
  function normalizePools(map, channel) {
    const out = [];
    if (!isObj(map)) return out;
    if (Array.isArray(map.lines)) {
      // a CONTAINER (RARE_DORK / TELEMETRY)
      map.lines.forEach((raw, i) => {
        const line = normalizeLine(raw, map);
        if (!line) return;
        const on = listOf(raw.on).filter((x) => typeof x === 'string');
        if (!on.length) return;
        const pool = makePool((channel || 'chan') + ':' + (line.id || i), map, channel, on, [line]);
        pool.noRepeat = false;                 // one line each: no-repeat would mute it
        pool.when = [];                        // the gate is the LINE's, not the container's
        out.push(pool);
      });
      return out;
    }
    for (const id of Object.keys(map)) {
      const p = map[id];
      if (!isObj(p) || !Array.isArray(p.lines) || !p.lines.length) continue;
      const on = listOf(p.on).filter((x) => typeof x === 'string');
      if (!on.length) continue;
      const lines = p.lines.map((l) => normalizeLine(l, p)).filter(Boolean);
      if (!lines.length) continue;
      out.push(makePool(id, p, channel, on, lines));
    }
    return out;
  }

  function useData(d) {
    const data = isObj(d) ? d : {};
    BEATS = normalizeBeats(data.BEATS || data.beats);
    POOLS = normalizePools(data.POOLS || data.pools, 'bark')
      .concat(normalizePools(data.RARE_DORK, 'rare'))
      .concat(normalizePools(data.TELEMETRY, 'telemetry'));
    if (isObj(data.CHAINS)) CHAINS = data.CHAINS;
    ready = true;
  }

  if (isObj(opts.data)) {
    useData(opts.data);
  } else {
    /* THREE OPTIONAL IMPORTS, ONE FAILURE MODE EACH. A missing story.js costs
     * the beats; a missing barks.js costs the pools; a missing chains.js costs
     * only the exact length of a lead chain, which falls back to a dial. */
    Promise.all([
      import('./story.js').catch((e) => { say('emi voice: story.js unavailable (' + ((e && e.message) || e) + ')'); return null; }),
      import('./barks.js').catch((e) => { say('emi voice: barks.js unavailable (' + ((e && e.message) || e) + ')'); return null; }),
      import('./chains.js').catch(() => null),
    ]).then(([s, b, c]) => {
      try {
        useData({
          BEATS: s && (s.BEATS || s.default),
          POOLS: b && (b.POOLS || (b.default && b.default.POOLS)),
          RARE_DORK: b && (b.RARE_DORK || (b.default && b.default.RARE_DORK)),
          TELEMETRY: b && (b.TELEMETRY || (b.default && b.default.TELEMETRY)),
          CHAINS: c && (c.CHAINS || (c.default && c.default.CHAINS)),
        });
      } catch (e) { say('emi voice: data shape refused - ' + ((e && e.message) || e)); ready = true; }
      /* THE OPENING MOMENT LANDS BEFORE WE DO. The shell's first `greet` fires
       * on the same frame as the mount and this import is at least a tick away,
       * so ONE moment is replayed - and only inside the grace window, because a
       * line three seconds after its moment is worse than a line that was
       * missed (emi/index.js's rule, same number). */
      /* WHOEVER OWNS THE BUFFER REPLAYS IT. With a controller wired in
       * (`onReady`) the buffer is ITS one slot, because only the controller
       * also knows whether the renderer has landed; a direct caller keeps
       * ours. Never both - two replays of one moment is two barks in a row. */
      const p = pending;
      pending = null;
      if (typeof opts.onReady === 'function') { try { opts.onReady(); } catch (e) { /* noop */ } }
      else if (p && now() - p.when < D.DATA_GRACE_MS) {
        try { onMoment(p.name, p.payload); } catch (e) { /* noop */ }
      }
    }).catch(() => { ready = true; });
  }

  /* ---------------------- bookkeeping -----------------------------------
   * `sessions` counts mounts; `days` rolls on the first moment of a local day.
   * Both are the spine every `sessionAtLeast` / `day2` gate hangs off. */
  blob.sessions += 1;
  if (blob.labSeen) blob.postLabSessions += 1;
  touch();

  function rollDay() {
    const key = dayKeyOf(new Date(now()));
    // Above the same-day return: an existing blob mid-day still gets anchored.
    if (!blob.firstDayKey) { blob.firstDayKey = key; touch(); }
    if (blob.lastDayKey === key) return;
    if (blob.lastDayKey) S.absenceDays = daysBetween(blob.lastDayKey, key);
    blob.lastDayKey = key;
    blob.days += 1;
    // Yesterday's best grade is what `afterBadDay` reads; today starts blank.
    blob.prevDayBest = blob.dayBest;
    blob.dayBest = null;
    S.newDay = true;
    touch();
  }

  /** The punch cards, straight off the store's cache. Read-only, always. */
  function cardsBlob() {
    try {
      const all = store && typeof store.get === 'function' ? store.get('punchCards') : null;
      return isObj(all) ? all : null;
    } catch (e) { return null; }
  }

  function enrolledCards() {
    const all = cardsBlob();
    if (!all) return 0;
    let n = 0;
    for (const k of Object.keys(all)) {
      const c = all[k];
      if (isObj(c) && typeof c.enrolledAt === 'string' && c.enrolledAt) n += 1;
    }
    return n;
  }

  function masteredCount(p) {
    const n = p ? Number(p.count) : NaN;
    if (Number.isFinite(n) && n > 0) return Math.round(n);
    try {
      if (store && typeof store.unlockedGames === 'function') {
        return Object.keys(store.unlockedGames() || {}).length;
      }
    } catch (e) { /* a card that cannot be read is simply not a mastery */ }
    return 0;
  }

  function streakOf(p) {
    const n = p ? Number(p.streak) : NaN;
    if (Number.isFinite(n)) return Math.round(n);
    try {
      if (store && typeof store.streak === 'function') return (store.streak().count | 0);
    } catch (e) { /* noop */ }
    return 0;
  }

  /** How full the card this stamp landed on now is. Payload first, store second. */
  function holesOf(p) {
    const n = p ? Number(p.holes) : NaN;
    if (Number.isFinite(n) && n > 0) return Math.round(n);
    const key = p && typeof p.gameKey === 'string' ? p.gameKey : null;
    if (!key) return 0;
    try {
      if (store && typeof store.punchCard === 'function') return (store.punchCard(key).punches | 0);
    } catch (e) { /* noop */ }
    return 0;
  }

  /** What this event teaches the blob. Runs BEFORE the predicates read it. */
  function bookkeep(name, p, c) {
    if (name === 'fail' || name === 'runLost' || name === 'streakBroken') {
      S.lastLossAt = now();
      if (blob.lastSessionEnd !== 'bad') { blob.lastSessionEnd = 'bad'; touch(); }
    } else if (name === 'win') {
      if (blob.lastSessionEnd !== 'ok') { blob.lastSessionEnd = 'ok'; touch(); }
    } else if (name === 'classStart') {
      S.classCount = {};                       // maxPerClass is per class, not per session
    }

    /* TODAY'S HOLES, for the double-punch beat. Counted off the stamp moments
     * the shell actually drew, which is the only place a hole is ever drawn. */
    if (name === 'stamp') {
      const key = blob.lastDayKey || dayKeyOf(new Date(now()));
      if (blob.punchDay !== key) { blob.punchDay = key; blob.punchesToday = 0; }
      blob.punchesToday += Math.max(1, Math.round(Number(p && p.minted) || 1));
      touch();
      c.punchesToday = blob.punchesToday;
    }

    /* GRADE-UP is a diff, so it has to be read before it is written; the day's
     * best is what tomorrow's `afterBadDay` will read. */
    const gk = p && typeof p.gameKey === 'string' ? p.gameKey : null;
    const g = gradeKey(p && p.grade);
    if (g && (name === 'win' || name === 'stamp' || name === 'reportCard')) {
      if (gk) {
        const prev = GRADE_RANK[blob.best[gk]] || 0;
        c.gradeUp = GRADE_RANK[g] > prev;
        if (GRADE_RANK[g] > prev) { blob.best[gk] = g; touch(); }
      }
      if ((GRADE_RANK[g] || 0) > (GRADE_RANK[blob.dayBest] || 0)) { blob.dayBest = g; touch(); }
    }
  }

  /* ---------------------- predicates ------------------------------------ */
  const warned = {};

  const PREDICATES = {
    firstSession: () => blob.sessions === 1,
    day2: () => blob.days === 2,
    daysAtLeast: (a) => blob.days >= (Number(a) || 0),
    sessionAtLeast: (a) => blob.sessions >= (Number(a) || 0),
    sessionIs: (a) => blob.sessions === (Number(a) || 0),
    streakAtLeast: (a, c) => c.streak >= (Number(a) || 0),
    streakIs: (a, c) => c.streak === (Number(a) || 0),
    gradeIs: (a, c) => !!c.grade && c.grade === gradeKey(a),
    perfect: (a, c) => c.p.perfect === true,
    postLoss: (a, c) => {
      const ms = Number(a) || D.POST_LOSS_MS;
      return S.lastLossAt > 0 && (c.now - S.lastLossAt) <= ms;
    },
    lateNight: () => {
      const h = new Date(now()).getHours();
      return h >= D.LATE_FROM || h < D.LATE_TO;
    },
    evening: () => new Date(now()).getHours() >= D.EVENING_HOUR,
    longAbsence: (a) => S.absenceDays >= (Number(a) || 2),
    petsAtLeast: (a, c) => c.pets >= (Number(a) || 0),
    hoursAtLeast: (a, c) => c.hours >= (Number(a) || 0),
    flingsAtLeast: (a, c) => c.flings >= (Number(a) || 0),
    bubblesAtLeast: (a, c) => c.bubbles >= (Number(a) || 0),
    masteredAtLeast: (a, c) => c.mastered >= (Number(a) || 0),
    masteredIs: (a, c) => c.mastered === (Number(a) || 0),
    labSeen: () => blob.labSeen === true,
    notLabSeen: () => blob.labSeen !== true,
    gradeUp: (a, c) => c.gradeUp === true,
    seen: (a) => !!blob.seen[String(a)],
    notSeen: (a) => !blob.seen[String(a)],
    seenThisSession: (a) => !!S.seenThisSession[String(a)],
    lastSessionBad: () => S.openedBad === true,
    firstOfDay: () => S.newDay && !S.greetSpent,
    notFirstOfDay: () => !(S.newDay && !S.greetSpent),
    repeatToday: () => !(S.newDay && !S.greetSpent),
    awayCountAtLeast: (a, c) => c.awayCount >= (Number(a) || 0),
    firstHole: (a, c) => c.holes === 1,
    punchesAtLeast: (a, c) => c.holes >= (Number(a) || 0),
    punchesTodayAtLeast: (a, c) => c.punchesToday >= (Number(a) || 0),
    cardsBelow: (a, c) => c.cards < (Number(a) || 0),
    /* A DECK IS FULL WHEN EVERY ACTIVE CLASS HAS A CARD. The shell knows how
     * many classes there are and says so on the enrolMint seam; with no such
     * payload the answer is no, never a guess. */
    deckFull: (a, c) => c.total > 0 && c.cards >= c.total,
    afterBadDay: () => {
      const r = GRADE_RANK[blob.prevDayBest] || 0;
      return r > 0 && r <= GRADE_RANK.c;
    },
    /* --- the perception wave (2026-08-24) ------------------------------- */
    /** CALENDAR days since the first day she ever spoke - `days` counts days
     *  played, which can never reach an anniversary honestly. */
    calendarDaysAtLeast: (a) => !!blob.firstDayKey
      && daysBetween(blob.firstDayKey, dayKeyOf(new Date(now()))) >= (Number(a) || 0),
    hidesAtLeast: (a, c) => c.hides >= (Number(a) || 0),
    /** Spot memory, read straight off the dropAt payload the widget builds. */
    zoneCountAtLeast: (a, c) => Math.round(Number(c.p.zoneCount) || 0) >= (Number(a) || 0),
    zoneRowIs: (a, c) => !!c.p.zoneRow && String(c.p.zoneRow) === String(a),
  };

  function holds(when, c) {
    if (!Array.isArray(when) || !when.length) return true;
    for (const raw of when) if (!test(raw, c)) return false;
    return true;
  }
  function holdsAny(when, c) {
    if (!Array.isArray(when) || !when.length) return true;
    for (const raw of when) if (test(raw, c)) return true;
    return false;
  }
  function test(raw, c) {
    if (typeof raw !== 'string' || !raw) return false;
    const i = raw.indexOf(':');
    const name = i < 0 ? raw : raw.slice(0, i);
    const arg = i < 0 ? null : raw.slice(i + 1);
    const fn = Object.prototype.hasOwnProperty.call(PREDICATES, name) ? PREDICATES[name] : null;
    if (!fn) {
      // A gate nobody implements CLOSES the line. Logged once per name, per session.
      if (!warned[name]) { warned[name] = true; say('emi voice: unknown predicate "' + name + '" (line held)'); }
      return false;
    }
    try { return !!fn(arg, c); } catch (e) { return false; }
  }

  /* ---------------------- performing ------------------------------------ */
  /** How long a chain runs, so a lead can hand over with no gap. */
  function chainMs(id) {
    try {
      const ch = CHAINS && CHAINS[id];
      if (ch && Array.isArray(ch.seq)) {
        let ms = 0;
        for (const f of ch.seq) ms += Number(f && f[1]) || 0;
        if (ms > 0) return ms;
      }
    } catch (e) { /* noop */ }
    return D.CHAIN_LEAD_MS;
  }

  /** A wordless reaction. `{chain}` or `{face, hold, fx, body}`. */
  function emoteIt(e) {
    if (!isObj(e)) return false;
    try {
      if (e.chain) return !!emi.emote(e.chain, { force: e.force === true });
      if (e.face) {
        return !!emi.emote(e.face, {
          hold: typeof e.hold === 'number' ? e.hold : undefined,
          fx: e.fx || null, body: e.body || null, force: e.force === true,
        });
      }
    } catch (err) { /* a mascot may never break a screen transition */ }
    return false;
  }

  function sayIt(line, face, nod) {
    if (typeof line !== 'string' || !line) return false;
    try { return !!emi.say(line, { face: face || undefined, nod: nod === true }); }
    catch (e) { return false; }
  }

  /**
   * THE LADDER. lead chain(s) -> the held event string -> the bubble -> a tail
   * chain. Everything after the first step is scheduled, never awaited: the
   * moment that fired this has already moved on.
   * @returns {boolean} true when the FIRST step actually landed
   */
  function perform(entry) {
    const leads = [];
    const lead = entry.lead;
    if (typeof lead === 'string') leads.push({ chain: lead });
    else if (Array.isArray(lead)) for (const l of lead) { if (typeof l === 'string') leads.push({ chain: l }); }
    else if (isObj(lead)) leads.push({ face: lead.face, hold: lead.hold });
    else if (typeof entry.chain === 'string') leads.push({ chain: entry.chain });

    const line = typeof entry.say === 'string' ? entry.say : (typeof entry.t === 'string' ? entry.t : null);
    const steps = [];
    let t = 0;
    for (const l of leads) {
      steps.push({ at: t, run: () => emoteIt(l) });
      t += l.chain ? chainMs(l.chain) : (Number(l.hold) || D.CHAIN_LEAD_MS);
    }
    if (entry.held) {
      // The locked EVENT REVEAL pattern: the chain builds, the string is held.
      // A beat's `fx` rides this frame (a lead chain already carries its own).
      const held = { face: entry.held, hold: D.HELD_MS, fx: entry.fx || null };
      steps.push({ at: t, run: () => emoteIt(held) });
      t += D.HELD_MS;
    }
    if (line) {
      steps.push({ at: t, run: () => sayIt(line, entry.face, entry.nod) });
      t += SAY_LEAD_MS + sayHoldMs(line);
    }
    if (entry.tail) steps.push({ at: t, run: () => emoteIt({ chain: entry.tail }) });
    if (!steps.length) return emoteIt(entry.emote);

    let first = false;
    steps.forEach((s, i) => {
      if (i === 0) first = !!s.run();
      else later(s.run, s.at);
    });
    return first;
  }

  /* ---------------------- the beats ------------------------------------- */
  function fireBeat(name, c) {
    if (!BEATS.length) return false;
    for (const b of BEATS) {
      if (b.on !== name) continue;
      if (b.once && blob.seen[b.id]) continue;
      if (b.requires.length && !b.requires.every((r) => !!blob.seen[r])) continue;
      if (!holds(b.when, c)) continue;
      if (b.whenAny.length && !holdsAny(b.whenAny, c)) continue;
      const done = (b.emote && !b.say && !b.lead && !b.held) ? emoteIt(b.emote) : perform(b);
      if (!done) continue;
      // SEEN ONLY WHEN IT WAS ACTUALLY SEEN: a dismissed EMI eats no beat.
      S.seenThisSession[b.id] = true;
      if (b.once) { blob.seen[b.id] = true; touch(); }
      if (b.double) S.doubleSpent = true;
      return true;
    }
    return false;
  }

  /* ---------------------- the barks ------------------------------------- */
  function poolBlocked(pool, c) {
    if (pool.channel !== 'bark' && S.channel[pool.channel]) return true;
    if (pool.maxPerSession && (S.poolCount[pool.id] || 0) >= pool.maxPerSession) return true;
    if (pool.maxPerClass && (S.classCount[pool.id] || 0) >= pool.maxPerClass) return true;
    if (pool.cooldownMs && S.poolLast[pool.id] && (c.now - S.poolLast[pool.id]) < pool.cooldownMs) return true;
    if (pool.oncePerStreak && blob.once[pool.id + '|streak|' + c.streak]) return true;
    if (pool.oncePerGamePerDay
      && blob.once[pool.id + '|' + (c.gameKey || '-') + '|' + (blob.lastDayKey || '-')]) return true;
    return false;
  }

  function eligiblePool(name, c) {
    let best = null;
    for (const p of POOLS) {
      if (p.on.indexOf(name) < 0) continue;
      if (poolBlocked(p, c)) continue;
      if (!holds(p.when, c)) continue;
      // Priority is the data's own word; a tie goes to the more specific gate.
      if (!best
        || p.priority > best.priority
        || (p.priority === best.priority && p.when.length > best.when.length)) best = p;
    }
    return best;
  }

  /** No-repeat, the rations, and unheard lines weighted `FRESH_WEIGHT`x. */
  function pickLine(pool, c) {
    const heard = plain(blob.heard[pool.id]);
    const last = blob.lastLine[pool.id];
    let usable = pool.lines.filter((l) => {
      if (l.onceEver && (Number(heard[lineKey(l)]) || 0) > 0) return false;
      if (l.maxPerSession && (S.lineCount[pool.id + '|' + lineKey(l)] || 0) >= l.maxPerSession) return false;
      if (l.when.length && !holds(l.when, c)) return false;
      return true;
    });
    if (!usable.length) return null;
    if (pool.noRepeat && usable.length > 1) {
      const fresh = usable.filter((l) => lineKey(l) !== last);
      if (fresh.length) usable = fresh;
    }
    if (S.doubleSpent) {
      const clowns = usable.filter((l) => !l.double);
      if (!clowns.length) return null;        // a pool of doubles is simply spent
      usable = clowns;
    }
    let total = 0;
    const w = usable.map((l) => {
      const n = (Number(heard[lineKey(l)]) || 0) === 0 ? D.FRESH_WEIGHT : 1;
      total += n;
      return n;
    });
    let r = rng() * total;
    for (let i = 0; i < usable.length; i++) { r -= w[i]; if (r < 0) return usable[i]; }
    return usable[usable.length - 1];
  }

  function fireBark(name, c) {
    const pool = eligiblePool(name, c);
    if (!pool) return false;
    /* THE FLOOR. Ninety seconds between any two barks; a ceremony is rare by
     * nature and is exempt by DECLARATION, never by accident. */
    if (!pool.ceremony && S.lastBarkAt && (c.now - S.lastBarkAt) < D.BARK_FLOOR_MS) return false;
    const odds = typeof pool.odds === 'number' ? pool.odds : D.BARK_ODDS;
    if (odds < 1 && rng() >= odds) return false;
    const line = pickLine(pool, c);
    if (!line) return false;
    if (!perform(line)) return false;

    S.lastBarkAt = c.now;
    S.poolLast[pool.id] = c.now;
    S.poolCount[pool.id] = (S.poolCount[pool.id] || 0) + 1;
    S.classCount[pool.id] = (S.classCount[pool.id] || 0) + 1;
    const lk = pool.id + '|' + lineKey(line);
    S.lineCount[lk] = (S.lineCount[lk] || 0) + 1;
    if (line.double) S.doubleSpent = true;
    if (pool.channel !== 'bark') S.channel[pool.channel] = true;
    if (pool.oncePerStreak) blob.once[pool.id + '|streak|' + c.streak] = true;
    if (pool.oncePerGamePerDay) {
      blob.once[pool.id + '|' + (c.gameKey || '-') + '|' + (blob.lastDayKey || '-')] = true;
    }
    const heard = plain(blob.heard[pool.id]);
    const k = lineKey(line);
    heard[k] = (Number(heard[k]) || 0) + 1;
    blob.heard[pool.id] = heard;
    blob.lastLine[pool.id] = k;
    touch();
    return true;
  }

  /* ---------------------- the exit flinch -------------------------------
   * Wordless, never explained, never referenced, and never in the way: the exit
   * is already happening. Arms on the owner's rule - the day-2 return has
   * happened AND ten pets have been given - and never twice in a row. */
  function flinchArmed() {
    if (blob.days < D.FLINCH_DAYS && !blob.seen.b13_day2_return) return false;
    let pets = 0;
    try { pets = Number((statsOf() || {}).pets) || 0; } catch (e) { pets = 0; }
    return pets >= D.FLINCH_PETS;
  }

  function flinch() {
    if (!flinchArmed()) return false;
    if (S.flinchSuppress) { S.flinchSuppress = false; return false; }
    if (blob.flinchLast) { blob.flinchLast = false; touch(); return false; }
    if (rng() >= D.FLINCH_ODDS) { blob.flinchLast = false; touch(); return false; }
    blob.flinchLast = true;
    // The door is already opening: bank the flag NOW, not on a debounce.
    save(true);
    const shook = emoteIt({ face: ';_;', hold: D.FLINCH_SAD_MS, body: 'shake', force: true });
    if (!shook) return false;
    later(() => { emoteIt({ face: 'T_T', hold: D.FLINCH_HOLD_MS, force: true }); },
      D.FLINCH_SAD_MS);
    return true;
  }

  /* ---------------------- the glitch ------------------------------------
   * One frame, once a session at most, only after the lab, and never in the
   * first minute of the first three sessions after it - the gag must not
   * cluster near the memory of the room it leaks from. */
  function glitch() {
    if (!blob.labSeen) return false;
    if (S.glitchCount >= D.GLITCH_PER_SESSION) return false;
    if (blob.postLabSessions <= D.POST_LAB_QUIET_SESSIONS
      && (now() - S.startedAt) < D.POST_LAB_QUIET_MS) return false;
    if (rng() >= D.GLITCH_BLINK_ODDS) return false;
    const text = rng() < D.GLITCH_HEX_ODDS ? '0x1F' : '#ERR';
    if (!emoteIt({ face: text, hold: D.GLITCH_HOLD_MS })) return false;
    S.glitchCount += 1;
    return true;
  }

  /* ---------------------- the forced post-lab greet ---------------------
   * Owner rec 4. The words live in story.js; if story.js never shipped, this is
   * simply a flag that is never spent - not a throw, and not an invented line. */
  function postLabGreet(c) {
    if (!blob.labSeen || blob.seen[POST_LAB_GREET_FLAG]) return false;
    const beat = BEATS.find((b) => b.id === POST_LAB_GREET_FLAG)
      || BEATS.find((b) => b.on === 'greet' && /postlab|post_lab/i.test(String(b.id)));
    if (!beat) return false;
    if (!perform(beat)) return false;
    blob.seen[POST_LAB_GREET_FLAG] = true;
    blob.seen[beat.id] = true;
    S.seenThisSession[beat.id] = true;
    touch();
    return true;
  }

  /* ---------------------- the hit test ----------------------------------
   * WHERE SHE WAS DROPPED. `document.elementFromPoint` does not exist in the
   * DOM double, so the whole thing is a try/catch that no-ops clean. The campus
   * paints class rooms as `.campus-room`, sealed wings as `.campus-room.locked`
   * and every office as `.campus-room.facility` - and because Records is one of
   * those offices and nothing in the markup tells it from the Front Office, a
   * FACILITY reports as geofenced rather than risking rec 3. */
  function hitTest(x, y) {
    try {
      if (typeof document === 'undefined' || typeof document.elementFromPoint !== 'function') return null;
      let node = document.elementFromPoint(x, y);
      for (let i = 0; node && i < 8; i++) {
        const raw = (node.getAttribute && node.getAttribute('class'));
        const cls = String(raw == null ? (node.className || '') : raw);
        const has = (k) => cls.split(/\s+/).indexOf(k) >= 0;
        if (has('campus-room') && has('facility')) return { what: 'facility' };
        if (has('campus-room') && has('locked')) return { what: 'sealed' };
        if (has('campus-wing')) return { what: 'wing' };
        if (has('campus-room')) return { what: 'room' };
        node = node.parentNode;
      }
    } catch (e) { /* no elementFromPoint, no answer - never a throw */ }
    return null;
  }

  /* ---------------------- the entry points ------------------------------ */
  function context(name, p) {
    const pl = p || {};
    let st = {};
    try { st = statsOf() || {}; } catch (e) { st = {}; }
    return {
      name, p: pl, now: now(),
      grade: gradeKey(pl.grade),
      gameKey: typeof pl.gameKey === 'string' ? pl.gameKey : null,
      streak: streakOf(pl),
      mastered: masteredCount(pl),
      holes: holesOf(pl),
      punchesToday: blob.punchesToday,
      cards: Number.isFinite(Number(pl.enrolled)) && Number(pl.enrolled) > 0
        ? Math.round(Number(pl.enrolled)) : enrolledCards(),
      total: Math.round(Number(pl.total) || 0),
      awayCount: Math.round(Number(pl.count) || 0),
      pets: Number(st.pets) || 0,
      flings: Number(st.flings) || 0,
      bubbles: Number(st.bubblesSeen) || 0,
      hides: Number(st.hides) || 0,
      hours: (Number(st.msVisible) || 0) / 3600000,
      gradeUp: false,
    };
  }

  /**
   * A shell moment. Returns TRUE when the voice performed, which is
   * `fireMoment`'s signal to skip the wordless table entirely.
   */
  function onMoment(name, p) {
    if (typeof name !== 'string' || !name) return false;
    try {
      /* THE GEOFENCE, FIRST AND UNCONDITIONAL. Not a quiet reaction: none. */
      if (name === 'lockedClick' && p && SILENT_TARGETS[String(p.what)]) return false;

      if (!ready || !canPerform()) {
        /* NOT YET. Either the script has not landed or the face has not, and
         * both mean the same thing: consuming a one-shot beat here would burn
         * it on a bubble that cannot be drawn. Remember, answer honestly. */
        pending = { name, payload: p, when: now() };
        return false;
      }

      rollDay();
      const c = context(name, p);
      bookkeep(name, p, c);

      let done = false;
      if (name === 'greet') done = postLabGreet(c);
      if (!done) done = fireBeat(name, c);
      if (!done) done = fireBark(name, c);
      /* THE FLINCH IS THE EXIT'S OWN CHANNEL and the last thing asked. Nothing
       * scripted rides `exitIntent` any more (the day-1 goodbye moved to
       * `dayDone`) and there is deliberately no pool on a real exit - a real
       * exit is 450ms into an Esc hold and the window is already closing. */
      if (!done && name === 'exitIntent') done = flinch();
      if (name === 'greet') S.greetSpent = true;
      return done;
    } catch (e) {
      say('emi voice: onMoment threw (ignored) - ' + ((e && e.message) || e));
      return false;
    }
  }

  /**
   * A pointer verb from the widget. The widget has ALREADY played its own
   * reaction; this only decides whether the moment also earns a line.
   */
  function onGesture(kind, p) {
    if (typeof kind !== 'string' || !kind) return false;
    try {
      /* THE IDLE BLINK IS THE ONE UNATTENDED "GESTURE", so it reaches the
       * GLITCH and nothing else: no beat and no bark may ever be spent by a
       * page nobody is touching (field bug, 2026-08-24). */
      if (kind === 'blinkIdle') return glitch();
      let payload = p || {};
      if (kind === 'dropAt') {
        const hit = hitTest(Number(payload.x) || 0, Number(payload.y) || 0);
        // A drop on an office could be the Records door, and rec 3 has no
        // "probably not" branch - the whole facility class is silent.
        if (hit && hit.what === 'facility') return false;
        payload = Object.assign({}, payload, { what: hit ? hit.what : null });
      }
      return onMoment('gesture:' + kind, payload);
    } catch (e) {
      say('emi voice: onGesture threw (ignored) - ' + ((e && e.message) || e));
      return false;
    }
  }

  /* THE LAST WRITE. A debounced blob in flight is a blob that never landed. */
  const onPageHide = () => { if (dirty) save(true); };
  if (typeof window !== 'undefined' && window.addEventListener) {
    window.addEventListener('pagehide', onPageHide);
  }

  if (typeof opts.onGesture === 'function') {
    try { opts.onGesture((kind, payload) => onGesture(kind, payload)); }
    catch (e) { say('emi voice: gesture subscribe failed - ' + ((e && e.message) || e)); }
  }

  const api = {
    onMoment,
    onGesture,
    /** THE LAB WAVE'S ONE VERB. Flipping it arms the glitch and buys one
     *  suppressed flinch (D.5); it is idempotent. */
    setLabSeen(on) {
      const next = on !== false;
      if (blob.labSeen === next) return blob.labSeen;
      blob.labSeen = next;
      if (next) { blob.postLabSessions = 1; S.flinchSuppress = true; }
      save(true);
      return blob.labSeen;
    },
    get labSeen() { return blob.labSeen; },

    /* ---- THE FIELD-TRIP SEAM (wave W2a, 2026-08-24) --------------------
     * A trip is not a moment: the line is landed by `widget.apparate`, not by
     * the ladder in this file, so `emi/fieldtrips.js` cannot spend a one-shot
     * through `fireBeat`. It needs the same LEDGER though - one POI, one
     * visit, for ever - and minting a second seen-map beside this one is how
     * two ledgers disagree. So the trips module reads and writes THIS one, by
     * these three members and nothing else. `sessions` is exposed for the same
     * reason: `state()` answers a deep JSON copy of the whole blob, which is a
     * silly price for one integer a scheduler asks for on an idle edge. */
    /** Has this id already fired? (Beat ids and POI ids share the namespace.) */
    hasSeen(id) { return !!blob.seen[String(id)]; },
    /** Bank one. Returns false when it was already banked - the caller's guard. */
    markSeen(id) {
      const k = String(id);
      if (!k || blob.seen[k]) return false;
      blob.seen[k] = true;
      touch();
      return true;
    },
    /** How many times EMI has been mounted, ever. The `sessionAtLeast` spine. */
    get sessions() { return blob.sessions; },
    /** Test seams. A suite that rolls dice must not flake. */
    setRng(fn) { if (typeof fn === 'function') rng = fn; },
    setClock(fn) { if (typeof fn === 'function') clock = fn; },
    /** Every predicate name this build answers - the suite validates the data
     *  files against it, so an unwired gate is caught before a player sees it. */
    predicates() { return Object.keys(PREDICATES).slice(); },
    /** Every trigger the loaded data can be woken by (beats + pools). */
    triggers() {
      const out = {};
      for (const b of BEATS) out[b.on] = true;
      for (const p of POOLS) for (const on of p.on) out[on] = true;
      return Object.keys(out);
    },
    /** Debug/read-only view; nothing outside may mutate the blob. */
    state() { return { blob: JSON.parse(JSON.stringify(blob)), session: Object.assign({}, S), ready }; },
    get ready() { return ready; },
    dials: D,
    flush() { save(true); },
    destroy() {
      killTimers();
      if (saveTimer !== null) { clearTimeout(saveTimer); saveTimer = null; }
      try { if (store && store.set) store.set(VOICE_STORE_KEY, JSON.parse(JSON.stringify(blob))); }
      catch (e) { /* noop */ }
      if (typeof window !== 'undefined' && window.removeEventListener) {
        window.removeEventListener('pagehide', onPageHide);
      }
    },
  };
  return api;
}

export default createVoice;
