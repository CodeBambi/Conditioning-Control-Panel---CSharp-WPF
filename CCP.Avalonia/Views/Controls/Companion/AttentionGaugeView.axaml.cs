using System;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Views.Controls.Companion;

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
            DataContext = new AttentionGaugeViewModel(() => TopLevel.GetTopLevel(this) as Windows.MainShellWindow);
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public AttentionGaugeViewModel? ViewModel
        {
            get => DataContext as AttentionGaugeViewModel;
            set => DataContext = value;
        }
    }

    /// <summary>
    /// Port of WPF <c>AttentionGaugeRuntimeVm</c> (CompanionDepthRuntimeVms.cs:274-367). The copy
    /// ladder is CCP.Core's <see cref="AttentionCopy"/>, shared with WPF; the budget is the same
    /// client mirror of <c>requests_remaining</c> (App.Ai) measured against the same ceiling.
    /// </summary>
    public sealed class AttentionGaugeViewModel : INotifyPropertyChanged
    {
        private readonly Func<Windows.MainShellWindow?> _shell;
        private bool _isDetailShown;
        private double _fraction = 1.0;
        private int _remaining;
        private bool _unlimited;

        public AttentionGaugeViewModel(Func<Windows.MainShellWindow?>? shell = null)
        {
            _shell = shell ?? (() => null);
            ToggleDetailCommand = new ToggleCommand(() => IsDetailShown = !IsDetailShown);
            UpsellCommand = new ToggleCommand(() => Shell?.ShowTab("patreon"));
            Sync();
        }

        /// <summary>The shell the upsell navigates (the view's top level).</summary>
        internal Windows.MainShellWindow? Shell => _shell();

        public string LocTitle => Loc.Get("companion_attention_title");
        public string LocTagTrain1 => Loc.Get("companion_tag_train1");
        public string LocDetailTip => Loc.Get("companion_attention_detail_tip");

        /// <summary>0..1 of today's chats left (1 when unlimited).</summary>
        public double Fraction => _fraction;
        public double BarFraction => AttentionCopy.BarFractionFor(_fraction);
        public bool IsSpent => AttentionCopy.IsSpent(_fraction);
        public string StateCopy => Loc.Get(AttentionCopy.CopyKeyFor(_fraction));

        /// <summary>Numeric detail, on demand only. Never says "tokens".</summary>
        public string DetailLine => _unlimited
            ? Loc.Get("companion_attention_detail_unlimited")
            : Loc.GetF("companion_attention_detail_fmt", _remaining);

        /// <summary>The barks-only floor promise, at rest, in her voice.</summary>
        public string FloorNote => Loc.Get("companion_attention_floor_note");
        public bool ShowFloorNote => AttentionCopy.ShowFloorNote(_fraction);

        /// <summary>WPF: not unlimited, under 40%, and no tier-1 AI access (App.Patreon.HasAiAccess).</summary>
        public bool ShowUpsell => !_unlimited && AttentionCopy.ShowUpsell(_fraction) && !CoreAccount.HasPremiumAccess;
        public string UpsellCopy => Loc.Get("companion_attention_upsell");

        /// <summary>Re-reads the request mirror (WPF AttentionGaugeRuntimeVm.Sync). Raises only on change.</summary>
        public void Sync()
        {
            try
            {
                var (remaining, limit) = ReadBudget();
                bool unlimited = limit <= 0;
                double f = unlimited ? 1.0 : FractionFor(remaining, limit);
                if (remaining == _remaining && unlimited == _unlimited && f == _fraction) return;
                _remaining = remaining;
                _unlimited = unlimited;
                _fraction = f;
                foreach (var n in new[] { nameof(Fraction), nameof(BarFraction), nameof(IsSpent), nameof(StateCopy),
                                          nameof(DetailLine), nameof(ShowFloorNote), nameof(ShowUpsell) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Companion room: attention sync failed"); }
        }

        /// <summary>0..1, clamped (WPF FractionFor).</summary>
        internal static double FractionFor(int remaining, int limit)
        {
            if (limit <= 0) return 1.0;
            double f = (double)remaining / limit;
            return f < 0 ? 0 : (f > 1 ? 1 : f);
        }

        /// <summary>
        /// WPF ReadBudget: the local provider reports -1 remaining and a BYO endpoint with
        /// <c>DailyRequestLimit == 0</c> has no cap - both are "unlimited" (limit 0).
        /// </summary>
        private static (int Remaining, int Limit) ReadBudget()
        {
            int remaining = App.Ai?.DailyRequestsRemaining ?? 0;
            if (remaining < 0) return (0, 0);
            var settings = CoreSettings.Current;
            var provider = settings?.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud;
            return provider switch
            {
                AiProviderType.Local => (0, 0),
                AiProviderType.OpenAiCompatible => (remaining, settings?.CompanionPrompt?.DailyRequestLimit ?? 0),
                _ => (remaining, ConditioningControlPanel.Services.AiService.EffectiveDailyLimit)
            };
        }

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

        /// <summary>WPF: the Patreon tab (ShowTab("patreon")).</summary>
        public ICommand UpsellCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>A constant-CanExecute relay (both commands are always armed, as in WPF).</summary>
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
