/* ============================================================================
 * games/daily-trigger/bank.js - THE WORD BANK + THE DAILY DRAW.
 *
 * THE ONE LAW OF THIS FILE: the day's entry is a pure function of the UTC date
 * string and nothing else. Not the player, not the grade tier, not the order in
 * which anything rolled. Every draw here runs off hash01()/makeRng() keyed by an
 * explicit tag string (never off a shared rng stream), so adding a roll can
 * never shift an existing one and two fresh boots on the same date always deal
 * the same board. That is what makes homeroom a global ritual and what lets a
 * retake replay the identical script.
 *
 *   dailyEntry('2026-08-19') -> {
 *     dateUtc, dayIndex, puzzleNumber, kind:'word'|'phrase', groups:[..],
 *     answer, letters, goldDay, goldIndex, revisionOf|null }
 *
 * Nothing in here touches the DOM, the engine or the bridge; it is unit-testable
 * on its own and the harness does exactly that.
 * ==========================================================================*/

import { hash01, makeRng, shuffled } from '../../core/rng.js';
import { THEME, COMMON, PHRASES } from './words-answers.js';
import { ACCEPT } from './words-accept.js';

/**
 * The puzzle-number epoch. Shared with core/timetable.js on purpose (same day 0
 * for the whole Arcademy). MOVING THIS RENUMBERS EVERY PAST PUZZLE and reshuffles
 * every future answer - it is a published number in share text. Don't.
 */
export const EPOCH_UTC = '2026-08-01';

/** ~15% of days are phrase days (dossier; tune after play-test). */
export const PHRASE_DAY_CHANCE = 0.15;
/** ~1 day in 14 gilds one tile on EVERY board (dossier's gold-letter day). */
export const GOLD_DAY_CHANCE = 1 / 14;
/** ~1 day in 25 re-serves an older word as revision (lexicon key revision_day). */
export const REVISION_DAY_CHANCE = 0.04;
/** How far back a revision day reaches. */
const REVISION_LOOKBACK = 45;

const FIVE = /^[a-z]{5}$/;
const ALPHA = /^[a-z]+$/;

/* ----------------------------------------------------------------------------
 * POOLS (parsed once, defensively)
 * -------------------------------------------------------------------------- */
function toks(raw) {
  return String(raw == null ? '' : raw).toLowerCase().trim().split(/\s+/).filter(Boolean);
}
function fiveOnly(list) {
  const out = [];
  const seen = Object.create(null);
  for (const w of list) {
    if (!FIVE.test(w) || seen[w]) continue;
    seen[w] = 1;
    out.push(w);
  }
  return out.sort();
}

let cached = null;

/**
 * The parsed bank. Lazy + memoised: a game module is imported once per page, and
 * the parse is ~4k tiny strings, so this costs nothing after the first class.
 * @returns {{answers:string[], theme:string[], common:string[],
 *            accept:Set<string>, phrases:string[][]}}
 */
export function bank() {
  if (cached) return cached;
  const theme = fiveOnly(toks(THEME));
  const common = fiveOnly(toks(COMMON));
  const answers = fiveOnly(theme.concat(common));
  const accept = new Set(fiveOnly(toks(ACCEPT)));
  // Every possible answer must be guessable even if the acceptance list forgot it.
  for (const w of answers) accept.add(w);
  const phrases = [];
  for (const p of (Array.isArray(PHRASES) ? PHRASES : [])) {
    if (!Array.isArray(p) || p.length < 2) continue;
    const groups = p.map((g) => String(g || '').toLowerCase()).filter((g) => ALPHA.test(g));
    if (groups.length < 2) continue;
    const letters = groups.join('').length;
    if (letters < 5 || letters > 12) continue;      // GROUND-RULES: total <= 12
    phrases.push(groups);
  }
  cached = { answers, theme, common, accept, phrases };
  return cached;
}

/** True when this word is in the theme (trigger/arcade) band. Flavour only. */
export function isThemeWord(word) {
  const b = bank();
  return b.theme.indexOf(String(word || '').toLowerCase()) >= 0;
}

/* ----------------------------------------------------------------------------
 * DATES (UTC only - local dates roll the streak, never the content)
 * -------------------------------------------------------------------------- */
/** 'YYYY-MM-DD' -> UTC ms, or NaN. */
export function utcMs(dateStr) {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(dateStr || ''));
  if (!m) return NaN;
  return Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
}

/** Whole UTC days from EPOCH_UTC (may be negative before the epoch). */
export function dayIndexFor(dateStr) {
  const a = utcMs(dateStr), b = utcMs(EPOCH_UTC);
  if (!Number.isFinite(a) || !Number.isFinite(b)) return 0;
  return Math.round((a - b) / 86400000);
}

/** dateStr shifted by n whole UTC days, back as 'YYYY-MM-DD'. */
export function shiftDate(dateStr, n) {
  const ms = utcMs(dateStr);
  if (!Number.isFinite(ms)) return String(dateStr || '');
  return new Date(ms + n * 86400000).toISOString().slice(0, 10);
}

/**
 * The UTC date a class seed was minted for. The shell hands games
 * `seed = '<utcDateSeed>|<gameKey>|t<tier>'` - the TIER IS IN THERE, so a game
 * that seeds its content off the whole string gets a different answer per tier
 * and the daily word stops being global. Take the date half only.
 */
