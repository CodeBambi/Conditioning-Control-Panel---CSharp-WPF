using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// ============================================================================
// GOON GAME — Phase B support types: event args, the opponent mirror, the
// finished-match record, and the narrow seam Phase D's sudden-death harness
// plugs into.
//
// These are Phase B types, NOT contracts — nothing here goes on the wire.
// GoonContracts.cs remains Fable's.
// ============================================================================

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>An element instruction from the local endurance ramp. Phase C executes these.</summary>
    public sealed class GoonElementCueEventArgs : EventArgs
    {
        public GoonElement Element { get; init; }
        /// <summary>0..1 pre-cap; every receiver-side cap still applies downstream.</summary>
        public double Intensity { get; init; }
        /// <summary>Burst length in ms; 0 = sustained until the matching Stop.</summary>
        public int DurationMs { get; init; }
        /// <summary>Milliseconds since the Live phase began (local monotonic).</summary>
        public long ElapsedMs { get; init; }
    }

    /// <summary>Everything the UI needs to fire an offensive payload; routing fields are filled by the service.</summary>
    public sealed class GoonPayloadRequest
    {
        public GoonPayloadKind Kind { get; init; }
        public int DurationMs { get; init; } = 30000;
        public List<string>? Tags { get; init; }
        public string? Text { get; init; }
        public bool Voice { get; init; }
        public string? Pattern { get; init; }
        public double Intensity { get; init; } = 0.5;
    }

    /// <summary>An inbound payload that passed the receiver-side gate. Phase C resolves + executes it.</summary>
    public sealed class GoonInboundPayloadEventArgs : EventArgs
    {
        public PayloadMsg Payload { get; init; } = null!;
        /// <summary>Local monotonic instant the payload should fire at (already clamped to the schedule buffer).</summary>
        public long FireAtLocalMs { get; init; }
    }

    /// <summary>Freshness of the opponent's state ticks.</summary>
    public enum GoonConnectionHealth { Fresh, Wobbly, Dead }

    /// <summary>Mirror of the opponent, rebuilt from every StateTickMsg. Bindable by the HUD.</summary>
    public sealed class GoonOpponentState
    {
        public string DisplayName { get; set; } = "";
        public string AppVersion { get; set; } = "";
        /// <summary>windows | android | ios | web — from the peer's advertised caps.</summary>
        public string Platform { get; set; } = "";
        public bool ToyConnected { get; set; }
        public GoonAttentionMode AttentionMode { get; set; } = GoonAttentionMode.NoCam;

        public int Score { get; set; }
        public int AttentionPct { get; set; } = 100;
        public int Charges { get; set; }
        public bool ToyActive { get; set; }
        public int? Closeness { get; set; }
        public List<string> ActiveEffects { get; set; } = new();

        public long LastTickLocalMs { get; set; }
        public GoonConnectionHealth Health { get; set; } = GoonConnectionHealth.Fresh;
        public bool HasSeenTick { get; set; }
    }

    /// <summary>
    /// The signed end-of-match record. Each side's own score is authoritative for that
    /// side; the two clients countersign the outcome. Disagreement is recorded as
    /// disputed and the uncontested parts still earn their cosmetics (plan §Phase B).
    /// </summary>
    public sealed class GoonMatchResult
    {
        public GoonEndReason EndReason { get; set; }
        /// <summary>null = draw.</summary>
        public bool? WinnerIsHost { get; set; }
        public bool LocalIsHost { get; set; }
        public int HostScore { get; set; }
        public int GuestScore { get; set; }
        public long SurvivedMs { get; set; }

        /// <summary>True once the opponent countersigned an identical outcome.</summary>
        public bool Agreed { get; set; }
        /// <summary>True when the two sides disagree on end reason or winner (or the peer never signed).</summary>
        public bool Disputed { get; set; }
        /// <summary>The peer's own claim, kept verbatim when disputed so the ledger can store both.</summary>
        public ResultMsg? RemoteClaim { get; set; }
        /// <summary>False for pre-Live cancellations — nothing to write to the ledger.</summary>
        public bool CountsForLedger { get; set; } = true;

        public bool LocalWon => WinnerIsHost.HasValue && WinnerIsHost.Value == LocalIsHost;
        public int LocalScore => LocalIsHost ? HostScore : GuestScore;
        public int RemoteScore => LocalIsHost ? GuestScore : HostScore;
    }

    /// <summary>Everything Phase D's round harness needs. Built by GoonMatchService at handoff.</summary>
    public sealed class GoonSuddenDeathContext
    {
        public IGoonTransport Transport { get; init; } = null!;
        /// <summary>Shared clock; null means "use Transport.Clock".</summary>
        public IMatchClock? Clock { get; init; }
        /// <summary>Seed -> deterministic PRNG (Phase A's GoonRng). Null means the runner picks its own.</summary>
        public Func<ulong, IGoonRng>? RngFactory { get; init; }
        /// <summary>Combined match seed; per-round seeds are re-XOR'd from fresh contributions.</summary>
        public ulong MatchSeed { get; init; }
        public bool IsHost { get; init; }
        public GoonAttentionMode LocalMode { get; init; }
        public GoonAttentionMode RemoteMode { get; init; }
        /// <summary>Net rounds down that ends the match (GoonConsts.SuddenDeathNetLoss).</summary>
        public int NetLossThreshold { get; init; } = GoonConsts.SuddenDeathNetLoss;

        /// <summary>
        /// Round kinds BOTH clients advertised (caps intersection). The ladder must only
        /// schedule kinds from this list; ReactionDuel is always present as the universal
        /// fallback every client must support.
        /// </summary>
        public IReadOnlyList<GoonRoundKind> AllowedRoundKinds { get; init; } =
            new[] { GoonRoundKind.ReactionDuel };
    }

    /// <summary>
    /// Capability advertisement helpers. The Windows client supports everything; mobile
    /// (Expo/RN) and web clients advertise subsets, and every cross-client rule is an
    /// INTERSECTION of the two advertisements (plan / GoonCaps doc).
    /// </summary>
    public static class GoonCapabilities
    {
        /// <summary>Every client must be able to run a reaction duel — the universal fallback round.</summary>
        public const GoonRoundKind UniversalRound = GoonRoundKind.ReactionDuel;

        /// <summary>What this (Windows/WPF) client advertises: the full set.</summary>
        public static GoonCaps Local() => new()
        {
            Platform = "windows",
            SupportedPayloads = Enum.GetValues<GoonPayloadKind>().ToList(),
            SupportedElements = GoonDraft.PoolV1.ToList(),
            SupportedRounds = Enum.GetValues<GoonRoundKind>().ToList(),
            MinProtocolVersion = GoonConsts.ProtocolVersion,
        };

        /// <summary>
        /// Intersection of two advertisements. A peer that advertised nothing (older client,
        /// or a hello without caps) is treated as "everything we support" so v1 peers still work.
        /// </summary>
        public static List<T> Intersect<T>(IEnumerable<T>? mine, IEnumerable<T>? theirs)
        {
            var m = (mine ?? Enumerable.Empty<T>()).Distinct().ToList();
            var t = (theirs ?? Enumerable.Empty<T>()).Distinct().ToList();
            if (t.Count == 0) return m;
            if (m.Count == 0) return t;
            return m.Where(t.Contains).ToList();
        }
    }

    /// <summary>
    /// The narrow seam between the match engine (Phase B) and the sudden-death round
    /// harness (Phase D, GoonSuddenDeathRunner). GoonMatchService owns the transport and
    /// the message pump, so it forwards round traffic through <see cref="HandleMessage"/>
    /// rather than the runner subscribing to the transport itself.
    /// </summary>
    public interface IGoonSuddenDeathRunner
    {
        /// <summary>Local player won a round. Arg = round number.</summary>
        event EventHandler<int>? RoundWon;

        /// <summary>Local player lost a round. Arg = round number.</summary>
        event EventHandler<int>? RoundLost;

        /// <summary>Net-loss threshold hit. Arg: true = the LOCAL player lost the match.</summary>
        event EventHandler<bool>? NetLossReached;

        /// <summary>Begins the escalating round ladder. Must not block the UI thread.</summary>
        Task StartAsync(GoonSuddenDeathContext context, CancellationToken ct);

        /// <summary>Round traffic forwarded by the match engine (RoundScheduleMsg / RoundResultMsg).</summary>
        void HandleMessage(GoonMessage message);

        /// <summary>Tear down immediately — mercy, abandon, or dispose. Must be idempotent.</summary>
        Task StopAsync();
    }
}
