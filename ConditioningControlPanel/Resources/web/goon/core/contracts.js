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
  /**
   * THE BASE ICE BUDGET, spent from the moment negotiation STARTS. This is the
   * number for a STUN-only room: with no relay credentials in hand there is no
   * candidate left to wait for once host and srflx pairs have failed, so failing
   * fast to the ws relay is strictly better than making the player watch a
   * spinner. See IceRelayGraceMs for what happens when TURN IS in play.
   */
  IceTimeoutMs: 10000,
  /**
   * THE TURN EXTENSION (2026-08-05, the cellular play-test). Added to
   * IceTimeoutMs whenever the room actually handed us TURN credentials, because
   * 10 s is not a budget a relayed pair on a carrier link can meet:
   *
   *   * a dead STUN/TURN url does not fail fast — libwebrtc runs its full STUN
   *     retransmit ladder (~9.5 s) before it reports 701. The desktop's own log
   *     from that evening shows THREE of those (metered's stun urls + UDP TURN
   *     allocate) finishing at roughly the same moment gathering completed —
   *     the ENTIRE base budget went on gathering, before one check ran;
   *   * the candidate that actually works for a phone behind carrier-grade NAT
   *     is TURN over TCP/TLS 443, which is the LAST one gathered (TCP connect +
   *     TLS handshake + Allocate, all over a link with 100-300 ms RTT);
   *   * every candidate then crosses the /v2/goon/signal MAILBOX, polled every
   *     2 s, so each side's relay candidate reaches the other up to a poll
   *     cycle plus an HTTP round trip late.
   *
   * Ten seconds killed a connection that was still legitimately in progress,
   * and because the fallback is TERMINAL (no upgrade path; see net/session.js)
   * that one impatient deadline turned the whole media-transfer feature off for
   * the rest of the match — `supportsBulk` is P2P-only by design.
   *
   * Waiting longer costs nothing when the link is genuinely broken: the
   * pc.connectionState === 'failed' edge is the real backstop and fires as soon
   * as the browser has exhausted its checks, well inside this budget.
   */
  IceRelayGraceMs: 15000,
  /**
   * THE ONE-SHOT PROGRESS EXTENSION. Granted at most once, when the budget above
   * runs out while the browser says checks are still in flight
   * (iceConnectionState checking/connected/completed — see iceChecksInFlight).
   * 'connected' without an open data channel is the DTLS+SCTP handshake still
   * running over a relay, which is precisely the case where giving up would be
   * absurd: the pair already won.
   */
  IceProgressGraceMs: 8000,
  /**
   * GUEST ONLY: how long a joined guest waits for the host's first offer before
   * giving up on P2P (webrtcTransport.waitForChannel). A guest that has redeemed
   * the code is owed an offer within a couple of signal-poll round trips; a host
   * that never sends one (app closed, pump dead) used to leave the guest on
   * "joining…" FOREVER, because the ICE budget only starts once negotiation
   * does. Deliberately generous — a slow poll cycle must not eat a live host —
   * and it feeds the same relay-fallback ladder as an ICE timeout, so a host
   * that is alive but unreachable over P2P signaling still gets its second chance.
   */
  NoOfferTimeoutMs: 20000,
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

/* ----------------------------------------------------------------------------
 * VOICE NOTES (2026-08-04) — the `t:'voice'` family's untrusted-number clamps.
 *
 * Same rule as clampWindowCount above, applied to a family whose numbers are
 * SIZES AND COUNTS rather than a display cap: nothing downstream may ever see a
 * NaN, a negative or an Infinity, because the receiver's enforcement (declared
 * size, part count, sequence order) is arithmetic on exactly these fields and a
 * single NaN would turn every comparison into `false` — i.e. into "accept".
 *
 * The CEILINGS are not here on purpose. VN_MAX_BYTES and friends belong to the
 * enforcing tier (ui/voice/voiceService.js) and are policy, not shape; this file
 * only guarantees that what arrives is a non-negative integer.
 * -------------------------------------------------------------------------- */

/** The three sub-kinds of `t:'voice'`. FROZEN: append only, never repurpose. */
export const VOICE_SUBS = Object.freeze(['meta', 'chunk', 'end']);

/**
 * Untrusted sub -> one of VOICE_SUBS, or '' for anything else.
 *
 * '' rather than a throw or a default, because the receiver switches on it: an
 * unknown sub (a newer peer's fourth kind) falls off the end of the switch and
 * is ignored, which is the same forward-compatibility the unknown-`t` path has.
 */
export function clampVoiceSub(v) {
  return VOICE_SUBS.includes(v) ? v : '';
}

/**
 * Untrusted count/size -> a non-negative safe integer. Anything that is not a
 * finite number (or a string a peer quoted its number into) is ZERO, which is
 * also what an ABSENT field means. Booleans, arrays and objects are zero too —
 * `Number(true)` being 1 is exactly the coercion that must not happen here.
 */
