using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Erasure has to be total, and the consent gate has to be the only door.
///
/// <para><b>Why the '.tmp' sibling has its own assertions.</b> Yesterday's Goon Game review found
/// '.part' temp files that no code path ever deleted — a purge that misses a partial file is a purge
/// that failed, and the ledger's atomic write leaves exactly that shape behind if the process dies
/// mid-save. The privacy panel's button says "forget everything", so every artifact the feature
/// creates is enumerated in <see cref="AwarenessLive.WipeEverything"/>'s doc comment and each one is
/// covered here: the JSON, the '.tmp', the in-memory counters and ring, the published frame and the
/// memory's habits/recent lines.</para>
/// </summary>
[Collection(AwarenessStaticsCollection.Name)]
public class AwarenessErasureTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public AwarenessErasureTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-aware-erase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "awareness_ledger.json");

        AwarenessLive.Ledger = null;
        AwarenessLive.Memory = null;
        AwarenessLive.Clear();
        AwarenessPause.Resume();
    }

    public void Dispose()
    {
        AwarenessLive.Ledger = null;
        AwarenessLive.Memory = null;
        AwarenessLive.Clear();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private ActivityLedger StartedLedger(DateTime now)
    {
        var ledger = new ActivityLedger(_path, () => now, () => 30);
        ledger.Start();
        return ledger;
    }

    private sealed class FakeMemory : ICompanionMemory
    {
        public readonly List<string?> Forgotten = new();
        private readonly List<ReactionSummary> _lines = new();

        public Task<IReadOnlyList<HabitRecord>> GetHabitsAsync(string appId, string? cluster)
            => Task.FromResult<IReadOnlyList<HabitRecord>>(Array.Empty<HabitRecord>());

        public Task<IReadOnlyList<ReactionSummary>> GetRecentReactionsAsync(int count)
            => Task.FromResult<IReadOnlyList<ReactionSummary>>(_lines.ToList());

        public Task RecordReactionAsync(ReactionSummary line)
        {
            _lines.Add(line);
            return Task.CompletedTask;
        }

        public Task ForgetAsync(string? appId)
        {
            Forgotten.Add(appId);
            if (appId == null) _lines.Clear();
            else _lines.RemoveAll(l => string.Equals(l.AppId, appId, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }
    }

    private static ContextFrame Frame(string appId, DateTime at) => new()
    {
        AppId = appId,
        ServiceName = appId,
        Category = ActivityCategory.Media,
        CutAt = at
    };

    // ===================== the wipe =====================

    [Fact]
    public void Wipe_RemovesTheLedgerFileAndItsInterruptedWriteSibling()
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Local);
        using var ledger = StartedLedger(now);

        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, now);
        ledger.Heartbeat(now.AddMinutes(5));
        ledger.SaveNow();
        Assert.True(File.Exists(_path));

        // The shape an interrupted atomic write leaves behind — a full copy of the data that no other
        // code path deletes.
        File.WriteAllText(ledger.LedgerTempPath, File.ReadAllText(_path));
        Assert.True(File.Exists(ledger.LedgerTempPath));

        AwarenessLive.Ledger = ledger;
        AwarenessLive.WipeEverything();

        Assert.False(File.Exists(_path));
        Assert.False(File.Exists(ledger.LedgerTempPath));
    }

    [Fact]
    public void Wipe_EmptiesTheInMemoryCountersAndTheSessionRing()
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Local);
        using var ledger = StartedLedger(now);

        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, now);
        ledger.NoteFocus("discord", null, ActivityCategory.Social, now.AddMinutes(3));
        Assert.True(ledger.AppCount > 0);
        Assert.NotEmpty(ledger.RecentTransitions);

        AwarenessLive.Ledger = ledger;
        AwarenessLive.WipeEverything();

        Assert.Equal(0, ledger.AppCount);
        Assert.Empty(ledger.RecentTransitions);
        Assert.Equal(0, ledger.Snapshot("youtube", now.AddMinutes(4)).VisitsToday);
    }

    [Fact]
    public void Wipe_SurvivesAWriteThatWasStillPending()
    {
        // The debounced save is cancelled by the wipe; if it were not, a queued write would put the
        // file back moments after the user was told it was gone.
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Local);
        using var ledger = StartedLedger(now);

        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, now);
        ledger.RequestSave();

        AwarenessLive.Ledger = ledger;
        AwarenessLive.WipeEverything();

        System.Threading.Thread.Sleep(1200);   // longer than the debounce
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Wipe_DropsThePublishedFrameSoTheWireViewCannotKeepShowingIt()
    {
        var now = DateTime.Now;
        AwarenessLive.Publish(Frame("youtube", now));
        Assert.NotNull(AwarenessLive.LastFrame);

        AwarenessLive.WipeEverything();
        Assert.Null(AwarenessLive.LastFrame);
        Assert.Null(AwarenessLive.LastFrameAt);
    }

    [Fact]
    public void Wipe_ClearsTheRecentLineBanListToo()
    {
        var memory = new FakeMemory();
        AwarenessLive.Memory = memory;

        AwarenessLive.WipeEverything();

        Assert.Contains(null, memory.Forgotten);   // null app id == "all of it"
    }

    [Fact]
    public void Wipe_WithNoLedgerConstructed_StillRunsAndStillClearsWhatItCan()
    {
        // "Nothing is loaded" is not the same as "nothing is there": awareness may never have run this
        // session while last week's file is still on disk, so the no-ledger branch deletes the two
        // paths directly (ActivityLedger.DefaultLedgerPath and its '.tmp' sibling). It is exercised
        // here for its never-throw contract; the deletion itself is asserted above against a ledger
        // pointed at a temp file, because this branch resolves a real user-data path.
        var memory = new FakeMemory();
        AwarenessLive.Ledger = null;
        AwarenessLive.Memory = memory;
        AwarenessLive.Publish(Frame("youtube", DateTime.Now));

        AwarenessLive.WipeEverything();

        Assert.Null(AwarenessLive.LastFrame);
        Assert.Contains(null, memory.Forgotten);
    }

    // ===================== per-app forget =====================

    [Fact]
    public void Forget_TakesOneAppOutOfTheLedgerTheRingAndTheMemory()
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Local);
        using var ledger = StartedLedger(now);
        var memory = new FakeMemory();

        // The ring records the app being LEFT, so three focuses are what puts both of them in it.
        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, now);
        ledger.NoteFocus("discord", null, ActivityCategory.Social, now.AddMinutes(2));
        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, now.AddMinutes(4));
        Assert.Contains(ledger.RecentTransitions, t => t.AppId == "discord");

        AwarenessLive.Ledger = ledger;
        AwarenessLive.Memory = memory;
        AwarenessLive.Forget("youtube");

        Assert.DoesNotContain(ledger.RecentTransitions, t => t.AppId == "youtube");
        Assert.Contains(ledger.RecentTransitions, t => t.AppId == "discord");
        Assert.Equal(0, ledger.Snapshot("youtube", now.AddMinutes(3)).VisitsToday);
        Assert.Contains("youtube", memory.Forgotten);
    }

    [Fact]
    public void Forget_ClearsTheWireViewOnlyWhenItWasShowingThatApp()
    {
        AwarenessLive.Publish(Frame("youtube", DateTime.Now));
        AwarenessLive.Forget("discord");
        Assert.NotNull(AwarenessLive.LastFrame);

        AwarenessLive.Forget("youtube");
        Assert.Null(AwarenessLive.LastFrame);
    }

    [Fact]
    public void Forget_IgnoresABlankAppId()
    {
        AwarenessLive.Publish(Frame("youtube", DateTime.Now));
        AwarenessLive.Forget(null);
        AwarenessLive.Forget("   ");
        Assert.NotNull(AwarenessLive.LastFrame);
    }

    // ===================== retention =====================

    [Fact]
    public void ShorteningRetention_AgesOutTheDaysThatFellOutsideIt()
    {
        var day1 = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Local);
        int retention = 30;
        var clock = day1;

        using var ledger = new ActivityLedger(_path, () => clock, () => retention);
        ledger.Start();
        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, day1);
        ledger.Heartbeat(day1.AddMinutes(10));

        // Ten days later the visit is still inside a 30-day window…
        clock = day1.AddDays(10);
        ledger.PruneRetention(clock);
        Assert.True(ledger.AppCount > 0);

        // …and outside a 7-day one, which is what the panel's second stop means.
        retention = 7;
        ledger.PruneRetention(clock);
        Assert.Equal(0, ledger.Snapshot("youtube", clock).MinutesThisWeek);
    }

    // ===================== the panel's own contract =====================

    [Fact]
    public void ThePanelExposesAWipeCommandAndAPauseCommand()
    {
        // The card is the answer to "what do you have on me?", so the undo has to be ON it.
        IAwarenessPrivacyVm vm = MockAwarenessPrivacyVm.Live();

        Assert.NotNull(vm.WipeCommand);
        Assert.NotNull(vm.PauseCommand);
        Assert.False(string.IsNullOrWhiteSpace(vm.WipeLabel));
        Assert.False(string.IsNullOrWhiteSpace(vm.PauseLabel));
        Assert.False(string.IsNullOrWhiteSpace(vm.RetentionLabel));
    }

    [Fact]
    public void TheWipeIsNeverOneClick()
    {
        // The destructive command runs only from ConfirmCommand, and only while armed.
        var vm = MockAwarenessPrivacyVm.Live();
        var confirm = new MemoryForgetConfirm();
        int fired = 0;
        confirm.Bind(new CompanionRelayCommand(() => fired++));

        confirm.ConfirmCommand.Execute(null);
        Assert.Equal(0, fired);

        confirm.ArmCommand.Execute(null);
        Assert.True(confirm.IsArmed);
        confirm.ConfirmCommand.Execute(null);
        Assert.Equal(1, fired);

        // …and a double-click cannot run it twice.
        confirm.ConfirmCommand.Execute(null);
        Assert.Equal(1, fired);
        Assert.NotNull(vm.WipeCommand);
    }

    [Fact]
    public void EveryAppChipCarriesItsOwnAction()
    {
        IAwarenessPrivacyVm vm = MockAwarenessPrivacyVm.Live();

        Assert.NotEmpty(vm.SeenApps);
        Assert.NotEmpty(vm.KnownApps);
        Assert.All(vm.SeenApps.Concat(vm.KnownApps), chip =>
        {
            Assert.False(string.IsNullOrWhiteSpace(chip.Label));
            Assert.False(string.IsNullOrWhiteSpace(chip.ActionTip));
            Assert.NotNull(chip.ActionCommand);
        });
    }

    [Fact]
    public void TheWireViewSaysNothingHasBeenSentRatherThanInventingAFrame()
    {
        var dormant = MockAwarenessPrivacyVm.Dormant();
        Assert.False(dormant.HasWireJson);
        Assert.False(string.IsNullOrWhiteSpace(dormant.WireJsonEmptyCopy));

        var live = MockAwarenessPrivacyVm.Live();
        Assert.True(live.HasWireJson);
    }
}
