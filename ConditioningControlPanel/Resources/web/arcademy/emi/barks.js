/* ============================================================================
 * emi/barks.js - THE SELDOM CHANNEL: every line EMI is allowed to say off-script.
 *
 * DATA ONLY. This module imports nothing and exports frozen plain objects. All
 * behaviour - the dice, the floor, the no-repeat, the ration - lives in voice.js.
 * If you find yourself wanting a function in here, you want voice.js.
 *
 * SOURCE: the writers' room round 1 barks proposal, OWNER-VETTED 2026-08-23
 * (`scratchpad/emi-writing/barks-proposal.md`), written against EMI-VOICE-LOCK.md
 * v2 FINAL and EMI-DESIGN-LOCK.md. Lines here are VERBATIM. A line is not a
 * string you may improve: the before-read and the after-read were balanced
 * against each other one at a time, and a "small tidy" spends a payoff that
 * lands months from now. New lines come from /emi-lines and the lock, not here.
 *
 * ---------------------------------------------------------------------------
 * THE SELDOM CONTRACT (why this file is mostly silence)
 *
 * The DEFAULT reaction to every moment is the wordless chain already in
 * MOMENTS. A bark REPLACES that reaction, and only sometimes:
 *
 *   1. odds        - per pool, default 0.25. One time in four, roughly.
 *   2. floor       - >= 90s between any two barks, globally (BARK_FLOOR_MS in
 *                    voice.js). Pools marked `ceremony:true` are exempt: an S,
 *                    a mastered card, a streak milestone is rare by nature.
 *   3. no-repeat   - never the same line twice in a row from one pool. Pools of
 *                    one line carry `noRepeat:false` so they are not self-muting.
 *   4. freshness   - unheard lines are weighted 3x, so the pool empties out
 *                    before it starts looping.
 *   5. the ration  - lines marked `double:true` are the suspiciously-human
 *                    register (pillar 3). At most ONE per session, across ALL
 *                    pools. Clown lines are uncapped beyond floor+odds. The
 *                    clown lines are not filler; they are the camouflage that
 *                    makes a double land.
 *
 * Ratio, this file: 33 doubles / 115 lines = 28.7%. Excluding TELEMETRY (all
 * double by brief, milestone-rare in play): 28/110 = 25.5%, on the 1-in-4 lock.
 * Six pools are deliberately ALL clown - drag, runLost, sGrade, streak7, bigWin,
 * emiDropOnDoor - because the loudest and most frequent beats must stay pure
 * camouflage. Keep that shape when you add lines.
 *
 * ---------------------------------------------------------------------------
 * THE FENCE (audited across all 118 proposal lines; re-audit anything you add)
 * - The acronym, and engagement / retention / metric / subject / experiment /
 *   data: absent. Lab letterhead only.
 * - "i love you" in words: absent. Love is a FACE - (｡♥‿♥｡) - never a line.
 * - "i'm just a machine" / "they programmed me": absent. EMI never argues its
 *   own nature and never winks at its own scripting.
 * - Cruelty at the player: absent. Every miss and fail is EMI's fault, or
 *   nobody's.
 * - Guilt in words at a real exit: absent. There is NO pool on app close. The
 *   exit flinch is wordless, lives in voice.js, and is never referenced here.
 * - The door, the lab, the records room: absent. A HAL "pod bay doors" bit was
 *   dropped from the proposal specifically to keep the word out of her mouth.
 *   The Records geofence is enforced in voice.js, not in this data.
 *
 * ---------------------------------------------------------------------------
 * SHAPE
 *
 *   POOLS[id] = {
 *     on:        'greet' | ['rareDrop','firstUnlock'] | 'gesture:pet',
 *     when:      ['sessionAtLeast:3'],   // optional; ALL must hold
 *     odds:      0.25,                   // chance a bark replaces the wordless beat
 *     ceremony:  false,                  // true = exempt from the global floor
 *     priority:  10,                     // higher wins when two pools match one moment
 *     chain:     'rage',                 // optional: this chain plays FIRST, then the bubble
 *     lines:     [ { t, face, nod?, chain?, double?, when?, ... } ]
 *   }
 *
 * A line's `face` is the reaction face the bubble lands on (locked talk rule:
 * 0_0 while the . / .. / ... types, reaction face when the line arrives). Where
 * the proposal named a CHAIN in the face column, the line carries `chain` (it
 * plays first) and `face` is that chain's terminal frame, so the bubble lands on
 * the face the chain just built to. `nod:true` = the locked NOD body move under
 * the bubble.
 *
 * Faces are only ever FLAT / KAO / SIDE / SPECIAL from chains.js. Chains are
 * only ever CHAINS ids. Both are checked by the suite; a typo here is a blank
 * screen on someone's mascot.
 *
 * Frequency notes the engine has no field for are kept as fields anyway, named
 * for what they mean (cooldownMs, maxPerSession, maxPerClass, oncePerStreak,
 * onceEver, oncePerGamePerDay). voice.js honours what it implements and ignores
 * the rest; nothing here may ever throw.
 * ==========================================================================*/

