using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IMemoryDiaryVm"/>.
    ///
    /// <para>This mock carries the wall's real projection behaviour (filter chip → sorted list via
    /// <see cref="FactOrdering.Project"/>) rather than a frozen array, because that behaviour is
    /// the part the builders will reuse and the part the unit tests pin down.</para>
    /// </summary>
    public sealed class MockMemoryDiaryVm : CompanionObservable, IMemoryDiaryVm
    {
        private readonly List<IMemoryFactVm> _all;
        private string _selectedFilterKey = "all";
        private IReadOnlyList<IMemoryFactVm> _facts;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockMemoryDiaryVm() : this(ArtboardFacts(), ArtboardStats()) { }

        public MockMemoryDiaryVm(List<IMemoryFactVm> facts, IReadOnlyList<IProfileStatVm> stats)
        {
            _all = facts;
            ProfileStats = stats;
            Filters = BuildFilters();
            _facts = FactOrdering.Project(_all, _selectedFilterKey);
            OpenStorageFolderCommand = CompanionRelayCommand.NoOp("memory.openFolder");
            ForgetEverythingCommand = CompanionRelayCommand.NoOp("memory.forgetAll");

            // The chips are radio-like: selecting one clears the rest and re-projects the wall.
            foreach (var f in Filters)
            {
                f.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(IFactFilterVm.IsSelected) || !f.IsSelected) return;
                    SelectedFilterKey = f.Key;
                };
            }
        }

        public string ProfileStripLabel { get; init; } = "SHE CAN SEE:";
        public IReadOnlyList<IProfileStatVm> ProfileStats { get; init; }

        /// <summary>Typed as the concrete list so the ctor can subscribe; the interface sees IFactFilterVm.</summary>
        public IReadOnlyList<CompanionFactFilter> Filters { get; }

        IReadOnlyList<IFactFilterVm> IMemoryDiaryVm.Filters => Filters;

        public IReadOnlyList<IMemoryFactVm> Facts
        {
            get => _facts;
            private set => Set(ref _facts, value);
        }

        public string SelectedFilterKey
        {
            get => _selectedFilterKey;
            set
            {
                if (!Set(ref _selectedFilterKey, value)) return;
                foreach (var f in Filters) f.IsSelected = string.Equals(f.Key, value, StringComparison.OrdinalIgnoreCase);
                Facts = FactOrdering.Project(_all, value);
            }
        }

        /// <summary>Only the dormant/ghost card left standing means the wall is empty.</summary>
        public bool IsEmpty
        {
            get
            {
                foreach (var f in _all) if (!f.IsDormant) return false;
                return true;
            }
        }

        public string EmptyCopy { get; init; } = "“tell me things and I'll keep them~”";
        public string StorageNote { get; init; } = "her memory lives on this machine only";
        public string StorageLinkLabel { get; init; } = "where?";
        public string ForgetEverythingLabel { get; init; } = "Forget everything…";

        public ICommand OpenStorageFolderCommand { get; }
        public ICommand ForgetEverythingCommand { get; }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>The artboard: five real facts plus the Train 4 promise card.</summary>
        public static MockMemoryDiaryVm Populated() => new();

        /// <summary>
        /// Fresh user. Never blank: the deterministic profile strip has content from minute one,
        /// and the wall shows the single ghost card.
        /// </summary>
        public static MockMemoryDiaryVm Empty() => new(
            new List<IMemoryFactVm>(),
            new IProfileStatVm[]
            {
                new CompanionProfileStat("Level 1"),
                new CompanionProfileStat("Streak 1 day"),
                new CompanionProfileStat("1 session")
            });

        /// <summary>Pre-Train 4: deterministic facts only, wall ends on the shimmer promise card.</summary>
        public static MockMemoryDiaryVm Dormant()
        {
            var facts = new List<IMemoryFactVm>
            {
                new CompanionMemoryFact
                {
                    Text = "First trance: 2026-03-02 — “the day we met.”",
                    KindKey = "moment",
                    KindLabel = "moment",
                    MetaLabel = "from the app · she brings this up on anniversaries",
                    IsPinned = true
                },
                DormantPromiseCard()
            };
            return new MockMemoryDiaryVm(facts, ArtboardStats());
        }

        /// <summary>Boundaries-only view — proves the steel rail and the always-first sort.</summary>
        public static MockMemoryDiaryVm BoundariesFilter()
        {
            var vm = Populated();
            vm.SelectedFilterKey = "boundary";
            return vm;
        }

        // ------------------------------- sample data -------------------------------

        private static IReadOnlyList<CompanionFactFilter> BuildFilters() => new[]
        {
            new CompanionFactFilter("all", "all", selected: true),
            new CompanionFactFilter("boundary", "boundaries"),
            new CompanionFactFilter("joke", "jokes"),
            new CompanionFactFilter("preference", "preferences"),
            new CompanionFactFilter("goal", "goals"),
            new CompanionFactFilter("moment", "moments")
        };

        private static IReadOnlyList<IProfileStatVm> ArtboardStats() => new IProfileStatVm[]
        {
            new CompanionProfileStat("Level 41"),
            new CompanionProfileStat("Streak 12 days"),
            new CompanionProfileStat("87 sessions"),
            new CompanionProfileStat("Archetype: Dreamer"),
            new CompanionProfileStat("Favorite: Flash")
        };

        private static List<IMemoryFactVm> ArtboardFacts() => new()
        {
            new CompanionMemoryFact
            {
                Text = "Never tease about chastity.",
                KindKey = "boundary",
                KindLabel = "boundary · always honored",
                MetaLabel = "set by you · 2026-07-30",
                IsBoundary = true
            },
            new CompanionMemoryFact
            {
                Text = "First trance: 2026-03-02 — “the day we met.”",
                KindKey = "moment",
                KindLabel = "moment",
                MetaLabel = "pinned · she brings this up on anniversaries",
                IsPinned = true
            },
            new CompanionMemoryFact
            {
                Text = "Calls his cat “Prime Minister Beans.”",
                KindKey = "joke",
                KindLabel = "running joke",
                MetaLabel = "used 4× · last: yesterday"
            },
            new CompanionMemoryFact
            {
                Text = "Melts fastest to spiral + whisper combos.",
                KindKey = "preference",
                KindLabel = "preference",
                MetaLabel = "from chat · salience high"
            },
            new CompanionMemoryFact
            {
                Text = "Wants to hit Level 50 before September.",
                KindKey = "goal",
                KindLabel = "goal · open thread",
                MetaLabel = "she checks in on this"
            },
            DormantPromiseCard()
        };

        private static CompanionMemoryFact DormantPromiseCard() => new()
        {
            Text = "“soon I'll remember what you say too… choose your words carefully~”",
            KindKey = "all",
            KindLabel = "soon · train 4",
            MetaLabel = string.Empty,
            IsDormant = true
        };
    }
}
