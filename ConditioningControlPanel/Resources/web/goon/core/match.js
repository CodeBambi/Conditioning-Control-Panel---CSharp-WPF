// The match state machine — port of Services/GoonGame/GoonMatchService.cs.
//
//   Idle -> Lobby -> Consent -> Draft -> Countdown -> Live -> SuddenDeath -> Recap -> Idle
//
// Owns a transport (injected), the SHARED endurance ramp rolled from the draft agreement (the
// intersection of both players' allowed sets) and the combined match seed, the scoring/charge
// economy, the mercy and abandon flows, the receiver-side payload gate and the two-way result
// handshake.
//
// DRAFT (2026-08-03 redesign): both players toggle what they ALLOW, the pool is the intersection,
// any toggle clears BOTH signatures, and the ramp both sides run is one seeded roll over that
// pool — identical instants, identical elements. Bubbles are an always-on baseline underneath it.
//
// It PLANS but never PERFORMS: element cues and admitted payloads leave as events; the exec/
// layer does the fan-out. Node-import-safe — no DOM at import, no DOM at runtime.
//
// Safety rails honoured here (protocol §11):
//  * Mercy is available in EVERY phase and always ends the match locally, even if the wire is
//    dead. Pre-Live it degrades to a clean cancel that never reaches the ledger.
//  * No payload kind can touch panic/lockdown/tray/session state — no such message exists.
//  * The RECEIVER enforces the economy: burst/gap rate limit, charge cost against the opponent's
//    last self-reported meter, one heavy per match, schedule-buffer clamp and text cap —
//    regardless of what the wire claims.
//
// ---------------------------------------------------------------------------------------------
// TRANSPORT SURFACE CONSUMED (duck-typed; net/ owns the implementations). Mechanical camelCase of
// C# IGoonTransport / GoonTransportBase — the transport OWNS the MatchClock and gives it first
// refusal on inbound frames, exactly as GoonTransportBase does, so clock ping/pong never reaches
// this class:
//   transport.state                        -> GoonTransportState code
//   transport.clock                        -> MatchClock (nowMatchMs(), matchMsToLocal(), isSynced)
//   transport.onMessageReceived(fn) -> unsub        (alias accepted: onMessage)
//   transport.onStateChanged(fn)    -> unsub        (alias accepted: onState)
//   transport.sendAsync(msg)        -> Promise      (alias accepted: send)
//   transport.createInviteAsync(signal) -> Promise<string|null>   (alias: createInvite)
//   transport.joinAsync(code, signal)   -> Promise<boolean>       (alias: join)
//
// ---------------------------------------------------------------------------------------------
// SUDDEN-DEATH RUNNER SEAM — mechanical camelCase of C# IGoonSuddenDeathRunner. rounds/* owns the
// implementation; these names are the contract, do not rename:
//   match.suddenDeathRunner  (settable; `match.runner` is an alias onto the same backing field)
//   runner.onRoundWon(fn(roundNo))          -> unsub
//   runner.onRoundLost(fn(roundNo))         -> unsub
//   runner.onNetLossReached(fn(localLost))  -> unsub     localLost === true means WE lost
//   runner.startAsync(context, signal)      -> Promise   (C# StartAsync(ctx, ct); alias: start)
//   runner.handleMessage(msg)                            round + mercy traffic, forwarded by us
//   runner.stopAsync()                      -> Promise   idempotent (alias: stop)
// context = {transport, clock, rngFactory, matchSeed:bigint, isHost, localMode, remoteMode,
//            netLossThreshold, allowedRoundKinds}
// The runner NEVER subscribes to the transport itself — this class owns the message pump.
//
// ---------------------------------------------------------------------------------------------
// EXECUTOR SEAM (exec/ subscribes):
//   onElementStartRequested(fn(cue))     cue = {element, intensity, durationMs, elapsedMs}
//   onElementIntensityChanged(fn(cue))
//   onElementStopRequested(fn(cue))
//   onPayloadAccepted(fn({payload, fireAtLocalMs}))
//   ... and calls back notifyInboundPayloadFinished(payloadId, endured) when it is done.

import {
  GoonAttentionMode, GoonElement, GoonEndReason, GoonMatchPhase, GoonPayloadKind, GoonTransportState,
  GoonConsts, clampWindowCount, costOf, enumName, isClockMessage,
  makeConsent, makeDraft, makeEmote, makeHello, makeMatchStart, makeMediaPrep, makeMercy,
  makePayloadReceipt, makeResult, makeTick, makePayload, makeVoice, peerSpeaksVoice, VOICE_SUBS,
} from './contracts.js';
import { GoonRng, combineSeeds, newSeedContribution } from './rng.js';
import { localMonotonicMs } from './clock.js';
import { ticker } from './scheduler.js';
import { GoonPayloadRateLimiter, GoonReceiptStatus, GoonScoring } from './scoring.js';
import {
  ALWAYS_ON_ELEMENT, GoonCueAction, MIN_ALLOWED_ELEMENTS, PoolV1, buildRamp, defaultAllowed, isValidAllowed,
  isValidSharedPool, matchRiskTier, normalizeAllowed, sharedPool,
} from './draft.js';
import { UNIVERSAL_ROUND, intersect, local as localCapsOf } from './caps.js';
import { EMOTE_ICON_MAX_CHARS, EMOTE_TEXT_MAX_CHARS, TEXT_MAX_CHARS, sanitizeName, sanitizeText } from '../exec/sanitize.js';

// ---- local tuning, deliberately NOT in GoonConsts (mirrors the C# private consts) ----
const COUNTDOWN_MS = 5000;
const PHASE_TIMER_INTERVAL_MS = 200;
const RESULT_HANDSHAKE_TIMEOUT_MS = 10000;
const DRAW_WINDOW_MS = 1500;
const PAYLOAD_SCHEDULE_BUFFER_MS = 1500;   // >= GoonConsts.MinScheduleBufferMs
const NO_CAM_CHECK_INTERVAL_MS = 90000;
const MAX_PAYLOAD_DURATION_MS = 180000;

/** Freshness of the opponent's state ticks. */
export const GoonConnectionHealth = Object.freeze({ Fresh: 0, Wobbly: 1, Dead: 2 });

/** Mirror of the opponent, rebuilt from every tick. Bindable by the HUD. */
export class GoonOpponentState {
  constructor() {
    this.displayName = '';
    this.appVersion = '';
    this.platform = '';           // windows | android | ios | web
    this.toyConnected = false;
    this.attentionMode = GoonAttentionMode.NoCam;

    this.score = 0;
    this.attentionPct = 100;
    this.charges = 0;
    this.toyActive = false;
    this.closeness = null;
    this.activeEffects = [];
    /* How many FLOATING VIDEO WINDOWS they have up right now, 0..4 (tick `vwin`, added
       2026-08-04). It keeps the wire's name rather than a camelCase one because it is the one
       member here that is a straight copy of an OPTIONAL field: grep `vwin` and you get the
       contract, the serializer, this line and the monitor that draws it, with nothing in between
       to translate. 0 is also what a peer that never heard of the field looks like. */
    this.vwin = 0;

    this.lastTickLocalMs = 0;
    this.health = GoonConnectionHealth.Fresh;
    this.hasSeenTick = false;
  }
}

/**
 * The signed end-of-match record. Each side's own score is authoritative for that side; the two
 * clients countersign the outcome. Disagreement is recorded as disputed and the uncontested parts
 * still earn their cosmetics.
 */
export class GoonMatchResult {
  constructor(o = {}) {
    this.endReason = o.endReason ?? GoonEndReason.Mercy;
    this.winnerIsHost = o.winnerIsHost ?? null;   // null = draw
    this.localIsHost = !!o.localIsHost;
    this.hostScore = o.hostScore ?? 0;
    this.guestScore = o.guestScore ?? 0;
    this.survivedMs = o.survivedMs ?? 0;

    this.agreed = false;
    this.disputed = false;
    this.remoteClaim = null;
    this.countsForLedger = o.countsForLedger ?? true;
  }

  get localWon() { return this.winnerIsHost !== null && this.winnerIsHost === this.localIsHost; }
  get localScore() { return this.localIsHost ? this.hostScore : this.guestScore; }
  get remoteScore() { return this.localIsHost ? this.guestScore : this.hostScore; }
}

// ------------------------------------------------------------------ small helpers

function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }

