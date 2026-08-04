using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// ============================================================================
// GOON GAME — Phase 0 contracts. Fable-authored; agents DO NOT redesign.
// Extend via NEW members only if genuinely required, and note the addition in
// docs/GOON_GAME_PLAN.md ("Decisions made in Phase X").
//
// Wire format: Newtonsoft JSON, snake_case via explicit [JsonProperty] on every
// serialized member. Every P2P message inherits GoonMessage and is routed by
// its "t" discriminator. Times labeled *MatchMs* are on the shared MatchClock
// (see IMatchClock); times labeled *LocalMs* never leave this machine.
//
// PLATFORM-AGNOSTIC MANDATE (2026-08-03): mobile (CCP-Mobile/Expo) and web
// clients are planned. The PROTOCOL is the product; this file is merely the
// C# binding of it. Therefore: wire enums carry EXPLICIT integer codes and are
// NEVER reordered or renumbered; clients advertise capabilities in Hello and
// may only be sent what they advertised; nothing in any schema may assume
// Windows/WPF. The neutral spec lives in docs/GOON_GAME_PROTOCOL.md (written
// after Wave-1) — schema changes here must be mirrored there.
// ============================================================================

namespace ConditioningControlPanel.Services.GoonGame
{
    // ------------------------------------------------------------------ enums

    public enum GoonMatchPhase
    {
        Idle,
        Lobby,          // invite created / joined, waiting for both parties
        Consent,        // consent sheet shown, awaiting both confirms
        Draft,          // element drafting
        Countdown,      // clock synced, start scheduled
        Live,           // continuous endurance phase
        SuddenDeath,    // synchronized rounds
        Recap,
    }

    public enum GoonAttentionMode { Cam = 0, NoCam = 1 }

    /// <summary>Draft pool. Each element = a session ingredient the drafting player ENDURES.
    /// Wire codes are FROZEN — append only, never renumber.</summary>
    public enum GoonElement
    {
        Flashes = 0,
        Videos = 1,
        Subliminals = 2,
        Bubbles = 3,
        LockCards = 4,
        ToyPatterns = 5,
        BrainDrain = 6,
        BouncingText = 7,
        Spiral = 8,     // the DTRH hypnotic spiral as a duel overlay (in-page veil, see GoonDraft)
    }

    /// <summary>
    /// Offensive payload kinds. THIS IS THE WHOLE SET — GG never dispatches
    /// Remote Control verbs. Panic/strict-lock/tray/session/wallpaper/autonomy/
    /// mind-wipe/hypnotube are BANNED by design (plan §safety rails).
    /// </summary>
    public enum GoonPayloadKind
    {
        FlashBurst = 0,
        SubliminalStorm = 1,
        BubbleSwarm = 2,
        Video = 3,      // mandatory video, attention checks ON
        LockCard = 4,   // voice if receiver enabled it, typed fallback
        ToyPattern = 5, // haptics v2 mixer only; receiver MasterCap applies
        BrainDrain = 6, // the one "heavy"; once per match per player
        Spiral = 7,     // hypnotic spiral overlay; sustained, repeatable (NOT the heavy)
    }

    public enum GoonRoundKind
    {
        QuickDrawLockCard = 0,  // shared-seed identical card, race to solve
        StaringContest = 1,     // cam+cam only
        ReactionDuel = 2,       // fallback when either side is NoCam
        BubbleRace = 3,         // shared-seed identical swarm, race to clear
    }

    public enum GoonEndReason
    {
        Mercy = 0,              // dignified self-declared finish (Esc ladder lands here)
        SuddenDeathLoss = 1,    // net -3 rounds in sudden death
        Abandon = 2,            // disconnect beyond grace, no resume
        Draw = 3,               // both merciful within the same tick window
    }

    // --------------------------------------------------------------- constants

    public static class GoonConsts
    {
        public const int ProtocolVersion = 1;

        // Timing
        public const int LiveDurationSecDefault = 720;      // 12 min
        public const int TickIntervalMs = 3000;             // state tick cadence
        public const int TickStaleMs = 15000;               // "wobbly" UI threshold
        public const int TickDeadMs = 60000;                // abandon flow threshold
        public const int MinScheduleBufferMs = 1000;        // fire-at >= now + this
        public const int ClockPingRounds = 10;              // NTP-style sync rounds
        public const int ClockResyncIntervalMs = 30000;
        public const int IceTimeoutMs = 10000;              // P2P give-up -> relay
        public const int ReconnectGraceMs = 5000;

