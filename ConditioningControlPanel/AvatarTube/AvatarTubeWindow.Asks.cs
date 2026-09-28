using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Services.Companion.Asks;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Ask card answers under the speech bubble. The same card shows on the chat page; a click on
    /// either surface resolves it for both (<see cref="CompanionAskService.CardChanged"/>).
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private string? _askCardId;
        private string? _askQuestion;
        private bool _askHooked;

        /// <summary>Says the card's question and puts its answers under it.</summary>
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

        /// <summary>Any other line taking the bubble drops the buttons; the card lives on in the chat page.</summary>
        private void SyncAskButtonsFor(string? text)
        {
            if (AskButtons == null || _askCardId == null) return;
            if (string.Equals(text, _askQuestion, StringComparison.Ordinal)) return;
            _askCardId = null;
            _askQuestion = null;
            AskButtons.Children.Clear();
            AskButtons.Visibility = Visibility.Collapsed;
        }

        private void RenderAskButtons(AskCard card)
        {
            if (AskButtons == null) return;
            AskButtons.Children.Clear();
            bool open = card.IsOpen(DateTime.UtcNow);
            foreach (var choice in card.Choices)
            {
                var (bg, border, fg) = AskTones.Colours(choice.Tone);
                bool chosen = card.ChosenId == choice.Id;
                var chip = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFrom(bg)!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFrom(border)!,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(11, 6, 11, 6),
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = open ? Cursors.Hand : Cursors.Arrow,
                    Opacity = open || chosen ? 1 : 0.4,
                    Child = new TextBlock
                    {
                        Text = chosen ? "✓ " + choice.Label : choice.Label,
                        Foreground = (Brush)new BrushConverter().ConvertFrom(fg)!,
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                    },
                    ToolTip = card.Kind == AskKind.Watch && choice.Action == AskAction.Watch ? card.Subject : null,
                };
                if (open)
                {
                    var cardId = card.Id;
                    var choiceId = choice.Id;
                    chip.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        CompanionAskService.Instance.Answer(cardId, choiceId);
                    };
                }
                AskButtons.Children.Add(chip);
            }
            AskButtons.Visibility = card.Choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
