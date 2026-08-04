// The P2P media-transfer protocol — the thing that puts the SENDER's own images and clips on the
// receiver's screen, over the second WebRTC data channel, with the server never touching a byte.
//
// PEER-SYMMETRIC: one instance per side does both jobs at once. Transfers in the two directions
// are fully independent (separate slots, separate tid spaces, separate byte budgets); the only
// shared resource is the SCTP association's congestion window, which is what the backpressure pump
// in `_pump` manages.
//
// TRANSPORT-AGNOSTIC by injection, not by import: the five bulk members of GoonTransportBase are
// handed in as functions, so this module has no idea whether it is driving a real RTCDataChannel,
// the in-process loopback pair, or a test stub. That is what makes the whole protocol runnable
// under node (test/selftest-transfer.js) — and NODE-IMPORT-SAFE: nothing here touches
// RTCPeerConnection, window, document or fetch, at module scope or anywhere else.
//
// WHY IT BYPASSES core/wire.js, stated once so nobody "fixes" it:
//  1. SIZE — a 16 KiB chunk plus its header exceeds MAX_WIRE_BYTES on both guards, and
//     base64-in-JSON would inflate 4/3 and cost a full encode/decode per chunk.
//  2. CATALOG — MessageFactories is closed and `parse()` drops unknown `t`. Adding xfer_* would put
//     nine new types into the C#-parity surface the vectors pin, and would make them ROUTABLE OVER
//     THE RELAY, which is the one thing this feature must never allow.
//  3. RESUME — the transport's resume buffer replays 128 serialized frames. A transfer needs
//     ack/offset resume, not replay.
//  4. PARITY — bypassing wire.js means the C# client, the vectors, the relay ring and the ledger
//     stay byte-identical. Zero parity risk is the point.
//
// FRAME TAXONOMY on the one bulk channel:
//   string       ONE control JSON object, {"t":"xfer_*","v":1,...}
//   ArrayBuffer  ONE data chunk: 8-byte little-endian header {tid uint32, offset uint32} + payload
//
// ORDERED + RELIABLE is assumed (the channel is created that way). Ordering is what guarantees
// `xfer_end` can never overtake the last chunk, and it deletes a whole race class for free.

/** Protocol version carried on every control frame. */
export const XFER_PROTO = 1;

/** Chunk header: uint32 tid, uint32 offset, little-endian. */
export const CHUNK_HEADER_BYTES = 8;
/** Body per chunk. Header + body = 16384, the historically interoperable SCTP message ceiling. */
export const CHUNK_BODY_BYTES = 16 * 1024 - CHUNK_HEADER_BYTES;   // 16376

/** Stop filling the channel above this. */
export const BULK_HIGH_WATER = 1 << 20;    // 1 MiB
/** `bufferedAmountLowThreshold` — the channel tells us when it drains to here. */
export const BULK_LOW_WATER = 1 << 18;     // 256 KiB
/** Receiver acks at most this often, by bytes received. */
export const ACK_EVERY_BYTES = 1 << 18;    // 256 KiB

/** Per artifact, both directions. */
export const MAX_ARTIFACT_BYTES = 24 * 1024 * 1024;
/**
 * Tighter cap for un-transcoded originals. An exempt original is by definition un-optimised (no
 * 720p ceiling, no re-encode), and 20 MB of that is ~40 s of dead time on a mediocre link.
 * Enforced by the SENDER's eligibility filter (net/mediaQueue.js) — the receiver cannot tell an
 * exempt original from a compressed artifact and does not try.
 */
export const MAX_EXEMPT_BYTES = 8 * 1024 * 1024;
/** Per match, per direction. */
export const MAX_SESSION_BYTES = 192 * 1024 * 1024;

export const MAX_CONCURRENT_IN = 1;
export const MAX_CONCURRENT_OUT = 1;

/** No accept/decline in this long -> cancel the offer and move on. */
export const OFFER_TIMEOUT_MS = 8000;
/** No ack progress in this long -> cancel; the queue requeues at the back. */
export const STALL_TIMEOUT_MS = 20000;
/** No `xfer_hello` in this long after the channel opens -> the peer does not transfer. Stay dormant. */
export const HELLO_TIMEOUT_MS = 3000;
/** Throughput budget: never spend longer than this on one artifact. */
export const MAX_XFER_MS = 90000;
/** Chunks for a tid we never accepted, before we say so. */
export const STRAY_CHUNK_LIMIT = 64;
/** `xfer:` tags a single payload may carry (FlashBurst is the only one that uses more than 1). */
export const XFER_TAGS_MAX = 3;

/** Pump/watchdog cadence. Chromium honours `bufferedamountlow`, but a missed event is a transfer
 *  that never finishes, and 50 ms of polling is cheaper than that failure mode. */
export const PUMP_WATCHDOG_MS = 50;

/** The only media types either side will carry. */
export const ACCEPT_MIME = Object.freeze(new Set([
  'video/mp4', 'video/webm',
  'image/png', 'image/jpeg', 'image/gif', 'image/webp',
]));

const SHA_RE = /^[0-9a-f]{64}$/;

/** Control verbs, as they appear in `t`. */
export const XferVerb = Object.freeze({
  Hello: 'xfer_hello',
  Offer: 'xfer_offer',
  Accept: 'xfer_accept',
  Decline: 'xfer_decline',
  Ack: 'xfer_ack',
  End: 'xfer_end',
  Done: 'xfer_done',
  Fail: 'xfer_fail',
  Cancel: 'xfer_cancel',
});

/**
 * Why an offer was refused. `have` is a SUCCESS — cross-session reuse, the receiver already holds
 * that artifact — and must not read as a failure anywhere in the code or the logs.
 */
export const XferDecline = Object.freeze({
  Have: 'have', Blocked: 'blocked', TooBig: 'too_big', BadMime: 'bad_mime',
  Busy: 'busy', Quota: 'quota', Off: 'off',
});

/** Why a transfer died after it started. */
export const XferFail = Object.freeze({
  HashMismatch: 'hash_mismatch', Magic: 'magic', TooBig: 'too_big',
  StoreFull: 'store_full', Blocked: 'blocked', Io: 'io',
});

