using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z6 — Her attention. The daily budget, elegant rather than scary.
    ///
    /// <para>Copy rules, from the design doc and doc 01 §5.4:</para>
    /// <list type="bullet">
    ///   <item>Never the word "tokens". The ladder is in chats and in her voice.</item>
    ///   <item>The floor is not mute: barks keep playing, and the card says so
    ///   ("her voice never runs out — only the thinking does").</item>
    ///   <item>The Patreon line appears below 40% only, once, quietly, in character.</item>
    ///   <item>The numeric detail is on demand — hover or click, never in the resting state.</item>
    /// </list>
    ///
    /// <para>The thresholds themselves live in <see cref="AttentionCopy"/> so they are testable and
    /// exist in exactly one place.</para>
    /// </summary>
    public interface IAttentionGaugeVm : INotifyPropertyChanged
    {
        /// <summary>0..1 of today's budget remaining.</summary>
        double Fraction { get; }

        /// <summary>
        /// What the bar draws — <see cref="AttentionCopy.BarFractionFor"/> keeps a sliver at zero
        /// so a spent meter reads as empty rather than broken.
        /// </summary>
        double BarFraction { get; }

        /// <summary>
        /// True when the budget is actually spent, as opposed to the bar merely drawing its 4%
        /// sliver. The view styles the empty bar off THIS, never off the sliver's value — 4 chats
        /// left out of 100 is not the same thing as none, and the two produce the same double.
        /// </summary>
        bool IsSpent { get; }

        /// <summary>The headline copy for the current rung of the ladder.</summary>
        string StateCopy { get; }

        /// <summary>"~63 chats left · resets at midnight". Numbers only — hover/click reveals it.</summary>
        string DetailLine { get; }

        /// <summary>
        /// The barks-only floor promise: "her voice never runs out — only the thinking does".
        ///
        /// <para>Its own property, and NOT part of <see cref="DetailLine"/>, because it is not
        /// detail-on-demand. The numbers are optional; this sentence is the card's answer to
        /// "am I being rationed?" and has to be readable at rest.</para>
        /// </summary>
        string FloorNote { get; }

        /// <summary>Whether <see cref="FloorNote"/> is shown at all — see AttentionCopy.ShowFloorNote.</summary>
        bool ShowFloorNote { get; }

        /// <summary>Two-way: the numeric detail is revealed on hover/click.</summary>
        bool IsDetailShown { get; set; }

        /// <summary>Below 40% only.</summary>
        bool ShowUpsell { get; }
        /// <summary>"want me louder? you know where the lab is~".</summary>
        string UpsellCopy { get; }

        ICommand UpsellCommand { get; }
        ICommand ToggleDetailCommand { get; }
    }
}
