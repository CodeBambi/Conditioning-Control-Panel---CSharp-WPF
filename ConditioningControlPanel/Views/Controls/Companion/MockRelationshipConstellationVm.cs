using System.Collections.Generic;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IRelationshipConstellationVm"/>.
    /// Copy is the mockup's, verbatim, so a XAML change that breaks the layout is visible in the
    /// designer before anything is wired to a service.
    /// </summary>
    public sealed class MockRelationshipConstellationVm : CompanionObservable, IRelationshipConstellationVm
    {
        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockRelationshipConstellationVm() : this(isLive: true, currentStage: 2) { }

        public MockRelationshipConstellationVm(bool isLive, int currentStage)
        {
            IsLive = isLive;
            CurrentStage = ConstellationMath.ClampStage(currentStage);
            Nodes = BuildNodes(IsLive, CurrentStage);
            NodeCommand = CompanionRelayCommand.NoOp("constellation.node");
        }

        public bool IsLive { get; }
        public int CurrentStage { get; }
        public IReadOnlyList<IConstellationNodeVm> Nodes { get; }
        public ICommand NodeCommand { get; }

        public string FlavorLine { get; init; } =
            CompanionLocStaging.Resolve("companion_constellation_flavor");
        public string FlavorAccent { get; init; } =
            CompanionLocStaging.Resolve("companion_constellation_flavor_accent");
        public string DormantCopy { get; init; } =
            CompanionLocStaging.Resolve("companion_constellation_dormant");

        /// <summary>Stage 2 of 5, live — the artboard state.</summary>
        public static MockRelationshipConstellationVm Live() => new(isLive: true, currentStage: 2);

        /// <summary>Pre-Train 4: names visible, every node a faint outline, promise copy under.</summary>
        public static MockRelationshipConstellationVm Dormant() => new(isLive: false, currentStage: 0);

        /// <summary>Freshly met — the very first node is current, nothing is filled.</summary>
        public static MockRelationshipConstellationVm FreshlyMet() => new(isLive: true, currentStage: 0)
        {
            FlavorLine = CompanionLocStaging.Resolve("companion_constellation_flavor_new"),
            FlavorAccent = CompanionLocStaging.Resolve("companion_constellation_flavor_new_accent")
        };

        /// <summary>The end of the ratchet.</summary>
        public static MockRelationshipConstellationVm Inevitable() => new(isLive: true, currentStage: 4)
        {
            FlavorLine = CompanionLocStaging.Resolve("companion_constellation_flavor_final"),
            FlavorAccent = CompanionLocStaging.Resolve("companion_constellation_flavor_final_accent")
        };

        private static IReadOnlyList<IConstellationNodeVm> BuildNodes(bool isLive, int currentStage)
        {
            // Stage names come from companion_stage_0..4 through the staged loc layer, so this
            // mock exercises the same key path the shipped viewmodel uses (mods reflavor via the
            // _<modId> sibling keys).

            var list = new List<IConstellationNodeVm>(ConstellationMath.StageCount);
            for (int i = 0; i < ConstellationMath.StageCount; i++)
            {
                var state = ConstellationMath.StateFor(i, currentStage, isLive);
                list.Add(new CompanionConstellationNode
                {
                    Index = i,
                    Name = CompanionLocStaging.Resolve(ConstellationMath.StageKey(i)),
                    // Mockup glyph ladder: reached ✦, here ★, still ahead ✧.
                    Glyph = GlyphFor(state),
                    Description = CompanionLocStaging.Resolve($"companion_stage_{i}_blurb"),
                    State = state
                });
            }
            return list;
        }

        /// <summary>The mockup's three star glyphs, one per node state.</summary>
        public static string GlyphFor(ConstellationNodeState state) => state switch
        {
            ConstellationNodeState.Filled => "✦",
            ConstellationNodeState.Current => "★",
            _ => "✧"
        };
    }
}
