using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The browser card's fold arrow pill, PORTED from WPF SettingsTabView.PaintFoldArrow
    /// (210e0e262). MainShellWindow.DashboardFold.cs owns the setting and the rows; this paints
    /// the button for the state it is told: Home's hue, glyph, label, tooltip, and while FOLDED a
    /// breathing glow (a loop only under ambient loops, static under Reduced, none at Motion Off).
    /// </summary>
    public partial class SettingsTabView
    {
        /// <summary>Home's hue: WPF NavStripRules.Accent(NavSections.Home) = Lilac #B79CFF
        /// (SectionTabStrip.xaml.cs:114).</summary>
        internal static readonly Color HomeHue = Color.FromRgb(0xB7, 0x9C, 0xFF);

        private DispatcherTimer? _foldBreath;
        private DropShadowEffect? _foldGlow;
        private long _foldBreathStart;
        private bool? _foldPainted;

        internal void PaintFoldArrow(bool collapsed)
        {
            _foldPainted = collapsed;
            var btn = BtnFoldBrowser;
            btn.Background = Tint(HomeHue, BrowserFoldRule.ArrowFill);
            btn.Tag = Tint(HomeHue, BrowserFoldRule.ArrowHoverFill);
            btn.BorderBrush = Tint(HomeHue, BrowserFoldRule.ArrowBorder);
            // Core's chevrons are Segoe MDL2 code points (no such font on Linux): plain arrows.
            TxtFoldBrowser.Text = collapsed ? "▾" : "▴";
            // Bound, not assigned, so a language switch re-reads them (P09).
            var label = BrowserFoldRule.LabelKey(collapsed);
            TxtFoldBrowserLabel.Bind(TextBlock.TextProperty, LocBinding(label));
            btn.Bind(global::Avalonia.Automation.AutomationProperties.NameProperty, LocBinding(label));
            btn.Bind(ToolTip.TipProperty, LocBinding(BrowserFoldRule.TooltipKey(collapsed)));

            _foldBreath?.Stop();
            if (!BrowserFoldRule.Glows(collapsed) || !AmbientFxCanvas.Env.AllowTransitions)
            {
                btn.ClearValue(Visual.EffectProperty);
                _foldGlow = null;
                return;
            }
            _foldGlow ??= new DropShadowEffect { BlurRadius = 16, OffsetX = 0, OffsetY = 0 };
            _foldGlow.Color = HomeHue;
            btn.Effect = _foldGlow;
            if (!AmbientFxCanvas.Env.AllowAmbientLoops || !IsVisible || VisualRoot == null) { _foldGlow.Opacity = BrowserFoldRule.GlowStatic; return; }
            _foldGlow.Opacity = BrowserFoldRule.GlowLow;
            _foldBreathStart = FxAdorner.Time.GetTimestamp();
            // A sine breath, low -> high -> low over GlowBreathMs, at 24 fps like WPF's SetDesiredFrameRate.
            _foldBreath ??= new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 24), DispatcherPriority.Background, (_, _) => StepFoldBreath());
            _foldBreath.Start();
        }

        internal bool FoldArrowBreathing => _foldBreath?.IsEnabled == true;

        internal void StepFoldBreath()
        {
            if (_foldGlow == null) return;
            double half = BrowserFoldRule.GlowBreathMs / 2000.0;
            double t = FxAdorner.Time.GetElapsedTime(_foldBreathStart).TotalSeconds % (2 * half);
            double u = t < half ? t / half : 2 - (t / half);
            _foldGlow.Opacity = BrowserFoldRule.GlowLow + ((BrowserFoldRule.GlowHigh - BrowserFoldRule.GlowLow) * (1 - Math.Cos(Math.PI * u)) / 2);
        }

        /// <summary>Visibility hook: the breath runs only while Home is on screen (P01).</summary>
        private void SyncFoldArrowBreath()
        {
            if (!IsVisible) _foldBreath?.Stop();
            else if (_foldPainted is { } c) PaintFoldArrow(c);
        }

        private static global::Avalonia.Data.Binding LocBinding(string key) =>
            new($"[{key}]") { Source = LocalizationManager.Instance, Mode = global::Avalonia.Data.BindingMode.OneWay };

        private static SolidColorBrush Tint(Color hue, double alpha) =>
            new(Color.FromArgb((byte)Math.Round(255 * alpha), hue.R, hue.G, hue.B));
    }
}
