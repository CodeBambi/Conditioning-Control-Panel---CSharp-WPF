/* ============================================================================
 * backroom/kit/triggers.js - the reels, and whose words are on them.
 *
 * Triple Trigger spins three reels of three-word phrases, and the good version
 * of that game spins YOUR phrases. The host knows them; this asks for them and
 * turns the answer into reel rows.
 *
 * WHY EXACTLY THREE WORDS. A reel row has one line of room and a slot window
 * is read in about a third of a second. Two words look like a label, four
 * wrap, three read as a phrase and stack into a column that scans. Anything
 * that is not three words is not rejected as invalid, it simply is not a reel
 * row, and it is left where it was.
 *
 * THE FALLBACK GOES THROUGH THE LEXICON, NOT THROUGH A MOD SWITCH. It would be
 * easy to branch on the active mod and hand back a Bambi set, a Drone set and
 * so on, and it would be wrong: `init.modId` reaches the page, but the page
 * never reads it and may not start now. Mods skin display strings by shipping
 * `lexicon.json`, so the neutral rows below are keys, and a mod re-voices all
 * six by overriding them. That is one mechanism instead of two, and it means a
 * mod nobody here has heard of gets its own reels for free.
 *
 * A GLYPH ROW IS NOT A BLANK. A short list is padded with `{glyph:true}` rows
 * rather than with empty strings, because a slot machine with gaps in the reel
 * reads as broken, and a spiral glyph reads as the house's own symbol.
 * ==========================================================================*/

/** How many rows a reel wants at most. Past a dozen the column stops feeling
 *  like a set of your own words and starts feeling like a dictionary. */
export const REEL_CAP = 12;
/** The fewest rows a reel can have and still look like a reel turning. */
export const REEL_MIN = 6;

/**
 * The neutral fallback, and the six rows a mod overrides to re-voice the reels
 * in its own tongue. Neutral means neutral: these are the phrases the CCP
 * Default mod would use, warm and suggestive without belonging to any one
 * script. Every value is three words and under 96 characters, so it survives
 * `MergeModTable` and can be skinned.
 */
export const BK_TRIGGERS = Object.freeze({
  bk_tr_1: 'soft and slow',
  bk_tr_2: 'eyes on me',
  bk_tr_3: 'let it go',
  bk_tr_4: 'sink a little',
  bk_tr_5: 'good, stay there',
  bk_tr_6: 'warm and quiet',
});

/** The glyph a padded row wears. The house's own mark, not a placeholder box. */
export const REEL_GLYPH = '@';

/** Exactly three words, once whitespace is tidied. Nothing else qualifies. */
export function isThreeWords(text) {
  const s = String(text == null ? '' : text).replace(/\s+/g, ' ').trim();
  if (!s) return false;
  return s.split(' ').length === 3;
}

function tidy(text) {
  return String(text == null ? '' : text).replace(/\s+/g, ' ').trim();
}

/**
 * buildReel(list, { t, cap, min }) -> [{ word } | { glyph:true }]
 *
 * Pure, so it can be reasoned about and tested without a host: the asking is
 * `reelRows` below. Order is preserved, because the player's own list has an
 * order they recognise and shuffling it makes their words look like ours.
 */
export function buildReel(list, opts) {
  const o = opts || {};
  const cap = Math.max(1, Math.round(Number(o.cap) || REEL_CAP));
  const min = Math.max(1, Math.round(Number(o.min) || REEL_MIN));
  const t = (typeof o.t === 'function') ? o.t : ((k, fb) => fb || BK_TRIGGERS[k] || '');

  const rows = [];
  const seen = new Set();
  const take = (raw) => {
    if (rows.length >= cap) return;
    const s = tidy(raw);
    if (!isThreeWords(s)) return;
    const key = s.toLowerCase();
    if (seen.has(key)) return;      // the same phrase twice is a bug, not a reel
    seen.add(key);
    rows.push({ word: s });
  };

  for (const item of (Array.isArray(list) ? list : [])) {
    // The host may hand back strings or {phrase}/{word}/{text} objects.
    if (typeof item === 'string') take(item);
    else if (item && typeof item === 'object') take(item.phrase || item.word || item.text);
  }

  /* THE PLAYER'S OWN WORDS COME FIRST AND THE HOUSE'S FILL IN BEHIND THEM.
   * A reel that opened with our phrases and buried theirs would be the wrong
   * way round: the point of the machine is that it knows them. */
  if (rows.length < min) {
    for (const key of Object.keys(BK_TRIGGERS)) {
      if (rows.length >= min) break;
      take(t(key, BK_TRIGGERS[key]));
    }
  }
  // Still short, because a mod re-voiced a row into something that is not
  // three words. Glyphs finish the column rather than leaving a hole in it.
  while (rows.length < min) rows.push({ glyph: true, word: REEL_GLYPH });

  return rows;
}

/**
 * reelRows(api, { t, cap, min }) -> Promise<rows>
 *
 * Asks the host once and never rejects. `api.triggers()` already resolves to
 * an empty array on a host with no trigger store and on a timeout, so the
 * failure path here is simply the fallback path, which is the same code.
 */
export function reelRows(api, opts) {
  const ask = (api && typeof api.triggers === 'function') ? api.triggers() : Promise.resolve([]);
  return ask.then(
    (list) => buildReel(list, opts),
    () => buildReel([], opts),
  );
}

export default { buildReel, reelRows, isThreeWords, BK_TRIGGERS, REEL_CAP, REEL_MIN };
