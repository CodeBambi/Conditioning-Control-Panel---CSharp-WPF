// PORTED from ConditioningControlPanel/MainWindow/MainWindow.DashboardFold.cs (211 lines).
// One truth: AppSettings.DashboardBrowserCollapsed minus a run-only reveal; every surface (two row
// heights, the body, the chevron, its tooltip, the billboard) is re-derived from it by
// SettleBrowserFold through Core BrowserFoldRule. The ease is on the card frame and always lands on
// the settle, so an interrupted ease cannot leave the card looking folded while the setting says open.

using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Interaction motion, the short end of the 80-400ms band (WPF :45).</summary>
        private const int BrowserFoldMs = 180;

        private bool _browserFoldAnimating;
        private CancellationTokenSource? _browserFoldEase;

        /// <summary>A reveal in force: the app called the browser. Never persisted (WPF :54).</summary>
        private bool _browserRevealed;

        /// <summary>WPF BrowserFolded (:60): the saved preference minus a reveal in force.</summary>
        internal bool BrowserFolded => CoreSettings.Current.DashboardBrowserCollapsed && !_browserRevealed;

        /// <summary>WPF InitDashboardBrowserFold (:64), from the constructor: wires the chevron and
        /// restores the saved fold without animation.</summary>
        private void InitDashboardBrowserFold()
        {
            try
            {
                var dash = Named<Tabs.SettingsTabView>("SettingsTab");
                var btn = dash?.FindControl<Button>("BtnFoldBrowser");
                if (btn != null) btn.Click += BtnFoldBrowser_Click;
                ApplyBrowserFold(animate: false);
            }
            catch (Exception ex) { Log.Warning(ex, "InitDashboardBrowserFold failed"); }
        }

        /// <summary>WPF ResettleBrowserFold (:74): the dashboard coming on screen re-settles.</summary>
        internal void ResettleBrowserFold()
        {
            try { if (!_browserFoldAnimating) SettleBrowserFold(); }
            catch (Exception ex) { Log.Debug("ResettleBrowserFold: {E}", ex.Message); }
        }

        /// <summary>WPF RevealDashboardBrowser (:93): the app is calling the browser, so the card
        /// opens for this run whatever the preference says. Never writes the setting.</summary>
        internal void RevealDashboardBrowser(string reason)
        {
            try
            {
                if (!BrowserFolded) { _browserRevealed = true; return; }
                _browserRevealed = true;
                Log.Debug("Dashboard browser revealed by {Reason}", reason);
                ApplyBrowserFold(animate: Env.AllowTransitions);
            }
            catch (Exception ex) { Log.Debug("RevealDashboardBrowser({Reason}): {E}", reason, ex.Message); }
        }

        /// <summary>WPF BtnFoldBrowser_Click (:107): the chevron is the preference; it toggles the
        /// EFFECTIVE state and spends any reveal.</summary>
        internal void BtnFoldBrowser_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_browserFoldAnimating) return;
                bool collapsed = BrowserFoldRule.Toggle(BrowserFolded);
                _browserRevealed = false;
                CoreSettings.Current.DashboardBrowserCollapsed = collapsed;
                CoreSettings.Save();
                ApplyBrowserFold(animate: Env.AllowTransitions);
            }
            catch (Exception ex) { Log.Warning(ex, "BtnFoldBrowser_Click failed"); }
        }

        /// <summary>WPF ApplyBrowserFold (:131): settle, then ease the frame from the old height to
        /// the new one with the body collapsed for the whole ease (native web view airspace).</summary>
        private void ApplyBrowserFold(bool animate)
        {
            var dash = Named<Tabs.SettingsTabView>("SettingsTab");
            var frame = dash?.FindControl<Border>("BrowserCardFrame");
            var body = dash?.FindControl<Border>("BrowserFoldBody");
            if (frame == null || body == null) return;

            double from = frame.Bounds.Height;
            _browserFoldEase?.Cancel();
            _browserFoldEase = null;
            _browserFoldAnimating = false;
            SettleBrowserFold();
            if (!animate || from <= 0 || !frame.IsEffectivelyVisible) return;

            body.IsVisible = false;
            frame.UpdateLayout();
            double to = frame.Bounds.Height;
            if (to <= 0 || Math.Abs(to - from) < 1.0) { SettleBrowserFold(); return; }

            var cts = _browserFoldEase = new CancellationTokenSource();
            _browserFoldAnimating = true;
            var ease = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(BrowserFoldMs),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(HeightProperty, from) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(HeightProperty, to) } },
                },
            };
            ease.RunAsync(frame, cts.Token).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_browserFoldEase, cts)) return;   // replaced by a newer fold
                _browserFoldEase = null;
                _browserFoldAnimating = false;
                try { SettleBrowserFold(); }
                catch (Exception ex) { Log.Debug("Browser fold settle: {E}", ex.Message); }
            }));
        }

        /// <summary>WPF SettleBrowserFold (:177): re-reads the bool and writes every surface the
        /// fold owns. Idempotent; the only writer of these values.</summary>
        private void SettleBrowserFold()
        {
            var dash = Named<Tabs.SettingsTabView>("SettingsTab");
            var column = dash?.FindControl<Grid>("BrowserColumn");
            var frame = dash?.FindControl<Border>("BrowserCardFrame");
            var body = dash?.FindControl<Border>("BrowserFoldBody");
            if (column == null || frame == null || body == null || column.RowDefinitions.Count < 2) return;

            bool collapsed = BrowserFolded;
            frame.Height = double.NaN;
            column.RowDefinitions[0].Height = BrowserFoldRule.CardRowIsStar(collapsed) ? GridLength.Star : GridLength.Auto;
            column.RowDefinitions[1].Height = BrowserFoldRule.FoldRowIsStar(collapsed) ? GridLength.Star : new GridLength(0);
            body.IsVisible = BrowserFoldRule.BodyShown(collapsed);

            if (dash!.FindControl<TextBlock>("TxtFoldBrowser") is { } glyph) glyph.Text = BrowserFoldRule.Chevron(collapsed);
            if (dash.FindControl<Button>("BtnFoldBrowser") is { } btn)
            {
                // Bound, not assigned: the tooltip follows a language switch (P09).
                btn.Bind(ToolTip.TipProperty, new global::Avalonia.Data.Binding($"[{BrowserFoldRule.TooltipKey(collapsed)}]")
                    { Source = LocalizationManager.Instance, Mode = global::Avalonia.Data.BindingMode.OneWay });
            }

            ApplyBillboard(BrowserFoldRule.BillboardShown(collapsed));
        }
    }
}
