// The primary transport — port of Services/GoonGame/GoonWebRtcTransport.cs onto the browser's
// native RTCPeerConnection (the C# side rides SIPSorcery; the signaling blobs are byte-identical,
// so a C# peer and a page peer interoperate with no translation layer).
//
// Shape:
//   host  createInvite() -> /v2/goon/invite mints a code -> short-poll /v2/goon/signal until the
//         guest joins -> offer/answer/ICE through the mailbox -> DTLS -> channel.
//   guest join(code)     -> /v2/goon/join -> same mailbox from the other side.
// Once the channel opens the mailbox is dropped and nothing but the two clients is involved.
//
// DATA CHANNEL ONLY. No tracks are ever added, so no codec is negotiated and there is no path —
// deliberate or accidental — for mic or camera to reach the peer.
//
// Channel config: TWO channels, both ordered + reliable (no maxRetransmits, no maxPacketLifeTime).
//   'goon'        the game channel, created in-band by the host, JSON only, 16 KB/frame.
//   'goon-media'  the BULK channel, negotiated out-of-band (`negotiated:true, id:1`) and created
//                 symmetrically by both roles, carrying net/mediaChannel.js's control JSON and its
//                 16384-byte binary chunks. It needs NO renegotiation — the m=application section
//                 is already in the SDP because the host creates 'goon' — and its absence is never
//                 fatal: `supportsBulk` stays false and the duel behaves exactly as it does today.
//                 SCTP head-of-line blocking is PER-STREAM, so a 24 MB transfer cannot delay a
//                 tick; what the two channels share is the association's congestion window, which
//                 is what mediaChannel's backpressure pump manages.
//
// ICE: three public STUN providers, ALWAYS, plus whatever turn/turns entries the ROOM handed us
// (net/signaling.js iceServers — server-minted, short-lived; the provider's own stun urls are
// dropped, see turnOnlyIceServers). A pair that cannot be formed gives up after `iceBudgetMs` —
// GoonConsts.IceTimeoutMs alone when there is no relay to wait for, plus IceRelayGraceMs when
// there is, plus one IceProgressGraceMs if checks are still in flight at that point — sets
// `iceFailed`, and the caller opens a relay transport on the SAME room: `code`/`token`
// deliberately survive the failure so session.js can adopt it.
//
// A TURN-mediated pair is STILL P2P as far as this file and `supportsBulk` are concerned: it ends
// in ConnectedP2P over a real data channel. The thing media must never ride is the ws/HTTP mailbox
// in net/relayTransport.js, which is a different object entirely.
//
// NODE-IMPORT-SAFE: nothing at module scope touches RTCPeerConnection. It is constructed only
// inside the negotiation path, guarded — a page without WebRTC fails with lastError
// 'webrtc_unavailable' instead of throwing at import.

import { GoonConsts, GoonTransportState } from '../core/contracts.js';
// Layering: the protocol owns its own numbers, the transport imports them. Nothing in
// mediaChannel.js touches RTCPeerConnection or window, so this import stays node-safe.
import { BULK_LOW_WATER } from './mediaChannel.js';
import { GoonSignalingClient, normalizeCode } from './signaling.js';
import { GoonTransportBase, emit } from './transportBase.js';

/** Public STUN. Two providers because one of them will be blocked on someone's network. */
export const STUN_URLS = Object.freeze([
  'stun:stun.l.google.com:19302',
  'stun:stun1.l.google.com:19302',
  'stun:stun.cloudflare.com:3478',
]);

/**
 * Does this entry carry at least one turn/turns url? The room's server-minted
 * list is the only source of relay candidates; the built-in STUN list never is.
 */
export function entryHasTurn(entry) {
  if (!entry) return false;
  const urls = Array.isArray(entry.urls) ? entry.urls : [entry.urls];
  return urls.some((u) => /^turns?:/i.test(String(u || '')));
}

/**
 * THE ROOM'S ENTRIES, TURN URLS ONLY (2026-08-05).
 *
 * metered's credentials endpoint answers with `stun:` urls alongside its turn
 * ones, and on the cellular play-test every single one of them 701'd:
 *   stun:global.relay.metered.ca:80 / :443, stun:stun.relay.metered.ca:80 —
 *   "STUN binding request timed out".
 * A dead STUN url is not free. libwebrtc runs the full binding retransmit
 * ladder (~9.5 s) before it gives up, and iceGatheringState stays 'gathering'
 * the whole time, so three dead urls are three parallel legs holding the
 * gathering phase open for most of the ICE budget — for candidates we do not
 * need, because STUN_URLS above already covers srflx with three public
 * providers and a TURN allocation yields its own srflx on the way through.
 *
 * So: keep every entry that can produce a RELAY candidate, and inside it keep
 * only the turn/turns urls. Entries with no turn url at all are dropped whole.
 * A server that hands out nothing but stun therefore contributes nothing, which
 * is exactly right — that is the STUN-only case the built-in list already is.
 */
export function turnOnlyIceServers(entries) {
  if (!Array.isArray(entries)) return [];
  const out = [];
  for (const e of entries) {
    if (!entryHasTurn(e)) continue;
    const urls = (Array.isArray(e.urls) ? e.urls : [e.urls])
      .filter((u) => /^turns?:/i.test(String(u || '')));
    if (!urls.length) continue;
    const kept = { urls };
    if (e.username !== undefined) kept.username = e.username;
    if (e.credential !== undefined) kept.credential = e.credential;
    out.push(kept);
  }
  return out;
}

