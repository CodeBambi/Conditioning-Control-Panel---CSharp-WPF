/* ============================================================================
 * ui/stake.js - PvP stakes on a Goon match (owner, 2026-09-28).
 *
 * A player may put TIME (Chaster lock time) or SPARKLES on a real 1v1, picked
 * in the lobby before the start. Each side stakes against the HOUSE, never the
 * opponent: SP never passes between players. The server settles; the desktop
 * host books a lost time stake on the loser's own Chaster tab. This page only
 * picks, shows and reports.
 *
 * Rules this file holds:
 *   - PvP only. Practice, a standalone web page (no desktop host) and an
 *     anonymous guest seat never see the row (`stakeEligible`).
 *   - The row locks at Countdown and stays locked; the server locks too.
 *   - Only a match that runs to the clock settles. Mercy (Esc / tap out),
 *     abandon, draw, a disputed result or a missing claim are VOID on the
 *     server: leaving mid-match never costs anything.
 *   - Every number on the row comes from the host's `stake` frames (the server
 *     reply verbatim plus `op`). The fallback options below only draw the row
 *     until the first limits reply lands.
 *
 * Wire (page <-> GoonHostService, brief "PvP stakes"):
 *   page -> host {type:'stake-limits'} | {type:'stake-offer', match, kind, amount}
 *               | {type:'stake-state', match} | {type:'stake-state', match, lock:true}
 *               | {type:'stake-settle', match}
 *   host -> page {type:'stake', op:'limits'|'offer'|'state'|'settled', ok, game, match?, ...reply}
 * `stake-settle` asks the host (Services/Stakes/StakeBridge) to keep polling
 * until the match settles, book it, and answer `op:'settled'` (`gave_up` when
 * nothing settled in time). `limits` carries the host's `time_ok`.
 * `lock` (sent by BOTH seats at Countdown, server CONTRACT "goon lock") is passed
 * through to /v2/stakes/state; either seat locks the match for good.
 * ==========================================================================*/

import { el } from './router.js';
import { S } from './strings.js';

/** Fallback table, the brief's constants. The server's `limits.options` wins. */
export const STAKE_FALLBACK = Object.freeze({ time: Object.freeze([900, 1800]), sp: Object.freeze([5, 10, 25]) });

/** How often the lobby asks for the opponent's stake while it waits. */
export const LOBBY_POLL_MS = 4000;

/** Past this after the match ended with no settled reply, the recap says the stake came back. */
export const SETTLE_GIVE_UP_MS = 150000;

const KINDS = ['none', 'time', 'sp'];

/** {kind, amount} or null, from anything the wire hands us. None always reads amount 0. */
export function normStake(s) {
  if (!s || typeof s !== 'object') return null;
  const kind = KINDS.indexOf(s.kind) >= 0 ? s.kind : null;
  if (!kind) return null;
  if (kind === 'none') return { kind, amount: 0 };
  const amount = Math.floor(Number(s.amount) || 0);
  return amount > 0 ? { kind, amount } : null;
}

export function sameStake(a, b) {
  const x = normStake(a) || { kind: 'none', amount: 0 };
  const y = normStake(b) || { kind: 'none', amount: 0 };
  return x.kind === y.kind && x.amount === y.amount;
}

export function isStaked(s) {
  const n = normStake(s);
  return !!n && n.kind !== 'none';
}

/**
 * May this match carry a stake at all. Hosted by the desktop app, a real
 * opponent (not the practice bot), a signed-in account (not a `g_` seat), and
 * a room code the server knows.
 */
export function stakeEligible({ hosted, practice, identity, code } = {}) {
  if (!hosted || practice) return false;
  const id = identity || {};
  if (id.anonymous || !id.unifiedId) return false;
  return typeof code === 'string' && /^[A-Z0-9]{4,12}$/.test(code);
}

/**
 * The pills, in order: off, the time options (only when the host says time is
 * allowed: Chaster linked, tab on, no safety hold), then the SP options.
 */