        // Payload economy (receiver-enforced; the wire is untrusted)
        public const int PayloadMinGapMs = 30000;           // 1 per 30 s ...
        public const int PayloadBurst = 2;                  // ... with burst of 2
        public const int ChargeCap = 3;
        public const int ChargeTrickleMs = 90000;           // +1 per clean 90 s
        public const int OpponentTextMaxChars = 200;        // cap+strip like trigger_custom_subliminal

        // Reaction fairness
        public const int SuspectReactionMs = 100;           // below this = badge as suspect

        // Sudden death
        public const int SuddenDeathNetLoss = 3;            // net rounds down -> loss

        // Scoring
        public const double DraftRiskStep = 0.15;           // x (1 + step * riskTier)
        public const double AttentionMultMin = 0.5;
        public const double AttentionMultMax = 1.5;
        public const double NoCamFailedCheckMult = 0.6;     // for 60 s after a failed check
        public const int NoCamFailedCheckPenaltyMs = 60000;

        // Floating video windows on the tick (StateTickMsg.VideoWindows). TWO caps on purpose:
        // the WIRE cap is what a frame may even claim, the DISPLAY cap is what a monitor will
        // ever draw (the web client's exec/videos.js MAX_WINDOWS). Clamping to the wire cap and
        // then to the display cap means a peer that grows its pool to six still reads as "full"
        // instead of as garbage.
        public const int VideoWindowsWireMax = 8;
        public const int VideoWindowsDisplayMax = 4;

        /// <summary>Charge cost per payload kind.</summary>
        public static int CostOf(GoonPayloadKind kind) => kind switch
        {
            GoonPayloadKind.FlashBurst => 1,
            GoonPayloadKind.SubliminalStorm => 1,
            GoonPayloadKind.BubbleSwarm => 1,
            GoonPayloadKind.Video => 2,
            GoonPayloadKind.LockCard => 2,
            GoonPayloadKind.ToyPattern => 2,
            GoonPayloadKind.BrainDrain => 3,
            GoonPayloadKind.Spiral => 2,
            _ => int.MaxValue,
        };
    }

    // -------------------------------------------------------------- transport

    /// <summary>State of the underlying channel, whatever the mode.</summary>
    public enum GoonTransportState { Disconnected, Signaling, ConnectingP2P, ConnectedP2P, ConnectedRelay, Reconnecting, Closed }

    /// <summary>
    /// One duplex ordered-reliable message channel to the opponent. Implementations:
    /// WebRTC data channel (primary), proxy relay (fallback), in-process loopback (mock).
    /// Implementations own serialization; callers deal in GoonMessage objects.
    /// </summary>
    public interface IGoonTransport : IDisposable
    {
        GoonTransportState State { get; }
        IMatchClock Clock { get; }
        event EventHandler<GoonMessage>? MessageReceived;      // raised on the UI dispatcher
        event EventHandler<GoonTransportState>? StateChanged;  // raised on the UI dispatcher

        /// <summary>Host path: mint invite via server, return the code to show the user.</summary>
        System.Threading.Tasks.Task<string?> CreateInviteAsync(System.Threading.CancellationToken ct);
        /// <summary>Joiner path: connect using a code the user typed.</summary>
        System.Threading.Tasks.Task<bool> JoinAsync(string inviteCode, System.Threading.CancellationToken ct);
        System.Threading.Tasks.Task SendAsync(GoonMessage message, System.Threading.CancellationToken ct);
        System.Threading.Tasks.Task CloseAsync();
    }

    /// <summary>
    /// Shared match clock. Offset established by ping rounds on channel open
    /// (median of GoonConsts.ClockPingRounds), re-synced every ClockResyncIntervalMs.
    /// Monotonic (Stopwatch-backed) — NEVER wall clock.
    /// </summary>
    public interface IMatchClock
    {
        bool IsSynced { get; }
        long NowMatchMs { get; }
        double RttMs { get; }
        /// <summary>Convert a shared-clock timestamp to a local Environment.TickCount64-comparable instant.</summary>
        long MatchMsToLocalMs(long matchMs);
    }

    /// <summary>Deterministic PRNG both sides construct from the same seed. xoshiro256**.</summary>
    public interface IGoonRng
    {
        ulong NextULong();
        int NextInt(int minInclusive, int maxExclusive);
        double NextDouble();
    }

    // -------------------------------------------------------------- messages

