/* ============================================================================
 * core/vocab.js - THE SCHOOL'S OWN VOCABULARY (the word floor).
 *
 * The host's pool (init.words) is the player's list, and it MAY BE EMPTY - that
 * is a contract, not a failure (ArcademyHostService.BuildWords). Empty means
 * seven of eleven classes lose their word layer to a still image or a silent
 * skip, and sort's GLIMPSE card and echo's word faces switch off entirely.
 * This file is the floor under that: a small niche-agnostic vocabulary the
 * school lends the day when the player's own list has nothing in it.
 *
 * FALLBACK ONLY, never a merge. A configured pool is the player's curation and
 * the promise of that list is that it is THEIRS - diluting it with house words
 * would break the promise silently, in a layer designed not to be noticed.
 * With any host word present this module returns the host's array untouched,
 * so the configured case stays byte-identical.
 *
 * SESSION-ONLY, like everything downstream of it: nothing here is persisted,
 * nothing is sent to the host, and SubliminalPool is NEVER written (trap 24,
 * DECISIONS #10). A house word absorbed by Daily Trigger is session-only in
 * exactly the way a host word is.
 *
 * PURE: no imports, no DOM, no store, no clock. The day's seeded rng is passed
 * in so two devices on the same UTC date deal the same words.
 * ==========================================================================*/

/**
 * The house vocabulary: 24 words, deliberately niche-agnostic and mod-agnostic.
 * Every one of them is something the school could say to anybody - no name, no
 * pronoun, no persona, nothing a creator mod would have to disown. Uppercase
 * because that is how a sub_flash card renders every other word it is given.
 *
 * The spelling is LOCKED: `slug()` below turns each entry into the filename its
 * whisper clip ships under (assets/sublim/<slug>.mp3), so renaming a word here
 * orphans a clip on disk.
 */
export const HOUSE_WORDS = Object.freeze([
  'FOCUS', 'RELAX', 'BREATHE', 'LET GO', 'SINK', 'DEEPER', 'DRIFT', 'BLANK',
  'EMPTY', 'LISTEN', 'OBEY', 'GOOD', 'AGAIN', 'STAY', 'SMILE', 'SOFTER',
  'MELT', 'CALM', 'DROP', 'TRUST', 'QUIET', 'OPEN', 'GIVE IN', 'FLOAT',
]);

/** How many house words a fallback day deals when the caller does not say. */
export const HOUSE_DEAL = 12;

/**
 * A house word's audio filename stem: lowercase, spaces to underscores.
 * "LET GO" -> "let_go" (the clip is assets/sublim/let_go.mp3).
 * @param {string} word
 * @returns {string}
 */
export function slug(word) {
  return String(word == null ? '' : word).trim().toLowerCase().replace(/\s+/g, '_');
}

/**
 * The day's word floor.
 *
 * Returns the host pool UNTOUCHED (same strings, a new array) whenever it holds
 * anything usable; otherwise a seeded shuffled slice of HOUSE_WORDS. The caller
 * keeps the result wherever it kept init.words - everything downstream (the
 * engine's copy, ctx.words, ctx.absorb, every class's wordPool()) is unchanged
 * by construction.
 *
 * @param {string[]} hostWords  init.words. May be empty; that is the contract.
 * @param {() => number} rng    the day's seeded rng, e.g. makeRng(seed + '|vocab').
 *                              Anything that is not a function deals the list in order.
 * @param {number} [want=12]    how many house words to deal on the fallback leg.
 * @returns {{words: string[], source: 'host'|'house'}}
 */
export function dayVocabulary(hostWords, rng, want = HOUSE_DEAL) {
  const host = (Array.isArray(hostWords) ? hostWords : [])
    .filter((w) => typeof w === 'string' && w.trim());
  if (host.length) return { words: host, source: 'host' };

  const roll = typeof rng === 'function' ? rng : null;
  const pool = HOUSE_WORDS.slice();
  if (roll) {
    // Fisher-Yates on the day's seed: the same date deals the same words.
    for (let i = pool.length - 1; i > 0; i--) {
      const j = Math.floor(roll() * (i + 1)) % (i + 1);
      const tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
    }
  }
  const n = Number.isFinite(+want) ? Math.max(1, Math.min(pool.length, Math.floor(+want))) : HOUSE_DEAL;
  return { words: pool.slice(0, n), source: 'house' };
}

export default { HOUSE_WORDS, HOUSE_DEAL, slug, dayVocabulary };
