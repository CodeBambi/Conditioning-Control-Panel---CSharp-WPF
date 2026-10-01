using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Asks;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>
    /// The chat-send extras the WPF tube runs around an AI reply: the thinking bubble
    /// (Speech.cs StartThinkingAnimation), the double bounce (Speech.cs PlayDoubleBounce) and the ask
    /// card answers under the bubble (AvatarTubeWindow.Asks.cs), all against the Core pieces.
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private bool _isWaitingForAi;   // blocks nothing here but IsCompanionBusy, as WPF's bark gate
        private DispatcherTimer? _thinkingTimer;
        private string _thinkingPhraseBase = string.Empty;
        private int _thinkingTickCount;
        private int _thinkingGeneration;
        private DispatcherTimer? _bounceTimer;
        private string? _askCardId;
        private string? _askQuestion;
        private bool _askHooked;

        /// <summary>WPF StartThinkingAnimation: phrase + 0-3 dots every 500ms until the reply lands.</summary>
        internal void StartThinkingAnimation()
        {
            StopThinkingAnimation();
            _isWaitingForAi = true;
            _thinkingPhraseBase = ThinkingPhrases.Pick(Random.Shared);
            _thinkingTickCount = 0;
            var generation = ++_thinkingGeneration;

            // The first frame as WPF ShowGiggle(source: AI) draws it: unbadged, silent, no chat-log
            // entry and no auto-dismiss - the reply pre-empts it.
            _speechTimer?.Stop();
            if (!Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted)
            {
                if (_isShowingChatHistory)
                {
                    _isShowingChatHistory = false;
                    _chatHistoryView.IsVisible = false;
                    _speechScroller.IsVisible = true;
                }
                SyncAskButtonsFor(_thinkingPhraseBase);
                _aiBadge.IsVisible = false;
                _policyBadge.IsVisible = false;
                _txtSpeech.Text = _thinkingPhraseBase;
                ApplySpeechBubblePlacement();
                _speechBubble.IsVisible = true;
                _isGiggling = true;
            }

            _thinkingTimer = new DispatcherTimer { Interval = ThinkingPhrases.TickInterval };
            _thinkingTimer.Tick += (s, _) =>
            {
                if (generation != _thinkingGeneration) { (s as DispatcherTimer)?.Stop(); return; }
                if (++_thinkingTickCount > 3)
                {
                    _thinkingPhraseBase = ThinkingPhrases.Pick(Random.Shared);
                    _thinkingTickCount = 0;
                }
                _txtSpeech.Text = ThinkingPhrases.Frame(_thinkingPhraseBase, _thinkingTickCount);
            };
            _thinkingTimer.Start();
        }

        /// <summary>Cancels any in-flight thinking animation. Safe to call repeatedly.</summary>
        internal void StopThinkingAnimation()
        {
            _thinkingGeneration++;
            _thinkingTimer?.Stop();
            _thinkingTimer = null;
            _isWaitingForAi = false;
        }

        /// <summary>
        /// WPF PlayDoubleBounce: Y 0 → -15 (80ms) → 0 (160) → -8 (220) → 0 (280), linear, then released
        /// so nothing holds the transform. Stepped by hand on AvatarBounceHost (see the axaml note).
        /// </summary>
        internal void PlayDoubleBounce()
        {
            var host = this.FindControl<Grid>("AvatarBounceHost");
            if (host == null) return;
            var translate = new TranslateTransform();
            host.RenderTransform = translate;
            _bounceTimer?.Stop();
            var clock = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                var ms = clock.Elapsed.TotalMilliseconds;
                translate.Y = DoubleBounceY(ms);
                if (ms >= 280) { timer.Stop(); host.RenderTransform = null; }
            };
            _bounceTimer = timer;
            timer.Start();
        }

        internal static double DoubleBounceY(double ms)
        {
            static double Lerp(double a, double b, double t) => a + (b - a) * t;
            return ms switch
            {
                < 0 => 0,
                < 80 => Lerp(0, -15, ms / 80),
                < 160 => Lerp(-15, 0, (ms - 80) / 80),
                < 220 => Lerp(0, -8, (ms - 160) / 60),
                < 280 => Lerp(-8, 0, (ms - 220) / 60),
                _ => 0,
            };
        }

        /// <summary>WPF ShowAskCard (Asks.cs): says the card's question and puts its answers under it.</summary>
        internal void ShowAskCard(AskCard card)
        {
            if (!_askHooked)
            {
                _askHooked = true;
                CompanionAskService.Instance.CardChanged += c => RunOnAvatar(() => { if (c.Id == _askCardId) RenderAskButtons(c); });
            }
            _askCardId = card.Id;
            _askQuestion = card.Question;
            RenderAskButtons(card);
            GigglePriority(card.Question, playSound: true, aiGenerated: false);
        }

        /// <summary>Any other line taking the bubble drops the buttons; the card lives on in the chat log.</summary>
        private void SyncAskButtonsFor(string? text)
        {
            if (_askCardId == null || string.Equals(text, _askQuestion, StringComparison.Ordinal)) return;
            _askCardId = null;
            _askQuestion = null;
            var buttons = this.FindControl<WrapPanel>("AskButtons")!;
            buttons.Children.Clear();
            buttons.IsVisible = false;
        }

        private void RenderAskButtons(AskCard card)
        {
            var buttons = this.FindControl<WrapPanel>("AskButtons")!;
            buttons.Children.Clear();
            bool open = card.IsOpen(DateTime.UtcNow);
            foreach (var choice in card.Choices)
            {
                var (bg, border, fg) = AskTones.Colours(choice.Tone);
                bool chosen = card.ChosenId == choice.Id;
                var chip = new Border
                {
                    Name = "AskChoice_" + choice.Id,
                    Background = Brush.Parse(bg),
                    BorderBrush = Brush.Parse(border),
                    BorderThickness = new global::Avalonia.Thickness(1),
                    CornerRadius = new global::Avalonia.CornerRadius(8),
                    Padding = new global::Avalonia.Thickness(11, 6, 11, 6),
                    Margin = new global::Avalonia.Thickness(0, 0, 6, 6),
                    Cursor = new Cursor(open ? StandardCursorType.Hand : StandardCursorType.Arrow),
                    Opacity = open || chosen ? 1 : 0.4,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = chosen ? "✓ " + choice.Label : choice.Label,
                        Foreground = Brush.Parse(fg),
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold,
                    },
                };
                if (card.Kind == AskKind.Watch && choice.Action == AskAction.Watch) ToolTip.SetTip(chip, card.Subject);
                if (open)
                {
                    var (cardId, choiceId) = (card.Id, choice.Id);
                    chip.PointerReleased += (_, e) =>
                    {
                        if (e.InitialPressMouseButton != MouseButton.Left) return;
                        e.Handled = true;
                        CompanionAskService.Instance.Answer(cardId, choiceId);
                    };
                }
                buttons.Children.Add(chip);
            }
            buttons.IsVisible = card.Choices.Count > 0;
        }
    }
}
