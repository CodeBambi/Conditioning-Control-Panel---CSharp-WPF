using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z0 — the header band that sits above the Companion Card: title, subtitle, the tutorial chip
    /// and the AI-entitlement plate.
    ///
    /// <para>It rides on <see cref="ICompanionHeroCardVm.Header"/> rather than on its own control so
    /// the hero package ships one composed unit. That property has a default implementation of
    /// <c>null</c>, and a null header collapses the band — which is exactly what a host that draws
    /// its own page header wants.</para>
    ///
    /// <para>Entitlement is a <i>glance</i>, never a wall: when <see cref="HasAiAccess"/> is false the
    /// plate dims and a Velvet-Vault teaser ribbon appears beside it (design §3 Z0). The band never
    /// blocks anything — the ribbon is a button to the Patreon tab and nothing more.</para>
    /// </summary>
    public interface ICompanionHeaderVm : INotifyPropertyChanged
    {
        /// <summary>"Companion" — the tab title, Fredoka + pink glow.</summary>
        string Title { get; }

        /// <summary>"her room — everything she is, knows, and notices".</summary>
        string Subtitle { get; }

        /// <summary>Label on the tutorial chip (the existing BtnCompanionTutorial).</summary>
        string TutorialLabel { get; }

        ICommand TutorialCommand { get; }

        /// <summary>The tab-level "is AI unlocked?" glance. False dims the plate and shows the ribbon.</summary>
        bool HasAiAccess { get; }

        /// <summary>The entitlement plate's text, e.g. "LAB · AI".</summary>
        string AiPlateLabel { get; }

        /// <summary>The always-dim plate for the tier above, e.g. "PRIME".</summary>
        string NextTierPlateLabel { get; }

        /// <summary>Free-tier ribbon copy, e.g. "unlock her voice". Shown only when not entitled.</summary>
        string TeaserRibbonLabel { get; }

        /// <summary>Plate / ribbon click-through. The design's one job for this band.</summary>
        ICommand OpenPatreonCommand { get; }
    }
}
