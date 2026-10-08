// PORTED from WPF 7.1.5 Views/Tabs/SettingsTabView.xaml (FavoritesDrawer, FavoritesRail, the
// "Left or right?" foot) and SettingsTabView.xaml.cs ("the favorites drawer" region) (parity lane E3).
//
// Nav polish wave 3 (owner, 2026-10-06: "move the favourite rail on the other side, and make it
// collapsable ... we click to open it and to close as a drawer"). Column 3 of Home holds two
// things: the HANDLE (22 px, always there, a real Button) and the BODY, whose Width is the only
// thing that moves: 0 closed, 92 open, over 200 ms (instant under Motion Off). Closed by default;
// the state is AppSettings.FavoritesDrawerOpen and only the handle writes it. A pin while it is
// closed opens it for 2.5 s with the glow ring on the new chip and writes nothing. The body PUSHES
// the browser column (it never floats over it); the mosaic does not move.
//
// This control owns the frame, the handle juice and the click-choice foot. The chips are built by
// the shell (MainShellWindow.DashboardFavorites.cs) from the Ctrl+K palette rows, through
// BuildChip here so every chip has one face. Numbers: Core HomeDashboardRules.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.NavRail;

using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using Serilog;
using R = ConditioningControlPanel.Services.HomeDashboardRules;

namespace ConditioningControlPanel.Avalonia.Controls.Home
{
    public sealed class FavoritesDrawer : Grid
    {
        private readonly Button _handle;
        private readonly Border _handlePlate;
        private readonly TextBlock _star, _chevron, _handleLabel;
        private readonly ScaleTransform _starScale = new(1, 1);
        private readonly Grid _body;
        private readonly Border _rail;
        private readonly AmbientFxCanvas _fx = new() { IsHitTestVisible = false, Margin = new Thickness(-36, 0, 0, 0) };
        private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromMilliseconds(R.FavoritesDrawerPeekMs) };
        private DispatcherTimer? _twinkle;
        private bool _open, _peeking, _modHooked;
        private IDisposable? _visWatch;
        private Color _glow = ToColor(NavStripRules.Lilac);

        public StackPanel FavoritesList { get; } = new();
        public StackPanel RecentList { get; } = new();
        public TextBlock FavoritesEmpty { get; }
        public TextBlock RecentEmpty { get; }

        // The "Left or right?" foot.
        public Button ClickChoiceAnchor { get; }
        public Popup ClickChoicePopup { get; }
        public CheckBox InvertDashboardClicks { get; }
        public TextBlock ClickChoiceDescription { get; }

        /// <summary>The player flipped the "swap the clicks" box (the shell saves and repaints).</summary>
        public event Action<bool>? InvertClicksChanged;

        private bool _refreshingChoice;

