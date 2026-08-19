/* ============================================================================
 * games/impulse-control/stimset.js - the stimulus supply. PURE (no DOM).
 *
 * The stream generator deals abstract records; this module turns a record into
 * "what the player actually sees" for the active `stimulusStyle`:
 *
 *   words   go/nogo pairs, one letter off      (lexicon rows, mod-skinnable)
 *   glyphs  shipped geometry + its mirrored / rotated twin
 *   media   an asset-provider loop + a CSS-FILTER twin of the SAME asset
 *   mixed   all three, seeded rotation (default)
 *
 * STABLE MAPPING PER BLOCK. A Go/No-Go test only works if the player has learned
 * which face means GO, so `lockBlock(similarity)` fixes ONE pair per render class
 * for the whole block and every trial in that block draws from it. Tier pressure
 * arrives as a NEARER pair next block, never as a mapping the player cannot have
 * learned. `mapping()` is what the chrome shows during calibration.
 *
 * NEAR-TWIN LAW. A NO-GO is never a different thing, it is the GO stimulus with
 * one thing wrong: one letter, one mirror, one hue rotation. `similarity` (0..1,
 * tier-scaled) picks how near the locked pair is: low similarity locks the
 * loudest pair, high similarity the subtlest. That is how decoys get harder
 * without the classic knobs moving.
 *
 * MEDIA TWINS ARE CSS FILTERS, NEVER CANVAS. The synthesis closure allows
 * CSS-filter transforms on CORS-tainted (remote Scrolller) media precisely
 * because they avoid a canvas read; this class declares `canvasSafe:false` and
 * has no canvas consumer at all. NOTE FOR THE BUILD LIST: iOS webview filter
 * perf on a live loop is unverified (dossier open question) - if it stutters,
 * `MEDIA_TWINS` is the one place to drop to transform-only twins.
 *
 * EMPTY-POOL CONTRACT. Words may be empty (the global vocabulary is allowed to
 * be), media may be unavailable (no provider, offline, empty pool). Every style
 * therefore degrades: media -> glyphs -> words -> shipped glyphs. A style never
 * yields a blank stimulus, because a blank aperture is an unscoreable trial.
 * ==========================================================================*/

/** Word-pair rows (lexicon keys; see lex.js). `d` = how far apart the pair reads. */
export const WORD_PAIRS = Object.freeze([
  Object.freeze({ goKey: 'ic_word_go_1', nogoKey: 'ic_word_nogo_1', d: 0.55 }),  // OBEY / OBEV
  Object.freeze({ goKey: 'ic_word_go_2', nogoKey: 'ic_word_nogo_2', d: 0.3 }),   // GOOD / G00D
  Object.freeze({ goKey: 'ic_word_go_3', nogoKey: 'ic_word_nogo_3', d: 0.35 }),  // DEEPER / DEEPEB
  Object.freeze({ goKey: 'ic_word_go_4', nogoKey: 'ic_word_nogo_4', d: 0.5 }),   // FOCUS / FOCVS
  Object.freeze({ goKey: 'ic_word_go_5', nogoKey: 'ic_word_nogo_5', d: 0.4 }),   // HOLD / H0LD
  Object.freeze({ goKey: 'ic_word_go_6', nogoKey: 'ic_word_nogo_6', d: 0.2 }),   // YIELD / YEILD
]);

/**
 * Shipped glyph pairs - geometry, not strings, so they need no lexicon row and
 * work with any mod (and with none). `d` = perceptual distance (higher = easier).
 */
export const GLYPH_PAIRS = Object.freeze([
  Object.freeze({ go: '▲', twin: '▼', d: 0.9 }),   // up / down triangle
  Object.freeze({ go: '⮞', twin: '⮜', d: 0.8 }),   // arrow right / left
  Object.freeze({ go: '◐', twin: '◑', d: 0.5 }),   // half-filled circle L/R
  Object.freeze({ go: '◧', twin: '◨', d: 0.45 }),  // square half L / R
  Object.freeze({ go: '↻', twin: '↺', d: 0.4 }),   // clockwise / anticlockwise
  Object.freeze({ go: '⦿', twin: '⦾', d: 0.35 }),  // dotted / hollow circle
  Object.freeze({ go: '✦', twin: '✧', d: 0.3 }),   // filled / hollow star
  Object.freeze({ go: '⬡', twin: '⬢', d: 0.2 }),   // hex hollow / filled
]);

