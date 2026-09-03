/* ============================================================================
 * core/timetable.js - the day's four classes. BUILD-CONTRACT §7.
 *
 * PURE FUNCTION. buildTimetable({dateSeed, registry, overrideCalendar}) is a
 * deterministic function of its arguments and nothing else: no clock, no
 * Math.random, no storage, no bridge. Two players on the same UTC date with the
 * same registry and the same cached calendar get the identical timetable
 * (regression #978: the UTC date seeds CONTENT, local midnight rolls the
 * player's attendance - attendance lives in core/store.js, not here).
 *
 * ---------------------------------------------------------------------------
 * HOW HISTORY WORKS (the bit worth reading before you change anything)
 *
 * The constraints need to know what the player was served on previous days, and
 * the contract forbids a history store: "derive prior days by re-running the
 * generator on prior dates". A naive recursion (day D asks for D-1, which asks
 * for D-2...) never terminates, so instead we WALK FORWARD from a fixed EPOCH
 * to the target date, accumulating the days we generate. Because the epoch is
 * fixed, the walk is a fixed point: the entry the walk produces for D-1 is
 * byte-identical to what buildTimetable('D-1') returns on its own. That is what
 * makes "no repeat within 3 days" mean the same thing to the generator and to
 * the player.
 *
 * Cost is one cheap pick per day since the epoch (a few RNG draws over a
 * small pool), memoised per (date, pool, calendar). MAX_WALK_DAYS is a
 * pathology guard, not a design parameter: cross it (>10 years past the epoch)
 * and the window starts sliding, which reintroduces a little drift in the
 * derived history. Nothing else breaks.
 *
 * ---------------------------------------------------------------------------
 * CONSTRAINTS, and why the small V1 pool cannot crash them
 *
 * Slot 1 is always the homeroom (daily_trigger). Slots 2-4 are drawn under four
 * constraints; when the surviving candidate set is EMPTY they are relaxed in the
 * contract's order - flagship -> meaty -> family -> no-repeat - until something
 * survives. Two details make that survivable on a tiny pool:
 *
 *   - no-repeat NARROWS before it dies (3 -> 2 -> 1 -> off), so the very last
 *     thing the generator gives up is "not the class you played yesterday";
 *   - every constraint also exists as a seeded WEIGHT (weightFor), so once a
 *     hard filter is gone its PREFERENCE still shapes the draw.
 *
 * With the 4-game Semester-1 pool (3 rotating games into 3 slots) no-repeat-3 is
 * mathematically unsatisfiable, so most days relax down to the 1-day window and
 * the board becomes a rotation - which is the best available answer, not a bug.
 * One consequence worth knowing: a pool with exactly ONE meaty game cannot
 * satisfy "one meaty per day" AND no-repeat, and the contract ranks no-repeat
 * higher, so some days legitimately have no meaty class. Add more games and the
 * hard filters simply start binding again - no code change.
 *
 * FOUR CLASSES A DAY (owner ruling 2026-08-23 - the pace levers). The day used
 * to deal three. The fourth slot costs the no-repeat window a little headroom
 * and nothing else: with the shipped pool (9 live classes, misdirection
 * retired) the homeroom is fixed and EIGHT rotating games have to cover THREE
 * slots a night, so a strict 3-day window would need nine distinct games and
 * only has eight. The ladder already answers that - it NARROWS to 2 on the days
 * arithmetic forbids 3 - so the board still refuses yesterday's class every
 * night, which is the promise players actually feel. Add a ninth rotating class
 * and no-repeat-3 binds again with no code change.
 * ==========================================================================*/

import { makeRng } from './rng.js';

/* ----------------------------------------------------------------------------
 * TUNABLES - one block, playtest-facing.
 * -------------------------------------------------------------------------- */
