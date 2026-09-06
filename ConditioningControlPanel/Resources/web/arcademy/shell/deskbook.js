/* ============================================================================
 * shell/deskbook.js - THE BOOK ON THE DESK.
 *
 * The big open volume in the Records Office, read as a two-page spread over the
 * painted plate: the left page and the right page are two elements the room
 * pins to the rects the art was measured at (shell/recordsroom.js's LEDGER),
 * and everything between them - the chapters, the tabs down the right edge, the
 * page arrows, the turn - lives here.
 *
 * `left` and `right` are SIDE HOSTS, one box out from the paper. The paper
 * clips (a page does); the tabs hang off the fore-edge and must not be clipped.
 * See the leaves, below - it is the whole reason the second box exists.
 *
 * THREE THINGS IT IS NOT
 *  - it is not a screen. It mints no stage, binds no Esc, and knows nothing
 *    about the router. The room hands it two rects and takes it away again.
 *  - it is not LEXICON COPY. The prose in a book is prose: it does not go
 *    through `t()`, because trap 26 caps a mod-skinnable row at 96 characters
 *    and a paragraph is not a label. Only the chrome is keyed - the three
 *    chapter tabs and the two page arrows - and those are short by nature.
 *  - it is not THIS FILE'S WRITING. `BOOK` below is the owner's approved draft
 *    transcribed verbatim (W2, 2026-08-25): ten spreads, three chapters, two
 *    blank cream pages and one numbered list. Nobody but the owner writes the
 *    school's story, so nothing in that table gets tightened, re-punctuated or
 *    "improved" by anybody editing this file for another reason.
 *
 * THE TURN. A page turn is one half-page rotating on the spine (CSS, ~500ms),
 * and the spread underneath repaints at the MIDPOINT so the new pages are
 * revealed by the leaf rather than popping in behind it. `.arc-reduced` cuts
 * the leaf entirely and the spread simply changes - the decoration law, and the
 * page you asked for is never the thing that is lost.
 *
 * THE KEYS. ArrowLeft / ArrowRight, on `document`, and they are a PASSIVE READ
 * with one guard: the injected `isActive()` (the room answers "the book view is
 * the live slide"). It never touches Escape - boot.js owns that key and the
 * shell owns its ladder (traps 29 / 48 / 80) - and it never calls
 * `stopPropagation`. Modifier chords are ignored, so a browser shortcut is
 * never eaten by a book nobody is looking at.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';

/* ----------------------------------------------------------------------------
 * THE TABLE
 *
 * `{chapters:[{key, titleKey, title, pages:[{head, body, list?, blank?}]}]}` -
 * and this is the OWNER'S PROSE, transcribed verbatim from the approved draft
 * (`arcademy-vn-set/records/BOOK-DRAFT.md`, 2026-08-25). Nobody but the owner
 * writes the school's story; nothing below is paraphrased, re-punctuated or
 * tightened, and there is not an em-dash in it, which is not an accident.
 *
 * WHAT A PAGE IS.
 *   head   the page's own heading, exactly as the draft headed it. Uppercased
 *          by the sheet, so the case here is the draft's.
 *   body   PARAGRAPHS, separated by a blank line. The draft's hard wrapping is
 *          not content; its paragraph breaks are.
 *   list   an ordered list, for the numbered house rules only. `n` carries the
 *          rule's own number so THE REST can open at six instead of restarting
 *          at one, which is what a book does and what an `<ol>` will not do on
 *          its own.
 *   blank  a cream page with nothing on it. The inside cover's verso and the
 *          book's back page are BLANK in the draft, and a blank page in a real
 *          book is blank - not a note about being blank. It keeps its folio.
 *
 * THE SHAPE, and both halves of it are load-bearing.
 *   - Every chapter starts on an EVEN page index, so a tab lands the reader on
 *     a whole spread rather than in the middle of one (`paginate` derives the
 *     spread by floor(at/2), and an odd start would hide the chapter's first
 *     page behind a backwards turn).
 *   - THREE chapters, three tabs, three lexicon rows that already exist and are
 *     already mirrored in the C# NeutralLexicon. The maintenance page is the
 *     back of the book and rides at the end of `tips` rather than minting a
 *     fourth tab: "Maintenance" is not a chapter of anything, and a key minted
 *     here would render as a de-snaked stub on the desktop host.
 *
 *   0-1   the inside cover        story   spread 0
 *   2-9   THE ARCADEMY                    spreads 1-4
 *   10-13 HOUSE RULES             rules   spreads 5-6
 *   14-23 TIPS, ten rooms         tips    spreads 7-11
 *   24-25 the back page                   spread 12
 *
 * SIX PAGES ARE CONTINUATIONS, and they are the whole difference between this
 * table and the draft's ten spreads. The draft budgeted 90-140 words a page;
 * at the shipped 19px on a 436x492 rect the real ceiling is nearer 95 with a
 * heading, and four of the draft's pages ran 31 to 167 pixels past the
 * fore-edge - measured headless, every spread, not eyeballed. Each of those
 * four TURNS at a paragraph or a rule boundary rather than dropping the hand
 * the book is written in, which is the owner's own instruction. A continuation
 * page carries no head, because a continuation page in a real book carries
 * none; the numbered rules carry their own numbers across the break.
 * -------------------------------------------------------------------------- */

