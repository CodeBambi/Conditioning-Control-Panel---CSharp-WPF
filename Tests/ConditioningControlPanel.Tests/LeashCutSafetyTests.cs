using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Leash's way out (owner, 2026-09-26): the cut releases every lock locally and at once
/// (<see cref="LeashCutSafety"/>), a leashed account refuses the trap commands from any remote
/// session (<see cref="LeashRemoteRule"/>), and the tray cut stays reachable (<see cref="LeashTrayRule"/>).
/// </summary>
[Collection(LeashGuardCollection.Name)]
public class LeashCutSafetyTests
{
    // ============================ the cut ============================

    private sealed class FakeTargets : LeashCutSafety.ITargets
    {
        public readonly List<string> Calls = new();
        public bool Lockdown, Remote, StrictVideo;
        public bool Strict = true, Panic = false;
        public bool TabCan;
        public string? Throws;

        // Lockdown's own Deactivate puts the PRE-lockdown values back; model that, so the test
        // proves the release runs after it and wins.
        public bool PreStrict = true, PrePanic = false;

        private void Hit(string name)
        {
            Calls.Add(name);
            if (Throws == name) throw new InvalidOperationException("boom " + name);
        }

        public bool LockdownActive => Lockdown;
        public void EndLockdown() { Hit("endLockdown"); Lockdown = false; Strict = PreStrict; Panic = PrePanic; }
        public void DiscardLockdownRecovery() => Hit("discardRecovery");
        public bool RemoteActive => Remote;
        public void EndRemote() { Hit("endRemote"); Remote = false; }
        public bool StrictVideoRunning => StrictVideo;
        public void StopVideo() { Hit("stopVideo"); StrictVideo = false; }
        public void ReleaseSafetySettings() { Hit("release"); Strict = false; Panic = true; }
        public bool DropUnpushedLeashBookings() { Hit("tab"); return TabCan; }
    }

    [Fact]
    public void Cut_EndsEverything_ThenReleasesLast()
    {
        var t = new FakeTargets { Lockdown = true, Remote = true, StrictVideo = true };

        var done = LeashCutSafety.Apply(t);

        Assert.Equal(new[] { "endLockdown", "discardRecovery", "endRemote", "stopVideo", "release", "tab" }, t.Calls);
        Assert.Equal(new[] { LeashCutSafety.StepLockdown, LeashCutSafety.StepRemote, LeashCutSafety.StepVideo,
            LeashCutSafety.StepSettings }, done);
        // Lockdown restored strict ON / panic OFF on its way out; the release still won.
        Assert.False(t.Strict);
        Assert.True(t.Panic);
    }

    [Fact]
    public void Cut_WithNothingRunning_StillResetsBothSettings()
    {
        // The user turned Strict Lock on themselves: the cut resets it anyway (owner rule).
        var t = new FakeTargets { Strict = true, Panic = false };

        var done = LeashCutSafety.Apply(t);

        Assert.Equal(new[] { LeashCutSafety.StepSettings }, done);
        Assert.DoesNotContain("endLockdown", t.Calls);
        Assert.DoesNotContain("endRemote", t.Calls);
        Assert.DoesNotContain("stopVideo", t.Calls);
        // A leftover recovery file is discarded even without an active lockdown.
        Assert.Contains("discardRecovery", t.Calls);
        Assert.False(t.Strict);
        Assert.True(t.Panic);
    }

    [Theory]
    [InlineData("endLockdown")]
    [InlineData("endRemote")]
    [InlineData("stopVideo")]
    [InlineData("tab")]
    public void Cut_AFailingStep_NeverStopsTheRelease_AndNeverThrows(string failing)
    {
        var t = new FakeTargets { Lockdown = true, Remote = true, StrictVideo = true, Throws = failing };

        var ex = Record.Exception(() => LeashCutSafety.Apply(t));

        Assert.Null(ex);
        Assert.Contains("release", t.Calls);
        Assert.Contains("discardRecovery", t.Calls);
        Assert.True(t.Panic);
        Assert.False(t.Strict);
    }

