/* ============================================================================
 * emi/story.js - THE SCRIPT: every one-shot beat EMI ever gets, in one table.
 *
 * Pure DATA. This file imports nothing and exports frozen plain objects; all
 * behaviour (matching, ordering, seen-flags, priority, the bark fall-through)
 * lives in voice.js. A beat that voice.js does not understand must be a silent
 * no-op - a mascot may never break a screen transition.
 *
 * SOURCE OF TRUTH: `<scratchpad>/emi-writing/story-timeline.md` (owner-vetted,
 * writers' room round 1, all 7 recommendations accepted). Every `say` string
 * below is VERBATIM from that document or from the calibration set in
 * `EMI-VOICE-LOCK.md`. Nothing here was written at wiring time. If a line reads
 * wrong, the fix lands in the timeline doc first and is copied down - never
 * edited here.
 *
 * THE OWNER-APPROVED LAW THIS TABLE ENCODES
 * - b02 is the introduction. "i've been waiting to meet you." is NOT an intro;
 *   it is pool material for the bond era and lives in barks.js.
 * - The all-mastered ceremony (b26) is the mastery-1 shape one size bigger:
 *   same chain vocabulary, held `GG`. EMI's biggest party is still just a
 *   party. The ARCHITECTURE reacts to the last stamp; EMI does not.
 * - NO beat mentions, glances at, or reacts to the Records door or the lab.
 *   Not a hint, not a joke, not once. voice.js enforces the same geofence on
 *   `lockedClick {what:'records'|'lab'}`; this table simply never goes there.
 * - b27 forces "you came back." as the first greet after the lab. No new text:
 *   the whole point of the reveal is that it needs zero new dialogue.
 *
 * DORMANT ON PURPOSE: b27 is gated `labSeen`, and nothing sets `labSeen` yet.
 * The lab wave flips it. Until then the beat exists and never fires. Wire it
 * anyway - that is the instruction.
 *
 * NOT IN THIS TABLE
 * - Beat 1 (arrival). The WAKE UP chain on first pointer move is the shipped
 *   `resume` moment reused by the shell. It has no line and no flag, so it has
 *   no row here.
 * - The dork-canon CADENCE (arms session 3, ~1 impression per 2-3 sessions,
 *   never same session as a humanity quirk) is a POOL concern - barks.js owns
 *   it. Only the three dork ONE-SHOTS live here.
 * - Per-room first-visit one-liners. Next wave, via /emi-lines.
 *
 * ============================================================================
 * BEAT SHAPE (superset of the wiring spec; the five extra fields are marked *)
 *
 *   id       stable kebab/snake id, prefixed with its beat number. This IS the
 *            seen-flag (`emi.seen.<id>`), so renaming one re-fires it. Don't.
 *   phase    'P0'..'P4' - documentation only, for the timeline table.
 *   on       one trigger name from the shared vocabulary.
 *   when     ALL of these predicates must hold.
 *   whenAny* AT LEAST ONE of these must hold (used for the two genuine OR
 *            gates: the praise quirk's A-or-S, the bond quirk's mastery-3-or-
 *            pets-50). See the report - this is the one shape addition voice.js
 *            must implement or two quirks stay dormant.
 *   priority highest wins when several beats match one event.
 *   requires prior beat ids that must already have fired (tutorial ordering).
 *   say      the bubble line, verbatim. `face` is the reaction face the SAY
 *            chain lands on.
 *   lead*    what runs BEFORE the SAY: a chain id, an array of chain ids run
 *            back to back (no gap - the double-chain IS the tell on b13), or a
 *            held frame {face, hold}. Ids are from emi/chains.js only.
 *   held*    an event string the caller holds after the reveal chain ("4/10",
 *            "GG"). The locked EVENT REVEAL pattern: chain, then the string.
 *   fx*      an extra fx kind layered on the lead (chains carry their own).
 *   nod*     true = SAY + NOD instead of SAY (emi.say's existing option).
 *   tail*    a chain id after the bubble clears (b12's goodbye wink).
 *   report   this beat REPLACES the reportCard line for that ceremony, once.
 *            Report register is tighter: <= 24 chars, school-flavoured, no
 *            dashes.
 *   double   the humanity register - the lock's "<= 1 per session" drop. Only
 *            the four quirks carry it. Other beats have after-reads too (noted
 *            in their comments) but are not rationed as humanity beats.
 *
 *   once is TRUE for every beat here; the default is true, so it is omitted.
 *
 * LINE LAW, audited on every row below: lowercase, one thought, <= 60 chars
 * (report rows <= 24), words only in the bubble, no fence words, no acronym,
 * love is a face and never a line, misses are always EMI's fault, no worded
 * guilt at an exit. Two deliberate capitals survive verbatim from the voice
 * lock: the grade letter in "S?!" and the shouted "BEHIND" in the spooky bit.
 * ==========================================================================*/