/** Why a transfer was called off. */
export const XferCancel = Object.freeze({
  PeerGone: 'peer_gone', MatchOver: 'match_over', Superseded: 'superseded',
  Timeout: 'timeout', User: 'user', Stray: 'stray',
});

/**
 * The receiver-side store this module drives. Phase 5b implements it (exec/receivedStore.js, disk
 * via the host with an in-memory blob fallback); everything below is the whole contract.
 *
 * THE FIRST FIVE MEMBERS ARE SYNCHRONOUS AND MUST STAY THAT WAY. The offer gate has to resolve in
 * one turn: a round trip inside it would wedge the protocol's tightest path and would hand a
 * sender a timing oracle for "does the other player already have this / is it blocklisted".
 *
 * @typedef {object} ReceivedStore
 * @property {(sha:string) => boolean} has
 *   Is this artifact COMMITTED and usable right now? Answers `decline:'have'` — the cross-session
 *   reuse win. Primed before any screen mounts from the `received:[]` list on the manifest frame.
 * @property {(sha:string) => number} partialLength
 *   Bytes already durably written for an INCOMPLETE artifact, 0 when there is no partial. This is
 *   the resume offset, and it is the receiver's promise: everything below it is on disk.
 * @property {(sha:string, mime:string, bytes:number) => boolean} begin
 *   Open — or REOPEN, for a resume — the partial for `sha`. Returns false when the store refuses
 *   (full, io, quota); the caller answers `xfer_fail:'store_full'`. Must be idempotent: a resumed
 *   transfer calls it again with the same arguments and must keep the existing partial.
 * @property {(sha:string, offset:number, bytes:ArrayBuffer) => boolean} write
 *   Append at exactly `offset` (never a seek past the end). False means the write failed and the
 *   caller answers `xfer_fail:'io'`.
 * @property {(sha:string) => boolean} knowsBlocked
 *   LOCAL knowledge only — "do we already know this hash is blocklisted?". Unknown is NOT blocked:
 *   the real gate is at render time (media.acquireByTag), and a network lookup here would add
 *   latency to the hot path and leak a timing oracle. Optional; a store without it never blocks.
 * @property {(sha:string) => Promise<{ok:boolean, url?:string, bytes?:number, error?:string}>} commit
 *   Finish the artifact. THE HOST VERIFIES THE SHA HERE — the page's hash is a CLAIM — and sniffs
 *   the magic bytes; `error` uses the DtrhLoomStore vocabulary
 *   (`bad-name | too-big | bad-format | hash-mismatch | cap-reached | io-failed`), which this
 *   module maps onto the `xfer_fail` reasons. `hash-mismatch` also earns the sha a session-lived
 *   `neverAccept` entry.
 * @property {(sha:string) => void} abort
 *   Throw the partial away. Called on cancel, on terminal channel death, and on every failure.
 */

/**
 * One artifact the local side is willing to send. Supplied by the prepick queue, which gets it
 * from the compression side's `artifacts.open(sha)`.
 *
 * @typedef {object} XferSource
 * @property {string} sha256      lower-case hex, 64 chars — the wire identity, host-computed
 * @property {number} bytes       exact artifact length
 * @property {string} mime        must be in ACCEPT_MIME
 * @property {'image'|'video'} kind
 * @property {(offset:number, len:number) => (ArrayBuffer|Promise<ArrayBuffer>)} read
 * @property {number} [dur_ms]
 * @property {number} [w]
 * @property {number} [h]
 */

const noop = () => {};

/** Fires each listener, isolating throws — the transportBase discipline. */
function fire(listeners, arg, logger, tag, what) {
  for (const fn of Array.from(listeners)) {
    try { fn(arg); } catch (e) {
      if (logger && logger.error) logger.error(`[${tag}] ${what} listener threw: ${(e && e.message) || e}`);
    }
  }
}

function listenerSet() {
  const set = new Set();
  return {
    set,
    on(fn) {
      if (typeof fn !== 'function') return noop;
      set.add(fn);
      return () => set.delete(fn);
    },
  };
}

/** mime -> the `kind` family it must agree with. */
function familyOf(mime) {
  if (typeof mime !== 'string') return null;
  if (mime.startsWith('image/')) return 'image';
  if (mime.startsWith('video/')) return 'video';
  return null;
}

/** Build one 16 KiB-or-less chunk frame: {tid, offset} header + body. */
export function packChunk(tid, offset, body) {
  const src = body instanceof ArrayBuffer
    ? new Uint8Array(body)
    : new Uint8Array(body.buffer, body.byteOffset, body.byteLength);
  const out = new ArrayBuffer(CHUNK_HEADER_BYTES + src.length);
  const dv = new DataView(out);
  dv.setUint32(0, tid >>> 0, true);
  dv.setUint32(4, offset >>> 0, true);
  new Uint8Array(out, CHUNK_HEADER_BYTES).set(src);
  return out;
}

/**
 * Read a chunk frame back. Returns null for anything too short to be one — the wire is untrusted
 * and a truncated frame is a dropped frame, never a throw.
 * @returns {{tid:number, offset:number, body:ArrayBuffer}|null}
 */
export function unpackChunk(buf) {
  const ab = toArrayBuffer(buf);
  if (!ab || ab.byteLength < CHUNK_HEADER_BYTES) return null;
  const dv = new DataView(ab);
  return {
    tid: dv.getUint32(0, true),
    offset: dv.getUint32(4, true),
    body: ab.slice(CHUNK_HEADER_BYTES),
  };
}

/** ArrayBuffer | TypedArray | DataView -> ArrayBuffer. Anything else -> null. */
function toArrayBuffer(v) {
  if (v instanceof ArrayBuffer) return v;
  if (v && typeof v === 'object' && v.buffer instanceof ArrayBuffer
    && typeof v.byteLength === 'number' && typeof v.byteOffset === 'number') {
    return v.buffer.slice(v.byteOffset, v.byteOffset + v.byteLength);
  }
  return null;
}

