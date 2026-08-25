/* ============================================================================
 * emi/asks.js - THE ASK ENGINE (wave EMI ASKS, 2026-08-25).
 *
 * An ASK is the one thing EMI has never done: she puts a question on the glass
 * and then WAITS for you. Everything else she does is a reaction. Spec (owner
 * locked 0825): `planning/arcademy/EMI-ASKS.md`. Every `say` string in the
 * table below is VERBATIM from it - a line that could not be wired was LEFT
 * OUT, never re-worded.
 *
 * ---------------------------------------------------------------------------
 * THE SHAPE
 *
 *   bubble line  through `emi.say()` - the ordinary path, so the pose, the
 *                hold and the Blipese babble are the ones she already has
 *                (trap 70: the voice hangs off setBubble and nowhere else).
 *   the strip    two chips under the bubble, inside `.emi` (the widget mints
 *                and owns the node; this file only says what goes on it).
 *   the end      a chip click or a dismiss. SHE DOES NOT GIVE UP any more
 *                (owner, 2026-08-25): the question has no auto-hide and the
 *                bubble stays in sight until it is answered. The one exception
 *                is the dares, which ride `classStart` and must be off the
 *                glass before the board takes input (trap 97).
 *
 * AND SHE CANNOT BE TALKED OVER. From the moment the question lands until the
 * strip comes down, the widget holds the glass for her: a later SAY is parked
 * in one slot and released afterwards if it is still worth saying, and every
 * other reaction is refused. That fence is widget.js's (`askOwnsGlass`), and
 * it is what trap 104 is about - the ask's own line carries `ask: true` and is
 * the only thing that may cross it.
 *
 * THE IGNORED PATH IS WORDLESS AND UNIVERSAL. Chips slide out, `-_-` holds
 * 1400, the bubble says `...`, then idle. No line, ever. Silence is on-lock:
 * there is no guilt in words here, and a skipped "bed?" costs HER sleep.
 *
 * ---------------------------------------------------------------------------
 * THE GATES (each one a different kind of no)
 *
 *   1. NEVER BEFORE THE THIRD SESSION - `voice.sessions`, read never minted,
 *      the same spine `sessionAtLeast` and the field trips hang off.
 *   2. NOT MID-CLASS. `classStart` is the one moment that fires INSIDE the
 *      gate (the dares live there) and it is evaluated BEFORE the latch is
 *      set, which is why `note()` runs the latches and `offer()` runs the
 *      asks - two entry points, one order.
 *   3. NOT OVER A LIVE VERB. A say, a chain, a press, a drag, a field trip, an
 *      off channel, a dismissed or disabled EMI: the widget owns that truth
 *      and answers it in one call (`askReady`).
 *   4. THE CADENCE. `sessions - lastAskSession >= 3`, or `>= 2` on a 1-in-3
 *      roll. The NAME ask is EXEMPT and spends nothing.
 *   5. ONE ASK A SITTING. Session-local on purpose: banking it would make a
 *      reload the way to farm a second one.
 *
 * ANY PRESS CANCELS, AND CANCELLING IS FREE. A pointerdown or a keypress that
 * is not the strip's own ends the ask as `ignored` and spends NO cadence - the
 * player was doing something else, and charging them three sessions for that
 * would be the feature punishing them for playing the game. Touch always wins
 * (trap 75's law, one level up).
 *
 * THE LISTENERS ARE TRAP 80's SHAPE. `document` pointerdown + keydown, both
 * `{passive:true}`, both in the BUBBLE phase, neither ever calling
 * `preventDefault` or `stopPropagation`, both removed on `destroy()`. EMI adds
 * no rung to the Esc ladder here either: Esc dismisses the ask on its way past
 * and the ladder never notices.
 *
 * INPUT TRUST (trap 59). `#arc-emi` is `pointer-events:none` and exactly two
 * nodes turned it back on. The chips are the THIRD, and they are the only new
 * pointer-active elements this wave mints. Nothing else on the layer changed.
 * ==========================================================================*/

/* THE SAY CADENCE IS THE WIDGET'S (voice.js's import, for the same reason):
 * the strip has to land WITH the line, not with the typing dots, so this file
 * needs to know how long the run-up actually is. */
import { SAY_LEAD_MS, sanitizeAskName, DIALS as WIDGET_DIALS } from './widget.js';

/* ONE SANITISER, TWO READERS. widget.js owns it because widget.js is the file
 * that reads the stored `emi` blob back on boot; re-exported here so the ask
 * table's own module is the one a caller (and the suite) reaches for. */
export { sanitizeAskName };

