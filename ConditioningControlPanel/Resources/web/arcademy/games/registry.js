/* ============================================================================
 * games/registry.js - the guarded registry (BUILD-CONTRACT §11).
 *
 * Imports the four Semester-1 games with Promise.allSettled, exactly like
 * intake/render/captcha/index.js: one broken module NEVER blocks the others and
 * NEVER throws. A failed import logs and leaves a STUB in its slot whose class
 * renders `class_suspended` - the shell must always be able to deal a board,
 * because a blank board is indistinguishable from a dead app.
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
});

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
 * Load every game. Never rejects.
 * @param {Function=} log
 * @returns {Promise<{list:Array, byKey:Object, ok:number, failed:string[]}>}
 */
export async function loadGames(log) {
  const say = typeof log === 'function' ? log : () => {};
  const keys = Object.keys(GAME_PATHS);

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
