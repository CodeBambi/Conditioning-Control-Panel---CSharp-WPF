using System;

namespace ConditioningControlPanel.Services
{
    public static class TutorialEventBus
    {
        public static event EventHandler<string>? Event;

        public static string? LastSavedEnhancementPath { get; set; }

        // Set when the user starts the HT interactive tutorial; consumed by
        // DeeperEditorWindow.Loaded to start Part 2 with a fresh overlay scoped
        // to the editor. Splitting the tutorial across two overlays sidesteps
        // the cross-window state machine entirely.
        public static bool StartHTPart2OnEditorLoad { get; set; }

        public static void Emit(string name)
        {
            try { Event?.Invoke(null, name); } catch { }
        }
    }
}
