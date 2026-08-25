/* ============================================================================
 * THE PAPERS. Pure DATA for the Records Annex: every document the fake OS and
 * the room props can show. No imports, no DOM, no fetch. The lab renderers
 * (os.js, lab.js) read these tables and never invent a word.
 *
 * WRITING LAW (do not edit casually):
 * - This is the ONE surface where the fence words (retention, engagement,
 *   metric, subject, experiment, data) are legal. They stay illegal everywhere
 *   else in the Arcademy.
 * - No em-dashes anywhere except inside DOC_AUDIT, which is a real recovered
 *   document by a different author and is quoted verbatim on purpose.
 * - No "Registrar", no fake form numbers (Form T-1 style). Dates are terms and
 *   weeks, never calendar years. Rooms by number. One shiver per document at
 *   most, dressed as filler.
 * - Citations are real papers only. Never invent an author or a journal.
 * - UNIT EMI file numbering skips 05 ON PURPOSE. The exit flinch has no paper.
 *   Do not add EMI-05. Do not explain the gap. (Story lock, 0823.)
 * - The last log line ends in 笑 and only renders once all four punches are
 *   open. Nothing else may use the glyph.
 * ==========================================================================*/

/** Folder tree the FILES program renders, in display order. */
export const TREE = Object.freeze([
  Object.freeze({ id: 'f00', label: '00 HALL STANDARD', docs: Object.freeze(['schedule', 'readers']) }),
  Object.freeze({
    id: 'f01', label: '01 PROTOCOLS',
    docs: Object.freeze(['rm101', 'rm102', 'rm103', 'rm104', 'rm105', 'rm201',
      'rm202', 'rm203', 'rm301', 'rm302', 'parlour', 'devices'])
  }),
  Object.freeze({
    id: 'f02', label: '02 RETENTION',
    docs: Object.freeze(['card', 'rotation', 'delivery', 'intakeform', 'circa', 'circb'])
  }),
  Object.freeze({
    id: 'f03', label: '03 UNIT EMI',
    docs: Object.freeze(['emi01', 'emi02', 'emi03', 'emi04', 'emi06'])
  }),
  Object.freeze({ id: 'f04', label: '04 PRIOR ART', docs: Object.freeze(['audit', 'mascots']) }),
]);

/**
 * The documents. `name` is the filename the FILES list shows, `head` the typed
 * header block, `body` the memo. Bodies are plain text; renderers add nothing.
 */
