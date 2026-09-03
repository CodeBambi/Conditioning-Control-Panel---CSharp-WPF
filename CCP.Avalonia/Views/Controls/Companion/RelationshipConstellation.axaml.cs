using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z1 bottom band — the relationship constellation. See the XAML header for the visual spec.
    ///
    /// <para>The WPF code-behind only fires the band's one-shot intro (node twinkle when live,
    /// shimmer sweep when dormant) from two keyed Storyboards. Those are not ported
    /// (CompanionTheme.axaml section 17), so neither is <c>PlayIntro</c>; the band is static
    /// here and the host that replays the intro on stage-up does not exist on this head yet.</para>
    /// </summary>
    public partial class RelationshipConstellation : UserControl
    {
        public RelationshipConstellation()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = RelationshipConstellationViewModel.Live();
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public RelationshipConstellationViewModel? ViewModel
        {
            get => DataContext as RelationshipConstellationViewModel;
            set => DataContext = value;
        }
    }

    /// <summary>
    /// The WPF head's MockRelationshipConstellationVm, minus the interface: IRelationshipConstellationVm,
    /// ConstellationMath and CompanionRelayCommand all still live in the WPF head
    /// (CompanionVmPrimitives.cs), so this carries the same placeholder states and copies the
    /// three lines of stage arithmetic rather than a second class hierarchy.
    /// </summary>
    public sealed class RelationshipConstellationViewModel
    {
        /// <summary>New ▸ Warming ▸ Bestie ▸ Possessive ▸ Inevitable. ConstellationMath.StageCount.</summary>
        public const int StageCount = 5;

        public RelationshipConstellationViewModel(bool isLive, int currentStage)
        {
            IsLive = isLive;
            CurrentStage = currentStage < 0 ? 0 : (currentStage >= StageCount ? StageCount - 1 : currentStage);

            var nodes = new List<ConstellationNodeViewModel>(StageCount);
            for (int i = 0; i < StageCount; i++)
            {
                // ConstellationMath.StateFor: dormant = every node Future.
                bool filled = isLive && i < CurrentStage;
                bool current = isLive && i == CurrentStage;
                nodes.Add(new ConstellationNodeViewModel
                {
                    Index = i,
                    // companion_stage_{i}: the same key path the shipped vm uses (mods reflavor
                    // via the _<modId> sibling keys, not wired here).
                    Name = Loc.Get($"companion_stage_{i}"),
                    // Mockup glyph ladder: reached ✦, here ★, still ahead ✧.
                    Glyph = filled ? "✦" : current ? "★" : "✧",
                    Description = Loc.Get($"companion_stage_{i}_blurb"),
                    IsFilled = filled,
                    IsCurrent = current,
                });
            }
            Nodes = nodes;
        }

        /// <summary>Train 4 landed. False = dormant outlines + promise copy.</summary>
        public bool IsLive { get; }

        /// <summary>0..4. Meaningless while <see cref="IsLive"/> is false.</summary>
        public int CurrentStage { get; }

        /// <summary>ConstellationFillConverter.FillFraction: 0..1 along the node-centre span.</summary>
        public double FillFraction => IsLive ? CurrentStage / (double)(StageCount - 1) : 0.0;

        /// <summary>Always five entries, in order.</summary>
        public IReadOnlyList<ConstellationNodeViewModel> Nodes { get; }

        public string FlavorLine { get; init; } = Loc.Get("companion_constellation_flavor");
        public string FlavorAccent { get; init; } = Loc.Get("companion_constellation_flavor_accent");
        public string DormantCopy { get; init; } = Loc.Get("companion_constellation_dormant");

        /// <summary>Stage 2 of 5, live — the artboard state.</summary>
        public static RelationshipConstellationViewModel Live() => new(isLive: true, currentStage: 2);

        /// <summary>Pre-Train 4: names visible, every node a faint outline, promise copy under.</summary>
        public static RelationshipConstellationViewModel Dormant() => new(isLive: false, currentStage: 0);
    }

    /// <summary>One node of the ladder. State is two bools so the XAML can bind them to classes.</summary>
    public sealed class ConstellationNodeViewModel
    {
        public int Index { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Glyph { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool IsFilled { get; init; }
        public bool IsCurrent { get; init; }
    }
}