        public FavoritesDrawer()
        {
            ColumnDefinitions = new ColumnDefinitions($"{R.FavoritesHandleWidth},Auto");

            // ---- the handle ------------------------------------------------------------------
            _star = new TextBlock
            {
                Text = "★", FontSize = 13, Margin = new Thickness(0, 9, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransform = _starScale, RenderTransformOrigin = RelativePoint.Center,
            };
            _handleLabel = new TextBlock
            {
                FontSize = 12, FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _handleLabel.Bind(TextBlock.TextProperty, Str("rail_favorites_drawer"));
            _chevron = new TextBlock
            {
                Text = R.DrawerChevron(false), FontSize = 16, FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Center,
            };
            var face = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            face.Children.Add(_star);
            var rotated = new LayoutTransformControl { LayoutTransform = new RotateTransform(-90), Child = _handleLabel, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(rotated, 1);
            face.Children.Add(rotated);
            Grid.SetRow(_chevron, 2);
            face.Children.Add(_chevron);

            _handlePlate = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), Child = face };
            _handle = new Button
            {
                Name = "FavoritesDrawerHandle",
                Margin = new Thickness(2, 5, 2, 5),
                Padding = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Template = new FuncControlTemplate<Button>((b, _) => new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
                }),
                Content = _handlePlate,
            };
            ToolTip.SetTip(_handle, Loc.Get("tooltip_rail_favorites_drawer"));
            _handle.Click += (_, _) => HandleClick();
            _handle.PointerEntered += (_, _) => { PaintHandle(hover: true); TwinkleStar(1.3, 150); };
            _handle.PointerExited += (_, _) => PaintHandle(hover: false);
            Children.Add(_handle);

            // ---- the body ----------------------------------------------------------------------
            FavoritesEmpty = EmptyText("rail_favorites_empty");
            RecentEmpty = EmptyText("rail_recent_empty");
            var stack = new StackPanel { Margin = new Thickness(4, 10, 4, 10), Width = R.FavoritesChipWidth, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(Caption("rail_favorites_caption", new Thickness(0, 0, 0, 3)));
            stack.Children.Add(FavoritesList);
            stack.Children.Add(FavoritesEmpty);
            stack.Children.Add(Caption("rail_recent_caption", new Thickness(0, 8, 0, 6)));
            stack.Children.Add(RecentList);
            stack.Children.Add(RecentEmpty);
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = stack,
            };

            // The foot: "Left or right?" opens the click choice (hover peeks, a click pins it).
            ClickChoiceDescription = new TextBlock { FontSize = 13, LineHeight = 20, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            InvertDashboardClicks = new CheckBox { Foreground = Brushes.White, FontSize = 13, Cursor = new Cursor(StandardCursorType.Hand) };
            InvertDashboardClicks.Bind(ContentControl.ContentProperty, Str("dash_click_invert"));
            InvertDashboardClicks.IsCheckedChanged += (_, _) =>
            {
                if (_refreshingChoice) return;
                InvertClicksChanged?.Invoke(InvertDashboardClicks.IsChecked == true);
            };
            var choiceTitle = new TextBlock { FontSize = 17, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 10) };
            choiceTitle.Bind(TextBlock.TextProperty, Str("dash_click_choice"));
            choiceTitle.Bind(TextBlock.ForegroundProperty, choiceTitle.GetResourceObservable("PinkBrush"));
            var pagesPin = new TextBlock { FontSize = 12, LineHeight = 18, Foreground = new SolidColorBrush(Color.Parse("#CCCCDF")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
            pagesPin.Bind(TextBlock.TextProperty, Str("dash_click_pages_pin"));
            var choiceBody = new Border
            {
                Width = 290, Padding = new Thickness(18), CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.Parse("#FF1B1B30")), BorderThickness = new Thickness(1),
                Child = new StackPanel { Children = { choiceTitle, ClickChoiceDescription, InvertDashboardClicks, pagesPin } },
            };
            choiceBody.Bind(Border.BorderBrushProperty, choiceBody.GetResourceObservable("SecondaryBrush"));

            var anchorText = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            anchorText.Bind(TextBlock.TextProperty, Str("dash_click_choice"));
            ClickChoiceAnchor = new Button
            {
                FontSize = 11, FontWeight = FontWeight.SemiBold, Padding = new Thickness(2, 8),
                Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, Content = anchorText,
            };
            ClickChoiceAnchor.Bind(TemplatedControl.ForegroundProperty, ClickChoiceAnchor.GetResourceObservable("TextLightBrush"));
            ClickChoicePopup = new Popup
            {
                PlacementTarget = ClickChoiceAnchor, Placement = PlacementMode.Left, HorizontalOffset = -6,
                IsLightDismissEnabled = true, Child = choiceBody,
            };
            ClickChoiceAnchor.Click += (_, _) => { RefreshChoiceText(); ClickChoicePopup.IsOpen = true; };
            var foot = new Border
            {
                Margin = new Thickness(8, 0, 8, 0), Padding = new Thickness(0, 5, 0, 7),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new Grid { Children = { ClickChoiceAnchor, ClickChoicePopup } },
            };
            foot.Bind(Border.BorderBrushProperty, foot.GetResourceObservable("GlassBorderBrush"));
            Grid.SetRow(foot, 1);

            var railGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            railGrid.Children.Add(scroll);
            railGrid.Children.Add(foot);
            _rail = new Border
            {
                Name = "FavoritesRail",
                Width = R.FavoritesRailWidth, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 5, 5), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Child = railGrid,
            };
            _rail.Bind(Border.BackgroundProperty, _rail.GetResourceObservable("ElevatedSurfaceBrush"));
            _body = new Grid { Name = "FavoritesDrawerBody", Width = 0, ClipToBounds = true, Children = { _rail } };
            Grid.SetColumn(_body, 1);
            Children.Add(_body);

            // Drawn last so it sits over the handle, 36 px wider to the left so a burst spills onto the page.
            Grid.SetColumnSpan(_fx, 2);
            Children.Add(_fx);

            _peekTimer.Tick += (_, _) => EndPeek(animate: true);
            AttachedToVisualTree += (_, _) =>
            {
                if (!_modHooked) { CoreMods.ModChanged += OnModChanged; _modHooked = true; }
                _visWatch?.Dispose();
                _visWatch = EffectiveVisibility.Watch(this, UpdateFx);
                Paint();
                UpdateFx();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                if (_modHooked) { CoreMods.ModChanged -= OnModChanged; _modHooked = false; }
                _visWatch?.Dispose();
                _visWatch = null;
                _peekTimer.Stop();
                StopFx();
            };
        }

