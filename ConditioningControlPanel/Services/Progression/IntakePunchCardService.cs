using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The eight-hole intake punch card - two rows of four.
///
/// <para><b>How a hole is earned.</b> The first is free, punched the moment the card is opened:
/// a card that starts empty reads as a chore, and a card that starts with something on it reads
/// as progress worth finishing. Every hole after that takes the same two-part work:</para>
/// <list type="number">
///   <item>complete a Graded Intake (which drafts a session), then</item>
///   <item>actually run that session.</item>
/// </list>
/// <para>Two parts rather than one on purpose. The point of the intake is that it produces a
/// session worth using; stamping on the quiz alone would reward the quiz and ignore the thing
/// the quiz exists to make. It also gives the half-punched hole something to say - "intake done,
/// now run your session" - which is a second re-engagement hook per week for free.</para>
///
/// <para><b>Pacing.</b> Nothing here is time-gated. For free accounts the limiter is
/// <see cref="IntakePassService"/>'s one run a week, so a full card is roughly seven weeks.
/// Patrons are limited only by the work itself: eight intakes and eight sessions. That is the
/// intended shape of "or pay and do it sooner" - subscribing buys access, never a skip.</para>
///
/// <para><b>What counts as running the session.</b> <see cref="SessionCreditPercent"/> of its
/// length, or a natural completion, whichever lands first. A drafted session is an hour long, so
/// demanding an uninterrupted sixty minutes for the second hole would put the steepest ask in the
/// whole funnel directly in front of a brand-new user. Half is enough to prove the session was
/// used rather than started and abandoned. The threshold is one constant - move it if play-testing
/// disagrees. Note the elapsed figure is Stopwatch-backed inside SessionEngine (it distrusts the
/// wall clock past 30s of divergence), so this cannot be farmed by moving the system clock.</para>
///
/// <para><b>The prize is deliberately not granted here.</b> The card raises
/// <see cref="PunchCardCompleted"/> and records <see cref="IntakePunchCardState.PrizeClaimedUtc"/>;
/// what the prize IS remains a product decision. Keep it that way if the reward ever becomes
/// something paid - this state is local, a rolled-back clock buys extra free intakes, and a
/// client-authoritative card must never be the only thing standing between a user and premium
/// access. Anything with real value needs the completion validated server-side first.</para>
/// </summary>
public class IntakePunchCardService : IDisposable
{
    /// <summary>Two rows of four.</summary>
    public const int TotalHoles = 8;

    /// <summary>How much of a drafted session must be run for its stamp. See the class remarks -
    /// this is the tuning knob, not a magic number.</summary>
    public const double SessionCreditPercent = 50.0;

    /// <summary>The card is hidden everywhere in the UI while the prize at eight holes is still
    /// TBD - it comes back once there is something real to hand over. Gates the Quests-tab
    /// panel, the weekly-nudge pitch line and the intake-pass help tip; the service itself keeps
    /// stamping silently so nobody loses progress in the meantime. readonly rather than const so
    /// the gated paint paths do not trip unreachable-code warnings.</summary>
    public static readonly bool UiEnabled = false;

    /// <summary>Ceiling on un-redeemed drafts. Can never legitimately exceed the holes left, and
    /// bounding it stops a pathological file from growing without limit.</summary>
    private const int MaxPendingDrafts = TotalHoles;

    /// <summary>Drafts older than this are swept on load. Covers the case where the user deleted
    /// the session file, which would otherwise leave a stamp permanently un-redeemable.</summary>
    private static readonly TimeSpan PendingDraftMaxAge = TimeSpan.FromDays(90);

    private readonly string _statePath;
    private readonly DispatcherTimer _saveTimer;
    private bool _isDirty;

    public IntakePunchCardState State { get; private set; }

    /// <summary>Any change worth repainting for - a punch, a new pending stamp, a fresh card.</summary>
    public event EventHandler? PunchCardChanged;

    /// <summary>The eighth hole just landed. Fired once, at the moment of completion, not on
    /// every subsequent load of an already-full card.</summary>
    public event EventHandler? PunchCardCompleted;

    /// <param name="statePathOverride">Test seam. Production passes nothing and gets the real
    /// per-user file; tests pass a temp path so a test run can never touch - or wipe - a real
    /// user's card. Supplying it also skips the autosave timer, which needs a Dispatcher that a
    /// headless test host does not have.</param>
    public IntakePunchCardService(string? statePathOverride = null)
    {
        _statePath = statePathOverride ?? Path.Combine(
            App.UserDataPath,
            "intake_punchcard.json");

        State = LoadState();
        EnsureCardStarted();

        // Same 30s dirty-flush cadence as QuestService: serialize on the UI thread, write off it.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (_, _) =>
        {
            if (!_isDirty) return;
            _isDirty = false;
            SaveAsync();
        };
        if (statePathOverride == null) _saveTimer.Start();
    }

    // ============================ read-only view ============================

    public int PunchedCount => Math.Clamp(State.PunchedCount, 0, TotalHoles);
    public int HolesRemaining => Math.Max(0, TotalHoles - PunchedCount);
    public bool IsComplete => State.CompletedUtc.HasValue || PunchedCount >= TotalHoles;

