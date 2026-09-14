/* ============================================================================
 * backroom/smoke/mock-bell.js - a stand-in for `/v2/backroom/bell/*` for the
 * room's dev harness and the node tests (CONTRACT 10.16.B).
 *
 *   GET  state       -> { ok, open, entries[<=20, newest first], optIn, visit:{day, comp},
 *                         jackpot:{ mustHit } }
 *   POST opt {on}    -> { ok, optIn }  (bad_input when `on` is not a boolean)
 *   too_fast         -> HTTP 200 { ok:false, reason:'too_fast', retryInMs }, the wheel's soft convention
 *   closed           -> HTTP 403 { ok:false, reason:'closed' }; `state` still answers with open:false
 *
 * `handle()` resolves like the host relay: { ok:true, status, body } or a host
 * refusal { ok:false, reason }. NOT the server: the shapes and the refusals the
 * page must cope with, nothing else. `reads` counts what the page asked for, so
 * a check can prove the bell is never polled while seated.
 * ==========================================================================*/

const DAY_MS = 86400000;

/** The fixture the room harness shows: one of every line the bell prints, newest first. */
export const BELL_FIXTURE = Object.freeze([
  Object.freeze({ t: -20 * 1000, station: 'slot', line: 'spiral3', pay: 10, name: null }),
  Object.freeze({ t: -4 * 60 * 1000, station: 'wheel', line: 'jackpot', pay: 1000, name: 'Rosewood' }),
  Object.freeze({ t: -47 * 60 * 1000, station: 'cards', line: 'blackjack', pay: 3, name: null }),
  Object.freeze({ t: -3 * 3600 * 1000, station: 'roulette', line: 'wake', pay: 24, name: '  a  very   long   display name that runs on  ' }),
  Object.freeze({ t: -2 * DAY_MS, station: 'slot', line: 'emi3', pay: 400, name: null }),
  Object.freeze({ t: -3 * DAY_MS, station: 'wheel', line: 'deep', pay: 40, name: null }),
  Object.freeze({ t: -4 * DAY_MS, station: 'nope', line: 'nope', pay: 1, name: null }),   // never printed
]);

/**
 * @param {Object} o { now(), open, optIn, mustHit, entries (offsets in ms, as the fixture), comp }
 */
export function createBellMock(o = {}) {
  const now = typeof o.now === 'function' ? o.now : () => Date.now();
  const st = {
    open: o.open !== false,
    optIn: o.optIn === true,
    mustHit: o.mustHit === true,
    comp: Number(o.comp) || 0,
    rows: (Array.isArray(o.entries) ? o.entries : BELL_FIXTURE).map((e) => ({ ...e })),
  };
  const reads = { state: 0, opt: 0 };
  const faults = [];

  /** Offsets on the fixture are relative to now; an absolute `t` is left alone. */
  const entries = () => st.rows.slice(0, 20).map((e) => ({ ...e, t: e.t <= 0 ? now() + e.t : e.t }));

  function state() {
    return { ok: true, open: st.open, entries: entries(), optIn: st.optIn,
             visit: { day: Math.floor(now() / DAY_MS), comp: st.comp },
             jackpot: { mustHit: st.mustHit } };
  }

  function opt(body) {
    const on = body && body.on;
    if (typeof on !== 'boolean') return { ok: false, reason: 'bad_input' };
    st.optIn = on;
    return { ok: true, optIn: st.optIn };
  }

  async function handle(op, body = {}) {
    if (op === 'state') reads.state++;
    if (op === 'opt') reads.opt++;
    const f = faults.find((x) => x.op === op && x.times > 0);
    if (f) {
      f.times--;
      if (f.reason === 'closed') return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
      if (f.reason === 'too_fast') return { ok: true, status: 200, body: { ok: false, reason: 'too_fast', retryInMs: 5000 } };
      return { ok: false, status: 0, reason: f.reason };   // host refusals: timeout, offline
    }
    // A shut door still answers `state` with open:false so the room can show the sign.
    if (!st.open && op !== 'state') return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
    if (op === 'state') return { ok: true, status: 200, body: state() };
    if (op === 'opt') return { ok: true, status: 200, body: opt(body) };
    return { ok: false, status: 0, reason: 'bad_op' };
  }

  return {
    handle, reads, state: () => st,
    /** Next `times` calls to `op` fail with `reason` ('closed', 'too_fast', 'timeout', 'offline'). */
    fail(op, reason, times = 1) { faults.push({ op, reason, times }); },
    setOpen(v) { st.open = !!v; },
    setMustHit(v) { st.mustHit = !!v; },
    /** A new big win lands at the top of the list, newest first. */
    push(entry) { st.rows.unshift({ t: 0, name: null, pay: 0, ...entry }); st.rows.length = Math.min(st.rows.length, 20); },
  };
}
