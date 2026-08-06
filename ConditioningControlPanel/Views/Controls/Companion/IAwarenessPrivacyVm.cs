using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// One app the card offers as a one-click action: "hide this from her" on the seen-app row,
    /// "forget this" on the row of apps she is actually counting.
    ///
    /// <para>Zero-typing entry is the whole point (doc 02 §6.1). Process names are not app names, and
    /// nobody knows offhand that Teams is "ms-teams" — the same reason the Triggers tab grew its seen-app
    /// chips, whose in-memory ring this reuses. Apps only: a chip that offered a TITLE would put the
    /// thing the panel promises to keep local into a list the user can share a screenshot of.</para>
    /// </summary>
    public interface IAwarenessAppChipVm
    {
        /// <summary>The app as the user would recognise it.</summary>
        string Label { get; }

        /// <summary>What pressing it does, in one short phrase. Also the tooltip.</summary>
        string ActionTip { get; }

        /// <summary>The chip's single action.</summary>
        ICommand ActionCommand { get; }
    }

    /// <summary>
    /// Z5 — What she can see. The privacy inversion given a glass-walled surface. Design goal:
    /// capability visible, never a surprise.
    ///
    /// <para>The wire view is the flagship trust element: a monospace line showing <i>exactly</i>
    /// the projected ContextFrame and nothing else, with the real cloud JSON one click underneath
    /// (<see cref="WireJson"/>). When her eyes are closed it grays out and says so rather than
    /// disappearing.</para>
    ///
    /// <para>Everything below the wire view is the undo: the deny list, the per-app title allow list,
    /// the apps she is counting with a forget button each, the retention choice, the hour-long pause and
    /// the total wipe. A joke about a habit the user cannot see or erase crosses from funny to
    /// surveillance, so these are not settings — they are the feature.</para>
    ///
    /// <para><see cref="IsEverythingAvailable"/> false is the pre-Train-2 bridge: only the Off and
    /// Broad-strokes stops are live (they drive today's single toggle) and the wire view shows the
    /// shimmer placeholder instead of a frame.</para>
    /// </summary>
    public interface IAwarenessPrivacyVm : INotifyPropertyChanged
    {
        /// <summary>Two-way — the 3-stop segmented dial. Flipping past Off raises the consent card.</summary>
        AwarenessIntensity Intensity { get; set; }

        /// <summary>Train 2 landed: the third stop is selectable.</summary>
        bool IsEverythingAvailable { get; }

        /// <summary>The live frame, e.g. "[ fun · Chrome · 22m ]".</summary>
        string WireLine { get; }
        /// <summary>False renders the frame grayed with "[ her eyes are closed ]".</summary>
        bool IsWireLive { get; }
        /// <summary>"this exact line — nothing more — is what she gets."</summary>
        string WireCaption { get; }
        /// <summary>"she'll start noticing things…" — the pre-Train-2 promise.</summary>
        string DormantCopy { get; }
        /// <summary>True while the zone is bridging (renders <see cref="DormantCopy"/>).</summary>
        bool IsDormant { get; }

        // ---------------------------------------------------------------------------------
        //  the wire, in full
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// The actual cloud projection of the last frame she cut — the bytes, not a description of
        /// them. Empty when no frame has been cut yet, in which case the card says that rather than
        /// rendering an invented one.
        /// </summary>
        string WireJson { get; }

        /// <summary>True when there is a real projection to show.</summary>
        bool HasWireJson { get; }

        /// <summary>The line shown instead of the JSON when nothing has been sent yet.</summary>
        string WireJsonEmptyCopy { get; }

        /// <summary>Two-way — whether the JSON block is expanded. Collapsed by default; this is a card.</summary>
        bool IsJsonExpanded { get; set; }

        /// <summary>"show the exact data ▾" / "hide it ▴".</summary>
        string JsonToggleLabel { get; }

        /// <summary>Expands / collapses <see cref="WireJson"/>.</summary>
        ICommand ToggleJsonCommand { get; }

        // ---------------------------------------------------------------------------------
        //  lists
        // ---------------------------------------------------------------------------------

        /// <summary>Seeded rows (password managers, banking, email) plus anything the user added.</summary>
        IReadOnlyList<IDenyChipVm> DenyList { get; }
        string AddDenyLabel { get; }

        /// <summary>The apps whose page title she is allowed to carry. Ships empty — that is the inversion.</summary>
        IReadOnlyList<IDenyChipVm> TitleAllowList { get; }
        string TitleAllowLabel { get; }

        /// <summary>Recently seen apps, offered as one-click "hide this from her".</summary>
        IReadOnlyList<IAwarenessAppChipVm> SeenApps { get; }
        string SeenAppsLabel { get; }

        /// <summary>Apps the ledger is actually counting, each with a forget button.</summary>
        IReadOnlyList<IAwarenessAppChipVm> KnownApps { get; }
        string KnownAppsLabel { get; }

        /// <summary>Two-way. Default OFF — the inverted default, made visible.</summary>
        bool AllowPageTitles { get; set; }
        /// <summary>"page titles: hidden" / "…: allowed per app".</summary>
        string PageTitlesLabel { get; }

        // ---------------------------------------------------------------------------------
        //  ledger controls
        // ---------------------------------------------------------------------------------

        /// <summary>Two-way — how long the counters are kept. The card offers 7 or 30.</summary>
        int RetentionDays { get; set; }

        /// <summary>"kept for 30 days, then deleted".</summary>
        string RetentionLabel { get; }

        /// <summary>"pause for an hour" / "paused — 43m left". Two states, one button.</summary>
        string PauseLabel { get; }

        /// <summary>True while a pause is running.</summary>
        bool IsPaused { get; }

        /// <summary>Pauses her for an hour, or lifts a running pause.</summary>
        ICommand PauseCommand { get; }

        /// <summary>
        /// Erases everything she has noticed. Bound through the view's two-step confirm, never straight
        /// to a button — this is the most destructive control on the card.
        /// </summary>
        ICommand WipeCommand { get; }
        string WipeLabel { get; }

        // ---------------------------------------------------------------------------------
        //  commands
        // ---------------------------------------------------------------------------------

        ICommand AddDenyCommand { get; }
        ICommand AllowPerAppCommand { get; }
        /// <summary>Deep-links to the Workshop's awareness cooldown sliders.</summary>
        ICommand FineTuningCommand { get; }
    }
}
