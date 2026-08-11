using System.Text.Json.Serialization;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>The punch-card document (`intake_punchcard.json`; WPF IntakePunchCardState.cs:25-58).</summary>
public sealed class IntakePunchCardDocument
{
    /// <summary>The schema version this build writes.</summary>
    public const int CurrentSchemaVersion = 1;

    public DateTimeOffset? CardStartedUtc { get; set; }

    public int PunchedCount { get; set; }

    public List<DateTimeOffset> PunchedUtc { get; set; } = new();

    public List<IntakePendingDraft> PendingDrafts { get; set; } = new();

    public DateTimeOffset? CompletedUtc { get; set; }

    public DateTimeOffset? PrizeClaimedUtc { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}

/// <summary>A drafted session awaiting its run (half a stamp; IntakePunchCardState.cs:44-52).</summary>
public sealed class IntakePendingDraft
{
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("draftedUtc")] public DateTimeOffset DraftedUtc { get; set; }
}

/// <summary>
/// SP-054: the intake punch card on SP-005 machinery (WPF Services/Progression/IntakePunchCardService.cs,
/// 395 lines — the transition and Sanitize bodies were read for this port). 8 holes, the
/// FIRST HOLE FREE (:139-148); holes 2-8 need a completed intake AND the drafted session run
/// ≥50% or to natural end (:181-210).
///
/// **The degraded-delivery contract, verbatim:** no session engine exists, so
/// <see cref="NotifySessionProgress"/>/<see cref="NotifySessionCompleted"/> stay typed
/// seams no caller invokes — punches pend SILENTLY until the 90-day/8-entry eviction the
/// WPF card already applies (:154-176, :300-324). "Forever" would be unbounded growth — a
/// different behavior, never ported.
///
/// **UiEnabled=false parity (:55-62):** the card UI is HIDDEN in WPF itself (prize TBD)
/// while stamping continues — greenfield parity = silent stamping, NO painted card (the
/// SP-050 table's "NOTHING today" cell, honest).
///
/// The 8th hole sets CompletedUtc; the prize is deliberately NOT granted client-side;
/// <see cref="MarkPrizeClaimed"/> stops a double hand-over (:215-249).
///
/// Persistence: WPF's 30s dirty-flush + atomic .tmp-swap + main→.tmp load ladder
/// (:101-107, :261-290, :327-342) rides the SP-005 store (chained writer, atomic
/// temp+rename+flush, typed Quarantined/NewerSchema outcomes); a punch saves IMMEDIATELY
/// (:212-236 — "a punch is the payoff for an hour of work"). The load REPAIRS are ported
/// as <see cref="Repair"/> (pure, returns whether anything changed so the caller saves and
/// the file HEALS — the WPF remarks at :305-314 on why the fix must reach disk).
/// </summary>
public sealed class IntakePunchCard
{
    /// <summary>Card size (:53).</summary>
    public const int TotalHoles = 8;

    /// <summary>The session-run credit threshold (:56).</summary>
    public const double SessionCreditPercent = 50.0;

    /// <summary>Pending-draft caps (:65, :68) — the eviction that makes "pend forever" honest.</summary>
    public const int MaxPendingDrafts = 8;

    /// <summary>Pending-draft caps (:65, :68) — the eviction that makes "pend forever" honest.</summary>
    public static readonly TimeSpan PendingDraftMaxAge = TimeSpan.FromDays(90);

    private readonly PersistenceStore<IntakePunchCardDocument> _store;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<string> _log;

    public IntakePunchCard(
        PersistenceStore<IntakePunchCardDocument> store,
        Func<DateTimeOffset>? utcNow,
        Action<string> log)
    {
        _store = store;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _log = log;
    }

    private IntakePunchCardDocument State => _store.Current;

    /// <summary>Card complete (:111 — either the stamp or the count says so).</summary>
    public bool IsComplete => State.CompletedUtc.HasValue || State.PunchedCount >= TotalHoles;

    /// <summary>A drafted session is waiting on its run (:115).</summary>
    public bool HasPendingStamp => !IsComplete && State.PendingDrafts.Count > 0;

    /// <summary>Prize waiting on its claim (:119).</summary>
    public bool PrizeAwaitingClaim => IsComplete && !State.PrizeClaimedUtc.HasValue;

    /// <summary>Open a card if there isn't one, and punch the free hole. Idempotent
    /// (:139-148). Called when the intake completes (the first intake opens the card).</summary>
    public void EnsureCardStarted()
    {
        if (State.CardStartedUtc.HasValue) return;
        var now = _utcNow();
        _store.Mutate(d =>
        {
            d.CardStartedUtc = now;
            d.PunchedCount = 1;
            d.PunchedUtc.Add(now);
        });
        _ = _store.Save();
        _log("intake-punch: new card opened, first hole on the house (:139-148)");
    }

    /// <summary>A Graded Intake finished and drafted <paramref name="sessionId"/> — half a
    /// stamp (:154-176). No-op once full / blank id / duplicate id (Ordinal). Evicts the
    /// OLDEST beyond 8 ("the newest draft is the one the user is most likely to run").</summary>
    public void NotifyIntakeCompleted(string? sessionId)
    {
        try
        {
            if (IsComplete || string.IsNullOrWhiteSpace(sessionId)) return;
            if (State.PendingDrafts.Any(d => string.Equals(d.SessionId, sessionId, StringComparison.Ordinal)))
            {
                return;
            }

            var now = _utcNow();
            _store.Mutate(d =>
            {
                d.PendingDrafts.Add(new IntakePendingDraft { SessionId = sessionId!, DraftedUtc = now });
                while (d.PendingDrafts.Count > MaxPendingDrafts)
                {
                    d.PendingDrafts.RemoveAt(0);
                }
            });
            _ = _store.Save();
            _log($"intake-punch: intake completed, stamp pending ({State.PendingDrafts.Count} pending)"); // presence+shape — never the session id
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _log($"intake-punch: NotifyIntakeCompleted threw ({ex.GetType().Name}) — typed, stamp not queued");
        }
    }

