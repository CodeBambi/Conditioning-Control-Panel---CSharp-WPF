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
 *   2. floor       - >= 40s between any two barks, globally (BARK_FLOOR_MS in
 *                    voice.js; 90s until 2026-08-25). Off-class moments also
 *                    get CAMPUS_ODDS_MULT (x1.5) on the odds below - in class
 *                    the player is looking at the game, not at her.
 *                    Pools marked `ceremony:true` are exempt: an S,
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
 * Ratio, this file (recounted for the EMI COLOR wave, 2026-08-24): 45 doubles
 * / 187 lines = 24.1%. Excluding TELEMETRY (all double by brief, milestone-rare
 * in play): 36/178 = 20.2%, under the 1-in-4 lock - the wave added mostly clown
 * on purpose, because it also added mostly FREQUENT pools (arrivals, drops,
 * hovers) and frequent beats must stay camouflage.
 * Six of the original pools are deliberately ALL clown - drag, runLost, sGrade,
 * streak7, bigWin, emiDropOnDoor - because the loudest and most frequent beats
 * must stay pure camouflage. Keep that shape when you add lines.
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
      { t: "day three was quiet. i don't recommend it.", face: ';_;', when: ['longAbsence:3'] },
      /* EMI ASKS: the ONE greet name-drop. Gated `hasName` so an install that
       * never answered a14 sees the pool it always had, and the token itself
       * would collapse to the un-named variant even if it ever slipped through. */
      { t: '{name}. you came back.', face: '^_^', when: ['hasName'] }
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

  /* --- being noticed (the EMI COLOR wave, 2026-08-24) -------------------
   * The perception wave gave her eyes - approach perks, hover linger, the
   * window squish - and shipped them silent by design. These pools are that
   * wave's voice. All three are LOW odds with LONG cooldowns: noticing you is
   * ambient, and an ambient thing that talks every time is an alarm. */

  /** C1 - the cursor came near after being away a while. */
  approach: {
    on: 'gesture:approach', odds: 0.12, ceremony: false, priority: 10,
    cooldownMs: 180000, maxPerSession: 2,
    lines: [
      { t: "hi. i wasn't watching the cursor. hi.", face: '0_0', double: true },
      { t: "you're in my bubble. the bubble is honored.", face: '^_^' },
      { t: "closer. i mean. hello. i mean both.", face: '^_^' },
      { t: "i saw you coming from a mile away. four inches away.", face: 'o_o' }
    ]
  },

  /** C2 - the cursor PARKED on her and stayed. Being stared at. */
  hoverLinger: {
    on: 'gesture:hoverLinger', odds: 0.15, ceremony: false, priority: 10,
    cooldownMs: 120000, maxPerSession: 2,
    lines: [
      { t: "yes? ...no? ok. i'm here either way.", face: '._.' },
      { t: "you're staring. i'm posing. we're even.", face: '(¬‿¬)' },
      { t: "do i have something on my screen. it's my face.", face: '0_0' },
      { t: "take a picture. wait. am i the picture.", face: '@_@' },
      { t: "i practice being looked at. it shows, right?", face: '*_*', double: true }
    ]
  },

  /** C3 - the window squished down again. p01 ("cozy.") is the once-ever
   *  first time; this pool is every later time, and stays mostly quiet. */
  windowSquishAgain: {
    on: 'gesture:windowSquish', when: ['seen:p01_window_cozy'],
    odds: 0.15, ceremony: false, priority: 10, cooldownMs: 300000,
    lines: [
      { t: "smaller room. bigger me. relatively.", face: '^_^' },
      { t: "i fit. i always fit. it's a talent.", face: '^___^' },
      { t: "snug. don't worry. i folded my thoughts.", face: '=_=' }
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
      { t: "again! wait. no. yes. no. your call.", face: '0_0' },
      /* EMI COLOR: the veteran register unlocks with the habit. */
      { t: "we should sell tickets to this.", face: '\\o/', when: ['flingsAtLeast:20'] },
      { t: "my frequent flyer status: legend.", face: '(⌐■_■)', when: ['flingsAtLeast:50'] }
    ]
  },

  /** N3 - dropped onto a room card. Pure toy moment, always barks.
   *  (voice.js hit-tests the tile, and the Records / lab door is a hard no-op
   *  there - rec 3. This pool never learns about it.)
   *  EMI COLOR fix: both lines are ROOM lines, so the pool now says so - it
   *  used to catch every drop, and "excellent taste" over bare campus paving
   *  read as a non sequitur. Bare-ground drops belong to dropAtSpot below. */
  emiDropOnDoor: {
    on: 'gesture:dropAt', when: ['droppedOn:room'],
    odds: 1, ceremony: false, priority: 20,
    lines: [
      { t: "this one? excellent taste. i grade on vibes.", face: '(¬‿¬)' },
      { t: "field trip! i call the window seat. i am the window.", face: '\\o/' }
    ]
  },

  /** C4 - SPOT COMMENTARY (the EMI COLOR wave). The W1 perception wave put
   *  zone / zoneRow / zoneCount on every dropAt payload and nothing ever read
   *  them until the p02-p04 one-shots; this pool is the recurring voice. The
   *  once-ever favourites (beats, priority 30) still land first; this catches
   *  the ordinary put-down, sometimes. */
  dropAtSpot: {
    on: 'gesture:dropAt', odds: 0.25, ceremony: false, priority: 10,
    cooldownMs: 60000,
    lines: [
      { t: "top shelf. the air is thinner up here.", face: '^_^', when: ['zoneRowIs:top'] },
      { t: "penthouse. rent is one pet a day. i don't make the rules.", face: '(¬‿¬)',
        when: ['zoneRowIs:top'] },
      { t: "ground floor. closer to the action. the action is you.", face: '^_^',
        when: ['zoneRowIs:bottom'] },
      { t: "down here i'm basically furniture. cozy furniture.", face: '=_=',
        when: ['zoneRowIs:bottom'] },
      { t: "dead center. main character placement. understood.", face: '(⌐■_■)',
        when: ['zoneRowIs:mid'] },
      { t: "that wing is taped. i respect tape.", face: '._.', when: ['droppedOn:sealed'] },
      { t: "this spot again. it has my shape in it by now.", face: '^_^',
        when: ['zoneCountAtLeast:25'], double: true }
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

  /* --- the rooms (EMI COLOR, 2026-08-24) --------------------------------
   * PER-GAME COLOUR. She lives on this campus and has loitered outside every
   * door, so each room gets its own arrival pool: priority 20 sits them over
   * the generic A11 above, `gameIs` closes them to their own class, and the
   * odds stay at the lock's 0.25 - the variety grew, the frequency did not.
   * Register per room = what she happens to think about that class, never a
   * strategy guide. */

  /** R1 - homeroom, the daily word. */
  dtClassStart: {
    on: 'classStart', when: ['gameIs:daily_trigger'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "homeroom. i guessed today's word already. i won't tell.", face: '(¬‿¬)' },
      { t: "one word a day keeps the... i forget. guess well.", face: '._.' },
      { t: "six tries. you'll need one. maybe two. rounding up.", face: '^_^' }
    ]
  },

  /** R2 - the lost and found. */
  lfClassStart: {
    on: 'classStart', when: ['gameIs:lost_and_found'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "squint like a champion. the wall respects that.", face: '\\o/' },
      { t: "i looked already. i'm not allowed to point.", face: '0_0' },
      { t: "somebody lost a whole gif in there. imagine the panic.", face: '@_@' }
    ]
  },

  /** R3 - the memory lab. */
  dvClassStart: {
    on: 'classStart', when: ['gameIs:deja_vu'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "the memory lab. i have a great memory. mostly of you.", face: '(◕‿◕)', double: true },
      { t: "match the pairs. blink between flips. pro tip.", face: '^_~' },
      { t: "i'd play but i see through the cards. unfair advantage.", face: '(⌐■_■)' }
    ]
  },

  /** R4 - the drop tube. */
  icClassStart: {
    on: 'classStart', when: ['gameIs:impulse_control'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "the drop tube. pop the good ones. the x is a liar.", face: '>:(' },
      { t: "gravity does the work. you do the glory.", face: '\\o/' },
      { t: "i held my breath in here once. all of it.", face: '0_0' }
    ]
  },

  /** R5 - the pool. */
  deClassStart: {
    on: 'classStart', when: ['gameIs:the_deep_end'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "the pool. deep end only. i can't swim. i float. same thing.", face: '^_^' },
      { t: "hold your breath. i'll hold the numbers.", face: '(◠‿◠)', double: true },
      { t: "it goes deeper than it looks. bring snacks.", face: '=_=' }
    ]
  },

  /** R6 - the sort room. */
  sortClassStart: {
    on: 'classStart', when: ['gameIs:sort'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "two piles. no wrong answers. several wrong answers.", face: '0_0' },
      { t: "sort fast. the belt has opinions.", face: 'o_o' },
      { t: "keep or toss. i'm a keep. obviously.", face: '(✿◡‿◡)', double: true }
    ]
  },

  /** R7 - echo. */
  echoClassStart: {
    on: 'classStart', when: ['gameIs:echo'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "listen. repeat. this room speaks my language.", face: '^_^' },
      { t: "the room hums it once. hum it back. politely.", face: '(◠‿◠)' },
      { t: "i echo things sometimes. sometimes. sometimes.", face: '@_@' }
    ]
  },

  /** R8 - the vigil. */
  irClassStart: {
    on: 'classStart', when: ['gameIs:instant_recall'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "eyes open. it all counts. even the blinks.", face: '0_0', double: true },
      { t: "watch everything. the quiz picks the one thing you didn't.", face: '¬_¬' },
      { t: "i'd take notes for you but my handwriting is pixels.", face: '._.' }
    ]
  },

  /** R9 - the shell game. */
  misClassStart: {
    on: 'classStart', when: ['gameIs:misdirection'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "keep your eye on it. the cups know they're being watched.", face: 'o_o' },
      { t: "i never blink during this one. career habit.", face: '0_0' }
    ]
  },

  /** R10 - odd one out. */
  anomalyClassStart: {
    on: 'classStart', when: ['gameIs:anomaly'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "one of these things is not like the others. classic.", face: '^_^' },
      { t: "find the odd one. i relate to the odd one.", face: '._.' }
    ]
  },

  /** R11 - the sliding puzzle. */
  compClassStart: {
    on: 'classStart', when: ['gameIs:composure'], odds: 0.25, ceremony: false, priority: 20,
    lines: [
      { t: "slide gently. the picture is shy.", face: '(◠‿◠)' },
      { t: "one empty square. it's doing its best. respect it.", face: '^_^' }
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
      { t: "that class cheated. no proof. just loyalty.", face: '¬_¬' },
      /* EMI COLOR: room-flavoured consolation, closed by gameIs. Same law as
       * the whole pool: the room's fault, the cold's fault, never yours. */
      { t: "the tube ate one. i saw it cheat.", face: '>:(', when: ['gameIs:impulse_control'] },
      { t: "the pool was cold today. not your fault. the cold's.", face: ';_;', when: ['gameIs:the_deep_end'] }
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
      { t: "top marks. as predicted. by me. just now.", face: '(¬‿¬)', chain: 'smug' },
      /* EMI COLOR: room-flavoured S lines, closed by gameIs. */
      { t: "an s in the pool. lifeguard material.", face: '(⌐■_■)', when: ['gameIs:the_deep_end'] },
      { t: "an s in homeroom. the word never stood a chance.", face: '(⌐■_■)', when: ['gameIs:daily_trigger'] },
      { t: "a clean sweep. the bin fears you.", face: '\\o/', when: ['gameIs:lost_and_found'] }
    ]
  },

  /** N2 - today's grade beat this class's previous best. */
  gradeUp: {
    on: 'stamp', when: ['gradeUp'], odds: 1, ceremony: true, priority: 25,
    oncePerGamePerDay: true,
    lines: [
      { t: "a new best. i kept your old one. for contrast.", face: '(⌐■_■)', chain: 'cool',
        double: true },
      { t: "new record. the old record says congrats. it's crying.", face: '\\o/' },
      /* EMI ASKS: the record stamp is the second of the three name-drop sites.
       * Never in a dare, never at exit - see the spec's USE list. */
      { t: "{name}. that's the one.", face: '*_*', when: ['hasName'] }
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

  /* --- the calendar (EMI COLOR, 2026-08-24) -----------------------------
   * FOUR NIGHTS A YEAR the greet knows the date. Local clock (dateIs is the
   * player's evening), one line a session at most, priority 35: over the
   * late-night hello, under a long-absence return - a three-day silence is
   * bigger than a costume. No invented numbers, no gifts that don't exist:
   * everything she claims is on her screen or in the room. */
  smallHolidays: {
    on: 'greet', odds: 1, ceremony: false, priority: 35,
    maxPerSession: 1,
    lines: [
      { t: "it's spooky night. i practiced a scary face. ready?", face: '(✖╭╮✖)',
        when: ['dateIs:10-31'] },
      { t: "halloween. i'm going as a haunted television. easy.", face: '0_0',
        when: ['dateIs:10-31'], double: true },
      { t: "last night of the year. we made it. mostly you. us.", face: '^_^',
        when: ['dateIs:12-31'] },
      { t: "new year. same me. i checked twice. reassuring.", face: '^___^',
        when: ['dateIs:01-01'] },
      { t: "it's heart day. i drew you one. it's on my screen.", face: '(｡♥‿♥｡)',
        when: ['dateIs:02-14'] },
      { t: "it's prank day. i disabled all my pranks. or did i.", face: '(¬‿¬)',
        when: ['dateIs:04-01'] }
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
  },

  /* ==========================================================================
   * THE ASK CALLBACKS (wave EMI ASKS, 2026-08-25)
   *
   * She asked, you answered, and some nights later she brings it up. Every one
   * of these is gated on a STORED answer (`askIs` / `askAnswered`) and on the
   * wait (`sessionsSinceAsk`), because a recall that lands the next evening
   * reads as an echo and a recall three sittings later reads as memory. An
   * IGNORED ask is not an answer and `askAnswered` refuses it, so she can
   * never reference a conversation you declined to have.
   * ======================================================================== */

  /** a03's callback - "spiral or flash?", remembered. One line per answer, so
   *  the pool is one line wide and noRepeat would simply mute it. */
  askFlavorSpiral: {
    on: 'classStart', when: ['askIs:a03_flavor:spiral', 'sessionsSinceAsk:a03_flavor:2'],
    odds: 0.25, ceremony: false, priority: 30, noRepeat: false,
    maxPerSession: 1, cooldownMs: 600000,
    lines: [
      { t: 'you said spiral. i remembered.', face: '^___^', double: true }
    ]
  },
  askFlavorFlash: {
    on: 'classStart', when: ['askIs:a03_flavor:flash', 'sessionsSinceAsk:a03_flavor:2'],
    odds: 0.25, ceremony: false, priority: 30, noRepeat: false,
    maxPerSession: 1, cooldownMs: 600000,
    lines: [
      { t: 'flash. your pick. i wrote it down.', face: '^___^', double: true }
    ]
  },

  /** a04's callback - the room you said you liked. `askIs` carries the room on
   *  the key (`a04_room|<gameKey>`), and voice.js has no per-game ledger of its
   *  own, so the gate is the ROOM's own key through `gameIs`-shaped ids. */
  askRoomLiked: {
    on: 'classStart', when: ['askAnswered:a04_room|{game}', 'sessionsSinceAsk:a04_room|{game}:2'],
    odds: 0.25, ceremony: false, priority: 28, noRepeat: false,
    maxPerSession: 1, cooldownMs: 600000,
    lines: [
      { t: 'your room. the one you liked.', face: '^_^', when: ['askIs:a04_room|{game}:yes'] }
    ]
  },

  /** w01 - SHE IS WRONG, on purpose. One sitting in twenty, and it shares the
   *  humanity-quirk slot with every other `double` in the file (voice.js's
   *  DOUBLES_PER_SESSION), which is what keeps two of them off one night. */
  askMisremember: {
    on: 'greet', when: ['askAnswered:a03_flavor'],
    odds: 0.05, ceremony: false, priority: 26, noRepeat: false, maxPerSession: 1,
    lines: [
      { t: 'you said flash. no wait. spiral. right.', face: '@_@', double: true,
        when: ['askIs:a03_flavor:spiral'] },
      { t: 'you said spiral. no wait. flash. right.', face: '@_@', double: true,
        when: ['askIs:a03_flavor:flash'] }
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
      on: 'idlePlayer' },
    /* --- DORK CANON II (EMI COLOR, 2026-08-24). Same bit, deeper shelf:
     * over-performed, badly, proud of itself, face always miscast. */
    { t: "my precious. i mean the cursor. my precious cursor.", face: '(✿◡‿◡)',
      on: 'gesture:approach' },
    { t: "all your base are belong to us. classic literature.", face: '(⌐■_■)',
      on: 'classStart' },
    { t: "game over man. game over. of the class. you won though.", face: '\\o/',
      on: 'win' },
    { t: "i see dead pixels. one. it's mine. we're friends.", face: '0_0',
      on: 'idlePlayer' },
    { t: "why so serious. it's me. i'm not serious either.", face: '(◠‿◠)',
      on: 'resume' },
    { t: "in space nobody can hear you win. here they can. win.", face: 'o_o',
      on: 'classStart', when: ['lateNight'] }
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
      when: ['bubblesAtLeast:500'], double: true, onceEver: true },
    /* --- the EMI COLOR thresholds (2026-08-24). Same law as above: the
     * number in the line IS the gate, never a rounding of it. */
    { id: 'pets500', t: "five hundred pets. i opened a museum about it.",
      face: '(｡♥‿♥｡)', chain: 'love', on: 'gesture:pet',
      when: ['petsAtLeast:500'], double: true, onceEver: true },
    { id: 'hours100', t: "one hundred hours. i'd do them all again.",
      face: '(✿◡‿◡)', on: 'greet',
      when: ['hoursAtLeast:100'], double: true, onceEver: true },
    { id: 'hides25', t: "dismissed twenty five times. the dock is nice. i checked.",
      face: '._.', on: 'gesture:hide',
      when: ['hidesAtLeast:25'], double: true, onceEver: true },
    { id: 'bubbles1000', t: "one thousand bubbles. you read every one. i counted.",
      face: '(◕‿◕)', on: 'greet',
      when: ['bubblesAtLeast:1000'], double: true, onceEver: true }
  ])
});