export function stakeOptions(limits) {
  const l = limits || {};
  const o = l.options && typeof l.options === 'object' ? l.options : STAKE_FALLBACK;
  const nums = (a, fb) => (Array.isArray(a) ? a : fb)
    .map((v) => Math.floor(Number(v) || 0)).filter((v) => v > 0);
  const out = [{ kind: 'none', amount: 0 }];
  if (l.time_ok === true) for (const t of nums(o.time, STAKE_FALLBACK.time)) out.push({ kind: 'time', amount: t });
  for (const p of nums(o.sp, STAKE_FALLBACK.sp)) out.push({ kind: 'sp', amount: p });
  return out;
}

/** "off" / "15 min" / "10 ✦". */
export function stakeLabel(s) {
  const n = normStake(s);
  if (!n || n.kind === 'none') return S.stake.off;
  if (n.kind === 'time') return S.stake.min(Math.max(1, Math.round(n.amount / 60)));
  return S.stake.sp(n.amount);
}

/** The opponent chip: "them: 10 ✦", "them: no stake", or '' while unknown. */
export function themLine(them, known) {
  if (!known) return '';
  return isStaked(them) ? S.stake.them(stakeLabel(them)) : S.stake.themNone;
}

/** A refusal, in words. Unknown reasons read as "down for now", never a code. */
export function refusalLine(reason) {
  switch (reason) {
    case 'daily_cap': return S.stake.dailyCap;
    case 'pair_cap': return S.stake.pairCap;
    case 'insufficient_sp': return S.stake.noSp;
    case 'started': return S.stake.started;
    case 'bad_amount':
    case 'no_chaster': return S.stake.badAmount;
    default: return S.stake.down;
  }
}

/**
 * The recap line for a settled reply. `staked` is what WE put on it. Returns
 * {text, tone} or null when there is nothing to say (no stake). A lost time
 * stake says what the player's own tab booked (`booked_s`, from the host) when
 * the host said so, as chess does: the tab can refuse or cut the server's time_s.
 */
export function resultLine(settled, staked) {
  if (!isStaked(staked)) return null;
  if (!settled || typeof settled !== 'object') return { text: S.stake.settling, tone: 'pending' };
  const delta = Math.floor(Number(settled.sp_delta) || 0);
  const booked = settled.booked_s;
  const timeS = Math.floor(typeof booked === 'number' && Number.isFinite(booked) ? booked : (Number(settled.time_s) || 0));
  if (settled.result === 'won') {
    return delta > 0 ? { text: S.stake.wonSp(delta), tone: 'won' } : { text: S.stake.returned, tone: 'void' };
  }
  if (settled.result === 'lost') {
    if (timeS > 0) return { text: S.stake.lostTime(Math.max(1, Math.round(timeS / 60))), tone: 'lost' };
    const s = normStake(staked);
    const lostSp = delta < 0 ? -delta : (s && s.kind === 'sp' ? s.amount : 0);
    return lostSp > 0 ? { text: S.stake.lostSp(lostSp), tone: 'lost' } : { text: S.stake.lost, tone: 'lost' };
  }
  return { text: S.stake.returned, tone: 'void' };
}

/* ----------------------------------------------------------------- claim */

/**
 * The result claim both pages post to /v2/goon/ledger when the result
 * finalises. The server settles a stake only when both claims agree on a
 * match that ran to the clock. Kept to the fields both sides hold identically
 * after the countersign: survived_ms is each side's own clock and would make
 * every honest pair look disputed.
 * @returns {object|null} null when there is nothing claimable
 */
export function buildClaimBody({ session, result } = {}) {
  const s = session || {};
  const room = s.room || {};
  const id = s.identity || {};
  const r = result || null;
  if (!r || r.countsForLedger === false) return null;
  const role = room.role === 'host' ? 'host' : (room.role === 'guest' ? 'guest' : '');
  const code = String(room.code || '');
  const unifiedId = String(id.unifiedId || '');
  if (!role || !code || !unifiedId || id.anonymous) return null;
  const winner = r.winnerIsHost === true ? true : (r.winnerIsHost === false ? false : null);
  return {
    unified_id: unifiedId,
    code,
    token: String(room.token || ''),
    role,
    result: {
      end_reason: Math.floor(Number(r.endReason) || 0),
      winner_is_host: winner,
      host_score: Math.max(0, Math.floor(Number(r.hostScore) || 0)),
      guest_score: Math.max(0, Math.floor(Number(r.guestScore) || 0)),
    },
  };
}

