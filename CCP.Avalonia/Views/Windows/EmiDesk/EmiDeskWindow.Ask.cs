using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The offer: WPF EmiDeskWindow.Bubble.cs ShowAsk :586, BuildChips :643, AnswerAsk :760,
    /// CancelAsk :795, ReleaseParked :848, the kill moments :77. A question goes up with two chips
    /// and WAITS: no timer, no give-up. The chips, Esc, a tear-down and a full-screen feature are
    /// the ways out. Chip 1 is YES. A line that lands while she waits parks (one slot, newest wins).
    /// The effect is re-checked for feasibility on the click, so a door that locked or a tour that
    /// started while the chips were up does nothing instead of misfiring.
    /// </summary>
    public partial class EmiDeskWindow
    {
        private static readonly IBrush ChipHoverFill = new ImmutableSolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4));

        private const double ChipFontAtDefaultWidth = 9.0;
        private const int AskDot1Ms = 420;        // the locked . / .. / ... cadence, same as Say
        private const int AskDot2Ms = 420;
        private const int AskDot3Ms = 520;
        private const int IgnoreFaceMs = 1400;    // MOMENTS: askIgnored is a 1400 ms hold
        private const int IgnoreThinkMs = 700;

        /// <summary>WPF AskKillMoments: a full-screen feature takes the question down.</summary>
        private static readonly HashSet<string> AskKillMoments = new(StringComparer.Ordinal)
        {
            "videoRunning", "lockdownArmed", "lockdownCountdown", "intakeOpened", "intakeRunning",
            "panicPressed"
        };

        private AskDraw? _ask;
        private LineDraw? _parked;          // ONE slot. A second line while an ask waits replaces it.
        private DispatcherTimer? _askTimer;
        private StackPanel? _chips;
        private int _askStage;
        private bool _askHooked;

        /// <summary>True while an offer is on screen and still waiting for an answer.</summary>
        public bool AskLive => _ask != null;

        /// <summary>The chip labels while they are up (test seam).</summary>
        internal IReadOnlyList<string> ChipLabels
        {
            get
            {
                var list = new List<string>();
                if (_chips is not { IsVisible: true }) return list;
                foreach (var c in _chips.Children)
                    if (c is Button { Content: Border { Child: TextBlock t } }) list.Add(t.Text ?? "");
                return list;
            }
        }

        /// <summary>Put a question up and WAIT (WPF ShowAsk).</summary>
        public void ShowAsk(AskDraw? ask)
        {
            if (PresentationActive) return;
            if (ask == null) return;
            try
            {
                HookAsk();
                EnsureBubble();
                CancelHold();
                CancelChain();
                StopIdleBeats();
                EndAskTimer();

                _ask = ask;
                EmiLineEngine.Instance.Ack(ask.Id);
                EmiDeskService.Instance.NoteEmiSpoke();

                _askStage = 0;
                DrawFace(EmiChains.RestFace);
                ShowBubble(".");
                _askTimer = NewAskTimer(AskDot1Ms, AskCadenceStep);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ShowAsk({Id}) failed", ask.Id);
                _ask = null;
            }
        }

        /// <summary>Test seam: skip the dots and put the question and chips up now.</summary>
        internal void FinishAskCadence()
        {
            if (_ask == null) return;
            EndAskTimer();
            _askStage = 2;
            AskCadenceStep();
        }

        private void AskCadenceStep()
        {
            var ask = _ask;
            if (ask == null) return;
            _askStage++;
            switch (_askStage)
            {
                case 1:
                    ShowBubble("..");
                    _askTimer = NewAskTimer(AskDot2Ms, AskCadenceStep);
                    break;
                case 2:
                    ShowBubble("...");
                    _askTimer = NewAskTimer(AskDot3Ms, AskCadenceStep);
                    break;
                default:
                    DrawFace(string.IsNullOrEmpty(ask.Face) ? "^_^" : ask.Face);
                    ShowBubble(ask.Question);
                    BuildChips(ask);
                    Log.Information("[EmiDesk] offer {Id} is up, waiting", ask.Id);
                    break;
            }
        }

        private void BuildChips(AskDraw ask)
        {
            if (_chips == null)
            {
                _chips = new StackPanel { Orientation = Orientation.Horizontal, IsVisible = false };
                _bubbleCanvas.Children.Add(_chips);
            }
            _chips.Children.Clear();

            IReadOnlyList<string> labels = ask.Chips != null && ask.Chips.Count >= 2
                ? ask.Chips
                : new[] { "yes", "no" };

            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                var b = MakeChip(labels[i]);
                b.Click += (_, e) => { e.Handled = true; AnswerAsk(idx); };
                _chips.Children.Add(b);
            }
            _chips.IsVisible = true;

            // The bubble layer is not hit-testable (it would eat her drag), so it is opened for
            // exactly as long as there are chips to click. A Canvas with no Background does not
            // hit-test itself, and the bubble and tail stay opted out.
            _bubbleCanvas.IsHitTestVisible = true;
            LayoutBubble();
            LayoutChips();
        }

        private Button MakeChip(string? label)
        {
            double fs = Math.Max(ChipFontAtDefaultWidth,
                Math.Round(ChipFontAtDefaultWidth * BodyWidth / BubbleFontRefWidth));

            var shell = new Border
            {
                Background = BubbleFill,
                BorderBrush = BubbleInk,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(label) ? "ok" : label!.Trim(),
                    FontFamily = PixelFont,
                    FontSize = fs,
                    Foreground = BubbleInk,
                    Margin = new Thickness(7, 4, 7, 4)
                }
            };

            var b = new Button
            {
                Content = shell,
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Focusable = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0
            };
            b.PointerEntered += (_, _) => shell.Background = ChipHoverFill;
            b.PointerExited += (_, _) => shell.Background = BubbleFill;
            return b;
        }

        /// <summary>Hang the chip row under the bubble it belongs to, inside her own window.</summary>
        private void LayoutChips()
        {
            try
            {
                if (_chips is not { IsVisible: true } || _bubble == null) return;
                _chips.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = _chips.DesiredSize.Width;
                double left = Canvas.GetLeft(_bubble);
                double top = Canvas.GetTop(_bubble);
                if (double.IsNaN(left)) left = BubbleEdgeGap;
                if (double.IsNaN(top)) top = 2;
                double bh = Math.Max(_bubble.DesiredSize.Height, _bubble.Bounds.Height);

                double winW = double.IsNaN(Width) ? Bounds.Width : Width;
                double x = left;
                if (winW > 0 && x + w > winW - BubbleEdgeGap) x = winW - BubbleEdgeGap - w;
                if (x < BubbleEdgeGap) x = BubbleEdgeGap;

                Canvas.SetLeft(_chips, Math.Round(x));
                Canvas.SetTop(_chips, Math.Round(top + bh + 12));
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chip layout failed"); }
        }

        private void HideChips()
        {
            if (_chips != null)
            {
                _chips.Children.Clear();
                _chips.IsVisible = false;
            }
            // Shut the layer the moment the last chip is gone: open, it sits over her whole window.
            _bubbleCanvas.IsHitTestVisible = false;
        }

        /// <summary>A chip was clicked. The only answer that counts as an answer. Index 0 is YES.</summary>
        internal void AnswerAsk(int index)
        {
            var ask = _ask;
            if (ask == null) return;
            try
            {
                bool yes = index == 0;
                var reply = yes ? ask.Yes : ask.No;
                string? effect = yes ? ask.Effect : ask.EffectNo;

                Log.Information("[EmiDesk] offer {Id} answered {Answer} -> {Effect}", ask.Id,
                    yes ? "yes" : "no", string.IsNullOrEmpty(effect) ? "none" : effect);

                _ask = null;
                EndAskTimer();
                HideChips();
                HideBubble();

                EmiLineEngine.Instance.NoteAskAnswered();
                EmiDeskService.Instance.Fire("askAnswered", new { answer = yes ? "yes" : "no" });

                // The reply first, so she is already talking when the effect lands, then the effect.
                // fromAsk: the effect's own moment must not speak a SECOND line on top of the reply.
                if (reply != null) SpeakLine(reply);

                // Access is re-checked on the click: the world may have moved while she waited.
                if (!string.IsNullOrEmpty(effect))
                {
                    if (EmiOffers.EffectFeasible(effect)) EmiOffers.Run(effect, fromAsk: true);
                    else Log.Information("[EmiDesk] offer {Id}: {Effect} is no longer possible, skipped", ask.Id, effect);
                }

                ReleaseParked();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] answering offer {Id} failed", ask.Id);
                _ask = null;
                EndAskTimer();
                HideChips();
            }
        }

        /// <summary>
        /// The question went away without an answer. Not a sulk: a <c>-_-</c>, a beat of thinking,
        /// and then she drops it. Three of these and she stops asking this launch.
        /// </summary>
        public void CancelAsk(string why) => EndAskUnanswered(why, visual: true);

        private void EndAskUnanswered(string why, bool visual)
        {
            var ask = _ask;
            if (ask == null) return;
            try
            {
                Log.Information("[EmiDesk] offer {Id} ignored ({Why})", ask.Id, why);
                _ask = null;
                EndAskTimer();
                HideChips();
                HideBubble();

                EmiLineEngine.Instance.NoteAskIgnored();
                EmiDeskService.Instance.Fire("askIgnored", new { });
                if (!visual) return;

                CancelChain();
                DrawFace("-_-");
                _askTimer = NewAskTimer(IgnoreFaceMs, () =>
                {
                    ShowBubble("...");
                    _askTimer = NewAskTimer(IgnoreThinkMs, () =>
                    {
                        EndAskTimer();
                        HideBubble();
                        DrawFace(EmiChains.RestFace);
                        RestartIdleBeats();
                        ReleaseParked();
                    });
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] cancelling offer {Id} failed", ask.Id);
                _ask = null;
                EndAskTimer();
                HideChips();
            }
        }

        /// <summary>Let the one line that arrived behind the question have its turn.</summary>
        private void ReleaseParked()
        {
            var line = _parked;
            _parked = null;
            if (line == null) return;
            // A short beat so it does not step on the reply's own first frame.
            _askTimer = NewAskTimer(900, () => { _askTimer = null; SpeakLine(line); });
        }

        private void HookAsk()
        {
            if (_askHooked) return;
            _askHooked = true;
            KeyDown += OnAskKeyDown;
            EmiDeskService.Instance.MomentFired += OnMomentForAsk;
        }

        /// <summary>Esc on her own window answers nothing and drops the question. Her window only:
        /// the app's panic key is a global hook and is not touched here.</summary>
        private void OnAskKeyDown(object? sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key != Key.Escape || !AskLive) return;
                e.Handled = true;
                CancelAsk("escape");
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] Esc on the bubble failed"); }
        }

        private void OnMomentForAsk(object? sender, EmiMoment m)
        {
            try
            {
                if (!AskLive || m == null) return;
                if (!AskKillMoments.Contains(m.Id)) return;
                if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => CancelAsk(m.Id)); return; }
                CancelAsk(m.Id);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ask kill-moment handler failed"); }
        }

        private void EndAskTimer()
        {
            var t = _askTimer;
            _askTimer = null;
            if (t == null) return;
            try { t.Stop(); } catch { /* already dead */ }
        }

        private static DispatcherTimer NewAskTimer(int ms, Action step)
        {
            var t = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms))
            };
            void Tick(object? s, EventArgs e)
            {
                try
                {
                    t.Stop();
                    t.Tick -= Tick;
                    step();
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ask timer step failed"); }
            }
            t.Tick += Tick;
            t.Start();
            return t;
        }

        /// <summary>Take any waiting question down with her. No face: she is already fading out.</summary>
        private void TearDownAsk()
        {
            try
            {
                EndAskUnanswered("teardown", visual: false);
                _ask = null;
                _parked = null;
                EndAskTimer();
                HideChips();
                if (_askHooked)
                {
                    _askHooked = false;
                    KeyDown -= OnAskKeyDown;
                    EmiDeskService.Instance.MomentFired -= OnMomentForAsk;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ask tear-down failed"); }
        }
    }
}
