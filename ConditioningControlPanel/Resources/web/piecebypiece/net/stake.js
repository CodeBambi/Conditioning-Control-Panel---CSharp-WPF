/* ============================================================================
 * net/stake.js - PvP stakes on an online chess match.
 *
 * A player stakes TIME (Chaster lock time) or SPARKLES against the house, never
 * against the opponent. The pick is made in the lobby (it waits for the next
 * match) and can be changed on the found screen; the server locks it on the
 * first move. Leaving mid-match never costs: the server voids it. A finished
 * match spends the pick: the next one starts at Off and the player picks again
 * (owner, 2026-09-29), so a stake is never carried into a match unasked.
 *
 * The page never talks to /v2/stakes itself. Every call goes through the host
 * (Services/Stakes/StakeBridge.cs), which also books a lost time stake in C#:
 *
 *   page -> host  { type:'stake-limits' }
 *                 { type:'stake-offer', match, kind, amount }
 *                 { type:'stake-state', match }
 *                 { type:'stake-settle', match }        the match is over
 *   host -> page  { type:'stake', op, ...route reply }  op: limits|offer|state|settled
 *
 * Online PvP only: nothing here is ever called for a solo or hotseat game. A
 * page with no host (a plain browser) never hears a limits reply, so it stays
 * disabled and draws no row at all.
 *
 * The top half is pure (node-tested in smoke/stake-smoke.mjs); createStake is
 * the small controller the door holds.
 * ==========================================================================*/

export const DEFAULT_OPTIONS = Object.freeze({ time: [900, 1800], sp: [5, 10, 25] });

/** English, in case the host sends no labels. The host's are the stake_* loc keys. */
export const EN = Object.freeze({
  title: 'Stake', off: 'Off', minutes: '{0} min', sparkles: '{0} ✦',
  you: 'You', them: 'Them', locked: 'Locked', none: 'No stake',
  won_sp: '+{0} ✦', lost_time: '+{0} min on your lock', lost_sp: 'Stake lost',
  returned: 'Stake returned', pending: 'Settling the stake',
  refused_daily_cap: 'No more stakes today', refused_pair_cap: 'Already staked with them today',
  refused_insufficient_sp: 'Not enough ✦', refused_started: 'Too late, the game started',
  refused_signin: 'Sign in to stake', refused_other: 'Stake not taken',
});

export const NONE = Object.freeze({ kind: 'none', amount: 0 });
const STORE_KEY = 'pbp.stake.v1';
const POLL_MS = 3000;
const POLL_CAP_MS = 5 * 60 * 1000;

export function fill(tpl, n) { return String(tpl ?? '').replace('{0}', String(n)); }

/** A stake from the wire or storage, or null when it is not one. None normalises to amount 0. */
export function readStake(v) {
  if (!v || typeof v !== 'object') return null;
  const kind = String(v.kind || '').toLowerCase();
  if (kind === 'none') return { kind: 'none', amount: 0 };
  const amount = Math.round(Number(v.amount));
  if ((kind === 'time' || kind === 'sp') && Number.isFinite(amount) && amount > 0) return { kind, amount };
  return null;
}

export function sameStake(a, b) {
  const x = readStake(a) || NONE; const y = readStake(b) || NONE;
  return x.kind === y.kind && x.amount === y.amount;
}

export function isNone(s) { const x = readStake(s); return !x || x.kind === 'none'; }

/** The short label: "Off", "15 min", "10 ✦". */
export function stakeLabel(s, labels = EN) {
  const x = readStake(s) || NONE;
  const L = { ...EN, ...(labels || {}) };
  if (x.kind === 'time') return fill(L.minutes, Math.floor(x.amount / 60));
  if (x.kind === 'sp') return fill(L.sparkles, x.amount);
  return L.off;
}

/** The pills to draw: Off, then the time sizes when time is on offer, then the sparkle sizes. */
export function pills(options = DEFAULT_OPTIONS, timeOk = false) {
  const o = options || DEFAULT_OPTIONS;
  const list = [{ kind: 'none', amount: 0 }];
  if (timeOk) for (const t of (Array.isArray(o.time) ? o.time : DEFAULT_OPTIONS.time)) list.push({ kind: 'time', amount: t });
  for (const s of (Array.isArray(o.sp) ? o.sp : DEFAULT_OPTIONS.sp)) list.push({ kind: 'sp', amount: s });
  return list.filter((p) => readStake(p));
}

