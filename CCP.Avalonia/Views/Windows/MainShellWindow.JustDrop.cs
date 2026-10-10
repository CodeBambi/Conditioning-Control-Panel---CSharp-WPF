// PORTED from ConditioningControlPanel/MainWindow/MainWindow.JustDrop.cs (7.1.5), plus the three window
// keys ShowTab intercepts there (MainWindow.TabNavigation.cs:245-272, MainWindow.Lab.cs OpenFypFeed):
// "fyp" (the For You feed), "justdrop" (the shop) and "webapp" (the web app in the browser).
//
// Whether the Just Drop door exists for this account, and telling the app when that changes. Withheld
// by default: nothing can make the door appear except JustDropService.DoorAvailable turning true, a
// per-launch server GET with a false default and no persistence. Just Drop is for everyone (Tier 0):
// there is no tier check anywhere on this path.
//
// The refusals live at the one door every caller comes through (ShowTab -> OpenWindowKey): the Premium
// cards, the Play card, the Home mystery tile, the tease tile, the palette rows and EMI's ring.
//
// not ported: the Session door's Takeaway shelf (MainShellWindow.Takeaway.cs is still a note, so the
// "order a drop" card and the replay cards have nowhere to draw; JustDropHostService.LaunchReplay is
// ready for them).

using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.JustDrop;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.

        /// <summary>Behind the marquee (3 s), the update banner (5 s) and the announcement (7 s):
        /// startup has enough to do, and this is a page the user has to walk to.</summary>
        internal const int JustDropCheckDelayMs = 9000;

        private bool _justDropDoorHooked;
        private static bool _justDropCheckKicked;

        /// <summary>Test seam: the one server check (default: after the delay, never in a sandbox).</summary>
        internal static Func<Task> JustDropKickoff { get; set; } = async () =>
        {
            if (SandboxNet.Active) return;
            await Task.Delay(JustDropCheckDelayMs).ConfigureAwait(false);
            await JustDropService.RefreshAvailabilityAsync().ConfigureAwait(false);
        };

        /// <summary>Test seam: the "webapp" key's door (default: the Play card's, the system browser).</summary>
        internal static Action<MainShellWindow>? WebAppDoor { get; set; }

        /// <summary>The Core doors' window halves on this head. Idempotent.</summary>
        internal static void SeedWindowDoors()
        {
            SettingsPaletteIndex.JustDropDoorAvailableProvider = () => JustDropService.DoorAvailable;
            Models.ExclusiveFeature.JustDropDoorProvider = SettingsPaletteIndex.JustDropDoorAvailable;   // as App.axaml.cs seeds it
            JustDropHostService.LaunchShopProvider = () => GameWindow.LaunchJustDropShop();
            JustDropHostService.LaunchReplayProvider = code => GameWindow.LaunchJustDropReplay(code);
            JustDropHostService.IsActiveProvider = GameWindow.IsJustDropOpen;
            JustDropHostService.CloseActiveProvider = GameWindow.CloseJustDrop;
            FypHostService.LaunchProvider = () => GameWindow.Launch(GameWindow.FypId);
            FypHostService.IsActiveProvider = GameWindow.IsFypOpen;
            FypHostService.IsGhostedProvider = GameWindow.IsFypGhostedAny;
            FypHostService.CloseProvider = GameWindow.CloseFyp;
        }

        /// <summary>
        /// Subscribes to the availability flag and kicks the one server check. The subscription is
        /// what lets the door appear mid-session when the answer lands, without a restart.
        /// </summary>
        private void InitializeJustDropDoor()
        {
            try
            {
                SeedWindowDoors();

                if (!_justDropDoorHooked)
                {
                    JustDropService.AvailabilityChanged += OnJustDropAvailabilityChanged;
                    // A static event holding a dead window is the leak that survives a refactor unnoticed.
                    Closed += (_, _) => JustDropService.AvailabilityChanged -= OnJustDropAvailabilityChanged;
                    _justDropDoorHooked = true;
                }

                if (_justDropCheckKicked) return;
                _justDropCheckKicked = true;
                _ = Task.Run(async () =>
                {
                    try { await JustDropKickoff().ConfigureAwait(false); }
                    catch (Exception ex) { Log.Debug("JustDrop: availability kickoff failed: {E}", ex.Message); }
                });
            }
            catch (Exception ex)
            {
                // A door that fails to initialise stays hidden, which is the shipped default.
                Log.Warning(ex, "InitializeJustDropDoor failed; the door stays hidden");
            }
        }

        private void OnJustDropAvailabilityChanged(object? sender, EventArgs e)
        {
            if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => OnJustDropAvailabilityChanged(sender, e));
                return;
            }
            try { ApplyJustDropDoorVisibility(); }
            catch (Exception ex) { Log.Warning(ex, "OnJustDropAvailabilityChanged failed"); }
        }

        /// <summary>
        /// Fans the flag out to the surfaces that render it: the Premium shelf's card (hidden, not
        /// veiled, while the door is shut). The palette row and EMI's ring read the flag when they open.
        /// </summary>
        private void ApplyJustDropDoorVisibility()
        {
            try
            {
                RefreshExclusivesTab();
                Log.Debug("JustDrop door visibility -> {State}", JustDropService.DoorAvailable ? "visible" : "hidden");
            }
            catch (Exception ex) { Log.Warning(ex, "ApplyJustDropDoorVisibility failed"); }
        }

        /// <summary>
        /// WPF ShowTab's three intercepts: a key that opens a window (or the browser) and leaves the
        /// tab on screen alone. Each destination refuses in its own words; the caller never decides access.
        /// </summary>
        internal void OpenWindowKey(string tab)
        {
            switch (tab)
            {
                case "fyp":
                    NoteDestinationOpened(tab);   // WPF: a window key counts as an open on the RECENT rail
                    OpenFypFeed();
                    break;
                case "justdrop":
                    // The withheld refusal: a no-op, never a redirect, and before anything announces the door.
                    if (!JustDropService.DoorAvailable)
                    {
                        Log.Debug("ShowTab(justdrop) ignored - the door is not available on this account");
                        return;
                    }
                    NoteDestinationOpened(tab);
                    try { SeedWindowDoors(); JustDropHostService.LaunchShop(); }
                    catch (Exception ex) { Log.Warning(ex, "Just Drop failed to open"); }
                    break;
                case "webapp":
                    // WPF OpenWebAppFromNav: the browser, and the banner beat retires.
                    if (WebAppDoor is { } door) door(this); else OpenPlayWebApp();
                    break;
            }
        }

        /// <summary>
        /// WPF OpenFypFeed: the For You feed window. The card never blocks, so premium is enforced
        /// at the door, out loud (TierGate raises the refusal naming the feature; today's free day opens it).
        /// </summary>
        internal void OpenFypFeed()
        {
            try
            {
                SeedWindowDoors();
                // The gate is the window's own (GameWindow.FypGate: DemandPremium(tab_fyp, "fyp")), so
                // every route to the feed answers to it, this one included.
                FypHostService.Launch();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OpenFypFeed failed");
                _ = Dialogs.MessageDialog.ShowAsync(this, Loc.Get("tab_fyp"), ex.Message);
            }
        }
    }
}
