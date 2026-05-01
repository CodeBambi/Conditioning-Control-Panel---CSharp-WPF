using System;

namespace ConditioningControlPanel.Services
{
    public static class TutorialEventBus
    {
        public static event EventHandler<string>? Event;

        public static string? LastSavedEnhancementPath { get; set; }

        // Set when an interactive Deeper tutorial finishes its in-dialog Part 1
        // and the editor should pick up Part 2 once it's loaded. Replaces the
        // older boolean StartHTPart2OnEditorLoad flag - typed as a TutorialType
        // so the same dispatch handles HT, Local Audio, and Local Video.
        // The dialog only sets this AFTER its own validation passes (so an
        // empty-source Create click doesn't strand a Part 2 flag forever).
        public static TutorialType? PendingPart2Tutorial { get; set; }

        public static void Emit(string name)
        {
            try { Event?.Invoke(null, name); } catch { }
        }
    }
}
