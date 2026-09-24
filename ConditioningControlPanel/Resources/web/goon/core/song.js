/* ============================================================================
 * core/song.js - Game Night "pick a song". PURE: no DOM, no network, node-safe.
 *
 * The match lasts as long as the song. The HOST pastes a BambiCloud track link,
 * the page reads the file's length off its metadata, and that length (clamped
 * 60..1200 s) becomes the consent sheet's live duration through the existing
 * proposeConsent road. The url rides to the guest in one `t:'song'` frame.
 *
 * THE BRIGHT LINE, copied from Resources/web/dtrh/race/cloud.js (race/CLOUD.md):
 * the page talks to the audio CDN directly or not at all. No proxy, no
 * credentials, and only two kinds of url are ever loaded: one on
 * cdn.bambicloud.com, and a same-origin one the LOCAL player picked. A url that
 * came off the WIRE is held to the stricter half: https on the CDN host only,
 * never "same origin", because the peer's origin is not ours and a relative
 * idea of "self" is exactly the thing a peer must not be able to point at.
 *
 * The pure link bits are COPIED from race/cloud.js rather than imported: the
 * goon page is deployed standalone and cannot reach outside goon/.
 *
 * PLAYBACK FOLLOWS THE MATCH CLOCK. songSyncAction() is the whole rule: the
 * element is told where the match says it should be, and re-seeked when it has
 * drifted more than SONG_DRIFT_MS. The existing duration timer ends Live, so
 * the end of the match never waits on the audio.
 * ==========================================================================*/

/** The only third-party host a song url may name. Its files answer `Access-Control-Allow-Origin: *`. */
export const SONG_HOST = 'cdn.bambicloud.com';
/** A song shorter than a minute or longer than twenty is clamped into this band. */
export const SONG_MIN_SEC = 60;
export const SONG_MAX_SEC = 1200;
/** How far the element may wander from the match clock before it is re-seeked. */
export const SONG_DRIFT_MS = 300;
/** Waiting on metadata: past this the pick is dropped and the match runs silent on defaults. */
export const SONG_LOAD_TIMEOUT_MS = 15000;
/** Wire caps. A title is a label, a url is one CDN path. */
export const SONG_TITLE_MAX = 60;
export const SONG_URL_MAX = 512;

/** The sub-kinds of `t:'song'`. FROZEN: append only, never repurpose. */
export const SONG_SUBS = Object.freeze(['set', 'clear']);

/** Untrusted sub -> one of SONG_SUBS, or '' (a newer peer's kind falls off the switch). */
export function clampSongSub(v) {
  return SONG_SUBS.includes(v) ? v : '';
}

/**
 * Untrusted length in seconds -> 0 (no song / unknown) or an integer in
 * SONG_MIN_SEC..SONG_MAX_SEC. Booleans, objects and NaN read as 0, the same
 * rule clampVoiceCount lives by: `Number(true)` being 1 must never happen here.
 */
export function clampSongSec(v) {
  const n = typeof v === 'number' ? v : (typeof v === 'string' ? Number(v) : NaN);
  if (!Number.isFinite(n) || n <= 0) return 0;
  const i = Math.round(n);
  return i < SONG_MIN_SEC ? SONG_MIN_SEC : (i > SONG_MAX_SEC ? SONG_MAX_SEC : i);
}

/** The last path segment, made readable (race/cloud.js titleOf). */
export function songTitleOf(u) {
  const seg = u.pathname.split('/').filter(Boolean).pop() || '';
  let s = seg;
  try { s = decodeURIComponent(seg); } catch (e) { /* keep the raw segment */ }
  s = s.replace(/\.[a-z0-9]{2,4}$/i, '').replace(/[_+-]+/g, ' ').trim().toLowerCase();
  return (s || u.host).slice(0, SONG_TITLE_MAX);
}

