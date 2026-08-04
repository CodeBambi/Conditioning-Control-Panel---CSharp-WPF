/* ============================================================================
 * ui/inviteLink.js — the SHAREABLE invite, and the deep link that redeems it.
 *
 * A six-character code is a fine thing to read down a phone line and a terrible
 * thing to type on one. This module is the other half of hosting: the same room,
 * expressed as a URL the host can paste anywhere, which boots the standalone web
 * client straight into the join flow with nothing left to type.
 *
 *   host  ->  buildInviteUrl(code, {hosted, origin, pathname})  ->  ".../?join=ABC123"
 *   guest ->  readJoinCode(location.search)  ->  "ABC123"  ->  join, auto-submitted
 *             stripJoinParam(window)          (so a refresh is not a second join)
 *
 * TWO BASES, ONE FUNCTION, AND THE DIFFERENCE IS LOAD-BEARING:
 *   - STANDALONE the page already IS the public deployment, so the link is
 *     `location.origin + location.pathname` — whatever host the player actually
 *     reached us on, including a LAN address during a play-test.
 *   - HOSTED the page is served from the WebView2 virtual host
 *     `https://ccp.game/goon/index.html` (GoonHostService), which resolves on
 *     exactly one machine and nowhere else. Pasting that URL into a chat window
 *     hands the other player a link that cannot possibly load, so the hosted
 *     copy points at the PUBLIC deployment instead.
 *
 * NORMALISATION IS THE SERVER'S, VERBATIM — net/signaling.js normalizeCode (trim
 * + uppercase + strip dashes/spaces) and nothing else, so a link that has been
 * through a chat client's auto-lowercasing, a line wrap or a helpful hyphen
 * still redeems the same room. It deliberately does NOT fold Crockford's I/L->1
 * or O->0, for the reason spelled out in ui/screens/join.js.
 *
 * No DOM and no side effects at import: every window/location/history touch
 * below is inside a function and guarded, so this is import-safe under node.
 * ==========================================================================*/

import { normalizeCode } from '../net/signaling.js';

/** The invite code length the server mints (mirrors ui/screens/join.js). */
export const CODE_LEN = 6;

/** The querystring key. One name, spelled in exactly one place. */
export const JOIN_PARAM = 'join';

/**
 * WHERE THE PUBLIC WEB CLIENT LIVES — the only base a HOSTED page may put in a
 * link, because `https://ccp.game/…` is a WebView2 virtual host and resolves
 * nowhere but inside the app.
 *
 * TODO(deploy): this is the documented target of the standalone PWA build
 * (index.html's PWA banner names `cclabs.app/goon-beta/`) and it is asserted
 * here rather than derived, because there is nothing to derive it from inside
 * the app. When the beta path moves — to `cclabs.app/goon/`, to its own
 * subdomain, or behind a version prefix — change it HERE and nowhere else, and
 * make sure the deployed page is the one that reads `?join=`.
 */
export const GOON_PUBLIC_URL = 'https://cclabs.app/goon-beta/';

/**
 * The Discord invite. Mirrors the one the desktop app opens
 * (MainWindow.AccountShell.cs / MainWindow.Assets.cs) — the same server, so a
 * player who followed a duel link into the browser lands where the packs are.
 */
export const DISCORD_INVITE_URL = 'https://discord.gg/YxVAMt4qaZ';

/** Anything that is not an http(s) origin (file:, "null", a stub) is unusable. */
function usableOrigin(origin) {
  const s = String(origin || '');
  return /^https?:\/\//i.test(s) ? s.replace(/\/+$/, '') : '';
}

/**
 * A room code -> the URL that joins it.
 *
 * @param {string} code the code the server minted
 * @param {object} [o]
 * @param {boolean} [o.hosted] true inside WebView2 (bridge.isHosted)
 * @param {string} [o.origin] `location.origin` — standalone only
 * @param {string} [o.pathname] `location.pathname` — standalone only
 * @returns {string} the link, or '' when there is no code to link to
 */