/** C# Math.Round(double) is banker's rounding. */
function roundHalfToEven(x) {
  const f = Math.floor(x);
  const d = x - f;
  if (d > 0.5) return f + 1;
  if (d < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
}

/** Resolve a duck-typed method by preferred name, then aliases. */
function bindFn(obj, names) {
  if (!obj) return null;
  for (const n of names) if (typeof obj[n] === 'function') return obj[n].bind(obj);
  return null;
}

function makeEvent() {
  const set = new Set();
  return {
    on(fn) {
      if (typeof fn !== 'function') return () => {};
      set.add(fn);
      return () => set.delete(fn);
    },
    emit(arg, onError) {
      if (set.size === 0) return;
      for (const fn of Array.from(set)) {
        try { fn(arg); } catch (e) { if (onError) onError(e); }
      }
    },
    clear() { set.clear(); },
  };
}

export class GoonMatchService {
  /**
   * @param {object} transport see the TRANSPORT SURFACE block above
   * @param {boolean} isHost
   * @param {object} [o]
   * @param {(seed:bigint)=>object} [o.rngFactory] defaults to the real xoshiro256** GoonRng
   * @param {object} [o.logger] console-shaped
   * @param {string} [o.displayName] C# reads App.Settings; the page must inject it
   * @param {string} [o.appVersion]  C# reads UpdateService.AppVersion; likewise
   * @param {object} [o.caps] override for what we advertise (see caps.js local())
   */
  constructor(transport, isHost, {
    rngFactory = (seed) => new GoonRng(seed),
    logger = null,
    displayName = 'Player',
    appVersion = '',
    caps = null,
    tag = 'GG',
  } = {}) {
    if (!transport) throw new Error('GoonMatchService: transport required');
    this._transport = transport;
    this._isHost = !!isHost;
    this._rngFactory = typeof rngFactory === 'function' ? rngFactory : ((seed) => new GoonRng(seed));
    this._log = logger || (typeof console !== 'undefined' ? console : null);
    this._tag = tag;

    this._send$ = bindFn(transport, ['sendAsync', 'send']);
    this._createInvite$ = bindFn(transport, ['createInviteAsync', 'createInvite']);
    this._join$ = bindFn(transport, ['joinAsync', 'join']);

    this._liveWatchStartLocalMs = null;
    this._liveWatchStoppedLocalMs = null;
    this._inboundLimiter = new GoonPayloadRateLimiter();
    this._outboundLimiter = new GoonPayloadRateLimiter();
    this._outboundCosts = new Map();
    this._activeElements = new Set();
    // The agreement: two allowed sets, two signatures, one intersection.
    this._localAllowed = [];
    this._remoteAllowed = [];
    this._sharedPool = [];
    this._draftResolved = false;

    this._liveTicker = null;    // 1 s score/ramp/tick pump
    this._phaseTicker = null;   // 200 ms countdown + handshake deadlines

    this._remoteHello = null;
    this._helloSent = false;
    this._localConsentConfirmed = false;
    this._remoteConsentConfirmed = false;
    this._localDraftConfirmed = false;
    this._remoteDraftConfirmed = false;
    this._startProposed = false;

    // P2P media transfer. THREE independent booleans, never one:
    //   _peerSupportsTransfer  their BUILD speaks the protocol (hello caps, version discriminator)
    //   _localMediaTransfer    OUR opt-in       (per-side declaration on the consent frame)
    //   _remoteMediaTransfer   THEIR opt-in     (the same field, as they last declared it)
    // None of them is a consent TERM — see the guard comment on sameSheet at the foot of the file.
    this._peerSupportsTransfer = false;
    this._localMediaTransfer = false;
    this._remoteMediaTransfer = false;

    // VOICE NOTES. The same three booleans, for the same reasons, kept SEPARATE from the media
    // triple above rather than folded into it: they are two different consents (their library vs
    // their actual voice) and neither may ever imply the other. `_peerSupportsVoice` comes off
    // caps.voice — a revision integer, not an entitlement — and is what stops us sending into a
    // build that will drop the frames without a word (the family has no receipts to notice with).
    this._peerSupportsVoice = false;
    this._localVoiceNotes = false;
    this._remoteVoiceNotes = false;

    // "Still assembling a library" (the `media_prep` frame). A PRESENCE HINT and
    // nothing else: no phase, no confirmation and no countdown reads either of
    // these, so a peer that never sends the frame — an older build, the C#
    // client — simply stays `false` and the lobby behaves exactly as it did.
    this._localMediaPrep = false;
    this._remoteMediaPrep = false;

    this._localSeedContribution = null;
    this._remoteSeedContribution = null;

    this._ramp = [];
    this._rampIndex = 0;
    this._rampView = null;      // memoized read-only view of _ramp — see `rampCues`
    this._rampViewSrc = null;
    this._liveDurationMs = 0;
    this._lastTickSentLocalMs = 0;
    this._nextInteractionCheckMs = 0;

    this._payloadSeq = 0;
    this._localHeavyUsed = false;
    this._localHeavyPayloadId = null;
    this._remoteHeavyUsed = false;
    this._opponentChargesKnown = 0;

    this._localMercyMatchMs = null;
    this._remoteMercyMatchMs = null;

    this._ended = false;
    this._finalized = false;
    this._countersignSent = false;
    this._resultDeadlineLocalMs = 0;

    this._runner = null;
    this._runnerUnsubs = [];
    this._runnerAbort = null;
    this._disposed = false;

    this._phase = GoonMatchPhase.Idle;
    this._matchSeed = 0n;
    this._startMatchMs = 0;
    this._lobbyFailureReason = null;
    this._localCloseness = null;
    this._localWindowCount = 0;   // floating video windows WE have up; rides every tick as `vwin`

    this.localAttentionMode = GoonAttentionMode.NoCam;
    this.localToyConnected = false;
    this.localDisplayName = displayName;
    this.localAppVersion = appVersion;

    this._localCaps = caps || localCapsOf();
    this._availableDraftPool = PoolV1.slice();
    this._availablePayloadKinds = Object.values(GoonPayloadKind);
    this._allowedRoundKinds = [UNIVERSAL_ROUND];

    this._consentSheet = makeConsent();
    this._scoring = new GoonScoring(this.localAttentionMode, 0, { logger: this._log });
    this._opponent = new GoonOpponentState();
    this._result = null;

    this._ev = {
      phaseChanged: makeEvent(),
      elementStartRequested: makeEvent(),
      elementIntensityChanged: makeEvent(),
      elementStopRequested: makeEvent(),
      payloadAccepted: makeEvent(),
      payloadRejected: makeEvent(),
      payloadReceiptReceived: makeEvent(),
      consentChanged: makeEvent(),
      draftChanged: makeEvent(),
      opponentStateChanged: makeEvent(),
      connectionHealthChanged: makeEvent(),
      emoteReceived: makeEvent(),
      voiceFrameReceived: makeEvent(),
      mediaPrepChanged: makeEvent(),
      interactionCheckDue: makeEvent(),
      lobbyFailed: makeEvent(),
      matchEnded: makeEvent(),
      resultFinalized: makeEvent(),
    };

    const onMsg = bindFn(transport, ['onMessageReceived', 'onMessage']);
    const onState = bindFn(transport, ['onStateChanged', 'onState']);
    this._transportUnsubs = [];
    if (onMsg) this._transportUnsubs.push(onMsg((m) => this._onMessageReceived(m)) || (() => {}));
    if (onState) this._transportUnsubs.push(onState((s) => this._onTransportStateChanged(s)) || (() => {}));
  }

  // ------------------------------------------------------------ state

  get isHost() { return this._isHost; }
  get phase() { return this._phase; }
  get scoring() { return this._scoring; }
  get opponent() { return this._opponent; }
  get result() { return this._result; }

  /** The sheet both sides must confirm byte-identically before drafting (a `consent` message). */
  get consentSheet() { return this._consentSheet; }
  get localConsentConfirmed() { return this._localConsentConfirmed; }
  get remoteConsentConfirmed() { return this._remoteConsentConfirmed; }

  /** OUR media-transfer opt-in, as it rides every consent frame we send. */
  get localMediaTransfer() { return this._localMediaTransfer; }
  /** THEIR opt-in, as last declared on a consent frame. Absent field -> false. */
  get remoteMediaTransfer() { return this._remoteMediaTransfer; }
  /** Their BUILD advertised `caps.transfer` in the hello. Nothing to do with consent. */
  get peerSupportsTransfer() { return this._peerSupportsTransfer; }
  /**
   * All three, ANDed. Still NOT the whole gate: the sender also needs the host's premium
   * capability (session.caps.mediaTransfer) and `transport.supportsBulk` — see the one-predicate
   * gate in net/mediaQueue.js. This getter is the MATCH's half of it.
   */
  get mediaTransferAgreed() {
    return this._localMediaTransfer && this._remoteMediaTransfer && this._peerSupportsTransfer;
  }

  /** OUR voice-note opt-in, as it rides every consent frame we send. */
  get localVoiceNotes() { return this._localVoiceNotes; }
  /** THEIR opt-in, as last declared on a consent frame. Absent field -> false. */
  get remoteVoiceNotes() { return this._remoteVoiceNotes; }
  /** Their BUILD advertised `caps.voice >= 1` in the hello. Nothing to do with consent. */
  get peerSupportsVoice() { return this._peerSupportsVoice; }
  /**
   * All three, ANDed — the MATCH's half of "is voice live right now". The other half is the
   * PHASE (Countdown/Live/SuddenDeath) and it is applied at the send/receive door rather than
   * here, because this getter is also what the lobby reads while there is no phase to speak of.
   * ui/voice/voiceService.js `available()` is the one predicate that combines both.
   */
  get voiceNotesAgreed() {
    return this._localVoiceNotes && this._remoteVoiceNotes && this._peerSupportsVoice;
  }

  /** The three phases a voice note may cross the wire in. Everything else is silence. */
  get voicePhaseOpen() {
    return this._phase === GoonMatchPhase.Countdown
      || this._phase === GoonMatchPhase.Live
      || this._phase === GoonMatchPhase.SuddenDeath;
  }

  /** Are WE the one still picking media (`media_prep`, as last declared)? */
  get localMediaPrep() { return this._localMediaPrep; }
  /** Are THEY? Absent frame -> false, so an older peer reads as "ready". */
  get remoteMediaPrep() { return this._remoteMediaPrep; }

  /** What WE allow (canonical, always-on element excluded). */
  get localAllowedElements() { return this._localAllowed; }
  /** What THEY allow, as last broadcast. */
  get remoteAllowedElements() { return this._remoteAllowed; }
  /** The intersection — the pool the ramp is rolled from. Both clients compute the same list. */
  get sharedElementPool() { return sharedPool(this._localAllowed, this._remoteAllowed); }
  get localDraftConfirmed() { return this._localDraftConfirmed; }
  get remoteDraftConfirmed() { return this._remoteDraftConfirmed; }
  /** True once both signatures landed on one pair of sets with a workable intersection. */
  get draftResolved() { return this._draftResolved; }
  get minAllowedElements() { return MIN_ALLOWED_ELEMENTS; }

  // LEGACY aliases: the pre-agreement names, kept so ui/soloDriver.js and older tooling keep
  // working. localDraft/remoteDraft are now the ALLOWED sets, not three private picks.
  get localDraft() { return this._localAllowed; }
  get remoteDraft() { return this._remoteAllowed; }
  get localDraftLocked() { return this._localDraftConfirmed; }
  get remoteDraftLocked() { return this._remoteDraftConfirmed; }

  /** @returns {bigint} */
  get matchSeed() { return this._matchSeed; }
  get startMatchMs() { return this._startMatchMs; }

  /** Why the lobby was torn down, when it was (protocol/caps mismatch). */
  get lobbyFailureReason() { return this._lobbyFailureReason; }

  /** Self-reported closeness dial, 0-3 or null. Bluffable BY DESIGN. */
  get localCloseness() { return this._localCloseness; }

  /** Floating video windows WE have up right now (0..4), as the next tick will report them. */
  get localWindowCount() { return this._localWindowCount; }

  /** The opponent's lobby hello (identity + capabilities), once it arrives. */
  get remoteHello() { return this._remoteHello; }
  get localCaps() { return this._localCaps; }
  get remoteCaps() { return this._remoteHello ? this._remoteHello.caps : null; }

  /** The draft pool actually offered: the INTERSECTION of both clients' elements. */
  get availableDraftPool() { return this._availableDraftPool; }
  /** Payload kinds the PEER can actually run — the only ones we may send. */
  get availablePayloadKinds() { return this._availablePayloadKinds; }
  /** Round kinds both clients advertised (ReactionDuel always included). */
  get allowedRoundKinds() { return this._allowedRoundKinds; }

  /** C# TimeSpan LiveElapsed / LiveRemaining -> milliseconds (JS has no TimeSpan). */
  get liveElapsedMs() { return this._liveElapsed(); }
  get liveRemainingMs() {
    if (this._liveDurationMs <= 0) return 0;
    const remaining = this._liveDurationMs - this._liveElapsed();
    return remaining <= 0 ? 0 : remaining;
  }

  /**
   * READ-ONLY view of the rolled endurance ramp, for anything that wants to look AHEAD of the
   * pump (ui/announcer.js reads it to warn "get ready to watch" before a video starts).
   *
   * Frozen cue copies, memoized per roll: a caller cannot reach the array the pump walks, and
   * polling this every 250 ms costs one identity check. Empty before Live.
   *
   * JS-ONLY ADDITION: the C# GoonMatchService has no equivalent property. It is pure read and
   * changes no behaviour, so parity is unaffected and the C# side needs nothing.
   *
   * @returns {ReadonlyArray<{offsetMs:number,action:number,element:number,intensity:number,durationMs:number}>}
   */
  get rampCues() {
    const src = this._ramp || [];
    if (this._rampView && this._rampViewSrc === src) return this._rampView;
    this._rampViewSrc = src;
    this._rampView = Object.freeze(src.map((c) => Object.freeze({ ...c })));
    return this._rampView;
  }

  /** The sudden-death seam. `runner` is an alias for the same field (see header block). */
  get suddenDeathRunner() { return this._runner; }
  set suddenDeathRunner(v) { this._runner = v || null; }
  get runner() { return this._runner; }
  set runner(v) { this._runner = v || null; }

  // ----------------------------------------------------------- events

  onPhaseChanged(fn) { return this._ev.phaseChanged.on(fn); }
  onElementStartRequested(fn) { return this._ev.elementStartRequested.on(fn); }
  onElementIntensityChanged(fn) { return this._ev.elementIntensityChanged.on(fn); }
  onElementStopRequested(fn) { return this._ev.elementStopRequested.on(fn); }
  onPayloadAccepted(fn) { return this._ev.payloadAccepted.on(fn); }
  onPayloadRejected(fn) { return this._ev.payloadRejected.on(fn); }
  onPayloadReceiptReceived(fn) { return this._ev.payloadReceiptReceived.on(fn); }
  onConsentChanged(fn) { return this._ev.consentChanged.on(fn); }
  onDraftChanged(fn) { return this._ev.draftChanged.on(fn); }
  onOpponentStateChanged(fn) { return this._ev.opponentStateChanged.on(fn); }
  onConnectionHealthChanged(fn) { return this._ev.connectionHealthChanged.on(fn); }
  onEmoteReceived(fn) { return this._ev.emoteReceived.on(fn); }
  /**
   * Every inbound `t:'voice'` frame, phase-gated and shape-clamped, in arrival order.
   *
   * DELIBERATELY NOT CONSENT-GATED HERE. The receiver's "my opt-in is off, so this is dropped
   * UNREAD" rule is the whole safety property of the feature and it belongs to ONE owner
   * (ui/voice/voiceService.js), which drops the frame before a single byte is decoded. Splitting
   * the same rule across two tiers is how a feature ends up with a path where only one of them
   * ran. What core owes the service is a frame that is the right shape, from the right phase.
   */
  onVoiceFrame(fn) { return this._ev.voiceFrameReceived.on(fn); }
  /** fn(preparing) whenever the OPPONENT's `media_prep` declaration changes. */
  onMediaPrepChanged(fn) { return this._ev.mediaPrepChanged.on(fn); }
  /** No-cam only: prompt an interaction check, then call reportInteractionCheck(). */
  onInteractionCheckDue(fn) { return this._ev.interactionCheckDue.on(fn); }
  onLobbyFailed(fn) { return this._ev.lobbyFailed.on(fn); }
  onMatchEnded(fn) { return this._ev.matchEnded.on(fn); }
  onResultFinalized(fn) { return this._ev.resultFinalized.on(fn); }

  // ------------------------------------------------------- lobby entry

  /** Host path. Resolves with the invite code to show the user, or null on failure. */
  async createInviteAsync(signal) {
    if (this._phase !== GoonMatchPhase.Idle) return null;
    this._setPhase(GoonMatchPhase.Lobby);
    this._startPhaseTimer();
    try {
      if (!this._createInvite$) throw new Error('transport has no createInviteAsync');
      const code = await this._createInvite$(signal);
      if (!code) {
        this._warn('invite creation returned no code');
        this._resetToIdle();
      }
      return code || null;
    } catch (e) {
      this._error(`invite creation failed: ${(e && e.stack) || e}`);
      this._resetToIdle();
      return null;
    }
  }

  /** Joiner path. */
  async joinAsync(inviteCode, signal) {
    if (this._phase !== GoonMatchPhase.Idle) return false;
    this._setPhase(GoonMatchPhase.Lobby);
    this._startPhaseTimer();
    try {
      if (!this._join$) throw new Error('transport has no joinAsync');
      const ok = await this._join$(inviteCode, signal);
      if (!ok) this._resetToIdle();
      return !!ok;
    } catch (e) {
      this._error(`join failed: ${(e && e.stack) || e}`);
      this._resetToIdle();
      return false;
    }
  }

  /**
   * Enters the Lobby over a transport that is already signaling or connected — the relay-fallback
   * path, where the relay re-uses the room the failed P2P attempt minted, so neither
   * createInviteAsync nor joinAsync may be called again.
   */
  adoptLobby() {
    if (this._phase !== GoonMatchPhase.Idle) return false;
    this._setPhase(GoonMatchPhase.Lobby);
    this._startPhaseTimer();
    // If the transport connected before this service subscribed, its state change already fired
    // without us and nothing else will trigger the hello.
    const st = this._transport.state;
    if (st === GoonTransportState.ConnectedP2P || st === GoonTransportState.ConnectedRelay) this._sendHelloOnce();
    return true;
  }

  // ----------------------------------------------------------- consent

  /** Publishes a new sheet. ANY change clears BOTH confirmations. */
  proposeConsent(liveDurationSec, toyCap, payloadMinGapMs) {
    if (this._phase !== GoonMatchPhase.Lobby && this._phase !== GoonMatchPhase.Consent) return;

    this._consentSheet = makeConsent({
      live_duration_sec: clamp(liveDurationSec, 60, 3600),
      // The sheet can only LOWER a receiver's own cap; the local mixer caps unconditionally on top.
      toy_cap: clamp(toyCap, 0.0, 1.0),
      payload_min_gap_ms: Math.max(GoonConsts.PayloadMinGapMs, payloadMinGapMs),
      confirmed: false,
      // Not terms — OUR standing declarations, republished on every frame we author.
      media_transfer: this._localMediaTransfer,
      voice_notes: this._localVoiceNotes,
    });
    this._localConsentConfirmed = false;
    this._remoteConsentConfirmed = false;
    this._send(this._consentSheet);
    this._ev.consentChanged.emit(undefined, (e) => this._warn(`consentChanged handler threw: ${e && e.message}`));
  }

  /** Confirms the CURRENT sheet. Both confirmations on one fingerprint -> Draft. */
  confirmConsent() {
    if (this._phase !== GoonMatchPhase.Consent) return;
    this._localConsentConfirmed = true;
    this._send(cloneSheet(this._consentSheet, true, this._localMediaTransfer, this._localVoiceNotes));
    this._ev.consentChanged.emit(undefined, (e) => this._warn(`consentChanged handler threw: ${e && e.message}`));
    this._tryEnterDraft();
  }

  /** Backing out of the sheet. Never ends the lobby by itself. */
  withdrawConsent() {
    if (this._phase !== GoonMatchPhase.Consent) return;
    this._localConsentConfirmed = false;
    this._send(cloneSheet(this._consentSheet, false, this._localMediaTransfer, this._localVoiceNotes));
    this._ev.consentChanged.emit(undefined, (e) => this._warn(`consentChanged handler threw: ${e && e.message}`));
  }

  /**
   * The media-transfer opt-in (lobby checkbox). Flipping it is a CHANGE OF TERMS in every way that
   * matters to a player, so it clears BOTH confirmations — the rule every other consent term lives
   * by, honoured HERE BY HAND rather than through `sameSheet()`, which must never learn about this
   * field (see the guard comment at the foot of the file).
   *
   * The re-sent sheet carries the new declaration; the peer's `_handleConsent` reads it back out
   * and the two sides converge without the fingerprint ever changing.
   *
   * @returns {boolean} true when the toggle was taken (Lobby/Consent only).
   */
  setMediaTransfer(on) {
    if (this._phase !== GoonMatchPhase.Lobby && this._phase !== GoonMatchPhase.Consent) return false;

    this._localMediaTransfer = !!on;
    this._localConsentConfirmed = false;
    this._remoteConsentConfirmed = false;
    this._consentSheet = cloneSheet(this._consentSheet, false, this._localMediaTransfer, this._localVoiceNotes);
    this._send(this._consentSheet);
    this._ev.consentChanged.emit(undefined, (e) => this._warn(`consentChanged handler threw: ${e && e.message}`));
    return true;
  }

  /**
   * The VOICE-NOTE opt-in — setMediaTransfer's twin, line for line, and for every one of the same
   * reasons (see the block above and the guard comment on `sameSheet`).
   *
   * The one thing worth saying twice: flipping this CLEARS BOTH CONFIRMATIONS. Your opponent
   * signed a sheet on which nobody's voice was going to be recorded; turning the mic on after
   * they signed would be advancing them onto a term they never saw, and it is exactly the kind of
   * term this rule exists for. The ack gate in front of the toggle (ui/screens/voice.js) is the
   * LOCAL half of the same idea — this is the half the other player gets.
   *
   * It is NOT the whole gate either: the peer's build has to speak the family (`caps.voice`) and
   * the phase has to be open. `voiceNotesAgreed` ANDs the consents; the service ANDs the rest.
   *
   * @returns {boolean} true when the toggle was taken (Lobby/Consent only).
   */
  setLocalVoiceNotes(on) {
    if (this._phase !== GoonMatchPhase.Lobby && this._phase !== GoonMatchPhase.Consent) return false;

    this._localVoiceNotes = !!on;
    this._localConsentConfirmed = false;
    this._remoteConsentConfirmed = false;
    this._consentSheet = cloneSheet(this._consentSheet, false, this._localMediaTransfer, this._localVoiceNotes);
    this._send(this._consentSheet);
    this._ev.consentChanged.emit(undefined, (e) => this._warn(`consentChanged handler threw: ${e && e.message}`));
    return true;
  }

  // ------------------------------------------------------------- draft
  //
  // The draft is a mutual agreement, not two loadouts: each side publishes the elements it ALLOWS
  // and the effective pool is the intersection. Changing a toggle clears BOTH signatures — the
  // same rule the consent sheet lives by, for the same reason (nobody is advanced onto terms they
  // never saw). Two signatures on one pair of sets resolve the draft.

  /**
   * Publishes the local allowed set. ANY change clears BOTH confirmations.
   * C# out-error -> {ok, error}.
   */
  setAllowedElements(allowed) {
    if (this._phase !== GoonMatchPhase.Draft) return { ok: false, error: 'not drafting' };

    const next = this._sanitizeAllowed(allowed);
    const valid = isValidAllowed(next);
    if (!valid.ok) return valid;

    this._localAllowed = next;
    this._localDraftConfirmed = false;
    this._remoteDraftConfirmed = false;
    this._draftResolved = false;
    this._sendDraftState();
    this._ev.draftChanged.emit(undefined, (e) => this._warn(`draftChanged handler threw: ${e && e.message}`));
    return { ok: true, error: '' };
  }

  /**
   * Flips one element on or off. Returns the same {ok,error} shape; a toggle that would leave
   * fewer than MIN_ALLOWED_ELEMENTS on is REFUSED and nothing changes.
   */
  toggleAllowedElement(element) {
    if (this._phase !== GoonMatchPhase.Draft) return { ok: false, error: 'not drafting' };
    const e = Number(element);
    const has = this._localAllowed.includes(e);
    const next = has ? this._localAllowed.filter((x) => x !== e) : this._localAllowed.concat([e]);
    return this.setAllowedElements(next);
  }

  /** Signs the CURRENT pair of sets. Both signatures + a workable intersection -> resolved. */
  confirmDraft() {
    if (this._phase !== GoonMatchPhase.Draft) return { ok: false, error: 'not drafting' };

    const mine = isValidAllowed(this._localAllowed);
    if (!mine.ok) return mine;

    const pool = sharedPool(this._localAllowed, this._remoteAllowed);
    const shared = isValidSharedPool(pool);
    if (!shared.ok) return shared;      // the confirm strip shows this inline; nothing is signed

    this._localDraftConfirmed = true;
    this._sendDraftState();
    this._scoring.configure(this.localAttentionMode, matchRiskTier(pool));
    this._ev.draftChanged.emit(undefined, (e) => this._warn(`draftChanged handler threw: ${e && e.message}`));
    this._tryResolveDraft();
    return { ok: true, error: '' };
  }

  /** Backing out of a signature without touching the toggles. */
  withdrawDraft() {
    if (this._phase !== GoonMatchPhase.Draft || !this._localDraftConfirmed) return;
    this._localDraftConfirmed = false;
    this._draftResolved = false;
    this._sendDraftState();
    this._ev.draftChanged.emit(undefined, (e) => this._warn(`draftChanged handler threw: ${e && e.message}`));
  }

  // LEGACY names. setDraft(list) is setAllowedElements(list); lockDraft() is confirmDraft().
  setDraft(picks) { return this.setAllowedElements(picks); }
  lockDraft() { return this.confirmDraft(); }

  /** Anything the caps intersection cannot mirror is dropped rather than trusted. */
  _sanitizeAllowed(allowed) {
    const pool = this._availableDraftPool;
    return normalizeAllowed((allowed || []).filter((e) => pool.includes(Number(e))));
  }

  _sendDraftState() {
    this._send(makeDraft({
      allowed: this._localAllowed.slice(),
      confirmed: this._localDraftConfirmed,
    }));
  }

  _tryResolveDraft() {
    if (this._phase !== GoonMatchPhase.Draft) return;
    if (!this._localDraftConfirmed || !this._remoteDraftConfirmed) return;

    const pool = sharedPool(this._localAllowed, this._remoteAllowed);
    if (!isValidSharedPool(pool).ok) return;   // cannot happen once both confirmed, but never guess

    this._sharedPool = pool;
    this._draftResolved = true;
    this._scoring.configure(this.localAttentionMode, matchRiskTier(pool));
    this._info(`draft agreed: pool ${pool.join('+')} (+ always-on ${enumName(GoonElement, ALWAYS_ON_ELEMENT)})`);
  }

  // -------------------------------------------------- live-phase input

  /** Gaze-derived attention percentage (0..100). Cam mode only. */
  reportAttention(pct) { this._scoring.reportAttention(pct); }

  /** Outcome of an interaction check prompt. No-cam mode only. */
  reportInteractionCheck(passed) {
    this._scoring.reportInteractionCheck(passed);
    if (!passed) this._info(`interaction check failed (${this._scoring.failedChecks} total)`);
  }

  setCloseness(closeness) {
    this._localCloseness = (closeness === null || closeness === undefined) ? null : clamp(closeness | 0, 0, 3);
  }

  /**
   * How many FLOATING VIDEO WINDOWS the local player has up (exec/videos.js owns the pool; the
   * count arrives through exec/executor.js, which is the one module that holds both the renderers
   * and the match). Rides the next state tick as `vwin` so the opponent's monitor can draw them.
   *
   * IT IS A REPORT, NOT A COMMAND: nothing in the engine reads it, no score, no rate limit, no
   * gate. Purely additive — leave it at 0 and every behaviour is exactly what it was.
   *
   * PHASE-GATED one way only. A non-zero count is refused outside Live/SuddenDeath, because a
   * window cannot exist before the run starts or after it ends and a stale claim would leave a
   * phantom stack on their little screen. ZERO is always accepted: that is the teardown path
   * (executor.stopAll on mercy/recap) and it must never be the thing that strands the report.
   *
   * MIRROR: GoonMatchService.SetLocalWindowCount(int).
   *
   * @param   {number} count 0..4 after clamping; anything unusable reads as 0
   * @returns {boolean} true when the value was taken
   */
  setLocalWindowCount(count) {
    if (this._disposed) return false;
    const n = clampWindowCount(count);
    if (n > 0 && this._phase !== GoonMatchPhase.Live && this._phase !== GoonMatchPhase.SuddenDeath) return false;
    this._localWindowCount = n;
    return true;
  }

  sendEmote(text, icon) {
    if (this._ended || this._phase === GoonMatchPhase.Idle) return;
    this._send(makeEmote({
      text: sanitizeText(text, EMOTE_TEXT_MAX_CHARS),
      icon: sanitizeText(icon, EMOTE_ICON_MAX_CHARS),
    }));
  }

  /* ------------------------------------------------------------ voice notes
   *
   * ONE DOOR, three verbs on top of it. Everything a voice frame has to satisfy before it may
   * leave is checked HERE, once, so a caller cannot assemble a note that goes out half-gated:
   *
   *   · not disposed, not ended, and the PHASE is Countdown/Live/SuddenDeath. A note that lands
   *     on a recap has nowhere to play and a note before the countdown is a mic hot in a lobby;
   *   · BOTH consents plus the peer's `caps.voice` (voiceNotesAgreed). Sending to a build that
   *     never heard of the family is not "harmless": the frame is dropped silently at the far end
   *     and this family has NO receipt, so the sender would sit there believing it landed;
   *   · the sub is one of the three (clampVoiceSub in the factory turns anything else into '',
   *     and '' is refused here rather than put on the wire as a frame nobody can route).
   *
   * FIRE AND FORGET, exactly like `t:'emote'`: no id reservation, no ACK, no retry, no ledger
   * entry, no charge. The boolean is "we handed it to the transport", never "they heard it".
   * The BYTES ceiling is not here either — a frame too big for the lane is dropped by
   * wire.serializeForSend with an error, and the chunk plan that keeps us under it belongs to
   * ui/voice/voiceService.js, which is the tier that knows what a chunk is.
   *
   * @returns {boolean} true when the frame reached the transport
   */
  sendVoiceFrame(sub, fields = {}) {
    if (this._disposed || this._ended) return false;
    if (!this.voicePhaseOpen) return false;
    if (!this.voiceNotesAgreed) return false;

    const msg = makeVoice(Object.assign({}, fields, {
      sub,
      // The one string on this family that a HUMAN chose. Sanitized on the way out as well as on
      // the way in, exactly as the emote family does with its own text and icon.
      emote: fields.emote == null ? null : (sanitizeText(fields.emote, EMOTE_ICON_MAX_CHARS) || null),
    }));
    if (msg.sub === '') return false;

    this._send(msg);
    return true;
  }

  /** Announce one note: id, total bytes, duration, chunk count, and the emote it rides (or null). */
  sendVoiceMeta({ id, bytes, durMs, parts, emote = null } = {}) {
    return this.sendVoiceFrame('meta', { id, bytes, durMs, parts, emote });
  }

  /** One base64 slice of it. The lane is ordered, so `seq` is a check and never a sort key. */
  sendVoiceChunk(id, seq, data) {
    if (typeof data !== 'string' || data === '') return false;
    return this.sendVoiceFrame('chunk', { id, seq, data });
  }

  /** ...and the full stop. Its own frame rather than a flag on the last chunk, so a truncated
   *  transfer is INDISTINGUISHABLE from one that is still arriving until the sender says so. */
  sendVoiceEnd(id) {
    return this.sendVoiceFrame('end', { id });
  }

  /**
   * Declare whether we are still assembling a library (`media_prep`, §6).
   *
   * Pre-live only, and NOT a term: it clears no confirmation, blocks no phase
   * and is never folded into the consent fingerprint (`sameSheet` must never
   * learn about it, for the reason spelled out at the foot of this file). The
   * only thing it does is let the other side's lobby say "they joined and are
   * picking their media" instead of nothing at all.
   *
   * Edge-triggered: an unchanged value sends nothing, so a screen that repaints
   * ten times a second cannot turn a status hint into wire traffic.
   *
   * @returns {boolean} true when a frame actually went out
   */
  setMediaPrep(on) {
    if (this._disposed || this._ended) return false;
    if (this._phase === GoonMatchPhase.Idle) return false;
    const next = !!on;
    if (next === this._localMediaPrep) return false;
    this._localMediaPrep = next;
    this._send(makeMediaPrep({ preparing: next }));
    return true;
  }

  // ---------------------------------------------------------- payloads

  /**
   * Fires an offensive payload at the opponent. Sender-side mirror of the receiver's gate
   * (charges, rate limit, one heavy per match) so a well-behaved client is never rejected; the
   * receiver re-checks everything anyway. C# out-error -> {ok, error, id}.
   */
  msUntilNextPayloadMs() {
    return this._outboundLimiter.msUntilNextToken(localMonotonicMs());
  }

  tryFirePayload(request) {
    if (!request) return { ok: false, error: 'no request', id: null };
    if (this._phase !== GoonMatchPhase.Live && this._phase !== GoonMatchPhase.SuddenDeath) {
      return { ok: false, error: 'match is not live', id: null };
    }
    if (request.kind === GoonPayloadKind.BrainDrain && this._localHeavyUsed) {
      return { ok: false, error: 'heavy already used this match', id: null };
    }
    if (!this._availablePayloadKinds.includes(request.kind)) {
      return { ok: false, error: `opponent's client cannot run ${request.kind}`, id: null };
    }

    let cost = costOf(request.kind);
    if (cost <= 0 || !Number.isFinite(cost)) return { ok: false, error: 'unknown payload', id: null };
    if (this._scoring.charges < cost) return { ok: false, error: `needs ${cost} charge(s)`, id: null };

    const nowLocal = localMonotonicMs();
    if (!this._outboundLimiter.tryAdmit(nowLocal)) {
      // (UI reads the same limiter via msUntilNextPayloadMs() below.)
      return {
        ok: false,
        error: `rate limited (${Math.trunc(this._outboundLimiter.msUntilNextToken(nowLocal) / 1000)}s)`,
        id: null,
      };
    }
    const spend = this._scoring.trySpend(request.kind);
    if (!spend.ok) return { ok: false, error: `needs ${spend.cost} charge(s)`, id: null };
    cost = spend.cost;

    const id = `p${++this._payloadSeq}${this._isHost ? 'h' : 'g'}`;
    const msg = makePayload({
      id,
      kind: request.kind,
      fire_at_match_ms: this._clockNow() + Math.max(GoonConsts.MinScheduleBufferMs, PAYLOAD_SCHEDULE_BUFFER_MS),
      duration_ms: clamp(request.durationMs ?? 30000, 1000, MAX_PAYLOAD_DURATION_MS),
      tags: request.tags ?? null,
      text: sanitizeText(request.text, TEXT_MAX_CHARS),
      voice: !!request.voice,
      pattern: request.pattern ?? null,
      intensity: clamp(request.intensity ?? 0.5, 0.0, 1.0),
    });

    if (request.kind === GoonPayloadKind.BrainDrain) {
      this._localHeavyUsed = true;
      this._localHeavyPayloadId = id;
    }
    this._outboundCosts.set(id, cost);
    this._send(msg);
    this._info(`payload out ${id} ${request.kind} cost ${cost}`);
    return { ok: true, error: '', id };
  }

  /**
   * Credits charges earned outside the payload/round economy (the bubble economy is the first
   * consumer). Integer count >= 1; the total is clamped to GoonConsts.ChargeCap exactly like every
   * other earning, and the next state tick reports the new meter. No-ops outside the Live phase.
   *
   * MIRROR: GoonMatchService.CreditCharges(int, string).
   *
   * @returns {boolean} true when at least the request was accepted (credited, cap permitting)
   */
  creditCharges(count, reason) {
    if (this._disposed || this._ended) return false;
    if (this._phase !== GoonMatchPhase.Live) return false;

    const n = Math.trunc(Number(count));
    if (!Number.isFinite(n) || n < 1) return false;

    const before = this._scoring.charges;
    for (let i = 0; i < n; i++) this._scoring.awardEventWon();   // caps at GoonConsts.ChargeCap
    this._info(`charges +${n} (${reason || 'unspecified'}) -> ${this._scoring.charges} (was ${before})`);
    return true;
  }

  /**
   * The executor calls this when an accepted inbound payload finishes.
   * endured = the receiver took it all the way -> +1 charge.
   */
  notifyInboundPayloadFinished(payloadId, endured) {
    if (!payloadId) return;
    this._send(makePayloadReceipt({
      id: payloadId,
      status: endured ? GoonReceiptStatus.Survived : GoonReceiptStatus.Completed,
    }));
    if (endured) this._scoring.awardPayloadEndured();
  }

  // ------------------------------------------------------------- mercy

  /**
   * The dignified concede. Available in EVERY phase — the Esc ladder maps straight here.
   * Pre-Live it degrades to a clean cancel that never reaches the ledger.
   */
  declareMercy() {
    if (this._ended || this._phase === GoonMatchPhase.Idle) return;

    const atMs = this._clockNow();
    this._localMercyMatchMs = atMs;
    this._send(makeMercy({ at_match_ms: atMs }));

    const preLive = this._isPreLive(this._phase);
    const draw = this._remoteMercyMatchMs !== null && Math.abs(atMs - this._remoteMercyMatchMs) <= DRAW_WINDOW_MS;

    this._info(`local mercy at ${atMs} (preLive=${preLive}, draw=${draw})`);
    this._endMatch(draw ? GoonEndReason.Draw : GoonEndReason.Mercy, !draw, !preLive);
  }

  /** Aborts a lobby/consent/draft locally without the mercy semantics (UI "cancel"). */
  cancelMatch(reason) {
    if (this._ended) { this._resetToIdle(); return; }
    this._info(`match cancelled: ${reason}`);
    if (this._phase === GoonMatchPhase.Live || this._phase === GoonMatchPhase.SuddenDeath) {
      this.declareMercy();
      return;
    }
    this._send(makeMercy({ at_match_ms: this._clockNow() }));
    this._endMatch(GoonEndReason.Mercy, true, false);
  }

  /**
   * Clean lobby failure: incompatible protocol version or an empty capability intersection. Tears
   * the lobby down with a reason instead of letting the two clients desync mid-match.
   */
  _failLobby(reason) {
    this._warn(`lobby failed: ${reason}`);
    this._lobbyFailureReason = reason;
    this._ev.lobbyFailed.emit(reason, (e) => this._warn(`lobbyFailed handler threw: ${e && e.message}`));

    // No dedicated teardown verb in the protocol: a pre-live mercy is the clean exit both clients
    // already understand, and it never reaches the ledger.
    this._send(makeMercy({ at_match_ms: this._clockNow() }));
    this._ended = true;
    this._stopTimers();
    this._stopPhaseTimer();
    this._setPhase(GoonMatchPhase.Idle);
  }

  // ------------------------------------------------------ message pump

  _onMessageReceived(message) {
    if (this._disposed || !message) return;
    try {
      switch (message.t) {
        case 'hello': this._handleHello(message); break;
        case 'consent': this._handleConsent(message); break;
        case 'draft': this._handleDraft(message); break;
        case 'match_start': this._handleMatchStart(message); break;
        case 'tick': this._handleTick(message); break;
        case 'payload': this._handleInboundPayload(message); break;
        case 'payload_receipt': this._handleReceipt(message); break;
        case 'mercy': this._handleRemoteMercy(message); break;
        case 'emote': this._handleEmote(message); break;
        case 'voice': this._handleVoice(message); break;
        case 'media_prep': this._handleMediaPrep(message); break;
        case 'result': this._handleRemoteResult(message); break;
        case 'round':
        case 'round_result':
          this._forwardToRunner(message);
          break;
        default:
          // Clock ping/pong belongs to the transport; anything else is a newer protocol.
          if (!isClockMessage(message)) this._info(`ignoring message '${message.t}'`);
          break;
      }
    } catch (e) {
      this._error(`message handling failed for ${message.t}: ${(e && e.stack) || e}`);
    }
  }

  _onTransportStateChanged(state) {
    if (this._disposed) return;
    this._info(`transport state ${state}`);
    if (state === GoonTransportState.ConnectedP2P || state === GoonTransportState.ConnectedRelay) {
      this._sendHelloOnce();
    }
  }

  _sendHelloOnce() {
    if (this._helloSent) return;
    this._helloSent = true;
    this._send(makeHello({
      display_name: this.localDisplayName || 'Player',
      attention_mode: this.localAttentionMode,
      toy_connected: this.localToyConnected,
      app_version: this.localAppVersion || '',
      caps: this._localCaps,
    }));
  }

  _handleHello(hello) {
    this._remoteHello = hello;
    this._opponent.displayName = sanitizeName(hello.display_name);
    this._opponent.appVersion = sanitizeText(hello.app_version, 16);
    this._opponent.attentionMode = hello.attention_mode;
    this._opponent.toyConnected = hello.toy_connected;
    this._opponent.platform = sanitizeText(hello.caps ? hello.caps.platform : '', 16);
    this._ev.opponentStateChanged.emit(undefined, (e) => this._warn(`opponentStateChanged handler threw: ${e && e.message}`));

    this._sendHelloOnce();

    // Protocol compatibility — fail the lobby cleanly instead of desyncing later.
    const caps = hello.caps;

    // The media-transfer version discriminator. Read BEFORE the compatibility checks and kept out
    // of every intersection ON PURPOSE: it advertises what their BUILD understands, not what the
    // pairing can run, so it can never narrow a pool and can never fail a lobby. A peer that omits
    // it (C# reference client, older page) reads false and the feature simply never starts.
    this._peerSupportsTransfer = !!(caps && caps.transfer);
    // ...and the voice-note discriminator, read on exactly the same terms and in exactly the same
    // place. `peerSpeaksVoice` is forgiving about a quoted number and strict about a boolean —
    // see the helper in core/contracts.js. False for every peer that predates the family, which
    // is what keeps a fire-and-forget send from disappearing into a build that cannot hear it.
    this._peerSupportsVoice = peerSpeaksVoice(caps);

    if (caps && caps.min_v > GoonConsts.ProtocolVersion) {
      this._failLobby(`opponent requires protocol v${caps.min_v}, this client speaks v${GoonConsts.ProtocolVersion} - update required`);
      return;
    }
    if (hello.v < this._localCaps.min_v) {
      this._failLobby(`opponent speaks protocol v${hello.v}, this client needs at least v${this._localCaps.min_v}`);
      return;
    }

    // Every cross-client set is an intersection of the two advertisements.
    this._availableDraftPool = intersect(this._localCaps.elements, caps && caps.elements);
    this._availablePayloadKinds = intersect(this._localCaps.payloads, caps && caps.payloads);

    const rounds = intersect(this._localCaps.rounds, caps && caps.rounds);
    if (!rounds.includes(UNIVERSAL_ROUND)) rounds.push(UNIVERSAL_ROUND);
    this._allowedRoundKinds = rounds;

    // The always-on element does not count: the agreement needs MIN_ALLOWED_ELEMENTS TOGGLEABLE
    // elements both clients can run, or there is nothing to roll a ramp from.
    const rollable = normalizeAllowed(this._availableDraftPool);
    if (rollable.length < MIN_ALLOWED_ELEMENTS) {
      this._failLobby(`only ${rollable.length} shared draft element(s) - need ${MIN_ALLOWED_ELEMENTS}`);
      return;
    }

    this._info(`caps: peer ${this._opponent.platform} v${hello.v}, pool ${this._availableDraftPool.length}, ` +
      `payloads ${this._availablePayloadKinds.length}, rounds ${this._allowedRoundKinds.length}`);

    if (this._phase === GoonMatchPhase.Lobby) {
      this._setPhase(GoonMatchPhase.Consent);
      // The host authors the opening proposal; the guest may counter-propose.
      if (this._isHost) {
        this.proposeConsent(
          this._consentSheet.live_duration_sec,
          this._consentSheet.toy_cap,
          this._consentSheet.payload_min_gap_ms,
        );
      }
    }
  }

  _handleConsent(sheet) {
    if (this._phase === GoonMatchPhase.Lobby) this._setPhase(GoonMatchPhase.Consent);
    if (this._phase !== GoonMatchPhase.Consent) return;

    // Their declaration rides EVERY consent frame, counter-proposal or not, so it is read on both
    // branches before anything else is decided. It is theirs alone: it never touches our own flag.
    this._remoteMediaTransfer = !!sheet.media_transfer;
    this._remoteVoiceNotes = !!sheet.voice_notes;

    if (!sameSheet(sheet, this._consentSheet)) {
      // A counter-proposal: adopt it and clear BOTH confirms so nobody can be advanced onto
      // terms they never saw. Note the LOCAL media_transfer and voice_notes being carried across
      // — adopting their terms must not silently flip either of our own opt-ins.
      this._consentSheet = cloneSheet(sheet, false, this._localMediaTransfer, this._localVoiceNotes);
      this._localConsentConfirmed = false;
      this._remoteConsentConfirmed = !!sheet.confirmed;
      this._ev.consentChanged.emit(undefined, (e) => this._warn(`consentChanged handler threw: ${e && e.message}`));
      return;
    }

    this._remoteConsentConfirmed = !!sheet.confirmed;
    this._ev.consentChanged.emit(undefined, (e) => this._warn(`consentChanged handler threw: ${e && e.message}`));
    this._tryEnterDraft();
  }

  _tryEnterDraft() {
    if (this._phase !== GoonMatchPhase.Consent) return;
    if (!this._localConsentConfirmed || !this._remoteConsentConfirmed) return;

    this._liveDurationMs = this._consentSheet.live_duration_sec * 1000;
    this._inboundLimiter.reset();
    this._outboundLimiter.reset();

    // Everything the pairing can run is allowed by default, on both sides — the agreement screen
    // is about switching things OFF. The peer's set is assumed to be the same default until their
    // first draft frame says otherwise, so the intersection is never briefly empty.
    this._localAllowed = defaultAllowed(this._availableDraftPool);
    this._remoteAllowed = defaultAllowed(this._availableDraftPool);
    this._localDraftConfirmed = false;
    this._remoteDraftConfirmed = false;
    this._draftResolved = false;
    this._sharedPool = [];

    this._setPhase(GoonMatchPhase.Draft);
    this._sendDraftState();
  }

  _handleDraft(draft) {
    if (this._phase !== GoonMatchPhase.Draft) return;

    // Prefer the v2 fields; fall back to the v1 pair so a capture/older frame still parses.
    const rawAllowed = (Array.isArray(draft.allowed) && draft.allowed.length > 0)
      ? draft.allowed : (draft.elements || []);
    const rawConfirmed = (draft.confirmed === undefined || draft.confirmed === null)
      ? !!draft.locked : !!draft.confirmed;

    const incoming = this._sanitizeAllowed(rawAllowed);
    const changed = !sameElementSet(incoming, this._remoteAllowed);

    this._remoteAllowed = incoming;
    this._remoteDraftConfirmed = rawConfirmed;

    if (changed) {
      // They moved a toggle: BOTH signatures die, exactly as on the consent sheet. Re-publishing
      // our (unchanged) set tells them our signature dropped — and because the set is unchanged,
      // their handler sees `changed === false` and the exchange terminates.
      this._localDraftConfirmed = false;
      this._draftResolved = false;
      this._sendDraftState();
    }

    this._ev.draftChanged.emit(undefined, (e) => this._warn(`draftChanged handler threw: ${e && e.message}`));
    this._tryResolveDraft();
  }

  // -------------------------------------------------------- countdown

  _tryProposeStart() {
    if (this._startProposed || !this._isHost) return;
    if (this._phase !== GoonMatchPhase.Draft) return;
    if (!this._draftResolved) return;
    if (!this._clock() || !this._clock().isSynced) return;   // fire-at-timestamp needs a synced clock

    this._startProposed = true;
    if (this._localSeedContribution === null) this._localSeedContribution = newSeedContribution();
    this._startMatchMs = this._clockNow() + Math.max(GoonConsts.MinScheduleBufferMs, COUNTDOWN_MS);
    this._send(makeMatchStart({ start_match_ms: this._startMatchMs, seed_contribution: this._localSeedContribution }));
    this._setPhase(GoonMatchPhase.Countdown);
  }

  _handleMatchStart(start) {
    if (this._phase !== GoonMatchPhase.Draft && this._phase !== GoonMatchPhase.Countdown) return;

    this._remoteSeedContribution = start.seed_contribution;

    if (!this._isHost) {
      // Guest adopts the host's instant and answers with its own contribution.
      this._startMatchMs = start.start_match_ms;
      if (this._localSeedContribution === null) {
        this._localSeedContribution = newSeedContribution();
        this._send(makeMatchStart({
          start_match_ms: this._startMatchMs,
          seed_contribution: this._localSeedContribution,
        }));
      }
      this._setPhase(GoonMatchPhase.Countdown);
    }

    if (this._localSeedContribution !== null && this._remoteSeedContribution !== null) {
      this._matchSeed = combineSeeds(this._localSeedContribution, this._remoteSeedContribution);
    }
  }

  // ------------------------------------------------------------- live

  _enterLive() {
    if (this._phase !== GoonMatchPhase.Countdown) return;

    if (this._matchSeed === 0n && this._localSeedContribution !== null) {
      // Peer contribution never arrived — degrade to our own rather than stall the match.
      this._matchSeed = this._localSeedContribution;
      this._warn('starting Live without a peer seed contribution');
    }

    // The pool is the agreement's intersection, and the ramp is rolled from it — one schedule,
    // both players, no exchange: buildRamp is a pure function of (pool, seed, duration).
    const pool = this._draftResolved && this._sharedPool.length > 0
      ? this._sharedPool
      : sharedPool(this._localAllowed, this._remoteAllowed);
    this._sharedPool = pool;

    this._scoring.reset();
    this._scoring.configure(this.localAttentionMode, matchRiskTier(pool));
    this._ramp = buildRamp(pool, this._matchSeed, this._consentSheet.live_duration_sec, this._rngFactory);
    this._rampIndex = 0;
    this._liveDurationMs = this._consentSheet.live_duration_sec * 1000;
    this._activeElements.clear();
    this._restartLiveWatch();
    this._lastTickSentLocalMs = localMonotonicMs();
    this._opponent.lastTickLocalMs = localMonotonicMs();
    this._nextInteractionCheckMs = NO_CAM_CHECK_INTERVAL_MS;

    this._liveTicker = ticker(1000, (elapsedMs) => this._liveTick(elapsedMs), { logger: this._log, tag: `${this._tag}Live` });

    this._info(`Live phase: seed ${this._matchSeed.toString(16)}, ${this._consentSheet.live_duration_sec}s, ` +
      `pool ${pool.join('+')} (+always-on bubbles), risk ${this._scoring.riskTier}, ${this._ramp.length} cues`);

    this._setPhase(GoonMatchPhase.Live);
  }

  /** @param {number} realElapsedMs actual ms since the previous tick (throttled tabs catch up) */
  _liveTick(realElapsedMs) {
    if (this._disposed || this._ended) return;
    if (this._phase !== GoonMatchPhase.Live && this._phase !== GoonMatchPhase.SuddenDeath) return;

    try {
      const elapsed = this._liveElapsed();

      if (this._phase === GoonMatchPhase.Live) {
        this._pumpRamp(elapsed);
        this._scoring.tick(Math.max(0, realElapsedMs) / 1000);
        this._pumpInteractionChecks(elapsed);

        if (elapsed >= this._liveDurationMs) {
          this._enterSuddenDeath();
          return;
        }
      }

      this._maybeSendStateTick();
      this._checkOpponentFreshness();
    } catch (e) {
      this._error(`live tick failed: ${(e && e.stack) || e}`);
    }
  }

  _pumpRamp(elapsedMs) {
    while (this._rampIndex < this._ramp.length && this._ramp[this._rampIndex].offsetMs <= elapsedMs) {
      const cue = this._ramp[this._rampIndex++];
      const args = {
        element: cue.element,
        intensity: cue.intensity,
        durationMs: cue.durationMs,
        elapsedMs,
      };

      switch (cue.action) {
        case GoonCueAction.Start:
          if (!this._activeElements.has(cue.element)) {
            this._activeElements.add(cue.element);
            this._raiseCue('elementStartRequested', args);
          } else {
            this._raiseCue('elementIntensityChanged', args);
          }
          break;
        case GoonCueAction.Intensity:
          if (this._activeElements.has(cue.element)) this._raiseCue('elementIntensityChanged', args);
          break;
        case GoonCueAction.Stop:
          if (this._activeElements.delete(cue.element)) this._raiseCue('elementStopRequested', args);
          break;
        default:
          break;
      }
    }
  }

  _pumpInteractionChecks(elapsedMs) {
    if (this.localAttentionMode !== GoonAttentionMode.NoCam) return;
    if (elapsedMs < this._nextInteractionCheckMs) return;
    this._nextInteractionCheckMs = elapsedMs + NO_CAM_CHECK_INTERVAL_MS;
    this._ev.interactionCheckDue.emit(undefined, (e) => this._warn(`interactionCheckDue handler threw: ${e && e.message}`));
  }

  _maybeSendStateTick() {
    const now = localMonotonicMs();
    if (now - this._lastTickSentLocalMs < GoonConsts.TickIntervalMs) return;
    this._lastTickSentLocalMs = now;

    this._send(makeTick({
      at_match_ms: this._clockNow(),
      score: this._scoring.score,
      attention_pct: roundHalfToEven(this._scoring.attentionPct),
      attention_mode: this.localAttentionMode,
      // C# sends GoonElement.ToString(), i.e. the NAME ("Flashes") — a Windows peer's HUD reads
      // these verbatim, so the name is the wire value, not the code.
      active_effects: Array.from(this._activeElements, (x) => enumName(GoonElement, x)),
      toy: this._activeElements.has(GoonElement.ToyPatterns),
      closeness: this._localCloseness,
      charges: this._scoring.charges,
      // APPEND-ONLY optional field. A peer that predates it drops it on the floor; the C#
      // reference client sends 0 because it has no floating windows to report.
      vwin: this._localWindowCount,
    }));
  }

  _checkOpponentFreshness() {
    const age = localMonotonicMs() - this._opponent.lastTickLocalMs;
    const health = age >= GoonConsts.TickDeadMs ? GoonConnectionHealth.Dead
      : age >= GoonConsts.TickStaleMs ? GoonConnectionHealth.Wobbly
        : GoonConnectionHealth.Fresh;

    if (health !== this._opponent.health) {
      this._opponent.health = health;
      this._info(`opponent connection ${health} (age ${Math.trunc(age)}ms)`);
      this._ev.connectionHealthChanged.emit(health, (e) => this._warn(`connectionHealthChanged handler threw: ${e && e.message}`));
    }

    if (health === GoonConnectionHealth.Dead && !this._ended) {
      // Recorded as an abandon, NEVER auto-declared a mercy.
      this._endMatch(GoonEndReason.Abandon, false, true);
    }
  }

  _handleTick(tick) {
    this._opponent.score = tick.score;
    this._opponent.attentionPct = clamp(tick.attention_pct, 0, 100);
    this._opponent.attentionMode = tick.attention_mode;
    this._opponent.activeEffects = tick.active_effects || [];
    this._opponent.toyActive = !!tick.toy;
    this._opponent.closeness = (tick.closeness === null || tick.closeness === undefined) ? null : clamp(tick.closeness, 0, 3);
    this._opponent.charges = clamp(tick.charges, 0, GoonConsts.ChargeCap);
    // Optional and untrusted: absent (an older peer, or the C# client) reads 0, and the clamp runs
    // here as well as in wire.js because a tick can also reach us from a caller that built one by
    // hand (the solo driver, a play-test harness) and never went through parse().
    this._opponent.vwin = clampWindowCount(tick.vwin);
    this._opponent.lastTickLocalMs = localMonotonicMs();
    this._opponent.hasSeenTick = true;

    // The opponent's self-reported meter is the ceiling we hold them to when their next payload
    // lands (C# assigns unconditionally — a lower claim tightens the ceiling immediately).
    this._opponentChargesKnown = this._opponent.charges;

    if (this._opponent.health !== GoonConnectionHealth.Fresh) {
      this._opponent.health = GoonConnectionHealth.Fresh;
      this._ev.connectionHealthChanged.emit(GoonConnectionHealth.Fresh, (e) => this._warn(`connectionHealthChanged handler threw: ${e && e.message}`));
    }
    this._ev.opponentStateChanged.emit(undefined, (e) => this._warn(`opponentStateChanged handler threw: ${e && e.message}`));
  }

  // ----------------------------------------------- inbound payload gate

  _handleInboundPayload(payload) {
    if (!payload.id) payload.id = `anon${++this._payloadSeq}`;

    if (this._phase !== GoonMatchPhase.Live && this._phase !== GoonMatchPhase.SuddenDeath) {
      this._rejectPayload(payload, GoonReceiptStatus.RejectedFiltered, 'not live');
      return;
    }

    const cost = costOf(payload.kind);
    if (cost <= 0 || !Number.isFinite(cost)) {
      this._rejectPayload(payload, GoonReceiptStatus.RejectedFiltered, 'unknown kind');
      return;
    }
    // Belt-and-braces: a kind we never advertised is not runnable here, whatever the wire says.
    if (!this._localCaps.payloads.includes(payload.kind)) {
      this._rejectPayload(payload, GoonReceiptStatus.RejectedFiltered, 'kind not in our advertised caps');
      return;
    }
    if (payload.kind === GoonPayloadKind.BrainDrain && this._remoteHeavyUsed) {
      this._rejectPayload(payload, GoonReceiptStatus.RejectedFiltered, 'heavy already used');
      return;
    }

    const nowLocal = localMonotonicMs();
    if (!this._inboundLimiter.tryAdmit(nowLocal)) {
      this._rejectPayload(payload, GoonReceiptStatus.RejectedRate, 'rate limit');
      return;
    }
    if (this._opponentChargesKnown < cost) {
      // Economy violation. No dedicated receipt status exists in v1, so it rides rejected_rate.
      this._rejectPayload(payload, GoonReceiptStatus.RejectedRate,
        `charge economy: claimed ${this._opponentChargesKnown} < cost ${cost}`);
      return;
    }

    this._opponentChargesKnown -= cost;
    if (payload.kind === GoonPayloadKind.BrainDrain) this._remoteHeavyUsed = true;

    // Defensive normalisation. exec/ still owns full receiver-side resolution (tags -> own
    // library, tag stripping, level gates, mixer caps).
    payload.text = sanitizeText(payload.text, TEXT_MAX_CHARS);
    payload.intensity = clamp(payload.intensity, 0.0, 1.0);
    // ONE clamp band for every kind (1 s .. MAX_PAYLOAD_DURATION_MS) — no per-kind table on either
    // side, so a new sustained kind (Spiral, 2026-08-03) inherits BrainDrain's band by
    // construction. Narrowing a single kind here means adding the same switch to
    // GoonMatchService.HandleInboundPayload in the same commit.
    payload.duration_ms = clamp(payload.duration_ms, 1000, MAX_PAYLOAD_DURATION_MS);

    const earliestMatchMs = this._clockNow() + GoonConsts.MinScheduleBufferMs;
    if (payload.fire_at_match_ms < earliestMatchMs) payload.fire_at_match_ms = earliestMatchMs;

    this._send(makePayloadReceipt({ id: payload.id, status: GoonReceiptStatus.Accepted }));

    let fireAtLocalMs;
    try { fireAtLocalMs = this._clock().matchMsToLocal(payload.fire_at_match_ms); }
    catch { fireAtLocalMs = localMonotonicMs() + GoonConsts.MinScheduleBufferMs; }

    this._info(`payload in ${payload.id} ${payload.kind} accepted, fires in ${Math.trunc(fireAtLocalMs - localMonotonicMs())}ms`);

    this._ev.payloadAccepted.emit({ payload, fireAtLocalMs },
      (e) => this._error(`payloadAccepted handler threw: ${(e && e.stack) || e}`));
  }

  _rejectPayload(payload, status, reason) {
    this._info(`payload in ${payload.id} ${payload.kind} rejected: ${reason}`);
    const receipt = makePayloadReceipt({ id: payload.id, status });
    this._send(receipt);
    this._ev.payloadRejected.emit(receipt, (e) => this._warn(`payloadRejected handler threw: ${e && e.message}`));
  }

  _handleReceipt(receipt) {
    const status = typeof receipt.status === 'string' ? receipt.status : '';
    if (receipt.id && status.toLowerCase().startsWith('rejected') && this._outboundCosts.has(receipt.id)) {
      const cost = this._outboundCosts.get(receipt.id);
      this._outboundCosts.delete(receipt.id);
      this._scoring.refund(cost);
      // A rejected heavy was never delivered — give the once-per-match slot back.
      if (this._localHeavyPayloadId === receipt.id) {
        this._localHeavyPayloadId = null;
        this._localHeavyUsed = false;
      }
      this._info(`payload ${receipt.id} rejected (${status}) - ${cost} charge(s) refunded`);
    } else if (receipt.id && (status === GoonReceiptStatus.Completed || status === GoonReceiptStatus.Survived)) {
      this._outboundCosts.delete(receipt.id);
    }

    this._ev.payloadReceiptReceived.emit(receipt, (e) => this._warn(`payloadReceiptReceived handler threw: ${e && e.message}`));
  }

  _handleEmote(emote) {
    emote.text = sanitizeText(emote.text, EMOTE_TEXT_MAX_CHARS);
    emote.icon = sanitizeText(emote.icon, EMOTE_ICON_MAX_CHARS);
    this._ev.emoteReceived.emit(emote, (e) => this._warn(`emoteReceived handler threw: ${e && e.message}`));
  }

  /**
   * An inbound voice frame. TWO gates and nothing else:
   *
   *   1. the PHASE. A note that arrives on the recap (or in the lobby) is dropped without being
   *      looked at — post-match frames are ignored, per the protocol, and a mic that could reach
   *      somebody after they tapped out is the one shape of this feature nobody agreed to;
   *   2. the SUB. '' is what clampVoiceSub answers for a kind we do not know, which is how a
   *      newer peer's fourth sub arrives — ignored, never routed, never an error.
   *
   * Everything else — the local opt-in, the size ceilings, the rate limit, the assembly, the
   * decode — is ui/voice/voiceService.js's, deliberately (see onVoiceFrame). `emote` is sanitized
   * here for the same reason `_handleEmote` sanitizes its own strings: it is the one member of
   * this family that a person typed, and it ends up on a bubble.
   */
  _handleVoice(frame) {
    if (!this.voicePhaseOpen) return;
    if (!frame.sub || !VOICE_SUBS.includes(frame.sub)) return;
    frame.emote = frame.emote == null ? null : (sanitizeText(frame.emote, EMOTE_ICON_MAX_CHARS) || null);
    this._ev.voiceFrameReceived.emit(frame, (e) => this._warn(`voiceFrame handler threw: ${e && e.message}`));
  }

  /**
   * Their `media_prep`. `=== true`, not truthy: the wire is untrusted, absent is
   * the common case (every client that predates this), and anything that is not
   * an explicit `true` means "ready" — the state that changes nothing.
   */
  _handleMediaPrep(msg) {
    const next = msg.preparing === true;
    if (next === this._remoteMediaPrep) return;
    this._remoteMediaPrep = next;
    this._info(`opponent media_prep -> ${next ? 'picking' : 'ready'}`);
    this._ev.mediaPrepChanged.emit(next, (e) => this._warn(`mediaPrepChanged handler threw: ${e && e.message}`));
  }

  _handleRemoteMercy(mercy) {
    if (this._ended) return;
    this._remoteMercyMatchMs = mercy.at_match_ms;
    if (this._phase === GoonMatchPhase.SuddenDeath) this._forwardToRunner(mercy);

    const preLive = this._isPreLive(this._phase);
    const draw = this._localMercyMatchMs !== null && Math.abs(mercy.at_match_ms - this._localMercyMatchMs) <= DRAW_WINDOW_MS;

    this._info(`opponent mercy at ${mercy.at_match_ms} (draw=${draw})`);
    this._endMatch(draw ? GoonEndReason.Draw : GoonEndReason.Mercy, false, !preLive);
  }

  _isPreLive(phase) {
    return phase === GoonMatchPhase.Lobby || phase === GoonMatchPhase.Consent
      || phase === GoonMatchPhase.Draft || phase === GoonMatchPhase.Countdown;
  }

  // ------------------------------------------------------ sudden death

  _enterSuddenDeath() {
    if (this._phase !== GoonMatchPhase.Live) return;

    this._stopAllElements();
    this._setPhase(GoonMatchPhase.SuddenDeath);

    const runner = this._runner;
    if (!runner) {
      // No runner attached: degrade to the score comparison rather than hanging.
      this._warn('no sudden-death runner attached - settling on score');
      const mine = this._scoring.score;
      const theirs = this._opponent.score;
      if (mine === theirs) this._endMatch(GoonEndReason.Draw, false, true);
      else this._endMatch(GoonEndReason.SuddenDeathLoss, mine < theirs, true);
      return;
    }

    this._runnerUnsubs = [];
    const sub = (name, fn) => {
      const reg = bindFn(runner, [name]);
      if (reg) {
        const off = reg(fn);
        if (typeof off === 'function') this._runnerUnsubs.push(off);
      } else {
        this._warn(`sudden-death runner has no ${name}()`);
      }
    };
    sub('onRoundWon', (roundNo) => this._onRoundWon(roundNo));
    sub('onRoundLost', (roundNo) => this._onRoundLost(roundNo));
    sub('onNetLossReached', (localLost) => this._onNetLossReached(localLost));

    const ctx = {
      transport: this._transport,
      clock: this._clock(),
      rngFactory: this._rngFactory,
      matchSeed: this._matchSeed,
      isHost: this._isHost,
      localMode: this.localAttentionMode,
      remoteMode: this._opponent.attentionMode,
      netLossThreshold: GoonConsts.SuddenDeathNetLoss,
      // Caps intersection: never schedule a round the peer's client cannot render.
      allowedRoundKinds: this._allowedRoundKinds,
    };

    this._runnerAbort = (typeof AbortController === 'function') ? new AbortController() : null;
    void this._runSuddenDeath(runner, ctx, this._runnerAbort ? this._runnerAbort.signal : undefined);
  }

  async _runSuddenDeath(runner, ctx, signal) {
    try {
      const start = bindFn(runner, ['startAsync', 'start']);
      if (!start) throw new Error('sudden-death runner has no startAsync()');
      await start(ctx, signal);
    } catch (e) {
      if (signal && signal.aborted) return;               // mercy / dispose during sudden death
      if (e && e.name === 'AbortError') return;
      this._error(`sudden-death runner failed: ${(e && e.stack) || e}`);
      if (this._disposed || this._ended) return;
      try { this._endMatch(GoonEndReason.Draw, false, true); }
      catch (inner) { this._error(`sudden-death failure handling threw: ${(inner && inner.stack) || inner}`); }
    }
  }

  _forwardToRunner(message) {
    const runner = this._runner;
    if (!runner) return;
    const handle = bindFn(runner, ['handleMessage']);
    if (!handle) return;
    try { handle(message); }
    catch (e) { this._error(`runner.handleMessage threw for ${message.t}: ${(e && e.stack) || e}`); }
  }

  _onRoundWon(roundNo) {
    if (this._disposed) return;
    this._info(`sudden-death round ${roundNo} won`);
    this._scoring.awardEventWon();
  }

  _onRoundLost(roundNo) {
    if (this._disposed) return;
    this._info(`sudden-death round ${roundNo} lost`);
  }

  _onNetLossReached(localLost) {
    if (this._disposed) return;
    this._endMatch(GoonEndReason.SuddenDeathLoss, !!localLost, true);
  }

  _detachRunner() {
    const runner = this._runner;
    if (!runner) return;

    for (const off of this._runnerUnsubs) { try { off(); } catch { /* already gone */ } }
    this._runnerUnsubs = [];
    this._runner = null;

    try { if (this._runnerAbort) this._runnerAbort.abort(); } catch { /* already gone */ }
    this._runnerAbort = null;

    const stop = bindFn(runner, ['stopAsync', 'stop']);
    if (!stop) return;
    try {
      const p = stop();
      if (p && typeof p.then === 'function') p.then(undefined, (e) => this._warn(`sudden-death stopAsync threw: ${e && e.message}`));
    } catch (e) {
      this._warn(`sudden-death stopAsync threw: ${e && e.message}`);
    }
  }

  // ------------------------------------------------- end + result sign

  _endMatch(reason, localLost, countsForLedger) {
    if (this._ended) return;
    this._ended = true;

    this._stopTimers();
    this._stopAllElements();
    this._detachRunner();

    const survivedMs = this._liveElapsed();
    this._stopLiveWatch();

    const winnerIsHost = reason === GoonEndReason.Draw ? null : (localLost ? !this._isHost : this._isHost);

    const result = new GoonMatchResult({
      endReason: reason,
      winnerIsHost,
      localIsHost: this._isHost,
      hostScore: this._isHost ? this._scoring.score : this._opponent.score,
      guestScore: this._isHost ? this._opponent.score : this._scoring.score,
      survivedMs,
      countsForLedger,
    });
    this._result = result;

    this._setPhase(GoonMatchPhase.Recap);

    this._send(makeResult({
      end_reason: reason,
      winner_is_host: winnerIsHost,
      host_score: result.hostScore,
      guest_score: result.guestScore,
      survived_ms: survivedMs,
      agree: false,
    }));

    this._resultDeadlineLocalMs = localMonotonicMs() + RESULT_HANDSHAKE_TIMEOUT_MS;
    this._info(`match ended: ${reason}, localLost=${localLost}, survived ${Math.trunc(survivedMs / 1000)}s, ` +
      `${result.hostScore}-${result.guestScore}`);

    this._ev.matchEnded.emit(result, (e) => this._error(`matchEnded handler threw: ${(e && e.stack) || e}`));
  }

  _handleRemoteResult(remote) {
    if (!this._ended) {
      // Their end signal reached us before ours fired (lost mercy, or a sudden-death exit resolved
      // on their side first). Adopt their outcome.
      const localLost = remote.winner_is_host !== null && remote.winner_is_host !== undefined
        && remote.winner_is_host !== this._isHost;
      this._endMatch(remote.end_reason, localLost, true);
    }
    const result = this._result;
    if (!result) return;

    // Each side's own score is authoritative for its own half.
    if (this._isHost) result.guestScore = remote.guest_score;
    else result.hostScore = remote.host_score;

    if (remote.agree) {
      result.agreed = true;
      result.disputed = false;
      this._finalizeResult();
      return;
    }

    const sameReason = remote.end_reason === result.endReason;
    const remoteWinner = (remote.winner_is_host === undefined) ? null : remote.winner_is_host;
    const sameWinner = remoteWinner === result.winnerIsHost;

    if (sameReason && sameWinner) {
      if (!this._countersignSent) {
        this._countersignSent = true;
        this._send(makeResult({
          end_reason: result.endReason,
          winner_is_host: result.winnerIsHost,
          host_score: result.hostScore,
          guest_score: result.guestScore,
          survived_ms: result.survivedMs,
          agree: true,
        }));
      }
      result.agreed = true;
      result.disputed = false;
    } else {
      // Disputed: both claims are recorded; the ledger stores the pair and the uncontested
      // cosmetics (survival time, payload counts) are still granted.
      result.disputed = true;
      result.remoteClaim = remote;
      this._warn(`result DISPUTED - local ${result.endReason}/${result.winnerIsHost} vs remote ${remote.end_reason}/${remoteWinner}`);
    }

    this._finalizeResult();
  }

  _finalizeResult() {
    const result = this._result;
    if (this._finalized || !result) return;
    this._finalized = true;
    this._stopPhaseTimer();
    this._ev.resultFinalized.emit(result, (e) => this._error(`resultFinalized handler threw: ${(e && e.stack) || e}`));
  }

  // ------------------------------------------------------ phase plumbing

  _setPhase(phase) {
    if (this._phase === phase) return;
    this._phase = phase;
    this._info(`phase -> ${phase}`);
    this._ev.phaseChanged.emit(phase, (e) => this._error(`phaseChanged handler threw: ${(e && e.stack) || e}`));
  }

  _startPhaseTimer() {
    if (this._phaseTicker) return;
    this._phaseTicker = ticker(PHASE_TIMER_INTERVAL_MS, () => this._phaseTick(), { logger: this._log, tag: `${this._tag}Phase` });
  }

  _phaseTick() {
    if (this._disposed) return;
    try {
      switch (this._phase) {
        case GoonMatchPhase.Draft:
          this._tryProposeStart();
          break;

        case GoonMatchPhase.Countdown:
          if (this._startMatchMs > 0 && this._clockNow() >= this._startMatchMs) this._enterLive();
          break;

        case GoonMatchPhase.Recap:
          if (!this._finalized && localMonotonicMs() >= this._resultDeadlineLocalMs) {
            if (this._result && !this._result.agreed) {
              this._result.disputed = true;
              this._warn('result handshake timed out - recorded as disputed');
            }
            this._finalizeResult();
          }
          break;

        default:
          break;
      }
    } catch (e) {
      this._error(`phase tick failed: ${(e && e.stack) || e}`);
    }
  }

  _stopPhaseTimer() {
    if (!this._phaseTicker) return;
    this._phaseTicker.stop();
    this._phaseTicker = null;
  }

  _stopTimers() {
    if (this._liveTicker) {
      this._liveTicker.stop();
      this._liveTicker = null;
    }
    // The phase timer keeps running through Recap for the handshake deadline.
  }

  /** Stops every element the ramp started. exec/ does the real teardown. */
  _stopAllElements() {
    if (this._activeElements.size === 0) return;
    const elapsed = this._liveElapsed();
    for (const element of Array.from(this._activeElements)) {
      this._raiseCue('elementStopRequested', { element, intensity: 0, durationMs: 0, elapsedMs: elapsed });
    }
    this._activeElements.clear();
  }

  _resetToIdle() {
    this._stopTimers();
    this._stopPhaseTimer();
    this._setPhase(GoonMatchPhase.Idle);
  }

  // ------------------------------------------------------------ helpers

  _clock() {
    try { return this._transport.clock || null; } catch { return null; }
  }

  _clockNow() {
    try {
      const c = this._clock();
      return c ? c.nowMatchMs() : 0;
    } catch { return 0; }
  }

  _restartLiveWatch() {
    this._liveWatchStartLocalMs = localMonotonicMs();
    this._liveWatchStoppedLocalMs = null;
  }

  _stopLiveWatch() {
    if (this._liveWatchStartLocalMs !== null && this._liveWatchStoppedLocalMs === null) {
      this._liveWatchStoppedLocalMs = localMonotonicMs();
    }
  }

  /** Monotonic ms since the Live phase began; frozen once the match ends (C# Stopwatch). */
  _liveElapsed() {
    if (this._liveWatchStartLocalMs === null) return 0;
    const end = this._liveWatchStoppedLocalMs === null ? localMonotonicMs() : this._liveWatchStoppedLocalMs;
    return Math.floor(end - this._liveWatchStartLocalMs);
  }

  _send(message) {
    if (this._disposed || !message) return;
    try {
      if (!this._send$) { this._warn(`no transport send for ${message.t}`); return; }
      const p = this._send$(message);
      if (p && typeof p.then === 'function') {
        p.then(undefined, (e) => this._warn(`send failed for ${message.t}: ${e && e.message}`));
      }
    } catch (e) {
      this._warn(`send failed for ${message.t}: ${e && e.message}`);
    }
  }

  _raiseCue(name, args) {
    this._ev[name].emit(args, (e) => this._error(`element cue handler threw for ${args.element}: ${(e && e.stack) || e}`));
  }

  _info(m) { if (this._log && this._log.info) this._log.info(`[${this._tag}] ${m}`); }
  _warn(m) { if (this._log && this._log.warn) this._log.warn(`[${this._tag}] ${m}`); }
  _error(m) { if (this._log && this._log.error) this._log.error(`[${this._tag}] ${m}`); }

  dispose() {
    if (this._disposed) return;
    this._disposed = true;

    for (const off of this._transportUnsubs) { try { off(); } catch { /* already gone */ } }
    this._transportUnsubs = [];

    this._stopTimers();
    this._stopPhaseTimer();
    this._detachRunner();
    this._stopLiveWatch();
    for (const ev of Object.values(this._ev)) ev.clear();
  }
}

// ------------------------------------------------------------------ sheet helpers

/**
 * Re-sign a sheet.
 *
 * `mediaTransfer` and `voiceNotes` are passed in rather than copied off `sheet` because one of the
 * call sites hands this function the INBOUND sheet (the counter-proposal branch of
 * `_handleConsent`). Copying them there would adopt the PEER's opt-ins as our own — a consent flag
 * silently flipped by the other player, which is the one thing a per-side declaration exists to
 * prevent. Always OUR values, on every branch.
 *
 * A NEW PER-SIDE DECLARATION MUST BE ADDED AS A PARAMETER, never read off `sheet`. That is the
 * entire discipline here, and `voice_notes` is the second field to follow it.
 */
function cloneSheet(sheet, confirmed, mediaTransfer, voiceNotes) {
  return makeConsent({
    live_duration_sec: sheet.live_duration_sec,
    toy_cap: sheet.toy_cap,
    payload_min_gap_ms: sheet.payload_min_gap_ms,
    confirmed,
    media_transfer: !!mediaTransfer,
    voice_notes: !!voiceNotes,
  });
}

/** Set identity for two CANONICAL (normalizeAllowed'd) element lists. */
function sameElementSet(a, b) {
  if (!a || !b || a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}

/**
 * Sheet identity ignores `confirmed` — the TERMS must match, not the signature.
 *
 * DO NOT ADD FIELDS TO THIS FUNCTION. It is the sheet FINGERPRINT, and a mismatch makes
 * `_handleConsent` adopt the peer's sheet and clear BOTH confirmations. Teach it about a field a
 * peer might not send — `media_transfer` and `voice_notes` being the live examples, but the rule
 * is general — and
 * that peer (the C# reference client, an older page, anything that drops the member) echoes a
 * sheet that can NEVER compare equal: each side keeps adopting the other's sheet, keeps clearing
 * the other's confirmation, and the lobby wedges PERMANENTLY. There is no timeout out of it.
 *
 * Anything genuinely per-side belongs OUTSIDE the fingerprint, declared on the frame and ANDed
 * locally — that is exactly what `media_transfer` does (see makeConsent in core/contracts.js).
 */
function sameSheet(a, b) {
  return a.live_duration_sec === b.live_duration_sec
    && Math.abs(a.toy_cap - b.toy_cap) < 0.0001
    && a.payload_min_gap_ms === b.payload_min_gap_ms;
}