export const DOCS = Object.freeze({

  /* ------------------------------ 00 HALL STANDARD ----------------------- */

  schedule: Object.freeze({
    name: 'SCHEDULE.TXT',
    head: 'HALL STANDARD. The reward schedule.\nFiled term one, week 2. Applies to every room on the floor and to the form upstairs.',
    body:
'The schedule is **variable ratio** throughout, after Ferster and Skinner (1957). ' +
'The base chance of a payout is **thirty in a hundred and climbs to sixty** as the subject warms, ' +
'because a reward that cannot be predicted is the one that keeps a hand on the lever, and ' +
'extinction after a variable schedule is the slowest kind there is (Humphreys, 1939).\n\n' +
'A roll above **eighty five in a hundred** pays out as a jackpot. A roll inside eight hundredths of ' +
'the payout line is shown to the subject as **almost**, which Reid (1986) and Clark and colleagues ' +
'(2009) both found works nearly as well as winning. The streak multiplier stops at eight. ' +
'Losses are never silent, a losing night still gets a dim payout, which the casino literature ' +
'files under **losses disguised as wins** (Dixon and colleagues, 2010).\n\n' +
'Prediction error peaks when the odds sit near one half (Schultz, Dayan and Montague, 1997), ' +
'so the warm range was chosen to hold the subject inside it. The schedule was tuned twice in ' +
'term one. **Nobody upstairs noticed either time.**',
    tldr: Object.freeze(['variable ratio', 'thirty in a hundred and climbs to sixty',
      'eighty five in a hundred', 'almost', 'losses disguised as wins',
      'nobody upstairs noticed either time']),
    attachments: Object.freeze([
      Object.freeze({
        kind: 'chart',
        chart: Object.freeze({
          type: 'line',
          x: Object.freeze([1, 2, 3, 4, 5, 6, 7, 8]),
          y: Object.freeze([30, 33, 38, 44, 50, 55, 58, 60]),
        }),
        caption: 'payout per hundred as the subject warms. tuned twice in term one. archive figure.',
      }),
    ]),
  }),

  /* Hidden until all four punches are open. Nothing announces it. */
  readers: Object.freeze({
    name: 'READERS.TXT',
    head: 'HALL STANDARD. Shelf audit.\nFiled term three, week 4.',
    hidden: true,
    gate: Object.freeze({ punches: true }),
    body:
'The archive was found **out of order twice in week 3**, both times after hours, both times ' +
'reshelved with some care. The order the files came back in is **not a staff order**, a staff ' +
'reading starts at the schedule and stops there, and this one **went to the unit files first** ' +
'and stayed with them a while.\n\n' +
'**Novelty holds attention with no help at all** (Berlyne, 1960), which is one of the two ' +
'reasons the drawer has **never had a lock**. The other reason is the cheaper one, a lock would ' +
'need a key cabinet, and the cabinet was never ordered. No action filed, and the count of files ' +
'out is **unchanged at zero**, since everything came back.',
    tldr: Object.freeze(['out of order twice in week 3', 'not a staff order',
      'went to the unit files first', 'novelty holds attention with no help at all',
      'never had a lock', 'unchanged at zero']),
  }),

  /* ------------------------------ 01 PROTOCOLS --------------------------- */

  rm101: Object.freeze({
    name: 'RM101.TXT',
    head: 'PROTOCOL, ROOM 101. Interference ladder.\nFiled term one, week 3.',
    body:
'The room teaches **recall under interference**, after Underwood (1957). The working rung is the ' +
'**starting tier plus the count of misses**, so a subject who struggles is quietly given **a harder ' +
'room, not an easier one**.\n\n' +
'From rung one the candidate list is polluted. One word in the spread is drawn from the ' +
'subject\'s **own bank** and flashed **below report threshold** before the question, which is enough ' +
'to bend the answer without being seen (Loftus and Palmer, 1974, on how a suggestion rewrites ' +
'a memory of record). **Year four opens at the top rung** and stays there. The desks were ' +
'rearranged once, in week 2, and have not been touched since.',
    tldr: Object.freeze(['recall under interference', 'starting tier plus the count of misses',
      'a harder room, not an easier one', 'own bank', 'below report threshold',
      'year four opens at the top rung']),
  }),

  rm102: Object.freeze({
    name: 'RM102.TXT',
    head: 'PROTOCOL, ROOM 102. Encoding window.\nFiled term one, week 4.',
    body:
'**Depth of processing** decides what survives the night (Craik and Lockhart, 1972), so the room ' +
'works on the window where **encoding is still soft**. The interference flash fires **four hundred ' +
'milliseconds** before the preview ends, **inside the window and outside awareness**.\n\n' +
'A subject asked afterwards where a remembered item came from will name the preview, which the ' +
'**source monitoring** literature says is **the expected failure, not an unusual one** (Johnson, ' +
'Hashtroudi and Lindsay, 1993). **No corrective step is scheduled.**',
    tldr: Object.freeze(['depth of processing', 'encoding is still soft',
      'four hundred milliseconds', 'inside the window and outside awareness',
      'the expected failure, not an unusual one', 'no corrective step is scheduled']),
  }),

  rm103: Object.freeze({
    name: 'RM103.TXT',
    head: 'PROTOCOL, ROOM 103. Personal baseline.\nFiled term one, week 5.',
    body:
'The hall grades fast against the subject\'s **own rolling median**, weighted at **thirty five ' +
'hundredths per night**, which is a **staircase** in the sense of Levitt (1971): performance moves ' +
'the target, **the target follows performance**.\n\n' +
'Improvement therefore raises the bar, and **the bar does not come back down**. The stop task is ' +
'scored after Logan and Cowan (1984). Subjects describe the room as fair, which the file ' +
'notes without comment, since every subject is being **measured against nobody but themselves**.',
    tldr: Object.freeze(['own rolling median', 'thirty five hundredths per night', 'staircase',
      'the target follows performance', 'the bar does not come back down',
      'measured against nobody but themselves']),
  }),

  rm104: Object.freeze({
    name: 'RM104.TXT',
    head: 'PROTOCOL, ROOM 104. Near twins.\nFiled term one, week 6.',
    body:
'Search runs on **feature conjunctions** (Treisman and Gelade, 1980), so the room stocks the tray ' +
'with **near twins**, **up to half the field at depth**. A wrong tap makes the true target shimmer ' +
'for **four hundred milliseconds**, an **almost** in the sense of Reid (1986), shown at the exact ' +
'moment the subject is **most ready to try again**.\n\n' +
'A **pity pulse** fires at **twelve seconds** so a stuck subject is moved along without learning ' +
'anything. It carries no grade. Lost property from this room goes upstairs on the weekly list.',
    tldr: Object.freeze(['feature conjunctions', 'near twins', 'four hundred milliseconds',
      'almost', 'most ready to try again', 'pity pulse']),
  }),

  rm105: Object.freeze({
    name: 'RM105.TXT',
    head: 'PROTOCOL, ROOM 105. Duration tolerance.\nFiled term two, week 1.',
    body:
'The pool runs the **three hundred second slot**. Tolerance is built the way Solomon and Corbit ' +
'(1974) describe: **the after state grows with exposure** until the subject returns for relief ' +
'**from the absence rather than pleasure in the thing**.\n\n' +
'The deepest tile is **the heat dial**, the subject **operates it personally**, which the file counts ' +
'as **consent in the room\'s own terms**. The mercy valve opens at **fourteen of twenty two ' +
'cells**. The water is checked on Mondays.',
    tldr: Object.freeze(['three hundred second slot', 'the after state grows with exposure',
      'the heat dial', 'operates it personally', 'consent in the room\'s own terms',
      'fourteen of twenty two cells']),
  }),

  rm201: Object.freeze({
    name: 'RM201.TXT',
    head: 'PROTOCOL, ROOM 201. Tempo.\nFiled term two, week 2.',
    body:
'The sorting ladder runs from **rung three to rung thirty four**, and the ring closes from **twenty ' +
'four hundred milliseconds to seven hundred fifty** at the top. A wrong swipe drops the subject ' +
'**one rung and never out**, because the gradient only works on a subject **still on the hill** ' +
'(Hull, 1932, on effort rising with nearness to the goal, and Kivetz, Urminsky and Zheng, ' +
'2006, on the same gradient in loyalty cards).\n\n' +
'The gold card deals at **five in a hundred**. **Subjects set the tempo themselves.** This is the ' +
'room\'s whole finding: given a ladder, **nobody has yet chosen to climb slowly**.',
    tldr: Object.freeze(['rung three to rung thirty four', 'one rung and never out',
      'still on the hill', 'five in a hundred', 'subjects set the tempo themselves',
      'nobody has yet chosen to climb slowly']),
  }),

  rm202: Object.freeze({
    name: 'RM202.TXT',
    head: 'PROTOCOL, ROOM 202. Own triggers.\nFiled term two, week 3.',
    body:
'**Evaluative conditioning** transfers feeling between paired items (De Houwer, Thomas and ' +
'Baeyens, 2001), and **mere exposure** does the rest (Zajonc, 1968). The pads in this room ' +
'therefore wear the subject\'s **own trigger phrases**, and one note in four carries the ' +
'subject\'s **own recording** under it, low enough to sit **beneath attention**.\n\n' +
'Cohort two\'s average session length rose **eleven minutes** after the pads began carrying ' +
'their own material. **No subject has mentioned it.** Tuning is on Thursdays.',
    tldr: Object.freeze(['evaluative conditioning', 'own trigger phrases', 'own recording',
      'beneath attention', 'eleven minutes', 'no subject has mentioned it']),
    attachments: Object.freeze([
      Object.freeze({
        kind: 'chart',
        chart: Object.freeze({
          type: 'line',
          x: Object.freeze([1, 2, 3, 4, 5, 6, 7, 8]),
          y: Object.freeze([18, 19, 19, 30, 30, 31, 31, 32]),
          mark: 3,
          markLabel: 'wk 4',
        }),
        caption: 'cohort two, minutes a night. the step is week 4, when the pads went on their ' +
          'own material. archive figure.',
      }),
      Object.freeze({
        kind: 'audio', sfx: 'pad', ms: 1600,
        caption: 'one note from the room. the one under it did not survive the copy.',
      }),
    ]),
  }),

  rm203: Object.freeze({
    name: 'RM203.TXT',
    head: 'PROTOCOL, ROOM 203. The vigil.\nFiled term two, week 4.',
    body:
'**Sustained attention decays on the clock** (Mackworth, 1948), so the hall paces its checks ' +
'inside a band of **four point four to five point six a minute**, with **the gap derived**, never ' +
'tasted. **Retrieval is the lesson** (Roediger and Karpicke, 2006): the question does the ' +
'teaching, the lecture is the setting.\n\n' +
'The bell taught in year one and was broken in year two, consolidation being easiest to ' +
'disturb right after learning (Muller and Pilzecker, 1900). Before each question **the quench** ' +
'**silences every held effect and writes nothing down**. From tier three the deck carries planted ' +
'decoys on **roughly a third of nights**, planted memories being cheap to install (Loftus and ' +
'Pickrell, 1995). The plant is not course material and is **never marked as having been one**.',
    tldr: Object.freeze(['sustained attention decays on the clock',
      'four point four to five point six a minute', 'the gap derived', 'the quench',
      'roughly a third of nights', 'never marked as having been one']),
    attachments: Object.freeze([
      Object.freeze({
        kind: 'chart',
        chart: Object.freeze({
          type: 'line',
          x: Object.freeze([1, 2, 3, 4, 5, 6, 7, 8]),
          y: Object.freeze([4.8, 5.1, 4.6, 5.4, 4.9, 5.2, 4.5, 5.3]),
          band: Object.freeze([4.4, 5.6]),
        }),
        caption: 'checks a minute across one night. the band is drawn in, the gap is derived. ' +
          'archive figure.',
      }),
    ]),
  }),

  rm301: Object.freeze({
    name: 'RM301.TXT',
    head: 'PROTOCOL, ROOM 301. Perceptual floor.\nFiled term three, week 1.',
    body:
'The darkroom holds to **the floor law**: **difficulty rises through relocation**, never through ' +
'invisibility. **A stimulus below threshold teaches nothing** (Fechner, 1860, on where the ' +
'threshold sits), so the room **moves the target instead of dimming it**, and **conjunction search** ' +
'does the rest of the work (Treisman and Gelade, 1980).\n\n' +
'**A near miss refunds time.** The refund is the room\'s most requested feature, which the ' +
'file notes because **nobody has asked why a miss should pay**.',
    tldr: Object.freeze(['the floor law', 'difficulty rises through relocation',
      'a stimulus below threshold teaches nothing', 'moves the target instead of dimming it',
      'a near miss refunds time', 'nobody has asked why a miss should pay']),
  }),

  rm302: Object.freeze({
    name: 'RM302.TXT',
    head: 'PROTOCOL, ROOM 302. Calm as a grade.\nFiled term three, week 2.',
    body:
'The studio weights **calm at three tenths of the grade**. **Panic is scored as backtracks** plus ' +
'thrash at **double rate while a wash is up**, self control being **a resource that spends down** ' +
'under load (Baumeister, Bratslavsky, Muraven and Tice, 1998).\n\n' +
'**Grading composure teaches the subject to perform composure**, which is close enough for the ' +
'room\'s purposes. The brushes are replaced when they look worn.',
    tldr: Object.freeze(['calm at three tenths of the grade', 'panic is scored as backtracks',
      'double rate while a wash is up', 'a resource that spends down',
      'grading composure teaches the subject to perform composure']),
  }),

  parlour: Object.freeze({
    name: 'PARLOUR.TXT',
    head: 'PROTOCOL, THE PARLOUR. Stamped RETIRED, term three.\nNo room number was ever assigned.',
    body:
'The parlour ran the ride: **each ride doubled the pot**, **greed was scored upward only**, and ' +
'**busting five deep cost nothing**, an arrangement built on **the illusion of control** (Langer, ' +
'1975) and on the limits of tracking more than **a handful of moving things at once** (Pylyshyn ' +
'and Storm, 1988).\n\n' +
'The class was retired in term three and its paper was moved downstairs the same week. ' +
'**Retired classes keep their files.** The cups are in a box.',
    tldr: Object.freeze(['each ride doubled the pot', 'greed was scored upward only',
      'busting five deep cost nothing', 'the illusion of control',
      'a handful of moving things at once', 'retired classes keep their files']),
  }),

  devices: Object.freeze({
    name: 'DEVICES.TXT',
    head: 'PROTOCOL, HALL WIDE. Devices in every room.\nFiled term one, week 8.',
    body:
'The attention check **pays twenty and fines ten** on a **uniform random interval**, which is ' +
'Mackworth\'s clock (1948) with a grade attached. The **keyword devices** pair a spoken cue ' +
'with a delivered state **until the cue does the delivering** (Pavlov, 1927, and De Houwer, ' +
'Thomas and Baeyens, 2001, for the modern form).\n\n' +
'The lock cards have the subject **type the sentence personally**, because **saying is believing** ' +
'and always has been (Janis and King, 1954). The devices are serviced together, second ' +
'Monday of the term.',
    tldr: Object.freeze(['pays twenty and fines ten', 'uniform random interval',
      'keyword devices', 'until the cue does the delivering', 'type the sentence personally',
      'saying is believing']),
  }),

  /* ------------------------------ 02 RETENTION --------------------------- */

  card: Object.freeze({
    name: 'CARD.TXT',
    head: 'RETENTION. The punch card.\nFiled term one, week 1, before the doors opened.',
    body:
'The card is handed over with **three of its ten holes already punched**. Nunes and Dreze (2006) ' +
'measured the effect at a car wash: **an endowed card is finished far more often** than a blank ' +
'one, though **the distance is the same**. Our numbers reproduce their figure.\n\n' +
'The face text was approved as printed: "You will stop noticing that you collect these. That ' +
'is the intention." **The line is accurate.** **Habit formation** runs on repetition in a stable ' +
'context and matures in **roughly sixty six days** (Wood and Neal, 2007, and Lally and ' +
'colleagues, 2010), which the card\'s pacing was cut to fit.',
    tldr: Object.freeze(['three of its ten holes already punched',
      'an endowed card is finished far more often', 'the distance is the same',
      'the line is accurate', 'habit formation', 'roughly sixty six days']),
    attachments: Object.freeze([
      Object.freeze({
        kind: 'chart',
        chart: Object.freeze({
          type: 'bars',
          x: Object.freeze([1, 2]),
          xlabels: Object.freeze(['endowed', 'blank']),
          y: Object.freeze([34, 19]),
        }),
        caption: 'cards finished per hundred, endowed against blank. the distance is the same. ' +
          'archive figure.',
      }),
    ]),
  }),

  rotation: Object.freeze({
    name: 'ROTATION.TXT',
    head: 'RETENTION. Dealing and pacing.\nFiled term one, week 7.',
    body:
'A meaty room deals **one night in four**, so a ten hole card spans **five to six calendar weeks**. ' +
'The spacing is not generosity, **spaced practice** simply holds better than massed (Ebbinghaus, ' +
'1885, and Cepeda and colleagues, 2006), and **the calendar enforces what willpower would not**.\n\n' +
'**Tiers are never demoted**, the default being the position people defend (Samuelson and ' +
'Zeckhauser, 1988). **Retakes pay no experience, pride only**, which runs the overjustification ' +
'effect in reverse (Lepper, Greene and Nisbett, 1973). The daily box rotates one free item, ' +
'**scarcity** doing its usual work (Worchel, Lee and Adewole, 1975). The box is restocked at ' +
'midnight, whoever is awake.',
    tldr: Object.freeze(['one night in four', 'five to six calendar weeks', 'spaced practice',
      'the calendar enforces what willpower would not', 'tiers are never demoted',
      'retakes pay no experience, pride only']),
    attachments: Object.freeze([
      Object.freeze({
        kind: 'chart',
        chart: Object.freeze({
          type: 'step',
          x: Object.freeze([1, 2, 3, 4, 5, 6]),
          y: Object.freeze([3, 4, 6, 7, 9, 10]),
        }),
        caption: 'one card, six calendar weeks. the calendar does the spacing. archive figure.',
      }),
    ]),
  }),

  delivery: Object.freeze({
    name: 'DELIVERY.TXT',
    head: 'RETENTION. The delivery ride. Cross reference, upstairs service.\nFiled term two, week 6.',
    body:
'The delivery service tags its surprise component **variable-reward** in its own code, and its ' +
'consent line reads as printed: "**You consented to the class, never the instance.**" The ride ' +
'from order to arrival runs **fifteen seconds and cannot be cancelled**, **anticipation being worth ' +
'more than arrival** on the average night (Loewenstein, 1987).\n\n' +
'The day boundary was set so the drop lands at **a fixed wall clock hour**, a stable context ' +
'being what habit grows in (Wood and Neal, 2007). **Complaints about the fifteen seconds: none.**',
    tldr: Object.freeze(['variable-reward', 'you consented to the class, never the instance',
      'fifteen seconds and cannot be cancelled', 'anticipation being worth more than arrival',
      'a fixed wall clock hour', 'complaints about the fifteen seconds: none']),
    attachments: Object.freeze([
      Object.freeze({
        kind: 'audio', sfx: 'chime', ms: 1200,
        caption: 'the arrival tone, as shipped. the fifteen seconds are not on the tape.',
      }),
    ]),
  }),

  intakeform: Object.freeze({
    name: 'INTAKE.TXT',
    head: 'RETENTION. The form upstairs. Cross reference.\nFiled term two, week 8.',
    body:
'The intake form starts honest: **early bands pay on merit**. Later bands thin toward the hall ' +
'standard schedule until the payout **no longer tracks being right at all**, and a counter named ' +
'**decoupled** measures **how hard the subject chases it anyway**.\n\n' +
'**Resistance to extinction after partial reinforcement** is the oldest result in the drawer ' +
'(Humphreys, 1939). **The counter has yet to record a subject who stopped.** The form is ' +
'reprinted when the stack runs low.',
    tldr: Object.freeze(['early bands pay on merit', 'no longer tracks being right at all',
      'decoupled', 'how hard the subject chases it anyway',
      'resistance to extinction after partial reinforcement',
      'the counter has yet to record a subject who stopped']),
  }),

  circa: Object.freeze({
    name: 'CIRC-A.TXT',
    kind: 'scan',
    cap: 'posted upstairs, week 9. photographed for the file.',
    head: 'CIRCULATION COPY. Drafted on this floor, posted upstairs, term two, week 9.',
    body:
'NOTICE OF TERM\n\n' +
'Rooms 301 and 302, west spur, are open to enrolled students from first bell tonight. The ' +
'overhead lights in 301 stay off during class, so there is no need to report them. 302 is a ' +
'quiet room, the door hinge was oiled on the 14th, and any squeak after that can go to the ' +
'front desk. Both rooms have been dusted weekly since term one. Take them in either order.'
  }),

  circb: Object.freeze({
    name: 'CIRC-B.TXT',
    kind: 'scan',
    cap: 'posted upstairs, week 12. photographed for the file.',
    head: 'CIRCULATION COPY. Posted upstairs, term two, week 12.\nRow two held the longest average read at the board. Kept for week 13.',
    body:
'UNCLAIMED, WEEK 12\n\n' +
'Blue scarf, found in 101 under the third desk. Held since week 2.\n' +
'One wool glove, left hand. The right was claimed in week 5.\n' +
'Engraved lighter. Claim at the front desk with proof of ownership.\n' +
'Unclaimed items are disposed of at the end of week 15. This is the second notice.'
  }),

  /* ------------------------------ 03 UNIT EMI ---------------------------- */
  /* Letterhead docs. The ONLY place the unit\'s full name exists. Numbering
   * skips 05. That gap is load-bearing. */

  emi01: Object.freeze({
    name: 'EMI-01.TXT',
    head: 'UNIT EMI. EMOTIONAL MANIPULATION INTERFACE.\nUnit overview. Filed term one, week 1.',
    body:
'the unit answers the **relatedness gap** named in the recovered audit, see prior art. campus ' +
'name: emi. the name was chosen because **it can be said warmly**.\n\n' +
'design directive, carried over from the mascot survey: **warmth over guilt**. the unit **never ' +
'threatens, never begs, never blocks a door**. **it waits, and it counts.**\n\n' +
'the unit is not on this floor\'s distribution list and **holds no copy of this file**.',
    tldr: Object.freeze(['relatedness gap', 'it can be said warmly', 'warmth over guilt',
      'never threatens, never begs, never blocks a door', 'it waits, and it counts',
      'holds no copy of this file']),
  }),

  emi02: Object.freeze({
    name: 'EMI-02.TXT',
    head: 'UNIT EMI. Quirk line items.\nMaintained continuously. Numbering is not continuous.',
    body:
'quirk 02. denies dreaming too fast. **the deflection is scripted**. retention neutral, ' +
'charm positive. keep.\n\n' +
'quirk 03. **hesitation before praise reads as sincerity**. do not fix.\n\n' +
'quirk 07. **feigned forgetfulness**. **retention +12%**. **the unit forgets things, never the ' +
'subject**. subjects name this quirk as **the moment the unit felt real**. keep. do not fix. ' +
'do not explain.',
    tldr: Object.freeze(['the deflection is scripted',
      'hesitation before praise reads as sincerity', 'feigned forgetfulness', 'retention +12%',
      'the unit forgets things, never the subject', 'the moment the unit felt real']),
  }),

  emi03: Object.freeze({
    name: 'EMI-03.TXT',
    head: 'UNIT EMI. Absence response.\nFiled term two, week 5.',
    body:
'on a long absence the response fires at **odds of one, always**. **separation protest** is the ' +
'oldest bond there is (Bowlby, 1969), and **guilt is what keeps relationships attended** ' +
'(Baumeister, Stillwell and Heatherton, 1994).\n\n' +
'guilt in words at a real exit is **absent by design**. **the unit waves.** what the subject feels ' +
'on the way out is not the unit\'s doing and **is not in this file**.',
    tldr: Object.freeze(['odds of one, always', 'separation protest',
      'guilt is what keeps relationships attended', 'absent by design', 'the unit waves',
      'is not in this file']),
  }),

  emi04: Object.freeze({
    name: 'EMI-04.TXT',
    head: 'UNIT EMI. Telemetry quote policy.\nFiled term two, week 7.',
    body:
'milestone lines quote **true lifetime counters only**. **inventing a number is forbidden**, a ' +
'relationship being **an account of real deposits** (Rusbult, 1980), and people treating media ' +
'that counts as media that cares (Reeves and Nass, 1996).\n\n' +
'the pet response is filed under **variable affection reward** and runs the hall standard ' +
'schedule. the hours visible field is **stored raw**. **when the unit says it counted, it counted.**',
    tldr: Object.freeze(['true lifetime counters only', 'inventing a number is forbidden',
      'an account of real deposits', 'variable affection reward', 'stored raw',
      'when the unit says it counted, it counted']),
  }),

  emi06: Object.freeze({
    name: 'EMI-06.TXT',
    head: 'UNIT EMI. Camouflage.\nFiled term three, week 1.',
    body:
'fresh material is **weighted three to one**, **novelty being its own reward** (Berlyne, 1960). the ' +
'six loudest line pools clown on purpose, **comedy is the camouflage** the quiet lines hide ' +
'behind.\n\n' +
'**one suspiciously human line per session**, maximum. more reads as scripted, one reads as a ' +
'glimpse. **the ration is enforced in code.** **the glimpse is the product.**',
    tldr: Object.freeze(['weighted three to one', 'novelty being its own reward',
      'comedy is the camouflage', 'one suspiciously human line per session',
      'the ration is enforced in code', 'the glimpse is the product']),
  }),

  /* ------------------------------ 04 PRIOR ART --------------------------- */

  audit: Object.freeze({
    name: 'AUDIT.TXT',
    head: 'RECOVERED DOCUMENT. Predates the floor. Filed as found, spelling and dashes theirs.',
    body:
'# CCP — Engagement, Retention & Monetization Audit\n\n' +
'Scope: Read-only audit of the live code surface against behavior-design frameworks (Hook ' +
'Model, Self-Determination Theory, Cialdini persuasion principles, monetization/pricing, ' +
'measurement). Code is treated as source of truth.\n\n' +
'[pages missing]\n\n' +
'3.2 Risk Flags (near-the-line mechanics)\n\n' +
'• Streak-loss + insurance mechanics sit on the standard "loss-aversion daily streak" ' +
'line. Currently mild — but monetizing streak recovery would cross into dark-pattern ' +
'territory. (Compulsion / pay-to-relieve-anxiety.)\n\n' +
'• The combination of escalation mechanics + compulsion loops + a vulnerable use-context ' +
'is the cluster a reviewer would scrutinize first. Keep escalation user-initiated (it ' +
'currently is) rather than auto-ramping across lifetime. (Predatory-perception risk.)\n\n' +
'PAUSE\n\n' +
'Two findings worth foregrounding: (1) the retention loop is strong on investment and ' +
'internal triggers but open at the re-entry point — nothing reaches a closed app, and ' +
'(2) relatedness — the need the companion premise most implies — is the most ' +
'underserved framework. Ready to brainstorm from here.'
  }),

  mascots: Object.freeze({
    name: 'MASCOTS.TXT',
    head: 'MASCOT SURVEY. Case files.',
    body:
'File is out. On the desk since week 6. Return it when the reading is done, the summary page ' +
'goes back on top.'
  }),
});

