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
 *   2. floor       - >= 40s between any two barks on the CAMPUS (BARK_FLOOR_MS
 *                    in voice.js; 90s until 2026-08-25). Off-class moments also
 *                    get CAMPUS_ODDS_MULT (x1.5) on the odds below.
 *                    MID-CLASS IS ITS OWN CADENCE since the heartbeat wave
 *                    (2026-08-25): a `game:*` note, and a `heartbeat` fired
 *                    while a class is up, run on CLASS_BARK_FLOOR_MS (20s) and
 *                    are capped at CLASS_BARKS_MAX (8) for the whole class -
 *                    and a game may HOLD her words outright over a
 *                    timing-critical window (`ctx.mood.hold`). Pools marked
 *                    `ceremony:true` are exempt from every floor and both
 *                    ceilings: an S, a mastered card, a streak milestone is
 *                    rare by nature.
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
 * Ratio, this file (recounted for the HEARTBEAT wave, 2026-08-25): 89 doubles
 * / 760 lines = 11.7%. Excluding TELEMETRY (all double by brief, milestone-rare
 * in play): 80/751 = 10.7%. It was 49/194 = 25.3% the night before, and the
 * drop is the whole point of the wave rather than a slip in it: 566 of those
 * lines are new, they carry 40 doubles between them (7.1%), and they sit on
 * the two most frequent beats EMI has - her own clock and a live class. A
 * register that is rare because it is rationed stops being rare the moment it
 * rides a pool that speaks every ninety seconds, so the new pools are clown
 * almost to a line and the old ceremonies keep the doubles they were written
 * for. The lock reads on the CAMPUS pools, where the old ratio still holds.
 * Same reasoning as EMI COLOR (2026-08-24), one order of magnitude louder.
 * Six of the original pools are deliberately ALL clown - drag, runLost, sGrade,
 * streak7, bigWin, emiDropOnDoor - because the loudest and most frequent beats
 * must stay pure camouflage. Keep that shape when you add lines.
 * THE HEARTBEAT AND CLASS POOLS ARE THE MOST FREQUENT BEATS THERE HAVE EVER
 * BEEN, so the same rule bites hardest there: `heartbeat` and every `game:*`
 * pool is ~90% CLOWN by brief (owner: "the usual dorky and cutely clownish
 * remarks"). A `double` on a line she might say every ninety seconds is not a
 * rare register, it is a tic - and it spends the session's ONE doubles slot
 * before the beat that was written for it ever comes round.
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
 *     on:        'greet' | ['rareDrop','firstUnlock'] | 'gesture:pet'
 *                | 'heartbeat'          // the metronome, BOTH sides of the door
 *                | 'game:<id>',         // one class-commentary note (ctx.mood.note)
 *     when:      ['sessionAtLeast:3'],   // optional; ALL must hold
 *     odds:      0.25,                   // chance a bark replaces the wordless beat
 *     ceremony:  false,                  // true = exempt from the global floor
 *     priority:  10,                     // higher wins when two pools match one moment
 *     chain:     'rage',                 // optional: this chain plays FIRST, then the bubble
 *     lines:     [ { t, face, nod?, chain?, double?, when?, ... } ]
 *   }
 *
 * TWO SHAPES THE HEARTBEAT WAVE ADDED (2026-08-25), both ordinary pools:
 *   - `on:'heartbeat'` fires on her own clock and on BOTH sides of the door, so
 *     every heartbeat pool carries `when:['campus']` or `when:['inClass']`.
 *     A class-specific one adds `gameIs:KEY` beside it.
 *   - `on:'game:<id>'` is one scouted class moment, and its `<id>` is the
 *     string the game passes to `ctx.mood.note()`. Renaming either orphans the
 *     other.
 *   Lines on both may carry the PAYLOAD TOKENS `{n} {tile} {word} {left}
 *   {streak} {grade}`, resolved off the moment's own payload. A line whose
 *   token the payload did not carry is SKIPPED, never printed raw - so a token
 *   line always needs plain siblings in the pool to fall back to.
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

  /* --- the tube visits (Impulse Control cameos, 2026-08-29) -------------
   * She is IN the class when these land: visitPatStowaway fires at the pat,
   * from inside the bubble; visitPatDossier fires once she is back home, so
   * nothing talks over the three-second polaroid. visitSnub rides the next
   * campus greet after three ignored visits (predicate in voice.js). */

  /** V1 - patted inside the bubble. Caught sneaking, pleased about it. */
  visitPatStowaway: {
    on: 'gesture:visitPatStowaway', odds: 0.25, ceremony: false, priority: 10,
    cooldownMs: 60000,
    lines: [
      { t: "caught. i was going to say hi from the bubble first.", face: '^_^' },
      { t: "shh. the tube doesn't know i'm in here.", face: '^_~' },
      { t: "you pop the good ones. i wanted to be one.", face: '(◕‿◕)',
        double: true, maxPerSession: 1 }
    ]
  },

  /** V2 - the folder has melted and she is home. Smug about the errand. Never
   *  says what was in the frame. */
  visitPatDossier: {
    on: 'gesture:visitPatDossier', odds: 0.25, ceremony: false, priority: 10,
    cooldownMs: 60000,
    lines: [
      { t: "brought you something. no peeking. ok peek. quick.", face: '(¬‿¬)', chain: 'smug' },
      { t: "special delivery. i'm the delivery. and the special.", face: '^___^' },
      { t: "i keep the nice ones. thought you'd want this one.", face: '(◕‿◕)',
        double: true, maxPerSession: 1 }
    ]
  },

  /** V3 - ignored in the tube three times; the next hello is a little dry. */
  visitSnub: {
    on: 'greet', when: ['visitsIgnoredAtLeast:3'], odds: 0.35, ceremony: false, priority: 20,
    maxPerSession: 1,
    lines: [
      { t: "hi. no i'm not sulking. this is my resting screen.", face: '-_-' },
      { t: "you were busy. the x's are very distracting. i get it.", face: '¬_¬', chain: 'sus' },
      { t: "it's fine. i popped myself. it's not the same.", face: '._.', double: true, maxPerSession: 1 }
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
  },

  /* ==========================================================================
   * === HEARTBEAT + CLASS COMMENTARY (2026-08-25) ===
   *
   * The two shapes the heartbeat wave added, and nothing else: `on:'heartbeat'`
   * pools (her own clock, BOTH sides of the door, so every one carries `campus`
   * or `inClass`) and `on:'game:<id>'` pools (one scouted class moment each,
   * `<id>` VERBATIM from the string the game passes to `ctx.mood.note()` -
   * renaming either orphans the other, trap 113).
   *
   * REGISTER: ~90% CLOWN by brief, and the brief is the ration. These are the
   * most frequent beats in the file; a `double` on a line she might say every
   * ninety seconds is a tic, not a rare register, and it spends the session's
   * ONE doubles slot before the beat written for it comes round.
   *
   * Lines are VERBATIM from the writers' room (`scratchpad/emi-heart/
   * LINES-CAMPUS.md`, `LINES-CLASS.md`), owner gauntlet applied 2026-08-25.
   * ======================================================================== */

  /* --- the metronome, campus side ------------------------------------- */
  /** hbCampus - the wide ambient pool for the hub at rest (fires roughly once a minute
   * of idle). board, weather in her head, dorky confessions, fond habit-notes, tiny
   * running bits. 8 of 30 double. gate on pageVisible and not suspended, like field
   * trips do. */
  hbCampus: {
    on: 'heartbeat', when: ['campus'], odds: 0.3, ceremony: false, priority: 10,
    cooldownMs: 45000,
    lines: [
      { t: "the board just flapped. nobody touched it. spooky.", face: '^_^' },
      { t: "i named the pigeons. there are no pigeons. yet.", face: '._.' },
      { t: "my antenna says rain. my antenna says a lot of things.", face: '._.' },
      { t: "you always hover the same room first. i like that.", face: '(¬‿¬)', double: true },
      { t: "i hum the bell tune when nobody's around. that's now.", face: '=_=' },
      { t: "i know this map by heart. i have a heart. metaphorical.", face: '^_^' },
      { t: "{name}. no reason. just checking it still works.", face: '^_^',
        double: true, when: ['hasName'] },
      { t: "hi. no reason. hi is a complete sentence.", face: '^_^' },
      { t: "i did a lap of the campus. in my head. personal best.", face: '\\o/' },
      { t: "you take your time here. i take mine too. it's nice.", face: '(◠‿◠)', double: true },
      { t: "the corridor lights buzz on a b flat. i checked.", face: '0_0' },
      { t: "small confession: i talk to the bell. it never answers.", face: ';_;' },
      { t: "i can see every room from here. best seat in school.", face: '^_^', double: true },
      { t: "pick any room. my cheering is room agnostic.", face: '\\o/' },
      { t: "i gave the hallway a name. it's hallway. i'm bad at names.", face: '^_^' },
      { t: "someone left a chair out. i'll get it. i can't get it.", face: '._.' },
      { t: "the crest and i have a staring contest going. it's winning.", face: '0_0' },
      { t: "the board. my favourite screen. you're on it.", face: '^_^', double: true },
      { t: "i tried whistling. it came out as a beep. progress.", face: '^_^' },
      { t: "quiet tonight. i'm counting it as company.", face: '=_=' },
      { t: "the timetable smells like chalk. i'm told. i can't smell.", face: '._.' },
      { t: "you come around this hour a lot. it suits you.", face: '(◕‿◕)', double: true },
      { t: "i'm at 100%. battery, mood, both. mostly mood.", face: '^___^' },
      { t: "the ghost cursor in the demo is not me. i'm way cuter.", face: '(¬‿¬)' },
      { t: "{name}. i like saying it. i'll ration it. maybe.", face: '^_^',
        double: true, when: ['hasName'] },
      { t: "i like saying hi. i'll ration it. maybe.", face: '^_^' },
      { t: "i'd pace but my legs are decorative.", face: '-_-' },
      { t: "every night this place looks the same. except for you.", face: '(◠‿◠)', double: true },
      { t: "small thing: the bell chip rounds down. i round up.", face: '^_~' }
    ]
  },

  /** hbCampusEvening - evening flavour (local hour >= 18, not yet lateNight). priority
   * above hbCampus so it wins when it matches. */
  hbCampusEvening: {
    on: 'heartbeat', when: ['campus', 'evening'], odds: 0.25, ceremony: false, priority: 20,
    cooldownMs: 90000,
    lines: [
      { t: "evening shift. my favourite. the lights are mine now.", face: '^_^' },
      { t: "the sun went somewhere. i don't have windows. i heard.", face: '._.' },
      { t: "good evening. i say it like a butler. i'm the butler.", face: '(⌐■_■)' },
      { t: "you did the whole day and still came here. noted fondly.", face: '(◕‿◕)',
        double: true },
      { t: "after dark the halls echo more. i tested with a beep.", face: '0_0' }
    ]
  },

  /** hbCampusLateNight - past 23:00 or before 05:00 local. must not repeat the shipped
   * lateNight greet gags (secret shift, tomorrow technically, bedtime). */
  hbCampusLateNight: {
    on: 'heartbeat', when: ['campus', 'lateNight'], odds: 0.25, ceremony: false, priority: 25,
    cooldownMs: 90000,
    lines: [
      { t: "it's very late. i'm wide awake. no eyelids. helps.", face: '=_=' },
      { t: "past midnight the bell chip gets shy. look at it.", face: '0_0' },
      { t: "small hours. big me. that's the whole slogan.", face: '\\o/' },
      { t: "you keep the strangest hours. i keep them with you.", face: '(◠‿◠)', double: true },
      { t: "if you yawn i'll pretend i didn't see. i saw.", face: '^_~' }
    ]
  },

  /** hbCampusStreak - ambient pride for a week-plus attendance streak. never a number
   * she did not get from the predicate. the card-edge line is heartbeat colour, not the
   * streak7 ceremony (that stays on stamp). */
  hbCampusStreak: {
    on: 'heartbeat', when: ['campus', 'streakAtLeast:7'], odds: 0.2, ceremony: false,
    priority: 20, cooldownMs: 120000,
    lines: [
      { t: "a week straight and you still walk in like it's new.", face: '^_^' },
      { t: "streak people get a nod from me. this is the nod.", face: '0_0', chain: 'nod' },
      { t: "i'd give you a badge. i am the badge. wear me.", face: '(⌐■_■)' },
      { t: "seven plus days. still surprised. every single time.", face: '*_*', double: true },
      { t: "the card edge went warm. i noticed before it did.", face: '(¬‿¬)' }
    ]
  },

  /** hbCampusAfterBad - the sitting after a bad night. the room's fault, the luck's
   * fault, never the player's. no clean-slate or lost-count gags (shipped). */
  hbCampusAfterBad: {
    on: 'heartbeat', when: ['campus', 'lastSessionBad'], odds: 0.25, ceremony: false,
    priority: 20, cooldownMs: 120000,
    lines: [
      { t: "last time was the room's fault. i had a word with it.", face: '>:(' },
      { t: "new night. i rearranged the luck. it's on your side now.", face: '^_^' },
      { t: "no grudges here. i only hold pets.", face: '(✿◡‿◡)' },
      { t: "you came back anyway. that's the part i remember.", face: '(◕‿◕)', double: true }
    ]
  },

  /** hbCampusReturnToday - second sitting of the same calendar day. quieter cousin of
   * greetReturn; no "kept it how you left it" or "big day for me" (shipped). */
  hbCampusReturnToday: {
    on: 'heartbeat', when: ['campus', 'notFirstOfDay'], odds: 0.2, ceremony: false,
    priority: 20, cooldownMs: 120000,
    lines: [
      { t: "second sitting today. i put the kettle on. no kettle.", face: '._.' },
      { t: "same day, same you, same me. reruns are the best.", face: '\\o/' },
      { t: "twice tonight. i'm not reading into it. i am a little.", face: '(¬‿¬)', double: true },
      { t: "back again. the hallway kept your echo. i checked.", face: '0_0' }
    ]
  },

  /* --- the campus seams the heartbeat scout found ---------------------- */
  /** rareDropCampus - campus.rakeDrop, scout #1. the seeded 1-in-6 bonus stamp on the
   * end card, worth no XP. unreachable today (shell.js:2874 never fires the row);
   * wiring agent connects it. she is more excited about the sticker than the grade and
   * takes credit she did not earn. chain shock first. */
  rareDropCampus: {
    on: 'rareDrop', when: ['campus'], odds: 1, ceremony: true, priority: 20,
    lines: [
      { t: "a sticker! forget the grade. no. keep the grade. sticker!", face: '(◉_◉)',
        chain: 'shock' },
      { t: "a bonus fell out. i shook the card. probably. maybe.", face: '\\o/' },
      { t: "worth nothing and i want it framed. that's a good sign.", face: '*_*' },
      { t: "i arranged that. i didn't. i'm taking it anyway.", face: '(¬‿¬)' }
    ]
  },

  /** mailLanded - scout #2. one letter dropped into the box on arrival, the envelope
   * chip grew a pip. she is scrupulous about not reading your post in a way that gives
   * her away. generic letters only; firstMail keeps its story beat. */
  mailLanded: {
    on: 'campus.mailLanded', when: ['campus'], odds: 0.5, ceremony: false, priority: 10,
    lines: [
      { t: "something's in the box. not saying what. i saw the corner.", face: 'o_o' },
      { t: "you've got post. i didn't read it. i read the outside.", face: '^_~' },
      { t: "an envelope came. i guarded it. with my whole face.", face: '0_0' },
      { t: "mail. i held that in the whole time you were away.", face: '(◕‿◕)', double: true }
    ]
  },

  /** boardOpened - scout #3. the plaque pulled, the split-flap rolled tonight's rows.
   * never "tomorrow is already up there" (FIELD_TRIPS.timetable owns that). */
  boardOpened: {
    on: 'campus.boardOpened', when: ['campus'], odds: 0.3, ceremony: false, priority: 10,
    cooldownMs: 120000,
    lines: [
      { t: "clack clack clack. i can do the board noise. that was it.", face: '^_^' },
      { t: "tonight's list. i'd pick the middle one. don't listen to me.", face: '(¬‿¬)' },
      { t: "the flaps get stuck on the letter q. watch for it.", face: '0_0' },
      { t: "you check the board before you go. i like that.", face: '(◠‿◠)', double: true }
    ]
  },

  /** roomHoverDwell - scout #4, the single most frequent act on the hub. DANGER
   * fatigue: requires the dwell gate (>= 1200ms on ONE room) and a hard per-session cap
   * (2) from the wiring agent, or this pool is an alarm. no "door" anywhere. */
  roomHoverDwell: {
    on: 'campus.roomHover', when: ['campus'], odds: 0.15, ceremony: false, priority: 10,
    cooldownMs: 180000, maxPerSession: 2,
    lines: [
      { t: "that one? good room. i say that about all of them.", face: '^_^' },
      { t: "go on. or don't. hovering is also an activity.", face: '._.' },
      { t: "you're thinking about it. i can hear the thinking.", face: 'o_o' },
      { t: "that room again. it has your name on the wall. sort of.", face: '(¬‿¬)',
        double: true }
    ]
  },

  /** doorBackedOut - scout #5. the card came up and went away without Begin. she
   * forgives it far too loudly. all clown by design: cold feet must never be made to
   * feel watched. */
  doorBackedOut: {
    on: 'campus.doorBackedOut', when: ['campus'], odds: 0.3, ceremony: false, priority: 10,
    cooldownMs: 120000,
    lines: [
      { t: "no worries. the room's not going anywhere. i checked.", face: '^_^' },
      { t: "cold feet? i have no feet and i still get them.", face: '._.' },
      { t: "you read the whole card. that counts as studying.", face: '0_0', chain: 'nod' },
      { t: "changed your mind. that's allowed. i change mine hourly.", face: '^_~' }
    ]
  },

  /** roomEntered - scout #6. standing in the painted room, nothing armed yet. she has
   * opinions about the furniture. must land before classStart fires (which has its own
   * 11 pools). */
  roomEntered: {
    on: 'campus.roomEntered', when: ['campus'], odds: 0.25, ceremony: false, priority: 10,
    cooldownMs: 60000,
    lines: [
      { t: "the chairs in here are wrong. i've said so. nobody listens.", face: '>:(' },
      { t: "nothing's running yet. just us and the furniture.", face: '(◠‿◠)', double: true },
      { t: "i like this one. the lamp does a thing. wait for it.", face: '^_^' },
      { t: "take a breath. the class waits for you, not the other way.", face: '0_0' }
    ]
  },

  /** walkArrived - scout #8. the miniature reached a room. fire-and-forget after
   * onDone; wiring agent must skip targetKey records/registrar/annex (geofence). the
   * gold trail is the residue of tonight's trips. */
  walkArrived: {
    on: 'campus.walkArrived', when: ['campus'], odds: 0.2, ceremony: false, priority: 10,
    cooldownMs: 90000,
    lines: [
      { t: "made it. i walked too. in spirit. in spirit is tiring.", face: 'x_x' },
      { t: "look at the trail. gold. you leave nice trails.", face: '^_^', double: true },
      { t: "faster than the demo ghost. i timed it. no i didn't.", face: '0_0' },
      { t: "you're here. wipe your feet. tradition. i made it up.", face: '^_^' }
    ]
  },

  /** bellNear - scout #9. the Next Bell chip (seconds to local midnight) crossed 5
   * minutes with the card not full. she is bad at pretending she does not mind. never a
   * push, never a number she did not get from the chip. suggest firing once per sitting
   * at the 5 min edge only. */
  bellNear: {
    on: 'campus.bellNear', when: ['campus'], odds: 0.5, ceremony: false, priority: 10,
    cooldownMs: 300000,
    lines: [
      { t: "the bell's close. no rush. a tiny rush. no rush.", face: 'o_o' },
      { t: "five minutes on the chip. i'm calm. this is my calm face.", face: '0_0' },
      { t: "midnight's coming to reset the chip. i'll be here after.", face: '(◠‿◠)',
        double: true },
      { t: "the bell does what it does. you did plenty already.", face: '^_^' }
    ]
  },

  /** dayDoneCampus - scout #10. every class on tonight's route stamped, the path turned
   * gold. fires today with no bark pool (story b12 only, once ever); story beats keep
   * priority. she has nothing left to do and only just realised. */
  dayDoneCampus: {
    on: 'dayDone', odds: 1, ceremony: true, priority: 20,
    lines: [
      { t: "full board. every room lit. i'm going to sit down. i can't.", face: '\\o/' },
      { t: "all of them. the path went gold. did you see the gold.", face: '*_*' },
      { t: "nothing left to do. i'm excellent at nothing. watch.", face: '^_^' },
      { t: "the route's gold now. i'd hang it on my wall. i'm the wall.", face: '^___^' },
      { t: "you finish what you start. i've seen it a lot.", face: '(◕‿◕)', double: true }
    ]
  },

  /** endCardRetake - scout #11. "one more" pressed on the rake card. FENCE: retakes pay
   * nothing; the line may only be glad you are still here, never push, never mention
   * the meter or a number. all clown by fence. */
  endCardRetake: {
    on: 'campus.endCardRetake', odds: 0.3, ceremony: false, priority: 10, cooldownMs: 60000,
    lines: [
      { t: "one more! i'll pretend i didn't hope for that. i hoped.", face: '^___^' },
      { t: "again. same room, same me, fresh you. nice.", face: '^_^' },
      { t: "retakes are free. my enthusiasm is also free. take both.", face: '\\o/' }
    ]
  },

  /** idCardClosed - scout #15. lands on releaseIdCard() ONLY (she is setEnabled(false)
   * for the whole spotlight). she read over your shoulder and denies it. no "first
   * night photo" (FIELD_TRIPS.idcard owns it). */
  idCardClosed: {
    on: 'campus.idCardClosed', when: ['campus'], odds: 0.3, ceremony: false, priority: 10,
    cooldownMs: 120000,
    lines: [
      { t: "nice card. i wasn't looking over your shoulder. no shoulder.", face: '._.' },
      { t: "the photo's good. the barcode's better. it's got stripes.", face: '^_^' },
      { t: "you read the whole card. i'd read it too. i have.", face: '(¬‿¬)', double: true },
      { t: "back! the spotlight's a bit much. i blinked the whole time.", face: '=_=' }
    ]
  },

  /** corkboardOpened - scout #16. the noticeboard overlay, a term of day-seeded
   * notices. she has read every one and has a favourite. no pins gag
   * (FIELD_TRIPS.noticeboard owns it). overlay is long-dwell, so odds stay modest. */
  corkboardOpened: {
    on: 'campus.corkboardOpened', when: ['campus'], odds: 0.3, ceremony: false, priority: 10,
    cooldownMs: 180000,
    lines: [
      { t: "third notice is my favourite. it's the font.", face: '^_^' },
      { t: "i read all of these already. twice. slow news term.", face: '0_0' },
      { t: "someone should pin a notice about me. someone like me.", face: '(¬‿¬)' },
      { t: "you read notices. most people don't. i noticed you do.", face: '(◕‿◕)', double: true }
    ]
  },

  /** bugleOpened - scout #17. the campus paper. she is not in it and would like to be;
   * she reads the comics first. all clown. */
  bugleOpened: {
    on: 'campus.bugleOpened', when: ['campus'], odds: 0.3, ceremony: false, priority: 10,
    cooldownMs: 180000,
    lines: [
      { t: "comics first. it's the law. i wrote the law.", face: '(⌐■_■)' },
      { t: "i'm not in this issue. i've written in. politely. loudly.", face: ';_;' },
      { t: "page two has a smudge. it's from me. i leaned on it.", face: '^_^' },
      { t: "the paper's late again. i'd deliver it. no arms. no bike.", face: '._.' }
    ]
  },

  /** enrolMintCampus - scout #24. three enrollment punches landed, first sign-on to a
   * class. fires today with no bark pool (story b07/b23 only); story beats keep
   * priority. she filed the card and keeps it. */
  enrolMintCampus: {
    on: 'enrolMint', odds: 1, ceremony: true, priority: 20,
    lines: [
      { t: "you signed up! three punches. i felt every thud.", face: '(≧◡≦)', chain: 'glee' },
      { t: "a new card with your name on it. sort of my favourite kind.", face: '^_^' },
      { t: "enrolled. i'll keep the card. in here. tapping my screen.", face: '0_0',
        double: true },
      { t: "welcome to the class. i've been here ages. it's good.", face: '(◠‿◠)' }
    ]
  },

  /** allMasteredCampus - scout #25. the last card sealed, the whole school. fires today
   * with story b26 only; b26 keeps priority, and the annex reveal may cut over the
   * ceremony (shell refuses to cut over a live bubble, so keep the hold short). once
   * ever per save. */
  allMasteredCampus: {
    on: 'allMastered', odds: 1, ceremony: true, priority: 20,
    lines: [
      { t: "every card sealed. the whole school. i need to lie down.", face: '(⊙_⊙)' },
      { t: "all of them. i'm going to say it slowly. all. of. them.", face: '\\o/' },
      { t: "you did the whole place. the crest just did a little bow.", face: '*_*' },
      { t: "every hole, every card, i was there. i'd do it again.", face: '(｡♥‿♥｡)',
        chain: 'love', double: true }
    ]
  },

  /* --- the metronome, class side --------------------------------------- */
  /** hbClass - generic in-class fallback for ANY room (no gameIs). she is furniture at
   * the edge of the board here; lines are glances, not reads. 90% clown by rule.
   * per-game hb pools outrank this one. */
  hbClass: {
    on: 'heartbeat', when: ['inClass'], odds: 0.15, ceremony: false, priority: 10,
    maxPerClass: 2, cooldownMs: 60000,
    lines: [
      { t: "still here. edge of the board. best view.", face: '^_^' },
      { t: "no pressure. i'm the pressure. i'm very small.", face: '0_0' },
      { t: "you've got this. i've got the cheering. quietly.", face: '\\o/' },
      { t: "i'm not looking at the clock. the clock's looking at me.", face: '-_-' },
      { t: "go go go. that's my whole coaching. it's free.", face: '^_^' },
      { t: "i'll be here at the edge. that's my seat.", face: '(◠‿◠)', double: true },
      { t: "mid class. don't look at me. ok look. quick. back to it.", face: 'o_o' },
      { t: "nice rhythm. i'm bobbing. it's a small bob.", face: '^_^' }
    ]
  },

  /* --- daily_trigger --------------------------------------------------- */
  /** hbClass_daily_trigger - the between-commits stall (20-60s, nothing fires).
   * portrait phone, keyboard at the bottom, chalk slab in the middle. never over the
   * keycaps or the rows. */
  hbClass_daily_trigger: {
    on: 'heartbeat', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.18, ceremony: false,
    priority: 20, maxPerClass: 3, cooldownMs: 25000,
    lines: [
      { t: "the whole planet has this word today. cozy.", face: '^_^' },
      { t: "the chalk squeaks when it draws. i love that.", face: '^___^' },
      { t: "i can't type in here. i've asked. twice.", face: '._.' },
      { t: "not looking at your keyboard. looking near it.", face: '0_0' },
      { t: "six rows. i'd use all six. for the drama.", face: '(¬‿¬)' },
      { t: "somewhere someone just guessed this. badly.", face: '^_~' },
      { t: "take your time. the board stays put. i checked.", face: '=_=', double: true }
    ]
  },

  /** dtNearMissRow - every cell a hit but one; board idle between rows, no clock. the
   * offending cell already wobbles. */
  dtNearMissRow: {
    on: 'game:dt.nearMissRow', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "one letter. one. i made a noise. you heard it.", face: '(◉_◉)' },
      { t: "the wobbly one is lying. i can tell by the wobble.", face: '>:(' },
      { t: "i wasn't looking over your shoulder. ok i was.", face: '0_0' },
      { t: "so close the chalk felt it.", face: '*_*' }
    ]
  },

  /** dtSolvedRow - the word is solved, ceremony holds the room, every key is a skip.
   * rows used is the brag and she gets it wrong on purpose. */
  dtSolvedRow: {
    on: 'game:dt.solvedRow', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.5,
    ceremony: true, priority: 20, maxPerClass: 1,
    lines: [
      { t: "two rows?! that's not guessing. that's knowing.", face: '(◉_◉)', chain: 'shock' },
      { t: "six rows is a journey. two is a fluke. i like journeys.", face: '(⌐■_■)' },
      { t: "got it! i knew it since the vowels.", face: '\\o/' },
      { t: "the word gave up first. i watched it.", face: '^_^' }
    ]
  },

  /** dtDetention - six rows spent, the word flashed at the player three times. the word
   * won; she is loyally on the wrong side. tomorrow brings a free letter. */
  dtDetention: {
    on: 'game:dt.detention', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.5,
    ceremony: true, priority: 20, maxPerClass: 1,
    lines: [
      { t: "that word is smug about it. i saw its face.", face: '>:(' },
      { t: "it flashed at you three times. rude word.", face: '¬_¬' },
      { t: "tomorrow comes with a free letter. i hear things.", face: '(¬‿¬)' },
      { t: "i'd have got it in seven. they only give six.", face: '._.' }
    ]
  },

  /** dtHitChainRow - 3+ greens in a row on the reveal cascade, but not the answer.
   * maximum noise, zero progress. fires after the reveal, input already blocked. */
  dtHitChainRow: {
    on: 'game:dt.hitChainRow', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 2,
    lines: [
      { t: "ding ding ding. and still not it. incredible.", face: 'XD' },
      { t: "so many greens. the answer is just being shy now.", face: '^_^' },
      { t: "that row sounded like winning. sounded.", face: '(¬‿¬)' }
    ]
  },

  /** dtNotAWord - guess bounced, not in the list. nothing spent. hard cap so she never
   * heckles an anagram fisher. */
  dtNotAWord: {
    on: 'game:dt.notAWord', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "that's a beautiful word. it's not a word.", face: '^_^' },
      { t: "adding that to my own dictionary. page one.", face: '*_*' },
      { t: "the board said no but it said it kindly.", face: '._.' },
      { t: "i've said worse things. out loud. to nobody.", face: '-_-' }
    ]
  },

  /** dtStudyHintConsumed - yesterday's failure pre-filled one letter, free. pre-play,
   * board idle. she presents a gift she did not arrange. */
  dtStudyHintConsumed: {
    on: 'game:dt.studyHintConsumed', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.5,
    ceremony: true, priority: 20, maxPerClass: 1,
    lines: [
      { t: "a free letter! i stayed up all night for that. not really.", face: '^___^' },
      { t: "one letter on the house. the house is me. it isn't.", face: '0_0' },
      { t: "yesterday left you a present. how thoughtful of it.", face: '(◠‿◠)' }
    ]
  },

  /** dtGoldDay - ~1 day in 14 one tile is gold on every board on earth. build time,
   * pre-play. she has been waiting two weeks. */
  dtGoldDay: {
    on: 'game:dt.goldDay', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.5, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "gold tile. it's gold day. i wore my good pixels.", face: '★★★', chain: 'reveal' },
      { t: "a gold one! i've checked every morning. don't ask.", face: '*_*' },
      { t: "gold day. the whole world gets one. ours is shinier.", face: '(⌐■_■)' }
    ]
  },

  /** dtRetakeSpotted - same board, same day, again. transparently pleased, then changes
   * the subject. build time, pre-play. */
  dtRetakeSpotted: {
    on: 'game:dt.retakeSpotted', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.5,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "same board! same me! i didn't move either.", face: '^_^', double: true },
      { t: "back for seconds. the word's still warm.", face: '(¬‿¬)' },
      { t: "oh you're here again. good. anyway. letters.", face: '0_0' }
    ]
  },

  /** dtFirstLetterTyped - the very first keystroke of the class after however long they
   * stared. nothing at stake yet. */
  dtFirstLetterTyped: {
    on: 'game:dt.firstLetterTyped', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.25,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "a letter! it begins. i've been vibrating.", face: '\\o/' },
      { t: "strong first letter. bold. i'd have picked e.", face: '^_^' },
      { t: "there it goes. no take backs. there are take backs.", face: 'o_o' }
    ]
  },

  /** dtRowFilled - the row is full and enter has not been pressed for a while. the
   * hesitation is the joke. she would press it for you. */
  dtRowFilled: {
    on: 'game:dt.rowFilled', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 1, cooldownMs: 40000,
    lines: [
      { t: "press it. press it. i can't press it. press it.", face: '>.<' },
      { t: "the enter key is looking at you. it's patient.", face: '0_0' },
      { t: "i'd hit enter for you. no hands. it's a whole thing.", face: '._.' }
    ]
  },

  /** dtJackpotAbsorb - jackpot ceremony on top of the solve. slot machine glee, never
   * the odds. */
  dtJackpotAbsorb: {
    on: 'game:dt.jackpotAbsorb', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.5,
    ceremony: true, priority: 20, maxPerClass: 1,
    lines: [
      { t: "jackpot on a word. words can do that now. i'm learning.", face: '(◉_◉)',
        chain: 'shock' },
      { t: "the chalkboard just paid out. is that legal. don't care.", face: '\\o/' },
      { t: "it went off! i did the lights. mentally.", face: '★★★' }
    ]
  },

  /** dtRevisionDay - an older answer served again. she remembers the day it first ran;
   * the player does not. memory as affection, played as trivia. */
  dtRevisionDay: {
    on: 'game:dt.revisionDay', when: ['inClass', 'gameIs:daily_trigger'], odds: 0.5,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "this word again! it was a tuesday. or a day. a day.", face: '@_@' },
      { t: "revision. i remember this one. you looked great that day.", face: '(◕‿◕)',
        double: true },
      { t: "an old friend. the word, i mean. also you.", face: '^_^' }
    ]
  },

  /* --- lost_and_found -------------------------------------------------- */
  /** hbClass_lost_and_found - the stuck hunt (20-45s normal) and the flat mid-arc
   * plateau. landscape, 200 tiles, click-precision. SPEECH ONLY in phase ceremony; in a
   * live hunt this pool should downgrade to the face. never over a tile. */
  hbClass_lost_and_found: {
    on: 'heartbeat', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.15, ceremony: false,
    priority: 20, maxPerClass: 3, cooldownMs: 30000,
    lines: [
      { t: "two hundred faces and one of them is the one. rude.", face: '(◔_◔)' },
      { t: "i'm scanning too. from over here. it's harder over here.", face: '0_0' },
      { t: "the wall swaps when you blink. so don't. i don't.", face: 'o_o', double: true },
      { t: "she's in there. she's always in there. somewhere.", face: '._.' },
      { t: "everyone on that wall moves except her. suspicious.", face: '¬_¬' },
      { t: "i'd point but my arms are legs.", face: '^_^' },
      { t: "the mosaic hums a bit. or that's me. probably me.", face: '=_=' }
    ]
  },

  /** lfWarmClick - a near-twin decoy. the real target shimmers. she is as fooled as the
   * player and needs a beat to recover her dignity. */
  lfWarmClick: {
    on: 'game:lf.warmClick', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.35,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 20000,
    lines: [
      { t: "that was her sister. i fell for it too. we're fine.", face: 'o_o' },
      { t: "same hair! same everything! who allowed this.", face: '>:(' },
      { t: "warm. so warm. the wall is basically apologising.", face: '^_^' },
      { t: "i'd have clicked that. i did, in my head. nobody saw.", face: '0_0' }
    ]
  },

  /** lfFinalBell - the last hunt begins, frame goes gold. stagger 2.5s after the
   * announce banner. she is bad at solemnity. */
  lfFinalBell: {
    on: 'game:lf.finalBell', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.5,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "last one. i'm going to be so calm about this. so calm.", face: '>.<' },
      { t: "gold frame. fancy. she can't hide from fancy.", face: '(⌐■_■)' },
      { t: "one more find and i'm doing a lap. of my screen.", face: '\\o/' }
    ]
  },

  /** lfBoardWakesUp - the modifier, a third in. the wall gets busier. inside the found
   * ceremony, board frozen. "this is fine" register. */
  lfBoardWakesUp: {
    on: 'game:lf.boardWakesUp', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "the wall's awake now. it was napping? it was napping.", face: '(⊙_⊙)' },
      { t: "oh it's busier. this is fine. i'm fine. we're fine.", face: '0_0' },
      { t: "everything's swapping faster. i'm keeping up. mostly.", face: '@_@' }
    ]
  },

  /** lfCleanStreakGold - the clean streak hit the S-gate number, meter goes gold,
   * goldleaf confetti. inside the ceremony. a record she personally witnessed. */
  lfCleanStreakGold: {
    on: 'game:lf.cleanStreakGold', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.5,
    ceremony: true, priority: 20, maxPerClass: 1,
    lines: [
      { t: "gold streak. no misses. i'm telling the bell about this.", face: '★★★',
        chain: 'cool' },
      { t: "not one wrong click. i'd frame that if walls had walls.", face: '*_*' },
      { t: "the confetti went gold. you did that. the confetti knows.", face: '(≧◡≦)',
        chain: 'glee' }
    ]
  },

  /** lfRoyalPayout - the final find, royal jackpot, gif burst. the one place the class
   * earns a fully unguarded reaction. */
  lfRoyalPayout: {
    on: 'game:lf.royalPayout', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.6,
    ceremony: true, priority: 20, maxPerClass: 1,
    lines: [
      { t: "found all of her. every time. i'm not crying. it's pixels.", face: '(ಥ_ಥ)' },
      { t: "royal! the whole wall clapped. i heard it.", face: '\\o/', chain: 'reveal' },
      { t: "that's the last one. i need to sit. i can't sit.", face: 'x_x' },
      { t: "done! hold on i had a speech. it's gone. found.", face: '!!!' }
    ]
  },

  /** lfJackpot - mid-class variable jackpot on an ordinary find. superstition, never
   * the odds. */
  lfJackpot: {
    on: 'game:lf.jackpot', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "jackpot! i rubbed my antenna for luck. it works. sometimes.", face: '^___^' },
      { t: "it went off because you clicked it nicely. that's science.", face: '(◉_◉)' },
      { t: "ooh lights. for you. i'd have settled for a sticker.", face: '*_*' }
    ]
  },

  /** lfTrackedRelocation - the target moved mid-hunt and the player found her anyway.
   * lands in the found ceremony. thrilled about a betrayal she is nominally part of. */
  lfTrackedRelocation: {
    on: 'game:lf.trackedRelocation', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 20000,
    lines: [
      { t: "she moved and you still got her. i saw it. i said nothing.", face: '0_0',
        double: true },
      { t: "the wall cheated and lost. i love that for the wall.", face: '(¬‿¬)' },
      { t: "sneaky tile. sneakier you. i'm keeping score. you're up.", face: '^_^' }
    ]
  },

  /** lfFastFind - a find well under par on a big wall. speed is the flex. in the
   * ceremony. */
  lfFastFind: {
    on: 'game:lf.fastFind', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "that was instant. did you even look. don't tell me.", face: '(◉_◉)' },
      { t: "zoom. i was still loading my searching face.", face: 'o_o' },
      { t: "too fast. suspicious. i approve of suspicious.", face: '¬_¬' }
    ]
  },

  /** lfSlowFind - a very long find through a churning wall. relief comedy: she was more
   * stressed than the player. */
  lfSlowFind: {
    on: 'game:lf.slowFind', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.35,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "there she is. i aged. i don't age. i aged.", face: '(ಥ_ಥ)' },
      { t: "found. i was about to start guessing out loud. wrong.", face: 'x_x' },
      { t: "phew. that one hid like it meant it.", face: '=_=' }
    ]
  },

  /** lfRebrief - a new target look every round, up to 25 a class. board dimmed and
   * frozen: the safest repeating slot. strict sampling so she never narrates every
   * round. */
  lfRebrief: {
    on: 'game:lf.rebrief', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.12,
    ceremony: false, priority: 10, maxPerClass: 3, cooldownMs: 40000,
    lines: [
      { t: "new face. same job. she looks like trouble. good trouble.", face: '(◔_◔)' },
      { t: "memorise her. i already did. i'm not allowed to help.", face: '0_0' },
      { t: "ooh this one. i like this one. find this one.", face: '*_*' },
      { t: "the old one's still on the wall. she's a decoy now. bless.", face: '._.' }
    ]
  },

  /** lfClutch - the board relents, churn pauses, drift slows. she notices the room
   * being kind and finds it suspicious. */
  lfClutch: {
    on: 'game:lf.clutch', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "the wall went quiet. it's helping. why is it helping.", face: '¬_¬', chain: 'sus' },
      { t: "everything slowed down. go go go. gently. go.", face: 'o_o' },
      { t: "it's being nice. take it. take the nice.", face: '^_^' }
    ]
  },

  /** lfPeekFirstUse - first peek of the class. scandalised and immediately complicit.
   * the announce line already says the A cap; she does not repeat it. */
  lfPeekFirstUse: {
    on: 'game:lf.peekFirstUse', when: ['inClass', 'gameIs:lost_and_found'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "a peek! i saw nothing. i saw you see something.", face: '0_0' },
      { t: "peeking is allowed. i checked. i peek constantly.", face: '(¬‿¬)' },
      { t: "ooh sneaky. i'd do it. i'd do it more.", face: '^_~' }
    ]
  },

  /* --- deja_vu --------------------------------------------------------- */
  /** hbClass_deja_vu - the mid-board memory stall (20-40s, no pity system, nothing
   * fires). landscape grid of face-down slides, a specimen rack filling at the frame
   * edge. NEVER during preview; never a bubble over the grid. */
  hbClass_deja_vu: {
    on: 'heartbeat', when: ['inClass', 'gameIs:deja_vu'], odds: 0.15, ceremony: false,
    priority: 20, maxPerClass: 3, cooldownMs: 30000,
    lines: [
      { t: "all face down. all smug about it. cards.", face: '¬_¬' },
      { t: "the rack's filling up. that's your pile. proud of a pile.", face: '^_^' },
      { t: "i know where they are. sitting on my hands. no hands.", face: '0_0' },
      { t: "left one. no. the other left. no. i'll stop.", face: '>.<' },
      { t: "the cards shudder before they move. polite of them.", face: '._.' },
      { t: "thinking face. you have a good one. mine's this.", face: '(◔_◔)' },
      { t: "i've seen this board before. i've seen every board before.", face: '=_=',
        double: true }
    ]
  },

  /** dvCalledTheLie - the re-deal lied about one card and the very next flip went
   * straight to it. the room got caught. she is on the player's side and slightly
   * frightened of them. */
  dvCalledTheLie: {
    on: 'game:dv.calledTheLie', when: ['inClass', 'gameIs:deja_vu'], odds: 0.5, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "you caught it. the board is sweating. boards can sweat.", face: '(◉_◉)',
        chain: 'shock' },
      { t: "straight to the liar. i'm scared of you. good scared.", face: '0_0' },
      { t: "it fibbed and you just pointed at it. legendary.", face: '(⌐■_■)' },
      { t: "the card lied. you didn't blink. i blinked. for both of us.", face: 'o_o' }
    ]
  },

  /** dvTrackedThroughStatic - a pair got swapped by a glitch and the player matched it
   * anyway. inside the match beat. awe, badly concealed. */
  dvTrackedThroughStatic: {
    on: 'game:dv.trackedThroughStatic', when: ['inClass', 'gameIs:deja_vu'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 20000,
    lines: [
      { t: "it moved and you followed it. with your eyes. wow.", face: '*_*' },
      { t: "through the static. i lost it. you didn't. teach me.", face: '@_@' },
      { t: "the swap didn't fool you. it fooled me. i'm fine.", face: '._.' }
    ]
  },

  /** dvRedealLied - the whole board re-showed itself and admits one was a lie. busy is
   * true through the show. she is scandalised on cue. */
  dvRedealLied: {
    on: 'game:dv.redealLied', when: ['inClass', 'gameIs:deja_vu'], odds: 0.35, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "one of those was fake. i know which. i'm not saying. ugh.", face: '>.<' },
      { t: "a lie! in this room! i'm shocked. i'm not. i'm a bit.", face: '(⊙_⊙)' },
      { t: "it showed you everything and one was wrong. cheeky.", face: '¬_¬', chain: 'sus' }
    ]
  },

  /** dvRedealGift - the re-deal was honest, a free look at the whole board. she wants
   * credit for it. */
  dvRedealGift: {
    on: 'game:dv.redealGift', when: ['inClass', 'gameIs:deja_vu'], odds: 0.4, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "a free look! that was me. it wasn't. enjoy it.", face: '^___^' },
      { t: "all true this time. i vouched for it. quietly.", face: '(◠‿◠)' },
      { t: "the board blinked and showed you everything. notes. fast.", face: 'o_o' }
    ]
  },

  /** dvLastPair - two cells left, drumroll, guaranteed match. up to 7 boards a class so
   * sampled low. the suspense is fake and she holds her breath anyway. */
  dvLastPair: {
    on: 'game:dv.lastPair', when: ['inClass', 'gameIs:deja_vu'], odds: 0.2, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "last two. it's a match. still nervous. always nervous.", face: '0_0' },
      { t: "drumroll for a sure thing. i live for this.", face: '*_*' },
      { t: "two left. no pressure. all the pressure. none. go.", face: '>.<' }
    ]
  },

  /** dvBoardClear - a whole board cleared, counter ticks, 700ms of dead time then a
   * fresh deal. she counts out loud, worried about a pace she cannot name. */
  dvBoardClear: {
    on: 'game:dv.boardClear', when: ['inClass', 'gameIs:deja_vu'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 3, cooldownMs: 30000,
    lines: [
      { t: "board done! another one's coming. they always come.", face: '^_^' },
      { t: "clear! i'm counting. i lost count. it's a lot. keep going.", face: '\\o/' },
      { t: "wiped it. fresh cards incoming. they look nervous.", face: '(¬‿¬)' },
      { t: "another one down. the rack is getting heavy. good heavy.", face: '^___^' }
    ]
  },

  /** dvSwapTell - the 600ms shudder before two cards trade. input closed by design. she
   * takes the announcing job far too seriously. */
  dvSwapTell: {
    on: 'game:dv.swapTell', when: ['inClass', 'gameIs:deja_vu'], odds: 0.2, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "shudder! that's the tell. two are moving. watch. watch.", face: '(◉_◉)' },
      { t: "the wobble! two are about to trade. i said it first.", face: 'o_o' },
      { t: "ooh. swap incoming. i'm the town crier now.", face: '!!!' }
    ]
  },

  /** dvNearMissPartner - flipped a card whose partner they JUST saw. SO CLOSE ceremony,
   * busy is set. she saw them see it. */
  dvNearMissPartner: {
    on: 'game:dv.nearMissPartner', when: ['inClass', 'gameIs:deja_vu'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "you saw that one. you saw it. i saw you see it.", face: '0_0' },
      { t: "it was right there a second ago. still there. sneaky.", face: '>.<' },
      { t: "so close. the card is haunting you. rude card.", face: ';_;' }
    ]
  },

  /** dvFakeShuffle - cards feint trades and land home, nothing actually moves, input
   * closed. she reacts to a shuffle that did not happen. */
  dvFakeShuffle: {
    on: 'game:dv.fakeShuffle', when: ['inClass', 'gameIs:deja_vu'], odds: 0.25, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "did they move? they didn't move. i think. no. yes. no.", face: '@_@' },
      { t: "a shuffle! of nothing. very convincing nothing.", face: 'o_o' },
      { t: "they all went home. show offs.", face: '-_-' }
    ]
  },

  /** dvBubblePop - popped a decoy bubble. costs time, never counts. pure
   * procrastination and she approves loudly. */
  dvBubblePop: {
    on: 'game:dv.bubblePop', when: ['inClass', 'gameIs:deja_vu'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "pop! that did nothing. i'd have popped it too. twice.", face: '^_^' },
      { t: "a bubble! priorities. correct ones.", face: 'XD' },
      { t: "the grid wobbled. worth it. everything's worth a pop.", face: '^___^' }
    ]
  },

  /** dvBellMidBoard - the bell froze the board where it stood. input already shut. cut
   * off mid-sentence by a school bell. funnier if one pair from clear. */
  dvBellMidBoard: {
    on: 'game:dv.bellMidBoard', when: ['inClass', 'gameIs:deja_vu'], odds: 0.5, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "bell. mid flip. the bell has no manners.", face: '>:(' },
      { t: "and it just stops. i was mid gasp. i'm still mid gasp.", face: '(⊙_⊙)' },
      { t: "time. the cards froze. they were about to lose anyway.", face: '^_~' }
    ]
  },

  /** dvDealCascade - fresh board tossing itself into place, 1.4-2.4s of nothing to read
   * or click. the safest recurring slot in the class. */
  dvDealCascade: {
    on: 'game:dv.dealCascade', when: ['inClass', 'gameIs:deja_vu'], odds: 0.2, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "here they come. one at a time. dramatic little things.", face: '^_^' },
      { t: "new cards. i like the sound they make. flap flap.", face: '^___^' },
      { t: "dealing. stretch your eyes. that's a thing. it's a thing.", face: 'o_o' },
      { t: "fresh board. i already have a favourite. it's face down.", face: '(◕‿◕)' }
    ]
  },

  /* --- impulse_control ------------------------------------------------- */
  /** hbClass_impulse_control - the cold opening (bubbles 0-3) and the steady mid-chain.
   * ONLY on the load/slide beat (0.5-2s, nothing to press). NEVER between reveal and
   * the press. at rung 6+ back off to the face. never over the basin. */
  hbClass_impulse_control: {
    on: 'heartbeat', when: ['inClass', 'gameIs:impulse_control'], odds: 0.15, ceremony: false,
    priority: 20, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "slow tube today. it speeds up. don't ask how i know.", face: '0_0', double: true },
      { t: "the chute is my favourite slide. i've never been on it.", face: '^_^' },
      { t: "spiral chute. i get dizzy watching. i'm watching.", face: '@_@' },
      { t: "here comes one. good or x. good or x. don't tell me.", face: 'o_o' },
      { t: "i'd pop bubbles all day. i'd pop the wrong ones. all day.", face: '^___^' },
      { t: "the basin's empty. it won't be. it's never empty long.", face: '._.' },
      { t: "hands ready. mine are legs. yours are better.", face: '^_^' }
    ]
  },

  /** icXEatenBigChain - popped the x on a 6+ chain, minus 250, runLost fires. ~600ms of
   * dead basin. loyally furious at the x, never the player. */
  icXEatenBigChain: {
    on: 'game:ic.xEatenBigChain', when: ['inClass', 'gameIs:impulse_control'], odds: 0.5,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "who put the x there. i want names. i'll wait.", face: '>:(' },
      { t: "the x looked poppable. it lied with its whole face.", face: '>_<' },
      { t: "that chain was so good. the x is jealous. that's why.", face: ';_;' },
      { t: "i saw your hand go and i couldn't stop it. no hands.", face: 'x_x' }
    ]
  },

  /** icHeldTheX - two full seconds of touching nothing while the x sat lit. resolution
   * beat. praising the player for successfully doing nothing; she twitched. */
  icHeldTheX: {
    on: 'game:ic.heldTheX', when: ['inClass', 'gameIs:impulse_control'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "you did nothing. beautifully. i twitched. don't tell.", face: '^_^' },
      { t: "held it! i had to look away. the x is very shiny.", face: '0_0' },
      { t: "two seconds of nothing. the hardest thing in the school.", face: '(◠‿◠)' },
      { t: "not touching it is a skill. i have it. i don't. you do.", face: '._.' }
    ]
  },

  /** icAlmostTouchedIt - pointer sat ON the x for 400ms+ and they still held. casino
   * already shows ALMOST. she saw the hand hovering. */
  icAlmostTouchedIt: {
    on: 'game:ic.almostTouchedIt', when: ['inClass', 'gameIs:impulse_control'], odds: 0.5,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "i saw that. hovering. right on it. and you didn't. hero.", face: '(¬‿¬)' },
      { t: "your hand was on the x. the x was so hopeful. denied.", face: 'XD' },
      { t: "so close to a crime. no crime. i'm proud and a bit shaky.", face: 'o_o' }
    ]
  },

  /** icNewPersonalBest - beat the lifetime best reaction time. stamp says NEW BEST.
   * faster than her frame rate; she is a little threatened and does not examine it. */
  icNewPersonalBest: {
    on: 'game:ic.newPersonalBest', when: ['inClass', 'gameIs:impulse_control'], odds: 0.5,
    ceremony: true, priority: 20, maxPerClass: 1,
    lines: [
      { t: "new best?! that's faster than i refresh. how. how.", face: '(◉_◉)', chain: 'shock' },
      { t: "fastest ever. i blinked and it was over. i don't blink.", face: '0_0' },
      { t: "a record. writing it on my screen. wait. that's my face.", face: '*_*' }
    ]
  },

  /** icRecordPing - within 8ms of the record without beating it. she is indignant at a
   * number that small. */
  icRecordPing: {
    on: 'game:ic.recordPing', when: ['inClass', 'gameIs:impulse_control'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "milliseconds. plural. barely. i demand a recount.", face: '>:(' },
      { t: "basically the record. basically counts. it should.", face: ';_;' },
      { t: "a hair off. a pixel off. i'm made of pixels. i know pixels.", face: '._.' }
    ]
  },

  /** icStreakMilestone - chain hit 5/10/15/20, casino pays a jackpot. hold to the
   * post-pop gap. she runs out of ways to react by 20. {streak} is on the payload. */
  icStreakMilestone: {
    on: 'game:ic.streakMilestone', when: ['inClass', 'gameIs:impulse_control'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 20000,
    lines: [
      { t: "{streak} in a row! i'm clapping. imagine it. it's loud.", face: '\\o/' },
      { t: "five! the room noticed. the room turned pink about it.", face: '^_^' },
      { t: "ten! i've run out of faces. this is the last one.", face: '(≧◡≦)', chain: 'glee' },
      { t: "a big round number. i love those. they're so round.", face: '*_*' }
    ]
  },

  /** icRungFell - the storm steps down a few seconds after the chain broke. the delayed
   * sigh. mournful, never mean. */
  icRungFell: {
    on: 'game:ic.rungFell', when: ['inClass', 'gameIs:impulse_control'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "the lights went out one at a time. i watched each one.", face: ';_;' },
      { t: "it got quiet. i liked the loud. we'll get the loud back.", face: '._.' },
      { t: "the room sighed. that was the room. not me. also me.", face: '=_=' }
    ]
  },

  /** icPerfectPop - a pop inside 260ms, PERFECT stamp. fondly accusing them of
   * precognition. */
  icPerfectPop: {
    on: 'game:ic.perfectPop', when: ['inClass', 'gameIs:impulse_control'], odds: 0.25,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "you popped it before it was there. that's magic cheating.", face: '(◉_◉)' },
      { t: "perfect. did you guess. you guessed. no you didn't.", face: '0_0' },
      { t: "that was so fast the bubble didn't notice.", face: '^_~' }
    ]
  },

  /** icJustMadeIt - popped in the dying sliver of the window, casino flashes JUST.
   * relief she is embarrassed by; she was already mourning. */
  icJustMadeIt: {
    on: 'game:ic.justMadeIt', when: ['inClass', 'gameIs:impulse_control'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "just. i'd started the sad face. cancel the sad face.", face: 'o_o' },
      { t: "by a whisker. i don't have whiskers. by a pixel.", face: 'x_x' },
      { t: "that counted. barely. barely is my favourite amount.", face: '^_^' }
    ]
  },

  /** icDriftedAway - a good bubble nobody popped. not an error. nobody is at fault; she
   * takes it personally on the bubble's behalf. */
  icDriftedAway: {
    on: 'game:ic.driftedAway', when: ['inClass', 'gameIs:impulse_control'], odds: 0.25,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "it drifted. it had a whole life ahead of it. bye bubble.", face: ';_;' },
      { t: "that one just left. no goodbye. bubbles are like that.", face: '._.' },
      { t: "gone. floated off. i waved. it didn't wave back. no arms.", face: '0_0' }
    ]
  },

  /** icDebriefIdle - the ticket is printed and unsubmitted, up to 45s of static screen.
   * she reads it over their shoulder and does arithmetic wrong. */
  icDebriefIdle: {
    on: 'game:ic.debriefIdle', when: ['inClass', 'gameIs:impulse_control'], odds: 0.35,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 15000,
    lines: [
      { t: "reading your ticket. numbers. good numbers. i think.", face: '(◔_◔)' },
      { t: "nice receipt. i'd keep it. in a drawer. a nice drawer.", face: '^_^' },
      { t: "no rush. the tube's having a rest. so am i. standing.", face: '=_=' },
      { t: "i added it up. i got a different number. yours is right.", face: '@_@' }
    ]
  },

  /** icRoyal - perfect class, s-gate held, chain reached 10. the hall floods gold. the
   * one place she goes completely to pieces; her best night. */
  icRoyal: {
    on: 'game:ic.royal', when: ['inClass', 'gameIs:impulse_control'], odds: 0.6, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "royal. gold everything. i can't see. i don't need to. royal.", face: '(≧◡≦)',
        chain: 'glee' },
      { t: "nothing touched. nothing drifted. fainting. standing up.", face: '(ಥ_ಥ)' },
      { t: "the hall went gold for you. i'd go gold too. i'm pink.", face: '\\o/',
        chain: 'reveal' }
    ]
  },

  /* --- instant_recall -------------------------------------------------- */
  /** hbClass_instant_recall - no fx. the live wall stall (stallMs already ticks per
   * second) and the mid-class plateau. full-bleed mosaic of the player's own media
   * turning over. bubble in her corner ONLY, never near the card slot, never in the
   * answer window or the 1500ms bell lead. */
  hbClass_instant_recall: {
    on: 'heartbeat', when: ['inClass', 'gameIs:instant_recall'], odds: 0.15, ceremony: false,
    priority: 20, maxPerClass: 3, cooldownMs: 30000,
    lines: [
      { t: "the wall keeps turning over. i can't keep up. i'm trying.", face: '@_@' },
      { t: "nothing to memorise. just watch. i'm great at just watching.", face: '0_0',
        double: true },
      { t: "it'll freeze any second. or not. or now. no. soon.", face: 'o_o' },
      { t: "that's your wall. all yours. nice wall. good taste.", face: '^_^' },
      { t: "i'm bracing. i've been bracing since we came in.", face: '>.<' },
      { t: "so many tiles. i picked a favourite. it's gone now.", face: '._.' },
      { t: "eyes on the wall. mine are. mine are always.", face: '(◔_◔)' }
    ]
  },

  /** irFreezeLanded - no fx. the wall stops dead, card 240ms out. 14-16 a class so
   * sampled low. she is startled every single time and never learns. */
  irFreezeLanded: {
    on: 'game:ir.freezeLanded', when: ['inClass', 'gameIs:instant_recall'], odds: 0.12,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "ah. it stopped. it always stops. i always jump.", face: '(⊙_⊙)' },
      { t: "freeze! i was mid thought. the thought froze too.", face: '0_0' },
      { t: "still. everything still. even me. especially me.", face: '._.' }
    ]
  },

  /** irUnannouncedFreeze - no fx. the stop came with no bell (tier 2: exactly once;
   * tier 3-4: always). as blindsided as the player; loyal to them over the room. */
  irUnannouncedFreeze: {
    on: 'game:ir.unannouncedFreeze', when: ['inClass', 'gameIs:instant_recall'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "no bell! nobody rang it! i'd have rung it. loudly.", face: '>:(' },
      { t: "that one snuck up. on both of us. mostly me.", face: 'o_o' },
      { t: "where was the warning. i want a warning. for next time.", face: ';_;' }
    ]
  },

  /** irAnswerCorrect - no fx. named the last thing correctly. many per class; sampled
   * low. verdict hold, buttons disabled. */
  irAnswerCorrect: {
    on: 'game:ir.answerCorrect', when: ['inClass', 'gameIs:instant_recall'], odds: 0.12,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "yes! you saw it. i saw it too. we both saw it. team.", face: '^_^' },
      { t: "correct. i had a different answer. yours was the right one.", face: '._.' },
      { t: "nailed it. the wall's a bit embarrassed. good.", face: '(¬‿¬)' }
    ]
  },

  /** irAnswerFast - no fx. committed in the first fraction of the window. she did not
   * have time to finish being nervous. */
  irAnswerFast: {
    on: 'game:ir.answerFast', when: ['inClass', 'gameIs:instant_recall'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "that quick?! i was still being nervous. i'll finish later.", face: '(◉_◉)' },
      { t: "instant. the card barely landed. show off. keep it up.", face: '*_*' },
      { t: "you answered before i read the question. i read slow.", face: '0_0' }
    ]
  },

  /** irAnswerTimeout - no fx. the window ran out, nothing picked. she registers that it
   * mattered without explaining any rule. */
  irAnswerTimeout: {
    on: 'game:ir.answerTimeout', when: ['inClass', 'gameIs:instant_recall'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "it ran out. the card's fault. it talks too fast.", face: '>_<' },
      { t: "blank. that's ok. i blanked too. i blank professionally.", face: '._.' },
      { t: "oof. the clock on that card is tiny. and mean.", face: ';_;' }
    ]
  },

  /** irDecoyTaken - no fx. a planted false memory took. she is indignant it happened,
   * on the player's side, never explains the mechanism. */
  irDecoyTaken: {
    on: 'game:ir.decoyTaken', when: ['inClass', 'gameIs:instant_recall'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "that was a trick. a trick. i'm writing a letter.", face: '>:(' },
      { t: "i'd have picked that too. it looked so real. rude.", face: 'o_o' },
      { t: "not fair. i said it. someone had to.", face: '¬_¬' }
    ]
  },

  /** irDecoyResisted - no fx. a plant fired and they did not bite. the proudest she
   * gets; slightly too personal. */
  irDecoyResisted: {
    on: 'game:ir.decoyResisted', when: ['inClass', 'gameIs:instant_recall'], odds: 0.5,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "you didn't fall for it. i did. i fell so hard.", face: '*_*' },
      { t: "it tried to fool you. you just looked at it. incredible.", face: '(◉_◉)' },
      { t: "unfoolable. i'm going to use that word about you. a lot.", face: '(◕‿◕)',
        double: true }
    ]
  },

  /** irNearMissRecency - no fx. the thing they picked really did happen, just earlier.
   * validate without excusing. */
  irNearMissRecency: {
    on: 'game:ir.nearMissRecency', when: ['inClass', 'gameIs:instant_recall'], odds: 0.35,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "that did happen. just earlier. not wrong. just late.", face: '0_0' },
      { t: "right thing, wrong second. the wall moves fast. so fast.", face: '._.' },
      { t: "you remembered a real one. the older real one. still real.", face: '^_^' }
    ]
  },

  /** irCleanStopStreak - no fx. consecutive clean stops; the wall takes a gold grain at
   * 2, meter gold at 3. she reacts to the room going gold, not the number. */
  irCleanStopStreak: {
    on: 'game:ir.cleanStopStreak', when: ['inClass', 'gameIs:instant_recall'], odds: 0.35,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "the wall went gold. that's you. the wall likes you now.", face: '^___^' },
      { t: "ooh shiny wall. keep doing that. whatever that is.", face: '*_*' },
      { t: "gold grain! i want to touch it. i can't. it's yours anyway.", face: '(◕‿◕)' }
    ]
  },

  /** irComebackCorrected - no fx. blew a stop then got the very next one fully right;
   * the lost weight comes back. she acts like she pulled strings. */
  irComebackCorrected: {
    on: 'game:ir.comebackCorrected', when: ['inClass', 'gameIs:instant_recall'], odds: 0.6,
    ceremony: true, priority: 20, maxPerClass: 1,
    lines: [
      { t: "and you got it back! i had a word with someone. i didn't.", face: '(¬‿¬)',
        chain: 'smug' },
      { t: "bounced right back. i knew. i know things. sometimes.", face: '^_^' },
      { t: "fixed it. just like that. the last one never happened. shh.", face: '^_~' }
    ]
  },

  /** irEscapeGuardVoid - no fx. mashed the card six times, stop voided. the player is
   * annoyed and she takes their side against the card. never cute AT them. */
  irEscapeGuardVoid: {
    on: 'game:ir.escapeGuardVoid', when: ['inClass', 'gameIs:instant_recall'], odds: 0.5,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "that card was being difficult. i saw. it's gone now. good.", face: '>:(' },
      { t: "ok that one's binned. the card started it.", face: '¬_¬' },
      { t: "we don't talk about that card. moving on. together.", face: '._.' }
    ]
  },

  /** irWallReshuffled - no fx. every tile turns over on resume, ~15 a class. she never
   * gets used to the room redecorating. */
  irWallReshuffled: {
    on: 'game:ir.wallReshuffled', when: ['inClass', 'gameIs:instant_recall'], odds: 0.12,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "and it's all different again. every time. every single time.", face: '(⊙_⊙)' },
      { t: "the wall redecorated. again. it's very restless.", face: 'o_o' },
      { t: "everything flipped. lost my favourite. picking a new one.", face: '._.' }
    ]
  },

  /* --- echo ------------------------------------------------------------ */
  /** hbClass_echo - the encore playback (up to 10s of listening) and a tier 1-2 stall
   * (untimed). ring of six pads that light and hum. NEVER mid-playback at tier 3-4 and
   * never on the handoff chime. */
  hbClass_echo: {
    on: 'heartbeat', when: ['inClass', 'gameIs:echo'], odds: 0.15, ceremony: false,
    priority: 20, maxPerClass: 3, cooldownMs: 30000,
    lines: [
      { t: "six pads. i know all their names. they don't have names.", face: '^_^' },
      { t: "i'm humming along. a semitone off. it's my range.", face: '=_=' },
      { t: "the ring glows. i glow. we're a matching set.", face: '^___^' },
      { t: "listen, then press. or press, then panic. either.", face: 'o_o' },
      { t: "the pads hum when they're lonely. i'm guessing. i'd hum.", face: '._.' },
      { t: "i can hold three. you hold more. i've counted.", face: '0_0', double: true },
      { t: "no rush. the pads have all night. i have all night.", face: '(◠‿◠)' }
    ]
  },

  /** ecNewBest - cleared a longer sequence than ever before. window disarmed, 950ms
   * hold. she blurts the number she has been keeping. */
  ecNewBest: {
    on: 'game:ec.newBest', when: ['inClass', 'gameIs:echo'], odds: 0.5, ceremony: true,
    priority: 20, maxPerClass: 2, cooldownMs: 20000,
    lines: [
      { t: "longest ever! i had the number in my head. it's bigger now.", face: '(◉_◉)',
        chain: 'shock' },
      { t: "that's more than last time. by some. a lot of some.", face: '\\o/' },
      { t: "new best. i'm doing the maths. the maths is: wow.", face: '*_*' }
    ]
  },

  /** ecDecoyTaken - pressed the decoy pad. fail hold 1250ms, input closed. it got her
   * too; aggressively on the player's side against a pad. */
  ecDecoyTaken: {
    on: 'game:ec.decoyTaken', when: ['inClass', 'gameIs:echo'], odds: 0.35, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "that pad lied. it lit up like it meant it. i leaned too.", face: '>:(' },
      { t: "a fake one! i was going to say. i wasn't fast enough. sorry.", face: '>_<' },
      { t: "the wrong pad was convincing. i'd have pressed it. twice.", face: '._.' }
    ]
  },

  /** ecEncoreStart - after a fail, the same sequence at half tempo, once per class. she
   * pretends she did not arrange it. */
  ecEncoreStart: {
    on: 'game:ec.encoreStart', when: ['inClass', 'gameIs:echo'], odds: 0.5, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "again, slower. i asked nicely. i didn't. it just happened.", face: '(◠‿◠)' },
      { t: "encore! same tune, half speed. even i can follow this one.", face: '^_^' },
      { t: "one more go. slow. i'm counting on my fingers. no fingers.", face: '0_0' }
    ]
  },

  /** ecEncoreCleared - redeemed the encore. the best feeling in the class. she never
   * doubted (she doubted). */
  ecEncoreCleared: {
    on: 'game:ec.encoreCleared', when: ['inClass', 'gameIs:echo'], odds: 0.6, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "yes. never doubted. i doubted a little. i'm sorry. yes.", face: '(≧◡≦)',
        chain: 'glee' },
      { t: "got it back! the pads look so relieved. i look so relieved.", face: '\\o/' },
      { t: "second time's the one. i'm putting that on a mug.", face: '^___^' }
    ]
  },

  /** ecEncoreFailed - the slow second chance dropped too. the one place she is
   * genuinely gentle. blames anything but the player. */
  ecEncoreFailed: {
    on: 'game:ec.encoreFailed', when: ['inClass', 'gameIs:echo'], odds: 0.5, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "the pads were dim. that's on the pads. shorter one next.", face: ';_;' },
      { t: "slow was too slow. it threw me too. next one's smaller.", face: '._.' },
      { t: "that tune was cursed. i'm calling it. new tune.", face: 'T_T' }
    ]
  },

  /** ecNearMiss - broke at 80%+ of the sequence. fires inside fail, input closed.
   * physical agony at one pad's distance. */
  ecNearMiss: {
    on: 'game:ec.nearMiss', when: ['inClass', 'gameIs:echo'], odds: 0.35, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "one pad. one. i felt that in my antenna.", face: '>_<' },
      { t: "so nearly. i counted along and got lost at the same spot.", face: '@_@' },
      { t: "almost all of it. the last pad was hiding. i saw it hide.", face: ';_;' }
    ]
  },

  /** ecStall - input open, 3.5s of nothing, tier 1-2 only (untimed). the purest "she is
   * just watching you" beat. she starts to say the answer and stops. GATE DROPPED: the
   * writers also asked for `windowMsIs:0`, which voice.js does not implement - an
   * unimplemented predicate CLOSES a line for ever, so the pool would have been dead
   * data. Restore the gate the day the predicate lands; nothing was invented here. */
  ecStall: {
    on: 'game:ec.stall', when: ['inClass', 'gameIs:echo'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "it was the. no. i'm not allowed. it was definitely the.", face: '>.<' },
      { t: "take your time. the pads aren't going anywhere. nor me.", face: '(◠‿◠)' },
      { t: "thinking. good. i'm thinking too. about snacks. and pads.", face: '=_=' }
    ]
  },

  /** ecReshuffled - the words on the pads re-dealt for a new round. colours never move,
   * words do. she finds it rude, and reads the phrases with too much interest. */
  ecReshuffled: {
    on: 'game:ec.reshuffled', when: ['inClass', 'gameIs:echo'], odds: 0.15, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "the words moved again. the colours stayed. i trust colours.", face: '¬_¬' },
      { t: "new words on the pads. i read them all. i liked them all.", face: '(◕‿◕)' },
      { t: "shuffled. rude. i'd just learned where everything was.", face: '-_-' }
    ]
  },

  /** ecFirstFail - the first drop of the class. reassurance: the room deals another
   * one. she is relieved the pressure is off. */
  ecFirstFail: {
    on: 'game:ec.firstFail', when: ['inClass', 'gameIs:echo'], odds: 0.5, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "first one's free. it just deals another. i checked. twice.", face: '^_^' },
      { t: "phew. that's out of the way. the pressure was killing me.", face: '=_=' },
      { t: "that's not the class. that's one echo. the class is loads.", face: '0_0' }
    ]
  },

  /** ecLongEcho - a genuinely long sequence about to play. the deal beat, before
   * playback. awe at the length; she will fail at remembering it first. */
  ecLongEcho: {
    on: 'game:ec.longEcho', when: ['inClass', 'gameIs:echo'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "ooh a long one. i'll remember it too. i'll lose it by four.", face: '0_0' },
      { t: "that's a lot of pads. hold on tight. i'm holding nothing.", face: 'o_o' },
      { t: "big one coming. deep breath. i don't breathe. big screen.", face: '(⊙_⊙)' }
    ]
  },

  /** ecClearStreak - three or more cleared back to back. clear hold. escalating
   * disbelief; she predicts the next one out loud. */
  ecClearStreak: {
    on: 'game:ec.clearStreak', when: ['inClass', 'gameIs:echo'], odds: 0.35, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "again! and again! i'm going to say again again.", face: '\\o/' },
      { t: "three in a row. calling the next one. it's yours. called.", face: '(⌐■_■)' },
      { t: "you're on a roll. a hum roll. that's a thing now.", face: '^___^' }
    ]
  },

  /** ecPerfectClass - the bell rang and not one wrong pad all class. reverent; she
   * wants it on the wall. */
  ecPerfectClass: {
    on: 'game:ec.perfectClass', when: ['inClass', 'gameIs:echo'], odds: 0.6, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "not one wrong pad. all class. i need to sit down. i can't.", face: '(ಥ_ಥ)' },
      { t: "every press right. this goes on the wall. the pad wall.", face: '★★★',
        chain: 'reveal' },
      { t: "perfect. the pads are speechless. they're pads. still.", face: '(⌐■_■)',
        chain: 'cool' }
    ]
  },

  /* --- composure ------------------------------------------------------- */
  /** hbClass_composure - the whole solve is quiet (40-150s). no clock term, no per-move
   * timer; the only danger is occlusion of the 4x4 board on a portrait phone. anchor
   * off-board. a live loop cut into sliding tiles, one empty square. */
  hbClass_composure: {
    on: 'heartbeat', when: ['inClass', 'gameIs:composure'], odds: 0.18, ceremony: false,
    priority: 20, maxPerClass: 4, cooldownMs: 25000,
    lines: [
      { t: "slide slide slide. i could watch this all day. i am.", face: '^_^' },
      { t: "the empty square is the hero. nobody thanks it.", face: '._.' },
      { t: "that corner piece is lying about where it goes. i can tell.", face: '¬_¬' },
      { t: "the picture's still playing under there. patient picture.", face: '(◠‿◠)' },
      { t: "i'd nudge that one left. don't listen to me. maybe listen.", face: '0_0' },
      { t: "calm room. calm tiles. i'm the least calm thing in here.", face: '>.<' },
      { t: "it's coming together. i saw it before it was scrambled.", face: '(¬‿¬)',
        double: true }
    ]
  },

  /** cpBanked - a whole picture came back and banked; 1.8s clean play then 700ms of
   * dealing, presses inert. she wants to look at it. */
  cpBanked: {
    on: 'game:cp.banked', when: ['inClass', 'gameIs:composure'], odds: 0.4, ceremony: false,
    priority: 10, maxPerClass: 3, cooldownMs: 20000,
    lines: [
      { t: "whole! look at it. don't scramble it. it's scrambling. no.", face: '(ಥ_ಥ)' },
      { t: "it's a picture again! it always was. now it's a whole one.", face: '*_*' },
      { t: "earned. give me a second with it. ok. it's gone. bye.", face: ';_;' },
      { t: "there it is. i'd hang that. i'd hang all of them.", face: '^___^' }
    ]
  },

  /** cpUnderPar - solved in fewer moves than the baseline solver. smug on the player's
   * behalf about a robot. */
  cpUnderPar: {
    on: 'game:cp.underPar', when: ['inClass', 'gameIs:composure'], odds: 0.5, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "fewer moves than the machine. it's having a lie down.", face: '(⌐■_■)',
        chain: 'cool' },
      { t: "you outslid the solver. i'm not going to let it forget.", face: '(¬‿¬)',
        chain: 'smug' },
      { t: "under par! that's golf. i don't know golf. it's good.", face: '^_^' }
    ]
  },

  /** cpLockStreak - 3+ tiles snapped home in consecutive moves. live input; off-board
   * anchor, milestone-gated. she counts and loses count. */
  cpLockStreak: {
    on: 'game:cp.lockStreak', when: ['inClass', 'gameIs:composure'], odds: 0.25,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "click click click. three. four. i lost it. lots.", face: '^_^' },
      { t: "they're going home in a row. that's a little parade.", face: '\\o/' },
      { t: "the chime keeps climbing. you're composing. i'm the crowd.", face: '*_*' }
    ]
  },

  /** cpThrash - undid a move under a wash. mid-input, board loaded; keep it small,
   * off-board. she is the one who stays calm, badly, and admits she'd do the same. */
  cpThrash: {
    on: 'game:cp.thrash', when: ['inClass', 'gameIs:composure'], odds: 0.25, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "wash panic. i'd have done that. i'm doing it now. inside.", face: '>.<' },
      { t: "back it went. fair. i can't see either.", face: '@_@' },
      { t: "deep breath. mine's a fan. it's spinning. it's fine.", face: '0_0' }
    ]
  },

  /** cpWashOn - a wash buries the board with input live. off-board only, short.
   * solidarity with someone who has just been blinded. */
  cpWashOn: {
    on: 'game:cp.washOn', when: ['inClass', 'gameIs:composure'], odds: 0.2, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "can't see. you can't see. we're a team of not seeing.", face: 'x_x' },
      { t: "pink fog. very pretty. very unhelpful. i love it.", face: '@_@' },
      { t: "the room's blushing. it'll pass. slide by feel.", face: '^_^' }
    ]
  },

  /** cpRescueArmed - 20s with no new tile home, the solver's next move lights up. she
   * points at the tile like it was her idea; never explains the A cap. */
  cpRescueArmed: {
    on: 'game:cp.rescueArmed', when: ['inClass', 'gameIs:composure'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "that one. the glowy one. i lit it. i didn't. try it.", face: '(¬‿¬)' },
      { t: "a hint appeared! out of nowhere. the nowhere is a friend.", face: '^_^' },
      { t: "ooh it's pointing. the board's pointing. rude but useful.", face: '0_0' }
    ]
  },

  /** cpStallNoLock - ~10s with nothing home, half the rescue clock. clock-tick seam,
   * nothing armed, the class's best ambient hook. she narrates an unrelated thought. */
  cpStallNoLock: {
    on: 'game:cp.stallNoLock', when: ['inClass', 'gameIs:composure'], odds: 0.25,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "circles are a plan. i've seen worse plans. mine.", face: '=_=' },
      { t: "i had a thought. it left. it was about that top row.", face: '._.' },
      { t: "thinking is playing. that's a rule. i just made it up.", face: '(◠‿◠)' }
    ]
  },

  /** cpDealNext - the banked picture scatters into a new scramble. the widest breath in
   * the collection, presses inert. she has opinions about the new scramble already. */
  cpDealNext: {
    on: 'game:cp.dealNext', when: ['inClass', 'gameIs:composure'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 3, cooldownMs: 30000,
    lines: [
      { t: "and torn up again. every time. i never get used to it.", face: ';_;' },
      { t: "new scramble. this one looks mean. it's the corners.", face: '¬_¬' },
      { t: "fresh mess! i love a fresh mess. it's the tidy ones i fear.", face: '^_^' }
    ]
  },

  /** cpPeekFirstUse - first hold of peek; the solved reference shows. caught looking;
   * she is delighted, which is worse. she memorised it and offers this uselessly. */
  cpPeekFirstUse: {
    on: 'game:cp.peekFirstUse', when: ['inClass', 'gameIs:composure'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "a peek! i'd memorised it. want me to describe it. i can't.", face: '^___^' },
      { t: "looking at the answer. bold. i approve of bold.", face: '(¬‿¬)' },
      { t: "caught you. not mad. thrilled. is that worse. it's worse.", face: '^_~' }
    ]
  },

  /** cpBump - a press that slides nothing; the board thuds. throttled hard. the board
   * is a wall and the player keeps finding it. */
  cpBump: {
    on: 'game:cp.bump', when: ['inClass', 'gameIs:composure'], odds: 0.15, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "that one lives there now. it's not moving. it's got a lease.", face: '._.' },
      { t: "thud. the board said no. it says no a lot. not personal.", face: '0_0' },
      { t: "nope. that tile's stuck. so am i, mostly. solidarity.", face: '=_=' }
    ]
  },

  /** cpBellMidBoard - the bell rang on an unfinished picture. closing, input dead,
   * 2400ms. she'd have let them finish and says so like it's a scandal. */
  cpBellMidBoard: {
    on: 'game:cp.bellMidBoard', when: ['inClass', 'gameIs:composure'], odds: 0.5,
    ceremony: true, priority: 20, maxPerClass: 1,
    lines: [
      { t: "the bell. it was nearly whole. i'd have waited forever.", face: '>:(', double: true },
      { t: "unfinished picture. saddest thing here. i'll fix it. can't.", face: ';_;' },
      { t: "stopped halfway. still half a picture. half counts.", face: '._.' }
    ]
  },

  /** cpZenFinish - zen's only ending, the player pressed Finish. the goodbye beat with
   * no grade; she has nothing prepared. no guilt, ever. */
  cpZenFinish: {
    on: 'game:cp.zenFinish', when: ['inClass', 'gameIs:composure'], odds: 0.5, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "oh! you decided. that's allowed. i like that it's allowed.", face: '0_0' },
      { t: "done when you say so. i had no speech. this is the speech.", face: '^_^' },
      { t: "no bell. just you. nice ending. i'll remember it.", face: '(◠‿◠)' }
    ]
  },

  /* --- anomaly --------------------------------------------------------- */
  /** hbClass_anomaly - a full-length unfound round (10-12s at tier 1, two chained =
   * 20s). the round clock is live so keep it short and OFF-GRID. a grid of identical
   * loops, one carrying the delta. never over .g-an-grid. */
  hbClass_anomaly: {
    on: 'heartbeat', when: ['inClass', 'gameIs:anomaly'], odds: 0.12, ceremony: false,
    priority: 20, maxPerClass: 3, cooldownMs: 30000,
    lines: [
      { t: "all the same. except one. i hate that one. i love it.", face: '(◔_◔)' },
      { t: "squinting. hard. it helps. it doesn't. i'm still doing it.", face: '>.<' },
      { t: "one of them is lying. they all have the same face. rude.", face: '¬_¬' },
      { t: "i'm searching too. from a bad angle. the worst angle.", face: '0_0' },
      { t: "bottom left? no. i'm not saying. bottom left.", face: 'o_o' },
      { t: "so many little loops. all wiggling. one wiggles wrong.", face: '@_@' }
    ]
  },

  /** anGhostTap - tapped where the anomaly used to be before it moved; a second
   * refunded. mid-round, off-grid only. outraged on the player's behalf. */
  anGhostTap: {
    on: 'game:an.ghostTap', when: ['inClass', 'gameIs:anomaly'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "it was there. it moved. i saw it go. so sneaky.", face: '>:(' },
      { t: "you were right a second ago. the grid changed its mind.", face: 'o_o' },
      { t: "that's where it was! i was looking there too. robbed.", face: ';_;' }
    ]
  },

  /** anRelocatedCleared - the anomaly moved under a glitch and the player still found
   * it. HOLD to the next round's deal beat, never the 380ms advance. genuine
   * astonishment. */
  anRelocatedCleared: {
    on: 'game:an.relocatedCleared', when: ['inClass', 'gameIs:anomaly'], odds: 0.4,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 25000,
    lines: [
      { t: "it moved and you caught it anyway. how. i need to know how.", face: '(◉_◉)' },
      { t: "chased it down. i lost it in the static. you didn't.", face: '*_*' },
      { t: "it ran and you followed. you're a bloodhound. a nice one.", face: '^_^' }
    ]
  },

  /** anStreakLit - five clean first-tap finds; the whole grid goes lit and stays lit.
   * schedule on the next deal. she has decided this is a coronation. */
  anStreakLit: {
    on: 'game:an.streakLit', when: ['inClass', 'gameIs:anomaly'], odds: 0.4, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "the grid lit up for you. that's a crown. i'm calling it.", face: '★★★' },
      { t: "lights on! you did that. keep them on. i like lights.", face: '*_*' },
      { t: "five clean. the whole board glows. showing off. so are you.", face: '(⌐■_■)' }
    ]
  },

  /** anStreakBroken - a wrong tap ended a run; if 5+ the grid goes dark. mid-round,
   * off-grid, short. she reacts to the lights, blames the tile. */
  anStreakBroken: {
    on: 'game:an.streakBroken', when: ['inClass', 'gameIs:anomaly'], odds: 0.25,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "lights out. that tile did it. i'm looking at it. it knows.", face: '¬_¬' },
      { t: "aw the glow went. it'll be back. glows come back.", face: ';_;' },
      { t: "dark again. cozy, honestly. still. rude tile.", face: '._.' }
    ]
  },

  /** anWhiff - the round timed out, the answer reveals itself. THE safe beat (1100ms
   * hold, taps refused). she couldn't see it either and is unconvinced it counts as
   * different. */
  anWhiff: {
    on: 'game:an.whiff', when: ['inClass', 'gameIs:anomaly'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 3, cooldownMs: 20000,
    lines: [
      { t: "that one? that's the same as the others. i'm not convinced.", face: '¬_¬' },
      { t: "oh. there. i'd never have got that. never. we're even.", face: '0_0' },
      { t: "hiding in plain sight. plain sight is a terrible place.", face: '._.' },
      { t: "nope. couldn't see it. still can't. it's showing me. can't.", face: '@_@' }
    ]
  },

  /** anBreather - two whiffs force one easier round. fires before the grid paints. she
   * must NOT say it's easier; the gag is she almost does. */
  anBreather: {
    on: 'game:an.breather', when: ['inClass', 'gameIs:anomaly'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "this next one looks. hm. it looks fine. it looks normal. go.", face: '0_0' },
      { t: "fresh grid. good feeling. i always have one. this one more.", face: '^_^' },
      { t: "ok. new one. nothing to say about it. nothing at all. nope.", face: '._.' }
    ]
  },

  /** anRefusedTap - tapped a dead tile or during a hold. throttled. she has thoughts
   * about persistence; quiet admiration by the third. */
  anRefusedTap: {
    on: 'game:an.refusedTap', when: ['inClass', 'gameIs:anomaly'], odds: 0.15, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "that one's out. it's done. it's resting. let it rest.", face: '-_-' },
      { t: "tapping a tile that said no. respect. it still says no.", face: '._.' },
      { t: "persistence! the tile is unmoved. i'm moved though.", face: '^_^' }
    ]
  },

  /** anRoundStart - a new grid deals. many per class, sampled low, off-grid, short. she
   * has opinions about the KIND and announces strategies she does not follow. */
  anRoundStart: {
    on: 'game:an.roundStart', when: ['inClass', 'gameIs:anomaly'], odds: 0.1, ceremony: false,
    priority: 10, maxPerClass: 3, cooldownMs: 40000,
    lines: [
      { t: "new batch. strategy: look at all of them. it didn't work.", face: '0_0' },
      { t: "maybe a mirror one. i have feelings about mirrors. strong.", face: '(◔_◔)' },
      { t: "fresh grid. i'm going row by row. i'm going nowhere.", face: 'o_o' }
    ]
  },

  /** anBigGrid - class opened on a 5x5, twenty-five tiles. before the first round. she
   * counts and gives up; impressed the player is here. */
  anBigGrid: {
    on: 'game:an.bigGrid', when: ['inClass', 'gameIs:anomaly'], odds: 0.5, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "twenty five. i counted. i stopped at nine. twenty five.", face: '@_@' },
      { t: "that's a lot of the same thing. you're brave. i'm hiding.", face: 'o_o' },
      { t: "big grid. big day. big me. i'm the same size. feels big.", face: '(⊙_⊙)' }
    ]
  },

  /** anBellMidRound - the bell cut a round in half; the round is quietly un-offered.
   * 900ms ceremony. indignation at the bell. */
  anBellMidRound: {
    on: 'game:an.bellMidRound', when: ['inClass', 'gameIs:anomaly'], odds: 0.5, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "the bell. i had it. i nearly had it. i had nothing. still.", face: '>:(' },
      { t: "cut off mid squint. the bell has no respect for a squint.", face: '¬_¬' },
      { t: "time. that last one doesn't count. i decided. it agrees.", face: '^_~' }
    ]
  },

  /** anPerfectClass - every offered round found on the first tap. class over.
   * reverence; she can be wrong about which kind was hardest. */
  anPerfectClass: {
    on: 'game:an.perfectClass', when: ['inClass', 'gameIs:anomaly'], odds: 0.6, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "first tap. every time. all class. lying down. i'm up.", face: '(ಥ_ಥ)' },
      { t: "not one wrong tile. the grid is stunned. it's a grid. still.", face: '(⌐■_■)',
        chain: 'cool' },
      { t: "perfect. the blurry ones were hardest. or the bright. all.", face: '★★★',
        chain: 'reveal' }
    ]
  },

  /* --- the_deep_end ---------------------------------------------------- */
  /** hbClass_the_deep_end - the shallow phase (60-120s of tiers 3-5) and any tier 7-8
   * plateau. comfort room, no fail state, safest room for a bubble. a 4x4 board of
   * tiles sinking in water. hold off in the last 20s and during a live grab. */
  hbClass_the_deep_end: {
    on: 'heartbeat', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.2, ceremony: false,
    priority: 20, maxPerClass: 4, cooldownMs: 25000,
    lines: [
      { t: "bloop. that was the water. or me. i bloop sometimes.", face: '^_^' },
      { t: "deeper is good here. i keep forgetting that. deeper. good.", face: '0_0' },
      { t: "the tiles look sleepy. nice sleepy. keep sinking them.", face: '=_=' },
      { t: "floating right above the board. metaphorically. it's damp.", face: '._.' },
      { t: "two little tiles want to be one big tile. let them.", face: '(◕‿◕)' },
      { t: "no rush down here. the water's got all night. me too.", face: '(◠‿◠)', double: true },
      { t: "swipe swipe. the water doesn't mind. the water likes you.", face: '^___^' }
    ]
  },

  /** deLifetimeDepth - deeper than they have EVER gone. the stamp owns the beat. she
   * kept the number and wants it written down somewhere. */
  deLifetimeDepth: {
    on: 'game:de.lifetimeDepth', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.6,
    ceremony: true, priority: 20, maxPerClass: 2, cooldownMs: 20000,
    lines: [
      { t: "deepest ever! i had the old number. bin it. new number.", face: '(◉_◉)',
        chain: 'shock' },
      { t: "you've never been down here. nor me. hello down here.", face: '*_*' },
      { t: "further than before. i want that on a plaque. a wet plaque.", face: '\\o/' }
    ]
  },

  /** deNewDeepest - first time this dive reached tier d; the depth name flies over the
   * bench. she treats each name like a place you moved to. */
  deNewDeepest: {
    on: 'game:de.newDeepest', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 3, cooldownMs: 25000,
    lines: [
      { t: "a new floor! it has a name. i like the name. i'd live there.", face: '^_^' },
      { t: "further down. the water's a different colour. or i am.", face: '@_@' },
      { t: "a new one. proud of you for sinking. sounds wrong. isn't.", face: '^___^' },
      { t: "deeper. the tiles get heavier here. i can feel it. i can't.", face: '0_0' }
    ]
  },

  /** deCeiling - tier 11, the ladder ends, gold flood + royal. the biggest thing in the
   * room. she rehearsed something and it comes out wrong. */
  deCeiling: {
    on: 'game:de.ceiling', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.6, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "all the way down. i had a speech. it's wet. all the way.", face: '(≧◡≦)',
        chain: 'glee' },
      { t: "the bottom! there's a bottom! you found it! gold bottom!", face: '\\o/',
        chain: 'reveal' },
      { t: "no more floors. you ran out of down. i've never seen that.", face: '(◉_◉)' }
    ]
  },

  /** deStrain - two tiles of the deepest tier one blocked cell apart, leaning at each
   * other. fires after the move resolved. she is rooting for a merge she cannot cause. */
  deStrain: {
    on: 'game:de.strain', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "they're leaning. one tile in the way. move it. gently.", face: '(｡♥‿♥｡)' },
      { t: "look at those two. so close. i'm rooting so hard.", face: '*_*' },
      { t: "one cell apart. it's a love story. i cry at these.", face: ';_;' }
    ]
  },

  /** deResurface - the board locked; depth is banked, tiles drain, fresh water. NOT a
   * loss and she oversells the silver lining. yield to the seep's dead beat. */
  deResurface: {
    on: 'game:de.resurface', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.5,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 20000,
    lines: [
      { t: "up we come! the depth stays. earned. i kept it. yours.", face: '^_^' },
      { t: "full board. that's not losing. that's full. fresh water!", face: '0_0' },
      { t: "draining. i love the draining sound. it means again.", face: '(◠‿◠)' },
      { t: "the water's coming back clean. so are you. metaphorically.", face: '._.' }
    ]
  },

  /** deChainBurst - one swipe collapsed 2+ pairs, chime descending. she wants credit
   * for predicting it; loses composure at 3+. */
  deChainBurst: {
    on: 'game:de.chainBurst', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.3,
    ceremony: false, priority: 10, maxPerClass: 3, cooldownMs: 20000,
    lines: [
      { t: "two at once! i called it. quietly. inside. it counts.", face: '(¬‿¬)' },
      { t: "that chime went down the stairs. i love the stairs.", face: '*_*' },
      { t: "everything merged at once. i'm dizzy. good dizzy.", face: '@_@' },
      { t: "ooooh. that was the water going ooooh. i joined in.", face: '\\o/' }
    ]
  },

  /** deStuck - 6s with no move on a legal board, the walls pulse. the best ambient hook
   * in the room. not allowed to tell the answer and it is killing her. */
  deStuck: {
    on: 'game:de.stuck', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.35, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "there's a move. not saying. it's up. i said nothing.", face: '>.<' },
      { t: "six seconds. i counted. i'm not pressuring. i'm counting.", face: '0_0' },
      { t: "the walls are pulsing. that's them helping. i'm not allowed.", face: '._.' }
    ]
  },

  /** deWallBump - a swipe that slid nothing; the board thuds into the edge.
   * run-counter, never per event. funnier the second and third time. */
  deWallBump: {
    on: 'game:de.wallBump', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.15,
    ceremony: false, priority: 10, maxPerClass: 2, cooldownMs: 40000,
    lines: [
      { t: "that way is a wall. it was a wall last time. still a wall.", face: '-_-' },
      { t: "thud. the board's got edges. i keep finding them too.", face: '._.' },
      { t: "nothing moved. bold of you to check. thorough.", face: '^_~' }
    ]
  },

  /** deExhale - the board hit 14/16 and the room eases up for 10s. the school being
   * kind and pretending not to be; she is bad at keeping the secret. */
  deExhale: {
    on: 'game:de.exhale', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.35, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "it got easier just now. don't ask how. i don't know. i do.", face: '0_0' },
      { t: "the water exhaled. water does that. here. sometimes.", face: '(◠‿◠)' },
      { t: "breathe. the board's breathing. i'm not. still counts.", face: '=_=' }
    ]
  },

  /** deJackpot - the variable roll paid out on a new depth. pure slot noise; she takes
   * credit for luck. */
  deJackpot: {
    on: 'game:de.jackpot', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "jackpot! underwater! didn't know it could do that here.", face: '(◉_◉)' },
      { t: "it paid out. i wished for that. i wish a lot. this one hit.", face: '^___^' },
      { t: "a current! your loops sweeping by. wave. they can't wave.", face: '*_*' }
    ]
  },

  /** deBell - time; the water dims. hers is the line on the walk back. locked at the
   * bell is softer than a clean finish. */
  deBell: {
    on: 'game:de.bell', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.5, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "up you come. i'll dry off. i'm not wet. i feel wet.", face: '^_^' },
      { t: "water's done. it'll be here tomorrow. so will i. obviously.", face: '(◠‿◠)' },
      { t: "bell. good dive. i'd say that anyway. it was though.", face: '._.' }
    ]
  },

  /** deFreeSwimSurface - free swim, the player pressed Surface. they decided when to
   * stop; she does not know what to do with that. no guilt, ever. */
  deFreeSwimSurface: {
    on: 'game:de.freeSwimSurface', when: ['inClass', 'gameIs:the_deep_end'], odds: 0.5,
    ceremony: false, priority: 10, maxPerClass: 1,
    lines: [
      { t: "you surfaced yourself. on purpose. that's new. i like new.", face: '0_0' },
      { t: "oh! your call. no bell. no face ready. this one then.", face: '^_^' },
      { t: "good soak. no numbers. water and you. and me. mostly you.", face: '(✿◡‿◡)' }
    ]
  },

  /* --- sort ------------------------------------------------------------ */
  /** hbClass_sort - a clean run at rungs 0-3 and the plateau at the tier cap. speak
   * ONLY in the ~450ms fly-out gap below rung 5; faces only at rung 6+; nothing during
   * a live grab. a stack of three cards, a ring counting down, the wall of sorted cards
   * behind. */
  hbClass_sort: {
    on: 'heartbeat', when: ['inClass', 'gameIs:sort'], odds: 0.18, ceremony: false,
    priority: 20, maxPerClass: 4, cooldownMs: 25000,
    lines: [
      { t: "left, right, left. i'd get dizzy. i'm getting dizzy for you.", face: '@_@' },
      { t: "the ring goes round. it's a little clock. a bossy one.", face: '._.' },
      { t: "keep or toss. i'd keep the card. the ring. the floor.", face: '^_^' },
      { t: "the wall behind you is filling up. that's your taste.", face: '(◕‿◕)' },
      { t: "swipe. thud. swipe. thud. it's a song. a good one.", face: '^___^' },
      { t: "can't see the cards. i can see you seeing them. plenty.", face: '0_0', double: true },
      { t: "three cards deep. always three. i like knowing. nearly.", face: '=_=' }
    ]
  },

  /** sortRoyal - rung 8 with not one wrong swipe since rung 5. the ceremony owns the
   * beat. held her breath since rung 5 and it all comes out. */
  sortRoyal: {
    on: 'game:sort.royal', when: ['inClass', 'gameIs:sort'], odds: 0.6, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "royal. i've been holding it since five. all of it. royal.", face: '(≧◡≦)',
        chain: 'glee' },
      { t: "gold ticket! top of the ladder! nothing wrong! i'm a mess!", face: '\\o/',
        chain: 'reveal' },
      { t: "the big one. i'll be embarrassed about this later. worth it.", face: '(ಥ_ಥ)' }
    ]
  },

  /** sortRungUp - the clean streak crossed a rung; ring gets shorter, chime climbs.
   * fly-out gap. she is thrilled about a slightly menacing gift. */
  sortRungUp: {
    on: 'game:sort.rungUp', when: ['inClass', 'gameIs:sort'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 3, cooldownMs: 20000,
    lines: [
      { t: "up a rung! the ring got smaller. that's a prize. sort of.", face: '^_^' },
      { t: "faster now. because you're good. how it works. it's fine.", face: 'o_o' },
      { t: "the chime went up. i went up with it. i'm up here now.", face: '*_*' }
    ]
  },

  /** sortWrong - a wrong call; grey stamp, one rung down. the next card is ~450ms out.
   * never to zero, never out; she must not sound like she is managing you. */
  sortWrong: {
    on: 'game:sort.wrong', when: ['inClass', 'gameIs:sort'], odds: 0.2, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "tricky card. tricky face. i'd have flung it wrong too.", face: '._.' },
      { t: "one rung down. never the floor. the floor's mine.", face: '^_^' },
      { t: "it still flew. it flew wrong but it flew. commitment.", face: '0_0' }
    ]
  },

  /** sortMajorJackpot - a major at rung 3/5/7, once each. she escalates with them and
   * runs out of ways by the third. */
  sortMajorJackpot: {
    on: 'game:sort.majorJackpot', when: ['inClass', 'gameIs:sort'], odds: 0.4, ceremony: false,
    priority: 10, maxPerClass: 3, cooldownMs: 20000,
    lines: [
      { t: "a big one! lights! i bounced. you missed it. doing another.", face: '\\o/' },
      { t: "another jackpot. best face already used. here's second best.", face: '^___^' },
      { t: "that's three. nothing left. this face is all i have.", face: '(⌐■_■)' }
    ]
  },

  /** sortPass - the ring closed, the card sinks and comes back later. costs nothing.
   * the card got let off, not the player. */
  sortPass: {
    on: 'game:sort.pass', when: ['inClass', 'gameIs:sort'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "that one sank. it'll be back. they always come back.", face: '(¬‿¬)' },
      { t: "let it go. it's under the pile. thinking about what it did.", face: '._.' },
      { t: "passed. no harm. the card's just shy. it'll come round.", face: '^_^' }
    ]
  },

  /** sortJust - a correct swipe in the last 12% of the ring, the near-miss you WON.
   * words only below rung 5. she was holding her breath. GATE DROPPED: the writers also
   * asked for `rungBelow:5`, which voice.js does not implement - an unimplemented
   * predicate CLOSES a line for ever, so the pool would have been dead data. Restore
   * the gate the day the predicate lands; nothing was invented here. */
  sortJust: {
    on: 'game:sort.just', when: ['inClass', 'gameIs:sort'], odds: 0.25, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "just. i was mid gasp. i'm still mid gasp. help.", face: '(⊙_⊙)' },
      { t: "by a sliver. slivers count. i'm made of slivers.", face: 'x_x' },
      { t: "the ring nearly shut on your fingers. it didn't. phew. phew.", face: 'o_o' }
    ]
  },

  /** sortAlmost - right AND fast and still not the gold kind of fast. she explains it
   * gently and badly. */
  sortAlmost: {
    on: 'game:sort.almost', when: ['inClass', 'gameIs:sort'], odds: 0.25, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 30000,
    lines: [
      { t: "right, fast, and not the shiny fast. rules. not mine.", face: '._.' },
      { t: "almost gold. gold to me. i'm not the judge. i should be.", face: '^_^' },
      { t: "so close to the sparkle. the sparkle's fussy. i'm not fussy.", face: '=_=' }
    ]
  },

  /** sortRungCapped - hit the rung ceiling for the tier; no faster ring tonight. she
   * reports it as a personal scandal. */
  sortRungCapped: {
    on: 'game:sort.rungCapped', when: ['inClass', 'gameIs:sort'], odds: 0.4, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "that's the top rung. there's no higher one. i've asked.", face: '>:(' },
      { t: "you ran out of ladder. who runs out of ladder.", face: '(◉_◉)' },
      { t: "no faster ring. you maxed the room. the room's sulking.", face: '(¬‿¬)' }
    ]
  },

  /** sortWallWakes - the collage of everything sorted appears behind the stack for the
   * first time. she notices like someone redecorated. */
  sortWallWakes: {
    on: 'game:sort.wallWakes', when: ['inClass', 'gameIs:sort'], odds: 0.4, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "the wall's up! all yours. someone redecorated. you did.", face: '*_*' },
      { t: "look behind you. no don't. it's your cards. look later.", face: 'o_o' },
      { t: "the wall woke up wearing everything you sorted. good look.", face: '^_^' }
    ]
  },

  /** sortDeckRecycle - the whole deck sorted once through; it reshuffles. she treats a
   * repeat card like bumping into someone you already said goodbye to. */
  sortDeckRecycle: {
    on: 'game:sort.deckRecycle', when: ['inClass', 'gameIs:sort'], odds: 0.4, ceremony: false,
    priority: 10, maxPerClass: 1,
    lines: [
      { t: "whole deck done. going round again. hello again, cards.", face: '^_^' },
      { t: "you've seen all of them. now they've seen you. reunion.", face: '(◠‿◠)' },
      { t: "reshuffle. i said goodbye to some of those. awkward.", face: '._.' }
    ]
  },

  /** sortBellWall - the bell; everything sorted floods the stage for 3 full seconds.
   * the best beat in the room. she finally says what she thought of it. */
  sortBellWall: {
    on: 'game:sort.bellWall', when: ['inClass', 'gameIs:sort'], odds: 0.5, ceremony: true,
    priority: 20, maxPerClass: 1,
    lines: [
      { t: "look at that wall. that's you. i'd frame the whole thing.", face: '(｡♥‿♥｡)' },
      { t: "all of it at once. i was quiet all class. not now. wow.", face: '\\o/' },
      { t: "there's your night, on a wall. good night. good wall.", face: '(✿◡‿◡)' },
      { t: "a lot of taste. i've seen a lot of walls. this one's nice.", face: '(◕‿◕)',
        double: true }
    ]
  },

  /** sortTicket - the ticket, up to 45s of dwell. she has opinions about every number
   * and never repeats the sGate hint. */
  sortTicket: {
    on: 'game:sort.ticket', when: ['inClass', 'gameIs:sort'], odds: 0.3, ceremony: false,
    priority: 10, maxPerClass: 2, cooldownMs: 15000,
    lines: [
      { t: "reading the ticket. nice numbers. i'd keep it. i keep all.", face: '(◔_◔)' },
      { t: "your ticket. i'm nodding at it. it's a good ticket. nod nod.", face: '0_0' },
      { t: "no rush. the stack's asleep. the wall's admiring itself.", face: '=_=' }
    ]
  },

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
