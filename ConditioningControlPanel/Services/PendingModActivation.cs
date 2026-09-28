using System;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The mod a user explicitly chose in the first-run <c>ModPickerDialog</c>, held until its content
    /// is actually on disk. The picker only ever REQUESTED the download, so a first-run user picked
    /// (say) Bambi Sleep, waited out a few hundred MB, closed the picker — and was still on CCP
    /// Default, which has no idle bark rules, so the companion looked broken.
    ///
    /// The choice is persisted (<see cref="Models.AppSettings.PendingModActivationId"/>) so a restart
    /// mid-download still honours it, and is dropped the moment the user switches mods by hand — a
    /// manual choice always outranks a queued one. ONLY a mod ticked in the picker is ever recorded;
    /// nothing here infers a choice.
    ///
    /// Activation itself is MainWindow's ApplyActiveModChange path — the same one the top-bar combo
    /// and the Mod Manager use. This type only decides WHEN.
    /// </summary>
    internal static class PendingModActivation
    {
        /// <summary>Which of the three timings applied the choice. Support reads this out of the log.</summary>
        internal enum Trigger
        {
            /// <summary>The content was already on disk when the user chose it.</summary>
            Immediate,

            /// <summary>The pack finished installing (picker open or long since closed).</summary>
            PackArrived,

            /// <summary>The download completed while the app was closed; honoured on the next launch.</summary>
            RestartResume
        }

        private static bool _hooked;

        /// <summary>
        /// The window that hosts the switch. Handed in by <see cref="Attach"/> rather than read from
        /// Application.Current.MainWindow, which is the SPLASH while the main window is being created
        /// and null whenever the app is hidden to tray.
        /// </summary>
        private static MainWindow? _window;

        // ---- rules and state: PendingModChoice in Core ----

        internal static bool ShouldRecord(string? chosenModId, string? activeModId)
            => PendingModChoice.ShouldRecord(chosenModId, activeModId);

        internal static bool Matches(string? pendingModId, string? modOrPackId)
            => PendingModChoice.Matches(pendingModId, modOrPackId);

        internal static bool ShouldActivate(string? pendingModId, string? activeModId, bool contentAvailable)
            => PendingModChoice.ShouldActivate(pendingModId, activeModId, contentAvailable);

        internal static string? Pending => PendingModChoice.Pending;

        internal static void Record(string modId) => PendingModChoice.Record(modId, App.Mods?.ActiveModId);

        internal static void Clear(string reason) => PendingModChoice.Clear(reason);

        internal static bool IsContentAvailable(string modId)
            => PendingModChoice.IsContentAvailable(modId, App.ReleaseContent);

        // ---- wiring ----

        /// <summary>
        /// Starts listening for the chosen mod's content arriving and honours a choice whose download
        /// finished while the app was closed. Idempotent; called from MainWindow's Loaded handler, so
        /// the window this ultimately repaints is guaranteed to exist.
        /// </summary>
        internal static void Attach(MainWindow window)
        {
            try
            {
                _window = window;
                if (App.Mods == null) return;

                if (!_hooked)
                {
                    _hooked = true;
                    App.Mods.ModAvailabilityChanged += OnModAvailabilityChanged;
                }

                ApplyIfReady(Trigger.RestartResume);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ModPicker] Could not arm the pending mod activation");
            }
        }

        private static void OnModAvailabilityChanged(object? sender, string modOrPackId)
        {
            // Raised off pack installs and background extractions - never throw back into them.
            try
            {
                if (!Matches(Pending, modOrPackId)) return;
                ApplyIfReady(Trigger.PackArrived);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ModPicker] Pending activation handler failed");
            }
        }

        /// <summary>
        /// Activates the pending choice once its content is on disk. Safe from any thread and at any
        /// point in the session: it marshals, checks for shutdown, and no-ops when there is nothing
        /// pending, no MainWindow, or the bytes have not landed yet.
        /// </summary>
        internal static void ApplyIfReady(Trigger trigger)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                if (!dispatcher.CheckAccess())
                {
                    // Normal, never Loaded: Loaded-priority work is starved in this app and silently
                    // never runs.
                    dispatcher.BeginInvoke(new Action(() => ApplyIfReady(trigger)), DispatcherPriority.Normal);
                    return;
                }

                var pending = Pending;
                if (pending == null) return;

                var activeModId = App.Mods?.ActiveModId;
                if (string.Equals(pending, activeModId, StringComparison.OrdinalIgnoreCase))
                {
                    Clear("it is already the active mod");
                    return;
                }

                if (!ShouldActivate(pending, activeModId, IsContentAvailable(pending))) return;

                // Prefer the live window: _window is a static ref that survives a window
                // re-creation, so a stale unloaded instance must lose to App.MainWindowRef.
                var window = (_window is { IsLoaded: true }) ? _window : (App.MainWindowRef ?? _window);
                if (window == null) return;   // pre-window pack install: the next signal (or launch) applies it

                window.ActivateChosenMod(pending, trigger);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ModPicker] Pending activation failed");
            }
        }
    }
}