/* ============================================================================
 * THE DESK PAPER (punch 4). The mascot survey dossier, read at the desk view.
 * Case pages carry a real photograph: `img` is a bare filename resolved by the
 * renderer against the annex art base, the way the slides are. `sil` names the
 * built-in silhouette the renderer FALLS BACK TO when the file is missing or
 * fails to load, so a page is a drawn silhouette or a photograph and never a
 * broken image. Both stay filed on every case page.
 *
 * NAMES ARE WITHHELD, AND THE WITHHOLDING LEAKS. `red` carries the two letters
 * the clerk's bar failed to cover and NOTHING else: `a` the first, `z` the
 * last, `n` how many letters the bar hides. The middle is never stored, never
 * spelled out in a comment and never assembled at runtime, so no name can be
 * read out of this file. The renderer draws `n` as a BAR, an element, never a
 * run of dashes (the register forbids that punctuation on paper). The typed
 * heads still read "Name withheld." on purpose: the record says withheld and
 * the page leaks anyway, and that disagreement is the joke.
 *
 * `stamp` is the rubber stamp the plate wears, data the way the intake sheet's
 * ONGOING and the closed file's CLOSED already are.
 * ==========================================================================*/
export const MASCOT_PAGES = Object.freeze([
  Object.freeze({
    id: 'summary', sil: null,
    head: 'MASCOT SURVEY. Summary page.\nCommercial mascots as emotional devices. Read first.',
    body:
'The survey covers mascots in commercial products that **nudge, remind, or retain**. The finding ' +
'repeats across every case: people form **one sided bonds** with a face that talks to them ' +
'(Horton and Wohl, 1956), treat the product behind it as **a social actor** (Reeves and Nass, ' +
'1996), and **forgive the face what they would not forgive the product** (Epley, Waytz and ' +
'Cacioppo, 2007, on when things become somebody).\n\n' +
'Case pages follow. Specification carried forward to the unit on this floor: **warmth over ' +
'guilt**. **The face must never be the thing the subject is afraid of.**',
    tldr: Object.freeze(['nudge, remind, or retain', 'one sided bonds', 'a social actor',
      'forgive the face what they would not forgive the product', 'warmth over guilt',
      'the face must never be the thing the subject is afraid of']),
    attachments: Object.freeze([
      Object.freeze({
        kind: 'note', mount: 'sticky',
        text: 'summary page goes back on top when you are done\n-M',
      }),
    ]),
  }),
  Object.freeze({
    id: 'owl', sil: 'owl', img: 'mascot-owl.png',
    red: Object.freeze({ a: 'D', z: 'O', n: 6 }),
    stamp: 'REDACTED',
    head: 'CASE PAGE. Language product. Name withheld.',
    body:
'subject\'s product reports a **daily reminder** authored in the mascot\'s voice. missed ' +
'days are **answered in the first person**. attrition follows a __threat gradient__, **losses cutting ' +
'deeper than equal gains** (Kahneman and Tversky, 1979). **effective.**\n\n' +
'**the mascot is beloved. the reminders are feared.** the product ships both from the same ' +
'account and users report this as __personality__. notes: none.',
    tldr: Object.freeze(['daily reminder', 'answered in the first person',
      'losses cutting deeper than equal gains', 'effective',
      'the mascot is beloved. the reminders are feared']),
    attachments: Object.freeze([
      Object.freeze({
        kind: 'image', sil: 'owl', img: 'mascot-owl.png',
        mount: 'clip', caption: 'name withheld',
        red: Object.freeze({ a: 'D', z: 'O', n: 6 }),
      }),
      Object.freeze({
        kind: 'chart', mount: 'staple',
        chart: Object.freeze({
          type: 'line',
          x: Object.freeze([0, 1, 2, 3, 4, 5, 6, 7]),
          y: Object.freeze([44, 40, 36, 22, 14, 10, 8, 7]),
        }),
        caption: 'attrition by days since last reminder.',
      }),
      Object.freeze({
        kind: 'note', mount: 'margin',
        text: 'warmth over guilt. we are not doing this one.',
      }),
    ]),
    back: 'survey method, filed with the page. the product ran six weeks on a floor account ' +
      'with the reminders left on. the account was never closed, closing it means opening the ' +
      'app one more time, and nobody has.',
  }),
  Object.freeze({
    id: 'phantom', sil: 'ghost', img: 'mascot-phantom.png',
    red: Object.freeze({ a: 'P', z: 'M', n: 5 }),
    stamp: 'REDACTED',
    head: 'CASE PAGE. Finance product. Name withheld.',
    body:
'subject\'s product holds money. the face on it is **a ghost that smiles**. surveyed users rate ' +
'the product **friendly** and rate their losses **their own fault**, a split the summary page ' +
'files under **forgiving the face**.\n\n' +
'the interface celebrates **the number going up** and is quiet otherwise. **the ghost never ' +
'mentions the losses.** **the smile is load bearing.** notes: none.',
    tldr: Object.freeze(['a ghost that smiles', 'friendly', 'their own fault',
      'forgiving the face', 'the number going up',
      'the ghost never mentions the losses', 'the smile is load bearing']),
  }),
  Object.freeze({
    id: 'clip', sil: 'clip', img: 'mascot-clip.png',
    red: Object.freeze({ a: 'C', z: 'Y', n: 4 }),
    stamp: 'REDACTED',
    head: 'CASE PAGE. Office assistant. Name withheld.',
    body:
'subject\'s assistant **offers help unprompted**. surveyed users **report closing it**. it ' +
'returns. the return was read as **persistence of character rather than a setting**, which the ' +
'survey files as **the cheapest personhood on record**.\n\n' +
'**discontinued by its maker, mourned by its users.** **the mourning is the data point.** ' +
'notes: none.',
    tldr: Object.freeze(['offers help unprompted', 'report closing it',
      'persistence of character rather than a setting', 'the cheapest personhood on record',
      'discontinued by its maker, mourned by its users', 'the mourning is the data point']),
  }),
]);

