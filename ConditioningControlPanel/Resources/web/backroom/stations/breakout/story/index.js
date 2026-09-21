/* ============================================================================
 * stations/breakout/story/index.js - the long story, as pure data.
 *
 * Five acts, twenty-eight walls, played in order. Nothing here touches the DOM,
 * the clock or Math.random: this whole file runs under `node --test`.
 *
 * The walls of an act are NOT hardcoded positions. They are computed from the
 * board lists the five act files export, in act order, so a stub act with one
 * board still plays end to end. `ACT_TARGET` is what each act owes when it is
 * finished; a lane that ships a different count shifts every wall after it.
 *
 * The house game (the eight walls plus the office ending) and the doors
 * (doors.js) are untouched. This is a third thing beside them.
 * ==========================================================================*/
import ACT1 from './act1.js';
import ACT2 from './act2.js';
import ACT3 from './act3.js';
import ACT4 from './act4.js';
import ACT5 from './act5.js';

/** The five acts, in play order. Each is `{ id, title, cap, colour, words, boards }`. */
export const ACTS = [ACT1, ACT2, ACT3, ACT4, ACT5];

/** What each act owes when its lane is done. The scaffold's stubs are all 1. */
export const ACT_TARGET = { monday: 5, habit: 7, notice: 7, enough: 6, out: 3 };
/** Twenty-eight walls when every act has shipped its target. */
export const STORY_TARGET_WALLS = 28;

/** The one board entry that is not a board: build the house finale instead. */
export const HOUSE_FINALE = 'finale';
/** True for the `{ house: 'finale' }` entry, false for an authored board. */
export const isHouseEntry = entry => !!(entry && entry.house);

/**
 * Every wall of the story, flat, in play order.
 * `{ wall, of, act, actIx, actWall, first, last, board }`, `wall` 1-based.
 * `board` is the raw act-file entry: an authored board, or `{ house: 'finale' }`.
 */
export const STORY_BOARDS = (() => {
  const out = [];
  ACTS.forEach((act, actIx) => {
    const boards = Array.isArray(act.boards) ? act.boards : [];
    boards.forEach((board, i) => {
      out.push({ wall: out.length + 1, of: 0, act, actIx, actWall: i + 1,
        first: i === 0, last: i === boards.length - 1, board });
    });
  });
  for (const row of out) row.of = out.length;
  return out;
})();

/** How many walls the story actually has right now (28 once all five acts are filled). */
export const STORY_WALLS = STORY_BOARDS.length;

/** The wall's row, or null off the end. `wall` is 1-based. */
export function storyBoardAt(wall) {
  const n = Math.floor(Number(wall));
  if (!Number.isFinite(n) || n < 1 || n > STORY_BOARDS.length) return null;
  return STORY_BOARDS[n - 1];
}
/** The act that owns this wall, or null. */
export function actOfWall(wall) {
  const row = storyBoardAt(wall);
  return row ? row.act : null;
}
/** True on the first wall of an act: the wall that raises the act title card. */
export function isActStart(wall) {
  const row = storyBoardAt(wall);
  return !!(row && row.first);
}
/** The most `g.sat` may reach while this wall plays. 1 off the end of the story. */
export function capForWall(wall) {
  const act = actOfWall(wall);
  const cap = act ? Number(act.cap) : 1;
  return Number.isFinite(cap) ? Math.max(0, Math.min(1, cap)) : 1;
}
export const actById = id => ACTS.find(a => a.id === id) || null;
/** The first wall of an act, 1-based, or 0 for an act with no boards. */
export function firstWallOfAct(id) {
  const row = STORY_BOARDS.find(r => r.act.id === id);
  return row ? row.wall : 0;
}
/** The last wall of an act, 1-based, or 0. */
export function lastWallOfAct(id) {
  let last = 0;
  for (const row of STORY_BOARDS) if (row.act.id === id) last = row.wall;
  return last;
}
/** A board by its id (`st_<act>_<nn>`), for `?board=` and the dev harness. */
export function storyBoardById(id) {
  return STORY_BOARDS.find(r => r.board && r.board.id === id) || null;
}

/* --------------------------------------------------------------- progress */
/* Progress is `{ wall, cleared }` in localStorage `bo.story.v1`. `wall` is where
 * Continue picks up, `cleared` is the highest wall the player has finished.
 * Act one is always open. Any later act opens when the act before it is cleared. */

/** Act one is open. Act N opens once the last wall of act N-1 is cleared. */
export function actUnlocked(id, cleared = 0, unlockAll = false) {
  if (unlockAll) return true;
  const ix = ACTS.findIndex(a => a.id === id);
  if (ix < 0) return false;
  if (ix === 0) return true;
  const need = lastWallOfAct(ACTS[ix - 1].id);
  return need > 0 && Number(cleared) >= need;
}
/** Read a stored progress blob into `{ wall, cleared }`, both sane. */
export function readProgress(raw) {
  let data = raw;
  if (typeof raw === 'string') { try { data = JSON.parse(raw); } catch (e) { data = null; } }
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, Math.floor(Number(v)) || 0));
  const cleared = data ? clamp(data.cleared, 0, STORY_WALLS) : 0;
  const wall = data ? clamp(data.wall, 1, STORY_WALLS) : 1;
  return { wall, cleared };
}
/** Fold a cleared wall into the progress blob. Never walks backwards. */
export function noteCleared(progress, wall) {
  const now = readProgress(progress);
  const n = Math.max(1, Math.min(STORY_WALLS, Math.floor(Number(wall)) || 1));
  return { wall: Math.max(now.wall, Math.min(STORY_WALLS, n + 1)), cleared: Math.max(now.cleared, n) };
}

export default ACTS;
