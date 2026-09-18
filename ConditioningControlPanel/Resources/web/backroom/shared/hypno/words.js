/* ============================================================================
 * shared/hypno/words.js - the page's half of a subliminal, on EVERY station.
 *
 * The slot has always drawn its own words (stations/slot/word.js): the dealt text BIG at the centre
 * in the house colours, zooming toward the viewer (callout.word), the clicker and the word cue under
 * it, and the word SPOKEN by the host (callout.js -> voice.js -> word.speak, CONTRACT 10.21). The
 * host is then sent fx.sub_pair / fx.sub_cascade with { wordsShown: true } so it plays only the
 * spiral or the picture that follows, and fx.sub_single not at all.
 *
 * The wheel, the roulette and the card table used to hand the same three ids to the host with the
 * word KEYS and nothing else, so on the desktop their words were the app's own silent text flash and
 * on the phone playtest a silent fade: no zoom, no colours, no voice. Owner 2026-09-18: every sub is
 * the slot's sub. This is that rule in one place; the stations call showSubs() on the frame their
 * fx fire and hand the host what comes back.
 *
 *   wordBook(rep)                       Map key -> text from a media reply's `words` ([{ key, text }])
 *   wordTexts(keys, book, n)            the texts for dealt keys s0..s3, the presets where a key has none
 *   showSubs(callout, steps, opts)      draws the words, returns { steps, words, wordsMs }
 *
 * `steps` is [{ id, symbols, args }] in firing order. The FIRST sub step names the chain (1, 2 or 3
 * words: its word keys, else its id's count); every sub step is rewritten as the slot does it. The
 * subliminal gate off (gates.subliminal === false), no callout, or a callout without word() leaves
 * the list untouched, so the host stays the only judge, as before.
 * ==========================================================================*/

import { WORD_MS, WORD_GAP_MS } from './callout.js';

export const SUB_FX = Object.freeze(['fx.sub_single', 'fx.sub_pair', 'fx.sub_cascade']);
export const SUB_COUNT = Object.freeze({ 'fx.sub_single': 1, 'fx.sub_pair': 2, 'fx.sub_cascade': 3 });
/** The four bundled words (Resources/Audio/backroom/words), the same fallback the slot's symbols.js keeps. */
export const PRESET_WORDS = Object.freeze(['DROP', 'RELAX', 'LET GO', 'SINK']);
export const WORD_KEY_RE = /^s\d{1,2}$/;

const isSub = (id) => Object.prototype.hasOwnProperty.call(SUB_COUNT, id);

/** key -> text for a media reply's dealt words. Anything that is not { key: 'sN', text: '...' } is skipped. */
export function wordBook(rep) {
  const book = new Map();
  const list = rep && Array.isArray(rep.words) ? rep.words : [];
  for (const w of list) {
    if (!w || typeof w.key !== 'string' || !WORD_KEY_RE.test(w.key)) continue;
    const text = typeof w.text === 'string' ? w.text.trim() : '';
    if (text) book.set(w.key, text);
  }
  return book;
}

/** The word keys among a step's symbols, in order. */
export function wordKeysOf(symbols) {
  return (Array.isArray(symbols) ? symbols : []).filter((s) => typeof s === 'string' && WORD_KEY_RE.test(s));
}

/** Texts for `keys` (or `n` presets when there are none), at most three, never empty when n > 0. */
export function wordTexts(keys, book, n = 0) {
  const out = [];
  const list = Array.isArray(keys) ? keys : [];
  for (const k of list) {
    if (out.length >= 3) break;
    const i = Number(String(k).slice(1)) || 0;
    const text = book instanceof Map ? book.get(k) : null;
    out.push(text || PRESET_WORDS[i % PRESET_WORDS.length]);
  }
  for (let i = out.length; i < Math.min(3, n | 0); i++) out.push(PRESET_WORDS[i % PRESET_WORDS.length]);
  return out;
}

/** One word onset to the last word gone, the slot's rule (station.js: WORD_MS + WORD_GAP_MS per extra word). */
export const wordsMsFor = (n) => (n > 0 ? WORD_MS + WORD_GAP_MS * (n - 1) : 0);

/** FNV-1a over a seed string, so a replayed landing shows the same reversal gag. */
export function seedOf(s) {
  let h = 2166136261;
  const str = String(s == null ? '' : s);
  for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); }
  return h >>> 0;
}

/**
 * Draw the words of the sub steps in `steps` on the page and rewrite the list for the host.
 * Returns { steps, words, wordsMs }: the steps to fire (fx.sub_single dropped, pair and cascade
 * carrying wordsShown), the texts shown, and how long the centre is theirs (0 when nothing was drawn).
 */
export function showSubs(callout, steps, { book = null, gates = null, seed = '' } = {}) {
  const list = Array.isArray(steps) ? steps : [];
  const untouched = { steps: list, words: [], wordsMs: 0 };
  if (!callout || typeof callout.word !== 'function') return untouched;
  if (gates && gates.subliminal === false) return untouched;
  const first = list.find((s) => s && isSub(s.id));
  if (!first) return untouched;
  const keys = wordKeysOf(first.symbols);
  const texts = wordTexts(keys, book, keys.length ? keys.length : SUB_COUNT[first.id]);
  if (!texts.length) return untouched;
  try { callout.word(texts[0], { chain: texts.slice(1), seed: seedOf(seed) }); } catch (e) { return untouched; }
  const out = [];
  for (const s of list) {
    if (!s || !isSub(s.id)) { out.push(s); continue; }
    if (s.id === 'fx.sub_single') continue;
    out.push({ ...s, args: { ...(s.args || {}), wordsShown: true } });
  }
  return { steps: out, words: texts, wordsMs: wordsMsFor(texts.length) };
}
