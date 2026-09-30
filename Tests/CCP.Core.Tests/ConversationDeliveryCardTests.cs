using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The model is told "a choice card appears" only when settings exist, asks are on AND the head
/// shows the card (WPF's App.Settings?.Current?.CompanionAsksEnabled == true, plus OfferForRequest).</summary>
[Collection(SessionStatics.Name)]
public sealed class ConversationDeliveryCardTests
{
    [Fact]
    public void CardFollowsOnlyWithSettingsAndAHeadThatShowsTheCard()
    {
        const string ask = "recommend me a video to watch";
        var (provider, shown) = (CoreSettings.ServiceProvider, ConversationDelivery.AskCardsShown);
        try
        {
            ConversationDelivery.AskCardsShown = () => true;
            CoreSettings.ServiceProvider = null;
            Assert.False(ConversationDelivery.CardFollows(ask));   // no settings => false, as WPF

            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.CompanionAsksEnabled = true;
            Assert.True(ConversationDelivery.CardFollows(ask));

            ConversationDelivery.AskCardsShown = null;              // a head that shows no card
            Assert.False(ConversationDelivery.CardFollows(ask));
        }
        finally { (CoreSettings.ServiceProvider, ConversationDelivery.AskCardsShown) = (provider, shown); }
    }
}
