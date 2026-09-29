using System;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The settings half of "the mod the user picked becomes active once its pack is on disk":
    /// the persisted choice and the rules on it. The head owns the activator (WPF:
    /// <c>PendingModActivation</c> + <c>MainWindow.ActivateChosenMod</c>).
    /// </summary>
    public static class PendingModChoice
    {
        /// <summary>Record only a real change: a pick that is already the active mod is not pending.</summary>
        public static bool ShouldRecord(string? chosenModId, string? activeModId)
            => !string.IsNullOrWhiteSpace(chosenModId)
               && !string.Equals(chosenModId, activeModId, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when an availability signal (mod id or its pack id) is about the pending mod.</summary>
        public static bool Matches(string? pendingModId, string? modOrPackId)
        {
            if (string.IsNullOrWhiteSpace(pendingModId) || string.IsNullOrWhiteSpace(modOrPackId))
                return false;
            if (string.Equals(pendingModId, modOrPackId, StringComparison.OrdinalIgnoreCase))
                return true;

            var packId = ModService.PackIdForMod(pendingModId!);
            return !string.IsNullOrEmpty(packId)
                   && string.Equals(packId, modOrPackId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldActivate(string? pendingModId, string? activeModId, bool contentAvailable)
            => contentAvailable
               && !string.IsNullOrWhiteSpace(pendingModId)
               && !string.Equals(pendingModId, activeModId, StringComparison.OrdinalIgnoreCase);

        /// <summary>The persisted pending mod id, or null.</summary>
        public static string? Pending
        {
            get
            {
                var id = CoreSettings.Service?.Current?.PendingModActivationId;
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }
        }

        public static void Record(string modId, string? activeModId)
        {
            try
            {
                var settings = CoreSettings.Service?.Current;
                if (settings == null) return;

                if (!ShouldRecord(modId, activeModId))
                {
                    Clear("the chosen mod is already the active one");
                    return;
                }

                settings.PendingModActivationId = modId;
                // SaveImmediate, not the 500ms-debounced Save: the user just committed to a
                // 77-345MB download and may kill the app right after - the choice must survive.
                CoreSettings.Service?.SaveImmediate();
                Log.Information(
                    "[ModPicker] {ModId} chosen - it becomes the active mod as soon as its content is on disk", modId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModPicker] Could not record the chosen mod");
            }
        }

        public static void Clear(string reason)
        {
            try
            {
                var settings = CoreSettings.Service?.Current;
                if (settings == null || string.IsNullOrWhiteSpace(settings.PendingModActivationId)) return;

                Log.Debug("[ModPicker] Dropping the pending activation of {ModId}: {Reason}",
                    settings.PendingModActivationId, reason);
                settings.PendingModActivationId = "";
                CoreSettings.Service?.Save();
            }
            catch (Exception ex)
            {
                Log.Debug("[ModPicker] Could not clear the pending activation: {Error}", ex.Message);
            }
        }

        /// <summary>A mod with no pack is always available; a pack mod needs its pack installed.</summary>
        public static bool IsContentAvailable(string modId, ReleaseContentService? svc)
        {
            try
            {
                var packId = ModService.PackIdForMod(modId);
                if (string.IsNullOrEmpty(packId)) return true;

                if (svc == null) return false;
                return svc.IsFullInstall || svc.IsInstalled(packId!);
            }
            catch (Exception ex)
            {
                Log.Debug("[ModPicker] Availability check for {ModId} failed: {Error}", modId, ex.Message);
                return false;
            }
        }
    }
}
