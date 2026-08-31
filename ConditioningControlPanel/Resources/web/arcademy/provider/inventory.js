/* ============================================================================
 * provider/inventory.js — PURE inventory rules for the asset provider.
 *
 * The two bright lines from GROUND-RULES §8, both enforced here:
 *
 *  1. LOCAL vs REMOTE IS DECIDED BY ORIGIN, never by a marker. Anything not
 *     served from https://ccp.* is remote (DTRH's lesson: "a marker is a hint,
 *     an origin is a fact, and only the fact can keep tainted media out of a
 *     canvas"). A `canvasSafe` pool therefore filters on isLocalUrl().
 *  2. iOS IS MP4-ONLY, so the provider filters formats per platform before a url
 *     ever reaches a game.
 *
 * The manifest itself comes from C# (init.settings, mirroring how DTRH builds
 * DtrhAssetManifest) because a page cannot enumerate a virtual host.
 * ==========================================================================*/

export const IMAGE_EXTS = Object.freeze(['jpg', 'jpeg', 'png', 'webp', 'gif', 'avif', 'svg']);
export const LOOP_EXTS  = Object.freeze(['gif', 'webp', 'mp4', 'webm', 'm4v']);
export const VIDEO_EXTS = Object.freeze(['mp4', 'webm', 'm4v']);
export const IOS_VIDEO_EXTS = Object.freeze(['mp4']);

/** The virtual host the desktop shell maps to App.EffectiveAssetsPath. */
export const ASSET_HOST = 'https://ccp.assets/';