export function dateFromSeed(seed, fallback) {
  const head = String(seed == null ? '' : seed).split('|')[0].trim();
  if (/^\d{4}-\d{2}-\d{2}$/.test(head)) return head;
  if (fallback && /^\d{4}-\d{2}-\d{2}$/.test(String(fallback))) return String(fallback);
  return head || '1970-01-01';
}

/* ----------------------------------------------------------------------------
 * THE DAILY DRAW
 * -------------------------------------------------------------------------- */
/** A tagged, order-independent roll for one date. */
function roll(dateStr, tag) { return hash01('dt|' + tag + '|' + dateStr); }

/**
 * Cycle a pool without repeats: the pool is shuffled per epoch (one epoch = one
 * full pass), and the day walks it in order. A whole cycle is the "Full
 * Vocabulary" diploma; the next epoch reshuffles under a new seed.
 */
function cyclePick(pool, index) {
  if (!pool.length) return null;
  const n = pool.length;
  const i = ((index % n) + n) % n;
  const epoch = Math.floor(index / n);
  return shuffled(pool, makeRng('dt|cycle|' + n + '|e' + epoch))[i];
}

function wordEntry(dateStr, depth) {
  const b = bank();
  const idx = dayIndexFor(dateStr);

  /* Revision ("echo") day: legitimately re-serves an older word, skinned as
   * revision_day. Depth-guarded so a revision day can never point at another. */
  if (!depth && b.answers.length && roll(dateStr, 'revision') < REVISION_DAY_CHANCE) {
    const back = 7 + Math.floor(roll(dateStr, 'revback') * REVISION_LOOKBACK);
    const src = shiftDate(dateStr, -back);
    const older = wordEntry(src, 1);
    if (older && older.answer) return { answer: older.answer, revisionOf: src, dayIndex: idx };
  }
  return { answer: cyclePick(b.answers, idx), revisionOf: null, dayIndex: idx };
}

/**
 * THE day's entry. Pure in `dateStr`.
 * @param {string} dateStr 'YYYY-MM-DD' (UTC)
 */
export function dailyEntry(dateStr) {
  const date = /^\d{4}-\d{2}-\d{2}$/.test(String(dateStr || '')) ? String(dateStr) : '1970-01-01';
  const b = bank();
  const idx = dayIndexFor(date);

  let kind = 'word';
  let groups;
  let revisionOf = null;

  const phraseDay = b.phrases.length > 0 && roll(date, 'phrase') < PHRASE_DAY_CHANCE;
  if (phraseDay) {
    kind = 'phrase';
    groups = cyclePick(b.phrases, idx) || b.phrases[0];
  } else {
    const w = wordEntry(date, 0);
    revisionOf = w.revisionOf;
    groups = [w.answer || (b.answers[0] || 'blank')];
  }

  const answer = groups.join('');
  const letters = answer.length;
  const goldDay = roll(date, 'gold') < GOLD_DAY_CHANCE;
  const goldIndex = goldDay ? Math.min(letters - 1, Math.floor(roll(date, 'goldpos') * letters)) : -1;

  return Object.freeze({
    dateUtc: date,
    dayIndex: idx,
    puzzleNumber: idx + 1,
    kind,
    groups: Object.freeze(groups.slice()),
    answer,
    letters,
    goldDay,
    goldIndex,
    revisionOf,
  });
}

/* ----------------------------------------------------------------------------
 * ACCEPTANCE
 * -------------------------------------------------------------------------- */
export const REJECT = Object.freeze({
  SHORT: 'short',
  NOT_A_WORD: 'not_a_word',
  HARD_HIT: 'hard_hit',
  HARD_NEAR: 'hard_near',
});

/**
 * Is this guess legal on this day?
 * @param {string} guess    a-z, already joined (no spaces)
 * @param {Object} entry    dailyEntry() result
 * @returns {{ok:boolean, reason?:string}}
 */
export function isAcceptable(guess, entry) {
  const g = String(guess || '').toLowerCase();
  const need = (entry && entry.letters) || 5;
  if (g.length !== need || !ALPHA.test(g)) return { ok: false, reason: REJECT.SHORT };
  // A phrase day's halves are not dictionary words: any A-Z fill of the right
  // length is legal (the dossier's "the lexicon is the metagame" rule).
  if (entry && entry.kind === 'phrase') return { ok: true };
  if (need !== 5) return { ok: true };
  return bank().accept.has(g) ? { ok: true } : { ok: false, reason: REJECT.NOT_A_WORD };
}

/* ----------------------------------------------------------------------------
 * CANDIDATE POLLUTION (the twist: whisper OTHER words from the same bank)
 * -------------------------------------------------------------------------- */
/**
 * Words to whisper AT the player - honest (never presented as hints) but drawn
 * from the same bank, so they poison the mental shortlist a bank-aware player
 * relies on. Seeded off the date + a nonce so a retake replays the same whispers.
 *
 * @param {string} dateStr
 * @param {number} count
 * @param {string} exclude   the real answer, never whispered
 */
export function pollutionWords(dateStr, count, exclude) {
  const b = bank();
  const bad = String(exclude || '').toLowerCase();
  const n = Math.max(0, Math.min(64, Math.round(Number(count) || 0)));
  if (!n || !b.answers.length) return [];
  // Bias toward the theme band: those are the words a veteran is shortlisting.
  const pool = (b.theme.length >= 8 ? b.theme : b.answers).filter((w) => w !== bad);
  const order = shuffled(pool, makeRng('dt|pollute|' + dateStr));
  return order.slice(0, n);
}

export default { bank, dailyEntry, isAcceptable, pollutionWords, dateFromSeed };
