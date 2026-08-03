// Goon Game protocol contracts — JS binding of Services/GoonGame/GoonContracts.cs.
// Wire enum codes are FROZEN: append only, never renumber (docs/GOON_GAME_PROTOCOL.md §9).
// Every factory returns the COMPLETE field set including nulls; wire.js drops nulls on the
// way out (Newtonsoft NullValueHandling.Ignore parity) and overlays inbound frames onto a
// fresh factory object so a missing member reads as its C# default.

export const PROTOCOL_VERSION = 1;

export const GoonElement = Object.freeze({
  Flashes: 0,
  Videos: 1,
  Subliminals: 2,
  Bubbles: 3,
  LockCards: 4,
  ToyPatterns: 5,
  BrainDrain: 6,
  BouncingText: 7,
  Spiral: 8,        // hypnotic spiral overlay (in-page veil) — see draft.js for its profile
});

export const GoonPayloadKind = Object.freeze({
  FlashBurst: 0,
  SubliminalStorm: 1,
  BubbleSwarm: 2,
  Video: 3,
  LockCard: 4,
  ToyPattern: 5,
  BrainDrain: 6,   // the one "heavy"; once per match per player
  Spiral: 7,       // sustained, repeatable (NOT the heavy)
});

export const GoonRoundKind = Object.freeze({
  QuickDrawLockCard: 0,
  StaringContest: 1,
  ReactionDuel: 2,
  BubbleRace: 3,
});

export const GoonEndReason = Object.freeze({
  Mercy: 0,
  SuddenDeathLoss: 1,
  Abandon: 2,
  Draw: 3,
});

export const GoonAttentionMode = Object.freeze({
  Cam: 0,
  NoCam: 1,
});

// Local-only (never on the wire). Codes = C# declaration order.
export const GoonMatchPhase = Object.freeze({
  Idle: 0,
  Lobby: 1,
  Consent: 2,
  Draft: 3,
  Countdown: 4,
  Live: 5,
  SuddenDeath: 6,
  Recap: 7,
});

export const GoonTransportState = Object.freeze({
  Disconnected: 0,
  Signaling: 1,
  ConnectingP2P: 2,
  ConnectedP2P: 3,
  ConnectedRelay: 4,
  Reconnecting: 5,
  Closed: 6,
});

/** Reverse lookup for logs. Returns the numeric code as a string when unknown. */
export function enumName(enumObj, code) {
  for (const k of Object.keys(enumObj)) if (enumObj[k] === code) return k;
  return String(code);
}

export const GoonConsts = Object.freeze({
  ProtocolVersion: 1,

  // Timing
  LiveDurationSecDefault: 720,
  TickIntervalMs: 3000,
  TickStaleMs: 15000,
  TickDeadMs: 60000,
  MinScheduleBufferMs: 1000,
  ClockPingRounds: 10,
  ClockResyncIntervalMs: 30000,
  IceTimeoutMs: 10000,
  ReconnectGraceMs: 5000,

  // Payload economy (receiver-enforced; the wire is untrusted)
  PayloadMinGapMs: 30000,
  PayloadBurst: 2,
  ChargeCap: 3,
  ChargeTrickleMs: 90000,
  OpponentTextMaxChars: 200,

  // Reaction fairness
  SuspectReactionMs: 100,

  // Sudden death
  SuddenDeathNetLoss: 3,

  // Scoring
  DraftRiskStep: 0.15,
  AttentionMultMin: 0.5,
  AttentionMultMax: 1.5,
  NoCamFailedCheckMult: 0.6,
  NoCamFailedCheckPenaltyMs: 60000,

  // Floating video windows on the tick (`vwin`). TWO caps, on purpose:
  // the WIRE cap is what a frame may even claim, the DISPLAY cap is what a
  // monitor will ever draw (exec/videos.js MAX_WINDOWS). Clamping to the wire
  // cap first and then to the display cap means a peer that grows its pool to
  // six still reads as "full" here instead of as garbage.
  VideoWindowsWireMax: 8,
  VideoWindowsDisplayMax: 4,
});

/**
 * Untrusted window count -> 0..VideoWindowsDisplayMax. Anything that is not a
 * finite number (null, a string, NaN, an object) is ZERO, which is also what an
 * ABSENT field means: an older peer, or the C# reference client, simply never
 * mentions `vwin` and its monitor stack stays empty.
 *
 * MIRROR: StateTickMsg.ClampVideoWindows in Services/GoonGame/GoonContracts.cs.
 */
