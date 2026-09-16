/* ============================================================================
 * fonts.js - the four faces the room carries with it.
 *
 * The first three caption faces are Windows system fonts. On a phone or a Mac
 * they fall back to whatever is there, so the same remix reads one way on the
 * machine that made it and another on the machine that opens it. These four
 * ship with the room instead: a latin subset woff2 each, the licence next to
 * it, about 80 KB for the set.
 *
 * `ensureFonts` is cached and never rejects. A face that will not load resolves
 * false and the stack in FONTS falls back, because a missing file must not stop
 * a frame from being drawn.
 *
 * The caption draws at weight 700 and every file here is a single weight, so
 * each face is registered as 700: an exact match, which keeps the browser from
 * smearing a fake bold over a poster face that is already heavy.
 * ==========================================================================*/

export const BUNDLED_FONTS = {
  block: { family: 'Anton', file: 'anton-latin.woff2', name: 'Anton' },
  script: { family: 'Pacifico', file: 'pacifico-latin.woff2', name: 'Pacifico' },
  round: { family: 'Fredoka', file: 'fredoka-latin.woff2', name: 'Fredoka' },
  pixel: { family: 'Press Start 2P', file: 'pressstart2p-latin.woff2', name: 'Press Start 2P' },
};

export const BUNDLED_KEYS = Object.keys(BUNDLED_FONTS);

/** Where a bundled file sits, next to the engine rather than at a fixed path. */
export function fontUrl(key) {
  const spec = BUNDLED_FONTS[key];
  if (!spec) return null;
  return new URL('../assets/fonts/' + spec.file, import.meta.url).href;
}

/** The bundled keys a set of blocks asks for, in table order, no repeats. */
export function fontsInUse(blocks) {
  const want = new Set();
  for (const b of blocks || []) {
    if (b && b.effect === 'caption' && b.params && BUNDLED_FONTS[b.params.font]) want.add(b.params.font);
  }
  return BUNDLED_KEYS.filter((k) => want.has(k));
}

const cache = new Map();

/**
 * Load the bundled faces for `keys` (one key or a list). Resolves true when
 * every one of them is ready to draw with, false when at least one is not.
 * Cached per key, so calling it on every edit costs nothing.
 */
export function ensureFonts(keys) {
  const list = (Array.isArray(keys) ? keys : [keys]).filter((k) => BUNDLED_FONTS[k]);
  if (!list.length) return Promise.resolve(true);
  return Promise.all(list.map(one)).then((all) => all.every(Boolean));
}

/** True once this key has loaded, without waiting on anything. */
export function fontLoaded(key) {
  return cache.get(key)?.done === true;
}

function one(key) {
  const had = cache.get(key);
  if (had) return had.promise;
  const entry = { done: false, promise: null };
  entry.promise = load(key).then((ok) => { entry.done = ok; return ok; });
  cache.set(key, entry);
  return entry.promise;
}

async function load(key) {
  const spec = BUNDLED_FONTS[key];
  // node, and any browser old enough to have no FontFace: nothing to load, and
  // the fallback in the stack is what draws
  const set = typeof self === 'undefined' ? null : self.fonts;
  if (!spec || !set || typeof FontFace === 'undefined') return false;
  try {
    const face = new FontFace(spec.family, `url(${fontUrl(key)}) format('woff2')`, {
      weight: '700', style: 'normal', display: 'block',
    });
    await face.load();
    set.add(face);
    return true;
  } catch {
    return false;
  }
}
