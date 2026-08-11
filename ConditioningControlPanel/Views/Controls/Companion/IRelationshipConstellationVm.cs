using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z1 bottom band — the Relationship Constellation, the retention spine. Five stage nodes on a
    /// gradient connector: New ▸ Warming ▸ Bestie ▸ Possessive ▸ Inevitable.
    ///
    /// <para>Ratchet design: the flavor line is deliberately vague and carries no numbers. The user
    /// is told she is warming up, never how many points that took.</para>
    ///
    /// <para><see cref="IsLive"/> false is the pre-Train-4 dormant state: names visible, all nodes
    /// faint outlines, one-shot shimmer sweep on load, and the promise copy underneath. No lock
    /// icon — this is not a paywall.</para>
    /// </summary>
    public interface IRelationshipConstellationVm : INotifyPropertyChanged
    {
        /// <summary>Train 4 landed. False = dormant outlines + promise copy.</summary>
        bool IsLive { get; }

        /// <summary>0..4. Meaningless while <see cref="IsLive"/> is false.</summary>
        int CurrentStage { get; }

        /// <summary>Always five entries, in order. Names are localized and mod-reflavourable.</summary>
        IReadOnlyList<IConstellationNodeVm> Nodes { get; }

        /// <summary>"she remembers small things now…" — the vague progress hint.</summary>
        string FlavorLine { get; }

        /// <summary>The highlighted clause inside the flavor line ("running jokes unlocked.").</summary>
        string FlavorAccent { get; }

        /// <summary>"you two have history — soon she'll start counting it." (dormant only).</summary>
        string DormantCopy { get; }

        /// <summary>Node click → the achievement-toast-styled stage card. Parameter is the node vm.</summary>
        ICommand NodeCommand { get; }
    }
}
