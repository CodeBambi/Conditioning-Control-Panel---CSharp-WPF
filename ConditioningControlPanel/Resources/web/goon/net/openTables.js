/* ============================================================================
 * net/openTables.js - Open tables: the pure half.
 *
 * The lobby of open games (owner, 2026-09-23). Everything here is data in,
 * data out, so test/selftest-opentables.js can pin it without a DOM or a server:
 *
 *   - normalizeTable / normalizeOpen: a /v2/goon/open answer made safe to show.
 *     NO FREE TEXT FROM A HOST REACHES THE LIST. A row is a sanitised display
 *     name, a level, an https avatar url, and flags. Anything else on the wire
 *     is dropped here, so a server that one day sent more would still render
 *     exactly this much.
 *   - sortTables / groupTables: friends first, then everyone else, newest first.
 *   - gateFor: who may host / join. Every 1v1 is a Prime (tier 2) perk; the
 *     practice match is free and is never asked about here at all.
 *   - createTicker: the one timer both loops use (poll /open every 15 s while
 *     the list is visible, renew /list every 60 s while a table waits).
 *   - small formatters for the row chips.
 * ==========================================================================*/

import { cleanName } from '../ui/rivalry.js';

export const VISIBILITY = Object.freeze({ Friends: 'friends', Anyone: 'anyone', Off: 'off' });
export const VISIBILITIES = Object.freeze([VISIBILITY.Friends, VISIBILITY.Anyone, VISIBILITY.Off]);

/** How often the list refreshes while it is on screen. */
export const OPEN_POLL_MS = 15000;
/** How often a waiting host renews its listing (the server keeps it 150 s). */
export const LIST_RENEW_MS = 60000;
/** The server's cap, mirrored so a longer answer cannot flood the page. */
export const TABLES_MAX = 20;

const CODE_RE = /^[A-Z0-9]{4,12}$/;
const AVATAR_MAX = 512;
const LEVEL_MAX = 9999;
const CARD_MAX_SEC = 3600;
const WAIT_MAX_SEC = 24 * 3600;

export function normalizeVisibility(v) {
  const s = String(v == null ? '' : v).toLowerCase();
  return VISIBILITIES.includes(s) ? s : VISIBILITY.Friends;
}

function intIn(v, lo, hi) {
  const n = Math.floor(Number(v));
  if (!Number.isFinite(n)) return null;
  return Math.max(lo, Math.min(hi, n));
}

/**
 * Only an https url or the server's own friend-avatar path, only so long, never
 * anything with a quote or a space in it. The server (CCP-Server #209) sends
 * `/v2/friends/avatar/<uid>`, a proxy-relative path, and only on friend rows.
 */
