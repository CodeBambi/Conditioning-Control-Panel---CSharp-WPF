using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Ask EMI wave 1's tutorial half: the short walk, the completion ledger, and the narration seam.
///
/// <para>The whole class swaps <see cref="TutorialService.CompletionStore"/> for an in-memory one
/// in its constructor and puts the shipping ledger back in <see cref="Dispose"/>. Two reasons, and
/// the second is the important one: a test must not write the developer's real
/// <c>%LOCALAPPDATA%/ConditioningControlPanel/emi-desk.json</c>, and a tour walked to its end in
/// ANY test in this file would do exactly that through the shipping store. xUnit runs the tests
/// inside one class sequentially, so the swap is safe here and nowhere else - if these ever move
/// into another class, the swap has to move with them.</para>
/// </summary>
public class ShortWalkTourTests : IDisposable
{
    /// <summary>The ledger, with the disk taken out of it.</summary>
    private sealed class FakeStore : TutorialService.ITourCompletionStore
    {
        public readonly List<string> Latched = new();
        public bool Has(string name) => Latched.Contains(name, StringComparer.OrdinalIgnoreCase);
        public void Latch(string name) { if (!Has(name)) Latched.Add(name); }
    }

    private readonly FakeStore _store = new();

    public ShortWalkTourTests()
    {
        TutorialService.CompletionStore = _store;
    }

    public void Dispose()
    {
        // null restores the shipping EmiState-backed ledger.
        TutorialService.CompletionStore = null!;
    }

    // =====================================================================================
    //  the seven cards
    // =====================================================================================

    /// <summary>
    /// The seven step ids, in order. These are API twice over: the narrator maps each one onto the
    /// line pool <c>tour.&lt;stepId&gt;</c>, and the pools are written against this list
    /// (docs/emi-desk/WAVE1-CONTRACT.md). Renaming a step here mutes her for that card without any
    /// error anywhere - a moment nobody declared is dropped in silence, by design.
    /// </summary>
    [Fact]
    public void ShortWalkHasTheSevenContractStepsInOrder()
    {
        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);

