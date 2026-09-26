/* ============================================================================
 * ui/shareWords.js - the words and numbers on the share card (2026-09-24).
 *
 * Pure: no DOM, no canvas, safe under node. ui/shareCard.js draws whatever this
 * hands it, and test/selftest-share.js pins the rules below.
 *
 * THE CARD TAKES A PLAIN OBJECT, NOT A MATCH:
 *   { you:  { name, avatarUrl, score, stats: { key: number } },
 *     them: { name, avatarUrl, score, stats: { key: number } },
 *     flavour, outcome: 'w'|'l'|'d'|'a', seed, highlights: [string], date }
 * buildShareData() is the ONE place a match is read. A new scoring system adds
 * keys to `stats` (bubbles popped, effects received, minigame results) and the
 * card picks them up through STAT_ORDER / STAT_LABELS without being touched.
 *
 * THE FLAVOUR WORD comes in PAIRS. Both clients hold the same matchSeed, so both
 * land on the same row: the winner reads the left word, the loser the right one
 * ("Mesmerist" / "Mesmerised"). A draw reads one word both sides share. The
 * table is keyed by the LOCAL flavour (each player picks their own), so two
 * players on different flavours see words from different tables; same row index.
 * Every word is safe for a public channel. Keep it that way.
 * ==========================================================================*/

export const CARD_W = 1200;
export const CARD_H = 630;

/** [winner word, loser word] pairs, plus the draw words. */
export const WORDS = Object.freeze({
  trance: {
    pairs: [['Hypnotist', 'Hypnotised'], ['Spinner', 'Spun'], ['Mesmerist', 'Mesmerised'], ['Wide awake', 'Under']],
    draw: ['Entranced', 'In sync', 'Mirrored'],
  },
  pink: {
    pairs: [['Iconic', 'Giggly'], ['Glossy', 'Dazzled'], ['Bubbly', 'Popped'], ['Queen bee', 'Airhead']],
    draw: ['Twinning', 'Matching', 'Besties'],
  },
  frills: {
    pairs: [['Poised', 'Flustered'], ['Pristine', 'Ruffled'], ['Proper', 'Blushing'], ['Composed', 'Undone']],
    draw: ['Tea party', 'Twin bows', 'Even stitch'],
  },
  shiny: {
    pairs: [['Polished', 'Buffed'], ['Chrome', 'Glazed'], ['Online', 'Rebooted'], ['Sleek', 'Smudged']],
    draw: ['Reflected', 'Synced', 'Mirror finish'],
  },
  censored: {
    pairs: [['Redacted', 'Blurred'], ['Classified', 'Pixelated'], ['Unseen', 'Bleeped'], ['Clean', 'Blacked out']],
    draw: ['Withheld', 'Static', 'Both bleeped'],
  },
  /* 'mine', '' (never picked) and anything unknown. */
  plain: {
    pairs: [['Unbroken', 'Broken'], ['Steady', 'Shaken'], ['Standing', 'Floored'], ['Iron', 'Melted']],
    draw: ['Stalemate', 'Dead even', 'Matched'],
  },
});

/** An abandon is nobody's win: one word, whatever the flavour. */
export const ABANDON_WORD = 'No show';

/** Flavour ids the card knows how to colour. Anything else wears 'plain'. */
export const FLAVOUR_TINTS = Object.freeze({
  trance: '#b99cff', pink: '#ff87c7', frills: '#ffb3d9', shiny: '#5fffd0', censored: '#9fb4c8', plain: '#ff69b4',
});
export const FLAVOUR_NAMES = Object.freeze({
  trance: 'Trance', pink: 'Pink', frills: 'Frills', shiny: 'Shiny', censored: 'Censored', plain: 'Goon Game',
});

export function flavourKey(id) {
  const k = String(id || '').toLowerCase();
  return Object.prototype.hasOwnProperty.call(WORDS, k) && k !== 'plain' ? k : 'plain';
}

