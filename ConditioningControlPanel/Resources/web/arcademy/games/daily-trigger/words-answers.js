/* ============================================================================
 * games/daily-trigger/words-answers.js - THE ANSWER POOL (the day's word).
 *
 * Two bands, ONE pool. `THEME` is the conditioning / arcade-flavoured band (the
 * words the metagame is about internalising); `COMMON` is the widened band of
 * ordinary English. The daily draw runs over the UNION, and that union IS the
 * "widened shared pool" SYNTHESIS-NOTES adopted for veterans: a player who has
 * memorised the trigger words still cannot enumerate the board, because half the
 * pool is plain English. The pool is NOT tier-dependent - the day's word must be
 * globally identical (BUILD-CONTRACT #978 rule / the homeroom ruling), so tier
 * may never change which word is drawn.
 *
 * FORMAT: whitespace-separated lowercase a-z, EXACTLY five letters. `bank.js`
 * filters, dedupes and sorts on load, so a stray token degrades to "absent"
 * rather than to a broken board. Do not add slurs or anything hateful; the
 * horny-adjacent theme band is deliberate (this is an adult app).
 *
 * PHRASES drive phrase days (~15% of days, all tiers - see bank.js): two groups
 * with a free gap, spaces auto-marked and never guessed, total letters <= 10.
 * ==========================================================================*/

/** The trigger / mantra / arcade band. */
export const THEME = `
abyss adore alien arena aroma babes badge below bends bimbo blank blaze blink bliss blitz block
blond blush board boost boots bossy bound brain brats bunny candy chain chant charm cheat chest
chime click clock clone cloud coins combo crave crown cuffs curls cutie cycle dazed depth ditzy
diver dizzy dodge dolls dolly dream drift drill drips droid drone drown eager ember empty extra
faint faith flame flash float flush focus foggy fuzzy gasps gazes ghost giddy gleam glint gloss
glows habit halos hazed heads heavy heels honey hypno joker jumps kitty kneel laced laser latex
learn leash level limbo lines lives loops loopy loose lower loyal lucky lulls magic mazes mecha
medal melts might minds misty moans molds murky needy ninja obeys ocean order pearl pilot pinky
pixel plumb plush power prime prism prize pulse puppy purrs quest quiet relax retry robot rocks
rogue ruled rules runes sated satin scent score serve shake shape shift shine shiny shook shore
sighs sigil silky sinks sissy skull sleep snaps soaks spark spawn spell spins stare still sugar
super sways sweet swirl swoon sword tamed tease teddy tempo tempt think throb tiara tides tiles
timer token tones toyed train trust twirl under urges vapor verse vivid voice watch waves whirl
woozy words worth yearn yield
`;

/** The widened band: ordinary English, guessable, no obscurities. */
export const COMMON = `
about above actor adopt agree amber angel anger angle apple asset audio avoid awake award bench
berry birth black blade bloom blues blunt boast booth breed brick bride brief bring built bunch
burst buyer cabin catch cause chair chalk champ chief child chill chirp choir clash clasp class
clean clear coach coast cobra cocoa color crack craft crane crash crate cross crude cruel crumb
crush death debut decay delay delta dozen draft drain drama drank dusty dwarf dwell eagle early
enemy enjoy enter entry equal exile exist fable faced fairy fetch fever fiber field fiery fleet
flesh flick fling flint forge forth forty forum found fruit fudge fully funny gauge glide globe
gloom glory glove grape graph grasp grass grave grind groan groom group grove hairy handy happy
hardy harsh heart hedge hefty hello herbs hover human humid humor hurry irony issue ivory jelly
jewel knife knock knots known koala layer lease least leave ledge liver llama lobby local lodge
macro madam major maker mango media medic melon mercy merge minus mixer model money month movie
muddy mummy mural music niece nifty night ninth noble occur offer often olive onion outer owner
oxide ozone paint patch patio pause peach pedal piece pinch pitch pivot pizza pluck plume plump
poach point pride print prior probe prone purse quack quail quake quart quota quote radar radio
rainy react ready realm rebel refer rifle right rigid rinse risky route royal rugby ruler rumor
sauce sauna savor scale scalp scrap screw scrub seize sense share shark sharp shave shawl short
shout shove shrub shrug slack slate sleek sleet slice smash smell smile smirk smoke snore snout
snowy soapy sober spade spare speak spear speck spire spite split spoil spoke squid stack staff
stage stain stash state steak steal steam stock stole stone stool stoop strip study stuff stump
stunt swell swept swift swing syrup tarot taste tasty taunt teach theme there these thick thief
thumb thump tidal tiger tight topaz topic torch total touch trait tramp trash tread treat truce
truck truly trump trunk twice twine twist uncle unfit usage usher usual vague valid video vigor
villa vinyl viola voter vowel wafer wager wagon weigh weird whale wharf wheat whose widen wider
widow width women woods world worry worse wrote yacht yeast young yours
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

export default { THEME, COMMON, PHRASES };
