// net/mediaQueue.js — the PREPICK QUEUE (spec §3): what gets sent, when, and how a landed
// artifact ends up on a payload the opponent is about to receive.
//
// It owns exactly three things: one net/mediaChannel.js instance, the queue of picks, and the
// `enabled()` gate. Everything else is injected, so this module is node-testable and imports
// nothing from exec/ or ui/.
//
// THE DRAW IS Math.random, NOT core/rng.js GoonRng — and that looks like a bug unless it is
// written down. GoonRng is the SHARED DETERMINISTIC stream: both clients derive it from the same
// seed, so anything drawn from it is knowable by the opponent IN ADVANCE. Which of your files you
// are about to send must be private and non-deterministic. exec/media.js's own deck uses
// Math.random for exactly this reason (media.js:41). NO_ECHO = 8 is ported from there too, so a
// small pool does not repeat itself.
//
// SILENT DEGRADATION IS THE ABSENCE OF A SPECIAL CASE. On a relay transport `supportsBulk` is
// false, `enabled()` is false, `tagsFor()` returns [], and `tryFirePayload` fires the payload
// untagged — byte-identical to the wire before this feature existed. There is no "relay mode"
// branch anywhere, and there must never be one.
//
// TWO tryFirePayload INSTANCE WRAPPERS NOW EXIST. boot.js applies the matchLog wrapper first
// (attachMatch -> matchLog.attach), then this one, so the queue's wrapper is the OUTERMOST: a
// payload gets its tags BEFORE the log sees it, and the log records exactly what went out. Order
// matters for readability, not correctness — both are instance patches that die with the match.

import { GoonMatchPhase, GoonPayloadKind } from '../core/contracts.js';
import {
  createMediaChannel,
  ACCEPT_MIME,
  MAX_ARTIFACT_BYTES,
  MAX_EXEMPT_BYTES,
  MAX_XFER_MS,
  XFER_TAGS_MAX,
  XferDecline,
} from './mediaChannel.js';

/** Landed-or-in-flight artifacts we try to keep ahead of the match. */
export const QUEUE_DEPTH = 4;
/** The idle poll only bothers when fewer than this many have landed. */
export const QUEUE_MIN = 2;
/** How many landed artifacts are tracked per match, so a 12-minute run cannot grow unbounded. */
export const LANDED_MAX = 12;
/** Ported from exec/media.js: a reshuffled deck must not repeat the last N picks. */
export const NO_ECHO = 8;
/** Cheap wedge insurance while the match is running. */
export const IDLE_POLL_MS = 5000;
/** Throughput guess before anything has landed. Replaced by the measured rate. */
export const EST_THROUGHPUT_BPS = 500 * 1024;

/**
 * The payload kinds that carry sender media in v1. BubbleSwarm (sub-second pop confetti, many
 * per swarm) and the BrainDrain wash (re-picks on a slow timer, feeds a CSS custom property
 * rather than an element src) are excluded ON PURPOSE — listed as decisions, not oversights.
 */
export const XFER_KINDS = Object.freeze(new Set([GoonPayloadKind.Video, GoonPayloadKind.FlashBurst]));

/** Payload kind -> the media kind exec/ will ask the store for. */
const KIND_MEDIA = Object.freeze({
  [GoonPayloadKind.Video]: 'video',
  [GoonPayloadKind.FlashBurst]: 'image',
});

/** How many tags one payload of that kind may carry. */
const KIND_TAGS = Object.freeze({
  [GoonPayloadKind.Video]: 1,          // one artifact per floating window
  [GoonPayloadKind.FlashBurst]: XFER_TAGS_MAX,
});

const noop = () => {};

/**
 * @param {object} o
 * @param {{listSendable:() => Array, open:(sha:string) => (object|Promise<object|null>|null)}} o.artifacts
 *   The compression side's local artifact source. Only those two verbs are used.
 * @param {object} o.store   exec/receivedStore.js — handed straight to the channel
 * @param {object} [o.blocklist] net/blocklist.js
 * @param {object} [o.logger]
 * @param {() => boolean} [o.canSend] the PREMIUM gate: session.caps.mediaTransfer === true
 * @param {number} [o.idlePollMs] TEST AFFORDANCE
 * @param {object} [o.channelOptions] TEST AFFORDANCE — {timeouts, limits} for the channel
 */
