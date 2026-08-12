using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Click-to-open tile for a feature on the dashboard grid. Shows an icon + title;
    /// when locked, desaturates the content and overlays a padlock + required level.
    ///
    /// FX (PR-2 of the overhaul), all of it opacity-only and all of it colour-driven from
    /// FxTheme so a mod switch re-tints the mosaic without touching this file:
    ///   * hover  = <see cref="MotionFx.HoverLift"/> on the root border + a 150ms rim-light;
    ///   * active = the ActiveGlow drop shadow and the ring breathe together on one 3.5s clock.
    /// Both clocks are gated on <see cref="MotionFx"/> + <see cref="PerformanceProfile"/> and stop
    /// when the window is deactivated or minimised; the resting state is the static art that
    /// shipped before, never "the same thing, slower".
    /// </summary>
    public partial class FeatureCard : UserControl
    {
        private const double ActiveGlowMinOpacity = 0.50;
        private const double ActiveGlowMaxOpacity = 0.90;
        private const double ActiveRingMinOpacity = 0.55;
        private const double ActiveRingMaxOpacity = 1.00;
        private const double ActiveBreathSeconds = 3.5;
        private const double RimLightOpacity = 0.85;
        private const int RimLightMs = 150;
        private const int AmbientFrameRate = 24;

        private Window? _hostWindow;
        private bool _hovered;

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(FeatureCard),
                new PropertyMetadata("Feature", OnTitleChanged));

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(FeatureCard),
                new PropertyMetadata(null, OnIconChanged));

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(FeatureCard),
                new PropertyMetadata(null, OnGlyphChanged));

        public static readonly DependencyProperty LockLevelProperty =
            DependencyProperty.Register(nameof(LockLevel), typeof(int), typeof(FeatureCard),
                new PropertyMetadata(0, OnLockStateChanged));

        public static readonly DependencyProperty IsLockedProperty =
            DependencyProperty.Register(nameof(IsLocked), typeof(bool), typeof(FeatureCard),
                new PropertyMetadata(false, OnLockStateChanged));

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(FeatureCard),
                new PropertyMetadata(false, OnActiveStateChanged));

        public static readonly DependencyProperty HelpSectionIdProperty =
            DependencyProperty.Register(nameof(HelpSectionId), typeof(string), typeof(FeatureCard),
                new PropertyMetadata(null, OnHelpSectionIdChanged));

        public static readonly DependencyProperty TierBadgeProperty =
            DependencyProperty.Register(nameof(TierBadge), typeof(string), typeof(FeatureCard),
                new PropertyMetadata(null, OnTierBadgeChanged));

        public static readonly RoutedEvent ClickEvent =
            EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(FeatureCard));

        public static readonly RoutedEvent ToggleRequestedEvent =
            EventManager.RegisterRoutedEvent(nameof(ToggleRequested), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(FeatureCard));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public ImageSource? Icon
        {
            get => (ImageSource?)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public string? Glyph
        {
            get => (string?)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        /// <summary>Required level for this feature. 0 means always unlocked.</summary>
        public int LockLevel
        {
            get => (int)GetValue(LockLevelProperty);
            set => SetValue(LockLevelProperty, value);
        }

        public bool IsLocked
        {
            get => (bool)GetValue(IsLockedProperty);
            set => SetValue(IsLockedProperty, value);
        }

        /// <summary>
        /// Highlights the card with a pink glow + border when the underlying
        /// feature is enabled in settings.
        /// </summary>
        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        /// <summary>
        /// ID of the entry in <see cref="Services.HelpContentService"/> whose rich
        /// tooltip should be shown when hovering the "?" icon. Null/unknown IDs
        /// hide the icon entirely.
        /// </summary>
        public string? HelpSectionId
        {
            get => (string?)GetValue(HelpSectionIdProperty);
            set => SetValue(HelpSectionIdProperty, value);
        }

        /// <summary>
        /// Short price tag shown as a pill in the card's top-left corner — "TIER 1", "LAB",
        /// "1 / WEEK", "SOON". Null or blank hides it, which is how a free tile says "free":
        /// by carrying no tag at all, so the only badges on the wall are the ones that cost
        /// something.
        ///
        /// <para>This is presentation only. The refusal lives in <see cref="Services.TierGate"/>,
        /// which the click handler consults, so a badge and its refusal cannot drift apart the
        /// way a hand-written "premium" label would. Deliberately NOT <see cref="IsLocked"/>:
        /// that veils the card to 35% and hides the art these tiles exist to show.</para>
        /// </summary>
        public string? TierBadge
        {
            get => (string?)GetValue(TierBadgeProperty);
            set => SetValue(TierBadgeProperty, value);
        }

        public event RoutedEventHandler Click
        {
            add => AddHandler(ClickEvent, value);
            remove => RemoveHandler(ClickEvent, value);
        }

        /// <summary>
        /// Raised on right-click so the dashboard can quick-toggle the underlying
        /// feature on/off without opening its config popup. Left-click still opens
        /// the popup via <see cref="Click"/>.
        /// </summary>
        public event RoutedEventHandler ToggleRequested
        {
            add => AddHandler(ToggleRequestedEvent, value);
            remove => RemoveHandler(ToggleRequestedEvent, value);
        }

        public FeatureCard()
        {
            InitializeComponent();
            ApplyLockState();
            // Build the help tooltip once the card is in the visual tree so that
            // FindResource can walk up into the owning Window's resources where
            // HelpButtonStyle / HelpTooltipStyle / PinkBrush live.
            Loaded += OnCardLoaded;
            Unloaded += OnCardUnloaded;
            IsVisibleChanged += OnCardVisibilityChanged;
            MouseEnter += OnCardMouseEnter;
            MouseLeave += OnCardMouseLeave;
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FeatureCard c) c.TxtTitle.Text = e.NewValue as string ?? "";
        }

        private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FeatureCard c) return;
            var src = e.NewValue as ImageSource;
            if (src != null)
            {
                c.ImgIconHost.Background = new ImageBrush(src)
                {
                    Stretch = System.Windows.Media.Stretch.UniformToFill,
                    AlignmentY = AlignmentY.Center
                };
                c.ImgIconHost.Visibility = Visibility.Visible;
                c.GlyphHost.Visibility = Visibility.Collapsed;
            }
            else
            {
                c.ImgIconHost.Background = null;
                if (!string.IsNullOrEmpty(c.Glyph))
                {
                    c.ImgIconHost.Visibility = Visibility.Collapsed;
                    c.GlyphHost.Visibility = Visibility.Visible;
                }
            }
        }

        private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FeatureCard c) return;
            var glyph = e.NewValue as string;
            c.TxtGlyph.Text = glyph ?? "";
            if (c.Icon == null && !string.IsNullOrEmpty(glyph))
            {
                c.GlyphHost.Visibility = Visibility.Visible;
                c.ImgIconHost.Visibility = Visibility.Collapsed;
            }
            else if (string.IsNullOrEmpty(glyph))
            {
                c.GlyphHost.Visibility = Visibility.Collapsed;
            }
        }

        private static void OnLockStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FeatureCard c) c.ApplyLockState();
        }

        private static void OnActiveStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FeatureCard c) c.ApplyActiveState();
        }

        private static void OnTierBadgeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FeatureCard c) return;
            var text = e.NewValue as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                c.TierBadgeHost.Visibility = Visibility.Collapsed;
                return;
            }
            c.TxtTierBadge.Text = text;
            c.TierBadgeHost.Visibility = Visibility.Visible;
        }

        private static void OnHelpSectionIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FeatureCard c) c.RefreshHelpTooltip();
        }

        private void RefreshHelpTooltip()
        {
            var id = HelpSectionId;
            if (string.IsNullOrWhiteSpace(id) || !Services.HelpContentService.HasContent(id))
            {
                BtnHelp.Visibility = Visibility.Collapsed;
                BtnHelp.ToolTip = null;
                Controls.HelpPopover.Clear(BtnHelp);
                return;
            }
            // If we haven't been added to the visual tree yet, resource lookup
            // will fail. Wait until Loaded rebuilds us.
            if (!IsLoaded)
            {
                BtnHelp.Visibility = Visibility.Visible;
                return;
            }
            // Interactive popover replaces the old non-interactive ToolTip; clear
            // ToolTip so the two never double-render. Attach is idempotent.
            BtnHelp.ToolTip = null;
            Controls.HelpPopover.Attach(BtnHelp, Services.HelpContentService.GetContent(id));
            BtnHelp.Visibility = Visibility.Visible;
        }

        private void ApplyLockState()
        {
            if (IsLocked)
            {
                LockedOverlay.Visibility = Visibility.Visible;
                TxtLockLabel.Text = LockLevel > 0 ? $"Lvl {LockLevel}" : "Locked";
                ContentRoot.Opacity = 0.35;
            }
            else
            {
                LockedOverlay.Visibility = Visibility.Collapsed;
                ContentRoot.Opacity = 1.0;
            }
            ApplyActiveState();
        }

        private void ApplyActiveState()
        {
            // Active state is suppressed while the card is locked — a locked feature
            // can't really be "on" even if the underlying setting is true.
            var showActive = IsActive && !IsLocked;
            ActiveBorder.Visibility = showActive ? Visibility.Visible : Visibility.Collapsed;
            ApplyActiveBreath(showActive);
        }

        // ============================== FX ==============================

        /// <summary>
        /// Re-evaluates every clock this card owns. Called on load, on activation changes, and
        /// from MainWindow when the mod or the reduced-motion setting moves.
        /// </summary>
        internal void RefreshFx()
        {
            try { ApplyActiveBreath(IsActive && !IsLocked); }
            catch (Exception ex) { App.Logger?.Debug("FeatureCard.RefreshFx: {E}", ex.Message); }
        }

        /// <summary>The single gate for this card's ambient clock: visibility + window focus +
        /// motion + tier.</summary>
        private bool AmbientAllowed
        {
            get
            {
                // A Forever clock on a collapsed tab burns a composition slot for the rest of the
                // session with nothing on screen to show for it. The card stays Loaded when its
                // tab is hidden - only IsVisible goes false - so Unloaded is not the hook that
                // catches this; IsVisibleChanged (wired in the ctor) is.
                if (!IsVisible) return false;
                var w = _hostWindow;
                if (w != null && (!w.IsActive || w.WindowState == WindowState.Minimized)) return false;
                return MotionFx.AllowAmbientLoops;
            }
        }

        /// <summary>
        /// The active tile's breath: the drop shadow and the ring share one 3.5s clock so the
        /// tile pulses as one object rather than as two overlapping effects. When ambient motion
        /// is not allowed both park at their PEAK values, which is the static look that shipped -
        /// degrading to "dimmer forever" would just look broken.
        /// </summary>
        private void ApplyActiveBreath(bool active)
        {
            try
            {
                if (ActiveGlow == null || ActiveBorder == null) return;

                if (!active)
                {
                    StopAnim(ActiveGlow, DropShadowEffect.OpacityProperty);
                    ActiveGlow.Opacity = 0;
                    ActiveBorder.BeginAnimation(OpacityProperty, null);
                    ActiveBorder.Opacity = 1;
                    return;
                }

                var tier = PerformanceProfile.CurrentTier;
                bool glow = PerformanceProfile.AllowGlow(tier) && MotionFx.Level != MotionLevel.Off;
                if (glow && !ActiveGlow.IsFrozen)
                    ActiveGlow.BlurRadius = Math.Min(18, PerformanceProfile.MaxGlowBlurRadius(tier));

                if (!AmbientAllowed)
                {
                    StopAnim(ActiveGlow, DropShadowEffect.OpacityProperty);
                    ActiveGlow.Opacity = glow ? ActiveGlowMaxOpacity : 0;
                    ActiveBorder.BeginAnimation(OpacityProperty, null);
                    ActiveBorder.Opacity = ActiveRingMaxOpacity;
                    return;
                }

                if (glow) Breathe(ActiveGlow, DropShadowEffect.OpacityProperty, ActiveGlowMinOpacity, ActiveGlowMaxOpacity);
                else { StopAnim(ActiveGlow, DropShadowEffect.OpacityProperty); ActiveGlow.Opacity = 0; }

                Breathe(ActiveBorder, OpacityProperty, ActiveRingMinOpacity, ActiveRingMaxOpacity);
            }
            catch (Exception ex) { App.Logger?.Debug("FeatureCard.ApplyActiveBreath: {E}", ex.Message); }
        }

        private static void Breathe(IAnimatable target, DependencyProperty property, double min, double max)
        {
            if (target is Freezable { IsFrozen: true }) return;
            var anim = new DoubleAnimation(min, max, TimeSpan.FromSeconds(ActiveBreathSeconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
            target.BeginAnimation(property, anim);
        }

        private static void StopAnim(IAnimatable target, DependencyProperty property)
        {
            if (target is Freezable { IsFrozen: true }) return;
            target.BeginAnimation(property, null);
        }

        private void OnCardLoaded(object sender, RoutedEventArgs e)
        {
            RefreshHelpTooltip();
            try
            {
                HookWindow(Window.GetWindow(this));
                RefreshFx();
            }
            catch (Exception ex) { App.Logger?.Debug("FeatureCard.OnCardLoaded: {E}", ex.Message); }
        }

        private void OnCardUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                UnhookWindow();
                ApplyActiveBreath(false);
            }
            catch (Exception ex) { App.Logger?.Debug("FeatureCard.OnCardUnloaded: {E}", ex.Message); }
        }

        private void HookWindow(Window? window)
        {
            if (ReferenceEquals(_hostWindow, window)) return;
            UnhookWindow();
            _hostWindow = window;
            if (_hostWindow == null) return;
            _hostWindow.Activated += OnHostWindowStateish;
            _hostWindow.Deactivated += OnHostWindowStateish;
            _hostWindow.StateChanged += OnHostWindowStateish;
        }

        private void UnhookWindow()
        {
            if (_hostWindow == null) return;
            _hostWindow.Activated -= OnHostWindowStateish;
            _hostWindow.Deactivated -= OnHostWindowStateish;
            _hostWindow.StateChanged -= OnHostWindowStateish;
            _hostWindow = null;
        }

        private void OnHostWindowStateish(object? sender, EventArgs e) => RefreshFx();

        /// <summary>Tab hidden = clock stopped, tab shown again = clock re-armed. RefreshFx is
        /// idempotent, so this needs no latch of its own.</summary>
        private void OnCardVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshFx();

        private void OnCardMouseEnter(object sender, MouseEventArgs e) => ApplyHover(true);

        private void OnCardMouseLeave(object sender, MouseEventArgs e) => ApplyHover(false);

        /// <summary>
        /// Hover: a 1.02 lift plus the rim-light. RenderTransform only, so the tile never takes
        /// layout with it - the 6px margin on RootBorder is the headroom the lift paints into.
        /// </summary>
        private void ApplyHover(bool on)
        {
            if (_hovered == on) return;
            _hovered = on;
            try
            {
                // A locked tile is not an affordance; lighting it up on hover promises a click
                // that does nothing.
                if (IsLocked) on = false;

                MotionFx.HoverLift(RootBorder, on);

                if (RimLight == null) return;
                double to = on ? RimLightOpacity : 0;
                if (!MotionFx.AllowTransitions)
                {
                    RimLight.BeginAnimation(OpacityProperty, null);
                    RimLight.Opacity = to;
                    return;
                }
                var fade = new DoubleAnimation(to, TimeSpan.FromMilliseconds(RimLightMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                RimLight.BeginAnimation(OpacityProperty, fade);
            }
            catch (Exception ex) { App.Logger?.Debug("FeatureCard.ApplyHover: {E}", ex.Message); }
        }

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            // Swallow clicks that originate inside the help button so the user
            // can hover/click the "?" without also opening the feature popup.
            if (e.OriginalSource is DependencyObject src && IsDescendantOf(src, BtnHelp))
                return;
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            // Swallow right-clicks inside the help button so they don't toggle.
            if (e.OriginalSource is DependencyObject src && IsDescendantOf(src, BtnHelp))
                return;
            // QoL: right-click is a quick on/off shortcut. A locked feature can't be
            // toggled on, so right-clicking it does nothing.
            if (IsLocked) return;
            e.Handled = true;
            RaiseEvent(new RoutedEventArgs(ToggleRequestedEvent, this));
        }

        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            var current = node;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor)) return true;
                // ContentElements (e.g. Run, Hyperlink) are not part of the visual tree —
                // VisualTreeHelper.GetParent throws "X is not a Visual or Visual3D" on them.
                // Fall back to LogicalTreeHelper for those.
                current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : System.Windows.LogicalTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
