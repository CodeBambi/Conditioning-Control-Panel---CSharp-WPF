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
        public bool IsSpent => AttentionCopy.IsSpent(Fraction);
        public bool ShowUpsell => AttentionCopy.ShowUpsell(Fraction);
        public bool ShowFloorNote => AttentionCopy.ShowFloorNote(Fraction);

        /// <summary>
        /// Resolved through the staged loc layer from the ladder key, so this mock exercises the
        /// exact same key selection the shipped viewmodel will use.
        /// </summary>
        public string StateCopy => CompanionLocStaging.Resolve(AttentionCopy.CopyKeyFor(Fraction));

        /// <summary>
        /// Numeric detail, on demand only. Note what it never says: "tokens" — and note what it no
        /// longer carries either: the floor promise, which moved to <see cref="FloorNote"/> because
        /// hiding it behind hover left the drained card saying nothing but "tomorrow~".
        /// </summary>
        public string DetailLine { get; init; } =
            CompanionLocStaging.Resolve("companion_attention_detail_line");

        /// <summary>The barks-only floor promise, at rest, in her voice.</summary>
        public string FloorNote { get; init; } =
            CompanionLocStaging.Resolve("companion_attention_floor_note");

        public string UpsellCopy { get; init; } =
            CompanionLocStaging.Resolve("companion_attention_upsell");

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
            DetailLine = CompanionLocStaging.Resolve("companion_attention_detail_line_spent")
        };

        /// <summary>
        /// 4% left — a real four percent, not the spent sliver. Exists because the two used to be
        /// indistinguishable to the view and this is the exhibit that proves they no longer are.
        /// </summary>
        public static MockAttentionGaugeVm AlmostSpent() => new(0.04);
    }
}
