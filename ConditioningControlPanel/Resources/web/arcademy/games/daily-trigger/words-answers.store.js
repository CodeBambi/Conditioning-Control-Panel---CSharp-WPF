/* ============================================================================
 * games/daily-trigger/words-answers.store.js - THE ANSWER POOL, store edition.
 *
 * The same shape as words-answers.js (THEME / COMMON / PHRASES), drawn on by
 * `bank.js` instead of that file when the host says this is an app-store build.
 * Nothing here replaces the real pool: both files ship in every tree, the flag
 * picks one, and off the flag the day's word is exactly the word it has always
 * been on every host.
 *
 * WHY IT EXISTS. The owner's 2026-08-25 ruling stands and is not softened here:
 * the answers are niche only, because a random English word is not what this
 * app is about. A store build is simply a narrower version of the same idea. It
 * keeps the bands that read as hypnosis, training and the school itself, drops
 * the three that read as pornography, and grows one new band of ordinary calm
 * so the pool stays big enough to be a pool.
 *
 * THE FIVE KEPT BANDS ARE NOT COPIED, THEY ARE DERIVED. `STORE_CATS` names them
 * and the words come straight out of `THEME_GROUPS`, so the two pools can never
 * drift into disagreeing about what a 'trance' word is. That cuts both ways and
 * is worth knowing before you edit the other file: ANYTHING ADDED TO trance,
 * training, arcade, school OR melt LANDS IN THE STORE BUILD TOO. Put a word
 * that would not survive a review in one of those bands and it ships. The three
 * dropped bands (submission, denial, bimbo) are the ones with the latitude.
 *
 * SIZE IS LOAD-BEARING, the same way it is next door: bank.js walks one shuffled
 * pass of the whole pool before any word repeats, so the pool length IS the
 * repeat-free horizon in days. The five kept bands are 272 words and COMMON is
 * 46, which is under the ~400 floor on its own; the `focus` band below carries
 * 115 more and lands the union at 433, so no word comes round inside a year.
 * Shrink the focus band and you shorten that horizon.
 * ==========================================================================*/

import { THEME_GROUPS, COMMON as BASE_COMMON } from './words-answers.js';

/**
 * Which of the eight bands survive. Names, not indexes, because `cat` is a
 * stable key (it is the `dt_cat_<cat>` lexicon row EMI speaks when she names the
 * band for a stuck player) and the order of the groups is free to change.
 *
 * The three that are absent are absent on purpose: 'submission', 'denial' and
 * 'bimbo'. Adding one back is a content decision, not a tidy-up.
 */
export const STORE_CATS = Object.freeze(['trance', 'training', 'arcade', 'school', 'melt']);

/**
 * The new band, and the only words in this file that are not next door.
 *
 * Ordinary calm: attention, weather, growing things, small comforts, a school
 * day's furniture and a little music. Nothing here is a trigger word and
 * nothing here is a euphemism, which is the whole brief. None of the 115
 * collides with any of the eight existing bands, so every one of them is a word
 * the pool did not already have.
 */
export const FOCUS_WORDS = `
  alert aware acute clear crisp brisk lucid sober plain think recap tally recur
  peace relax calms comfy cushy downy couch quilt duvet sheet linen towel robes
  socks shawl scarf hands palms wrist ankle thumb lungs yawns snore
  beach shore sands dunes hills vales woods trees ferns grass field river brook
  creek lakes ponds marsh reeds mossy pines cedar birch maple aspen elder olive
  lemon melon plums pears grape toast bread scone juice water
  robin finch doves swans geese otter sheep lambs birds herds
  piano chord hymns lyric organ flute cello harps
  draft index shelf plans query facts logic proof chart graph
  sunny rainy windy balmy chill frost snows gusts storm
  hours weeks month today dates
`;

/**
 * The store pool's bands, in the same `{cat, words}` shape `categoryOf()` reads,
 * so a stuck-hint names a band in a store build exactly as it does anywhere
 * else. The focus band rides last and carries its own `cat`, which needs a
 * `dt_cat_focus` lexicon row; without one the hint falls back to the plain
 * wording, which is the same graceful shape every other band already has.
 */
export const THEME_GROUPS_STORE = Object.freeze(
  THEME_GROUPS.filter((g) => g && STORE_CATS.indexOf(g.cat) >= 0)
    .concat([Object.freeze({ cat: 'focus', words: FOCUS_WORDS })]),
);

/** The flat pool, same contract as `THEME`: one space-joined string. */
export const THEME = THEME_GROUPS_STORE.map((g) => g.words).join(' ');

/** Unchanged. The campus / sweet shop / arcade band was already ordinary English. */
export const COMMON = BASE_COMMON;

/**
 * Phrase days, minus the three that read as something else out of context
 * ('good girl', 'good toy', 'open wide'). The remaining twenty five keep the
 * same shape: two groups, a free gap, ten letters or fewer, famous enough to
 * land as a gift rather than as a wall.
 */
export const PHRASES = Object.freeze([
  ['dont', 'think'], ['sink', 'deep'], ['drop', 'down'], ['pink', 'mist'],
  ['empty', 'head'], ['soft', 'focus'], ['deep', 'sleep'], ['blank', 'slate'],
  ['stay', 'down'], ['melt', 'away'], ['drift', 'off'], ['thank', 'you'],
  ['slow', 'blink'], ['heavy', 'eyes'], ['let', 'go'], ['count', 'down'],
  ['press', 'play'], ['high', 'score'], ['game', 'over'], ['bonus', 'round'],
  ['free', 'play'], ['quiet', 'mind'], ['sweet', 'spot'], ['keep', 'still'],
  ['one', 'more'],
]);

export default { THEME, THEME_GROUPS: THEME_GROUPS_STORE, COMMON, PHRASES, STORE_CATS };
