// PORTED from WPF 7.1.5 Views/Tabs/SettingsTabView.xaml.cs (the drawer wiring, PaintFoldArrow,
// RefreshClickPreference) and SettingsTabView.Depth.cs (PaintDepthHome) (parity lane E3).
//
// The view's half of the 7.1.5 Home: it paints what it owns (the fold arrow, the gesture
// caption, Home's drop shadows) and forwards anything with state to the shell
// (MainShellWindow.DashboardFold.cs / .DashboardFavorites.cs). Numbers: Core HomeDashboardRules,
// BrowserFoldRule, DepthRules.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Depth;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private bool _homeWired;
        private bool _foldCollapsed = true;
        private DispatcherTimer? _foldBreath;
        private bool _foldBreathHigh;

        /// <summary>The right column's rows: 0 = the browser card, 1 = the row a folded card gives
        /// back (the board). RowDefinitions carry no x:Name on this head.</summary>
        internal RowDefinition BrowserCardRow => HomeRightColumn.RowDefinitions[0];
        internal RowDefinition BrowserFoldRow => HomeRightColumn.RowDefinitions[1];

        /// <summary>One-time wiring of the 7.1.5 Home pieces this view owns. Called from WireStubs.</summary>
        private void WireHome()
        {
            if (_homeWired) return;
            _homeWired = true;

            FavoritesDrawer.InvertClicksChanged += invert =>
            {
                var s = CoreSettings.Current;
                if (s.DashboardInvertClicks == invert) return;
                s.DashboardInvertClicks = invert;
                try { CoreSettings.Save(); } catch (Exception ex) { Serilog.Log.Debug("Click swap save: {E}", ex.Message); }
                RefreshClickPreference();
            };
            FavoritesDrawer.ApplySetting();

            // The breath is a DispatcherTimer, and a running timer roots this view (and the shell):
            // it lives only while the page is in a window (ShellMemoryTests).
            DetachedFromVisualTree += (_, _) => _foldBreath?.Stop();
            AttachedToVisualTree += (_, _) => ApplyFoldGlow();
            BtnFoldBrowser.TemplateApplied += (_, e) => { _foldPlate = e.NameScope.Find<Border>("FoldPlate"); ApplyFoldGlow(); };
            BtnFoldBrowser.PointerEntered += (_, _) => PaintFoldPlate(hover: true);
            BtnFoldBrowser.PointerExited += (_, _) => PaintFoldPlate(hover: false);

            PaintDepthHome(NavStripRules.Accent(NavSections.Home));
            RefreshClickPreference();
            PaintFoldArrow(CoreSettings.Current.DashboardBrowserCollapsed);
        }

        // ---- the gesture caption and the click swap ------------------------------------------

        /// <summary>WPF RefreshClickPreference + RefreshDashboardToggleHint: the caption on the logo
        /// face says what a right-click does (or that the clicks are swapped) until it has been
        /// used three times.</summary>
        internal void RefreshClickPreference()
        {
            var s = CoreSettings.Current;
            DashToggleHint.Text = Loc.Get(s.DashboardInvertClicks ? "dash_click_swapped" : "dash_toggle_hint");
            DashToggleHint.IsVisible = HomeDashboardRules.ShowToggleHint(s.DashboardToggleHintUses);
            FavoritesDrawer.RefreshChoiceText();
        }

        private void NoteToggleHintUse()
        {
            var s = CoreSettings.Current;
            if (!HomeDashboardRules.ShowToggleHint(s.DashboardToggleHintUses)) return;
            s.DashboardToggleHintUses++;
            try { CoreSettings.Save(); } catch (Exception ex) { Serilog.Log.Debug("Toggle hint save: {E}", ex.Message); }
            RefreshClickPreference();
        }

        // ---- the fold arrow --------------------------------------------------------------------

        /// <summary>
        /// The arrow pill: glyph, label, tooltip, Home's hue and the breathing glow while shut, all
        /// from <see cref="BrowserFoldRule"/>. Called on every settle (MainShellWindow.DashboardFold).
        /// </summary>
        internal void PaintFoldArrow(bool collapsed)
        {
            _foldCollapsed = collapsed;
            TxtFoldBrowser.Text = BrowserFoldRule.Chevron(collapsed);
            // A glyph the font lacks (Linux has no Segoe MDL2) falls back to a plain arrow.
            if (!global::ConditioningControlPanel.Avalonia.Controls.NavRail.SectionTabStrip.GlyphRenders(TxtFoldBrowser.Text))
            {
                TxtFoldBrowser.Text = collapsed ? "▾" : "▴";
                TxtFoldBrowser.FontFamily = FontFamily.Default;
            }
            TxtFoldBrowserLabel.Bind(TextBlock.TextProperty, LocBinding(BrowserFoldRule.LabelKey(collapsed)));
            ToolTip.SetTip(BtnFoldBrowser, Loc.Get(BrowserFoldRule.TooltipKey(collapsed)));
            global::Avalonia.Automation.AutomationProperties.SetName(BtnFoldBrowser, Loc.Get(BrowserFoldRule.LabelKey(collapsed)));
            PaintFoldPlate(hover: BtnFoldBrowser.IsPointerOver);
            ApplyFoldGlow();
        }

        private void PaintFoldPlate(bool hover)
        {
            uint hue = NavStripRules.Accent(NavSections.Home);
            BtnFoldBrowser.Background = Tint(hue, hover ? BrowserFoldRule.ArrowHoverFill : BrowserFoldRule.ArrowFill);
            BtnFoldBrowser.BorderBrush = Tint(hue, BrowserFoldRule.ArrowBorder);
        }

        /// <summary>The breathing glow while folded: a closed browser has to read as openable.
        /// None while open or at Motion Off, static when ambient loops are refused.</summary>
        private void ApplyFoldGlow()
        {
            _foldBreath?.Stop();
            var plate = FoldPlate();
            if (plate == null || !this.IsAttachedToVisualTree()) return;   // re-applied on attach
            plate.Transitions = null;
            if (!BrowserFoldRule.Glows(_foldCollapsed) || !AmbientFxCanvas.Env.AllowTransitions)
            {
                plate.BoxShadow = default;
                return;
            }
            if (!AmbientFxCanvas.Env.AllowAmbientLoops)
            {
                plate.BoxShadow = FoldGlow(BrowserFoldRule.GlowStatic);
                return;
            }
            plate.BoxShadow = FoldGlow(BrowserFoldRule.GlowLow);
            plate.Transitions = new Transitions
            {
                new BoxShadowsTransition
                {
                    Property = Border.BoxShadowProperty,
                    Duration = TimeSpan.FromMilliseconds(BrowserFoldRule.GlowBreathMs / 2),
                    Easing = new SineEaseInOut(),
                },
            };
            _foldBreath ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BrowserFoldRule.GlowBreathMs / 2) };
            _foldBreath.Tick -= FoldBreathe;
            _foldBreath.Tick += FoldBreathe;
            _foldBreath.Start();
        }

        private void FoldBreathe(object? sender, EventArgs e)
        {
            var plate = FoldPlate();
            if (plate == null || !_foldCollapsed || !IsEffectivelyVisible) { if (!_foldCollapsed) _foldBreath?.Stop(); return; }
            _foldBreathHigh = !_foldBreathHigh;
            plate.BoxShadow = FoldGlow(_foldBreathHigh ? BrowserFoldRule.GlowHigh : BrowserFoldRule.GlowLow);
        }

        private Border? _foldPlate;

        /// <summary>The arrow's template border (the glow lives on it), once the template is up.</summary>
        private Border? FoldPlate() => _foldPlate;

        private static BoxShadows FoldGlow(double opacity)
        {
            var c = DepthPaint.ToColor(NavStripRules.Accent(NavSections.Home));
            return new BoxShadows(new BoxShadow { Color = Color.FromArgb((byte)Math.Round(opacity * 255), c.R, c.G, c.B), Blur = 16 });
        }

        private static SolidColorBrush Tint(uint hue, double alpha)
        {
            var c = DepthPaint.ToColor(hue);
            return new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B));
        }

        private static Binding LocBinding(string key) =>
            new($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay };

        // ---- depth -----------------------------------------------------------------------------

        /// <summary>
        /// WPF 7.1.5 SettingsTabView.Depth.cs: Home's raised things (the account ledge) throw their
        /// shadow through <c>HomeDepthDropBand</c> / <c>HomeDepthDropDisc</c>; this repaints them
        /// pulled toward the section hue (Core DepthRules.ShadowColor). Static paint, no clock.
        /// </summary>
        internal void PaintDepthHome(uint hue)
        {
            Resources["HomeDepthDropBand"] = DropBand(hue);
            Resources["HomeDepthDropDisc"] = DropDisc(hue);
        }

        internal static LinearGradientBrush DropBand(uint hue)
        {
            var ink = DepthPaint.ToColor(DepthRules.ShadowColor(hue));
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(ink, 0),
                    new GradientStop(Color.FromArgb(0, ink.R, ink.G, ink.B), 1),
                },
            };
        }

        internal static RadialGradientBrush DropDisc(uint hue)
        {
            var ink = DepthPaint.ToColor(DepthRules.ShadowColor(hue));
            var mid = DepthPaint.ToColor(DepthRules.ShadowColor(hue, DepthRules.ShadowAlpha * 0.58));
            return new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(ink, 0),
                    new GradientStop(mid, 0.6),
                    new GradientStop(Color.FromArgb(0, ink.R, ink.G, ink.B), 1),
                },
            };
        }
    }
}