/** CSS-filter / transform twins for media stimuli. Class names live in style.js. */
export const MEDIA_TWINS = Object.freeze([
  Object.freeze({ cls: 'twin-flip', d: 0.9 }),
  Object.freeze({ cls: 'twin-mirror', d: 0.6 }),
  Object.freeze({ cls: 'twin-hue', d: 0.4 }),
  Object.freeze({ cls: 'twin-dim', d: 0.2 }),
]);

export const STYLES = Object.freeze(['words', 'glyphs', 'media', 'mixed']);

function pickBy(list, r) {
  if (!list || !list.length) return null;
  const i = Math.floor((r < 0 ? 0 : r > 0.999999 ? 0.999999 : r) * list.length);
  return list[i];
}

/** The entry whose distance best matches "how near a twin do we want". */
export function nearest(list, similarity) {
  if (!list || !list.length) return null;
  const want = 1 - Math.max(0, Math.min(1, Number(similarity) || 0));  // sim 1 -> want 0 (subtlest)
  let best = list[0];
  let bestErr = Infinity;
  for (const c of list) {
    const err = Math.abs((c.d == null ? 0.5 : c.d) - want);
    if (err < bestErr) { bestErr = err; best = c; }
  }
  return best;
}

/**
 * @param {Object} o
 * @param {Function} o.t            ctx.lexicon
 * @param {Function} o.rng          seeded stream for style/pair choices
 * @param {string=} o.style         words|glyphs|media|mixed
 * @param {Array=} o.words          the global word vocabulary IF a shell ever
 *                                  hands one to a game (it does not today - see
 *                                  the build report's shared-layer notes); the
 *                                  lexicon stimset rows are the real supply.
 * @param {Function=} o.mediaUrl    () => url|null from the asset pool
 * @param {Function=} o.log
 */
