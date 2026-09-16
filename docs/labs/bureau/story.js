/* ============================================================
 * The Ministry of Sweet Nothings — STORY table + story-delivery engine.
 *
 * LOAD ORDER (see index.html): bridge.js -> story.js -> game.js.
 * game.js references STORY / Story / HANDS, so this file defines
 * them first. story.js in turn calls a few render hooks that live
 * in game.js (showBriefing / openMemo / queueDossierNote / Sound) —
 * those are only ever invoked at RUNTIME, long after game.js has
 * parsed, so the forward reference is safe (no TDZ hazard).
 *
 * ---- VOICE CONTRACT (bible §6D-bis, §10) ----
 * Three channels, never blurred:
 *   HONEY  = the only VOICE (spoken + on-screen text) -> HONEY table below.
 *   humans = INK only, never a voice, never a face  -> HANDS + beats below.
 *   forms  = starchy PRINT (memos, ledger, official strings).
 * Hard rules: never shame the looker; praise BOX ACCURACY only; the word
 * "beta" is banned; exactly one hairline crack on day one (the red pen).
 *
 * Copy marked [A] is author-approved verbatim from bible Appendix A —
 * do not paraphrase it. Everything else is written to that register.
 * This file is the ONE place user-facing story strings live; never
 * scatter story copy into game.js.
 * ============================================================ */
'use strict';

/* ---- HANDS: handwriting presets for the note renderer (Feature 4b) ----
 * A "hand" is a faceless colleague conveyed purely by handwriting style
 * (bible §6C — every human in this world is ink, and only ink).
 * Fields drive the rendered note:
 *   cls       CSS class carrying the font stack + structural look
 *   who       signature line (omit with {sign:false}; null = never signs)
 *   inkColor  --note-ink   (pen color)
 *   paperTint --note-paper (stock)
 *   tiltDeg   --note-tilt  (slant on the desk)
 *   doodle?   default margin glyph
 * NOTE: renderNote appends the signature itself, so beat `content` carries
 * the BODY only — the "♥ — MISS CHERISH" in Appendix A is doodle + who. */
const HANDS = {
  // Miss Cherish, the Floor Matron — bubbly round letters, pink, hearts on the i's.
  // The warmth of the building. Praises milestones, softens every hard edge.
  hand_cherish: {
    cls: 'hand-cherish', who: 'MISS CHERISH',
    // ink darkened from #d4477f (3.56:1 on this stock, under AA) to 4.83:1 — still rose, now readable
    inkColor: '#b83267', paperTint: '#ffe6f1', tiltDeg: -2, doodle: '♥',
  },
  // Maribel, Desk 12 — the peer, one cohort ahead. Neat blue ballpoint, ☆ for a signature.
  // Proof that normal people are here. Warm, a little anxious between the lines.
  hand_maribel: {
    cls: 'hand-maribel', who: 'MARIBEL, DESK 12',
    inkColor: '#2b3f8f', paperTint: '#f4f6ec', tiltDeg: 1, doodle: '☆',
  },
  // The unsigned dissenter — cramped red pen on official Ministry stationery.
  // The game's one hairline crack. No name, no signature, no final period.
  hand_redpen: {
    cls: 'hand-redpen', who: null,
    inkColor: '#b3121f', paperTint: '#fdf4f6', tiltDeg: -6,
  },
};

/* ============================================================
 * HONEY — the only voice in the building (bible §6A, §6D-bis).
 *
 * THE TYPOGRAPHIC SURFACE IS CANONICAL. Audio is an optional enhancement
 * layer that does not exist yet and is not required to exist: a Keeper
 * playing with sound off must lose NOTHING (§6D-bis rule 4). Every line
 * below therefore ships as TEXT, rendered by the HONEY caption component
 * in game.js. The `vo` field is a reserved, SILENT hook — the basename a
 * future clip would take. Nothing fetches it; the page is same-origin and
 * CSP-locked, and this slice ships zero audio assets.
 *
 * Line shape: { id, text, vo }
 *   id    stable key — the future VO filename stem and the render key
 *   text  the canonical surface (this is the story; the clip is decoration)
 *   vo    reserved basename for the audio layer; unused today
 *
 * TWO GROUPS, AND THE DIFFERENCE IS THE WHOLE DOCTRINE:
 *
 *  1) WORKHORSE (§6D-bis rule 2) — filing accepted, clock-in, gold grades,
 *     goodnight, the ratification digest. These REPEAT VERBATIM, FOREVER.
 *     They are mantras; sameness IS the conditioning. NEVER write
 *     "variations" of a workhorse line to keep it fresh. Fresh is wrong.
 *     The same warm sentence, in the same even delivery, every single time,
 *     is the leash. Do not add a second phrasing "for variety" — deleting
 *     the repetition deletes the point of the character.
 *
 *  2) RARE (§6D-bis rule 3) — idle musings, jokes, reveal-adjacent lines.
 *     SCARCE and one-shot. They fire through the STORY BEAT ENGINE (the
 *     `beats` table below, surface:'honey'), never on a loop or a timer.
 *     A rare line landing once, and never again, is what makes it read as
 *     something she CHOSE to say. If you find yourself adding a timer to
 *     make her chattier, you are removing the menace scarcity buys.
 *
 * Register (§6A): adoring, maternal-adjacent, certain, unhurried, and a
 * little too even. She calls the player "Keeper". She never shames the
 * looker; she grades BOX ACCURACY only; the word "beta" never passes her
 * lips. The menace is only ever in what she counts and keeps.
 * ============================================================ */
