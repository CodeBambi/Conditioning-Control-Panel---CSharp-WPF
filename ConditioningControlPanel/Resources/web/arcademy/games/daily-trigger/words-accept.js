/* ============================================================================
 * games/daily-trigger/words-accept.js - THE ACCEPTANCE LIST.
 *
 * Words a guess may BE, on a five-letter day. This is deliberately much wider
 * than the answer pool (words-answers.js) and much narrower than a full English
 * dictionary: it is a hand-curated list of real five-letter words, shipped in
 * the game's own folder because the Arcademy is fully playable under OfflineMode
 * and may never fetch a dictionary.
 *
 * The answer pool is unioned in on load (bank.js), so every possible answer is
 * always acceptable even if it is missing here.
 *
 * FORMAT: whitespace-separated lowercase a-z, exactly five letters. bank.js
 * filters/dedupes, so a stray token is simply ignored. No slurs, no hateful
 * terms; ordinary adult vocabulary is fine.
 *
 * PHRASE DAYS DO NOT USE THIS LIST - a phrase day accepts any A-Z fill of the
 * right length (the dossier's "the lexicon is the metagame" rule), because the
 * phrase halves are not single dictionary words.
 * ==========================================================================*/

/** Curated real words, acceptance only. */
export const ACCEPT = `
abbey abide abled abort about above abuse abyss acorn acrid acted actor acute adage adapt added
adept admit adobe adopt adore adult affix afire afoot afoul after again agate agent agile aging
agony agree ahead aider aimed aired aisle alarm album alder alert algae alias alibi alien align
alike alive allay alley allot allow alloy aloft aloha alone along aloof aloud altar alter amass
amaze amber amend amiss amity among ample amply amuse angel anger angle angst anime ankle annex
annoy annul anode antic anvil aorta apart aphid aping apnea apple apply apron arbor ardor arena
argon argue arise armed armor aroma arose array arrow arson artsy ascot ashen ashes aside askew
aspen aspic assay asset atlas atoll atone attic audio audit auger aught augur aunts aural auras
avail avert avian avoid await awake award aware awash awful awoke axial axing axiom axles azure
babes bacon badge badly bagel baggy baker baled baler balmy balsa banal bands bandy banjo banks
bared barge barks barns baron basal based basic basil basin basis basks baste batch bathe baton
batty bawdy bayou beach beads beady beams beans beard bears beast beats beech beefy beeps beers
beets befit began beget begin begun being belay belie belle bells belly below belts bench bends
bento berry berth beset betas betel bevel bezel bicep biddy bided bidet bigot biked biker bilge
billy bimbo binds binge bingo biome biped birch birds birth bison biter bites bitty black blade
blame bland blank blare blast blaze bleak bleat bleed bleep blend bless blimp blind blink bliss
blitz bloat blobs block blocs blogs bloke blond blood bloom blots blown bluer blues bluff blunt
blurb blurs blurt blush board boars boast boats bobby bodes bogus boils bolds bolts bombs bonds
boned boner bones bongo bonny bonus boost booth boots booze boozy borax bored borne boron bosom
bossy botch bough bound bowed bowel bowls boxed boxer boxes brace brags braid brain brake brand
brass brats brave brawl brawn brays bread break bream breed brews briar bribe brick bride brief
brims brine bring brink briny brisk broad broil broke brook broom broth brown brows brush brute
buddy budge buffs buggy bugle build built bulbs bulge bulks bulky bulls bully bumps bumpy bunch
bunks bunny bunts buoys burly burnt burps burro burst busts butch butte buxom buyer bylaw byway
cabin cable cacao cache cacti caddy cadet cadre cagey cairn cakes camel camps canal candy canes
canny canoe canon caped caper capon carat cards cared cares caret cargo carol carry carts carve
caste casts catch cater cause cease cedar cedes cello cents chafe chaff chain chair chalk champ
chant chaos chaps chard charm chars chart chase chats cheap cheat check cheek cheer chefs chess
chest chews chewy chick chide chief child chill chime china chink chips chirp chive chock choir
choke chomp chops chord chore chose chuck chump chunk churn chute cider cigar cinch circa cited
cites civic civil clack clads claim clamp clang clank claps clash clasp class claws clays clean
clear clefs clerk click cliff climb clime cling clink clips cloak clock clods clogs clomp clone
clops close cloth clots cloud clout clove clown clubs cluck clued clues clump clung coach coals
coast coats cobra cocky cocoa codec coded coder codes coeds coifs coils coins colds colic colon
color colts comas combo combs comer comes comet comfy comic comma conch condo cones conic conks
cooks cools coops coped copes copra copse coral cords cored corer cores corks corny corps costs
couch cough could count coupe court coven cover coves covet covey cowed cower coyly crabs crack
craft crags cramp crams crane crank craps crash crass crate crave crawl craws craze crazy creak
cream credo creed creek creel creep crepe crept cress crest crews cribs cried crier cries crime
crimp crisp croak crock crone crony crook croon crops cross crowd crown crows crude cruel crumb
crush crust crypt cubed cubes cubic cuffs culls cults cumin cupid curbs curds cured cures curio
curls curly curry curse curst curve curvy cushy cusps cutie cyber cycle cynic daddy daily dairy
daisy dally dance dandy dares darts dated dater dates datum daubs daunt dazed deals dealt deans
dears death debar debit debts debug debut decaf decal decay decks decoy decry deeds deems defer
deify deign deism deist deity delay delta delve demon demos demur denim dense dents depot depth
derby deter detox deuce devil dials diary diced dicey dicta diets digit dikes dills dilly dimes
diner dines dingo dingy dinky diode dirge dirty disco discs dishy disks ditch ditto ditty ditzy
divan divas dived diver dives divot dizzy docks dodge dodgy doers doggy dogma doily doing doled
doles dolls dolly dolts domed domes donor donut dooms doors doped dopes dorks dorky dosed doses
doted dotes doubt dough douse doves dowdy dowel dower downs dowry dowse dozed dozen dozer draft
drags drain drake drama drams drank drape drawl drawn draws drays dread dream dregs dress dried
drier dries drift drill drily drink drips drive droid droll drone drool droop drops drove drown
drugs druid drums drunk dryad dryer dryly ducal ducat ducks ducts dudes duels duets dukes dully
dummy dumps dumpy dunce dunes duped dupes dusks dusky dusts dusty duvet dwarf dwell dwelt dyers
dying eager eagle eared earls early earns earth eased easel eases eaten eater eaves ebony edema
edged edger edges edict edify edits eerie egged egret eider eight eject eking elate elbow elder
elect elegy elide elite elope elude elves email embed ember emcee emend emery emirs emits empty
enact ended endow enema enemy enjoy ennui enrol ensue enter entry envoy epics epoch epoxy equal
equip erase erect erode erred error erupt essay ester ether ethic ethos etude evade evens event
evert every evict evils evoke exact exalt exams excel exert exile exist exits expel extol extra
exude exult eying fable faced facer faces facet facts faded fades fails faint fairs fairy faith
faked faker fakes falls false famed fancy fangs fanny farce fared fares farms fasts fatal fated
fates fatty fault fauna fauns favor fawns faxed faxes fazed feast feats fecal feeds feels feign
feint fells felon felts femur fence fends feral ferns ferry fests fetal fetch feted fetes fetid
fetus feuds fever fewer fiats fiber ficus fiefs field fiend fiery fifes fifth fifty fight filch
filed filer files filet fills filly films filmy filth final finch finds fined finer fines finis
finny fired fires firms first firth fishy fists fitly fiver fives fixed fixer fixes fjord flack
flags flail flair flake flaky flame flank flaps flare flash flask flats flaws flays fleas fleck
flees fleet flesh flick flied flier flies fling flint flips flirt flits float flock floes flogs
flood floor flops flora floss flour flout flown flows flubs flues fluff fluid fluke flume flung
flunk flush flute flyby flyer foals foams foamy focal focus foggy foils foist folds folio folks
folly fonts foods fools foots foray force fords forge forgo forks forms forte forth forts forty
forum fouls found fount fours fowls foxes foyer frail frame franc frank frats fraud frays freak
freed freer frees fresh frets friar fried fries frill frisk frizz frock frogs frond front frost
froth frown froze fruit fryer fudge fuels fugue fully fumed fumes funds fungi funky funny furls
furor furry fused fuses fussy fusty futon fuzzy gable gaffe gaily gains gaits galas gales galls
gamed gamer games gamey gamma gamut gangs gaped gapes garbs gases gasps gassy gated gates gator
gauge gaunt gauze gauzy gavel gawks gawky gayer gazed gazer gazes gears gecko geeks geeky geese
gelid genes genie genoa genre gents genus germs ghost ghoul giant gibes giddy gifts gilds gills
gilts gimme gimpy girds girls girth gismo given giver gives gizmo glade glads gland glare glass
glaze gleam glean glees glens glide glint glitz gloat globe globs gloom glory gloss glove glows
glued glues gluey gluts glyph gnarl gnash gnats gnome goads goals goats godly gofer going golds
golfs gonad goner gongs gonna gooey goofs goofy goons goose gorge gorse gouge gourd gowns grabs
grace grade grads graft grail grain grams grand grant grape graph grasp grass grate grave gravy
grays graze great greed green greet greys grids grief grill grime grimy grind grins gripe grips
grist grits groan groin groom grope gross group grout grove growl grown grows grubs gruel gruff
grump grunt guano guard guava guess guest guide guild guile guilt guise gulch gulfs gulls gully
gulps gumbo gummy guppy gurus gusto gusts gusty gutsy guyed gypsy gyros habit hacks hafts haiku
hails hairs hairy haled hales halls halon halos halts halve hands handy hangs hanks happy hardy
hared harem hares harks harms harps harpy harsh haste hasty hatch hated hater hates haunt haven
haves havoc hawks hazed hazel hazes heads heady heals heaps heard hears heart heath heats heave
heavy hedge heeds heels hefts hefty heirs heist helix hello hells helms helps hemps hence herbs
herds heron hertz hewed hewer hexed hexes hider hides highs hiked hiker hikes hilly hilts hinds
hinge hints hippo hippy hired hirer hires hitch hived hives hoard hoary hobby hocks hoist hokey
holds holes homed homer homes homey honed hones honey honks honor hoods hoofs hooks hooky hoops
hoots hoped hopes horde horns horny horse horsy hosed hoses hosts hotel hotly hound hours house
hovel hover howdy howls hubby huffs huffy huger hulks hulls human humid humor humps humus hunch
hunks hunts hurls hurry hurts husks husky hussy hutch hydra hyena hymns hyped hyper hypes hypno
icing ideal ideas idiom idiot idled idler idles idols igloo iliac image imams imbed imbue impel
imply inane inapt incur index inept inert infer infix ingot inked inker inlay inlet inner input
inset inter intro inure irate irked irons irony isles issue itchy items ivory jabot jacks jaded
jades jails jambs jaunt jawed jazzy jeans jeeps jeers jelly jerks jerky jetty jewel jiffy jilts
jingo jinks jived jives jocks joins joint joist joked joker jokes jolly jolts joule joust jowls
judge judos juice juicy julep jumbo jumps jumpy junco junks junky junta juror kappa kaput karat
karma kayak kebab keels keens keeps kelps kempt kendo kerbs kerns ketch keyed khaki kicks kiddo
kills kilns kilos kilts kinds kings kinks kinky kiosk kites kitty kiwis knack knave knead kneed
kneel knees knell knelt knife knits knobs knock knoll knots known knows koala kudos kudzu label
labor laced laces lacks laded laden lades ladle lager lairs laity lakes lambs lamed lamer lames
lamps lance lands lanes lanky lapel lapse larch large largo larks larva laser lasso lasts latch
later latex lathe laths latte laugh laved lawns laxly layer leach leads leafy leaks leaky leans
leant leaps leapt learn lease leash least leave ledge leech leeks leers leery lefts lefty legal
leggy legit lemma lemon lemur lends lento leper letup levee level lever liars libel liens lifer
lifts light liked liken liker likes lilac lilts limbo limbs limed limes limit limns limos limps
lined linen liner lines lingo links lints linty lions lipid liras lisps lists liter lithe litre
lived liven liver lives livid llama loach loads loafs loamy loans loath lobby lobed lobes local
locks locus lodes lodge lofts lofty logic logos loins loner longs looks looms loons loony loops
loopy loose loots loped lopes lords lorry loser loses lotus louse lousy louts loved lover loves
lowed lower lowly loyal lucid lucks lucky lucre lulls lumen lumps lumpy lunar lunch lunge lungs
lupin lupus lurch lured lures lurid lurks lusts lusty lutes lying lymph lynch lyres lyric macaw
maced maces macho macro madam madly mafia magic magma maids mails maims mains maize major maker
makes males malls malts mamba mambo mamma manes mange mango mangy mania manic manly manor manse
maple march mares marks marry marsh marts masks mason masts match mated mater mates matte mauls
mauve maven maxim maybe mayor mazes meals mealy means meant meats meaty mecha medal media medic
meets melds melee melon melts memos mends menus meows mercy merge merit merry mesas messy metal
meted meter metre metro mewls micro midge midst miens might miked mikes miler miles milks milky
mills mimed mimes mimic mince minds mined miner mines minim minis minks minor mints minty minus
mired mires mirth miser mists misty miter mitts mixed mixer mixes moans moats mocha mocks modal
model modem modes moist molar molds moldy moles molls molts money month moods moody mooed moons
moors moose moots moped mopes moral moray morel morns moron morph mossy motel motes moths motif
motor motto mould moult mound mount mourn mouse mousy mouth moved mover moves movie mowed mower
mucks mucky mucus muddy muffs muggy mulch mules mulls multi mummy mumps munch mural murks murky
mused muses mushy music musks musky musty muted muter mutes mutts mynah myrrh myths nabob nacho
nadir nails naive naked named namer names nanny napes nappy narcs nasal nasty natal natty naval
navel needs needy neigh nerds nerdy nerve nervy nests never newer newly newsy newts nexus nicer
niche nicks niece nifty night nines ninja ninny ninth nippy niter nitro nixed noble nobly nodal
nodes noise noisy nomad nooks noons noose norms north nosed noses nosey notch noted noter notes
nouns novas novel noway nubby nudes nudge nuked nukes numbs nurse nutty nylon nymph oaken oared
oases oasis oaten oaths obese obeys occur ocean ocher ochre octal octet odder oddly odium offal
offer often ogled ogler ogles ogres oiled oiler oinks okapi okays olden older oldie olive omega
omens omits onion onset oomph oozed oozes opals opens opera opine opium opted optic orals orate
orbit order organ oriel other otter ought ounce ousts outdo outed outer outgo ovals ovary ovate
ovens overs overt ovoid ovule owing owlet owned owner oxide ozone paced pacer paces packs pacts
paddy padre pagan paged pager pages pails pains paint pairs paled paler pales palls palms palmy
palsy panda panel panes pangs panic pansy pants papal papas paper pappy parch pared parer pares
parka parks parry parse parts party pasha passe pasta paste pasts pasty patch paten pater pates
paths patio patsy pause paved paver paves pawed pawls pawns payee payer peace peach peaks peaky
peals pearl pears pecan pecks pedal peeks peels peeps peers peeve pekoe pelts penal pence pends
penny peons peony peppy perch peril perks perky perms pesky pesos pesto pests petal peter petit
petty pewee phase phial phone phony photo phyla piano picas picks picky picot piece piers piety
piggy piked piker pikes pilaf piled piles pills pilot pimps pinch pined pines pings pinks pinky
pinto pints pinup pions pious piped piper pipes pique pitch piths pithy piton pivot pixel pixie
pizza place plaid plain plait plane plank plans plant plate plays plaza plead pleas pleat plebe
plied plier plies plods plops plots plows ploys pluck plugs plumb plume plump plums plunk plush
plyer poach pocks podia poems poesy poets point poise poked poker pokes pokey polar poled poler
poles polio polka polls polos polyp pomps ponds pones pooch pools poops popes poppy porch pored
pores porgy porks porky ports posed poser poses posit posse posts potty pouch pound pours pouts
pouty power poxes prams prank prate prawn prays preen preps press preys price pricy pride pried
prier pries prigs prima prime primo primp print prior prise prism privy prize probe prods profs
promo prone prong proof props prose prosy proud prove prowl proxy prude prune psalm pubes pubic
pubis puces pucks pudgy puffs puffy puked pukes pulls pulps pulpy pulse pumas pumps punch punks
punky punts pupae pupal pupas pupil puppy puree purer purge purls purrs purse pushy putty pygmy
pylon pyres quack quads quaff quail quake quaky qualm quant quark quart quash quasi quays queen
queer quell query quest queue quick quids quiet quiff quill quilt quins quips quire quirk quirt
quite quits quota quote quoth rabbi rabid raced racer races racks radar radii radio radix radon
rafts ragas raged rages raids rails rainy raise rajah raked raker rakes rally ramen ramps ranch
rands randy range rangy ranks rants raped rapes rapid rarer rasps raspy ratio ratty raved ravel
raven raver raves rawer rayon razed razes razor reach react reads ready realm reams reaps rearm
rears rebel rebus rebut recap recur redid redly redox reeds reedy reefs reeks reels refer refit
regal rehab reign reins relax relay relic relit remit remix renal rends renew rents repay repel
reply reset resin resit rests retch retro retry reuse revel revue rheas rhino rhyme rials ricer
rices ricks ridge ridgy rifle rifts right rigid rigor riled riles rills rimed rimes rinds rings
rinse riots ripen riper risen riser rises risks risky rites ritzy rival riven river rivet roach
roads roams roars roast robed robes robin robot rocks rocky rodeo roger rogue roils roles rolls
romps roods roofs rooks rooms roomy roost roots ropes ropey roses rosin rotas rotor rouge rough
round rouse roust route routs rover roves rowdy rowed rowel rower royal ruble ruddy ruder ruffs
rugby ruing ruins ruled ruler rules rumba rumen rumor rumps runes rungs runny runts runty rupee
rural ruses rusks rusts rusty sabot sabre sacks sadly safer safes sagas sages saggy sails saint
saith sakes salad sales salon salsa salts salty salve salvo samba sandy saner sappy sarge sassy
satay sated sates satin satyr sauce saucy sauna saved saver saves savor savvy sawed scabs scads
scald scale scalp scaly scamp scans scant scare scarf scarp scars scary scats scene scent scion
scoff scold scone scoop scoot scope score scorn scour scout scowl scram scrap scree screw scrim
scrip scrub scrum scuba scuds scuff scull scums scurf seals seams seamy sears seats sedan sedge
seeds seedy seeks seems seeps seers seize sells semen sends sense sepal sepia septa serfs serge
serum serve servo setup seven sever sewed sewer sexed sexes shack shade shads shady shaft shags
shake shaky shale shall shalt shame shams shank shape shard share shark sharp shave shawl sheaf
shear sheds sheen sheep sheer sheet sheik shelf shell shied shier shies shift shims shine shins
shiny ships shire shirk shirt shoal shock shoed shoes shone shook shoos shoot shops shore shorn
short shots shout shove shown shows showy shred shrew shrub shrug shuck shuns shunt shush shuts
shyer shyly sibyl sicko sided sider sides sidle siege sieve sifts sighs sight sigil sigma signs
silks silky sills silly silos silty since sinew singe sings sinks sinus sired siren sires sisal
sissy sitar sited sites sixes sixth sixty sized sizer sizes skate skeet skein skews skids skied
skier skies skiff skill skimp skims skins skips skirt skits skulk skull skunk slabs slack slags
slain slake slams slang slant slaps slash slate slats slave slaws slays sleds sleek sleep sleet
slept slews slice slick slide slime slims slimy sling slink slips slits slobs sloes slogs sloop
slope slops slosh sloth slots slows slugs slump slums slung slunk slurp slurs slush slyly smack
small smart smash smear smell smelt smile smirk smite smith smock smogs smoke smoky smote snack
snafu snags snail snake snaky snaps snare snarl sneak sneer snide sniff snipe snips snits snobs
snood snoop snoot snore snort snots snout snows snowy snubs snuff snugs soaks soaps soapy soars
sober socks sodas sofas softy soggy soils solar soled soles solid solos solve sonar songs sonic
sonny sooth sooty sorer sorry sorta sorts souls sound soups soupy sours south sowed sower space
spade spake spank spans spare spark spars spasm spate spats spawn spays speak spear speck specs
speed spell spelt spend spent sperm spews spice spicy spied spiel spies spike spiky spill spilt
spine spins spiny spire spite spits splat splay split spoil spoke spoof spook spool spoon spoor
spore sport spots spout spray spree sprig spuds spume spunk spurn spurs spurt squab squad squat
squib squid stabs stack staff stage stags staid stain stair stake stale stalk stall stamp stand
stank staph stare stark stars start stash state stats stave stays stead steak steal steam steed
steel steep steer stein stems steps stern stews stick sties stiff stile still stilt sting stink
stint stirs stoat stock stoic stoke stole stomp stone stony stood stool stoop stops store stork
storm story stout stove stows strap straw stray strep strew strip strop strum strut stubs studs
study stuff stump stung stunk stunt style styli suave sucks sudsy suede sugar suing suite suits
sulks sulky sully sumac sumps sunny sunup super surer surfs surge surly sushi swabs swags swain
swami swamp swank swans swaps sward swarm swash swath swats sways swear sweat swede sweep sweet
swell swept swift swigs swill swims swine swing swipe swirl swish swoon swoop sword swore sworn
swung sylph synod syrup tabby table taboo tacit tacks tacky tacos taffy tails taint taken taker
takes tales talks tally talon tamed tamer tames tamps tango tangs tangy tanks tansy taped taper
tapes tapir tardy tared tares tarns tarot tarps tarry tarts taste tasty tatty taunt taupe tawny
taxed taxes taxis teach teaks teals teams tears teary tease teats techs teddy teems teens teeny
teeth telex tells tempi tempo temps tempt tench tends tenet tenor tense tenth tents tepee tepid
terms terns terse tests testy texts thane thank thaws theft their theme there therm these theta
thick thief thigh thine thing think thins third thong thorn those thous three threw throb throw
thrum thuds thugs thumb thump thyme tiara tibia ticks tidal tided tides tiers tiger tight tikes
tiled tiler tiles tills tilts timed timer times timid tinge tings tinny tints tipsy tired tires
titan titer tithe title toads toast today toddy togas toils toked token tokes tolls tombs tomes
tonal toned toner tones tonga tonic tools tooth toots topaz toped topic toque torch torso torts
torus total toted totem totes touch tough tours touts towed towel tower towns toxic toxin toyed
trace track tract trade trail train trait tramp traps trash trawl trays tread treat treks trend
trews triad trial tribe trice trick tried trier tries trill trims trios tripe trips trite troll
tromp troop trope troth trout trove truce truck truer truly trump trunk truss trust truth tryst
tubal tubby tubed tuber tubes tucks tufts tulip tulle tumor tunas tuned tuner tunes tunic turbo
turds turfs turns tutor tutus tuxes twain twang tweak tweed tween tweet twerp twice twigs twill
twine twins twirl twist twits tying tykes typal typed types typos tyros udder ulcer ultra umber
umbra unbar uncle uncut under undid undue unfed unfit unify union unite units unity unlit unmet
unpin unsay untie until unwed unzip upend upper upset urban urged urges urine usage users usher
using usual usurp usury uteri utile utter uvula vague valet valid valor value valve vamps vaned
vanes vapid vapor vases vault vaunt veals veers vegan veils veins velar veldt vends venom vents
venue verbs verge verse verso verve vests vetch vexed vexes vials viand vibes vicar vices video
views vigil vigor viler villa vinyl viola viols viper viral vireo virus visas vises visit visor
vista vital vivid vixen vocal vodka vogue voice voids voile voles volts vomit voted voter votes
vouch vowed vowel vroom vulva wacko wacky waded wader wades wadis wafer wafts waged wager wages
wagon waifs wails waist waits waive waked waken wakes wales walks walls waltz wands waned wanes
wanly wanna wants wards wares warms warns warps warts warty washy wasps waste watch water watts
waved waver waves waxed waxen waxes weals weans wears weary weave wedge wedgy weeds weedy weeks
weeny weeps weepy wefts weigh weird welds wells welts wench wends whack whale wharf whats wheal
wheat wheel whelk whelm whelp whens where whets which whiff while whims whine whiny whips whirl
whirr whisk whist white whits whole whoop whorl whose wicks widen wider widow width wield wilds
wiles wilts wimps wimpy wince winch winds windy wined wines wings winks winos wiped wiper wipes
wired wires wiser wisps wispy witch witty wives woken wolfs woman wombs women woods woody wooed
wooer wools woozy words wordy works world worms wormy worry worse worst worth would wound woven
wowed wrack wraps wrath wreak wreck wrens wrest wring wrist write writs wrong wrote wroth wrung
wryly xenon xerox xylem yacht yanks yards yarns yawls yawns yeahs yearn yeast yells yelps yeses
yield yodel yogas yogis yoked yokel yokes yolks young yours youth yowls yucca yucky yummy zebra
zeros zesty zilch zincs zings zippy zonal zoned zones zooms
`;

export default ACCEPT;
