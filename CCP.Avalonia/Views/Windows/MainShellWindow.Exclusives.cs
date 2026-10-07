// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.Exclusives.cs (1296 lines).
//
// ponytail: stub except RefreshExclusivesTab (the roster/gate repaint now lives in ExclusivesTabView). Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// Members dropped (48):
//   private sealed class ExclusiveCardUi
//   private readonly List<ExclusiveCardUi> _exclusiveCards
//   private readonly List<TextBlock> _exclusiveTeaserMarks
//   private readonly List<Border> _exclusiveTeaserCards
//   private readonly List<(…)
//   private readonly List<DropShadowEffect> _exclusiveAccentShadows
//   private bool _exclusivesBuilt
//   private bool _exclusivesSheenRetryQueued
//   private Storyboard? _spotFreeFx
//   private static readonly FontFamily FredokaFont
//   private static readonly Color FreeTodayGold
//   private static SolidColorBrush Freeze(…)
//   private static Color VaultAccent(…)
//   private const byte TeaserMarkAlpha
//   private const double VaultPartnerHueShift
//   internal static Color VaultPartner(…)
//   private static Color VaultPartner(…)
//   internal static Color ShiftHue(…)
//   private static SolidColorBrush ExclusiveEdgeDefault(…)
//   private static SolidColorBrush SpotlightEdgeDefault(…)
//   private LinearGradientBrush AccentGradient(…)
//   private DropShadowEffect AccentTitleShadow(…)
//   private static bool IsExclusiveFreeToday(…)
//   internal void OpenExclusiveSpotlight(…)
//   private void EnsureExclusivesBuilt(…)
//   private void ApplySpotlightArt(…)
//   private Border BuildExclusiveCard(…)
//   private Border BuildComingSoonCard(…)
//   private static string TeaserEmoji(…)
//   private static void OnExclusiveCardHover(…)
//   internal void RefreshExclusivesTab(…)
//   private void RetintVaultChrome(…)
//   private static void TintShadow(…)
//   private static void TintGradient(…)
//   private void ApplyExclusiveCardState(…)
//   private static void ApplyVeilLockBreath(…)
//   private static Storyboard? ApplyFreeTodayPulse(…)
//   private void RefreshExclusiveTierPlates(…)
//   private void StartExclusivesMotion(…)
//   private void StopExclusivesMotion(…)
//   private int _exclusivesSheenRetries
//   private void RestartExclusiveSheens(…)
//   private void AttachExclusiveSheens(…)
//   private static string ExclusiveTitle(…)
//   private static string ExclusiveArtPath(…)
//   private const int ExclusiveCardDecodeWidth
//   private const int ExclusiveHeroDecodeWidth
//   private static ImageSource? LoadPackImage(…)

using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.

        /// <summary>WPF RefreshExclusivesTab, for the entitlement events (tier change, day rollover).
        /// The body lives in ExclusivesTabView.RefreshVault.</summary>
        internal void RefreshExclusivesTab()
        {
            Named<Tabs.ExclusivesTabView>("ExclusivesTab")?.RefreshVault();
            RefreshInvites();
        }

        /// <summary>WPF OpenExclusiveFeature (main 2e9080399): each card opens the door the launcher or the Play
        /// wall uses, so the card never decides access; the door's own gate refuses. Doors with no host on this
        /// head never get here (ExclusivesTabView.IsOnThisBuild).</summary>
        internal void OpenExclusiveFeature(string key)
        {
            switch (key)
            {
                case "gazeminigame": Named<Tabs.PlayTabView>("PlayTab")?.OpenGazeMinigame(); break;
                case "focusgaze":
                    // A switch on the Play wall, not a window: go there and show it.
                    ShowTab("play");
                    if (Named<Tabs.PlayTabView>("PlayTab")?.FindControl<Control>("SlotFocusGaze") is { } slot)
                        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => slot.BringIntoView());
                    break;
                default: ShowTab(key); break;
            }
        }

        /// <summary>WPF _invitePanel.RefreshAsync(): throttled inside, so a repaint storm costs one read.</summary>
        internal void RefreshInvites() => _ = Named<Tabs.ExclusivesTabView>("ExclusivesTab")
            ?.FindControl<Controls.Invites.InvitePanel>("InvitesHost")?.RefreshAsync();
    }
}