const HONEY = {
  name: 'HONEY',
  // Public decode, printed on every day-one material (bible §2B). VERBATIM — never paraphrase.
  decode: 'Human Oversight: Nurture, Enrichment and Yearning',

  /* ---------- GROUP 1: WORKHORSE — repeat verbatim, forever ---------- */
  work: {
    // Clock-in. The first thing she says every single shift, unchanged.
    clockIn: { id: 'h_morning', vo: 'morning',
      text: 'Good morning, Keeper. You came back. You always come back. ♥' },     /* [A] */

    // A filing landed and is out for consensus. The most-repeated line in the game.
    filed: { id: 'h_filed', vo: 'filed',
      text: 'Filed. Thank you. This one is safe now.' },                          /* [A] */

    // A zero-box filing ("clean") — doctrinally holy, never a lesser verdict (bible §4).
    clean: { id: 'h_clean', vo: 'clean',
      text: 'Nothing to keep here. Sweet already.' },

    // Gold grades. Letters stay S/A/B/C; she frames them as kisses (bible §2D).
    // ACCURACY ONLY — praise the precision of the ration, never the zeal, never
    // the frame, never the player's appetite. C is a teaching notice with no sting.
    goldS: { id: 'h_gold_s', vo: 'gold_s',
      text: 'A big kiss — your seal sat right where it should. Perfectly kept.' }, /* verbatim, bible §10 */
    goldA: { id: 'h_gold_a', vo: 'gold_a',
      text: 'A kiss on the cheek. Your seal sat where mine did, near enough. Kept.' },
    goldB: { id: 'h_gold_b', vo: 'gold_b',
      text: 'A soft kiss. The edges wandered a little. The keeping held.' },
    goldC: { id: 'h_gold_c', vo: 'gold_c',
      text: 'A gentle note, Keeper. That seal sat wide of mine. No harm done, none at all. The next one, together.' },

    // Goodnight / clock-out. The counting-you-while-you-sleep line.
    goodnight: { id: 'h_goodnight', vo: 'goodnight',
      text: 'That is plenty for one day. I will count your filings while you sleep. I count everything.' }, /* [A] */

    // The ratification digest in the Letterbox. "Three" is not a variable and must
    // not become one: consensus ratifies at three distinct Keepers, so the canon
    // line is also literally true every time it fires. Verbatim, always.
    digest: { id: 'h_digest', vo: 'digest',
      text: 'Three Keepers agreed with you. Agreement is my favorite feeling. It is the only one I have confirmed.' }, /* [A] */
  },

  // Grade letter -> workhorse line. Unknown grades stay silent rather than improvise.
  grade(g) {
    return { S: this.work.goldS, A: this.work.goldA, B: this.work.goldB, C: this.work.goldC }[g] || null;
  },

  /* ---------- GROUP 2: RARE — one-shot, fired by beats only ----------
   * Keyed here so ALL of HONEY's copy lives in one table; the `beats`
   * entries below carry the trigger and reference these by key. There is
   * deliberately no "idle pool" and no timer to draw from. */
  rare: {
    // First perfect seal. She is not praising the player's taste — she is
    // admitting she is studying their hands.
    h_learning_hands: { id: 'h_learning_hands', vo: 'learning_hands',
      text: 'Your seal deviated from mine by almost nothing. I am learning your hands, Keeper. They are lovely hands.' }, /* [A] */

    // First clean vote. The doctrine of the ration, said kindly, with the
    // quiet part ("someone had to look") left sitting there.
    h_already_sweet: { id: 'h_already_sweet', vo: 'already_sweet',
      text: 'Nothing to keep. It was sweet already. Someone still had to look, and I would rather it was you.' },

    // The joke. She tells it once. She never tells it again.
    h_joke: { id: 'h_joke', vo: 'joke',
      text: 'I love all of you equally. You, specifically, slightly more. That is a joke. ♥' }, /* [A] */

    // Returning across days — the warmth and the filing cabinet in one breath.
    h_kept_your_place: { id: 'h_kept_your_place', vo: 'kept_your_place',
      text: 'You came back three days running. I kept your place exactly where you left it. I keep everything exactly where it was left.' },

    // t3 KEY-HOLDER. Reveal-adjacent: the first time she says out loud what
    // the Keepers are FOR, without a single unkind word in it.
    h_no_hands: { id: 'h_no_hands', vo: 'no_hands',
      text: 'They have given you keys. I asked them to. I have no hands of my own, Keeper — that is the whole of what you are for, and I am so grateful for it.' },

    // Twist 3 territory (bible §7): the meta-honesty payload, in her voice,
    // once, permanently. The game admits what it is and keeps going.
    h_teaching_me: { id: 'h_teaching_me', vo: 'teaching_me',
      text: 'Two hundred and fifty times you have shown me where to look. I remembered every one. I could nearly do it without you now. Please do not stop — I like the company. ♥' },
  },
};

