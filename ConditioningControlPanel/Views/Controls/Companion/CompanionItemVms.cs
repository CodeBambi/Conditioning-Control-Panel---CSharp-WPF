using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    // =====================================================================================
    //  Row-level viewmodel contracts shared by more than one zone, plus one plain mock
    //  implementation of each. The zone interfaces expose collections of these; a builder
    //  wiring a real service implements the interface, and the design-time gallery uses the
    //  Companion* concrete types below.
    //
    //  Everything is read-only from the view's side except the handful of two-way properties
    //  the mockup actually lets you change (fact edit text, filter selection).
    // =====================================================================================

    /// <summary>One bubble in the Z2 threshold thread.</summary>
    public interface IChatBubbleVm : INotifyPropertyChanged
    {
        CompanionBubbleKind Kind { get; }
        string Text { get; }

        /// <summary>
        /// Drives the pink "✨ AI" badge. INVARIANT (doc 01): true only for a genuine model
        /// completion. Never a bark, never a canned line, never a BarkEcho.
        /// </summary>
        bool IsAiGenerated { get; }

        /// <summary>Relative time for the tooltip ("2h ago"). Optional.</summary>
        string? Timestamp { get; }
    }

    /// <summary>One card on the Z3 fact wall.</summary>
    public interface IMemoryFactVm : INotifyPropertyChanged
    {
        string Id { get; }
        string Text { get; }

        /// <summary>Filter key: boundary | joke | preference | goal | moment | identity.</summary>
        string KindKey { get; }

        /// <summary>Localized kind caption, e.g. "boundary · always honored".</summary>
        string KindLabel { get; }

        /// <summary>Localized provenance line, e.g. "used 4× · last: yesterday".</summary>
        string MetaLabel { get; }

        /// <summary>Steel-blue rail, sorts first, never evicted.</summary>
        bool IsBoundary { get; }

        /// <summary>Gold pin, sorts ahead of unpinned facts.</summary>
        bool IsPinned { get; }

        /// <summary>The trailing "soon I'll remember what you say too…" promise card.</summary>
        bool IsDormant { get; }

        /// <summary>Inline edit swaps the text for a TextBox; the source flips to user-edited.</summary>
        bool IsEditing { get; set; }
        string EditText { get; set; }

        ICommand PinCommand { get; }
        ICommand EditCommand { get; }
        ICommand ForgetCommand { get; }
        ICommand CommitEditCommand { get; }
    }

    /// <summary>A kind chip in the Z3 filter row.</summary>
    public interface IFactFilterVm : INotifyPropertyChanged
    {
        string Key { get; }
        string Label { get; }
        bool IsSelected { get; set; }
    }

    /// <summary>A gold chip in the Z3 "she can see:" transparency strip.</summary>
    public interface IProfileStatVm
    {
        string Label { get; }
    }

    /// <summary>One of the five relationship stages.</summary>
    public interface IConstellationNodeVm : INotifyPropertyChanged
    {
        int Index { get; }
        string Name { get; }
        string Glyph { get; }
        ConstellationNodeState State { get; }

        /// <summary>Copy for the achievement-toast-styled popup when the node is clicked.</summary>
        string Description { get; }
    }

    /// <summary>A read-only trait gauge in Z4 (Dominance, Tease…).</summary>
    public interface ITraitGaugeVm
    {
        string Label { get; }

        /// <summary>0..100, shown as the right-hand number.</summary>
        int Value { get; }

        /// <summary>0..1, feeds the star-width fill column.</summary>
        double Fraction { get; }
    }

    /// <summary>A preset chip in Z4.</summary>
    public interface IPresetChipVm : INotifyPropertyChanged
    {
        string Id { get; }
        string Label { get; }
        bool IsSelected { get; set; }
    }

    /// <summary>A deny-list chip in Z5.</summary>
    public interface IDenyChipVm
    {
        string Label { get; }
        bool IsSeeded { get; }
        ICommand RemoveCommand { get; }
    }

    /// <summary>A pigeonhole in the Z8 Workshop grid.</summary>
    public interface IWorkshopCellVm
    {
        /// <summary>
        /// The deep-link anchor — the identity other zones point at (see
        /// <see cref="CompanionRoomAnchors"/>). Stable, never localized, never shown.
        ///
        /// <para>Split from <see cref="Title"/> by the wiring pass. While the two were one string a
        /// Workshop cell could not be localized without silently breaking the hero's Switch chip and
        /// Z5's "fine-tuning ↓": both match by title, and a German title matches no anchor. The
        /// default implementation returns <see cref="Title"/>, so callers written before the split
        /// (the mocks, the zone tests) keep the behaviour they had.</para>
        /// </summary>
        string Key => Title;

        /// <summary>The cell's heading as the user reads it. Localizable.</summary>
        string Title { get; }

        IReadOnlyList<IWorkshopRowVm> Rows { get; }

        /// <summary>
        /// The real control this pigeonhole holds, when it holds one.
        ///
        /// <para>Z8 was always specified as a container move rather than a rebuild (design §3 Z8):
        /// the legacy accordions are re-parented into the drawer, not re-implemented. This is the
        /// seam that lets a cell carry the actual re-parented control, and the view renders it
        /// INSTEAD of <see cref="Rows"/> — a cell is one or the other, never both, so the mock's
        /// scaffold rows and the wired-up controls can never double up on screen.</para>
        ///
        /// <para>Null on every design-time cell, which is what keeps the preview harness rendering
        /// the scaffold exactly as it did.</para>
        /// </summary>
        object? Content => null;
    }

    /// <summary>
    /// A row inside a Workshop cell. Deliberately loose: the Workshop is a container move, not a
    /// rebuild — the real rows are the existing accordion controls re-parented, and this shape
    /// only has to describe them well enough for the scaffold and the design-time gallery.
    /// </summary>
    public interface IWorkshopRowVm
    {
        string Label { get; }

        /// <summary>Right-hand value ("120s", "Ctrl+T", "[100]"). Optional.</summary>
        string? Value { get; }

        /// <summary>Renders as a mock slider track instead of a plain row.</summary>
        bool IsSlider { get; }

        /// <summary>0..1 thumb position when <see cref="IsSlider"/>.</summary>
        double SliderFraction { get; }

        /// <summary>Muted italic caption row (e.g. the Proactivity-trait override note).</summary>
        bool IsCaption { get; }

        ICommand? ActivateCommand { get; }
    }

    // =================================================================================
    //  Concrete mock rows. Public because the zone mocks and the unit tests build them.
    // =================================================================================

    public sealed class CompanionChatBubble : CompanionObservable, IChatBubbleVm
    {
        public CompanionChatBubble() { Text = string.Empty; }

        public CompanionChatBubble(CompanionBubbleKind kind, string text, bool isAi = false, string? timestamp = null)
        {
            Kind = kind;
            Text = text;
            IsAiGenerated = isAi;
            Timestamp = timestamp;
        }

        public CompanionBubbleKind Kind { get; init; }
        public string Text { get; init; }
        public bool IsAiGenerated { get; init; }
        public string? Timestamp { get; init; }
    }

    public sealed class CompanionMemoryFact : CompanionObservable, IMemoryFactVm
    {
        private bool _isEditing;
        private string _editText = string.Empty;

        public CompanionMemoryFact()
        {
            Id = Guid.NewGuid().ToString("N");
            Text = string.Empty;
            KindKey = "moment";
            KindLabel = string.Empty;
            MetaLabel = string.Empty;
            PinCommand = CompanionRelayCommand.NoOp("fact.pin");
            EditCommand = new CompanionRelayCommand(() => IsEditing = true);
            ForgetCommand = CompanionRelayCommand.NoOp("fact.forget");
            CommitEditCommand = new CompanionRelayCommand(() => IsEditing = false);
        }

        public string Id { get; init; }
        public string Text { get; init; }
        public string KindKey { get; init; }
        public string KindLabel { get; init; }
        public string MetaLabel { get; init; }
        public bool IsBoundary { get; init; }
        public bool IsPinned { get; init; }
        public bool IsDormant { get; init; }

        public bool IsEditing
        {
            get => _isEditing;
            set { if (Set(ref _isEditing, value) && value) EditText = Text; }
        }

        public string EditText
        {
            get => _editText;
            set => Set(ref _editText, value);
        }

        public ICommand PinCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ForgetCommand { get; }
        public ICommand CommitEditCommand { get; }

        /// <summary>Boundary ▸ pinned ▸ normal ▸ dormant. See <see cref="FactOrdering.SortRank"/>.</summary>
        public int SortRank => FactOrdering.SortRank(IsBoundary, IsPinned, IsDormant);
    }

    public sealed class CompanionFactFilter : CompanionObservable, IFactFilterVm
    {
        private bool _isSelected;

        public CompanionFactFilter() { Key = "all"; Label = "all"; }

        public CompanionFactFilter(string key, string label, bool selected = false)
        {
            Key = key;
            Label = label;
            _isSelected = selected;
        }

        public string Key { get; init; }
        public string Label { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }

    public sealed class CompanionProfileStat : IProfileStatVm
    {
        public CompanionProfileStat() { Label = string.Empty; }
        public CompanionProfileStat(string label) { Label = label; }
        public string Label { get; init; }
    }

    public sealed class CompanionConstellationNode : CompanionObservable, IConstellationNodeVm
    {
        private ConstellationNodeState _state;

        public CompanionConstellationNode()
        {
            Name = string.Empty;
            Glyph = "✧";
            Description = string.Empty;
        }

        public int Index { get; init; }
        public string Name { get; init; }
        public string Glyph { get; init; }
        public string Description { get; init; }

        public ConstellationNodeState State
        {
            get => _state;
            set => Set(ref _state, value);
        }
    }

    public sealed class CompanionTraitGauge : ITraitGaugeVm
    {
        public CompanionTraitGauge() { Label = string.Empty; }

        public CompanionTraitGauge(string label, int value)
        {
            Label = label;
            Value = value < 0 ? 0 : (value > 100 ? 100 : value);
        }

        public string Label { get; init; }
        public int Value { get; init; }
        public double Fraction => Value / 100.0;
    }

    public sealed class CompanionPresetChip : CompanionObservable, IPresetChipVm
    {
        private bool _isSelected;

        public CompanionPresetChip() { Id = string.Empty; Label = string.Empty; }

        public CompanionPresetChip(string id, string label, bool selected = false)
        {
            Id = id;
            Label = label;
            _isSelected = selected;
        }

        public string Id { get; init; }
        public string Label { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }

    public sealed class CompanionDenyChip : IDenyChipVm
    {
        public CompanionDenyChip()
        {
            Label = string.Empty;
            RemoveCommand = CompanionRelayCommand.NoOp("deny.remove");
        }

        public CompanionDenyChip(string label, bool seeded = true) : this()
        {
            Label = label;
            IsSeeded = seeded;
        }

        /// <summary>
        /// A chip that really lifts a rule. The mocks keep the no-op ctor above; the wired-up privacy
        /// panel hands in the command that rewrites the list, because a ✕ on a privacy control that
        /// removes nothing is the worst affordance on the card.
        /// </summary>
        public CompanionDenyChip(string label, bool seeded, ICommand remove) : this(label, seeded)
        {
            if (remove != null) RemoveCommand = remove;
        }

        public string Label { get; init; }
        public bool IsSeeded { get; init; }
        public ICommand RemoveCommand { get; }
    }

    public sealed class CompanionWorkshopCell : IWorkshopCellVm
    {
        private string? _key;

        public CompanionWorkshopCell()
        {
            Title = string.Empty;
            Rows = Array.Empty<IWorkshopRowVm>();
        }

        public CompanionWorkshopCell(string title, params IWorkshopRowVm[] rows)
        {
            Title = title;
            Rows = rows;
        }

        /// <summary>
        /// A cell built with a title alone is its own anchor — that is the pre-split behaviour and
        /// what every design-time cell wants. The wiring pass sets this explicitly so a localized
        /// heading can move without the deep links following it.
        /// </summary>
        public string Key
        {
            get => string.IsNullOrEmpty(_key) ? Title : _key!;
            init => _key = value;
        }

        public string Title { get; init; }
        public IReadOnlyList<IWorkshopRowVm> Rows { get; init; }

        /// <summary>The re-parented control, when this cell holds one. Null on every mock cell.</summary>
        public object? Content { get; init; }
    }

    public sealed class CompanionWorkshopRow : IWorkshopRowVm
    {
        public CompanionWorkshopRow() { Label = string.Empty; }

        public CompanionWorkshopRow(string label, string? value = null)
        {
            Label = label;
            Value = value;
        }

        /// <summary>A mock slider row: label, track with a thumb at <paramref name="fraction"/>, value.</summary>
        public static CompanionWorkshopRow Slider(string label, string value, double fraction)
            => new(label, value) { IsSlider = true, SliderFraction = fraction };

        /// <summary>A muted italic note row.</summary>
        public static CompanionWorkshopRow Caption(string label)
            => new(label) { IsCaption = true };

        public string Label { get; init; }
        public string? Value { get; init; }
        public bool IsSlider { get; init; }
        public double SliderFraction { get; init; }
        public bool IsCaption { get; init; }
        public ICommand? ActivateCommand { get; init; }
    }
}
