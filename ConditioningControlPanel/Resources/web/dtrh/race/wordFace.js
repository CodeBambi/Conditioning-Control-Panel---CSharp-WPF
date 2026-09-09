/* ============================================================================
 * race/wordFace.js - the word painted ON the bubble, not on a plate over it.
 *
 *   createWordFaces() -> { faceFor(spec), report(), clear(), dispose() }
 *
 * WHAT THIS REPLACES. The first cut hung the word on its own sprite above the bubble with
 * a screen-size floor and a stacking pass, so a line of words read as a rising stack of
 * plates. The owner drove it and asked for the opposite: the word inside the bubble, skewed
 * and small when it is far away, because the reading moment is the flash on the glass when
 * it pops, not the road ahead. So the bubble's own texture becomes the word's canvas.
 *
 * HOW. One CanvasTexture per (bubble kind, ink, word): the kind's sprite PNG drawn in with
 * its tint baked, then the word set centred inside the circle in the HUD sans at weight 800,
 * lowercase, dark outlined so it survives the bubble's own highlight. The sprite material
 * then wears white, because the tint is already in the pixels and multiplying it a second
 * time would drag the ink toward the bubble's colour.
 *
 * THE CACHE. A chant says one word forty times and a road says two thousand of them, so the
 * textures are held in an LRU of CACHE_MAX. Evicting disposes: a texture that is still on a
 * live sprite is only ever the most recently asked for ones, and the cap is far over the
 * bubbles the pool can hold at once. Nothing is allocated on a frame that spawns nothing.
 *
 * THE MEASURE. Every paint counts the ink it laid down inside the circle (one scratch canvas,
 * read back once, stride SAMPLE_STEP) and keeps the ratio on the entry. That number is what
 * race/smoke/face-check.mjs holds the promise "the word is actually on the bubble" against;
 * the game never reads it.
 * ==========================================================================*/

import * as THREE from 'three';

/** The face canvas, square. A word bubble is about 85 px on a phone (x3 for its pixel ratio)
 *  and 185 on a desktop at the pop point, so 256 keeps the letters crisp on the glass; the
 *  cache at its cap is 25 MB of texture, which a phone holds fine. */
export const TEX_PX = 256;
/** How many faces live at once before the least recently asked for one is disposed. */
export const CACHE_MAX = 96;
/** The word fits inside this much of the bubble's diameter: the rest is the bubble's rim.
 *  0.7 until 0909; the owner could not read it at speed, so the word takes more of the glass. */
export const TEXT_FRAC = 0.8;
/** At most this many lines. A merged two word bubble reads as two; a set label of three or
 *  four words is balanced across the same two rather than shrinking to nothing on one. */
export const MAX_LINES = 2;
/** Type sizes in canvas pixels, and the dark outline that keeps ink off the bubble's highlight.
 *  (34 / 9 / 2 on the old 128 px face: the outline is a third thicker in bubble terms now.) */
export const FONT_MAX_PX = 84, FONT_MIN_PX = 18, OUTLINE_PX = 6;
/** A dark halo blurred out behind the outline: the letters lift off the bubble's own colour. */
export const HALO_PX = 8;
/** The default ink. An accent word or a trigger row hands its set's own colour in instead.
 *  Brighter than the bubble pinks it sits on, so a plain word bubble reads pale on rose. */
export const INK = '#ffb3e1';
/** Every SAMPLE_STEP-th pixel of the inner circle is read back to measure what was painted. */
export const SAMPLE_STEP = 8;

const FALLBACK_FONT = "'Poppins', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";
const OUTLINE = 'rgba(10,8,18,0.94)';

/** The HUD's own font stack (styles.css --rh-font), read once, so a word on the road is set
 *  in the same face as the chrome. Never a book face: the pitch's one hard rule about type. */
function hudFont() {
  try {
    const v = getComputedStyle(document.documentElement).getPropertyValue('--rh-font');
    return (v && v.trim()) || FALLBACK_FONT;
  } catch (e) { return FALLBACK_FONT; }
}