export function clampVoiceCount(v) {
  const n = typeof v === 'number' ? v : (typeof v === 'string' ? Number(v) : NaN);
  if (!Number.isFinite(n)) return 0;
  const i = Math.trunc(n);
  if (i <= 0) return 0;
  return i > Number.MAX_SAFE_INTEGER ? Number.MAX_SAFE_INTEGER : i;
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
 *
 * WHAT A COST STILL MEANS (owner, 2026-08-05: the charge REQUIREMENT is gone). Nothing is paid
 * for any more — neither core/match.js nor ui/arsenal.js checks a balance before a throw. TWO
 * consumers survive, and both are about SHAPE rather than payment:
 *
 *   1. THE UNKNOWN-KIND GUARD. `Infinity` is still how both ends say "that is not a payload kind
 *      I know", which is the check that stops a malformed or future frame being scheduled. That
 *      guard is why costOf() is still called on the hot path at all.
 *   2. THE DROP RARITY CURVE. ui/drops.js weights each arsenal candidate at 1/cost^COST_BIAS, so
 *      a cost now reads as a RARITY: flash (1) rains, brain drain (3) is a treat. Moving a number
 *      here retunes how often that item DROPS, and nothing else.
 *
 * Keep the table in step with the C# side regardless: it is parity, and the wire still carries
 * the meter these numbers were once priced against.
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
 *
 * `transfer` (2026-08-04) is APPEND-ONLY, the vwin/DraftMsg precedent: "this build speaks the
 * P2P media-transfer protocol (net/mediaChannel.js) and will ACCEPT offers". It is a VERSION
 * DISCRIMINATOR, not an entitlement — every build that understands the protocol advertises it
 * true regardless of what the user pays for, because the premium gate is local and lives on the
 * SEND side only (session.caps.mediaTransfer).
 *
 * ABSENT MEANS FALSE. The C# reference client and any older page omit it; `false` means the
 * transfer queue never starts, no `xfer:` tags are emitted and that peer sees exactly the wire it
 * sees today. It enters NO intersection and can never fail a lobby.
 *
 * `voice` (2026-08-04) is the SAME KIND OF THING for the voice-note family (`t:'voice'`), and it
 * is an INTEGER rather than a boolean on purpose: it is a protocol REVISION number, so a later
 * build that changes the chunking or grows a sub can advertise 2 and both sides can tell which
 * dialect the other speaks without a second field. 0 (or absent) means "this build has never
 * heard of voice notes" — the mic is hidden and NOTHING is ever sent, because an unknown `t` is
 * dropped silently by the far side and a fire-and-forget family gets no receipt to notice with.
 * Like `transfer` it enters NO intersection and can never fail a lobby.
 */
export function makeCaps(o = {}) {
  return {
    platform: o.platform ?? 'web',
    payloads: o.payloads ?? [],
    elements: o.elements ?? [],
    rounds: o.rounds ?? [],
    min_v: o.min_v ?? PROTOCOL_VERSION,
    transfer: o.transfer ?? false,
    voice: clampVoiceCount(o.voice),
  };
}

/** The voice-note protocol revision THIS build speaks. Advertised as `caps.voice`. */
export const VOICE_CAP_VERSION = 1;

/**
 * A peer's `caps.voice`, from an UNTRUSTED hello, as a plain boolean "can we send to them".
 *
 * Forgiving about the shape and strict about the meaning: some clients quote every number, so a
 * `"1"` counts, but a boolean `true` does NOT — `true >= 1` is true in this language, and a peer
 * that put a boolean where a revision belongs is a broken frame, not a claim of support. Anything
 * unusable reads as "no support", which is the safe direction: we simply never send.
 */
export function peerSpeaksVoice(caps) {
  return clampVoiceCount(caps && caps.voice) >= 1;
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

/**
 * The consent sheet.
 *
 * `media_transfer` (2026-08-04) is APPEND-ONLY, the vwin/DraftMsg precedent, and it is NOT a term
 * of the sheet. ON THE WIRE IT ALWAYS MEANS "THE SENDER OPTS IN", NEVER "THE AGREED VALUE": it is
 * a PER-SIDE DECLARATION that happens to ride the consent frame, and each client ANDs its own
 * value with the one it last heard from the peer (core/match.js `mediaTransferAgreed`).
 *
 * It is deliberately absent from `sameSheet()` (match.js), which is the sheet FINGERPRINT. A peer
 * that drops the field — the C# client, an older page — would otherwise echo a sheet that never
 * compares equal, both sides would clear each other's confirmations forever, and the lobby would
 * wedge permanently. Read the guard comment on `sameSheet` before touching either.
 *
 * ABSENT MEANS FALSE: no declaration is no opt-in.
 *
 * `voice_notes` (2026-08-04) is a CLONE of that field in every respect — per-side declaration,
 * append-only, absent-means-false, outside `sameSheet()`, ANDed locally (core/match.js
 * `voiceNotesAgreed`) — and it is deliberately a second field rather than a flag folded into the
 * first: they are two different consents. "you may send me your library" and "you may send me
 * your VOICE" are not the same thing to agree to, and one must never be able to imply the other.
 */
export function makeConsent(o = {}) {
  return {
    t: 'consent',
    v: o.v ?? PROTOCOL_VERSION,
    live_duration_sec: o.live_duration_sec ?? GoonConsts.LiveDurationSecDefault,
    toy_cap: o.toy_cap ?? 0.7,
    payload_min_gap_ms: o.payload_min_gap_ms ?? GoonConsts.PayloadMinGapMs,
    confirmed: o.confirmed ?? false,
    media_transfer: o.media_transfer ?? false,
    voice_notes: o.voice_notes ?? false,
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

/**
 * ONE VOICE NOTE, in three sub-frames (2026-08-04). APPEND-ONLY, and it rides the CONTROL lane —
 * the same channel as every message above — never `goon-media`: the bulk channel does not exist on
 * the relay fallback, and voice has to work on the worst link a duel can end up on.
 *
 *   {t:'voice', sub:'meta',  id, bytes, durMs, emote|null, parts}   announces one note
 *   {t:'voice', sub:'chunk', id, seq, data}                         base64, capped well under 16K
 *   {t:'voice', sub:'end',   id}                                    after the last chunk
 *
 * ONE FACTORY FOR ALL THREE, because wire.js routes off `t` alone and a per-sub factory would mean
 * a second discriminator inside the parser. The unused members ride as their defaults (0 / null),
 * which is the vwin rule again — absent means the default, and the reader switches on `sub`.
 *
 * IT IS THE `t:'emote'` FAMILY, NOT A PAYLOAD. No cost, no charge, no receipt, no ACK, nothing in
 * active_effects, nothing in the ledger. A dropped frame is a note that never plays, and that is
 * the whole failure handling: re-sending, ACKing or queueing a voice note would make the wire a
 * place where somebody's voice can be owed to them.
 *
 * FIELD NAMING. `durMs` is camelCase where the rest of the wire is snake_case. That is pinned by
 * docs/GOON_VOICE_PLAN.md (the cross-client contract) and left exactly as written rather than
 * quietly "fixed" here — the name on the wire is the name in the spec, and there is no C# peer to
 * disagree with it (this family has no C# mirror; the C# client simply never sends or reads it).
 */
export function makeVoice(o = {}) {
  return {
    t: 'voice',
    v: o.v ?? PROTOCOL_VERSION,
    sub: clampVoiceSub(o.sub),
    /** Sender-local transfer id, monotonic. Unique per sender, NOT globally. */
    id: clampVoiceCount(o.id),
    /** chunk only: 0-based part index on an ORDERED lane, so it is a check, not a sort key. */
    seq: clampVoiceCount(o.seq),
    /** meta only: DECLARED total blob size. Checked against the accumulated one; both are capped. */
    bytes: clampVoiceCount(o.bytes),
    /** meta only: how many chunks to expect. */
    parts: clampVoiceCount(o.parts),
    /** meta only: recorded length. Cosmetic (the chip's timer) — playback is capped regardless. */
    durMs: clampVoiceCount(o.durMs),
    /** meta only: the emote this note is tied to, or null for a live one. Sanitized in match.js. */
    emote: o.emote ?? null,
    /** chunk only: base64 text. Null on meta/end so stripNulls keeps those two frames tiny. */
    data: o.data ?? null,
  };
}

/**
 * "I am still getting my library together" (protocol §6, v1.4).
 *
 * A PRESENCE HINT, not a term and not a phase. A first-time guest who arrived on
 * an invite link is sent through ui/screens/mediaSetup.js before the lobby, and
 * without this the host stares at "waiting for them" with no way to tell an
 * empty room from a busy one — which is exactly how a host gives up on a duel
 * thirty seconds before it would have started.
 *
 * NOTHING WAITS ON IT. The engine does not gate a phase, a confirmation or a
 * countdown on `preparing`; the lobby paints a line and that is the whole of it.
 * A peer that never sends it (an older build, the C# client, a guest who already
 * had a deck) reads as `false` — absent means "not preparing", exactly like
 * tick's `vwin` — and a peer that only ever sends `true` and then vanishes costs
 * nothing but a stale line on a screen that is already being torn down.
 *
 * APPEND-ONLY and unversioned by design: an older peer drops the whole frame as
 * an unknown `t` (wire.js parse logs and returns null), which is the documented
 * forward-compatible behaviour and needs no capability bit.
 */
export function makeMediaPrep(o = {}) {
  return {
    t: 'media_prep',
    v: o.v ?? PROTOCOL_VERSION,
    preparing: o.preparing ?? false,
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
  voice: makeVoice,
  media_prep: makeMediaPrep,
  result: makeResult,
  clock_ping: makeClockPing,
  clock_pong: makeClockPong,
});

export function isClockMessage(msg) {
  return !!msg && (msg.t === 'clock_ping' || msg.t === 'clock_pong');
}
