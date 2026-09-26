using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Lesson-locked shelf rows (part 4): while a purchasable's lesson is incomplete, its
/// Toybox row swaps the buy button for the lesson text + a progress bar + the count.
/// The shelf builders in the main file call <see cref="BuildLessonLockPanel"/> wherever
/// they would have placed an Unlock/Train button.
/// </summary>
public partial class ChaosHubWindow
{
    private static readonly Color LessonTrack = Color.FromRgb(0x2A, 0x26, 0x4C);
    private static readonly Color LessonFillA = Color.FromRgb(0xE8, 0x43, 0x93);
    private static readonly Color LessonFillB = Color.FromRgb(0x8B, 0x5C, 0xF6);
    private const double LESSON_BAR_WIDTH = 120;
    private const double LESSON_BAR_HEIGHT = 7;

    /// <summary>The right-column block for a lesson-blocked row: lesson text, progress bar,
    /// "lesson: x of y". Before any progress, the tooltip keeps its mystery.</summary>
    private FrameworkElement BuildLessonLockPanel(string id, Color accent)
    {
        var def = ChaosLessons.ById(id);
        if (def == null) return new StackPanel();   // defensive: only called for table ids
        long target = Math.Max(1, def.Target);
        long progress = Math.Clamp(ChaosLessons.Progress(id), 0, target);

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };

        panel.Children.Add(new ConditioningControlPanel.Helpers.EmojiTextBlock
        {
            Text = "🔒 " + def.Text,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, accent.R, accent.G, accent.B)),
            FontSize = 10.5,
            FontStyle = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
        });

        double frac = (double)progress / target;
        var fill = new Border
        {
            CornerRadius = new CornerRadius(LESSON_BAR_HEIGHT / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = progress <= 0 ? 0 : Math.Max(LESSON_BAR_HEIGHT, LESSON_BAR_WIDTH * frac),
            Background = new LinearGradientBrush(LessonFillA, LessonFillB, 0),
        };
        panel.Children.Add(new Border
        {
            Width = LESSON_BAR_WIDTH,
            Height = LESSON_BAR_HEIGHT,
            CornerRadius = new CornerRadius(LESSON_BAR_HEIGHT / 2),
            Background = new SolidColorBrush(LessonTrack),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 7, 0, 4),
            Child = fill,
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"lesson: {progress} of {target}",
            Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0xB8, 0xB8, 0xD0)),
            FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Right,
        });

        // The row keeps its mystery line; the hover always gives the exact mechanics —
        // what counts, the precise window/threshold, and what doesn't count.
        ChaosTips.Attach(panel,
            progress <= 0 ? "the lesson" : "the lesson, half-learned",
            string.IsNullOrEmpty(def.Detail) ? def.Text : def.Detail,
            $"progress: {progress} of {target}", accent);
        return panel;
    }
}