/**
 * How long the ICE attempt gets, from the moment negotiation starts.
 * See GoonConsts.IceRelayGraceMs for the whole argument; the short version is
 * that a relayed pair on a carrier link cannot be judged in ten seconds, and a
 * STUN-only room has nothing to wait for.
 */
export function iceBudgetMs(turnConfigured) {
  return GoonConsts.IceTimeoutMs + (turnConfigured ? GoonConsts.IceRelayGraceMs : 0);
}

/**
 * Is the browser still DOING something, as of `iceConnectionState`? Gate for the
 * one-shot IceProgressGraceMs extension.
 *
 *   checking   — pairs are being probed right now.
 *   connected  — a pair WON; the only thing left is DTLS + SCTP, which over a
 *                relay is another couple of round trips. Giving up here would
 *                throw away a working link.
 *   completed  — same, with gathering finished too.
 *
 * 'new' means nothing has started (no extension: something is wrong upstream),
 * 'disconnected' means a pair died, and 'failed'/'closed' are terminal — the
 * transport already folds on those via pc.onconnectionstatechange.
 */
export function iceChecksInFlight(iceConnectionState) {
  const s = String(iceConnectionState || '');
  return s === 'checking' || s === 'connected' || s === 'completed';
}

const CHANNEL_LABEL = 'goon';

/** The bulk channel. Negotiated out-of-band, so both roles create it and neither announces it. */
const MEDIA_CHANNEL_LABEL = 'goon-media';
/**
 * ODD BY DESIGN. Non-negotiated channels take SCTP stream ids by role: the DTLS client (here the
 * offerer = the host) gets EVEN ids, the DTLS server ODD. Only the host ever calls
 * `createDataChannel` in-band and it gets id 0, so the whole odd space is unused and an explicit
 * odd id cannot collide.
 */
const MEDIA_CHANNEL_ID = 1;

const SIGNAL_POLL_MS = 2000;          // setup only; stops when the channel opens
const SIGNAL_FAILURE_LIMIT = 5;
// Bounded so a peer that never comes back can't grow this without limit.
const RESUME_BUFFER_MAX = 128;

function hasWebRtc() {
  return typeof RTCPeerConnection !== 'undefined';
}

export class GoonWebRtcTransport extends GoonTransportBase {
  /**
   * @param {object} o
   * @param {boolean} o.isHost host mints the invite and owns the match clock
   * @param {GoonSignalingClient} [o.signaling] shared client — pass one in so a fallback relay reuses the room
   * @param {object} [o.logger]
   * @param {string} [o.displayName]
   */
  constructor({ isHost = false, signaling = null, logger = null, displayName = '' } = {}) {
    super({ isHost, tag: isHost ? 'GoonP2P:host' : 'GoonP2P:guest', logger });

    this._ownsSignaling = !signaling;
    this._signaling = signaling || new GoonSignalingClient({ logger });
    this._role = isHost ? 'host' : 'guest';
    this._displayName = displayName;

    this._outSignals = [];
    this._wakeResolve = null;
    this._pendingRemoteCandidates = [];
    this._resumeBuffer = [];

    this._cancelled = false;
    this._pumping = false;
    this._signalCursor = 0;
    this._remoteDescriptionSet = false;
    this._negotiationStarted = false;
    this._everConnected = false;
    this._opened = false;             // idempotence guard for channel-open
    this._reconnectGraceTimer = null;

    this._pc = null;
    this._dc = null;
    this._mdc = null;                 // the bulk channel; null = the feature is simply absent
    this._bulkMessageListeners = new Set();
    this._bulkStateListeners = new Set();
    this._bulkOpen = false;           // idempotence guard for the bulk open/closed reports

    this._code = null;
    this._token = null;
    this._iceFailed = false;
    this._peerDisplayName = null;

    /**
     * Did the ROOM hand us TURN credentials? Decided once, in _startPeer, off
     * the entries the signaling client recorded — and it is what buys the
     * attempt GoonConsts.IceRelayGraceMs on top of the base budget. Deliberately
     * "were relay credentials in play", not "did we gather a relay candidate":
     * the relay candidate is exactly the one that arrives LATE, so making the
     * extension depend on having already seen it would arm the longer budget
     * only after the short one had already killed the attempt.
     */
    this._turnConfigured = false;
    /**
     * Diagnostics only (the fallback breadcrumb + the grace warn): did either
     * side ever produce a `typ relay` candidate? Nothing branches on it.
     */
    this._sawLocalRelay = false;
    this._sawRemoteRelay = false;
    this._pairLogged = false;         // one-shot guard for the selected-pair breadcrumb
  }

  /** Invite code for this room. Survives an ICE failure so the relay can adopt it. */
  get code() { return this._code; }
  /** Room token for this side. Same lifetime note as `code`. */
  get token() { return this._token; }
  /** Set when ICE gave up. The caller's cue to open a relay transport on this room. */
  get iceFailed() { return this._iceFailed; }
  /** Display name the server reported for the opponent (guest side, from /join). */
  get peerDisplayName() { return this._peerDisplayName; }

  /** Wake the signal pump NOW — see GoonRelayTransport.nudge for who calls this and why. */
  nudge() { this._wake(); }

  // ------------------------------------------------------------------ setup

