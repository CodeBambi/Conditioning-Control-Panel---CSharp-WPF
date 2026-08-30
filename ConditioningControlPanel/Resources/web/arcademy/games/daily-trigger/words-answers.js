/* ============================================================================
 * games/daily-trigger/words-answers.js - THE ANSWER POOL (the day's word).
 *
 * THE RULING (owner, 2026-08-25): THE ANSWERS ARE NICHE ONLY. The day's word
 * can never be a random English word - every answer must relate to what this
 * app is about (trance / conditioning / submission / bimbo + sissy dress-up /
 * pleasure + denial / the Arcademy's own school-and-arcade framing). The old
 * "widened shared pool" of plain English (380 words, SYNTHESIS-NOTES' answer
 * to veterans enumerating the board) is GONE; the defence against enumeration
 * is now the SIZE of the curated band, not its blandness.
 *
 * Two bands, ONE pool, drawn as the UNION. `THEME` is the curated niche band
 * (the words the metagame is about internalising - `isThemeWord` skins a hit
 * as "one of your own words"); `COMMON` is a deliberately TINY band of ordinary
 * campus / sweet-shop / arcade English that still reads on-theme, kept only so
 * the two-band shape (and that flavour line) survives. It may stay empty.
 * The pool is NOT tier-dependent - the day's word must be globally identical
 * (BUILD-CONTRACT #978 rule / the homeroom ruling), so tier may never change
 * which word is drawn.
 *
 * SIZE IS LOAD-BEARING: bank.js's cyclePick() walks ONE shuffled pass of the
 * whole pool before any word repeats, so the pool length IS the repeat-free
 * horizon in days (phrase days ~15% and revision days ~4% only stretch it).
 * Keep the union above ~400 so no word comes round inside a year; it sits at
 * ~578 as of 2026-08-25 (532 theme + 46 common).
 *
 * FORMAT: whitespace-separated lowercase a-z, EXACTLY five letters. `bank.js`
 * filters, dedupes and sorts on load, so a stray token degrades to "absent"
 * rather than to a broken board. The category groups below are for the human
 * editing the file - bank.js sees one flat string. Do not add slurs or
 * anything hateful, nothing implying minors; the horny-adjacent band is
 * deliberate (this is an adult app). The three mod names (bambi / sissy /
 * circe) are in on purpose. Every answer is auto-unioned into ACCEPT by
 * bank.js, so a word here never needs a twin in words-accept.js.
 *
 * PHRASES drive phrase days (~15% of days, all tiers - see bank.js): two groups
 * with a free gap, spaces auto-marked and never guessed, total letters <= 10.
 * ==========================================================================*/

/**
 * The curated niche band (owner ruling 2026-08-25: the whole answer pool is this),
 * as DATA rather than as an anonymous array (stuck-hints, 2026-08-30).
 *
 * NOT ONE WORD MOVED. The bands below were already the eight comment-headed
 * segments of one `[...].join(' ')`; all that changed is that each segment now
 * carries the name of the band it always was, so `categoryOf()` in bank.js can
 * answer "what KIND of word is today's" for EMI's first stuck-hint. `THEME` is
 * still the same flat, space-joined string in the same order - the pool, the
 * shuffle and the repeat-free horizon are byte-identical. The owner's ruling on
 * difficulty stands: hints only, the answers stay niche.
 *
 * `cat` is a STABLE key: it is the `dt_cat_<cat>` lexicon row EMI speaks and
 * renaming one silently drops the category to the plain fallback.
 */