        Assert.Equal(
            new[] { "sw-assets", "sw-flash", "sw-panic", "sw-dock", "sw-xp", "sw-settings", "sw-done" },
            svc.CurrentSteps.Select(s => s.Id).ToArray());
    }

    /// <summary>
    /// Ninety seconds means seven cards, not nine and not five. A step added without a pool written
    /// for it is a card she stands next to saying nothing.
    /// </summary>
    [Fact]
    public void ShortWalkIsSevenStepsLong()
    {
        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);
        Assert.Equal(7, svc.TotalSteps);
    }

    /// <summary>
    /// THE ROT GUARD. A TargetElementName that does not resolve degrades SILENTLY to an unspotlit
    /// centred card - no exception, no log, just a tour that stops pointing at things. Three steps
    /// of the older tours rotted that way (BtnProgression, FlashSection, BtnOpenAssets) and nobody
    /// noticed for releases. Every name the short walk points at is checked against the XAML here.
    /// </summary>
    [Fact]
    public void EveryShortWalkTargetIsALiveXamlName()
    {
        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);

        var names = new HashSet<string>(StringComparer.Ordinal);
        // Every product root, so a view that moves out of the WPF head keeps feeding this set
        // rather than quietly turning its own tour targets into "dead" names.
        foreach (var path in SourceRoots.EnumerateProductSources("*.xaml"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"x:Name=""([A-Za-z_][A-Za-z0-9_]*)"""))
                names.Add(m.Groups[1].Value);
        }

        Assert.True(names.Count > 500, $"only {names.Count} x:Names were scanned - the scan is broken, not the tour");

        var dead = svc.CurrentSteps
            .Where(s => s.TargetElementName != null && !names.Contains(s.TargetElementName))
            .Select(s => $"{s.Id} -> {s.TargetElementName}")
            .ToList();

        Assert.True(dead.Count == 0,
            "these short walk steps point at an x:Name that does not exist:\n  " + string.Join("\n  ", dead));
    }

    /// <summary>
    /// A spotlight step must never be <c>Center</c>: <c>TutorialOverlay.UpdateSpotlight</c>
    /// early-returns for centred cards BEFORE it measures anything, so a centred step with a target
    /// spotlights nothing at all.
    /// </summary>
    [Fact]
    public void NoShortWalkSpotlightStepIsCentred()
    {
        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);

        var wrong = svc.CurrentSteps
            .Where(s => s.TargetElementName != null && s.TextPosition == TutorialStepPosition.Center)
            .Select(s => s.Id)
            .ToList();

        Assert.True(wrong.Count == 0, "centred cards never spotlight: " + string.Join(", ", wrong));
    }

    /// <summary>
    /// The cards have to teach on their own: with EMI Desk off, missing or muted the seven
    /// descriptions ARE the tour. Every one carries a title and a body, and both come from
    /// <c>en.json</c> - which is checked here too, because Loc.Get answers a missing key with the
    /// key itself and a raw "tut_sw_xp_body" on screen is not an error the app can see.
    /// </summary>
    [Fact]
    public void EveryShortWalkCardHasEnglishCopy()
    {
        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);

        var en = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(SourceRoots.LanguagesDirectory, "en.json"))).RootElement;

        foreach (var step in svc.CurrentSteps)
        {
            var slug = step.Id.Replace("sw-", "");
            foreach (var key in new[] { $"tut_sw_{slug}_title", $"tut_sw_{slug}_body" })
            {
                Assert.True(en.TryGetProperty(key, out var value), $"{key} is missing from en.json");
                var text = value.GetString();
                Assert.False(string.IsNullOrWhiteSpace(text), $"{key} is empty");
                // The language files are strict-JSON clean as of 2026-07-29 and stay that way.
                Assert.DoesNotContain('\n', text!);
                Assert.DoesNotContain('\r', text!);
            }
        }
    }

    // =====================================================================================
    //  the completion ledger
    // =====================================================================================

    /// <summary>Walking the last card off the end is the one thing that latches.</summary>
    [Fact]
    public void FinishingTheWalkLatchesIt()
    {
        var svc = new TutorialService();
        Assert.False(svc.HasCompleted(TutorialType.ShortWalk));

        svc.Start(TutorialType.ShortWalk);
        for (int i = 0; i < 7; i++) svc.Next();   // six advances, then off the end

        Assert.False(svc.IsActive);
        Assert.True(svc.HasCompleted(TutorialType.ShortWalk));
        Assert.Equal(new[] { "ShortWalk" }, _store.Latched.ToArray());
    }

    /// <summary>
    /// A walk somebody bailed out of is a walk they have not had. Escape, the skip button, the host
    /// window closing and app shutdown all land in Skip, and none of them may latch: latching a
    /// skip is how a first-run user is never offered the tour they never actually saw.
    /// </summary>
    [Fact]
    public void SkippingTheWalkLatchesNothing()
    {
        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);
        svc.Next();
        svc.Next();
        svc.Skip();

        Assert.False(svc.IsActive);
        Assert.False(svc.HasCompleted(TutorialType.ShortWalk));
        Assert.Empty(_store.Latched);
    }

    /// <summary>
    /// Skipping on the LAST card is still a skip. The finish is the advance off the end, not the
    /// index - otherwise Escape on card seven would count as having walked it.
    /// </summary>
    [Fact]
    public void SkippingOnTheLastCardIsStillASkip()
    {
        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);
        for (int i = 0; i < 6; i++) svc.Next();
        Assert.True(svc.IsLastStep);

        svc.Skip();
        Assert.False(svc.HasCompleted(TutorialType.ShortWalk));
    }

    /// <summary>
    /// The point of the whole exercise: the latch outlives the service. Before this, every restart
    /// re-offered every tour, because TutorialService remembered nothing at all.
    /// </summary>
    [Fact]
    public void HasCompletedSurvivesAReload()
    {
        var first = new TutorialService();
        first.Start(TutorialType.ShortWalk);
        for (int i = 0; i < 7; i++) first.Next();

        var afterRestart = new TutorialService();
        Assert.True(afterRestart.HasCompleted(TutorialType.ShortWalk));
        Assert.False(afterRestart.HasCompleted(TutorialType.FullTour));
    }

    /// <summary>Finishing twice writes one row, not two.</summary>
    [Fact]
    public void LatchingIsIdempotent()
    {
        var svc = new TutorialService();
        for (int run = 0; run < 2; run++)
        {
            svc.Start(TutorialType.ShortWalk);
            for (int i = 0; i < 7; i++) svc.Next();
        }
        Assert.Equal(new[] { "ShortWalk" }, _store.Latched.ToArray());
    }

    /// <summary>
    /// A ledger that throws is not allowed to take the tour with it. HasCompleted answers false and
    /// a finish still ends cleanly.
    /// </summary>
    [Fact]
    public void AThrowingLedgerNeverBreaksATour()
    {
        TutorialService.CompletionStore = new ThrowingStore();
        try
        {
            var svc = new TutorialService();
            Assert.False(svc.HasCompleted(TutorialType.ShortWalk));

            svc.Start(TutorialType.ShortWalk);
            for (int i = 0; i < 7; i++) svc.Next();

            Assert.False(svc.IsActive);
        }
        finally
        {
            TutorialService.CompletionStore = _store;
        }
    }

    private sealed class ThrowingStore : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => throw new InvalidOperationException("ledger is on fire");
        public void Latch(string name) => throw new InvalidOperationException("ledger is on fire");
    }

    // =====================================================================================
    //  the narration seam
    // =====================================================================================

    /// <summary>
    /// THE LAW. Narration is additive and never load-bearing. There is no EmiDeskService in a test
    /// run (App.EmiDesk is null, exactly as it is on a run where her construction threw), and the
    /// tour has to walk from end to end without noticing.
    /// </summary>
    [Fact]
    public void NarratorDegradesSilentlyWithNoEmiDeskService()
    {
        Assert.Null(App.EmiDesk);   // the premise; if this ever fails the test proves nothing

        var svc = new TutorialService();
        using var narrator = new EmiTourNarrator(svc);

        svc.Start(TutorialType.ShortWalk);
        Assert.Equal("sw-assets", svc.CurrentStep?.Id);

        for (int i = 0; i < 6; i++) svc.Next();
        Assert.Equal("sw-done", svc.CurrentStep?.Id);

        svc.Next();
        Assert.False(svc.IsActive);
        Assert.True(svc.HasCompleted(TutorialType.ShortWalk));
    }

    /// <summary>A skipped tour is the same story: no EMI, no difference.</summary>
    [Fact]
    public void NarratorDegradesSilentlyOnASkip()
    {
        var svc = new TutorialService();
        using var narrator = new EmiTourNarrator(svc);

        svc.Start(TutorialType.ShortWalk);
        svc.Next();
        svc.Skip();

        Assert.False(svc.IsActive);
        Assert.Empty(_store.Latched);
    }

    /// <summary>
    /// Attach/Detach are the app's pair (armed in MainWindow.StartTutorial, released when the
    /// overlay closes). Both are idempotent and neither throws on null, because a tour must never
    /// fail to start over its own narration bookkeeping.
    /// </summary>
    [Fact]
    public void AttachAndDetachAreIdempotentAndNullSafe()
    {
        EmiTourNarrator.Detach();
        Assert.Null(EmiTourNarrator.Active);

        EmiTourNarrator.Attach(null);
        Assert.Null(EmiTourNarrator.Active);

        var svc = new TutorialService();
        EmiTourNarrator.Attach(svc);
        var first = EmiTourNarrator.Active;
        Assert.NotNull(first);

        // Same service twice keeps the same narrator: two would double every line.
        EmiTourNarrator.Attach(svc);
        Assert.Same(first, EmiTourNarrator.Active);

        EmiTourNarrator.Detach();
        Assert.Null(EmiTourNarrator.Active);
        EmiTourNarrator.Detach();
        Assert.Null(EmiTourNarrator.Active);
    }

    /// <summary>
    /// A disposed narrator is deaf. It is detached when the overlay closes, and a tour started
    /// afterwards by some other window must not reach a listener nobody owns any more.
    /// </summary>
    [Fact]
    public void ADisposedNarratorStopsListening()
    {
        var svc = new TutorialService();
        var narrator = new EmiTourNarrator(svc);
        narrator.Dispose();
        narrator.Dispose();   // idempotent

        svc.Start(TutorialType.ShortWalk);
        for (int i = 0; i < 7; i++) svc.Next();
        Assert.True(svc.HasCompleted(TutorialType.ShortWalk));
    }

    // =====================================================================================
    //  the moment vocabulary
    // =====================================================================================

    /// <summary>
    /// The narrator's per-step moments exist. <c>Fire</c> on an id the lines file does not carry is
    /// dropped in silence - correct for a typo, and indistinguishable from a card she simply had
    /// nothing to say about. EmiMomentIdWiringTests covers the four literals (tourStarted,
    /// tourFinished, tourSkipped, tourStep); the seven per-step ids are BUILT at runtime from the
    /// step id, so no regex over the source can see them and they are checked here instead.
    /// </summary>
    [Fact]
    public void EveryShortWalkStepHasItsOwnMoment()
    {
        var moments = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppDir(), "Resources", "emi", "desk-lines.json"))).RootElement.GetProperty("moments");

        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);

        var missing = svc.CurrentSteps
            .Select(s => EmiTourNarrator.StepMomentPrefix + s.Id)
            .Where(id => !moments.TryGetProperty(id, out _))
            .ToList();

        Assert.True(missing.Count == 0,
            "these short walk moments are not declared in desk-lines.json, so she is mute on those "
            + "cards:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>The fallback the other nine tours ride has to be there, or they narrate nothing.</summary>
    [Fact]
    public void TheStepFallbackMomentExists()
    {
        var moments = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppDir(), "Resources", "emi", "desk-lines.json"))).RootElement.GetProperty("moments");

        Assert.True(moments.TryGetProperty(EmiTourNarrator.StepFallbackMoment, out _),
            EmiTourNarrator.StepFallbackMoment + " is missing from desk-lines.json");
    }

    // =====================================================================================
    //  locating the tree
    // =====================================================================================

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