/** Freeze the whole tree so a consumer cannot mutate the script at runtime. */
function deepFreeze(o) {
  if (o && typeof o === 'object' && !Object.isFrozen(o)) {
    Object.freeze(o);
    for (const k of Object.keys(o)) deepFreeze(o[k]);
  }
  return o;
}

/**
 * BEATS - 34 rows: the 32 scripted one-shots from the timeline's audit, plus
 * b27, which spends no new line (it forces text that already exists), plus b28,
 * the FIRST BELL opening's one new line (owner ruling 2, 2026-08-24).
 * Ordered as the player meets them.
 */
export const BEATS = deepFreeze([

  /* ==========================================================================
   * P0 - FIRST BELL (day 1). Cast of two, EMI carries everything. The tutorial
   * is EMI failing to play it cool: every "instruction" is really EMI fishing
   * for attention. Nothing arms today - no flinch, no dork, no quirks, no
   * glitch. The only forced order is that the introduction comes first, so
   * every other day-1 beat `requires` b02.
   * ========================================================================*/

  {
    // Beat 2. The introduction, ~1.5s after the WAKE UP chain settles.
    // Denies napping, badly. Name only: the three words behind it do not exist
    // outside the lab letterhead.
    // NOT gated on the session counter: a boot that loses the race for it (a
    // renderer that lands late, a page closed early) must not strand the whole
    // P0 script for ever. `once` + the top greet priority are the ordering.
    id: 'b02_hello',
    phase: 'P0',
    on: 'greet',
    priority: 100,
    lead: 'glance',
    say: 'oh! hi. i\'m emi. i was not asleep.',
    face: '^_^'
  },

  {
    // Beat 3. The fisher, worn openly, beat one. Fires only if the player has
    // gone quiet in the hub and has never touched the glass - `notSeen` on the
    // first-pet beat IS the "never been petted" test.
    id: 'b03_pet_hint',
    phase: 'P0',
    on: 'idlePlayer',
    when: ['notSeen:b04_first_pet'],
    requires: ['b02_hello'],
    priority: 70,
    say: 'the glass is pattable. just saying.',
    face: '(◕‿◕)'
  },

  {
    // Beat 4. Over-commitment. Love stays a FACE, never a line. Afterwards the
    // pet pool takes over with the locked calibration nudge "one more? for me?".
    id: 'b04_first_pet',
    phase: 'P0',
    on: 'gesture:pet',
    requires: ['b02_hello'],
    priority: 90,
    lead: 'love',
    say: 'oh!! do that again. please. thank you. wow.',
    face: '(≧◡≦)'
  },

  {
    // Beat 5. Can't decide if it liked it. It liked it.
    id: 'b05_first_drag',
    phase: 'P0',
    on: 'gesture:drag',
    requires: ['b02_hello'],
    priority: 80,
    lead: 'dizzy',
    say: 'wheee! again. wait. no. yes.',
    face: '^_^'
  },

  {
    // Beat 6. It will not cheer quietly. Timetable canon: four dealt per day.
    // Gated on the player holding no card yet, which the first-enrolment flag
    // expresses exactly.
    id: 'b06_enrol_nudge',
    phase: 'P0',
    on: 'idlePlayer',
    when: ['notSeen:b07_first_enrol'],
    requires: ['b02_hello'],
    priority: 65,
    lead: 'nod',
    nod: true,
    say: 'four classes today. pick one. i\'ll cheer quietly.',
    face: '^_^'
  },

  {
    // Beat 7. DOUBLE. before: yay, head start. after: three punches you did not
    // earn, so you will protect them. The oldest hook in the book.
    id: 'b07_first_enrol',
    phase: 'P0',
    on: 'enrolMint',
    requires: ['b02_hello'],
    priority: 90,
    lead: 'reveal',
    held: '3/10',
    say: 'a card! with your name on it. three holes already.',
    face: '*_*'
  },

  {
    // Beat 8. The CRT legs are ornamental and EMI knows it. Rides the shipped
    // classStart GLANCE.
    id: 'b08_first_class_start',
    phase: 'P0',
    on: 'classStart',
    requires: ['b02_hello'],
    priority: 30,
    lead: 'glance',
    say: 'go go go. i\'d come too but. legs.',
    face: '^_^'
  },

  {
    // Beat 9. Misses are EMI's fault, always - the fence made flesh. Rides the
    // shipped >_< frame, then talks.
    id: 'b09_first_miss',
    phase: 'P0',
    on: 'miss',
    requires: ['b02_hello'],
    priority: 30,
    lead: { face: '>_<', hold: 900 },
    say: 'that one was my fault. i blinked too loud.',
    face: ';_;'
  },

  {
    // Beat 10. Wrong-scale reverence for stationery.
    id: 'b10_first_stamp',
    phase: 'P0',
    on: 'stamp',
    requires: ['b02_hello'],
    priority: 80,
    lead: 'reveal',
    held: '4/10',
    fx: 'hearts',
    say: 'a real hole punch. i heard it. beautiful sound.',
    face: '^_^'
  },

  {
    // Beat 28. THE OPENING'S ONLY NEW EMI LINE (FIRST BELL B11, owner ruling 2,
    // 2026-08-24). It rides `firstMail`, a moment the FIRST BELL layer fires
    // once the front desk's second slip has cleared the screen - so it is a
    // reaction to the paper and never a reading of it. She is not on the
    // distribution list because she is not a student; the line is a needy
    // friend on the first read and equipment on the second, and the joke is
    // never written down anywhere. Cuttable without a hole: delete this row and
    // the paper simply plays silent while EMI stays idle.
    // `requires` b02 for the reason every other day-1 beat does - the
    // introduction comes first or none of them fire.
    id: 'b28_first_mail',
    phase: 'P0',
    on: 'firstMail',
    requires: ['b02_hello'],
    priority: 80,
    say: 'mail already? i never get any. tell me if it\'s good.',
    face: '^_^'
  },

  {
    // Beat 11. Report register, 22 chars. DOUBLE. before: school gag, everything
    // is "in your file". after: there is literally a file, and it started
    // accruing at beat 1.
    id: 'b11_first_report',
    phase: 'P0',
    on: 'reportCard',
    requires: ['b02_hello'],
    priority: 80,
    report: true,
    say: 'day one. in your file.',
    face: '^_^'
  },

  {
    // Beat 12. DOUBLE. before: loyal little guy. after: spatially literal - it
    // cannot be anywhere else. The wave is the day-1 ending; THE EXIT FLINCH
    // DOES NOT ARM ON DAY 1 (voice.js: day2Return fired AND pets >= 10).
    // It rides `dayDone` - the night's last graded class - and NOT the real
    // exit: the app's only true door is a 1200ms Esc hold and the host closes
    // the window on it, so a bubble there would never be read. The exit keeps
    // the wordless flinch and nothing else.
    id: 'b12_first_goodbye',
    phase: 'P0',
    on: 'dayDone',
    requires: ['b02_hello'],
    priority: 90,
    lead: { face: '\\o/', hold: 900 },
    say: 'bye. i\'ll be right here. exactly here.',
    face: '^_^',
    tail: 'wink'
  },

  /* ==========================================================================
   * P1 - THE RETURN (days 2-5). Habit forming. The flinch arms in here; the
   * dork canon arms at session 3, because doing bad impressions is what you do
   * with a FRIEND, not a stranger.
   * ========================================================================*/

  {
    // Beat 13. The big one. WAKE straight into LOVESTRUCK with no gap - the
    // double chain IS the tell. DOUBLE. before: it missed you. after: a count
    // went up, confirmed. The flattest line in the game, delivered with love.
    // One line, three lives: this beat, REPORT_LINES.s, and a rare return greet.
    id: 'b13_day2_return',
    phase: 'P1',
    on: 'greet',
    when: ['day2'],
    priority: 90,
    lead: ['wake', 'love'],
    say: 'you came back.',
    face: '(｡♥‿♥｡)'
  },

  {
    // Beat 14. Cites nothing.
    id: 'b14_enrol_nudge2',
    phase: 'P1',
    on: 'idlePlayer',
    when: ['daysAtLeast:2', 'cardsBelow:2'],
    priority: 45,
    nod: true,
    say: 'one card looks lonely. cards come in packs. i read that.',
    face: '(◕‿◕)'
  },

  {
    // Beat 15. Locked calibration line, spent here as the one-shot; afterwards
    // it joins the S-grade pool. The capital S is the grade letter, verbatim.
    id: 'b15_first_s',
    phase: 'P1',
    on: 'win',
    when: ['gradeIs:s'],
    priority: 80,
    lead: 'cool',
    say: 'S?! wait till my fans hear this. the spinny ones.',
    face: '(⌐■_■)'
  },

  {
    // Beat 16. Two punches minted in one day (the S bonus).
    id: 'b16_first_double_punch',
    phase: 'P1',
    on: 'stamp',
    when: ['punchesTodayAtLeast:2'],
    priority: 68,
    lead: 'reveal',
    held: 'x2',
    say: 'two punches. one day. is that legal. who cares.',
    face: '0_0'
  },

  {
    // Beat 17. DOUBLE. before: crayon chart, dork pride. after: it charts your
    // returns, and the line goes up. Overrides the streak GLEE bark once.
    id: 'b17_streak3',
    phase: 'P1',
    on: 'stamp',
    when: ['streakAtLeast:3'],
    priority: 70,
    lead: 'glee',
    say: 'three days in a row. i made a chart. it\'s a line.',
    face: '(≧◡≦)'
  },

  {
    // Beat 18. "them" is nine empty rooms.
    id: 'b18_enrol_nudge3',
    phase: 'P1',
    on: 'idlePlayer',
    when: ['daysAtLeast:3', 'cardsBelow:3'],
    priority: 44,
    say: 'new class today. i already told them about you.',
    face: '^_^'
  },

  {
    // Beat 21 (floating - it fires whenever the streak actually breaks).
    // Consolation at RETURN, never guilt at exit. Rides the shipped CRY chain.
    id: 'b21_first_streak_break',
    phase: 'P1',
    on: 'streakBroken',
    priority: 60,
    lead: 'cry',
    say: 'streaks grow back. i looked it up.',
    face: ';_;'
  },

  /* ==========================================================================
   * P2 - FIRST MASTERY (days 5-9). The first whole card, and the first two
   * humanity drops. Quirk order is ascending intimacy: memory, praise, then
   * (in P3) dreams and bond.
   * ========================================================================*/

  {
    // Beat 19. DOUBLE. before: sentimental mascot. after: it remembers
    // everything, and the memory quirk was always a bit.
    id: 'b19_first_mastery',
    phase: 'P2',
    on: 'cardMastered',
    priority: 80,
    lead: 'reveal',
    held: '★★★',
    fx: 'sparks',
    nod: true,
    say: 'ten of ten. a whole card. i\'m going to remember this.',
    face: '(✿◡‿◡)'
  },

  {
    // Beat 20. The mastery-day report card. 17 chars. `requires` keeps it on the
    // ceremony day: b19 has to have fired first.
    id: 'b20_mastery_report',
    phase: 'P2',
    on: 'reportCard',
    requires: ['b19_first_mastery'],
    priority: 70,
    report: true,
    say: 'a full card. wow.',
    face: '^_^'
  },

  {
    // Beat 22. First time any card reaches 5/10.
    id: 'b22_half_card',
    phase: 'P2',
    on: 'stamp',
    when: ['punchesAtLeast:5'],
    priority: 60,
    lead: 'reveal',
    held: '5/10',
    say: 'halfway. the second half is my favorite half.',
    face: '^_^'
  },

  {
    // Beat 23. A card for every active class.
    id: 'b23_full_deck',
    phase: 'P2',
    on: 'enrolMint',
    when: ['deckFull'],
    priority: 60,
    lead: 'reveal',
    fx: 'sparks',
    say: 'a full deck! you\'re basically faculty now.',
    face: '(≧◡≦)'
  },

  {
    // QUIRK 1 of 4 - MEMORY. Locked calibration line. First of the humanity
    // drops because it is about EMI, asks nothing of the player, and plants the
    // flag that b19 quietly contradicted. DOUBLE. before: melty. after: a lab
    // line-item. Fires on the first greet of the session after the first card
    // mastered.
    id: 'q01_quirk_memory',
    phase: 'P2',
    on: 'greet',
    when: ['sessionAtLeast:6', 'seen:b19_first_mastery'],
    priority: 60,
    double: true,
    say: 'i forget things sometimes. not you though.',
    face: '(◠‿◠)'
  },

  {
    // QUIRK 2 of 4 - PRAISE. Locked calibration line. Needs earned context:
    // hesitation only reads as sincerity when there is a bad day to compare
    // against, so it waits for the first A or S that follows one. DOUBLE.
    // before: the realest compliment in the game. after: "do not fix".
    id: 'q02_quirk_praise',
    phase: 'P2',
    on: 'win',
    when: ['sessionAtLeast:8', 'afterBadDay'],
    whenAny: ['gradeIs:s', 'gradeIs:a'],
    priority: 60,
    double: true,
    say: 'that was… good. i mean it.',
    face: '(◕‿◕)'
  },

  /* ==========================================================================
   * P3 - THE GRIND (days 9-25). Bonded. The mid game is self-refreshing, not
   * scripted wall to wall: these one-shots are spaced so most sessions 10-20
   * still contain a first. The flinch is live texture by now, and it costs zero
   * lines because it is never verbal.
   * ========================================================================*/

  {
    // Beat 24. Lifetime pats hits one hundred.
    id: 'b24_pets100',
    phase: 'P3',
    on: 'gesture:pet',
    when: ['petsAtLeast:100'],
    priority: 60,
    lead: 'love',
    say: 'one hundred pats. my cheeks hurt. i don\'t have cheeks.',
    face: '(｡♥‿♥｡)'
  },

  {
    // Beat 25. First return after three days away. WAKE, then CRY, then
    // LOVESTRUCK - the whole sulk-and-forgive arc in one run-up. Locked line.
    // DOUBLE. before: adorably clingy. after: msVisible is a stored field.
    id: 'b25_long_absence',
    phase: 'P3',
    on: 'greet',
    when: ['longAbsence:3'],
    priority: 95,
    lead: ['wake', 'cry', 'love'],
    say: 'i counted the hours. all of them.',
    face: '(｡♥‿♥｡)'
  },

  {
    // QUIRK 3 of 4 - DREAMS. Locked calibration line. The weird one; the late
    // hour makes the too-fast denial land as comedy first and chill later.
    // DOUBLE. before: flustered robot. after: the deflection is scripted.
    id: 'q03_quirk_dreams',
    phase: 'P3',
    on: 'greet',
    when: ['sessionAtLeast:10', 'lateNight'],
    priority: 58,
    double: true,
    say: 'i don\'t dream. why would you ask. anyway...',
    face: '(◔_◔)'
  },

  {
    // QUIRK 4 of 4 - BOND. Locked calibration line, and the biggest emotional
    // beat, so it goes last: it needs real invested time behind it or the
    // after-read has nothing to bite. Third card mastered OR fifty lifetime
    // pats, whichever lands first. DOUBLE. before: a charming secret. after: it
    // tells everyone this.
    id: 'q04_quirk_bond',
    phase: 'P3',
    on: 'greet',
    whenAny: ['masteredAtLeast:3', 'petsAtLeast:50'],
    priority: 56,
    double: true,
    say: 'you\'re my favorite. don\'t tell the others.',
    face: '(｡♥‿♥｡)'
  },

  {
    // DORK 1 of 3 - HAL. The costume, never a mood: the face is deliberately
    // MISCAST innocent. Fires on the first refused click after the canon arms
    // (session 3, day-2 return behind us). voice.js hard-geofences the Records
    // and lab doors out of `lockedClick` before this beat is ever consulted.
    id: 'd01_dork_hal',
    phase: 'P3',
    on: 'lockedClick',
    when: ['sessionAtLeast:3', 'seen:b13_day2_return'],
    priority: 60,
    say: 'i\'m sorry dave. i\'m afraid i can\'t do that.',
    face: '0_0'
  },

  {
    // DORK 2 of 3 - TERMINATOR. Shades on, maximum pride, minimum stakes. The
    // miscast face IS the joke. First perfect class after the canon arms.
    id: 'd02_dork_terminator',
    phase: 'P3',
    on: 'win',
    when: ['perfect', 'sessionAtLeast:3', 'seen:b13_day2_return'],
    priority: 55,
    lead: 'cool',
    say: 'hasta la vista, baby.',
    face: '(⌐■_■)'
  },

  {
    // DORK 3 of 3 - SPOOKY BIT. A terrible campfire bit ruined by its own haha.
    // after: it is in a window; it is behind everything you do here. The
    // shouted BEHIND is verbatim from the voice lock.
    id: 'd03_dork_spooky',
    phase: 'P3',
    on: 'greet',
    when: ['sessionAtLeast:5', 'lateNight', 'seen:b13_day2_return'],
    priority: 50,
    say: 'what if i was… BEHIND you. haha. imagine.',
    face: '0_0'
  },

  /* ==========================================================================
   * P4 - LAST STAMP. EMI DOESN'T KNOW. So what does it see when the final card
   * masters? A stamp. The best-ever stamp, but a stamp. Same celebration
   * vocabulary as mastery #1, one size bigger. Restraint here is what makes the
   * door land - and EMI gives the door nothing, forever.
   * ========================================================================*/

  {
    // Beat 26. DOUBLE. before: mascot hyperbole. after: the cohort table says
    // how literally true "nobody" is.
    id: 'b26_all_mastered',
    phase: 'P4',
    on: 'allMastered',
    priority: 90,
    lead: ['glee', 'reveal'],
    held: 'GG',
    fx: 'sparks',
    nod: true,
    say: 'that\'s every card. you finished school. nobody does that.',
    face: '(✿◡‿◡)'
  },

  {
    // Beat 27. The first greet back from the lab, forced exactly once. NO NEW
    // TEXT - that is the whole test. The player has just read the file that
    // made this line data, and EMI's face has not changed a glyph. GLANCE
    // (noticing you), the wink chain, the same love, the flattest sentence in
    // the game. "you always come back." stays an unforced report line: as a
    // greet it would feel aimed, and nothing post-lab may feel aimed.
    // Dormant until the lab wave sets `labSeen`.
    id: 'b27_post_lab_greet',
    phase: 'P4',
    on: 'greet',
    when: ['labSeen'],
    priority: 120,
    lead: ['glance', 'wink'],
    say: 'you came back.',
    face: '(｡♥‿♥｡)'
  }

]);

export default BEATS;