/* ---------------------- dials ------------------------------------------ */
export const ASK_DIALS = Object.freeze({
  ASK_FROM_SESSION: 3,        // gate 1
  CADENCE_SESSIONS: 3,        // gate 4: the plain spacing
  CADENCE_MIN_SESSIONS: 2,    // ...and the short one, on a roll
  CADENCE_ROLL: 1 / 3,        // ...the roll
  ASKS_PER_SESSION: 1,        // gate 5 (the NAME ask is over and above it)
  /* SHE DOES NOT GIVE UP (owner, 2026-08-25: "when EMI asks us something we
   * need to keep the bubble in sight till we respond"). Zero is not "expire
   * immediately", it is NO AUTO-HIDE: the question and its chips stay on the
   * glass until they are answered or dismissed, and the player can never be
   * trapped by that because ANY press anywhere is a free dismiss (see the
   * header). It was 40000 through the EMI ASKS wave. */
  GIVE_UP_MS: 0,
  /* ...WITH ONE CARVE-OUT, AND IT IS TRAP 97's. A dare is offered on
   * `classStart`, one beat before a board takes input; a strip that sat there
   * would be a pointer-active node over a live precision board (trap 59). So
   * the dares alone still leave, and they leave fast. */
  DARE_GIVE_UP_MS: 12000,
  /* HOW LONG THE QUESTION ITSELF HOLDS. Not a timeout - widget.js's
   * ASK_HOLD_MS is an hour, and `releaseAskLine()` is what actually takes it
   * down. Read from the widget so there is one number, not two. */
  HOLD_MS: WIDGET_DIALS.ASK_HOLD_MS,
  /* AN INTERRUPTED ASK COMES BACK, ONCE. A press, a pet, a drag or a docking
   * is not an answer and not a refusal, so the question is owed one more go on
   * the next quiet moment of the same sitting. One, so a player who keeps
   * clicking past her is never nagged into a third. */
  REASK_MAX: 1,
  IGNORED_HOLD_MS: 1400,      // -_- , then `...` , then idle
  /* THE STRIP LANDS WITH THE LINE, not with the typing dots - so its lead IS
   * the say ladder's run-up. A dial and not a constant only so a suite can
   * compress it; nothing else may ever change it. */
  STRIP_LEAD_MS: SAY_LEAD_MS,
  /* HOW LONG A LANDED LINE OWNS THE GLASS, near enough. Anything this file
   * schedules AFTER one of her lines waits STRIP_LEAD_MS + this, so a follow-up
   * never lands on a bubble that is still up (trap 72s lesson). */
  AFTER_SAY_MS: 4590,         // (3400 x 1.35 - it tracks the bubble hold, 2026-08-25)
  NAME_MAX: 8,                // a14: 8 characters, sanitised
  NAME_REASK_AFTER: 10,       // a skipped name is asked once more, this many later
  BED_SKIPS_GROGGY: 3,        // three skipped "bed?"s in a row buys the groggy greet
  GROGGY_MS: 60000,           // ...and =_= holds that long
  PET_ASK_ODDS: 0.25,         // a13 is rare even when it is eligible
  LOVE_LEAD_MS: 1200,         // the `love` chain, before a13's line lands
  DARE_XP: 15,                // DOCUMENTATION ONLY - the HOST owns the number
});

/** The moments this module is willing to be woken by. */
export const ASK_TRIGGERS = Object.freeze([
  'greet', 'classStart', 'idlePlayer', 'reportCard', 'streakBroken', 'exitAim',
  /* THE HEARTBEAT'S SLOT (2026-08-25). Not a new question and not a new
   * cadence - a WAY IN. The metronome fills campus silence every ten seconds
   * or so, and one of the things it may draw is "her question, now" rather
   * than making an eligible ask wait for the next `greet` or the next
   * `idlePlayer` edge, which on a quiet campus can be minutes away. Every gate
   * in this file still runs, and NO frequency dial moved. */
  'heartbeat',
]);

/** Which `on` rows a `heartbeat` may stand in for. The heartbeat IS campus
 *  idle, so it answers for the two triggers that already mean that - the
 *  arrival and the quiet - and for nothing else. A dare (`classStart`) is
 *  emphatically not on this list: trap 97 owns where those may be offered,
 *  and `exitAim` is a hand travelling to the close button (trap 95). */
const HEARTBEAT_STANDS_IN = Object.freeze({ idlePlayer: true, greet: true });

/** The three dare kinds, and the only strings the payout frame may carry. */
export const DARE_KINDS = Object.freeze(['S', 'streak', 'fast']);

/** The families a precision board belongs to - a08 is barred from both. */
const PRECISION = Object.freeze({ tracking: true, reflex: true });

/* ---------------------- helpers ---------------------------------------- */
function isObj(v) { return !!v && typeof v === 'object' && !Array.isArray(v); }
function intOf(v) { const n = Number(v); return Number.isFinite(n) && n > 0 ? Math.round(n) : 0; }

/* ============================================================================
 * THE TABLE
 *
 *   id       stable; it IS the store key under `emi.ask`. Renaming re-opens it.
 *   on       one trigger from ASK_TRIGGERS.
 *   q        the question, verbatim. `face` is the face it is asked with.
 *   chips    exactly two labels, in order. Chip 1 is the YES side.
 *   yes/no   {say, face} - the reaction, verbatim, through the ordinary say.
 *   when(c)  every other gate this ask wants, over the context below.
 *   once     'ever' (one answer for ever) | 'room' (once per gameKey) | null.
 *   effect   what a YES actually does, by name; `runEffect` is the one switch.
 *   dare     the kind flagged on the NEXT class ('S' | 'streak' | 'fast').
 *   win/lose the dare's resolution lines, verbatim.
 *   exempt   true = does not spend (or wait on) the cadence.
 * ==========================================================================*/
