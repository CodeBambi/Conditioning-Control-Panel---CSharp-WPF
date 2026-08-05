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
    // The dimmed-Host note, and since 2026-08-05 the ONLY "this costs money"
    // sentence in the duel — sending stopped being one. Lowercase, an explanation
    // rather than a pitch, and it names the thing that is still free instead of stopping at "no".
    hostNoLab: 'hosting is a supporter perk — joining a room is always free.',
    join: 'Join with a code',
    practice: 'Practice',
    practiceNote: 'solo · scripted opponent',
    assets: 'Media library',
    options: 'Options',
    how: 'How it works',
    quit: 'Quit',
    /* THE FINEPRINT SAYS WHAT ACTUALLY CROSSES (2026-08-05 privacy pass). It read
       "Nothing you own leaves this machine" until today, which was written before
       media transfer and voice notes existed and has been the exact opposite of
       the truth ever since: with sending switched on, copies of your images and
       clips are sent to your opponent, and a voice note is your recorded voice
       going to a stranger. Both are opt-in and both are off until you say so —
       which is the honest version of the same reassurance, so that is what it says. */
    fineprint: 'Both players endure their own library. Nothing you own is sent anywhere until you switch sending on — then it goes to your opponent, and nowhere else.',
  },

  /** The "how it works" modal — exactly six bullets, in reading order. */
  how: {
    headline: 'how it works',
    bullets: [
      'two players, one clock. you both endure your own library at the same time.',
      'you both agree what stays switched on. whatever is left, you BOTH endure — same effects, same moments.',
      /* These two read "…enduring what they send you earns charges." and
         "charges buy payloads you fire at them." until 2026-08-05, when the
         owner deleted the charge requirement. They now teach the loop that
         actually exists: pop bubbles, get items, throw when the cooldown lets
         you. STILL EXACTLY SIX BULLETS — do not let this become seven. */
      'holding still earns points; popping bubbles drops the items you throw.',
      'throw one whenever your cooldown is up. the receiver decides what it can run.',
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
    /* --- the shareable link (ui/inviteLink.js). The PRIMARY copy button: a
       code is a fine thing to read aloud and a miserable thing to thumb into a
       phone, and the link opens straight into the room with nothing to type.
       The plain-code button stays right beside it — some people paste into
       places a URL would be eaten. --- */
    copyLink: 'Copy invite link',
    copiedLink: 'Link copied',
    linkNote: 'the link opens the game and joins this room — no typing at their end.',
    inviteLinkLine: (url) => 'Goon Game duel — tap to join: ' + url + ' (expires in 5 min)',
  },

  /* ----------------------------------------------------------------- join */
  join: {
    eyebrow: 'join a room',
    lead: 'type the six characters they sent you.',
    /** Arrived on a `?join=` link: nothing to type, so nothing is asked for. */
    leadLinked: 'the link brought the code with it. one moment…',
    action: 'Join',
    joining: 'joining…',
    back: 'Back',
    errUnknown: 'No room with that code. Check the last character?',
    errFull: 'That room already has two players.',
    // NOT "full": the other seat is yours. Sending a player off to re-read a code
    // they typed correctly is the worst thing this screen can do.
    errSelf: 'That is your own room — the other player has to be on a different account.',
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
    /* --- the media-transfer opt-in. Per-side: this row says what YOU are
       willing to send, and the chip says what THEY answered. --- */
    transfer: 'Send them your own media',
    /* THE CONSENT SENTENCE FOR SHARING, and every clause in it is checked against
       the code (2026-08-05 privacy pass):
         · "sent to them"   — net/mediaQueue.js puts compressed copies on the wire.
                              This is the half the old copy buried; it now leads.
         · "encrypted"      — the bulk lane is a WebRTC data channel, so DTLS/SCTP.
         · "never through our servers" — `supportsBulk` is P2P-ONLY (see
                              net/mediaQueue.js:625 and paintTransfer below): on the
                              ws-mailbox relay the row greys out and nothing crosses.
                              A TURN hop is still this data channel and still sees
                              nothing but ciphertext.
         · "theirs to keep"  — exec/receivedStore.js writes it down on their side.
                              We cannot unsend it and the copy must not imply we can. */
    transferSub: 'a few of your images and clips are sent to your opponent — encrypted, straight to their machine, never through our servers. what lands on their side is theirs to keep.',
    /**
     * NO LONGER A PITCH, because it is no longer a perk (owner call 2026-08-05):
     * sending is free for every seat and the only thing money buys is HOSTING
     * (title.hostNoLab, above). Renamed off `transferNoPremium` so the key stops
     * lying about why the row is dark.
     *
     * Deliberately still HERE rather than deleted. caps.mediaTransfer can be
     * false in exactly two ways now — a server that answers `media_send:false`
     * (the policy hook the field was kept alive for), or a host frame so old it
     * never carried the flag — and a greyed row with no sentence under it is the
     * precise bug report this string family exists to prevent. Vague on purpose:
     * naming a cause we cannot tell apart would be a guess.
     */
    transferOff: 'sending is switched off for this seat — you can still receive theirs.',
    transferPeerOld: 'their app is too old for this — nothing crosses either way.',
    transferRelay: 'only on a direct connection. this one is relayed.',
    /**
     * ICE is still deciding (2026-08-05). The old code showed transferRelay
     * during the 10-20s negotiation window, players read the terminal verdict,
     * confirmed consent without the box, and the un-grey a few seconds later
     * went unseen — the peer then spent the whole match "toggle off".
     */
    transferConnecting: 'checking the connection — this unlocks if it comes up direct…',
    /**
     * The lane is ON but the local library has nothing compressed to offer
     * (round-11: every gate green, sendable=0, attacks silently fell back to
     * the receiver's own pool). Not a block — receiving still works and the
     * count climbs live as compression runs — just the honest reason.
     */
    transferNoAmmo: 'nothing ready to send yet — compress your library in the media screen, or your attacks will use their media instead.',
    transferTheirsOn: 'they opted in',
    transferTheirsOff: 'they have not',
    /* --- "they are still picking their media" (the `media_prep` wire message).
       The host used to sit on "waiting for them" for a minute with no idea
       whether anybody had arrived; this is the difference between a dead room
       and a room where somebody is busy. --- */
    eyebrowPicking: 'they are getting set up',
    prepPicking: (name) => (name || 'they') + ' joined — picking their media…',
  },

  /* -------------------------------------------------------------- discord
   * The sharing panel, the VS splash, the HUD minis and the recap plates.
   *
   * Two rules that are safety copy, not style:
   *   1. every line says WHO SEES WHAT. "your opponent" is named in the toggle
   *      notes because "share" on its own has been read as "publish" before;
   *   2. nothing here ever formats an id. The Message buttons take a NAME, and
   *      the page has no other identifier to leak (see ui/discord.js).
   * Lowercase furniture, sentence case for the sheets — the house register. */
  discord: {
    eyebrow: 'discord',
    lead: 'off by default. nothing is shared until you switch it on here.',
    you: 'you',
    /** The indicator on your own plate while a picture is going out. */
    visible: 'they can see your picture',

    toggleAvatar: 'Use my Discord picture',
    toggleAvatarNote: 'your opponent sees your avatar on the versus card, on the desk and on the end card. only them, only during a duel.',
    toggleDm: 'Allow Discord DMs',
    toggleDmNote: 'gives your opponent a Message button after the match. they never see your account name here, and you can switch this off again whenever you like.',
    toggleRp: 'Show Goon Game on Discord',
    toggleRpNote: 'your discord status reads "Goon Game" while you play. fixed words only — never your opponent, never what happened.',

    connectCta: 'Connect Discord',
    connectLine: 'connect discord in the app to use your picture. this only brings the window forward — nothing signs you in but you.',
    hostedOnly: 'this page is running in a plain browser. open the goon game from the app to connect discord.',

    lastTitle: 'last opponent',
    lastNone: 'nobody yet — whoever you duel next shows up here.',
    lastClear: 'forget them',
    messageOn: (name) => 'Message ' + (name || 'them') + ' on Discord',
    /** The recap's warmer variant, offered in the moment it means something. */
    ggMessage: (name) => 'GG! Message ' + (name || 'them'),
    dmShort: 'DM',

    agoNow: 'just now',
    agoUnknown: 'earlier',
    agoMinutes: (n) => n + ' min ago',
    agoHours: (n) => n + (n === 1 ? ' hour ago' : ' hours ago'),
    agoDays: (n) => n + (n === 1 ? ' day ago' : ' days ago'),

    /** Practice mode's opponent. Tile avatar, no DM — it is not a person. */
    practiceBot: 'Practice Bot',
    vs: 'VS',

    /** The one-time confirm, before the first duel with anything switched on. */
    sharePrompt: {
      icon: '👁',
      headline: 'Your opponent is about to see this',
      line: 'Your Discord picture and DM button will be visible to whoever you duel, for this match and every one after it. You can switch both off in the lobby at any time.',
      go: 'That is fine',
      cancel: 'Keep it private',
    },
    /** Every Message button goes through this — a browser opening mid-duel is a big surprise. */
    dmConfirm: {
      icon: '💬',
      headline: 'Open Discord?',
      line: (name) => 'This leaves the duel and opens ' + (name || 'their') + ' profile in your browser.',
      go: 'Open Discord',
      cancel: 'Stay here',
    },
  },

  /* ---------------------------------------------------------------- draft */
  draft: {
    eyebrow: 'agree what you both endure',
    lead: 'everything is on. switch off what you will not take — you both get whatever is left.',
    pickCta: 'Pick 3',
    lock: 'Lock in',
    locked: 'locked — waiting for them',
    theirs: 'their picks',
    /* NO `score` MULTIPLIER LINE (2026-08-05). The "match risk N/7" readout and
     * its seven-segment meters went on 2026-08-04; the multiplier they fed
     * ("you both score ×1.30") survived one more day and then went with the
     * rest of the risk indicator. It was the tier in disguise — the same engine
     * number, two decimal places, still no glossary. `pool` below says the same
     * thing in the unit the screen is actually about: how many effects you both
     * just agreed to take. */
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
    /* Was `'1 pt/s · score ×' + mult`, off the engine's riskMultiplier — the
     * recap's copy of the risk readout, and it left with the other two on
     * 2026-08-05. The BASE RATE stays because it is the one thing the scoreline
     * above it cannot say on its own, and attention is named instead of the
     * multiplier because attention is the only part of the formula that was
     * ever yours to move: the pool bonus was identical for both of you. */
    scoreFineprint: '1 pt/s, for as long as your attention holds',
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
    /* Was "+1 charge for them" — accurate until 2026-08-05, when charges stopped
       buying anything. Riding a payload out now earns exactly the credit for
       having ridden it out, which is what the note says. */
    chipEnduredNote: 'they rode it out',
    chipBlocked: 'blocked',
    chipTooSoon: 'too soon',
    dirIn: 'they sent',
    dirOut: 'you sent',

    /* --- the report card (spec §7.5). It appears ONLY when a duel partner's
       own media actually reached this machine, and only on the recap: filing a
       report mid-duel would be a way to interrupt one, and interruptions are
       exactly what an opponent would weaponise. --- */
    reportTitle: 'report what they sent',
    reportLead: 'these files came off their machine, not your library. if any of it should not exist, say so.',
    reportPick: 'pick the one you mean',
    reportFlagged: 'flagged during the match',
    reportPrivacy: "a moderator gets the file's fingerprint, a small thumbnail and your note. they never get your name, and the other player is never told.",

    /* --- the "so what WAS that" card. STANDALONE ONLY: it is written for the
       phone joiner who arrived from an invite link, endured a match and has
       never seen the app the payloads came out of. Hosted, the player is
       already inside it and the card would be selling them their own desk. --- */
    ctaTitle: 'what was throwing all that at you',
    ctaLead: 'the flashes, the subliminals, the lock cards — all of it comes out of the Conditioning Control Panel, a desktop app that runs the whole thing for you, opponent or no opponent.',
    ctaLink: 'See what it does',
    ctaFine: 'opens cclabs.app in a new tab.',
  },

  /** Locally computed cosmetics. Nothing here reaches the server. */
  titles: Object.freeze({
    graceful: { name: 'Graceful', why: 'went eight minutes, then bowed out on your own terms.' },
    ironEdge: { name: 'Iron Edge', why: 'the clock broke before you did.' },
    stoneWall: { name: 'Stone Wall', why: 'four of theirs, straight through, no flinching.' },
    untouchable: { name: 'Untouchable', why: 'nothing they threw ever reached you.' },
    gg: { name: 'GG', why: 'broke, but never looked away.' },
  }),

  /* ------------------------------------------------- the report card mechanics
   * The five REASON LABELS are written for a player who has just seen something
   * they wish they had not, not for a lawyer. The WIRE CODES they map to
   * (`csam`, `nonconsensual`, `gore`, `illegal`, `other` — proxy/goon-routes.js
   * REPORT_REASONS) are NOT translatable copy and live in ui/report.js; nothing
   * here is ever put on the wire.
   *
   * Lowercase like the rest of the furniture, and deliberately unsensational:
   * this control has to be usable by someone who is upset. */
  report: {
    reasonHead: 'what is wrong with it',
    reasons: [
      { code: 'csam', label: 'sexual content involving a minor' },
      { code: 'nonconsensual', label: 'someone who did not agree to be in it' },
      { code: 'gore', label: 'real violence, injury or a death' },
      { code: 'illegal', label: 'something else that is against the law' },
      { code: 'other', label: 'something else' },
    ],
    /** `other` is the one reason a moderator cannot act on without words. */
    noteHead: 'tell us what it is',
    noteHint: (max) => 'up to ' + max + ' characters.',
    notePlaceholder: 'in your own words…',
    noteNeeded: 'a line or two, so a moderator knows what they are looking at.',

    submit: 'send report',
    submitting: 'sending…',
    /** The id is a handle for a follow-up, not a receipt to celebrate. */
    done: (id) => 'report sent — id ' + (id || 'unknown'),
    deduped: 'already reported',
    failed: "couldn't send — try again",
    retry: 'try again',
    givenUp: "couldn't send. it did not go through — nothing was recorded.",
    cancel: 'never mind',

    thumbLabel: (kind, n) => (kind === 'video' ? 'clip ' : 'image ') + n,
    evidenceNote: 'a small thumbnail is made here and sent with the report, so a human can check without asking you for the file.',
  },

  /* -------------------------------------------------------------- options */
  options: {
    headline: 'options',
    master: 'Master',
    music: 'Music',
    /* The low binaural bed that runs under a match (ui/droneBed.js). Named for
       what you hear rather than for what it is: "binaural bed" is the
       engineering word, "Drone" is the one a player already has for it. */
    drone: 'Drone',
    /* The old single "SFX" slider, split in two. Named for WHO MAKES THE SOUND,
       because that is the line a player can hold in their head while dragging:
       "UI sounds" is the chrome under their own hands, "Game sounds" is
       everything the match does at them. Neither says "bus". */
    ui: 'UI sounds',
    game: 'Game sounds',
    /* The opponent's video windows. "Media" rather than "Videos" because the
       toggle two rows down is already called Skippable videos and two rows
       reading "videos" would be read as one setting split in half. */
    media: 'Media',
    mediaNote: 'the volume of the clips your opponent throws at you. a click still mutes one window on its own.',
    motion: 'Reduce motion',
    skippable: 'Skippable videos',
    /* Says what the toggle does AND what it does not take away: a window can
       always be muted with a click, on or off. */
    skippableNote: 'gives each floating video an ✕ to close it early. off, they run their course. either way a click mutes one, and a right-click hands it the sound.',
    shaderSpirals: 'Shader spirals',
    /* The escape hatch, and the note says what to reach for it FOR: a picture
       that stops moving while the game carries on is the exact symptom. Names
       the fallback too, so turning it off does not feel like losing the bed. */
    shaderSpiralsNote: 'draws the spiral bed live instead of stretching a gif. turn it off if the picture ever freezes while the sound and the clock keep going — the bed falls back to the bundled spirals and nothing else changes.',
    /* The device tier (exec/perfTier.js). The toggle shows the RESOLVED answer
       — phones start on, desks start off, via the 'auto' pref — and touching it
       writes an explicit choice, same one-way door as the arsenal handle. The
       note says what it costs, because "performance mode" toggles that hide
       their trade read as snake oil. */
    perfLite: 'Lite graphics',
    perfLiteNote: 'fewer flashes and bubbles on screen at once, a lighter spiral and no glass-blur — for phones and small machines that drop frames mid-match. on by itself on most phones; nothing about the match itself changes.',
    /* THE VIEWER'S OWN SWITCH, and it is not the same decision as the sharing
     * toggles in the lobby: those say what leaves this machine, this says what
     * arrives. Off, their picture is never even FETCHED (ui/discord.js), which
     * is the difference between hiding a face and not asking for one. */
    oppAvatars: 'Show opponent avatars',
    oppAvatarsNote: "off, you see a coloured initial instead of your opponent's discord picture — and their picture is never downloaded at all. it changes nothing about what you share.",
    /* The seventh slider. "Voice notes" in full, never just "Voice": the page
       has no other voice in it, and a lone "Voice" reads as narration or TTS.
       Says whose voice it is in the note, because that is the whole surprise. */
    voice: 'Voice notes',
    fullscreen: 'Fullscreen',
    reset: 'Reset',
    close: 'close',
    lockedNote: 'Match settings are locked once you start.',
  },

  /* ---------------------------------------------------------------- voice notes
   * THE COPY DECK FOR A FEATURE THAT RECORDS SOMEBODY'S ACTUAL VOICE. Written in
   * wave 1 and complete on purpose: waves 2 and 3 are READ-ONLY on this file, so
   * every line the mic HUD, the screen, the lobby and the toasts need is already
   * here. Adding one later means editing a file two other agents are holding.
   *
   * Three rules that are safety copy rather than style, and they are the reason
   * this block is longer than it looks like it needs to be:
   *
   *   1. EVERY LINE THAT MENTIONS SENDING ALSO MENTIONS HEARING. Turning this on
   *      is two consents in one switch — your voice goes to them, and theirs can
   *      come back — and a player who only reads the button must not be able to
   *      miss the second half. That is why the ack gate exists at all.
   *   2. NOTHING HERE PROMISES DELETION OR PRIVACY IT CANNOT KEEP — and until the
   *      2026-08-05 privacy pass this block broke its own rule. It said "no server
   *      ever hears them" and "there is no server in the middle", which is FALSE on
   *      the fallback path and was false the day it was written: a voice note rides
   *      the CONTROL lane precisely so it survives a relayed link (see the header of
   *      ui/voice/voiceService.js and core/contracts.js makeVoice), and on that link
   *      every frame is plain JSON through our /v2/goon/relay mailbox. There is no
   *      application-layer encryption on that hop — TLS to us, readable by us.
   *      So the copy now says the true thing in two clauses: direct link, straight
   *      to them and we never see it; relayed link, it passes through our server.
   *      Still no "disappears after" and no "only you can hear it": what happens on
   *      their machine is theirs, and the ack line says so.
   *   3. THE OFF PATH IS NEVER SCOLDED. Cancelling a recording, refusing the mic
   *      and turning the whole thing off are all ordinary things to do, phrased
   *      as ordinary things.
   *
   * Lowercase furniture, sentence case for the sheet — the house register. */
  voice: {
    /* --- the title menu ------------------------------------------------- */
    /** The menu item. Verb-first like "Host a match" — it is somewhere you go to make something. */
    menu: 'Send voice notes',
    menuNote: 'record a few, use them mid-duel',

    /* --- the screen ----------------------------------------------------- */
    eyebrow: 'voice notes',
    lead: 'ten seconds each, sent to whoever you are duelling. on a direct link it goes straight to them, encrypted, and we never see it — on a relayed one it passes through our server to reach them.',
    back: 'back',

    /* --- the acknowledgment gate (shown ONCE, before the toggle will move)
       Sheet register: sentence case, full sentences, and the second paragraph is
       the one that is easy to forget — this switch also lets THEM be heard. */
    ack: {
      icon: '🎙',
      headline: 'This records your real voice',
      line: 'Holding the mic records your real voice and sends that recording to the person you are duelling. On a direct connection it travels straight to their machine, encrypted, and our servers never see it. If your two networks can only reach each other through our relay, the note passes through our server on the way to them.',
      lineTwo: 'Switching this on also means you may HEAR them: if you have both turned it on, their voice plays on your side too. Once a note has reached them it is on their machine, and what happens to it there is theirs — it cannot be taken back.',
      go: 'I understand',
      cancel: 'Not now',
    },

    /* --- the opt-in toggle ---------------------------------------------- */
    toggle: 'Voice notes',
    /** Shown under the toggle when it is ON. Says both directions, every time. */
    toggleOn: 'on — they can hear you, and you can hear them, when you have both switched it on.',
    /** ...and when it is OFF. "dropped without being played" is the literal truth. */
    toggleOff: 'off — nothing is recorded, and anything they send is dropped without being played.',
    /** The toggle is dead until the modal has been read. */
    toggleLocked: 'read the note above first.',

    /* --- recording, in the library screen -------------------------------- */
    record: 'Record a note',
    recording: 'recording…',
    recordStop: 'Stop',
    /** The live counter while recording. Seconds, one decimal, with the ceiling. */
    recordTimer: (ms, maxMs) => (Math.max(0, ms) / 1000).toFixed(1) + 's / ' + Math.round((maxMs || 10000) / 1000) + 's',
    recordCapped: 'ten seconds is the lot.',

    /* --- the note list --------------------------------------------------- */
    /** Auto-name. Numbered rather than timestamped: a player picks by position. */
    noteName: (n) => 'Note ' + n,
    /** The duration chip on a row. */
    noteLength: (ms) => (Math.max(0, ms) / 1000).toFixed(1) + 's',
    play: 'play',
    stop: 'stop',
    delete: 'delete',
    /** The list before anything is in it. Says what to press, not just that it is empty. */
    empty: 'no notes yet — record one above.',
    /** Storage ceiling (8). Phrased as a fact, not a refusal. */
    full: (max) => 'that is all ' + max + ' — delete one to record another.',
    /** Confirm before a note goes. Cheap to re-record, so no scary sheet. */
    deleteConfirm: 'delete this one?',

    /* --- the emote association ------------------------------------------- */
    /** The picker's label on a row. */
    linkLabel: 'send with an emote',
    /** The "no emote" option, and what the row says when one is chosen. */
    linkNone: 'on its own',
    linkedTo: (emote) => 'goes out with ' + (emote || 'that emote'),
    /** One note per emote: picking an emote that is taken moves it. */
    linkMoved: (emote) => (emote || 'that emote') + ' had another note — it has this one now.',
    linkHelp: 'firing that emote in a match sends this note with it. the emote never waits for it.',

    /* --- the mic HUD (ui/voice/micHud.js) --------------------------------
       Held-button copy. Short enough to read at a glance while holding a button
       down, because that is the only time any of it is on screen. */
    hudLabel: 'voice note',
    /** A tap that was too short to be a recording. A hint, never an error. */
    holdHint: 'hold the mic to record',
    /** THE DESK'S KEY. A desktop seat fires its whole arsenal off the number row
       (ui/arsenal.js binds 1..7 on `window` precisely so the drawer can stay
       shut), and the mic is the one control in that drawer with no key of its
       own — which is how an in-app player ends up with no way to record at all.
       ui/hud.js owns the binding; ui/voice/micHud.js still has no key handler,
       so the Escape-is-Mercy ladder is untouched. Said on the button's tooltip,
       and shown as the hint when the key is TAPPED rather than held. */
    holdKeyHint: 'hold V to record',
    /** While held. The gesture out is a slide, and it has to be said, not guessed. */
    slideToCancel: 'slide left to cancel',
    /** The last three seconds (micHud.MIC_COUNTDOWN_MS). The question stops being
       "how much have I said" and becomes "how long have I got", so the number
       does too — a countdown, not a fraction to subtract mid-sentence. */
    recordCountdown: (sec) => Math.max(0, sec) + 's left',
    /** The three outcomes, in the order a player meets them. */
    sending: 'sending…',
    sent: 'sent',
    cancelled: 'cancelled',
    /** The recorder came back with nothing usable (a mic that produced silence). */
    sendFailed: 'that one did not record — try again',
    /**
     * THE MIC NEVER OPENED, which is a different sentence from the one above and
     * used to borrow it. `sendFailed` blames the recording ("that one did not
     * record"); this is for the case where there was no recording to blame —
     * getUserMedia timed out, MediaRecorder would not construct, the recorder
     * fell over mid-note. Both end in "try again" because both are recoverable,
     * and after 2026-08-05 both actually are.
     */
    micFailed: 'the mic did not open — try again',
    /** The 4 s floor between sends, phrased as a wait rather than a refusal. */
    tooSoon: (sec) => 'one more in ' + sec + 's',

    /* --- the incoming indicator ------------------------------------------ */
    /** The chip by their bezel while a note plays. Lowercase furniture. */
    incoming: 'they said something',
    /** ...and the emote-attached variant, anchored on the bubble instead. */
    incomingWithEmote: 'they said something',

    /* --- the lobby ROW (2026-08-05) ---------------------------------------
     * This used to be a read-only sentence, and the ONLY switch lived on a
     * title-menu screen a player has no reason to visit before a duel. The
     * owner then failed to find the mic three times running with the feature
     * working exactly as written — because their own side had never been
     * switched on, and nothing on the road into a match ever offered to. The
     * toggle is here too now, behind the SAME acknowledgment gate (rule 1: the
     * sentence that mentions HEARING them is never skippable), and the old
     * status sentence stays as the sub-line under it. */
    lobbyBoth: 'voice notes on for this match',
    lobbyYours: 'you have voice notes on — they have not',
    lobbyTheirs: 'they have voice notes on — you have not',
    lobbyPeerOld: 'their app is too old for voice notes',
    /** The row's own explanation, when there is no status to report yet. */
    lobbySub: 'hold the mic under your items to send ten seconds of your voice. you both have to switch it on.',
    /** Before the acknowledgment has been read. Says what to press, not "no". */
    lobbyLocked: 'tap this row to read what it does first.',
    /** The right-hand chip, exactly like the transfer row's pair. */
    lobbyTheirsOn: 'they opted in',
    lobbyTheirsOff: 'they have not',

    /* --- the refusals ----------------------------------------------------- */
    /** getUserMedia said no. NOT an error tone: it is a perfectly good answer. */
    micDenied: 'no microphone access — the mic stays off',
    /** No input device at all, or a host that cannot reach one. */
    micMissing: 'no microphone found',
    /** The one line that explains a hidden mic button mid-match. */
    notActive: 'voice notes need both of you switched on',
    /** Where the volume lives, from the screen that made the note. */
    volumeHint: 'their notes play at the Voice notes volume in options.',
  },

  /* --------------------------------------------------- the assets screen
   * The compression queue lives in the app, not in this page, and every line
   * below has to keep saying so: the player is agreeing to spend minutes of
   * their machine's time, and the only honest way to ask is with the real
   * number and the real encoder. Lowercase throughout, like the rest of the
   * quiet furniture.
   *
   * "compressed" here NEVER means "your file was changed". A copy is made and
   * kept beside the original; that copy is the only thing that can travel. The
   * protected note says it out loud because it is the one fear this screen has
   * to answer before anything else on it matters. */
  assets: {
    eyebrow: 'your media',
    lead: 'a compressed copy is what travels to your opponent. your originals are never touched, moved or replaced.',
    /** No host = no queue. Rendered instead of the grid, immediately. */
    standaloneHeadline: 'compression lives in the app',
    standaloneLine: 'this page is running in a plain browser, so there is no library to compress. open the goon game from the app to use this screen.',

    /** Standalone's OWN library: files picked straight off the device. */
    local: {
      headline: 'add media to send',
      line: 'pick files from this device and copies of them can be sent to your opponent mid-duel, encrypted, straight from you to them. nothing is uploaded to our servers — but what reaches them is theirs to keep.',
      add: 'add files',
      limits: (max, vmax) => 'jpg, png, gif, webp, mp4, webm, mov · or a zip of them · up to ' + max
        + ' travels as-is, bigger photos and gifs are compressed to fit, clips up to ' + vmax,
      empty: 'nothing added yet.',
      note: 'your picks last until this page closes — add them again next visit.',
      remove: 'remove',
      skipDupe: (n) => n + ' already added',
      skipBig: (n, max) => n + ' over ' + max,
      skipType: (n) => n + ' of a format we can\'t carry (jpg, png, gif, webp, mp4, webm and mov travel)',
      /**
       * The container was welcome but THIS device has no decoder for what is
       * inside — iPhone HEVC on an older browser, mostly. Distinct from
       * skipType on purpose: "wrong format" tells the player to convert,
       * "can't decode" tells them nothing they did is wrong.
       */
      skipCodec: (n) => n + (n === 1 ? ' clip' : ' clips') + ' this device can\'t decode — a re-save as mp4 usually fixes it',
      skipFailed: (n) => n + ' unreadable',
      added: (n) => n + ' added',
      /**
       * Adding is no longer instant — a zip of two hundred photos is two hundred
       * decodes and two hundred encodes. This line is the whole difference
       * between "working" and "the button is broken"; it lives on a role=status.
       */
      adding: (done, total) => 'adding ' + done + ' / ' + total + '…',
      addingOne: (name) => 'adding ' + name + '…',
      /** Mid-compression, when the encoder is actually reporting a percentage. */
      compressing: (name, pct) => 'compressing ' + name + '… ' + pct + '%',
      /** The good news in the summary: these were too big, and now they are not. */
      compressed: (n) => n + ' compressed to fit',
      /**
       * A clip past the wire's artifact cap. Its OWN sentence, carrying the
       * VIDEO number: a browser cannot transcode video, so this is the one
       * refusal the player can act on (trim it, or send a gif instead).
       */
      skipBigVideo: (n, max) => n + (n === 1 ? ' video' : ' videos') + ' too big to send (' + max + ' max)',
      /**
       * The ceilings trimmed a REAL library. Never "unreadable", never a
       * per-file size: what happened is that we took the first N of something
       * legitimate, and the player is owed exactly that sentence.
       */
      trimmed: (n, max) => n + ' more left out — one zip adds its first ' + max + ' files',
      /** A compressed row: what it weighed, and what actually travels. */
      sizeShrunk: (from, to) => from + ' → ' + to,
      /** A zip opened fine and held nothing we can send — say so, never stay silent. */
      zipNone: 'no media in that zip',
      /**
       * The ARCHIVE itself could not be opened (corrupt, truncated, a format we
       * cannot walk). Its own line on purpose: the size of a zip is no longer a
       * reason to refuse one, and reporting an archive failure with the
       * per-file cap text is what told a player with a 1 GB library that it was
       * "1 over 8 MB".
       */
      zipBad: (n) => (n === 1 ? "couldn't open that zip" : n + " zips couldn't be opened"),
    },

    statReady: (n) => n + ' ready',
    statNeeds: (n) => n + ' to compress',
    statFailed: (n) => n + ' failed',
    statExempt: (n) => n + ' small enough already',
    statUsage: (text) => text + ' cached',

    compressAll: (n) => 'compress everything (' + n + ')',
    compressAllIdle: 'everything is compressed',
    pause: 'pause',
    resume: 'resume',
    deleteCompressed: 'delete compressed…',
    filterAll: 'all',
    filterNeeds: 'needs work',
    filterReady: 'ready',
    filterFailed: 'failed',
    filterExempt: 'as-is',
    searchLabel: 'filter by name',
    searchPlaceholder: 'name contains…',

    minutes: (n) => n + (n === 1 ? ' minute' : ' minutes'),
    eta: (mins, encoder) => '~' + mins + ' left · ' + encoder,
    etaEstimating: 'estimating…',
    etaPausedMatch: 'paused for the match',
    etaPausedUser: 'paused — nothing is running',
    encoderHw: 'hardware encoder',
    encoderSw: 'software encoder',

    badgeNotReady: 'not ready',
    badgeQueued: 'queued',
    badgeWorking: (pct) => pct + '%',
    badgeReady: 'ready',
    badgeFailed: (why) => (why ? 'failed · ' + why : 'failed'),
    badgeExempt: 'small — sends as-is',

    tileCompress: 'compress',
    tileDelete: 'delete copy',
    tileRetry: 'try again',
    tileCancel: 'cancel',
    playPreview: 'play preview',

    loading: 'reading your library…',
    empty: 'no media in your active preset yet.',
    emptyFiltered: 'nothing matches that.',
    more: (n) => 'show more (' + n + ')',

    capLabel: 'cache limit',
    capValue: (gb) => gb + ' GB',
    overCap: 'over the limit — the least-used copies get dropped first.',
    presetChanged: (n) => 'your preset changed · ' + n + ' need compressing',
    protectedNote: 'copies live beside your library, never inside it. deleting them here frees space and costs nothing but the time to make them again — and anything an opponent sent you is stored separately and is never touched by this button.',
    back: 'back',

    confirmCompress: {
      icon: '⏳',
      headline: 'this will take a while',
      line: (mins, size, encoder) => 'about ' + mins + ' on your ' + encoder + ', working through ' + size + '. it pauses itself the moment a match starts, and you can stop it whenever you like.',
      go: 'start compressing',
      cancel: 'not now',
    },
    confirmDelete: {
      icon: '✖',
      headline: 'delete every compressed copy?',
      line: (size) => 'frees ' + size + '. your originals are untouched. anything an opponent sent you is stored separately and stays.',
      go: 'delete them',
      cancel: 'keep them',
    },

    /** The title-screen ribbon: "412 ready · 3.1 GB cached". */
    ribbon: (ready, size) => ready + ' ready · ' + size + ' cached',
  },

  /* ------------------------------------------------- the media-setup step
   * FIRST-RUN ONBOARDING FOR LINK JOINERS (ui/screens/mediaSetup.js). Somebody
   * tapped a duel link, has never opened this thing before, and is about to be
   * dropped into a lobby with an empty deck — where every effect fires against
   * a blank screen and the match looks broken rather than empty.
   *
   * Two rules the copy has to keep:
   *   1. the twenty-item suggestion is a SUGGESTION and must read as one. The
   *      button unlocks on the first file; nothing here may sound like a gate.
   *   2. it says where the media GOES. A stranger asking a first-time visitor
   *      for their porn folder has about one sentence to be honest in, and until
   *      the 2026-08-05 privacy pass that sentence spent it on the wrong half
   *      ("travel nowhere") — true only for as long as the lobby toggle stays
   *      off, and read by everybody as a promise about the whole feature. It now
   *      names the toggle AND what crossing it means. */
  mediaSetup: {
    eyebrow: 'before you play',
    headline: 'bring something to endure',
    /** The whole premise, in one line: the game plays YOUR library at you. */
    lead: 'a duel runs on your own media — every flash, clip and whisper you get is yours. your deck is empty, so it would be a very quiet match.',
    tips: [
      'twenty-odd images or gifs is where it starts to feel like a duel. more is better, and more is easy.',
      'throw in a few short clips too — those are the ones that take the whole screen.',
      'a .zip of the lot works: drop the archive in and it unpacks itself.',
    ],
    /** Named because it is the answer to "…but I do not have a folder ready". */
    discordLine: 'no stash yet? plenty of girls share their packs on the discord.',
    discordCta: 'open the discord',
    add: 'add media',
    /**
     * The button once SOMETHING is in: the phone's photo sheet is one-shot per
     * visit, so "add more" is the whole answer to "…but that was only one
     * album" (2026-08-04 play-test — the first small batch read as the end of
     * the road and the primary button below whisked people into the room).
     */
    addMore: 'add more · another album, a zip…',
    /** The running tally. Reassuring at 3, congratulatory at 20 — see `enough`. */
    count: (n) => n + (n === 1 ? ' item added' : ' items added'),
    countNone: 'nothing added yet.',
    /** Under the tally while they are still short of the suggestion. */
    suggest: (want) => 'aim for about ' + want + '. you can always add more later.',
    /** …and once they clear it. Praise, not a permission slip. */
    enough: 'that will do nicely.',
    remove: 'remove',
    lock: "I'm set",
    /** The commitment, wearing its own number — nobody locks in "3 picks" thinking they added an album. */
    lockN: (n) => "I'm set — lock in " + n + (n === 1 ? ' pick' : ' picks'),
    lockNeed: 'add at least one thing',
    lockBusy: 'still adding…',
    /** The opponent is watching a "picking their media" line while this is up. */
    waiting: 'they know you are still picking. take your time.',
    note: 'your picks stay on this device unless you switch sending on in the lobby — then copies of a few of them are sent to your opponent, encrypted and never through our servers, and whatever reaches them is theirs to keep.',
    leave: 'Leave',
  },

  /* --------------------------------------------------------------- sheets */
  sheets: {
    /* THE HOST GATE. Not a fault and not a wall: joining is free forever, so the sentence has to
       leave the player somewhere to go rather than only closing a door. It also answers the
       retired 402 `no_pass` — an old server's "your free match is spent" is the same conversation
       with worse information, and this is the only copy left for either. */
    noHostAccess: {
      icon: '✦',
      headline: 'Hosting is a supporter perk',
      line: "Opening a room is for Tier 2 supporters. Joining someone else's is always free — ask them for a code.",
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
    // A seat problem is never a connection problem, and the fall-through sheet
    // ("could not reach the server") would tell the player to check their wifi
    // about a room that answered perfectly.
    seatTaken: {
      icon: '👥',
      headline: 'That room is taken',
      line: 'Someone else is already in there. Ask for a fresh code.',
    },
    selfJoin: {
      icon: '🪞',
      headline: 'That is your own room',
      line: 'You are the host of that code. The other player has to be on a different account.',
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

  /* ------------------------------------------------------ the desk's chrome
   * The zen toggle (ui/hud.js): ONE button that clears the desk down to the
   * opponent monitor and the arsenal — the score, its multiplier, the
   * closeness dial and the mercy button all step off together.
   *
   * A glyph, because it sits in the top-right corner beside the gear where a
   * word would not fit, so the label travels in aria-label + title instead and
   * the pressed state travels in aria-pressed. The "escape still ends the
   * match" half is not decoration: while the panels are away it is the only
   * place that says so. */
  hud: {
    zenHide: 'hide panels',
    zenShow: 'show panels',
    /** ⊟ collapse / ⊞ expand — a box that loses and regains its contents. */
    zenHideGlyph: '⊟',
    zenShowGlyph: '⊞',
    zenShowTitle: 'show panels · escape still ends the match',
    /* THERE IS NO `scoreMult` ANY MORE (2026-08-05). The top bar's third line
     * read "×1.30 score" (and "×1.30 risk" before that) off the engine's
     * riskMultiplier. It was the last player-facing piece of the risk system,
     * and the owner's verdict on the whole family holds for it too: a number
     * fixed at the draft, unchangeable mid-match and unexplained by anything on
     * the desk. Deleted rather than reworded — the score above it is the same
     * information, already counted. */
    /* THERE IS NO `charges` STRING ANY MORE (2026-08-05). `charges: (n, cap) =>
     * n + " / " + cap + " charges"` lived here for a matter of hours: it was
     * what replaced the five-diamond pip row in the morning's pass, and by the
     * evening the owner had removed the charge REQUIREMENT it described ("we
     * still have the charge system in (you need 3 to do X etc), we should remove
     * the requirement entirely"). A count of a currency that buys nothing is not
     * a readout, it is a rumour of a rule — and this one actively misinforms,
     * because "1 / 3" beside an item you CAN throw says you cannot.
     *
     * Nothing in ui/hud.js builds the line. Do not restore this key without a
     * mechanic behind it; the engine's meter (core/scoring.js) is still there
     * and still on the wire, and that is deliberately not the same thing. */
    /* ---- the heat gauge (ui/drops.js) ----
     * ONE word, because it sits over the arsenal rail and the bar is the rest of
     * the sentence. `heatValue` is the aria-valuetext: colour never travels
     * alone here, and "78% hot" is what a screen reader gets instead of a fill. */
    heatLabel: 'heat',
    heatValue: (pct) => Math.round(pct) + '% — pops fill it, drops spend it',
  },

  /* ------------------------------------------------- the opponent monitor */
  monitor: {
    idle: 'quiet',
    dropHint: 'drop it here',
    /** The green checkmark on their projection: they took the whole payload. */
    passed: 'they held it',
    /* THEIR CHARGE COUNT IS GONE TOO (2026-08-05) — `charges: (n) => n + " charge(s)"`,
     * and five `.gg-pip--sm` diamonds before that. It followed S.hud.charges out
     * the door and for the sharper reason: yours was at least a fact about you,
     * while theirs was only ever a THREAT FORECAST — "they can afford the heavy"
     * — and with the requirement removed there is no forecast to make. They
     * throw when their cooldown is up, exactly like you do. ui/opponent.js draws
     * no such span; the titlebar is grip · dot · name · score. */
    /** The titlebar's tooltip. All three gestures, because not one of them is
     *  discoverable and the grip dots only ever promise the first. */
    dragHint: 'drag to move · wheel to resize · double-tap to put it back',
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
    /* ---- the collapsible sidebar (ui/hud.js) ----
     * The handle NEVER hides, so it has to say what it does in BOTH states and
     * say it to a screen reader too. `tabCount` is the compact "you still have
     * things" badge on a collapsed drawer — the only readout left once the
     * stickers are gone — and it is a number with a labelled name beside it,
     * never a bare dot. */
    sidebarShow: 'show items',
    sidebarHide: 'hide items',
    /** ▸ shut (pull me open) · ◂ open (push me shut). */
    sidebarShowGlyph: '▸',
    sidebarHideGlyph: '◂',
    sidebarTitle: 'items — keys 1-7 still fire while this is shut',
    tabCount: (n) => String(n | 0),
    tabCountLabel: (n) => (n | 0) + ((n | 0) === 1 ? ' item ready' : ' items ready'),
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
    linkCopied: 'invite link copied',
    copyFailed: 'could not copy — select the code instead',
    peerWobbly: 'their connection is wobbling',
    peerBack: 'they are back',
    /* `charge: '+1 charge'` was here with no caller at all — the HUD played its
       own inline "+1" chip instead. Removed 2026-08-05 with the rest of the
       charge surface rather than left as a toast for a thing that no longer
       means anything. */
    left: 'left the match',
  },
});

/**
 * The draftable elements, in the order the draft grid renders them.
 *
 * THERE IS NO `risk` FIELD ANY MORE (2026-08-04). This table used to carry a
 * hand-copied duplicate of core/draft.js's 0-3 riskTier, which the draft tile
 * painted as three pips and selftest-hud cross-checked so the copy could not
 * drift. The tier still exists inside the engine — it is what core/scoring.js
 * multiplies the per-second score by, and it is C#-parity — but it is no longer
 * shown to anyone, so the duplicate has no reason to exist. Do not add it back:
 * a second copy of an engine number is a drift bug waiting for a quiet week.
 * The BLURB is now the whole story a tile tells, so blurbs earn their keep.
 */
export const ELEMENTS = Object.freeze([
  { id: GoonElement.Flashes, name: 'flashes', blurb: 'constant. builds all match.' },
  { id: GoonElement.BouncingText, name: 'bouncing text', blurb: 'always on your screen. slow burn.' },
  { id: GoonElement.Subliminals, name: 'subliminals', blurb: 'quiet, and it climbs to the top.' },
  // Always on for both players, start to finish — not a toggle. The draft grid renders it as a
  // locked tile (core/draft.js ALWAYS_ON_ELEMENT); the blurb has to say so.
  { id: GoonElement.Bubbles, name: 'bubbles', blurb: 'the whole match, both of you. thin at first, then not.' },
  { id: GoonElement.Videos, name: 'videos', blurb: 'long takeovers. 1–2 minutes.' },
  { id: GoonElement.LockCards, name: 'lock cards', blurb: "type it out. can't look away." },
  { id: GoonElement.ToyPatterns, name: 'toy patterns', blurb: 'bursts, capped by your own limit.' },
  { id: GoonElement.Spiral, name: 'spiral', blurb: "a slow spiral takes the whole screen. it doesn't blink. neither will they." },
  { id: GoonElement.BrainDrain, name: 'brain drain', blurb: "late, heavy, and it doesn't stop." },
]);


export default S;