export function createStimset({ t, rng, style, words, mediaUrl, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const lex = typeof t === 'function' ? t : (k, f) => f || k;
  const rand = typeof rng === 'function' ? rng : Math.random;
  const wanted = STYLES.indexOf(String(style)) >= 0 ? String(style) : 'mixed';

  /* --- availability probes, done ONCE ----------------------------------- */
  const extraWords = (Array.isArray(words) ? words : [])
    .filter((w) => typeof w === 'string' && w.trim().length >= 3 && w.trim().length <= 12)
    .map((w) => w.trim().toUpperCase());

  const wordRows = WORD_PAIRS
    .map((p) => ({ go: lex(p.goKey, ''), nogo: lex(p.nogoKey, ''), d: p.d }))
    .filter((p) => p.go && p.nogo && p.go !== p.nogo);

  const hasWords = wordRows.length > 0 || extraWords.length > 0;
  const hasGlyphs = GLYPH_PAIRS.length > 0;
  let mediaProbe = null;

  function probeMedia() {
    if (mediaProbe != null) return mediaProbe;
    let url = null;
    try { url = typeof mediaUrl === 'function' ? mediaUrl() : null; } catch (e) { url = null; }
    mediaProbe = !!(url && typeof url === 'string');
    if (!mediaProbe) say('stimset: no media pool - media stimuli degrade to glyphs/words');
    return mediaProbe;
  }

  function availableStyles() {
    const out = [];
    if (wanted === 'mixed') {
      if (hasWords) out.push('words');
      if (hasGlyphs) out.push('glyphs');
      if (probeMedia()) out.push('media');
    } else if (wanted === 'words') {
      if (hasWords) out.push('words'); else if (hasGlyphs) out.push('glyphs');
    } else if (wanted === 'glyphs') {
      if (hasGlyphs) out.push('glyphs'); else if (hasWords) out.push('words');
    } else {
      if (probeMedia()) out.push('media');
      else if (hasGlyphs) out.push('glyphs');
      else if (hasWords) out.push('words');
    }
    if (!out.length && hasGlyphs) out.push('glyphs');     // the shipped floor
    return out;
  }

  const styles = availableStyles();

  /** One-character mutation, for a vocabulary word with no authored twin. */
  function mutate(w, similarity) {
    const swaps = { O: '0', I: '1', E: 'B', A: '4', S: '5', G: '6', T: '7', L: 'I', U: 'V' };
    const chars = String(w).split('');
    const near = Math.max(0, Math.min(1, Number(similarity) || 0));
    const order = chars.map((c, i) => i);
    if (near >= 0.7) order.reverse();          // deep in the word = harder to spot
    for (const i of order) {
      const s = swaps[chars[i]];
      if (s) { const twin = chars.slice(); twin[i] = s; return twin.join(''); }
    }
    return chars.length > 3 ? chars.slice(0, -1).join('') + (w.endsWith('E') ? 'A' : 'E') : w + 'E';
  }

  /* --- the locked block mapping ----------------------------------------- */
  let locked = { similarity: 0.5, word: null, glyph: null, media: null };

  function lockBlock(similarity) {
    const sim = Math.max(0, Math.min(1, Number(similarity) || 0));
    const word = (() => {
      if (wordRows.length) {
        // Seeded jitter around the similarity target so two blocks at the same
        // tier are not always the identical pair.
        const cand = wordRows.slice().sort((a, b) => Math.abs(a.d - (1 - sim)) - Math.abs(b.d - (1 - sim)));
        const top = cand.slice(0, Math.min(3, cand.length));
        return pickBy(top, rand());
      }
      if (extraWords.length) {
        const go = pickBy(extraWords, rand()) || 'OBEY';
        return { go, nogo: mutate(go, sim), d: 1 - sim };
      }
      return null;
    })();
    const glyphCand = GLYPH_PAIRS.slice().sort((a, b) => Math.abs(a.d - (1 - sim)) - Math.abs(b.d - (1 - sim)));
    const glyph = pickBy(glyphCand.slice(0, Math.min(3, glyphCand.length)), rand()) || GLYPH_PAIRS[0];
    let mediaUrlLocked = null;
    if (styles.indexOf('media') >= 0) {
      try { mediaUrlLocked = typeof mediaUrl === 'function' ? mediaUrl() : null; } catch (e) { mediaUrlLocked = null; }
    }
    const media = mediaUrlLocked
      ? { url: mediaUrlLocked, twin: nearest(MEDIA_TWINS, sim) }
      : null;
    locked = { similarity: sim, word, glyph, media };
    return locked;
  }

  lockBlock(0.5);

  return {
    /** Which render classes actually work in this class (diagnostics + tests). */
    styles() { return styles.slice(); },
    get requested() { return wanted; },
    lockBlock,

    /** What the calibration screen tells the player GO looks like. */
    mapping() {
      const l = locked;
      if (l.word && styles.indexOf('words') >= 0) return { go: l.word.go, nogo: l.word.nogo, render: 'word' };
      if (l.glyph) return { go: l.glyph.go, nogo: l.glyph.twin, render: 'glyph' };
      return { go: '?', nogo: '?', render: 'glyph' };
    },

    /**
     * Dress one stream record with the block's locked mapping.
     * @param {{cls:'go'|'nogo', similarity?:number}} rec
     * @returns {{render:'word'|'glyph'|'media', text?:string, url?:string,
     *            twinCls?:string, label:string}}
     */
    dress(rec) {
      const r = rec || {};
      const isNogo = r.cls === 'nogo';
      const kind = styles.length <= 1 ? (styles[0] || 'glyphs') : pickBy(styles, rand());

      if (kind === 'words' && locked.word) {
        const text = isNogo ? locked.word.nogo : locked.word.go;
        return { render: 'word', text, label: text };
      }
      if (kind === 'media' && locked.media && locked.media.url) {
        const twin = isNogo ? locked.media.twin : null;
        return {
          render: 'media',
          url: locked.media.url,
          twinCls: twin ? twin.cls : '',
          label: isNogo ? 'media/' + (twin ? twin.cls : 'twin') : 'media',
        };
      }
      const pair = locked.glyph || GLYPH_PAIRS[0];
      const text = isNogo ? pair.twin : pair.go;
      return { render: 'glyph', text, label: text };
    },

    /** The GO face - what a priming flash whispers before a NO-GO. Null = nothing
     *  safe to whisper (media-only block, or an empty word pool). */
    primeText() {
      if (locked.word && styles.indexOf('words') >= 0) return locked.word.go;
      if (locked.glyph) return locked.glyph.go;
      return null;
    },
  };
}

export default createStimset;
