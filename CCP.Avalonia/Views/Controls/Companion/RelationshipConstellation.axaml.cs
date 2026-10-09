using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Views.Controls.Companion;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z1 bottom band — the relationship constellation. See the XAML header for the visual spec.
    ///
    /// <para>WPF's code-behind fires a one-shot intro on Loaded: the dormant shimmer sweep (ported
    /// here as one DoubleTransition, like ChatThresholdView's) or, when live, the node twinkle -
    /// which the shipped dormant vm never reaches, so it is not ported.</para>
    /// </summary>
    public partial class RelationshipConstellation : UserControl
    {
        private bool _introPlayed;

        public RelationshipConstellation()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = RelationshipConstellationViewModel.Runtime();
            // One-shot: re-entering the tab must not stack a second sweep. Normal, never Loaded priority.
            Loaded += (_, _) =>
            {
                if (_introPlayed) return;
                _introPlayed = true;
                Dispatcher.UIThread.Post(PlayIntro, DispatcherPriority.Normal);
            };
        }

        /// <summary>True once the dormant sweep has been started (tests).</summary>
        internal bool ShimmerStarted { get; private set; }

        /// <summary>WPF PlayIntro: the dormant shimmer, once, from a loaded tree.</summary>
        public void PlayIntro()
        {
            if (!IsLoaded || ViewModel is not { IsLive: false }) return;
            var band = this.FindControl<Border>("DormantShimmer");
            var host = this.FindControl<Border>("DormantHost");
            if (band?.RenderTransform is not TransformGroup group) return;
            var shift = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (shift is null) return;

            // One-time Bounds read - a value, not a binding (WPF: ActualWidth + 90, else 620).
            double travel = host is { Bounds.Width: > 1 } ? host.Bounds.Width + 90 : 620;
            shift.Transitions = null;
            shift.X = -90;
            shift.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = TranslateTransform.XProperty,
                    Duration = TimeSpan.FromSeconds(1.4),
                    Delay = TimeSpan.FromSeconds(0.25),   // CmpShimmerSweepStoryboard BeginTime
                    Easing = new CubicEaseInOut()
                }
            };
            band.Opacity = 1;
            shift.X = travel;
            ShimmerStarted = true;
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public RelationshipConstellationViewModel? ViewModel
        {
            get => DataContext as RelationshipConstellationViewModel;
            set => DataContext = value;
        }
    }

    /// <summary>
    /// WPF <c>RelationshipConstellationRuntimeVm</c> (CompanionHeroRuntimeVm.cs:301-346) over CCP.Core's
    /// <see cref="ConstellationMath"/>. Train 4 owns the stages, so the shipped state is dormant: names
    /// visible (a mod may reflavor them), nodes outlined, the promise copy underneath. The node command
    /// is a no-op there too, which is why the node buttons carry none.
    /// </summary>
    public sealed class RelationshipConstellationViewModel
    {
        public RelationshipConstellationViewModel(bool isLive, int currentStage, string? modId = null)
        {
            IsLive = isLive;
            CurrentStage = ConstellationMath.ClampStage(currentStage);

            var nodes = new List<ConstellationNodeViewModel>(ConstellationMath.StageCount);
            for (int i = 0; i < ConstellationMath.StageCount; i++)
            {
                var state = ConstellationMath.StateFor(i, CurrentStage, isLive);
                nodes.Add(new ConstellationNodeViewModel
                {
                    Index = i,
                    Name = ResolveStage(i, modId),
                    // Mockup glyph ladder: reached ✦, here ★, still ahead ✧ (the runtime's only glyph).
                    Glyph = state switch { ConstellationNodeState.Filled => "✦", ConstellationNodeState.Current => "★", _ => "✧" },
                    Description = Loc.Get($"companion_stage_{i}_blurb"),
                    IsFilled = state == ConstellationNodeState.Filled,
                    IsCurrent = state == ConstellationNodeState.Current,
                });
            }
            Nodes = nodes;
        }

        /// <summary>Train 4 landed. False = dormant outlines + promise copy.</summary>
        public bool IsLive { get; }

        /// <summary>0..4. Meaningless while <see cref="IsLive"/> is false.</summary>
        public int CurrentStage { get; }

        /// <summary>ConstellationFillConverter.FillFraction: 0..1 along the node-centre span.</summary>
        public double FillFraction => IsLive ? CurrentStage / (double)(ConstellationMath.StageCount - 1) : 0.0;

        /// <summary>Always five entries, in order.</summary>
        public IReadOnlyList<ConstellationNodeViewModel> Nodes { get; }

        public string FlavorLine { get; init; } = Loc.Get("companion_constellation_flavor_new");
        public string FlavorAccent { get; init; } = Loc.Get("companion_constellation_flavor_new_accent");
        public string DormantCopy { get; init; } = Loc.Get("companion_constellation_dormant");

        /// <summary>What WPF's hero builds today: dormant, stage names reflavored by the active mod.</summary>
        public static RelationshipConstellationViewModel Runtime() => new(isLive: false, currentStage: 0, CoreMods.ActiveModId);

        /// <summary>A mod may reflavor a stage name; the base key is the fallback (WPF ResolveStage).</summary>
        private static string ResolveStage(int index, string? modId)
        {
            if (!string.IsNullOrEmpty(modId))
            {
                var modKey = ConstellationMath.StageKey(index, modId);
                var modName = Loc.Get(modKey);
                if (!string.Equals(modName, modKey, StringComparison.Ordinal)) return modName;
            }
            return Loc.Get(ConstellationMath.StageKey(index));
        }
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