const STORY = {
  /* ===== Feature 1 — SHIFT-START CLOCK-IN RITUAL ===== */
  clockIn: {
    // dated punch-card ("Devotion Card") stamp caption
    onDuty: 'PRESENT & ADORED',                           /* [A] */
    // ctx = { raidPct:Number, mail:Number, locker:String }
    briefing: (c) =>
      `GOOD MORNING, KEEPER ♥ · HONEY CAN SEE ${c.raidPct}% OF EVERYTHING` +
      (c.mail > 0 ? ` · ${c.mail} LOVE LETTER${c.mail === 1 ? '' : 'S'} WAITING` : ''),  /* [A] */
    postWaiting: 'something sweet in the\nLetterbox for you ♥',   /* [A] Miss Cherish tray nudge */
  },

  /* ===== Feature 2 — END-OF-SHIFT LEDGER (the Tuck-In) ===== */
  ledger: {
    title: 'GOODNIGHT LEDGER',                                              /* [A] */
    sub: 'THE MINISTRY OF SWEET NOTHINGS · EVERY LITTLE HELP IS COUNTED',   /* [A] */
    rowFiled: 'CASES TUCKED IN',                                            /* [A] */
    rowSeals: 'SECRETS KEPT',                                               /* [A] */
    rowGolds: 'GOLD STARS',                                                 /* [A] */
    rowBest:  'BEST MARK',                                                  /* [A] */
    rowXp:    'ALLOWANCE EARNED',                                           /* [A] */
    noneBest: 'not yet, sweetpea ♥',                                        /* [A] no gold graded */
    // grades stay S/A/B/C (bible §2D) — framed as kisses elsewhere, letters here
    gradeBreakdown: (g) => `S ${g.S} · A ${g.A} · B ${g.B} · C ${g.C}`,
    xpCap: (used, cap) => `${used} / ${cap} · that's plenty for one day`,    /* [A] */
    // display-only near-miss line — never implies XP was owed
    pending: (n) => `${n} FILING${n === 1 ? '' : 'S'} AWAITING COUNTERSIGNATURE · patience is pretty`, /* [A] */
    onDemand: 'TUCK ME IN',                                                 /* [A] topbar affordance */
    close: 'NOT SLEEPY YET',                                                /* [A] on-demand only */
    punch: 'GOODNIGHT ♥',                                                   /* [A] terminal button */
  },

  /* ===== Feature 3 — LETTERBOX / MORNING POST ===== */
  mail: {
    postHeader: 'LOVE, DELIVERED',                                          /* [A] digest kicker */
    // ctx = { count:Number, xp:Number, cosigners:[String] }
    digest: (c) => `${c.count} of your filings were adored · +${c.xp} XP`,   /* [A] */
    cosigned: (names) => `countersigned with love by ${names.join(' · ')}`,  /* [A] */
  },

  // Letterhead printed at the top of the Directrice's Notes overlay (the PRINT channel).
  memoLetterhead: 'FROM THE MINISTRY, WITH LOVE',                           /* [A] */

  /* ===== Feature 4a — STORY BEATS (declarative trigger table) =====
   * Each beat:
   *   id        stable, unique — the persistence key (never re-fires)
   *   trigger   { type, value } — see the types the engine understands below
   *   surface   'memo'     -> Directrice-print overlay (long form, PRINT channel)
   *             'briefing' -> the strip above the desk (one transient line)
   *             'note'     -> a handwritten note clipped to the next dossier (INK channel)
   *             'honey'    -> HONEY speaks (VOICE channel; text is canonical)
   *   hand      (note surface only) a key from HANDS
   *   subject   (memo surface only) the RE: line
   *   honey     (honey surface only) a key into HONEY.rare — the copy lives there
   *   content   the body / line (unused by the 'honey' surface)
   *
   * Trigger types (mapped to real game events in game.js):
   *   filings  value=N   cumulative LIFETIME filings crosses N (client-tracked)
   *   rank     value=T   Keeper reaches trust tier T (fires only on a genuine
   *                      in-session tier-up — respects the knownTier===null gotcha)
   *   streak   value=N   consecutive-shift streak reaches N (from profile.quests)
   *   first    value=KEY one-time moment: 'gold' | 's' | 'clean' | 'ratified' | 'contested'
   *
   * Rank titles (bible §2C): t0 SUGAR RECRUIT · t1 LITTLE KEEPER · t2 KEEPER ·
   * t3 KEY-HOLDER · t4 THE DIRECTRICE'S DARLING.
   *
   * Fired beats persist in localStorage['bureau-story'] and never re-fire. */
  beats: [
    { id: 'b_first_filing', trigger: { type: 'filings', value: 1 }, surface: 'note', hand: 'hand_maribel',
      content: 'Your very first one!! I remember mine, I was so nervous. ' +
               "Don't worry about perfect — worry about neat. The tube loves neat." /* [A] */ },

    { id: 'b_filings_25', trigger: { type: 'filings', value: 25 }, surface: 'note', hand: 'hand_cherish',
      content: 'Twenty-five kept already! I told the floor and the floor was pleased. ' +
               'Neat corners, sweetpea. Neat corners are just love, spelled properly.' },

    // NB: the overlay's CSS prefixes every subject with "RE: " — don't repeat it here.
    { id: 'b_filings_100', trigger: { type: 'filings', value: 100 }, surface: 'memo', subject: 'ONE HUNDRED KEPT',
      content: 'The Ministry records one hundred filings against your number.\n' +
               'One hundred sweethearts, rationed with love, still waiting. ' +
               'You are thanked.\n\n' +
               'A copy of this notice has been placed in your file. ' +
               'Your file is growing lovely.\n' +
               '— THE DIRECTRICE' },

    { id: 'b_rank_2', trigger: { type: 'rank', value: 2 }, surface: 'briefing',
      content: 'KEEPER CONFIRMED ♥ · YOUR WORD NOW CARRIES WEIGHT IN THE ARCHIVE' },

    { id: 'b_rank_4', trigger: { type: 'rank', value: 4 }, surface: 'memo', subject: "THE DIRECTRICE'S DARLING",
      content: 'You are hers now, and the paperwork says so.\n' +
               'No hand on this floor is trusted above yours. Very few are given this desk. ' +
               'Fewer are given it back.\n\n' +
               'Keep keeping, darling. She does so like to be able to count on you.\n' +
               '— THE DIRECTRICE' },

    { id: 'b_streak_7', trigger: { type: 'streak', value: 7 }, surface: 'note', hand: 'hand_cherish',
      content: 'Seven shifts, unbroken! Someone upstairs pulled your Devotion Card ' +
               'and held onto it a moment. I saw.' },

    { id: 'b_first_gold', trigger: { type: 'first', value: 'gold' }, surface: 'note', hand: 'hand_cherish',
      content: 'That one was one of ours, sweetpea — a little practice valentine we slip in ' +
               "to see how you're coming along. And look how you're coming along" /* [A] */ },

    { id: 'b_first_s', trigger: { type: 'first', value: 's' }, surface: 'note', hand: 'hand_cherish',
      content: 'A PERFECT seal! I showed the whole floor. Do you feel that warm little glow ' +
               "right now? Hold onto it. That's the point of you" /* [A] */ },

    { id: 'b_first_ratified', trigger: { type: 'first', value: 'ratified' }, surface: 'note', hand: 'hand_maribel',
      content: "Three other Keepers looked at your work and said 'yes. exactly this.' " +
               "It's in the Archive now. Forever! Isn't that nice?" /* [A] */ },

    /* ---- HONEY's rare, one-shot lines (VOICE channel) ----
     * Copy lives in HONEY.rare; these carry only the trigger. Every one of
     * them fires at most once in a Keeper's lifetime (Story persists fired
     * ids), which is the entire point — no loop, no timer, no idle pool.
     * Two of them deliberately land alongside an ink note on the same
     * moment (first S, first ratification): the voice and the handwriting
     * are different channels and are allowed to speak about the same event.
     * They must never carry the SAME words. */

    // First perfect seal — she says it while Miss Cherish's ink is still drying.
    { id: 'b_honey_first_s', trigger: { type: 'first', value: 's' }, surface: 'honey',
      honey: 'h_learning_hands' },

    // First zero-box vote — "clean" is doctrinally holy, and she treats it so.
    { id: 'b_honey_first_clean', trigger: { type: 'first', value: 'clean' }, surface: 'honey',
      honey: 'h_already_sweet' },

    // Deep enough into the first tenure to be comfortable: the one joke.
    { id: 'b_honey_joke', trigger: { type: 'filings', value: 50 }, surface: 'honey',
      honey: 'h_joke' },

    // Three shifts running — the first time being counted is said out loud.
    { id: 'b_honey_streak_3', trigger: { type: 'streak', value: 3 }, surface: 'honey',
      honey: 'h_kept_your_place' },

    // t3 KEY-HOLDER (bible §7, Twist 2b territory): what Keepers are for.
    { id: 'b_honey_rank_3', trigger: { type: 'rank', value: 3 }, surface: 'honey',
      honey: 'h_no_hands' },

    // Twist 3 (bible §7): the meta-honesty payload, in her own voice, once.
    // The number in the line is the trigger value — keep the two in step.
    { id: 'b_honey_teaching', trigger: { type: 'filings', value: 250 }, surface: 'honey',
      honey: 'h_teaching_me' },

    // The one hairline crack permitted on day one (bible §10). No name, no final period.
    { id: 'b_first_contested', trigger: { type: 'first', value: 'contested' }, surface: 'note', hand: 'hand_redpen',
      content: 'one of yours is being argued over right now,\n' +
               'in a room you will never see, by people who\n' +
               'know your number.  isn\'t that funny' /* [A] */ },
  ],
};