    /// <summary>Base envelope. "t" is the discriminator used by the router.</summary>
    public abstract class GoonMessage
    {
        [JsonProperty("t")] public abstract string Type { get; }
        [JsonProperty("v")] public int Version { get; set; } = GoonConsts.ProtocolVersion;
    }

    public sealed class ClockPingMsg : GoonMessage
    {
        public override string Type => "clock_ping";
        [JsonProperty("seq")] public int Seq { get; set; }
        [JsonProperty("sent_local_ms")] public long SentLocalMs { get; set; }
    }

    public sealed class ClockPongMsg : GoonMessage
    {
        public override string Type => "clock_pong";
        [JsonProperty("seq")] public int Seq { get; set; }
        [JsonProperty("echo_sent_local_ms")] public long EchoSentLocalMs { get; set; }
        [JsonProperty("pong_local_ms")] public long PongLocalMs { get; set; }
    }

    /// <summary>
    /// Per-client capability advertisement. Mobile/web clients advertise a smaller
    /// set. RULES: the draft pool offered to a player = the INTERSECTION of both
    /// players' SupportedElements; a sender may only send payload kinds the
    /// RECEIVER advertised (receiver drops others with a rejected_filtered
    /// receipt); sudden-death round kinds come from the rounds intersection
    /// (ReactionDuel is the universal fallback every client MUST support).
    /// </summary>
    public sealed class GoonCaps
    {
        [JsonProperty("platform")] public string Platform { get; set; } = "windows"; // windows|android|ios|web
        [JsonProperty("payloads")] public List<GoonPayloadKind> SupportedPayloads { get; set; } = new();
        [JsonProperty("elements")] public List<GoonElement> SupportedElements { get; set; } = new();
        [JsonProperty("rounds")] public List<GoonRoundKind> SupportedRounds { get; set; } = new();
        [JsonProperty("min_v")] public int MinProtocolVersion { get; set; } = GoonConsts.ProtocolVersion;
    }

    /// <summary>Lobby hello: identity + capabilities + consent inputs.</summary>
    public sealed class HelloMsg : GoonMessage
    {
        public override string Type => "hello";
        [JsonProperty("display_name")] public string DisplayName { get; set; } = "";
        [JsonProperty("attention_mode")] public GoonAttentionMode AttentionMode { get; set; }
        [JsonProperty("toy_connected")] public bool ToyConnected { get; set; }
        [JsonProperty("app_version")] public string AppVersion { get; set; } = "";
        [JsonProperty("caps")] public GoonCaps Caps { get; set; } = new();
    }

    /// <summary>
    /// The consent sheet both players must confirm, byte-identical on both sides.
    ///
    /// <c>voice_notes</c> (2026-08-04) is APPEND-ONLY, on the vwin precedent, and it is NOT A TERM
    /// OF THE SHEET. It is a PER-SIDE DECLARATION that happens to ride the consent frame: on the
    /// wire it always means "THE SENDER opts in to voice notes", never "the agreed value", and each
    /// client ANDs its own value with the one it last heard from the peer (the web client's
    /// core/match.js <c>voiceNotesAgreed</c>).
    ///
    /// IT MUST NEVER JOIN THE SHEET FINGERPRINT. The comparison that decides "same terms or a
    /// counter-proposal" is over the three real terms above; a client that includes this field in
    /// it would never compare equal against a peer that drops it, both sides would clear each
    /// other's confirmations forever, and the lobby would wedge with no timeout out of it. (See the
    /// guard comment on sameSheet() in the web client's core/match.js.)
    ///
    /// ABSENT MEANS FALSE — no declaration is no opt-in — which is also why there is no Normalize
    /// entry for it in GoonWire: a bool has no illegal range to clamp, Newtonsoft's error handler
    /// already lands a malformed value on <c>false</c>, and a peer that has never heard of the
    /// field reads exactly the same thing as one that opted out.
    ///
    /// THIS CLIENT NEVER SENDS OR PLAYS AUDIO. The voice-note family (<c>t:"voice"</c>) is
    /// web-only; this property exists so the reference implementation round-trips the sheet without
    /// dropping a member — parity, not participation. It ships <c>false</c> and means it.
    /// </summary>
    public sealed class ConsentSheetMsg : GoonMessage
    {
        public override string Type => "consent";
        [JsonProperty("live_duration_sec")] public int LiveDurationSec { get; set; } = GoonConsts.LiveDurationSecDefault;
        [JsonProperty("toy_cap")] public double ToyCap { get; set; } = 0.7;   // can only LOWER the receiver's own cap
        [JsonProperty("payload_min_gap_ms")] public int PayloadMinGapMs { get; set; } = GoonConsts.PayloadMinGapMs;
        [JsonProperty("confirmed")] public bool Confirmed { get; set; }
        [JsonProperty("voice_notes")] public bool VoiceNotes { get; set; }   // per-side opt-in; ABSENT = false
    }