export const THEME_GROUPS = Object.freeze([
  /* trance / hypnosis (84) */
  Object.freeze({ cat: 'trance', words: `
  abyss blank blink dazed dazes depth dozed dozes dream drift drips drone drown empty faint fades
  faded float focus foggy fuzzy gazed gazes glaze hazed hazes heavy hypno limbo lower lulls melts
  misty murky sinks sleep slept spell stare still sways swung swirl swoon tides tones trust under
  whirl woozy yield yearn blurs dulls numbs inert vapor glass gleam glint glows watch clock ticks
  timed count chime chant verse lines words voice dizzy giddy heady tipsy dopey mushy melty waken
  woken awake wakes dives
  ` }),
  /* conditioning / training (44) */
  Object.freeze({ cat: 'training', words: `
  train learn drill habit reset prime loops cycle daily rites study teach tutor coach guide shape
  molds mould wired wires brand stamp carve tuned tunes tweak nudge minds brain patch fixed edits
  wipes wiped erase clean slate fresh again creed dogma faith obeys rerun
  ` }),
  /* submission / control / femdom (80) */
  Object.freeze({ cat: 'submission', words: `
  kneel knelt serve bound leash owned owner rules ruled ruler mercy bends bowed plead pleas chain
  cuffs binds ropes roped knots caged cages locks keyed leads stays fetch puppy kitty bunny tamed
  tames domme queen deity crown reign order edict bossy stern harsh grasp grips holds clasp power
  might force reins yoked sworn swear vowed loyal lowly timid coyly floor boots heels adore idols
  altar prays thank sorry spank swats sting stung smack slaps scold shame shush quiet muted mutes
  ` }),
  /* pleasure / edging / denial / sensation (79) */
  Object.freeze({ cat: 'denial', words: `
  tease taunt toyed edges edged ruins wreck aches ached throb pulse swell needy needs wants crave
  urges itchy burns ember flame blaze heats sweat flush moans sighs gasps whine mewls purrs yelps
  pants shake shook quake tense tight grind rides shock jolts vibes wands slick moist soaks juicy
  sweet honey sugar candy cream peach curvy curve thigh waist belly mouth teeth smile grins pouty
  pouts kissy licks bites nails claws touch grope pinch peaks crest spill burst sated spent
  ` }),
  /* bimbo / glam / dumb (101) */
  Object.freeze({ cat: 'bimbo', words: `
  bimbo blond gloss ditzy dolly dolls cutie babes pinky pinks blush rouge liner shiny glitz bling
  sheen shine spark gaudy showy vogue style model poses posed flirt winks girly dummy dunce ditsy
  silly sassy perky peppy cheer plush fluff frill laced lacey satin silky sheer gauze tulle nylon
  skirt dress gowns frock tutus panty thong strap latex vinyl charm tiara pearl jewel rings beads
  curls curly braid bangs roses coral mauve lilac preen primp teeny cuter angel fairy pixie nymph
  siren vixen foxes vamps lured lures hooks snare traps hexed hexes curse witch magic runes sigil
  brews circe bambi sissy goons
  ` }),
  /* app features / arcade (102) */
  Object.freeze({ cat: 'arcade', words: `
  flash video timer level badge quest drain popup panic chaos pause plays press click audio sound
  sonic noise texts fonts pixel frame blast combo bonus gifts prize medal ranks coins token chips
  punch cards decks wheel reels spins slots lucky bingo lotto dealt deals games gamer retry round
  extra lives heart bells dings alarm beats tempo loopy noisy mists smoke cloud twist twirl whorl
  coils orbit droid robot cyber codes coded input bytes error crash froze stuck stiff stone tiles
  score cheat mazes arena laser block dodge jumps snaps boost blitz clone ghost halos super shift
  tempt worth prism ocean waves rocks
  ` }),
  /* school framing (27) */
  Object.freeze({ cat: 'school', words: `
  class grade exams pupil chalk board desks notes books pages paper marks tests essay honor merit
  award tiers years halls plaid pleat tasks chore tardy later dorms
  ` }),
  /* mind-melt / happy (15) */
  Object.freeze({ cat: 'melt', words: `
  vapid inane batty dotty kooky wacky happy laugh merry jolly enjoy bliss elate highs light
  ` }),
]);

/** The flat pool `bank.js` parses - the SAME string, in the same order, as the
 *  anonymous array this used to be. Nothing downstream may read the groups. */
export const THEME = THEME_GROUPS.map((g) => g.words).join(' ');

/**
 * The tiny on-theme "ordinary English" band: campus, sweet shop, arcade cabinet.
 * Nothing here is a trigger word, everything reads as the school. Keep it small
 * (it is NOT the old widened pool) and keep it on-theme, or drop it to empty.
 */
export const COMMON = `
  bench chair early night smirk sleek sharp taste smell sense scale share stage staff stash steam
  sauna spare ready quote print paint piece point music movie media radio jelly fudge syrup cocoa
  mango berry apple funny nifty handy hello yours drama fable theme topic truly valid
`;

/**
 * Phrase days: [groupA, groupB], both a-z, total letters <= 10. Spaces are free
 * (auto-marked, never typed). Deliberately short and famous-shaped: a phrase day
 * must read as a gift, not as a 10-letter wall.
 */
export const PHRASES = Object.freeze([
  ['good', 'girl'], ['dont', 'think'], ['sink', 'deep'], ['drop', 'down'],
  ['pink', 'mist'], ['empty', 'head'], ['soft', 'focus'], ['deep', 'sleep'],
  ['blank', 'slate'], ['good', 'toy'], ['stay', 'down'], ['melt', 'away'],
  ['drift', 'off'], ['thank', 'you'], ['slow', 'blink'], ['heavy', 'eyes'],
  ['open', 'wide'], ['let', 'go'], ['count', 'down'], ['press', 'play'],
  ['high', 'score'], ['game', 'over'], ['bonus', 'round'], ['free', 'play'],
  ['quiet', 'mind'], ['sweet', 'spot'], ['keep', 'still'], ['one', 'more'],
]);

export default { THEME, THEME_GROUPS, COMMON, PHRASES };