  async createInvite() {
    if (!this.isHost) throw new Error('createInvite is the host path.');

    this._setState(GoonTransportState.Signaling, 'minting invite');
    const invite = await this._signaling.createInvite(this._displayName, false);
    if (!invite) {
      this._setLastError(this._signaling.lastError);
      this._setState(GoonTransportState.Disconnected, this.lastError);
      return null;
    }

    this._code = invite.code;
    this._token = invite.token;
    this._startSignalPump();
    return invite.code;
  }

  async join(inviteCode) {
    if (this.isHost) throw new Error('join is the guest path.');

    this._setState(GoonTransportState.Signaling, 'redeeming code');
    const joined = await this._signaling.join(inviteCode, this._displayName);
    if (!joined) {
      this._setLastError(this._signaling.lastError);
      this._setState(GoonTransportState.Disconnected, this.lastError);
      return false;
    }

    this._code = normalizeCode(inviteCode);
    this._token = joined.token;
    this._peerDisplayName = joined.peerDisplayName;
    this._startSignalPump();
    return true;
  }

  /**
   * Resolves when the data channel is open, or when ICE gives up (iceBudgetMs after negotiation
   * STARTS, plus at most one IceProgressGraceMs extension), or — guest only — when no offer ever
   * arrives (GoonConsts.NoOfferTimeoutMs after the join). False means "fall back to relay" — check
   * `iceFailed` to tell that apart from a signaling failure.
   *
   * THE BUDGET IS NOT A CONSTANT ANY MORE (2026-08-05). A STUN-only room still fails at
   * GoonConsts.IceTimeoutMs; a room carrying TURN credentials gets IceRelayGraceMs on top, and one
   * further IceProgressGraceMs if the browser is demonstrably still checking when that runs out.
   * The whole argument lives on GoonConsts.IceRelayGraceMs — in one line: the flat 10 s deadline
   * was killing cellular duels that were still mid-handshake, and because the relay fallback is
   * terminal that silently disabled media transfer for the rest of the match.
   */
  async waitForChannel() {
    let budgetMs = GoonConsts.IceTimeoutMs;
    let deadline = Date.now() + budgetMs;
    let negotiationSeen = false;
    let graceUsed = false;
    // GUEST ONLY: a joined guest is OWED an offer — it redeemed the code, the host learns
    // `peer_joined` on its next poll and offers immediately. If none arrives inside
    // NoOfferTimeoutMs the host side is dead or unreachable, and without this budget the
    // guest sat on "joining…" forever (the ICE budget below never starts until an offer
    // lands). The HOST keeps its untimed wait on purpose: its pre-negotiation phase is a
    // human typing a code, and that must not eat into any budget.
    const noOfferDeadline = this.isHost ? Infinity : Date.now() + GoonConsts.NoOfferTimeoutMs;

    while (!this._cancelled && !this.isDisposed) {
      if (this.state === GoonTransportState.ConnectedP2P) return true;
      if (this.state === GoonTransportState.Disconnected && this.lastError !== null) return false;
      if (this.state === GoonTransportState.Closed) return false;

      // The ICE budget starts when there is actually an ICE process to time out — waiting for a
      // human to type the code must not eat into it.
      if (!negotiationSeen && this._negotiationStarted) {
        negotiationSeen = true;
        budgetMs = iceBudgetMs(this._turnConfigured);
        deadline = Date.now() + budgetMs;
        // WARN, like the other ICE diagnostics in this file: this is the line that tells the next
        // play-test which budget the attempt actually ran on, which is the difference between
        // "TURN was never in play" and "TURN was in play and still lost".
        this._warn(`ice budget ${budgetMs}ms (turn creds ${this._turnConfigured ? 'present' : 'ABSENT'})`);
      }

      if (!negotiationSeen && Date.now() > noOfferDeadline) {
        // Same shape as an ICE timeout so the caller's relay-fallback ladder runs: a host
        // that is alive but couldn't reach us over P2P signaling still gets its second
        // chance, and a host that is gone fails the relay connect with a real error sheet
        // instead of an infinite spinner.
        this._iceFailed = true;
        this._setLastError('no_offer');
        this._warn(`no offer from the host in ${GoonConsts.NoOfferTimeoutMs}ms — relay fallback`);
        this._setState(GoonTransportState.Disconnected, 'no_offer');
        return false;
      }

      if (negotiationSeen && Date.now() > deadline) {
        const iceState = (this._pc && this._pc.iceConnectionState) || 'none';

        // THE ONE-SHOT EXTENSION. Only when the browser says checks are still running: a pair that
        // is mid-probe, or one that already won and is only waiting on DTLS+SCTP over a relay, is
        // not a link to throw away over a stopwatch. Once, never twice — a second extension would
        // be an unbounded wait dressed as a budget.
        if (!graceUsed && iceChecksInFlight(iceState)) {
          graceUsed = true;
          deadline = Date.now() + GoonConsts.IceProgressGraceMs;
          this._warn(`ice budget ${budgetMs}ms elapsed but checks are still in flight`
            + ` (iceConnectionState=${iceState}, relay candidates local=${this._sawLocalRelay} remote=${this._sawRemoteRelay})`
            + ` — one grace extension of ${GoonConsts.IceProgressGraceMs}ms`);
          await this._sleep(100);
          continue;
        }

        this._iceFailed = true;
        this._setLastError('ice_timeout');
        this._warn(`ICE did not complete in ${budgetMs + (graceUsed ? GoonConsts.IceProgressGraceMs : 0)}ms`
          + ` (iceConnectionState=${iceState}, turn creds ${this._turnConfigured ? 'present' : 'ABSENT'},`
          + ` relay candidates local=${this._sawLocalRelay} remote=${this._sawRemoteRelay}) — relay fallback`);
        this._setState(GoonTransportState.Disconnected, 'ice_timeout');
        return false;
      }

      await this._sleep(100);
    }

    return this.state === GoonTransportState.ConnectedP2P;
  }