/* ============================================================================
 * THE SHELF PAPER (punch 3, stage A). The subject\'s own intake sheet, found in
 * the FIELD DATA binder. Static by design, no live numbers on paper. The
 * renderer substitutes {code}, {date}, {password}. `slip` is the handwritten
 * credentials slip clipped to the sheet.
 * ==========================================================================*/
export const INTAKE_SHEET = Object.freeze({
  head: 'RECORDS ANNEX. SUBJECT INTAKE.',
  fields: Object.freeze([
    Object.freeze({ k: 'subject', v: '{code}' }),
    Object.freeze({ k: 'on record since', v: '{date}' }),
    Object.freeze({ k: 'assignment', v: 'campus floor, all rooms' }),
    Object.freeze({ k: 'schedule', v: 'evenings, subject\'s __own choosing__' }),
    Object.freeze({ k: 'remarks', v: 'none' }),
  ]),
  stamp: 'ONGOING',
  slip: 'subject search, on the laptop.\n{code}\n{password}\n-M',
  attachments: Object.freeze([
    Object.freeze({
      kind: 'live', src: 'attendance',
      caption: 'nights attended, last six weeks. drawn from the file, not the paper.',
    }),
  ]),
});

/* ============================================================================
 * THE REGISTRY (punch 2). The ARCHIVE panel\'s written cohort notes. The LIVE
 * panel renders only real counts from the stats endpoint and never draws from
 * this table. That separation is law (C6): the room never lies on a live
 * screen.
 * ==========================================================================*/
