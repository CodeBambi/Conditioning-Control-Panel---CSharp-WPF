/* ============================================================================
 * shell/bugle.js - THE PAPER (PHANTOM POST, agent M2).
 *
 * A small campus newspaper, read as an overlay: a masthead, a flag line, two
 * columns of set copy, a comics box, and pages you turn. It is the corkboard's
 * long-form twin - the wall says six things badly, the paper says one thing at
 * length - and it is built on the same bones: an OVERLAY, not a screen, mounted
 * on its own fixed stage, removed rather than hidden (trap 27), with the way
 * out on the sticky exit bar (trap 46).
 *
 * THE TABLE BELOW IS THE PAPER ITSELF: the term's five issues, three pages to
 * an issue, a kicker and a headline on each, and a comics slot on the pages
 * that carry one. The masthead above it is still awaiting its own copy.
 *
 * THE MASTHEAD FACE is 'Arcademy Display' (styles.css --disp), which is the
 * bundled Graduate woff2 under art/fonts/ - a collegiate slab serif that
 * happens to be exactly what a small-town paper sets its own name in. No new
 * font ships with this wave and none may (trap 2: the webview is offline, a
 * font host silently falls back and burns a boot on a DNS timeout).
 *
 * ---------------------------------------------------------------------------
 * STATE-NEEDS  (the driver's Wave 4 - this file persists NOTHING itself)
 * ---------------------------------------------------------------------------
 * Read from and written to an INJECTED plain object handed in as `state`, with
 * an injected `save(state)` callback. No core/store.js import, no meta-command,
 * no localStorage. Hand it `{}` and the paper simply forgets it was read.
 *
 *   state.issues        { <issueId>: { readAt: string|null, lastPage: number } }
 *                       `readAt` is a LOCAL 'yyyy-mm-dd' (trap 8: every date
 *                       shown to the player on this page is local). `lastPage`
 *                       is a zero-based page index, clamped on read, so a
 *                       reopened issue lands where the player left it.
 *
 *   state.latestSeen    string|null. The id of the newest issue that has ever
 *                       been opened. It is what the prop's marker hangs off:
 *                       a paper with a fresh issue on it is worth crossing a
 *                       campus for, and a paper without one is scenery.
 *
 *   state.opens         number. Plain counter, incremented once per open.
 *
 * The `bugleRead` counter family stays the DRIVER'S: this module reports what
 * happened through `onRead(issueId)` / `onPage(index)` and through the state
 * object, and the driver decides what a counter means.
 *
 * ---------------------------------------------------------------------------
 * WIRING THE DRIVER STILL OWNS
 * ---------------------------------------------------------------------------
 *   - Esc. House law (shell/exits.js header): nothing outside shell.js handles
 *     Esc by itself, and a modal gets ONE rung at the TOP of escapeStep (trap
 *     48). This overlay ships `close()` and binds nothing by default;
 *     `openBugle(id, { bindEscape: true })` is the standalone/demo path.
 *   - Where the folded prop sits on the campus.
 *   - The lexicon rows. Chrome goes through `t(key, fallback)`, so an
 *     unmirrored key renders English (trap 15) until the host's NeutralLexicon
 *     grows the `bugle_*` family. The COPY in ISSUES is content and stays a
 *     plain string in the table, per the ground rules.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { exitBar, sign as signExit } from './exits.js';

/* ----------------------------------------------------------------------------
 * THE SHEET
 * A real file (shell/bugle.css), linked once and lazily, resolved against THIS
 * MODULE rather than the document - shell modules and the document can sit at
 * different roots (the campus logo bug, campus.js:320).
 * -------------------------------------------------------------------------- */

export const STYLE_ID = 'arc-bugle-style';

export const STYLE_HREF = (function resolveSheet() {
  try { return new URL('./bugle.css', import.meta.url).href; }
  catch (e) { return 'shell/bugle.css'; }
}());

/** Link the sheet once. Idempotent, guarded, a no-op on the node DOM double. */
export function ensureStyles(doc) {
  const d = doc || (typeof document !== 'undefined' ? document : null);
  if (!d || typeof d.createElement !== 'function') return false;
  try {
    if (d.getElementById && d.getElementById(STYLE_ID)) return true;
    const link = d.createElement('link');
    link.id = STYLE_ID;
    link.rel = 'stylesheet';
    link.href = STYLE_HREF;
    const head = d.head || d.body || d.documentElement;
    if (!head || typeof head.appendChild !== 'function') return false;
    head.appendChild(link);
    return true;
  } catch (e) { return false; }
}

/* ----------------------------------------------------------------------------
 * THE PAPER'S OWN NAME
 * Content, not chrome, so it lives here rather than behind t(). The strap is
 * the line under the rule; the flag bits are the small print beside the issue
 * number - price, boast, the usual furniture of a masthead.
 * -------------------------------------------------------------------------- */
export const MASTHEAD = Object.freeze({
  title: 'The Arcademy Bugle',
  strap: "The campus paper of record, as was my father's before me.",
  flagLeft: 'circulation: up',
  flagRight: 'free to take, one per reader',
});

/* ----------------------------------------------------------------------------
 * THE TABLE
 * The term's issues, oldest first. `pages[].body` accepts a string OR a list of
 * paragraphs; a paper wants paragraphs and one string is the degenerate case of
 * a list, so the renderer takes both and every page here uses lists.
 * `comics` puts the comics box on that page: a caption string draws the one
 * framed panel it describes, and a page without the field renders columns only.
 * `halfStar` prints the ornament and sets the sparse column treatment.
 * `when` is the gate the shell answers before an issue exists to any reader.
 * -------------------------------------------------------------------------- */

