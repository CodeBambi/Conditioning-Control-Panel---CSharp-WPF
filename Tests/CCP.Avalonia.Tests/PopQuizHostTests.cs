using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>PopQuizHost: the Test button path opens a quiz, a lock card defers it until the card
/// closes, and CoreEngine.Stop closes an open one.</summary>
public sealed class PopQuizHostTests
{
    [Fact]
    public Task OpensDefersBehindLockCardAndClosesOnEngineStop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var scheduler = PopQuizHost.Instance.Scheduler;
        var wasEnabled = CoreSettings.Current.PopQuizEnabled;
        CoreEngine.PopQuiz = scheduler;
        try
        {
            CoreSettings.Current.PopQuizEnabled = true;
            scheduler.Start();
            scheduler.Show(isTest: true);
            Assert.True(PopQuizWindow.IsAnyOpen());
            CoreEngine.Stop();
            Assert.False(PopQuizWindow.IsAnyOpen());

            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false, isTest: true);
            scheduler.Show(isTest: true);
            Assert.False(PopQuizWindow.IsAnyOpen());   // deferred, not stacked on the card
            LockCardWindow.ForceCloseAll();
            Dispatcher.UIThread.RunJobs();
            Assert.True(PopQuizWindow.IsAnyOpen());    // replayed once the card left

            // #763 the other way: a card waits behind an open quiz and replays when it closes.
            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false, isTest: true);
            Assert.False(LockCardWindow.IsAnyOpen());
            PopQuizWindow.ForceCloseAll();
            Assert.True(LockCardWindow.IsAnyOpen());
            LockCardWindow.ForceCloseAll();

            // Audit #1833: a card held behind a quiz must not flash up while the engine stops.
            scheduler.Start();
            scheduler.Show(isTest: true);
            Assert.True(PopQuizWindow.IsAnyOpen());
            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false, isTest: true);
            Assert.False(LockCardWindow.IsAnyOpen());   // held
            CoreEngine.Stop();
            Dispatcher.UIThread.RunJobs();
            Assert.False(PopQuizWindow.IsAnyOpen());
            Assert.False(LockCardWindow.IsAnyOpen());
        }
        finally
        {
            PopQuizWindow.ForceCloseAll();
            LockCardWindow.ForceCloseAll();
            CoreEngine.PopQuiz = null;
            CoreSettings.Current.PopQuizEnabled = wasEnabled;
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF 7.1.5 (f154640bb): the card's "Turn these off" link switches PopQuizEnabled off,
    /// keeps the Graded Intake switch in step, closes like Esc and stops the schedule.</summary>
    [Theory]
    [InlineData(false)]   // Enter on the focused link
    [InlineData(true)]    // left click (WPF MouseLeftButtonUp)
    public Task TurnOffLinkSwitchesQuizzesOffAndSyncsTheIntakeSwitch(bool byMouse) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var scheduler = PopQuizHost.Instance.Scheduler;
        var wasEnabled = CoreSettings.Current.PopQuizEnabled;
        CoreEngine.PopQuiz = scheduler;
        var tab = new global::ConditioningControlPanel.Avalonia.Views.Tabs.GradedIntakeTabView();
        var host = new global::Avalonia.Controls.Window { Content = tab };
        try
        {
            CoreSettings.Current.PopQuizEnabled = true;
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var chk = tab.FindControl<global::Avalonia.Controls.CheckBox>("ChkPopQuizEnabled")!;
            Assert.True(chk.IsChecked);
            scheduler.Start();
            var quiz = new PopQuizWindow(PopQuizScheduler.QuestionPool[0], isTest: true);
            quiz.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(PopQuizWindow.IsAnyOpen());

            var link = quiz.FindControl<global::Avalonia.Controls.TextBlock>("TxtTurnOff")!;
            Assert.True(link.Focusable);
            if (byMouse)
            {
                var at = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), quiz)!.Value;
                quiz.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
                quiz.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
            }
            else
                link.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
                {
                    RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
                    Key = global::Avalonia.Input.Key.Enter,
                });
            Dispatcher.UIThread.RunJobs();

            Assert.False(CoreSettings.Current.PopQuizEnabled);
            Assert.False(PopQuizWindow.IsAnyOpen());
            Assert.False(scheduler.IsRunning);
            Assert.False(chk.IsChecked);
        }
        finally
        {
            host.Close();
            PopQuizWindow.ForceCloseAll();
            scheduler.Stop();
            CoreEngine.PopQuiz = null;
            CoreSettings.Current.PopQuizEnabled = wasEnabled;
        }
        return Task.CompletedTask;
    });
}