    /// <summary>An intake is done and its session is waiting to be run - the next hole is
    /// half-stamped.</summary>
    public bool HasPendingStamp => !IsComplete && State.PendingDrafts.Count > 0;

    /// <summary>The card is full but the prize has not been handed over yet. The UI uses this to
    /// show a claim affordance; what claiming actually grants is still TBD.</summary>
    public bool PrizeAwaitingClaim => IsComplete && !State.PrizeClaimedUtc.HasValue;

    /// <summary>Has this user ever finished a Graded Intake? The first hole is free, so anything
    /// beyond one punch - or a stamp still pending - means a real run happened. Used to give the
    /// very first run a gentler launch (the intake normally minimizes the control panel, which on
    /// a first-ever run reads as the app crashing).</summary>
    public bool HasEverCompletedIntake => State.PunchedCount > 1 || State.PendingDrafts.Count > 0;

    /// <summary>State of a single hole, for painting. Index is 0-based.</summary>
    public PunchHoleView HoleAt(int index)
    {
        if (index < 0 || index >= TotalHoles) return PunchHoleView.Empty;
        if (index < PunchedCount) return PunchHoleView.Punched;
        if (index == PunchedCount && HasPendingStamp) return PunchHoleView.Pending;
        return PunchHoleView.Empty;
    }

    // ============================ transitions ============================

    /// <summary>Open a card if there isn't one, and punch the free hole. Idempotent.</summary>
    private void EnsureCardStarted()
    {
        if (State.CardStartedUtc.HasValue) return;
        var now = DateTime.UtcNow;
        State.CardStartedUtc = now;
        State.PunchedCount = 1;
        State.PunchedUtc.Add(now);
        _isDirty = true;
        App.Logger?.Information("IntakePunchCard: new card opened, first hole on the house");
    }

    /// <summary>
    /// A Graded Intake finished and drafted <paramref name="sessionId"/>. That is half a stamp -
    /// the hole lands when the session is actually run. No-op once the card is full.
    /// </summary>
    public void NotifyIntakeCompleted(string? sessionId)
    {
        try
        {
            if (IsComplete || string.IsNullOrWhiteSpace(sessionId)) return;

            // Re-drafting the same session id twice must not queue two stamps.
            if (State.PendingDrafts.Any(d => string.Equals(d.SessionId, sessionId, StringComparison.Ordinal)))
                return;

            State.PendingDrafts.Add(new PendingIntakeDraft
            {
                SessionId = sessionId!,
                DraftedUtc = DateTime.UtcNow,
            });

            // Oldest out first - the newest draft is the one the user is most likely to run.
            while (State.PendingDrafts.Count > MaxPendingDrafts)
                State.PendingDrafts.RemoveAt(0);

            _isDirty = true;
            App.Logger?.Information("IntakePunchCard: intake completed, stamp pending on session {Id}", sessionId);
            RaiseChanged();
        }
        catch (Exception ex) { App.Logger?.Warning("IntakePunchCard.NotifyIntakeCompleted: {E}", ex.Message); }
    }

    /// <summary>Session progress tick. Redeems the pending stamp once the session passes
    /// <see cref="SessionCreditPercent"/>. Safe to call every tick - redemption removes the draft,
    /// so it can only fire once per intake.</summary>
    public void NotifySessionProgress(Session? session, double progressPercent)
    {
        if (session == null || progressPercent < SessionCreditPercent) return;
        TryRedeem(session.Id, "progress");
    }

    /// <summary>Session reached its natural end. Redeems regardless of the percentage threshold -
    /// a session that ran to completion has unambiguously been used.</summary>
    public void NotifySessionCompleted(Session? session)
    {
        if (session == null) return;
        TryRedeem(session.Id, "completed");
    }

    /// <summary>Punch a hole if <paramref name="sessionId"/> is one of the drafts we are waiting on.</summary>
    private void TryRedeem(string? sessionId, string via)
    {
        try
        {
            if (IsComplete || string.IsNullOrWhiteSpace(sessionId)) return;

            var draft = State.PendingDrafts
                .FirstOrDefault(d => string.Equals(d.SessionId, sessionId, StringComparison.Ordinal));
            if (draft == null) return;

            State.PendingDrafts.Remove(draft);
            Punch(via);
        }
        catch (Exception ex) { App.Logger?.Warning("IntakePunchCard.TryRedeem: {E}", ex.Message); }
    }

    private void Punch(string via)
    {
        var now = DateTime.UtcNow;
        State.PunchedCount = Math.Min(TotalHoles, State.PunchedCount + 1);
        State.PunchedUtc.Add(now);
        _isDirty = true;

        var completedNow = State.PunchedCount >= TotalHoles && !State.CompletedUtc.HasValue;
        if (completedNow) State.CompletedUtc = now;

        App.Logger?.Information("IntakePunchCard: hole {N}/{Total} punched (via {Via})",
            State.PunchedCount, TotalHoles, via);

        // Persist immediately rather than waiting on the dirty timer. A punch is the payoff for
        // an hour of work; losing one to a crash in the next 30 seconds is not a trade worth taking.
        Save();
        RaiseChanged();

        if (completedNow)
        {
            App.Logger?.Information("IntakePunchCard: card COMPLETE - prize awaiting claim");
            try { PunchCardCompleted?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Warning("IntakePunchCard: completion handler threw: {E}", ex.Message); }
        }
    }