export function createMediaQueue({
  artifacts = null, store = null, blocklist = null, logger = null,
  canSend = null, idlePollMs, channelOptions = null,
} = {}) {
  const info = (m) => { try { logger?.info?.('[GG queue] ' + m); } catch (_e) { /* ignore */ } };
  const warn = (m) => { try { logger?.warn?.('[GG queue] ' + m); } catch (_e) { /* ignore */ } };
  const POLL_MS = Number.isFinite(idlePollMs) ? idlePollMs : IDLE_POLL_MS;
  const sendGate = typeof canSend === 'function' ? canSend : (() => false);

  let match = null;
  let transport = null;
  let channel = null;
  let unsubs = [];
  let pollTimer = 0;
  let attached = false;

  let origFire = null;
  let wrappedFire = null;

  /** {sha, kind, mime, bytes} for everything that has LANDED and not yet been consumed. */
  let landed = [];
  /** sha -> {state:'offered'|'sending', attempts} for what is in flight. */
  const inFlight = new Map();
  /** sha -> attempts, so one requeue is allowed before a permanent skip. */
  const attempts = new Map();
  /** Shas this match will never offer again (blocked, hash-mismatched, twice-failed). */
  const neverOffer = new Set();
  /** Shas already landed (or in flight) this match — never offer the same file twice. */
  const seen = new Set();
  /** What we knew about a sha when we picked it — the `decline:'have'` landing needs it back. */
  const pickInfo = new Map();

  /** The shuffled deck over eligible artifacts, drawn from the end. */
  let deck = [];
  const recent = [];

  const receivedSubs = new Set();
  let picks = 0;
  let consumed = 0;

  /* ------------------------------------------------------------------- the gate */

  /**
   * ALL FIVE, AND'd (spec §3.6). Note `transport.supportsBulk` and NOT `isConnected`:
   * CONNECTED_STATES treats P2P and Relay identically, so "connected" is true on exactly
   * the transport that must never see a byte of media.
   */
  function enabled() {
    return !!(sendGate()
      && match && match.peerSupportsTransfer
      && match.localMediaTransfer
      && match.remoteMediaTransfer
      && transport && transport.supportsBulk
      && channel && channel.helloSeen);
  }

  /**
   * The RECEIVE gate. Deliberately NOT `enabled()`: receiving is not premium-gated (a free player
   * seeing a supporter's media is the whole product), so `canSend` has no business here.
   */
  function acceptOffers() {
    return !!(match && match.peerSupportsTransfer
      && match.localMediaTransfer && match.remoteMediaTransfer
      && transport && transport.supportsBulk);
  }

  /* ------------------------------------------------------------- eligibility + deck */

  function estBps() {
    const rate = channel ? (channel.stats().lastRateBps || 0) : 0;
    return rate > 0 ? rate : EST_THROUGHPUT_BPS;
  }

  /** Everything from the local source we would be willing to offer RIGHT NOW. */
  function eligible() {
    let all = [];
    try { all = artifacts && typeof artifacts.listSendable === 'function' ? artifacts.listSendable() : []; }
    catch (e) { warn('listSendable threw: ' + ((e && e.message) || e)); return []; }
    if (!Array.isArray(all)) return [];

    const budget = estBps();
    const out = [];
    for (const raw of all) {
      const a = raw || {};
      const sha = typeof a.sha === 'string' ? a.sha : (typeof a.sha256 === 'string' ? a.sha256 : '');
      if (!/^[0-9a-f]{64}$/.test(sha)) continue;
      if (neverOffer.has(sha) || seen.has(sha)) continue;
      const mime = String(a.mime || '');
      if (!ACCEPT_MIME.has(mime)) continue;
      const kind = a.kind === 'video' ? 'video' : (a.kind === 'image' ? 'image' : '');
      if (!kind) continue;
      const bytes = Number(a.bytes);
      if (!Number.isInteger(bytes) || bytes < 1) continue;
      // An exempt original is by definition un-optimised, so it gets the tighter cap.
      if (bytes > (a.exempt ? MAX_EXEMPT_BYTES : MAX_ARTIFACT_BYTES)) continue;
      // …and nothing is offered that cannot plausibly land inside the match's patience.
      if ((bytes / budget) * 1000 > MAX_XFER_MS) continue;
      out.push({ sha, bytes, mime, kind, exempt: !!a.exempt });
    }
    return out;
  }

  /** Reshuffle over the CURRENT eligible set, pushing the last NO_ECHO picks to the bottom. */
  function reshuffle(pool) {
    deck = pool.slice();
    for (let i = deck.length - 1; i > 0; i--) {
      const j = (Math.random() * (i + 1)) | 0;             // Math.random — see the header
      [deck[i], deck[j]] = [deck[j], deck[i]];
    }
    if (deck.length > NO_ECHO) {
      for (const sha of recent) {
        const at = deck.findIndex((x) => x.sha === sha);
        if (at >= 0) { const [e] = deck.splice(at, 1); deck.unshift(e); }
      }
    }
  }

  /** The next artifact worth offering, or null. */
  function nextPick() {
    const pool = eligible();
    if (!pool.length) return null;
    const live = new Set(pool.map((e) => e.sha));
    deck = deck.filter((e) => live.has(e.sha) && !seen.has(e.sha) && !neverOffer.has(e.sha));
    if (!deck.length) reshuffle(pool);
    const pick = deck.pop() || null;
    if (!pick) return null;
    recent.push(pick.sha);
    if (recent.length > NO_ECHO) recent.shift();
    return pick;
  }

  /* -------------------------------------------------------------------- the pump */

  function inFlightCount() { return inFlight.size; }

  /** Fill to QUEUE_DEPTH. Event-driven: channel open, xfer_done, decline:'have', consumption. */
  function pump() {
    if (!attached || !enabled()) return;
    let guard = 0;
    while (landed.length + inFlightCount() < QUEUE_DEPTH && guard++ < QUEUE_DEPTH) {
      if (!offerNext()) break;
    }
  }

  /** The 5 s idle poll's cheaper cousin — only bothers when the larder is actually low. */
  function pumpIfHungry() {
    if (landed.length < QUEUE_MIN) pump();
  }

  /** @returns {boolean} true when an offer went out (or is being opened). */
  function offerNext() {
    if (!channel) return false;
    if (inFlightCount() >= 1) return false;               // MAX_CONCURRENT_OUT
    const pick = nextPick();
    if (!pick) return false;

    seen.add(pick.sha);
    pickInfo.set(pick.sha, { kind: pick.kind, mime: pick.mime, bytes: pick.bytes });
    inFlight.set(pick.sha, { state: 'offered', attempts: attempts.get(pick.sha) || 0 });
    picks++;

    // artifacts.open may be async (the page fetches the bytes once and slices).
    let opened = null;
    try { opened = artifacts.open(pick.sha); } catch (e) {
      warn(`open(${pick.sha.slice(0, 8)}) threw: ${(e && e.message) || e}`);
      opened = null;
    }
    Promise.resolve(opened).then((src) => {
      if (!attached || !channel) return;
      if (!src || typeof src.read !== 'function') {
        inFlight.delete(pick.sha);
        neverOffer.add(pick.sha);                          // unreadable is permanently unsendable
        return;
      }
      const tid = channel.send({
        sha256: pick.sha,
        bytes: Number(src.bytes) || pick.bytes,
        mime: src.mime || pick.mime,
        kind: pick.kind,
        read: (offset, len) => src.read(offset, len),
      });
      if (tid == null) {
        // The channel refused right now (busy, not open, sha refused earlier). Put it
        // back: a NULL RETURN IS NORMAL and the next top-up tries something else.
        inFlight.delete(pick.sha);
        seen.delete(pick.sha);
      }
    }).catch((e) => {
      inFlight.delete(pick.sha);
      warn(`open(${pick.sha.slice(0, 8)}) failed: ${(e && e.message) || e}`);
    });
    return true;
  }

  /** A slot died. One requeue, then a permanent skip for the match. */
  function failed(sha, why, permanent) {
    inFlight.delete(sha);
    if (permanent) { neverOffer.add(sha); return; }
    const n = (attempts.get(sha) || 0) + 1;
    attempts.set(sha, n);
    if (n >= 2) neverOffer.add(sha);
    else seen.delete(sha);                                 // eligible again, at the back
    void why;
  }

  function pushLanded(a) {
    landed.push(a);
    while (landed.length > LANDED_MAX) landed.shift();
  }

  /* -------------------------------------------------------------------- the tags */

  /**
   * WHY is a media-kind payload about to fire untagged? One short sentence naming
   * the FIRST failing gate, for the diagnostic below. The order mirrors enabled().
   */
  function whyNoTags() {
    if (!sendGate()) return 'this side\'s send capability is off (caps.mediaTransfer)';
    if (!match || !match.peerSupportsTransfer) return 'the peer\'s build does not advertise transfer (stale page?)';
    if (!match.localMediaTransfer) return 'our lobby send toggle is off';
    if (!match.remoteMediaTransfer) return 'their lobby send toggle is off';
    if (!transport || !transport.supportsBulk) return 'no bulk lane on this link (relay fallback)';
    if (!channel || !channel.helloSeen) return 'the peer never said xfer_hello';
    let sendable = -1;
    try { sendable = eligible().length + seen.size; } catch (_e) { /* leave -1 */ }
    return 'no artifact landed yet (sendable=' + sendable + ', in flight=' + inFlightCount()
      + ', landed=' + landed.length + ', consumed=' + consumed + ')';
  }

  /** Reasons already said this match — the diagnostic prints each ONCE, not per throw. */
  const saidWhy = new Set();

  /**
   * The tags for one payload, or [] when nothing has landed / the feature is off.
   * `[]` IS THE NORMAL PATH: the payload then fires exactly as it does today, and a drop can
   * never block on a transfer.
   *
   * THE WARN IS THE PLAY-TEST'S EYES (2026-08-05). "It defaults to local media" reached the
   * owner three times with three different root causes upstream of here (relay link, the
   * un-advertised transfer cap, a stale phone page) and every round was diagnosed by
   * archaeology. A media-kind payload leaving untagged now SAYS WHY, once per distinct
   * reason per match — warn level, because that is what reaches the C# log and the phone's
   * ?debug=1 overlay.
   */
  function tagsFor(kind) {
    if (!XFER_KINDS.has(kind)) return [];
    const out = [];
    if (enabled()) {
      const want = KIND_TAGS[kind] || 1;
      const mediaKind = KIND_MEDIA[kind];
      for (const a of landed) {
        if (a.kind !== mediaKind) continue;
        out.push('xfer:' + a.sha);
        if (out.length >= want) break;
      }
    }
    if (!out.length) {
      const why = whyNoTags();
      if (!saidWhy.has(why)) {
        saidWhy.add(why);
        warn('payload kind ' + kind + ' fired untagged — ' + why + ' (said once per reason)');
      }
    }
    return out;
  }

  /** A payload went out carrying these tags — those artifacts are spent. */
  function markConsumed(tags) {
    if (!Array.isArray(tags) || !tags.length) return;
    const spent = new Set();
    for (const t of tags) {
      if (typeof t === 'string' && t.startsWith('xfer:')) spent.add(t.slice(5));
    }
    if (!spent.size) return;
    const before = landed.length;
    landed = landed.filter((a) => !spent.has(a.sha));
    consumed += before - landed.length;
    pump();                                                // consumption is a top-up trigger
  }

  /* ------------------------------------------------------------------- lifecycle */

  function bindChannel() {
    channel = createMediaChannel(Object.assign({
      sendBulk: (d) => transport.sendBulk(d),
      bulkBufferedAmount: () => transport.bulkBufferedAmount,
      bulkLowThreshold: () => transport.bulkLowThreshold,
      onBulkMessage: (fn) => transport.onBulkMessage(fn),
      onBulkStateChanged: (fn) => transport.onBulkStateChanged(fn),
      store,
      blocklist,
      logger,
      acceptOffers,
      isHost: !!(match && match.isHost),
      tag: (match && match.isHost) ? 'GG:xfer:host' : 'GG:xfer:guest',
    }, channelOptions || {}));

    unsubs.push(channel.onLanded((e) => {
      if (e.direction === 'out') {
        inFlight.delete(e.sha256);
        pushLanded({ sha: e.sha256, kind: e.kind, mime: e.mime, bytes: e.bytes });
        info(`sent ${e.sha256.slice(0, 8)} (${Math.round(e.bytes / 1024)} KB in ${e.ms}ms)`);
        pump();
        return;
      }
      // INBOUND: the store already committed it. Announce it so boot can register it
      // with exec/media.js — this module never imports exec/.
      for (const fn of Array.from(receivedSubs)) {
        try { fn({ sha: e.sha256, kind: e.kind, mime: e.mime, bytes: e.bytes, url: e.url || '' }); }
        catch (err) { warn('onReceived handler threw: ' + ((err && err.message) || err)); }
      }
    }));

    unsubs.push(channel.onDeclined((e) => {
      if (e.direction !== 'out') return;
      if (e.why === XferDecline.Have) {
        // A SUCCESS — cross-session reuse. The slot lands immediately and this must not
        // read as a failure anywhere, in the code or the logs.
        inFlight.delete(e.sha256);
        const kindOf = deckKindFor(e.sha256);
        pushLanded({ sha: e.sha256, kind: kindOf.kind, mime: kindOf.mime, bytes: kindOf.bytes });
        info(`they already had ${e.sha256.slice(0, 8)} — reusing it`);
        pump();
        return;
      }
      // `blocked` gets a permanent skip and NO sender-facing UI, ever: telling a sender
      // their file is blocklisted is a moderation-evasion oracle.
      failed(e.sha256, e.why, e.why === XferDecline.Blocked);
      pump();
    }));

    unsubs.push(channel.onFailed((e) => {
      if (e.direction !== 'out') return;
      failed(e.sha256, e.why, false);
      pump();
    }));

    // The transport's 'open' fires the moment the channel attaches, and on the ANSWERING side
    // that is routinely already readyState 'open' — pass supportsBulk so the open is never missed.
    channel.open({ alreadyOpen: !!(transport && transport.supportsBulk) });
  }

  function deckKindFor(sha) {
    return pickInfo.get(sha) || { kind: 'image', mime: '', bytes: 0 };
  }

  function onPhase(phase) {
    if (phase === GoonMatchPhase.Draft) {
      // THE BIGGEST DEAD-TIME BUDGET THE MATCH HAS. The draft is untimed and both consents
      // have just landed, so this is where the queue primes to depth.
      info('draft — priming the send queue');
      pump();
      return;
    }
    if (phase === GoonMatchPhase.Recap || phase === GoonMatchPhase.Idle) {
      cancelAll('match_over');
      return;
    }
    pumpIfHungry();
  }

  function cancelAll(why) {
    if (why && (inFlight.size || landed.length)) {
      info(`clearing the queue (${why}): ${inFlight.size} in flight, ${landed.length} landed`);
    }
    inFlight.clear();
    landed = [];
    deck = [];
    // The STORE is deliberately untouched: committed artifacts are hash-keyed and stay valid.
  }

  function startPoll() {
    stopPoll();
    try {
      pollTimer = setInterval(() => {
        if (!attached || !match) return;
        const p = match.phase;
        if (p === GoonMatchPhase.Draft || p === GoonMatchPhase.Countdown
          || p === GoonMatchPhase.Live || p === GoonMatchPhase.SuddenDeath) pumpIfHungry();
      }, POLL_MS);
      if (pollTimer && typeof pollTimer.unref === 'function') pollTimer.unref();
    } catch (_e) { pollTimer = 0; }
  }

  function stopPoll() {
    if (!pollTimer) return;
    try { clearInterval(pollTimer); } catch (_e) { /* ignore */ }
    pollTimer = 0;
  }

  /** Unbind everything. A NAMED function, so `attach` never depends on a `this` binding. */
  function detach() {
    attached = false;
    saidWhy.clear();                 // a new match earns fresh diagnostics
    stopPoll();
    for (const off of unsubs) { try { off(); } catch (_e) { /* ignore */ } }
    unsubs = [];
    // Restores the wrapper matchLog installed UNDERNEATH ours — one layer peeled,
    // not the whole stack, because the log's patch is not ours to remove.
    if (match && origFire && match.tryFirePayload === wrappedFire) {
      match.tryFirePayload = (req) => origFire(req);
    }
    origFire = null;
    wrappedFire = null;
    if (channel) { try { channel.close(); } catch (_e) { /* ignore */ } }
    channel = null;
    cancelAll();
    pickInfo.clear();
    attempts.clear();
    neverOffer.clear();
    seen.clear();
    recent.length = 0;
    match = null;
    transport = null;
  }

  return {
    /**
     * Bind to a match + transport. Called from boot.js attachMatch, which the relay fallback
     * re-runs through onCurrentMatchChanged — so the queue re-attaches over the NEW transport
     * automatically, sees supportsBulk false, and goes dormant. No special case.
     */
    attach(m, t) {
      detach();
      if (!m || !t) return false;
      match = m;
      transport = t;
      attached = true;

      bindChannel();

      unsubs.push(match.onPhaseChanged(onPhase));
      unsubs.push(match.onConsentChanged(() => {
        // A player who withdraws the toggle mid-lobby stops the queue immediately.
        if (!enabled()) cancelAll('consent withdrawn');
        else pump();
      }));

      /* THE INSTANCE WRAP (spec §4.2). Applied AFTER boot's matchLog wrapper, so this one is
       * outermost — see the header. Zero changes to ui/arsenal.js and ui/soloDriver.js. */
      origFire = match.tryFirePayload.bind(match);
      wrappedFire = (req) => {
        let out = req;
        // No enabled() pre-check here: tagsFor answers [] when the lane is off
        // AND says why (once per reason) — the pre-check was hiding exactly the
        // failures the 2026-08-05 play-tests needed named.
        if (req && XFER_KINDS.has(req.kind) && !req.tags) {
          const tags = tagsFor(req.kind);                  // [] when nothing has landed
          if (tags.length) out = Object.assign({}, req, { tags });
        }
        const res = origFire(out);
        if (res && res.ok && out.tags) markConsumed(out.tags);
        return res;
      };
      match.tryFirePayload = wrappedFire;

      startPoll();
      onPhase(match.phase);
      return true;
    },

    detach,

    tagsFor,
    markConsumed,
    enabled,

    /**
     * How many local artifacts could be offered RIGHT NOW. The lobby's honesty
     * line: every consent/connection gate can pass and the lane still starves
     * when the library was never compressed (round-11's "sendable=0" — the last
     * root cause of the 2026-08-05 saga), and only a number the UI can read
     * turns that from archaeology into a sentence on the screen.
     */
    sendableCount() {
      try { return eligible().length; } catch (_e) { return 0; }
    },

    /** @returns {() => void} unsubscribe. {sha, kind, mime, bytes, url} for INBOUND landings. */
    onReceived(fn) {
      if (typeof fn !== 'function') return noop;
      receivedSubs.add(fn);
      return () => receivedSubs.delete(fn);
    },

    /** Manual top-up. The event triggers cover the real cases; this is for tests + __gg. */
    pump,

    stats() {
      return {
        attached,
        enabled: enabled(),
        landed: landed.length,
        inFlight: inFlightCount(),
        neverOffer: neverOffer.size,
        picks,
        consumed,
        deck: deck.length,
        estBps: estBps(),
        channel: channel ? channel.stats() : null,
      };
    },
  };
}

export default createMediaQueue;
