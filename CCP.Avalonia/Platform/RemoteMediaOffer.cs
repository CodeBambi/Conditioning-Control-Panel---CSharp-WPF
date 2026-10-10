using System;
using System.Threading;
using Avalonia.Controls;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// WPF App.OfferRemoteMediaSource (App.xaml.cs:279-340): when a local-only library comes up empty
/// (the "no videos" dialog), offer the online source once per launch through the "remotemedia"
/// intro card. It only OFFERS: nothing is fetched here, and the card's own switch is what sets
/// MediaSource and the consent every remote fetch checks.
/// </summary>
internal static class RemoteMediaOffer
{
    /// <summary>WPF RemoteMediaIntroKey.</summary>
    internal const string IntroKey = "remotemedia";

    private static int _claimed;

    /// <summary>The card. Tests swap it (RunsAlone).</summary>
    internal static Action<string, Window?> ShowCard = (key, owner) => Views.Windows.FeatureIntroPopup.ShowIfFirstTime(key, owner);

    /// <summary>The route. Tests swap it (RunsAlone).</summary>
    internal static Action<InboxItem> Present = StartupLadder.PresentOrInbox;

    /// <summary>Test seam: give the launch its one offer back.</summary>
    internal static void ResetForTests() => Interlocked.Exchange(ref _claimed, 0);

    /// <summary>True when the offer was handed to the presenter.</summary>
    internal static bool Offer(string surface, Window? owner = null)
    {
        try
        {
            // Already pointed at the remote pool: the dead end is a different problem (no niches
            // selected, no network) and a "try online media" card would be noise.
            if (!string.Equals(CoreSettings.Current.MediaSource, "local", StringComparison.OrdinalIgnoreCase)) return false;
            // Never stack on an update modal. ponytail: WPF parks the offer for a later flush; here
            // the budget is simply left unspent and the next dead end offers again.
            if (AppUpdater.IsUpdateDialogActive) return false;

            Log.Information("RemoteMedia: empty assets at {Surface} - offering the online source", surface);
            // Through the presenter: shown at once when nothing is quiet, parked as an Inbox row
            // inside the first-launch window. The once-per-launch claim lives in the open action: a
            // card that only ever became a row must not spend the launch's one offer.
            Present(new InboxItem
            {
                Key = "intro:remote-media",
                Glyph = "🌐",
                Title = "Media without the download",
                Summary = "Your folders are empty - she can stream from the online pool instead.",
                Open = () =>
                {
                    if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0) return;
                    ShowCard(IntroKey, owner);
                },
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RemoteMedia: offer failed at {Surface}", surface);
            return false;
        }
    }
}