export const BOOK = Object.freeze({
  chapters: Object.freeze([
    Object.freeze({
      key: 'story',
      titleKey: 'records_book_ch_school',
      title: 'The Arcademy',
      pages: Object.freeze([
        /* -------------------------------------------- the inside cover */
        Object.freeze({ head: '', body: '', blank: true }),
        Object.freeze({
          head: 'A note on the first page',
          body: "Hi. This is the desk book. Whoever's on the desk writes in it, and since nobody's on the desk after dark that's mostly been us, the front desk, in a few different handwritings over the years. The first part is the story of the place as far as anyone remembers it, the middle is the house rules, and the back is tips for every room, which get updated whenever somebody finds a better way and leaves a note in the tray.",
        }),
        /* CONTINUATION. The draft wrote this as one page; at the shipped 19px
         * on a 436x492 rect it ran 76px past the fore-edge (measured headless,
         * not guessed), so it turns at the paragraph break rather than
         * shrinking the hand the book is written in. No head: a continuation
         * page in a real book has none. */
        Object.freeze({
          head: '',
          body: "You're welcome to read it here as long as you like. Please don't take it out of the office, the last copy that left came back with pool water in it.\n\n- the front desk",
        }),
        /* -------------------------------------------- THE ARCADEMY */
        Object.freeze({
          head: 'How it started',
          body: "The building was a bowling alley first, then a roller rink for about a year, and then it sat empty long enough that the pigeons got the mail. The cabinets came later, one at a time, from an arcade across town that was closing and didn't want to pay to haul them. The Main Hall got the first five, room 101 to 105, and somebody with a label maker decided that if the machines were going to be in rooms then the rooms should have numbers, and once you've got numbered rooms you've basically got a school.",
        }),
        Object.freeze({
          head: '',
          body: 'The name was a joke on the sign order form. It stuck because the sign was already paid for.',
        }),
        Object.freeze({
          head: 'The midway',
          body: 'The long corridor with the checkered tape is the midway. It was supposed to be a hallway. The tape went down for one open night and never came back up, and the route lamps came with the cabinets so they got hung too, and now if you take the tape up the place looks wrong.',
        }),
        Object.freeze({
          head: '',
          body: "Classes run in the evening because that's when the power's cheap. The board over the front desk deals four rooms at first bell, homeroom first, then the other three in whatever order you like. The board is old, it clatters, and it is right far more often than it has any business being, so we stopped arguing with it.",
        }),
        Object.freeze({
          head: 'The wings',
          body: "The west spur got its own rooms the second term, 201 to 203, after the hall ran out of wall. The 300s are up the stairs by the bell tower and they're the coldest rooms we've got, bring a jumper. The pool is the pool. It came with the building and nobody has ever found the drain, so it stays full, and room 105 was built around it rather than the other way round.",
        }),
        Object.freeze({
          head: '',
          body: "One room in the west spur went dark in term three. The cabinet is fine, the plaque is still on the door. It's just not on the board anymore, and the tape across the doorway is there so nobody trips over the cable.",
        }),
        Object.freeze({
          head: 'The office',
          body: "This room is where the cards live. Every student gets one card per class, ten stamps on it, and the wall behind the counter is where they hang between nights. The board deals classes but the office keeps score, which is the only reason the office exists, that and the phone, which has rung twice in living memory, both times a wrong number.\n\nThe door on the right is the storeroom. It sticks. If it's open, that's the draught, not us.",
        }),
      ]),
    }),
    Object.freeze({
      key: 'rules',
      titleKey: 'records_book_ch_rules',
      title: 'House rules',
      pages: Object.freeze([
        Object.freeze({
          head: 'The board and the stamps',
          body: '',
          list: Object.freeze([
            Object.freeze({ n: 1, text: "The board deals at dusk. Four rooms, homeroom first, the rest in any order. What's lit is what's on tonight, and it changes tomorrow." }),
            Object.freeze({ n: 2, text: "Finish a class and your card gets one stamp for that room. One a night per class, no matter how many times you replay it, and replaying is fine, we just don't stamp twice." }),
            Object.freeze({ n: 3, text: "Leaving a class early doesn't stamp. Neither does a free swim. They're for practice, and practice is free." }),
          ]),
        }),
        /* CONTINUATION, and the `n` on every rule is exactly why the list is
         * data: four is still four on the second page of the section. */
        Object.freeze({
          head: '',
          body: '',
          list: Object.freeze([
            Object.freeze({ n: 4, text: "Ten stamps and the card's full. A full card means that room stays lit for you on the board every night after, whether the board dealt it or not." }),
            Object.freeze({ n: 5, text: 'Grades are S, A, B and C. A C still stamps. We are not that kind of school.' }),
          ]),
        }),
        Object.freeze({
          head: 'The rest',
          body: '',
          list: Object.freeze([
            Object.freeze({ n: 6, text: "Tokens you don't use can go in the fountain in the quad. There's no rule that it's lucky, it's just what everybody does, and the yearbook has three pages of people saying it worked." }),
            Object.freeze({ n: 7, text: 'The notice board in the hall is for notices. The one in this office is the same board, we just post here first because the pins are here.' }),
          ]),
        }),
        Object.freeze({
          head: '',
          body: '',
          list: Object.freeze([
            Object.freeze({ n: 8, text: 'Cabinets act up. Give it one gentle kick, then leave us a note in the tray. Two kicks is a maintenance ticket and those take a week.' }),
            Object.freeze({ n: 9, text: "Don't go in the pool after the bell. The lights go off on a timer and it's a long way to the ladder in the dark." }),
            Object.freeze({ n: 10, text: 'The storeroom is not a classroom. (Added term three.)' }),
          ]),
        }),
      ]),
    }),
    Object.freeze({
      key: 'tips',
      titleKey: 'records_book_ch_tips',
      title: 'Tips',
      pages: Object.freeze([
        Object.freeze({
          head: '101 Homeroom, Daily Trigger',
          body: "It's one word a day and it's the same word for everybody, so if you've already heard it in the corridor, that's on you. Six rows is your whole budget. The best opening word uses five different common letters, and the second row should spend what the first one told you rather than guessing fresh. Every wrong row turns the room up a notch, so a slow careful row three beats a fast wrong one. The stars mean right letter right place, the half marks mean right letter wrong seat, and a cross means it's not in there at all, stop trying it.",
        }),
        Object.freeze({
          head: '102 Memory Lab, Deja Vu',
          body: "Turn two slides, a pair stays lit, anything else flips back. The board only moves while nothing's face up and it always shudders first, so when you feel the shudder, look at the whole board and not at the slide you were about to turn. Clear a board and a fresh one deals until the bell, and the bell is the only thing that ends the class, so there's no prize for rushing the last pair. If the whole board re-deals, the pairs are the same, only the seats changed, which is annoying but not the same as starting over.",
        }),
        Object.freeze({
          head: '103 Discipline Hall, Impulse Control',
          body: "A bubble lands in the dish, you pop it, and the faster you are the more it pays. A bubble wearing an X is a trap, and the trick is that the room wants your hand on the button already, so keep your hand off the button and on the table until you've read the bubble. Misses just drift off the dish, nothing's taken from you, so the only real way to lose is to pop something you shouldn't. Ninety seconds. It's short because your hands get tired, not because we're being kind.",
        }),
        Object.freeze({
          head: '104 Lost & Found, Lost & Found',
          body: "You're given a list and the wall keeps moving. Pick one thing from the list and hunt only that, then the next, because scanning for all of them at once is how the wall wins. The wall drifts in rows, so when a row slides, the thing you were tracking is still in that row, just further along. When the picture glitches and swaps, don't chase the swap, wait one beat and it settles. If you finish the list before the bell, the class is done, and yes you can go and get a drink.",
        }),
        Object.freeze({
          head: '105 The Pool, The Deep End',
          body: "Swipe, or use the arrows, and every tile on the board slides that way at once. Two matching tiles meet and sink one depth. Keep your heaviest tile in a corner and never swipe away from that corner unless there's nothing else, and there is almost always something else. A locked board isn't a loss, the depth you reached is earned and the water turns fresh. The ladder ends at the eleventh depth and the class holds you there if you make it. Free swim starts at the ladder and never stamps, so that's the place to practise the corner thing.",
        }),
        Object.freeze({
          head: '201 Sort',
          body: "Cards come and you send them where they go. The label on each card is the rule, and the rule changes without a sign, so read the card and not the pile you sent the last one to. There's a somebody in that room who deals a card wrong on purpose now and then. Don't take it personally, just send it back where it belongs. Three minutes. The pace picks up in the last minute and that's when the mistakes happen, so slow down a hair exactly when it feels like you should speed up.",
        }),
        Object.freeze({
          head: '202 Echo',
          body: "The pads play a sequence, you watch it and listen to it, then you repeat it back in order by tap or by key. Listening is the half people skip, and the notes are different enough that your ears can carry a sequence your eyes lose. A pad may light out of turn. Leave that one alone, it's not part of the sequence, it's the room being the room. Two minutes, quick class, good for a night when you've only got one more in you.",
        }),
        Object.freeze({
          head: '203 Instant Recall',
          body: "A wall of your own media keeps changing and effects fire over it, and then without warning everything freezes and you're asked what just happened. A word, an effect, a spiral, a face from the wall. Early on a bell warns you before the stop. Later there's no bell, it just stops. The only tip that works is to actually watch, which sounds like nothing and isn't. Three minutes, and it stops about fifteen times, so budget your attention like it's tokens.",
        }),
        Object.freeze({
          head: '301 Anomaly',
          body: "Every tile on the wall is the same loop, playing in step, and one isn't. Tap it. The first tap is the one that counts, so a wrong first tap costs you the round. The room tints, drifts and glitches every tile at once, and all of that is noise, because noise hits every tile the same way and the odd one out is odd in a way the noise can't fake. Look for the tile that's a half-beat behind the rest. That's usually it.",
        }),
        Object.freeze({
          head: '302 Composure',
          body: "Tap a piece beside the gap and it slides in. Arrows, WASD and swipes do the same. A piece that reaches its own place locks with a snap, and it can still be slid, so don't panic when a locked piece moves. The room will bury the board in wash and you keep sliding anyway, because the picture underneath never moved. Finish a picture and the next one deals. The bell ends the class, not the solve, so a half-finished picture at the bell is fine, the solves you earned are what count.",
        }),
        /* ------------------------------------------------ the back page */
        Object.freeze({
          head: 'Maintenance',
          body: 'Storeroom door, right wall. Sticks in damp weather. Plane the top edge or replace the latch. Been on this list since term three. Low priority, nobody uses it.\n\nRoute lamp, midway, third from the hall end. Flickers. Bulb is fine. Left as is.',
        }),
        Object.freeze({ head: '', body: '', blank: true }),
      ]),
    }),
  ]),
});

