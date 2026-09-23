/* ============================================================================
 * ui/duel/games.js - which Arcademy class a game card holds. PURE: no DOM.
 *
 * Owner, 2026-09-23: "I want the actual game ... The grid with the real tiles
 * and real effects from the Deep End game - for the sort the card swiping ui,
 * etc - everything directly in that window as is on the arcademy". So a duel
 * no longer runs a board of its own: it mounts the REAL Arcademy class inside
 * the Goon window (ui/duel/arcademyHost.js). This table is the whole list of
 * classes a card can hold. Adding a game is one row here:
 *
 *   id      the Arcademy game key (the folder under arcademy/games/)
 *   name    what the intro card and the result say
 *   rule    one line under the name
 *   path    the class module, relative to THIS file (ccp.game maps the whole
 *           Resources/web folder, so the goon page reaches the arcademy tree
 *           same-origin)
 *   tiled   true = the result is {tile, score}, highest tile first (The Deep
 *           End's natural result); false = {score} only
 *   bot     what the practice bot plausibly reaches, as [low, high] ranges
 *           (it cannot play a DOM game, so it reports a believable number
 *           drawn from these, seeded)
 *   lib     optional extra module (relative to this file) handed to result()
 *   result  (instance, {report, lib}) -> {score, tile?}: the class's OWN
 *           result, read off its diagnostics at the duel bell
 *
 * WIRE: the throw frame carries `game` (an id from this table). A frame with
 * no game id is from a build that only knew The Deep End, so it means
 * deep-end. A receiver that does not know an id refuses the throw ('busy').
 * ==========================================================================*/

export const DEFAULT_DUEL_GAME = 'the-deep-end';

export const DUEL_GAMES = Object.freeze([
  Object.freeze({
    id: 'the-deep-end',
    name: 'The Deep End',
    rule: 'Swipe or arrow keys. Deepest tile wins.',
    path: '../../../arcademy/games/the-deep-end/index.js',
    tiled: true,
    // tier reached and score per second of play at a novice-to-steady pace
    bot: Object.freeze({ tileAt60: [5, 7], tileAt120: [6, 8], scorePerSec: [6, 16] }),
    // The dive's best tier (banked across resurfaces) and the running score.
    result(instance) {
      const s = instance && typeof instance.snapshot === 'function' ? instance.snapshot() : null;
      return { tile: s ? s.bestDeepest : 0, score: s ? s.score : 0 };
    },
  }),
  Object.freeze({
    id: 'sort',
    name: 'Sort',
    rule: 'Swipe right for moving, left for still.',
    path: '../../../arcademy/games/sort/index.js',
    tiled: false,
    // points per second: a composite of about .35 to .75 over a short bell
    bot: Object.freeze({ scoreRange: [320, 760] }),
    lib: '../../../arcademy/games/sort/grade.js',
    // The class's own composite (accuracy, tempo, perfect share), x1000 so it reads as points.
    result(instance, { lib } = {}) {
      const d = instance && typeof instance.diagnostics === 'function' ? instance.diagnostics() : null;
      if (!d || !d.live || !lib || typeof lib.gradeClass !== 'function') return { score: 0 };
      const g = lib.gradeClass({
        correct: d.correct, wrong: d.wrong, perfect: d.perfect, passed: d.passed,
        bestRung: d.bestRung, rungCap: d.rungCap, longestChain: d.longestChain,
      });
      return { score: (Number(g && g.composite) || 0) * 1000 };
    },
  }),
]);

const BY_ID = new Map(DUEL_GAMES.map((g) => [g.id, g]));

/** A game row, or null when this build does not have it. */
export function duelGame(id) {
  return BY_ID.get(normalizeGameId(id)) || null;
}

/** A frame's game id: missing means The Deep End (older builds knew only it). */
export function normalizeGameId(id) {
  if (id == null || id === '') return DEFAULT_DUEL_GAME;
  return String(id);
}

/** Does this build have that game? */
export function knownGame(id) {
  return BY_ID.has(normalizeGameId(id));
}

/**
 * The game a thrown card holds. Deterministic in (seed, idx) so a replay of the
 * same match deals the same card, and it alternates rather than repeats.
 * @param {bigint|string|number} seed any seed; only its low bits are read
 */
export function pickDuelGame(seed, idx) {
  let n = 0;
  try { n = Number(BigInt.asUintN(16, BigInt(seed || 0))); } catch (_e) { n = 0; }
  const i = (n + Math.max(0, idx | 0)) % DUEL_GAMES.length;
  return DUEL_GAMES[i].id;
}

/**
 * The practice bot's result for a game: a believable number, never a played
 * one. `rand` is () => [0,1), seeded by the caller.
 * @returns {{game:string, score:number, tile?:number}}
 */
export function botResult(id, lenSec, rand = Math.random) {
  const g = duelGame(id) || duelGame(DEFAULT_DUEL_GAME);
  const r = () => { const v = Number(rand()); return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0.5; };
  const span = (a) => a[0] + r() * (a[1] - a[0]);
  const len = Math.max(20, Number(lenSec) || 60);
  if (g.tiled) {
    const k = Math.min(1, Math.max(0, (len - 60) / 60));
    const lo = g.bot.tileAt60[0] + k * (g.bot.tileAt120[0] - g.bot.tileAt60[0]);
    const hi = g.bot.tileAt60[1] + k * (g.bot.tileAt120[1] - g.bot.tileAt60[1]);
    const tile = Math.round(lo + r() * (hi - lo));
    const score = Math.round(len * span(g.bot.scorePerSec) * (0.8 + tile / 20));
    return { game: g.id, tile, score };
  }
  return { game: g.id, score: Math.round(span(g.bot.scoreRange)) };
}

export default DUEL_GAMES;
