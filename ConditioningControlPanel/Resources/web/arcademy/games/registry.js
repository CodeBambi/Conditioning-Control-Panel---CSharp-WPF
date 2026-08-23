/* ============================================================================
 * games/registry.js - the guarded registry (BUILD-CONTRACT §11).
 *
 * Imports every OPEN class with Promise.allSettled, exactly like
 * intake/render/captcha/index.js (Semester I's four, Semester II's three,
 * Semester III's three): one broken module NEVER blocks the others and NEVER
 * throws. A failed import logs and leaves a STUB in its slot whose class renders
 * `class_suspended` - the shell must always be able to deal a board, because a
 * blank board is indistinguishable from a dead app.
 *
 * THE SEMESTER GATE. `GAME_SEMESTER` says which semester each class belongs to
 * and `OPEN_SEMESTERS` says which semesters have opened; `loadGames` imports
 * ONLY the open ones. A closed semester is not a suspended class - it is simply
 * ABSENT from the pool, so the timetable never deals it and shell/campus.js
 * keeps that wing taped shut. That is the one-line release gate: ship a semester
 * dark by dropping its number from the set, open it by putting the number back.
 *
 * STATIC DESCRIPTORS. The timetable needs {family, meaty, flagship, timeBudgetSec}
 * for a game even when its module failed to load, so they live here as a fallback
 * table; a module that loads successfully overrides them with its own exported
 * values (the module is the source of truth, this table is the parachute).
 *
 * TIER PROMOTION (shell rule, not per-game): tier = 1 + floor(promotions / 2),
 * capped at 4; a class promotes on an S or an A. Stored per game in the meta
 * store, so it survives across sessions and never rides a game's own state.
 * ==========================================================================*/

import { isPromoting } from '../core/grades.js';

/** key -> module path. Games NEVER import each other; only this file imports them. */
export const GAME_PATHS = Object.freeze({
  daily_trigger: './daily-trigger/index.js',
  lost_and_found: './lost-and-found/index.js',
  deja_vu: './deja-vu/index.js',
  impulse_control: './impulse-control/index.js',
  misdirection: './misdirection/index.js',
  sort: './sort/index.js',
  echo: './echo/index.js',
  instant_recall: './instant-recall/index.js',
  anomaly: './anomaly/index.js',
  composure: './composure/index.js',
  the_deep_end: './the-deep-end/index.js',
});

/** Fallback descriptors (see header). Families per SYNTHESIS #3. */
export const GAME_META = Object.freeze({
  daily_trigger: { family: 'word', meaty: false, flagship: true, timeBudgetSec: 90 },
  // MEATY: mirrors the module's own export. This table is only the parachute the
  // suspended stub flies under, but the timetable reads a suspended class's
  // descriptor too, so a mismatch would quietly change the day's shape on the one
  // day the import fails.
  lost_and_found: { family: 'search', meaty: true, flagship: true, timeBudgetSec: 120 },
  deja_vu: { family: 'memory', meaty: false, flagship: false, timeBudgetSec: 90 },
  impulse_control: { family: 'reflex', meaty: false, flagship: false, timeBudgetSec: 90 },
  // MEATY, same law as lost_and_found above: this row must equal the module's own
  // exports, because the timetable reads a SUSPENDED class's descriptor too and a
  // mismatch would quietly reshape the day on the one day the import fails.
  // The Deep End is the second meaty class (DECISIONS #3's 300s slot) and the first
  // Semester III class brought forward - family 'comfort', never a flagship.
  the_deep_end: { family: 'comfort', meaty: true, flagship: false, timeBudgetSec: 300 },
  /* Semester II. Families per SYNTHESIS #3; 'recall' joins the list with Instant
     Recall (core/lexicon.js has no family_recall/family_puzzle row yet, so the
     chip degrades to the de-snaked key - readable English, never a raw token). */
  misdirection: { family: 'tracking', meaty: false, flagship: false, timeBudgetSec: 120 },
  /* SORT inherited Misdirection's family, not its room (lot 2 razed the
     parlour; sort is room 203 by the Main Gate). Not meaty: 120s is the
     class, and the pile-picking DOOR runs outside the clock (shell S3), so the
     budget here is the sorting itself and nothing else. */
  sort: { family: 'tracking', meaty: false, flagship: false, timeBudgetSec: 120 },
  echo: { family: 'memory', meaty: false, flagship: false, timeBudgetSec: 105 },
  // MEATY (ruled 2026-08-23): the third meaty class. With ten games no-repeat-3 binds again
  // and outranks the meaty preference, so two meaty classes left ~a third of nights with the
  // slot unfilled (trap 6 re-opened); see composure below for the full count. Mirrors the module's own export.
  instant_recall: { family: 'recall', meaty: true, flagship: false, timeBudgetSec: 120 },
  /* Semester III (The Deep End above was brought forward). */
  anomaly: { family: 'search', meaty: false, flagship: false, timeBudgetSec: 90 },
  // MEATY (ruled 2026-08-23, same reason as instant_recall): four meaty classes (L&F, The Deep
  // End, Instant Recall, Composure) are what it takes for a no-repeat-3 pool to deal one meaty
  // class EVERY night (measured 28/28; three gave 21/28). Mirrors the module's own export.
  composure: { family: 'puzzle', meaty: true, flagship: false, timeBudgetSec: 120 },
});