/** A seed of any shape (bigint, number, string) to a non-negative index into `len`. */
export function seedIndex(seed, len) {
  const n = Math.max(1, len | 0);
  try {
    if (typeof seed === 'bigint') return Number(((seed % BigInt(n)) + BigInt(n)) % BigInt(n));
  } catch (_e) { /* fall through */ }
  const num = Number(seed);
  if (Number.isFinite(num)) return Math.abs(Math.floor(num)) % n;
  // Strings: a small FNV-1a, stable across runs and platforms.
  let h = 0x811c9dc5;
  const s = String(seed == null ? '' : seed);
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; }
  return h % n;
}

/**
 * The one word on the card. `outcome` is the LOCAL player's: 'w' | 'l' | 'd' | 'a'.
 * Deterministic in (flavour, outcome, seed).
 */
export function flavourWord(flavour, outcome, seed) {
  if (outcome === 'a') return ABANDON_WORD;
  const t = WORDS[flavourKey(flavour)];
  if (outcome === 'd') return t.draw[seedIndex(seed, t.draw.length)];
  const pair = t.pairs[seedIndex(seed, t.pairs.length)];
  return outcome === 'w' ? pair[0] : pair[1];
}

/**
 * Stat keys the card knows, in the order it shows them. The first four present
 * win; anything unknown follows in insertion order with a humanised label.
 * Sending leads (owner's scoring: sending pays most), then duels. Time survived
 * is deliberately NOT a line.
 */
export const STAT_ORDER = Object.freeze(['landed', 'duels', 'popped', 'combo', 'held']);
export const STAT_LABELS = Object.freeze({
  landed: 'Hits landed',
  duels: 'Duels won',
  popped: 'Bubbles popped',
  combo: 'Best combo',
  held: 'Hits held',
});
export const MAX_STAT_LINES = 4;

export function humanise(key) {
  const s = String(key || '').replace(/[_-]+/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2').trim().toLowerCase();
  return s ? s.charAt(0).toUpperCase() + s.slice(1) : '';
}

const count = (v) => {
  const n = Math.floor(Number(v));
  return Number.isFinite(n) && n >= 0 ? Math.min(n, 999999) : null;
};

/** [{ key, label, you, them }] - at most MAX_STAT_LINES, only keys either side has a number for. */
export function statLines(data) {
  const a = (data && data.you && data.you.stats) || {};
  const b = (data && data.them && data.them.stats) || {};
  const keys = [];
  for (const k of STAT_ORDER) if (count(a[k]) != null || count(b[k]) != null) keys.push(k);
  for (const k of [...Object.keys(a), ...Object.keys(b)]) {
    if (!keys.includes(k) && (count(a[k]) != null || count(b[k]) != null)) keys.push(k);
  }
  return keys.slice(0, MAX_STAT_LINES).map((key) => ({
    key,
    label: STAT_LABELS[key] || humanise(key),
    you: count(a[key]),
    them: count(b[key]),
  }));
}

/** 'Sep 24, 2026' in plain English, so a card reads the same in every locale. */
export function cardDate(ms) {
  const d = new Date(Number(ms) || Date.now());
  const mon = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][d.getMonth()];
  return mon + ' ' + d.getDate() + ', ' + d.getFullYear();
}

/** The headline under the word, from the local outcome. */
export function verdictLine(outcome, you, them) {
  const y = cleanName(you) || 'You';
  const t = cleanName(them) || 'They';
  if (outcome === 'w') return y + ' held. ' + t + ' broke.';
  if (outcome === 'l') return t + ' held. ' + y + ' broke.';
  if (outcome === 'a') return t + ' walked out.';
  return 'Nobody broke.';
}

/** A peer-supplied name, made safe to print: trimmed, single-line, clamped. */
export function cleanName(name) {
  return String(name == null ? '' : name).replace(/[\u0000-\u001f\u007f]+/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 24);
}

/** Only pictures a canvas can export: data: images. Anything else draws a monogram. */
export function safeAvatar(url) {
  const s = String(url || '');
  return /^data:image\/(png|jpe?g|webp|gif);base64,/i.test(s) ? s : '';
}