export function clampWindowCount(v) {
  // A number, or a string a peer wrote its number into (some clients quote everything). NOTHING
  // else: `Number([3])` is 3 and `Number(true)` is 1 in this language, and an array or a boolean
  // arriving where an integer belongs is a broken frame, not a count of three windows.
  const n = typeof v === 'number' ? v : (typeof v === 'string' ? Number(v) : NaN);
  if (!Number.isFinite(n)) return 0;
  const i = Math.trunc(n);
  const wire = i < 0 ? 0 : (i > GoonConsts.VideoWindowsWireMax ? GoonConsts.VideoWindowsWireMax : i);
  return wire > GoonConsts.VideoWindowsDisplayMax ? GoonConsts.VideoWindowsDisplayMax : wire;
}

const COSTS = Object.freeze({
  [GoonPayloadKind.FlashBurst]: 1,
  [GoonPayloadKind.SubliminalStorm]: 1,
  [GoonPayloadKind.BubbleSwarm]: 1,
  [GoonPayloadKind.Video]: 2,
  [GoonPayloadKind.LockCard]: 2,
  [GoonPayloadKind.ToyPattern]: 2,
  [GoonPayloadKind.BrainDrain]: 3,
  [GoonPayloadKind.Spiral]: 2,
});

/**
 * Charge cost per payload kind. Unknown kind -> Infinity, mirroring C#'s int.MaxValue
 * default: a cost nothing can afford, i.e. the payload is rejected rather than free.
 */
export function costOf(kind) {
  const c = COSTS[kind];
  return c === undefined ? Infinity : c;
}

// ------------------------------------------------------------------ factories

/**
 * Capability advertisement (sub-object of hello, not a message).
 * NOTE: platform defaults to "web" here, diverging from the C# DTO's "windows" — this binding
 * only ever runs in a browser/WebView, and caps drive the draft/payload intersections.
 */
export function makeCaps(o = {}) {
  return {
    platform: o.platform ?? 'web',
    payloads: o.payloads ?? [],
    elements: o.elements ?? [],
    rounds: o.rounds ?? [],
    min_v: o.min_v ?? PROTOCOL_VERSION,
  };
}

export function makeHello(o = {}) {
  return {
    t: 'hello',
    v: o.v ?? PROTOCOL_VERSION,
    display_name: o.display_name ?? '',
    attention_mode: o.attention_mode ?? GoonAttentionMode.Cam,
    toy_connected: o.toy_connected ?? false,
    app_version: o.app_version ?? '',
    caps: makeCaps(o.caps ?? {}),
  };
}

export function makeConsent(o = {}) {
  return {
    t: 'consent',
    v: o.v ?? PROTOCOL_VERSION,
    live_duration_sec: o.live_duration_sec ?? GoonConsts.LiveDurationSecDefault,
    toy_cap: o.toy_cap ?? 0.7,
    payload_min_gap_ms: o.payload_min_gap_ms ?? GoonConsts.PayloadMinGapMs,
    confirmed: o.confirmed ?? false,
  };
}

/**
 * The draft agreement (2026-08-03 redesign). `allowed` is the sender's ALLOWED element set and
 * `confirmed` its signature on the current pair of sets; the effective pool is the intersection of
 * the two `allowed` sets.
 *
 * `elements`/`locked` are the v1 field names and stay on the wire, carrying the same values as
 * `allowed`/`confirmed`, so an older reader (or a log, or a capture) still parses. New readers
 * prefer `allowed`/`confirmed` and fall back to the legacy pair when they are absent.
 * APPEND-ONLY: never renumber, never repurpose, never drop a field from this shape.
 */
export function makeDraft(o = {}) {
  const allowed = o.allowed ?? o.elements ?? [];
  const confirmed = o.confirmed ?? o.locked ?? false;
  return {
    t: 'draft',
    v: o.v ?? PROTOCOL_VERSION,
    elements: o.elements ?? allowed,     // legacy mirror of `allowed`
    locked: o.locked ?? confirmed,       // legacy mirror of `confirmed`
    allowed,
    confirmed,
  };
}

export function makeMatchStart(o = {}) {
  return {
    t: 'match_start',
    v: o.v ?? PROTOCOL_VERSION,
    start_match_ms: o.start_match_ms ?? 0,
    seed_contribution: o.seed_contribution ?? 0n,
  };
}

/**
 * Periodic state tick.
 *
 * `vwin` (2026-08-04) is APPEND-ONLY, exactly like DraftMsg's allowed/confirmed pair: how many
 * FLOATING VIDEO WINDOWS the sender currently has up (exec/videos.js, shared pool of
 * MAX_WINDOWS=4). It cannot be derived from `active_effects` and never could — half of those
 * windows are SELF-POPPED (a `video` bubble the sender popped on their own field), which the
 * other side has no way of knowing about, and a thrown one is a payload, which never appears
 * in active_effects either.
 *
 * ABSENT MEANS ZERO. An older peer, or the C# reference client (which leaves its local count at
 * 0), omits it and every reader here reads 0 — no branch, no version check, no new enum code.
 */