/**
 * Which semester each class belongs to. A key missing from this table is treated
 * as Semester I (open by default) - a new game is never accidentally invisible.
 */
export const GAME_SEMESTER = Object.freeze({
  daily_trigger: 1,
  lost_and_found: 1,
  deja_vu: 1,
  impulse_control: 1,
  misdirection: 2,
  sort: 2,
  echo: 2,
  instant_recall: 2,
  anomaly: 3,
  composure: 3,
  the_deep_end: 3,
});

/**
 * The semesters that have opened. THE release gate - see the header. Drop a
 * number and every class in it leaves the pool (no stub, no board row) and
 * shell/campus.js re-tapes that wing; put it back and the wing opens.
 */
export const OPEN_SEMESTERS = new Set([1, 2, 3]);

/**
 * RETIRED CLASSES (owner ruling 2026-08-23). A class whose semester is open but
 * which has been pulled from the deal: it is ABSENT exactly like a closed
 * semester's games (no stub, no board row, no campus room - the wing stays
 * open for its neighbours), so the module can stay on disk while a
 * replacement is designed. Misdirection was retired as "boring" after the
 * owner's play-test; the lot-2 geography rework then razed The Parlour
 * outright (the front office stands there), so un-retiring a class now also
 * means giving it a room in shell/campus.js ROOMS.
 */
export const RETIRED_GAMES = new Set(['misdirection']);

/** True when a class's semester has opened (unknown keys are Semester I) AND
 *  the class has not been retired - this is the ONE gate the registry pool,
 *  the timetable and shell/campus.js all read, so they can never disagree. */
export function isOpenSemester(key) {
  if (RETIRED_GAMES.has(key)) return false;
  return OPEN_SEMESTERS.has(GAME_SEMESTER[key] || 1);
}

export const MAX_TIER = 4;
const PROMOTIONS_PER_TIER = 2;

/* ----------------------------------------------------------------------------
 * TIER MATH
 * -------------------------------------------------------------------------- */
/** tier = 1 + floor(promotions / 2), capped at MAX_TIER. */
export function tierForPromotions(promotions) {
  const p = Math.max(0, Math.round(Number(promotions) || 0));
  return Math.min(MAX_TIER, 1 + Math.floor(p / PROMOTIONS_PER_TIER));
}

/** The tier a game should be played at, from its meta-store row. */
export function tierFor(gameMeta) {
  const m = gameMeta || {};
  if (m.tier != null) {
    const t = Math.round(Number(m.tier));
    if (Number.isFinite(t) && t >= 1) return Math.min(MAX_TIER, t);
  }
  return tierForPromotions(m.promotions);
}

/**
 * Apply one class result to a game's meta row.
 * @returns {{tier:number, promotions:number, played:number, best:string,
 *            lastGrade:string, promoted:boolean}}
 */
export function advance(gameMeta, grade) {
  const m = gameMeta || {};
  const promoted = isPromoting(grade);
  const promotions = Math.max(0, Math.round(Number(m.promotions) || 0)) + (promoted ? 1 : 0);
  const beforeTier = tierFor(m);
  const tier = tierForPromotions(promotions);
  const best = betterGrade(m.best, grade);
  return {
    tier,
    promotions,
    played: Math.max(0, Math.round(Number(m.played) || 0)) + 1,
    best,
    lastGrade: String(grade || ''),
    promoted: tier > beforeTier,
  };
}

function betterGrade(a, b) {
  const order = ['S', 'A', 'B', 'C'];
  const ai = order.indexOf(String(a || '').toUpperCase());
  const bi = order.indexOf(String(b || '').toUpperCase());
  if (ai < 0) return bi < 0 ? (a || b || '') : String(b).toUpperCase();
  if (bi < 0) return String(a).toUpperCase();
  return order[Math.min(ai, bi)];
}

/* ----------------------------------------------------------------------------
 * THE class_suspended STUB - what a dead slot plays instead of nothing.
 * -------------------------------------------------------------------------- */
