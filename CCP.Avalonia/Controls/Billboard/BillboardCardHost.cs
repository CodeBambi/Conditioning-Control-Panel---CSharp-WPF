// PORTED from ConditioningControlPanel/Controls/Billboard/BillboardCardHost.cs + .Chips.cs (the
// Tonight Board deck on Home). Slice A (sync6-tonight-board): the card face (poster art, shade, eyebrow,
// title, line, one button, badge), the snooze x with its toast, the "paused" pill, the dot row whose
// fill IS the 12 s hold, and the push between cards. It plays no sound (WPF c0c67ff47 silenced every
// board cue). Not yet here (sync6-tonight-board-b): the drawn art views (board tiles, clips, wheel,
// quests, invite, tip and house scenes, living posters), the text rise, badge thud, sparks, motes,
// glint and the CTA press juice.

using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>WPF BillboardCardHost: one card at a time from a <see cref="BillboardDeck"/>, a row
    /// of dots under it with the hold filling the current one.</summary>
    internal sealed class BillboardCardHost : Grid
    {
        /// <summary>The hold reads elapsed time here; tests step it (P08/P40).</summary>
        internal static TimeProvider Time = TimeProvider.System;

        private static readonly Color Plate = Color.FromRgb(0x12, 0x13, 0x27);
        private static readonly Color Ink = Color.FromRgb(0x0d, 0x0e, 0x1c);

        private readonly BillboardDeck _deck;
        private readonly Border _stage = new() { CornerRadius = new CornerRadius(10), ClipToBounds = true };
        private readonly Panel _slides = new();
        private readonly StackPanel _meta = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _eyebrow = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        private readonly TextBlock _title = new() { FontWeight = FontWeight.Bold, FontSize = 30, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 90, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        private readonly TextBlock _line = new() { FontSize = 14, Margin = new Thickness(0, 4, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xc9, 0xc7, 0xee)), TextWrapping = TextWrapping.Wrap, MaxHeight = 42, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        private readonly Button _cta;
        private readonly Border _ctaFace = new() { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        private readonly TextBlock _ctaText = new() { FontWeight = FontWeight.Bold, FontSize = 14, Margin = new Thickness(18, 7, 18, 8), HorizontalAlignment = HorizontalAlignment.Center };
        private readonly Border _badge = new() { Padding = new Thickness(9, 3, 9, 4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, IsVisible = false };
        private readonly TextBlock _badgeText = new() { FontSize = 11, FontWeight = FontWeight.Bold };
        private readonly Button _snooze;
        private readonly Border _paused = new();
        private readonly Border _toast = new();
        private readonly TextBlock _toastText = new() { FontSize = 13 };
        private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        private Control? _current;
        private DeckCard? _card;
        private bool _pointerOver, _folding, _begun;
        private Rectangle? _fill;
        private double _holdBase;
        private long _holdSince;
        private DispatcherTimer? _holdTimer;
        private IDisposable? _toastTimer;
        private IDisposable? _visibilityWatch;

        /// <summary>The button (or a click on the card) asks for this card's action.</summary>
        public event Action<DeckCard>? ActionRequested;

        internal DeckCard? CurrentCard => _card;
        internal BillboardDeck Deck => _deck;
        internal global::Avalonia.Controls.Controls ChipButtons => _chips.Children;
        internal Button Cta => _cta;
        internal Button SnoozeButton => _snooze;
        internal string ToastText => _toastText.Text ?? string.Empty;
        internal bool HoldRunning => _holdTimer != null;
        internal double HoldProgress => Math.Clamp(_holdBase + (HoldRunning ? Elapsed() : 0), 0, 1);

        public BillboardCardHost(BillboardDeck deck)
        {
            _deck = deck ?? throw new ArgumentNullException(nameof(deck));
            RowDefinitions = new RowDefinitions("*,Auto");

            var stageRoot = new Panel();
            stageRoot.Children.Add(_slides);
            _eyebrow.FontFamily = (FontFamily?)FindRes("Font.Mono") ?? FontFamily.Default;
            _title.Foreground = (IBrush?)FindRes("TextLightBrush") ?? Brushes.White;
            _cta = BuildCta();
            _meta.Children.Add(_eyebrow);
            _meta.Children.Add(_title);
            _meta.Children.Add(_line);
            _meta.Children.Add(_cta);
            stageRoot.Children.Add(_meta);
            _badge.Child = _badgeText;
            stageRoot.Children.Add(_badge);
            _snooze = BuildSnooze();
            stageRoot.Children.Add(_snooze);
            stageRoot.Children.Add(BuildPill(_paused, new TextBlock { Text = Loc.Get("billboard_deck_paused"), FontSize = 11, Foreground = MutedBrush }, top: true));
            stageRoot.Children.Add(BuildPill(_toast, _toastText, top: false));
            _toastText.Foreground = (IBrush?)FindRes("TextLightBrush") ?? Brushes.White;
            _stage.Child = stageRoot;
            Children.Add(_stage);

            var chipRow = new Border { Padding = new Thickness(0, 8, 0, 2), Child = _chips };
            SetRow(chipRow, 1);
            Children.Add(chipRow);

            _stage.SizeChanged += (_, _) => Lay();
            _stage.PointerEntered += (_, _) => { _pointerOver = true; OnHoverChanged(); };
            _stage.PointerExited += (_, _) => { _pointerOver = false; OnHoverChanged(); };
            _stage.PointerPressed += OnStagePressed;
            PropertyChanged += (_, e) => { if (e.Property == IsKeyboardFocusWithinProperty) UpdateRunning(); };
        }

        private static object? FindRes(string key) =>
            Application.Current?.TryFindResource(key, out var v) == true ? v : null;

        private static IBrush MutedBrush => (IBrush?)FindRes("TextMutedBrush") ?? Brushes.Gray;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _visibilityWatch = EffectiveVisibility.Watch(this, UpdateRunning);
            UpdateRunning();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _visibilityWatch?.Dispose();
            _visibilityWatch = null;
            (_current?.Tag as IBillboardArtView)?.Hold();
            PauseHold();
            base.OnDetachedFromVisualTree(e);
        }

        // ---- the walk (WPF :149-258) -------------------------------------------------------------

        /// <summary>Builds the deck and shows its first card, still.</summary>
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

        public void MarkDirty() => _deck.MarkDirty();

        /// <summary>The shell closed: every art view frees its player and bitmaps (P68).</summary>
        public void ReleaseArt()
        {
            foreach (var slide in _slides.Children) (slide.Tag as IBillboardArtView)?.Release();
        }

        /// <summary>The motion level or a visibility gate changed: re-read every gate now.</summary>
        public void RefreshMotion() => UpdateRunning();

        internal bool HoldMayRun =>
            !_folding && _card != null && _fill != null && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is { IsVisible: true } &&
            DashboardBillboard.ShouldAdvance(_pointerOver, onScreen: true, Env.Level != MotionLevel.Off, IsKeyboardFocusWithin);

        private void Show(DeckCard? card, bool animate)
        {
            if (card == null) { ClearStage(); return; }
            bool same = _card != null && _current != null && string.Equals(_card.Spec.Id, card.Spec.Id, StringComparison.Ordinal);
            _card = card;
            animate &= Env.AllowTransitions && !same;
            if (!same)
            {
                var slide = BuildSlide(card);
                var old = _current;
                _current = slide;
                _slides.Children.Add(slide);
                if (old != null)
                {
                    (old.Tag as IBillboardArtView)?.Hold();
                    if (animate) Push(old, slide);
                    else Retire(old);
                }
            }
            PaintWords(card);
            PaintBadge(card);
            _snooze.IsVisible = card.CanSnooze;
            RebuildChips();
            Lay();
            UpdateRunning();
        }

        private void ClearStage()
        {
            foreach (var slide in _slides.Children.ToArray()) Retire(slide);
            _current = null;
            _card = null;
            _meta.IsVisible = _badge.IsVisible = _snooze.IsVisible = false;
            _chips.Children.Clear();
            _fill = null;
            PauseHold();
        }

        /// <summary>WPF Retire: the slide's art lets go of its player, then the slide leaves.</summary>
        private void Retire(Control slide)
        {
            try { (slide.Tag as IBillboardArtView)?.Release(); } catch (Exception ex) { Log.Debug("Billboard art release failed: {E}", ex.Message); }
            _slides.Children.Remove(slide);
        }

        /// <summary>WPF UpdateRunning: another tab or a fold holds the art; hover never does.</summary>
        private void UpdateRunning()
        {
            var art = _current?.Tag as IBillboardArtView;
            bool onScreen = IsEffectivelyVisible && TopLevel.GetTopLevel(this) is { IsVisible: true };
            try
            {
                if (DashboardBillboard.ArtShouldPlay(onScreen, _folding)) art?.Run();
                else art?.Hold();
            }
            catch (Exception ex) { Log.Debug("Billboard art run/hold failed: {E}", ex.Message); }
            if (HoldMayRun) ResumeHold();
            else PauseHold();
        }

        private void OnHoverChanged()
        {
            _paused.Opacity = _pointerOver && Env.Level != MotionLevel.Off ? 1 : 0;
            _snooze.Opacity = _pointerOver ? 1 : 0;
            UpdateRunning();
        }

        /// <summary>WPF Fx Push: the old card slides a little left, the new one comes in from 40%
        /// of the width, a touch large, over PushMs.</summary>
        private async void Push(Control old, Control next)
        {
            double w = Math.Max(1, _stage.Bounds.Width);
            var dur = TimeSpan.FromMilliseconds(DashboardBillboard.PushMs);
            try
            {
                var inbound = Anim(dur, $"translateX({Px(w * DashboardBillboard.PushNewStart)}) scale({Px(DashboardBillboard.PushNewScale, "")})", "translateX(0px) scale(1)").RunAsync(next);
                await Anim(dur, "translateX(0px)", $"translateX({Px(-w * DashboardBillboard.PushOldShift)})").RunAsync(old);
                await inbound;
            }
            catch (Exception ex) { Log.Debug("Billboard push failed: {E}", ex.Message); }
            Retire(old);
        }

        private static string Px(double v, string unit = "px") => v.ToString("0.###", CultureInfo.InvariantCulture) + unit;

        private static Animation Anim(TimeSpan dur, string from, string to) => new()
        {
            Duration = dur,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(RenderTransformProperty, TransformOperations.Parse(from)) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(RenderTransformProperty, TransformOperations.Parse(to)) } },
            },
        };

        // ---- words (WPF :267-335) ----------------------------------------------------------------

        private void PaintWords(DeckCard card)
        {
            var spec = card.Spec;
            var hue = HueOf(card);
            bool bleed = DashboardBillboard.IsFullBleed(spec);
            _meta.IsVisible = true;
            _eyebrow.Text = (spec.Eyebrow ?? string.Empty).ToLowerInvariant();
            _eyebrow.Foreground = new SolidColorBrush(hue);
            _title.Text = spec.Title ?? string.Empty;
            _line.Text = spec.Line ?? string.Empty;
            _eyebrow.IsVisible = !bleed && _eyebrow.Text.Length > 0;
            _title.IsVisible = !bleed && _title.Text.Length > 0;
            _line.IsVisible = !bleed && _line.Text.Length > 0;

            var action = card.Action;
            bool hasButton = action.Kind != BillboardActionKind.None;
            _cta.IsVisible = hasButton;
            _ctaText.Text = action.Label;
            _ctaFace.Background = new SolidColorBrush(hue);
            _cta.Margin = new Thickness(0, bleed ? 0 : 12, 0, 0);
            AutomationProperties.SetName(_cta, string.IsNullOrEmpty(spec.Title) ? action.Label : action.Label + ", " + spec.Title);
            _stage.Cursor = !bleed && hasButton ? new Cursor(StandardCursorType.Hand) : null;
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
            _badgeText.Foreground = new SolidColorBrush(hue);
            _badge.BorderBrush = new SolidColorBrush(hue);
            _badge.Background = new SolidColorBrush(hue, 0.10);
            _badge.IsVisible = true;
        }

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
        }

        // ---- input (WPF :337-412) ----------------------------------------------------------------

        private void OnStagePressed(object? sender, PointerPressedEventArgs e)
        {
            if (_card == null || _folding || !e.GetCurrentPoint(_stage).Properties.IsLeftButtonPressed) return;
            for (var v = e.Source as Visual; v != null && !ReferenceEquals(v, _stage); v = v.GetVisualParent())
                if (v is Button) return;
            // A board card's touch ripples its art (slice b); anywhere else on a card is the button.
            if (DashboardBillboard.IsFullBleed(_card.Spec)) return;
            if (_card.Action.Kind != BillboardActionKind.None) Press();
        }

        /// <summary>WPF Press: sparks and the (silenced) chime, then the action. No sparks here yet, so
        /// the action runs at once rather than after PressActionDelayMs.</summary>
        private void Press()
        {
            var card = _card;
            if (card == null) return;
            try { ActionRequested?.Invoke(card); }
            catch (Exception ex) { Log.Warning(ex, "Billboard action failed"); }
        }

        private void OnSnoozeClick()
        {
            var card = _card;
            if (card == null || _folding || !card.CanSnooze) return;
            _folding = true;
            UpdateRunning();
            void Land()
            {
                _folding = false;
                var label = ChipLabel(card);
                Show(_deck.SnoozeCurrent(DateTime.UtcNow), animate: true);
                Toast(string.Format(CultureInfo.CurrentCulture, Loc.Get("billboard_deck_snoozed"), label));
            }
            if (Env.AllowTransitions)
            {
                if (_current != null) _current.Opacity = 0.4;
                DispatcherTimer.RunOnce(Land, TimeSpan.FromMilliseconds(DashboardBillboard.SnoozeFoldMs));
            }
            else Land();
        }

        private void Toast(string text)
        {
            _toastText.Text = text;
            _toast.Opacity = 1;
            _toastTimer?.Dispose();
            _toastTimer = DispatcherTimer.RunOnce(() => _toast.Opacity = 0, TimeSpan.FromMilliseconds(1800));
        }

        // ---- pieces (WPF :414-600) ---------------------------------------------------------------

        private static FuncControlTemplate<Button> Bare() => new((b, _) => new ContentPresenter
        {
            [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
        });

        private Button BuildCta()
        {
            _ctaText.Foreground = new SolidColorBrush(Ink);
            _ctaFace.Child = _ctaText;
            _ctaFace.BorderBrush = new SolidColorBrush(Colors.White, 0.35);
            var button = new Button
            {
                Template = Bare(), Content = _ctaFace, Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Left, Focusable = true, Margin = new Thickness(0, 12, 0, 0),
            };
            button.Click += (_, _) => { if (_card != null && !_folding) Press(); };
            button.GotFocus += (_, _) => _ctaFace.BorderBrush = Brushes.White;
            button.LostFocus += (_, _) => _ctaFace.BorderBrush = new SolidColorBrush(Colors.White, 0.35);
            return button;
        }

        private Button BuildSnooze()
        {
            var face = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0xc0, 12, 12, 26)), BorderBrush = new SolidColorBrush(Colors.White, 0.25),
                Child = new TextBlock { Text = "×", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = MutedBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) },
            };
            var button = new Button
            {
                Template = Bare(), Content = face, Cursor = new Cursor(StandardCursorType.Hand), Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0),
            };
            ToolTip.SetTip(button, Loc.Get("billboard_deck_snooze"));
            AutomationProperties.SetName(button, Loc.Get("billboard_deck_snooze"));
            button.Click += (_, _) => OnSnoozeClick();
            button.PointerEntered += (_, _) => face.BorderBrush = new SolidColorBrush(Color.FromRgb(0xff, 0x4f, 0xa8));
            button.PointerExited += (_, _) => face.BorderBrush = new SolidColorBrush(Colors.White, 0.25);
            button.GotFocus += (_, _) => button.Opacity = 1;
            return button;
        }

        private static Border BuildPill(Border pill, TextBlock text, bool top)
        {
            pill.Child = text;
            pill.Padding = top ? new Thickness(10, 3, 10, 4) : new Thickness(14, 6, 14, 7);
            pill.CornerRadius = new CornerRadius(999);
            pill.BorderThickness = new Thickness(1);
            pill.BorderBrush = new SolidColorBrush(Colors.White, 0.18);
            pill.Background = top ? new SolidColorBrush(Color.FromArgb(0xb3, 10, 10, 22)) : new SolidColorBrush(Color.FromRgb(0x24, 0x25, 0x4a));
            pill.HorizontalAlignment = HorizontalAlignment.Center;
            pill.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            pill.Margin = top ? new Thickness(0, 14, 0, 0) : new Thickness(0, 0, 0, 14);
            pill.IsHitTestVisible = false;
            pill.Opacity = 0;
            pill.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) } };
            return pill;
        }

        /// <summary>WPF Slide: the registered art view, a poster, or a plain ground, and the shade.
        /// The slide's Tag carries the art's lifecycle.</summary>
        private static Control BuildSlide(DeckCard card)
        {
            var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0d, 0x1a)) };
            Control? art = null;
            try { art = BillboardArt.Create(card.Spec.ArtKey ?? string.Empty, card.Spec.ArtData); }
            catch (Exception ex) { Log.Debug("Billboard art {Key} did not build: {E}", card.Spec.ArtKey, ex.Message); }
            if (art != null)
            {
                root.Children.Add(art);
                root.Tag = art as IBillboardArtView;
            }
            else if (card.Spec.ArtKey == BuiltInArtKeys.Poster && card.Spec.ArtData is string poster)
            {
                try { root.Children.Add(new Image { Stretch = Stretch.UniformToFill, Source = new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + poster))) }); }
                catch (Exception ex) { Log.Debug("Billboard poster {Poster} did not load: {E}", poster, ex.Message); }
            }
            if (!DashboardBillboard.IsFullBleed(card.Spec))
            {
                root.Children.Add(new Border
                {
                    IsHitTestVisible = false,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.FromArgb(0xeb, 9, 9, 20), 0), new GradientStop(Color.FromArgb(0x99, 9, 9, 20), 0.38), new GradientStop(Color.FromArgb(0, 9, 9, 20), 0.62) },
                    },
                });
            }
            return root;
        }

        // ---- the dots and the hold (WPF .Chips.cs) -----------------------------------------------

        internal const double DotPx = 8, DotCurrentPx = 26;

        private void RebuildChips()
        {
            PauseHold();
            _holdBase = 0;
            _chips.Children.Clear();
            _fill = null;
            var cards = _deck.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                int index = i;
                bool current = i == _deck.Index;
                var chip = BuildChip(cards[i], current, out var fill);
                chip.Click += (_, _) => OnChipClick(index);
                _chips.Children.Add(chip);
                if (current) _fill = fill;
            }
        }

        private static Button BuildChip(DeckCard card, bool current, out Rectangle fill)
        {
            var tint = ChipTint(card) ?? HueOf(card);
            fill = new Rectangle
            {
                Fill = new SolidColorBrush(tint, 0.93), HorizontalAlignment = HorizontalAlignment.Left,
                // The hold scales the fill (render only), never its Width (a layout pass per tick).
                Width = DotCurrentPx - 2, IsVisible = current, IsHitTestVisible = false,
                RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative), RenderTransform = new ScaleTransform(0, 1),
            };
            var rest = Blend(Plate, tint, current ? 0.22 : 0.62);
            var face = new Border
            {
                Width = current ? DotCurrentPx : DotPx, Height = DotPx, CornerRadius = new CornerRadius(DotPx / 2),
                BorderThickness = new Thickness(1), ClipToBounds = true, Background = new SolidColorBrush(rest),
                BorderBrush = current ? new SolidColorBrush(Blend(tint, Colors.White, 0.25)) : new SolidColorBrush(Colors.White, 0.2),
                Child = fill, Margin = new Thickness(5, 6),
            };
            var name = ChipLabel(card);
            if (string.IsNullOrWhiteSpace(name)) name = card.Spec.Title ?? string.Empty;
            var button = new Button { Template = Bare(), Content = new Panel { Background = Brushes.Transparent, Children = { face } }, Cursor = new Cursor(StandardCursorType.Hand), Focusable = true };
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, name);
            if (!current)
            {
                var hover = Blend(Plate, tint, 0.9);
                button.PointerEntered += (_, _) => face.Background = new SolidColorBrush(hover);
                button.PointerExited += (_, _) => face.Background = new SolidColorBrush(rest);
            }
            button.GotFocus += (_, _) => face.BorderBrush = Brushes.White;
            return button;
        }

        private void OnChipClick(int index)
        {
            if (_folding) return;
            if (index == _deck.Index) { PauseHold(); _holdBase = 0; SetFill(0); UpdateRunning(); return; }
            Show(_deck.Select(index), animate: true);
        }

        private double Elapsed() => Time.GetElapsedTime(_holdSince).TotalMilliseconds / (DashboardBillboard.HoldSeconds * 1000.0);

        private void SetFill(double progress)
        {
            if (_fill?.RenderTransform is ScaleTransform scale) scale.ScaleX = Math.Clamp(progress, 0, 1);
        }

        /// <summary>Runs the fill from where it stands; the fill reaching the end moves the deck on, so
        /// the bar and the change never disagree (WPF ResumeHold).</summary>
        private void ResumeHold()
        {
            if (_holdTimer != null) return;
            _holdSince = Time.GetTimestamp();
            _holdTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, (_, _) => HoldTick());
            _holdTimer.Start();
        }

        /// <summary>One frame of the hold (the timer's handler; tests step <see cref="Time"/> and call it).</summary>
        internal void HoldTick()
        {
            if (_holdTimer == null) return;
            double p = HoldProgress;
            SetFill(p);
            if (p >= 1) { PauseHold(); Advance(); }
        }

        private void PauseHold()
        {
            if (_holdTimer == null) return;
            _holdBase = HoldProgress;
            _holdTimer.Stop();
            _holdTimer = null;
            SetFill(_holdBase);
        }

        // ---- helpers -----------------------------------------------------------------------------

        internal static Color HueOf(DeckCard card) =>
            Color.TryParse(DashboardBillboard.Accent(card.Spec.AccentHex), out var c) ? c : Color.FromRgb(0xff, 0x4f, 0xa8);

        internal static Color? ChipTint(DeckCard card) => card.Spec.Kind switch
        {
            BillboardCardKind.Board => Color.FromRgb(0xff, 0x4f, 0xa8),
            BillboardCardKind.Showcase when card.Spec.Badge == BillboardBadge.Prime => Color.FromRgb(0x5f, 0xe3, 0xff),
            BillboardCardKind.Showcase => Color.FromRgb(0xff, 0xc9, 0x4a),
            _ => null,
        };

        internal static string ChipLabel(DeckCard card)
        {
            var key = DashboardBillboard.ChipKey(card.Spec);
            return key != null ? Loc.Get(key) : card.Spec.Title ?? string.Empty;
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        }
    }
}
