/* ============================================================================
 * shared/text/wrap.js - word wrapping and font fitting for the Back Room's
 * drawn text: the slot marquee message board, the reel's subliminal / trigger
 * glyphs and the big centred word beat (shared/hypno/callout.js .word()).
 *
 * PURE: no DOM, no canvas, no three. Everything that needs to know how wide a
 * string actually is takes a `measure(text, size) -> width` function, so the
 * node tests drive it with a fake metric and the pages hand it ctx.measureText
 * or a DOM probe.
 *
 *   wrapLines(text, maxChars, maxLines, { minLines })
 *       Greedy word wrap at maxChars per line, hard-splitting a word that is
 *       longer than a line. NEVER returns more than maxLines: when the greedy
 *       wrap overflows, the line width is widened (the smallest width that
 *       fits) instead of dropping text. `minLines` forces a phrase onto at
 *       least that many lines, which is how a long trigger phrase reads on two
 *       lines in the reel window instead of one squeezed one.
 *
 *   fitText(text, { measure, width, height, maxLines, min, max, lineHeight, minLines })
 *       The largest size in [min, max] whose wrapped block fits width x height.
 *       Returns { size, lines, width, height } - the block's own measured box,
 *       so a caller can centre it. Falls back to `min` when nothing fits.
 * ==========================================================================*/

const WS = /\s+/;

/** Splits a word too long for one line into maxChars-sized pieces. */
function hardSplit(word, maxChars) {
  const out = [];
  for (let i = 0; i < word.length; i += maxChars) out.push(word.slice(i, i + maxChars));
  return out;
}

/** One greedy pass at `width` characters per line. Never drops a character. */
function greedy(words, width) {
  const lines = [];
  let line = '';
  for (const raw of words) {
    for (const w of raw.length > width ? hardSplit(raw, width) : [raw]) {
      if (!line) { line = w; continue; }
      if (line.length + 1 + w.length <= width) line += ' ' + w;
      else { lines.push(line); line = w; }
    }
  }
  if (line) lines.push(line);
  return lines.length ? lines : [''];
}

/**
 * Word-wraps `text` into at most `maxLines` lines of about `maxChars` characters.
 * @param {string} text
 * @param {number} maxChars   characters that fit on one line (>= 1)
 * @param {number} [maxLines] hard ceiling on the number of lines (>= 1)
 * @param {{minLines?:number}} [opts] minLines: wrap onto at least this many lines when the text is long enough
 * @returns {string[]} 1..maxLines lines, never empty
 */
export function wrapLines(text, maxChars, maxLines = 3, opts = {}) {
  const s = String(text == null ? '' : text).replace(/\s+/g, ' ').trim();
  const cap = Math.max(1, Math.floor(maxLines) || 1);
  let width = Math.max(1, Math.floor(maxChars) || 1);
  if (!s) return [''];
  const words = s.split(WS);
  const minLines = Math.max(1, Math.min(cap, Math.floor(opts.minLines) || 1));
  // minLines: narrow the line until the text is spread over that many lines (a long trigger phrase reads on
  // two lines rather than one squeezed one). Never narrower than the longest single word we would have to cut.
  if (minLines > 1) {
    let longest = 1;
    for (const w of words) longest = Math.max(longest, w.length);
    const want = Math.max(longest, Math.ceil(s.length / minLines));
    if (want < width) width = want;
  }
  let lines = greedy(words, width);
  if (lines.length <= cap) return lines;
  // Too many lines: widen to the smallest width that fits the cap, so nothing is ever dropped.
  let lo = width, hi = s.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (greedy(words, mid).length <= cap) hi = mid; else lo = mid + 1;
  }
  lines = greedy(words, lo);
  return lines.slice(0, cap);
}

/**
 * The largest font size whose wrapped block fits the box.
 * @param {string} text
 * @param {Object} o
 * @param {(text:string,size:number)=>number} o.measure  width of `text` drawn at `size`
 * @param {number} o.width        the box's width in the same units measure returns
 * @param {number} [o.height]     the box's height; Infinity when only the width matters
 * @param {number} [o.maxLines]   default 3
 * @param {number} [o.minLines]   default 1
 * @param {number} [o.min]        smallest size to consider, default 8
 * @param {number} [o.max]        largest size to consider, default 96
 * @param {number} [o.lineHeight] multiple of the size, default 1.1
 * @returns {{size:number, lines:string[], width:number, height:number}}
 */
export function fitText(text, o = {}) {
  const measure = typeof o.measure === 'function' ? o.measure : (s, size) => String(s).length * size * 0.55;
  const box = Math.max(1, Number(o.width) || 1);
  const tall = Number.isFinite(o.height) ? Math.max(1, Number(o.height)) : Infinity;
  const maxLines = Math.max(1, Math.floor(o.maxLines) || 3);
  const minLines = Math.max(1, Math.floor(o.minLines) || 1);
  const lh = Number(o.lineHeight) > 0 ? Number(o.lineHeight) : 1.1;
  const min = Math.max(1, Number(o.min) || 8), max = Math.max(min, Number(o.max) || 96);
  const s = String(text == null ? '' : text).replace(/\s+/g, ' ').trim();

  const layout = (size) => {
    // Characters per line at this size, from the string's own average glyph width.
    const per = s.length ? measure(s, size) / s.length : size * 0.55;
    const chars = Math.max(1, Math.floor(box / Math.max(0.0001, per)));
    const lines = wrapLines(s, chars, maxLines, { minLines });
    let w = 0;
    for (const l of lines) w = Math.max(w, measure(l, size));
    return { size, lines, width: w, height: lines.length * size * lh };
  };
  const fits = (r) => r.width <= box && r.height <= tall;

  let best = null, lo = min, hi = max;
  while (lo <= hi) {
    const mid = Math.floor((lo + hi) / 2);
    const r = layout(mid);
    if (fits(r)) { best = r; lo = mid + 1; } else hi = mid - 1;
  }
  return best || layout(min);
}
