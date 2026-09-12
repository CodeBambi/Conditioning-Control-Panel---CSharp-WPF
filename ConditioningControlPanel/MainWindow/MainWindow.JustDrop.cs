using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Services.JustDrop;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Whether the Just Drop door exists for this account, and telling the app when that changes.
    ///
    /// <para><b>Withheld by default.</b> Nothing can make the door appear except
    /// <see cref="JustDropService.DoorAvailable"/> turning true, and nothing the user can edit in
    /// settings.json feeds that: the flag is a per-launch server GET with a false default and no
    /// persistence (see JustDropService for why the tier gates' 24h-cache posture would be wrong
    /// here).</para>
    ///
    /// <para>The shop is a window (<see cref="JustDropHostService"/>). Since 2026-09-11 (owner
    /// call: a creator tool, not a game) it is reached from one rail row, Studio > Creator Tools,
    /// plus the Exclusives shelf and the dashboard tease tile; this file shows and hides that row
    /// and fans the flag out to the other surfaces that render it.</para>
    ///
    /// <para>The refusal itself lives in <c>ShowTab</c>, which is also what launches the window -
    /// one gate, one entry point, every caller.</para>
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
        /// The only place the rail row's Visibility is written.
        ///
        /// <para>Collapsed rather than Height 0: a shut door's rows stay hit-testable at Height 0
        /// because you can open that door, and a row the account cannot open would be a row that
        /// does nothing. MeasureDoorPanel skips Collapsed children, so the Studio accordion's open
        /// height follows.</para>
        /// </summary>
        private void ApplyJustDropDoorVisibility()
        {
            try
            {
                var available = JustDropService.DoorAvailable;

                // The rail row (Studio > Creator Tools).
                if (BtnNavJustDrop != null)
                    BtnNavJustDrop.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

                // Then the one other surface that renders the flag. There were two until
                // 2026-09-12, when the dashboard's nameless tease tile became the Deeper editor
                // tile and stopped reading this flag at all.
                //
                // The Session door's Takeaway shelf ends in an "order a drop" card that only
                // exists while the door does. Rebuilt here rather than left to the next repaint:
                // a reveal that lands mid-session must reach every surface that reads the flag,
                // not just the ones the user happens to open next.
                RefreshTakeawayShelf();

                App.Logger?.Debug("JustDrop door visibility -> {State}", available ? "visible" : "hidden");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "ApplyJustDropDoorVisibility failed"); }
        }
    }
}
