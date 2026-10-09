// PORTED from ConditioningControlPanel/MainWindow/MainWindow.DashboardBillboard.cs (296 lines).
// One full-size promo slide in the row the folded browser gives back (Core DashboardBillboard roster,
// one slot). The 12 s clock runs only while the billboard is on screen, motion allows ambient loops,
// and it is neither paused, hovered nor keyboard-focused; manual navigation works at every motion level.

using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
        private const int BillboardFadeMs = 220;   // WPF :73

        private DispatcherTimer? _billboardTimer;
        private int _billboardIndex;
        private bool _billboardPointerOver;
        private bool _billboardWired;
        private bool _billboardPaused;
        private CancellationTokenSource? _billboardFade;

        /// <summary>The roster index on screen (test seam).</summary>
        internal int BillboardIndex => _billboardIndex;

        /// <summary>True while the rotation clock runs (test seam).</summary>
        internal bool BillboardClockRunning => _billboardTimer != null;

        private Tabs.SettingsTabView? Dash => Named<Tabs.SettingsTabView>("SettingsTab");

        /// <summary>WPF ApplyBillboard (:89): the fold's hook - the billboard exists only while the
        /// browser is shut.</summary>
        private void ApplyBillboard(bool show)
        {
            var host = Dash?.FindControl<Border>("DashBillboard");
            if (host == null) return;
            host.IsVisible = show;
            if (!show) { StopBillboardClock(); return; }
            EnsureBillboardWired();
            RestartBillboardClock();
        }

        /// <summary>WPF EnsureBillboardWired (:110): the slide, the dots, navigation and every
        /// reason the clock stops (pointer, keyboard focus, pause, visibility, motion gate).</summary>
        private void EnsureBillboardWired()
        {
            if (_billboardWired) return;
            var dash = Dash;
            var host = dash?.FindControl<Border>("DashBillboard");
            var dots = dash?.FindControl<StackPanel>("BillboardDots");
            if (dash == null || host == null || dots == null) return;
            _billboardWired = true;

            for (int i = 0; i < DashboardBillboard.Roster.Count; i++)
            {
                int index = i;
                var dot = new RadioButton { GroupName = "DashboardSlides", Classes = { "dot" } };
                var key = DashboardBillboard.CardAt(i).TitleKey;
                BindLoc(dot, ToolTip.TipProperty, key);
                BindLoc(dot, AutomationProperties.NameProperty, key);
                dot.Click += (_, _) => StepBillboard(index - _billboardIndex, automatic: false);
                dots.Children.Add(dot);
            }
            FillBillboard();
            dash.FindControl<Button>("BillboardPrevious")!.Click += (_, _) => StepBillboard(-1, automatic: false);
            dash.FindControl<Button>("BillboardNext")!.Click += (_, _) => StepBillboard(1, automatic: false);
            dash.FindControl<Button>("BillboardCard")!.Click += (_, _) => OpenBillboardCard();
            var pause = dash.FindControl<Button>("BillboardPause")!;
            pause.Click += (_, _) =>
            {
                _billboardPaused = !_billboardPaused;
                dash.FindControl<TextBlock>("TxtBillboardPause")!.Text = _billboardPaused ? "▶" : "Ⅱ";
                var key = _billboardPaused ? "btn_program_resume" : "btn_program_pause";
                BindLoc(pause, ToolTip.TipProperty, key);
                BindLoc(pause, AutomationProperties.NameProperty, key);
                RestartBillboardClock();
            };
            host.PointerEntered += (_, _) => { _billboardPointerOver = true; StopBillboardClock(); };
            host.PointerExited += (_, _) => { _billboardPointerOver = false; RestartBillboardClock(); };
            host.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsKeyboardFocusWithinProperty) RestartBillboardClock();
            };
            // Tab hidden / shown (P01): the shell hides the whole tab with IsVisible.
            dash.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) RestartBillboardClock(); };
            Env.MotionGateChanged += RestartBillboardClock;
            Closed += (_, _) => { Env.MotionGateChanged -= RestartBillboardClock; StopBillboardClock(); };
        }

        private static void BindLoc(AvaloniaObject target, AvaloniaProperty property, string key) =>
            target.Bind(property, new global::Avalonia.Data.Binding($"[{key}]")
                { Source = LocalizationManager.Instance, Mode = global::Avalonia.Data.BindingMode.OneWay });

        private bool BillboardMayAdvance(Border host) =>
            host.IsEffectivelyVisible && !_billboardPaused && !host.IsKeyboardFocusWithin && Env.AllowAmbientLoops
            && DashboardBillboard.ShouldAdvance(_billboardPointerOver, onScreen: true);

        /// <summary>WPF RestartBillboardClock (:172).</summary>
        private void RestartBillboardClock()
        {
            StopBillboardClock();
            var host = Dash?.FindControl<Border>("DashBillboard");
            if (host == null || !BillboardMayAdvance(host)) return;
            _billboardTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(DashboardBillboard.RotateSeconds),
            };
            _billboardTimer.Tick += (_, _) => BillboardTick();
            _billboardTimer.Start();
        }

        private void StopBillboardClock()
        {
            _billboardTimer?.Stop();
            _billboardTimer = null;
        }

        /// <summary>One clock tick (the timer's handler; tests step it directly).</summary>
        internal void BillboardTick() => StepBillboard(1, automatic: true);

        /// <summary>WPF StepBillboardRack (:195): one slide step; an automatic step re-checks every
        /// gate first, a manual one restarts the clock.</summary>
        private void StepBillboard(int direction, bool automatic)
        {
            try
            {
                var host = Dash?.FindControl<Border>("DashBillboard");
                if (host?.IsEffectivelyVisible != true) return;
                if (automatic && !BillboardMayAdvance(host)) { StopBillboardClock(); return; }
                _billboardIndex = DashboardBillboard.SlideIndex(_billboardIndex, direction, DashboardBillboard.Roster.Count);
                FillBillboard();
                FadeBillboard();
                if (!automatic) RestartBillboardClock();
            }
            catch (Exception ex) { Log.Warning(ex, "Dashboard billboard: step failed"); }
        }

        /// <summary>WPF FillBillboardSlot + UpdateBillboardPosition (:221/:228).</summary>
        private void FillBillboard()
        {
            var dash = Dash;
            if (dash == null) return;
            var card = DashboardBillboard.CardAt(_billboardIndex);
            BindLoc(dash.FindControl<TextBlock>("BillboardEyebrow")!, TextBlock.TextProperty, card.EyebrowKey);
            BindLoc(dash.FindControl<TextBlock>("BillboardTitle")!, TextBlock.TextProperty, card.TitleKey);
            BindLoc(dash.FindControl<TextBlock>("BillboardLine")!, TextBlock.TextProperty, card.LineKey);
            var button = dash.FindControl<Button>("BillboardCard")!;
            var tip = Loc.Get(card.TitleKey) + "  ·  " + Loc.Get(card.LineKey);
            ToolTip.SetTip(button, tip);
            AutomationProperties.SetName(button, tip);

            bool plate = card.Art == BillboardArt.Plate;
            var art = LoadBillboardArt(card);
            dash.FindControl<Image>("BillboardCover")!.IsVisible = !plate;
            dash.FindControl<Grid>("BillboardPlate")!.IsVisible = plate;
            dash.FindControl<Image>("BillboardCover")!.Source = art;
            dash.FindControl<Image>("BillboardPlateGround")!.Source = art;
            dash.FindControl<Image>("BillboardPlateMark")!.Source = art;

            var dots = dash.FindControl<StackPanel>("BillboardDots")!;
            for (int i = 0; i < dots.Children.Count; i++)
                ((RadioButton)dots.Children[i]).IsChecked = i == _billboardIndex;
        }

        /// <summary>Missing art leaves the words readable over the shade, never a throw (WPF :241).</summary>
        private static Bitmap? LoadBillboardArt(BillboardCard card)
        {
            try { return new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + card.Poster))); }
            catch (Exception ex)
            {
                Log.Debug("Billboard art {Poster} did not load: {E}", card.Poster, ex.Message);
                return null;
            }
        }

        /// <summary>WPF FadeBillboardSlot (:259): only when the motion gate allows transitions.</summary>
        private void FadeBillboard()
        {
            var button = Dash?.FindControl<Button>("BillboardCard");
            if (button == null || !Env.AllowTransitions) return;
            _billboardFade?.Cancel();
            _billboardFade = new CancellationTokenSource();
            _ = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(BillboardFadeMs),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1d) } },
                },
            }.RunAsync(button, _billboardFade.Token);
        }

        /// <summary>WPF BillboardCard_Click (:278): a Link leaves through the one opener, a Tab is
        /// an in-app navigation.</summary>
        private void OpenBillboardCard()
        {
            try
            {
                var card = DashboardBillboard.CardAt(_billboardIndex);
                if (card.Kind == BillboardTargetKind.Link) BillboardOpenUrl(card.Target);
                else ShowTab(card.Target);
            }
            catch (Exception ex) { Log.Warning(ex, "Dashboard billboard: card click failed"); }
        }

        /// <summary>The link opener (seam: tests never launch a browser).</summary>
        internal static Action<string> BillboardOpenUrl = url => Platform.ExternalOpener.Open(url);
    }
}
