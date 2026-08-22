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

/** Which pool kind an url belongs to. 'loop' = animated/video, 'still' = image. */
export function kindOf(entry) {
  if (entry && typeof entry === 'object' && (entry.kind === 'loop' || entry.kind === 'still')) return entry.kind;
  const ext = extOf(typeof entry === 'string' ? entry : (entry && (entry.url || entry.path)) || '');
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

export default { buildLocalPools, isLocalUrl, formatOk, kindOf, toAssetUrl, wantRemote };