    /// <summary>Session progress tick — redeems once the session passes 50% (:181-185).
    /// TYPED SEAM this build: no session engine invokes it (the degraded contract).</summary>
    public void NotifySessionProgress(string? sessionId, double progressPercent)
    {
        if (progressPercent < SessionCreditPercent) return;
        TryRedeem(sessionId, "progress");
    }

    /// <summary>Session reached its natural end — redeems regardless of percent (:189-193).
    /// TYPED SEAM this build: no session engine invokes it (the degraded contract).</summary>
    public void NotifySessionCompleted(string? sessionId) => TryRedeem(sessionId, "completed");

    /// <summary>Punch a hole if <paramref name="sessionId"/> is a pending draft (:196-210).</summary>
    private void TryRedeem(string? sessionId, string via)
    {
        try
        {
            if (IsComplete || string.IsNullOrWhiteSpace(sessionId)) return;

            IntakePendingDraft? draft = null;
            var completedNow = false;
            _store.Mutate(d =>
            {
                draft = d.PendingDrafts.FirstOrDefault(x =>
                    string.Equals(x.SessionId, sessionId, StringComparison.Ordinal));
                if (draft == null) return;

                d.PendingDrafts.Remove(draft);
                // Punch (:212-236): immediate, 8th hole stamps completion.
                var now = _utcNow();
                d.PunchedCount = Math.Min(TotalHoles, d.PunchedCount + 1);
                d.PunchedUtc.Add(now);
                completedNow = d.PunchedCount >= TotalHoles && !d.CompletedUtc.HasValue;
                if (completedNow)
                {
                    d.CompletedUtc = now;
                }
            });

            if (draft == null) return;

            // A punch saves IMMEDIATELY (:230-234) — never left to the dirty flush.
            _ = _store.Save();
            _log($"intake-punch: hole {State.PunchedCount}/{TotalHoles} punched (via {via})");
            Changed?.Invoke();

            if (completedNow)
            {
                _log("intake-punch: card COMPLETE — prize awaiting claim (never granted client-side, :215-249)");
                try { PunchCardCompleted?.Invoke(); }
                catch (Exception ex) { _log($"intake-punch: completion handler threw ({ex.GetType().Name}) — isolated"); }
            }
        }
        catch (Exception ex)
        {
            _log($"intake-punch: TryRedeem threw ({ex.GetType().Name}) — typed, draft stands");
        }
    }

    /// <summary>Record the prize hand-over exactly once (:241-249). What the prize IS
    /// remains TBD — this only stops it being handed over twice.</summary>
    public void MarkPrizeClaimed()
    {
        if (!IsComplete || State.PrizeClaimedUtc.HasValue) return;
        var now = _utcNow();
        _store.Mutate(d => d.PrizeClaimedUtc = now);
        _ = _store.Save();
        _log("intake-punch: prize claimed (once)");
        Changed?.Invoke();
    }

    /// <summary>Card/punch changes (WPF RaiseChanged).</summary>
    public event Action? Changed;

    /// <summary>Card completion (WPF PunchCardCompleted — fires once, on the 8th hole).</summary>
    public event Action? PunchCardCompleted;

    /// <summary>
    /// The load repairs (Sanitize :300-324), pure: fix anything a hand-edited or drifted
    /// file could assert; returns true when anything changed (the caller saves so the file
    /// HEALS — the WPF :305-314 remarks). Run once after the store's phase-3 load; SP-005's
    /// typed Quarantined/NewerSchema outcomes replace WPF's .tmp load ladder (:261-290).
    /// </summary>
    public static bool Repair(IntakePunchCardDocument s, DateTimeOffset utcNow)
    {
        var dirty = false;

        // Null lists (a literal JSON null round-trips onto the non-nullable members).
        if (s.PunchedUtc is null) { s.PunchedUtc = new List<DateTimeOffset>(); dirty = true; }
        if (s.PendingDrafts is null) { s.PendingDrafts = new List<IntakePendingDraft>(); dirty = true; }

        var clamped = Math.Clamp(s.PunchedCount, 0, TotalHoles);
        if (clamped != s.PunchedCount) { s.PunchedCount = clamped; dirty = true; }

        // A full card must have a completion stamp, or PrizeAwaitingClaim never resolves.
        if (s.PunchedCount >= TotalHoles && !s.CompletedUtc.HasValue)
        {
            s.CompletedUtc = s.PunchedUtc.Count > 0 ? s.PunchedUtc[^1] : utcNow;
            dirty = true;
        }

        // Drop drafts whose session the user has almost certainly deleted, and blank ids.
        var cutoff = utcNow - PendingDraftMaxAge;
        if (s.PendingDrafts.RemoveAll(d => string.IsNullOrWhiteSpace(d.SessionId) || d.DraftedUtc < cutoff) > 0)
        {
            dirty = true;
        }

        while (s.PendingDrafts.Count > MaxPendingDrafts) { s.PendingDrafts.RemoveAt(0); dirty = true; }

        return dirty;
    }
}
