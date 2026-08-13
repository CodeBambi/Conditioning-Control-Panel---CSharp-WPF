using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Services.JustDrop;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Just Drop door's visibility, and nothing else.
    ///
    /// <para><b>Withheld by default.</b> <c>DoorJustDrop</c> and <c>DoorPanelJustDrop</c> ship
    /// <c>Collapsed</c> in MainWindow.xaml, so on a fresh install - and on every launch where the
    /// server does not answer - the rail below Library simply ends. Nothing here can make the door
    /// appear except <see cref="JustDropService.DoorAvailable"/> turning true, and nothing the user
    /// can edit in settings.json feeds that: the flag is a per-launch server GET with a false
    /// default and no persistence (see JustDropService for why the tier gates' 24h-cache posture
    /// would be wrong here).</para>
    ///
    /// <para>The refusal itself lives in <c>ShowTab</c>, not here. Hiding the rail rows stops the
    /// door being <i>found</i>; the guard at the top of ShowTab stops it being <i>reached</i> by
    /// the callers the rail does not own (bark rules, the dashboard tile, a deep link).</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Behind the marquee (3s), the update banner (5s) and the announcement (7s).
        /// Same reasoning: startup already has enough to do, and nothing about this door is
        /// urgent - it is a page the user has to walk to.</summary>
        private const int JustDropCheckDelayMs = 9000;

        private bool _justDropDoorHooked;

        /// <summary>
        /// Subscribes to the availability flag and kicks the one server check. Called once from
        /// the Loaded path; the subscription is what lets the door appear mid-session when the
        /// answer lands ~9s later, without the user having to restart.
        /// </summary>
        private void InitializeJustDropDoor()
        {
            try
            {
                // Paint whatever we already know first: DoorAvailable is false at this point on a
                // cold start, but this method is idempotent and a re-entry must not leave a
                // revealed door hidden.
                ApplyJustDropDoorVisibility();

                if (!_justDropDoorHooked)
                {
                    JustDropService.AvailabilityChanged += OnJustDropAvailabilityChanged;
                    // AvailabilityChanged is a STATIC event and this handler is an instance
                    // method, so the subscription roots the window for the life of the process.
                    // One MainWindow is only ever built today, but a static event holding a dead
                    // window is the shape of leak that survives a refactor unnoticed.
                    Closed += (_, __) => JustDropService.AvailabilityChanged -= OnJustDropAvailabilityChanged;
                    _justDropDoorHooked = true;
                }

                // Fire-and-forget with a delay. ContinueWith is deliberately avoided: the service
                // raises AvailabilityChanged on the dispatcher itself, so there is nothing to
                // marshal here and no callback that could outlive the window.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(JustDropCheckDelayMs).ConfigureAwait(false);
                        await JustDropService.RefreshAvailabilityAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("JustDrop: availability kickoff failed: {E}", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                // A door that fails to initialise stays hidden, which is the shipped default -
                // degraded, not broken.
                App.Logger?.Warning(ex, "InitializeJustDropDoor failed; the door stays hidden");
            }
        }

        /// <summary>The service raises this on the dispatcher, so no marshalling is needed - but
        /// the window can still be closing underneath it, hence the validity check.</summary>
        private void OnJustDropAvailabilityChanged(object? sender, EventArgs e)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                ApplyJustDropDoorVisibility();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "OnJustDropAvailabilityChanged failed"); }
        }

        /// <summary>
        /// The only place either rail row's Visibility is written.
        ///
        /// <para>The panel follows the header rather than being left Visible-at-Height-0 like the
        /// other shut doors: those are doors you can open, and a hit-testable strip belonging to a
        /// door with no header would be a row that does nothing. It is re-collapsed to Height 0 on
        /// reveal so the accordion starts from the same closed state every other door does.</para>
        /// </summary>
        private void ApplyJustDropDoorVisibility()
        {
            try
            {
                var available = JustDropService.DoorAvailable;
                var visibility = available ? Visibility.Visible : Visibility.Collapsed;

                if (DoorJustDrop != null) DoorJustDrop.Visibility = visibility;
                if (DoorPanelJustDrop != null)
                {
                    DoorPanelJustDrop.Visibility = visibility;
                    if (available && !string.Equals(_expandedDoor, "justdrop", StringComparison.Ordinal))
                    {
                        DoorPanelJustDrop.Height = 0;
                        DoorPanelJustDrop.IsHitTestVisible = false;
                    }
                }

                App.Logger?.Debug("JustDrop door visibility -> {State}", available ? "visible" : "hidden");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "ApplyJustDropDoorVisibility failed"); }
        }
    }
}