export const ISSUES = Object.freeze([
  Object.freeze({
    id: 'g1',
    number: 'Vol. XLIV, No. 1',
    headline: 'THE BUGLE RETURNS: A YEAR OF CHANGE DECLARED FOR THE FOURTEENTH CONSECUTIVE YEAR',
    pages: Object.freeze([
      Object.freeze({
        kicker: 'THE EDITOR SPEAKS',
        title: 'CHANGE, AGAIN, AT LAST',
        body: Object.freeze([
          'After a summer silence this paper observed by choice, and not, as certain corridors have suggested, for want of news, the Bugle returns to the campus larger, bolder, and up eleven on the spring circulation figure, a number I print because it is true and because my father taught me that the truth sells itself, though it never hurts to give it capitals. This will be a YEAR OF CHANGE. I have declared one every autumn for fourteen years now, and I regard the run as proof of the policy rather than otherwise, since a school that can absorb fourteen consecutive years of change and still argue about its founding year, of which we possess three carved candidates and no appetite for fewer, is a school with change to spare.',
          'My father, in his final editorial, wrote that a paper that rests is a paper that rusts, or words very close to that effect, and this paper does not rust. Inside this bumper number: the Recital, revision FORTY FOUR, previewed exclusively on page two; the Harvest Luncheon, announced in full on page three; and the motto over the Main Gate rendered, for balance, in two of its translations. Read on, for the year, as my father nearly said, will not change itself, allegedly.',
          'R. Baxter Jr., Editor, as was my father before me.',
        ]),
      }),
      Object.freeze({
        kicker: 'THE ARTS',
        title: 'REVISION FORTY FOUR: \'MY BOLDEST\', SAYS THE MUSIC ROOM',
        body: Object.freeze([
          '[The following programme note reaches us from the Music Room and is printed in full, edited. Ed.]',
          'The Recital enters the autumn in its forty fourth revision, and connoisseurs of the form will understand me when I say that forty four is not a number but a vintage. The programme has been maturing since the spring in exactly the conditions great programmes require, which are privacy, patience, and distance from the opinions of the tower, and it will be given in the Main Hall on the evening of the twenty-ninth of November, an evening I have held in my mind for years the way one holds a note, gently and without letting go. In Vienna we were taught that a concert begins long before anyone plays, and by that reckoning this one has been in glorious progress for four decades, interrupted only once, by the weather that year, of which I will say nothing further. Of the bells, their temperament, and the quarter tone that certain towers still decline to concede, I say only that the Recital will be in tune whatever the hour is when it begins. Dr. F. Sharp, Music Room.',
          '[The Bugle has now previewed this Recital forty four times, a record for the school press and, we believe, for any press, allegedly. Ed.]',
        ]),
      }),
      Object.freeze({
        kicker: 'THE TABLE',
        title: 'HARVEST LUNCHEON TO FILL THE MAIN HALL WITH \'A MENU THAT HAS FOUNDATIONS\'',
        body: Object.freeze([
          '[From the kitchen, a menu and, as our readers have come to expect, a memoir. Printed in full, edited. Ed.]',
          'On the evening of the twenty-ninth of November the kitchen will lay the Harvest Luncheon in the Main Hall, and I use the word lay as a mason uses it, for this meal has foundations. The bird course alone has been three years in negotiation with my supplier, a man of the old school who will remain nameless because his name is my advantage, and the centrepiece reduction is the same reduction that carried the kitchen through the year of the burst pipe, refined since until the spoon stands upright in it out of respect. Five stars, I say now, calmly and in advance, because I have tasted the rehearsals. The campus is invited in its entirety, and the kitchen asks only that guests arrive hungry and leave their reviews at home. From the kitchen, with what remains of my patience, P. Tartine.',
          'The Bugle notes with PRIDE that the Main Hall will thus stand at the centre of BOTH of the season\'s great occasions, a coincidence of scheduling that speaks, in this editor\'s view, to a campus firing on every cylinder at once.',
        ]),
        comics: 'A single panel: the Main Hall wearing two evening banners at once, one for the Recital and one for the Luncheon, a small bird reading both, caption \'Plenty of room.\'',
      }),
    ]),
  }),
  Object.freeze({
    id: 'g2',
    number: 'Vol. XLIV, No. 2',
    headline: 'THE PALATE RETURNS: TUESDAY SOUP RATED FOR THE FIRST TIME SINCE 1994',
    when: Object.freeze({ daysAfterIssueRead: ['g1', 1] }),
    pages: Object.freeze([
      Object.freeze({
        kicker: 'EXCLUSIVE',
        title: 'THREE STARS AND ONE HALF: THE REVIEW IN FULL',
        body: Object.freeze([
          'The Bugle publishes below, exactly as it reached this desk and by means this paper will defend to its last pica, the first notice from the critic signing himself The Palate since the four-star soup review of 1994, an event now older than some of our staplers.',
          '\'One returns to a kitchen the way one returns to a childhood house, braced for what time has done to it, and one finds the Tuesday soup very much as one left it, which is the compliment and the complaint in a single spoon. The stock is honest, the carrot is present and accounted for, and the whole is warmed by a confidence that borders on autobiography. And yet the salt hangs back where it once stepped forward, and the finish, which in 1994 promised to become something, has instead become something else. Three stars and one half.\'',
          'That is the review in its entirety, and the Bugle observes only that a half star, wherever it finally comes to rest, is the smallest object ever to strike this campus this hard, allegedly.',
          'R. Baxter Jr., Editor, as was my father before me.',
        ]),
      }),
      Object.freeze({
        kicker: 'FROM THE EDITOR\'S CHAIR',
        title: 'THIS PAPER PROTECTS ITS SOURCES, AND IT PROTECTS THEM PROUDLY',
        body: Object.freeze([
          'Demands have reached this desk, some of them in handwriting I recognise from the weekly menu, that the Bugle unmask its correspondent, and it will not. My father, in his final editorial, wrote shield the writer and print the weather, or words to that effect, and I have never been more his son than this week, for a newspaper that hands over its critics is a newsletter, and the Arcademy deserves a NEWSPAPER.',
          'I will add, because the figures deserve the daylight, that circulation since the review has DOUBLED. Doubled from what is not a question a serious paper answers, but doubled it has, and the lesson for the doubters is the one my father spent his life setting in type: controversy, honestly reported, pays for its own ink, allegedly.',
        ]),
        comics: 'A single panel: a soup bowl on a witness stand under a lamp, sweating, caption \'The soup declined to comment.\'',
      }),
      Object.freeze({
        kicker: 'LETTERS TO THE EDITOR, EDITED FOR LENGTH AND FLAVOUR',
        title: 'THE KITCHEN, THE TOWER, AND ONE LATE CORRECTION',
        body: Object.freeze([
          'From the Keeper of the Kitchens comes a letter of some eleven pages, printed here in the fair and generous excerpt our readers expect: \'Monsieur... coward... 1994... the kitchen keeps its receipts... five stars.\' [The ellipses are editorial, the sentiment intact. Ed.]',
          'From the Music Room, on an unrelated matter, we are asked to state that the bells accompanying Tuesday\'s practice were one quarter of a tone flat, that this is a fact of physics and not of taste, and that the Recital, which matures nicely, will not be taking the bells\' opinion into account. [Printed as received, save the parts about the tower, which were longer. Ed.]',
          'And from the Bell Tower, a correction to our last number: the Bugle gave the time of the Quad\'s first frost as ten minutes to seven, whereas the tower\'s log shows six minutes to, a difference our readers will recognise, the tower adding that it bears the front desk no ill will and that the front desk is welcome to its opinion. The Bugle stands by its original time and thanks both clocks for their service.',
        ]),
      }),
    ]),
  }),
  Object.freeze({
    id: 'g3',
    number: 'Vol. XLIV, No. 3',
    headline: 'PEACE IN OUR HALL: RECITAL AND LUNCHEON TO SHARE THE TWENTY-NINTH (DEVELOPMENTS, PAGE THREE)',
    when: Object.freeze({ daysAfterRead: ['m12', 1] }),
    pages: Object.freeze([
      Object.freeze({
        kicker: 'HISTORIC',
        title: 'THE STORY OF THE DECADE, AND THIS PAPER HAS COVERED SOME DECADES',
        body: Object.freeze([
          'Let the record of this campus show that when peace came, the Bugle was FIRST. The Music Room and the kitchen, whose rivalry this paper has chronicled with a devotion some have called excessive and my father would have called Tuesday, have joined their two great occasions into one shared evening on the twenty-ninth: a Recital with a catering credit, a Luncheon with a dessert course in seven movements, each movement named for a revision that mattered. Persons wishing to understand what that last phrase means are directed to the Music Room, at their own risk.',
          'My father, in his final editorial, wrote that the news is what happens while a man is setting the type, and those words have never rung truer than this week, for reasons the sharp-eyed reader may discover as this number proceeds. Circulation, meanwhile, is up again, comfortably, on a figure that was itself up. The Bugle congratulates the happy institutions and has already reserved its seat, front row, both events, which are now the same event, which is the story of the decade, allegedly.',
          'R. Baxter Jr., Editor, as was my father before me.',
        ]),
        comics: 'A single panel: two hands shaking warmly while each hides a poster behind its back, caption \'After you.\'',
      }),
      Object.freeze({
        kicker: 'THE GROUNDS',
        title: 'HEDGE REACHES FRONT PATH: A NOTICE, REPRINTED AS VERSE',
        body: Object.freeze([
          'The Bugle reprints below, in the line breaks this editor felt it deserved, the latest frontier report from the grounds, whose author continues to decline our invitation to contribute a column, or to acknowledge, in any medium, that these reprints occur.',
          'The hedge has reached the Front Path,',
          'having crossed open lawn in under a fortnight,',
          'which I said it would do',
          'and have it in pencil that I said so,',
          'and I am now asking, formally and without prejudice,',
          'for either shears or reinforcements,',
          'whichever the school can spare first.',
          'The Bugle has followed this hedge for years and considers it the finest running story on the campus, a war of inches conducted between a man who documents and a plant that does not, and we say to our readers what we have always said: the hedge holds ground, but the paper holds the record. Donations of shears may be left at the Bugle office, which will report on them.',
        ]),
      }),
      Object.freeze({
        kicker: 'LATE NEWS, HELD FOR PRESS',
        title: 'ALLIANCE ENDS OVER POSTER; UNSIGNED CRITIC AWARDS THE MERGER TWO STARS: \'NEEDED SALT\'',
        body: Object.freeze([
          'A newspaper prints the news in the order the news arrives, which is what my father would have wanted, and what page one, set in happier hours, must now be read as a monument to. The alliance of the year is over. It lasted, by the tower\'s reckoning, one afternoon and four minutes, and it died where great alliances have always died, on the poster, where the question of whose name stands above whose proved to contain, in the Music Room\'s phrase, physics, and in the kitchen\'s phrase, everything she needed to know.',
          'Into this wound, unsigned as ever, came The Palate, whose review of the merger itself reached the campus by plain circular and is quoted here under the doctrine of fair thunder: two stars, the whole, though warm, wants seasoning, it needed salt. The kitchen\'s response named no one and implied a vocabulary. The Music Room\'s response, lodged in our letters drawer and summarised here with the care it deserves, volunteers that its occupant has never once eaten the Tuesday soup, a defence the campus received in silence, since somebody reviewing that soup down the decades has certainly been eating it, allegedly.',
          'The Bugle takes no side, holds both coats, and reminds its readers that the twenty-ninth is still coming, that the Hall is still booked twice, and that this paper will be there FIRST, whichever evening it turns out to be.',
        ]),
      }),
    ]),
  }),
  Object.freeze({
    id: 'g4',
    number: 'Vol. XLIV, No. 4',
    headline: 'THE FINDINGS, SERIALISED: \'NEVER LOST, MERELY UNLOCATED\'',
    when: Object.freeze({ daysAfterRead: ['m15', 1] }),
    pages: Object.freeze([
      Object.freeze({
        kicker: 'SERIALISATION OF THE CENTURY',
        title: 'SIXTY PAGES, ONE VERDICT, AND THE BUGLE HAS ALL OF THEM',
        body: Object.freeze([
          'Beginning this number and continuing for as many numbers as it takes, the Bugle serialises the findings of the inquiry into the vanished sign-up sheet, a document of sixty pages whose author has asked us, in a letter of protest we will print in full on request, not to serialise it. The Bugle honours the protest by recording it and proceeds anyway, for my father, in his final editorial, wrote that the length of the truth is not the reader\'s problem, or words to that effect, and sixty pages is not length, it is CIRCULATION, which stands this month at its strongest since the flood year.',
          'Part one, comprising the findings\' opening movement, establishes the following: that the board was present; that the pins were present, numbering two; that the sheet, at the material hour, was in the state the inquiry terms attendant upon the board; and that at some hour thereafter, material or otherwise, it entered its present condition, which the findings describe in the sentence this campus will be quoting at weddings: the sheet was at no point lost, it was, and remains, merely unlocated. The remaining fifty-seven pages develop this position, and the Bugle will be with them every step.',
          'R. Baxter Jr., Editor, as was my father before me.',
        ]),
      }),
      Object.freeze({
        kicker: 'CORRECTIONS',
        title: 'A CORRECTION REGARDING LAST NUMBER\'S CORRECTION',
        body: Object.freeze([
          'In our last number this column corrected the time of the Quad\'s first frost from ten minutes to seven to six minutes to seven, on the authority of the tower. The tower now writes that the correction, while gratefully received, misquotes its log, which reads six minutes past, not to, a distinction the tower describes as the whole of the matter. This column accordingly corrects its correction, notes that the original error has now been corrected twice in opposite directions, and takes the only position consistent with this paper\'s traditions, which is that we stand by the original error. My father stood by his errors, his father stood by the errors before those, and a paper that will not stand by its errors will fall for anything.',
          'Separately, the kitchen asks us to state that last number\'s phrase eleven pages was an undercount, the letter having run, we are assured, to fourteen, and the Bugle regrets the flattery.',
        ]),
        comics: 'A single panel: sixty numbered pages queuing at the Bugle\'s hatch, the first page wearing a rosette, caption \'Parts two to sixty await their turn.\'',
      }),
      Object.freeze({
        kicker: 'REPRINTED WITHOUT PERMISSION, WHICH IS THE SINCEREST KIND',
        title: 'THE LETTER FROM ABOVE, IN FULL, AND WHAT COMES NEXT',
        body: Object.freeze([
          'The Bugle has obtained, by means it will defend and cannot describe, the letter lately received from the highest level regarding the findings, and prints it entire, this being, we believe, the first appearance of that correspondence in any public print in the school\'s history, whichever history the reader keeps:',
          '\'I have received the findings of the recent inquiry, all sixty of their pages, and while thoroughness is a virtue the school has always admired, we find ourselves hoping, with the greatest warmth, that whatever is found in future will consent to be found more briefly.\'',
          'It is signed as it is signed, on the cream paper, and this paper\'s hands shook a little setting it, which the compositor may record as pride. The Bugle offers no commentary, commentary being unnecessary where perfection has gone before, and turns instead to the future: our next number will carry, IN FULL AND WITHOUT FAIL, the identity of the critic known as The Palate, an unmasking thirty-two years in the preparing, secured by this desk at a cost it will disclose to nobody, least of all the kitchen. The name will appear in our next number, in full, allegedly.',
        ]),
      }),
    ]),
  }),
  Object.freeze({
    id: 'g5',
    number: 'Vol. XLIV, No. 5',
    headline: '\'YOU ARE THE PALATE\': KITCHEN NAMES EDITOR IN THE LETTER OF THE SEASON (THE PROMISED UNMASKING APPEARS ON PAGE TWO, AS PRINTED)',
    when: Object.freeze({ daysAfterRead: ['m16', 1] }),
    pages: Object.freeze([
      Object.freeze({
        kicker: 'EXCLUSIVE OF THE CENTURY',
        title: 'THIS EDITOR STANDS ACCUSED, AND THE COVERAGE WILL BE FEARLESS',
        body: Object.freeze([
          'The Bugle has in its history printed harvests, frosts, findings, and forty four previews of a single concert, but it has never until today enjoyed the honour of being its own front page. The Keeper of the Kitchens, in an open letter this paper reproduces in full and unedited, a courtesy she will find nowhere else, names the undersigned as The Palate, or as his maker, and awards him one star, the first she has ever given to anything, an accusation printed above the fold, under the banner, in the type reserved since my father\'s day for weather of national importance, because that is what a story of this magnitude deserves.',
          'The undersigned denies the charge with his whole chest and covers it with his whole front page, and if the lady cannot understand how both of those things can be true at once, then she has never loved a newspaper. Circulation, since she asks so tenderly after our health, is up, and I will not be more specific, my father having taught me that modesty, like the truth, is best set in capitals: UP. As for the unmasking promised in our last number, the reader is directed to page two, which the Bugle presents exactly as it came off the stone, and stands behind, and will be standing behind for some time.',
          'R. Baxter Jr., Editor, as was my father before me.',
        ]),
      }),
      Object.freeze({
        kicker: 'THE REVEAL',
        title: 'THE PALATE: THE ANSWER IN FULL, CONTINUED FROM PAGE ONE',
        body: Object.freeze([
          '(continued from page one)',
          'The remainder of this page reached the reader exactly as it went to press, and the Bugle stands by every word of it.',
        ]),
        halfStar: true,
      }),
      Object.freeze({
        kicker: 'AND FINALLY',
        title: 'BOTH MIDNIGHTS PASS, THE BENCH RETURNS, THE RECITAL SPRINGS, THE SEASON CLOSES',
        body: Object.freeze([
          'The twenty-ninth has come and gone, and the Bugle\'s verdict on the great contested evening is that it was, on the highest authority and the cream paper, a triumph, a judgement this paper is honoured to second and in no position to elaborate. The tower reports that both midnights passed without incident, four minutes apart, and that the Hall stood composed throughout, which those who know the Hall will recognise as its finest register. From the grounds comes word that the bench is back, facing the wrong way, moved by neither of the parties best placed to move it, a mystery this paper adds to its shelf with something like affection.',
          'The Recital, meanwhile, moves to spring, where revision forty five, already described by the Music Room as her strongest, will find the acoustics it deserves, and the Harvest Luncheon rests on its laurels, of which the kitchen counts five, as always. The Palate has not written again, the kitchen watches this paper, this paper watches its post, and somewhere on the campus a half star is still owed a home.',
          'And so the Bugle closes its season, up on every figure it has ever published and owing its thanks to a readership it has never needed to see to believe in. My father ended his final editorial, as I never tire of almost quoting, with the words leave them wanting the next number, and the Bugle obeys: the spring will bring the Recital, the second edition of the findings, and, it says here, answers, allegedly.',
          'R. Baxter Jr., Editor, as was my father before me.',
        ]),
        comics: 'A single panel: a long dining table laid for a grand evening, every chair empty, one plate holding a single half star, caption \'Compliments to the chef.\'',
      }),
    ]),
  }),
]);