    [Fact]
    public void Cut_AFailingRelease_NeverThrows()
    {
        var t = new FakeTargets { Throws = "release" };
        Assert.Null(Record.Exception(() => LeashCutSafety.Apply(t)));
        Assert.Contains("tab", t.Calls);
    }

    [Fact]
    public void Cut_ReportsTheTabStepOnlyWhenItDroppedSomething()
    {
        Assert.Contains(LeashCutSafety.StepTab, LeashCutSafety.Apply(new FakeTargets { TabCan = true }));
        Assert.DoesNotContain(LeashCutSafety.StepTab, LeashCutSafety.Apply(new FakeTargets { TabCan = false }));
    }

    [Fact]
    public void Cut_NullTargets_IsANoOp()
    {
        Assert.Empty(LeashCutSafety.Apply((LeashCutSafety.ITargets)null!));
    }

    [Fact]
    public void AppTargets_UsesTheLocalRemoteEnd_AndTheLockdownDeactivate()
    {
        // Pinned in source: the real targets must not wait on the network for the remote end, and
        // must end Lockdown through Deactivate (which is what restores + deletes the recovery file).
        var src = ReadSource("ConditioningControlPanel", "Services", "Leash", "LeashCutSafety.cs");
        Assert.Contains("App.RemoteControl?.EndSessionNow()", src);
        Assert.Contains("App.Lockdown?.Deactivate()", src);
        Assert.Contains("LockdownService.DiscardRecovery()", src);
        Assert.Contains("s.StrictLockEnabled = false;", src);
        Assert.Contains("s.PanicKeyEnabled = true;", src);
        Assert.Contains("SaveImmediate()", src);
        Assert.Contains("SyncNoPanicState()", src);
        Assert.Contains("App.Video?.ForceCleanup()", src);
    }

    [Fact]
    public void Remote_EndSessionNow_CleansUpBeforeItTalksToTheServer()
    {
        var src = ReadSource("ConditioningControlPanel", "Services", "RemoteControlService.cs");
        var body = Between(src, "public void EndSessionNow()", "private void CleanupSession()");
        var cleanup = body.IndexOf("CleanupSession();", StringComparison.Ordinal);
        var post = body.IndexOf("/v2/remote/stop", StringComparison.Ordinal);
        Assert.True(cleanup >= 0 && post > cleanup, "EndSessionNow must clean up locally before the stop call");
        Assert.DoesNotContain("await AuthPostAsync", body.Substring(0, cleanup));
    }

    // ============================ the remote door ============================

    [Theory]
    [InlineData("enable_strict_lock", false, LeashRemoteVerdict.Refuse)]
    [InlineData("disable_panic", false, LeashRemoteVerdict.Refuse)]
    [InlineData("start_session", true, LeashRemoteVerdict.StripStrict)]
    [InlineData("start_session", false, LeashRemoteVerdict.Allow)]
    [InlineData("disable_strict_lock", false, LeashRemoteVerdict.Allow)]
    [InlineData("enable_panic", false, LeashRemoteVerdict.Allow)]
    [InlineData("trigger_panic", false, LeashRemoteVerdict.Allow)]
    [InlineData("trigger_flash", false, LeashRemoteVerdict.Allow)]
    [InlineData(null, false, LeashRemoteVerdict.Allow)]
    public void Leashed_RefusesOnlyTheTraps(string? action, bool asksStrict, LeashRemoteVerdict expected)
    {
        Assert.Equal(expected, LeashRemoteRule.Screen(action, asksStrict, leashed: true));
    }

    [Theory]
    [InlineData("enable_strict_lock", false)]
    [InlineData("disable_panic", false)]
    [InlineData("start_session", true)]
    public void NotLeashed_ChangesNothing(string action, bool asksStrict)
    {
        Assert.Equal(LeashRemoteVerdict.Allow, LeashRemoteRule.Screen(action, asksStrict, leashed: false));
    }

