/* ============================================================================
 * audioSrc.js — WHICH HOST is a given audio file actually on?
 *
 * The installer no longer carries the heavy audio (assets/vo, assets/sfx,
 * assets/music, and the dtrh bubble sfx this page borrows for its chimes and
 * pops). Those arrive as downloaded content packs under
 * %LOCALAPPDATA%/ConditioningControlPanel/content/Resources/web, which the WPF
 * host maps as a SECOND virtual origin, https://ccp.content — a byte-for-byte
 * mirror of the page's own tree. So one clip is either
 *
 *     https://ccp.game/intake/assets/vo/…     (legacy full install / dev build)
 *     https://ccp.content/intake/assets/vo/…  (fresh install + downloaded pack)
 *     nowhere                                 (fresh install, packs skipped)
 *
 * and the ONLY difference is the origin — every path stays what it was, so the
 * existing `new URL(rel, import.meta.url)` resolution keeps doing the work and
 * this module just moves the result onto the right host.
 *
 * The host injects window.CCP_CONTENT_READY before any page script runs (true =
 * the content folder exists and is non-empty). That picks which host to TRY
 * FIRST; every loader then gets exactly ONE retry on the other host before
 * falling into the silent-missing-audio path it already had. A wrong first guess
 * costs one 404, never a lost sound, and the third case above still degrades to
 * the synth voices / text-only VO exactly as it does today.
 *
 * NOT for manifests. sfx_manifest.json and vo_manifest.json stay in the
 * installer (they describe what MIGHT be there), so their URLs keep pointing at
 * the page's own origin — only runtime-fetched media goes through here.
 *
 * SIDE-EFFECT FREE AT IMPORT, like every module under core/: pure string work,
 * no fetch, no DOM, safe in the headless tests.
 * ==========================================================================*/

const CONTENT_ORIGIN = 'https://ccp.content';

/** The origin the page itself is served from (https://ccp.game when hosted).
 *  Empty under file:// / headless, where neither host exists and every URL
 *  passes through untouched. */
const PAGE_ORIGIN = (() => {
  try {
    const o = (typeof location !== 'undefined') ? location.origin : '';
    return (o && /^https?:$/.test(location.protocol)) ? o : '';
  } catch (_e) { return ''; }
})();

/** Did the host tell us downloaded pack content is on disk? */
export function contentReady() {
  try { return typeof window !== 'undefined' && window.CCP_CONTENT_READY === true; }
  catch (_e) { return false; }
}

/** Same path on the content host, or null when `url` isn't a page-origin URL. */
function toContent(url) {
  const s = String(url || '');
  if (!s || s.startsWith(CONTENT_ORIGIN + '/')) return null;
  if (s.startsWith('/') && !s.startsWith('//')) return CONTENT_ORIGIN + s;
  if (PAGE_ORIGIN && s.startsWith(PAGE_ORIGIN + '/')) return CONTENT_ORIGIN + s.slice(PAGE_ORIGIN.length);
  return null;
}

/** Same path back on the page's own origin, or null when `url` isn't a content URL. */
function toGame(url) {
  const s = String(url || '');
  if (!PAGE_ORIGIN || !s.startsWith(CONTENT_ORIGIN + '/')) return null;
  return PAGE_ORIGIN + s.slice(CONTENT_ORIGIN.length);
}

/**
 * The URL to try FIRST for one runtime-fetched audio file. Feed it whatever the
 * existing builders produce; it only moves onto the content host when the host
 * says packs are installed, and returns anything else unchanged.
 */
export function audioUrl(url) {
  if (!contentReady()) return url;
  return toContent(url) || url;
}

/**
 * The OTHER host's candidate for `url` (content <-> page origin), or null when
 * there isn't one: ccp.assets media, data:/blob:, file:// standalone, and any
 * URL already sitting on the host it would swap to.
 */
export function altAudioUrl(url) {
  return toContent(url) || toGame(url);
}

/**
 * The alternate-host candidate for a media ELEMENT that just failed to load, or
 * null once it has been spent for the current clip. Self-resetting: the marker
 * is the alternate we handed out, so a new clip gets its own single retry and a
 * retry that fails again gives up instead of ping-ponging.
 */
export function altSrcFor(el) {
  if (!el) return null;
  let cur = '';
  try { cur = el.currentSrc || el.src || ''; } catch (_e) { return null; }
  if (!cur || cur === el.__ccpAltSrc) return null;   // this IS the retry that just failed
  const alt = altAudioUrl(cur);
  if (!alt) return null;
  try { el.__ccpAltSrc = alt; } catch (_e) { /* one retry regardless */ }
  return alt;
}