/** Is this pick one of the pills on offer. */
export function onOffer(pick, options, timeOk) {
  return pills(options, timeOk).some((p) => sameStake(p, pick));
}

/** A refusal reason as a short line. */
export function refusalText(reason, labels = EN) {
  const L = { ...EN, ...(labels || {}) };
  return L['refused_' + reason] || L.refused_other;
}

/**
 * The end card's line, or null when there is nothing to say (no stake).
 * `settled` is the server's { result, sp_delta, time_s }; `booked` the seconds
 * the host says actually landed on the tab (undefined when it did not say).
 */
export function resultText(you, settled, booked, labels = EN) {
  if (isNone(you) || !settled) return null;
  const L = { ...EN, ...(labels || {}) };
  const r = String(settled.result || '');
  if (r === 'won') {
    const d = Math.round(Number(settled.sp_delta) || 0);
    return d > 0 ? fill(L.won_sp, d) : L.returned;
  }
  if (r === 'lost') {
    const t = Number.isFinite(Number(booked)) ? Number(booked) : Number(settled.time_s) || 0;
    return t > 0 ? fill(L.lost_time, Math.round(t / 60)) : L.lost_sp;
  }
  return L.returned;
}

/**
 * Fold one host frame into the state. Pure: returns the next state. Frames for
 * another match are ignored, except limits, which are about the player.
 */
export function reduce(state, msg) {
  if (!msg || msg.type !== 'stake') return state;
  const s = { ...state };
  const forThis = !msg.match || !s.match || msg.match === s.match;
  switch (msg.op) {
    case 'limits': {
      s.enabled = msg.ok === true && msg.enabled !== false;
      if (msg.options && typeof msg.options === 'object') s.options = { ...DEFAULT_OPTIONS, ...msg.options };
      s.timeOk = msg.time_ok === true;
      if (msg.labels && typeof msg.labels === 'object') s.labels = { ...EN, ...msg.labels };
      // a remembered pick that is no longer on offer waits as Off
      if (!onOffer(s.pick, s.options, s.timeOk)) s.pick = { ...NONE };
      return s;
    }
    case 'offer': {
      if (!forThis) return s;
      s.busy = false;
      if (msg.ok === true) { s.you = readStake(msg.stake) || { ...NONE }; s.refusal = null; }
      else { s.refusal = msg.reason || 'other'; if (msg.reason === 'started') s.locked = true; }
      return s;
    }
    case 'state': {
      if (!forThis || msg.ok !== true) return s;
      s.you = readStake(msg.you) || { ...NONE };
      s.them = readStake(msg.them) || { ...NONE };
      s.locked = msg.locked === true || s.locked;
      if (msg.settled && typeof msg.settled === 'object') {
        s.settled = msg.settled;
        if (msg.booked_s !== undefined) s.booked = msg.booked_s;
        s.pending = false;
      }
      return s;
    }
    case 'settled': {
      if (msg.match && s.ended && msg.match !== s.ended) return s;
      s.pending = false;
      if (msg.gave_up) { if (!s.settled) s.settled = { result: 'void' }; return s; }
      if (msg.settled && typeof msg.settled === 'object') {
        s.settled = msg.settled;
        if (msg.you) s.you = readStake(msg.you) || s.you;
        if (msg.booked_s !== undefined) s.booked = msg.booked_s;
      }
      return s;
    }
    default: return s;
  }
}

export function initialState(pick = NONE) {
  return {
    enabled: false, timeOk: false, options: { ...DEFAULT_OPTIONS }, labels: { ...EN },
    pick: readStake(pick) || { ...NONE },
    match: null, ended: null, you: null, them: null, locked: false,
    settled: null, booked: undefined, pending: false, refusal: null, busy: false,
  };
}

// ---------------------------------------------------------------- the controller