export const HOMEROOM_KEY = 'daily_trigger';
export const CLASSES_PER_DAY = 4;
/** GROUND-RULES §3 amendment 3 + DECISIONS #3. */
export const FAMILIES = Object.freeze(['word', 'memory', 'search', 'tracking', 'reflex', 'comfort']);
/* THE CEILINGS ARE BOTH 300 NOW, AND `meaty` NO LONGER MEANS "LONG" (owner
 * ruling, the class-length wave). DECISIONS #3 used to read "one meaty (<=300s)
 * + three quick (<=180s)", and the two constants were the two halves of that
 * sentence. They are not any more:
 *   - THE BUDGET IS PER-MODULE. Each game's own `timeBudgetSec` is the class
 *     length; the timetable never shortens a class to fit a slot. So a
 *     non-meaty class may legitimately run 300s (Anomaly, Deja Vu) and a meaty
 *     one may be shorter than a quick one (Instant Recall's 180s).
 *   - `meaty` IS THE ANCHOR-SLOT FLAG. It says "this class is the one the night
 *     is built around", which is what meatyOk/meatyCapOk below deal - exactly
 *     one anchor a day, never two. Nothing about it is a duration any more.
 * Both constants therefore hold the SAME number and clampBudget's `meaty`
 * argument is a no-op today; they stay separate so a future ruling can move one
 * ceiling without hunting every call site. */
export const MEATY_MAX_SEC = 300;
export const QUICK_MAX_SEC = 300;
export const MIN_BUDGET_SEC = 30;
/** Mockup's departure-board periods. */
export const PERIOD_LABELS = Object.freeze(['09:00', '09:05', '09:10', '09:15']);

/** Anchor for the history walk. NEVER move this: it would reshuffle every past day. */
export const EPOCH = '2026-08-01';
const MAX_WALK_DAYS = 4000;          // ~11 years of epoch-anchored history
const NO_REPEAT_DAYS = 3;
/** no-repeat NARROWS before it switches off (see pickDay). */
const NO_REPEAT_WINDOWS = Object.freeze([NO_REPEAT_DAYS, 2, 1, 0]);
const FLAGSHIP_WINDOW_DAYS = 7;
const FLAGSHIP_MAX_PER_WINDOW = 2;   // "flagship ~2x/week"

/** Seeded weights (see header). Preferences that outlive constraint relaxation. */
const W_FLAGSHIP_UNDER_QUOTA = 3.0;
const W_FLAGSHIP_OVER_QUOTA = 0.15;
const W_PLAYED_DAYS_AGO = Object.freeze({ 1: 0.10, 2: 0.35, 3: 0.70 });

const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

/* ----------------------------------------------------------------------------
 * DATE MATH - UTC only, on 'YYYY-MM-DD' strings.
 * -------------------------------------------------------------------------- */
/** @returns {number} UTC-midnight ms, or NaN when the string is not a date. */
export function parseDay(s) {
  if (!DATE_RE.test(String(s || ''))) return NaN;
  const [y, m, d] = String(s).split('-').map(Number);
  const ms = Date.UTC(y, m - 1, d);
  // Reject impossible dates (2026-02-31) - Date.UTC would roll them over.
  const back = fmtDay(ms);
  return back === s ? ms : NaN;
}

/** @returns {string} 'YYYY-MM-DD' for a UTC ms stamp. */
export function fmtDay(ms) {
  const d = new Date(ms);
  const p = (n) => String(n).padStart(2, '0');
  return d.getUTCFullYear() + '-' + p(d.getUTCMonth() + 1) + '-' + p(d.getUTCDate());
}

/** Shift a 'YYYY-MM-DD' by n days. Returns the input unchanged if unparseable. */
export function dayAdd(s, n) {
  const ms = parseDay(s);
  if (!Number.isFinite(ms)) return s;
  return fmtDay(ms + Math.round(n) * 86400000);
}

/** Whole days from a to b (b - a). NaN-safe: returns 0 on a bad input. */
export function daysBetween(a, b) {
  const x = parseDay(a), y = parseDay(b);
  if (!Number.isFinite(x) || !Number.isFinite(y)) return 0;
  return Math.round((y - x) / 86400000);
}

/* ----------------------------------------------------------------------------
 * POOL NORMALIZATION
 * -------------------------------------------------------------------------- */
