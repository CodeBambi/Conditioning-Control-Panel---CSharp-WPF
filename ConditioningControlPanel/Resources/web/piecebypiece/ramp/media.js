/* ============================================================================
 * ramp/media.js - where the ramp's pictures come from.
 *
 * TWO sources, ONE interface, so a layer never knows which one it is riding:
 *
 *   createFixtureMedia(list)  dev/test. `list` is the parsed dev/media.json:
 *                             [{ kind:'image'|'gif'|'video', url }]. An EMPTY
 *                             list is a supported state, not an error - draw()
 *                             answers null, the video card never appears and
 *                             the gif layers fall back to a pink noise tile.
 *
 *   createHostMedia()         the shipping source. STUB for now: the C# host
 *                             will post the same manifest DtRH gets
 *                             (DtrhAssetManifest -> https://ccp.assets/ urls,
 *                             see dtrh/hostMedia.js) and the page will feed it
 *                             in through `adopt()`. Until that lands it behaves
 *                             exactly like an empty fixture source, so the ramp
 *                             degrades instead of throwing.
 *
 * Everything is URL-only. No fetch, no blobs, no canvas, no WebGL upload: the
 * manifest may carry remote CDN entries that send no CORS headers, and a plain
 * <img>/<video>/background-image is the only road those can take (this is the
 * same rule that keeps dtrh/hostMedia.js's two pools apart).
 * ==========================================================================*/

const KINDS = ['image', 'gif', 'video'];
const NO_ECHO = 6;   // a reshuffled deck avoids repeating the last N draws

/** A pink static tile, generated in-page, used wherever a gif is missing. */
export function noiseTileUrl(seed = 2) {
  const svg = '<svg xmlns="http://www.w3.org/2000/svg" width="160" height="160">'
    + '<filter id="n"><feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="'
    + Math.max(1, seed | 0)
    + '"/><feColorMatrix values="0.9 0 0 0 0.55  0 0.2 0 0 0.08  0 0 0.6 0 0.5  0 0 0 0.7 0"/></filter>'
    + '<rect width="160" height="160" filter="url(#n)"/></svg>';
  return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
}

/** Shuffle in place with an optional injected rng (tests want determinism). */
function shuffle(arr, rnd) {
  for (let i = arr.length - 1; i > 0; i--) {
    const j = Math.floor((rnd ? rnd() : Math.random()) * (i + 1));
    const tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp;
  }
  return arr;
}

/** The shared pool body both sources use. `entries` may be replaced by adopt(). */
function createPool(entries, { rnd, label } = {}) {
  let all = [];
  const decks = {};      // kind -> remaining shuffled indices
  const recent = [];     // last NO_ECHO urls handed out

  function ingest(list) {
    all = [];
    for (const raw of Array.isArray(list) ? list : []) {
      if (!raw) continue;
      const url = typeof raw === 'string' ? raw : raw.url;
      if (!url || typeof url !== 'string') continue;
      let kind = typeof raw === 'string' ? '' : String(raw.kind || '');
      if (!KINDS.includes(kind)) {
        // infer from the extension when the entry did not say
        if (/\.(mp4|webm|m4v|mov)(\?|$)/i.test(url)) kind = 'video';
        else if (/\.gif(\?|$)/i.test(url)) kind = 'gif';
        else kind = 'image';
      }
      all.push({ kind, url, name: (typeof raw === 'object' && raw.name) || url.split('/').pop() });
    }
    for (const k of KINDS) delete decks[k];
    recent.length = 0;
  }
  ingest(entries);

  /** Indices of every entry usable as `kind` (a gif is also a fine image). */
  function poolFor(kind) {
    const want = kind === 'gif' ? ['gif'] : (kind === 'video' ? ['video'] : ['image', 'gif']);
    const out = [];
    for (let i = 0; i < all.length; i++) if (want.includes(all[i].kind)) out.push(i);
    return out;
  }

  function has(kind) { return poolFor(kind).length > 0; }

  /** One url, non-repeating across a deck, echo-guarded on tiny pools. */
  function draw(kind) {
    const k = KINDS.includes(kind) ? kind : 'image';
    const pool = poolFor(k);
    if (!pool.length) return null;
    let deck = decks[k];
    if (!deck || !deck.length) deck = decks[k] = shuffle(pool.slice(), rnd);
    let idx = deck.pop();
    // echo guard: on a pool bigger than the guard, skip a url we just used
    if (pool.length > NO_ECHO && recent.includes(all[idx].url) && deck.length) idx = deck.pop();
    const url = all[idx].url;
    recent.push(url);
    while (recent.length > NO_ECHO) recent.shift();
    return url;
  }

  /** A gif url, or a generated pink noise tile when the pool has none. */
  function drawTile() { return draw('gif') || draw('image') || noiseTileUrl(2 + ((Math.random() * 3) | 0)); }

  function stats() {
    const out = { label: label || 'pool', total: all.length };
    for (const k of KINDS) out[k] = all.filter((e) => e.kind === k).length;
    return out;
  }

  return { has, draw, drawTile, stats, adopt: ingest, get size() { return all.length; } };
}

/**
 * Dev/test source. Pass the parsed dev/media.json (or []). An empty list is a
 * first-class state: no video card, gifs become pink noise tiles.
 */
export function createFixtureMedia(list, opts = {}) {
  const pool = createPool(list, { rnd: opts.rnd, label: 'fixture' });
  return { kind: 'fixture', ...pool };
}

/**
 * Shipping source (STUB). The C# host has no Piece by Piece manifest yet; when
 * it grows one it will post the DtRH-shaped frame
 *   { type:'manifest', images:[...], videos:[...] }   // https://ccp.assets/ urls
 * and the page will call `media.adopt(entries)` with it. Nothing in the ramp
 * needs to change: adopt() is the same door createFixtureMedia uses.
 *
 * Do NOT build the C# side from here - this is only the page-side seam.
 */
export function createHostMedia(entries) {
  const pool = createPool(entries || [], { label: 'host' });
  if (!pool.size) {
    try { console.warn('[pbp/ramp] host media pool is empty; the ramp runs without pictures.'); } catch { /* no console */ }
  }
  return { kind: 'host', ...pool };
}

/** Load dev/media.json next to the harness. Never throws: [] on any failure. */
export async function loadFixtureList(url) {
  try {
    const res = await fetch(url, { cache: 'no-cache' });
    if (!res.ok) return [];
    const json = await res.json();
    return Array.isArray(json) ? json : (Array.isArray(json && json.entries) ? json.entries : []);
  } catch { return []; }
}

export default createFixtureMedia;
