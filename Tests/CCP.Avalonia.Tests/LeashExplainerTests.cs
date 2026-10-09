using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash.Explain;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;
using Settings = ConditioningControlPanel.Models.AppSettings;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 5 r11: the leash explainer (WPF 7.1.5 Controls/Leash/Explain): the "?" door, the
/// holder's first offer (BeforeOffer), and the ask card's "read first" gate.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps LeashExplainer / LeashExplainHost seams and the live settings flags
public sealed class LeashExplainerTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static T Find<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().First(c => c.Tag as string == tag);

    [Fact]
    public async Task BeforeOffer_FirstTimeOpensTheExplainer_AndSendsOnlyFromOffer()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            LeashFx.ForceStill = true;
            Window? shown = null;
            LeashExplainer.PresentOverride = w => shown = w;
            try
            {
                var s = new Settings();
                int sent = 0;
                Assert.False(LeashExplainer.BeforeOffer(null, "Juno", () => sent++, s));
                Assert.NotNull(shown);
                Assert.Equal(0, sent);
                var card = shown!.GetLogicalDescendants().OfType<LeashExplainCard>().Single();
                Assert.Equal(LeashIntroSide.Holder, card.Side);
                Assert.Equal(4, card.Panels.Count);
                Assert.Equal(2, card.Lines.Count);
                Find<Button>(shown, "leash-explain-offer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, sent);
                Assert.True(s.LeashIntroSeenHolder);

                // Seen: the next offer sends at once, no window.
                shown = null;
                Assert.True(LeashExplainer.BeforeOffer(null, "Juno", () => sent++, s));
                Assert.Null(shown);
                Assert.Equal(2, sent);
            }
            finally { LeashExplainer.PresentOverride = null; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task HelpDoor_OpensTheExplainer_HolderOrLeashedBySurface()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            LeashFx.ForceStill = true;
            Window? shown = null;
            var oldPresenter = LeashExplainHost.Presenter;
            LeashExplainHost.Presenter = null;
            LeashExplainer.PresentOverride = w => shown = w;
            try
            {
                LeashExplainHost.Show(LeashExplainRole.Offer);
                Assert.Equal(LeashIntroSide.Holder, shown!.GetLogicalDescendants().OfType<LeashExplainCard>().Single().Side);
                Assert.Contains(shown.GetLogicalDescendants().OfType<Button>(), b => b.Tag as string == "leash-explain-got-it");
                LeashExplainHost.Show(LeashExplainRole.Gate);
                Assert.Equal(LeashIntroSide.Leashed, shown!.GetLogicalDescendants().OfType<LeashExplainCard>().Single().Side);
            }
            finally
            {
                LeashExplainer.PresentOverride = null;
                LeashExplainHost.Presenter = oldPresenter;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AskCard_FirstAsk_HoldsPutItOnUntilRead()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            LeashFx.ForceStill = true;
            var live = CoreSettings.Current;
            bool was = live.LeashIntroSeenLeashed;
            live.LeashIntroSeenLeashed = false;
            try
            {
                var offer = new LeashOffer(new LeashPerson("h1", "Vex", null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
                var ask = new LeashAskCard(offer, () => null);
                var intro = Assert.IsType<LeashExplainCard>(ask.ExplainerSlot.Child);
                Assert.False(ask.PutItOnButton.IsEnabled);
                intro.AdvanceReadClock(TimeSpan.FromMilliseconds(LeashIntroRule.AskReadMs + 100));
                Assert.True(ask.PutItOnButton.IsEnabled);

                live.LeashIntroSeenLeashed = true;
                Assert.True(new LeashAskCard(offer, () => null).PutItOnButton.IsEnabled);
            }
            finally { live.LeashIntroSeenLeashed = was; }
            return Task.CompletedTask;
        });
    }
}
