using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The favorites drawer at the right edge of Home, PORTED from WPF SettingsTabView.xaml.cs
    /// "the favorites drawer" region (11552cadf drawer, e2c475a8a mod colour + juice). Closed by
    /// default; AppSettings.FavoritesDrawerOpen persists and only the handle writes it. A pin
    /// while it is closed slides it out for 2.5 s, then puts it away unless the pointer is inside.
    /// </summary>
    public partial class SettingsTabView
    {
        internal const double FavoritesDrawerWidth = 92;
        internal const int FavoritesDrawerSlideMs = 200;
        internal const int FavoritesDrawerPeekMs = 2500;
        internal const double FavoritesGlowLow = 0.30, FavoritesGlowHigh = 0.85;
        internal const int FavoritesGlowBreathMs = 2600;
        internal const int FavoritesTwinkleEveryMs = 6500;

        private readonly DispatcherTimer _drawerPeekTimer = new() { Interval = TimeSpan.FromMilliseconds(FavoritesDrawerPeekMs) };
        private DispatcherTimer? _drawerTwinkle, _drawerBreath;
        private DropShadowEffect? _drawerGlow;
        private long _drawerBreathStart;
        private bool _drawerOpen, _drawerPeeking, _drawerModHooked;

        internal bool FavoritesDrawerIsOpen => _drawerOpen;
        internal bool FavoritesDrawerBreathing => _drawerBreath?.IsEnabled == true;

        /// <summary>False while the window is minimised: the shell's ApplyDashboardFxLoops funnel
        /// sets it, and every Home loop parks.</summary>
        private bool _hostShown = true;

        internal void ApplyHostShown(bool shown)
        {
            _hostShown = shown;
            SyncFavoritesDrawerFx();
            SyncFoldArrowBreath();
        }
        internal bool FavoritesDrawerIsPeeking => _drawerPeeking;

        /// <summary>Constructor hook (WPF ctor :59-70).</summary>
        private void InitFavoritesDrawer()
        {
            _drawerPeekTimer.Tick += (_, _) => FavoritesDrawerPeekElapsed();
            FavoritesDrawer.PointerExited += (_, _) => { if (_drawerPeeking && !_drawerPeekTimer.IsEnabled) EndFavoritesDrawerPeek(animate: true); };
            FavoritesDrawerHandle.Click += (_, _) => FavoritesDrawerHandleClicked();
            FavoritesDrawerHandle.PointerEntered += (_, _) => TwinkleFavoritesDrawerStar(1.3, 150);
            ApplyFavoritesDrawerSetting();
            PaintFavoritesDrawer();
            AttachedToVisualTree += (_, _) => { HookFavoritesDrawerMod(); SyncFavoritesDrawerFx(); SyncFoldArrowBreath(); };
            DetachedFromVisualTree += (_, _) => { UnhookFavoritesDrawerMod(); StopFavoritesDrawerFx(); _foldBreath?.Stop(); _drawerPeekTimer.Stop(); };
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (!IsVisible && _drawerPeeking) EndFavoritesDrawerPeek(animate: false);
                SyncFavoritesDrawerFx();
            };
        }

        /// <summary>Paint the saved state with no slide (startup, settings reload).</summary>
        internal void ApplyFavoritesDrawerSetting()
        {
            if (_drawerPeeking) return;
            SetFavoritesDrawer(CoreSettings.Current?.FavoritesDrawerOpen == true, animate: false);
        }

        /// <summary>Open or close the body. Writes nothing: only the handle persists.</summary>
        internal void SetFavoritesDrawer(bool open, bool animate)
        {
            _drawerOpen = open;
            FavoritesDrawerChevron.Text = open ? "›" : "‹";
            double to = open ? FavoritesDrawerWidth : 0;
            bool slide = animate && AmbientFxCanvas.Env.AllowTransitions;
            FavoritesDrawerBody.Transitions = slide
                ? new Transitions { new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(FavoritesDrawerSlideMs), Easing = new CubicEaseOut() } }
                : null;
            FavoritesDrawerBody.Width = to;
        }

        internal void FavoritesDrawerHandleClicked()
        {
            // A click during a pin's peek reads what the player sees: the body is out, so it goes away.
            _drawerPeeking = false;
            _drawerPeekTimer.Stop();
            bool open = !_drawerOpen;
            BurstFavoritesDrawer(open ? 90 : 60);
            TwinkleFavoritesDrawerStar(1.45, 220);
            SetFavoritesDrawer(open, animate: true);
            var s = CoreSettings.Current;
            if (s == null || s.FavoritesDrawerOpen == open) return;
            s.FavoritesDrawerOpen = open;
            try { CoreSettings.Save(); }
            catch (Exception ex) { Log.Debug("Favorites drawer save: {E}", ex.Message); }
        }

        /// <summary>A destination was just pinned (MainShellWindow.TogglePinned). Closed drawer on a
        /// visible Home: slide out for <see cref="FavoritesDrawerPeekMs"/>. Home hidden: nothing moves.</summary>
        internal void PeekFavoritesDrawer(string pinnedId)
        {
            if (!IsEffectivelyVisible) return;
            if (!_drawerOpen)
            {
                _drawerPeeking = true;
                SetFavoritesDrawer(true, animate: true);
            }
            BurstFavoritesDrawer(90);
            TwinkleFavoritesDrawerStar(1.45, 220);
            if (_drawerPeeking) { _drawerPeekTimer.Stop(); _drawerPeekTimer.Start(); }
        }

        internal void FavoritesDrawerPeekElapsed()
        {
            _drawerPeekTimer.Stop();
            if (!FavoritesDrawer.IsPointerOver) EndFavoritesDrawerPeek(animate: true);
        }

        private void EndFavoritesDrawerPeek(bool animate)
        {
            if (!_drawerPeeking) return;
            _drawerPeeking = false;
            _drawerPeekTimer.Stop();
            SetFavoritesDrawer(CoreSettings.Current?.FavoritesDrawerOpen == true, animate);
        }

        /// <summary>The six FavHandle* brushes (and so the rail border) from the mod's glow colour.</summary>
        internal void PaintFavoritesDrawer()
        {
            try
            {
                var c = AmbientFxCanvas.Env.GlowColor;
                var res = FavoritesDrawer.Resources;
                res["FavHandleFill"] = TintA(c, 0x26);
                res["FavHandleBorder"] = TintA(c, 0x80);
                res["FavHandleFillHover"] = TintA(c, 0x4D);
                res["FavHandleBorderHover"] = TintA(c, 0xFF);
                res["FavHandleText"] = TintA(Lighten(c, 0.18), 0xFF);
                res["FavHandleTextHover"] = TintA(Lighten(c, 0.55), 0xFF);
                if (_drawerGlow != null) _drawerGlow.Color = c;
            }
            catch (Exception ex) { Log.Debug("Favorites drawer paint: {E}", ex.Message); }
        }

        private void OnFavoritesDrawerModChanged(object? sender, ModPackage e) =>
            Dispatcher.UIThread.Post(() => { PaintFavoritesDrawer(); if (FavoritesDrawerFx.IsRunning) FavoritesDrawerFx.StartLayers(FavoritesDrawerFxConfig()); });

        private void HookFavoritesDrawerMod()
        {
            if (_drawerModHooked) return;
            CoreMods.ModChanged += OnFavoritesDrawerModChanged;
            _drawerModHooked = true;
        }

        private void UnhookFavoritesDrawerMod()
        {
            if (!_drawerModHooked) return;
            CoreMods.ModChanged -= OnFavoritesDrawerModChanged;
            _drawerModHooked = false;
        }

        private static AmbientFxConfig FavoritesDrawerFxConfig() => new()
        {
            // ponytail: WPF adds AmbientFxLayers.Embers; the Avalonia canvas has no Embers layer yet.
            Layers = AmbientFxLayers.DustField,
            Intensity = 0.9,
            DustDensity = 0.35,
        };

        /// <summary>Motes, glow breath and twinkle run only while Home is attached AND visible (P01).</summary>
        private void SyncFavoritesDrawerFx()
        {
            if (!IsVisible || VisualRoot == null || !_hostShown) { StopFavoritesDrawerFx(); return; }
            try
            {
                if (FavoritesDrawerFx.IsRunning) FavoritesDrawerFx.Resume();
                else FavoritesDrawerFx.StartLayers(FavoritesDrawerFxConfig());
                ApplyFavoritesDrawerGlow();
                if (AmbientFxCanvas.Env.AllowAmbientLoops)
                {
                    _drawerTwinkle ??= new DispatcherTimer(TimeSpan.FromMilliseconds(FavoritesTwinkleEveryMs), DispatcherPriority.Background, (_, _) =>
                    {
                        if (IsVisible && !FavoritesDrawerHandle.IsPointerOver) TwinkleFavoritesDrawerStar(1.3, 190);
                    });
                    _drawerTwinkle.Start();
                }
            }
            catch (Exception ex) { Log.Debug("Favorites drawer fx: {E}", ex.Message); }
        }

        private void StopFavoritesDrawerFx()
        {
            try { FavoritesDrawerFx.Pause(); } catch { }
            _drawerTwinkle?.Stop();
            _drawerBreath?.Stop();
        }

        /// <summary>WPF ApplyFavoritesDrawerGlow: a soft glow in the mod colour under the handle that
        /// breathes under ambient loops (24 fps), holds still under Reduced and is absent under Off.</summary>
        private void ApplyFavoritesDrawerGlow()
        {
            _drawerBreath?.Stop();
            if (!AmbientFxCanvas.Env.AllowTransitions) { FavoritesDrawerHandle.ClearValue(Visual.EffectProperty); _drawerGlow = null; return; }
            _drawerGlow ??= new DropShadowEffect { BlurRadius = 18, OffsetX = 0, OffsetY = 0 };
            _drawerGlow.Color = AmbientFxCanvas.Env.GlowColor;
            _drawerGlow.Opacity = (FavoritesGlowLow + FavoritesGlowHigh) / 2;
            FavoritesDrawerHandle.Effect = _drawerGlow;
            if (!AmbientFxCanvas.Env.AllowAmbientLoops) return;
            _drawerBreathStart = FxAdorner.Time.GetTimestamp();
            _drawerBreath ??= new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 24), DispatcherPriority.Background, (_, _) =>
            {
                if (_drawerGlow == null) return;
                double half = FavoritesGlowBreathMs / 1000.0;
                double t = FxAdorner.Time.GetElapsedTime(_drawerBreathStart).TotalSeconds % (2 * half);
                double u = t < half ? t / half : 2 - (t / half);
                _drawerGlow.Opacity = FavoritesGlowLow + ((FavoritesGlowHigh - FavoritesGlowLow) * (1 - Math.Cos(Math.PI * u)) / 2);
            });
            _drawerBreath.Start();
        }

        /// <summary>The star swells to <paramref name="scale"/> and settles back. No-op under Off.</summary>
        internal void TwinkleFavoritesDrawerStar(double scale, int ms)
        {
            if (!AmbientFxCanvas.Env.AllowTransitions) return;
            if (FavoritesDrawerStar.RenderTransform is not ScaleTransform st) return;
            var ease = new BackEaseOut();
            var up = TimeSpan.FromMilliseconds(ms);
            st.Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = up, Easing = ease },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = up, Easing = ease },
            };
            st.ScaleX = st.ScaleY = scale;
            DispatcherTimer.RunOnce(() => { st.ScaleX = st.ScaleY = 1.0; }, up);
        }

        /// <summary>A spark burst at the handle's centre; the canvas refuses it itself under reduced motion.</summary>
        internal void BurstFavoritesDrawer(int count)
        {
            try
            {
                if (!FavoritesDrawerFx.IsRunning) return;
                var b = FavoritesDrawerHandle.Bounds;
                var p = FavoritesDrawerHandle.TranslatePoint(new Point(b.Width / 2, b.Height / 2), FavoritesDrawerFx);
                if (p is { } at) FavoritesDrawerFx.Burst(at.X, at.Y, null, count);
            }
            catch (Exception ex) { Log.Debug("Favorites drawer burst: {E}", ex.Message); }
        }

        private static SolidColorBrush TintA(Color c, byte alpha) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

        /// <summary>Mix toward white by <paramref name="t"/> so the label reads on the dark page.</summary>
        internal static Color Lighten(Color c, double t)
        {
            t = Math.Clamp(t, 0, 1);
            byte L(byte v) => (byte)Math.Round(v + (255 - v) * t);
            return Color.FromRgb(L(c.R), L(c.G), L(c.B));
        }
    }
}
