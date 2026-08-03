/* ============================================================================
 * ui/strings.js — every user-facing string on the Goon Game page.
 *
 * ONE place, so a copy pass is a diff in this file and never a hunt through
 * seven screens. Register: lowercase, cheeky-dignified, never shouty. Sentence
 * case only where the string is a full sentence (the fineprint, the sheets).
 *
 * Rules that are load-bearing, not stylistic:
 *   - NOTHING here formats a secret (codes are the only identifier a screen
 *     ever renders; tokens live in bridge's net config and never reach the UI).
 *   - Interpolators are FUNCTIONS, not template literals evaluated at import,
 *     so this module is import-safe under node (no DOM, no side effects).
 *   - The mercy copy is not here: sibling I owns ui/mercy.js and its wording is
 *     part of the safety contract, not the copy deck.
 * ==========================================================================*/

import { GoonElement } from '../core/contracts.js';

/** mm:ss for a millisecond duration. Negative and NaN clamp to 0:00. */
export function mmss(ms) {
  const total = Math.max(0, Math.floor((Number(ms) || 0) / 1000));
  const m = Math.floor(total / 60);
  const s = total % 60;
  return m + ':' + String(s).padStart(2, '0');
}

/** "1 min" / "12 min" — the consent slider's value chip. */
export function minutes(sec) {
  const m = Math.max(1, Math.round((Number(sec) || 0) / 60));
  return m + ' min';
}

