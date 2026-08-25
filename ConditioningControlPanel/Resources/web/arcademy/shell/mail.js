/* ============================================================================
 * shell/mail.js - THE PHANTOM POST, engine half: the catalog and the postman.
 *
 * The school writes to you. This module owns WHAT it may write and WHEN a
 * letter is allowed to land; shell/mailbox.js owns what the paper looks like
 * once it has. Nothing here touches the DOM, the bridge, a timer or a clock it
 * was not handed, which is the whole reason the delivery rules are testable in
 * node without a school around them.
 *
 * FOUR LAWS
 *
 *  1. THE POSTMAN NEVER OWNS THE POSTBOX. Persistence is INJECTED: a plain
 *     `state` object plus a `save` callback. This file writes into the object
 *     it was given and asks the caller to bank it. It never imports
 *     core/store.js, never posts a meta-command, and never touches
 *     localStorage (there is none in this bundle, deliberately). The driver
 *     wires it to the store in Wave 4 - see STATE-NEEDS below.
 *
 *  2. ONE LETTER PER `deliver()`. The mailbox is a slow drip, not an inbox
 *     flood: however many letters qualify tonight, a call hands over exactly
 *     one, in catalog order, and the rest wait for the next call. A campus
 *     arrival is one call. That single rule is what stops a returning player
 *     from opening the box to six unread envelopes.
 *
 *  3. A GATE NOBODY IMPLEMENTS HOLDS THE LETTER. Triggers are a table of named
 *     clauses (the spirit of emi/voice.js's predicates, self-contained here),
 *     and an unknown clause name closes the letter rather than opening it -
 *     logged once per name, per session. A typo costs a letter that never
 *     arrives, never a letter that arrives to the wrong player on the wrong
 *     night.
 *
 *  4. THE CATALOG HOLDS LETTERS AND NOTHING ELSE. Every visible string in the
 *     table is the copy exactly as it goes on the paper. Chrome strings
 *     (buttons, labels) are NOT here at all: they are lexicon rows in
 *     mailbox.js, because a letter body is content and a button caption is
 *     chrome.
 *
 * ----------------------------------------------------------------------------
 * STATE-NEEDS  (the Wave 4 driver owns every line of this; this file owns none)
 * ----------------------------------------------------------------------------
 *
 * KEY          `mail`, one page-owned top-level key in the C# meta store,
 *              written through core/store.js's ordinary seam
 *              (`store.set('mail', blob)`), exactly the way `emi` is. It is
 *              NOT host-owned: no C# change is needed, and the blob is a few
 *              hundred bytes, well inside the 32KB per-value cap.
 *
 * SHAPE        {
 *                v: 1,                        // MAIL_STATE_VERSION
 *                letters: {
 *                  '<letterId>': {
 *                    deliveredAt: 1756070000000,   // epoch ms, the moment it landed
 *                    readAt: null                  // epoch ms once opened, else null
 *                  }
 *                }
 *              }
 *              `deliveredAt` and `readAt` are the only two facts kept per
 *              letter, and both are epoch ms so a "which local day" question is
 *              answered from the clock at read time (trap 8: dates on this page
 *              are LOCAL). An unknown id in the blob is ignored and preserved,
 *              so a letter retired from the catalog cannot corrupt the box.
 *
 * SAVE         `save(state)` is called after EVERY mutation (a delivery, a
 *              read). The driver should debounce it the way emi/index.js
 *              debounces its own writes; this file deliberately does not, so
 *              the caller keeps control of how chatty the bridge gets.
 *
 * CONTEXT      `ctx` is what the shell knows tonight. Either a plain object or
 *              a function returning one (re-read on every call, so a long-lived
 *              engine never paints from a stale night):
 *                day        {number}  school days attended (store `days`)
 *                punches    {number}  stamps earned across every card
 *                streak     {number}  the HOST-owned attendance streak
 *                dateIs     {string}  today, LOCAL, as 'MM-DD'
 *                seenFlags  {Object}  flag -> truthy, whatever the shell has
 *                                     already shown this save (annex reveal,
 *                                     orientation, first bell ...)
 *              Every field is optional; a missing one reads as 0 / '' / {} and
 *              simply holds the letters that asked about it.
 *
 * WHEN TO CALL `deliver()` once per arrival at the campus, after the store has
 *              settled. `pending()` is the same question without the side
 *              effect (useful for a suite or a debug door).
 *
 * COUNTERS     The `mailRead` family (and any achievement hanging off it) is
 *              the driver's, not this file's. This module counts nothing it
 *              does not need to decide a delivery.
 *
 * LEXICON      No rows here. mailbox.js declares the `mail_*` chrome rows the
 *              host's NeutralLexicon must mirror.
 * ==========================================================================*/

/** Bump only when the blob's SHAPE changes; a reader tolerates an older one. */
export const MAIL_STATE_VERSION = 1;

/* ----------------------------------------------------------------------------
 * THE LETTERHEADS
 *
 * One row per sender key. Nothing in here is a colour or a pixel: each field is
 * a TOKEN that shell/mail.css turns into a treatment, so the paper is dressed
 * from this table and a new sender is a row plus a selector, never a new
 * layout. `accent` names a shell palette token (styles.css :root), which is how
 * a mod's `init.palette` reskins the whole postbox for free.
 *
 *   accent  pink | lav | gold | slate | ink   the rule, the seal, the head line
 *   rule    double | single | dotted | none | torn   the hairline under the head
 *   mark    seal | crest | stamp | none       the thing pressed into the corner
 *   paper   cream | plain | pulp | note | grey  the sheet itself
 * -------------------------------------------------------------------------- */