    [Theory]
    [InlineData("{\"strict_lock\":true}", true)]
    [InlineData("{\"strict_lock\":false}", false)]
    [InlineData("{\"strict_lock\":\"true\"}", true)]
    [InlineData("{\"strict_lock\":\"yes\"}", false)]
    [InlineData("{\"strict_lock\":1}", true)]
    [InlineData("{\"strict_lock\":0}", false)]
    [InlineData("{\"strict_lock\":{\"a\":1}}", false)]
    [InlineData("{}", false)]
    public void AsksStrictLock_ReadsTolerantly(string json, bool expected)
    {
        Assert.Equal(expected, LeashRemoteRule.AsksStrictLock(JObject.Parse(json)));
    }

    [Fact]
    public void AsksStrictLock_NullParameters_IsFalse() => Assert.False(LeashRemoteRule.AsksStrictLock(null));

    [Fact]
    public void RemoteDoor_ScreensBeforeAnyCommandRuns_AndStripsTheStartSessionFlag()
    {
        var src = ReadSource("ConditioningControlPanel", "Services", "RemoteControlService.cs");
        var exec = Between(src, "private void ExecuteCommand(string action, JObject? parameters)", "switch (action)");
        Assert.Contains("LeashRemoteRule.Screen(", exec);
        Assert.Contains("LeashGuard.Check()", exec);
        Assert.Contains("LeashRemoteVerdict.Refuse", exec);
        Assert.Contains("return;", exec);

        var start = Between(src, "case \"start_session\":", "case \"pause_session\":");
        Assert.Contains("leashVerdict != Leash.LeashRemoteVerdict.StripStrict", start);
    }

    // ============================ the guard ============================

    [Fact]
    public void Guard_DefaultsToNotLeashed_AndAThrowCountsAsLeashed()
    {
        var saved = LeashGuard.IsLeashed;
        try
        {
            LeashGuard.IsLeashed = () => false;
            Assert.False(LeashGuard.Check());
            LeashGuard.IsLeashed = () => true;
            Assert.True(LeashGuard.Check());
            LeashGuard.IsLeashed = () => throw new InvalidOperationException("service not built");
            Assert.True(LeashGuard.Check());
            LeashGuard.IsLeashed = null!;
            Assert.False(LeashGuard.Check());
        }
        finally { LeashGuard.IsLeashed = saved; }
    }

    // ============================ the tray ============================

    [Fact]
    public void Tray_CutIsAlwaysAllowed_OthersFollowTheLock()
    {
        Assert.True(LeashTrayRule.IsAlwaysAllowed(LeashTrayRule.CutActionId));
        Assert.True(LeashTrayRule.ItemEnabled(LeashTrayRule.CutActionId, lockedOut: true));
        Assert.False(LeashTrayRule.ItemEnabled("launcher_back", lockedOut: true));
        Assert.True(LeashTrayRule.ItemEnabled("launcher_back", lockedOut: false));
        Assert.False(LeashTrayRule.IsAlwaysAllowed(null));
    }

    [Fact]
    public void Tray_IconStaysUpWhileLeashed()
    {
        Assert.True(LeashTrayRule.KeepIconVisible(true));
        Assert.False(LeashTrayRule.KeepIconVisible(false));

        var src = ReadSource("ConditioningControlPanel", "Services", "Notifications", "TrayIconService.cs");
        var show = Between(src, "public void ShowWindow()", "private void EnsureOnScreen()");
        var hide = show.IndexOf("_notifyIcon.Visible = false;", StringComparison.Ordinal);
        var keep = show.IndexOf("LeashTrayRule.KeepIconVisible", StringComparison.Ordinal);
        Assert.True(hide >= 0 && keep > hide, "ShowWindow must re-show the icon for a leashed user after hiding it");
        Assert.Contains("public void SyncLeashIcon()", src);
    }

    // ============================ helpers ============================

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"'{start}' not found");
        int to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"'{end}' not found after '{start}'");
        return source.Substring(from, to - from);
    }
}

/// <summary><see cref="LeashGuard.IsLeashed"/> is process-wide; the suite that swaps it runs alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class LeashGuardCollection
{
    public const string Name = "LeashGuard static seam";
}