  // ------------------------------------------------------------------ signaling pump

  _startSignalPump() {
    if (this._pumping) return;
    this._pumping = true;
    this._signalPump().catch((e) => {
      this._error(`Signaling pump died: ${(e && e.message) || e}`);
      this._setLastError('signaling_error');
      this._setState(GoonTransportState.Disconnected, 'signaling_error');
    });
  }

  /**
   * Post-and-drain against /v2/goon/signal every SIGNAL_POLL_MS, waking early whenever we have
   * something to send. Exits as soon as the channel opens — this mailbox is a setup-only facility
   * and must not keep costing requests during a 12-minute match.
   */
  async _signalPump() {
    let failures = 0;

    while (!this._cancelled && !this.isDisposed) {
      if (this.state === GoonTransportState.ConnectedP2P) break;

      const outgoing = this._outSignals;
      this._outSignals = [];

      const result = await this._signaling.signal(
        this._code || '', this._token || '', this._role, this._signalCursor, outgoing);

      if (this._cancelled || this.isDisposed) return;

      if (!result) {
        // Re-queue what we tried to send; a transient 5xx must not eat the offer.
        this._outSignals = outgoing.concat(this._outSignals);

        failures++;
        if (failures >= SIGNAL_FAILURE_LIMIT) {
          this._setLastError(this._signaling.lastError || 'signaling_failed');
          this._setState(GoonTransportState.Disconnected, this.lastError);
          return;
        }

        const retry = this._signaling.retryAfterSeconds;
        const wait = Number.isFinite(retry) && retry > 0
          ? Math.min(retry, 30) * 1000
          : SIGNAL_POLL_MS * failures;
        await this._sleep(wait);
        continue;
      }

      failures = 0;
      this._signalCursor = result.cursor;

      if (result.peerGone) {
        this._setLastError('peer_left');
        this._setState(GoonTransportState.Disconnected, 'peer left the room');
        return;
      }

      for (const msg of result.messages) await this._handleSignal(msg);

      // Host waits for the guest to appear before spending anything on ICE.
      if (this.isHost && result.peerJoined && !this._negotiationStarted) await this._startPeer(null);

      if (this.state === GoonTransportState.ConnectedP2P) break;

      await this._waitForWake(SIGNAL_POLL_MS);
    }
  }

  _enqueueSignal(kind, data) {
    this._outSignals.push({ kind, data });
    this._wake();
  }

  async _handleSignal(item) {
    switch (item.kind) {
      case 'offer': {
        if (this.isHost) return;                    // hosts make offers, they don't take them
        const offer = parseJson(item.data);
        if (!offer) return;
        await this._startPeer(offer);
        break;
      }

      case 'answer': {
        if (!this.isHost) return;
        const answer = parseJson(item.data);
        if (!answer || !this._pc) return;
        try {
          await this._pc.setRemoteDescription(answer);
        } catch (e) {
          this._warn(`setRemoteDescription(answer) rejected: ${(e && e.message) || e}`);
          this._setLastError('sdp_rejected');
          return;
        }
        this._remoteDescriptionSet = true;
        await this._drainPendingCandidates();
        break;
      }

      case 'ice': {
        const cand = parseJson(item.data);
        if (!cand) return;
        // Candidates routinely land before the description they belong to.
        if (!this._pc || !this._remoteDescriptionSet) {
          this._pendingRemoteCandidates.push(cand);
          return;
        }
        await this._tryAddCandidate(cand);
        break;
      }

      case 'bye':
        this._setLastError('peer_bye');
        this._setState(GoonTransportState.Disconnected, 'peer said bye');
        break;

      default:
        break;
    }
  }

  async _drainPendingCandidates() {
    if (this._pendingRemoteCandidates.length === 0) return;
    const pending = this._pendingRemoteCandidates;
    this._pendingRemoteCandidates = [];
    for (const c of pending) await this._tryAddCandidate(c);
  }

  async _tryAddCandidate(cand) {
    try {
      // Diagnostics only: the SDP candidate line carries its type as `typ relay`. Knowing whether
      // the PEER ever managed a TURN allocation is half of any "why did this pair not form"
      // question, and the phone side never ships us a log of its own.
      if (cand && / typ relay\b/.test(String(cand.candidate || ''))) this._sawRemoteRelay = true;
      if (this._pc) await this._pc.addIceCandidate(cand);
    } catch (e) {
      // One unusable candidate is normal (IPv6-only, a stale srflx); the pair still works.
      this._info(`Rejected a remote ICE candidate: ${(e && e.message) || e}`);
    }
  }

  // ------------------------------------------------------------------ peer connection

