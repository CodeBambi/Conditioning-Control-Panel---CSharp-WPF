using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IMemoryDiaryVm"/>.
    ///
    /// <para>This mock carries the wall's real projection behaviour rather than a frozen array,
    /// because that behaviour is the part the builders will reuse and the part the unit tests pin
    /// down. Three things actually happen here:</para>
    /// <list type="bullet">
    ///   <item>selecting a kind chip re-projects the wall through
    ///   <see cref="FactOrdering.Project"/> (filter ▸ boundary ▸ pinned ▸ salience ▸ dormant);</item>
    ///   <item>pinning a card re-projects too, so the card visibly climbs — that feedback is the
    ///   whole point of the pin;</item>
    ///   <item>forgetting a card removes it and can tip the wall back into its empty state.</item>
    /// </list>
    ///
    /// <para>The cards are <see cref="MemoryFactCard"/>s, so pin / edit / forget are real commands
    /// in the designer and in a play-test, not no-ops.</para>
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
            _all = facts ?? new List<IMemoryFactVm>();
            ProfileStats = stats;
            Filters = BuildFilters();
            foreach (var fact in _all) Attach(fact);
            _facts = FactOrdering.Project(_all, _selectedFilterKey);
            OpenStorageFolderCommand = CompanionRelayCommand.NoOp("memory.openFolder");
            ForgetEverythingCommand = new CompanionRelayCommand(ForgetEverything);

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
                Reproject();
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

        // ------------------------------- wall mutation -------------------------------

        /// <summary>
        /// Wires a card up to the wall: a pin flips the sort rank, so the projection has to be
        /// rerun, and a forget takes the card out of the underlying list.
        /// </summary>
        private void Attach(IMemoryFactVm fact)
        {
            if (fact is MemoryFactCard card) card.Forgotten = Forget;
            fact.PropertyChanged += OnFactPropertyChanged;
        }

        private void Detach(IMemoryFactVm fact)
        {
            if (fact is MemoryFactCard card) card.Forgotten = null;
            fact.PropertyChanged -= OnFactPropertyChanged;
        }

        private void OnFactPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IMemoryFactVm.IsPinned)) Reproject();
        }

        /// <summary>Removes one card and re-projects. Public shape kept small on purpose.</summary>
        public void Forget(IMemoryFactVm fact)
        {
            if (fact == null || !_all.Remove(fact)) return;
            Detach(fact);
            Reproject();
            Raise(nameof(IsEmpty));
        }

        /// <summary>
        /// "Forget everything…" — every real fact goes; the dormant promise card is copy, not a
        /// memory, so it stays and the wall lands on its designed empty state rather than a hole.
        /// </summary>
        public void ForgetEverything()
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                var fact = _all[i];
                if (fact.IsDormant) continue;
                _all.RemoveAt(i);
                Detach(fact);
            }
            Reproject();
            Raise(nameof(IsEmpty));
        }

        private void Reproject() => Facts = FactOrdering.Project(_all, _selectedFilterKey);

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
                new MemoryFactCard(
                    "First trance: 2026-03-02 — “the day we met.”",
                    "moment", "moment",
                    "from the app · she brings this up on anniversaries",
                    isPinned: true),
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
            new MemoryFactCard(
                "Never tease about chastity.",
                "boundary", "boundary · always honored",
                "set by you · 2026-07-30",
                isBoundary: true)
            {
                UserEditedMetaLabel = "set by you · edited just now"
            },
            new MemoryFactCard(
                "First trance: 2026-03-02 — “the day we met.”",
                "moment", "moment",
                "pinned · she brings this up on anniversaries",
                isPinned: true)
            {
                UserEditedMetaLabel = "pinned · edited by you"
            },
            new MemoryFactCard(
                "Calls his cat “Prime Minister Beans.”",
                "joke", "running joke",
                "used 4× · last: yesterday")
            {
                UserEditedMetaLabel = "edited by you · she'll use your wording"
            },
            new MemoryFactCard(
                "Melts fastest to spiral + whisper combos.",
                "preference", "preference",
                "from chat · salience high")
            {
                UserEditedMetaLabel = "edited by you · she'll use your wording"
            },
            new MemoryFactCard(
                "Wants to hit Level 50 before September.",
                "goal", "goal · open thread",
                "she checks in on this")
            {
                UserEditedMetaLabel = "edited by you · she checks in on this"
            },
            DormantPromiseCard()
        };

        private static MemoryFactCard DormantPromiseCard() => new(
            "“soon I'll remember what you say too… choose your words carefully~”",
            "all", "soon · train 4",
            string.Empty,
            isDormant: true);
    }
}