/** The leaf's whole travel. Mirrored in recordsroom.css - move one, move both. */
export const FLIP_MS = 500;
/** When the spread underneath repaints: the leaf is edge-on to the reader. */
const FLIP_MID_MS = Math.round(FLIP_MS * 0.5);

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

/** The shell's `<html class="arc-reduced">`, read defensively. */
function htmlReduced() {
  try {
    const de = (typeof document !== 'undefined') ? document.documentElement : null;
    if (de && de.classList && typeof de.classList.contains === 'function'
      && de.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* noop */ }
  return false;
}

/** One cue through the one door (trap 18): a REQUEST on `document`, never a node. */
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

/**
 * Flatten the table into the reading order, remembering where each chapter
 * begins. PURE - handed the table so a suite can drive a fixture of its own.
 * @returns {{pages:Array, chapters:Array, spreads:number}}
 */
export function paginate(book) {
  const table = (book && Array.isArray(book.chapters)) ? book.chapters : [];
  const pages = [];
  const chapters = [];
  for (let c = 0; c < table.length; c += 1) {
    const ch = table[c] || {};
    const rows = Array.isArray(ch.pages) ? ch.pages : [];
    const at = pages.length;
    for (let p = 0; p < rows.length; p += 1) {
      const row = rows[p] || {};
      pages.push({
        head: String(row.head || ''),
        body: String(row.body || ''),
        /* THE NUMBERED RULES keep their own numbers across the page break, so
         * the list rides through as data rather than as a paragraph that says
         * "6." in it. Absent on every other page, which is most of them. */
        list: Array.isArray(row.list) ? row.list.slice() : null,
        blank: !!row.blank,
        chapter: c,
        n: pages.length + 1,
      });
    }
    chapters.push({
      key: String(ch.key || ('ch' + c)),
      titleKey: ch.titleKey || null,
      title: String(ch.title || ''),
      at: at,
      /* A chapter opens on the SPREAD its first page falls on - a tab that
       * landed the reader on a right-hand page would hide half the chapter
       * behind a backwards turn. */
      spread: Math.floor(at / 2),
      pages: rows.length,
    });
  }
  return { pages: pages, chapters: chapters, spreads: Math.max(1, Math.ceil(pages.length / 2)) };
}

/* ----------------------------------------------------------------------------
 * THE BOOK
 * -------------------------------------------------------------------------- */

/**
 * createDeskBook(opts) -> {left, right, ...} or null with no DOM.
 *
 * @param {Object=} opts
 * @param {Function=} opts.t          lexicon lookup override
 * @param {Object=} opts.book         the table (default BOOK)
 * @param {number=} opts.page         the remembered SPREAD index
 * @param {Function=} opts.onPage     onPage(spread) - the room persists it
 * @param {Function=} opts.isActive   () -> is the book the live view?
 * @param {boolean=} opts.reduced     force the cut (the html class is honoured)
 * @param {Function=} opts.log
 */
export function createDeskBook(opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;

  const t = typeof o.t === 'function' ? o.t : lexT;
  const log = typeof o.log === 'function' ? o.log : function () {};
  const isActive = typeof o.isActive === 'function' ? o.isActive : function () { return true; };
  const model = paginate(o.book || BOOK);
  const reducedNow = () => !!o.reduced || htmlReduced();

  const timers = [];
  let dead = false;
  let busy = false;
  let spread = 0;

  /* THE REMEMBERED PAGE. A junk value, a value from a shorter book, a value
   * from a book that has since grown - all of them clamp into range rather
   * than opening on a blank spread. */
  spread = clampSpread(o.page);

  function clampSpread(v) {
    const n = Math.round(Number(v));
    if (!isFinite(n)) return 0;
    return Math.max(0, Math.min(model.spreads - 1, n));
  }

  function later(fn, ms) {
    const id = setTimeout(function () {
      if (dead) return;
      try { fn(); } catch (e) { log('deskbook timer threw: ' + ((e && e.message) || e)); }
    }, ms);
    timers.push(id);
    return id;
  }

  /* ------------------------------------------------------------- the leaves */
  /* TWO BOXES A SIDE, and the second one is not decoration.
   *
   * What the room pins to a measured page rect is a SIDE HOST (`.rdb-side`).
   * The paper (`.rdb-page`) fills it and CLIPS - it has to, it is a page: a
   * paragraph must not run off the edge of the sheet and the turning leaf
   * sweeps inside it. Anything that hangs OFF the paper therefore cannot be a
   * child of the paper, and the chapter tabs are exactly that: a real book's
   * index tabs stick out past the fore-edge. Parented to the page they were
   * laid out at `left:100%` inside an `overflow:hidden` box, which is to say
   * they were rendered and then clipped away to nothing - three tabs that
   * existed in the DOM and had never once been seen on screen.
   *
   * So they hang off the SIDE, which does not clip, over the painted page-edge
   * in the plate. The room still gets `left` and `right` and still pins them to
   * LEDGER_PAGES; the extra box is entirely inside this file. */
  const left = el('div', 'rdb-side rdb-side-left');
  const right = el('div', 'rdb-side rdb-side-right');

  const leftPage = el('div', 'rdb-page rdb-left');
  attr(leftPage, 'role', 'group');
  const rightPage = el('div', 'rdb-page rdb-right');
  attr(rightPage, 'role', 'group');
  left.appendChild(leftPage);
  right.appendChild(rightPage);

  const leftBody = el('div', 'rdb-leaf');
  const rightBody = el('div', 'rdb-leaf');
  leftPage.appendChild(leftBody);
  rightPage.appendChild(rightBody);

  /* THE TURNING LEAF. One node, parked and invisible until a turn: it sweeps
   * across the spine while the spread underneath repaints at the midpoint. It
   * is decoration end to end - `aria-hidden`, no text, no pointer events - so
   * a reduced-motion reader loses a sweep and nothing else. */
  const flip = el('i', 'rdb-flip');
  attr(flip, 'aria-hidden', 'true');
  rightPage.appendChild(flip);

  /* --------------------------------------------------------------- the tabs */
  /* Down the RIGHT page's outer edge, the way a real book's index tabs sit -
   * one per chapter, the live one pushed proud. A tab is a jump, never a page
   * turn: it lands on the chapter's own opening spread.
   *
   * SIBLING OF THE PAGE, NOT A CHILD OF IT (see the leaves, above). */
  const tabs = el('div', 'rdb-tabs');
  attr(tabs, 'role', 'tablist');
  const tabEls = [];
  for (let i = 0; i < model.chapters.length; i += 1) {
    const ch = model.chapters[i];
    const label = ch.titleKey ? String(t(ch.titleKey, ch.title)) : ch.title;
    const b = el('button', 'rdb-tab', label);
    b.type = 'button';
    attr(b, 'role', 'tab');
    attr(b, 'aria-label', label);
    attr(b, 'data-chapter', ch.key);
    /* eslint-disable-next-line no-loop-func */
    b.addEventListener('click', function () { goto(ch.spread, 'tab'); });
    tabs.appendChild(b);
    tabEls.push(b);
  }
  right.appendChild(tabs);

  /* ------------------------------------------------------------- the arrows */

  const prevLabel = String(t('records_book_prev', 'Back a page'));
  const nextLabel = String(t('records_book_next', 'Next page'));
  const prevBtn = el('button', 'rdb-arrow rdb-prev', '‹ ' + prevLabel);
  prevBtn.type = 'button';
  attr(prevBtn, 'aria-label', prevLabel);
  prevBtn.addEventListener('click', function () { prev(); });
  /* The arrows are ON the paper (the tabs are the only thing that hangs off). */
  leftPage.appendChild(prevBtn);

  const nextBtn = el('button', 'rdb-arrow rdb-next', nextLabel + ' ›');
  nextBtn.type = 'button';
  attr(nextBtn, 'aria-label', nextLabel);
  nextBtn.addEventListener('click', function () { next(); });
  rightPage.appendChild(nextBtn);

  /* ---------------------------------------------------------------- paint */

  /** The draft's hard wrapping is not content; its blank lines are. */
  function paragraphs(body) {
    return String(body || '').split(/\n\s*\n/).map(function (s2) {
      return s2.replace(/\s*\n\s*/g, ' ').trim();
    }).filter(function (s2) { return s2.length > 0; });
  }

  function paintSide(host, page) {
    host.textContent = '';
    if (!page || page.blank) {
      /* THE BLANK PAGE. The verso of the inside cover, the back of the book,
       * and the odd side left over by an odd page count are all the same
       * thing: an empty side of a real book is EMPTY, not a note about being
       * empty. It keeps its folio so the spread still reads as a book. */
      host.appendChild(el('p', 'rdb-blank', ''));
      if (page) host.appendChild(el('span', 'rdb-folio', String(page.n)));
      return;
    }
    if (page.head) host.appendChild(el('h3', 'rdb-head', page.head));
    /* ONE <p> PER PARAGRAPH. A page of prose that arrives as a single node is
     * a wall; the draft's paragraph breaks are the author's and they survive. */
    const paras = paragraphs(page.body);
    for (let i = 0; i < paras.length; i += 1) host.appendChild(el('p', 'rdb-body', paras[i]));
    if (page.list && page.list.length) {
      /* THE HOUSE RULES. A real <ol>, and every item carries its own `value`
       * so THE REST opens at six instead of starting again at one. */
      const ol = doc.createElement('ol');
      ol.className = 'rdb-list';
      for (let i = 0; i < page.list.length; i += 1) {
        const row = page.list[i] || {};
        const li = el('li', 'rdb-rule', String(row.text || ''));
        const n = Number(row.n);
        if (isFinite(n) && n > 0) attr(li, 'value', String(n));
        ol.appendChild(li);
      }
      host.appendChild(ol);
    }
    host.appendChild(el('span', 'rdb-folio', String(page.n)));
  }

  function paint() {
    paintSide(leftBody, model.pages[spread * 2] || null);
    paintSide(rightBody, model.pages[spread * 2 + 1] || null);
    const chapter = (model.pages[spread * 2] || model.pages[spread * 2 + 1] || {}).chapter;
    for (let i = 0; i < tabEls.length; i += 1) {
      const on = (i === chapter);
      try { tabEls[i].classList.toggle('is-live', !!on); } catch (e) { /* noop */ }
      attr(tabEls[i], 'aria-selected', on ? 'true' : 'false');
    }
    try {
      prevBtn.disabled = spread <= 0;
      nextBtn.disabled = spread >= model.spreads - 1;
    } catch (e) { /* noop */ }
  }

  /* ---------------------------------------------------------------- turns */

  /**
   * goto(target, why) - the ONE mover. Every verb (an arrow, a key, a tab)
   * lands here so the flip, the persist and the busy latch have exactly one
   * home. Answers whether the book actually moved.
   */
  function goto(target, why) {
    if (dead || busy) return false;
    const want = clampSpread(target);
    if (want === spread) return false;
    const dir = want > spread ? 'fwd' : 'back';

    if (reducedNow()) {
      spread = want;
      paint();
      bank();
      return true;
    }

    busy = true;
    try {
      flip.className = 'rdb-flip is-' + dir;
      /* the reflow the split-flap board reads for the same reason (trap 4):
       * without it the browser coalesces the start pose and nothing turns */
      void right.offsetWidth;
      flip.classList.add('is-turning');
    } catch (e) { /* noop */ }
    sfx('paper', 0.2, { pitch: dir === 'fwd' ? 1 : 0.94 });

    /* THE REVEAL IS BEHIND THE LEAF, not in front of it. */
    later(function () { spread = want; paint(); bank(); }, FLIP_MID_MS);
    later(function () {
      busy = false;
      try { flip.className = 'rdb-flip'; } catch (e) { /* noop */ }
    }, FLIP_MS + 20);
    return true;
  }

  function next() { return goto(spread + 1, 'next'); }
  function prev() { return goto(spread - 1, 'prev'); }

  function bank() {
    if (typeof o.onPage !== 'function') return;
    try { o.onPage(spread); }
    catch (e) { log('deskbook page save: ' + ((e && e.message) || e)); }
  }

  /* ----------------------------------------------------------------- keys */

  function onKey(ev) {
    if (dead || !ev) return;
    if (ev.altKey || ev.ctrlKey || ev.metaKey) return;
    let live = false;
    try { live = !!isActive(); } catch (e) { live = false; }
    if (!live) return;
    if (ev.key === 'ArrowRight') { next(); return; }
    if (ev.key === 'ArrowLeft') { prev(); }
  }
  if (typeof doc.addEventListener === 'function') doc.addEventListener('keydown', onKey);

  paint();

  return {
    left: left,
    right: right,
    /** The reading model (test seam): pages, chapters, spread count. */
    model: model,
    next: next,
    prev: prev,
    goto: goto,
    /** Which spread is open. */
    spread: function () { return spread; },
    /** True while a leaf is in the air (the busy latch). */
    turning: function () { return busy; },
    destroy() {
      if (dead) return;
      dead = true;
      for (let i = 0; i < timers.length; i += 1) { try { clearTimeout(timers[i]); } catch (e) { /* noop */ } }
      timers.length = 0;
      if (typeof doc.removeEventListener === 'function') {
        try { doc.removeEventListener('keydown', onKey); } catch (e) { /* noop */ }
      }
      try { left.remove(); } catch (e) { /* noop */ }
      try { right.remove(); } catch (e) { /* noop */ }
    },
  };
}

export default createDeskBook;
