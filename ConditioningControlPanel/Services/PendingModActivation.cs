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

        // ---- pure rules ----

        /// <summary>
        /// Worth remembering at all? Nothing to do for an empty choice or for the mod already running
        /// (the picker's "restore what I had" preselect is the common case of the latter).
        /// </summary>
        internal static bool ShouldRecord(string? chosenModId, string? activeModId)
            => !string.IsNullOrWhiteSpace(chosenModId)
               && !string.Equals(chosenModId, activeModId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Does an availability signal satisfy the pending choice? ModService raises
        /// <c>ModAvailabilityChanged</c> with a mod id, falling back to the pack id when the pack maps
        /// to no built-in mod, so both spellings have to match.
        /// </summary>
        internal static bool Matches(string? pendingModId, string? modOrPackId)
        {
            if (string.IsNullOrWhiteSpace(pendingModId) || string.IsNullOrWhiteSpace(modOrPackId))
                return false;
            if (string.Equals(pendingModId, modOrPackId, StringComparison.OrdinalIgnoreCase))
                return true;

            var packId = ModService.PackIdForMod(pendingModId!);
            return !string.IsNullOrEmpty(packId)
                   && string.Equals(packId, modOrPackId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The switch is due: there is a choice, its content has landed, and it is not already the
        /// active mod. A pending choice that is already active is satisfied, not pending — the caller
        /// clears it instead of switching.
        /// </summary>
        internal static bool ShouldActivate(string? pendingModId, string? activeModId, bool contentAvailable)
            => contentAvailable
               && !string.IsNullOrWhiteSpace(pendingModId)
               && !string.Equals(pendingModId, activeModId, StringComparison.OrdinalIgnoreCase);

        // ---- state ----

        /// <summary>The mod waiting to be activated, or null when there is nothing pending.</summary>
        internal static string? Pending
        {
            get
            {
                var id = App.Settings?.Current?.PendingModActivationId;
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }
        }

        /// <summary>Remembers a mod the user chose in the picker. Saved immediately — the download can outlive this session.</summary>
        internal static void Record(string modId)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;

                if (!ShouldRecord(modId, App.Mods?.ActiveModId))
                {
                    Clear("the chosen mod is already the active one");
                    return;
                }

                settings.PendingModActivationId = modId;
                App.Settings?.Save();
                App.Logger?.Information(
                    "[ModPicker] {ModId} chosen - it becomes the active mod as soon as its content is on disk", modId);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ModPicker] Could not record the chosen mod");
            }
        }

        /// <summary>Drops the pending choice (applied, superseded by a manual switch, or moot).</summary>
        internal static void Clear(string reason)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null || string.IsNullOrWhiteSpace(settings.PendingModActivationId)) return;

                App.Logger?.Debug("[ModPicker] Dropping the pending activation of {ModId}: {Reason}",
                    settings.PendingModActivationId, reason);
                settings.PendingModActivationId = "";
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[ModPicker] Could not clear the pending activation: {Error}", ex.Message);
            }
        }

        /// <summary>Is this mod's media actually on disk? A mod with no pack ships in the box.</summary>
        internal static bool IsContentAvailable(string modId)
        {
            try
            {
                var packId = ModService.PackIdForMod(modId);
                if (string.IsNullOrEmpty(packId)) return true;

                var svc = App.ReleaseContent;
                if (svc == null) return false;
                return svc.IsFullInstall || svc.IsInstalled(packId!);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[ModPicker] Availability check for {ModId} failed: {Error}", modId, ex.Message);
                return false;
            }
        }

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

                var window = _window ?? App.MainWindowRef;
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
