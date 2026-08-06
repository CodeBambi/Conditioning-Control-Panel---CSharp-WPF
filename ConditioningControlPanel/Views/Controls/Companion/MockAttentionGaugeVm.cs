using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IAttentionGaugeVm"/>.
    ///
    /// <para>The copy is derived from <see cref="AttentionCopy"/> rather than hard-coded per state,
    /// so the mock and the shipped viewmodel cross the thresholds at exactly the same points.
    /// (The strings here are the EN masters; the real VM resolves the same keys through
    /// LocalizationManager.)</para>
    /// </summary>
    public sealed class MockAttentionGaugeVm : CompanionObservable, IAttentionGaugeVm
    {
        private bool _isDetailShown;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockAttentionGaugeVm() : this(0.72) { }

        public MockAttentionGaugeVm(double fraction)
        {
            Fraction = FractionToStarConverter.ToFraction(fraction);
            ToggleDetailCommand = new CompanionRelayCommand(() => IsDetailShown = !IsDetailShown);
            UpsellCommand = CompanionRelayCommand.NoOp("attention.upsell");
        }

        public double Fraction { get; }
        public double BarFraction => AttentionCopy.BarFractionFor(Fraction);
        public bool ShowUpsell => AttentionCopy.ShowUpsell(Fraction);

        /// <summary>
        /// Resolved through the staged loc layer from the ladder key, so this mock exercises the
        /// exact same key selection the shipped viewmodel will use.
        /// </summary>
        public string StateCopy => CompanionLocStaging.Resolve(AttentionCopy.CopyKeyFor(Fraction));

        /// <summary>
        /// Numeric detail, on demand only. Note what it never says: "tokens". The trailing clause
        /// is load-bearing — the floor is not mute, and the card has to promise that out loud.
        /// </summary>
        public string DetailLine { get; init; } =
            "~63 chats · resets at midnight · her voice never runs out — only the thinking does";

        public string UpsellCopy { get; init; } = "“want me louder? you know where the lab is~”";

        public bool IsDetailShown
        {
            get => _isDetailShown;
            set => Set(ref _isDetailShown, value);
        }

        public ICommand UpsellCommand { get; }
        public ICommand ToggleDetailCommand { get; }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>The artboard: 72% left, no upsell.</summary>
        public static MockAttentionGaugeVm Plenty() => new(0.72);

        /// <summary>Below 40%: "she's saving her best lines" plus the one quiet upsell line.</summary>
        public static MockAttentionGaugeVm Saving() => new(0.30);

        /// <summary>Below 15%.</summary>
        public static MockAttentionGaugeVm Whispering() => new(0.08);

        /// <summary>Spent: the bar keeps a sliver, barks keep playing, tomorrow is promised.</summary>
        public static MockAttentionGaugeVm Drained() => new(0.0)
        {
            DetailLine = "0 chats left · resets at midnight · barks keep playing — she never goes mute"
        };
    }
}
