using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.ViewModels;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// One fact card on the Z3 wall, backed by a real <see cref="MemoryFact"/>.
    ///
    /// <para>Every mutation goes through <see cref="CompanionMemoryViewModel"/> rather than through
    /// <see cref="IMemoryStore"/> directly. That is not indirection for its own sake: the panel
    /// viewmodel is where the edit path's provenance and salience-floor rules live (a hand-edited
    /// fact is the user telling us it matters, so it is floored above the top-K cut), where a stale
    /// row resyncs instead of lying about a delete that did not happen, and where the all-or-nothing
    /// wipe goes through the brain so the live turn log dies with the file. Re-implementing any of
    /// that against the store would have quietly dropped it.</para>
    /// </summary>
    internal sealed class MemoryFactRuntimeVm : CompanionObservable, IMemoryFactVm
    {
        private readonly MemoryFactRowViewModel _row;
        private readonly CompanionMemoryViewModel _owner;
        private readonly Action _changed;
        private readonly MemoryFact _fact;

        public MemoryFactRuntimeVm(MemoryFactRowViewModel row, MemoryFact fact,
            CompanionMemoryViewModel owner, Action changed)
        {
            _row = row;
            _fact = fact;
            _owner = owner;
            _changed = changed;

            PinCommand = new CompanionRelayCommand(() => { _owner.TogglePin(_row); _changed(); });
            EditCommand = new CompanionRelayCommand(() => _owner.BeginEdit(_row));
            ForgetCommand = new CompanionRelayCommand(() => { _owner.Delete(_row); _changed(); });
            CommitEditCommand = new CompanionRelayCommand(() => { _owner.CommitEdit(_row); _changed(); });

            _row.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(MemoryFactRowViewModel.IsEditing): Raise(nameof(IsEditing)); break;
                    case nameof(MemoryFactRowViewModel.EditText): Raise(nameof(EditText)); break;
                    case nameof(MemoryFactRowViewModel.Text): Raise(nameof(Text)); break;
                    case nameof(MemoryFactRowViewModel.Pinned): Raise(nameof(IsPinned)); break;
                }
            };
        }

        public string Id => _row.Id;
        public string Text => _row.Text;
        public string KindKey => KindKeyFor(_row.Kind);
        public string KindLabel => Loc.Get("companion_memory_card_" + KindKey);
        public string MetaLabel => BuildMeta(_fact.Uses, _fact.LastUsed, _row.IsUserEdited);
        public bool IsBoundary => _row.Kind == MemoryFactKind.Boundary;
        public bool IsPinned => _row.Pinned;
        public bool IsDormant => false;

        public bool IsEditing
        {
            get => _row.IsEditing;
            set => _row.IsEditing = value;
        }

        public string EditText
        {
            get => _row.EditText;
            set => _row.EditText = value;
        }

        public ICommand PinCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ForgetCommand { get; }
        public ICommand CommitEditCommand { get; }

        /// <summary>
        /// Memory kinds → the wall's filter chips. <see cref="MemoryFactKind.Event"/> is the design's
        /// "moment"; <see cref="MemoryFactKind.Identity"/> has no chip of its own and so appears only
        /// under "all", which is deliberate — an identity fact is not something anyone filters FOR.
        /// </summary>
        internal static string KindKeyFor(MemoryFactKind kind) => kind switch
        {
            MemoryFactKind.Boundary => "boundary",
            MemoryFactKind.Joke => "joke",
            MemoryFactKind.Preference => "preference",
            MemoryFactKind.Goal => "goal",
            MemoryFactKind.Identity => "identity",
            _ => "moment"
        };

        /// <summary>"used 4× · last: 2d ago", or the provenance line for a hand-edited fact.</summary>
        internal static string BuildMeta(int uses, DateTime? lastUsed, bool userEdited)
        {
            var parts = new List<string>(3);
            if (uses > 0) parts.Add(Loc.GetF("companion_memory_meta_uses", uses));
            if (lastUsed.HasValue)
                parts.Add(Loc.GetF(
                    "companion_memory_meta_last", ChatThresholdRuntimeVm.RelativeTime(lastUsed.Value)));
            if (userEdited) parts.Add(Loc.Get("companion_memory_meta_edited"));
            return parts.Count == 0
                ? Loc.Get("companion_memory_meta_new")
                : string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Z3 — What she knows about you, over the real <see cref="IMemoryStore"/>.
    ///
    /// <para>Never paywalled and never gated on the brain's kill switch: the profile strip is
    /// deterministic app state that exists from minute one, and this card is the app's answer to
    /// "what do you have on me?". A store that is missing entirely (brain failed to construct)
    /// renders the empty state rather than an error — the question still deserves an answer.</para>
    ///
    /// <para>The trailing dormant card is the Train 4 promise: today's facts are deterministic,
    /// and what she remembers of what you SAY arrives with the extractor.</para>
    /// </summary>
    internal sealed class MemoryDiaryRuntimeVm : CompanionObservable, IMemoryDiaryVm
    {
        private readonly CompanionRuntimeContext _ctx;
        private readonly List<IMemoryFactVm> _all = new();
        private readonly List<IProfileStatVm> _profile = new();
        private readonly List<IFactFilterVm> _filters = new();

        private CompanionMemoryViewModel _inner;
        private IMemoryStore? _store;
        private string _selectedFilterKey = "all";
        private IReadOnlyList<IMemoryFactVm> _facts = Array.Empty<IMemoryFactVm>();

        public MemoryDiaryRuntimeVm(CompanionRuntimeContext ctx)
        {
            _ctx = ctx;
            _store = App.Brain?.Memory;
            _inner = new CompanionMemoryViewModel(_store, () => App.Brain?.Forget());

            foreach (var key in FactOrdering.FilterKeys)
            {
                var chip = new CompanionFactFilter(key,
                    Loc.Get("companion_memory_filter_" + key),
                    selected: key == "all");
                chip.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != nameof(IFactFilterVm.IsSelected)) return;
                    if (s is IFactFilterVm f && f.IsSelected) SelectedFilterKey = f.Key;
                };
                _filters.Add(chip);
            }

            OpenStorageFolderCommand = new CompanionRelayCommand(OpenStorageFolder);
            ForgetEverythingCommand = new CompanionRelayCommand(ForgetEverything);

            Sync();
        }

        public string ProfileStripLabel => Loc.Get("companion_memory_profile_strip");
        public IReadOnlyList<IProfileStatVm> ProfileStats => _profile;
        public IReadOnlyList<IFactFilterVm> Filters => _filters;
        public IReadOnlyList<IMemoryFactVm> Facts => _facts;

        public string SelectedFilterKey
        {
            get => _selectedFilterKey;
            set
            {
                var key = string.IsNullOrWhiteSpace(value) ? "all" : value;
                if (!Set(ref _selectedFilterKey, key)) return;
                foreach (var chip in _filters)
                    chip.IsSelected = string.Equals(chip.Key, key, StringComparison.OrdinalIgnoreCase);
                Project();
            }
        }

        /// <summary>True when nothing but the dormant promise card is on the wall.</summary>
        public bool IsEmpty => _all.All(f => f.IsDormant);

        public string EmptyCopy => Loc.Get("companion_memory_empty_copy");
        public string StorageNote => Loc.Get("companion_memory_storage_note");
        public string StorageLinkLabel => Loc.Get("companion_memory_storage_link");
        public string ForgetEverythingLabel => Loc.Get("companion_memory_forget_everything");

        public ICommand OpenStorageFolderCommand { get; }
        public ICommand ForgetEverythingCommand { get; }

        // =====================================================================================

        /// <summary>
        /// Rebuilds the wall and the profile strip from the store.
        ///
        /// <para>The store is re-read each time rather than captured once: <c>App.Brain</c> is
        /// constructed during startup, and a room built before it exists must pick it up when it
        /// arrives instead of showing an empty diary for the rest of the session.</para>
        /// </summary>
        public void Sync()
        {
            CompanionRuntimeContext.Guarded(() =>
            {
                var live = App.Brain?.Memory;
                if (!ReferenceEquals(live, _store))
                {
                    _store = live;
                    _inner = new CompanionMemoryViewModel(_store, () => App.Brain?.Forget());
                }
                else
                {
                    _inner.Refresh();
                }

                Rebuild();
            }, "memory sync");
        }

        private void Rebuild()
        {
            _profile.Clear();
            foreach (var signal in _inner.ProfileSignals)
                _profile.Add(new CompanionProfileStat($"{signal.Label} {signal.Value}".Trim()));
            Raise(nameof(ProfileStats));

            _all.Clear();
            var raw = _store?.GetFacts() ?? Array.Empty<MemoryFact>();
            var byId = raw.Where(f => f != null).ToDictionary(f => f.Id, StringComparer.Ordinal);

            foreach (var group in _inner.Groups)
            {
                foreach (var row in group.Facts)
                {
                    if (!byId.TryGetValue(row.Id, out var fact)) continue;
                    _all.Add(new MemoryFactRuntimeVm(row, fact, _inner, Sync));
                }
            }

            // The Train 4 promise card always closes the wall (FactOrdering sorts it last and
            // exempts it from the kind filter), so an empty diary is a promise, not a void.
            _all.Add(new CompanionMemoryFact
            {
                Text = Loc.Get("companion_memory_dormant_promise"),
                KindKey = "dormant",
                KindLabel = Loc.Get("companion_memory_card_dormant"),
                MetaLabel = string.Empty,
                IsDormant = true
            });

            Project();
            Raise(nameof(IsEmpty));
        }

        private void Project()
        {
            _facts = FactOrdering.Project(_all, SelectedFilterKey);
            Raise(nameof(Facts));
        }

        // =====================================================================================

        /// <summary>Opens <c>%LOCALAPPDATA%\ConditioningControlPanel\companion\</c> in Explorer.</summary>
        private static void OpenStorageFolder() => CompanionRuntimeContext.Guarded(() =>
        {
            var dir = MemoryStore.CompanionDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }, "open memory folder");

        /// <summary>
        /// THE wipe (doc 01 §2.4): facts, profile, episodes AND the conversation, through
        /// <c>CompanionBrain.Forget</c>. The confirmation is the card's own two-step, so by the time
        /// this runs the user has already said yes twice.
        ///
        /// <para>The Engine Room keeps a narrower "clear conversation" for the case the legacy
        /// Reset Memory button existed for — a companion stuck in an old pattern — because that user
        /// wants her to forget the thread, not to forget that they are level 41.</para>
        /// </summary>
        private void ForgetEverything()
        {
            CompanionRuntimeContext.Guarded(() => App.Bark?.NotifyUiAction("reset_memory"), "wipe bark");
            _inner.ForgetEverything();

            // The on-screen bubble log is a separate store the brain does not own; the legacy button
            // cleared it too, and a "blank slate" that still lists yesterday's bubbles is not one.
            CompanionRuntimeContext.Guarded(
                () => App.AvatarWindow?.ChatHistory.Clear(), "clear bubble history");

            App.Logger?.Information("Companion memory wiped from the diary");
            Sync();
        }
    }
}
