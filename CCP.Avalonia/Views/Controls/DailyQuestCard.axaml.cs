using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// Everything one seat of the daily board needs to paint itself. Built by the shell's quest
    /// refresh, which owns the quest/localization/mod lookups; the card owns only how it looks.
    /// Copied from the WPF code-behind with <c>ImageSource</c> -> <see cref="IImage"/>.
    /// </summary>
    internal sealed class DailyQuestCardModel
    {
        public int Slot { get; init; }                  // zero-based
        public string Icon { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public int Current { get; init; }
        public int Target { get; init; }
        public bool IsCompleted { get; init; }
        public string XpText { get; init; } = "";
        public string? BonusText { get; init; }         // streak / reroll multipliers, or null
        public IImage? Art { get; init; }
        public bool CanReroll { get; init; }
        public string RerollText { get; init; } = "";
        public string? RerollTooltip { get; init; }
    }

    /// <summary>
    /// One card of the three-up daily board.
    ///
    /// PORTED from ConditioningControlPanel/Views/Controls/DailyQuestCard.xaml.cs. Deviations:
    ///  - <c>MotionFx</c> (HoverLift, BarFill, the reduced-motion gate) lives in the WPF head, so
    ///    the bar is seated directly and the hover glow is attached/detached with no tween.
    ///  - <c>FrameworkElement</c> -> <see cref="Control"/>, <c>Visibility</c> -> <c>IsVisible</c>,
    ///    <c>MouseEnter/Leave</c> -> <c>PointerEntered/Exited</c>, <c>ToolTip=</c> ->
    ///    <c>ToolTip.SetTip</c>, <c>App.Logger</c> -> Serilog's static <c>Log</c>.
    ///  - The reroll caption goes on an inner TextBlock, not <c>Content</c>: Avalonia would read a
    ///    "_" in it as an access key.
    ///  - The parameterless constructor seeds a sample seat so <c>--render-all</c> draws a card
    ///    with real text rather than an empty frame. Production callers overwrite it with
    ///    <see cref="Apply"/>, exactly as the WPF original does.
    ///
    /// <para><b>The bar belongs to the card.</b> Apply can run before the tab has ever been
    /// measured, and a bar cannot be filled against a zero-width track, so each card remembers its
    /// own fraction and re-applies it on its track's SizeChanged.</para>
    /// </summary>
    public partial class DailyQuestCard : UserControl
    {
        /// <summary>Raised when this seat's reroll button is pressed. The shell spends the reroll;
        /// the card never touches quest state.</summary>
        internal event EventHandler? RerollRequested;

        /// <summary>Zero-based seat index, echoed back on <see cref="RerollRequested"/> by way of
        /// the sender. Set by <see cref="Apply"/>.</summary>
        internal int Slot { get; private set; }

        /// <summary>Exposed so a burst effect can anchor at the cap of the bar that just filled.</summary>
        internal Control ProgressTrack => _track;
        internal Control ProgressFill => _fill;

        private readonly Border _root;
        private readonly Border _track;
        private readonly Border _fill;
        private readonly Border _completedOverlay;
        private readonly TextBlock _txtSlot, _txtIcon, _txtName, _txtDesc;
        private readonly TextBlock _txtXp, _txtXpBonus, _txtProgress, _txtRemaining, _txtReroll;
        private readonly Image _art;
        private readonly Button _btnReroll;

        private double _fraction;
        private bool _hasFraction;
        private DropShadowEffect? _hoverGlow;

        private static readonly IBrush GoldFill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly IBrush DoneFill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
        private static readonly IBrush RestBorder = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));
        private static readonly IBrush LiveBorder = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xD7, 0x00));
        private static readonly IBrush DoneBorder = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0xE6, 0x76));

        public DailyQuestCard()
        {
            AvaloniaXamlLoader.Load(this);

            _root = this.FindControl<Border>("Root")!;
            _track = this.FindControl<Border>("Track")!;
            _fill = this.FindControl<Border>("Fill")!;
            _completedOverlay = this.FindControl<Border>("CompletedOverlay")!;
            _txtSlot = this.FindControl<TextBlock>("TxtSlot")!;
            _txtIcon = this.FindControl<TextBlock>("TxtIcon")!;
            _txtName = this.FindControl<TextBlock>("TxtName")!;
            _txtDesc = this.FindControl<TextBlock>("TxtDesc")!;
            _txtXp = this.FindControl<TextBlock>("TxtXp")!;
            _txtXpBonus = this.FindControl<TextBlock>("TxtXpBonus")!;
            _txtProgress = this.FindControl<TextBlock>("TxtProgress")!;
            _txtRemaining = this.FindControl<TextBlock>("TxtRemaining")!;
            _txtReroll = this.FindControl<TextBlock>("TxtReroll")!;
            _art = this.FindControl<Image>("Art")!;
            _btnReroll = this.FindControl<Button>("BtnReroll")!;

            _track.SizeChanged += (_, _) => ApplyFraction();
            _root.PointerEntered += OnCardPointerEntered;
            _root.PointerExited += OnCardPointerExited;
            _btnReroll.Click += (_, _) => RerollRequested?.Invoke(this, EventArgs.Empty);

            // Render/design seat: sample data so the headless proof draws every string and a
            // partially filled bar. Overwritten by the first Apply.
            Apply(new DailyQuestCardModel
            {
                Slot = 0,
                Icon = "🌀",
                Name = "Surrender",
                Description = "Complete 5 minutes of guided breathing.",
                Current = 3,
                Target = 5,
                XpText = "+120 XP",
                BonusText = "×1.5 streak",
                CanReroll = true,
                RerollText = "Reroll this seat",
                RerollTooltip = "1 reroll left today",
            });
        }

        // ---- PAINT ---------------------------------------------------------------

        /// <summary>Paint this seat from live quest state.</summary>
        internal void Apply(DailyQuestCardModel m)
        {
            Slot = m.Slot;
            IsVisible = true;
            Opacity = 1.0;

            _txtSlot.Text = (m.Slot + 1).ToString();
            _txtIcon.Text = m.Icon;
            _txtName.Text = m.Name;
            _txtDesc.Text = m.Description;
            _txtDesc.IsVisible = !string.IsNullOrWhiteSpace(m.Description);

            _txtXp.Text = m.XpText;
            if (string.IsNullOrWhiteSpace(m.BonusText))
            {
                _txtXpBonus.IsVisible = false;
            }
            else
            {
                _txtXpBonus.Text = m.BonusText;
                _txtXpBonus.IsVisible = true;
            }

            if (m.Art != null) _art.Source = m.Art;

            // A stamped seat reads 100% whatever its stored counter says: a quest completed by a
            // tracker that overshot its target (minutes arrive in whole-minute lumps) would
            // otherwise paint a bar that is not quite full under a green check.
            double fraction = m.IsCompleted ? 1.0
                            : m.Target > 0 ? Math.Max(0, Math.Min(1.0, (double)m.Current / m.Target))
                            : 0;

            int current = m.IsCompleted ? Math.Max(m.Current, m.Target) : m.Current;
            _txtProgress.Text = $"{current} / {m.Target}";

            if (m.IsCompleted)
            {
                _completedOverlay.IsVisible = true;
                _root.BorderBrush = DoneBorder;
                _fill.Background = DoneFill;
                _txtProgress.Foreground = DoneFill;
                // The separator lives in this string rather than in a third TextBlock, so an empty
                // remainder leaves no orphaned dot on the line.
                _txtRemaining.Text = "· done";
            }
            else
            {
                _completedOverlay.IsVisible = false;
                _root.BorderBrush = LiveBorder;
                _fill.Background = GoldFill;
                _txtProgress.Foreground = GoldFill;
                int left = Math.Max(0, m.Target - m.Current);
                _txtRemaining.Text = left > 0 ? $"· {left} to go" : "";
            }

            _txtReroll.Text = m.RerollText;
            _btnReroll.IsEnabled = m.CanReroll;
            _btnReroll.IsVisible = !m.IsCompleted;
            ToolTip.SetTip(_btnReroll, m.RerollTooltip);

            SetFraction(fraction);
        }

        /// <summary>
        /// A seat the pool could not fill. Rare (it needs every legal quest to be excluded or
        /// locked), but it must render as an obviously empty seat rather than as a broken card.
        /// </summary>
        internal void ShowEmpty(int slot, string title, string subtitle)
        {
            Slot = slot;
            IsVisible = true;
            Opacity = 0.55;

            _txtSlot.Text = (slot + 1).ToString();
            _txtIcon.Text = "";
            _txtName.Text = title;
            _txtDesc.Text = subtitle;
            _txtDesc.IsVisible = true;
            _txtXp.Text = "";
            _txtXpBonus.IsVisible = false;
            _art.Source = null;
            _completedOverlay.IsVisible = false;
            _root.BorderBrush = RestBorder;
            _txtProgress.Text = "";
            _txtRemaining.Text = "";
            _btnReroll.IsVisible = false;

            SetFraction(0);
        }

        // ---- BAR -----------------------------------------------------------------

        private void SetFraction(double fraction)
        {
            if (double.IsNaN(fraction)) fraction = 0;
            _fraction = Math.Max(0, Math.Min(1, fraction));
            _hasFraction = true;
            ApplyFraction();
        }

        /// <summary>
        /// Seat the fill against the track's CURRENT width. Called both from Apply (which can run
        /// before the tab has ever been measured, in which case the track is 0 wide and this is a
        /// no-op) and from the track's SizeChanged, which is what finally lands it.
        /// ponytail: MotionFx is NOT coming to Core - it is WPF Storyboard code and Core has no
        /// UI at all, so "wired when it moves" was never true. The Avalonia shape is a table of
        /// tween closures stepped off one shared ~16ms DispatcherTimer (CCP.Avalonia/Views/Windows/
        /// ChaosHudWindow.axaml.cs is the worked example); Animation.RunAsync cannot be used here
        /// because TransformAnimator seizes the target visual's RenderTransform. The width
        /// is set directly here, so the bar is correct but never animates.
        /// </summary>
        private void ApplyFraction()
        {
            try
            {
                if (!_hasFraction) return;
                double available = _track.Bounds.Width;
                if (available <= 0) return;

                _fill.Width = available * _fraction;
            }
            catch (Exception ex) { Log.Debug("DailyQuestCard bar: {E}", ex.Message); }
        }

        // ---- INTERACTION ---------------------------------------------------------

        // ponytail: MotionFx.HoverLift is WPF Storyboard code and stays head-side; the Avalonia
        // shape is the shared-DispatcherTimer tween named above. The reduced-motion gate itself is
        // NOT blocked - CoreSettings.Current.MotionLevel is in Core today. What is missing is
        // Core. The glow still attaches and clears; it just arrives at full strength instead of
        // blooming over 180ms.
        private void OnCardPointerEntered(object? sender, PointerEventArgs e)
        {
            try
            {
                _hoverGlow ??= new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0xD7, 0x00),
                    OffsetX = 0,
                    OffsetY = 0,
                    BlurRadius = 18,
                    Opacity = 0.5,
                };
                _root.Effect = _hoverGlow;
            }
            catch (Exception ex) { Log.Debug("DailyQuestCard hover in: {E}", ex.Message); }
        }

        private void OnCardPointerExited(object? sender, PointerEventArgs e)
        {
            try { _root.Effect = null; }
            catch (Exception ex) { Log.Debug("DailyQuestCard hover out: {E}", ex.Message); }
        }
    }
}
