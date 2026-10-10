using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class SettingsTabView : UserControl
    {
        /// <summary>Width of the marquee's edge fade, in the banner's own units.</summary>
        private const double MarqueeFadePx = 40;

        public SettingsTabView()
        {
            InitializeComponent();
            // Esc in the click-choice popup closes it, it is not the panic key's press (TAB-8).
            Services.Safety.EscapeClaim.Mark(ClickChoiceBody);
            _clickChoiceClose.Tick += (_, _) =>
            {
                _clickChoiceClose.Stop();
                if (!_clickChoicePinned && !ClickChoiceAnchor.IsMouseOver && !ClickChoiceBody.IsMouseOver && !ClickChoiceBody.IsKeyboardFocusWithin)
                    ClickChoicePopup.IsOpen = false;
            };
            Loaded += (_, _) => RefreshClickPreference();
            Unloaded += (_, _) => { _clickChoiceClose.Stop(); ClickChoicePopup.IsOpen = false; };
            IsVisibleChanged += (_, _) => { if (!IsVisible) { _clickChoiceClose.Stop(); ClickChoicePopup.IsOpen = false; } };
            // The Dashboard is the one tab the app LANDS on - it ships Visible in MainWindow.xaml
            // and nothing calls ShowTab("settings") at startup - so its one-shot ? box explainer
            // has no navigation to ride. Wired in code rather than XAML because the seam is
            // MainWindow's, not this view's (same shape as DiscordTabView's hook for the Profile
            // tab); MainWindow.TabNavigation.OnDashboardTabVisibilityChanged owns every decision.
            IsVisibleChanged += (_, _) =>
            {
                try
                {
                    if (Window.GetWindow(this) is MainWindow mw)
                        mw.OnDashboardTabVisibilityChanged(IsVisible);
                }
                catch (Exception ex) { App.Logger?.Debug("SettingsTabView visibility hook: {E}", ex.Message); }
            };

            // Flashes v2: the flash tile wears the v2 pill while a Back Room flash style is owned.
            // PrizeGrants is the only ownership truth (never settings). The tab is mounted for the
            // app's life, but the hook is balanced on Loaded/Unloaded all the same.
            Loaded += (_, _) => { Services.Prizes.PrizeGrants.GrantsChanged += RefreshV2Badges; RefreshV2Badges(); };
            Unloaded += (_, _) => Services.Prizes.PrizeGrants.GrantsChanged -= RefreshV2Badges;

            // The favorites drawer (nav polish wave 3). Painted closed by the XAML; the saved
            // state lands here when settings already exist and again from InitFavoritesRail.
            _drawerPeekTimer.Tick += (_, _) => { _drawerPeekTimer.Stop(); if (!FavoritesDrawer.IsMouseOver) EndFavoritesDrawerPeek(animate: true); };
            FavoritesDrawer.MouseLeave += (_, _) => { if (_drawerPeeking && !_drawerPeekTimer.IsEnabled) EndFavoritesDrawerPeek(animate: true); };
            IsVisibleChanged += (_, _) => { if (!IsVisible && _drawerPeeking) EndFavoritesDrawerPeek(animate: false); };
            ApplyFavoritesDrawerSetting();
            // The handle wears the mod's colour and its juice (owner, 2026-10-06).
            PaintFavoritesDrawer();
            Loaded += (_, _) => { HookFavoritesDrawerMod(); StartFavoritesDrawerFx(); };
            Unloaded += (_, _) => { UnhookFavoritesDrawerMod(); StopFavoritesDrawerFx(); };
            IsVisibleChanged += (_, _) => { if (IsVisible) StartFavoritesDrawerFx(); else StopFavoritesDrawerFx(); };
            FavoritesDrawerHandle.MouseEnter += (_, _) => TwinkleFavoritesDrawerStar(1.3, 150);
        }

        // ------------------------------------------------------------ the favorites drawer

        /// <summary>The open body's width: the old column 0 (see FavoritesDrawerBody in the XAML).</summary>
        internal const double FavoritesDrawerWidth = 92;
        /// <summary>Open and close slide, in ms; instant under Motion Off.</summary>
        internal const int FavoritesDrawerSlideMs = 200;
        /// <summary>How long a pin keeps a closed drawer open before it closes again.</summary>
        internal const int FavoritesDrawerPeekMs = 2500;

        private readonly System.Windows.Threading.DispatcherTimer _drawerPeekTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(FavoritesDrawerPeekMs),
        };
        private bool _drawerOpen;
        private bool _drawerPeeking;

        /// <summary>True while the body is showing (open for good, or open for a pin's peek).</summary>
        internal bool FavoritesDrawerIsOpen => _drawerOpen;

        /// <summary>Paint the saved state with no slide (startup, settings reload).</summary>
        internal void ApplyFavoritesDrawerSetting()
        {
            if (_drawerPeeking) return;
            SetFavoritesDrawer(App.Settings?.Current?.FavoritesDrawerOpen == true, animate: false);
        }

        /// <summary>Open or close the body. Writes nothing: only the handle persists.</summary>
        internal void SetFavoritesDrawer(bool open, bool animate)
        {
            _drawerOpen = open;
            // The chevron points the way a click goes: left opens (the body comes in from the
            // edge), right closes.
            FavoritesDrawerChevron.Text = open ? "›" : "‹";
            double to = open ? FavoritesDrawerWidth : 0;
            double from = FavoritesDrawerBody.Width;   // the animated value while one is running
            FavoritesDrawerBody.Width = to;
            if (!animate || !Services.MotionFx.AllowTransitions || double.IsNaN(from) || from == to)
            {
                FavoritesDrawerBody.BeginAnimation(WidthProperty, null);
                return;
            }
            var slide = new System.Windows.Media.Animation.DoubleAnimation(from, to, TimeSpan.FromMilliseconds(FavoritesDrawerSlideMs))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
            };
            FavoritesDrawerBody.BeginAnimation(WidthProperty, slide);
        }

        private void FavoritesDrawerHandle_Click(object sender, RoutedEventArgs e)
        {
            // A click during a pin's peek reads what the player sees: the body is out, so the
            // handle puts it away.
            _drawerPeeking = false;
            _drawerPeekTimer.Stop();
            bool open = !_drawerOpen;
            BurstFavoritesDrawer(open ? 90 : 60);
            TwinkleFavoritesDrawerStar(1.45, 220);
            SetFavoritesDrawer(open, animate: true);
            var s = App.Settings?.Current;
            if (s == null || s.FavoritesDrawerOpen == open) return;
            s.FavoritesDrawerOpen = open;
            try { App.Settings?.Save(); }
            catch (Exception ex) { App.Logger?.Debug("Favorites drawer save: {E}", ex.Message); }
        }

        /// <summary>
        /// A destination was just pinned. Closed drawer on a visible Home: slide it out for
        /// <see cref="FavoritesDrawerPeekMs"/>, glow the new chip, then put it away again unless
        /// the pointer is inside (then it goes on MouseLeave). Open drawer: just the glow. Home
        /// not on screen (a pin from Ctrl+K on another page): nothing to show, nothing moves.
        /// </summary>
        internal void PeekFavoritesDrawer(string pinnedId)
        {
            if (!IsVisible) return;
            int glowAfter = 0;
            if (!_drawerOpen)
            {
                _drawerPeeking = true;
                SetFavoritesDrawer(true, animate: true);
                glowAfter = Services.MotionFx.AllowTransitions ? FavoritesDrawerSlideMs + 40 : 0;
            }
            BurstFavoritesDrawer(90);
            TwinkleFavoritesDrawerStar(1.45, 220);
            if (_drawerPeeking)
            {
                _drawerPeekTimer.Stop();
                _drawerPeekTimer.Start();
            }

            var glow = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, glowAfter)) };
            glow.Tick += (_, _) =>
            {
                glow.Stop();
                var chip = FindFavoriteChip(pinnedId);
                ConditioningControlPanel.Controls.NavRail.NavGlow.Once(chip, ConditioningControlPanel.Controls.NavRail.NavStripRules.Lilac, why: "favorites pin " + pinnedId);
            };
            glow.Start();
        }

        /// <summary>The FAVORITES chip for a palette id (chips carry the id in Tag).</summary>
        internal FrameworkElement? FindFavoriteChip(string id)
        {
            foreach (var child in FavoritesList.Children)
                if (child is FrameworkElement fe && fe.Tag is string tag && string.Equals(tag, id, StringComparison.Ordinal))
                    return fe;
            return null;
        }

        private void EndFavoritesDrawerPeek(bool animate)
        {
            if (!_drawerPeeking) return;
            _drawerPeeking = false;
            _drawerPeekTimer.Stop();
            SetFavoritesDrawer(App.Settings?.Current?.FavoritesDrawerOpen == true, animate);
        }

        // ---- the handle's colour and juice (owner, 2026-10-06: "the mod color and FX, add juice
        // and make it more noticeable, maybe some particles")

        /// <summary>Breathing glow on the handle: how far it swings and how long one breath takes.</summary>
        internal const double FavoritesGlowLow = 0.30, FavoritesGlowHigh = 0.85;
        internal const int FavoritesGlowBreathMs = 2600;
        /// <summary>The star twinkles on its own this often while ambient loops are allowed.</summary>
        internal const int FavoritesTwinkleEveryMs = 6500;

        private EventHandler<Models.ModPackage>? _drawerModHook;
        private System.Windows.Threading.DispatcherTimer? _drawerTwinkle;

        /// <summary>The six FavHandle* brushes and the rail border, from the mod's glow colour
        /// (FxTheme, the same source every ambient effect reads). Lilac only when no palette is up.</summary>
        internal void PaintFavoritesDrawer()
        {
            try
            {
                var c = Services.FxTheme.GlowColor;
                var res = FavoritesDrawer.Resources;
                res["FavHandleFill"] = Frozen(c, 0x26);
                res["FavHandleBorder"] = Frozen(c, 0x80);
                res["FavHandleFillHover"] = Frozen(c, 0x4D);
                res["FavHandleBorderHover"] = Frozen(c, 0xFF);
                res["FavHandleText"] = Frozen(Lighten(c, 0.18), 0xFF);
                res["FavHandleTextHover"] = Frozen(Lighten(c, 0.55), 0xFF);
                ApplyFavoritesDrawerGlow(c);
            }
            catch (Exception ex) { App.Logger?.Debug("Favorites drawer paint: {E}", ex.Message); }
        }

        /// <summary>A soft glow in the mod colour under the handle; it breathes while ambient
        /// loops are allowed, holds still under Reduced, and is absent under Off.</summary>
        private void ApplyFavoritesDrawerGlow(Color c)
        {
            if (!Services.MotionFx.AllowTransitions) { FavoritesDrawerHandle.Effect = null; return; }
            var glow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = c, BlurRadius = 18, ShadowDepth = 0, Opacity = (FavoritesGlowLow + FavoritesGlowHigh) / 2,
            };
            FavoritesDrawerHandle.Effect = glow;
            if (!Services.MotionFx.AllowAmbientLoops) return;
            var breath = new System.Windows.Media.Animation.DoubleAnimation(FavoritesGlowLow, FavoritesGlowHigh, TimeSpan.FromMilliseconds(FavoritesGlowBreathMs))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut },
            };
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(breath, MainWindow.AmbientFrameRate);
            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, breath);
        }

        private void HookFavoritesDrawerMod()
        {
            if (_drawerModHook != null || App.Mods == null) return;
            _drawerModHook = (_, _) => Dispatcher.BeginInvoke(new Action(() => { PaintFavoritesDrawer(); FavoritesDrawerFx?.StartLayers(FavoritesDrawerFxConfig()); }));
            App.Mods.ModChanged += _drawerModHook;
        }

        private void UnhookFavoritesDrawerMod()
        {
            if (_drawerModHook == null || App.Mods == null) return;
            App.Mods.ModChanged -= _drawerModHook;
            _drawerModHook = null;
        }

        /// <summary>Motes rising along the handle, in the mod's particle colour, sparse: the
        /// handle is 22 px wide and the canvas 58, so a handful reads as a glint, not weather.</summary>
        private static AmbientFxConfig FavoritesDrawerFxConfig() => new()
        {
            Layers = AmbientFxLayers.Embers | AmbientFxLayers.DustField,
            Intensity = 0.9,
            DustDensity = 0.35,
        };

        private void StartFavoritesDrawerFx()
        {
            try
            {
                if (FavoritesDrawerFx == null) return;
                if (FavoritesDrawerFx.IsRunning) FavoritesDrawerFx.Resume();
                else FavoritesDrawerFx.StartLayers(FavoritesDrawerFxConfig());
                if (Services.MotionFx.AllowAmbientLoops)
                {
                    _drawerTwinkle ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FavoritesTwinkleEveryMs) };
                    _drawerTwinkle.Tick -= FavoritesTwinkle_Tick;
                    _drawerTwinkle.Tick += FavoritesTwinkle_Tick;
                    _drawerTwinkle.Start();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Favorites drawer fx: {E}", ex.Message); }
        }

        private void StopFavoritesDrawerFx()
        {
            try { FavoritesDrawerFx?.Pause(); } catch { }
            _drawerTwinkle?.Stop();
        }

        private void FavoritesTwinkle_Tick(object? sender, EventArgs e)
        {
            if (!IsVisible || FavoritesDrawerHandle.IsMouseOver) return;
            TwinkleFavoritesDrawerStar(1.3, 190);
        }

        /// <summary>The star swells to <paramref name="scale"/> and settles back, <paramref name="ms"/> each way. No-op under Off.</summary>
        internal void TwinkleFavoritesDrawerStar(double scale, int ms)
        {
            if (!Services.MotionFx.AllowTransitions) return;
            if (FavoritesDrawerStar.RenderTransform is not ScaleTransform st) return;
            var up = new System.Windows.Media.Animation.DoubleAnimation(1.0, scale, TimeSpan.FromMilliseconds(ms))
            {
                AutoReverse = true,
                EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.6 },
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, up);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, up);
        }

        /// <summary>A spark burst at the handle's centre (mod particle colour). Nothing at the
        /// Performance tier or under reduced motion: the canvas refuses it itself.</summary>
        internal void BurstFavoritesDrawer(int count)
        {
            try
            {
                if (FavoritesDrawerFx == null || !FavoritesDrawerFx.IsRunning) return;
                var p = FavoritesDrawerHandle.TranslatePoint(new Point(FavoritesDrawerHandle.ActualWidth / 2, FavoritesDrawerHandle.ActualHeight / 2), FavoritesDrawerFx);
                FavoritesDrawerFx.Burst(p.X, p.Y, null, count);
            }
            catch (Exception ex) { App.Logger?.Debug("Favorites drawer burst: {E}", ex.Message); }
        }

        private static SolidColorBrush Frozen(Color c, byte alpha)
        {
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        /// <summary>Mix toward white by <paramref name="t"/> (0..1) so the label reads on the dark page.</summary>
        internal static Color Lighten(Color c, double t)
        {
            t = Math.Clamp(t, 0, 1);
            byte L(byte v) => (byte)Math.Round(v + (255 - v) * t);
            return Color.FromRgb(L(c.R), L(c.G), L(c.B));
        }

        private readonly System.Windows.Threading.DispatcherTimer _clickChoiceClose = new()
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        private bool _refreshingClickChoice;
        private bool _clickChoicePinned;

        internal void RefreshClickPreference()
        {
            _refreshingClickChoice = true;
            bool invert = App.Settings?.Current?.DashboardInvertClicks == true;
            InvertDashboardClicks.IsChecked = invert;
            ClickChoiceDescription.Text = Localization.Loc.Get(invert ? "dash_click_swapped" : "dash_click_default");
            DashToggleHint.Text = Localization.Loc.Get(invert ? "dash_click_swapped" : "dash_toggle_hint");
            _refreshingClickChoice = false;
        }

        private void HoverClickChoice(object sender, MouseEventArgs e)
        {
            _clickChoiceClose.Stop();
            if (ClickChoicePopup.IsOpen) return;
            RefreshClickPreference();
            // Hover must not capture the mouse: capture makes the anchor lose IsMouseOver.
            ClickChoicePopup.StaysOpen = true;
            ClickChoicePopup.IsOpen = true;
        }

        private void OpenClickChoice(object sender, RoutedEventArgs e)
        {
            _clickChoiceClose.Stop();
            RefreshClickPreference();
            _clickChoicePinned = true;
            ClickChoicePopup.StaysOpen = false;
            ClickChoicePopup.IsOpen = true;
            InvertDashboardClicks.Focus();
        }

        private void ClickChoiceClosed(object sender, EventArgs e)
        {
            _clickChoiceClose.Stop();
            _clickChoicePinned = false;
            ClickChoicePopup.StaysOpen = true;
        }

        private void KeepClickChoiceOpen(object sender, MouseEventArgs e) => _clickChoiceClose.Stop();

        private void ScheduleClickChoiceClose(object sender, MouseEventArgs e)
        {
            _clickChoiceClose.Stop();
            if (!_clickChoicePinned) _clickChoiceClose.Start();
        }

        private void ClickChoiceKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            Services.Safety.EscapeClaim.Taken();
            ClickChoicePopup.IsOpen = false;
            e.Handled = true;
            ClickChoiceAnchor.Focus();
        }

        private void ClickChoiceChanged(object sender, RoutedEventArgs e)
        {
            if (_refreshingClickChoice || App.Settings?.Current is not { } settings) return;
            _clickChoicePinned = true;
            _clickChoiceClose.Stop();
            ClickChoicePopup.StaysOpen = false;
            settings.DashboardInvertClicks = InvertDashboardClicks.IsChecked == true;
            App.Settings.Save();
            RefreshClickPreference();
        }

        private void FeatureSection_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is FrameworkElement section && section.ActualWidth > 0 && section.ActualHeight > 0)
            {
                var clip = new RectangleGeometry(new Rect(0, 0, section.ActualWidth, section.ActualHeight), 20, 20);
                clip.Freeze();
                section.Clip = clip;
            }
        }

        /// <summary>Re-reads ownership onto the wall's v2 pills. Safe from any thread.</summary>
        private void RefreshV2Badges()
        {
            void Apply()
            {
                try
                {
                    // Which grants count as "flashes v2" (Jackpot Remix is a flash prize too) is
                    // V2Badges', shared with the Bubble Pop tile and the side rail's chips.
                    if (CardFlash != null)
                        CardFlash.ShowV2Badge = Services.Prizes.V2Badges.FlashOwned();
                }
                catch (Exception ex) { App.Logger?.Debug("SettingsTabView.RefreshV2Badges: {E}", ex.Message); }
            }
            if (Dispatcher.CheckAccess()) Apply(); else Dispatcher.BeginInvoke((Action)Apply);
        }

        /// <summary>
        /// Rebuilds the marquee's edge-fade mask whenever the banner is resized.
        ///
        /// Self-contained on purpose (no MainWindow hop): it needs nothing but its own width, and
        /// the banner resizes with every window resize. The brush uses
        /// <see cref="BrushMappingMode.Absolute"/> so its start/end points are this element's own
        /// coordinates rather than a bounding box - the shipped relative-mapped version was
        /// stretched across the marquee text's full (thousands of pixels wide) bounds, which put
        /// its 6% fade entirely off-screen and made the effect invisible. Absolute mapping also
        /// means the fade stays a constant ~40px instead of growing with the window.
        /// </summary>
        private void MarqueeFadeHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement host) return;
                double w = e.NewSize.Width;
                if (double.IsNaN(w) || w <= 16) { host.OpacityMask = null; return; }

                // Never eat more than a quarter of the banner from each side on a narrow window.
                double fade = Math.Min(MarqueeFadePx, w / 4.0);
                double stop = fade / w;

                var brush = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(w, 0),
                };
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
                brush.GradientStops.Add(new GradientStop(Colors.Black, stop));
                brush.GradientStops.Add(new GradientStop(Colors.Black, 1.0 - stop));
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                brush.Freeze();
                host.OpacityMask = brush;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MarqueeFadeHost_SizeChanged: {E}", ex.Message);
            }
        }

        // PHASE 8 (demolition): 41 shims left this file with LegacyDashboardHost and the Collapsed
        // CardSystem tile - the whole Flash/Visuals/Video/Subliminal dial set, the ChkDualMon and
        // performance twins, and CardSystem_Click. Nothing they reached was lost: the dials live on
        // the Studio rack's *FeatureControl panels, the performance switches on
        // Views/Controls/AppSettings/PerformanceSettingsSection, ChkDualMon on
        // Features/SystemFeatureControl.ChkMultiMon, and the System popup on VelvetBtnSystem_Click
        // below - which is now the ONLY caller of MainWindow.CardSystem_Click.
        private void BrowserLoadingText_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BrowserLoadingText_Click(sender, e);
        }
        private void BrowserSiteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BrowserSiteToggle_Click(sender, e);
        }
        // Phase 2: BtnAudioOutputRefresh_Click / BtnAudioLayers_Click moved with the Audio
        // section to Views/Controls/AppSettings/AudioSettingsSection.xaml.cs.
        // Phase 2: BtnClearStartupVideo_Click / BtnSelectStartupVideo_Click moved with the startup
        // group to Views/Controls/AppSettings/GeneralSettingsSection.xaml.cs.
        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDiscord_Click(sender, e);
        }
        private void BtnPopOutBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPopOutBrowser_Click(sender, e);
        }
        private void BtnMuteBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnMuteBrowser_Click(sender, e);
        }
        private void BtnQuickLogout_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnQuickLogout_Click(sender, e);
        }
        private void BtnLinkPhone_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LinkPhoneDialog { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
        }
        private void BtnReloadBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnReloadBrowser_Click(sender, e);
        }
        // The browser card's fold arrow. MainWindow.DashboardFold.cs owns the setting, the row
        // arithmetic and the height ease; this view only carries the button and paints it.
        private void BtnFoldBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnFoldBrowser_Click(sender, e);
        }

        /// <summary>
        /// Paints the fold arrow for the state the fold's one bool says (owner, 2026-10-06: the
        /// arrow must be unmissable). Home's hue from the one hue table, the glyph and the label
        /// for what a click will do, and while FOLDED a breathing glow so a shut browser is
        /// obviously openable: a loop only under <see cref="MotionFx.AllowAmbientLoops"/>, a static
        /// glow under Reduced (or a tier that refuses loops), nothing at Motion Off. Open, no glow.
        /// Idempotent: the settle calls it every time it runs.
        /// </summary>
        internal void PaintFoldArrow(bool collapsed)
        {
            var btn = BtnFoldBrowser;
            if (btn == null) return;
            var hue = NavStripRules.Accent(NavSections.Home);
            btn.Background = Tint(hue, BrowserFoldRule.ArrowFill);
            btn.Tag = Tint(hue, BrowserFoldRule.ArrowHoverFill);
            btn.BorderBrush = Tint(hue, BrowserFoldRule.ArrowBorder);
            if (TxtFoldBrowser != null) TxtFoldBrowser.Text = BrowserFoldRule.Chevron(collapsed);
            var labelKey = BrowserFoldRule.LabelKey(collapsed);
            // Rebind rather than assign, so a language switch re-reads the label live.
            if (TxtFoldBrowserLabel != null)
                BindingOperations.SetBinding(TxtFoldBrowserLabel, TextBlock.TextProperty,
                    new Binding($"[{labelKey}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(btn, AutomationProperties.NameProperty,
                new Binding($"[{labelKey}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(btn, FrameworkElement.ToolTipProperty,
                new Binding($"[{BrowserFoldRule.TooltipKey(collapsed)}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });

            var glow = btn.Effect as DropShadowEffect;
            glow?.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            if (!collapsed || MotionFx.Level == MotionLevel.Off)
            {
                btn.Effect = null;
                return;
            }
            if (glow == null)
            {
                glow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 16 };
                btn.Effect = glow;
            }
            glow.Color = hue;
            if (MotionFx.AllowAmbientLoops)
            {
                glow.Opacity = BrowserFoldRule.GlowLow;
                var breath = new DoubleAnimation(BrowserFoldRule.GlowLow, BrowserFoldRule.GlowHigh,
                    TimeSpan.FromMilliseconds(BrowserFoldRule.GlowBreathMs / 2))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                // An ambient loop: 24 fps is plenty for a breath and keeps the cost down.
                Timeline.SetDesiredFrameRate(breath, 24);
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, breath);
            }
            else
            {
                glow.Opacity = BrowserFoldRule.GlowStatic;
            }
        }

        private static SolidColorBrush Tint(Color hue, double alpha)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * alpha), hue.R, hue.G, hue.B));
            b.Freeze();
            return b;
        }
        // Phase 2: BtnExportPhrases_Click / BtnImportPhrases_Click moved with the phrase-backup
        // card to Views/Controls/AppSettings/DataSettingsSection.xaml.cs.
        private void BtnUnifiedLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnUnifiedLogin_Click(sender, e);
        }
        private void BtnWebcamTracking_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWebcamTracking_Click(sender, e);
        }
        // ---- velvet mosaic (4x4 hybrid wall, 2026-08-11) ---------------------------------
        // Third wall in two days; the seven destination shims (CardDtrh/CardGoon/CardFyp/
        // CardIntake/CardRemote/CardLoom/CardDeeper) went with their tiles - those features
        // kept their Play-door cards throughout, so nothing lost a home. The FX tiles are
        // back, split into left-click-opens (Card*_Click -> OpenStudioModule) and
        // right-click-toggles (Card*_Toggle -> ToggleWallFeature); the three diagonal combo
        // tiles forward per-half. Same shape as ever: the view forwards, MainWindow decides.
        private void CardFlash_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CardFlash_Click(sender, e);
        }
        private void CardFlash_Toggle(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("flash");
        }
        private void CardSubliminal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CardSubliminal_Click(sender, e);
        }
        private void CardSubliminal_Toggle(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("subliminal");
        }
        private void CardBouncingText_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CardBouncingText_Click(sender, e);
        }
        private void CardBouncingText_Toggle(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("bouncingtext");
        }
        private void CardBubblePop_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CardBubblePop_Click(sender, e);
        }
        private void CardBubblePop_Toggle(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("bubbles");
        }
        private void CardLockCard_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CardLockCard_Click(sender, e);
        }
        private void CardLockCard_Toggle(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("lockcard");
        }
        // Diagonal combos: A = the top-left half, B = the bottom-right, as authored in XAML.
        private void ComboVideoBubble_ClickA(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.OpenStudioModule("video");
        }
        private void ComboVideoBubble_ClickB(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.OpenStudioModule("bubblecount");
        }
        private void ComboVideoBubble_ToggleA(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("video");
        }
        private void ComboVideoBubble_ToggleB(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("bubblecount");
        }
        private void ComboSpiralPink_ClickA(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.OpenStudioModule("spiral");
        }
        private void ComboSpiralPink_ClickB(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.OpenStudioModule("pinkfilter");
        }
        private void ComboSpiralPink_ToggleA(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("spiral");
        }
        private void ComboSpiralPink_ToggleB(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("pinkfilter");
        }
        private void ComboMindDrain_ClickA(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.OpenStudioModule("mindwipe");
        }
        private void ComboMindDrain_ClickB(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.OpenStudioModule("braindrain");
        }
        private void ComboMindDrain_ToggleA(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("mindwipe");
        }
        private void ComboMindDrain_ToggleB(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature("braindrain");
        }
        private void CardMystery_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CardMystery_Click(sender, e);
        }
        /// <summary>Clicking the revealed face is clicking the box - one navigation, two faces.</summary>
        private void MysteryRevealFace_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CardMystery_Click(sender, e);
        }
        private void CardVault_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CardVault_Click(sender, e);
        }
        private void CardDeeperEditor_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardDeeperEditor_Click(sender, e);
        }
        private void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkDiscordRichPresence_Changed(sender, e);
        }
        // Phase 2: ChkEnableDeeper_Changed moved with the Deeper master switch to
        // Views/Controls/AppSettings/GeneralSettingsSection.xaml.cs.
        // BtnPanicKey_Click / ChkNoPanic_Changed left with their controls in Phase 2 — the panic
        // key is rebound and disabled in Settings → Devices now.
        // Phase 2: ChkOfflineMode_Changed moved with the offline toggle to
        // Views/Controls/AppSettings/DataSettingsSection.xaml.cs.
        // Phase 2: ChkStartHidden_Click / ChkWinStart_Click moved with the startup group to
        // Views/Controls/AppSettings/GeneralSettingsSection.xaml.cs.
        private void ImgLogo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ImgLogo_MouseLeftButtonDown(sender, e);
        }
        // Weekly intake pass card face (the flipped-over centre logo tile). Sits INSIDE
        // LogoBrandFrame, so the click would otherwise bubble on into the logo's click-pulse
        // easter egg; MainWindow's handler marks the event Handled to stop that.
        private void IntakePassFace_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.IntakePassFace_MouseLeftButtonDown(sender, e);
        }
        // Phase 3: SliderAudioSyncLatency_Changed / SliderAudioSyncIntensity_Changed moved with
        // the audio-sync tuning panel to Views/Controls/AppSettings/AudioSettingsSection.xaml.cs.
        private void ToggleEnhanceIfPossible_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ToggleEnhanceIfPossible_Changed(sender, e);
        }
        private void ChkForceShowBambiCloud_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkForceShowBambiCloud_Changed(sender, e);
        }
        private void VelvetBtnAppInfo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.VelvetBtnAppInfo_Click(sender, e);
        }
        private void VelvetBtnSchedulerRamp_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.VelvetBtnSchedulerRamp_Click(sender, e);
        }
        private void VelvetBtnWebcam_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.VelvetBtnWebcam_Click(sender, e);
        }
        private void VelvetBtnCatalogue_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCatalogue_Click(sender, e);
        }
        /// <summary>
        /// Quick-toggles row · "System". Phase 3 traded the ⚙ mosaic tile for the Brain Drain
        /// rescue and moved its entry point here; the tile itself is still in the tree (Collapsed)
        /// and both callers reach the SAME <c>MainWindow.CardSystem_Click</c>, so the popup, its
        /// mod-aware title and its <c>NotifyFeatureOpened("System")</c> bark are byte-for-byte
        /// what they were.
        /// </summary>
        private void VelvetBtnSystem_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardSystem_Click(sender, e);
        }
        // Home audio card. Pure forwarding, like every re-parented cell: the shell owns the
        // canonical Settings/Audio controls and mirrors both ways. See MainWindow.HomeAudio.cs.
        private void HomeSliderMaster_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeSliderMaster_Changed(sender, e);
        }

        private void HomeChkAudioDuck_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeChkAudioDuck_Changed(sender, e);
        }

        /// <summary>The advanced disclosure. Purely local - nothing below it owns state, so the
        /// shell has no reason to know whether the drawer is open.</summary>
        private void HomeBtnAudioAdvanced_Changed(object sender, RoutedEventArgs e)
        {
            if (HomeAudioAdvanced == null || HomeBtnAudioAdvanced == null) return;
            HomeAudioAdvanced.Visibility = HomeBtnAudioAdvanced.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void HomeSliderVideoVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeSliderVideoVolume_Changed(sender, e);
        }

        private void HomeSliderDuck_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeSliderDuck_Changed(sender, e);
        }

        private void HomeChkExcludeBambiCloudDucking_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeChkExcludeBambiCloudDucking_Changed(sender, e);
        }

        private void HomeCmbAudioOutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeCmbAudioOutputDevice_SelectionChanged(sender, e);
        }

        private void HomeBtnAudioOutputRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnAudioOutputRefresh_Click(sender, e);
        }

        private void HomeBtnTestAudio_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnTestAudio_Click(sender, e);
        }

        // Self-contained on the old dashboard too - it only opens a window.
        private void HomeBtnAudioLayers_Click(object sender, RoutedEventArgs e)
            => LayeredAudioWindow.Open(this);

        // Training Programs "Today" card (row 0). Loaded is the card's own first-paint hook so
        // the dashboard can show it without the user visiting the Programs tab first.
        private void ProgramTodayCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ProgramTodayCard_Loaded(sender, e);
        }
        private void ProgramTodayCard_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ProgramTodayCard_Click(sender, e);
        }
    }
}
