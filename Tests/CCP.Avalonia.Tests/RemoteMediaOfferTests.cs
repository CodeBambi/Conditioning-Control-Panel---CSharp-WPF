using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HB19: the remote-media offer after the "no videos" dialog (WPF App.OfferRemoteMediaSource).
/// Swaps the process-wide presenter and card seams, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class RemoteMediaOfferTests
{
    [Fact]
    public void A_local_library_gets_one_offer_a_launch_and_a_remote_one_gets_none()
    {
        var (present, card, source) = (RemoteMediaOffer.Present, RemoteMediaOffer.ShowCard, CoreSettings.Current.MediaSource);
        var rows = new List<InboxItem>();
        var cards = new List<string>();
        RemoteMediaOffer.Present = rows.Add;
        RemoteMediaOffer.ShowCard = (key, _) => cards.Add(key);
        RemoteMediaOffer.ResetForTests();
        try
        {
            // Already on the online pool: the dead end is a different problem, no card.
            CoreSettings.Current.MediaSource = "online";
            Assert.False(RemoteMediaOffer.Offer("videos"));
            Assert.Empty(rows);

            CoreSettings.Current.MediaSource = "local";
            Assert.True(RemoteMediaOffer.Offer("videos"));
            var row = Assert.Single(rows);
            Assert.Equal("intro:remote-media", row.Key);
            Assert.Empty(cards);   // a row that is never opened spends nothing

            row.Open!();
            Assert.Equal(new[] { RemoteMediaOffer.IntroKey }, cards);
            Assert.Equal("local", CoreSettings.Current.MediaSource);   // the offer itself changes nothing and fetches nothing

            // One offer per launch, however many dead ends follow.
            Assert.True(RemoteMediaOffer.Offer("videos"));
            rows[^1].Open!();
            Assert.Single(cards);
        }
        finally
        {
            RemoteMediaOffer.Present = present; RemoteMediaOffer.ShowCard = card;
            RemoteMediaOffer.ResetForTests();
            CoreSettings.Current.MediaSource = source;
        }
    }
}