  /** @param {object|null} remoteOffer null on the host (we make the offer), the inbound offer otherwise. */
  async _startPeer(remoteOffer) {
    if (this._negotiationStarted) return;
    this._negotiationStarted = true;

    if (!hasWebRtc()) {
      // Not an ICE problem, but the same remedy: the room is live and the pass is burned, so flag
      // it so session.js adopts it over relay instead of folding the lobby.
      this._iceFailed = true;
      this._setLastError('webrtc_unavailable');
      this._setState(GoonTransportState.Disconnected, 'webrtc_unavailable');
      return;
    }

    this._setState(GoonTransportState.ConnectingP2P, this.isHost ? 'offering' : 'answering');

    try {
      /* STUN always; TURN when the ROOM carried credentials (net/signaling.js
       * iceServers — server-minted, short-lived, already sanitized). This is
       * what lets a phone on carrier NAT and a desktop behind a home router
       * actually meet: without a relay candidate those two networks never
       * complete ICE, every duel lands on the HTTP-mailbox fallback, and the
       * media transfer (P2P-only by design) never gets a channel to run on. */
      /* TURN URLS ONLY out of the room's list — see turnOnlyIceServers for why the provider's
       * own `stun:` entries are dropped rather than merged (they are three more dead legs
       * holding iceGatheringState open for most of the budget, on a network where they 701). */
      const roomTurn = turnOnlyIceServers(this._signaling?.iceServers);
      const iceServers = STUN_URLS.map((urls) => ({ urls })).concat(roomTurn);
      /* THE BUDGET SWITCH, decided here and read by waitForChannel: relay credentials in hand
       * means this attempt is allowed to take as long as a relayed cellular pair actually takes. */
      this._turnConfigured = roomTurn.length > 0;
      /* DIAGNOSTIC WARNS, deliberately (2026-08-05): info lines die before the
       * C# log and the phone's ?debug=1 overlay, and this exact spot is where
       * three play-test evenings' worth of "still relayed" questions get their
       * answer — did the room carry TURN entries at all, did the browser take
       * them, and what did gathering actually produce. */
      this._warn(`ice config: ${iceServers.length} entr(ies), ${roomTurn.length} carrying turn urls`
        + ` (room list ${(this._signaling?.iceServers || []).length} raw, stun-only entries dropped)`);
      const pc = new RTCPeerConnection({ iceServers });
      this._pc = pc;
      const iceTally = { host: 0, srflx: 0, relay: 0, prflx: 0 };
      pc.onicecandidateerror = (e) => {
        // 701 = unreachable/DNS (often noise when one of several urls is slow);
        // 401/403 from a TURN url = credentials refused — THE line to look for.
        this._warn(`ice candidate error ${e && e.errorCode}: ${(e && e.errorText) || ''} (${(e && e.url) || '?'})`);
      };
      pc.onicegatheringstatechange = () => {
        if (pc.iceGatheringState === 'complete') {
          this._warn(`ice gathering complete: host=${iceTally.host} srflx=${iceTally.srflx} relay=${iceTally.relay} prflx=${iceTally.prflx}`);
        }
      };

      // SYMMETRIC AND IMMEDIATE, on both roles, before any offer/answer work. A negotiated channel
      // is out-of-band by definition, so both sides must create it themselves and neither will see
      // an `ondatachannel` for it. Spike 0 confirmed the guest may do this before
      // setRemoteDescription (the SCTP transport is created lazily) — if that ever stops being
      // true, this block moves to just after setRemoteDescription below and nothing else changes.
      try {
        const mdc = pc.createDataChannel(MEDIA_CHANNEL_LABEL, {
          negotiated: true, id: MEDIA_CHANNEL_ID, ordered: true,
        });
        this._attachMediaChannel(mdc);
      } catch (e) {
        // The feature is simply ABSENT. NEVER fatal: `supportsBulk` stays false, the transfer queue
        // stays dormant, and the duel behaves exactly as it does today.
        this._mdc = null;
        this._info(`media channel unavailable: ${(e && e.message) || e}`);
      }

      pc.onicecandidate = (e) => {
        // Trickle: every candidate goes out the moment it is gathered.
        if (!e || !e.candidate || this.isDisposed) return;
        const t = String(e.candidate.type || '');
        if (t in iceTally) iceTally[t]++;
        if (t === 'relay') this._sawLocalRelay = true;
        try { this._enqueueSignal('ice', JSON.stringify(e.candidate.toJSON())); }
        catch (err) { this._info(`Failed to queue local candidate: ${(err && err.message) || err}`); }
      };

      pc.onconnectionstatechange = () => this._onPeerStateChanged(pc.connectionState);
      pc.oniceconnectionstatechange = () => this._info(`ICE state ${pc.iceConnectionState}`);

      if (this.isHost) {
        // ordered, no maxRetransmits/maxPacketLifeTime => fully reliable.
        this._attachChannel(pc.createDataChannel(CHANNEL_LABEL, { ordered: true }));

        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        this._enqueueSignal('offer', JSON.stringify({ type: 'offer', sdp: pc.localDescription.sdp }));
      } else {
        // LABEL-GUARDED. Our media channel is negotiated, so it never fires this event — but the
        // unguarded version attached ANY inbound channel and overwrote `this._dc`, which let a
        // hostile peer hijack the guest's game channel by opening a second in-band channel.
        pc.ondatachannel = (e) => {
          const ch = e && e.channel;
          if (!ch) return;
          if (ch.label !== CHANNEL_LABEL) {
            this._warn(`ignoring unexpected in-band data channel '${ch.label}'`);
            try { ch.close(); } catch (_e) { /* already gone */ }
            return;
          }
          this._attachChannel(ch);
        };

        try {
          await pc.setRemoteDescription(remoteOffer);
        } catch (e) {
          // SAME REMEDY AS webrtc_unavailable ABOVE, and for the same reason: the
          // room is live and the pass is burned, so this has to be flagged for
          // the relay or session.js reads it as a signaling-level failure and
          // FOLDS THE LOBBY — which is a guest being evicted to the title screen
          // with nothing said, the 2026-08-04 phone report. An offer this browser
          // will not take is precisely a case a relay carries fine: the SDP never
          // has to be understood by anything but the two peers' own stacks.
          this._warn(`setRemoteDescription(offer) rejected: ${(e && e.message) || e}`);
          this._iceFailed = true;
          this._setLastError('sdp_rejected');
          this._setState(GoonTransportState.Disconnected, 'sdp_rejected');
          return;
        }
        this._remoteDescriptionSet = true;
        await this._drainPendingCandidates();

        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        this._enqueueSignal('answer', JSON.stringify({ type: 'answer', sdp: pc.localDescription.sdp }));
      }
    } catch (e) {
      this._error(`Peer connection setup failed: ${(e && e.message) || e}`);
      this._iceFailed = true;
      this._setLastError('peer_setup_failed');
      this._setState(GoonTransportState.Disconnected, 'peer_setup_failed');
    }
  }

