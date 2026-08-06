using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z1 — The Companion Card. The Trainer Card recipe applied to <i>her</i>: portrait, identity
    /// stack, status pills, quick actions, XP, and the relationship constellation along the bottom.
    ///
    /// <para>Design invariants the view relies on:</para>
    /// <list type="bullet">
    ///   <item>The hero stays fully alive for a logged-out / free user — barks are free. Only the
    ///   chat surface locks. The page must never look broken.</item>
    ///   <item><see cref="IsCompanionEnabled"/> false is the "she's asleep — wake her?" state:
    ///   portrait desaturates, the wake affordance appears, everything else stays readable.</item>
    ///   <item><see cref="IsMoodLive"/> false (pre-Train 4) renders the sleeping-moon token at 40%
    ///   with an in-character tooltip — not a hidden control, not a gray box.</item>
    /// </list>
    /// </summary>
    public interface ICompanionHeroCardVm : INotifyPropertyChanged
    {
        // ---- identity ----
        string Name { get; }
        /// <summary>Active mod display name for the gradient chip ("Bambi Sleep").</summary>
        string ModName { get; }
        /// <summary>The existing one-line companion description.</summary>
        string Flavor { get; }
        /// <summary>Companion bust. Null renders the gradient placeholder disc.</summary>
        ImageSource? Portrait { get; }

        // ---- state ----
        /// <summary>ChkAvatarEnabled. False = the asleep hero.</summary>
        bool IsCompanionEnabled { get; }
        /// <summary>Provider live — drives the presence dot (pink pulse) and the AI pill.</summary>
        bool IsAiLive { get; }
        /// <summary>Awareness on — drives the awareness pill's "eyes open / eyes closed".</summary>
        bool IsAwarenessOpen { get; }
        /// <summary>"Cloud — she's listening" / "Off — she's asleep".</summary>
        string AiPillText { get; }
        /// <summary>"Eyes open — broad strokes" / "Eyes closed".</summary>
        string AwarenessPillText { get; }
        /// <summary>In-character copy shown when she is disabled.</summary>
        string AsleepCopy { get; }

        // ---- daily mood token (Train 4) ----
        bool IsMoodLive { get; }
        /// <summary>Mood glyph, e.g. "✧". Pre-T4 the view substitutes the sleeping moon.</summary>
        string MoodGlyph { get; }
        /// <summary>Localized mood word, e.g. "bratty".</summary>
        string MoodWord { get; }
        /// <summary>Caption under the word ("today's mood") or the dormant tooltip copy.</summary>
        string MoodCaption { get; }

        // ---- progression ----
        int Level { get; }
        /// <summary>0..1 for the star-width XP fill.</summary>
        double XpFraction { get; }
        /// <summary>"341 / 550 XP".</summary>
        string XpLabel { get; }
        /// <summary>"next: Lv 42".</summary>
        string NextLevelLabel { get; }

        // ---- quick actions ----
        /// <summary>Hint text on the chat chip — the live shortcut, whose editor lives in Z8.</summary>
        string ChatShortcutHint { get; }
        bool IsMuted { get; }
        bool IsCompanionShown { get; }

        ICommand ChatCommand { get; }
        ICommand SwitchCommand { get; }
        ICommand DetachCommand { get; }
        ICommand ToggleMuteCommand { get; }
        ICommand ToggleShownCommand { get; }
        /// <summary>AI pill deep-link: expands the Engine Room and brings it into view.</summary>
        ICommand OpenEngineRoomCommand { get; }
        /// <summary>Awareness pill deep-link: focuses "What she can see".</summary>
        ICommand FocusAwarenessCommand { get; }
        /// <summary>The wake affordance in the asleep state.</summary>
        ICommand WakeCommand { get; }

        /// <summary>The constellation band along the hero's bottom edge.</summary>
        IRelationshipConstellationVm Constellation { get; }
    }
}