/**
 * The scoring lane's per-player shape, flattened to card stats:
 *   { score, sent: { landed, held, byKind }, received: { held, byKind },
 *     pops, bestCombo, duels: { won, lost, points } }
 * `sent.held` (what they held of yours) and the byKind maps stay off the card:
 * four lines is the budget and these are the four that tell the story.
 */
export function statsFromScoring(p) {
  if (!p || typeof p !== 'object') return {};
  const out = {};
  const put = (k, v) => { const n = count(v); if (n != null) out[k] = n; };
  put('landed', p.sent && p.sent.landed);
  put('duels', p.duels && p.duels.won);
  put('popped', p.pops);
  put('combo', p.bestCombo);
  put('held', p.received && p.received.held);
  return out;
}

/**
 * Everything the card needs, read from the match once.
 * @param {object} o
 * @param {object} o.result      core/match.js result ({ localScore, remoteScore, localWon, endReason, survivedMs })
 * @param {'w'|'l'|'d'|null} o.outcome  ui/rivalry.js outcomeOf(result); null = abandon
 * @param {object} [o.log]       matchLog: payloads() + stats()
 * @param {object} [o.duels]     duelSummary(match): { won, lost, tied }
 * @param {object} [o.scoring]   { you, them } in the scoring lane's shape (statsFromScoring). When
 *                              present it is the whole truth: stats AND scores come from it.
 * @param {object} [o.extra]     { you: {...}, them: {...} } flat extra stats, merged last
 */
export function buildShareData({
  result = null, outcome = null, log = null, duels = null, scoring = null, extra = null,
  youName = '', themName = '', youAvatar = '', themAvatar = '',
  flavour = '', seed = 0, highlights = [], now = Date.now(),
} = {}) {
  let landedOnThem = 0, landedOnYou = 0, enduredYou = 0, enduredThem = 0;
  try {
    for (const e of (log && typeof log.payloads === 'function' ? log.payloads() : [])) {
      const hit = e && (e.status === 'landed' || e.status === 'endured');
      if (!hit) continue;
      if (e.dir === 'in') { landedOnYou++; if (e.status === 'endured') enduredYou++; }
      else { landedOnThem++; if (e.status === 'endured') enduredThem++; }
    }
  } catch (_e) { /* a card never breaks on a log */ }

  let you = { landed: landedOnThem, held: enduredYou };
  let them = { landed: landedOnYou, held: enduredThem };
  if (duels && (duels.won || duels.lost || duels.tied)) {
    you = { landed: you.landed, duels: count(duels.won) || 0, held: you.held };
    them = { landed: them.landed, duels: count(duels.lost) || 0, held: them.held };
  }
  const scored = !!(scoring && scoring.you && scoring.them);
  if (scored) { you = statsFromScoring(scoring.you); them = statsFromScoring(scoring.them); }
  if (extra && typeof extra === 'object') {
    Object.assign(you, (extra.you && typeof extra.you === 'object') ? extra.you : {});
    Object.assign(them, (extra.them && typeof extra.them === 'object') ? extra.them : {});
  }

  const o = outcome === 'w' || outcome === 'l' || outcome === 'd' ? outcome : 'a';
  return {
    you: { name: cleanName(youName) || 'You', avatarUrl: safeAvatar(youAvatar), score: count(scored && scoring.you.score != null ? scoring.you.score : result && result.localScore) || 0, stats: you },
    them: { name: cleanName(themName) || 'Them', avatarUrl: safeAvatar(themAvatar), score: count(scored && scoring.them.score != null ? scoring.them.score : result && result.remoteScore) || 0, stats: them },
    flavour: flavourKey(flavour),
    outcome: o,
    seed,
    heldMs: Math.max(0, Number(result && result.survivedMs) || 0),
    highlights: (Array.isArray(highlights) ? highlights : []).map((h) => cleanName(h)).filter(Boolean).slice(0, 2),
    date: now,
  };
}

/** A string that changes whenever the drawn card would. Repaints skip a redraw when it holds. */
export function cardKey(data) {
  try {
    return JSON.stringify([data.you, data.them, data.flavour, data.outcome, String(data.seed), data.heldMs, data.highlights]);
  } catch (_e) { return String(Math.random()); }
}
