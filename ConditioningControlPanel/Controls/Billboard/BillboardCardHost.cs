using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Launcher;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The Tonight Board on Home (2026-10-07): one card at a time from a <see cref="BillboardDeck"/>,
    /// its art filling the card, a shade and three lines of words on the left, one button, a corner
    /// badge, a snooze x, and a row of labelled chips under it with a progress fill on the current one.
    ///
    /// <para>Code-only on purpose: no XAML namescope, so the host drops into the DashBillboard well
    /// without fighting the Home tab's names. The juice (push, text rise, thud, press sparks, snooze
    /// fold, motes) lives in the .Fx partial, the chips in the .Chips partial.</para>
    ///
    /// <para>Motion: the hold runs unless the level is Off; pushes and rises need transitions;
    /// particles need <see cref="MotionFx.AllowParticles"/>; art views play only on ambient loops.
    /// Motion Off is a still card that never changes by itself.</para>
    /// </summary>
    public sealed partial class BillboardCardHost : Grid
    {
        private static readonly FontFamily Display = new(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka, Segoe UI");
        private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");
        private static readonly Color Ink = Color.FromRgb(0x0d, 0x0e, 0x1c);

        private readonly BillboardDeck _deck;
        private readonly Grid _stage = new() { ClipToBounds = true };
        private readonly Grid _slides = new();
        private readonly Rectangle _glint = new() { IsHitTestVisible = false, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TranslateTransform _glintMove = new();
        private readonly StackPanel _meta = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _eyebrow = new();
        private readonly TextBlock _title = new();
        private readonly TextBlock _line = new();
        private readonly Button _cta;
        private readonly Border _ctaFace = new();
        private readonly Border _ctaDrop = new();
        private readonly TextBlock _ctaText = new();
        private readonly TranslateTransform _ctaPress = new();
        private readonly Border _badge = new();
        private readonly TextBlock _badgeText = new();
        private readonly Button _snooze;
        private readonly Border _paused = new();
        private readonly Border _toast = new();
        private readonly TextBlock _toastText = new();
        private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        private Slide? _current;
        private DeckCard? _card;
        private bool _pointerOver;
        private bool _folding;
        private bool _begun;
        private int _changes;
        private DispatcherTimer? _toastTimer;

        /// <summary>The button (or a click on the card) asks for this card's action.</summary>
        public event Action<DeckCard>? ActionRequested;

        /// <summary>The card on screen.</summary>
        public DeckCard? CurrentCard => _card;

        /// <summary>The deck being shown.</summary>
        public BillboardDeck Deck => _deck;

        public BillboardCardHost(BillboardDeck deck)
        {
            _deck = deck ?? throw new ArgumentNullException(nameof(deck));
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- the stage -------------------------------------------------------------------
            _stage.Children.Add(_slides);

            _glint.RenderTransform = _glintMove;
            _stage.Children.Add(_glint);

            _eyebrow.FontFamily = Mono;
            _eyebrow.FontSize = 12;
            _eyebrow.TextTrimming = TextTrimming.CharacterEllipsis;
            _title.FontFamily = Display;
            _title.FontWeight = FontWeights.Bold;
            _title.FontSize = 30;
            _title.Margin = new Thickness(0, 2, 0, 0);
            _title.TextWrapping = TextWrapping.Wrap;
            _title.MaxHeight = 90;
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            _title.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
            _line.FontSize = 14;
            _line.Margin = new Thickness(0, 4, 0, 0);
            _line.Foreground = new SolidColorBrush(Color.FromRgb(0xc9, 0xc7, 0xee));
            _line.TextWrapping = TextWrapping.Wrap;
            _line.MaxHeight = 42;
            _line.TextTrimming = TextTrimming.CharacterEllipsis;
            foreach (var t in new[] { _eyebrow, _title, _line }) t.IsHitTestVisible = false;

            _cta = BuildCta();
            _meta.Children.Add(_eyebrow);
            _meta.Children.Add(_title);
            _meta.Children.Add(_line);
            _meta.Children.Add(_cta);
            _meta.RenderTransform = new TranslateTransform();
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

            SetRow(_stage, 0);
            Children.Add(_stage);

            var chipRow = new Border { Padding = new Thickness(0, 8, 0, 2), Child = _chips };
            SetRow(chipRow, 1);
            Children.Add(chipRow);

            _stage.SizeChanged += (_, _) => Lay();
            _stage.MouseEnter += (_, _) => { _pointerOver = true; OnHoverChanged(); };
            _stage.MouseLeave += (_, _) => { _pointerOver = false; OnHoverChanged(); };
            _stage.MouseLeftButtonDown += OnStagePressed;
            IsKeyboardFocusWithinChanged += (_, _) => UpdateRunning();
            IsVisibleChanged += (_, _) => UpdateRunning();
        }

        // ---- the walk ----------------------------------------------------------------------------

        /// <summary>Builds the deck and shows its first card, still (no push, no sound).</summary>
        public void Begin()
        {
            _begun = true;
            Show(_deck.Start(), animate: false, sound: false);
        }

        /// <summary>The next card (the hold ran out, or someone asked).</summary>
        public void Advance()
        {
            if (!_begun) { Begin(); return; }
            if (_folding) return;
            Show(_deck.Next(), animate: true, sound: true);
        }

        /// <summary>A provider's state moved: rebuild at the next card change.</summary>
        public void MarkDirty() => _deck.MarkDirty();

        /// <summary>The motion level changed: re-read every gate now.</summary>
        public void RefreshMotion() => UpdateRunning();

        /// <summary>Whether the hold clock may run right now.</summary>
        internal bool HoldMayRun =>
            !_folding && _card != null &&
            DashboardBillboard.ShouldAdvance(_pointerOver, IsVisible, MotionFx.Level != Models.MotionLevel.Off, IsKeyboardFocusWithin);

        private void Show(DeckCard? card, bool animate, bool sound)
        {
            if (card == null)
            {
                ClearStage();
                return;
            }

            bool same = _card != null && string.Equals(_card.Spec.Id, card.Spec.Id, StringComparison.Ordinal) && _current != null;
            _card = card;
            animate &= MotionFx.AllowTransitions && !same;

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
                if (sound) LauncherSfx.BoardCardChange(_changes);
                _changes++;
            }

            PaintWords(card);
            PaintBadge(card);
            _snooze.Visibility = card.CanSnooze ? Visibility.Visible : Visibility.Collapsed;
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
            _meta.Visibility = Visibility.Collapsed;
            _badge.Visibility = Visibility.Collapsed;
            _snooze.Visibility = Visibility.Collapsed;
            _chips.Children.Clear();
            StopHold();
        }

        private void Retire(Slide slide)
        {
            try { slide.Art?.Release(); } catch (Exception ex) { App.Logger?.Debug("Billboard art release failed: {E}", ex.Message); }
            _slides.Children.Remove(slide.Root);
        }

        /// <summary>Plays or holds the art and the clock, from every gate there is.</summary>
        private void UpdateRunning()
        {
            // Contract: hover, another tab or Motion Off holds the art still. The board still takes
            // a touch while held (its ripple runs on a clock of its own).
            var art = _current?.Art;
            bool artRuns = IsVisible && !_folding && !_pointerOver;
            try
            {
                if (art != null)
                {
                    if (artRuns) art.Play();
                    else art.Pause();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Billboard art play/pause failed: {E}", ex.Message); }

            if (HoldMayRun) ResumeHold();
            else PauseHold();
        }

        private void OnHoverChanged()
        {
            FadeTo(_paused, _pointerOver && MotionFx.Level != Models.MotionLevel.Off ? 1 : 0, 200);
            FadeTo(_snooze, _pointerOver ? 1 : 0, 200);
            UpdateRunning();
        }

        // ---- words -------------------------------------------------------------------------------

        private void PaintWords(DeckCard card)
        {
            var spec = card.Spec;
            var hue = HueOf(card);
            bool bleed = DashboardBillboard.IsFullBleed(spec);
            _meta.Visibility = Visibility.Visible;

            _eyebrow.Text = (spec.Eyebrow ?? string.Empty).ToLowerInvariant();
            _eyebrow.Foreground = Brush(hue);
            _title.Text = spec.Title ?? string.Empty;
            _line.Text = spec.Line ?? string.Empty;
            _eyebrow.Visibility = bleed || string.IsNullOrEmpty(_eyebrow.Text) ? Visibility.Collapsed : Visibility.Visible;
            _title.Visibility = bleed || string.IsNullOrEmpty(_title.Text) ? Visibility.Collapsed : Visibility.Visible;
            _line.Visibility = bleed || string.IsNullOrEmpty(_line.Text) ? Visibility.Collapsed : Visibility.Visible;

            var action = card.Action;
            bool hasButton = action.Kind != BillboardActionKind.None;
            _cta.Visibility = hasButton ? Visibility.Visible : Visibility.Collapsed;
            _ctaText.Text = action.Label;
            _ctaFace.Background = Brush(hue);
            _ctaDrop.Background = new SolidColorBrush(DepthRules.ShadowColor(hue));
            _cta.Margin = new Thickness(0, bleed ? 0 : 12, 0, 0);
            AutomationProperties.SetName(_cta, string.IsNullOrEmpty(spec.Title) ? action.Label : action.Label + ", " + spec.Title);
            _stage.Cursor = !bleed && hasButton ? Cursors.Hand : null;

            // A board shows its button bottom-right and nothing else: the picture is the words.
            _meta.HorizontalAlignment = bleed ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private void PaintBadge(DeckCard card)
        {
            var key = DashboardBillboard.BadgeKey(card.Badge);
            if (key == null) { _badge.Visibility = Visibility.Collapsed; return; }
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
            _badge.Visibility = Visibility.Visible;
        }

        /// <summary>Sizes that follow the card: margins, the words' width, the title's size.</summary>
        private void Lay()
        {
            double w = _stage.ActualWidth, h = _stage.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var r = new RectangleGeometry(new Rect(0, 0, w, h), 10, 10);
            r.Freeze();
            _stage.Clip = r;

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

        private void OnStagePressed(object sender, MouseButtonEventArgs e)
        {
            if (_card == null || _folding) return;
            if (IsInsideButton(e.OriginalSource as DependencyObject)) return;

            if (DashboardBillboard.IsFullBleed(_card.Spec))
            {
                // A touch on the board: the art ripples from that tile (even hover-paused), and
                // the ripple's two notes are ours: the tile view plays no sound.
                var art = _current?.ArtElement;
                if (art != null && art.ActualWidth > 0 && art.ActualHeight > 0)
                {
                    var p = e.GetPosition(art);
                    try { _current!.Art?.Touch(new Point(Math.Clamp(p.X / art.ActualWidth, 0, 1), Math.Clamp(p.Y / art.ActualHeight, 0, 1))); }
                    catch (Exception ex) { App.Logger?.Debug("Billboard touch failed: {E}", ex.Message); }
                    LauncherSfx.BoardTouch();
                }
                return;
            }

            // Anywhere else on a card with a button is the button.
            if (_card.Action.Kind != BillboardActionKind.None) Press(e.GetPosition(_stage));
        }

        private bool IsInsideButton(DependencyObject? d)
        {
            while (d != null && !ReferenceEquals(d, _stage))
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>The button pressed (or the card): sparks, the chime, then the action.</summary>
        private void Press(Point at)
        {
            var card = _card;
            if (card == null) return;
            Burst(at, HueOf(card), 26, 1.0);
            LauncherSfx.BoardPress();
            After(DashboardBillboard.PressActionDelayMs, () =>
            {
                try { ActionRequested?.Invoke(card); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Billboard action failed"); }
            });
        }

        private void OnCtaClick(object sender, RoutedEventArgs e)
        {
            if (_card == null || _folding) return;
            var at = _cta.TranslatePoint(new Point(_cta.ActualWidth / 2, _cta.ActualHeight / 2), _stage);
            Press(at);
        }

        private void OnSnoozeClick(object sender, RoutedEventArgs e)
        {
            var card = _card;
            if (card == null || _folding || !card.CanSnooze) return;
            _folding = true;
            UpdateRunning();
            var at = _snooze.TranslatePoint(new Point(_snooze.ActualWidth / 2, _snooze.ActualHeight / 2), _stage);
            Burst(at, HueOf(card), 16, 0.7);
            LauncherSfx.BoardSnooze();
            Fold();
            PopCurrentChip();
            After(MotionFx.AllowTransitions ? DashboardBillboard.SnoozeFoldMs : 0, () =>
            {
                _folding = false;
                var label = ChipLabel(card);
                Show(_deck.SnoozeCurrent(DateTime.UtcNow), animate: true, sound: false);
                Toast(string.Format(Loc.Get("billboard_deck_snoozed"), label));
            });
        }

        // ---- pieces ------------------------------------------------------------------------------

        private Button BuildCta()
        {
            _ctaText.FontFamily = Display;
            _ctaText.FontWeight = FontWeights.Bold;
            _ctaText.FontSize = 14;
            _ctaText.Foreground = new SolidColorBrush(Ink);
            _ctaText.Margin = new Thickness(18, 7, 18, 8);
            _ctaText.HorizontalAlignment = HorizontalAlignment.Center;

            _ctaDrop.CornerRadius = new CornerRadius(10);
            _ctaDrop.Margin = new Thickness(0, DepthRules.RaisedPx, 0, -DepthRules.RaisedPx);
            _ctaDrop.IsHitTestVisible = false;

            var sheen = new Border { CornerRadius = new CornerRadius(9), IsHitTestVisible = false };
            sheen.SetResourceReference(Border.BackgroundProperty, "DepthRaisedSheen");
            var faceGrid = new Grid();
            faceGrid.Children.Add(sheen);
            faceGrid.Children.Add(_ctaText);
            _ctaFace.CornerRadius = new CornerRadius(10);
            _ctaFace.BorderThickness = new Thickness(1);
            _ctaFace.SetResourceReference(Border.BorderBrushProperty, "DepthRaisedBevel");
            _ctaFace.Child = faceGrid;
            _ctaFace.RenderTransform = _ctaPress;

            var ring = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Margin = new Thickness(-3), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            ring.SetResourceReference(Border.BorderBrushProperty, "TextLightBrush");

            var root = new Grid { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } } };
            root.Children.Add(_ctaDrop);
            root.Children.Add(_ctaFace);
            root.Children.Add(ring);

            var button = new Button
            {
                Template = BareTemplate(),
                Content = root,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                Focusable = true,
                Margin = new Thickness(0, 12, 0, 0),
            };
            button.Click += OnCtaClick;
            button.PreviewMouseLeftButtonDown += (_, _) => PressDown();
            button.PreviewMouseLeftButtonUp += (_, _) => PressUp();
            button.MouseLeave += (_, _) => { if (_ctaPress.Y > 0) PressUp(); };
            button.MouseEnter += (_, _) => Lift(true);
            button.MouseLeave += (_, _) => Lift(false);
            button.IsKeyboardFocusedChanged += (_, _) => ring.Visibility = button.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
            return button;
        }

        private void BuildBadge()
        {
            _badgeText.FontFamily = Mono;
            _badgeText.FontSize = 11;
            _badgeText.FontWeight = FontWeights.Bold;
            _badge.Child = _badgeText;
            _badge.Padding = new Thickness(9, 3, 9, 4);
            _badge.CornerRadius = new CornerRadius(6);
            _badge.BorderThickness = new Thickness(1);
            _badge.HorizontalAlignment = HorizontalAlignment.Left;
            _badge.VerticalAlignment = VerticalAlignment.Top;
            _badge.IsHitTestVisible = false;
            _badge.Visibility = Visibility.Collapsed;
            _badge.RenderTransformOrigin = new Point(0.5, 0.5);
            _badge.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } };
        }

        private Button BuildSnooze()
        {
            // A raised coin (depth law): a drop disc under it, a bevelled face, "x" on top.
            var drop = new Ellipse { Margin = new Thickness(1, DepthRules.RaisedPx, -1, -DepthRules.RaisedPx), IsHitTestVisible = false };
            drop.SetResourceReference(Shape.FillProperty, "DepthDropDisc");
            var glyph = new TextBlock { Text = "×", FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            var face = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(0xc0, 12, 12, 26)), Child = glyph };
            face.SetResourceReference(Border.BorderBrushProperty, "DepthRaisedBevel");
            var root = new Grid { Width = 28, Height = 28 };
            root.Children.Add(drop);
            root.Children.Add(face);

            var button = new Button
            {
                Template = BareTemplate(),
                Content = root,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                Opacity = 0,
                ToolTip = Loc.Get("billboard_deck_snooze"),
            };
            AutomationProperties.SetName(button, Loc.Get("billboard_deck_snooze"));
            button.Click += OnSnoozeClick;
            button.MouseEnter += (_, _) => face.BorderBrush = Brush(Color.FromRgb(0xff, 0x4f, 0xa8));
            button.MouseLeave += (_, _) => face.SetResourceReference(Border.BorderBrushProperty, "DepthRaisedBevel");
            button.IsKeyboardFocusedChanged += (_, _) => { if (button.IsKeyboardFocused) button.Opacity = 1; };
            return button;
        }

        private void BuildPausedPill()
        {
            var text = new TextBlock { Text = Loc.Get("billboard_deck_paused"), FontFamily = Mono, FontSize = 11 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            _paused.Child = text;
            _paused.Padding = new Thickness(10, 3, 10, 4);
            _paused.CornerRadius = new CornerRadius(999);
            _paused.BorderThickness = new Thickness(1);
            _paused.SetResourceReference(Border.BorderBrushProperty, "DepthPressedBevel");
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
            _toastText.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
            _toast.Child = _toastText;
            _toast.Padding = new Thickness(14, 6, 14, 7);
            _toast.CornerRadius = new CornerRadius(999);
            _toast.BorderThickness = new Thickness(1);
            _toast.SetResourceReference(Border.BorderBrushProperty, "DepthFloatRim");
            _toast.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x25, 0x4a));
            _toast.HorizontalAlignment = HorizontalAlignment.Center;
            _toast.VerticalAlignment = VerticalAlignment.Bottom;
            _toast.Margin = new Thickness(0, 0, 0, 14);
            _toast.IsHitTestVisible = false;
            _toast.Opacity = 0;
            _toast.RenderTransform = new TranslateTransform();
        }

        private static ControlTemplate BareTemplate()
        {
            var t = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            t.Seal();
            return t;
        }

        // ---- helpers -----------------------------------------------------------------------------

        internal static Color HueOf(DeckCard card) =>
            BillboardVectorArt.ParseHue(DashboardBillboard.Accent(card.Spec.AccentHex), Color.FromRgb(0xff, 0x4f, 0xa8));

        private static SolidColorBrush Brush(Color c, double alpha = 1)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        /// <summary>The chip's words: the kind's own label, or the card's title for house and events.</summary>
        internal static string ChipLabel(DeckCard card)
        {
            var key = DashboardBillboard.ChipKey(card.Spec);
            if (key != null) return Loc.Get(key);
            return card.Spec.Title ?? string.Empty;
        }

        private void After(int ms, Action run)
        {
            if (ms <= 0) { run(); return; }
            var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { run(); }
                catch (Exception ex) { App.Logger?.Debug("Billboard delayed step failed: {E}", ex.Message); }
            };
            timer.Start();
        }

        private static void FadeTo(UIElement e, double to, int ms)
        {
            if (!MotionFx.AllowTransitions || ms <= 0)
            {
                e.BeginAnimation(OpacityProperty, null);
                e.Opacity = to;
                return;
            }
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)));
        }

        /// <summary>One card's picture on the stage: the art (or a plain ground) and its shade.</summary>
        private sealed class Slide
        {
            public readonly Grid Root = new() { IsHitTestVisible = true };
            public readonly FrameworkElement? ArtElement;
            public readonly IBillboardArtView? Art;
            public readonly TranslateTransform Move = new();
            public readonly ScaleTransform Zoom = new(1, 1);

            public Slide(DeckCard card)
            {
                Root.Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0d, 0x1a));
                Root.RenderTransformOrigin = new Point(0.5, 0.5);
                Root.RenderTransform = new TransformGroup { Children = { Zoom, Move } };

                FrameworkElement? art = null;
                try { art = BillboardArt.Create(card.Spec.ArtKey ?? string.Empty, card.Spec.ArtData); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Billboard art {Key} failed to build", card.Spec.ArtKey); }
                if (art is IAccentedArt tinted) tinted.Accent = HueOf(card);
                if (art != null)
                {
                    ArtElement = art;
                    Art = art as IBillboardArtView;
                    Root.Children.Add(art);
                }

                if (!DashboardBillboard.IsFullBleed(card.Spec))
                {
                    var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                    shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xeb, 9, 9, 20), 0));
                    shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 9, 9, 20), 0.38));
                    shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 9, 9, 20), 0.62));
                    shade.Freeze();
                    Root.Children.Add(new Rectangle { Fill = shade, IsHitTestVisible = false });
                }
            }
        }
    }
}