/**
 * The protocol. One instance per side, per match.
 *
 * @param {object} o
 * @param {(data:ArrayBuffer|string) => boolean} o.sendBulk          transport.sendBulk, bound
 * @param {() => number} o.bulkBufferedAmount                        transport.bulkBufferedAmount
 * @param {() => number} o.bulkLowThreshold                          transport.bulkLowThreshold
 * @param {(fn:(raw:any) => void) => (() => void)} o.onBulkMessage   raw string | ArrayBuffer
 * @param {(fn:(state:string) => void) => (() => void)} o.onBulkStateChanged  'open' | 'closed' | 'low'
 * @param {ReceivedStore} o.store
 * @param {{knows:(sha:string)=>boolean, isBlocked:(sha:string)=>boolean}} [o.blocklist]
 * @param {object} [o.logger] console-shaped
 * @param {() => boolean} [o.acceptOffers] the receive-side gate: consent + caps + supportsBulk.
 *   Consulted FRESH on every offer, so a player who withdraws the toggle mid-match stops taking
 *   bytes on the very next offer.
 * @param {boolean} [o.isHost] mints ODD tids when true, EVEN when false — see `_nextTid`
 * @param {string} [o.tag] log prefix
 * @param {object} [o.timeouts] TEST AFFORDANCE (the relayTransport waitMs/minGapMs precedent):
 *   {offerMs, stallMs, helloMs, maxXferMs, watchdogMs}. Production passes nothing.
 * @param {object} [o.limits] TEST AFFORDANCE, same rules: {maxArtifactBytes, maxSessionBytes,
 *   maxConcurrentIn, maxConcurrentOut, ackEveryBytes, highWater}. The exported constants remain
 *   the protocol; this only lets a node test reach `quota`, `busy` and the high-water halt without
 *   moving 192 MiB. Production passes nothing and gets the constants verbatim.
 */