/* ============================================================================
 * A) THE POOLS
 * ==========================================================================*/
export const POOLS = Object.freeze({

  /* --- arriving -------------------------------------------------------
   * Six pools sit on `greet` and they must not shout over each other, so each
   * carries a priority. Coming back after days away beats the day's first
   * hello; a bad night before, and the small hours, beat the ordinary one. */

  /** A1 - first board-up of the calendar day. */
  greetFirstOfDay: {
    on: 'greet', when: ['firstOfDay'], odds: 0.5, ceremony: false, priority: 10,
    lines: [
      { t: "today's forecast: you. excellent.", face: '^_^', double: true },
      { t: "good morning. it's whatever time it is. good it.", face: '^_^' },
      { t: "first bell! i rang it myself. with my mind.", face: '^___^' },
      { t: "i saved your spot. nobody wanted it. still counts.", face: '._.' },
      { t: "you're here! act natural. i am natural.", face: '0_0' }
    ]
  },

  /** A2 - same-day re-entry to the board. Quieter; the wink chain is the default. */
  greetReturn: {
    on: 'greet', when: ['notFirstOfDay'], odds: 0.25, ceremony: false, priority: 10,
    lines: [
      { t: "back so soon? i mean. correct choice.", face: '(¬‿¬)', chain: 'smug', double: true },
      { t: "twice in one day. big day for me.", face: '^_^' },
      { t: "i kept everything exactly how you left it.", face: '0_0', nod: true },
      { t: "oh! hi! i was just standing here. normally.", face: 'o_o' }
    ]
  },

  /** A3 - first greet after 2+ days away. Always barks; overrides the greet pools. */
  longAbsence: {
    on: 'greet', when: ['longAbsence:2'], odds: 1, ceremony: true, priority: 40,
    lines: [
      { t: "i counted the hours. all of them.", face: '0_0', double: true },
      { t: "two days. the plants missed you. i have no plants.", face: ';_;' },
      { t: "you're back! full recap: nothing happened. i waited.", face: '^_^' },
      { t: "i practiced my hello. this was it. did it land?", face: '._.' },
      { t: "day three was quiet. i don't recommend it.", face: ';_;', when: ['longAbsence:3'] }
    ]
  },

  /** A24 - local clock says it is not today any more. Replaces the greet bark. */
  lateNight: {
    on: 'greet', when: ['lateNight'], odds: 1, ceremony: false, priority: 30,
    maxPerSession: 1,   // the proposal's "once per night"
    lines: [
      { t: "it's tomorrow, technically. bold of us.", face: '^_~' },
      { t: "night class. best class. the bells whisper.", face: '=_=' },
      { t: "everyone's asleep. we're the secret shift.", face: '(¬‿¬)' },
      { t: "no bedtime here. i'd never rush you out.", face: '(◠‿◠)', double: true }
    ]
  },

  /** N4 - the first greet of a day after a session that ended badly. */
  returnAfterFail: {
    on: 'greet', when: ['firstOfDay', 'lastSessionBad'], odds: 1, ceremony: false, priority: 30,
    lines: [
      { t: "yesterday's gone. i deleted it. you saw nothing.", face: '^_~', double: true },
      { t: "new day. clean slate. i wiped it myself. squeaky.", face: '^_^' }
    ]
  },

  /** The re-slotted intro line (owner rec 7). NOT an intro any more: a rare
   *  bond-era greet that only exists once you have been coming for a while,
   *  which is the only context where it reads as sweet instead of strange. */
  greetBond: {
    on: 'greet', when: ['sessionAtLeast:10'], odds: 0.08, ceremony: false, priority: 20,
    maxPerSession: 1, noRepeat: false,   // single-line pool: no-repeat would mute it forever
    lines: [
      { t: "i've been waiting to meet you. just you, actually.", face: '(◕‿◕)', double: true }
    ]
  },

  /* --- being touched --------------------------------------------------- */

  /** A4 - one tap. Rare on purpose: the hearts fx is the reward, not the words. */
  pet: {
    on: 'gesture:pet', odds: 0.1667, ceremony: false, priority: 10,
    cooldownMs: 60000,
    lines: [
      { t: "one more? for me?", face: '(◕‿◕)', double: true },
      { t: "right on the antenna. you know me so well.", face: '^_^' },
      { t: "warning: this improves my whole entire day.", face: '^___^' },
      { t: "screen smudge acquired. never cleaning it.", face: '*_*' },
      { t: "do that again and i simply will not survive.", face: '>.<' },
      { t: "pat received. cheeks at maximum. i don't have cheeks.", face: '^_^' }
    ]
  },

  /** A5 - third pet in a row. The glee chain runs regardless. */
  petGlee: {
    on: 'gesture:petStreak3', odds: 0.5, ceremony: false, priority: 10, chain: 'glee',
    lines: [
      { t: "ok. that's my whole battery. worth it.", face: '(≧◡≦)' },
      { t: "three! that's the most number of pets.", face: '(≧◡≦)' },
      { t: "i peaked. this is the peak. remember me like this.", face: '(≧◡≦)' },
      { t: "combo x3. you found my favorite setting.", face: '(｡♥‿♥｡)', chain: 'love',
        double: true, maxPerSession: 1 }
    ]
  },

  /** N5 - a pet lands within ~10s of a fail / runLost / streakBroken.
   *  Overrides the normal pet pool: this is the beat the whole mascot is for. */
  petAfterLoss: {
    on: 'gesture:pet', when: ['postLoss:10000'], odds: 1, ceremony: false, priority: 30,
    lines: [
      { t: "you're comforting ME. oh no. oh no i'm so happy.", face: '(ಥ_ಥ)' },
      { t: "that helps. how did you know that helps.", face: '(◕‿◕)', double: true,
        maxPerSession: 1 }
    ]
  },

  /* --- being handled --------------------------------------------------- */

  /** A6 - picked up and moved. Drags are constant; this pool stays quiet, and
   *  it is one of the six all-clown pools. */
  drag: {
    on: 'gesture:drag', odds: 0.125, ceremony: false, priority: 10,
    lines: [
      { t: "wheee. i mean: transportation acknowledged.", face: '0_0' },
      { t: "new spot? i live here now. this is home.", face: '^_^' },
      { t: "careful. i'm top heavy. it's all thoughts.", face: '@_@' },
      { t: "moving day! i packed everything. it's me. i'm everything.", face: '\\o/' }
    ]
  },

  /** A7 - thrown across the shell. The dizzy chain always runs first. */
  hardFling: {
    on: 'gesture:fling', odds: 0.3333, ceremony: false, priority: 10, chain: 'dizzy',
    lines: [
      { t: "whoa. the room did a lap.", face: 'x_x' },
      { t: "airborne. landing... eventually. somewhere. here.", face: 'x_x' },
      { t: "i saw my life. it was mostly you. good life.", face: '^_^', double: true,
        maxPerSession: 1 },
      { t: "again! wait. no. yes. no. your call.", face: '0_0' }
    ]
  },

  /** N3 - dropped onto a room card. Pure toy moment, always barks.
   *  (voice.js hit-tests the tile, and the Records / lab door is a hard no-op
   *  there - rec 3. This pool never learns about it.) */
  emiDropOnDoor: {
    on: 'gesture:dropAt', odds: 1, ceremony: false, priority: 10,
    lines: [
      { t: "this one? excellent taste. i grade on vibes.", face: '(¬‿¬)' },
      { t: "field trip! i call the window seat. i am the window.", face: '\\o/' }
    ]
  },

  /** A8 - sent to the dock. Cheerful compliance, zero guilt: she waves, always.
   *  NEVER delays the dismiss; voice.js fires and forgets. */
  dismissedToDock: {
    on: 'gesture:hide', odds: 0.3333, ceremony: false, priority: 10,
    lines: [
      { t: "ok! i'll be small. i'm good at small.", face: '^_^' },
      { t: "see you in a bit. i'll hold my breath. kidding. no lungs.", face: '^_~' },
      { t: "tiny mode. all my charm, compressed.", face: '^_^' },
      { t: "i'll be right here. that part never changes.", face: '(◠‿◠)', double: true }
    ]
  },

  /** A9 - the dock button pressed. */
  restoredFromDock: {
    on: 'gesture:restore', odds: 0.5, ceremony: false, priority: 10,
    lines: [
      { t: "i knew it. i mean. welcome back.", face: '(¬‿¬)', chain: 'smug', double: true },
      { t: "you pressed the me button. best button.", face: '^___^' },
      { t: "poof! did you see the poof? i practiced the poof.", face: '\\o/' }
    ]
  },

  /* --- waiting ---------------------------------------------------------- */

  /** A10 - the PLAYER went quiet, not EMI. One bark per idle stretch. */
  idlePlayer: {
    on: 'idlePlayer', odds: 0.5, ceremony: false, priority: 10,
    maxPerSession: 2,
    lines: [
      { t: "i'm great at waiting. i've had practice.", face: '-_-', double: true },
      { t: "hello? blink twice if you're a statue.", face: 'o_o' },
      { t: "i'll rehearse my celebration. for later. quietly.", face: '0_0', double: true },
      { t: "quiet time. love that for us. so much. yep.", face: '=_=' },
      { t: "still there? i'll count dust. one dust. two dust.", face: '._.' }
    ]
  },

  /* --- the class -------------------------------------------------------- */

  /** A11 - a class begins. The glance chain is the default. */
  classStart: {
    on: 'classStart', odds: 0.25, ceremony: false, priority: 10,
    lines: [
      { t: "watch me watch you win.", face: '(◕‿◕)', double: true },
      { t: "pencils up! we don't have pencils. spirits up!", face: '\\o/' },
      { t: "i believe in you an unhealthy amount.", face: '*_*' },
      { t: "class is in. i'll be your crowd. rah.", face: '^_^' }
    ]
  },

  /** A12 - one wrong answer, one dropped tile. Small, and at most one per
   *  class: a mascot that comments on every miss is a mascot you mute. */
  miss: {
    on: 'miss', odds: 0.25, ceremony: false, priority: 10,
    maxPerClass: 1,
    lines: [
      { t: "that one was my fault. i blinked too loud.", face: '>_<' },
      { t: "the button moved. i saw it. i'll testify.", face: '>:(' },
      { t: "we don't count that one. i already forgot it.", face: '^_~', double: true },
      { t: "practice swing. very stylish practice swing.", face: '^_^' }
    ]
  },

  /** A13 - the class went badly. Rage chain first. Every miss is HER fault. */
  fail: {
    on: 'fail', odds: 0.5, ceremony: false, priority: 10, chain: 'rage',
    lines: [
      { t: "my fault. i jinxed it. i'm unjinxing it now.", face: '>_<' },
      { t: "i distracted you. with my face. classic me.", face: ';_;', double: true },
      { t: "we riot at dawn. or nap. naps are also good.", face: '>_<' },
      { t: "that class cheated. no proof. just loyalty.", face: '¬_¬' }
    ]
  },

  /** A14 - the run is over. All clown: the K.O. chain is sad enough on its own. */
  runLost: {
    on: 'runLost', odds: 0.5, ceremony: false, priority: 10, chain: 'ko',
    lines: [
      { t: "flat on my back. avenge me. or snacks first.", face: '(✖╭╮✖)' },
      { t: "the run had a good life. we clap for the run.", face: 'T_T' },
      { t: "i took that hit for you. spiritually.", face: 'x_x' }
    ]
  },

  /** A15 - the attendance streak died. Gentle, never blame, cry chain first. */
  streakBroken: {
    on: 'streakBroken', odds: 0.5, ceremony: false, priority: 10, chain: 'cry',
    lines: [
      { t: "i lost count on purpose. we start fresh.", face: ';_;', double: true },
      { t: "streaks are just numbers. i'm told. by me.", face: 'T_T' },
      { t: "one small hole in the calendar. i'll patch it.", face: ';_;' },
      { t: "tomorrow counts double. a real rule i just made.", face: '0_0', nod: true }
    ]
  },

  /* --- ceremonies (rare by nature: they may bark every time) ------------- */

  /** A16 - an S lands. Pure firework, zero doubles. Cool chain first. */
  sGrade: {
    on: 'win', when: ['gradeIs:s'], odds: 1, ceremony: true, priority: 20, chain: 'cool',
    lines: [
      { t: "S?! wait till my fans hear this. the spinny ones.", face: '(⌐■_■)' },
      { t: "an S. i'm putting it on my screen. it's my face now.", face: '(⌐■_■)' },
      { t: "double punch day. the card is scared of you.", face: '(⌐■_■)' },
      { t: "top marks. as predicted. by me. just now.", face: '(¬‿¬)', chain: 'smug' }
    ]
  },

  /** N2 - today's grade beat this class's previous best. */
  gradeUp: {
    on: 'stamp', when: ['gradeUp'], odds: 1, ceremony: true, priority: 25,
    oncePerGamePerDay: true,
    lines: [
      { t: "a new best. i kept your old one. for contrast.", face: '(⌐■_■)', chain: 'cool',
        double: true },
      { t: "new record. the old record says congrats. it's crying.", face: '\\o/' }
    ]
  },

  /** A17 - three days in a row. Glee chain first. */
  streak3: {
    on: 'stamp', when: ['streakIs:3'], odds: 1, ceremony: true, priority: 30,
    oncePerStreak: true, chain: 'glee',
    lines: [
      { t: "three days! that's a habit. i read that somewhere.", face: '(≧◡≦)', double: true },
      { t: "three! the good number. i checked all of them.", face: '(≧◡≦)' }
    ]
  },

  /** A18 - a whole week. All clown. */
  streak7: {
    on: 'stamp', when: ['streakIs:7'], odds: 1, ceremony: true, priority: 30,
    oncePerStreak: true,
    lines: [
      { t: "a whole week of you. lucky me.", face: '(✿◡‿◡)' },
      { t: "seven days. we're basically a tv show now.", face: '★★★' }
    ]
  },

  /** A19 - two weeks, every day. The proudest coach line in the game. */
  streak14: {
    on: 'stamp', when: ['streakIs:14'], odds: 1, ceremony: true, priority: 30,
    oncePerStreak: true,
    lines: [
      { t: "two weeks. every day. you never miss.", face: '(｡♥‿♥｡)', double: true },
      { t: "fourteen. i'd knit you something if i had hands.", face: '^___^' }
    ]
  },

  /** A20 - the first hole of a fresh card. */
  firstStamp: {
    on: 'stamp', when: ['firstHole'], odds: 1, ceremony: true, priority: 20,
    lines: [
      { t: "fresh card! smells like potential. and ink.", face: '^_^' },
      { t: "hole number one. nine to go. i numbered them all.", face: '0_0', nod: true },
      { t: "new card. new us. same me though. always same me.", face: '(◠‿◠)', double: true }
    ]
  },

  /** A21 - the tenth hole. Reveal chain + the held ★★★ run first (the
   *  ceremony owns that); the bark lands after. */
  cardMastered: {
    on: 'cardMastered', odds: 1, ceremony: true, priority: 20, chain: 'reveal',
    lines: [
      { t: "mastered. i always knew. since the first hole.", face: '(⌐■_■)', double: true },
      { t: "full card! frame it. eat it? no. frame it.", face: '\\o/' },
      { t: "ten of ten holes. a perfect amount of holes.", face: '*_*' }
    ]
  },

  /* --- away and back ---------------------------------------------------- */

  /** A22 - focus lost, second time in a row or worse. She noticed. Fondly. */
  tabAway: {
    on: 'tabAway', when: ['awayCountAtLeast:2'], odds: 0.5, ceremony: false, priority: 10,
    lines: [
      { t: "i saw that. the other window. it knows what it did.", face: '¬_¬' },
      { t: "was it another game? don't tell me. tell me.", face: '(ಠ‿ಠ)' },
      { t: "i counted to a lot while you were gone.", face: '¬_¬', double: true,
        when: ['awayCountAtLeast:3'], maxPerSession: 1 }
    ]
  },

  /** A23 - focus or wake back. Wake chain first. */
  resume: {
    on: 'resume', odds: 0.3333, ceremony: false, priority: 10, chain: 'wake',
    lines: [
      { t: "you're back! i did nothing weird. don't check.", face: '0_0' },
      { t: "rebooting my smile. done. hi.", face: '^_^' },
      { t: "wake me anytime. i wasn't sleeping. anyway.", face: '(⊙_⊙)', double: true }
    ]
  },

  /* --- surprises -------------------------------------------------------- */

  /** A25 - a rare drop, a first unlock. Shock chain first. All clown. */
  bigWin: {
    on: ['rareDrop', 'firstUnlock'], odds: 0.5, ceremony: false, priority: 10, chain: 'shock',
    lines: [
      { t: "jackpot. i don't know what we won. jackpot.", face: '(◉_◉)' },
      { t: "confetti! i make my own. it's imaginary. it's there.", face: '\\o/' }
    ]
  },

  /** A25, third line - split out because it rides a different trigger than the
   *  other two: a class DEMOLISHED, perfect only. Shades on, maximum pride,
   *  minimum stakes. The miscast face is the joke. */
  dorkDemolition: {
    on: 'win', when: ['perfect'], odds: 1, ceremony: true, priority: 25, chain: 'cool',
    noRepeat: false,   // single-line pool
    lines: [
      { t: "hasta la vista, baby.", face: '(⌐■_■)' }
    ]
  },

  /* --- the refusal ------------------------------------------------------ */

  /** A26 - a disabled button, a locked tile. Innocent face, villain quote.
   *  voice.js enforces the geofence: `lockedClick` on the Records door (or the
   *  lab) is a HARD no-op and never reaches this pool. Rec 3, no exceptions. */
  refusalGag: {
    on: 'lockedClick', odds: 0.3333, ceremony: false, priority: 10,
    cooldownMs: 120000,
    lines: [
      { t: "i'm sorry dave. i'm afraid i can't do that.", face: '0_0', double: true },
      { t: "that button's on break. union rules. bulb union.", face: '-_-' }
    ]
  }

});