  _onPeerStateChanged(s) {
    this._info(`Peer connection ${s}`);
    if (this.isDisposed) return;

    switch (s) {
      case 'connected':
        this._cancelReconnectGrace();
        break;

      case 'disconnected':
      case 'failed':
        if (this._everConnected) this._beginReconnectGrace(String(s));
        else {
          this._iceFailed = true;
          this._setLastError('ice_failed');
          this._setState(GoonTransportState.Disconnected, 'ice_failed');
        }
        break;

      case 'closed':
        if (this.state !== GoonTransportState.Closed) {
          this._setState(GoonTransportState.Disconnected, 'peer connection closed');
        }
        break;

      default:
        break;
    }
  }

  /**
   * Wire one data channel. IDEMPOTENT about the open event on purpose: the ANSWERING side's
   * ondatachannel routinely fires with readyState already 'open' (the SCTP stream is negotiated
   * before the event reaches us), so subscribing to onopen alone would strand the guest until the
   * ICE watchdog needlessly dropped a perfectly good match to relay. Never await onopen.
   */
  _attachChannel(dc) {
    if (!dc) return;
    this._dc = dc;
    try { dc.binaryType = 'arraybuffer'; } catch (_e) { /* not all impls expose it */ }

    dc.onopen = () => this._handleChannelOpen();

    dc.onmessage = (e) => {
      if (this.isDisposed || !e) return;
      try {
        const data = e.data;
        if (typeof data === 'string') this._handleIncomingRaw(data);
        else if (data && typeof data.byteLength === 'number') this._handleIncomingRaw(decodeUtf8(data));
      } catch (err) {
        this._warn(`Failed to read inbound frame: ${(err && err.message) || err}`);
      }
    };

    dc.onclose = () => {
      if (this.isDisposed) return;
      this._info('Data channel closed');
      if (this._everConnected && this.state !== GoonTransportState.Closed) {
        this._beginReconnectGrace('data channel closed');
      }
    };

    dc.onerror = (e) => this._warn(`Data channel error: ${(e && (e.message || (e.error && e.error.message))) || 'unknown'}`);

    if (dc.readyState === 'open') this._handleChannelOpen();
  }

  // ------------------------------------------------------------------ bulk channel

  /**
   * Wire the media channel. RAW PASSTHROUGH BOTH WAYS: nothing here parses, validates or
   * size-guards a bulk frame — `net/mediaChannel.js` owns the whole protocol and this transport
   * is only the pipe. Same idempotent-open discipline as `_attachChannel`.
   */
  _attachMediaChannel(mdc) {
    if (!mdc) return;
    this._mdc = mdc;

    try { mdc.binaryType = 'arraybuffer'; } catch (_e) { /* not all impls expose it */ }
    try { mdc.bufferedAmountLowThreshold = BULK_LOW_WATER; } catch (_e) { /* nor this */ }

    mdc.onopen = () => this._setBulkState('open');
    mdc.onclose = () => this._setBulkState('closed');

    mdc.onmessage = (e) => {
      if (this.isDisposed || !e) return;
      // string OR ArrayBuffer, exactly as it arrived.
      emit(this._bulkMessageListeners, e.data, this._log, this._tag, 'bulkMessage');
    };

    // Additive third state: the pump's own 50 ms watchdog is the guaranteed path, this just makes
    // the common case immediate. A listener that only tests for 'open'/'closed' ignores it.
    mdc.onbufferedamountlow = () => {
      if (this.isDisposed) return;
      emit(this._bulkStateListeners, 'low', this._log, this._tag, 'bulkStateChanged');
    };

    mdc.onerror = (e) => this._warn(`Media channel error: ${(e && (e.message || (e.error && e.error.message))) || 'unknown'}`);

    if (mdc.readyState === 'open') this._setBulkState('open');
  }

