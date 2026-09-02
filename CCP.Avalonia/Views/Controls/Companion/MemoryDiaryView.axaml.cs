using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z3 — the memory diary. See the XAML header for the visual spec.
    ///
    /// <para>PORTED from ConditioningControlPanel/Views/Controls/Companion/MemoryDiaryView.xaml.cs.
    /// Almost code-free by design: filtering, sorting, the kind rails and the hover-reveal action
    /// row are all declarative. What is left is the wipe's inline confirm flow and the inline-edit
    /// ergonomics (the box takes focus when it appears, Enter saves, Esc backs out).</para>
    ///
    /// <para>Deviations: <c>CompanionWheelRelay.Attach(FactWall)</c> is gone — Avalonia's
    /// ScrollViewer chains a wheel notch it cannot use to its parent by itself. The WPF
    /// <c>IMemoryDiaryVm</c>, <c>MockMemoryDiaryVm</c>, <c>MemoryFactCard</c>, <c>FactOrdering</c>
    /// and <c>MemoryForgetConfirm</c> live in the head; their shape is carried below as concrete
    /// classes seeded with the mock's artboard, the same way MakeHerYoursView does it.</para>
    /// </summary>
    public partial class MemoryDiaryView : UserControl
    {
        public MemoryDiaryView()
        {
            // ForgetConfirm is initialised inline, BEFORE InitializeComponent: the footer binds
            // #Root.ForgetConfirm.* and a CLR property assigned afterwards never re-notifies.
            InitializeComponent();
            DataContextChanged += (_, _) => ForgetConfirm.Bind(ViewModel?.ForgetEverythingCommand);
            // Leaving the tab backs the question out; the binding itself survives so the button
            // still works when the user comes back.
            Unloaded += (_, _) => ForgetConfirm.Disarm();
            DataContext = new MemoryDiaryViewModel();
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public MemoryDiaryViewModel? ViewModel
        {
            get => DataContext as MemoryDiaryViewModel;
            set => DataContext = value;
        }

        /// <summary>
        /// The "Forget everything…" two-step. Bound from the footer by name through the
        /// UserControl, so the destructive command has exactly one path to being executed.
        /// </summary>
        public MemoryForgetConfirm ForgetConfirm { get; } = new();

        // =====================================================================================
        //  inline edit
        // =====================================================================================

        /// <summary>Enter saves, Esc backs out. Esc leaves the fact exactly as it was.</summary>
        private void FactEdit_KeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not Control { DataContext: MemoryFact fact }) return;

            if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0)
            {
                if (fact.CommitEditCommand.CanExecute(null)) fact.CommitEditCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                fact.IsEditing = false;
                e.Handled = true;
            }
        }

        private void FactEditCancel_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: MemoryFact fact }) fact.IsEditing = false;
        }

        /// <summary>
        /// Puts the caret where the user is already looking. Deferred at Normal priority — the box
        /// is only laid out once the class selector has run, and Loaded priority is starved here.
        /// </summary>
        private void FactEdit_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is not TextBox box || e.Property != IsVisibleProperty) return;
            if (e.NewValue is not true) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (!box.IsVisible) return;
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// Fact kind → its accent colour, for the wall's kind-coloured card edge and kind tag. Port of
    /// the head's CompanionSurfaceConverters.cs. Unknown kinds fall back to violet — a new memory
    /// kind shipping from the Brain must render as a normal card, never as a blank or a crash.
    /// </summary>
    public sealed class CompanionFactKindBrushConverter : IValueConverter
    {
        /// <summary>Rail mode: the same hue at reduced alpha, so the edge whispers.</summary>
        public bool Soft { get; set; }

        private static readonly Dictionary<string, Color> Palette =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["boundary"] = Color.FromRgb(0x7F, 0xB2, 0xD9),   // steel — consent hygiene
                ["joke"] = Color.FromRgb(0xFF, 0x69, 0xB4),       // pink
                ["preference"] = Color.FromRgb(0xB4, 0x78, 0xFF), // purple
                ["goal"] = Color.FromRgb(0xFF, 0xD7, 0x00),       // gold
                ["moment"] = Color.FromRgb(0x93, 0x70, 0xDB),     // violet
                ["identity"] = Color.FromRgb(0x6E, 0xE7, 0xA7)    // live-green
            };

        private static readonly Color Fallback = Color.FromRgb(0xB4, 0x78, 0xFF);

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            Color c = Palette.TryGetValue(value as string ?? string.Empty, out var hue) ? hue : Fallback;
            return new SolidColorBrush(Soft ? Color.FromArgb(0x99, c.R, c.G, c.B) : c);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
    }

    /// <summary>
    /// The two-step "Forget everything…" flow for Z3, in her voice. Port of the head's
    /// MemoryForgetConfirm.cs. Invariants: the destructive command runs only from
    /// <see cref="ConfirmCommand"/> and only while armed; confirming disarms first so a double-click
    /// cannot fire the wipe twice; re-binding always disarms.
    /// </summary>
    public sealed class MemoryForgetConfirm : CompanionObservable
    {
        private ICommand? _target;
        private bool _isArmed;
        private readonly CompanionRelayCommand _arm, _confirm;

        public MemoryForgetConfirm()
        {
            ArmCommand = _arm = new CompanionRelayCommand(Arm, () => CanArm);
            ConfirmCommand = _confirm = new CompanionRelayCommand(Confirm, () => IsArmed);
            CancelCommand = new CompanionRelayCommand(Cancel);
        }

        /// <summary>True while the in-voice confirm strip is showing instead of the footer.</summary>
        public bool IsArmed
        {
            get => _isArmed;
            private set { if (Set(ref _isArmed, value)) _confirm.RaiseCanExecuteChanged(); }
        }

        /// <summary>There is something to wipe and it is willing to run.</summary>
        public bool CanArm => _target != null && _target.CanExecute(null);

        /// <summary>How many times the wipe actually ran. Diagnostics for the tests.</summary>
        public int ConfirmedCount { get; private set; }

        public ICommand ArmCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>Points the flow at a viewmodel's ForgetEverythingCommand (null unbinds). Always disarms.</summary>
        public void Bind(ICommand? forgetEverything)
        {
            _target = forgetEverything;
            IsArmed = false;
            Raise(nameof(CanArm));
            _arm.RaiseCanExecuteChanged();
        }

        public void Disarm() => IsArmed = false;

        private void Arm() { if (CanArm) IsArmed = true; }

        private void Cancel() => Disarm();

        private void Confirm()
        {
            if (!IsArmed) return;
            var target = _target;
            // Disarm before executing: the strip disappears on the first click, so a second one
            // lands on the restored footer instead of running the wipe again.
            IsArmed = false;
            if (target == null || !target.CanExecute(null)) return;
            ConfirmedCount++;
            target.Execute(null);
        }
    }

    /// <summary>One kind chip. Port of CompanionFactFilter / IFactFilterVm.</summary>
    public sealed class FactFilter : CompanionObservable
    {
        private bool _isSelected;

        public FactFilter(string key, string label, bool selected = false)
        {
            Key = key;
            Label = label;
            _isSelected = selected;
        }

        public string Key { get; }
        public string Label { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }

    /// <summary>One card on the wall. Port of MemoryFactCard / IMemoryFactVm.</summary>
    public sealed class MemoryFact : CompanionObservable
    {
        private string _text;
        private string _metaLabel;
        private bool _isPinned;
        private bool _isEditing;
        private string _editText = string.Empty;

        public MemoryFact(string text, string kindKey, string kindLabel, string metaLabel = "",
                          bool isBoundary = false, bool isPinned = false, bool isDormant = false)
        {
            _text = text;
            _metaLabel = metaLabel;
            _isPinned = isPinned;
            KindKey = string.IsNullOrWhiteSpace(kindKey) ? "moment" : kindKey;
            KindLabel = kindLabel;
            IsBoundary = isBoundary;
            IsDormant = isDormant;

            PinCommand = new CompanionRelayCommand(() => { if (CanPin) IsPinned = !IsPinned; }, () => CanPin);
            EditCommand = new CompanionRelayCommand(() => IsEditing = true, () => CanEdit);
            ForgetCommand = new CompanionRelayCommand(Forget, () => CanForget);
            CommitEditCommand = new CompanionRelayCommand(CommitEdit);
        }

        public string KindKey { get; }
        public string KindLabel { get; }
        public bool IsBoundary { get; }
        public bool IsDormant { get; }

        /// <summary>The meta line once the user has rewritten the fact.</summary>
        public string? UserEditedMetaLabel { get; init; }

        /// <summary>Set by the wall: a forget takes the card out of the list.</summary>
        public Action<MemoryFact>? Forgotten { get; set; }

        public string Text { get => _text; private set => Set(ref _text, value); }
        public string MetaLabel { get => _metaLabel; private set => Set(ref _metaLabel, value); }
        public bool IsPinned { get => _isPinned; private set => Set(ref _isPinned, value); }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (value && !CanEdit) return;             // dormant copy is never editable
                if (!Set(ref _isEditing, value)) return;
                if (value) EditText = Text;                // opening the box seeds it with the fact
            }
        }

        public string EditText { get => _editText; set => Set(ref _editText, value); }

        public ICommand PinCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ForgetCommand { get; }
        public ICommand CommitEditCommand { get; }

        /// <summary>A boundary already sorts first and can never sink; offering the pin would promise the wrong thing.</summary>
        public bool CanPin => !IsBoundary && !IsDormant;
        public bool CanEdit => !IsDormant;
        public bool CanForget => !IsDormant;

        private void CommitEdit()
        {
            if (!_isEditing) return;
            string trimmed = (EditText ?? string.Empty).Trim();
            if (trimmed.Length > 0)
            {
                bool changed = !string.Equals(trimmed, Text, StringComparison.Ordinal);
                Text = trimmed;
                if (changed && !string.IsNullOrWhiteSpace(UserEditedMetaLabel)) MetaLabel = UserEditedMetaLabel!;
            }
            _isEditing = false;
            Raise(nameof(IsEditing));
        }

        private void Forget()
        {
            if (!CanForget) return;
            _isEditing = false;
            Raise(nameof(IsEditing));
            Forgotten?.Invoke(this);
        }
    }

    /// <summary>
    /// The view's data contract, seeded with the WPF mock's artboard (five real facts plus the
    /// Train 4 promise card). Port of MockMemoryDiaryVm's used surface: selecting a chip
    /// re-projects the wall, pinning re-projects so the card visibly climbs, forgetting removes
    /// the card and can tip the wall back into its empty state.
    /// </summary>
    public sealed class MemoryDiaryViewModel : CompanionObservable
    {
        // ponytail: copy of the head's FactOrdering.FilterKeys; delete when CompanionVmPrimitives moves to Core
        private static readonly string[] FilterKeys = { "all", "boundary", "joke", "preference", "goal", "moment" };

        private readonly List<MemoryFact> _all;
        private string _selectedFilterKey = "all";
        private IReadOnlyList<MemoryFact> _facts = Array.Empty<MemoryFact>();

        public MemoryDiaryViewModel() : this(ArtboardFacts(), ArtboardStats()) { }

        public MemoryDiaryViewModel(List<MemoryFact> facts, IReadOnlyList<string> stats)
        {
            _all = facts;
            ProfileStats = stats;
            Filters = FilterKeys.Select(k => new FactFilter(k, Loc.Get($"companion_memory_filter_{k}"), k == "all")).ToArray();
            foreach (var fact in _all) Attach(fact);
            Reproject();
            ForgetEverythingCommand = new CompanionRelayCommand(ForgetEverything);

            // The chips are radio-like: selecting one clears the rest and re-projects the wall.
            // A ToggleButton also UNchecks on a second click; the chips are strictly single-select,
            // so clicking the active one is a no-op and we put it straight back.
            foreach (var f in Filters)
            {
                f.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(FactFilter.IsSelected)) return;
                    if (f.IsSelected) { SelectedFilterKey = f.Key; return; }
                    if (string.Equals(f.Key, _selectedFilterKey, StringComparison.OrdinalIgnoreCase))
                        f.IsSelected = true;
                };
            }
        }

        public string ProfileStripLabel { get; init; } = Loc.Get("companion_memory_profile_strip");
        public IReadOnlyList<string> ProfileStats { get; }
        public IReadOnlyList<FactFilter> Filters { get; }

        public IReadOnlyList<MemoryFact> Facts
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
        public bool IsEmpty => _all.All(f => f.IsDormant);

        public string EmptyCopy { get; init; } = Loc.Get("companion_memory_empty_copy");
        public string StorageNote { get; init; } = Loc.Get("companion_memory_storage_note");
        public string StorageLinkLabel { get; init; } = Loc.Get("companion_memory_storage_link");
        public string ForgetEverythingLabel { get; init; } = Loc.Get("companion_memory_forget_everything");

        // ponytail: needs a shell "open folder" on CorePaths' companion dir, wired when the diary moves to Core
        public ICommand? OpenStorageFolderCommand => null;
        public ICommand ForgetEverythingCommand { get; }

        private void Attach(MemoryFact fact)
        {
            fact.Forgotten = Forget;
            fact.PropertyChanged += OnFactPropertyChanged;
        }

        private void Detach(MemoryFact fact)
        {
            fact.Forgotten = null;
            fact.PropertyChanged -= OnFactPropertyChanged;
        }

        private void OnFactPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MemoryFact.IsPinned)) Reproject();
        }

        public void Forget(MemoryFact fact)
        {
            if (!_all.Remove(fact)) return;
            Detach(fact);
            Reproject();
            Raise(nameof(IsEmpty));
        }

        /// <summary>Every real fact goes; the dormant promise card is copy, not a memory, so it stays.</summary>
        public void ForgetEverything()
        {
            foreach (var fact in _all.Where(f => !f.IsDormant).ToList())
            {
                _all.Remove(fact);
                Detach(fact);
            }
            Reproject();
            Raise(nameof(IsEmpty));
        }

        /// <summary>filter ▸ boundary ▸ pinned ▸ salience ▸ dormant. OrderBy is stable, so insertion order is the tiebreak.</summary>
        private void Reproject()
        {
            bool all = _selectedFilterKey == "all";
            Facts = _all
                .Where(f => f.IsDormant || all || string.Equals(f.KindKey, _selectedFilterKey, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.IsDormant ? 3 : f.IsBoundary ? 0 : f.IsPinned ? 1 : 2)
                .ToArray();
        }

        // ------------------------------- sample data (the WPF mock's artboard) -------------------------------

        private static IReadOnlyList<string> ArtboardStats() => new[]
        {
            "Level 41", "Streak 12 days", "87 sessions", "Archetype: Dreamer", "Favorite: Flash"
        };

        private static List<MemoryFact> ArtboardFacts() => new()
        {
            new MemoryFact("Never tease about chastity.",
                "boundary", Loc.Get("companion_memory_card_boundary"), "set by you · 2026-07-30", isBoundary: true)
                { UserEditedMetaLabel = "set by you · edited just now" },
            new MemoryFact("First trance: 2026-03-02 — “the day we met.”",
                "moment", Loc.Get("companion_memory_card_moment"), "pinned · she brings this up on anniversaries", isPinned: true)
                { UserEditedMetaLabel = "pinned · edited by you" },
            new MemoryFact("Calls his cat “Prime Minister Beans.”",
                "joke", Loc.Get("companion_memory_card_joke"), "used 4× · last: yesterday")
                { UserEditedMetaLabel = "edited by you · she'll use your wording" },
            new MemoryFact("Melts fastest to spiral + whisper combos.",
                "preference", Loc.Get("companion_memory_card_preference"), "from chat · salience high")
                { UserEditedMetaLabel = "edited by you · she'll use your wording" },
            new MemoryFact("Wants to hit Level 50 before September.",
                "goal", Loc.Get("companion_memory_card_goal"), "she checks in on this")
                { UserEditedMetaLabel = "edited by you · she checks in on this" },
            new MemoryFact(Loc.Get("companion_memory_dormant_promise"),
                "all", Loc.Get("companion_memory_card_dormant"), string.Empty, isDormant: true)
        };
    }
}
