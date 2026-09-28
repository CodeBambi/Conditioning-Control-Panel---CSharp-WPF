using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The classic quiz (hidden on both heads, as in WPF): the AI gate, a full offline run over
/// QuizStore fallback questions, the history file in WPF's shape, the trend and the report.</summary>
public sealed class QuizWindowStaticTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task StartQuizIsGatedOnAi() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var host = new Window { Content = new GradedIntakeTabView() };
        host.Show();
        var tab = (GradedIntakeTabView)host.Content!;
        QuizWindow? opened = null;
        using var sub = Window.WindowOpenedEvent.AddClassHandler(typeof(QuizWindow), (s, _) => opened = (QuizWindow?)s);
        try
        {
            CoreAi.IsAvailableProvider = null;
            tab.BtnStartQuiz_Click(null, new RoutedEventArgs());
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            Assert.False(QuizWindow.IsOpen);
            var dialog = Assert.Single(host.OwnedWindows.OfType<MessageDialog>());
            Assert.Contains(dialog.GetVisualDescendantTexts(),
                t => t == Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"));
            dialog.Close();

            CoreAi.IsAvailableProvider = () => true;
            tab.BtnStartQuiz_Click(null, new RoutedEventArgs());
            Assert.True(QuizWindow.IsOpen);
            Assert.NotNull(opened);
        }
        finally
        {
            CoreAi.IsAvailableProvider = null;
            foreach (var q in host.OwnedWindows.ToList()) q.Close();
            opened?.Close();
            host.Close();
        }
    });

    [Fact]
    public Task FullOfflineRunScoresSavesHistoryAndReports() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var historyPath = Path.Combine(CorePaths.UserData, "quiz_history.json");
        File.Delete(historyPath);
        QuizWindow._random = new Random(7);   // seed that rolls neither the trick question nor surrender
        var w = new QuizWindow(false, false);
        w.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();   // Loaded builds the progress dots
        try
        {
            var bambi = QuizStore.FindCategory("bambi")!;
            w.DynamicCategoryButton_Click(new Border { Tag = bambi }, null!);
            Assert.Null(w.FindControl<TextBlock>("TxtError")!.Text);
            var answerA = w.FindControl<Border>("AnswerA")!;
            var resultPanel = w.FindControl<Grid>("ResultPanel")!;

            var expected = 0;
            for (var n = 1; n <= 10; n++)
            {
                // Always answer A; the oracle maps the shown text back to the fallback's points.
                var shown = w.FindControl<TextBlock>("TxtAnswerA")!.Text;
                var fallback = QuizStore.GetFallbackQuestion(QuizCategory.Bambi, n);
                Assert.Equal(fallback.QuestionText, w.FindControl<TextBlock>("TxtQuestion")!.Text);
                expected += fallback.Points[Array.IndexOf(fallback.Answers, shown)];

                w.Answer_Click(answerA, null!);
                for (var i = 0; i < 100 && !(answerA.IsHitTestVisible || resultPanel.IsVisible); i++)
                    await Task.Delay(50);
            }

            Assert.True(resultPanel.IsVisible);
            Assert.Equal(Loc.GetF("quiz_final_score", expected, 40), w.FindControl<TextBlock>("TxtFinalScore")!.Text);
            Assert.StartsWith($"Score: {(int)Math.Round(expected / 40.0 * 100)}%",
                ((TextBlock)w.FindControl<StackPanel>("TrendPanel")!.Children.Single()).Text);

            // History in WPF's shape: same keys, same order, as the Core golden fixture.
            var written = (JObject)JArray.Parse(File.ReadAllText(historyPath)).Single();
            var golden = (JObject)JArray.Parse(File.ReadAllText(Golden()))[0];
            Assert.Equal(Keys(golden), Keys(written));
            Assert.Equal(10, ((JArray)written["Answers"]!).Count);
            Assert.Equal(expected, (int)written["TotalScore"]!);
            Assert.Equal("bambi", (string?)written["CategoryId"]);

            var report = new QuizReportWindow(QuizStore.LoadHistory()[0]);
            Assert.Equal($"{expected} / 40  ({(int)Math.Round(expected / 40.0 * 100)}%)",
                report.FindControl<TextBlock>("TxtScore")!.Text);
        }
        finally
        {
            QuizWindow._random = new Random();
            w.Close();
        }
    });

    private static string[] Keys(JObject o) =>
        o.Properties().Select(p => p.Name)
            .Concat(((JArray)o["Answers"]!).OfType<JObject>().Take(1).SelectMany(a => a.Properties().Select(p => "Answers." + p.Name)))
            .ToArray();

    private static string Golden([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "CCP.Core.Tests", "Fixtures", "Quiz", "quiz_history_golden.json");
}

internal static class VisualTextExtensions
{
    internal static string?[] GetVisualDescendantTexts(this Visual v) =>
        global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v).OfType<TextBlock>().Select(t => t.Text).ToArray();
}