export function makeTick(o = {}) {
  return {
    t: 'tick',
    v: o.v ?? PROTOCOL_VERSION,
    at_match_ms: o.at_match_ms ?? 0,
    score: o.score ?? 0,
    attention_pct: o.attention_pct ?? 0,
    attention_mode: o.attention_mode ?? GoonAttentionMode.Cam,
    active_effects: o.active_effects ?? [],
    toy: o.toy ?? false,
    closeness: o.closeness ?? null,
    charges: o.charges ?? 0,
    vwin: clampWindowCount(o.vwin),
  };
}

export function makePayload(o = {}) {
  return {
    t: 'payload',
    v: o.v ?? PROTOCOL_VERSION,
    id: o.id ?? '',
    kind: o.kind ?? GoonPayloadKind.FlashBurst,
    fire_at_match_ms: o.fire_at_match_ms ?? 0,
    duration_ms: o.duration_ms ?? 0,
    tags: o.tags ?? null,
    text: o.text ?? null,
    voice: o.voice ?? false,
    pattern: o.pattern ?? null,
    intensity: o.intensity ?? 0,
  };
}

export function makePayloadReceipt(o = {}) {
  return {
    t: 'payload_receipt',
    v: o.v ?? PROTOCOL_VERSION,
    id: o.id ?? '',
    status: o.status ?? '',
  };
}

export function makeRound(o = {}) {
  return {
    t: 'round',
    v: o.v ?? PROTOCOL_VERSION,
    round_no: o.round_no ?? 0,
    kind: o.kind ?? GoonRoundKind.QuickDrawLockCard,
    fire_at_match_ms: o.fire_at_match_ms ?? 0,
    seed_contribution: o.seed_contribution ?? 0n,
    difficulty: o.difficulty ?? 0,
  };
}

export function makeRoundResult(o = {}) {
  return {
    t: 'round_result',
    v: o.v ?? PROTOCOL_VERSION,
    round_no: o.round_no ?? 0,
    completed: o.completed ?? false,
    elapsed_ms: o.elapsed_ms ?? 0,
    reaction_ms: o.reaction_ms ?? null,
    suspect: o.suspect ?? false,
    progress: o.progress ?? 0,
  };
}

export function makeMercy(o = {}) {
  return {
    t: 'mercy',
    v: o.v ?? PROTOCOL_VERSION,
    at_match_ms: o.at_match_ms ?? 0,
  };
}

export function makeEmote(o = {}) {
  return {
    t: 'emote',
    v: o.v ?? PROTOCOL_VERSION,
    text: o.text ?? '',
    icon: o.icon ?? '',
  };
}

export function makeResult(o = {}) {
  return {
    t: 'result',
    v: o.v ?? PROTOCOL_VERSION,
    end_reason: o.end_reason ?? GoonEndReason.Mercy,
    winner_is_host: o.winner_is_host ?? null,
    host_score: o.host_score ?? 0,
    guest_score: o.guest_score ?? 0,
    survived_ms: o.survived_ms ?? 0,
    agree: o.agree ?? false,
  };
}

export function makeClockPing(o = {}) {
  return {
    t: 'clock_ping',
    v: o.v ?? PROTOCOL_VERSION,
    seq: o.seq ?? 0,
    sent_local_ms: o.sent_local_ms ?? 0,
  };
}

export function makeClockPong(o = {}) {
  return {
    t: 'clock_pong',
    v: o.v ?? PROTOCOL_VERSION,
    seq: o.seq ?? 0,
    echo_sent_local_ms: o.echo_sent_local_ms ?? 0,
    pong_local_ms: o.pong_local_ms ?? 0,
  };
}

/** "t" -> factory. This map IS the message catalog; wire.js routes off it. */
export const MessageFactories = Object.freeze({
  hello: makeHello,
  consent: makeConsent,
  draft: makeDraft,
  match_start: makeMatchStart,
  tick: makeTick,
  payload: makePayload,
  payload_receipt: makePayloadReceipt,
  round: makeRound,
  round_result: makeRoundResult,
  mercy: makeMercy,
  emote: makeEmote,
  result: makeResult,
  clock_ping: makeClockPing,
  clock_pong: makeClockPong,
});

export function isClockMessage(msg) {
  return !!msg && (msg.t === 'clock_ping' || msg.t === 'clock_pong');
}
