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
 *  4. THE COPY IN THIS FILE IS A PLACEHOLDER AND SAYS SO. Every visible string
 *     in the catalog is prefixed `PLACEHOLDER:` and is structure only - the
 *     real letters arrive from the writing pass. Chrome strings (buttons,
 *     labels) are NOT here at all: they are lexicon rows in mailbox.js, because
 *     a letter body is content and a button caption is chrome.
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
 * PLACEHOLDER COPY, STRUCTURE ONLY. Six letters across all five letterheads and
 * across every clause the engine answers, so the mailbox has something honest
 * to be judged on before a single real word is written. Lengths vary on
 * purpose: a one-paragraph note and a four-paragraph letter break the paper in
 * different places.
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
    id: 'welcome',
    from: 'PLACEHOLDER: sender one',
    letterhead: 'office',
    heading: 'PLACEHOLDER: the first head line',
    body: Object.freeze([
      'PLACEHOLDER: the opening paragraph of the first letter, long enough that the paper has to wrap it and short enough that nobody has to scroll for the end of it.',
      'PLACEHOLDER: a second paragraph, which is where a real letter would say the one thing it came to say.',
    ]),
    trigger: Object.freeze({}),
    once: true,
  }),
  Object.freeze({
    id: 'first_card',
    from: 'PLACEHOLDER: sender two',
    letterhead: 'faculty',
    heading: 'PLACEHOLDER: the second head line',
    body: Object.freeze([
      'PLACEHOLDER: one paragraph, because a short letter is a shape the paper has to survive too.',
    ]),
    trigger: Object.freeze({ punchesAtLeast: 1 }),
    once: true,
  }),
  Object.freeze({
    id: 'streak_three',
    from: 'PLACEHOLDER: sender three',
    letterhead: 'notice',
    heading: 'PLACEHOLDER: the third head line',
    body: Object.freeze([
      'PLACEHOLDER: the first of four paragraphs, and the longest letter in the box, so the paper is measured against the worst case it will ever be asked to hold.',
      'PLACEHOLDER: the second paragraph carries on at the same width.',
      'PLACEHOLDER: the third paragraph is where a real letter would turn.',
      'PLACEHOLDER: and the last one signs off.',
    ]),
    trigger: Object.freeze({ streakAtLeast: 3, notSeen: 'mail_streak_shown' }),
    once: true,
  }),
  Object.freeze({
    id: 'week_one',
    from: 'PLACEHOLDER: sender four',
    letterhead: 'personal',
    heading: 'PLACEHOLDER: the fourth head line',
    body: Object.freeze([
      'PLACEHOLDER: a letter that only makes sense after the first one has been read, which is what the afterRead clause is for.',
      'PLACEHOLDER: a closing line.',
    ]),
    trigger: Object.freeze({ dayAtLeast: 5, afterRead: 'welcome' }),
    once: true,
  }),
  Object.freeze({
    id: 'flagged',
    from: 'PLACEHOLDER: sender five',
    letterhead: 'unsigned',
    heading: 'PLACEHOLDER: the fifth head line',
    body: Object.freeze([
      'PLACEHOLDER: an unsigned sheet, held until the shell says the flag it asks about has been shown.',
      'PLACEHOLDER: a second short paragraph, and no name at the end of it.',
    ]),
    trigger: Object.freeze({ seen: 'annex' }),
    once: true,
  }),
  Object.freeze({
    id: 'dated',
    from: 'PLACEHOLDER: sender one',
    letterhead: 'office',
    heading: 'PLACEHOLDER: the sixth head line',
    body: Object.freeze([
      'PLACEHOLDER: the one letter in the box that may arrive more than once, on the same local date every year, and never twice in a day.',
    ]),
    trigger: Object.freeze({ dateIs: '10-31' }),
    once: false,
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
    streak: num(ctx && ctx.streak),
    dateIs: (ctx && typeof ctx.dateIs === 'string') ? ctx.dateIs : '',
    seenFlags: (ctx && isObj(ctx.seenFlags)) ? ctx.seenFlags : {},
    delivered: (ctx && Array.isArray(ctx.delivered)) ? ctx.delivered : [],
    read: (ctx && Array.isArray(ctx.read)) ? ctx.read : [],
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
    for (let i = 0; i < letters.length; i += 1) {
      const r = rec(letters[i].id);
      if (!r || !num(r.deliveredAt)) continue;
      delivered.push(letters[i].id);
      if (num(r.readAt)) read.push(letters[i].id);
    }
    return {
      day: num(c.day),
      punches: num(c.punches),
      streak: num(c.streak),
      // A shell that does not carry the date gets today's, LOCAL, off the clock
      // it handed us - never a UTC one (trap 8).
      dateIs: (typeof c.dateIs === 'string' && c.dateIs) ? c.dateIs : monthDayOf(clock()),
      seenFlags: isObj(c.seenFlags) ? c.seenFlags : {},
      delivered,
      read,
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
  };
}

export default initMail;
