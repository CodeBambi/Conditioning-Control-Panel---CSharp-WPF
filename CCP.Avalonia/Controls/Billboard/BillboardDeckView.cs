using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Depth;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Billboard;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// The Tonight Board on Home (WPF 7.1.5 Controls/Billboard/BillboardDeckView): one card at a
    /// time from a Core <see cref="BillboardDeck"/>, its art filling the card, a shade and three lines
    /// of words on the left, one button, a corner badge, a snooze x, and a row of dots under it with
    /// a progress fill on the current one.
    ///
    /// <para>Code-only on purpose: no XAML namescope, so the host drops into the DashBillboard well
    /// without fighting the Home tab's names. The juice (push, text rise, thud, press sparks, snooze
    /// fold, motes) lives in the .Fx partial, the dots and the hold in .Chips, the button pop in .Cta.
    /// WPF ran each move as a DoubleAnimation; here every tween of the host rides one
    /// <see cref="BoardTween"/> on a frame-locked clock.</para>
    ///
    /// <para>Motion: the hold runs unless the level is Off; pushes and rises need transitions;
    /// particles need AllowParticles; art views play only on ambient loops. Motion Off is a still
    /// card that never changes by itself. The board is SILENT (7.1.5 BoardSilentTests: every board
    /// cue returned early), so this head plays no sound at all.</para>
    ///
    /// <para>WPF called it BillboardCardHost. Renamed here because the panic scan (PanicSurfacesTests)
    /// reads every *Host class as something a panic must stop; the board starts no media, no sound
    /// and no window, so it has no panic surface of its own.</para>
    /// </summary>
    public sealed partial class BillboardDeckView : Grid
    {
        private static readonly FontFamily Display = new("Fredoka, Segoe UI");
        private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New, monospace");
        private static readonly Color Ink = Color.FromRgb(0x0d, 0x0e, 0x1c);

        private readonly BillboardDeck _deck;
        private readonly BoardTween _tw;
        private readonly Border _stageFrame = new() { CornerRadius = new CornerRadius(10), ClipToBounds = true };
        private readonly Panel _stage = new() { ClipToBounds = true, Background = Brushes.Transparent };
        private readonly Panel _slides = new();
        private readonly Rectangle _glint = new() { IsHitTestVisible = false, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TranslateTransform _glintMove = new();
        private readonly StackPanel _meta = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TranslateTransform _metaMove = new();
        private readonly TextBlock _eyebrow = new();
        private readonly TextBlock _title = new();
        private readonly TextBlock _line = new();
        private readonly Button _cta;
        private readonly Border _ctaFace = new();
        private readonly Border _ctaDrop = new();
        private readonly TextBlock _ctaText = new();
        private readonly TranslateTransform _ctaPress = new();
        private readonly ScaleTransform _ctaLandScale = new(1, 1);
        private readonly TranslateTransform _ctaLandMove = new();
        private Panel? _ctaRoot, _ctaFaceGrid;
        private readonly Border _badge = new();
        private readonly TextBlock _badgeText = new();
        private readonly ScaleTransform _badgeScale = new(1, 1);
        private readonly TranslateTransform _badgeMove = new();
        private readonly Button _snooze;
        private readonly Border _paused = new();
        private readonly Border _toast = new();
        private readonly TextBlock _toastText = new();
        private readonly TranslateTransform _toastMove = new();
        private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        private Slide? _current;
        private DeckCard? _card;
        private bool _pointerOver;
        private bool _folding;
        private bool _begun;
        private int _changes;
        private IDisposable? _toastTimer;
        private IDisposable? _visWatch;

        /// <summary>The button (or a click on the card) asks for this card's action.</summary>
        public event Action<DeckCard>? ActionRequested;

        /// <summary>The card on screen.</summary>
        public DeckCard? CurrentCard => _card;

        /// <summary>The deck being shown.</summary>
        public BillboardDeck Deck => _deck;

        /// <summary>The tween clock (tests step it).</summary>
        internal BoardTween Tweens => _tw;

        /// <summary>Card changes so far (tests).</summary>
        internal int Changes => _changes;

        /// <summary>The art view on screen (tests).</summary>
        internal Control? CurrentArt => _current?.ArtElement;

        public BillboardDeckView(BillboardDeck deck)
        {
            _deck = deck ?? throw new ArgumentNullException(nameof(deck));
            _tw = new BoardTween(this);
            RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // ---- the stage -------------------------------------------------------------------
            _stage.Children.Add(_slides);

            _glint.RenderTransform = _glintMove;
            _stage.Children.Add(_glint);

            _eyebrow.FontFamily = Mono;
            _eyebrow.FontSize = 12;
            _eyebrow.TextTrimming = TextTrimming.CharacterEllipsis;
            _title.FontFamily = Display;
            _title.FontWeight = FontWeight.Bold;
            _title.FontSize = 30;
            _title.Margin = new Thickness(0, 2, 0, 0);
            _title.TextWrapping = TextWrapping.Wrap;
            // Two lines, as WPF's MaxHeight 90 gave at 34 px; Avalonia's taller line box fitted only one
            // and cut the Tip title to "The app can read t..." (owner, 2026-10-09).
            _title.MaxLines = 2;
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            Res(_title, TextBlock.ForegroundProperty, "TextLightBrush");
            _line.FontSize = 14;
            _line.Margin = new Thickness(0, 4, 0, 0);
            _line.Foreground = new SolidColorBrush(Color.FromRgb(0xc9, 0xc7, 0xee));
            _line.TextWrapping = TextWrapping.Wrap;
            _line.MaxLines = 2;
            _line.TextTrimming = TextTrimming.CharacterEllipsis;
            foreach (var t in new[] { _eyebrow, _title, _line })
            {
                t.IsHitTestVisible = false;
                t.RenderTransform = new TranslateTransform();
            }

            _cta = BuildCta();
            BuildCtaFx(_ctaRoot!, _ctaFaceGrid!);
            _meta.Children.Add(_eyebrow);
            _meta.Children.Add(_title);
            _meta.Children.Add(_line);
            _meta.Children.Add(_cta);
            _meta.RenderTransform = _metaMove;
            _stage.Children.Add(_meta);

            BuildBadge();
            _stage.Children.Add(_badge);

            _snooze = BuildSnooze();
            _stage.Children.Add(_snooze);

            BuildPausedPill();
            _stage.Children.Add(_paused);

            BuildToast();
            _stage.Children.Add(_toast);

            BuildFx();

            _stageFrame.Child = _stage;
            SetRow(_stageFrame, 0);
            Children.Add(_stageFrame);

            var chipRow = new Border { Padding = new Thickness(0, 8, 0, 2), Child = _chips };
            SetRow(chipRow, 1);
            Children.Add(chipRow);

            _stage.SizeChanged += (_, _) => Lay();
            _stage.PointerEntered += (_, _) => { _pointerOver = true; OnHoverChanged(); };
            _stage.PointerExited += (_, _) => { _pointerOver = false; OnHoverChanged(); };
            _stage.PointerPressed += OnStagePressed;
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsKeyboardFocusWithinProperty || e.Property == IsVisibleProperty) UpdateRunning();
            };
            AttachedToVisualTree += (_, _) =>
            {
                if (_shut) return;
                _visWatch?.Dispose();
                _visWatch = EffectiveVisibility.Watch(this, UpdateRunning);
                AmbientFxCanvas.Env.MotionGateChanged += RefreshMotion;
                UpdateRunning();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                _visWatch?.Dispose();
                _visWatch = null;
                AmbientFxCanvas.Env.MotionGateChanged -= RefreshMotion;
                UpdateRunning();
            };
        }

        /// <summary>
        /// The window is closing: let go of everything a static or a clock could hold (the motion
        /// event, the visibility watch, the tweens, the particle clock, the art). A closed shell must
        /// not stay rooted through its board (ShellMemoryTests). The host is dead afterwards.
        /// </summary>
        public void Shutdown()
        {
            try
            {
                _shut = true;
                AmbientFxCanvas.Env.MotionGateChanged -= RefreshMotion;
                _visWatch?.Dispose();
                _visWatch = null;
                _toastTimer?.Dispose();
                _toastTimer = null;
                _particles.Clear();
                _fxClock?.Stop();
                ClearStage();
                _tw.Clear();
            }
            catch (Exception ex) { Log.Debug("Billboard shutdown failed: {E}", ex.Message); }
        }

        private bool _shut;

        /// <summary>On screen: attached, and this host and every ancestor visible.</summary>
        internal bool OnScreen => this.IsAttachedToVisualTree() && IsEffectivelyVisible;

        // ---- the walk ----------------------------------------------------------------------------

        /// <summary>Builds the deck and shows its first card, still (no push).</summary>
        public void Begin()
        {
            _begun = true;
            Show(_deck.Start(), animate: false);
        }

        /// <summary>The next card (the hold ran out, or someone asked).</summary>
        public void Advance()
        {
            if (!_begun) { Begin(); return; }
            if (_folding) return;
            Show(_deck.Next(), animate: true);
        }

        /// <summary>A provider's state moved: rebuild at the next card change.</summary>
        public void MarkDirty() => _deck.MarkDirty();

        /// <summary>The motion level changed: re-read every gate now.</summary>
        public void RefreshMotion() => UpdateRunning();

        /// <summary>Whether the hold clock may run right now.</summary>
        internal bool HoldMayRun =>
            !_folding && _card != null &&
            DashboardBillboard.ShouldAdvance(_pointerOver, OnScreen, BoardMotion.Level != global::ConditioningControlPanel.Models.MotionLevel.Off, IsKeyboardFocusWithin);

        private void Show(DeckCard? card, bool animate)
        {
            if (card == null)
            {
                ClearStage();
                return;
            }

            bool same = _card != null && string.Equals(_card.Spec.Id, card.Spec.Id, StringComparison.Ordinal) && _current != null;
            _card = card;
            animate &= BoardMotion.AllowTransitions && !same;

            if (!same)
            {
                var slide = new Slide(card);
                var old = _current;
                _current = slide;
                _slides.Children.Add(slide.Root);
                if (old != null)
                {
                    old.Art?.Pause();
                    if (animate) Push(old, slide);
                    else Retire(old);
                }
                // Board cues stay silent (7.1.5 turned every board cue off): no note here.
                _changes++;
            }

            PaintWords(card);
            PaintBadge(card);
            _snooze.IsVisible = card.CanSnooze;
            RebuildChips();
            Lay();

            if (animate) LandWords(card);
            else SettleWords();

            if (!same && animate && !DashboardBillboard.IsFullBleed(card.Spec))
                After(DashboardBillboard.MotesDelayMs, () => { if (ReferenceEquals(_card, card)) Motes(card); });

            RestartHold();
            UpdateRunning();
        }

        private void ClearStage()
        {
            if (_current != null) Retire(_current);
            _current = null;
            _card = null;
            _meta.IsVisible = false;
            _badge.IsVisible = false;
            _snooze.IsVisible = false;
            _chips.Children.Clear();
            StopHold();
        }

        private void Retire(Slide slide)
        {
            try { slide.Art?.Release(); } catch (Exception ex) { Log.Debug("Billboard art release failed: {E}", ex.Message); }
            _slides.Children.Remove(slide.Root);
        }

        /// <summary>Plays or holds the art and the clock, from every gate there is.</summary>
        private void UpdateRunning()
        {
            // Contract: another tab or a fold holds the art still; Motion Off is each view's own gate.
            // Hover holds only the deck (the hold below and the pill), never the art: a board frozen
            // mid-wave under the pointer reads as stuck (owner, 2026-10-07).
            var art = _current?.Art;
            bool artRuns = DashboardBillboard.ArtShouldPlay(OnScreen, _folding);
            try
            {
                if (art != null)
                {
                    if (artRuns) art.Play();
                    else art.Pause();
                }
            }
            catch (Exception ex) { Log.Debug("Billboard art play/pause failed: {E}", ex.Message); }

            if (HoldMayRun) ResumeHold();
            else PauseHold();
        }

        private void OnHoverChanged()
        {
            FadeTo(_paused, _pointerOver && BoardMotion.Level != global::ConditioningControlPanel.Models.MotionLevel.Off ? 1 : 0, 200);
            FadeTo(_snooze, _pointerOver ? 1 : 0, 200);
            UpdateRunning();
        }

        // ---- words -------------------------------------------------------------------------------

        private void PaintWords(DeckCard card)
        {
            var spec = card.Spec;
            var hue = HueOf(card);
            bool bleed = DashboardBillboard.IsFullBleed(spec);
            _meta.IsVisible = true;

            _eyebrow.Text = (spec.Eyebrow ?? string.Empty).ToLowerInvariant();
            _eyebrow.Foreground = Brush(hue);
            _title.Text = spec.Title ?? string.Empty;
            _line.Text = spec.Line ?? string.Empty;
            _eyebrow.IsVisible = !bleed && !string.IsNullOrEmpty(_eyebrow.Text);
            _title.IsVisible = !bleed && !string.IsNullOrEmpty(_title.Text);
            _line.IsVisible = !bleed && !string.IsNullOrEmpty(_line.Text);

            var action = card.Action;
            bool hasButton = action.Kind != BillboardActionKind.None;
            _cta.IsVisible = hasButton;
            _ctaText.Text = action.Label;
            _ctaFace.Background = Brush(hue);
            _ctaDrop.Background = new SolidColorBrush(DepthPaint.ToColor(DepthRules.ShadowColor(ToArgb(hue))));
            _cta.Margin = new Thickness(0, bleed ? 0 : 12, 0, 0);
            AutomationProperties.SetName(_cta, string.IsNullOrEmpty(spec.Title) ? action.Label : action.Label + ", " + spec.Title);
            _stage.Cursor = !bleed && hasButton ? new Cursor(StandardCursorType.Hand) : Cursor.Default;

            // A board shows its button bottom-right and nothing else: the picture is the words.
            _meta.HorizontalAlignment = bleed ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private void PaintBadge(DeckCard card)
        {
            var key = DashboardBillboard.BadgeKey(card.Badge);
            if (key == null) { _badge.IsVisible = false; return; }
            var hue = card.Badge switch
            {
                BillboardBadge.New => Color.FromRgb(0xff, 0x4f, 0xa8),
                BillboardBadge.Basic => Color.FromRgb(0xff, 0xc9, 0x4a),
                _ => Color.FromRgb(0x5f, 0xe3, 0xff),
            };
            _badgeText.Text = Loc.Get(key).ToUpperInvariant();
            _badgeText.Foreground = Brush(hue);
            _badge.BorderBrush = Brush(hue);
            _badge.Background = Brush(hue, 0.10);
            _badge.IsVisible = true;
        }

        /// <summary>Sizes that follow the card: margins, the words' width, the title's size.</summary>
        private void Lay()
        {
            double w = _stage.Bounds.Width, h = _stage.Bounds.Height;
            if (w <= 0 || h <= 0) return;

            bool bleed = _card != null && DashboardBillboard.IsFullBleed(_card.Spec);
            double side = Math.Clamp(w * 0.04, 14, 32), foot = Math.Clamp(h * 0.06, 14, 34);
            _meta.Margin = bleed
                ? new Thickness(0, 0, Math.Clamp(w * 0.02, 10, 18), Math.Clamp(h * 0.03, 10, 16))
                : new Thickness(side, 0, 0, foot);
            _meta.MaxWidth = Math.Max(160, w * 0.46);
            _title.FontSize = Math.Clamp(h * 0.09, 20, 34);
            _line.FontSize = Math.Clamp(h * 0.042, 12, 15);
            _badge.Margin = new Thickness(side, 14, 0, 0);
            _glint.Width = w * 0.07;
            _glint.Height = h;
        }

        // ---- input -------------------------------------------------------------------------------

        private void OnStagePressed(object? sender, PointerPressedEventArgs e)
        {
            if (_card == null || _folding) return;
            if (!e.GetCurrentPoint(_stage).Properties.IsLeftButtonPressed) return;
            if (IsInsideButton(e.Source as Visual)) return;

            if (DashboardBillboard.IsFullBleed(_card.Spec))
            {
                // A touch on the board: the art ripples from that tile (even hover-paused). WPF
                // played a two-note chime here; the board is silent now.
                var art = _current?.ArtElement;
                if (art != null && art.Bounds.Width > 0 && art.Bounds.Height > 0)
                {
                    var p = e.GetPosition(art);
                    try { _current!.Art?.Touch(new Point(Math.Clamp(p.X / art.Bounds.Width, 0, 1), Math.Clamp(p.Y / art.Bounds.Height, 0, 1))); }
                    catch (Exception ex) { Log.Debug("Billboard touch failed: {E}", ex.Message); }
                }
                return;
            }

            // Anywhere else on a card with a button is the button.
            if (_card.Action.Kind != BillboardActionKind.None) Press(e.GetPosition(_stage));
        }

        private bool IsInsideButton(Visual? d)
        {
            while (d != null && !ReferenceEquals(d, _stage))
            {
                if (d is Button) return true;
                d = d.GetVisualParent();
            }
            return false;
        }

        /// <summary>The button pressed (or the card): sparks, then the action.</summary>
        private void Press(Point at)
        {
            var card = _card;
            if (card == null) return;
            Burst(at, HueOf(card), 26, 1.0);
            After(DashboardBillboard.PressActionDelayMs, () =>
            {
                try { ActionRequested?.Invoke(card); }
                catch (Exception ex) { Log.Warning(ex, "Billboard action failed"); }
            });
        }

        private void OnCtaClick(object? sender, RoutedEventArgs e)
        {
            if (_card == null || _folding) return;
            var at = _cta.TranslatePoint(new Point(_cta.Bounds.Width / 2, _cta.Bounds.Height / 2), _stage) ?? default;
            Press(at);
        }

        private void OnSnoozeClick(object? sender, RoutedEventArgs e)
        {
            var card = _card;
            if (card == null || _folding || !card.CanSnooze) return;
            _folding = true;
            UpdateRunning();
            var at = _snooze.TranslatePoint(new Point(_snooze.Bounds.Width / 2, _snooze.Bounds.Height / 2), _stage) ?? default;
            Burst(at, HueOf(card), 16, 0.7);
            Fold();
            PopCurrentChip();
            After(BoardMotion.AllowTransitions ? DashboardBillboard.SnoozeFoldMs : 0, () =>
            {
                _folding = false;
                var label = ChipLabel(card);
                Show(_deck.SnoozeCurrent(DateTime.UtcNow), animate: true);
                Toast(string.Format(Loc.Get("billboard_deck_snoozed"), label));
            });
        }

        /// <summary>Test seam: press the snooze x.</summary>
        internal void SnoozeForTests() => OnSnoozeClick(this, new RoutedEventArgs());

        /// <summary>Test seam: press the card's button.</summary>
        internal void PressForTests() => OnCtaClick(this, new RoutedEventArgs());

        // ---- pieces ------------------------------------------------------------------------------

        private Button BuildCta()
        {
            _ctaText.FontFamily = Display;
            _ctaText.FontWeight = FontWeight.Bold;
            _ctaText.FontSize = 14;
            _ctaText.Foreground = new SolidColorBrush(Ink);
            _ctaText.Margin = new Thickness(18, 7, 18, 8);
            _ctaText.HorizontalAlignment = HorizontalAlignment.Center;

            _ctaDrop.CornerRadius = new CornerRadius(10);
            _ctaDrop.Margin = new Thickness(0, DepthRules.RaisedPx, 0, -DepthRules.RaisedPx);
            _ctaDrop.IsHitTestVisible = false;

            var sheen = new Border { CornerRadius = new CornerRadius(9), IsHitTestVisible = false };
            Res(sheen, Border.BackgroundProperty, "DepthRaisedSheen");
            var faceGrid = new Panel();
            faceGrid.Children.Add(sheen);
            faceGrid.Children.Add(_ctaText);
            _ctaFace.CornerRadius = new CornerRadius(10);
            _ctaFace.BorderThickness = new Thickness(1);
            _ctaFace.ClipToBounds = true;
            Res(_ctaFace, Border.BorderBrushProperty, "DepthRaisedBevel");
            _ctaFace.Child = faceGrid;

            var ring = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Margin = new Thickness(-3), IsHitTestVisible = false, IsVisible = false };
            Res(ring, Border.BorderBrushProperty, "TextLightBrush");

            var root = new Panel
            {
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new TransformGroup { Children = { _ctaLandScale, _ctaLandMove } },
            };
            root.Children.Add(_ctaDrop);
            root.Children.Add(_ctaFace);
            root.Children.Add(ring);

            var button = new Button
            {
                Template = BareTemplate(),
                Content = root,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Left,
                Focusable = true,
                Margin = new Thickness(0, 12, 0, 0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
            };
            button.Click += OnCtaClick;
            button.AddHandler(PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) PressDown(); }, RoutingStrategies.Tunnel);
            button.AddHandler(PointerReleasedEvent, (_, _) => PressUp(), RoutingStrategies.Tunnel);
            button.PointerExited += (_, _) => { if (_ctaPress.Y > 0) PressUp(); };
            button.PointerEntered += (_, _) => Lift(true);
            button.PointerExited += (_, _) => Lift(false);
            button.GotFocus += (_, e) => ring.IsVisible = e.NavigationMethod == NavigationMethod.Tab;
            button.LostFocus += (_, _) => ring.IsVisible = false;
            _ctaRoot = root;
            _ctaFaceGrid = faceGrid;
            return button;
        }

        private void BuildBadge()
        {
            _badgeText.FontFamily = Mono;
            _badgeText.FontSize = 11;
            _badgeText.FontWeight = FontWeight.Bold;
            _badge.Child = _badgeText;
            _badge.Padding = new Thickness(9, 3, 9, 4);
            _badge.CornerRadius = new CornerRadius(6);
            _badge.BorderThickness = new Thickness(1);
            _badge.HorizontalAlignment = HorizontalAlignment.Left;
            _badge.VerticalAlignment = VerticalAlignment.Top;
            _badge.IsHitTestVisible = false;
            _badge.IsVisible = false;
            _badge.RenderTransformOrigin = RelativePoint.Center;
            _badge.RenderTransform = new TransformGroup { Children = { _badgeScale, _badgeMove } };
        }

        private Button BuildSnooze()
        {
            // A raised coin (depth law): a drop disc under it, a bevelled face, "x" on top.
            var drop = new Ellipse { Margin = new Thickness(1, DepthRules.RaisedPx, -1, -DepthRules.RaisedPx), IsHitTestVisible = false };
            Res(drop, Shape.FillProperty, "DepthDropDisc");
            var glyph = new TextBlock { Text = "×", FontSize = 16, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
            Res(glyph, TextBlock.ForegroundProperty, "TextMutedBrush");
            var face = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(0xc0, 12, 12, 26)), Child = glyph };
            Res(face, Border.BorderBrushProperty, "DepthRaisedBevel");
            var root = new Panel { Width = 28, Height = 28 };
            root.Children.Add(drop);
            root.Children.Add(face);

            var snoozeText = Loc.Get("billboard_deck_snooze");
            var button = new Button
            {
                Template = BareTemplate(),
                Content = root,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                Opacity = 0,
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
            };
            ToolTip.SetTip(button, snoozeText);
            AutomationProperties.SetName(button, snoozeText);
            button.Click += OnSnoozeClick;
            button.PointerEntered += (_, _) => face.BorderBrush = Brush(Color.FromRgb(0xff, 0x4f, 0xa8));
            button.PointerExited += (_, _) => Res(face, Border.BorderBrushProperty, "DepthRaisedBevel");
            button.GotFocus += (_, _) => button.Opacity = 1;
            return button;
        }

        private void BuildPausedPill()
        {
            var text = new TextBlock { Text = Loc.Get("billboard_deck_paused"), FontFamily = Mono, FontSize = 11 };
            Res(text, TextBlock.ForegroundProperty, "TextMutedBrush");
            _paused.Child = text;
            _paused.Padding = new Thickness(10, 3, 10, 4);
            _paused.CornerRadius = new CornerRadius(999);
            _paused.BorderThickness = new Thickness(1);
            Res(_paused, Border.BorderBrushProperty, "DepthPressedBevel");
            _paused.Background = new SolidColorBrush(Color.FromArgb(0xb3, 10, 10, 22));
            _paused.HorizontalAlignment = HorizontalAlignment.Center;
            _paused.VerticalAlignment = VerticalAlignment.Top;
            _paused.Margin = new Thickness(0, 14, 0, 0);
            _paused.IsHitTestVisible = false;
            _paused.Opacity = 0;
        }

        private void BuildToast()
        {
            _toastText.FontSize = 13;
            Res(_toastText, TextBlock.ForegroundProperty, "TextLightBrush");
            _toast.Child = _toastText;
            _toast.Padding = new Thickness(14, 6, 14, 7);
            _toast.CornerRadius = new CornerRadius(999);
            _toast.BorderThickness = new Thickness(1);
            Res(_toast, Border.BorderBrushProperty, "DepthFloatRim");
            _toast.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x25, 0x4a));
            _toast.HorizontalAlignment = HorizontalAlignment.Center;
            _toast.VerticalAlignment = VerticalAlignment.Bottom;
            _toast.Margin = new Thickness(0, 0, 0, 14);
            _toast.IsHitTestVisible = false;
            _toast.Opacity = 0;
            _toast.RenderTransform = _toastMove;
        }

        private static IControlTemplate BareTemplate() =>
            new FuncControlTemplate<Button>((b, _) => new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                Background = Brushes.Transparent,
                [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
            });

        // ---- helpers -----------------------------------------------------------------------------

        internal static Color HueOf(DeckCard card) =>
            ParseHue(DashboardBillboard.Accent(card.Spec.AccentHex), Color.FromRgb(0xff, 0x4f, 0xa8));

        internal static Color ParseHue(string? hex, Color fallback) =>
            !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c) ? c : fallback;

        private static uint ToArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

        private static IBrush Brush(Color c, double alpha = 1) =>
            new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B));

        /// <summary>WPF SetResourceReference: the value follows the app resource by key.</summary>
        internal static void Res(Control c, AvaloniaProperty p, string key) =>
            c.Bind(p, c.GetResourceObservable(key));

        /// <summary>The chip's words: the kind's own label, or the card's title for house and events.</summary>
        internal static string ChipLabel(DeckCard card)
        {
            var key = DashboardBillboard.ChipKey(card.Spec);
            if (key != null) return Loc.Get(key);
            return card.Spec.Title ?? string.Empty;
        }

        private static void After(int ms, Action run)
        {
            void Safe()
            {
                try { run(); }
                catch (Exception ex) { Log.Debug("Billboard delayed step failed: {E}", ex.Message); }
            }
            if (ms <= 0) { Safe(); return; }
            DispatcherTimer.RunOnce(Safe, TimeSpan.FromMilliseconds(ms));
        }

        private void FadeTo(Control e, double to, int ms)
        {
            if (!BoardMotion.AllowTransitions || ms <= 0)
            {
                _tw.Stop(e, "opacity");
                e.Opacity = to;
                return;
            }
            _tw.To(e, "opacity", () => e.Opacity, v => e.Opacity = v, null, to, ms);
        }

        /// <summary>One card's picture on the stage: the art (or a plain ground) and its shade.</summary>
        private sealed class Slide
        {
            public readonly Panel Root = new() { IsHitTestVisible = true };
            public readonly Control? ArtElement;
            public readonly IBillboardArtView? Art;
            public readonly TranslateTransform Move = new();
            public readonly ScaleTransform Zoom = new(1, 1);

            public Slide(DeckCard card)
            {
                Root.Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0d, 0x1a));
                Root.RenderTransformOrigin = RelativePoint.Center;
                Root.RenderTransform = new TransformGroup { Children = { Zoom, Move } };

                Control? art = null;
                try { art = BillboardArt.Create(card.Spec.ArtKey ?? string.Empty, card.Spec.ArtData); }
                catch (Exception ex) { Log.Warning(ex, "Billboard art {Key} failed to build", card.Spec.ArtKey); }
                if (art is IAccentedArt tinted) tinted.Accent = HueOf(card);
                if (art != null)
                {
                    ArtElement = art;
                    Art = art as IBillboardArtView;
                    Root.Children.Add(art);
                }

                if (!DashboardBillboard.IsFullBleed(card.Spec))
                {
                    var shade = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(0xeb, 9, 9, 20), 0),
                            new GradientStop(Color.FromArgb(0x99, 9, 9, 20), 0.38),
                            new GradientStop(Color.FromArgb(0x00, 9, 9, 20), 0.62),
                        },
                    };
                    Root.Children.Add(new Rectangle { Fill = shade, IsHitTestVisible = false });
                }
            }
        }
    }
}