export function extOf(url) {
  const s = String(url || '').split(/[?#]/)[0];
  const dot = s.lastIndexOf('.');
  if (dot < 0) return '';
  return s.slice(dot + 1).toLowerCase();
}

/** Local = served by one of our own virtual hosts (ccp.assets / ccp.game / ccp.mod),
 *  or a same-document relative path. Everything else is remote (CORS-tainted). */
export function isLocalUrl(url) {
  const s = String(url || '');
  if (!s) return false;
  if (/^https?:\/\//i.test(s)) return /^https?:\/\/ccp\.[a-z]+\//i.test(s);
  if (/^(data|blob):/i.test(s)) return true;
  return true;                    // relative -> our own document origin
}

/**
 * OUR OWN PAGE, as opposed to our own ORIGIN (2026-08-28). Same origin AND under
 * the document's own directory: the campus's bundled files (the ae-ph-N.svg
 * floor, a game's ./assets), which no warm or probe can make any readier. A
 * same-origin url OUTSIDE that directory is bytes that TRAVEL - inside the
 * Discord Activity the web shim rewrites every Scrolller row to
 * '<frame origin>/scrolller-media/<sub>/<path>', our origin on paper and a
 * proxied CDN in fact - and the warm rail and the vet must treat it exactly as
 * they treat images.scrolller.com. Local by isLocalUrl() is own-page by
 * definition (ccp.*, relative, data:, blob:). Without a `location` (a node
 * harness) nothing same-origin exists, so the answer is false and the callers
 * fall back to their old http(s)-only tests.
 */
export function isOwnPageUrl(url) {
  const s = String(url || '');
  if (!s) return false;
  if (isLocalUrl(s)) return true;
  try {
    if (typeof location === 'undefined' || !location || !location.origin) return false;
    const u = new URL(s);
    if (u.origin !== location.origin) return false;
    const p = String(location.pathname || '/');
    const dir = p.slice(0, p.lastIndexOf('/') + 1) || '/';
    return u.pathname.indexOf(dir) === 0;
  } catch (e) { return false; }
}

/** Turn a manifest entry into an absolute url. Accepts a bare relative path
 *  ("images/foo.gif"), an already-absolute url, or {url|path, kind, mime}. */
export function toAssetUrl(entry) {
  if (!entry) return null;
  const raw = typeof entry === 'string' ? entry : (entry.url || entry.path || entry.name);
  if (!raw) return null;
  const s = String(raw);
  if (/^(https?:|data:|blob:)/i.test(s)) return s;
  const clean = s.replace(/^[./\\]+/, '').replace(/\\/g, '/');
  return ASSET_HOST + clean.split('/').map(encodeURIComponent).join('/');
}

/**
 * A trailing `#.<ext>` FRAGMENT HINT, if the url carries one.
 *
 * The convention is already load-bearing here: `provider/index.js hintedPileUrl()` stamps
 * `#.mp4` on an extension-less `blob:` pile row so the games' `<video>`-vs-`<img>` regexes can
 * read its kind. The desktop host uses the same lane for a fact only IT can know - a `.webp`
 * whose VP8X container flag says it ANIMATES (ccp-bugs#1086, ArcademyHostService
 * AnimatedImageHint). The URL Standard drops the fragment before the fetch, so a hinted url
 * loads the identical bytes.
 *
 * A hint is a HINT, never an origin claim: it can only say which pool an url belongs in, and
 * `isLocalUrl` still decides local-vs-remote off the origin alone (see the header, rule 1).
 */
export function hintExtOf(url) {
  const m = /#\.([a-z0-9]{1,5})$/i.exec(String(url || ''));
  return m ? m[1].toLowerCase() : '';
}

/** Which pool kind an url belongs to. 'loop' = animated/video, 'still' = image. */
export function kindOf(entry) {
  if (entry && typeof entry === 'object' && (entry.kind === 'loop' || entry.kind === 'still')) return entry.kind;
  const raw = typeof entry === 'string' ? entry : (entry && (entry.url || entry.path)) || '';
  // The hint outranks the extension because it is the more specific fact: `a.webp` is a guess,
  // `a.webp#.gif` is the host reporting a header it actually read.
  const ext = hintExtOf(raw) || extOf(raw);
  // `webp` sits in LOOP_EXTS but is excluded here: the NAME cannot tell an animated webp from a
  // still one, so an unhinted webp is a still. A hinted one arrives as 'gif' above.
  if (LOOP_EXTS.includes(ext) && ext !== 'webp') return 'loop';
  if (ext === 'gif') return 'loop';
  return IMAGE_EXTS.includes(ext) ? 'still' : 'still';
}

/** Platform format gate. iOS plays mp4 only; a still is a still everywhere. */
export function formatOk(url, platform) {
  const ext = extOf(url);
  if (!ext) return true;
  const isVideo = VIDEO_EXTS.includes(ext);
  if (!isVideo) return true;
  const host = platform && (platform.host || platform.os || '');
  const ios = /ios|iphone|ipad/i.test(String(host)) || !!(platform && platform.iosMediaOnly);
  return ios ? IOS_VIDEO_EXTS.includes(ext) : true;
}

/**
 * Build the two local pools from a manifest.
 * @param {Array} manifest strings or {url,kind,mime} entries (ccp.assets-relative ok)
 * @param {object} platform init.platform projection
 * @returns {{loop:string[], still:string[]}}
 */
export function buildLocalPools(manifest, platform) {
  const out = { loop: [], still: [] };
  const seen = new Set();
  for (const entry of (Array.isArray(manifest) ? manifest : [])) {
    const url = toAssetUrl(entry);
    if (!url || seen.has(url)) continue;
    if (!isLocalUrl(url)) continue;          // a "local" manifest never carries remote urls
    if (!formatOk(url, platform)) continue;
    seen.add(url);
    out[kindOf(entry)].push(url);
  }
  return out;
}

/**
 * Should THIS draw be served from the remote pool?
 * Pure: ratio 0 -> never, 1 -> whenever remote has anything. canvasSafe pools
 * pass ratio 0 (the CORS two-pool law) — enforced by the caller too.
 */
export function wantRemote(ratio, rand, haveRemote, canvasSafe) {
  if (canvasSafe) return false;
  if (!haveRemote) return false;
  const r = Number(ratio);
  if (!Number.isFinite(r) || r <= 0) return false;
  return rand < Math.min(1, r);
}

export default { buildLocalPools, isLocalUrl, isOwnPageUrl, formatOk, hintExtOf, kindOf, toAssetUrl, wantRemote };