  _setBulkState(state) {
    if (this.isDisposed) return;
    const next = state === 'open';
    if (next === this._bulkOpen) return;
    this._bulkOpen = next;
    this._info(`Media channel ${state}`);
    emit(this._bulkStateListeners, state, this._log, this._tag, 'bulkStateChanged');
  }

  /**
   * P2P ONLY, and explicitly NOT `isConnected`: CONNECTED_STATES treats ConnectedRelay as
   * connected too, and the relay is the one transport media must never touch.
   */
  get supportsBulk() {
    return this.state === GoonTransportState.ConnectedP2P
      && !!this._mdc && this._mdc.readyState === 'open';
  }

  /**
   * Bypasses `_sendRaw` entirely, so bulk data can never enter the 128-frame resume buffer.
   * @returns {boolean} false whenever the bytes did NOT reach the channel — callers branch on it.
   */
  sendBulk(data) {
    if (this.isDisposed) return false;
    const mdc = this._mdc;
    if (!mdc || mdc.readyState !== 'open') return false;
    try { mdc.send(data); return true; }
    catch (e) { this._warn(`Bulk send failed: ${(e && e.message) || e}`); return false; }
  }

  get bulkBufferedAmount() {
    const mdc = this._mdc;
    const n = mdc ? Number(mdc.bufferedAmount) : 0;
    return Number.isFinite(n) ? n : 0;
  }

  get bulkLowThreshold() { return BULK_LOW_WATER; }

  /** @returns {() => void} unsubscribe */
  onBulkMessage(fn) {
    if (typeof fn !== 'function') return () => {};
    this._bulkMessageListeners.add(fn);
    return () => this._bulkMessageListeners.delete(fn);
  }

  /** @returns {() => void} unsubscribe */
  onBulkStateChanged(fn) {
    if (typeof fn !== 'function') return () => {};
    this._bulkStateListeners.add(fn);
    return () => this._bulkStateListeners.delete(fn);
  }

  /** The channel is usable. Idempotent — see the note in _attachChannel. */
  _handleChannelOpen() {
    if (this.isDisposed || this._opened) return;
    this._opened = true;

    this._everConnected = true;
    this._cancelReconnectGrace();
    // _setState kicks off the clock sync (autoClockSync) — GoonTransportBase.BeginClockSync.
    this._setState(GoonTransportState.ConnectedP2P, 'data channel open');
    this._flushResumeBuffer();
    // Fire-and-forget: getStats is async and nothing may wait on a diagnostic. The .catch is not
    // decoration — an unhandled rejection out of a LOG LINE would be a genuinely stupid way to
    // take down a connection that just succeeded.
    try { const p = this._logSelectedPair(); if (p && p.catch) p.catch(() => {}); }
    catch (_e) { /* diagnostics never bite */ }
  }

  /**
   * THE BREADCRUMB THAT ANSWERS "how did this duel actually connect" (2026-08-05). One warn, once
   * per transport, naming the nominated pair's candidate types — host/srflx/relay/prflx on each
   * side. `relay/…` is the receipt that TURN did its job; `srflx/srflx` on a cellular guest means
   * the phone punched through and the whole TURN apparatus was never needed.
   *
   * It is a warn because info lines do not survive the trip to the C# log or the ?debug=1 overlay,
   * and this line is the entire point of the next cellular play-test.
   *
   * BOTH STAT SHAPES. Chromium hangs the pair off `transport.selectedCandidatePairId`; Safari (the
   * iPhone side, and the one we most need to hear from) marks the pair itself with `selected`.
   * Neither is guaranteed, so the whole thing degrades to silence rather than throwing.
   */
  async _logSelectedPair() {
    if (this._pairLogged) return;
    const pc = this._pc;
    if (!pc || typeof pc.getStats !== 'function') return;
    this._pairLogged = true;
    try {
      const stats = await pc.getStats();
      if (!stats || typeof stats.forEach !== 'function') return;

      const byId = new Map();
      stats.forEach((r) => { if (r && r.id) byId.set(r.id, r); });

      let pair = null;
      stats.forEach((r) => {
        if (!r) return;
        if (r.type === 'transport' && r.selectedCandidatePairId && byId.has(r.selectedCandidatePairId)) {
          pair = byId.get(r.selectedCandidatePairId);
        }
      });
      if (!pair) {
        stats.forEach((r) => {
          if (pair || !r || r.type !== 'candidate-pair') return;
          if (r.selected === true || (r.nominated === true && r.state === 'succeeded')) pair = r;
        });
      }
      if (!pair) { this._warn('p2p link up — the browser named no selected candidate pair'); return; }

      const local = byId.get(pair.localCandidateId);
      const remote = byId.get(pair.remoteCandidateId);
      const lt = (local && local.candidateType) || '?';
      const rt = (remote && remote.candidateType) || '?';
      const proto = (local && (local.relayProtocol || local.protocol)) || '?';
      this._warn(`p2p link up via ${lt}/${rt} over ${proto}`
        + ` (turn creds ${this._turnConfigured ? 'present' : 'ABSENT'})`);
    } catch (e) {
      this._info(`selected-pair stats unavailable: ${(e && e.message) || e}`);
    }
  }

  // ------------------------------------------------------------------ reconnect / resume

