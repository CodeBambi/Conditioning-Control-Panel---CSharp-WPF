using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z5 — What she can see. The privacy inversion given a glass-walled surface. Design goal:
    /// capability visible, never a surprise.
    ///
    /// <para>The wire view is the flagship trust element: a monospace line showing <i>exactly</i>
    /// the projected ContextFrame and nothing else. When her eyes are closed it grays out and says
    /// so rather than disappearing.</para>
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

        /// <summary>Seeded rows (password managers, banking, email) plus anything the user added.</summary>
        IReadOnlyList<IDenyChipVm> DenyList { get; }
        string AddDenyLabel { get; }

        /// <summary>Two-way. Default OFF — the inverted default, made visible.</summary>
        bool AllowPageTitles { get; set; }
        /// <summary>"page titles: hidden" / "…: allowed per app".</summary>
        string PageTitlesLabel { get; }

        ICommand AddDenyCommand { get; }
        ICommand AllowPerAppCommand { get; }
        /// <summary>Deep-links to the Workshop's awareness cooldown sliders.</summary>
        ICommand FineTuningCommand { get; }
    }
}
