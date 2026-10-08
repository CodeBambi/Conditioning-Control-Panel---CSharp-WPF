using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>win-quiz-report: the past-quizzes list (WPF MainWindow.Lab.cs:468) is the report's only
/// opener. Hidden with BtnStartQuiz as in WPF; unhidden it lists trends + runs, and a run opens one report.</summary>
public sealed class QuizPastRunsReportTests
{
    [Fact]
    public Task PastRunOpensItsReportOneAtATime() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var historyPath = Path.Combine(CorePaths.UserData, "quiz_history.json");
        var saved = File.Exists(historyPath) ? File.ReadAllText(historyPath) : null;
        File.Copy(Golden(), historyPath, overwrite: true);
        var tab = new GradedIntakeTabView();
        var host = new Window { Content = tab };
        host.Show();
        try
        {
            // Hidden as in WPF: the early return leaves the list collapsed and empty.
            tab.RefreshPastQuizzes();
            Assert.False(tab.PastQuizzesPanel.IsVisible);
            Assert.Empty(tab.PastQuizzesList.Children);

            tab.BtnStartQuiz.IsVisible = true;
            tab.RefreshPastQuizzes();
            Assert.True(tab.PastQuizzesPanel.IsVisible && tab.TxtPastQuizzesHeader.IsVisible);
            var trends = tab.PastQuizzesList.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
            // The legacy entry (no CategoryId, enum Obedience) folds into "obedience" case-insensitively.
            Assert.Equal(new[] { "Obedience: 78% \u219128% · Devoted Servant", "Velvet Édition: 30%" }, trends);
            var runs = tab.PastQuizzesList.Children.OfType<Button>().ToList();
            Assert.Equal(3, runs.Count);
            Assert.EndsWith("Obedience  ·  31/40 (78%)", ((TextBlock)runs[0].Content!).Text);

            runs[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var first = Assert.Single(host.OwnedWindows.OfType<QuizReportWindow>());
            Assert.Equal("31 / 40  (78%)", first.FindControl<TextBlock>("TxtScore")!.Text);

            runs[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var second = Assert.Single(host.OwnedWindows.OfType<QuizReportWindow>());
            Assert.NotSame(first, second);
            Assert.Equal("12 / 40  (30%)", second.FindControl<TextBlock>("TxtScore")!.Text);
        }
        finally
        {
            foreach (var w in host.OwnedWindows.ToList()) w.Close();
            host.Close();
            if (saved is null) File.Delete(historyPath); else File.WriteAllText(historyPath, saved);
        }
        return Task.CompletedTask;
    });

    private static string Golden([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "CCP.Core.Tests", "Fixtures", "Quiz", "quiz_history_golden.json");
}