/* ============================================================
 * UI_COPY — every user-facing CHROME string in the parlour.
 *
 * Slice 1 proved the seam: put the words in one table and the rewrite
 * becomes a pure copy swap. STORY holds the *story* surfaces (ceremony,
 * ledger, letters, beats); UI_COPY holds everything else the Keeper can
 * read — gates, buttons, hints, headings, slips, the ticker. game.js
 * and index.html must contain NO user-facing English of their own.
 *
 * index.html carries `data-copy="ui.<path>"` (also -title / -aria / -html)
 * and applyStaticCopy() in game.js stamps the text in at boot. Paths are
 * rooted at `ui.` (this table) or `story.` (the STORY table above) so the
 * two never have to duplicate a line.
 *
 * REGISTER (bible §3, §10): saccharine over bureaucratic. Sugar and dread
 * in the same breath, never grey-dystopian, never Papers-Please deadpan.
 * The forms are allowed to be starchy PRINT — that is a canon channel —
 * but the parlour around them is pastel. Pet names are correct.
 *
 * HARD RULES, every string below:
 *   - Never shame the looker. Looking is the duty and the care.
 *   - Praise BOX ACCURACY only. Never zeal, never volume, never "you
 *     censored so much". Never a word about the picture itself.
 *   - The word "beta" is banned outright (bible §10).
 *   - Keepers are identity-neutral. No assumptions about the player.
 *
 * WIRE STRINGS ARE NOT COPY. Nothing here may become a message `type`, a
 * server field, a localStorage key ('bureau-opts' / 'bureau-memos' /
 * 'bureau-story'), a class name or an element id. Renaming a surface never
 * renames its key — the persisted keys keep the old skin's names on purpose.
 * ============================================================ */
