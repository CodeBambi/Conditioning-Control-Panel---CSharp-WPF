using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Click-to-open tile for a feature on the dashboard grid. Shows an icon + title; when
    /// locked, desaturates the content and overlays a padlock + required level. Ported from the
    /// WPF head; the property surface (Title, Icon, Glyph, LockLevel, IsLocked, IsActive,
    /// HelpSectionId, TierBadge, TeaseTier) and the Click / ToggleRequested events are the same,
    /// so a dashboard can be written against either.
    ///
    /// FX: hover = a 1.02 lift + 150ms rim-light, active = the glow and the ring breathing on one
    /// 3.5s clock. The breath pauses while the tile is not effectively visible (a hidden tab,
    /// via EffectiveVisibility), which WPF does not do.
    /// ponytail: needs MotionFx/PerformanceProfile/window focus, gate the breath when they move to Core
    /// </summary>
    public partial class FeatureCard : UserControl
    {
        private const double ActiveGlowMinOpacity = 0.50;
        private const double ActiveGlowMaxOpacity = 0.90;
        private const double ActiveRingMinOpacity = 0.55;
        private const double ActiveRingMaxOpacity = 1.00;
        private const double ActiveBreathSeconds = 3.5;
        private const double RimLightOpacity = 0.85;
        private const double HoverLiftScale = 1.02;
        private const int HoverMs = 150;
        private const double TeaseBlurRadius = 26;
        private const double TeaseBorderThickness = 2;
        private const double ContentClipRadius = 11;
        /// <summary>WPF LockedContentOpacity: the lock veil's rest opacity.</summary>
        private const double LockedContentOpacity = 0.35;
        /// <summary>WPF InactiveContentOpacity: a switched-OFF tile that opted into
        /// <see cref="DimWhenInactive"/>. Deep enough that lit tiles win the eye, clear of the lock veil.</summary>
        private const double InactiveContentOpacity = 0.62;
        /// <summary>WPF MutedTitleOpacity: chrome follows the art, a grey picture with a bright label half-says "on".</summary>
        private const double MutedTitleOpacity = 0.72;

        public static readonly StyledProperty<string> TitleProperty =
            AvaloniaProperty.Register<FeatureCard, string>(nameof(Title), "Feature");
        public static readonly StyledProperty<IImageBrushSource?> IconProperty =
            AvaloniaProperty.Register<FeatureCard, IImageBrushSource?>(nameof(Icon));
        public static readonly StyledProperty<string?> GlyphProperty =
            AvaloniaProperty.Register<FeatureCard, string?>(nameof(Glyph));
        public static readonly StyledProperty<int> LockLevelProperty =
            AvaloniaProperty.Register<FeatureCard, int>(nameof(LockLevel));
        public static readonly StyledProperty<bool> IsLockedProperty =
            AvaloniaProperty.Register<FeatureCard, bool>(nameof(IsLocked));
        public static readonly StyledProperty<bool> IsActiveProperty =
            AvaloniaProperty.Register<FeatureCard, bool>(nameof(IsActive));
        public static readonly StyledProperty<string?> HelpSectionIdProperty =
            AvaloniaProperty.Register<FeatureCard, string?>(nameof(HelpSectionId));
        public static readonly StyledProperty<string?> TierBadgeProperty =
            AvaloniaProperty.Register<FeatureCard, string?>(nameof(TierBadge));
        public static readonly StyledProperty<int> TeaseTierProperty =
            AvaloniaProperty.Register<FeatureCard, int>(nameof(TeaseTier));
        public static readonly StyledProperty<bool> DimWhenInactiveProperty =
            AvaloniaProperty.Register<FeatureCard, bool>(nameof(DimWhenInactive));

        public static readonly RoutedEvent<RoutedEventArgs> ClickEvent =
            RoutedEvent.Register<FeatureCard, RoutedEventArgs>(nameof(Click), RoutingStrategies.Bubble);
        public static readonly RoutedEvent<RoutedEventArgs> ToggleRequestedEvent =
            RoutedEvent.Register<FeatureCard, RoutedEventArgs>(nameof(ToggleRequested), RoutingStrategies.Bubble);

        public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public IImageBrushSource? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string? Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
        /// <summary>Required level for this feature. 0 means always unlocked.</summary>
        public int LockLevel { get => GetValue(LockLevelProperty); set => SetValue(LockLevelProperty, value); }
        public bool IsLocked { get => GetValue(IsLockedProperty); set => SetValue(IsLockedProperty, value); }
        /// <summary>Highlights the card with a glow + ring when the underlying feature is enabled.</summary>
        public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
        /// <summary>ID of the HelpContentService entry behind the "?" icon. Null/unknown hides it.</summary>
        public string? HelpSectionId { get => GetValue(HelpSectionIdProperty); set => SetValue(HelpSectionIdProperty, value); }
        /// <summary>Short price tag pill, top-left ("TIER 1", "LAB", "SOON"). Null/blank hides it.</summary>
        public string? TierBadge { get => GetValue(TierBadgeProperty); set => SetValue(TierBadgeProperty, value); }
        /// <summary>0 = normal card, 1 = gold tease livery, 2+ = diamond. Blurs the art, veils it, wears a "?".</summary>
        public int TeaseTier { get => GetValue(TeaseTierProperty); set => SetValue(TeaseTierProperty, value); }
        /// <summary>WPF DimWhenInactive (owner, 2026-10-09: "the buttons on dashboard for the features
        /// should be dimmed when off"): an OFF tile rests at 62% and drains to grey. Opt-in, because
        /// IsActive only means something on the feature toggles; navigation tiles never get it.</summary>
        public bool DimWhenInactive { get => GetValue(DimWhenInactiveProperty); set => SetValue(DimWhenInactiveProperty, value); }

        /// <summary>WPF DashboardDepth: the Home mosaic tile sits in a sunken socket and takes the
        /// shared lamp (DashboardCardDepth); the hover scale stands down so travel is the one motion.</summary>
        public bool DashboardDepth { get; set; }

        public event EventHandler<RoutedEventArgs> Click { add => AddHandler(ClickEvent, value); remove => RemoveHandler(ClickEvent, value); }
        /// <summary>Raised on right-click so the dashboard can quick-toggle the feature without opening its popup.</summary>
        public event EventHandler<RoutedEventArgs> ToggleRequested { add => AddHandler(ToggleRequestedEvent, value); remove => RemoveHandler(ToggleRequestedEvent, value); }

        private readonly Border _rootBorder, _imgIconHost, _imgIconMute, _glyphHost, _rimLight, _activeBorder, _lockedOverlay, _tierBadgeHost;
        private readonly Grid _contentRoot, _teaseHost;
        private readonly TextBlock _txtTitle, _txtGlyph, _txtTeaseGlyph, _txtLockLabel, _txtTierBadge;
        private readonly Button _btnHelp;
        private readonly Border _activeGlow;   // the GlowLayer sibling (CardGlow)
        private readonly ScaleTransform _rootScale = new(1, 1);
        private CancellationTokenSource? _breath;
        private IDisposable? _visibilityWatch;
        private bool _hovered;
        private readonly global::ConditioningControlPanel.Avalonia.Controls.Depth.DashboardCardDepth _depth;

        public FeatureCard()
        {
            AvaloniaXamlLoader.Load(this);
            _rootBorder = this.FindControl<Border>("RootBorder")!;
            _imgIconHost = this.FindControl<Border>("ImgIconHost")!;
            _imgIconMute = this.FindControl<Border>("ImgIconMute")!;
            _glyphHost = this.FindControl<Border>("GlyphHost")!;
            _rimLight = this.FindControl<Border>("RimLight")!;
            _activeBorder = this.FindControl<Border>("ActiveBorder")!;
            _lockedOverlay = this.FindControl<Border>("LockedOverlay")!;
            _tierBadgeHost = this.FindControl<Border>("TierBadgeHost")!;
            _contentRoot = this.FindControl<Grid>("ContentRoot")!;
            _teaseHost = this.FindControl<Grid>("TeaseHost")!;
            _txtTitle = this.FindControl<TextBlock>("TxtTitle")!;
            _txtGlyph = this.FindControl<TextBlock>("TxtGlyph")!;
            _txtTeaseGlyph = this.FindControl<TextBlock>("TxtTeaseGlyph")!;
            _txtLockLabel = this.FindControl<TextBlock>("TxtLockLabel")!;
            _txtTierBadge = this.FindControl<TextBlock>("TxtTierBadge")!;
            _btnHelp = this.FindControl<Button>("BtnHelp")!;
            _activeGlow = this.FindControl<Border>("GlowLayer")!;
            CardGlow.Bind(_activeGlow, () => CardGlow.BlurRadius);
            var depthSocket = this.FindControl<Border>("DepthSocket")!;
            _depth = new global::ConditioningControlPanel.Avalonia.Controls.Depth.DashboardCardDepth(this,
                this.FindControl<Panel>("PressFace")!, this.FindControl<Border>("DepthBevel")!,
                () => DashboardDepth, () => IsActive && !IsLocked,
                e => !IsLocked && !(e.Source is Visual src && (src == _btnHelp || _btnHelp.IsVisualAncestorOf(src))));
            Loaded += (_, _) => depthSocket.IsVisible = DashboardDepth;

            // Hover lift (WPF: MotionFx.HoverLift) as a transition on the root scale (the art pop is
            // Controls/HoverPop, driven from ApplyHover as WPF does); the 6px margin on RootBorder is the headroom the lift paints into.
            var hoverTransitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(HoverMs), Easing = new QuadraticEaseOut() },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(HoverMs), Easing = new QuadraticEaseOut() },
            };
            _rootScale.Transitions = hoverTransitions;
            _rootBorder.RenderTransform = _rootScale;

            _contentRoot.SizeChanged += (_, _) => UpdateRoundedClip();
            PointerEntered += (_, _) => ApplyHover(true);
            PointerExited += (_, _) => ApplyHover(false);
            PointerReleased += OnPointerReleased;
            Unloaded += (_, _) =>
            {
                _visibilityWatch?.Dispose();
                _visibilityWatch = null;
                ApplyActiveBreath(false);
                HelpPopover.Clear(_btnHelp);
            };
            Loaded += (_, _) =>
            {
                // Improvement over WPF: the breath parks while a hidden tab holds the tile.
                _visibilityWatch = global::ConditioningControlPanel.Avalonia.Controls.EffectiveVisibility.Watch(this, ApplyActiveState);
                ApplyActiveState(); // re-arm the breath after a detach/re-attach (tab switch)
                RefreshHelpTooltip();
            };

            ApplyLockState();
            RefreshHelpTooltip();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (_rootBorder is null) return; // fired before the XAML loaded
            if (change.Property == TitleProperty) _txtTitle.Text = Title ?? "";
            else if (change.Property == IconProperty || change.Property == GlyphProperty) ApplyArt();
            else if (change.Property == LockLevelProperty || change.Property == IsLockedProperty) ApplyLockState();
            else if (change.Property == IsActiveProperty) ApplyActiveState();
            else if (change.Property == HelpSectionIdProperty) RefreshHelpTooltip();
            else if (change.Property == TierBadgeProperty) ApplyTierBadge();
            else if (change.Property == TeaseTierProperty) { ApplyTeaseState(); ApplyMute(0); }
            else if (change.Property == DimWhenInactiveProperty) ApplyActiveState();
        }

        private void ApplyArt()
        {
            var src = Icon;
            _txtGlyph.Text = Glyph ?? "";
            // A direct draw, never an ImageBrush: the mosaic repaints every frame over its ambient
            // layer and a brush fill re-rendered its tile into a fresh surface each time (ArtFill).
            ConditioningControlPanel.Avalonia.Controls.Fx.ArtFill.Paint(_imgIconHost, src, Stretch.UniformToFill, AlignmentY.Center);
            _imgIconHost.IsVisible = src is not null;
            // The grey twin rides on top; no twin (not a bitmap) falls back to the opacity dim alone.
            var grey = src is null ? null : ArtDesaturate.Of(src);
            ConditioningControlPanel.Avalonia.Controls.Fx.ArtFill.Paint(_imgIconMute, grey, Stretch.UniformToFill, AlignmentY.Center);
            _imgIconMute.IsVisible = grey is not null;
            _glyphHost.IsVisible = src is null && !string.IsNullOrEmpty(Glyph);
        }

        private void ApplyTierBadge()
        {
            var text = TierBadge;
            if (string.IsNullOrWhiteSpace(text)) { _tierBadgeHost.IsVisible = false; return; }
            _txtTierBadge.Text = text;
            _tierBadgeHost.IsVisible = true;
            // A teased card's badge is worn in the livery metal, not in pink; the two properties
            // are written in whichever order the caller happens to use.
            if (TeaseTier > 0) ApplyTeaseState();
        }

        /// <summary>
        /// Puts on (or takes off) the tease costume. Reversible: TeaseTier = 0 rebinds the border
        /// and badge brushes to the theme resources instead of freezing them.
        /// ponytail: TierFxBorder's living-metal rim stays in the head; the tease rim is static here
        /// </summary>
        private void ApplyTeaseState()
        {
            if (TeaseTier <= 0)
            {
                _teaseHost.IsVisible = false;
                _imgIconHost.Effect = null;
                _glyphHost.Effect = null;
                _rootBorder[!Border.BorderBrushProperty] = this.GetResourceObservable("GlassBorderBrush").ToBinding();
                _rootBorder.BorderThickness = new Thickness(1);
                _tierBadgeHost[!Border.BorderBrushProperty] = this.GetResourceObservable("PinkBrush").ToBinding();
                _txtTierBadge[!TextBlock.ForegroundProperty] = this.GetResourceObservable("PinkBrush").ToBinding();
                return;
            }

            // Bound to the resource, not copied: a local value would be overwritten by the XAML
            // DynamicResource re-emitting on every attach, and misses before the card is attached.
            var liveryKey = TeaseTier >= 2 ? "Tier2DiamondBorderBrush" : "Tier1GoldBorderBrush";
            var livery = this.GetResourceObservable(liveryKey).ToBinding();

            // ONE BlurEffect shared by both art hosts: only one is ever visible (icon XOR glyph).
            var blur = new BlurEffect { Radius = TeaseBlurRadius };
            _imgIconHost.Effect = blur;
            _glyphHost.Effect = blur;

            _txtTeaseGlyph[!TextBlock.ForegroundProperty] = livery;
            _teaseHost.IsVisible = true;

            _rootBorder[!Border.BorderBrushProperty] = livery;
            _rootBorder.BorderThickness = new Thickness(TeaseBorderThickness);
            _tierBadgeHost[!Border.BorderBrushProperty] = livery;
            _txtTierBadge[!TextBlock.ForegroundProperty] = livery;
        }

        private void RefreshHelpTooltip()
        {
            var id = HelpSectionId;
            if (string.IsNullOrWhiteSpace(id) || !HelpContentService.HasContent(id))
            {
                HelpPopover.Clear(_btnHelp);
                _btnHelp.IsVisible = false;
                ToolTip.SetTip(_btnHelp, null);
                return;
            }

            HelpPopover.Attach(_btnHelp, HelpContentService.GetContent(id));
            ToolTip.SetTip(_btnHelp, null);
            _btnHelp.IsVisible = true;
        }

        private void ApplyLockState()
        {
            if (IsLocked)
            {
                _lockedOverlay.IsVisible = true;
                _txtLockLabel.Text = LockLevel > 0 ? $"Lvl {LockLevel}" : "Locked";
            }
            else
            {
                _lockedOverlay.IsVisible = false;
            }
            ApplyActiveState();
        }

        private void ApplyActiveState()
        {
            // A locked feature can't really be "on" even if the underlying setting is true.
            var showActive = IsActive && !IsLocked;
            _activeBorder.IsVisible = showActive;
            ApplyRestOpacity();
            ApplyMute(CardMuteRule.TransitionMs(Env.AllowTransitions, IsLoaded));
            ApplyActiveBreath(showActive);
            _depth?.Refresh();
        }

        /// <summary>
        /// WPF ApplyRestOpacity (FeatureCard.xaml.cs:563): the ONE writer for ContentRoot.Opacity.
        /// Lock, inactive-dim and hover all want that channel, so they are resolved here in priority
        /// order. A plain static assignment: the tile's face stays a cached bitmap.
        /// </summary>
        private void ApplyRestOpacity()
        {
            _contentRoot.Opacity =
                IsLocked ? LockedContentOpacity
                : DimWhenInactive && !IsActive && (DashboardDepth || !_hovered) ? InactiveContentOpacity
                : 1.0;
        }

        /// <summary>WPF ApplyMute (:586): the ONE writer for the grey layer and the title's muted
        /// weight, on <see cref="CardMuteRule"/>'s verdict. A one-shot fade, or a snap.</summary>
        private void ApplyMute(int ms)
        {
            bool mute = CardMuteRule.ShouldMute(DimWhenInactive, IsActive, IsLocked, _hovered && !DashboardDepth, TeaseTier > 0);
            _txtTitle.Opacity = mute ? MutedTitleOpacity : 1.0;
            CardMuteRule.Fade(_imgIconMute, mute, ms);
            IsMuted = mute;
        }

        /// <summary>Test seams: the resting face opacity, the title weight, the glow layer, and the
        /// mute verdict (the grey layer may still be mid-fade).</summary>
        internal double RestOpacity => _contentRoot.Opacity;
        internal double TitleOpacity => _txtTitle.Opacity;
        internal double GlowOpacity => _activeGlow.Opacity;
        internal bool IsMuted { get; private set; }
        internal bool HasGreyTwin => _imgIconMute.IsVisible;

        /// <summary>WPF FeatureCard.RefreshFx (:613): re-reads the motion/tier/focus gates. Called by
        /// the shell's ApplyDashboardFxLoops on a motion/performance change and window activation.</summary>
        internal void RefreshFx() => ApplyActiveState();

        /// <summary>True while the breath clock runs (test seam).</summary>
        internal bool IsBreathing => _breathClock?.IsRunning == true;

        /// <summary>WPF AmbientAllowed (:622): visibility + window focus + motion + tier.</summary>
        private bool AmbientAllowed =>
            IsEffectivelyVisible
            && (TopLevel.GetTopLevel(this) is not Window w || (w.IsActive && w.WindowState != WindowState.Minimized))
            && Env.AllowAmbientLoops;

        /// <summary>The glow and the ring share one 3.5s clock so the tile pulses as one object.
        /// WPF ApplyActiveBreath (:643): when ambient motion is not allowed both park at PEAK.</summary>
        private void ApplyActiveBreath(bool active)
        {
            _breath?.Cancel();
            _breath = null;
            _breathClock?.Stop();
            if (!active || !IsEffectivelyVisible)
            {
                _activeGlow.Opacity = 0;
                _activeBorder.Opacity = 1;
                return;
            }
            if (!AmbientAllowed)
            {
                // Mich's funnel gate (WPF :643): minimised, unfocused, reduced motion or the
                // Performance tier park both at PEAK (no glow at all where the tier forbids it).
                var tier = Env.CurrentTier;
                _activeGlow.Opacity = Env.AllowGlow(tier) && Env.Level != Models.MotionLevel.Off ? ActiveGlowMaxOpacity : 0;
                _activeBorder.Opacity = ActiveRingMaxOpacity;
                return;
            }
            _breathClock ??= new BreathClock(this, ActiveBreathSeconds);
            _breathClock.Start((_activeGlow, ActiveGlowMinOpacity, ActiveGlowMaxOpacity),
                               (_activeBorder, ActiveRingMinOpacity, ActiveRingMaxOpacity));
        }

        private BreathClock? _breathClock;

        /// <summary>Rounded clip matching RootBorder's inner arc: a Border never clips its
        /// CHILDREN to its CornerRadius, so the full-bleed art would poke square corners past the frame.</summary>
        private void UpdateRoundedClip()
        {
            var b = _contentRoot.Bounds;
            _contentRoot.Clip = b.Width <= 0 || b.Height <= 0
                ? null
                : new RectangleGeometry(new Rect(0, 0, b.Width, b.Height)) { RadiusX = ContentClipRadius, RadiusY = ContentClipRadius };
        }

        private void ApplyHover(bool on)
        {
            if (_hovered == on) return;
            _hovered = on;
            // WPF OnCardMouseEnter/Leave: the hover hands the colour straight back (off the dashboard).
            ApplyRestOpacity();
            ApplyMute(CardMuteRule.TransitionMs(Env.AllowTransitions, IsLoaded));
            // A locked tile is not an affordance; lighting it up promises a click that does nothing.
            if (IsLocked) on = false;
            _rootScale.ScaleX = _rootScale.ScaleY = on && !DashboardDepth ? HoverLiftScale : 1;
            if (on && !DashboardDepth) HoverPop.Enter(_imgIconHost); else HoverPop.Leave(_imgIconHost);
            _rimLight.Opacity = on ? RimLightOpacity : 0;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            // Swallow clicks that originate inside the help button so the user can hover/click
            // the "?" without also opening the feature popup.
            if (e.Source is Visual src && (src == _btnHelp || _btnHelp.IsVisualAncestorOf(src))) return;

            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                RaiseEvent(new RoutedEventArgs(ClickEvent, this));
            }
            else if (e.InitialPressMouseButton == MouseButton.Right)
            {
                // Right-click is a quick on/off shortcut. A locked feature can't be toggled on.
                if (IsLocked) return;
                e.Handled = true;
                RaiseEvent(new RoutedEventArgs(ToggleRequestedEvent, this));
            }
        }
    }
}
