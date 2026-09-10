/* ============================================================================
 * backroom/kit/api.js - the cashier's telephone.
 *
 * The page never dials the proxy. It says one thing up the seam and waits for
 * one thing back (BACKROOM-CONTRACT §3):
 *
 *   up    { type:'casino-request', reqId, op:'status'|'cage'|'play', body, localDay }
 *   down  { type:'casino-result',  reqId, ok, status, body }
 *
 * Every answer this module hands back has the SAME shape, win or lose or never
 * arrived: `{ ok, status, body }`. A caller therefore never has to write a
 * try/catch around a promise that might reject, and the floor can render a dark
 * cage instead of throwing. That is the whole reason this file exists: a room
 * with no line to the counter is still a room.
 *
 * There is no client-side chip arithmetic here on purpose (LEDGER TRUTH). The
 * server resolves; this file only carries the question and the receipt.
 * ==========================================================================*/

/** Every request gets this long before the cage goes dark. Eight seconds is the
 *  wallet's own patience: long enough for a cold server, short enough that a
 *  player standing at the counter does not think the app has died. */
const TIMEOUT_MS = 8000;

/** The trigger list is a smaller favour and a host that has no trigger store
 *  answers instantly or not at all, so it waits a shorter beat. */
const TRIGGER_TIMEOUT_MS = 2500;

/** The offline answer, minted fresh each time so nobody can mutate a shared one. */
function offline(reason) {
  return { ok: false, status: 0, body: { reason: reason || 'offline' } };
}

/**
 * The wire's id shape, and it is not a taste: `ArcademyWalletSyncService.IdShape`
 * is `^[A-Za-z0-9_-]{8,64}$` and every id the host mints is a GUID in "N" form,
 * i.e. 32 hex characters. A cageId or playId is an IDEMPOTENCY KEY - the server
 * remembers it and replays the original result - so it has to be unguessable
 * and it has to survive a retry unchanged. `crypto.randomUUID` where there is
 * one, `getRandomValues` where there is not, and a clock+counter floor so a
 * hardened webview with neither still mints something legal rather than
 * throwing at the counter.
 */
let idSeq = 0;
export function mintId() {
  const c = (typeof globalThis !== 'undefined') ? globalThis.crypto : null;
  try {
    if (c && typeof c.randomUUID === 'function') return c.randomUUID().replace(/-/g, '');
  } catch { /* fall through */ }
  try {
    if (c && typeof c.getRandomValues === 'function') {
      const b = new Uint8Array(16);
      c.getRandomValues(b);
      let s = '';
      for (let i = 0; i < b.length; i++) s += b[i].toString(16).padStart(2, '0');
      return s;
    }
  } catch { /* fall through */ }
  idSeq += 1;
  const rnd = Math.floor(Math.random() * 0xFFFFFFFF).toString(16).padStart(8, '0');
  return ('bk' + Date.now().toString(16) + rnd + idSeq.toString(16)).slice(0, 32);
}

/** 'yyyy-mm-dd' in LOCAL time. The same six lines shell/bugle.js and
 *  shell/corkboard.js carry, copied rather than imported because the kit does
 *  not reach into the shell (vendoring law) - and a date STAMP on this page is
 *  always local (trap 8), which is what the free scratcher's day is bounded by. */
export function localDay(when) {
  const d = (when instanceof Date) ? when : new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return y + '-' + m + '-' + day;
}

/**
 * createCasinoApi({ send, listen, log }) -> the counter's line.
 *
 * `send(msg)` and `listen(type, fn) -> unsubscribe` are the two things the
 * shell hands down. EITHER ONE MISSING is not an error: every call answers
 * `offline` on the next tick and the floor draws the cage dark. Rigs stub ctx
 * without them, and so does a web host that has not shipped the relay yet.
 */