    /// <summary>
    /// The draft agreement (2026-08-03 redesign). <c>allowed</c> is the sender's ALLOWED element
    /// set and <c>confirmed</c> its signature on the current pair of sets; the effective pool is
    /// the INTERSECTION of the two allowed sets, and any change to either clears BOTH confirms.
    ///
    /// <c>elements</c>/<c>locked</c> are the v1 field names. They stay on the wire carrying the
    /// same values as <c>allowed</c>/<c>confirmed</c> so an older reader (or a capture, or a log)
    /// still parses; new readers prefer the new pair and fall back to the legacy one when it is
    /// absent. APPEND-ONLY: never renumber, never repurpose, never drop a field.
    /// </summary>
    public sealed class DraftMsg : GoonMessage
    {
        public override string Type => "draft";
        [JsonProperty("elements")] public List<GoonElement> Elements { get; set; } = new();  // legacy mirror of Allowed
        [JsonProperty("locked")] public bool Locked { get; set; }                            // legacy mirror of Confirmed
        [JsonProperty("allowed")] public List<GoonElement>? Allowed { get; set; }
        [JsonProperty("confirmed")] public bool? Confirmed { get; set; }
    }

    /// <summary>Match start: fires the Live phase at a shared-clock instant.</summary>
    public sealed class MatchStartMsg : GoonMessage
    {
        public override string Type => "match_start";
        [JsonProperty("start_match_ms")] public long StartMatchMs { get; set; }
        [JsonProperty("seed_contribution")] public ulong SeedContribution { get; set; }  // XOR'd with peer's
    }

    /// <summary>
    /// Periodic state tick (GoonConsts.TickIntervalMs).
    ///
    /// <c>vwin</c> (2026-08-04) is APPEND-ONLY, exactly like DraftMsg's allowed/confirmed pair:
    /// how many FLOATING VIDEO WINDOWS the sender currently has up (the web client's
    /// exec/videos.js pool, capped at four). It cannot be derived from <c>active_effects</c> and
    /// never could — half of those windows are SELF-POPPED (a `video` bubble the sender popped on
    /// their own field), which the other side has no way of knowing about, and a thrown one is a
    /// payload, which never appears in active_effects either.
    ///
    /// ABSENT MEANS ZERO. An older peer omits it and reads 0 here; this reference client has no
    /// floating windows of its own, so it sends 0 and a web peer's monitor draws nothing. No new
    /// enum code, no reordering, no version branch.
    /// </summary>
    public sealed class StateTickMsg : GoonMessage
    {
        public override string Type => "tick";
        [JsonProperty("at_match_ms")] public long AtMatchMs { get; set; }
        [JsonProperty("score")] public int Score { get; set; }
        [JsonProperty("attention_pct")] public int AttentionPct { get; set; }        // NoCam: interaction-check health
        [JsonProperty("attention_mode")] public GoonAttentionMode AttentionMode { get; set; }
        [JsonProperty("active_effects")] public List<string> ActiveEffects { get; set; } = new();
        [JsonProperty("toy")] public bool ToyActive { get; set; }
        [JsonProperty("closeness")] public int? Closeness { get; set; }              // 0-3 self-report, null = hidden; bluffable BY DESIGN
        [JsonProperty("charges")] public int Charges { get; set; }
        [JsonProperty("vwin")] public int VideoWindows { get; set; }                 // 0-4 floating video windows; ABSENT = 0

        /// <summary>
        /// Untrusted window count -> 0..GoonConsts.VideoWindowsDisplayMax. Mirror of
        /// clampWindowCount() in the web client's core/contracts.js, and applied by GoonWire in
        /// BOTH directions so no consumer of a tick ever sees a negative or an absurd count.
        /// </summary>
        public static int ClampVideoWindows(int raw)
        {
            int wire = Math.Clamp(raw, 0, GoonConsts.VideoWindowsWireMax);
            return Math.Min(wire, GoonConsts.VideoWindowsDisplayMax);
        }
    }

