/* ============================================================================
 * ui/rivalry.js - the local win / loss / draw record per opponent.
 *
 * "you 3 - 2 them" in the lobby once a peer is known, and again on the recap.
 * Local only: nothing here is sent, and nothing a peer sends can write here
 * except through rivalKey(), which is the one door.
 *
 * WHO IS "THE SAME OPPONENT". The hello frame carries a display name and
 * nothing more stable (no account id reaches the page, by design: the host
 * keeps ids to itself). So the key is the sanitized, case-folded display name.
 * Two people with the same name share a record and a rename starts a fresh
 * one. That is honest for a cosmetic tally and it is the only identity the
 * page has. The raw wire string is NEVER a storage key: rivalKey() strips
 * control and markup characters, folds case and whitespace, and caps length.
 *
 * Pure where it matters (test/selftest-rivalry.js): the storage is injected,
 * defaulting to localStorage behind try/catch.
 * ==========================================================================*/

import { GoonEndReason } from '../core/contracts.js';

export const RIVALRY_KEY = 'goon.rivalry.v1';
/** Opponents remembered. The oldest-seen falls off past this. */
export const RIVALRY_MAX = 200;
const NAME_MAX = 32;
const COUNT_CAP = 99999;

function defaultStore() {
  try { return (typeof localStorage !== 'undefined') ? localStorage : null; } catch (_e) { return null; }
}

/** Control, zero-width, bidi-override and markup characters: never shown, never keyed. */
function isHiddenChar(cp) {
  return cp <= 0x1f || (cp >= 0x7f && cp <= 0x9f)
    || (cp >= 0x200b && cp <= 0x200f) || (cp >= 0x2028 && cp <= 0x202e)
    || (cp >= 0x2060 && cp <= 0x206f) || cp === 0xfeff
    || cp === 0x3c || cp === 0x3e;
}

/**
 * A display name off the wire, made safe to show back.
 * Control characters and angle brackets go, whitespace collapses, length caps.
 */
export function cleanName(raw) {
  let s = '';
  try { s = String(raw == null ? '' : raw); } catch (_e) { return ''; }
  s = Array.from(s).filter((ch) => !isHiddenChar(ch.codePointAt(0))).join('');
  s = s.replace(/\s+/g, ' ').trim();
  return s.slice(0, NAME_MAX);
}

/** The storage key for an opponent, or '' when there is nothing to key on. */
export function rivalKey(raw) {
  let s = cleanName(raw);
  try { s = s.normalize('NFKC'); } catch (_e) { /* old engine: keep as is */ }
  s = s.toLowerCase().replace(/\s+/g, ' ').trim();
  if (!s) return '';
  return 'n:' + s;
}

/** 'w' | 'l' | 'd' for a finished match result, or null when it is not one to count. */
export function outcomeOf(result) {
  if (!result) return null;
  if (result.endReason === GoonEndReason.Abandon) return null;   // nobody played that out
  if (result.endReason === GoonEndReason.Draw || result.winnerIsHost == null) return 'd';
  return result.localWon ? 'w' : 'l';
}

function toCount(v) {
  const n = Math.floor(Number(v));
  return Number.isFinite(n) && n > 0 ? Math.min(n, COUNT_CAP) : 0;
}

/**
 * @param {{store?:object, now?:()=>number}} [o]
 */
export function createRivalry({ store = defaultStore(), now = () => Date.now() } = {}) {
  function readAll() {
    try {
      if (!store) return {};
      const raw = store.getItem(RIVALRY_KEY);
      if (!raw) return {};
      const obj = JSON.parse(raw);
      return (obj && typeof obj === 'object' && !Array.isArray(obj)) ? obj : {};
    } catch (_e) { return {}; }
  }

  function writeAll(all) {
    try {
      if (!store) return;
      const keys = Object.keys(all);
      if (keys.length > RIVALRY_MAX) {
        keys.sort((a, b) => (Number(all[a] && all[a].at) || 0) - (Number(all[b] && all[b].at) || 0));
        for (const k of keys.slice(0, keys.length - RIVALRY_MAX)) delete all[k];
      }
      store.setItem(RIVALRY_KEY, JSON.stringify(all));
    } catch (_e) { /* a full quota never breaks a recap */ }
  }

  function recordFor(name) {
    const key = rivalKey(name);
    if (!key) return { w: 0, l: 0, d: 0, known: false };
    const row = readAll()[key];
    if (!row || typeof row !== 'object') return { w: 0, l: 0, d: 0, known: false };
    const r = { w: toCount(row.w), l: toCount(row.l), d: toCount(row.d) };
    r.known = (r.w + r.l + r.d) > 0;
    return r;
  }

  /** Add one outcome. @returns the updated record, or null when nothing was written. */
  function note(name, outcome) {
    const key = rivalKey(name);
    if (!key || (outcome !== 'w' && outcome !== 'l' && outcome !== 'd')) return null;
    const all = readAll();
    const prev = (all[key] && typeof all[key] === 'object') ? all[key] : {};
    const row = { w: toCount(prev.w), l: toCount(prev.l), d: toCount(prev.d), name: cleanName(name), at: now() };
    row[outcome] = Math.min(row[outcome] + 1, COUNT_CAP);
    all[key] = row;
    writeAll(all);
    return { w: row.w, l: row.l, d: row.d, known: true };
  }

  /** The last few opponents, newest first: [{name, w, l, d, at}]. Local only. */
  function recent(limit = 3) {
    const all = readAll();
    const rows = [];
    for (const k of Object.keys(all)) {
      const r = all[k];
      if (!r || typeof r !== 'object') continue;
      const name = cleanName(r.name);
      if (!name) continue;
      rows.push({ name, w: toCount(r.w), l: toCount(r.l), d: toCount(r.d), at: Number(r.at) || 0 });
    }
    rows.sort((a, b) => b.at - a.at);
    return rows.slice(0, Math.max(0, limit | 0));
  }

  return { recordFor, note, recent };
}

/** "you 3 - 2 Sam", plus the draws when there are any. '' for a stranger. */
export function formatRecord(rec, name) {
  if (!rec || !rec.known) return '';
  const who = cleanName(name) || 'them';
  let s = 'you ' + rec.w + ' - ' + rec.l + ' ' + who;
  if (rec.d > 0) s += ', ' + rec.d + (rec.d === 1 ? ' draw' : ' draws');
  return s;
}

/* The recap can paint more than once per match (the countersignature lands
 * late), and a remount must not count the same match twice. One latch per
 * match object, dying with it. */
const settled = new WeakSet();

/**
 * Book a finished match exactly once: the rivalry row and nothing else.
 * @returns {{w,l,d,known}|null} the record after booking, or null if skipped.
 */
export function settleOnce(match, rivalry, { practice = false } = {}) {
  try {
    if (!match || typeof match !== 'object' || settled.has(match)) return null;
    const result = match.result;
    if (!result) return null;                 // not final yet: try again on the next paint
    settled.add(match);
    if (practice) return null;
    const outcome = outcomeOf(result);
    const name = match.opponent ? match.opponent.displayName : '';
    return outcome ? rivalry.note(name, outcome) : null;
  } catch (_e) { return null; }
}