  /**
   * Hold the match together across a blip. For GoonConsts.ReconnectGraceMs the transport reports
   * Reconnecting and buffers outbound frames instead of dropping them; if SCTP comes back the
   * buffer flushes in order and the match never noticed. Past the grace we surface Disconnected and
   * it becomes the match engine's abandon flow — never recorded as a mercy.
   */
  _beginReconnectGrace(reason) {
    if (this._reconnectGraceTimer) return;

    this._setState(GoonTransportState.Reconnecting, reason);
    // Arm the open handler again so a channel that comes back flushes the resume buffer.
    this._opened = false;

    this._reconnectGraceTimer = setTimeout(() => {
      this._reconnectGraceTimer = null;
      if (this.isDisposed) return;
      if (this.state !== GoonTransportState.Reconnecting) return;
      this._setLastError('reconnect_timeout');
      this._warn(`Peer did not resume within ${GoonConsts.ReconnectGraceMs}ms`);
      this._setState(GoonTransportState.Disconnected, 'reconnect_timeout');
    }, GoonConsts.ReconnectGraceMs);
    if (typeof this._reconnectGraceTimer.unref === 'function') this._reconnectGraceTimer.unref();
  }

  _cancelReconnectGrace() {
    if (!this._reconnectGraceTimer) return;
    clearTimeout(this._reconnectGraceTimer);
    this._reconnectGraceTimer = null;
  }

  _flushResumeBuffer() {
    const dc = this._dc;
    if (!dc || dc.readyState !== 'open' || this._resumeBuffer.length === 0) return;

    let sent = 0;
    while (this._resumeBuffer.length > 0) {
      const json = this._resumeBuffer.shift();
      try { dc.send(json); sent++; }
      catch (e) {
        this._resumeBuffer.unshift(json);
        this._warn(`Resume flush failed after ${sent} frames: ${(e && e.message) || e}`);
        break;
      }
    }
    if (sent > 0) this._info(`Resumed: flushed ${sent} buffered frames`);
  }

  // ------------------------------------------------------------------ send

  async _sendRaw(json) {
    if (this.isDisposed) return;

    const dc = this._dc;
    if (dc && dc.readyState === 'open') {
      try { dc.send(json); return; }
      catch (e) { this._warn(`send failed — buffering for resume: ${(e && e.message) || e}`); }
    }

    if (this.state === GoonTransportState.Reconnecting || this.state === GoonTransportState.ConnectingP2P) {
      if (this._resumeBuffer.length < RESUME_BUFFER_MAX) this._resumeBuffer.push(json);
      else this._warn('Resume buffer full — dropping a frame');
    } else {
      this._warn(`Dropping a frame — channel not open (state ${this.state})`);
    }
  }

  // ------------------------------------------------------------------ teardown

  async close() {
    if (this.state === GoonTransportState.Closed) return;

    // Best-effort courtesy so the peer stops polling immediately instead of waiting out the room
    // TTL. Never blocks the close.
    try {
      if (this._code && this._token && this.state === GoonTransportState.Signaling) {
        await this._signaling.signal(this._code, this._token, this._role, this._signalCursor,
          [{ kind: 'bye', data: '' }]);
      }
    } catch (e) { this._info(`bye failed (harmless): ${(e && e.message) || e}`); }

    this._setState(GoonTransportState.Closed, 'closed by us');
    this._tearDown();
  }

  _tearDown() {
    this._cancelReconnectGrace();
    this._cancelled = true;
    this._wake();

    const dc = this._dc;
    const mdc = this._mdc;
    const pc = this._pc;
    this._dc = null;
    this._mdc = null;
    this._pc = null;

    // Tell anything mid-transfer the pipe is gone before the handles disappear.
    if (this._bulkOpen) {
      this._bulkOpen = false;
      emit(this._bulkStateListeners, 'closed', this._log, this._tag, 'bulkStateChanged');
    }

    try { if (dc) dc.close(); } catch (_e) { /* already gone */ }
    try { if (mdc) mdc.close(); } catch (_e) { /* already gone */ }
    try { if (pc) pc.close(); } catch (_e) { /* already gone */ }
    try { this._clock.stop(); } catch (_e) { /* already gone */ }
  }

  _disposeCore() {
    this._tearDown();
    // The base clears the message/state listeners; the bulk pair is ours.
    this._bulkMessageListeners.clear();
    this._bulkStateListeners.clear();
    if (this._ownsSignaling) this._signaling.dispose();
  }

  // ------------------------------------------------------------------ wake / sleep

  _wake() {
    const r = this._wakeResolve;
    this._wakeResolve = null;
    if (r) r();
  }

  _waitForWake(ms) {
    return new Promise((resolve) => {
      let done = false;
      const finish = () => { if (done) return; done = true; clearTimeout(timer); resolve(); };
      const timer = setTimeout(finish, ms);
      if (typeof timer.unref === 'function') timer.unref();
      this._wakeResolve = finish;
    });
  }

  _sleep(ms) {
    return new Promise((resolve) => {
      const t = setTimeout(resolve, ms);
      if (typeof t.unref === 'function') t.unref();
    });
  }
}

function parseJson(s) {
  try {
    const v = JSON.parse(String(s || ''));
    return (v && typeof v === 'object') ? v : null;
  } catch (_e) { return null; }
}

function decodeUtf8(buf) {
  if (typeof TextDecoder === 'function') return new TextDecoder().decode(buf);
  let out = '';
  const view = new Uint8Array(buf);
  for (let i = 0; i < view.length; i++) out += String.fromCharCode(view[i]);
  return out;
}

export { hasWebRtc };