export function buildInviteUrl(code, { hosted = false, origin = '', pathname = '' } = {}) {
  const c = normalizeCode(code).slice(0, CODE_LEN);
  if (!c) return '';

  let base = GOON_PUBLIC_URL;
  const org = hosted ? '' : usableOrigin(origin);
  if (org) {
    // A directory path keeps its trailing slash; index.html keeps its filename.
    // Either is a page the browser can load, which is the only test that matters.
    const p = String(pathname || '/');
    base = org + (p.charAt(0) === '/' ? p : '/' + p);
  }
  return base + (base.indexOf('?') >= 0 ? '&' : '?') + JOIN_PARAM + '=' + c;
}

/**
 * `location.search` -> the code somebody linked us to, normalized.
 *
 * Tolerant on purpose: case, surrounding whitespace, `%20`, and the hyphens a
 * human adds for readability all come back as the same six characters. A value
 * that normalizes to nothing is '' (no link), and a value that normalizes to
 * fewer than six characters is returned AS IS rather than swallowed — the join
 * screen prefills it and says "six characters, please", which is a better
 * failure than a menu that quietly ignored the link.
 *
 * @param {string} search e.g. `location.search`
 * @returns {string} '' or up to CODE_LEN normalized characters
 */
export function readJoinCode(search) {
  const s = String(search || '');
  if (!s) return '';
  let raw = null;
  try {
    raw = new URLSearchParams(s.charAt(0) === '?' ? s.slice(1) : s).get(JOIN_PARAM);
  } catch (_e) { return ''; }
  if (raw === null || raw === undefined) return '';
  return normalizeCode(raw).slice(0, CODE_LEN);
}

/** True when a code is long enough to hand to /v2/goon/join without asking first. */
export function isCompleteCode(code) {
  return normalizeCode(code).slice(0, CODE_LEN).length === CODE_LEN;
}

/**
 * Take the `join` param back out of the address bar, in place.
 *
 * WHY THIS IS NOT OPTIONAL: the link is meant to be tapped once. Leaving it in
 * the URL means a refresh, a back/forward, or an add-to-home-screen pin made
 * from this page re-attempts a room that is five minutes dead — and the player
 * gets "no room with that code" on every launch forever after. replaceState (not
 * pushState) so the history entry is corrected rather than duplicated.
 *
 * Everything else on the querystring survives: `?server=`, `?token=`, `?uid=`,
 * `?debug=` are the phone client's credentials and bridge.js has already adopted
 * them, but a player who pinned the page still deserves them in the URL.
 *
 * @param {Window} [win] defaults to the global window
 * @returns {boolean} true when a param was actually removed
 */
export function stripJoinParam(win) {
  try {
    const w = win || (typeof window !== 'undefined' ? window : null);
    if (!w || !w.location || !w.history || typeof w.history.replaceState !== 'function') return false;
    const href = String(w.location.href || '');
    if (!href) return false;
    const url = new URL(href);
    if (!url.searchParams.has(JOIN_PARAM)) return false;
    url.searchParams.delete(JOIN_PARAM);
    const qs = url.searchParams.toString();
    w.history.replaceState(null, '', url.pathname + (qs ? '?' + qs : '') + (url.hash || ''));
    return true;
  } catch (_e) {
    // A sandboxed iframe, a file: URL, a browser that refuses replaceState — the
    // link still worked, and re-joining a dead room is a survivable annoyance.
    return false;
  }
}

/**
 * The one call boot.js makes: read the link, then immediately erase it.
 *
 * Reading and stripping are deliberately ONE verb. Two calls would leave a
 * window in which an early failure returns the player to a page that is still
 * holding the code, and the next reload would join the dead room all over again.
 *
 * @param {Window} [win]
 * @returns {string} '' or the normalized code from the link
 */
export function consumeJoinCode(win) {
  const w = win || (typeof window !== 'undefined' ? window : null);
  const search = (w && w.location && w.location.search) || '';
  const code = readJoinCode(search);
  if (code) stripJoinParam(w);
  return code;
}

export default {
  CODE_LEN, JOIN_PARAM, GOON_PUBLIC_URL, DISCORD_INVITE_URL,
  buildInviteUrl, readJoinCode, isCompleteCode, stripJoinParam, consumeJoinCode,
};