/** Newest issue first is the reading order; the table is authored oldest first. */
export function latestIssue() {
  const out = availableIssues();
  return out.length ? out[out.length - 1] : null;
}

/**
 * The issues whose moment has come, in catalog order. No `when` gate = always
 * out; a gated issue with no evaluator installed stays on the stone (fail
 * closed - an issue held is an issue that can still run tomorrow).
 * @returns {Array}
 */
export function availableIssues() {
  return ISSUES.filter((iss) => {
    if (!iss.when) return true;
    if (typeof deps.when !== 'function') return false;
    try { return !!deps.when(iss.when); } catch (e) { return false; }
  });
}

/** Look one up by id AMONG THE AVAILABLE. Falls back to the latest, then null. */
export function findIssue(issueId) {
  const id = issueId == null ? '' : String(issueId);
  const out = availableIssues();
  for (let i = 0; i < out.length; i += 1) if (out[i].id === id) return out[i];
  return latestIssue();
}

/* ----------------------------------------------------------------------------
 * PLUMBING
 * -------------------------------------------------------------------------- */

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

function attr(node, name, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(name, value); }
  catch (e) { /* the DOM double may not carry attributes - never fatal */ }
}

function focusSoon(node) {
  try { if (node && typeof node.focus === 'function') node.focus(); }
  catch (e) { /* noop */ }
}