export const LETTERHEADS = Object.freeze({
  office: Object.freeze({ id: 'office', accent: 'pink', rule: 'double', mark: 'seal', paper: 'cream' }),
  faculty: Object.freeze({ id: 'faculty', accent: 'lav', rule: 'single', mark: 'crest', paper: 'plain' }),
  notice: Object.freeze({ id: 'notice', accent: 'gold', rule: 'dotted', mark: 'stamp', paper: 'pulp' }),
  personal: Object.freeze({ id: 'personal', accent: 'ink', rule: 'none', mark: 'none', paper: 'note' }),
  unsigned: Object.freeze({ id: 'unsigned', accent: 'slate', rule: 'torn', mark: 'none', paper: 'grey' }),
});

/** The fallback treatment for a letter whose letterhead key has no row. */
export const DEFAULT_LETTERHEAD = LETTERHEADS.office;

/* ----------------------------------------------------------------------------
 * THE CATALOG
 *
 * The season's letters, in delivery-priority order: dated letters first, then
 * the term's correspondence, then the standing circulars. Lengths vary: a
 * one-paragraph note and a four-paragraph letter break the paper in different
 * places, and the order here is the order the box hands them over.
 *
 * A row is:
 *   id          {string}   stable, never re-used, never renamed (it is the
 *                          state key AND what `afterRead` clauses point at)
 *   from        {string}   who it is from, as printed on the paper
 *   letterhead  {string}   a LETTERHEADS key
 *   heading     {string}   the head line on the paper
 *   body        {string[]} paragraphs, in order
 *   trigger     {Object}   clause -> argument, ALL of which must hold. `{}`
 *                          means "the moment the box exists".
 *   once        {boolean}  true (default) = it arrives once in a save's life.
 *                          false = it may arrive again on a LATER local day,
 *                          and only once the previous copy has been read.
 * -------------------------------------------------------------------------- */