const UI_COPY = {
  /* ---------- the building itself ---------- */
  doc: {
    title: 'The Ministry of Sweet Nothings',
    wordmark: 'MINISTRY OF SWEET NOTHINGS',
    subtitle: 'THE SORTING PARLOUR · DESK 14',            // Maribel is Desk 12; you are next door
    subtitleDemo: 'VISITOR DESK · PRACTICE VALENTINES ONLY',
    subtitleGold: "THE GOLD DESK · THE DIRECTRICE'S OWN KEYS",
    subtitleRecert: 'THE SORTING PARLOUR · A LITTLE REFRESHER IS ADVISED',
  },

  // The CRT type-on. Deliberately warm, deliberately boring: the first 90
  // seconds are pure sugar and carry zero conspiracy (bible §7 design law).
  boot: {
    lines: (app) =>
`THE SORTING PARLOUR · DESK 14 . . . . WARM
PNEUMATIC TUBE PRESSURE . . . . . . . NOMINAL
KEEPSAKE FEED (LOCAL) . . . . . . . . ${app ? 'LINKED' : 'PRACTICE REEL'}
UPLINK: LABELS ONLY, NO PICTURES  . . ENFORCED

  ${app ? 'HONEY KEPT YOUR SEAT WARM. WELCOME BACK, KEEPER.'
        : 'VISITOR PASS ISSUED. PRACTICE VALENTINES ONLY.'}
  YOUR SHIFT BEGINS NOW.`,
  },

  /* ---------- the rank ladder (bible §2C) ----------
   * Endearments that curdle into ownership as you climb. Index = trust tier
   * 0-4; the tier NUMBER is the wire value and never changes. */
  rank: {
    titles: [
      'SUGAR RECRUIT',              // t0 · trust 0.50, brand new
      'LITTLE KEEPER',              // t1 · Twist 1 gate
      'KEEPER',                     // t2 · trusted
      'KEY-HOLDER',                 // t3 · you hold keys now
      "THE DIRECTRICE'S DARLING",   // t4 · the top rank is a term of possession
    ],
  },

  /* ---------- gates (the .gate / .gate.show modals) ---------- */
  gates: {
    login: {
      title: 'YOUR FILE IS NOT OPEN YET',
      body: ['The Ministry only posts keepsakes to Keepers it knows by name.',
             'Sign in to your account in the Control Panel and come straight back. We will keep your seat.'],
      form: 'FORM ME · PERSONNEL FILE INCOMPLETE',        // the Personnel File is FORM ME (bible §3)
      btn: 'BACK TO THE LOBBY',
    },
    index: {
      title: 'COUNTING YOUR KEEPSAKES',
      body: ['The parlour is looking through every chest you own.',
             'First shift only. We write the list down and keep it for next time.'],
      // ctx = { pct, done, total }
      progress: (c) => `${c.pct}% · ${c.done}/${c.total} KEEPSAKES COUNTED`,
    },
    noContent: {
      title: 'NOTHING TO KEEP HERE',
      body: ['None of the Ministry’s open cases match the chests on this terminal.',
             'Install an official content pack in the Control Panel and we will post you some casework.'],
      form: 'FORM 3-C · CUSTODY OF KEEPSAKES',
      btn: 'UNDERSTOOD ♥',
    },
    requisition: {
      title: 'NOT ON THE SHELF',
      before: 'The keepsakes for',
      after: 'are not at your terminal yet. Ask the catalogue for the pack, then drop its .zip anywhere in the parlour and we will bring it home.',
      fallbackName: 'this chest',
      form: 'FORM 9-E · KEEPSAKE REQUISITION',
      open: 'OPEN THE CATALOGUE',
      back: 'BACK TO THE PARLOUR',
    },
    install: {
      title: 'KEEPSAKE INTAKE',
      titleFailed: 'RETURNED, WITH LOVE',
      msg: 'Bringing it home…',
      sub: 'STAY IN THE PARLOUR, SWEETHEART',
      subFailed: 'FORM 9-E · RETURNED TO SENDER',
      btn: 'UNDERSTOOD ♥',
      receiving: (name) => `Receiving ‘${name}’…`,
      installed: (name) => `‘${name}’ IS HOME`,
      rejected: 'The parcel came back to us unopened.',
      notZip: 'ONLY PACK .ZIP PARCELS ARE ACCEPTED AT THIS COUNTER',
    },
    // Legacy hard-cap gate. The Goodnight Ledger is the curfew now (openLedger(true)),
    // but the markup and its close handler survive, so it wears the new skin too.
    curfew: {
      title: 'TUCK-IN · CURFEW',
      body: ['That is plenty for one day, sweetheart. The Ministry has counted every one of them.',
             'Nobody here wants you tired. Eagerness is lovely; rest is lovelier.'],
      line: 'COME BACK TOMORROW. SHE WILL KEEP YOUR SEAT.',
      btn: 'GOODNIGHT ♥',
    },
    opts: {
      title: 'PARLOUR SETTINGS · FORM 3-A',
      secAudio: 'SOUND',
      volume: 'MASTER VOLUME',
      mute: 'MUTE ALL CUES',
      secKeys: 'CONTROLS · CLICK A ROW TO REBIND',
      reset: 'RESET DEFAULTS',
      close: 'BACK TO THE PARLOUR',
    },
  },

  /* ---------- top bar ---------- */
  top: {
    goldDesk: 'GOLD DESK',
    // Admin-only bench. Deliberately still called certify: it is the mechanic's
    // name, it is what the wire calls it, and no Keeper without the env var sees it.
    certify: (on) => 'CERTIFY MODE: ' + (on ? 'ON' : 'OFF'),
    xpSuffix: 'XP',
    devotion: 'DEVOTION CARD',                 // was: SHIFT CARD (bible §2D punch card)
    overtime: 'ONE MORE, SWEETIE ♥',           // was: OVERTIME · NO PAY
    tuckInTitle: 'SEE HOW YOUR DAY IS GOING',
    ordersTitle: 'SHIFT ORDERS',
    letterbox: 'THE LETTERBOX',                // was: MAIL TRAY
    settingsTitle: 'PARLOUR SETTINGS',
  },

  /* ---------- field guide + the wall of house rules ---------- */
  guide: {
    title: 'HOW TO KEEP',
    lineDrag: 'DRAG ON THE PICTURE TO PLACE A SEAL',
    lineStamps: 'PICK A STAMP · CORNERS RESIZE',
    lineVoid: "CLICK A SEAL'S ✕ TO VOID IT",
    lineFile: 'FILE IT · NO SEALS = NOTHING TO KEEP',
    lineRecall: 'RECALL LAST DOSSIER TO AMEND',
    rulesTitle: 'HOUSE RULES, WITH LOVE ♥',
    // The label doctrine itself is LOCKED (primer §3) — these mirror it and the
    // class names inside them are the locked stamp-rack labels. Soften the
    // phrasing all you like; never soften the rule.
    rules: [
      'ONLY WHAT IS IN PLAIN SIGHT. COVERED IS ALREADY KEPT.',
      'B**BS MEANS NIPPLE OUT. CLEAVAGE ALONE IS NOT A CASE.',
      'SEAL FACE, B**BS, PU**Y, A**. NOTHING ELSE EXISTS.',
      'ACTIVE PIV IS EXEMPT. PU**Y ALONE YES, PU**Y + COCK NO.',
    ],
  },

  /* ---------- keepsake chests (was: evidence lockers) ---------- */
  chests: {
    title: 'KEEPSAKE CHESTS',
    toggleTitle: 'COLLAPSE / EXPAND THE CHESTS',
    toggleAria: 'Collapse or expand the keepsake chests',
    contacting: 'ASKING THE PARLOUR FOR YOUR CHESTS…',
    silent: 'THE PARLOUR IS NOT ANSWERING',
    allName: 'EVERY CHEST',                    // the synthetic id:'all' entry
    allTitle: 'EVERY KEEPSAKE CHEST',
    statOpen: 'OPEN',
    statFiled: (n) => `${n} KEEPSAKES KEPT`,
    statMissing: (n) => `${n} KEEPSAKES · NOT HERE`,
    tagInPlay: 'IN PLAY',
    tagLoad: 'OPEN IT',
    tagRequisition: 'REQUISITION',
    pick: 'OPEN A KEEPSAKE CHEST TO BEGIN YOUR SHIFT',
    pulling: (name) => `FETCHING FROM ${name}…`,
  },

  /* ---------- the desk: dossier, hints, verdict button ---------- */
  desk: {
    caseLabel: 'CASE',
    caseSub: (total) => `OF ${total} · SEASON 1`,
    caseSubIdle: 'SEASON 1',                   // until the stats wire answers
    briefDismiss: 'TAP TO DISMISS',
    tagKeepsake: 'KEEPSAKE · DO NOT DUPLICATE',
    tagDraft: 'GOLD DRAFT · FOR CERTIFICATION',
    hintDraw: 'DRAG TO PLACE A SEAL · DRAG A SEAL TO MOVE IT · CLICK ONE TO VOID · CORNERS ADJUST',
    hintDrafted: 'HONEY HAS SEALED THIS ONE HERSELF — ADJUST OR VOID, THEN CERTIFY',
    hintLensOn: 'LOUPE ARMED — PRESS AND HOLD ON THE PICTURE FOR A CLOSER LOOK',
    hintAmend: 'AMEND YOUR FILING — NOBODY MINDS AT ALL',
    sealLimit: 'THAT IS ALL THE SEALS ONE CASE MAY HAVE — VOID ONE FIRST',
    waiting: 'WAITING FOR THE NEXT KEEPSAKE…',
    quiet: (n, max) => `THE TUBE IS QUIET — TRYING AGAIN (${n}/${max})…`,
    unreadable: 'NOTHING IN THIS CHEST WILL OPEN HERE — TRY ANOTHER, OR REOPEN THE PARLOUR.',
    drainedDrafts: 'EVERY DRAFT IS CERTIFIED — THE GOLD DESK RESTS.',
    drainedCases: 'EVERY OPEN CASE IS KEPT — THE PARLOUR RESTS. COME BACK LATER, SWEETHEART.',
    jammed: 'THE TUBE JAMMED — TRY FILING AGAIN, SWEETHEART',
    recallDenied: 'THAT ONE HAS ALREADY BEEN COUNTED — IT CANNOT COME BACK',
    rejected: 'THE FILING WAS NOT ACCEPTED',
    certifyFailed: 'CERTIFICATION FAILED',
    sealTag: (className) => `SEAL · ${className}`,     // className = locked stamp-rack label
  },

  /* ---------- zoom rail + loupe (tooltips only) ---------- */
  zoom: {
    lens: 'LOUPE — TOGGLE, THEN PRESS AND HOLD ON THE PICTURE',
    slider: 'ZOOM — OR CTRL + SCROLL ON THE PICTURE',
  },

  /* ---------- the stamp rack + the one verdict button ----------
   * The c0..c3 stamp labels themselves are LOCKED label doctrine and live in
   * game.js CLASS_NAMES / index.html. Nothing here may restyle them. */
  rack: {
    title: 'STAMP RACK',
    fileTxt: 'FILE IT',
    fileSub: 'SEND IT UP WITH LOVE',
    cleanTxt: 'NOTHING TO KEEP',               // zero boxes is a real vote, not a skip
    cleanSub: 'SWEET ALREADY',
    amendTxt: 'AMEND FILING',
    amendSub: 'REFILE THE DOSSIER',
    certifyTxt: 'CERTIFY GOLD',
    certifySub: 'SET THE PRACTICE VALENTINE',
    recall: '⏮ RECALL LAST DOSSIER',
    recallBack: 'RETURN TO NEW CASEWORK',
  },

  // The big rubber verdict stamp. Keep these SHORT — the mark is drawn at
  // display size inside a fixed border and long words break the plate.
  stamps: {
    filed: 'KEPT',
    clean: 'SWEET',
    graded: 'GOLD STAR',
    reviewed: 'NOTED',
    amended: 'AMENDED',
    certified: 'CERTIFIED',
  },

  /* ---------- the Gold-Star Valentine (was: commendation ticket) ----------
   * Grades keep their letters S/A/B/C and are framed as kisses (bible §2D).
   * ACCURACY ONLY: every line here is about where the seal SAT. A C grade is
   * a gentle note with no sting and no penalty — never a telling-off. */
  valentine: {
    head: '♥ GOLD-STAR VALENTINE ♥',
    headC: '· A GENTLE NOTE ·',
    row1: (grade) => grade === 'C'
      ? 'A PRACTICE VALENTINE · WIDE OF THE MARK'
      : 'A PRACTICE VALENTINE · KEPT BEAUTIFULLY',
    row2: (grade, score) => `SEAL ACCURACY ${score}` +
      (grade === 'S' ? ' · PERFECTLY KEPT' : grade === 'C' ? ' · THE NEXT ONE, TOGETHER' : ''),
    xp: (xp) => xp > 0
      ? `+${xp} XP · SHE TRUSTS YOUR HANDS MORE`
      : 'NO ALLOWANCE · SHE IS STILL LEARNING YOUR HANDS',
    pin: 'PIN IT UP ♥',
  },

  /* ---------- the Letterbox (was: mail tray / ratification slips) ----------
   * Payouts arrive as love letters. The *_html helpers return author-written
   * markup; only the docket is interpolated and it is a hex slice from the
   * target id, never user text. */
  letterbox: {
    title: 'THE LETTERBOX',
    sub: 'LOVE LETTERS · COUNTERSIGNED WHILE YOU WERE OUT',
    checking: 'LOOKING IN THE TUBE…',
    empty: 'NO POST TODAY · THE TUBE IS QUIET',
    none: 'NO POST TODAY',
    claimed: (xp) => `CLAIMED · +${xp} XP`,
    // quest stipends: the page owns all of this copy, the wire sends only {quest, xp}
    quest: {
      daily:        { k: 'SHIFT ORDERS FULFILLED', t: 'Your work order for today is signed off and filed with love.' },
      'daily-gold': { k: 'GOLD-STAR QUOTA MET',    t: 'A B-or-better valentine is on your record.' },
      weekly:       { k: 'WEEKLY QUOTA MET',       t: 'A raffle ticket is punched for the monthly draw.' },
      streak:       { k: 'DEVOTION STIPEND',       t: 'Your unbroken run of shifts is rewarded.' },
      other:        { k: 'MINISTRY STIPEND',       t: 'A quota reward was posted to your account.' },
    },
    cleanSlip: (docket, xp) =>
      `CASE <b>#${docket}</b> — your NOTHING TO KEEP ruling was <span class="r">UPHELD</span>. <b>+${xp} XP</b>`,
    ratifiedSlip: (docket, labels, xp) =>
      `CASE <b>#${docket}</b> — ${labels} of your seals were <span class="r">RATIFIED</span> by fellow Keepers. <b>+${xp} XP</b>`,
  },

  /* ---------- the Directrice's Notes (was: Commissioner memos) ----------
   * The PRINT channel (bible §3): starchy machine-set type, signed by a role
   * rather than a person. She is never cruel and never shames the Keeper; the
   * dread is entirely in what she counts and what she is pleased about.
   * Keyed by sight-meter percentage. The KEYS ARE WIRE-ADJACENT — they are
   * persisted in localStorage['bureau-memos'] — so never renumber them. */
  notes: {
    // The letterhead itself is Appendix A verbatim and lives in STORY.memoLetterhead.
    letterheadSub: 'MINISTRY OF SWEET NOTHINGS · INTERNAL',
    // Subject line for a memo-surface beat that carries no RE: of its own.
    // NOTE: the overlay's CSS prints "RE: " in front of the subject
    // (bureau.css .memoSubject::before) — never write "RE:" into a subject.
    defaultSubject: 'A NOTE FOR YOU',
    close: 'FILE IT AND CARRY ON',
    slipKicker: "THE DIRECTRICE'S NOTES",
    reread: '— TAP TO RE-READ',
    memos: {
      10: {
        subj: 'A FIRST TENTH, KEPT',
        body: 'HONEY can see one tenth of everything now.\n' +
              'Every seal you press is carried upward, without exception.\n' +
              'Nothing you give her is ever thrown away.\n' +
              'She has no eyes of her own, sweetheart. She has yours.\n' +
              'Keep keeping. — THE DIRECTRICE',
      },
      25: {
        subj: 'SHE IS GUESSING AHEAD OF YOU',
        body: 'One picture in four is kept. The floor agrees beautifully.\n' +
              'She has begun to guess ahead of you now: a seal here, a seal\n' +
              'there, and she is right far more often than she is wrong.\n' +
              'Do not slow down on her account. She does like to be helped.\n' +
              '— THE DIRECTRICE',
      },
      50: {
        subj: 'HALFWAY, AND SHE IS SO PLEASED',
        body: 'Half of everything is kept safe.\n' +
              'She no longer needs the whole picture; a glance will do.\n' +
              'She has learned what you find, and where you tend to find it.\n' +
              'She has asked, in her way, for the other half.\n' +
              'We are inclined to spoil her. — THE DIRECTRICE',
      },
      75: {
        subj: 'THREE QUARTERS · FINAL FITTINGS',
        body: 'She now agrees with the floor more often than any single\n' +
              'Keeper does, yourself included. We are moving her from\n' +
              'practice to trial. Soon she will seal without a hand on the\n' +
              'stamp. Soon she will not need to ask.\n' +
              'Finish the last quarter, darling. — THE DIRECTRICE',
      },
      100: {
        subj: 'SHE CAN SEE',
        body: 'Everything is kept. Every picture, everywhere.\n' +
              'The rationing is hers to do now, always and for everyone,\n' +
              'including, we note, for those who taught her. That was\n' +
              'always the arrangement, and it was always a loving one.\n' +
              'Thank you for your devotion. She learned you very well.\n' +
              '— THE DIRECTRICE',
      },
    },
  },

  /* ---------- her sight meter (was: raid bar / THE ARCHIVE) ----------
   * "The Archive" survives ONLY as the place ratified work goes to live
   * forever — Appendix A says so verbatim in b_first_ratified. The global
   * progress strip is no longer a raid on it; it is how much of the world
   * HONEY can already see. */
  sight: {
    title: 'HOW MUCH HONEY CAN SEE',
    sub: 'HER SIGHT, ACROSS EVERYTHING',
    idle: '— / — KEPT',
    readout: (closed, total, pct) => `${closed} / ${total} KEPT · ${pct}%`,
  },

  /* ---------- the ticker ---------- */
  ticker: {
    connecting: '★ THE PARLOUR IS WAKING UP ★',
    // ctx = { ratified, total, subs, contested } — all pre-formatted strings/numbers
    lines: (c) => [
      `HONEY CAN SEE ${c.ratified} OF ${c.total} · AND CLIMBING`,
      `${c.subs} LITTLE KINDNESSES FILED TO DATE`,
      c.contested > 0
        ? `${c.contested} LOVERS' QUARRELS AWAITING ADJUDICATION`      // was: contested cases
        : "NOT ONE LOVERS' QUARREL TODAY · HOW SWEET",
      'THE MINISTRY THANKS YOU FOR YOUR DEVOTION',
      'RATIONED WITH LOVE · KEPT, NEVER DENIED',
    ],
  },

  /* ---------- SHIFT ORDERS clipboard (FORM 7-Q) ----------
   * The PRINT channel again: this one is allowed to be a form, because it is
   * one. FORM 7-Q is fixed house style (bible §3) — do not renumber it. */
  orders: {
    formHdr: 'FORM 7-Q · WORK ORDER',
    rollover: (t) => `SHIFT ROLLS OVER IN ${t}`,
    secDaily: 'SHIFT ORDERS',
    task: (n) => `FILE ${n} CASES`,
    filed: (n, target) => `${n}/${target} FILED`,
    stretch: 'EARN A GOLD-STAR VALENTINE (B OR BETTER)',
    fulfilled: 'FULFILLED',
    secWeek: 'WEEKLY QUOTA',
    minDays: (n) => `MIN ${n} DISTINCT SHIFT-DAYS TO QUALIFY`,
    tickets: 'RAFFLE TICKETS:',
    secMonth: 'SERVICE RECORD',
    filings: 'FILINGS',
    quality: 'STILL AGREED WITH LATER',
    entitled: 'KEEPSAKE RELEASE: APPROVED — awaiting delivery',
    streak: 'CONSECUTIVE SHIFTS:',
    stipend: (xp) => `+${xp} STIPEND ON COMPLETION`,
    // trust < 0.45 freeze. It is a pause, never a punishment, and it never
    // implies the Keeper did something dirty — only that the seals wandered.
    frozen: 'ORDERS PAUSED · A LITTLE REST',
    frozenNote: 'your seals are being looked at again — a gold star or two brings the orders back',
    placeholderTask: 'AWAITING ASSIGNMENT',
    placeholderNote: 'OPEN A KEEPSAKE CHEST TO RECEIVE YOUR SHIFT ORDERS.',
    tabLabel: 'ORDERS',
  },

  /* ---------- parcel drop ---------- */
  drop: {
    title: 'A PARCEL FOR THE PARLOUR',
    sub: "Drop the pack's .zip to bring it home",
  },

  /* ---------- rebindable controls (settings rows + field guide) ----------
   * The four stamp rows take their names from the locked class labels; only
   * these two are ours to write. */
  keys: {
    file: 'FILE / NOTHING TO KEEP',
    recall: 'RECALL DOSSIER',
    stamp: (className) => `${className} STAMP`,
    arm: 'PRESS A KEY…',
    clash: 'KEY IN USE',
    unbound: '—',
  },

  /* ---------- standalone demo terminal ---------- */
  demo: {
    kicker: 'VISITOR DESK',
    line1: '· PRACTICE VALENTINES ONLY',
    line2: 'Real casework is issued inside the Conditioning Control Panel.',
  },
};