/** One cue through the one door (trap 18). Same defensive shape as records.js. */
function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

/** 'yyyy-mm-dd' in LOCAL time. What a date STAMP on this page always is. */
export function localDay(when) {
  const d = (when instanceof Date) ? when : new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return y + '-' + m + '-' + day;
}

function paragraphsOf(body) {
  if (Array.isArray(body)) return body.map((p) => String(p == null ? '' : p)).filter((p) => p.length);
  const one = String(body == null ? '' : body);
  return one.length ? [one] : [];
}

/* ----------------------------------------------------------------------------
 * THE INJECTED HALF
 * -------------------------------------------------------------------------- */

const deps = {
  state: null,
  save: null,
  mount: null,
  log: null,
  when: null,
};

/**
 * Hand the module its injected persistence and defaults. Every field optional.
 * `when` is the season gate evaluator: `(trigger) => boolean` (the shell wires
 * mail.js's triggerHolds over the shared context), so an issue whose moment
 * has not come does not exist yet as far as any reader can tell.
 * @param {{state?:Object, save?:Function, mount?:Object, log?:Function, when?:Function}} opts
 */
export function initBugle(opts) {
  const o = opts || {};
  if (o.state && typeof o.state === 'object') deps.state = o.state;
  if (typeof o.save === 'function') deps.save = o.save;
  if (o.mount) deps.mount = o.mount;
  if (typeof o.log === 'function') deps.log = o.log;
  if (typeof o.when === 'function') deps.when = o.when;
  return deps.state;
}