export const ASKS = Object.freeze([
  /* ---- check-ins (she cares) ---------------------------------------- */
  Object.freeze({
    id: 'a01_roughday', on: 'greet',
    q: 'rough day?', face: 'o_o',
    chips: ['yeah', 'nah'],
    yes: { say: 'ok. easy one today.', face: '^_^', effect: 'soft' },
    no: { say: 'good. show me then.', face: '^_~' },
    when: (c) => !c.firstSession,
  }),
  Object.freeze({
    id: 'a02_sleep', on: 'greet',
    q: 'did you sleep?', face: '(◔_◔)',
    chips: ['yeah', 'nah'],
    /* The YES is the humanity drop, so it spends the quirk slot the same way a
     * `double` bark does - one a session, across every pool (voice.js's
     * DOUBLES_PER_SESSION). `quirk:true` is how this file asks for it. */
    yes: { say: 'good. me neither. wait.', face: '@_@', quirk: true },
    no: { say: "same. we'll be slow together.", face: '=_=' },
    when: (c) => c.hour < 11,
  }),

  /* ---- memory (she remembers) ---------------------------------------- */
  Object.freeze({
    id: 'a03_flavor', on: 'idlePlayer',
    q: 'spiral or flash?', face: 'o_o',
    chips: ['spiral', 'flash'],
    /* The ANSWER is the point: `store` is the value banked under `.a`, so the
     * callback pools in barks.js can read it back with `askIs:a03_flavor:...`. */
    yes: { say: 'spiral. knew it.', face: '^_~', store: 'spiral' },
    no: { say: 'flash. loud. i respect it.', face: '*_*', store: 'flash' },
    once: 'ever',
    when: (c) => c.where === 'hub',
  }),
  Object.freeze({
    id: 'a04_room', on: 'classStart',
    q: 'do you like it in here?', face: 'o_o',
    chips: ['yeah', 'nah'],
    yes: { say: "me too. it's got a hum.", face: '^_^' },
    no: { say: 'we can leave after. one more though.', face: '._.' },
    once: 'room',
    when: (c) => !!c.gameKey,
  }),
  Object.freeze({
    id: 'a05_morning', on: 'greet',
    q: 'are you a morning person?', face: '(◔_◔)',
    chips: ['yeah', 'nah'],
    yes: { say: "i'm an all-the-time person.", face: '^___^' },
    no: { say: 'same. mornings are a rumor.', face: '=_=' },
    when: (c) => c.hour < 9,
  }),

  /* ---- for herself (she wants things) -------------------------------- */
  Object.freeze({
    id: 'a06_side', on: 'idlePlayer',
    q: 'can i sit on the other side today?', face: '._.',
    chips: ['ok', 'nah'],
    yes: { say: 'thanks. new view.', face: '*_*', effect: 'mirror' },
    no: { say: "ok. this side's fine. it's mine.", face: '-_-' },
    when: (c) => c.where === 'hub',
  }),
  Object.freeze({
    id: 'a07_onemore', on: 'reportCard',
    q: 'one more? a small one?', face: '._.',
    chips: ['ok', 'nah'],
    yes: { say: 'small. i lied. pick any.', face: '^___^' },
    no: { say: "fine. tomorrow then. i'll be here.", face: '^_^' },
  }),
  Object.freeze({
    id: 'a08_watch', on: 'classStart',
    q: 'can i watch this one up close?', face: 'o_o',
    chips: ['ok', 'nah'],
    yes: { say: 'front row.', face: '*_*', effect: 'watch' },
    no: { say: "ok. i'll watch from here. i have good eyes.", face: 'o_o' },
    when: (c) => !PRECISION[c.family],
  }),

  /* ---- dares (she is a gremlin) -------------------------------------- */
  Object.freeze({
    id: 'a09_dareS', on: 'classStart',
    q: "bet you can't S this one.", face: '>_<',
    chips: ['bet', 'nah'],
    yes: { say: null, dare: 'S' },
    no: { say: 'thought so.', face: '¬_¬' },
    win: { say: "fine. you did it. i'm not sulking.", face: 'T_T', after: '^_^' },
    lose: { say: 'told you. told you told you.', face: '^_~' },
    when: (c) => !!c.gameKey && c.streak >= 1,
  }),
  Object.freeze({
    id: 'a10_dareStreak', on: 'classStart',
    q: "three in a row. bet you can't.", face: '>.<',
    chips: ['bet', 'nah'],
    yes: { say: null, dare: 'streak' },
    no: { say: "smart. i'd have won.", face: '(¬‿¬)' },
    win: { say: "ok that was cool. don't get used to it.", face: '*_*' },
    lose: { say: "streak's mine now. i'm keeping it.", face: '^_~' },
    when: (c) => c.streak === 2,
  }),
  Object.freeze({
    id: 'a11_dareFast', on: 'classStart',
    q: 'faster than last time. bet.', face: '0_0',
    chips: ['bet', 'nah'],
    yes: { say: null, dare: 'fast' },
    no: { say: 'ok. no pressure. some pressure.', face: '^_~' },
    win: { say: 'you blinked and it was over. show off.', face: '@_@' },
    lose: { say: "slower. it's ok. i timed it wrong. probably.", face: '._.' },
    when: (c) => c.family === 'reflex' && c.bestRt > 0,
  }),

  /* ---- shared streaks ------------------------------------------------- */
  Object.freeze({
    id: 'a12_goagain', on: 'streakBroken',
    q: 'go again?', face: ';_;',
    chips: ['yes', 'later'],
    yes: { say: "yes. we're fixing it. right now.", face: '>_<', effect: 'timetable' },
    no: { say: 'ok. streaks are fake anyway. mostly.', face: 'T_T' },
  }),

  /* ---- she pets back --------------------------------------------------- */
  Object.freeze({
    id: 'a13_pet', on: 'idlePlayer',
    q: 'pet?', face: '._.',
    chips: ['pet', 'nah'],
    yes: { say: 'there it is.', face: '^___^', effect: 'pet' },
    no: { say: "ok. i'll just sit here. pettably.", face: '-_-' },
    when: (c) => c.pets >= 50,
    odds: ASK_DIALS.PET_ASK_ODDS,
  }),

  /* ---- name her back --------------------------------------------------- */
  Object.freeze({
    id: 'a14_name', on: 'greet',
    q: 'what do i call you?', face: 'o_o',
    /* THE ONE ASK WITH A KEYBOARD. Chip 1 is an 8-character field (Enter
     * submits, empty = skip); chip 2 is the ordinary out. */
    input: true,
    chips: ['ok', 'later'],
    yes: { say: null, face: '^_^', effect: 'name' },
    no: { say: "ok. 'you' works. i like you anyway.", face: '^_^' },
    exempt: true,
    /* NO `once` HERE, deliberately: the whole re-ask ladder (asked at 3, one
     * more try ten sittings after a skip, then never) is in `when`, and a
     * `once` gate on top of it would wall off the second try. */
    /* Session 3 ONWARDS while it has never been answered; a skip re-opens it
     * once, ten sessions later, and then never again (`n >= 2` is the wall).
     *
     * IT WAS `=== ASK_FROM_SESSION` UNTIL 2026-08-25 AND THAT WAS THE ONE-SHOT
     * BUG. Nothing is written to the ledger when an ask is INTERRUPTED (a
     * press spends no cadence and records no answer - trap 96), so a name ask
     * that a stray click took away left `answerOf` empty, session 3 never came
     * round again, and she could never learn your name on that save. `>=` is
     * the whole fix; the nagging it would otherwise buy is held off by
     * `S.nameAsked`, which is once a SITTING the way a15's `bedAsked` is. */
    when: (c) => {
      const prev = c.answerOf('a14_name');
      if (!prev) return c.sessions >= ASK_DIALS.ASK_FROM_SESSION;
      if (prev.a === 'yes' || intOf(prev.n) >= 2) return false;
      return c.sessions - intOf(prev.s) >= ASK_DIALS.NAME_REASK_AFTER;
    },
  }),

  /* ---- bedtime ritual --------------------------------------------------- */
  Object.freeze({
    id: 'a15_bed', on: 'exitAim',
    q: 'bed?', face: '=_=',
    chips: ['yeah', 'not yet'],
    yes: { say: "night. i'll keep the lights on.", face: 'ZzZ', effect: 'bedYes' },
    no: { say: "ok. i'll pretend i'm not tired.", face: '=_=' },
    /* THE FENCE. Never blocks, never delays, never cancels an exit, and there
     * is no "don't go" anywhere in it. She just asks, and the window goes. */
    exempt: true,
  }),
]);

