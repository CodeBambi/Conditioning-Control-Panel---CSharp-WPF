using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EmiTourNarrator on this head: a tour becomes tourStarted, a line per step (its own pool or
/// tourStep), tourFinished or tourSkipped; she is summoned first when she is enabled and away; a walk taken
/// to its end is latched so it is not offered again; and a narrator that throws never takes the tour down.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps EmiDeskBus.Sink, the narrator's desk seams and the tour ledger
public sealed class EmiTourNarratorTests
{
    private sealed class FakeStore : TutorialService.ITourCompletionStore
    {
        public readonly List<string> Latched = new();
        public bool Has(string name) => Latched.Contains(name, StringComparer.OrdinalIgnoreCase);
        public void Latch(string name) { if (!Has(name)) Latched.Add(name); }
    }

    private static Task With(Action<TutorialService, List<string>, FakeStore> body, bool deskOut = true) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            await Task.CompletedTask;
            var moments = new List<string>();
            var store = new FakeStore();
            var sink = EmiDeskBus.Sink; var out0 = EmiTourNarrator.DeskOut; var summon0 = EmiTourNarrator.SummonDesk;
            var has0 = EmiTourNarrator.HasMoment;
            TutorialService.CompletionStore = store;       // never the real emi-desk.json
            try
            {
                EmiDeskBus.Sink = (id, _) => moments.Add(id);
                EmiTourNarrator.DeskOut = () => deskOut;
                EmiTourNarrator.SummonDesk = () => { moments.Add("(summon)"); return Task.CompletedTask; };
                EmiTourNarrator.HasMoment = id => id == "tour.known";
                var svc = new TutorialService();
                using var narrator = new EmiTourNarrator(svc);
                body(svc, moments, store);
            }
            finally
            {
                EmiDeskBus.Sink = sink; EmiTourNarrator.DeskOut = out0; EmiTourNarrator.SummonDesk = summon0;
                EmiTourNarrator.HasMoment = has0;
                TutorialService.CompletionStore = null!;
            }
        });

    [Fact]
    public Task AWalkedTour_SaysStartedAStepLineEachCardAndFinished_AndIsLatched() => With((svc, moments, store) =>
    {
        svc.Start(TutorialType.ShortWalk);
        Assert.Equal("tourStarted", moments[0]);
        int guard = 0;
        while (svc.IsActive && guard++ < 60) svc.Next();
        Assert.False(svc.IsActive);
        Assert.Equal("tourFinished", moments[^1]);
        Assert.Contains("tourStep", moments);              // no pool of its own: the generic step line
        Assert.DoesNotContain("tourSkipped", moments);
        Assert.DoesNotContain("(summon)", moments);         // she was already out
        Assert.Contains(nameof(TutorialType.ShortWalk), store.Latched);   // a walk taken is not offered again
        Assert.True(TutorialService.CompletionStore.Has("shortwalk"));
    });

    [Fact]
    public Task ASkippedTour_SaysSkipped_OnceAndIsNotLatched() => With((svc, moments, store) =>
    {
        svc.Start(TutorialType.ShortWalk);
        svc.Skip();
        svc.Skip();
        Assert.Equal(1, moments.Count(m => m == "tourSkipped"));
        Assert.DoesNotContain("tourFinished", moments);
        Assert.Empty(store.Latched);
    });

    [Fact]
    public Task SheIsSummonedFirstWhenEnabledAndAway_AndNeverWhenSwitchedOff() => With((svc, moments, _) =>
    {
        var s = CoreSettings.Current;
        bool enabled = s.EmiDeskEnabled;
        try
        {
            s.EmiDeskEnabled = true;
            svc.Start(TutorialType.ShortWalk);
            Assert.Equal(new[] { "(summon)", "tourStarted" }, moments.Take(2));
            svc.Skip();

            moments.Clear();
            s.EmiDeskEnabled = false;
            svc.Start(TutorialType.ShortWalk);
            Assert.DoesNotContain("(summon)", moments);     // a tour never switches her on
            Assert.False(s.EmiDeskEnabled);
            svc.Skip();
        }
        finally { s.EmiDeskEnabled = enabled; }
    }, deskOut: false);

    [Fact]
    public Task AStepWithItsOwnPoolUsesIt_AndAThrowingDeskNeverStopsTheTour() => With((svc, moments, _) =>
    {
        EmiTourNarrator.HasMoment = id => id.StartsWith("tour.", StringComparison.Ordinal);
        svc.Start(TutorialType.ShortWalk);
        svc.Next();
        Assert.Contains(moments, m => m.StartsWith("tour.", StringComparison.Ordinal));
        Assert.DoesNotContain("tourStep", moments);

        EmiTourNarrator.HasMoment = _ => throw new InvalidOperationException("lines not loaded");
        int before = svc.CurrentStepIndex;
        svc.Next();
        Assert.True(!svc.IsActive || svc.CurrentStepIndex == before + 1);   // the tour moved on regardless
        svc.Skip();
    });

    [Fact]
    public Task AttachIsOneAtATime_AndDetachIsSafeTwice() => With((svc, _, _) =>
    {
        var before = EmiTourNarrator.Active;
        try
        {
            EmiTourNarrator.Attach(null);
            EmiTourNarrator.Attach(svc);
            var first = EmiTourNarrator.Active;
            Assert.NotNull(first);
            EmiTourNarrator.Attach(svc);
            Assert.Same(first, EmiTourNarrator.Active);
            EmiTourNarrator.Detach();
            EmiTourNarrator.Detach();
            Assert.Null(EmiTourNarrator.Active);
        }
        finally { EmiTourNarrator.Detach(); if (before != null) EmiTourNarrator.Attach(TutorialHead.Service); }
    });
}