export const S = Object.freeze({
  /* ---------------------------------------------------------------- title */
  title: {
    kicker: '1v1 · endurance duel · first to break loses',
    host: 'Host a match',
    join: 'Join with a code',
    practice: 'Practice',
    practiceNote: 'solo · scripted opponent',
    options: 'Options',
    how: 'How it works',
    quit: 'Quit',
    fineprint: 'Both players endure their own library. Nothing you own leaves this machine.',
  },

  /** The "how it works" modal — exactly six bullets, in reading order. */
  how: {
    headline: 'how it works',
    bullets: [
      'two players, one clock. you both endure your own library at the same time.',
      'you both agree what stays switched on. whatever is left, you BOTH endure — same effects, same moments.',
      'holding still earns points; enduring what they send you earns charges.',
      'charges buy payloads you fire at them. the receiver decides what it can run.',
      'mercy is always one key away. escape, any phase, no confirmation.',
      'if nobody breaks before the clock runs out, the higher score wins.',
    ],
    close: 'got it',
  },

  /* ----------------------------------------------------------------- host */
  host: {
    minting: 'minting room…',
    open: 'room open',
    copy: 'Copy invite line',
    copied: 'Copied',
    waiting: 'Waiting for your opponent…',
    cancel: 'Cancel',
    expiresIn: (ms) => 'expires in ' + mmss(ms),
    expired: 'this code expired. mint a new one.',
    inviteLine: (code) => 'Goon Game duel — code ' + code + ' (expires in 5 min)',
  },

  /* ----------------------------------------------------------------- join */
  join: {
    eyebrow: 'join a room',
    lead: 'type the six characters they sent you.',
    action: 'Join',
    joining: 'joining…',
    back: 'Back',
    errUnknown: 'No room with that code. Check the last character?',
    errFull: 'That room already has two players.',
    errExpired: 'That code expired. Ask for a fresh one.',
    errShort: 'six characters, please.',
  },

  /* ---------------------------------------------------------------- lobby */
  lobby: {
    eyebrowWaiting: 'waiting for them',
    eyebrowReady: 'agree the terms',
    you: 'you',
    them: 'them',
    noCam: 'no cam',
    cam: 'cam',
    unknown: '—',
    connecting: 'connecting…',
    direct: 'direct connection',
    relay: 'relayed connection',
    offline: 'not connected',
    duration: 'Match length',
    toyCap: 'Toy cap',
    toyCapDisabled: 'No toy connected',
    gap: 'Payload spacing',
    gapValue: (sec) => '1 payload / ' + sec + 's',
    confirm: "I'm in",
    confirmed: (name) => 'Ready — waiting for ' + (name || 'them'),
    changed: 'Settings changed — both of you confirm again.',
    leave: 'Leave',
    lampYou: 'you',
    lampThem: 'them',
  },

  /* ---------------------------------------------------------------- draft */
  draft: {
    eyebrow: 'agree what you both endure',
    lead: 'everything is on. switch off what you will not take — you both get whatever is left.',
    pickCta: 'Pick 3',
    lock: 'Lock in',
    locked: 'locked — waiting for them',
    theirs: 'their picks',
    risk: (n) => 'Match risk ' + n + ' / 7',
    score: (mult) => 'score ×' + mult.toFixed(2),
    unsupported: "your opponent's app can't do this",
    /* --- the agreement pass (2026-08-03 redesign) --- */
    confirm: "I'm good with this",
    confirmed: 'signed — waiting for them',
    theirSignature: 'they signed',
    theirWait: 'still deciding',
    theirsOff: 'they switched this off',
    alwaysOn: 'always on',
    alwaysOnWhy: 'bubbles run the whole match, for both of you. they build.',
    pool: (n) => 'you both get ' + n + ' effect' + (n === 1 ? '' : 's') + ' + bubbles',
    tooFewYours: (min) => 'keep at least ' + min + ' switched on.',
    tooFewShared: (n) => 'you two only agree on ' + n + ' — one of you has to open something up.',
    changed: 'something moved — both of you sign again.',
    rolled: 'the running order is rolled from the match seed. same for both of you.',
  },

  /* ------------------------------------------------------------ countdown */
  countdown: {
    go: 'go',
  },

  /* ---------------------------------------------------------------- recap */
  recap: {
    held: 'You held.',
    broke: 'You broke.',
    draw: 'Draw.',
    vanished: 'They vanished.',
    disputed: 'Results disagree — both were recorded.',
    unconfirmed: 'Unconfirmed — waiting on the other side.',
    mercyLine: (name, ms) => (name || 'they') + ' pressed mercy at ' + mmss(ms) + '.',
    sdLine: (a, b) => 'The clock ran out, ' + a + '–' + b + '.',
    abandonLine: 'Connection lost for a minute.',
    drawLine: 'You both let go at the same moment.',
    scoreline: 'scoreline',
    scoreFineprint: (risk) => '1 pt/s · risk ×' + risk.toFixed(2),
    survived: (ms) => 'survived ' + mmss(ms),
    payloads: 'payload log',
    noPayloads: 'nothing crossed the wire.',
    showAll: (n) => 'Show all (' + n + ')',
    titles: 'titles',
    rematch: 'Rematch',
    rematchSoon: 'soon',
    back: 'Back to menu',
    chipLanded: 'landed',
    chipEndured: 'endured',
    chipEnduredNote: '+1 charge for them',
    chipBlocked: 'blocked',
    chipTooSoon: 'too soon',
    dirIn: 'they sent',
    dirOut: 'you sent',
  },

  /** Locally computed cosmetics. Nothing here reaches the server. */
  titles: Object.freeze({
    graceful: { name: 'Graceful', why: 'went eight minutes, then bowed out on your own terms.' },
    ironEdge: { name: 'Iron Edge', why: 'the clock broke before you did.' },
    stoneWall: { name: 'Stone Wall', why: 'four of theirs, straight through, no flinching.' },
    untouchable: { name: 'Untouchable', why: 'nothing they threw ever reached you.' },
    gg: { name: 'GG', why: 'broke, but never looked away.' },
  }),

  /* -------------------------------------------------------------- options */
  options: {
    headline: 'options',
    master: 'Master',
    music: 'Music',
    sfx: 'SFX',
    motion: 'Reduce motion',
    skippable: 'Skippable videos',
    /* Says what the toggle does AND what it does not take away: a window can
       always be muted with a click, on or off. */
    skippableNote: 'gives each floating video an ✕ to close it early. off, they run their course. either way a click mutes one, and a right-click hands it the sound.',
    fullscreen: 'Fullscreen',
    reset: 'Reset',
    close: 'close',
    lockedNote: 'Match settings are locked once you start.',
  },

  /* --------------------------------------------------------------- sheets */
  sheets: {
    noPass: {
      icon: '✦',
      headline: 'Your free match is spent',
      line: (when) => 'Your next free match unlocks ' + (when || 'next week') + '.',
    },
    notDeployed: {
      icon: '⏳',
      headline: 'Warming up',
      line: 'The duel server is not answering yet. Try again in a minute.',
    },
    rateLimited: {
      icon: '⏱',
      headline: 'Slow down a moment',
      line: (sec) => (sec ? 'Try again in ' + sec + 's.' : 'Try again shortly.'),
    },
    network: {
      icon: '⚡',
      headline: 'Could not reach the server',
      line: 'Check your connection and try again.',
      action: 'Try again',
    },
    unauthorized: {
      icon: '🔒',
      headline: 'Signed out',
      line: 'Reconnect your account in the app, then come back.',
    },
    connectFailed: {
      icon: '🕸',
      headline: 'Could not connect',
      line: 'Neither a direct nor a relayed link came up. Try again.',
    },
    lobbyFailed: {
      icon: '⚙',
      headline: 'This pairing will not work',
      line: (reason) => String(reason || 'the two clients could not agree on a shared ruleset.'),
    },
    ok: 'OK',
    cancel: 'Cancel',
    retry: 'Try again',
  },

  /* ------------------------------------------------- the opponent monitor */
  monitor: {
    idle: 'quiet',
    dropHint: 'drop it here',
    /** The green checkmark on their projection: they took the whole payload. */
    passed: 'they held it',
  },

  /* ---------------------------------------------- the announcer ribbon (ui/announcer.js)
   * The one place on the page that raises its voice, because it is calling a
   * thing that is about to happen to BOTH of you (the ramp is one shared roll).
   * Sentence case with a real exclamation mark is deliberate here and nowhere
   * else: `ready` is a warning shouted across a room, `on` is the flat statement
   * that it landed. Keyed by GoonElement code.
   *
   * BUBBLES HAVE NO LINE ON PURPOSE — they are on from t=0 to the end for both
   * players, so an announcement would fire once at zero and mean nothing. */
  announce: {
    ready: {
      [GoonElement.Flashes]: 'Get ready to stare!',
      [GoonElement.Videos]: 'Get ready to watch!',
      [GoonElement.Subliminals]: 'Get ready to soak it up!',
      [GoonElement.LockCards]: 'Get ready to type!',
      [GoonElement.ToyPatterns]: 'Get ready to buzz!',
      [GoonElement.BrainDrain]: 'Get ready to melt!',
      [GoonElement.BouncingText]: 'Get ready to read!',
      [GoonElement.Spiral]: 'Get ready to sink!',
    },
    on: {
      [GoonElement.Flashes]: 'Flashes on',
      [GoonElement.Videos]: 'Video on',
      [GoonElement.Subliminals]: 'Subliminals on',
      [GoonElement.LockCards]: 'Lock card!',
      [GoonElement.ToyPatterns]: 'Toy on',
      [GoonElement.BrainDrain]: 'Brain drain on',
      [GoonElement.BouncingText]: 'Bouncing text on',
      [GoonElement.Spiral]: 'Spiral on',
    },
  },

  /* ------------------------------------------------- the arsenal + its economy
   * Items are EARNED, not merely afforded: a slot sits locked until a popped
   * bubble drops one (exec/bubbles.js -> ui/drops.js -> ui/arsenal.js). Every
   * state below is a WORD on the tile, because the greyed sticker alone is
   * colour and colour is never the only channel here. */
  arsenal: {
    locked: 'locked',
    lockedTip: 'pop bubbles to earn this',
    lockGlyph: '?',
    /** the ×N badge on an armed sticker */
    stack: (n) => '×' + (n | 0),
    /** the flourish when a drop lands */
    drop: (label) => '+1 ' + (label || 'item'),
    dropToast: (label) => (label || 'item') + ' dropped',
  },

  /* --------------------------------------------------------------- toasts */
  toasts: {
    copied: 'invite line copied',
    copyFailed: 'could not copy — select the code instead',
    peerWobbly: 'their connection is wobbling',
    peerBack: 'they are back',
    charge: '+1 charge',
    left: 'left the match',
  },
});