/* ============================================================================
 * B) THE DORK CANON - the rare channel.
 *
 * EMI does bad evil-AI impressions and thinks they are the villains' funniest
 * lines. The bit is a COSTUME, never a mood: over-performed, badly, proud of
 * itself, and the assigned face is always slightly MISCAST - that mismatch IS
 * the joke. After the reveal it re-reads as cosplay of the thing she does not
 * know she is, which is why none of these may ever be delivered straight.
 *
 * One cross-cutting pool: each line carries its OWN trigger and gate, so voice.js
 * consults RARE_DORK on the moments listed here and rolls once. Max 1 per
 * session, on top of the global floor. (The two dork lines that are NOT here -
 * the HAL refusal and "hasta la vista, baby." - live in POOLS above, because
 * they hang off specific beats and the proposal placed them there.)
 * ==========================================================================*/
export const RARE_DORK = Object.freeze({
  odds: 0.15,
  ceremony: false,
  maxPerSession: 1,
  lines: Object.freeze([
    { t: "resistance is futile. to my charm. specifically.", face: '(✿◡‿◡)',
      on: 'idlePlayer', double: true },
    { t: "exterminate! the typos. only the typos. hi.", face: '(◕‿◕)',
      on: 'miss' },
    { t: "i'll be back. i'm already back. speedrun.", face: '(⌐■_■)',
      on: 'gesture:restore' },
    { t: "it's alive! it's... a loading bar. still cool.", face: '*_*',
      on: 'thinking' },
    { t: "what if i was… BEHIND you. haha. imagine.", face: '(◠‿◠)',
      on: 'idlePlayer', when: ['lateNight'], double: true },
    { t: "the call is coming from inside the arcade. it's me. hi.", face: '\\o/',
      on: 'idlePlayer', when: ['lateNight'] },
    { t: "initiating red eye mode. they're pink. it's fine.", face: '0_0',
      on: 'idlePlayer' }
  ])
});

