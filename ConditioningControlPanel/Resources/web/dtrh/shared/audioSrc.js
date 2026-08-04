/* ============================================================================
 * audioSrc.js - which host is this audio file actually on?
 *
 * The installer no longer ships the heavy audio (per-persona barks, the drone
 * bed, the bubble sfx, the VN voiceover). Those download as content packs into
 * %LOCALAPPDATA%/ConditioningControlPanel/content/Resources/web, which the WPF
 * host maps as a SECOND virtual origin, https://ccp.content - a byte-for-byte
 * mirror of the ccp.game tree. So the same clip is either
 *
 *     https://ccp.game/dtrh/assets/...     (legacy full install / dev build)
 *     https://ccp.content/dtrh/assets/...  (fresh install + downloaded pack)
 *     nowhere                              (fresh install, packs skipped)
 *
 * and the only difference is the ORIGIN. The host injects
 * window.CCP_CONTENT_READY before any page script runs (true = the content
 * folder exists and is non-empty), which picks the first host to try; every
 * caller then gets ONE retry on the other host via altAudioUrl / altSrcFor
 * before falling into whatever silent-degradation it already had. A wrong first
 * guess therefore costs one 404, never a missing sound - and the third case
 * above still degrades exactly as it does today.
 *
 * NOT for manifests. bark manifest.js / bubbles manifest.js / vn manifest.json
 * stay in the installer (a missing import-time ES module is a dead page, not a
 * quiet one), so their URLs must keep pointing at the page's own origin. Only
 * runtime-fetched media (mp3/ogg/wav) goes through here.
 *
 * Pass-through by design: ccp.mod (creator-mod clips), ccp.assets (user media),
 * blob:/data: and file:// standalone all come back unchanged with no alternate.
 * ==========================================================================*/

const CONTENT_ORIGIN = 'https://ccp.content';

/** The origin the page itself is served from (ccp.game when hosted). Empty in
 *  file:// standalone, where neither host exists and everything passes through. */
const PAGE_ORIGIN = (() => {
  try {
    const o = (typeof location !== 'undefined') ? location.origin : '';
    return (o && /^https?:$/.test(location.protocol)) ? o : '';
  } catch (e) { return ''; }
})();

/** Did the host say downloaded pack content is on disk? */
export function contentReady() {
  try { return typeof window !== 'undefined' && window.CCP_CONTENT_READY === true; }
  catch (e) { return false; }
}

/** Same path on the content host, or null if `url` isn't a page-origin URL. */
function toContent(url) {
  const s = String(url || '');
  if (!s || s.startsWith(CONTENT_ORIGIN + '/')) return null;
  if (s.startsWith('/') && !s.startsWith('//')) return CONTENT_ORIGIN + s;   // '/dtrh/assets/...'
  if (PAGE_ORIGIN && s.startsWith(PAGE_ORIGIN + '/')) return CONTENT_ORIGIN + s.slice(PAGE_ORIGIN.length);
  return null;
}

/** Same path back on the page's own origin, or null if `url` isn't a content URL. */
function toGame(url) {
  const s = String(url || '');
  if (!PAGE_ORIGIN || !s.startsWith(CONTENT_ORIGIN + '/')) return null;
  return PAGE_ORIGIN + s.slice(CONTENT_ORIGIN.length);
}

/**
 * The URL to try FIRST for one runtime-fetched audio file. Takes whatever the
 * engines already build ('/dtrh/assets/...' or an absolute page URL) and only
 * moves it onto the content host when the host says packs are installed.
 */
export function audioUrl(url) {
  if (!contentReady()) return url;
  return toContent(url) || url;
}

/**
 * The OTHER host's candidate for `url` (content <-> page), or null when there
 * isn't one: mod/user-asset origins, data:/blob:, file:// standalone, and any
 * URL that is already on the host it would swap to.
 */
export function altAudioUrl(url) {
  return toContent(url) || toGame(url);
}

/**
 * The alternate-host candidate for a media element that just failed to load,
 * or null when it has already been tried for this clip. Self-resetting: the
 * marker is the alternate we handed out, so the next clip (a different src)
 * gets its own single retry and a retry that fails again gives up.
 *
 * Usage is always "one line in the existing failure path":
 *     const alt = altSrcFor(el);
 *     if (alt) { el.src = alt; el.play().catch(...); return; }
 *     ...existing silent handling...
 */
export function altSrcFor(el) {
  if (!el) return null;
  let cur = '';
  try { cur = el.currentSrc || el.src || ''; } catch (e) { return null; }
  if (!cur || cur === el.__ccpAltSrc) return null;   // this IS the retry that just failed
  const alt = altAudioUrl(cur);
  if (!alt) return null;
  try { el.__ccpAltSrc = alt; } catch (e) { /* frozen element: one retry is still fine */ }
  return alt;
}