        // ---- state ------------------------------------------------------------------------------

        internal bool IsOpen => _open;
        internal bool IsPeeking => _peeking;
        internal double BodyTargetWidth => R.DrawerBodyWidth(_open);
        internal Button Handle => _handle;
        internal Grid Body => _body;

        /// <summary>Reads the saved preference (startup, a settings reload). A running peek wins.</summary>
        public void ApplySetting()
        {
            if (_peeking) return;
            SetOpen(CoreSettings.Current.FavoritesDrawerOpen, animate: false);
        }

        public void SetOpen(bool open, bool animate)
        {
            _open = open;
            _chevron.Text = R.DrawerChevron(open);
            _body.Transitions = animate && AmbientFxCanvas.Env.AllowTransitions
                ? new Transitions { new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(R.FavoritesDrawerSlideMs), Easing = new CubicEaseOut() } }
                : null;
            _body.Width = R.DrawerBodyWidth(open);
        }

        private void HandleClick()
        {
            // A click during a pin's peek reads what the player sees: the body is out, so the
            // handle puts it away.
            _peeking = false;
            _peekTimer.Stop();
            bool open = !_open;
            Burst(open ? 90 : 60);
            TwinkleStar(1.45, 220);
            SetOpen(open, animate: true);
            var s = CoreSettings.Current;
            if (s.FavoritesDrawerOpen == open) return;
            s.FavoritesDrawerOpen = open;
            try { CoreSettings.Save(); }
            catch (Exception ex) { Log.Debug("Favorites drawer save: {E}", ex.Message); }
        }

        /// <summary>A new pin: slide a closed drawer out for 2.5 s and ring the chip. Writes nothing.</summary>
        public void Peek(string pinnedId)
        {
            if (!IsEffectivelyVisible) return;
            int glowAfter = 0;
            if (!_open)
            {
                _peeking = true;
                SetOpen(true, animate: true);
                glowAfter = AmbientFxCanvas.Env.AllowTransitions ? R.FavoritesDrawerSlideMs + 40 : 0;
            }
            Burst(90);
            TwinkleStar(1.45, 220);
            if (_peeking) { _peekTimer.Stop(); _peekTimer.Start(); }
            DispatcherTimer.RunOnce(() =>
            {
                try { NavGlow.Once(FindChip(pinnedId), NavStripRules.Lilac, why: "favorites pin " + pinnedId); }
                catch (Exception ex) { Log.Debug("Favorites pin glow: {E}", ex.Message); }
            }, TimeSpan.FromMilliseconds(Math.Max(1, glowAfter)));
        }

        internal Control? FindChip(string id)
        {
            foreach (var child in FavoritesList.Children)
                if (child is Control c && c.Tag is string tag && string.Equals(tag, id, StringComparison.Ordinal))
                    return c;
            return null;
        }

        private void EndPeek(bool animate)
        {
            _peekTimer.Stop();
            if (!_peeking) return;
            _peeking = false;
            SetOpen(CoreSettings.Current.FavoritesDrawerOpen, animate);
        }

        // ---- the click choice ----------------------------------------------------------------

        /// <summary>Repaints the foot's popover from the saved swap.</summary>
        public void RefreshChoiceText()
        {
            _refreshingChoice = true;
            bool invert = CoreSettings.Current.DashboardInvertClicks;
            InvertDashboardClicks.IsChecked = invert;
            ClickChoiceDescription.Text = Loc.Get(invert ? "dash_click_swapped" : "dash_click_default");
            _refreshingChoice = false;
        }

        // ---- the chips -------------------------------------------------------------------------

        private static readonly IBrush ChipScrim = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#00000000"), 0.28),
                new GradientStop(Color.Parse("#73000000"), 0.62),
                new GradientStop(Color.Parse("#DE000000"), 1),
            },
        };

        /// <summary>The foot band every chip wears so the caption reads over the art.</summary>
        public static Border Scrim() => new() { CornerRadius = new CornerRadius(8), IsHitTestVisible = false, Background = ChipScrim };

        /// <summary>
        /// One chip, 69 x 36: <paramref name="face"/> is its Background (the picture, clipped by the
        /// rounded border for free) and <paramref name="layers"/> go over it, then the caption.
        /// </summary>
        public static Button BuildChip(string id, string caption, IBrush? face, IEnumerable<Control> layers, bool locked, string tooltip)
        {
            var grid = new Grid();
            foreach (var l in layers) grid.Children.Add(l);
            grid.Children.Add(new TextBlock
            {
                Text = caption, FontSize = 8.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 3),
            });
            if (locked)
                grid.Children.Add(new TextBlock
                {
                    Text = "\U0001F512", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 3, 0),
                });

            var tint = new Border { CornerRadius = new CornerRadius(8), Opacity = 0, IsHitTestVisible = false };
            tint.Bind(Border.BackgroundProperty, tint.GetResourceObservable("TransparentPinkBrush"));
            var cb = new Border
            {
                CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
                Child = new Grid { Children = { tint, grid } },
            };
            if (face != null) cb.Background = face;
            else cb.Bind(Border.BackgroundProperty, cb.GetResourceObservable("AccentTintedBgBrush"));
            IBrush? restBorder = null;
            if (locked) cb.Bind(Border.BorderBrushProperty, cb.GetResourceObservable("Tier1GoldBorderBrush"));
            else cb.Bind(Border.BorderBrushProperty, cb.GetResourceObservable("GlassBorderBrush"));

            var chip = new Button
            {
                Tag = id,
                Height = R.FavoritesChipHeight,
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Template = new FuncControlTemplate<Button>((b, _) => new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
                }),
                Content = cb,
            };
            ToolTip.SetTip(chip, tooltip);
            chip.PointerEntered += (_, _) =>
            {
                restBorder = cb.BorderBrush;
                if (chip.TryFindResource("PinkBrush", chip.ActualThemeVariant, out var pink) && pink is IBrush pb) cb.BorderBrush = pb;
                tint.Opacity = 1;
            };
            chip.PointerExited += (_, _) =>
            {
                if (restBorder != null) cb.BorderBrush = restBorder;
                tint.Opacity = 0;
                cb.Opacity = 1;
            };
            chip.AddHandler(PointerPressedEvent, (_, _) => cb.Opacity = 0.82, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            chip.AddHandler(PointerReleasedEvent, (_, _) => cb.Opacity = 1, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            return chip;
        }

        // ---- the handle's colour and juice (the mod's glow colour; owner, 2026-10-06) -------

        private void OnModChanged(object? sender, Models.ModPackage e) =>
            Dispatcher.UIThread.Post(() => { Paint(); if (_fx.IsRunning) _fx.RefreshPalette(); });

        /// <summary>Repaints the handle and the rail rim from the active mod's glow colour.</summary>
        public void Paint()
        {
            try
            {
                if (this.TryFindResource("FxGlowColor", ActualThemeVariant, out var c) && c is Color glow) _glow = glow;
                PaintHandle(hover: _handle.IsPointerOver);
                _rail.BorderBrush = new SolidColorBrush(WithAlpha(_glow, 0x80));
                ApplyHandleGlow();
            }
            catch (Exception ex) { Log.Debug("Favorites drawer paint: {E}", ex.Message); }
        }

        private void PaintHandle(bool hover)
        {
            _handlePlate.Background = new SolidColorBrush(WithAlpha(_glow, hover ? (byte)0x4D : (byte)0x26));
            _handlePlate.BorderBrush = new SolidColorBrush(WithAlpha(_glow, hover ? (byte)0xFF : (byte)0x80));
            var text = new SolidColorBrush(Lighten(_glow, hover ? 0.55 : 0.18));
            _star.Foreground = text;
            _chevron.Foreground = text;
            _handleLabel.Foreground = text;
        }

        /// <summary>The handle breathes a glow (BoxShadow; never an Effect over a chip). The
        /// breath is a slow BoxShadow transition ping-pong, only while ambient loops are allowed.</summary>
        private DispatcherTimer? _breath;
        private bool _breathHigh;

        private void ApplyHandleGlow()
        {
            _breath?.Stop();
            if (!AmbientFxCanvas.Env.AllowTransitions) { _handlePlate.BoxShadow = default; return; }
            SetGlow((R.FavoritesGlowLow + R.FavoritesGlowHigh) / 2);
            if (!AmbientFxCanvas.Env.AllowAmbientLoops) return;
            _handlePlate.Transitions = new Transitions
            {
                new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = TimeSpan.FromMilliseconds(R.FavoritesGlowBreathMs), Easing = new SineEaseInOut() },
            };
            _breath ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(R.FavoritesGlowBreathMs) };
            _breath.Tick -= Breathe;
            _breath.Tick += Breathe;
            _breath.Start();
        }

        private void Breathe(object? sender, EventArgs e)
        {
            if (!IsEffectivelyVisible) return;
            _breathHigh = !_breathHigh;
            SetGlow(_breathHigh ? R.FavoritesGlowHigh : R.FavoritesGlowLow);
        }

        private void SetGlow(double opacity) =>
            _handlePlate.BoxShadow = new BoxShadows(new BoxShadow { Color = WithAlpha(_glow, (byte)Math.Round(opacity * 255)), Blur = 18 });

        private void UpdateFx()
        {
            if (this.IsAttachedToVisualTree() && IsEffectivelyVisible) StartFx();
            else StopFx();
        }

        private void StartFx()
        {
            try
            {
                if (_fx.IsRunning) _fx.Resume();
                else _fx.StartLayers(new AmbientFxConfig { Layers = AmbientFxLayers.Embers | AmbientFxLayers.DustField, Intensity = 0.9 });
                if (AmbientFxCanvas.Env.AllowAmbientLoops)
                {
                    _twinkle ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(R.FavoritesTwinkleEveryMs) };
                    _twinkle.Tick -= TwinkleTick;
                    _twinkle.Tick += TwinkleTick;
                    _twinkle.Start();
                }
                if (_breath != null && AmbientFxCanvas.Env.AllowAmbientLoops) _breath.Start();
            }
            catch (Exception ex) { Log.Debug("Favorites drawer fx: {E}", ex.Message); }
        }

        private void StopFx()
        {
            try { _fx.Pause(); } catch { }
            _twinkle?.Stop();
            _breath?.Stop();
        }

        private void TwinkleTick(object? sender, EventArgs e)
        {
            if (!IsEffectivelyVisible || _handle.IsPointerOver) return;
            TwinkleStar(1.3, 190);
        }

        internal void TwinkleStar(double scale, int ms)
        {
            if (!AmbientFxCanvas.Env.AllowTransitions) return;
            var d = TimeSpan.FromMilliseconds(ms);
            _starScale.Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = d, Easing = new BackEaseOut() },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = d, Easing = new BackEaseOut() },
            };
            _starScale.ScaleX = _starScale.ScaleY = scale;
            DispatcherTimer.RunOnce(() => _starScale.ScaleX = _starScale.ScaleY = 1.0, d);
        }

        private void Burst(int count)
        {
            try
            {
                if (!_fx.IsRunning) return;
                var p = _handle.TranslatePoint(new Point(_handle.Bounds.Width / 2, _handle.Bounds.Height / 2), _fx);
                if (p is { } at) _fx.Burst(at.X, at.Y, null, count);
            }
            catch (Exception ex) { Log.Debug("Favorites drawer burst: {E}", ex.Message); }
        }

        // ---- helpers ---------------------------------------------------------------------------

        private static global::Avalonia.Data.Binding Str(string key) =>
            new($"[{key}]") { Source = LocalizationManager.Instance, Mode = global::Avalonia.Data.BindingMode.OneWay };

        private static TextBlock Caption(string key, Thickness margin)
        {
            var t = new TextBlock { FontSize = 9, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Center, Margin = margin };
            t.Bind(TextBlock.TextProperty, Str(key));
            t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("SecondaryBrush"));
            return t;
        }

        private static TextBlock EmptyText(string key)
        {
            var t = new TextBlock { FontSize = 8, Opacity = 0.85, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            t.Bind(TextBlock.TextProperty, Str(key));
            t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("TextMutedBrush"));
            return t;
        }

        private static Color ToColor(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        internal static Color Lighten(Color c, double t)
        {
            t = Math.Clamp(t, 0, 1);
            byte L(byte v) => (byte)Math.Round(v + (255 - v) * t);
            return Color.FromRgb(L(c.R), L(c.G), L(c.B));
        }
    }
}
