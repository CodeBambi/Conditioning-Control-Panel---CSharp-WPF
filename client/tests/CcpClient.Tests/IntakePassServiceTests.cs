using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-054: the Weekly Intake Pass state machine (IntakePassService.cs port): ISO-week
/// arithmetic incl. the New-Year guard, the rollback guard, fail-closed, completion-spend
/// ONLY, the typed AvailableNoEntitlementProvider reason (consult ruling 5), and the
/// dual-provider refund against a fake seam. NeedsLogin is unreachable this build —
/// exercised HERE via the fake seam, never silently collapsed.
/// </summary>
public sealed class IntakePassServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-sp054-pass-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];
    private DateTimeOffset _now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero); // Tuesday, 2026-W33

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private sealed class FakeEntitlement : IntakePassService.IIntakeEntitlementSource
    {
        public bool Premium { get; set; }
        public bool? LoggedIn { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool IsPremium => ThrowOnRead ? throw new InvalidOperationException("provider fault") : Premium;
        public bool? IsLoggedIn => ThrowOnRead ? throw new InvalidOperationException("provider fault") : LoggedIn;
        public event Action? TierChanged;
        public void RaiseTierChanged() => TierChanged?.Invoke();
    }

    private sealed class SinkAdapter : ILogSink
    {
        private readonly List<string> _lines;
        public SinkAdapter(List<string> lines) => _lines = lines;
        public void Log(string message) => _lines.Add(message);
    }

    private PersistenceStore<IntakeSettingsDocument> NewStore()
    {
        var store = new PersistenceStore<IntakeSettingsDocument>(
            new OperationRegistry().OwnerFor("IntakeSettings"),
            new SinkAdapter(_log),
            Path.Combine(_root, Guid.NewGuid().ToString("N"), "intake_settings.json"),
            IntakeSettingsDocument.CurrentSchemaVersion);
        store.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return store;
    }

    // ---------- ISO-week arithmetic ----------

    [Theory]
    [InlineData(2021, 1, 1, "2020-W53")]  // the New-Year guard: Jan 1 keys to the PRIOR ISO year
    [InlineData(2021, 1, 4, "2021-W01")]
    [InlineData(2026, 8, 11, "2026-W33")]
    [InlineData(2026, 8, 10, "2026-W33")] // Monday of the same ISO week — same key
    [InlineData(2026, 8, 16, "2026-W33")] // Sunday — still the same key (reset is Monday 00:00)
    [InlineData(2026, 8, 17, "2026-W34")] // Monday — the new pass
    public void Week_Key_ISO_Arithmetic(int y, int m, int d, string expected) =>
        Assert.Equal(expected, IntakePassService.WeekKey(new DateTime(y, m, d)));

    [Fact]
    public void StartOfWeek_NextPass_DaysUntil()
    {
        var tuesday = new DateTime(2026, 8, 11, 15, 30, 0);
        Assert.Equal(new DateTime(2026, 8, 10), IntakePassService.StartOfWeek(tuesday));
        Assert.Equal(new DateTime(2026, 8, 10), IntakePassService.StartOfWeek(new DateTime(2026, 8, 16, 23, 59, 59)));
        Assert.Equal(new DateTime(2026, 8, 17), IntakePassService.NextPassLocal(tuesday));
        Assert.InRange(IntakePassService.DaysUntilNextPass(tuesday), 1, 7);
        Assert.Equal(1, IntakePassService.DaysUntilNextPass(new DateTime(2026, 8, 16, 23, 59, 59)));
    }

    // ---------- state evaluation matrix ----------

    [Fact]
    public void Default_Seam_Is_Available_With_The_Typed_Reason()
    {
        var pass = new IntakePassService(NewStore(), null, () => _now, _log.Add);
        var (state, reason) = pass.Evaluate();
        Assert.Equal(IntakePassService.IntakePassState.Available, state);
        Assert.Equal(IntakePassService.IntakePassReason.AvailableNoEntitlementProvider, reason);
        Assert.True(pass.CanStartIntake());
    }

    [Fact]
    public void Premium_Never_Spends()
    {
        var seam = new FakeEntitlement { Premium = true, LoggedIn = true };
        var store = NewStore();
        var pass = new IntakePassService(store, seam, () => _now, _log.Add);
        Assert.Equal(IntakePassService.IntakePassState.Premium, pass.Evaluate().State);
        pass.ConsumeForCompletedIntake();
        Assert.Equal(string.Empty, store.Current.PassSpentWeek);
        Assert.Null(store.Current.PassSpentUtc);
    }

    [Fact]
    public void Logged_Out_Seam_Is_NeedsLogin_And_Cannot_Start()
    {
        var seam = new FakeEntitlement { Premium = false, LoggedIn = false };
        var pass = new IntakePassService(NewStore(), seam, () => _now, _log.Add);
        var (state, reason) = pass.Evaluate();
        Assert.Equal(IntakePassService.IntakePassState.NeedsLogin, state);
        Assert.Equal(IntakePassService.IntakePassReason.LoginRequired, reason);
        Assert.False(pass.CanStartIntake());
    }

    [Fact]
    public void Spent_This_Week_Is_Spent()
    {
        var store = NewStore();
        store.Mutate(d => { d.PassSpentWeek = "2026-W33"; d.PassSpentUtc = _now.AddHours(-2); });
        var pass = new IntakePassService(store, null, () => _now, _log.Add);
        Assert.Equal(IntakePassService.IntakePassReason.SpentThisWeek, pass.Evaluate().Reason);
        Assert.False(pass.CanStartIntake());
        // A DIFFERENT week key is stale — the new pass is open (string authority, never a delta).
        store.Mutate(d => d.PassSpentWeek = "2026-W32");
        Assert.Equal(IntakePassService.IntakePassState.Available, pass.Evaluate().State);
    }

    [Fact]
    public void Future_Stamped_Spend_Beyond_5_Minutes_Is_Rollback_Spent()
    {
        var store = NewStore();
        store.Mutate(d => { d.PassSpentWeek = "2020-W01"; d.PassSpentUtc = _now.AddMinutes(10); });
        var pass = new IntakePassService(store, null, () => _now, _log.Add);
        Assert.Equal(IntakePassService.IntakePassReason.SpentClockRollback, pass.Evaluate().Reason);
        // Inside the tolerance the stamp stands as evidence, not a rollback.
        store.Mutate(d => d.PassSpentUtc = _now.AddMinutes(4));
        Assert.Equal(IntakePassService.IntakePassState.Available, pass.Evaluate().State);
    }

    [Fact]
    public void Any_Exception_Fails_CLOSED_To_Spent()
    {
        var seam = new FakeEntitlement { ThrowOnRead = true };
        var pass = new IntakePassService(NewStore(), seam, () => _now, _log.Add);
        var (state, reason) = pass.Evaluate();
        Assert.Equal(IntakePassService.IntakePassState.Spent, state);
        Assert.Equal(IntakePassService.IntakePassReason.SpentFailClosed, reason);
        Assert.False(pass.CanStartIntake());
        Assert.Contains(_log, l => l.Contains("fail-CLOSED"));
    }

    // ---------- completion-spend + persistence ----------

    [Fact]
    public async Task Completion_Spend_Writes_The_Week_And_Persists()
    {
        var path = Path.Combine(_root, "spend", "intake_settings.json");
        var registry = new OperationRegistry();
        var store = new PersistenceStore<IntakeSettingsDocument>(
            registry.OwnerFor("IntakeSettings"), new SinkAdapter(_log), path, IntakeSettingsDocument.CurrentSchemaVersion);
        await store.StartAsync(TestContext.Current.CancellationToken);
        var pass = new IntakePassService(store, null, () => _now, _log.Add);
        var changed = 0;
        pass.PassStateChanged += () => changed++;

        pass.ConsumeForCompletedIntake();
        await store.SaveImmediate();

        Assert.Equal("2026-W33", store.Current.PassSpentWeek);
        Assert.Equal(_now, store.Current.PassSpentUtc);
        Assert.Equal(1, changed);
        var onDisk = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("2026-W33", onDisk);

        // The spend evaluates Spent for the rest of the ISO week.
        Assert.Equal(IntakePassService.IntakePassState.Spent, pass.Evaluate().State);
        // Next Monday the SAME stamp is stale — Available again (Monday 00:00 local reset).
        _now = _now.AddDays(6); // 2026-08-17, W34
        Assert.Equal(IntakePassService.IntakePassState.Available, pass.Evaluate().State);
    }

    // ---------- the dual-provider refund ----------

    [Fact]
    public async Task Late_Premium_Refunds_A_This_Session_Spend()
    {
        var path = Path.Combine(_root, "refund", "intake_settings.json");
        var store = new PersistenceStore<IntakeSettingsDocument>(
            new OperationRegistry().OwnerFor("IntakeSettings"), new SinkAdapter(_log), path, IntakeSettingsDocument.CurrentSchemaVersion);
        await store.StartAsync(TestContext.Current.CancellationToken);
        var seam = new FakeEntitlement { Premium = false, LoggedIn = true };
        var pass = new IntakePassService(store, seam, () => _now, _log.Add);

        pass.ConsumeForCompletedIntake();
        Assert.Equal("2026-W33", store.Current.PassSpentWeek);

        seam.Premium = true;
        seam.RaiseTierChanged();
        await store.SaveImmediate();
        Assert.Equal(string.Empty, store.Current.PassSpentWeek);
        Assert.Null(store.Current.PassSpentUtc);
        Assert.DoesNotContain("\"passSpentWeek\": \"2026-W33\"", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Prior_Session_Stamps_Are_Never_Refunded()
    {
        var store = NewStore();
        store.Mutate(d => { d.PassSpentWeek = "2026-W33"; d.PassSpentUtc = _now.AddHours(-3); });
        var seam = new FakeEntitlement { Premium = false, LoggedIn = true };
        // A FRESH service: _spentThisSession is false (the stamp predates this process).
        var pass = new IntakePassService(store, seam, () => _now, _log.Add);
        seam.Premium = true;
        seam.RaiseTierChanged();
        Assert.Equal("2026-W33", store.Current.PassSpentWeek);
        Assert.NotNull(store.Current.PassSpentUtc);
    }

    [Fact]
    public void Refund_Requires_The_Current_Week()
    {
        var store = NewStore();
        var seam = new FakeEntitlement { Premium = false, LoggedIn = true };
        var pass = new IntakePassService(store, seam, () => _now, _log.Add);
        pass.ConsumeForCompletedIntake();
        // The week rolls over BEFORE the tier resolves — no refund (the spend belongs to W33).
        _now = _now.AddDays(6);
        seam.Premium = true;
        seam.RaiseTierChanged();
        Assert.Equal("2026-W33", store.Current.PassSpentWeek);
    }
}