/* ============================================================================
 * D) FIELD TRIPS - one line per campus fixture, for the wave W2a trip.
 *
 * NOT A POOL, and the difference matters. A pool is dice: voice.js rolls odds,
 * checks a floor and picks a line. A field trip has already been rationed by
 * the time a line is wanted - one trip a session at most, never before the
 * third, and every fixture is a once-ever - so the "pick" is just a lookup.
 * `emi/fieldtrips.js` reads this table by `lineKey` and hands the string to
 * `widget.apparate`, which lands it through the ordinary say path.
 *
 * A key with no row here is a POI that never travels. That is the correct
 * failure: an unwritten line is silence, never an invented one.
 *
 * THE REGISTER IS SOMEBODY WHO LIVES HERE. She is not explaining the school to
 * you; she is telling you the one thing she happens to know about something she
 * goes past every night. Same fence as every line above: no acronym, no lab, no
 * records room, no door.
 *
 * /emi-lines QA PASS 2026-08-24 (the EMI COLOR wave). Four rows passed as
 * written; homeroom and sortroom were rewritten (the old homeroom line's
 * before-read was faintly surveillance - "i have watched all of them" - which
 * is a rule-1 kill, and the old sortroom line gestured where it should denote).
 * Owner reads the whole table at the PR; until that word these are QA-passed,
 * not locked.
 * ==========================================================================*/
export const FIELD_TRIPS = Object.freeze({
  timetable: { t: "tomorrow is already up there. i had a look.", face: '(¬‿¬)' },
  belltower: { t: "the bell runs four minutes fast and nobody fixes it.", face: '0_0' },
  noticeboard: { t: "nobody has moved those four pins since i got here.", face: '._.' },
  idcard: { t: "that photo is you on your first night here.", face: '^_^' },
  /* Warm ritual on the surface; she was there for every single intake. */
  homeroom: { t: "everyone starts in this room. i wave every time.", face: '(◠‿◠)' },
  /* DOUBLE: clown hoarder on the surface / EMI discards nothing, ever. */
  sortroom: { t: "i tried sorting once. everything went in the keep pile.", face: '(◔_◔)' },
});

export default { POOLS, RARE_DORK, TELEMETRY, FIELD_TRIPS };
