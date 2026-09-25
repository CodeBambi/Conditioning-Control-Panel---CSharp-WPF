/* ============================================================================
 * ui/duel/copy.js - every word the game night duel puts on screen.
 *
 * Kept beside the feature instead of in ui/strings.js so three parallel lanes
 * do not all edit the same block. Voice: dry, short, plain. No exclamation
 * marks in chrome. English lives here; the other languages are the gg_duel_*
 * keys in i18n/<code>.js (see core/i18n.js).
 * ==========================================================================*/

import { deck, tpl, one } from '../../core/i18n.js';

export const DUEL_COPY_RAW = Object.freeze({
  cardLabel: 'game card',
  /** The one-line hint, shown once ever, on the first game card this player gets. */
  firstHint: 'Throw it and you both play the same quick Arcademy game.',
  incomingThem: 'Incoming game',
  incomingYou: 'Game on',
  gameName: 'The Deep End',
  rule: 'Same game, same seed. Best result wins.',
  busy: 'finish the game first',
  timeLeft: tpl('{n}s', (s) => ({ n: Math.max(0, s | 0) })),
  waiting: 'waiting for their board',
  won: tpl('won +{bonus}', (bonus) => ({ bonus })),
  lost: 'lost',
  /** Points model only: the stamp says the outcome and the pot line says what it paid. */
  wonPlain: 'won',
  potLine: (points) => '+' + Math.max(0, Math.round(Number(points) || 0)),
  tied: 'dead even',
  youLine: tpl('you {tile} / {score}', (tile, score) => ({ tile, score })),
  themLine: tpl('them {tile} / {score}', (tile, score) => ({ tile, score })),
  youPoints: tpl('you {score}', (score) => ({ score })),
  themPoints: tpl('them {score}', (score) => ({ score })),
  stripLabel: tpl('game night: {name}', (name) => ({ name })),
  unavailable: 'This game is not in this build. Sit tight, it counts as zero.',
  customize: 'Customize',
  lengthLabel: 'Game card length',
  lengthChoice: tpl('{sec}s', (sec) => ({ sec })),
  hostWins: "The host's pick is the one you play.",
  /* ui/duel/games.js - the rule line under each game's name. The names are the games' own. */
  ruleDeepEnd: 'Swipe or arrow keys. Deepest tile wins.',
  ruleSort: 'Swipe right for moving, left for still.',
  /* The Sort duel's VS reveal (ui/duel/noiseView.js) and the pre-match noise pick (ui/screens/noiseSetup.js). */
  vs: 'VS',
  revealYou: 'you',
  revealThem: 'them',
  revealRule: 'Keep yours. Bin the noise.',
  houseMix: 'house mix',
  botNiche: 'the bot',
  nicheMore: tpl('+{n} more', (n) => ({ n })),
  pickTitle: 'Pick your noise',
  pickLine: 'What you bin when a Sort game drops. One is picked already.',
  setupGo: 'Done',
  pickedYou: 'your noise',
  pickedThem: 'their noise',
  rolled: 'picked for you',
  noiseArchitecture: 'Architecture',
  noiseLandscapes: 'Landscapes',
  noiseSpace: 'Space',
  noiseFood: 'Food',
  noiseCars: 'Cars',
  noiseRooms: 'Rooms',
  noiseCats: 'Cats',
  recapLine: tpl({ one: '+ {n} game won', other: '+ {n} games won' }, (won) => ({ n: won }), one),
});

export const DUEL_COPY = deck('gg_duel', DUEL_COPY_RAW);

export default DUEL_COPY;