function readPick() {
  try { return readStake(JSON.parse(window.localStorage.getItem(STORE_KEY) || 'null')) || NONE; } catch { return NONE; }
}
function writePick(p) {
  try { window.localStorage.setItem(STORE_KEY, JSON.stringify(p)); } catch { /* no shelf */ }
}

/**
 * @param {object} o
 * @param {(msg:object)=>void} o.post       postToHost
 * @param {(fn:(msg:object)=>void)=>(()=>void)} o.onMessage  onHostMessage
 */
export function createStake({ post, onMessage, store = true } = {}) {
  let state = initialState(store ? readPick() : NONE);
  const subs = new Set();
  let poll = 0; let pollUntil = 0;
  const send = (m) => { try { post && post(m); } catch { /* no host */ } };
  const emit = () => { for (const fn of [...subs]) { try { fn(state); } catch { /* keep going */ } } };

  function stopPoll() { if (poll) { clearInterval(poll); poll = 0; } }
  function startPoll() {
    stopPoll();
    pollUntil = Date.now() + POLL_CAP_MS;
    poll = setInterval(() => {
      if (!state.match || state.locked || !state.enabled || Date.now() > pollUntil) { stopPoll(); return; }
      send({ type: 'stake-state', match: state.match });
    }, POLL_MS);
  }

  function offer() {
    if (!state.enabled || !state.match || state.locked) return;
    if (sameStake(state.pick, state.you || NONE) && state.you) return;
    state = { ...state, busy: true };
    send({ type: 'stake-offer', match: state.match, kind: state.pick.kind, amount: state.pick.amount });
  }

  function hear(m) {
    if (!m || m.type !== 'stake') return;
    const was = state;
    state = reduce(state, m);
    // limits landed while a match waits for its offer
    if (m.op === 'limits' && !was.enabled && state.enabled && state.match && !isNone(state.pick)) offer();
    emit();
  }
  const off = onMessage ? onMessage(hear) : () => {};

  return {
    get state() { return state; },
    subscribe(fn) { subs.add(fn); return () => subs.delete(fn); },
    /** Ask the host what is on offer (and whether stakes are on at all). */
    limits() { send({ type: 'stake-limits' }); },
    /** The player tapped a pill. Held for the next match; offered at once to a live one. */
    choose(kind, amount) {
      const p = readStake({ kind, amount });
      if (!p || !onOffer(p, state.options, state.timeOk)) return;
      state = { ...state, pick: p, refusal: null };
      if (store) writePick(p);
      offer();
      emit();
    },
    /** A match was dealt: offer the pick and watch for the opponent's stake and the lock. */
    begin(matchId) {
      if (!matchId || state.match === matchId) return;
      state = { ...state, match: String(matchId), ended: null, you: null, them: null, locked: false, settled: null, booked: undefined, pending: false, refusal: null, busy: false };
      if (!state.enabled) { emit(); return; }
      if (!isNone(state.pick)) offer();
      send({ type: 'stake-state', match: state.match });
      startPoll();
      emit();
    },
    /** The match is over: the host polls until it settles, and books what is owed. */
    end(matchId) {
      const id = matchId || state.match;
      stopPoll();
      // the match spends the pick: the next one starts at Off
      const pick = state.pick;
      state = { ...state, pick: { ...NONE } };
      if (store && !isNone(pick)) writePick(NONE);
      if (!id || !state.enabled) { emit(); return; }
      const staked = !isNone(state.you) || (!state.you && !isNone(pick));
      state = { ...state, ended: String(id), pending: staked && !state.settled };
      if (staked) send({ type: 'stake-settle', match: String(id) });
      emit();
    },
    /** Back to the menu: forget the match (the pick stays). */
    clear() {
      stopPoll();
      state = { ...state, match: null, ended: null, you: null, them: null, locked: false, settled: null, booked: undefined, pending: false, refusal: null, busy: false };
      emit();
    },
    /** The result line for the end card, or null. */
    resultLine() {
      if (state.pending && !state.settled) return state.labels.pending;
      return resultText(state.you, state.settled, state.booked, state.labels);
    },
    /** For a harness with no host: fold a host frame in as if the host had sent it. */
    feed: hear,
    dispose() { stopPoll(); subs.clear(); try { off(); } catch { /* gone */ } },
  };
}
