using System;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z6 — the attention meter. See the XAML header for the visual spec.
    ///
    /// <para>Code-free by design: the copy ladder is a pure function of the remaining fraction
    /// (AttentionCopy), the bar is a star-width column, and detail-on-demand is a command on the
    /// viewmodel. Nothing here animates.</para>
    /// </summary>
    public partial class AttentionGaugeView : UserControl
    {
        public AttentionGaugeView()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new AttentionGaugeViewModel();
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public AttentionGaugeViewModel? ViewModel
        {
            get => DataContext as AttentionGaugeViewModel;
            set => DataContext = value;
        }
    }

    /// <summary>
    /// Supplies everything the view binds to. The strings come from CCP.Core's <see cref="Loc"/> -
    /// the same localization runtime and the same JSON files the WPF head reads.
    ///
    /// <para>This exists because WPF's <c>{loc:Str key}</c> markup extension (LocExtension)
    /// cannot cross: it derives from System.Windows.Markup.MarkupExtension and stays in the head.
    /// The strings do cross; only the binding mechanism differs.</para>
    ///
    /// <para><b>Not a port of IAttentionGaugeVm.</b> That interface, MockAttentionGaugeVm and
    /// AttentionCopy all still live in the WPF head, so the thresholds cannot be reached from
    /// here and are deliberately NOT re-implemented - a second copy of the ladder is exactly the
    /// duplication the Core split exists to prevent. The numbers below are the WPF mock's
    /// artboard state (72% left) hard-coded, and every derived flag is the value AttentionCopy
    /// returns for it. Wiring the real viewmodel is a separate change from proving the view
    /// renders.</para>
    /// </summary>
    public sealed class AttentionGaugeViewModel : INotifyPropertyChanged
    {
        private bool _isDetailShown;

        public AttentionGaugeViewModel()
        {
            ToggleDetailCommand = new ToggleCommand(() => IsDetailShown = !IsDetailShown);
        }

        public string LocTitle => Loc.Get("companion_attention_title");
        public string LocTagTrain1 => Loc.Get("companion_tag_train1");
        public string LocDetailTip => Loc.Get("companion_attention_detail_tip");

        /// <summary>The headline copy. AttentionCopy.CopyKeyFor(0.72) is the "plenty" rung.</summary>
        public string StateCopy => Loc.Get("companion_attention_plenty");

        /// <summary>
        /// Numeric detail, on demand only. Note what it never says: "tokens".
        ///
        /// <para>This is the MOCK's static key. The shipped VM
        /// (AttentionGaugeRuntimeVm, CompanionDepthRuntimeVms.cs:304-306) formats it instead -
        /// <c>Loc.GetF("companion_attention_detail_fmt", remaining)</c>, or
        /// <c>companion_attention_detail_unlimited</c> when she thinks on the user's machine.
        /// Whoever wires the real chat budget in must switch to those two, not to this.</para>
        /// </summary>
        public string DetailLine => Loc.Get("companion_attention_detail_line");

        /// <summary>The barks-only floor promise, at rest, in her voice.</summary>
        public string FloorNote => Loc.Get("companion_attention_floor_note");

        public string UpsellCopy => Loc.Get("companion_attention_upsell");

        // Placeholder state: the WPF MockAttentionGaugeVm's default artboard, 72% remaining.
        public double BarFraction => 0.72;
        public bool IsSpent => false;
        public bool ShowFloorNote => false;
        public bool ShowUpsell => false;

        public bool IsDetailShown
        {
            get => _isDetailShown;
            set
            {
                if (_isDetailShown == value) return;
                _isDetailShown = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDetailShown)));
            }
        }

        public ICommand ToggleDetailCommand { get; }

        /// <summary>
        /// LEFT UNWIRED. The WPF original hands this to the Companion tab, which deep-links to the
        /// Patreon tab - head-owned navigation with no Avalonia counterpart yet. A null command
        /// disables the link rather than pretending the click did something.
        /// </summary>
        public ICommand? UpsellCommand => null;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>The one command this view can honour on its own: it only flips local state.</summary>
        private sealed class ToggleCommand : ICommand
        {
            private readonly Action _run;
            public ToggleCommand(Action run) => _run = run;
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _run();
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }
    }
}
