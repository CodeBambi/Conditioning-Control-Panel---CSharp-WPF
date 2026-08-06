using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

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
            //
            // A ToggleButton also UNchecks on a second click, and that used to leave the group with
            // nothing selected while SelectedFilterKey — and therefore the wall — stayed filtered:
            // facts missing, no chip lit, no way to read why. The mockup's chips are strictly
            // single-select, so clicking the active one is a no-op and we put it straight back.
            foreach (var f in Filters)
            {
                f.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(IFactFilterVm.IsSelected)) return;
                    if (f.IsSelected) { SelectedFilterKey = f.Key; return; }
                    if (string.Equals(f.Key, _selectedFilterKey, StringComparison.OrdinalIgnoreCase))
                        f.IsSelected = true;
                };
            }
        }

        public string ProfileStripLabel { get; init; } =
            Loc.Get("companion_memory_profile_strip");
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

        public string EmptyCopy { get; init; } =
            Loc.Get("companion_memory_empty_copy");
        public string StorageNote { get; init; } =
            Loc.Get("companion_memory_storage_note");
        public string StorageLinkLabel { get; init; } =
            Loc.Get("companion_memory_storage_link");
        public string ForgetEverythingLabel { get; init; } =
            Loc.Get("companion_memory_forget_everything");

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
                    "moment", Loc.Get("companion_memory_card_moment"),
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

        /// <summary>
        /// The chip row, built from <see cref="FactOrdering.FilterKeys"/> so the display order and
        /// the filter keys can never disagree, with every label resolved through the staged loc
        /// layer (companion_memory_filter_&lt;key&gt;).
        /// </summary>
        private static IReadOnlyList<CompanionFactFilter> BuildFilters()
        {
            var chips = new List<CompanionFactFilter>(FactOrdering.FilterKeys.Count);
            foreach (var key in FactOrdering.FilterKeys)
            {
                chips.Add(new CompanionFactFilter(
                    key,
                    Loc.Get($"companion_memory_filter_{key}"),
                    selected: string.Equals(key, "all", StringComparison.OrdinalIgnoreCase)));
            }
            return chips;
        }

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
                "boundary", Loc.Get("companion_memory_card_boundary"),
                "set by you · 2026-07-30",
                isBoundary: true)
            {
                UserEditedMetaLabel = "set by you · edited just now"
            },
            new MemoryFactCard(
                "First trance: 2026-03-02 — “the day we met.”",
                "moment", Loc.Get("companion_memory_card_moment"),
                "pinned · she brings this up on anniversaries",
                isPinned: true)
            {
                UserEditedMetaLabel = "pinned · edited by you"
            },
            new MemoryFactCard(
                "Calls his cat “Prime Minister Beans.”",
                "joke", Loc.Get("companion_memory_card_joke"),
                "used 4× · last: yesterday")
            {
                UserEditedMetaLabel = "edited by you · she'll use your wording"
            },
            new MemoryFactCard(
                "Melts fastest to spiral + whisper combos.",
                "preference", Loc.Get("companion_memory_card_preference"),
                "from chat · salience high")
            {
                UserEditedMetaLabel = "edited by you · she'll use your wording"
            },
            new MemoryFactCard(
                "Wants to hit Level 50 before September.",
                "goal", Loc.Get("companion_memory_card_goal"),
                "she checks in on this")
            {
                UserEditedMetaLabel = "edited by you · she checks in on this"
            },
            DormantPromiseCard()
        };

        private static MemoryFactCard DormantPromiseCard() => new(
            Loc.Get("companion_memory_dormant_promise"),
            "all", Loc.Get("companion_memory_card_dormant"),
            string.Empty,
            isDormant: true);
    }
}