/** What a track picked by its page link is called: the page link carries no name. */
export const SONG_FILE_TITLE = 'bambicloud track';
const FILE_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * A track's PAGE link (what copying a link on BambiCloud hands out,
 * `https://bambicloud.com/file/<uuid>`) -> its audio file on the CDN, or ''.
 * The CDN keeps every file at `https://cdn.bambicloud.com/<uuid>.mp3`: that is how
 * every level in race/levels.json is written down (id and url agree on all of
 * them, checked 2026-09-23), so this is a rename, not a lookup. No network. Only
 * the one path shape is read: a playlist or any other page stays refused as a page.
 */
export function bambicloudFileToCdn(u) {
  if (!u || !/^(www\.)?bambicloud\.com$/i.test(u.hostname) || u.port) return '';
  const parts = u.pathname.split('/').filter(Boolean);
  if (parts.length !== 2 || parts[0].toLowerCase() !== 'file' || !FILE_ID.test(parts[1])) return '';
  return 'https://' + SONG_HOST + '/' + parts[1].toLowerCase() + '.mp3';
}

/**
 * The LOCAL paste box. One link in, one verdict out, no network:
 *   { url, title }                      playable (the CDN, a track page mapped to it, or same origin)
 *   { refused: 'page' }                 a bambicloud.com page, not a file
 *   { refused: 'host' }                 some other site
 *   { refused: 'empty' | 'bad' }        nothing, or not a link at all
 *
 * @param {string} text
 * @param {string|null} origin  location.origin, so a same-origin file is playable
 */
export function parseSongLink(text, origin = null) {
  const s = String(text || '').trim().split(/\s+/)[0] || '';
  if (!s) return { refused: 'empty' };
  let u = null;
  try { u = new URL(s); } catch (e) { u = null; }
  if (!u || (u.protocol !== 'https:' && u.protocol !== 'http:')) return { refused: 'bad' };
  if (u.username || u.password) return { refused: 'bad' };
  if (u.host === SONG_HOST && u.protocol === 'https:') return { url: u.href, title: songTitleOf(u) };
  if (origin && u.origin === origin) return { url: u.href, title: songTitleOf(u) };
  const fileUrl = bambicloudFileToCdn(u);
  if (fileUrl) return { url: fileUrl, title: SONG_FILE_TITLE };
  if (/(^|\.)bambicloud\.com$/i.test(u.hostname)) return { refused: 'page' };
  return { refused: 'host' };
}

/**
 * A url that arrived on the WIRE -> the url to load, or '' to ignore the frame.
 * Stricter than parseSongLink on purpose: https, the CDN host exactly, no
 * credentials, no port, bounded length. Nothing else is ever handed to an element.
 */
export function wireSongUrl(v) {
  if (typeof v !== 'string' || v.length === 0 || v.length > SONG_URL_MAX) return '';
  let u = null;
  try { u = new URL(v); } catch (e) { return ''; }
  if (u.protocol !== 'https:' || u.host !== SONG_HOST || u.username || u.password || u.port) return '';
  return u.href;
}

/**
 * What the element should do right now, given the match clock.
 *
 *   { stop: true }      Live is over (or the song is): silence
 *   { seek: sec }       start here / come back here, the drift is past the limit
 *   null                leave it alone
 *
 * @param {object} o
 * @param {number} o.elapsedMs   the match's Live elapsed
 * @param {number} o.audioSec    the element's currentTime (NaN before it has one)
 * @param {number} [o.durationSec] the file's own length, when known
 * @param {number} [o.liveMs]    the match's Live length, when known
 * @param {number} [o.driftMs]
 */
export function songSyncAction({ elapsedMs, audioSec, durationSec = 0, liveMs = 0, driftMs = SONG_DRIFT_MS }) {
  const want = Math.max(0, Number(elapsedMs) || 0) / 1000;
  if (liveMs > 0 && want * 1000 >= liveMs) return { stop: true };
  if (durationSec > 0 && want >= durationSec) return { stop: true };
  const have = Number(audioSec);
  if (!Number.isFinite(have)) return { seek: want };
  return Math.abs(have - want) * 1000 > driftMs ? { seek: want } : null;
}

/** m:ss for the song row. */
export function songClock(sec) {
  const s = Math.max(0, Math.round(Number(sec) || 0));
  return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
}