/** Post the claim. Resolves, never rejects. */
export async function submitClaim({ session, result, post } = {}) {
  const body = buildClaimBody({ session, result });
  if (!body || typeof post !== 'function') return { ok: false, status: 0 };
  let res = null;
  try { res = await post('/v2/goon/ledger', body); } catch (_e) { res = null; }
  const status = res && Number.isFinite(res.status) ? res.status | 0 : 0;
  return { ok: status >= 200 && status < 300, status };
}

/* ---------------------------------------------------------------- client */

/**
 * The page's half of the stake bridge. One per page. `receive` is fed from
 * boot's single `bridge.on('stake', ...)` handler. State is per match code.
 */
export function createStakeClient({ send, logger = null, now = () => Date.now() } = {}) {
  const listeners = new Set();
  let limits = null;
  let limitsAsked = false;
  const byMatch = new Map();   // code -> {you, them, known, locked, settled, pending, error, endedAt}

  const entry = (code) => {
    let e = byMatch.get(code);
    if (!e) {
      e = {
        you: null, them: null, known: false, locked: false, lockSent: false, settled: null,
        booked: undefined,   // seconds the player's own tab took for a lost time stake, when the host said
        pending: false, error: '', endedAt: 0, final: false, lastOffer: null, retried: false,
      };
      byMatch.set(code, e);
    }
    return e;
  };
  const emit = () => { for (const fn of listeners) { try { fn(); } catch (_e) { /* a painter never breaks the wire */ } } };
  const tx = (m) => { try { send(m); } catch (e) { logger?.warn?.('stake send threw: ' + ((e && e.message) || e)); } };
  let current = '';   // the one match a reply without `match` belongs to

  return {
    get limits() { return limits; },
    enabled() { return !!(limits && limits.ok === true && limits.enabled !== false); },
    get(code) { return entry(code); },
    onChange(fn) { listeners.add(fn); return () => listeners.delete(fn); },

    askLimits(force = false) {
      if (limitsAsked && !force) return;
      limitsAsked = true;
      tx({ type: 'stake-limits' });
    },
    askState(code) { current = code; tx({ type: 'stake-state', match: code }); },
    offer(code, stake) {
      const n = normStake(stake);
      const e = entry(code);
      if (!n || e.locked || e.pending) return false;
      current = code;
      e.pending = true;
      e.error = '';
      e.lastOffer = n;
      e.retried = false;
      emit();
      tx({ type: 'stake-offer', match: code, kind: n.kind, amount: n.amount });
      return true;
    },
    /** The row locks here, at Countdown, and tells the server so. Sent once per match,
     *  staked or not: this seat's lock also locks the OTHER seat's stake. */
    lock(code) {
      const e = entry(code);
      if (!e.lockSent) { e.lockSent = true; tx({ type: 'stake-state', match: code, lock: true }); }
      if (!e.locked) { e.locked = true; emit(); }
    },
    /** Match over: ask the host to watch it until it settles (only if we staked). */
    finish(code) {
      const e = entry(code);
      e.locked = true;
      if (!e.endedAt) e.endedAt = now();
      if (isStaked(e.you) && !e.final) {
        e.final = true;
        current = code;
        tx({ type: 'stake-settle', match: code });
      }
      emit();
    },
    /** Settled, or given up on: the recap stops saying "settling". */
    settledFor(code) {
      const e = entry(code);
      if (e.settled) return e.booked !== undefined ? { ...e.settled, booked_s: e.booked } : e.settled;
      if (e.endedAt && now() - e.endedAt > SETTLE_GIVE_UP_MS) return { result: 'void', local: true };
      return null;
    },

    receive(m) {
      if (!m || typeof m !== 'object') return;
      const op = String(m.op || '');
      if (op === 'limits') {
        limits = m;
        emit();
        return;
      }
      const code = typeof m.match === 'string' && m.match ? m.match : current;
      if (!code) return;
      const e = entry(code);
      if (op === 'offer') {
        if (m.ok !== true && m.reason === 'busy' && e.lastOffer && !e.retried && !e.locked) {
          // A server lock was held for a moment: one quiet retry, then words.
          e.retried = true;
          const again = e.lastOffer;
          setTimeout(() => tx({ type: 'stake-offer', match: code, kind: again.kind, amount: again.amount }), 1000);
          return;
        }
        e.pending = false;
        e.retried = false;
        if (m.ok === true) {
          e.you = normStake(m.stake) || { kind: 'none', amount: 0 };
          e.error = '';
        } else {
          e.error = refusalLine(m.reason);
          if (m.reason === 'started') e.locked = true;
          if (m.reason === 'disabled' || m.reason === 'signin') limits = { ok: false, reason: m.reason };
        }
        emit();
        return;
      }
      if (op === 'state' || op === 'settled') {
        if (m.gave_up === true) {
          // The host watched ~2 min and nothing settled: void locally, nothing booked.
          if (!e.settled) e.settled = { result: 'void', local: true };
        } else if (m.ok === true) {
          if (!e.pending && m.you !== undefined) e.you = normStake(m.you) || e.you;
          if (m.them !== undefined) { e.them = normStake(m.them); e.known = true; }
          if (m.locked === true) e.locked = true;
          if (m.settled && typeof m.settled === 'object') e.settled = m.settled;
          if (typeof m.booked_s === 'number' && Number.isFinite(m.booked_s)) e.booked = m.booked_s;
        }
        emit();
      }
    },
  };
}

