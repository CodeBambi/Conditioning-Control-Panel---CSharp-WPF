using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The haunt's registry is a WPF visual tree, so every read of it is UI-thread work.
///
/// <para><b>The bug.</b> Three users' logs carried "Possession: target walk failed" with
/// InvalidOperationException from Dispatcher.VerifyAccess (ccp-bugs #1160, #1167, #1184). The
/// caller was PossessionEvents' settings trigger: AppSettings raises PropertyChanged INLINE on
/// whatever thread wrote the setting, and plenty of settings are written off the UI thread, so a
/// background write walked MainWindow's visual tree from a thread pool worker. The very first
/// thing GetPossessionTargets reads is Window.Content, a dependency property, and that throws.</para>
///
/// <para><b>Why source tripwires.</b> Reproducing it needs a realized MainWindow, an armed
/// lockdown and a background settings write landing inside a 750 ms window. The fix is two lines in
/// two files, and losing either of them silently brings the warning back, so both are pinned
/// here.</para>
/// </summary>
public class PossessionThreadAffinityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", Path.Combine(parts)));

    [Fact]
    public void The_settings_trigger_reacts_on_the_ui_thread()
    {
        var src = Read("Services", "Possession", "PossessionEvents.cs");

        // The reaction (NearestTarget walks the registry, RequestReactive mutates the live ledger)
        // has to be posted, and posted rather than pumped: a settings write must never block on a
        // dispatcher that is at that moment animating a ghost.
        Assert.Contains("Helpers.DispatcherHelper.RunOnUI(", src);
        Assert.DoesNotContain("RunOnUISync", src);

        int hop = src.IndexOf("Helpers.DispatcherHelper.RunOnUI(", StringComparison.Ordinal);
        int nearest = src.IndexOf("NearestTarget(PossessionRole.Label)", StringComparison.Ordinal);
        Assert.True(hop >= 0 && nearest > hop,
            "the registry read must happen INSIDE the dispatcher hop, not before it");
    }

    [Fact]
    public void The_target_walk_refuses_to_run_off_the_ui_thread()
    {
        var src = Read("MainWindow", "MainWindow.Possession.cs");

        // Belt for every other caller: the registry is public surface read from a dozen effects, so
        // an off-thread read degrades to the last good snapshot instead of throwing.
        int guard = src.IndexOf("if (!Dispatcher.CheckAccess())", StringComparison.Ordinal);
        int walk = src.IndexOf("var root = Content as DependencyObject", StringComparison.Ordinal);
        Assert.True(guard >= 0, "GetPossessionTargets lost its dispatcher-affinity guard");
        Assert.True(walk > guard, "the guard must come before the first dependency-property read");

        // And it must never wait on the UI thread to get an answer.
        Assert.DoesNotContain("Dispatcher.Invoke(", src);
    }
}