/**
 * The draftable elements, in the order the draft grid renders them.
 * `risk` mirrors core/draft.js riskTierOf — recomputed live from the engine at
 * render time; the value here is documentation and a fallback, never authority.
 */
export const ELEMENTS = Object.freeze([
  { id: GoonElement.Flashes, name: 'flashes', risk: 0, blurb: 'constant. builds all match.' },
  { id: GoonElement.BouncingText, name: 'bouncing text', risk: 0, blurb: 'always on your screen. slow burn.' },
  { id: GoonElement.Subliminals, name: 'subliminals', risk: 1, blurb: 'quiet, and it climbs to the top.' },
  // Always on for both players, start to finish — not a toggle. The draft grid renders it as a
  // locked tile (core/draft.js ALWAYS_ON_ELEMENT); the blurb has to say so.
  { id: GoonElement.Bubbles, name: 'bubbles', risk: 1, blurb: 'the whole match, both of you. thin at first, then not.' },
  { id: GoonElement.Videos, name: 'videos', risk: 2, blurb: 'long takeovers. 1–2 minutes.' },
  { id: GoonElement.LockCards, name: 'lock cards', risk: 2, blurb: "type it out. can't look away." },
  { id: GoonElement.ToyPatterns, name: 'toy patterns', risk: 2, blurb: 'bursts, capped by your own limit.' },
  { id: GoonElement.Spiral, name: 'spiral', risk: 2, blurb: "a slow spiral takes the whole screen. it doesn't blink. neither will they." },
  { id: GoonElement.BrainDrain, name: 'brain drain', risk: 3, blurb: "late, heavy, and it doesn't stop." },
]);


export default S;