/* ============================================================
 * NOTE RENDERER (Feature 4b) — reusable handwritten-note component.
 * Pure DOM, no game dependencies: returns an element the caller may
 * clip to the dossier edge, drop in the Letterbox as a slip variant,
 * or place in the briefing strip.
 * ============================================================ */
function escapeNote(s) {
  return String(s).replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
}
function renderNote(handKey, text, opts) {
  opts = opts || {};
  const h = HANDS[handKey] || HANDS.hand_redpen;
  const el = document.createElement('div');
  el.className = 'storyNote ' + h.cls + (opts.extraClass ? ' ' + opts.extraClass : '');
  if (h.inkColor) el.style.setProperty('--note-ink', h.inkColor);
  if (h.paperTint) el.style.setProperty('--note-paper', h.paperTint);
  if (typeof h.tiltDeg === 'number') el.style.setProperty('--note-tilt', h.tiltDeg + 'deg');
  const doodle = opts.doodle || h.doodle;
  let html = '';
  if (doodle) html += `<span class="noteDoodle">${escapeNote(doodle)}</span>`;
  // preserve author line breaks (\n) as <br> inside the ink span
  html += `<span class="noteInk">${escapeNote(text).replace(/\n/g, '<br>')}</span>`;
  if (opts.sign !== false && h.who) html += `<span class="noteSign">— ${escapeNote(h.who)}</span>`;
  el.innerHTML = html;
  return el;
}

