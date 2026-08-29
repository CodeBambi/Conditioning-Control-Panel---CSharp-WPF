using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The two defects the FIRST live run of EMI Desk turned up (QA 2026-08-29), pinned so they cannot
/// come back.
///
/// <para>Both are source tripwires rather than behavioural tests, and deliberately so: one lives in
/// a re-entrant modal pump and the other in a mouse gesture on a layered window, and neither can be
/// reached without a running WPF app, a real desktop and a 125% monitor. What CAN be checked
/// cheaply is that the guard is still written down - the bugs were both a missing line, not a wrong
/// algorithm.</para>
/// </summary>
public class EmiDeskLiveRunRegressionTests
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
    public void A_summon_interrupted_by_the_mute_prompt_cannot_finish()
    {
        // THE MODAL RE-ENTRY TRAP. Summon() sets IsOut=true and then BLOCKS in
        // EmiMutePromptWindow.ShowDialog, whose nested message pump keeps the chord, the dock chip
        // and the tray alive. A dismiss in that window put IsOut back to false and hid her, and the
        // summon then carried on and Show()ed her anyway: she came back on screen with IsOut=false,
        // where the hover x, Dismiss() and AvatarMuted all guard on IsOut and so did nothing at
        // all. She was unclosable for the rest of the session. The generation stamp is the fix.
        var src = Read("Services", "EmiDesk", "EmiDeskService.cs");

        Assert.Contains("_summonGen", src);
        Assert.Contains("long gen = ++_summonGen;", src);

        // The stamp has to be taken BEFORE the prompt and checked AFTER it, or it guards nothing.
        int stamp = src.IndexOf("long gen = ++_summonGen;", StringComparison.Ordinal);
        int prompt = src.IndexOf("MaybeAskAboutMuting();", StringComparison.Ordinal);
        int check = src.IndexOf("if (_summonGen != gen", StringComparison.Ordinal);
        Assert.True(stamp >= 0 && prompt > stamp && check > prompt,
            "the summon generation must be stamped before MaybeAskAboutMuting and checked after it");

        // And a dismiss has to invalidate the summon parked in that pump, or the prompt returning
        // still puts her back on screen behind it.
        Assert.Contains("_summonGen++;", src);

        // Belt and braces: a widget that IS on screen must always be dismissable, whatever the
        // flag says, so no future desync can strand her again.
        Assert.Contains("WindowOnScreen", src);
        Assert.Contains("if (IsOut || WindowOnScreen) Dismiss();", src);
    }

    [Fact]
    public void The_drag_threshold_lives_in_exactly_one_coordinate_space()
    {
        // THE COORDINATE TRAP, the app-wide one. The body measured its travel in PHYSICAL pixels
        // (PointToScreen) while the ring's drag watch measured DIPs, both against the same "6".
        // At the owner's 125% that made the two disagree by a quarter: a 7 px hand tremor closing
        // the ring was a click to one and a drag to the other, so the ring never toggled and she
        // crept across the desktop by exactly the tremor instead. One name, one space, DIPs.
        var body = Read("Windows", "EmiDesk", "EmiDeskWindow.xaml.cs");
        var ring = Read("Windows", "EmiDesk", "EmiDeskWindow.Ring.cs");

        Assert.DoesNotContain("DragThresholdPx", body);
        Assert.DoesNotContain("DragThresholdPx", ring);
        Assert.Contains("DragThresholdDip", body);
        Assert.Contains("DragThresholdDip", ring);

        // The body's own measurement must be scaled out of physical pixels before it is compared.
        Assert.Contains("double dx = (now.X - _dragStartScreen.X) / s;", body);
        Assert.Contains("double dy = (now.Y - _dragStartScreen.Y) / s;", body);
    }

    [Fact]
    public void Her_width_has_a_single_home()
    {
        // EmiState carried a second copy of the width (winWidthDip) that restore applied unclamped,
        // while the shrink offer and the grip both wrote AppSettings.EmiDeskWidth - so the two
        // sources drifted and a shrink did not survive the next summon. BRIEF 5 puts the width in
        // settings and the rect in EmiState; the duplicate is gone.
        Assert.DoesNotContain("WinWidthDip", Read("Services", "EmiDesk", "EmiState.cs"));
        Assert.DoesNotContain("WinWidthDip", Read("Windows", "EmiDesk", "EmiDeskWindow.xaml.cs"));
    }
}