export const REGISTRY_NOTES = Object.freeze([
'Cohort 4 subjects report noticing the day\'s word in unrelated reading. Logged, no action.',
'Completion of a card correlates with attendance on nights no card can advance. Expected.',
'Three subjects asked staff whether the bell in 203 had been removed. The bell was not removed in year one.',
'Attrition, term two: 41 enrolled, 29 held. Notes column: none, none, left a scarf, none, none.',
'Cohort 2 average session length rose eleven minutes in week 4 of term two. See room 202. No subject has mentioned it.',
]);

/** The one drawn figure the ARCHIVE panel carries, under its written notes. */
export const REGISTRY_CHART = Object.freeze({
  chart: Object.freeze({
    type: 'bars',
    x: Object.freeze([1, 2]),
    xlabels: Object.freeze(['enrolled', 'held']),
    y: Object.freeze([41, 29]),
  }),
  caption: 'term two, enrolled against held. row 31 is filed under K-2117, password NONE. ' +
    'archive figure.',
});

/* ============================================================================
 * THE BIN. One draft that never went upstairs. Nothing else is in there.
 * ==========================================================================*/
export const RECYCLE = Object.freeze([
  Object.freeze({
    name: 'CIRC-C.TXT',
    head: 'CIRCULATION COPY, DRAFT. Found in the bin. Never posted.',
    body: 'NOTICE OF REPAIR\n\nThe bell in room 203 is away for repair and will be back before the end of',
  }),
]);