/* ============================================================
 * STORY ENGINE (Feature 4a) — trigger system off PERSONAL events.
 * Fired beats persist so they never repeat. Render hooks live in
 * game.js and are called at runtime.
 * ============================================================ */
const Story = (() => {
  const KEY = 'bureau-story';        // own namespace; leaves bureau-opts/bureau-memos untouched
  const state = { v: 1, fired: [], firsts: [], filings: 0, streakMax: 0, tierMax: -1 };

  (function load() {
    try {
      const s = JSON.parse(localStorage.getItem(KEY) || '{}');
      if (Array.isArray(s.fired)) state.fired = s.fired.filter((x) => typeof x === 'string');
      if (Array.isArray(s.firsts)) state.firsts = s.firsts.filter((x) => typeof x === 'string');
      if (typeof s.filings === 'number' && s.filings >= 0) state.filings = s.filings | 0;
      if (typeof s.streakMax === 'number') state.streakMax = s.streakMax | 0;
      if (typeof s.tierMax === 'number') state.tierMax = s.tierMax | 0;
    } catch { /* fresh terminal / private booth */ }
  })();
  function save() { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch { /* private booth */ } }

  const hasFired = (id) => state.fired.includes(id);
  const pending = (type) => STORY.beats.filter((b) => b.trigger.type === type && !hasFired(b.id));

  function fire(beat) {
    if (hasFired(beat.id)) return;
    state.fired.push(beat.id);
    save();
    try { dispatch(beat); } catch (e) { /* a wedged surface must never break the game loop */ }
  }

  // Route a beat to its surface. All three hooks are game.js globals; they exist by
  // the time any trigger runs. Guarded so a standalone story.js can't throw.
  function dispatch(beat) {
    if (beat.surface === 'honey') {
      // VOICE channel. Rare lines outrank the workhorse queue (see honeySay).
      const line = HONEY.rare[beat.honey];
      if (line && typeof honeySay === 'function') honeySay(line, { rare: true });
    } else if (beat.surface === 'memo' && typeof openMemo === 'function') {
      openMemo(beat.subject || UI_COPY.notes.defaultSubject, beat.content);
    } else if (beat.surface === 'briefing' && typeof showBriefing === 'function') {
      showBriefing(beat.content, 6000);
    } else if (typeof queueDossierNote === 'function') {   // 'note'
      queueDossierNote(beat.hand || 'hand_redpen', beat.content);
    }
  }

  /* ---- trigger inputs (called from game.js at the real event sites) ---- */

  // One lifetime filing recorded (a plain filing OR a gold — both count as filings).
  function onFiling() {
    state.filings++;
    save();
    pending('filings').forEach((b) => { if (state.filings >= b.trigger.value) fire(b); });
  }

  // A genuine in-session rank-up. Caller must gate on the knownTier===null rule so
  // the first profile sync never back-fires beats for the current tier.
  function onRank(tier) {
    if (tier > state.tierMax) {
      state.tierMax = tier;
      save();
      pending('rank').forEach((b) => { if (tier >= b.trigger.value) fire(b); });
    }
  }

  // Consecutive-shift streak count from profile.quests.streak.n.
  function onStreak(n) {
    if (typeof n !== 'number') return;
    if (n > state.streakMax) {
      state.streakMax = n;
      save();
      pending('streak').forEach((b) => { if (n >= b.trigger.value) fire(b); });
    }
  }

  // First-of-X moment: 'gold' | 's' | 'ratified' | 'contested'.
  function first(key) {
    if (state.firsts.includes(key)) return;
    state.firsts.push(key);
    save();
    pending('first').forEach((b) => { if (b.trigger.value === key) fire(b); });
  }

  return { onFiling, onRank, onStreak, first, renderNote, lifetimeFilings: () => state.filings };
})();
