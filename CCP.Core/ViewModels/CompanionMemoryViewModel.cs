using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.ViewModels
{
    /// <summary>
    /// One remembered fact as the "What she knows about you" panel renders it (doc 01 §2.4).
    /// Wraps an immutable <see cref="MemoryFact"/> and adds the transient edit state the row
    /// template toggles on.
    /// </summary>
    public sealed class MemoryFactRowViewModel : INotifyPropertyChanged
    {
        private MemoryFact _fact;
        private bool _isEditing;
        private string _editText;

        public MemoryFactRowViewModel(MemoryFact fact)
        {
            _fact = fact ?? throw new ArgumentNullException(nameof(fact));
            _editText = fact.Text;
        }

        public string Id => _fact.Id;
        public MemoryFactKind Kind => _fact.Kind;
        public string Text => _fact.Text;
        public bool Pinned => _fact.Pinned;
        public double Salience => _fact.Salience;
        public string Source => _fact.Source;

        /// <summary>True once the user has hand-edited this line (doc 01 §2.4 provenance).</summary>
        public bool IsUserEdited =>
            string.Equals(_fact.Source, MemoryFact.SourceUserEdited, StringComparison.OrdinalIgnoreCase);

        /// <summary>Pin glyph, resolved to a Twemoji SVG by EmojiToImageSourceConverter.</summary>
        public string PinEmoji => _fact.Pinned ? "📌" : "📍";

        public string PinToolTip =>
            Loc.Get(_fact.Pinned ? "companion_memory_unpin_tooltip" : "companion_memory_pin_tooltip");

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                // Entering edit mode always starts from what is actually stored, so an abandoned
                // edit never leaks into the next one.
                if (value) _editText = _fact.Text;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotEditing));
                OnPropertyChanged(nameof(EditText));
            }
        }

        public bool IsNotEditing => !_isEditing;

        /// <summary>The edit buffer. Nothing reaches the store until CommitEdit runs.</summary>
        public string EditText
        {
            get => _editText;
            set
            {
                if (_editText == value) return;
                _editText = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        /// <summary>Re-reads the row from a fresh store record after a write.</summary>
        internal void Apply(MemoryFact fact)
        {
            _fact = fact ?? throw new ArgumentNullException(nameof(fact));
            _editText = fact.Text;
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(Pinned));
            OnPropertyChanged(nameof(Salience));
            OnPropertyChanged(nameof(Source));
            OnPropertyChanged(nameof(IsUserEdited));
            OnPropertyChanged(nameof(PinEmoji));
            OnPropertyChanged(nameof(PinToolTip));
            OnPropertyChanged(nameof(EditText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
    }

    /// <summary>One "kind" bucket in the panel (Boundaries, Running jokes, …).</summary>
    public sealed class MemoryFactGroupViewModel
    {
        public MemoryFactGroupViewModel(MemoryFactKind kind, string header)
        {
            Kind = kind;
            Header = header;
        }

        public MemoryFactKind Kind { get; }
        public string Header { get; }
        public ObservableCollection<MemoryFactRowViewModel> Facts { get; } = new();
    }

    /// <summary>One line of the read-only "she can see" block.</summary>
    public sealed class ProfileSignalViewModel
    {
        public ProfileSignalViewModel(string key, string label, string value)
        {
            Key = key;
            Label = label;
            Value = value;
        }

        public string Key { get; }
        public string Label { get; }
        public string Value { get; }
    }

    /// <summary>
    /// Presentation model for the "What she knows about you" panel (doc 01 §2.4) — the trust
    /// surface for the companion's memory: inspect it, pin it, edit it, delete it, wipe it.
    ///
    /// <para>It talks to <see cref="IMemoryStore"/> and nothing else. Train 1 ships a shell store
    /// (in-memory, empty at boot), so the panel's normal state on this branch is the empty state;
    /// when the memory branch lands its real store the same bindings light up with no UI change.
    /// A null store (kill switch off, or the brain failed to initialize) is a first-class state:
    /// <see cref="IsAvailable"/> is false and every mutation is a no-op rather than a crash.</para>
    /// </summary>
    public sealed class CompanionMemoryViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// A hand-edited fact is the user telling us it matters, so editing floors its salience.
        /// Without this an edit could leave a 0.1-salience line that never survives the top-K cut
        /// in <c>GetInjectionBlock</c> — i.e. the user "fixes" a memory and she still never uses it.
        /// </summary>
        public const double EditedSalienceFloor = 0.6;

        /// <summary>Fixed display order — boundaries first because they are consent hygiene.</summary>
        internal static readonly MemoryFactKind[] KindOrder =
        {
            MemoryFactKind.Boundary,
            MemoryFactKind.Identity,
            MemoryFactKind.Preference,
            MemoryFactKind.Goal,
            MemoryFactKind.Joke,
            MemoryFactKind.Event
        };

        private readonly IMemoryStore? _store;
        private readonly Action? _forgetEverything;

        /// <param name="forgetEverything">
        /// The brain's full wipe (<c>CompanionBrain.Forget</c>), when there is a brain. It is NOT
        /// optional flavour: <see cref="IMemoryStore.Wipe"/> deletes session.json off disk but cannot
        /// touch the live turn log the brain holds in RAM, so a store-only wipe is undone by the very
        /// next reply — or by the shutdown flush, even if the user never sends one. Null degrades to
        /// the store-only wipe, which is correct for tests and for a null brain (there is no live
        /// conversation to survive).
        /// </param>
        public CompanionMemoryViewModel(IMemoryStore? store, Action? forgetEverything = null)
        {
            _store = store;
            _forgetEverything = forgetEverything;
            Refresh();
        }

        /// <summary>False when there is no store at all (kill switch off / brain init failed).</summary>
        public bool IsAvailable => _store != null;

        public ObservableCollection<MemoryFactGroupViewModel> Groups { get; } = new();
        public ObservableCollection<ProfileSignalViewModel> ProfileSignals { get; } = new();

        public int FactCount => Groups.Sum(g => g.Facts.Count);
        public bool HasFacts => FactCount > 0;
        public bool IsEmpty => !HasFacts;
        public bool HasProfile => ProfileSignals.Count > 0;
        public string FactCountText => Loc.GetF("companion_memory_fact_count", FactCount);

        // ---------- reads ----------

        /// <summary>Rebuilds groups and the profile block from the store. Safe to call repeatedly.</summary>
        public void Refresh()
        {
            Groups.Clear();
            ProfileSignals.Clear();

            if (_store != null)
            {
                foreach (var signal in BuildProfileSignals(_store.Profile))
                    ProfileSignals.Add(signal);

                var facts = _store.GetFacts() ?? Array.Empty<MemoryFact>();
                foreach (var kind in KindOrder)
                {
                    var inKind = facts.Where(f => f.Kind == kind).ToList();
                    if (inKind.Count == 0) continue;

                    var group = new MemoryFactGroupViewModel(kind, HeaderFor(kind));
                    foreach (var f in inKind) group.Facts.Add(new MemoryFactRowViewModel(f));
                    Groups.Add(group);
                }
            }

            RaiseAggregates();
        }

        internal static IEnumerable<ProfileSignalViewModel> BuildProfileSignals(
            IReadOnlyDictionary<string, object?>? profile)
        {
            if (profile == null) yield break;

            foreach (var pair in profile.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var value = FormatSignalValue(pair.Value);
                if (string.IsNullOrWhiteSpace(value)) continue;
                yield return new ProfileSignalViewModel(pair.Key, LabelForSignal(pair.Key), value);
            }
        }

        private static string FormatSignalValue(object? value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case string s:
                    return s.Trim();
                case DateTime dt:
                    return dt.ToString("d", CultureInfo.CurrentCulture);
                case System.Collections.IEnumerable list and not string:
                    var parts = list.Cast<object?>().Select(o => o?.ToString()?.Trim())
                        .Where(o => !string.IsNullOrEmpty(o));
                    return string.Join(", ", parts);
                default:
                    return System.Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim() ?? string.Empty;
            }
        }

        /// <summary>
        /// Profile signal keys are open-ended (the memory branch writes whatever it learns), so the
        /// label falls back to a humanized key rather than showing a raw identifier. A translated
        /// key wins when one exists; Loc.Get echoes the key back when it does not.
        /// </summary>
        internal static string LabelForSignal(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            var locKey = "companion_memory_signal_" + ToSnakeCase(key);
            var translated = Loc.Get(locKey);
            return string.Equals(translated, locKey, StringComparison.Ordinal) ? Humanize(key) : translated;
        }

        internal static string ToSnakeCase(string key)
        {
            var sb = new StringBuilder(key.Length + 4);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (c == ' ' || c == '-' || c == '.')
                {
                    if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
                    continue;
                }
                if (char.IsUpper(c) && sb.Length > 0 && sb[^1] != '_') sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        internal static string Humanize(string key)
        {
            var snake = ToSnakeCase(key);
            if (snake.Length == 0) return string.Empty;
            var words = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return string.Empty;
            words[0] = char.ToUpperInvariant(words[0][0]) + words[0].Substring(1);
            return string.Join(" ", words);
        }

        internal static string HeaderFor(MemoryFactKind kind) => kind switch
        {
            MemoryFactKind.Identity => Loc.Get("companion_memory_kind_identity"),
            MemoryFactKind.Preference => Loc.Get("companion_memory_kind_preference"),
            MemoryFactKind.Boundary => Loc.Get("companion_memory_kind_boundary"),
            MemoryFactKind.Joke => Loc.Get("companion_memory_kind_joke"),
            MemoryFactKind.Goal => Loc.Get("companion_memory_kind_goal"),
            MemoryFactKind.Event => Loc.Get("companion_memory_kind_event"),
            _ => Loc.Get("companion_memory_kind_event")
        };

        // ---------- writes ----------

        /// <summary>Pins / unpins a fact. Pinning must NOT re-stamp the fact's source.</summary>
        public bool TogglePin(MemoryFactRowViewModel? row)
        {
            if (row == null || _store == null) return false;
            if (!_store.UpdateFact(row.Id, pinned: !row.Pinned))
            {
                // The row is stale (something else forgot it). Resync rather than lie to the user.
                Refresh();
                return false;
            }

            SyncRow(row);
            return true;
        }

        /// <summary>Puts a row into edit mode with the stored text preloaded.</summary>
        public void BeginEdit(MemoryFactRowViewModel? row)
        {
            if (row == null || _store == null) return;
            foreach (var other in AllRows())
                if (!ReferenceEquals(other, row)) other.IsEditing = false;
            row.IsEditing = true;
        }

        public void CancelEdit(MemoryFactRowViewModel? row)
        {
            if (row == null) return;
            row.EditText = row.Text;
            row.IsEditing = false;
        }

        /// <summary>
        /// Writes the edit buffer back. Returns true only when something actually changed on disk.
        /// Blank text is rejected (deleting is what the bin is for) and an unchanged edit is a no-op.
        /// </summary>
        public bool CommitEdit(MemoryFactRowViewModel? row)
        {
            if (row == null || _store == null) return false;

            var text = (row.EditText ?? string.Empty).Trim();
            if (text.Length == 0 || string.Equals(text, row.Text, StringComparison.Ordinal))
            {
                CancelEdit(row);
                return false;
            }

            var salience = Math.Max(row.Salience, EditedSalienceFloor);
            if (!_store.UpdateFact(row.Id, text: text, salience: salience))
            {
                row.IsEditing = false;
                Refresh();
                return false;
            }

            SyncRow(row);
            row.IsEditing = false;
            return true;
        }

        /// <summary>Forgets a single fact and drops its row (and its group, when it was the last one).</summary>
        public bool Delete(MemoryFactRowViewModel? row)
        {
            // Note: no per-row confirmation. A single fact is cheap to lose and the panel exists to
            // make forgetting easy; only the all-or-nothing wipe asks first.
            if (row == null || _store == null) return false;
            if (!_store.ForgetFact(row.Id))
            {
                // The store kept it (read-only file, unknown id). Resync rather than pretend the
                // row is gone — a panel that shows a deletion which did not happen is the exact
                // failure this surface exists to prevent.
                Refresh();
                return false;
            }

            var group = Groups.FirstOrDefault(g => g.Facts.Contains(row));
            if (group != null)
            {
                group.Facts.Remove(row);
                if (group.Facts.Count == 0) Groups.Remove(group);
            }

            RaiseAggregates();
            return true;
        }

        /// <summary>
        /// "Forget everything". The confirmation lives in the view — by the time this runs the user
        /// has already said yes.
        /// </summary>
        public bool ForgetEverything()
        {
            if (_store == null) return false;

            // Through the brain when there is one: it clears the live turn log and the recommendation
            // ban list as well as every file, which is the only ordering that makes the dialog's
            // "she's a blank slate" true a second later.
            if (_forgetEverything != null) _forgetEverything();
            else _store.Wipe();

            Refresh();
            return true;
        }

        // ---------- plumbing ----------

        private IEnumerable<MemoryFactRowViewModel> AllRows() => Groups.SelectMany(g => g.Facts);

        private void SyncRow(MemoryFactRowViewModel row)
        {
            var updated = _store?.GetFacts()?.FirstOrDefault(f => f.Id == row.Id);
            if (updated != null) row.Apply(updated);
            else Refresh();
        }

        private void RaiseAggregates()
        {
            OnPropertyChanged(nameof(FactCount));
            OnPropertyChanged(nameof(FactCountText));
            OnPropertyChanged(nameof(HasFacts));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasProfile));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
    }
}
