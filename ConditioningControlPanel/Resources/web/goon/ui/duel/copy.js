/* ============================================================================
 * ui/duel/copy.js - every word the game night duel puts on screen.
 *
 * Kept beside the feature instead of in ui/strings.js so three parallel lanes
 * do not all edit the same block. Voice: dry, short, plain. No exclamation
 * marks in chrome.
 * ==========================================================================*/

export const DUEL_COPY = Object.freeze({
  cardLabel: 'game card',
  /** The one-line hint, shown once ever, on the first game card this player gets. */
  firstHint: 'Throw it and you both play the same quick Arcademy game.',
  incomingThem: 'Incoming game',
  incomingYou: 'Game on',
  gameName: 'The Deep End',
  rule: 'Same game, same seed. Best result wins.',
  busy: 'finish the game first',
  timeLeft: (s) => Math.max(0, s | 0) + 's',
  waiting: 'waiting for their board',
  won: (bonus) => 'won +' + bonus,
  lost: 'lost',
  /** Points model only: the stamp says the outcome and the pot line says what it paid. */
  wonPlain: 'won',
  potLine: (points) => '+' + Math.max(0, Math.round(Number(points) || 0)),
  tied: 'dead even',
  youLine: (tile, score) => 'you ' + tile + ' / ' + score,
  themLine: (tile, score) => 'them ' + tile + ' / ' + score,
  youPoints: (score) => 'you ' + score,
  themPoints: (score) => 'them ' + score,
  stripLabel: (name) => 'game night: ' + name,
  unavailable: 'This game is not in this build. Sit tight, it counts as zero.',
  customize: 'Customize',
  lengthLabel: 'Game card length',
  lengthChoice: (sec) => sec + 's',
  hostWins: "The host's pick is the one you play.",
  recapLine: (won) => '+ ' + won + (won === 1 ? ' game won' : ' games won'),
});

export default DUEL_COPY;
