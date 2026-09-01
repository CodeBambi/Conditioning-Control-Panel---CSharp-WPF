using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    // =====================================================================================
    //  PORTED from ConditioningControlPanel/Views/Controls/Companion/IWorkshopAccordionVm.cs,
    //  plus the Workshop slice of that folder's CompanionItemVms.cs (IWorkshopCellVm,
    //  IWorkshopRowVm, CompanionWorkshopCell, CompanionWorkshopRow). Contracts and doc comments
    //  are unchanged - the whole file is plain net8.0 already, System.Windows.Input.ICommand
    //  included, so nothing here needed rewriting. It is COPIED rather than referenced only
    //  because a partial WPF-head class cannot be shared with this assembly; folding it into
    //  CCP.Core would delete the copy, and is the obvious follow-up.
    //
    //  The Workshop's item contracts live beside their own zone rather than in a ported
    //  CompanionItemVms.cs: no other zone consumes them, and a shared file would collide with
    //  whichever sibling port lands its zone's rows first.
    // =====================================================================================

    /// <summary>
    /// Z8 — The Workshop. The second collapsed drawer: roster, behavior sliders, triggers, phrases,
    /// her library, community prompts, and the awareness cooldowns.
    ///
    /// <para>"nothing was deleted. it just stopped being the front door." The interior is today's
    /// accordions almost verbatim — this is a container move, not a rebuild, so the shape here is
    /// deliberately generic: cells of rows, which the wiring pass replaces with the real controls
    /// while preserving every x:Name the MainWindow partials write to.</para>
    /// </summary>
    public interface IWorkshopAccordionVm : INotifyPropertyChanged
    {
        /// <summary>Two-way. The hero's Switch chip opens this straight onto the roster cell.</summary>
        bool IsExpanded { get; set; }

        string DrawerNote { get; }

        /// <summary>The pigeonholes, in display order.</summary>
        IReadOnlyList<IWorkshopCellVm> Cells { get; }

        /// <summary>Scrolls a named cell into view for the hero's Switch chip and Z5's fine-tuning
        /// link. Parameter is the cell title key.</summary>
        ICommand FocusCellCommand { get; }
    }

    /// <summary>One pigeonhole in the Z8 Workshop.</summary>
    public interface IWorkshopCellVm
    {
        /// <summary>
        /// The deep-link anchor — the identity other zones point at. Stable, never localized,
        /// never shown.
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