const AVATAR_PATH_RE = /^\/v2\/friends\/avatar\/[A-Za-z0-9_-]{1,64}$/;
export function safeAvatar(v) {
  if (typeof v !== 'string') return '';
  const s = v.trim();
  if (!s || s.length > AVATAR_MAX) return '';
  if (AVATAR_PATH_RE.test(s)) return s;
  if (!/^https:\/\/[^\s"'<>()\\]+$/i.test(s)) return '';
  return s;
}

/** A row's avatar as an <img> src: relative paths ride the server base, or there is none. */
export function resolveAvatar(avatar, serverBase) {
  const a = safeAvatar(avatar);
  if (!a) return '';
  if (a[0] !== '/') return a;
  const base = String(serverBase || '').replace(/\/+$/, '');
  return /^https:\/\//i.test(base) ? base + a : '';
}

/**
 * One row off the wire, or null when it cannot be shown.
 * @returns {{code, name, level, avatar, friend, song, cardSec, pictures, waitingSec}|null}
 */
export function normalizeTable(raw) {
  if (!raw || typeof raw !== 'object') return null;
  const code = String(raw.code == null ? '' : raw.code).trim().toUpperCase().replace(/[-\s]/g, '');
  if (!CODE_RE.test(code)) return null;
  const name = cleanName(raw.name);
  return {
    code,
    name: name || 'someone',
    level: intIn(raw.level, 0, LEVEL_MAX),
    avatar: safeAvatar(raw.avatar),
    friend: raw.friend === true,
    song: raw.song === true,
    cardSec: intIn(raw.cardSec, 0, CARD_MAX_SEC),
    pictures: raw.pictures === true,
    waitingSec: intIn(raw.waitingSec, 0, WAIT_MAX_SEC) ?? 0,
  };
}

/** Friends first, then newest (least waiting) first; one row per code; capped. */
export function sortTables(rows) {
  const seen = new Set();
  const out = [];
  for (const r of Array.isArray(rows) ? rows : []) {
    if (!r || seen.has(r.code)) continue;
    seen.add(r.code);
    out.push(r);
  }
  out.sort((a, b) => (Number(b.friend) - Number(a.friend)) || (a.waitingSec - b.waitingSec)
    || (a.code < b.code ? -1 : a.code > b.code ? 1 : 0));
  return out.slice(0, TABLES_MAX);
}

export function groupTables(rows) {
  const sorted = sortTables(rows);
  return { friends: sorted.filter((r) => r.friend), anyone: sorted.filter((r) => !r.friend) };
}

/**
 * A whole /open answer. `you` is null when the server did not say, which is
 * NOT the same as "no": gateFor then falls back to the host's own flags.
 */
export function normalizeOpen(json) {
  const j = (json && typeof json === 'object') ? json : {};
  const y = (j.you && typeof j.you === 'object') ? j.you : null;
  const you = y && (typeof y.canHost === 'boolean' || typeof y.canJoin === 'boolean')
    ? { canHost: y.canHost === true, canJoin: y.canJoin === true }
    : null;
  const tables = sortTables((Array.isArray(j.tables) ? j.tables : []).map(normalizeTable).filter(Boolean));
  const ago = intIn(j.lastOpenedAgoSec, 0, 365 * 24 * 3600);
  return { you, tables, lastOpenedAgoSec: ago };
}

/**
 * Who may host and join. Practice is free and never passes through here.
 *
 *   1. The server's own `you` block wins: it is the same entitlement the gates use.
 *   2. Hosted with no answer: the C# init flags. `=== false` locks, absent does
 *      not (a host that predates the flag must not lock a paying player out).
 *   3. Standalone with no answer: unknowable, so nothing is locked on a guess;
 *      the server's 403 opens the Prime sheet instead.
 * @returns {{canHost:boolean, canJoin:boolean, source:'server'|'host'|'unknown'}}
 */
export function gateFor({ you = null, caps = null, hosted = false } = {}) {
  if (you) return { canHost: you.canHost === true, canJoin: you.canJoin === true, source: 'server' };
  if (hosted) {
    const c = caps || {};
    return { canHost: c.canHost !== false, canJoin: c.canJoin !== false, source: 'host' };
  }
  return { canHost: true, canJoin: true, source: 'unknown' };
}

/** What a failed /join means for a row: Taken, the Prime sheet, sign in, or a plain sheet. */
export function joinOutcome(kind) {
  switch (String(kind || '')) {
    case 'already_joined':
    case 'unknown_code':
    case 'expired':
      return 'taken';
    // `no_join_access` is retired server-side (2026-09-24: joining is free). An OLD server in
    // front of this page can still send it, and the patron sheet is still the honest answer.
    case 'no_join_access':
    case 'no_host_access':
      return 'prime';
    case 'signin':
      return 'signin';
    default:
      return 'sheet';
  }
}

/** The listing body for /v2/goon/list, from what the host screen knows. */
export function listingBody({ code, visibility, song, cardSec, pictures } = {}) {
  return {
    code: String(code || '').trim().toUpperCase().replace(/[-\s]/g, ''),
    visibility: normalizeVisibility(visibility),
    song: song === true,
    cardSec: intIn(cardSec, 0, CARD_MAX_SEC) ?? 0,
    pictures: pictures === true,
  };
}

/* ------------------------------------------------------------ formatters */

export function clock(sec) {
  const s = Math.max(0, Math.floor(Number(sec) || 0));
  const m = Math.floor(s / 60);
  const r = s % 60;
  return m + ':' + (r < 10 ? '0' : '') + r;
}

/** "just now", "3 minutes ago", "an hour ago", "5 hours ago", "a while ago". */
export function agoText(sec) {
  if (sec == null || !Number.isFinite(Number(sec))) return '';
  const s = Math.max(0, Math.floor(Number(sec)));
  if (s < 60) return 'just now';
  const m = Math.floor(s / 60);
  if (m < 60) return m === 1 ? 'a minute ago' : m + ' minutes ago';
  const h = Math.floor(m / 60);
  if (h < 24) return h === 1 ? 'an hour ago' : h + ' hours ago';
  return 'a while ago';
}

/** A stable two-colour gradient per name, so a letter avatar reads as the same person. */
const SWATCH = ['#ff5fa2', '#b99cff', '#5fffd0', '#5fd8ff', '#ffcf6b'];
export function swatchFor(name) {
  let h = 0;
  const s = String(name || '');
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
  return [SWATCH[h % SWATCH.length], SWATCH[(h + 2) % SWATCH.length]];
}

/* ---------------------------------------------------------------- ticker */

/**
 * run() now, then every `intervalMs` while `isActive()` says so. `bump()` runs
 * it at once (a switch change) and restarts the wait. `stop()` is final.
 * Timers are injectable so the selftest can drive it without waiting.
 */
export function createTicker({ run, intervalMs, isActive = () => true,
  setTimer = (fn, ms) => setTimeout(fn, ms), clearTimer = (t) => clearTimeout(t) } = {}) {
  let t = null;
  let stopped = false;
  let runs = 0;

  function fire() {
    if (stopped) return;
    let active = true;
    try { active = !!isActive(); } catch (_e) { active = true; }
    if (active) {
      runs++;
      try { const p = run(); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch (_e) { /* a tick never throws */ }
    }
    schedule();
  }
  function schedule() {
    if (stopped) return;
    if (t !== null) clearTimer(t);
    t = setTimer(fire, intervalMs);
  }
  return {
    start() { if (!stopped && t === null) fire(); return this; },
    bump() { if (!stopped) fire(); },
    stop() { stopped = true; if (t !== null) clearTimer(t); t = null; },
    get runs() { return runs; },
    get stopped() { return stopped; },
  };
}