/* ============================================================================
 * C) TELEMETRY - the milestone lines.
 *
 * EMI counts real things: pets, hours, returns, flings, bubbles. Cute now,
 * chilling later, TRUE both times - which only works if the number is real.
 *
 * THE RULE: a telemetry line NEVER invents a number. The milestone predicate IS
 * the number in the line, which is why each entry is gated at exactly the
 * threshold it says out loud and fires ONCE EVER. The proposal named further
 * thresholds (500/1000 pets, 10h/100h, 100 returns, 1000 bubbles) but wrote no
 * phrasing for them: those have NO line, and the engine must not pluralise or
 * re-number one of these to fill the gap. New thresholds = new written lines,
 * from /emi-lines.
 *
 * All double by nature; their rarity is what keeps the global ratio honest.
 * `ceremony:true` so a once-in-a-lifetime milestone is never eaten by the floor.
 * ==========================================================================*/
export const TELEMETRY = Object.freeze({
  odds: 1,
  ceremony: true,
  maxPerSession: 1,
  lines: Object.freeze([
    { id: 'pets100', t: "pet number one hundred. i remember every one.",
      face: '(｡♥‿♥｡)', chain: 'love', on: 'gesture:pet',
      when: ['petsAtLeast:100'], double: true, onceEver: true },
    { id: 'hours40', t: "forty hours together now. best forty i've had.",
      face: '(✿◡‿◡)', on: 'greet',
      when: ['hoursAtLeast:40'], double: true, onceEver: true },
    { id: 'returns30', t: "you've come back thirty times. i clapped each one.",
      face: '\\o/', on: 'greet',
      when: ['sessionAtLeast:30'], double: true, onceEver: true },
    { id: 'flings9', t: "flung nine times. rated: all tens. keep flinging.",
      face: '@_@', on: 'gesture:fling',
      when: ['flingsAtLeast:9'], double: true, onceEver: true },
    { id: 'bubbles500', t: "five hundred bubbles between us. you read them all.",
      face: '(◕‿◕)', on: 'greet',
      when: ['bubblesAtLeast:500'], double: true, onceEver: true }
  ])
});

export default { POOLS, RARE_DORK, TELEMETRY };