export function createMediaChannel(o = {}) {
  const log = o.logger || null;
  const tag = o.tag || 'GoonXfer';
  const store = o.store || null;
  const blocklist = o.blocklist || null;
  const acceptOffers = typeof o.acceptOffers === 'function' ? o.acceptOffers : (() => false);
  const isHost = !!o.isHost;

  const sendBulk = typeof o.sendBulk === 'function' ? o.sendBulk : (() => false);
  const bufferedAmount = typeof o.bulkBufferedAmount === 'function' ? o.bulkBufferedAmount : (() => 0);
  const lowThreshold = typeof o.bulkLowThreshold === 'function' ? o.bulkLowThreshold : (() => BULK_LOW_WATER);
  const subMessage = typeof o.onBulkMessage === 'function' ? o.onBulkMessage : (() => noop);
  const subState = typeof o.onBulkStateChanged === 'function' ? o.onBulkStateChanged : (() => noop);

  const T = o.timeouts || {};
  const OFFER_MS = T.offerMs ?? OFFER_TIMEOUT_MS;
  const STALL_MS = T.stallMs ?? STALL_TIMEOUT_MS;
  const HELLO_MS = T.helloMs ?? HELLO_TIMEOUT_MS;
  const XFER_MS = T.maxXferMs ?? MAX_XFER_MS;
  const WATCHDOG_MS = T.watchdogMs ?? PUMP_WATCHDOG_MS;

  const L = o.limits || {};
  const CAP_ARTIFACT = L.maxArtifactBytes ?? MAX_ARTIFACT_BYTES;
  const CAP_SESSION = L.maxSessionBytes ?? MAX_SESSION_BYTES;
  const CAP_IN = L.maxConcurrentIn ?? MAX_CONCURRENT_IN;
  const CAP_OUT = L.maxConcurrentOut ?? MAX_CONCURRENT_OUT;
  const ACK_EVERY = L.ackEveryBytes ?? ACK_EVERY_BYTES;
  const HIGH_WATER = L.highWater ?? BULK_HIGH_WATER;

  const info = (m) => { if (log && log.info) log.info(`[${tag}] ${m}`); };
  const warn = (m) => { if (log && log.warn) log.warn(`[${tag}] ${m}`); };
  const error = (m) => { if (log && log.error) log.error(`[${tag}] ${m}`); };

  const ev = {
    offered: listenerSet(),
    landed: listenerSet(),
    declined: listenerSet(),
    failed: listenerSet(),
    progress: listenerSet(),
  };

  /** @type {Map<number, object>} our sends, keyed by OUR tid */
  const outbound = new Map();
  /** @type {Map<number, object>} their sends, keyed by THEIR tid */
  const inbound = new Map();

  /** Shas we will never offer again this session (declined blocked / hash mismatch / repeat fail). */
  const neverOffer = new Set();
  /** Shas we will never accept again this session (host-side hash verification said no). */
  const neverAccept = new Set();

  let opened = false;
  let disposed = false;
  let dormant = false;          // hello never arrived — this peer does not transfer
  let helloSeen = false;
  let peerHello = null;
  let strays = 0;
  let strayLimitHits = 0;
  let sessionBytesIn = 0;
  let sessionBytesOut = 0;
  let quotaRefused = false;     // they said `quota` once — stop offering for the rest of the match
  let tidCounter = 0;
  let helloTimer = null;
  let watchdogTimer = null;
  let lastRateBps = 0;
  let unsubMessage = noop;
  let unsubState = noop;

  /**
   * Tids are unique per SENDER, and the two senders' spaces are made DISJOINT: host mints odd,
   * guest even. `xfer_cancel` is the one verb either side may send about either direction, so
   * without disjoint spaces "cancel tid 1" would be ambiguous the instant both sides had a
   * transfer running — which is the bidirectional case the design explicitly supports.
   */
  function nextTid() {
    tidCounter += 1;
    return isHost ? (tidCounter * 2 - 1) : (tidCounter * 2);
  }

  // ---------------------------------------------------------------- control frames

  function sendControl(msg) {
    if (!opened) return false;
    let json;
    try { json = JSON.stringify(msg); } catch (e) {
      error(`could not serialize ${msg && msg.t}: ${(e && e.message) || e}`);
      return false;
    }
    return sendBulk(json) === true;
  }

  function sendHello() {
    sendControl({
      t: XferVerb.Hello,
      v: XFER_PROTO,
      proto: XFER_PROTO,
      max_artifact_bytes: CAP_ARTIFACT,
      max_session_bytes: CAP_SESSION,
      max_concurrent: CAP_IN,
      accepts: Array.from(ACCEPT_MIME),
    });
  }

  // ---------------------------------------------------------------- timers

  function armHelloTimeout() {
    clearHelloTimeout();
    if (helloSeen) return;
    helloTimer = setTimeout(() => {
      helloTimer = null;
      if (disposed || helloSeen) return;
      dormant = true;
      info(`no ${XferVerb.Hello} within ${HELLO_MS}ms — peer does not transfer; dormant for the match`);
    }, HELLO_MS);
    if (helloTimer && typeof helloTimer.unref === 'function') helloTimer.unref();
  }

  function clearHelloTimeout() {
    if (!helloTimer) return;
    clearTimeout(helloTimer);
    helloTimer = null;
  }

  /** Starts the pump/deadline watchdog when there is work; stops itself when there is not. */
  function arm() {
    if (disposed || watchdogTimer) return;
    if (outbound.size === 0 && inbound.size === 0) return;
    watchdogTimer = setTimeout(onWatchdog, WATCHDOG_MS);
    if (watchdogTimer && typeof watchdogTimer.unref === 'function') watchdogTimer.unref();
  }

  function onWatchdog() {
    watchdogTimer = null;
    if (disposed) return;
    try { checkDeadlines(); } catch (e) { error(`watchdog: ${(e && e.message) || e}`); }
    for (const slot of Array.from(outbound.values())) {
      if (slot.state === 'sending') pump(slot);
    }
    arm();
  }

  function checkDeadlines() {
    const now = Date.now();

    for (const slot of Array.from(outbound.values())) {
      if (slot.state === 'paused') continue;         // the channel is down; deadlines are frozen
      if (slot.state === 'offered' && now - slot.offeredAtMs > OFFER_MS) {
        cancelOutbound(slot, XferCancel.Timeout, `no answer to the offer in ${OFFER_MS}ms`);
        continue;
      }
      if (slot.state === 'sending') {
        if (now - slot.lastProgressAtMs > STALL_MS) {
          cancelOutbound(slot, XferCancel.Timeout, `no ack progress in ${STALL_MS}ms`);
          continue;
        }
        if (now - slot.startedAtMs > XFER_MS) {
          cancelOutbound(slot, XferCancel.Timeout, `over the ${XFER_MS}ms throughput budget`);
        }
      }
    }

    for (const slot of Array.from(inbound.values())) {
      if (slot.paused) continue;
      if (now - slot.lastProgressAtMs > STALL_MS) {
        warn(`inbound tid ${slot.tid} stalled — dropping the partial`);
        try { store.abort(slot.sha256); } catch (_e) { /* best effort */ }
        inbound.delete(slot.tid);
        emitFailed('in', slot, XferFail.Io);
      }
    }
  }

  // ---------------------------------------------------------------- events out

  const emitOffered = (direction, slot) => fire(ev.offered.set, {
    direction, tid: slot.tid, sha256: slot.sha256, bytes: slot.bytes, mime: slot.mime, kind: slot.kind,
  }, log, tag, 'offered');

  const emitDeclined = (direction, slot, why) => fire(ev.declined.set, {
    direction, tid: slot.tid, sha256: slot.sha256, why,
  }, log, tag, 'declined');

  const emitFailed = (direction, slot, why) => fire(ev.failed.set, {
    direction, tid: slot.tid, sha256: slot.sha256, why,
  }, log, tag, 'failed');

  const emitProgress = (direction, slot, transferred) => fire(ev.progress.set, {
    direction, tid: slot.tid, sha256: slot.sha256, transferred, bytes: slot.bytes,
  }, log, tag, 'progress');

  const emitLanded = (direction, slot, extra) => fire(ev.landed.set, Object.assign({
    direction, tid: slot.tid, sha256: slot.sha256, bytes: slot.bytes, kind: slot.kind, mime: slot.mime,
    ms: Math.max(0, Date.now() - slot.startedAtMs),
  }, extra || {}), log, tag, 'landed');

  // ---------------------------------------------------------------- sender

  /**
   * Offer one artifact. Returns its tid, or null when the channel cannot take it right now (not
   * open, no hello, dormant, a slot already busy, the sha refused earlier, or a bad source).
   * A NULL RETURN IS NORMAL — the queue tries again later, and a drop never waits on a transfer.
   */
  function send(source) {
    if (disposed || !opened || dormant || !helloSeen) return null;
    if (outbound.size >= CAP_OUT) return null;
    if (!source || typeof source.read !== 'function') return null;

    const sha = String(source.sha256 || '');
    if (!SHA_RE.test(sha)) { warn('refusing to offer an artifact with a malformed sha'); return null; }
    if (neverOffer.has(sha)) return null;
    if (quotaRefused) return null;

    const bytes = typeof source.bytes === 'number' ? source.bytes : NaN;
    if (!Number.isInteger(bytes) || bytes < 1 || bytes > CAP_ARTIFACT) {
      warn(`refusing to offer ${sha.slice(0, 8)}: ${bytes} bytes is out of range`);
      return null;
    }
    const mime = String(source.mime || '');
    if (!ACCEPT_MIME.has(mime)) { warn(`refusing to offer ${sha.slice(0, 8)}: mime ${mime}`); return null; }
    const kind = source.kind === 'video' ? 'video' : 'image';
    if (familyOf(mime) !== kind) { warn(`refusing to offer ${sha.slice(0, 8)}: kind/mime disagree`); return null; }

    const now = Date.now();
    const slot = {
      tid: nextTid(),
      sha256: sha, bytes, mime, kind, source,
      state: 'offered',
      offset: 0, ackedOffset: 0, endSent: false, pumping: false,
      offeredAtMs: now, startedAtMs: now, lastProgressAtMs: now,
    };
    outbound.set(slot.tid, slot);

    sendControl({
      t: XferVerb.Offer, v: XFER_PROTO,
      tid: slot.tid, sha256: sha, bytes, mime, kind,
      dur_ms: Number.isFinite(source.dur_ms) ? source.dur_ms : undefined,
      w: Number.isFinite(source.w) ? source.w : undefined,
      h: Number.isFinite(source.h) ? source.h : undefined,
    });
    emitOffered('out', slot);
    arm();
    return slot.tid;
  }

  /**
   * The backpressure pump. Fills the channel to BULK_HIGH_WATER and stops; the watchdog (and the
   * transport's `bufferedamountlow`, surfaced as a 'low' bulk state) brings it back.
   *
   * `sendBulk` returning false halts it deliberately: the transport drops frames in non-open
   * states, so a pump that assumed delivery would advance `offset` past bytes that never left.
   */
  async function pump(slot) {
    if (slot.pumping || disposed) return;
    slot.pumping = true;
    try {
      while (opened && slot.state === 'sending' && slot.offset < slot.bytes) {
        if (bufferedAmount() >= HIGH_WATER) break;

        const len = Math.min(CHUNK_BODY_BYTES, slot.bytes - slot.offset);
        const at = slot.offset;

        let body;
        try {
          body = await slot.source.read(at, len);
        } catch (e) {
          error(`read(${at},${len}) failed for ${slot.sha256.slice(0, 8)}: ${(e && e.message) || e}`);
          cancelOutbound(slot, XferCancel.Superseded, 'source read failed');
          return;
        }
        // The await above is a real yield: the channel may have died, or the transfer been
        // cancelled, while we were reading.
        if (disposed || !opened || slot.state !== 'sending' || !outbound.has(slot.tid)) return;

        const ab = toArrayBuffer(body);
        if (!ab || ab.byteLength === 0) {
          error(`read(${at},${len}) returned nothing for ${slot.sha256.slice(0, 8)}`);
          cancelOutbound(slot, XferCancel.Superseded, 'source read returned nothing');
          return;
        }

        const take = Math.min(ab.byteLength, len);
        if (!sendBulk(packChunk(slot.tid, at, take === ab.byteLength ? ab : ab.slice(0, take)))) break;

        slot.offset = at + take;
        slot.lastProgressAtMs = Date.now();
        sessionBytesOut += take;
        emitProgress('out', slot, slot.offset);
      }

      if (opened && slot.state === 'sending' && slot.offset >= slot.bytes && !slot.endSent) {
        slot.endSent = true;
        sendControl({ t: XferVerb.End, v: XFER_PROTO, tid: slot.tid, sha256: slot.sha256 });
      }
    } finally {
      slot.pumping = false;
    }
  }

  function cancelOutbound(slot, why, reason) {
    if (!outbound.has(slot.tid)) return;
    outbound.delete(slot.tid);
    slot.state = 'cancelled';
    info(`cancelling our tid ${slot.tid} (${why}): ${reason || why}`);
    sendControl({ t: XferVerb.Cancel, v: XFER_PROTO, tid: slot.tid, why });
    emitFailed('out', slot, why);
  }

  // ---------------------------------------------------------------- receiver: the offer gate

  function knownBlocked(sha) {
    if (neverAccept.has(sha)) return true;
    if (blocklist && typeof blocklist.knows === 'function' && typeof blocklist.isBlocked === 'function') {
      // ONLY if we already know. An unknown hash is accepted and enqueued for the batch lookup;
      // the gate that actually matters is media.acquireByTag at render time.
      if (blocklist.knows(sha) && blocklist.isBlocked(sha)) return true;
    }
    if (store && typeof store.knowsBlocked === 'function') {
      try { if (store.knowsBlocked(sha) === true) return true; } catch (_e) { /* never fatal */ }
    }
    return false;
  }

  function decline(tid, sha, why, msg) {
    sendControl({ t: XferVerb.Decline, v: XFER_PROTO, tid, why });
    emitDeclined('in', {
      tid, sha256: sha, bytes: Number(msg && msg.bytes) || 0,
      mime: (msg && msg.mime) || '', kind: (msg && msg.kind) || '',
    }, why);
  }

  /**
   * The offer gate. EVERY check happens before a single byte is allowed to flow, and the ORDER is
   * the contract (spec §2.4): cheapest and most absolute first, `have` deliberately ahead of
   * `busy`/`quota` so cross-session reuse is answered even when we are otherwise full.
   */
  function handleOffer(msg) {
    const tid = Number(msg.tid);
    const sha = typeof msg.sha256 === 'string' ? msg.sha256 : '';
    if (!Number.isInteger(tid) || tid < 0) return;

    // 1. the feature is off (no consent, no caps, no bulk channel). Checked ahead of everything,
    //    including the resume shortcut: a player who withdraws the toggle stops taking bytes on
    //    the very next offer, resumed or not.
    if (dormant || !acceptOffers()) { decline(tid, sha, XferDecline.Off, msg); return; }

    // A repeat offer for a tid we are already receiving is a RESUME, not a new transfer: it must
    // not be counted against concurrency (7) and must not be answered `have` (6) — the artifact is
    // half-written, not held. It picks up from exactly what is already on disk.
    const existing = inbound.get(tid);
    if (existing && existing.sha256 === sha) {
      existing.received = safePartialLength(sha);
      existing.ackedAt = existing.received;
      existing.paused = false;
      existing.lastProgressAtMs = Date.now();
      sendControl({ t: XferVerb.Accept, v: XFER_PROTO, tid, from_offset: existing.received });
      arm();
      return;
    }

    // 2. mime allowlist + kind/mime agreement
    const mime = typeof msg.mime === 'string' ? msg.mime : '';
    const kind = msg.kind === 'video' ? 'video' : (msg.kind === 'image' ? 'image' : '');
    if (!ACCEPT_MIME.has(mime) || !kind || familyOf(mime) !== kind) {
      decline(tid, sha, XferDecline.BadMime, msg); return;
    }

    // 3. the sha must be a sha (it is the only thing that ever becomes a filename)
    if (!SHA_RE.test(sha)) { decline(tid, sha, XferDecline.BadMime, msg); return; }

    // 4. size. A REAL number, not something JS would coerce into one: `Number('5000')`,
    //    `Number([5000])` and `Number(true)` are all finite here, and a size is a claim we act on,
    //    not a display hint we can afford to read forgivingly.
    const bytes = typeof msg.bytes === 'number' ? msg.bytes : NaN;
    if (!Number.isInteger(bytes) || bytes < 1 || bytes > CAP_ARTIFACT) {
      decline(tid, sha, XferDecline.TooBig, msg); return;
    }

    // 5. blocklisted, as far as we ALREADY know (never a network round trip — see knownBlocked)
    if (knownBlocked(sha)) { decline(tid, sha, XferDecline.Blocked, msg); return; }

    // 6. we already have it. THIS IS A SUCCESS — cross-session reuse, not a refusal.
    if (safeHas(sha)) { decline(tid, sha, XferDecline.Have, msg); return; }

    // 7. one at a time
    if (inbound.size >= CAP_IN) { decline(tid, sha, XferDecline.Busy, msg); return; }

    // 8. the per-match, per-direction byte budget
    if (sessionBytesIn + bytes > CAP_SESSION) { decline(tid, sha, XferDecline.Quota, msg); return; }

    // 9. accept, from whatever is already on disk
    const from = safePartialLength(sha);
    let began = false;
    try { began = store.begin(sha, mime, bytes) !== false; } catch (e) {
      error(`store.begin(${sha.slice(0, 8)}) threw: ${(e && e.message) || e}`);
      began = false;
    }
    if (!began) {
      sendControl({ t: XferVerb.Fail, v: XFER_PROTO, tid, why: XferFail.StoreFull });
      return;
    }

    const now = Date.now();
    const slot = {
      tid, sha256: sha, bytes, mime, kind,
      received: from, ackedAt: from, paused: false,
      startedAtMs: now, lastProgressAtMs: now,
    };
    inbound.set(tid, slot);
    sendControl({ t: XferVerb.Accept, v: XFER_PROTO, tid, from_offset: from });
    emitOffered('in', slot);
    arm();
  }

  function safeHas(sha) {
    try { return store && store.has(sha) === true; } catch (e) { error(`store.has threw: ${(e && e.message) || e}`); return false; }
  }

  function safePartialLength(sha) {
    try {
      const v = store && store.partialLength ? Number(store.partialLength(sha)) : 0;
      return Number.isInteger(v) && v > 0 ? v : 0;
    } catch (e) { error(`store.partialLength threw: ${(e && e.message) || e}`); return 0; }
  }

  // ---------------------------------------------------------------- receiver: chunks

  function handleChunk(raw) {
    const frame = unpackChunk(raw);
    if (!frame) { noteStray(0); return; }

    const slot = inbound.get(frame.tid);
    if (!slot) { noteStray(frame.tid); return; }

    // Ordered channel: a lower offset is a duplicate (harmless, drop it), a higher one is a gap
    // that the store cannot honour — `write` appends at exactly `offset` by contract.
    if (frame.offset < slot.received) return;
    if (frame.offset > slot.received) {
      failInbound(slot, XferFail.Io, `offset gap: got ${frame.offset}, expected ${slot.received}`);
      return;
    }

    // Writing stops at exactly the offered `bytes`. A peer that keeps sending is over-running.
    if (slot.received + frame.body.byteLength > slot.bytes) {
      failInbound(slot, XferFail.TooBig, `over-run past the offered ${slot.bytes} bytes`);
      return;
    }

    let wrote = false;
    try { wrote = store.write(slot.sha256, frame.offset, frame.body) !== false; } catch (e) {
      error(`store.write threw: ${(e && e.message) || e}`);
      wrote = false;
    }
    if (!wrote) { failInbound(slot, XferFail.Io, 'store refused the write'); return; }

    slot.received += frame.body.byteLength;
    slot.lastProgressAtMs = Date.now();
    sessionBytesIn += frame.body.byteLength;
    emitProgress('in', slot, slot.received);

    if (slot.received - slot.ackedAt >= ACK_EVERY) {
      slot.ackedAt = slot.received;
      sendControl({ t: XferVerb.Ack, v: XFER_PROTO, tid: slot.tid, offset: slot.received });
    }
  }

  /**
   * A chunk for a tid we never accepted. Dropped, always. Past STRAY_CHUNK_LIMIT we say so —
   * a peer cannot stream bytes we never agreed to receive, and it should not get to do it quietly.
   */
  function noteStray(tid) {
    strays++;
    if (strays % STRAY_CHUNK_LIMIT !== 0) return;
    strayLimitHits++;
    warn(`${strays} chunks for tids we never accepted (last: ${tid}) — telling them to stop`);
    sendControl({ t: XferVerb.Cancel, v: XFER_PROTO, tid, why: XferCancel.Stray });
  }

  function failInbound(slot, why, reason) {
    warn(`inbound tid ${slot.tid} failed (${why}): ${reason}`);
    try { store.abort(slot.sha256); } catch (_e) { /* best effort */ }
    inbound.delete(slot.tid);
    sendControl({ t: XferVerb.Fail, v: XFER_PROTO, tid: slot.tid, why });
    emitFailed('in', slot, why);
  }

  /** DtrhLoomStore error vocabulary -> the xfer_fail reasons. */
  function failReasonFor(storeError) {
    switch (String(storeError || '')) {
      case 'hash-mismatch': return XferFail.HashMismatch;
      case 'bad-format': case 'bad-name': return XferFail.Magic;
      case 'cap-reached': return XferFail.StoreFull;
      case 'too-big': return XferFail.TooBig;
      default: return XferFail.Io;
    }
  }

  async function handleEnd(msg) {
    const slot = inbound.get(Number(msg.tid));
    if (!slot) return;
    if (typeof msg.sha256 === 'string' && msg.sha256 !== slot.sha256) {
      failInbound(slot, XferFail.Io, 'xfer_end names a different artifact');
      return;
    }
    if (slot.received !== slot.bytes) {
      failInbound(slot, XferFail.Io, `end at ${slot.received} of ${slot.bytes} bytes`);
      return;
    }

    let result = null;
    try { result = await store.commit(slot.sha256); } catch (e) {
      error(`store.commit threw: ${(e && e.message) || e}`);
      result = { ok: false, error: 'io-failed' };
    }
    if (!inbound.has(slot.tid)) return;               // cancelled/torn down while committing
    inbound.delete(slot.tid);

    if (result && result.ok) {
      sendControl({ t: XferVerb.Done, v: XFER_PROTO, tid: slot.tid, sha256: slot.sha256 });
      emitLanded('in', slot, { url: result.url || null });
      arm();
      return;
    }

    // The sha is verified HOST-SIDE at commit — the page's hash is a claim — so a mismatch here is
    // the peer sending something other than what it offered. Never take that sha again this match.
    const why = failReasonFor(result && result.error);
    if (why === XferFail.HashMismatch) neverAccept.add(slot.sha256);
    try { store.abort(slot.sha256); } catch (_e) { /* best effort */ }
    sendControl({ t: XferVerb.Fail, v: XFER_PROTO, tid: slot.tid, why });
    emitFailed('in', slot, why);
    arm();
  }

  // ---------------------------------------------------------------- inbound control routing

  function handleControl(text) {
    let msg;
    try { msg = JSON.parse(text); } catch (_e) {
      warn('unparseable control frame on the media channel');
      return;
    }
    if (!msg || typeof msg !== 'object' || Array.isArray(msg) || typeof msg.t !== 'string') return;

    switch (msg.t) {
      case XferVerb.Hello: {
        peerHello = msg;
        helloSeen = true;
        dormant = false;
        clearHelloTimeout();
        info(`peer speaks xfer proto ${msg.proto ?? msg.v}`);
        resumePaused();
        break;
      }

      case XferVerb.Offer:
        handleOffer(msg);
        break;

      case XferVerb.Accept: {
        const slot = outbound.get(Number(msg.tid));
        if (!slot || slot.state === 'sending') return;
        const from = Number(msg.from_offset);
        const offset = Number.isInteger(from) && from > 0 ? Math.min(from, slot.bytes) : 0;
        slot.offset = offset;
        slot.ackedOffset = offset;
        slot.state = 'sending';
        slot.endSent = false;
        slot.startedAtMs = Date.now();
        slot.lastProgressAtMs = slot.startedAtMs;
        if (offset > 0) info(`resuming our tid ${slot.tid} from ${offset} of ${slot.bytes}`);
        pump(slot);
        arm();
        break;
      }

      case XferVerb.Decline: {
        const slot = outbound.get(Number(msg.tid));
        if (!slot) return;
        outbound.delete(slot.tid);
        const why = String(msg.why || XferDecline.Off);
        // `blocked` earns a permanent skip with NO sender-facing UI anywhere: telling a sender
        // their file is blocklisted is a moderation-evasion oracle.
        if (why === XferDecline.Blocked) neverOffer.add(slot.sha256);
        if (why === XferDecline.Quota) quotaRefused = true;
        emitDeclined('out', slot, why);
        arm();
        break;
      }

      case XferVerb.Ack: {
        const slot = outbound.get(Number(msg.tid));
        if (!slot) return;
        const at = Number(msg.offset);
        if (Number.isInteger(at) && at > slot.ackedOffset) slot.ackedOffset = Math.min(at, slot.bytes);
        slot.lastProgressAtMs = Date.now();
        break;
      }

      case XferVerb.Done: {
        const slot = outbound.get(Number(msg.tid));
        if (!slot) return;
        outbound.delete(slot.tid);
        const ms = Math.max(1, Date.now() - slot.startedAtMs);
        lastRateBps = Math.round((slot.bytes * 1000) / ms);
        emitLanded('out', slot, {});
        arm();
        break;
      }

      case XferVerb.Fail: {
        const slot = outbound.get(Number(msg.tid));
        if (!slot) return;
        outbound.delete(slot.tid);
        const why = String(msg.why || XferFail.Io);
        if (why === XferFail.HashMismatch || why === XferFail.Blocked || why === XferFail.Magic) {
          neverOffer.add(slot.sha256);
        }
        if (why === XferFail.StoreFull) quotaRefused = true;
        emitFailed('out', slot, why);
        arm();
        break;
      }

      case XferVerb.Cancel: {
        // Disjoint tid spaces (see nextTid) make this unambiguous: an odd tid is the host's send,
        // an even one the guest's, so exactly one of the two maps can hold it.
        const tid = Number(msg.tid);
        const why = String(msg.why || XferCancel.PeerGone);
        const mine = outbound.get(tid);
        if (mine) {
          outbound.delete(tid);
          info(`they cancelled our tid ${tid} (${why})`);
          emitFailed('out', mine, why);
          arm();
          return;
        }
        const theirs = inbound.get(tid);
        if (theirs) {
          info(`they abandoned their tid ${tid} (${why})`);
          try { store.abort(theirs.sha256); } catch (_e) { /* best effort */ }
          inbound.delete(tid);
          emitFailed('in', theirs, why);
          arm();
        }
        break;
      }

      case XferVerb.End:
        handleEnd(msg).catch((e) => error(`end handling threw: ${(e && e.message) || e}`));
        break;

      default:
        info(`ignoring unknown control verb '${msg.t}'`);
        break;
    }
  }

  // ---------------------------------------------------------------- channel lifecycle

  /**
   * The channel came back inside the reconnect grace. Everything paused re-offers its OWN tid and
   * the receiver answers with `from_offset` = what it already has on disk, so nothing acked is
   * ever re-sent.
   */
  function resumePaused() {
    for (const slot of Array.from(outbound.values())) {
      if (slot.state !== 'paused') continue;
      slot.state = 'offered';
      slot.offeredAtMs = Date.now();
      slot.lastProgressAtMs = slot.offeredAtMs;
      sendControl({
        t: XferVerb.Offer, v: XFER_PROTO,
        tid: slot.tid, sha256: slot.sha256, bytes: slot.bytes, mime: slot.mime, kind: slot.kind,
      });
    }
    for (const slot of inbound.values()) {
      slot.paused = false;
      slot.lastProgressAtMs = Date.now();
    }
    arm();
  }

  /**
   * The channel went away. PAUSE — keep tids, offsets and partial files, send nothing. If it comes
   * back inside GoonConsts.ReconnectGraceMs the transfers pick up where they stopped; if the
   * transport gives up for good the owner calls `close()`, which is the terminal path.
   */
  function onChannelClosed() {
    for (const slot of outbound.values()) {
      if (slot.state === 'sending' || slot.state === 'offered') slot.state = 'paused';
    }
    for (const slot of inbound.values()) slot.paused = true;
    if (watchdogTimer) { clearTimeout(watchdogTimer); watchdogTimer = null; }
  }

  function onBulkState(state) {
    if (disposed) return;
    if (state === 'open') {
      if (opened) return;
      opened = true;
      info('media channel open');
      sendHello();
      armHelloTimeout();
      resumePaused();
      return;
    }
    if (state === 'closed') {
      if (!opened) return;
      opened = false;
      info('media channel closed — pausing transfers');
      onChannelClosed();
      return;
    }
    // 'low' (or anything else the transport chooses to report): a pump kick, nothing more. The
    // 50 ms watchdog would have caught it anyway; this just makes the common case immediate.
    for (const slot of outbound.values()) if (slot.state === 'sending') pump(slot);
  }

  function onRaw(raw) {
    if (disposed) return;
    try {
      if (typeof raw === 'string') { handleControl(raw); return; }
      handleChunk(raw);
    } catch (e) {
      error(`inbound frame handling threw: ${(e && e.message) || e}`);
    }
  }

  // ---------------------------------------------------------------- public surface

  /**
   * Subscribe to the transport.
   *
   * `alreadyOpen` exists because the media channel can be up BEFORE anything subscribes — the
   * transport fires its 'open' the moment the channel attaches, which on the answering side is
   * routinely already `readyState === 'open'` (the same race `_attachChannel` documents for the
   * game channel). Pass `transport.supportsBulk` and no open event is ever missed.
   */
  function open({ alreadyOpen = false } = {}) {
    if (disposed) return;
    unsubMessage = subMessage(onRaw) || noop;
    unsubState = subState(onBulkState) || noop;
    if (alreadyOpen) onBulkState('open');
  }

  /**
   * TERMINAL. Cancel everything, tell the store to throw its partials away, keep what committed.
   * The committed artifacts are hash-keyed and stay valid — only in-flight work dies here.
   */
  function close(why = XferCancel.MatchOver) {
    if (disposed) return;
    disposed = true;

    for (const slot of Array.from(outbound.values())) {
      sendControl({ t: XferVerb.Cancel, v: XFER_PROTO, tid: slot.tid, why });
    }
    outbound.clear();

    for (const slot of Array.from(inbound.values())) {
      sendControl({ t: XferVerb.Cancel, v: XFER_PROTO, tid: slot.tid, why });
      try { store.abort(slot.sha256); } catch (_e) { /* best effort */ }
    }
    inbound.clear();

    clearHelloTimeout();
    if (watchdogTimer) { clearTimeout(watchdogTimer); watchdogTimer = null; }
    try { unsubMessage(); } catch (_e) { /* already gone */ }
    try { unsubState(); } catch (_e) { /* already gone */ }
    unsubMessage = noop;
    unsubState = noop;
    opened = false;
    for (const set of Object.values(ev)) set.set.clear();
  }

  /** Call off one of OUR transfers (the queue supersedes a pick, the user withdraws consent). */
  function cancel(tid, why = XferCancel.User) {
    const slot = outbound.get(Number(tid));
    if (!slot) return false;
    cancelOutbound(slot, why, 'cancelled by us');
    return true;
  }

  function stats() {
    return {
      open: opened,
      helloSeen,
      dormant,
      outbound: outbound.size,
      inbound: inbound.size,
      sessionBytesIn,
      sessionBytesOut,
      bufferedAmount: bufferedAmount(),
      /** What the channel will report `bufferedamountlow` at — the transport's number, not ours. */
      lowThreshold: lowThreshold(),
      strays,
      strayLimitHits,
      quotaRefused,
      neverOffer: neverOffer.size,
      neverAccept: neverAccept.size,
      /** Bytes/sec measured on the last completed send. 0 until one lands. */
      lastRateBps,
      peer: peerHello
        ? {
          proto: peerHello.proto ?? peerHello.v ?? null,
          maxArtifactBytes: peerHello.max_artifact_bytes ?? null,
          maxSessionBytes: peerHello.max_session_bytes ?? null,
          maxConcurrent: peerHello.max_concurrent ?? null,
          accepts: Array.isArray(peerHello.accepts) ? peerHello.accepts.slice() : [],
        }
        : null,
    };
  }

  return {
    open,
    close,
    send,
    cancel,
    stats,

    /** @returns {() => void} unsubscribe. {direction,tid,sha256,bytes,mime,kind} */
    onOffered: ev.offered.on,
    /** @returns {() => void} unsubscribe. {direction,tid,sha256,bytes,kind,mime,ms,url?} */
    onLanded: ev.landed.on,
    /** @returns {() => void} unsubscribe. {direction,tid,sha256,why} — `have` is a SUCCESS. */
    onDeclined: ev.declined.on,
    /** @returns {() => void} unsubscribe. {direction,tid,sha256,why} */
    onFailed: ev.failed.on,
    /** @returns {() => void} unsubscribe. {direction,tid,sha256,transferred,bytes} */
    onProgress: ev.progress.on,

    get isOpen() { return opened; },
    get helloSeen() { return helloSeen; },
    get isDormant() { return dormant; },
    get neverOfferSet() { return neverOffer; },
    get neverAcceptSet() { return neverAccept; },
  };
}