/** Split a phrase into at most MAX_LINES balanced lines. One word stays one line. */
export function linesOf(text) {
  const parts = String(text || '').toLowerCase().trim().split(/\s+/).filter(Boolean);
  if (parts.length <= 1) return parts.length ? [parts[0]] : [];
  // the split that leaves the two lines closest in length, so nothing hangs off on its own
  let cut = 1, best = Infinity;
  for (let i = 1; i < parts.length; i++) {
    const a = parts.slice(0, i).join(' ').length, b = parts.slice(i).join(' ').length;
    const d = Math.abs(a - b);
    if (d < best) { best = d; cut = i; }
  }
  return [parts.slice(0, cut).join(' '), parts.slice(cut).join(' ')];
}

export function createWordFaces() {
  const family = hudFont();
  const cache = new Map();          // key -> { tex, canvas, ratio, seen }
  let scratch = null, sctx = null;
  let stamp = 0, made = 0, evicted = 0, peak = 0;

  function canvasOf() {
    const c = document.createElement('canvas');
    c.width = TEX_PX; c.height = TEX_PX;
    return c;
  }

  /** The kind's sprite with its tint multiplied in, so the material can then wear plain white. */
  function paintBubble(c, image, tint) {
    c.clearRect(0, 0, TEX_PX, TEX_PX);
    let drew = false;
    try {
      if (image && (image.width || image.videoWidth)) { c.drawImage(image, 0, 0, TEX_PX, TEX_PX); drew = true; }
    } catch (e) { drew = false; }
    if (!drew) {                                     // the PNG has not landed yet: a soft disc stands in
      const g = c.createRadialGradient(TEX_PX / 2, TEX_PX / 2, TEX_PX * 0.06, TEX_PX / 2, TEX_PX / 2, TEX_PX * 0.47);
      g.addColorStop(0, 'rgba(255,255,255,0.95)'); g.addColorStop(0.62, 'rgba(255,255,255,0.5)'); g.addColorStop(1, 'rgba(255,255,255,0)');
      c.fillStyle = g; c.fillRect(0, 0, TEX_PX, TEX_PX);
    }
    if (tint) {
      c.globalCompositeOperation = 'multiply';
      c.fillStyle = tint; c.fillRect(0, 0, TEX_PX, TEX_PX);
      c.globalCompositeOperation = 'destination-in';
      if (drew) { try { c.drawImage(image, 0, 0, TEX_PX, TEX_PX); } catch (e) { /* keep what is there */ } }
      c.globalCompositeOperation = 'source-over';
    }
    return drew;
  }

  /** The word, centred, shrunk until it sits inside TEXT_FRAC of the diameter. */
  function paintWord(c, lines, ink) {
    const box = TEX_PX * TEXT_FRAC;
    const rows = Math.max(1, lines.length);
    let size = Math.min(FONT_MAX_PX, Math.floor((box / rows) * 0.86));
    for (; size > FONT_MIN_PX; size--) {
      c.font = `800 ${size}px ${family}`;
      let wide = 0;
      for (const line of lines) wide = Math.max(wide, c.measureText(line).width);
      if (wide <= box && size * rows * 1.06 <= box) break;
    }
    c.font = `800 ${size}px ${family}`;
    c.textAlign = 'center'; c.textBaseline = 'middle';
    c.lineWidth = OUTLINE_PX; c.lineJoin = 'round'; c.miterLimit = 2;
    c.strokeStyle = OUTLINE; c.fillStyle = ink || INK;
    const step = size * 1.06, top = TEX_PX / 2 - (step * (rows - 1)) / 2;
    // the outline pass also throws the halo; the fill goes down clean on top of both
    c.shadowColor = OUTLINE; c.shadowBlur = HALO_PX; c.shadowOffsetX = 0; c.shadowOffsetY = 0;
    lines.forEach((line, i) => c.strokeText(line, TEX_PX / 2, top + i * step));
    c.shadowColor = 'transparent'; c.shadowBlur = 0;
    lines.forEach((line, i) => c.fillText(line, TEX_PX / 2, top + i * step));
    return size;
  }

  /** How much of the inner circle the paint above actually covered. The smoke's evidence. */
  function measure(c) {
    let data = null;
    try { data = c.getImageData(0, 0, TEX_PX, TEX_PX).data; } catch (e) { return -1; }
    const mid = TEX_PX / 2, r2 = (TEX_PX * TEXT_FRAC / 2) ** 2;
    let inked = 0, seen = 0;
    for (let y = 0; y < TEX_PX; y += SAMPLE_STEP) {
      for (let x = 0; x < TEX_PX; x += SAMPLE_STEP) {
        if ((x - mid) ** 2 + (y - mid) ** 2 > r2) continue;
        seen++;
        if (data[(y * TEX_PX + x) * 4 + 3] > 24) inked++;
      }
    }
    return seen ? inked / seen : 0;
  }

  /**
   * The texture for one word on one kind of bubble.
   * @param spec { key, word, ink, image, tint } - `key` names the base (kind plus whether its
   *        PNG has landed), so a face is rebuilt when the sprite behind it changes and never
   *        otherwise. `image` is the kind's loaded texture image, `tint` its BUBBLE_KINDS tint.
   * @returns a THREE.CanvasTexture, or null for an empty word.
   */
  function faceFor({ key, word, ink = INK, image = null, tint = null } = {}) {
    const lines = linesOf(word);
    if (!lines.length) return null;
    const id = `${key || 'b'}|${ink || INK}|${lines.join(' ')}`;
    const hit = cache.get(id);
    if (hit) { hit.seen = ++stamp; return hit.tex; }
    if (cache.size >= CACHE_MAX) {                   // evict the least recently asked for one
      let oldKey = null, oldSeen = Infinity;
      for (const [k, v] of cache) if (v.seen < oldSeen) { oldSeen = v.seen; oldKey = k; }
      // its canvas goes with it rather than being recycled: a sprite still wearing an evicted
      // texture would otherwise quietly repaint itself with somebody else's word.
      if (oldKey != null) { cache.get(oldKey).tex.dispose(); cache.delete(oldKey); evicted++; }
    }
    const canvas = canvasOf();
    const c = canvas.getContext('2d');
    if (!scratch) { scratch = canvasOf(); sctx = scratch.getContext('2d'); }
    paintBubble(c, image, tint);
    sctx.clearRect(0, 0, TEX_PX, TEX_PX);
    const size = paintWord(sctx, lines, ink);
    const ratio = measure(sctx);                     // the WORD's own coverage, not the bubble's
    c.drawImage(scratch, 0, 0);
    const tex = new THREE.CanvasTexture(canvas);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.needsUpdate = true;
    const entry = { tex, canvas, ratio, size, lines: lines.length, word: lines.join(' '), ink: ink || INK, seen: ++stamp };
    cache.set(id, entry);
    made++;
    if (cache.size > peak) peak = cache.size;
    return tex;
  }

  function clear() {
    for (const v of cache.values()) if (v.tex) v.tex.dispose();
    cache.clear();
  }

  return {
    faceFor,
    clear,
    /** What race/smoke/face-check.mjs reads. Never read by the game. */
    report() {
      const rows = [];
      for (const [k, v] of cache) rows.push({ key: k, word: v.word, ink: v.ink, ratio: v.ratio, size: v.size, lines: v.lines });
      return { cap: CACHE_MAX, texPx: TEX_PX, textFrac: TEXT_FRAC, count: cache.size, made, evicted, peak, faces: rows };
    },
    dispose() { clear(); scratch = null; sctx = null; },
  };
}

// self-check: node race/smoke/face-check.mjs drives a worded road on a 390x844 phone and reads
// back what every live word bubble is wearing.