    /// <summary>
    /// Offensive payload. NO raw media, NO urls — receiver resolves tags against its
    /// OWN library and enforces gap/burst/cost/text rules regardless of the wire.
    /// </summary>
    public sealed class PayloadMsg : GoonMessage
    {
        public override string Type => "payload";
        [JsonProperty("id")] public string Id { get; set; } = "";                     // sender-unique, echoed in receipts
        [JsonProperty("kind")] public GoonPayloadKind Kind { get; set; }
        [JsonProperty("fire_at_match_ms")] public long FireAtMatchMs { get; set; }    // >= now + MinScheduleBufferMs
        [JsonProperty("duration_ms")] public int DurationMs { get; set; }
        [JsonProperty("tags")] public List<string>? Tags { get; set; }                // resolved receiver-side
        [JsonProperty("text")] public string? Text { get; set; }                      // capped+stripped receiver-side
        [JsonProperty("voice")] public bool Voice { get; set; }                       // lock card: request voice (receiver may fall back)
        [JsonProperty("pattern")] public string? Pattern { get; set; }                // toy: pulse|wave|fireworks|earthquake
        [JsonProperty("intensity")] public double Intensity { get; set; }             // 0..1 pre-cap; receiver mixer caps
    }

    /// <summary>Receiver -> sender: payload accepted/rejected/completed (drives recap + charge refunds on reject).</summary>
    public sealed class PayloadReceiptMsg : GoonMessage
    {
        public override string Type => "payload_receipt";
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("status")] public string Status { get; set; } = "";             // accepted|rejected_rate|rejected_filtered|completed|survived
    }

    /// <summary>Schedules one sudden-death round at a shared instant with a combined seed.</summary>
    public sealed class RoundScheduleMsg : GoonMessage
    {
        public override string Type => "round";
        [JsonProperty("round_no")] public int RoundNo { get; set; }
        [JsonProperty("kind")] public GoonRoundKind Kind { get; set; }
        [JsonProperty("fire_at_match_ms")] public long FireAtMatchMs { get; set; }
        [JsonProperty("seed_contribution")] public ulong SeedContribution { get; set; }
        [JsonProperty("difficulty")] public int Difficulty { get; set; }              // escalates per round
    }

    /// <summary>Locally-measured round outcome. Reaction deltas are LOCAL stopwatch ms — latency never touches them.</summary>
    public sealed class RoundResultMsg : GoonMessage
    {
        public override string Type => "round_result";
        [JsonProperty("round_no")] public int RoundNo { get; set; }
        [JsonProperty("completed")] public bool Completed { get; set; }
        [JsonProperty("elapsed_ms")] public int ElapsedMs { get; set; }
        [JsonProperty("reaction_ms")] public int? ReactionMs { get; set; }
        [JsonProperty("suspect")] public bool Suspect { get; set; }                   // < SuspectReactionMs
        /// <summary>Phase D addition: round-specific tally the winner check needs when neither side
        /// "completed" — bubbles cleared (BubbleRace), average attention % (StaringContest),
        /// mistakes typed (QuickDraw), false-start flag (ReactionDuel). 0 when unused.</summary>
        [JsonProperty("progress")] public int Progress { get; set; }
    }

    public sealed class MercyMsg : GoonMessage
    {
        public override string Type => "mercy";
        [JsonProperty("at_match_ms")] public long AtMatchMs { get; set; }
    }

    public sealed class EmoteMsg : GoonMessage
    {
        public override string Type => "emote";
        [JsonProperty("text")] public string Text { get; set; } = "";                 // <= 60 chars, RC emote rules
        [JsonProperty("icon")] public string Icon { get; set; } = "";                 // <= 8 chars
    }

    /// <summary>End-of-match signed result; both clients also write this to /v2/goon/ledger.</summary>
    public sealed class ResultMsg : GoonMessage
    {
        public override string Type => "result";
        [JsonProperty("end_reason")] public GoonEndReason EndReason { get; set; }
        [JsonProperty("winner_is_host")] public bool? WinnerIsHost { get; set; }      // null = draw
        [JsonProperty("host_score")] public int HostScore { get; set; }
        [JsonProperty("guest_score")] public int GuestScore { get; set; }
        [JsonProperty("survived_ms")] public long SurvivedMs { get; set; }
        [JsonProperty("agree")] public bool Agree { get; set; }                       // counterparty countersign
    }
}
