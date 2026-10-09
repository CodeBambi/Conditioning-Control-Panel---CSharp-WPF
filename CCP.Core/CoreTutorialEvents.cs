using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The tutorial event bus: WPF ConditioningControlPanel/Services/TutorialEventBus.cs, under a Core
    /// name (the WPF head references Core and keeps its own TutorialEventBus in the same namespace).
    /// Windows emit named moments ("EffectAdded", "RuleAdded", "FileSaved",
    /// "WindowLoaded:DeeperEditorWindow"); the tutorial overlay advances a step whose
    /// <see cref="CoreTutorial.Step.Advance"/> is OnEvent and whose
    /// <see cref="CoreTutorial.Step.AdvanceEventName"/> matches.
    /// </summary>
    public static class CoreTutorialEvents
    {
        public static event EventHandler<string>? Event;

        /// <summary>The path the Deeper editor last saved, for the HT walkthrough's follow-up card.</summary>
        public static string? LastSavedEnhancementPath { get; set; }

        /// <summary>Set by the New Enhancement dialog after its own validation passes: the tour
        /// (by name) the editor should continue as Part 2 once it has loaded. WPF typed it as a
        /// TutorialType; the seam takes names (CoreTutorial.Start(string)).</summary>
        public static string? PendingPart2Tutorial { get; set; }

        public static void Emit(string name)
        {
            try { Event?.Invoke(null, name); } catch { }
        }
    }
}