/* ------------------------------------------------------------------- row */

/**
 * The lobby row: `stake [off] [15 min] [30 min] [5 ✦] [10 ✦] [25 ✦]`, the
 * opponent's chip under it, and one quiet status line. Hidden until the host
 * says stakes are on for this account.
 */
export function mountStakeRow({ ledger, client, code, audio = null, pollMs = LOBBY_POLL_MS }) {
  const pills = el('div', { class: 'gg-stake-pills', role: 'group', 'aria-label': S.stake.label });
  const chip = el('span', { class: 'gg-stake-them', text: '' });
  const note = el('p', { class: 'gg-stake-note', text: '', role: 'status' });
  const node = el('div', { class: 'gg-stake', hidden: true }, [
    el('div', { class: 'gg-stake-row' }, [el('span', { class: 'gg-row-label', text: S.stake.label }), pills]),
    el('div', { class: 'gg-stake-meta' }, [chip]),
    note,
  ]);

  let drawnKey = '';
  function paint() {
    const on = client.enabled();
    node.hidden = !on;
    if (!on) return;
    const e = client.get(code);
    const opts = stakeOptions(client.limits);
    const key = opts.map((o) => o.kind + o.amount).join(',');
    if (key !== drawnKey) {
      drawnKey = key;
      pills.replaceChildren();
      for (const o of opts) {
        const b = el('button', { type: 'button', class: 'gg-stake-pill is-' + o.kind, text: stakeLabel(o) });
        b.__stake = o;
        ledger.listen(b, 'click', (ev) => {
          ev.preventDefault();
          if (b.disabled) return;
          try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
          client.offer(code, o);
        });
        pills.appendChild(b);
      }
    }
    const mine = e.you || { kind: 'none', amount: 0 };
    for (const b of pills.children) {
      const on2 = sameStake(b.__stake, mine);
      b.classList.toggle('is-on', on2);
      b.setAttribute('aria-pressed', on2 ? 'true' : 'false');
      b.disabled = e.locked || e.pending;
    }
    node.classList.toggle('is-locked', !!e.locked);
    chip.textContent = themLine(e.them, e.known);
    chip.hidden = !chip.textContent;
    note.textContent = e.error
      || (e.locked && isStaked(mine) ? S.stake.locked : '')
      || (mine.kind === 'time' && !e.locked ? S.stake.timeNote : '');
  }

  ledger.sub(client.onChange(paint));
  client.askLimits();
  client.askState(code);
  ledger.interval(() => { if (client.enabled() && !client.get(code).locked) client.askState(code); }, pollMs);
  paint();
  return { node, paint };
}