    /// <summary>Record that the prize has been handed over. What that prize is remains TBD -
    /// this only stops it being handed over twice.</summary>
    public void MarkPrizeClaimed()
    {
        if (!IsComplete || State.PrizeClaimedUtc.HasValue) return;
        State.PrizeClaimedUtc = DateTime.UtcNow;
        Save();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try { PunchCardChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { App.Logger?.Debug("IntakePunchCard.RaiseChanged: {E}", ex.Message); }
    }

    // ============================ persistence ============================

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Load, preferring the main file and falling back to a half-written .tmp - the same
    /// recovery ladder QuestService uses. A card represents weeks of the user's time, so the
    /// fresh-default path is the last resort, not the first.</summary>
    private IntakePunchCardState LoadState()
    {
        var tmpPath = _statePath + ".tmp";

        if (File.Exists(_statePath))
        {
            try
            {
                var json = File.ReadAllText(_statePath);
                var loaded = JsonSerializer.Deserialize<IntakePunchCardState>(json);
                if (loaded != null) return Sanitize(loaded);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "IntakePunchCard: state file corrupted, attempting .tmp recovery");
            }
        }

        if (File.Exists(tmpPath))
        {
            try
            {
                var json = File.ReadAllText(tmpPath);
                var recovered = JsonSerializer.Deserialize<IntakePunchCardState>(json);
                if (recovered != null)
                {
                    App.Logger?.Warning("IntakePunchCard: recovered state from .tmp");
                    try { File.Move(tmpPath, _statePath, overwrite: true); } catch { }
                    return Sanitize(recovered);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "IntakePunchCard: .tmp recovery failed");
            }
        }

        return new IntakePunchCardState();
    }

    /// <summary>Repair anything a hand-edited or drifted file could assert that the rest of the
    /// service assumes is impossible.</summary>
    /// <remarks>Instance, not static, so a repair can set <see cref="_isDirty"/>. When this was
    /// static the fixes below lived only in memory: a corrupt or hand-edited card was silently
    /// re-repaired on every single launch and the file on disk never healed. Harmless while
    /// everything is recomputed per load, but the moment anything trusts <c>CompletedUtc</c> from a
    /// file another process wrote, a card that reads as complete in RAM and incomplete on disk is a
    /// prize that pays twice or never.</remarks>
    private IntakePunchCardState Sanitize(IntakePunchCardState s)
    {
        if (s.PunchedUtc == null) { s.PunchedUtc = new(); _isDirty = true; }
        if (s.PendingDrafts == null) { s.PendingDrafts = new(); _isDirty = true; }

        var clamped = Math.Clamp(s.PunchedCount, 0, TotalHoles);
        if (clamped != s.PunchedCount) { s.PunchedCount = clamped; _isDirty = true; }

        // A full card must have a completion stamp, or PrizeAwaitingClaim never resolves.
        if (s.PunchedCount >= TotalHoles && !s.CompletedUtc.HasValue)
        {
            s.CompletedUtc = s.PunchedUtc.Count > 0 ? s.PunchedUtc[^1] : DateTime.UtcNow;
            _isDirty = true;
        }

        // Drop drafts whose session the user has almost certainly deleted, and any blank ids.
        var cutoff = DateTime.UtcNow - PendingDraftMaxAge;
        if (s.PendingDrafts.RemoveAll(d => string.IsNullOrWhiteSpace(d.SessionId) || d.DraftedUtc < cutoff) > 0)
            _isDirty = true;
        while (s.PendingDrafts.Count > MaxPendingDrafts) { s.PendingDrafts.RemoveAt(0); _isDirty = true; }

        return s;
    }

    /// <summary>Synchronous atomic write.</summary>
    public void Save()
    {
        _isDirty = false;
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(State, WriteOptions);
            var tmpPath = _statePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _statePath, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "IntakePunchCard: failed to save state");
        }
    }

    /// <summary>Serialize on the calling (UI) thread, write off it - QuestService's pattern.</summary>
    private void SaveAsync()
    {
        string json;
        try { json = JsonSerializer.Serialize(State, WriteOptions); }
        catch (Exception ex) { App.Logger?.Error(ex, "IntakePunchCard: serialize failed"); return; }

        var path = _statePath;
        var tmpPath = path + ".tmp";
        _ = Task.Run(() =>
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "IntakePunchCard: failed to save state");
            }
        });
    }

    public void Dispose()
    {
        try { _saveTimer.Stop(); } catch { }
        if (_isDirty) Save();
    }
}

/// <summary>How a single hole should be drawn.</summary>
public enum PunchHoleView
{
    Empty,
    /// <summary>Intake done, its session not run yet - a half stamp.</summary>
    Pending,
    Punched,
}