export function createCasinoApi(opts) {
  const o = opts || {};
  const send = (typeof o.send === 'function') ? o.send : null;
  const listen = (typeof o.listen === 'function') ? o.listen : null;
  const say = (typeof o.log === 'function') ? o.log : () => {};
  const timeoutMs = Number(o.timeoutMs) > 0 ? Number(o.timeoutMs) : TIMEOUT_MS;
  const wired = !!(send && listen);

  let dead = false;
  let lastStatus = null;            // the last ok `status` body, for a cheap re-read
  const pending = new Map();        // reqId -> { resolve, timer }
  let offResult = null;
  let triggerWait = null;           // one in-flight triggers-request, shared

  if (wired) {
    try {
      offResult = listen('casino-result', (m) => {
        if (dead || !m) return;
        const rec = pending.get(m.reqId);
        if (!rec) return;                       // a stale or foreign receipt is not ours
        pending.delete(m.reqId);
        try { clearTimeout(rec.timer); } catch { /* noop */ }
        const answer = {
          ok: m.ok === true,
          status: Number(m.status) || 0,
          body: (m.body && typeof m.body === 'object') ? m.body : {},
        };
        if (answer.ok && rec.op === 'status') lastStatus = answer.body;
        rec.resolve(answer);
      });
    } catch (e) { say('backroom: casino-result listen failed: ' + ((e && e.message) || e)); }
  }

  /** One question, one receipt, never a rejection. */
  function ask(op, body) {
    if (dead || !wired) return Promise.resolve(offline('offline'));
    const reqId = mintId();
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        pending.delete(reqId);
        // A timeout is NOT a refusal. The stake may well have landed, which is
        // why every id is minted once and reused on a retry: the server answers
        // the original result and nothing moves twice.
        resolve(offline('timeout'));
      }, timeoutMs);
      pending.set(reqId, { resolve, timer, op });
      try {
        send({ type: 'casino-request', reqId, op, body: body || {}, localDay: localDay() });
      } catch (e) {
        pending.delete(reqId);
        try { clearTimeout(timer); } catch { /* noop */ }
        say('backroom: casino-request send failed: ' + ((e && e.message) || e));
        resolve(offline('offline'));
      }
    });
  }

  return {
    /** Is there a line at all? The floor asks this before it dresses the cage. */
    wired: () => wired && !dead,

    /** The whole floor in one frame: chips, sparkle, pot, tree, config, hand. */
    status: () => ask('status', {}),

    /**
     * Sparkle in, chips out, one way. `cageId` is minted here so a caller
     * cannot forget it, and it is handed back on the answer so a retry can
     * carry the SAME one (idempotency, contract §1).
     */
    cage(sparkle, cageId) {
      const n = Math.round(Number(sparkle) || 0);
      const id = cageId ? String(cageId) : mintId();
      return ask('cage', { sparkle: n, cageId: id }).then((r) => Object.assign({ cageId: id }, r));
    },

    /** A stake at a machine. Same idempotency story as the cage. */
    play(machine, stake, input, playId) {
      const id = playId ? String(playId) : mintId();
      return ask('play', {
        machine: String(machine || ''),
        stake: Math.round(Number(stake) || 0),
        playId: id,
        input: (input && typeof input === 'object') ? input : {},
      }).then((r) => Object.assign({ playId: id }, r));
    },

    /** The last `status` body that came back ok, or null. Read, never written to. */
    last: () => lastStatus,

    /** Remember a status body the floor already has (the open frame), so a
     *  machine mounted a second later does not have to ask again. */
    seed(body) { if (body && typeof body === 'object') lastStatus = body; },

    /**
     * The player's own trigger phrases, raw. A host with no trigger store
     * answers `[]` and the kit's authored list takes over - which is why this
     * resolves to an array and never to an error.
     */
    triggers() {
      if (dead || !wired) return Promise.resolve([]);
      if (triggerWait) return triggerWait;
      triggerWait = new Promise((resolve) => {
        let off = null;
        let done = false;
        const finish = (list) => {
          if (done) return;
          done = true;
          try { if (off) off(); } catch { /* noop */ }
          try { clearTimeout(timer); } catch { /* noop */ }
          resolve(Array.isArray(list) ? list : []);
        };
        const timer = setTimeout(() => finish([]), TRIGGER_TIMEOUT_MS);
        try { off = listen('triggers-result', (m) => finish(m && m.triggers)); }
        catch { finish([]); return; }
        try { send({ type: 'triggers-request' }); }
        catch (e) { say('backroom: triggers-request failed: ' + ((e && e.message) || e)); finish([]); }
      });
      return triggerWait;
    },

    localDay,
    mintId,

    /** Every timer cleared, every waiter answered. Safe from any road, twice. */
    destroy() {
      if (dead) return;
      dead = true;
      for (const [, rec] of pending) {
        try { clearTimeout(rec.timer); } catch { /* noop */ }
        try { rec.resolve(offline('closed')); } catch { /* noop */ }
      }
      pending.clear();
      try { if (offResult) offResult(); } catch { /* noop */ }
      offResult = null;
      triggerWait = null;
    },
  };
}

export default createCasinoApi;
