using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The per-question breakdown of one finished quiz run: every option the taker saw, which one
    /// they picked, what each was worth, and the archetype card underneath.
    ///
    /// PORTED from ConditioningControlPanel/Windows/QuizReportWindow.xaml.cs. Deviations:
    ///  - <c>QuizHistoryEntry</c> / <c>QuizAnswerRecord</c> / <c>QuizCategory</c> live in the WPF
    ///    head's Services/Quiz/QuizService.cs, not in CCP.Core, and neither may be touched by this
    ///    port - so they are copied verbatim below (the TextEditorDialog/TextItem precedent).
    ///  - <c>FontWeights.Bold</c> -> <c>FontWeight.Bold</c>; the alignment enums come from
    ///    Avalonia.Layout.
    ///  - The two-colour <c>LinearGradientBrush</c> constructor does not exist in Avalonia, so the
    ///    profile card's rim is built from two GradientStops at the same 0-degree angle.
    ///  - KeyDown/Click are wired in the constructor rather than in markup.
    /// "YOUR PROFILE" stays hardcoded English exactly as in WPF - there is no loc key for it.
    /// </summary>
    public partial class QuizReportWindow : Window
    {
        /// <summary>Render/design constructor: one two-question run with a profile, so
        /// --render-view draws the question rows AND the archetype card. Internal, so no
        /// production caller can ship the sample.</summary>
        internal QuizReportWindow() : this(SampleEntry()) { }

        public QuizReportWindow(QuizHistoryEntry entry)
        {
            AvaloniaXamlLoader.Load(this);

            var categoryDisplay = !string.IsNullOrEmpty(entry.CategoryName) ? entry.CategoryName : entry.Category.ToString();
            this.FindControl<TextBlock>("TxtSubtitle")!.Text = $"{categoryDisplay}  ·  {entry.TakenAt:MMM d, yyyy  h:mm tt}";
            var pct = entry.MaxScore > 0 ? (int)Math.Round((double)entry.TotalScore / entry.MaxScore * 100) : 0;
            this.FindControl<TextBlock>("TxtScore")!.Text = $"{entry.TotalScore} / {entry.MaxScore}  ({pct}%)";

            BuildQuestions(entry);
            BuildProfileCard(entry.ProfileText);

            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private void BuildQuestions(QuizHistoryEntry entry)
        {
            var letters = new[] { "A", "B", "C", "D" };
            var contentPanel = this.FindControl<StackPanel>("ContentPanel")!;

            foreach (var answer in entry.Answers)
            {
                // Question header
                var qHeader = new TextBlock
                {
                    Text = $"Q{answer.QuestionNumber}. {answer.QuestionText}",
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.Bold,
                    FontSize = 15,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 14, 0, 6)
                };
                contentPanel.Children.Add(qHeader);

                // Answer options
                for (int i = 0; i < 4; i++)
                {
                    if (i >= answer.AllAnswers.Length) break;

                    bool isChosen = i == answer.ChosenIndex;

                    var row = new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12, 7, 12, 7),
                        Margin = new Thickness(0, 2, 0, 2),
                        Background = isChosen
                            ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x69, 0xB4))
                            : new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
                        BorderBrush = isChosen
                            ? new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x69, 0xB4))
                            : new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                        BorderThickness = new Thickness(1)
                    };

                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var letterTxt = new TextBlock
                    {
                        Text = letters[i],
                        FontWeight = FontWeight.Bold,
                        FontSize = 13,
                        Foreground = isChosen
                            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4))
                            : new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(letterTxt, 0);
                    grid.Children.Add(letterTxt);

                    var answerTxt = new TextBlock
                    {
                        Text = answer.AllAnswers[i],
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = isChosen
                            ? new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xF0))
                            : new SolidColorBrush(Color.FromArgb(0x70, 0xC0, 0xC0, 0xD0)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(answerTxt, 1);
                    grid.Children.Add(answerTxt);

                    var pointsTxt = new TextBlock
                    {
                        Text = isChosen ? $"+{answer.PointsEarned}" : $"{answer.AllPoints[i]}",
                        FontSize = 12,
                        Foreground = isChosen
                            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4))
                            : new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x90)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    Grid.SetColumn(pointsTxt, 2);
                    grid.Children.Add(pointsTxt);

                    row.Child = grid;
                    contentPanel.Children.Add(row);
                }
            }
        }

        private void BuildProfileCard(string profileText)
        {
            if (string.IsNullOrWhiteSpace(profileText)) return;

            var contentPanel = this.FindControl<StackPanel>("ContentPanel")!;

            var headerTxt = new TextBlock
            {
                Text = "YOUR PROFILE",
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90)),
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 8)
            };
            contentPanel.Children.Add(headerTxt);

            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20, 16, 20, 16),
                Margin = new Thickness(0, 0, 0, 8),
                Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1)
            };
            // WPF's LinearGradientBrush(start, end, angle) ctor has no Avalonia twin; angle 0 is a
            // left-to-right sweep, which is what these two relative points describe.
            card.BorderBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4), 0),
                    new GradientStop(Color.FromArgb(0x40, 0x9B, 0x59, 0xB6), 1),
                }
            };

            var profileTxt = new TextBlock
            {
                Text = profileText,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xD8)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 24,
                TextAlignment = TextAlignment.Center
            };

            card.Child = profileTxt;
            contentPanel.Children.Add(card);
        }

        /// <summary>Placeholder run for the render constructor - not production data.</summary>
        private static QuizHistoryEntry SampleEntry() => new()
        {
            TakenAt = new DateTime(2026, 3, 14, 21, 5, 0),
            Category = QuizCategory.Obedience,
            CategoryName = "Obedience",
            TotalScore = 7,
            MaxScore = 10,
            ProfileText = "You answer before you weigh it. The pause you used to take is shorter every week, "
                        + "and you have stopped noticing that it is gone.",
            Answers =
            {
                new QuizAnswerRecord
                {
                    QuestionNumber = 1,
                    QuestionText = "When you are told to wait, how long before you check the clock?",
                    AllAnswers = new[] { "Immediately", "After a minute", "I do not check", "I never waited" },
                    AllPoints = new[] { 1, 2, 4, 0 },
                    ChosenIndex = 2,
                    PointsEarned = 4,
                },
                new QuizAnswerRecord
                {
                    QuestionNumber = 2,
                    QuestionText = "An instruction arrives that you do not understand. You...",
                    AllAnswers = new[] { "Ask why", "Follow it anyway", "Stall", "Refuse" },
                    AllPoints = new[] { 1, 3, 2, 0 },
                    ChosenIndex = 1,
                    PointsEarned = 3,
                },
            },
        };
    }

    /// <summary>
    /// Copied from ConditioningControlPanel/Services/Quiz/QuizService.cs: the report's payload
    /// types live in the WPF head, not in CCP.Core, and neither may be touched by this port.
    /// ponytail: delete these three when the quiz payload types move out of
    /// ConditioningControlPanel/Services/Quiz/QuizService.cs into Core. QuizService itself is not
    /// what is needed here - only its nested QuizCategory / QuizDifficulty / QuizReport shapes are.
    /// </summary>
    public enum QuizCategory
    {
        Sissy,
        Bambi,
        Obedience,
        Mindlessness,
        Submission
    }

    /// <inheritdoc cref="QuizCategory"/>
    public class QuizAnswerRecord
    {
        public int QuestionNumber { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string[] AllAnswers { get; set; } = new string[4];
        public int[] AllPoints { get; set; } = new int[4];
        public int ChosenIndex { get; set; }
        public int PointsEarned { get; set; }
    }

    /// <inheritdoc cref="QuizCategory"/>
    public class QuizHistoryEntry
    {
        public DateTime TakenAt { get; set; }
        public QuizCategory Category { get; set; }
        public int TotalScore { get; set; }
        public int MaxScore { get; set; }
        public string ProfileText { get; set; } = string.Empty;
        public List<QuizAnswerRecord> Answers { get; set; } = new();

        /// <summary>String category ID for custom categories. Falls back to Category enum name for built-in.</summary>
        public string CategoryId { get; set; } = string.Empty;

        /// <summary>Display name for the category (useful for custom categories where enum doesn't apply).</summary>
        public string CategoryName { get; set; } = string.Empty;
    }
}