/** THE GROGGY GREET (a15's cost, three skips running). Verbatim. */
export const GROGGY = Object.freeze({
  say: 'morning. i think. what day is it.',
  face: '@_@',
  hold: '=_=',
});

/* ============================================================================
 * createAsks
 * ==========================================================================*/
/**
 * @param {Object} o
 * @param {Object}  o.widget    the widget handle (askReady / mountAsk / the seams)
 * @param {Object}  o.emi       the emi/index.js controller (say/emote)
 * @param {Object=} o.voice     emi/voice.js - the sessions counter, read only
 * @param {Object=} o.store     core/store.js - only ever read; the WIDGET writes
 * @param {Array=}  o.table     the ASKS table (injected by the suite)
 * @param {Function=} o.rng
 * @param {Function=} o.now
 * @param {Object=} o.dials
 * @param {Function=} o.log
 */
export function createAsks(o = {}) {
  const say = typeof o.log === 'function' ? o.log : () => {};
  const widget = o.widget;
  const emi = o.emi;
  if (!widget || !emi || typeof emi.say !== 'function') return null;
  if (typeof widget.askReady !== 'function' || typeof widget.mountAsk !== 'function') return null;

  const D = Object.assign({}, ASK_DIALS, isObj(o.dials) ? o.dials : {});
  const rng = typeof o.rng === 'function' ? o.rng : Math.random;
  const clock = typeof o.now === 'function' ? o.now : Date.now;
  const voice = isObj(o.voice) ? o.voice : null;
  const store = o.store && typeof o.store.get === 'function' ? o.store : null;
  const TABLE = (Array.isArray(o.table) ? o.table : ASKS)
    .filter((a) => isObj(a) && typeof a.id === 'string' && typeof a.on === 'string');

  /* ---------------------- the persisted half ---------------------------
   * IT RIDES THE WIDGET'S `emi` BLOB AND THE WIDGET'S WRITER. There is exactly
   * one thing on the page that writes the `emi` key, and it is widget.js's
   * `save()` - a second writer would make every drag a race that eats a name.
   * `widget.askState()` hands back the LIVE object; mutating it and calling
   * `widget.askSave()` is the whole contract. */
  const blob = widget.askState();

  function answerOf(id) {
    const row = blob.ask && blob.ask[String(id)];
    return isObj(row) ? row : null;
  }
  function record(id, answer) {
    const k = String(id);
    const prev = answerOf(k);
    if (!blob.ask) blob.ask = {};
    blob.ask[k] = {
      a: answer,
      n: intOf(prev && prev.n) + 1,
      s: sessions(),
    };
    widget.askSave(true);
  }

  function sessions() {
    if (!voice) return 0;
    const n = Number(voice.sessions);
    return Number.isFinite(n) ? n : 0;
  }

  /* ---------------------- session-only state ---------------------------- */
  const S = {
    asked: 0,              // gate 5 (the NAME ask is not counted here)
    midClass: false,
    soft: false,           // a01 yes: comfort faces + the extra report line
    softLineSpent: false,
    bedAsked: false,       // a15 is once a sitting
    nameAsked: false,      // ...and so is a14, now that its window is open-ended
    reask: null,           // the id of an ask a press took away, owed one more go
    reasks: 0,             // ...and how many have been paid back (cap D.REASK_MAX)
    dare: null,            // {kind, gameKey} - flagged on the NEXT class
    dareSpent: false,      // one dare a session, win or lose
    groggySpent: false,
    lastWhere: null,
  };

  const timers = new Set();
  function later(fn, ms) {
    const id = setTimeout(() => { timers.delete(id); try { fn(); } catch (e) { /* noop */ } },
      Math.max(0, ms | 0));
    timers.add(id);
    return id;
  }
  function killTimers() { for (const id of timers) clearTimeout(id); timers.clear(); }

  /* ---------------------- the live ask ---------------------------------- */
  /** {ask, strip, giveUp, landed} - null whenever no strip is up. */
  let live = null;

  function pageVisible() {
    try {
      if (typeof document === 'undefined') return true;
      if (typeof document.visibilityState !== 'string') return true;
      return document.visibilityState === 'visible';
    } catch (e) { return true; }
  }

  function hourNow() {
    try { return new Date(clock()).getHours(); } catch (e) { return 12; }
  }

  function bestRtOf(gameKey) {
    if (!store || typeof store.gameMeta !== 'function' || !gameKey) return 0;
    try { return intOf((store.gameMeta(gameKey) || {}).bestRtMs); } catch (e) { return 0; }
  }

  function streakNow(p) {
    const n = Number(p && p.streak);
    if (Number.isFinite(n)) return Math.round(n);
    try {
      if (store && typeof store.streak === 'function') return (store.streak().count | 0);
    } catch (e) { /* noop */ }
    return 0;
  }

  function petsNow() {
    try { return intOf((emi.stats() || {}).pets); } catch (e) { return 0; }
  }

  function context(name, p) {
    const pl = isObj(p) ? p : {};
    const gameKey = typeof pl.gameKey === 'string' ? pl.gameKey : null;
    return {
      name,
      p: pl,
      sessions: sessions(),
      firstSession: sessions() <= 1,
      hour: hourNow(),
      streak: streakNow(pl),
      pets: petsNow(),
      family: String(pl.family || ''),
      gameKey,
      where: typeof pl.where === 'string' ? pl.where : null,
      bestRt: bestRtOf(gameKey),
      name0: blob.name || '',
      answerOf,
      answered: (id) => !!answerOf(id),
    };
  }

  /* ---------------------- eligibility ----------------------------------- */
  /** The ONE-answer rules. `ever` = one answer for all time; `room` = one per
   *  gameKey, which is why a04's ledger key carries the room on it. */
  function keyFor(a, c) {
    return a.once === 'room' && c.gameKey ? a.id + '|' + c.gameKey : a.id;
  }
  function spent(a, c) {
    if (!a.once) return false;
    const row = answerOf(keyFor(a, c));
    /* AN IGNORED ASK IS NOT AN ANSWER. She may ask a `once` question again
     * some other night; what she may never do is ask it again after you told
     * her something. */
    return !!row && row.a !== 'ignored';
  }

  function eligible(name, c) {
    const out = [];
    /* A HEARTBEAT IS A SLOT, NOT A TRIGGER. It matches the rows that already
     * ride campus idle; every other gate below is untouched. */
    const hb = name === 'heartbeat';
    for (const a of TABLE) {
      if (hb ? !HEARTBEAT_STANDS_IN[a.on] : a.on !== name) continue;
      if (spent(a, c)) continue;
      if (a.id === 'a15_bed' && S.bedAsked) continue;
      if (a.id === 'a14_name' && S.nameAsked) continue;
      if (a.dareKind || (a.yes && a.yes.dare)) { if (S.dareSpent || S.dare) continue; }
      if (typeof a.when === 'function') {
        let ok = false;
        try { ok = !!a.when(c); } catch (e) { ok = false; }
        if (!ok) continue;
      }
      if (typeof a.odds === 'number' && a.odds < 1 && rng() >= a.odds) continue;
      out.push(a);
    }
    return out;
  }

  /** GATE 4. The name ask is exempt: it neither waits on the cadence nor
   *  spends it, which is what lets her learn your name on session three. */
  function cadenceOpen() {
    const last = intOf(blob.lastAskSession);
    if (!last) return true;
    const gap = sessions() - last;
    if (gap >= D.CADENCE_SESSIONS) return true;
    return gap >= D.CADENCE_MIN_SESSIONS && rng() < D.CADENCE_ROLL;
  }

  /* ---------------------- the strip ------------------------------------- */
  function giveUpMsFor(a) {
    /* A DARE LIVES ON THE ROOM CARD, NOT ON THE BOARD. `classStart` fires
     * before the class chrome is built and long before a board takes input, so
     * a dare that has not been answered by the time the player is actually
     * playing has missed its window - it leaves rather than sitting over a
     * precision board for another half minute. */
    return (a.yes && a.yes.dare) || a.on === 'classStart' ? D.DARE_GIVE_UP_MS : D.GIVE_UP_MS;
  }

  function unmount(slide) {
    if (!live) return;
    const l = live;
    live = null;
    if (l.giveUp) { clearTimeout(l.giveUp); l.giveUp = null; }
    try { widget.unmountAsk(slide !== false); } catch (e) { /* noop */ }
  }

  /**
   * SHE IS OWED ONE MORE GO (owner, 2026-08-25). An ask that a press, a pet, a
   * drag or a docking took off the glass was neither answered nor declined -
   * it was INTERRUPTED - and the ledger records nothing for it (trap 96), so
   * without this the question is simply gone for the rest of the sitting. It
   * comes back on the next quiet moment instead.
   *
   * NOT THE DARES. Their whole window is the room card, one beat before a
   * board takes input (trap 97); an idle moment is the wrong side of it, and a
   * dare armed off a moment with no class behind it would resolve against
   * whatever the player happened to play next.
   */
  function rememberReask(a) {
    if (!a || S.reask) return;
    if (S.reasks >= D.REASK_MAX) return;
    if (a.on === 'classStart' || (a.yes && a.yes.dare)) return;
    S.reask = a.id;
  }

  /** THE UNIVERSAL WORDLESS END. No line, ever - see the header. */
  function ignored(a, c, opts) {
    const noSpend = !!(opts && opts.noSpend);
    unmount(true);
    if (noSpend) rememberReask(a);
    if (!noSpend) record(keyFor(a, c), 'ignored');
    /* ...AND A PRESS IS NOT A SKIPPED BEDTIME EITHER. She only loses the sleep
     * when the question sat there unanswered; a player who was doing something
     * else was not saying no to bed. */
    if (a.id === 'a15_bed' && !noSpend) {
      blob.bedSkips = intOf(blob.bedSkips) + 1;
      widget.askSave(true);
    }
    try {
      widget.setBubble(null);
      widget.raw('-_-', { hold: D.IGNORED_HOLD_MS, clearBubble: false, force: true });
      widget.setBubble('...');
    } catch (e) { /* a mascot may never break a screen transition */ }
    say('emi asks: ' + a.id + ' ignored' + (noSpend ? ' (press - no cadence spent)' : ''));
  }

  function answer(a, c, which) {
    const side = which === 'yes' ? a.yes : a.no;
    if (!isObj(side)) { unmount(true); return; }
    /* THE ANSWER IS WHAT IS STORED. Most asks bank 'yes'/'no'; a03 banks the
     * FLAVOUR itself, because that is what the callback pools read back with
     * `askIs:a03_flavor:spiral`. One field, `.a`, either way. */
    const value = typeof side.store === 'string' ? side.store : which;
    unmount(true);
    record(keyFor(a, c), value);
    /* THE CADENCE IS SPENT ON AN ANSWER, NOT ON AN OFFER. */
    if (!a.exempt) { blob.lastAskSession = sessions(); }
    widget.askSave(true);
    if (side.quirk) { try { if (voice && typeof voice.spendQuirk === 'function') voice.spendQuirk(); } catch (e) { /* noop */ } }
    if (which === 'yes') runEffect(a, side, c);
    if (typeof side.say === 'string' && side.say) {
      try { emi.say(side.say, { face: side.face || '^_^' }); } catch (e) { /* noop */ }
    }
    say('emi asks: ' + a.id + ' = ' + value);
  }

  /* ---------------------- the effects ----------------------------------- */
  function runEffect(a, side, c) {
    const kind = typeof side.effect === 'string' ? side.effect : null;
    if (side.dare && DARE_KINDS.indexOf(side.dare) >= 0) {
      S.dare = { kind: side.dare, gameKey: c.gameKey || null, id: a.id };
      S.dareSpent = true;
      say('emi asks: dare armed - ' + side.dare);
      return;
    }
    if (!kind) return;
    try {
      if (kind === 'soft') { S.soft = true; return; }
      if (kind === 'mirror') { widget.parkMirrored(); return; }
      if (kind === 'watch') { widget.setGazeBias(-1); return; }
      if (kind === 'timetable') { openTimetable(); return; }
      if (kind === 'pet') { widget.creditPet(); return; }
      if (kind === 'bedYes') { blob.bedSkips = 0; widget.askSave(true); return; }
    } catch (e) { say('emi asks: effect "' + kind + '" threw (ignored)'); }
  }

  /**
   * A12's YES OPENS THE TIMETABLE. The plaque IS the school's navigation seam
   * (campus.js `.campus-boardtab`, whose click handler owns `boardOpenedDate`
   * and rolls the flaps), so this presses the page's own button rather than
   * minting a second way in. Not on the campus = nothing to open, and that is
   * a silent no-op, never an error.
   */
  function openTimetable() {
    try {
      if (typeof document === 'undefined' || !document.querySelector) return false;
      const tab = document.querySelector('.campus-stage .campus-boardtab');
      if (!tab || typeof tab.click !== 'function') return false;
      if (tab.getAttribute && tab.getAttribute('aria-expanded') === 'true') return false;
      tab.click();
      return true;
    } catch (e) { return false; }
  }

  /* ---------------------- the name -------------------------------------- */
  function submitName(a, c, raw) {
    const clean = sanitizeAskName(raw);
    if (!clean) { answer(a, c, 'no'); return; }
    unmount(true);
    blob.name = clean;
    record(a.id, 'yes');
    widget.askSave(true);
    /* VERBATIM, with the one substitution the spec allows here. */
    try { emi.say(clean + ". ok. i'm keeping that.", { face: '^_^' }); } catch (e) { /* noop */ }
    say('emi asks: name = ' + clean);
  }

  /* ---------------------- mounting -------------------------------------- */
  function mount(a, c) {
    const chips = Array.isArray(a.chips) ? a.chips.slice(0, 2) : ['ok', 'nah'];
    const giveUp = giveUpMsFor(a);
    /* THE LINE FIRST, AND IT DOES NOT COME DOWN ON ITS OWN. `ask: true` is the
     * flag widget.js reads: it opens the hold that keeps every later bark off
     * the glass (trap 104) and it hands the question a hold nothing but
     * `unmountAsk()` can end. Until 2026-08-25 the hold was the GIVE-UP window,
     * which meant a question with no give-up would have flashed for three
     * seconds and a later line could walk over it at any point. */
    let spoke = false;
    try { spoke = !!emi.say(a.q, { face: a.face || 'o_o', hold: D.HOLD_MS, ask: true }); }
    catch (e) { spoke = false; }
    if (!spoke) return false;

    live = { ask: a, ctx: c, giveUp: null };
    /* THE STRIP LANDS WITH THE LINE, not with the typing dots. A zero lead
     * (a suite compressing the ladder) mounts on the spot rather than on a
     * timer, so a synchronous test never has to await a 0ms setTimeout. */
    const raise = () => {
      if (!live || live.ask !== a) return;
      let strip = null;
      try {
        strip = widget.mountAsk({
          id: a.id,
          chips,
          input: a.input === true,
          maxLength: D.NAME_MAX,
          onChip: (i, text) => {
            if (!live || live.ask !== a) return;
            if (a.input && i === 0) { submitName(a, c, text); return; }
            answer(a, c, i === 0 ? 'yes' : 'no');
          },
          onDismiss: () => { if (live && live.ask === a) ignored(a, c); },
        });
      } catch (e) { strip = null; }
      if (!strip) { unmount(false); return; }
      live.strip = strip;
      /* ZERO IS "SHE WAITS" (D.GIVE_UP_MS). Only the classStart carve-out arms
       * a timer at all, and it is the one ask that must be off the glass
       * before a board goes live. */
      if (giveUp > 0) {
        live.giveUp = setTimeout(() => {
          if (!live || live.ask !== a) return;
          live.giveUp = null;
          ignored(a, c);
        }, giveUp);
      }
    };
    if (D.STRIP_LEAD_MS > 0) later(raise, D.STRIP_LEAD_MS); else raise();

    if (!a.exempt) S.asked += 1;
    if (a.id === 'a15_bed') S.bedAsked = true;
    if (a.id === 'a14_name') S.nameAsked = true;
    say('emi asks: ' + a.id);
    return true;
  }

  /* ---------------------- any press cancels ----------------------------- */
  /* TRAP 80's SHAPE, one level up: passive, bubble phase, no preventDefault,
   * no stopPropagation, never a rung on the Esc ladder. A press that lands on
   * the strip itself is the ANSWER and is left alone; anything else ends the
   * ask as ignored and spends NO cadence. */
  function insideStrip(target) {
    try {
      if (!live || !live.strip || !live.strip.el || !target) return false;
      const el = live.strip.el;
      if (target === el) return true;
      return !!(el.contains && el.contains(target));
    } catch (e) { return false; }
  }

  const onDocDown = (ev) => {
    if (!live) return;
    if (insideStrip(ev && ev.target)) return;
    ignored(live.ask, live.ctx, { noSpend: true });
  };
  const onDocKey = (ev) => {
    if (!live || !ev) return;
    const k = String(ev.key || '');
    /* THE FIELD'S KEYS COME FIRST, AND THAT ORDER IS THE FIX. `1` and `2` are
     * the chip shortcuts below, but they are also two of the eight characters
     * a name may be made of - typing "player1" into a14's field used to SUBMIT
     * on the last keystroke. A question with a keyboard has no chip shortcuts;
     * it has a field, a Send button and Enter. */
    if (live.ask && live.ask.input) {
      if (k === 'Tab' || k === 'Enter' || k === ' ') return;
      if (k.length === 1) return;                            // typing a name
      if (k === 'Backspace' || k === 'Delete') return;
      if (k === 'ArrowLeft' || k === 'ArrowRight' || k === 'Home' || k === 'End') return;
      ignored(live.ask, live.ctx, { noSpend: true });
      return;
    }
    /* THE KEYBOARD REACH (spec): 1 and 2 pick the chips. Anything else is a
     * player doing something else, and that is a dismiss. */
    if (k === '1' || k === '2') {
      if (live.strip && typeof live.strip.pick === 'function') { live.strip.pick(k === '1' ? 0 : 1); return; }
    }
    if (k === 'Tab' || k === 'Enter' || k === ' ') return;   // focus + activate
    ignored(live.ask, live.ctx, { noSpend: true });
  };
  if (typeof document !== 'undefined' && document.addEventListener) {
    document.addEventListener('pointerdown', onDocDown, { passive: true });
    document.addEventListener('keydown', onDocKey, { passive: true });
  }
  /* The widget's own pointer verbs are the other half: a pet, a drag or a
   * fling on EMI herself never reaches `document` before the widget acts. */
  const unGesture = typeof widget.onGesture === 'function'
    ? widget.onGesture((kind) => {
      if (!live) return;
      /* `gone` is the x button, an API hide and `setEnabled(false)` - the
       * screen changes that legitimately kill her. It spends nothing, exactly
       * like the presses, and it is what closes the ask when the question goes
       * away with the mascot rather than with an answer. */
      if (kind === 'pet' || kind === 'petStreak3' || kind === 'drag'
        || kind === 'fling' || kind === 'dropAt' || kind === 'hide' || kind === 'gone') {
        ignored(live.ask, live.ctx, { noSpend: true });
      }
    })
    : () => {};

  /* ---------------------- the dares ------------------------------------- */
  /**
   * RESOLVE ON THE CLASS'S OWN END. `win` and `fail` both carry the grade, the
   * gameKey and `perfect`; the reflex dare also needs `newBest`, which
   * impulse-control now reports additively on `endClass` (trap 54's spirit).
   * @returns {?string} the kind that WON, for the payout frame.
   */
  function resolveDare(name, p) {
    const d = S.dare;
    if (!d) return null;
    S.dare = null;
    const pl = isObj(p) ? p : {};
    if (d.gameKey && pl.gameKey && d.gameKey !== pl.gameKey) return null;
    const row = TABLE.find((a) => a.id === d.id) || null;
    if (!row) return null;
    let won = false;
    if (d.kind === 'S') won = String(pl.grade || '').toLowerCase() === 's';
    else if (d.kind === 'streak') won = name === 'win' && intOf(pl.streak) >= 3;
    else if (d.kind === 'fast') won = pl.newBest === true;
    const side = won ? row.win : row.lose;
    if (isObj(side) && typeof side.say === 'string') {
      try { emi.say(side.say, { face: side.face || '^_^' }); } catch (e) { /* noop */ }
      /* a09's win resolves T_T -> ^_^: the sulk, then the smile. */
      if (won && typeof side.after === 'string') {
        later(() => { try { emi.emote(side.after, { hold: 1200 }); } catch (e) { /* noop */ } },
          D.STRIP_LEAD_MS + D.AFTER_SAY_MS);
      }
    }
    say('emi asks: dare ' + d.kind + ' ' + (won ? 'WON' : 'lost'));
    return won ? d.kind : null;
  }

  /* ---------------------- the latches ----------------------------------- */
  /**
   * EVERY MOMENT, UNCONDITIONALLY. `offer` only sees the moments the voice and
   * the field trips both declined; the latches below have to be right whether
   * she spoke or not, so they live here and this is called first.
   */
  function note(name, p) {
    try {
      if (typeof name !== 'string') return;
      if (name === 'classStart') {
        /* The dare offer is evaluated BEFORE this latch closes (see `offer`),
         * which is the whole reason the two entry points exist. */
        S.midClass = true;
        return;
      }
      if (name === 'win' || name === 'fail' || name === 'runLost') {
        S.midClass = false;
        try { widget.setGazeBias(0); } catch (e) { /* noop */ }
        return;
      }
      if (name === 'reportCard' || name === 'greet' || name === 'dayDone') {
        S.midClass = false;
        try { widget.setGazeBias(0); } catch (e) { /* noop */ }
        return;
      }
      if (name === 'idlePlayer') { S.lastWhere = (p && p.where) || null; }
    } catch (e) { /* noop */ }
  }

  /**
   * THE DARE'S RESOLUTION SEAM. shell.js calls this on the class's own end,
   * before it posts `class-ended`, and puts the answer on the payout frame as
   * `dareWon`. It is a READ of a session flag - it never blocks, and a page
   * with no dare armed answers null on every class for ever.
   */
  function classResult(name, p) {
    try { return resolveDare(name, p); } catch (e) { return null; }
  }

  /* ---------------------- the groggy greet ------------------------------ */
  /**
   * THREE SKIPPED "bed?"s COST HER THE MORNING (spec). This is the ONE thing in
   * the file that runs BEFORE the voice, because it REPLACES the greet pool
   * for exactly one greet - asking afterwards would put two lines on one beat.
   * @returns {boolean} true when it took the moment.
   */
  function greetIntercept() {
    try {
      if (S.groggySpent) return false;
      if (intOf(blob.bedSkips) < D.BED_SKIPS_GROGGY) return false;
      if (!widget.askReady()) return false;
      S.groggySpent = true;
      blob.bedSkips = 0;
      widget.askSave(true);
      let spoke = false;
      try { spoke = !!emi.say(GROGGY.say, { face: GROGGY.face }); } catch (e) { spoke = false; }
      if (!spoke) return false;
      /* ...and then she is just tired for a while. A plain held face, so any
       * real reaction out-ranks it the instant one arrives. */
      later(() => {
        try { widget.raw(GROGGY.hold, { hold: D.GROGGY_MS }); } catch (e) { /* noop */ }
      }, D.STRIP_LEAD_MS + D.AFTER_SAY_MS);
      say('emi asks: groggy greet (three skipped beds)');
      return true;
    } catch (e) { return false; }
  }

  /* ---------------------- the comfort line ------------------------------ */
  /** a01's YES buys ONE extra line on the report card, and only one. */
  function softLine() {
    if (!S.soft || S.softLineSpent) return false;
    S.softLineSpent = true;
    later(() => { try { emi.say('you did fine.', { face: '^_^' }); } catch (e) { /* noop */ } },
      D.STRIP_LEAD_MS + D.AFTER_SAY_MS);
    return true;
  }

  /* ---------------------- the one entry point --------------------------- */
  /**
   * `emi/index.js` offers every moment the voice AND the field trips declined.
   * @returns {boolean} true when an ask took the moment.
   */
  function offer(name, payload) {
    try {
      if (typeof name !== 'string' || ASK_TRIGGERS.indexOf(name) < 0) return false;
      if (live) return false;
      if (!pageVisible()) return false;
      if (!widget.askReady()) return false;
      if (sessions() < D.ASK_FROM_SESSION) return false;
      /* NOT MID-CLASS - and `classStart` is the one moment evaluated while the
       * latch is still open, because the class has not started yet. */
      if (S.midClass && name !== 'classStart') return false;

      const c = context(name, payload);
      if (name === 'reportCard' && softLine()) return false;

      /* THE INTERRUPTED ASK COMES BACK FIRST (owner, 2026-08-25). It is not a
       * new question, so it jumps the eligibility roll and the cadence - but
       * every LIVE gate above still had to pass, and its own `when` is
       * re-checked, because the world moved while she was waiting. It rides
       * the next `idlePlayer` (the quiet moment the owner asked for) or the
       * next firing of its own trigger, whichever comes first. */
      const back = S.reask ? (TABLE.find((a) => a.id === S.reask) || null) : null;
      if (S.reask && !back) S.reask = null;
      // ...and a `heartbeat` is the quiet moment too (2026-08-25), so the one
      // owed question does not have to wait for an idle EDGE to come round.
      if (back && (name === 'idlePlayer' || name === 'heartbeat' || name === back.on)) {
        S.reask = null;
        let ok = !spent(back, c);
        if (ok && typeof back.when === 'function') {
          try { ok = !!back.when(c); } catch (e) { ok = false; }
        }
        if (ok) {
          S.reasks += 1;
          say('emi asks: ' + back.id + ' comes back (interrupted)');
          return mount(back, c);
        }
      }

      const list = eligible(name, c);
      if (!list.length) return false;
      /* THE EXEMPT ONE FIRST. The name ask neither waits on the cadence nor
       * spends it, so it is picked out before the cadence is even consulted. */
      const exempt = list.filter((a) => a.exempt === true);
      if (exempt.length) return mount(exempt[Math.floor(rng() * exempt.length)] || exempt[0], c);
      if (S.asked >= D.ASKS_PER_SESSION) return false;
      if (!cadenceOpen()) return false;
      const pick = list[Math.min(list.length - 1, Math.floor(rng() * list.length))];
      return mount(pick, c);
    } catch (e) {
      say('emi asks: offer threw (ignored) - ' + ((e && e.message) || e));
      return false;
    }
  }

  return {
    offer,
    note,
    classResult,
    greetIntercept,
    /** Read-only: the session flags the shell reads (a01's comfort faces). */
    get flags() { return { soft: S.soft, midClass: S.midClass, dare: S.dare ? S.dare.kind : null }; },
    /** The name she was given, or '' - never null, so a caller can concat. */
    get name() { return blob.name || ''; },
    /** Is a strip up right now? */
    active() { return !!live; },
    /** Test/debug: what could be asked on this moment, by id. */
    candidates(name, payload) {
      try { return eligible(name, context(name, payload)).map((a) => a.id); }
      catch (e) { return []; }
    },
    state() {
      return {
        blob: JSON.parse(JSON.stringify({
          ask: blob.ask || {}, name: blob.name || '',
          lastAskSession: intOf(blob.lastAskSession), bedSkips: intOf(blob.bedSkips),
        })),
        session: Object.assign({}, S),
        live: live ? live.ask.id : null,
      };
    },
    dials: D,
    /** Abort whatever is up. Not an answer, and it spends nothing. */
    cancel() {
      if (!live) return false;
      ignored(live.ask, live.ctx, { noSpend: true });
      return true;
    },
    destroy() {
      killTimers();
      unmount(false);
      try { unGesture(); } catch (e) { /* noop */ }
      if (typeof document !== 'undefined' && document.removeEventListener) {
        document.removeEventListener('pointerdown', onDocDown);
        document.removeEventListener('keydown', onDocKey);
      }
    },
  };
}

export default createAsks;
