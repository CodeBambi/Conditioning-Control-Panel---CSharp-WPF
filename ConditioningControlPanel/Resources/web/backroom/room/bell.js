/* ============================================================================
 * backroom/room/bell.js - the floor bell, pure (CONTRACT 10.16.B).
 *
 * The casino rings a bell when a machine pays big. Brake 1: it is somebody
 * else's party, so it is ONE LINE OF TEXT and never a sound.
 *
 * This file is the wording only: an entry off `GET /v2/backroom/bell/state`
 * becomes "someone hit 3 spirals 4 min ago". No DOM, no timers, no fetch,
 * importable from node.
 *
 * LAW I, display only: the relative time is `now - entry.t` against the CLIENT
 * clock, so a skewed clock only ever mis-says how long ago somebody else won.
 * Nothing here pays anything.
 *
 * LAW VII: every visible word is a lexicon key with an English fallback. The
 * keys are listed in the room/hud.js header for the integration pass.
 *
 * The pay is NOT printed. The contract's own example is the wording
 * ("someone hit 3 spirals 4 min ago"), and a floor full of other people's SP
 * totals is closer to a leaderboard than to a bell.
 * ==========================================================================*/

/** One line on screen at a time, rotating through the entries. */
export const ROTATE_MS = 8000;
/** The server caps its list at 20; the page never shows more than it is sent. */
export const MAX_ENTRIES = 20;
/** The opted-in display name is stored on the entry; the page still trims what it is handed. */
export const NAME_MAX = 24;

const MIN = 60000, HOUR = 3600000, DAY = 86400000;

/** station -> line -> [lexicon key, English fallback]. An entry off this map is dropped, never printed raw. */
export const PHRASES = Object.freeze({
  slot: Object.freeze({
    emi3: Object.freeze(['br_bell_slot_emi3', 'hit the jackpot']),
    gif3same: Object.freeze(['br_bell_slot_gif3same', 'hit three of a kind']),
    sub3: Object.freeze(['br_bell_slot_sub3', 'hit 3 subliminals']),
    spiral3: Object.freeze(['br_bell_slot_spiral3', 'hit 3 spirals']),
  }),
  wheel: Object.freeze({
    jackpot: Object.freeze(['br_bell_wheel_jackpot', 'took the pot']),
    dazed: Object.freeze(['br_bell_wheel_slice', 'hit a big slice']),
    deep: Object.freeze(['br_bell_wheel_slice', 'hit a big slice']),
  }),
  cards: Object.freeze({
    blackjack: Object.freeze(['br_bell_cards_blackjack', 'drew a natural']),
  }),
  roulette: Object.freeze({
    wake: Object.freeze(['br_bell_roulette_wake', 'doubled on a Spiral Wake']),
  }),
});

const fill = (s, vars) => String(s).replace(/\{(\w+)\}/g, (m, k) => (k in vars ? String(vars[k]) : m));

/** A lex reader that always answers a string, whatever the room hands us. */
const reader = (lex) => (key, fallback, vars) => {
  let s = fallback;
  try { if (typeof lex === 'function') { const v = lex(key, fallback); if (typeof v === 'string' && v) s = v; } } catch (e) { s = fallback; }
  return vars ? fill(s, vars) : String(s);
};

/**
 * The stored display name, read defensively: trimmed, inner whitespace
 * collapsed, truncated to 24. Anything that is not a non-empty string is null.
 */
export function bellName(name) {
  if (typeof name !== 'string') return null;
  const s = name.trim().replace(/\s+/g, ' ');
  return s ? s.slice(0, NAME_MAX) : null;
}

/** "just now" / "4 min ago" / "3 h ago" / "2 d ago", from the client clock. */
export function agoText(ms, lex) {
  const L = reader(lex);
  const d = Number.isFinite(ms) && ms > 0 ? ms : 0;
  if (d < MIN) return L('br_bell_ago_now', 'just now');
  if (d < HOUR) return L('br_bell_ago_min', '{n} min ago', { n: Math.floor(d / MIN) });
  if (d < DAY) return L('br_bell_ago_hour', '{n} h ago', { n: Math.floor(d / HOUR) });
  return L('br_bell_ago_day', '{n} d ago', { n: Math.floor(d / DAY) });
}

const own = (o, k) => !!o && typeof k === 'string' && Object.prototype.hasOwnProperty.call(o, k);

/**
 * The phrase pair for an entry, or null when the station or the line is not one
 * we print. Own keys only: a `line` of "toString" is an unknown line, not a
 * function off Object.prototype.
 */
export function phraseOf(entry) {
  if (!entry || !own(PHRASES, entry.station)) return null;
  const st = PHRASES[entry.station];
  return own(st, entry.line) ? st[entry.line] : null;
}

/**
 * One bell line, or null for an entry the room does not print.
 * "someone hit 3 spirals 4 min ago"; an opted-in entry puts the name where "someone" was.
 */
export function bellLine(entry, now, lex) {
  const p = phraseOf(entry);
  if (!p) return null;
  const L = reader(lex);
  const who = bellName(entry && entry.name) || L('br_bell_someone', 'someone');
  const t = Number(entry && entry.t);
  const ago = agoText(Number.isFinite(t) ? Number(now) - t : 0, lex);
  const what = L(p[0], p[1]);
  return L('br_bell_line', '{who} {what} {ago}', { who, what, ago });
}

/**
 * Every line the ticker rotates through, newest first, at most MAX_ENTRIES.
 * `standing` (the must-hit line, 10.16.E) is a first line that is not an entry.
 */
export function bellLines(entries, now, lex, standing) {
  const out = [];
  if (typeof standing === 'string' && standing) out.push(standing);
  const rows = Array.isArray(entries) ? entries.slice(0, MAX_ENTRIES) : [];
  for (const e of rows) {
    const line = bellLine(e, now, lex);
    if (line) out.push(line);
  }
  return out;
}
