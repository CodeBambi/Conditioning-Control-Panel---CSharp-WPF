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
  firstHint: 'Throw it and you both play a quick round of 2048.',
  incomingThem: 'Incoming game',
  incomingYou: 'Game on',
  gameName: 'The Deep End',
  rule: 'Swipe or arrow keys. Biggest tile wins.',
  busy: 'finish the game first',
  timeLeft: (s) => Math.max(0, s | 0) + 's',
  waiting: 'waiting for their board',
  won: (bonus) => 'won +' + bonus,
  lost: 'lost',
  tied: 'dead even',
  youLine: (tile, score) => 'you ' + tile + ' / ' + score,
  themLine: (tile, score) => 'them ' + tile + ' / ' + score,
  customize: 'Customize',
  lengthLabel: 'Game card length',
  lengthChoice: (sec) => sec + 's',
  hostWins: "The host's pick is the one you play.",
  recapLine: (won) => '+ ' + won + (won === 1 ? ' game won' : ' games won'),
});

export default DUEL_COPY;