function suspendedStub(key, reason) {
  return {
    key,
    family: (GAME_META[key] && GAME_META[key].family) || 'misc',
    meaty: !!(GAME_META[key] && GAME_META[key].meaty),
    flagship: !!(GAME_META[key] && GAME_META[key].flagship),
    timeBudgetSec: (GAME_META[key] && GAME_META[key].timeBudgetSec) || 90,
    suspended: true,
    manifest: { effectsConsumed: [], assetNeeds: null, boardSizes: null, keybinds: null, settings: [], peek: false },
    create(ctx) {
      return {
        start() {
          try {
            const root = ctx && ctx.root;
            if (!root) return;
            root.textContent = '';
            const wrap = document.createElement('div');
            wrap.className = 'arc-suspended';
            const h = document.createElement('h2');
            h.className = 'arc-h2';
            h.textContent = ctx.lexicon ? ctx.lexicon('class_suspended', 'Class Suspended') : 'Class Suspended';
            const p = document.createElement('p');
            p.className = 'arc-note';
            p.textContent = 'This class could not open. Your attendance is safe.';
            wrap.appendChild(h);
            wrap.appendChild(p);
            root.appendChild(wrap);
          } catch (e) { /* a stub that throws would defeat its own purpose */ }
          if (ctx && ctx.log) ctx.log('class_suspended stub for ' + key + ': ' + reason);
        },
        pause() {}, resume() {}, suspend() {}, destroy() {},
      };
    },
  };
}

/* ----------------------------------------------------------------------------
 * THE LOAD
 * -------------------------------------------------------------------------- */
function validate(mod) {
  const m = (mod && mod.default && typeof mod.default.create === 'function') ? mod.default : mod;
  if (!m || typeof m.create !== 'function') return null;
  return m;
}

/**
 * Load every game whose semester has opened. Never rejects.
 * @param {Function=} log
 * @returns {Promise<{list:Array, byKey:Object, ok:number, failed:string[]}>}
 */
export async function loadGames(log) {
  const say = typeof log === 'function' ? log : () => {};
  const keys = Object.keys(GAME_PATHS);
  /* THE SEMESTER GATE. Filtered IN PLACE so the two lines around it keep their
   * exact shape (the scratch shell-suite harness patches this function by
   * literal text to register its fixture class). A closed semester's games are
   * ABSENT - never a class_suspended stub, which is reserved for a class that
   * exists and could not open. */
  for (let i = keys.length - 1; i >= 0; i--) {
    if (!isOpenSemester(keys[i])) keys.splice(i, 1);
  }

  const settled = await Promise.allSettled(keys.map((k) => import(GAME_PATHS[k])));

  const list = [];
  const byKey = Object.create(null);
  const failed = [];

  settled.forEach((res, i) => {
    const key = keys[i];
    const fallback = GAME_META[key] || {};
    let mod = null;
    let reason = '';

    if (res.status === 'fulfilled') {
      mod = validate(res.value);
      if (!mod) reason = 'module has no create()';
    } else {
      reason = (res.reason && (res.reason.message || String(res.reason))) || 'import rejected';
    }

    if (!mod) {
      failed.push(key);
      say('game ' + key + ' failed to load (' + reason + ') - class_suspended stub installed');
      mod = suspendedStub(key, reason);
    }
    if (mod.key && mod.key !== key) {
      say('game ' + key + ' declares key "' + mod.key + '" - registry key wins');
    }

    const entry = {
      key,
      ok: !mod.suspended,
      error: mod.suspended ? reason : null,
      mod,
      // Module values win; the static table is the parachute.
      family: mod.family || fallback.family || 'misc',
      meaty: mod.meaty != null ? !!mod.meaty : !!fallback.meaty,
      flagship: mod.flagship != null ? !!mod.flagship : !!fallback.flagship,
      timeBudgetSec: mod.timeBudgetSec || fallback.timeBudgetSec || 90,
    };
    list.push(entry);
    byKey[key] = entry;
  });

  say('registry: ' + (keys.length - failed.length) + '/' + keys.length + ' classes live'
    + (failed.length ? ' (suspended: ' + failed.join(', ') + ')' : ''));

  return { list, byKey, ok: keys.length - failed.length, failed };
}

/** Descriptors for core/timetable.js - never the modules themselves. */
export function descriptors(list) {
  return (Array.isArray(list) ? list : []).map((e) => ({
    key: e.key, family: e.family, meaty: e.meaty, flagship: e.flagship, timeBudgetSec: e.timeBudgetSec,
  }));
}

export { suspendedStub };
export default loadGames;