export const MAIL = Object.freeze([
  Object.freeze({
    id: 'm23',
    from: 'Miss Pearl Tartine, Keeper of the Kitchens',
    letterhead: 'faculty',
    heading: 'The All Hallows Supper, a Menu and a Warning',
    body: Object.freeze([
      'On the last evening of October the kitchen sets its long table for the All Hallows supper, as it has every year since what happened at the Ashby fair, which I mention only to say that I will not be mentioning it, and the menu is as follows: the dark soup, which is the Tuesday soup wearing a cape, and I will hear no complaints about it; the bone bread, which contains no bones and never has, whatever was said in 1994; and the toffee apples, five stars, awarded in advance because I have made them for thirty-two years and my hand does not tremble.',
      'Ghosts are welcome at the long table, monsieur, they always have been, and I will say only this to any spirit thinking of leaving a review among the napkins: even the dead sign their work. Bon appettite.',
      'From the kitchen, with what remains of my patience,',
      'P. Tartine',
    ]),
    trigger: Object.freeze({ dateIs: '10-31' }),
    once: false,
  }),
  Object.freeze({
    id: 'm24',
    from: 'Mr. Theodore Pendle, Keeper of the Bell Tower',
    letterhead: 'faculty',
    heading: 'Log Extract, the Turning of the Year',
    body: Object.freeze([
      'The first of January, frost, stars very clear above the tower, the kind of night the glass approves of. The year turned at true midnight, which I observed alone with a cup of tea, as the keeper before me did until the fog year, and it turned again four minutes later for those keeping the other one, and I remained at the rail out of courtesy until both parties were safely across.',
      'The bells rang the new year in, three of them, the fourth resting through this year as it rested through the last, and if there is a better way to begin a January than hearing a cold bell ring true, I have not met it and do not expect to. To all keeping either time, a good year, fairly kept.',
      'Kept, T. Pendle.',
    ]),
    trigger: Object.freeze({ dateIs: '01-01' }),
    once: false,
  }),
  Object.freeze({
    id: 'm01',
    from: 'Mr. Aldous Petch, Acting Deputy Head',
    letterhead: 'office',
    heading: 'Circular to the Student Body: Resumption of Term',
    body: Object.freeze([
      'Further to my circular of Tuesday last, which a number of colleagues will not have had sight of, it having been issued during the vacation as a matter of prudence, I am pleased to confirm that the autumn term has commenced, and that it has done so on schedule, a phrase I use advisedly and in the tower\'s hearing. The Main Hall diary is now open for bookings of every kind and will be administered fairly, by me, as it has been these thirty-one years.',
      'Colleagues wishing to consult the diary will find it in my office, in the tall cabinet by the window, second drawer, the one that wants lifting slightly as you pull, a knack I am happy to demonstrate by appointment. I look forward to a fulsome year for the school and for all who serve her.',
      'Yours in continuity,',
      'A. Petch, Acting Deputy Head',
    ]),
    trigger: Object.freeze({}),
    once: true,
  }),
  Object.freeze({
    id: 'm02',
    from: 'Miss Pearl Tartine, Keeper of the Kitchens',
    letterhead: 'faculty',
    heading: 'An Open Letter to the One Who Signs Himself The Palate',
    body: Object.freeze([
      'Thirty-two years I have kept this kitchen, monsieur, through the winter of the burst pipe and the year the ovens took against me, and in all that time I have asked for nothing but what any artist asks, which is to be judged to her face. Instead you return after decades of silence, unsigned as ever, and you award my Tuesday soup three stars and one half, as though the half were a kindness, as though I would not feel the missing half more keenly than the whole.',
      'Let me say plainly that the soup this week achieved five stars, a rating I do not give lightly and have never once withheld, and that the kitchen keeps its receipts, monsieur, going back well past 1994. Name yourself, and the soup will be waiting, and so, with somewhat less warmth, will I. Bonne apetit.',
      'From the kitchen, with what remains of my patience,',
      'P. Tartine',
    ]),
    trigger: Object.freeze({ daysAfterIssueRead: ['g2', 1] }),
    once: true,
  }),
  Object.freeze({
    id: 'm03',
    from: 'Mr. Aldous Petch, Acting Deputy Head',
    letterhead: 'office',
    heading: 'Circular: Constitution of an Inquiry into the Whereabouts of the Main Hall Sign-Up Sheet',
    body: Object.freeze([
      'Further to my circular of Thursday last, pinned to the Notice Board at eleven minutes past three and no longer to be found upon it, I must inform colleagues that the Main Hall sign-up sheet has been removed by a person, or persons, or by some agency of weather or of nature, unknown. An inquiry is hereby constituted. It will be led by myself, assisted by myself, and I have accepted both appointments with a heavy sense of duty and no other candidates.',
      'Colleagues with information are invited to bring it to my office in confidence, where it will be kept alphabetically in the drawer I reserve for matters of this kind, between the complaints of the kitchen and those of the Music Room, which I mention only to reassure both parties of my continued neutrality. The inquiry will leave no drawer unopened, beginning with the tall cabinet by the window.',
      'Yours in continuity,',
      'A. Petch, Acting Deputy Head and Convenor of the Inquiry',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m02', 2] }),
    once: true,
  }),
  Object.freeze({
    id: 'm04',
    from: 'Mr. Theodore Pendle, Keeper of the Bell Tower',
    letterhead: 'faculty',
    heading: 'Deposition to the Inquiry, Entered Voluntarily',
    body: Object.freeze([
      'Thursday the ninth, mist off the Quad until ten, then bright. I pinned nothing and I removed nothing, but I can give the inquiry the one fact it will get from anybody, which is that the sheet went up at eleven minutes past three by the front desk\'s reckoning, an instant which, by tower time, never occurred, the tower having already reached a quarter past.',
      'Whether a thing pinned at a time that never happened can honestly be said to have hung there at all is a question for wiser heads than mine, and I decline to speculate further, except to note that the wind that afternoon was from the southwest, which it usually is when things go missing.',
      'Kept, T. Pendle.',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m03', 1] }),
    once: true,
  }),
  Object.freeze({
    id: 'm05',
    from: 'Dr. F. Sharp, Mistress of Music',
    letterhead: 'faculty',
    heading: 'A Statement I Make Freely, Naming No One',
    body: Object.freeze([
      'I will not say who took the sheet, because I was raised better than that, and because in Vienna we were taught that an accusation is a note held too long, souring everything around it. I will say only that the sheet hung at a perfectly reasonable height for any person of ordinary reach, that the kitchens keep long hours, and that certain arts on this campus involve the daily handling of paper, of pins, and of grudges, while mine involves only music.',
      'The Recital, I should add, continues to mature, and revision forty four is now in second thoughts of the most promising kind, whatever certain luncheon interests may wish. I am perfectly andante about the whole affair, and anyone who says otherwise may consult my rebuttal, which is in preparation.',
      'Dr. F. Sharp, Music Room',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m04', 1] }),
    once: true,
  }),
  Object.freeze({
    id: 'm06',
    from: 'Miss Pearl Tartine, Keeper of the Kitchens',
    letterhead: 'faculty',
    heading: 'On the Matter of the Sheet, Which I Shall Not Stoop to Discuss',
    body: Object.freeze([
      'I have been asked whether the kitchen had sight of the sign-up sheet, and I answer as I answer all things, openly, and with the dignity of my station. The kitchen bakes, monsieur, it does not steal. If anyone wishes to know where I was on the afternoon in question, I was where I am every afternoon, coaxing a reduction that would have made my grandmother weep, five stars, a rating I did not give lightly then and do not regret now.',
      'I note merely that some people rehearse with their hands empty and their evenings free, and that a woman who can carry a programme through forty four revisions could carry a single sheet of paper anywhere she pleased. I accuse no one, and the kitchen, as ever, keeps its receipts. Bon appettit.',
      'From the kitchen, with what remains of my patience,',
      'P. Tartine',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m05', 1] }),
    once: true,
  }),
  Object.freeze({
    id: 'm07',
    from: 'the Headmistress',
    letterhead: 'personal',
    heading: 'On the Autumn Programme',
    body: Object.freeze([
      'I have read the autumn programme with great care and greater fondness, and I find that it does the school credit, whichever of the two evenings it turns out to be. The Main Hall has held every kind of triumph in its time, and we are confident it is about to hold another, for the school has never yet disappointed us in autumn. You will forgive me for not attending in person, as is my custom, but we shall be thinking of the evening with the particular pride we reserve for things we do not need to see to believe in.',
      'E.',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m06', 2] }),
    once: true,
  }),
  Object.freeze({
    id: 'm08',
    from: 'Dr. F. Sharp, Mistress of Music',
    letterhead: 'faculty',
    heading: 'To the Kitchens, in a Spirit of Harmony',
    body: Object.freeze([
      'In Vienna, when two great houses quarrelled, they did not write letters, they programmed together, and it is in that spirit that I extend to the kitchens an offer I make freely and against the advice of nobody, since I consulted nobody. Let the Luncheon and the Recital cease circling one another like two halves of the same unplayed chord.',
      'I propose that the Recital programme carry, in type of a perfectly respectable size, a catering credit naming the kitchen, its keeper, and, if space allows, the soup. Revision forty five, which is already maturing into what I believe will be my finest, has room on its final page between the acknowledgements and the misprints, and I can think of no better company for either. I am feeling molto legato about our two arts, and I hope the kitchen will feel the same.',
      'Dr. F. Sharp, Music Room',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m07', 2] }),
    once: true,
  }),
  Object.freeze({
    id: 'm09',
    from: 'Miss Pearl Tartine, Keeper of the Kitchens',
    letterhead: 'faculty',
    heading: 'The Kitchen Answers, and Raises',
    body: Object.freeze([
      'I have received the Music Room\'s offer and I have read it twice, once with suspicion and once with something I am prepared, on reflection, to call emotion. The kitchen accepts, and the kitchen, which has never in its history been out-given, responds in kind: the Harvest Luncheon will close with a dessert course composed in honour of the Recital itself, a programme of seven movements in sugar, each one named for a revision that mattered.',
      'What the caramel will take from me, only the pan knows, but great alliances are not built on shallow syrup. Five stars, I say in advance and without hesitation, for I know what I am capable of when moved. Bonne appetite, my new friend, bonne appetite to us all.',
      'From the kitchen, with what remains of my patience,',
      'P. Tartine',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m08', 1] }),
    once: true,
  }),
  Object.freeze({
    id: 'm10',
    from: 'The Palate',
    letterhead: 'unsigned',
    heading: 'A Review of the Merger',
    body: Object.freeze([
      'One had hoped, when the two great institutions of this campus joined hands, that the result might rise like a good loaf, slowly and with structure. Instead the alliance has been rushed to table. The gesture is generous, the execution sentimental, and the whole, though warm, wants seasoning, for goodwill is not a flavour, whatever the posters say, and so one is obliged to award the affair two stars, for it needed salt.',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m09', 1] }),
    once: true,
  }),
  Object.freeze({
    id: 'm11',
    from: 'Dr. F. Sharp, Mistress of Music',
    letterhead: 'faculty',
    heading: 'On the Poster, and the End of an Understanding',
    body: Object.freeze([
      'In Vienna the soloist\'s name goes above the orchestra\'s, not from vanity but from physics, since that is where the eye begins, and I had assumed, wrongly as it now proves, that my partner in the late alliance understood this as a law of nature rather than a point for negotiation. I came down to the Quad this morning to find the shared poster set in a type that gives the Luncheon top billing, the Recital second billing, and the soup, I must note, a line to itself. There is no third printing of a poster, whatever the kitchen believes, and so the alliance is concluded.',
      'And since certain partisans are already whispering about a certain unsigned review, let me state it plainly and once: I have never in my life eaten the Tuesday soup, a sentence I am advised no innocent woman would need to write, and which I have written anyway, because innocence has nothing to fear from print. I bear no ill will, I am simply molto rubato about the entire morning, and revision forty five will now proceed alone, as perhaps, like all great work, it always had to.',
      'Dr. F. Sharp, Music Room',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m10', 0] }),
    once: true,
  }),
  Object.freeze({
    id: 'm12',
    from: 'Miss Pearl Tartine, Keeper of the Kitchens',
    letterhead: 'faculty',
    heading: 'A Word Regarding Vocabulary',
    body: Object.freeze([
      'I make no accusation, monsieur, I make an observation, and it is this: the review of our late alliance, unsigned as all cowardice is, remarks that the affair needed salt and speaks of things rising with structure, and the notice before it used, correctly, the word reduction. I have kept this kitchen for thirty-two years and I can count on one hand, with fingers to spare, the persons on this campus who know what a reduction is, and every one of them, monsieur, was until Tuesday my ally.',
      'I note also that a woman has written to the whole campus to announce that she has never once eaten my soup, which nobody had asked her, and in my kitchen we have a saying about cooks who declare the pot unburnt before anyone has smelled smoke. The kitchen says nothing further at this time, except that it keeps its receipts, that it has always kept its receipts, and that receipts, unlike reviewers, sign themselves. Bon apetit.',
      'From the kitchen, with what remains of my patience,',
      'P. Tartine',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m11', 1] }),
    once: true,
  }),
  Object.freeze({
    id: 'm13',
    from: 'Mr. Aldous Petch, Acting Deputy Head',
    letterhead: 'office',
    heading: 'Circular: Findings of the Inquiry (Extract, Pages One to Three of Sixty)',
    body: Object.freeze([
      'Further to my circular of Wednesday last, and conscious that colleagues will wish to digest the findings of the inquiry at a pace consistent with their other duties, I am releasing the document in a considerate extract, the whole being available in my office to any colleague with an afternoon. The inquiry examined the board, the pins, the light in the Entrance Hall at the material hour, the draught in the West Wing, which the findings attribute in passing to the decision of 1979, as all things must finally be, and the tall cabinet by the window, whose wobble I can now confirm to be structural rather than moral, a finding I regard as the inquiry\'s first fruit.',
      'On the central question the findings are unanimous, myself concurring with myself in full: the sheet was at no point lost. It was, and remains, merely unlocated, a distinction the sixty pages develop at what I believe to be a fulsome and satisfying length.',
      'Yours in continuity,',
      'A. Petch, Acting Deputy Head and Convenor of the Inquiry (Concluded)',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m12', 2] }),
    once: true,
  }),
  Object.freeze({
    id: 'm14',
    from: 'the Headmistress',
    letterhead: 'personal',
    heading: 'On the Findings',
    body: Object.freeze([
      'I have received the findings of the recent inquiry, all sixty of their pages, and while thoroughness is a virtue the school has always admired, we find ourselves hoping, with the greatest warmth, that whatever is found in future will consent to be found more briefly.',
      'M.',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m13', 2] }),
    once: true,
  }),
  Object.freeze({
    id: 'm15',
    from: 'Mr. Aldous Petch, Acting Deputy Head',
    letterhead: 'office',
    heading: 'Circular: Acknowledgement of Correspondence Received from the Highest Level',
    body: Object.freeze([
      'Further to my circular of Friday last, I am honoured to inform colleagues that the findings of the inquiry have been read at the very highest level, and that the school\'s response, which I have had framed pending filing, takes the form of editorial guidance of the most encouraging kind. Brevity, colleagues, I have long held among the first of the administrative virtues, and to receive her notes on the draft, for I think we may fairly call them that, has confirmed my intention, formed I would say independently and some time ago, to prepare a second edition of the findings, shorter in every respect except scope.',
      'It will be ready by Thursday, or by the Thursday following, whichever proves the truer Thursday.',
      'Yours in continuity,',
      'A. Petch, Acting Deputy Head, Convenor of the Inquiry (Concluded), and Editor of its Findings',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m14', 1] }),
    once: true,
  }),
  Object.freeze({
    id: 'm16',
    from: 'Miss Pearl Tartine, Keeper of the Kitchens',
    letterhead: 'faculty',
    heading: 'An Open Letter Concerning the Editor of This So-Called Newspaper',
    body: Object.freeze([
      'Thirty-two years, monsieur, I have hunted a ghost through your pages, and on Thursday your back page promised me his face in full, and I am told by those who set the type, for the printers eat lunch like anyone, that the promised page carries nothing upon it but half a star, sitting there like a crumb someone could not be troubled to wipe. I am done with ghosts, monsieur, and I have begun, at last, on arithmetic.',
      'Consider who has printed every review, who has profited by every wound, whose circulation rises each time my soup is made to bleed, and who alone on this campus edits every voice until all of them sound the same, so that no style could ever be told from his own. I name you, Roy Baxter Junior, you are The Palate, or you have made him, and I no longer care to learn which, for the kitchen has reached its verdict: one star, the first I have ever awarded to anything, and it is for your nerve, which is considerable. Bonne appetit.',
      'From the kitchen, with what remains of my patience,',
      'P. Tartine',
    ]),
    trigger: Object.freeze({ daysAfterIssueRead: ['g4', 2] }),
    once: true,
  }),
  Object.freeze({
    id: 'm17',
    from: 'Dr. F. Sharp, Mistress of Music',
    letterhead: 'faculty',
    heading: 'The Recital Moves to Spring, as Great Things Move',
    body: Object.freeze([
      'It will surprise nobody who understands the making of programmes that the Recital has elected to move to spring, a season with which it has always had a private understanding. I say elected, because that is the word: the vocabulary of delay has no place in the Music Room, and in Vienna we were taught that a premiere forced into the wrong autumn is simply a debt collected from the wrong spring.',
      'Revision forty five is already breathing, and I will say only that it opens with the fanfare rather than closing with it, which changes everything, and that those who have heard revision forty four described will find they had heard nothing at all. The Main Hall may spend its autumn evening however it now sees fit, for the Recital and I have somewhere better to be, which is the future.',
      'Dr. F. Sharp, Music Room',
    ]),
    trigger: Object.freeze({ daysAfterIssueRead: ['g5', 1] }),
    once: true,
  }),
  Object.freeze({
    id: 'm18',
    from: 'the Headmistress',
    letterhead: 'personal',
    heading: 'On the Autumn Evening',
    body: Object.freeze([
      'I write to say what the whole school must already feel, that the autumn evening was a triumph, as I never doubted it would be, for doubt is a habit we have not found it necessary to acquire where the school is concerned. We are told the Hall has rarely stood so composed, that the occasion carried itself with the dignity we have come to expect of our autumns, and we are pleased, more than pleased, to let the evening rest in memory exactly as it occurred, which is where we intend to leave it.',
      'A.',
    ]),
    trigger: Object.freeze({ daysAfterRead: ['m17', 2] }),
    once: true,
  }),
  Object.freeze({
    id: 'm21',
    from: 'Mr. Theodore Pendle, Keeper of the Bell Tower',
    letterhead: 'faculty',
    heading: 'Notice on the Keeping of Time This Term',
    body: Object.freeze([
      'Monday, mild, a low sky that never quite made rain. The term being new, I set out the position as I do each year for the benefit of anyone joining us and of several who have been here longer: the tower keeps true time, the front desk keeps the other one, and the difference between them, which is four minutes, is not a fault but a fact, like the difference between two honest men.',
      'Lessons, meals, and meetings may be kept by either clock provided a person is consistent, for it is the changing of allegiance mid-week that has the Quad making heavy weather of its mornings. The bells will ring the hours as always, three of them, the fourth being at rest, on which matter I thank all enquirers in advance and refer them to my previous thanks.',
      'Kept, T. Pendle.',
    ]),
    trigger: Object.freeze({ dayAtLeast: 2 }),
    once: true,
  }),
  Object.freeze({
    id: 'm19',
    from: 'Mr. Aldous Petch, Acting Deputy Head',
    letterhead: 'notice',
    heading: 'Circular: Lost Property, an Appeal and an Inventory',
    body: Object.freeze([
      'Further to my circular of last Michaelmas upon the same melancholy theme, the lost property drawer has again reached capacity, and I list its principal holdings in the hope of reunions: one green glove, much darned and clearly loved; a music stand of good quality, recovered from within the hedge, which surrendered it without conditions; an umbrella whose owner I believe I could identify from the manner of its folding alone, though discretion stays my hand; and a loose printed page bearing what appears to be a fragment of the school song\'s second verse, differing, I am obliged to record, from both fragments previously recovered, and retained by me pending the completion of that revision.',
      'Claimants may call at my office on any afternoon, where the drawer, the kettle, and I keep more or less continuous hours.',
      'Yours in continuity,',
      'A. Petch, Acting Deputy Head',
    ]),
    trigger: Object.freeze({ dayAtLeast: 3 }),
    once: true,
  }),
  Object.freeze({
    id: 'm22',
    from: 'Miss Pearl Tartine, Keeper of the Kitchens',
    letterhead: 'faculty',
    heading: 'A Standing Notice Concerning Fridays',
    body: Object.freeze([
      'It has come to the kitchen\'s attention that persons new to the campus continue to expect fish on Fridays, and I take up my pen once more, patiently, to explain what the kitchen can and cannot say. There is no fish on Fridays, there has been no fish on Fridays since the year of the arrangement, and the terms of that arrangement, which were honourable on both sides, remain between the fish and me.',
      'In its place the Friday table offers the pie, five stars, a rating that has not wavered in a decade because the pie has not wavered either, and what the pastry costs me every Thursday night, only the rolling pin knows. I ask for no thanks, only for an end to the questions. Bonne apetitte.',
      'From the kitchen, with what remains of my patience,',
      'P. Tartine',
    ]),
    trigger: Object.freeze({ dayAtLeast: 4 }),
    once: true,
  }),
  Object.freeze({
    id: 'm20',
    from: 'Mr. Aldous Petch, Acting Deputy Head',
    letterhead: 'notice',
    heading: 'Circular: The Fire Drill, a New Date',
    body: Object.freeze([
      'Further to my circular of the spring, and to its predecessors, whose number I will not embarrass the record by totalling, the fire drill stands rescheduled once more, this time to the second Tuesday of next month, a date chosen after consultation with the tower, the kitchens, and the weather, none of whom could attend the previous date either. Assembly will be on the far lawn, not the pool terrace, the pool being down its inch again this term and the plumber\'s opinion still pending.',
      'Colleagues will recall that the drill has now been rescheduled continuously since before any current tenure, my own included, and I regard this not as failure but as the highest form of readiness, for a school that is always about to practise its escape is a school that never stops thinking about it. The bell to listen for is the second bell, the fourth being at rest.',
      'Yours in continuity,',
      'A. Petch, Acting Deputy Head',
    ]),
    trigger: Object.freeze({ dayAtLeast: 5 }),
    once: true,
  }),
]);

/* ----------------------------------------------------------------------------
 * SMALL PURE HELPERS
 * -------------------------------------------------------------------------- */

function num(v) { const n = Number(v); return Number.isFinite(n) ? n : 0; }

function isObj(v) { return !!v && typeof v === 'object' && !Array.isArray(v); }

function pad2(n) { const s = String(n); return s.length < 2 ? '0' + s : s; }

/** LOCAL 'yyyy-mm-dd' (trap 8: every date this page reasons about is local). */
export function dayKeyOf(ms) {
  const d = new Date(num(ms));
  return d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate());
}

/** LOCAL 'MM-DD' - what a `dateIs` clause compares against. */
export function monthDayOf(ms) {
  const d = new Date(num(ms));
  return pad2(d.getMonth() + 1) + '-' + pad2(d.getDate());
}

function asList(v) {
  if (Array.isArray(v)) return v.filter((x) => typeof x === 'string' && x.length);
  if (typeof v === 'string' && v.length) return [v];
  return [];
}

function has(list, id) {
  if (!Array.isArray(list)) return false;
  for (let i = 0; i < list.length; i += 1) if (list[i] === id) return true;
  return false;
}

/** LOCAL midnight of the day holding `v` - epoch ms, a 'yyyy-mm-dd' string, or
 *  anything else (0). The spacing clauses count whole local days, never hours,
 *  so a letter read at 23:59 unlocks its reply the moment the date turns. */
function localDayStart(v) {
  if (typeof v === 'string') {
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(v);
    if (!m) return 0;
    return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])).getTime();
  }
  const n = num(v);
  if (!n) return 0;
  const d = new Date(n);
  return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}

/** Whole LOCAL days from `from` (ms or day string) to `nowMs`; -1 if unknown. */
function daysBetween(from, nowMs) {
  const a = localDayStart(from);
  if (!a) return -1;
  return Math.round((localDayStart(nowMs) - a) / 86400000);
}

/* ----------------------------------------------------------------------------
 * THE CLAUSES
 *
 * Every clause is `(arg, ctx) -> boolean` and every one of them is TOTAL: a
 * missing context field reads as 0 / '' / {} and the clause simply fails,
 * because a letter held is a letter that can still arrive tomorrow and a letter
 * sent on a guess cannot be taken back.
 *
 * `ctx.delivered` / `ctx.read` are filled in by the engine before it evaluates
 * anything, so `afterRead` needs no access to the state blob and this whole
 * table stays pure.
 * -------------------------------------------------------------------------- */
export const CLAUSES = Object.freeze({
  /** School days attended so far. */
  dayAtLeast: (a, c) => num(c.day) >= num(a),
  dayIs: (a, c) => num(c.day) === num(a),
  /** Stamps earned across every card. */
  punchesAtLeast: (a, c) => num(c.punches) >= num(a),
  /** SEALED CARDS - cards that reached their tenth hole. Not the same question
   *  as `punchesAtLeast`: nine cards at nine holes is 81 stamps and zero seals.
   *  It is the count the Records Annex reveal arms on, and the count THE SEEP's
   *  escalation ladder runs on (shell/seep.js `tierForSealed`), which is what
   *  lets a notice go up at the same moment the school starts getting thin. */
  sealedAtLeast: (a, c) => num(c.sealed) >= num(a),
  /** The host-owned attendance streak. */
  streakAtLeast: (a, c) => num(c.streak) >= num(a),
  streakIs: (a, c) => num(c.streak) === num(a),
  /** Today, LOCAL, as 'MM-DD'. */
  dateIs: (a, c) => !!c.dateIs && String(c.dateIs) === String(a),
  /** Every named flag is set in ctx.seenFlags (a string or a list of them). */
  seen: (a, c) => {
    const want = asList(a);
    if (!want.length) return false;
    for (let i = 0; i < want.length; i += 1) if (!c.seenFlags[want[i]]) return false;
    return true;
  },
  /** None of the named flags is set. */
  notSeen: (a, c) => {
    const want = asList(a);
    if (!want.length) return false;
    for (let i = 0; i < want.length; i += 1) if (c.seenFlags[want[i]]) return false;
    return true;
  },
  /** Another letter has landed (whether or not it was opened). */
  afterDelivered: (a, c) => has(c.delivered, String(a)),
  /** Another letter has been opened. Letters that answer letters use this. */
  afterRead: (a, c) => has(c.read, String(a)),

  /* -------- the season clauses (Wave 4b): spacing, cross-surface, calendar.
   * A story chain is `daysAfterRead` links; a letter that answers the paper is
   * `issueRead`/`daysAfterIssueRead`; a notice window closes with
   * `beforeDelivered`/`beforeIssueRead`. All TOTAL, all fail-closed. -------- */

  /** `['m03', 1]`: m03 read, and at least that many whole LOCAL days ago. */
  daysAfterRead: (a, c) => {
    const pair = Array.isArray(a) ? a : [a, 0];
    const id = String(pair[0] || '');
    if (!has(c.read, id)) return false;
    const gap = daysBetween(c.readAt[id], c.nowMs);
    return gap >= 0 && gap >= num(pair[1]);
  },
  /** The named letter has NOT landed yet (a window that closes on delivery). */
  beforeDelivered: (a, c) => !has(c.delivered, String(a)),
  /** A Bugle issue has been opened (ctx.issuesReadAt: id -> local day / ms). */
  issueRead: (a, c) => c.issuesReadAt[String(a)] != null,
  /** `['g1', 1]`: the issue read, and at least that many whole days ago. */
  daysAfterIssueRead: (a, c) => {
    const pair = Array.isArray(a) ? a : [a, 0];
    const at = c.issuesReadAt[String(pair[0] || '')];
    if (at == null) return false;
    const gap = daysBetween(at, c.nowMs);
    return gap >= 0 && gap >= num(pair[1]);
  },
  /** The named issue has NOT been opened yet. */
  beforeIssueRead: (a, c) => c.issuesReadAt[String(a)] == null,
  /** `[9, 10, 11]`: the LOCAL month is one of these (1-12). */
  monthIn: (a, c) => {
    const want = Array.isArray(a) ? a : [a];
    const mm = Number(String(c.dateIs || '').slice(0, 2));
    if (!mm) return false;
    for (let i = 0; i < want.length; i += 1) if (num(want[i]) === mm) return true;
    return false;
  },
});

const warned = Object.create(null);

/**
 * Evaluate a trigger object. ALL clauses must hold; `{}` (or a missing trigger)
 * always holds, which is how the opening letter is written.
 *
 * @param {Object} trigger  clause name -> argument
 * @param {Object} ctx      the shell's context, plus `delivered` / `read` id
 *                          lists when the caller has them
 * @param {Function=} log
 * @returns {boolean}
 */
export function triggerHolds(trigger, ctx, log) {
  const say = typeof log === 'function' ? log : () => {};
  if (trigger != null && !isObj(trigger)) return false;   // a shape nobody meant
  const t = trigger || {};
  const c = {
    day: num(ctx && ctx.day),
    punches: num(ctx && ctx.punches),
    sealed: num(ctx && ctx.sealed),
    streak: num(ctx && ctx.streak),
    dateIs: (ctx && typeof ctx.dateIs === 'string') ? ctx.dateIs : '',
    seenFlags: (ctx && isObj(ctx.seenFlags)) ? ctx.seenFlags : {},
    delivered: (ctx && Array.isArray(ctx.delivered)) ? ctx.delivered : [],
    read: (ctx && Array.isArray(ctx.read)) ? ctx.read : [],
    /* the season fields; a caller that predates them reads empty and every
     * clause asking about them fails closed, holding the letter. */
    readAt: (ctx && isObj(ctx.readAt)) ? ctx.readAt : {},
    issuesReadAt: (ctx && isObj(ctx.issuesReadAt)) ? ctx.issuesReadAt : {},
    nowMs: num(ctx && ctx.nowMs) || Date.now(),
  };

  const names = Object.keys(t);
  for (let i = 0; i < names.length; i += 1) {
    const name = names[i];
    const fn = Object.prototype.hasOwnProperty.call(CLAUSES, name) ? CLAUSES[name] : null;
    if (!fn) {
      // An unimplemented gate HOLDS the letter (law 3). Once per name, per session.
      if (!warned[name]) {
        warned[name] = true;
        say('mail: unknown trigger clause "' + name + '" (letter held)');
      }
      return false;
    }
    let ok = false;
    try { ok = !!fn(t[name], c); } catch (e) { ok = false; }
    if (!ok) return false;
  }
  return true;
}

/* ----------------------------------------------------------------------------
 * THE ENGINE
 * -------------------------------------------------------------------------- */

/**
 * Start the postman over an injected postbox.
 *
 * @param {Object} o
 * @param {Object|Function} o.ctx     the shell's context, or a getter for it
 * @param {Object} o.state            the persisted blob (see STATE-NEEDS). It is
 *                                    mutated in place and handed back to `save`.
 * @param {Function} o.save           save(state) - called after every mutation
 * @param {Array=} o.catalog          override the letters (suites only)
 * @param {Function=} o.now           () -> epoch ms (suites only)
 * @param {Function=} o.log
 * @returns {Object} {pending, deliver, markRead, unreadCount, all}
 */
export function initMail({ ctx, state, save, catalog, now, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const clock = typeof now === 'function' ? now : () => Date.now();
  const bank = typeof save === 'function' ? save : () => {};
  const letters = (Array.isArray(catalog) ? catalog : MAIL).filter(
    (l) => isObj(l) && typeof l.id === 'string' && l.id.length
  );

  /* THE BLOB. We hold the object the caller gave us and write into it, so the
   * driver's own reference stays the live one - `save` is a request to bank
   * what is already true, never a handover of a new object. */
  const blob = isObj(state) ? state : {};
  if (blob.v == null) blob.v = MAIL_STATE_VERSION;
  if (!isObj(blob.letters)) blob.letters = {};

  function rec(id) {
    const r = blob.letters[id];
    return isObj(r) ? r : null;
  }

  function readCtx(override) {
    const base = isObj(override) ? override
      : (typeof ctx === 'function' ? ctx() : ctx);
    const c = isObj(base) ? base : {};
    const delivered = [];
    const read = [];
    const readAt = {};
    for (let i = 0; i < letters.length; i += 1) {
      const r = rec(letters[i].id);
      if (!r || !num(r.deliveredAt)) continue;
      delivered.push(letters[i].id);
      if (num(r.readAt)) {
        read.push(letters[i].id);
        readAt[letters[i].id] = num(r.readAt);
      }
    }
    return {
      day: num(c.day),
      punches: num(c.punches),
      /* SEALED CARDS. Named through explicitly like every other fact: this
       * projection is a whitelist by design, so a field the shell adds and this
       * line does not name is a field every clause reads as nought. */
      sealed: num(c.sealed),
      streak: num(c.streak),
      // A shell that does not carry the date gets today's, LOCAL, off the clock
      // it handed us - never a UTC one (trap 8).
      dateIs: (typeof c.dateIs === 'string' && c.dateIs) ? c.dateIs : monthDayOf(clock()),
      seenFlags: isObj(c.seenFlags) ? c.seenFlags : {},
      delivered,
      read,
      readAt,
      // Cross-surface facts ride in from the shell (Bugle reads); the box's
      // own facts are minted here so no caller can forge a read it never made.
      issuesReadAt: isObj(c.issuesReadAt) ? c.issuesReadAt : {},
      nowMs: clock(),
    };
  }

  /** The paper the UI reads: the catalog row plus what the box knows about it. */
  function view(entry) {
    const r = rec(entry.id) || {};
    const head = LETTERHEADS[entry.letterhead] || DEFAULT_LETTERHEAD;
    return {
      id: entry.id,
      from: String(entry.from || ''),
      letterhead: head.id,
      head,
      heading: String(entry.heading || ''),
      body: Array.isArray(entry.body) ? entry.body.slice() : [],
      once: entry.once !== false,
      deliveredAt: num(r.deliveredAt) || null,
      readAt: num(r.readAt) || null,
      unread: !!num(r.deliveredAt) && !num(r.readAt),
    };
  }

  /**
   * May this letter land tonight?
   *
   * A `once` letter that has landed is finished for ever. A repeatable one
   * (`once:false`) waits for two things before it comes round again: a LATER
   * local day, and the previous copy actually opened. A re-delivery re-stamps
   * `deliveredAt` and clears `readAt` - one row per letter, always the copy
   * currently in the box.
   */
  function deliverable(entry, c, nowMs) {
    const r = rec(entry.id);
    if (r && num(r.deliveredAt)) {
      if (entry.once !== false) return false;
      if (!num(r.readAt)) return false;
      if (dayKeyOf(r.deliveredAt) === dayKeyOf(nowMs)) return false;
    }
    return triggerHolds(entry.trigger, c, say);
  }

  return {
    /**
     * Everything that WOULD land right now, in catalog order. The first entry
     * is exactly what the next `deliver()` returns; the rest wait their turn.
     * No side effect, so a suite or a debug door can ask freely.
     * @param {Object=} override  a context to use instead of the injected one
     * @returns {Array<Object>}
     */
    pending(override) {
      const c = readCtx(override);
      const nowMs = clock();
      const out = [];
      for (let i = 0; i < letters.length; i += 1) {
        if (deliverable(letters[i], c, nowMs)) out.push(view(letters[i]));
      }
      return out;
    },

    /**
     * Hand over ONE letter (law 2). Returns the letter that landed, or null on
     * a night when nothing qualified.
     * @param {Object=} override
     * @returns {?Object}
     */
    deliver(override) {
      const c = readCtx(override);
      const nowMs = clock();
      for (let i = 0; i < letters.length; i += 1) {
        const entry = letters[i];
        if (!deliverable(entry, c, nowMs)) continue;
        blob.letters[entry.id] = { deliveredAt: nowMs, readAt: null };
        try { bank(blob); } catch (e) { say('mail save failed: ' + ((e && e.message) || e)); }
        return view(entry);
      }
      return null;
    },

    /**
     * Stamp a letter opened. Idempotent: a second call is a no-op and answers
     * false, so a UI that re-renders cannot re-bank the same read.
     * @param {string} id
     * @returns {boolean} true when this call was the one that opened it
     */
    markRead(id) {
      const key = String(id || '');
      const r = rec(key);
      if (!r || !num(r.deliveredAt) || num(r.readAt)) return false;
      r.readAt = clock();
      try { bank(blob); } catch (e) { say('mail save failed: ' + ((e && e.message) || e)); }
      return true;
    },

    /** How many letters in the box have never been opened. */
    unreadCount() {
      let n = 0;
      for (let i = 0; i < letters.length; i += 1) {
        const r = rec(letters[i].id);
        if (r && num(r.deliveredAt) && !num(r.readAt)) n += 1;
      }
      return n;
    },

    /**
     * What is IN the box, newest first (catalog order breaks a tie). Letters
     * that have not been delivered are not in the box and are not here.
     * @returns {Array<Object>}
     */
    all() {
      const out = [];
      for (let i = 0; i < letters.length; i += 1) {
        const r = rec(letters[i].id);
        if (!r || !num(r.deliveredAt)) continue;
        out.push({ v: view(letters[i]), i });
      }
      out.sort((a, b) => (b.v.deliveredAt - a.v.deliveredAt) || (a.i - b.i));
      return out.map((x) => x.v);
    },

    /**
     * The fully-populated clause context (the shell's facts plus this box's
     * delivered/read ledger). The noticeboard and the Bugle evaluate their own
     * `when` gates against THIS through `triggerHolds`, so all three surfaces
     * tell one story off one set of facts.
     * @param {Object=} override
     * @returns {Object}
     */
    context(override) {
      return readCtx(override);
    },
  };
}

export default initMail;