/**
 * A budget is the MODULE's to declare; this only fences it into the legal band.
 * Both ceilings are 300 (see the TUNABLES note), so `meaty` changes nothing
 * today - it is kept because the ceiling is per class TYPE by contract, not
 * because an anchor class is allowed to be longer than a quick one.
 * A missing / junk / non-positive value falls back to 120s, the ordinary class
 * length, rather than to whatever the ceiling happens to be.
 */
function clampBudget(v, meaty) {
  const max = meaty ? MEATY_MAX_SEC : QUICK_MAX_SEC;
  const n = Number(v);
  if (!Number.isFinite(n) || n <= 0) return Math.min(120, max);
  return Math.max(MIN_BUDGET_SEC, Math.min(max, Math.round(n)));
}

/**
 * Accepts an array of descriptors or a key->descriptor map. Sorted by key so
 * the result never depends on Promise.allSettled completion order - a registry
 * that resolved in a different order must not deal a different timetable.
 */
function normalizePool(registry) {
  const list = Array.isArray(registry)
    ? registry
    : (registry && typeof registry === 'object' ? Object.keys(registry).map((k) => registry[k]) : []);
  const seen = new Set();
  const out = [];
  for (const g of list) {
    if (!g || !g.key || seen.has(String(g.key))) continue;
    seen.add(String(g.key));
    const meaty = !!g.meaty;
    out.push(Object.freeze({
      key: String(g.key),
      family: String(g.family || 'misc'),
      meaty,
      flagship: !!g.flagship,
      timeBudgetSec: clampBudget(g.timeBudgetSec, meaty),
      /* CLOCKLESS (owner ruling, the class-length wave). A class that keeps a
       * budget for its own bell but shows NO seconds anywhere: no board chip,
       * no room-card chip, no proctor chip, and no time bar. Daily Trigger is
       * the only one today - a one-minute wordle whose ritual is the board, not
       * the countdown. It is a DESCRIPTOR fact carried through to the dealt
       * class (classFrom) because the shell reads the class, never the module. */
      clockless: !!g.clockless,
    }));
  }
  out.sort((a, b) => (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
  return out;
}

/* ----------------------------------------------------------------------------
 * HISTORY HELPERS - read the accumulated walk, never a store.
 * -------------------------------------------------------------------------- */
/** How many of the `window` days BEFORE `date` served `key`. */
function countIn(history, date, window, key) {
  let n = 0;
  for (let i = 1; i <= window; i++) {
    const day = history.get(dayAdd(date, -i));
    if (day && day.keys.has(key)) n++;
  }
  return n;
}

/** How many days ago `key` was last served (1..window), or 0 if not in window. */
function daysAgo(history, date, window, key) {
  for (let i = 1; i <= window; i++) {
    const day = history.get(dayAdd(date, -i));
    if (day && day.keys.has(key)) return i;
  }
  return 0;
}

/* ----------------------------------------------------------------------------
 * THE PICK
 * -------------------------------------------------------------------------- */
/**
 * NEVER TWO MEATY CLASSES ON ONE BOARD. This is the half of DECISIONS #3 that
 * does NOT relax: a night is built around exactly ONE anchor class, and two
 * anchors back to back is a different night than the one that was designed.
 * NOTE WHAT IT NO LONGER GUARANTEES (the class-length wave): the cap counts
 * ANCHORS, not seconds, and since budgets went per-module a board can carry two
 * or three 300s classes as long as only one of them is flagged. If the night's
 * total length ever needs a ceiling, that is a NEW rule to write here - this
 * one was never it, it only used to look like it when meaty meant long.
 *
 * It has to be separate from meatyOk below because relaxation drops a whole
 * hard filter, and the old single predicate carried BOTH halves - so the moment
 * a four-slot board relaxed 'meaty' to fill its last seat, it lost the cap as
 * well and could deal two. (Measured the day CLASSES_PER_DAY went to 4: the
 * shipped nine-class pool dealt two meaty classes on real nights.) The cap is
 * applied to the candidate list BEFORE the ladder, so nothing can yield it.
 */
function meatyCapOk(g, chosen) {
  return !g.meaty || !chosen.some((c) => c.meaty);
}

/**
 * BOOK one meaty class per day, when the pool has one to book. This is the half
 * that DOES relax (the ladder drops it under the name 'meaty'): a pool whose
 * only meaty games are all inside the no-repeat window legitimately deals a
 * night without one, because no-repeat outranks it.
 */
function meatyOk(g, chosen, meatyAvailable) {
  if (!meatyAvailable) return true;                        // no meaty games -> no-op
  if (chosen.some((c) => c.meaty)) return !g.meaty;        // already booked the meaty slot
  const slotsLeftIncludingThis = CLASSES_PER_DAY - chosen.length;
  if (slotsLeftIncludingThis <= 1) return g.meaty;         // last chance to book it
  return true;
}

function weightFor(g, chosen, history, date) {
  let w = 1;
  if (g.flagship) {
    const prior = countIn(history, date, FLAGSHIP_WINDOW_DAYS, g.key);
    w *= (prior < FLAGSHIP_MAX_PER_WINDOW) ? W_FLAGSHIP_UNDER_QUOTA : W_FLAGSHIP_OVER_QUOTA;
  }
  const ago = daysAgo(history, date, NO_REPEAT_DAYS, g.key);
  if (ago && W_PLAYED_DAYS_AGO[ago] != null) w *= W_PLAYED_DAYS_AGO[ago];
  if (chosen.some((c) => c.family === g.family)) w *= 0.25;   // family preference survives relaxation
  return w > 0 ? w : 0.01;
}

function weightedPick(list, rng, chosen, history, date) {
  let total = 0;
  const weights = list.map((g) => {
    const w = weightFor(g, chosen, history, date);
    total += w;
    return w;
  });
  let r = rng() * total;
  for (let i = 0; i < list.length; i++) {
    r -= weights[i];
    if (r <= 0) return list[i];
  }
  return list[list.length - 1];
}

function classFrom(g, slot) {
  return {
    slot,
    gameKey: g.key,
    family: g.family,
    meaty: g.meaty,
    flagship: g.flagship,
    homeroom: slot === 1,
    timeBudgetSec: g.timeBudgetSec,
    clockless: !!g.clockless,          // see normalizePool: no chip, no bar
    timeLabel: PERIOD_LABELS[slot - 1] || PERIOD_LABELS[PERIOD_LABELS.length - 1],
    missing: false,
  };
}

/** overrideCalendar entries replace the seeded day VERBATIM (BUILD-CONTRACT §7). */
function fromOverride(date, entry, pool) {
  const byKey = new Map(pool.map((g) => [g.key, g]));
  const raw = Array.isArray(entry.classes) ? entry.classes : [];
  const classes = [];
  for (let i = 0; i < raw.length && i < 8; i++) {
    const item = raw[i];
    const key = String((item && (item.gameKey || item.key)) || item || '');
    if (!key) continue;
    const g = byKey.get(key);
    const slot = classes.length + 1;
    if (g) {
      const c = classFrom(g, slot);
      // A calendar may shorten a class but never exceed its class-type ceiling.
      if (item && item.timeBudgetSec) c.timeBudgetSec = clampBudget(item.timeBudgetSec, g.meaty);
      classes.push(c);
    } else {
      // Calendar names a game this build does not ship: keep the row (the day is
      // verbatim) but flag it so the shell renders class_suspended instead of
      // blanking the board.
      /* 120, LITERALLY, not QUICK_MAX_SEC. The ceiling is 300 now, and a
       * class that could not open must not advertise the longest chip on the
       * board - the row exists only so the shell can render class_suspended.
       * 120s is the ordinary class length, the same floor clampBudget uses. */
      classes.push({
        slot, gameKey: key, family: 'misc', meaty: false, flagship: false,
        homeroom: slot === 1, timeBudgetSec: 120, clockless: false,
        timeLabel: PERIOD_LABELS[slot - 1] || PERIOD_LABELS[PERIOD_LABELS.length - 1],
        missing: true,
      });
    }
  }
  return {
    dateSeed: date,
    source: 'override',
    banner: (entry && typeof entry.banner === 'string') ? entry.banner : null,
    relaxed: [],
    noRepeatWindow: NO_REPEAT_DAYS,
    classes,
    keys: new Set(classes.map((c) => c.gameKey)),
  };
}

function pickDay(date, pool, history, calendar) {
  const ov = calendar && Object.prototype.hasOwnProperty.call(calendar, date) ? calendar[date] : null;
  if (ov && typeof ov === 'object') return fromOverride(date, ov, pool);

  const rng = makeRng('arcademy|timetable|' + date);
  const chosen = [];
  const relaxed = [];
  let noRepeatWindow = NO_REPEAT_DAYS;
  const meatyAvailable = pool.some((g) => g.meaty);

  const homeroom = pool.find((g) => g.key === HOMEROOM_KEY) || pool[0] || null;
  if (homeroom) chosen.push(homeroom);

  while (chosen.length < CLASSES_PER_DAY) {
    const taken = new Set(chosen.map((c) => c.key));
    // The meaty CAP is applied here, outside the relaxation ladder, because it is
    // the one rule that must not yield (see meatyCapOk). It can never empty the
    // list on its own: it only ever hides meaty games, and it only does that once
    // a meaty game is already booked.
    let candidates = pool.filter((g) => !taken.has(g.key) && meatyCapOk(g, chosen));
    if (!candidates.length) candidates = pool.filter((g) => !taken.has(g.key));
    if (!candidates.length) break;   // pool smaller than a full day - short board, never a crash

    /* Relaxation order is law (BUILD-CONTRACT §7): flagship yields first, then
     * meaty, then family, and no-repeat yields LAST - and when it finally has to
     * yield it NARROWS (3 -> 2 -> 1 -> off) instead of switching off, so a pool
     * too small for no-repeat-3 still refuses yesterday's class for as long as
     * arithmetic allows. That is what keeps the 4-game V1 board rotating instead
     * of coin-flipping. */
    const hard = [
      ['flagship', (g) => !g.flagship
        || countIn(history, date, FLAGSHIP_WINDOW_DAYS, g.key) < FLAGSHIP_MAX_PER_WINDOW],
      ['meaty', (g) => meatyOk(g, chosen, meatyAvailable)],
      ['family', (g) => !chosen.some((c) => c.family === g.family)],
    ];
    const attempts = [];
    for (let d = 0; d <= hard.length; d++) attempts.push({ drop: d, window: NO_REPEAT_DAYS });
    for (let i = 1; i < NO_REPEAT_WINDOWS.length; i++) {
      attempts.push({ drop: hard.length, window: NO_REPEAT_WINDOWS[i] });
    }

    let survivors = [];
    for (const a of attempts) {
      const active = hard.slice(a.drop);
      const list = candidates.filter((g) => active.every(([, f]) => f(g))
        && (a.window === 0 || countIn(history, date, a.window, g.key) === 0));
      if (!list.length) continue;
      for (const [name] of hard.slice(0, a.drop)) {
        if (relaxed.indexOf(name) < 0) relaxed.push(name);
      }
      if (a.window < NO_REPEAT_DAYS && relaxed.indexOf('no-repeat') < 0) relaxed.push('no-repeat');
      if (a.window < noRepeatWindow) noRepeatWindow = a.window;
      survivors = list;
      break;
    }
    if (!survivors.length) survivors = candidates;   // unreachable (window 0 passes all), belt-and-braces

    chosen.push(weightedPick(survivors, rng, chosen, history, date));
  }

  const classes = chosen.map((g, i) => classFrom(g, i + 1));
  return {
    dateSeed: date,
    source: 'seed',
    banner: null,
    relaxed,
    noRepeatWindow,
    classes,
    keys: new Set(classes.map((c) => c.gameKey)),
  };
}

/* ----------------------------------------------------------------------------
 * THE WALK (+ memo). Pure: same inputs -> same output, cache or no cache.
 * -------------------------------------------------------------------------- */
const walkCache = new Map();
const WALK_CACHE_MAX = 8;

function signature(pool, calendar) {
  const p = pool.map((g) => `${g.key}:${g.family}:${g.meaty ? 1 : 0}${g.flagship ? 'F' : ''}${g.clockless ? 'C' : ''}:${g.timeBudgetSec}`).join(',');
  // Contents, not just date keys: two calendars naming the same date must not
  // share a memo entry (a key-only signature served the first one's board).
  const c = calendar
    ? Object.keys(calendar).sort().map((k) => k + '=' + JSON.stringify(calendar[k])).join(',')
    : '';
  return p + '|' + c;
}

function walk(target, pool, calendar) {
  const key = target + '|' + signature(pool, calendar);
  const hit = walkCache.get(key);
  if (hit) return hit;

  const days = new Map();
  const span = daysBetween(EPOCH, target);
  let cursor;
  if (!Number.isFinite(parseDay(target))) {
    cursor = target;                                  // unparseable seed: single day, no history
  } else if (span < 0) {
    cursor = target;                                  // before the epoch: no history to derive
  } else if (span > MAX_WALK_DAYS) {
    cursor = dayAdd(target, -MAX_WALK_DAYS);          // pathology guard (see header)
  } else {
    cursor = EPOCH;
  }

  for (let guard = 0; guard <= MAX_WALK_DAYS + 2; guard++) {
    const day = pickDay(cursor, pool, days, calendar);
    days.set(cursor, day);
    if (cursor === target) break;
    const next = dayAdd(cursor, 1);
    if (next === cursor) break;                       // unparseable: dayAdd is identity
    cursor = next;
  }

  if (walkCache.size >= WALK_CACHE_MAX) walkCache.clear();
  walkCache.set(key, days);
  return days;
}

/** Drop the memo (tests / a registry hot-swap). Never needed in normal use. */
export function clearTimetableCache() { walkCache.clear(); }

/* ----------------------------------------------------------------------------
 * PUBLIC API
 * -------------------------------------------------------------------------- */
/**
 * Build one day's timetable.
 *
 * @param {Object}  o
 * @param {string}  o.dateSeed          init.utcDateSeed ('YYYY-MM-DD')
 * @param {Array|Object} o.registry     game descriptors {key, family, meaty, flagship,
 *                                      timeBudgetSec, clockless}
 * @param {Object=} o.overrideCalendar  {'2026-09-01': {classes:[...], banner:'...'}} or null
 * @returns {{dateSeed:string, source:'seed'|'override', banner:?string,
 *            relaxed:string[], classes:Array<Object>}}
 */
export function buildTimetable({ dateSeed, registry, overrideCalendar } = {}) {
  const pool = normalizePool(registry);
  const date = String(dateSeed || EPOCH);
  const calendar = (overrideCalendar && typeof overrideCalendar === 'object') ? overrideCalendar : null;

  if (!pool.length) {
    return { dateSeed: date, source: 'seed', banner: null, relaxed: [], noRepeatWindow: NO_REPEAT_DAYS, classes: [] };
  }

  const days = walk(date, pool, calendar);
  const day = days.get(date) || pickDay(date, pool, new Map(), calendar);
  // Hand out a copy without the internal `keys` Set (the walk's own bookkeeping).
  return {
    dateSeed: day.dateSeed,
    source: day.source,
    banner: day.banner,
    relaxed: day.relaxed.slice(),
    noRepeatWindow: day.noRepeatWindow,
    classes: day.classes.map((c) => Object.assign({}, c)),
  };
}

/**
 * The prior days the constraints were derived from - diagnostics and tests only.
 * Returns oldest-first, at most `count` days ending the day BEFORE dateSeed.
 */
export function priorDays({ dateSeed, registry, overrideCalendar, count } = {}) {
  const pool = normalizePool(registry);
  const date = String(dateSeed || EPOCH);
  const calendar = (overrideCalendar && typeof overrideCalendar === 'object') ? overrideCalendar : null;
  if (!pool.length) return [];
  const days = walk(date, pool, calendar);
  const n = Math.max(0, Math.min(30, Math.round(count || NO_REPEAT_DAYS)));
  const out = [];
  for (let i = n; i >= 1; i--) {
    const d = days.get(dayAdd(date, -i));
    if (d) out.push({ dateSeed: d.dateSeed, classes: d.classes.map((c) => Object.assign({}, c)) });
  }
  return out;
}

export default buildTimetable;