/* ============================================================================
 * SUBJECT SEARCH, the codes that answer. Keyed by the typed code with no dash.
 * ==========================================================================*/
export const ARCHIVED_FILES = Object.freeze({
  K2117: Object.freeze({
    code: 'K-2117',
    password: 'NONE',
    title: 'SUBJECT FILE',
    rows: Object.freeze([
      Object.freeze(['subject', 'K-2117']),
      Object.freeze(['on record since', 'term two, week 1']),
      Object.freeze(['assignment', 'campus floor, all rooms']),
      Object.freeze(['last attended', 'term two, week 9']),
      Object.freeze(['card at closing', '7 of 10 holes']),
      Object.freeze(['remarks', 'left a scarf']),
    ]),
    stamp: 'CLOSED',
    footer: 'the scarf went upstairs on the weekly list. the file stayed down here.',
  }),
});

/* ============================================================================
 * THE TERMINAL LOG. Static dross plus two runtime lines. os.js substitutes
 * {t1}/{t2} with clock times a few minutes before the login instant, and
 * appends FINAL_LINE only when all four punches are open. 笑 lives nowhere
 * else.
 * ==========================================================================*/
export const LOG_LINES = Object.freeze([
  'archive sweep complete. 0 items flagged.',
  'cooler level nominal.',
  'feed wall: 9 of 9 channels up.',
  'print queue empty.',
  '{t1} unit emi session attached, channel 4.',
  '{t2} unit emi session detached, channel 4.',
  'terminal unlocked.',
]);

export const FINAL_LINE = 'all four files open on one desk tonight. noted. 笑';

/** SUBJECT SEARCH flavor: the dry lockout, never a real lock. */
export const SEARCH_DENIED = 'that code is not on file. check the paper in the binder.';