function stateOf(override) {
  const s = (override && typeof override === 'object') ? override
    : (deps.state && typeof deps.state === 'object') ? deps.state : {};
  if (!s.issues || typeof s.issues !== 'object') s.issues = {};
  return s;
}

function persist(s, save) {
  const fn = (typeof save === 'function') ? save : deps.save;
  if (typeof fn !== 'function') return;
  try { fn(s); }
  catch (e) { if (deps.log) { try { deps.log('bugle save: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
}

/** Is the newest issue still unopened? Drives the prop's quiet marker. */
export function hasUnreadIssue(override) {
  const s = stateOf(override);
  const latest = latestIssue();
  if (!latest) return false;
  const row = s.issues[latest.id];
  return !(row && row.readAt);
}

/* ----------------------------------------------------------------------------
 * THE OVERLAY
 * -------------------------------------------------------------------------- */

let live = null;   // the one open paper, or null

/**
 * Open the paper.
 *
 * @param {string=} issueId             defaults to the newest issue
 * @param {Object=} opts
 * @param {Object=} opts.mount          where to append (default document.body)
 * @param {Object=} opts.state          injected persistence (see STATE-NEEDS)
 * @param {Function=} opts.save         save(state) callback
 * @param {number=} opts.page           zero-based page to open on; defaults to
 *                                      the injected lastPage, then to 0
 * @param {boolean=} opts.bindEscape    self-bind Esc (default FALSE - the shell
 *                                      owns the ladder; this is for demos)
 * @param {Function=} opts.onClose      called once, after the stage is gone
 * @param {Function=} opts.onRead       onRead(issueId) on the first ever open
 * @param {Function=} opts.onPage       onPage(index, issueId) per page turn
 * @returns {?Object} {root, close(), destroy(), issue, page, goto(i)} or null
 */
export function openBugle(issueId, opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  const mount = o.mount || deps.mount || doc.body;
  if (!mount || typeof mount.appendChild !== 'function') return null;

  const issue = findIssue(issueId);
  if (!issue) return null;

  // ONE PAPER. A second open is the first one raised, not a second stage.
  if (live && !live.closed) { focusSoon(live.firstButton); return live.handle; }

  ensureStyles(doc);

  const s = stateOf(o.state);
  const pages = Array.isArray(issue.pages) ? issue.pages : [];
  const lastRow = s.issues[issue.id];
  const wasRead = !!(lastRow && lastRow.readAt);
  const clampPage = (i) => Math.max(0, Math.min(Math.max(0, pages.length - 1), Math.round(Number(i) || 0)));
  let page = clampPage(o.page != null ? o.page : (lastRow ? lastRow.lastPage : 0));

  const root = el('div', 'arc-buglestage');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');
  attr(root, 'aria-label', String(MASTHEAD.title || ''));

  const paper = el('div', 'arc-bugle');

  /* ----------------------------- the masthead --------------------------- */
  const head = el('header', 'arc-bugle-masthead');
  const flag = el('div', 'arc-bugle-flag');
  flag.appendChild(el('span', 'arc-bugle-flagbit', String(MASTHEAD.flagLeft || '')));
  flag.appendChild(el('span', 'arc-bugle-flagbit',
    /* The number string already reads as a full flag ('Vol. XLIV, No. 2');
     * prefixing the Issue label would print furniture twice. The label stays
     * for numbers that are bare numerals. */
    (/[A-Za-z]/.test(String(issue.number == null ? '' : issue.number))
      ? String(issue.number)
      : t('bugle_issue', 'Issue') + ' ' + String(issue.number == null ? '' : issue.number))));
  flag.appendChild(el('span', 'arc-bugle-flagbit', String(MASTHEAD.flagRight || '')));

  head.appendChild(el('h1', 'arc-bugle-name', String(MASTHEAD.title || '')));
  head.appendChild(el('p', 'arc-bugle-strap', String(MASTHEAD.strap || '')));
  head.appendChild(flag);
  paper.appendChild(head);

  /* ------------------------------- the tabs ----------------------------- */
  const tabs = el('nav', 'arc-bugle-tabs');
  attr(tabs, 'role', 'tablist');
  attr(tabs, 'aria-label', t('bugle_pages', 'Pages'));
  const tabButtons = [];
  for (let i = 0; i < pages.length; i += 1) {
    const tb = el('button', 'arc-bugle-tab', t('bugle_page', 'Page') + ' ' + (i + 1));
    tb.type = 'button';
    attr(tb, 'role', 'tab');
    (function bind(index) {
      tb.addEventListener('click', () => { goto(index); });
    }(i));
    tabs.appendChild(tb);
    tabButtons.push(tb);
  }
  if (pages.length > 1) paper.appendChild(tabs);

  /* ------------------------------- the page ----------------------------- */
  const sheet = el('div', 'arc-bugle-sheet');
  attr(sheet, 'role', 'tabpanel');
  paper.appendChild(sheet);

  function paintPage() {
    sheet.textContent = '';
    const p = pages[page];
    if (!p) {
      sheet.appendChild(el('p', 'arc-note arc-bugle-empty',
        t('bugle_empty', 'Nothing set for this page.')));
      return;
    }

    const lede = el('div', 'arc-bugle-lede');
    lede.appendChild(el('p', 'arc-bugle-kicker', String(p.kicker || '').toUpperCase()));
    lede.appendChild(el('h2', 'arc-bugle-headline', String(p.title || '')));
    lede.appendChild(el('div', 'arc-bugle-rule'));
    sheet.appendChild(lede);

    const cols = el('div', 'arc-bugle-cols' + (p.halfStar ? ' is-sparse' : ''));
    const paras = paragraphsOf(p.body);
    for (let i = 0; i < paras.length; i += 1) {
      cols.appendChild(el('p', 'arc-bugle-para' + (i === 0 ? ' is-open' : ''), paras[i]));
    }
    if (!paras.length && !p.halfStar) {
      cols.appendChild(el('p', 'arc-bugle-para', t('bugle_empty', 'Nothing set for this page.')));
    }
    /* THE HALF STAR. One page in the season prints nearly nothing on purpose,
     * and what it does print is this: a single small star, clipped to its left
     * half, slightly off centre, exactly as it came off the stone. The flag
     * renders the ornament between the paragraphs so the emptiness reads as
     * the page's content rather than a loading failure. */
    if (p.halfStar) {
      const orn = el('div', 'arc-bugle-halfstar');
      attr(orn, 'aria-hidden', 'true');
      orn.appendChild(el('span', 'arc-bugle-halfstar-glyph', '★'));
      cols.insertBefore(orn, cols.children[1] || null);
    }
    sheet.appendChild(cols);

    /* THE COMICS BOX. `comics: true` draws the three-frame slot (the shape
     * awaiting an art pass). `comics: '<caption>'` is the season's form: ONE
     * framed panel, empty but for its printed caption beneath - the panel is
     * described, never drawn, which is the correct amount of newspaper. */
    if (p.comics) {
      const box = el('figure', 'arc-bugle-comics');
      box.appendChild(el('figcaption', 'arc-bugle-comics-cap',
        t('bugle_comics', 'Comics').toUpperCase()));
      const strip = el('div', 'arc-bugle-strip');
      attr(strip, 'aria-hidden', 'true');
      const single = typeof p.comics === 'string';
      const frames = single ? 1 : 3;
      for (let i = 0; i < frames; i += 1) {
        const panel = el('div', 'arc-bugle-panel' + (single ? ' is-wide' : ''));
        if (!single) panel.appendChild(el('span', 'arc-bugle-panelnum', String(i + 1)));
        /* The single panel is empty on purpose; without a printed reason a
         * reader takes it for a picture that failed to load (T2, 08-27). */
        if (single) panel.appendChild(el('span', 'arc-bugle-panelheld',
          t('bugle_comics_held', 'Picture held at the printer. Described below.')));
        strip.appendChild(panel);
      }
      box.appendChild(strip);
      box.appendChild(el('p', 'arc-note arc-bugle-comics-note',
        single ? String(p.comics) : 'PLACEHOLDER: the strip goes in here.'));
      sheet.appendChild(box);
    }

    for (let i = 0; i < tabButtons.length; i += 1) {
      const on = (i === page);
      try { tabButtons[i].classList.toggle('is-on', on); } catch (e) { /* noop */ }
      attr(tabButtons[i], 'aria-selected', on ? 'true' : 'false');
    }
    if (prev) prev.disabled = (page <= 0);
    if (next) next.disabled = (page >= pages.length - 1);
    if (folio) folio.textContent = t('bugle_page', 'Page') + ' ' + (page + 1) + ' / ' + Math.max(1, pages.length);

    /* THE TURN IS A TRANSFORM, NEVER A BACKGROUND (trap 36). Remove, reflow,
     * re-add is the same re-trigger dance the split-flap board does (trap 4):
     * without the forced reflow the browser coalesces it and nothing moves. */
    try {
      sheet.classList.remove('is-turning');
      void sheet.offsetWidth;
      sheet.classList.add('is-turning');
    } catch (e) { /* a DOM double has no layout to force - nothing to animate */ }
  }

  /** Turn to a page. Clamped, idempotent, and it banks where the player is. */
  function goto(index) {
    const want = clampPage(index);
    if (want === page) return page;
    page = want;
    sfx('flap', 0.22);
    paintPage();
    s.issues[issue.id] = Object.assign({}, s.issues[issue.id] || {}, { lastPage: page });
    persist(s, o.save);
    try { if (typeof o.onPage === 'function') o.onPage(page, issue.id); }
    catch (e) { if (deps.log) { try { deps.log('bugle onPage: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
    return page;
  }

  /* ------------------------------- the foot ----------------------------- */
  const foot = el('div', 'arc-bugle-foot');
  const prev = el('button', 'btn ghost arc-bugle-turn', t('bugle_prev', 'Previous page'));
  prev.type = 'button';
  prev.addEventListener('click', () => { goto(page - 1); });
  const folio = el('span', 'arc-bugle-folio');
  const next = el('button', 'btn ghost arc-bugle-turn', t('bugle_next', 'Next page'));
  next.type = 'button';
  next.addEventListener('click', () => { goto(page + 1); });
  foot.appendChild(prev);
  foot.appendChild(folio);
  foot.appendChild(next);
  if (pages.length > 1) paper.appendChild(foot);

  /* ------------------------------ the way out --------------------------- */
  let closed = false;
  let escBound = false;

  function onKey(e) {
    if (!e) return;
    if (e.key !== 'Escape' && e.key !== 'Esc') return;
    try { e.preventDefault(); e.stopPropagation(); } catch (err) { /* noop */ }
    close();
  }

  function close() {
    if (closed) return;
    closed = true;
    if (escBound) {
      try { doc.removeEventListener('keydown', onKey, true); } catch (e) { /* noop */ }
      escBound = false;
    }
    try { root.remove(); } catch (e) { /* noop */ }
    if (live && live.handle === handle) live = null;
    sfx('paper', 0.18, { pitch: 0.92 });
    try { if (typeof o.onClose === 'function') o.onClose(); }
    catch (e) { if (deps.log) { try { deps.log('bugle onClose: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
  }

  const back = el('button', 'btn primary arc-bugle-back', t('back', 'Back'));
  back.type = 'button';
  back.addEventListener('click', close);
  signExit(back, { dir: 'back' });
  paper.appendChild(exitBar([back]));

  /* THE STAGE IS A DOOR TOO - a press on the dusk outside the paper folds it.
   * The paper is read-only, so a stray press costs nothing. */
  root.addEventListener('click', (e) => {
    if (e && e.target === root) close();
  });

  root.appendChild(paper);
  mount.appendChild(root);
  paintPage();

  if (o.bindEscape && typeof doc.addEventListener === 'function') {
    doc.addEventListener('keydown', onKey, true);
    escBound = true;
  }

  /* THE VISIT, BANKED. One write per open. `readAt` is set once and never
   * moved: it is "this issue has been picked up", not "when last touched". */
  const today = localDay();
  const row = Object.assign({ readAt: null, lastPage: 0 }, s.issues[issue.id] || {});
  if (!row.readAt) row.readAt = today;
  row.lastPage = page;
  s.issues[issue.id] = row;
  const latest = latestIssue();
  if (latest && latest.id === issue.id) s.latestSeen = issue.id;
  s.opens = Math.max(0, Math.round(Number(s.opens) || 0)) + 1;
  persist(s, o.save);

  if (!wasRead) {
    try { if (typeof o.onRead === 'function') o.onRead(issue.id); }
    catch (e) { if (deps.log) { try { deps.log('bugle onRead: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
  }

  sfx('paper', 0.3);
  focusSoon(back);

  const handle = {
    root: root,
    issue: issue,
    close: close,
    destroy: close,
    goto: goto,
    get page() { return page; },
    get closed() { return closed; },
  };
  live = { handle: handle, firstButton: back, get closed() { return closed; } };
  return handle;
}

/** The open paper, or null. Test seam and the driver's re-entry guard. */
export function currentBugle() {
  return (live && !live.closed) ? live.handle : null;
}

/* ----------------------------------------------------------------------------
 * THE PROP
 * A folded paper the driver scatters on the campus - on a bench, under a door,
 * in a rack by the hall. It is a BUTTON that reads as an object: the masthead
 * shows above the fold, the fold itself is a crease across the middle, and the
 * whole thing sits at an angle because nobody puts a newspaper down straight.
 *
 * POSITIONING IS THE DRIVER'S: --np-x / --np-y (percent of the parent,
 * absolute), overridden on the returned element or from campus CSS.
 * -------------------------------------------------------------------------- */

/**
 * @param {Object} parentEl
 * @param {Object=} opts
 * @param {Function=} opts.onOpen   called on click (the driver calls openBugle)
 * @param {Object=} opts.state
 * @param {string=} opts.label
 * @returns {?Object} {el, root, refresh(), destroy()}
 */
export function mountBugleProp(parentEl, opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  if (!parentEl || typeof parentEl.appendChild !== 'function') return null;

  ensureStyles(doc);

  const btn = el('button', 'arc-bugleprop');
  btn.type = 'button';
  const label = o.label || t('bugle_prop_label', 'The paper');
  attr(btn, 'aria-label', label);
  attr(btn, 'title', label);

  const fold = el('span', 'arc-bugleprop-fold');
  attr(fold, 'aria-hidden', 'true');
  // The masthead sliver above the fold, then three ruled lines of column, then
  // the crease. Fixed geometry: scenery that re-arranges itself reads as a bug.
  fold.appendChild(el('i', 'arc-bugleprop-name'));
  const lines = el('i', 'arc-bugleprop-lines');
  fold.appendChild(lines);
  fold.appendChild(el('i', 'arc-bugleprop-crease'));
  btn.appendChild(fold);

  btn.appendChild(el('span', 'arc-bugleprop-label', label.toUpperCase()));

  const dot = el('i', 'arc-bugleprop-new');
  attr(dot, 'aria-hidden', 'true');
  btn.appendChild(dot);

  function refresh() {
    let fresh = false;
    try { fresh = hasUnreadIssue(o.state); } catch (e) { fresh = false; }
    try { btn.classList.toggle('has-new', !!fresh); } catch (e) { /* noop */ }
    return fresh;
  }

  btn.addEventListener('click', () => {
    sfx('paper', 0.2);
    try { if (typeof o.onOpen === 'function') o.onOpen(); }
    catch (e) { if (deps.log) { try { deps.log('bugle prop onOpen: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
    refresh();
  });

  refresh();
  parentEl.appendChild(btn);

  return {
    el: btn,
    root: btn,
    refresh: refresh,
    destroy() { try { btn.remove(); } catch (e) { /* noop */ } },
  };
}

export default openBugle;
