using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF LockCardWindow.xaml.cs:721-785: typed answers go through LockCardText, wrong input
/// counts errors, and the session phrase survives a language change.</summary>
public sealed class LockCardTypingTests
{
    [Fact]
    public Task TypographicPhraseSolvesByKeyboardAndCountsErrors() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        const string phrase = "don\u2019t stop\u2026 \u201Cobey\u201D";
        var lang = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LockCardWindow.ShowOnAllMonitors(phrase, 2, strictMode: false, isTest: true);
            Dispatcher.UIThread.RunJobs();
            var card = LockCardWindow.Primary!;
            var box = card.FindControl<TextBox>("TxtInput")!;
            box.Focus();

            LocalizationManager.Instance.SetLanguage(lang == "de" ? "en" : "de");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(phrase, card.PhraseShown);

            card.KeyTextInput("x");                    // wrong from the first letter
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, card.TotalErrors);
            box.Text = "";
            Dispatcher.UIThread.RunJobs();

            foreach (var c in "don't stop... \"obey\"")   // what a keyboard can type, one key at a time
            {
                card.KeyTextInput(c.ToString());
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(card.IsCompleted);
            card.KeyTextInput("DON'T STOP... \"OBEY\"");      // repeat 2 as one chunk (an IME)
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, card.TotalErrors);             // "..." for "\u2026" is not a mistake
            Assert.True(card.IsCompleted, "the typed phrase did not solve the card");
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage(lang);
            LockCardWindow.ForceCloseAll();
        }
        return Task.CompletedTask;
    });

    /// <summary>A fast typist: the last letter of one repeat and the first of the next land before
    /// the dispatcher runs. Judged at the change (as WPF), that is one repeat and no error.</summary>
    [Fact]
    public Task FastTypingAcrossARepeatLosesNothing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        try
        {
            LockCardWindow.ShowOnAllMonitors("obey", 4, strictMode: false, isTest: true);
            Dispatcher.UIThread.RunJobs();
            var card = LockCardWindow.Primary!;
            var box = card.FindControl<TextBox>("TxtInput")!;
            box.Focus();
            foreach (var c in "obey") { card.KeyTextInput(c.ToString()); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(1, card.CompletedRepeats);
            Assert.Equal("", box.Text);
            Assert.Equal(0, box.CaretIndex);   // cleared for the next repeat, caret home

            foreach (var c in "obe") { card.KeyTextInput(c.ToString()); Dispatcher.UIThread.RunJobs(); }
            card.KeyTextInput("y");            // the last letter and the next repeat's first,
            card.KeyTextInput("o");            // both before the dispatcher runs
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, card.CompletedRepeats);
            Assert.Equal(0, card.TotalErrors);
            Assert.Equal(1, box.CaretIndex);

            // An input method committing two chunks before the dispatcher runs (a Text write, as
            // IMEs and the mirror sync do): each is judged as it lands.
            box.Text = "o" + "bey";
            box.Text = "obey" + "o";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, card.CompletedRepeats);
            Assert.Equal(0, card.TotalErrors);
            Assert.Equal("o", box.Text);
        }
        finally { LockCardWindow.ForceCloseAll(); }
        return Task.CompletedTask;
    });

    /// <summary>WPF dda21a45a (ccp-bugs #1163): the feature card's "Reset on typo" switch makes a
    /// mistake wipe the line, so the repeat starts over; the next correct repeat still counts.</summary>
    [Fact]
    public Task ResetOnTypoSwitchWipesTheLine() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = global::ConditioningControlPanel.CoreSettings.Current;
        var was = s.LockCardResetOnTypo;
        try
        {
            s.LockCardResetOnTypo = false;
            var feature = new global::ConditioningControlPanel.Avalonia.Views.Features.LockCardFeatureControl();
            var host = new Window { Content = feature };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            feature.FindControl<CheckBox>("ChkResetOnTypo")!.IsChecked = true;   // the user's switch
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.LockCardResetOnTypo);
            host.Close();

            LockCardWindow.ShowOnAllMonitors("obey", 2, strictMode: false, isTest: true);
            Dispatcher.UIThread.RunJobs();
            var card = LockCardWindow.Primary!;
            var box = card.FindControl<TextBox>("TxtInput")!;
            box.Focus();
            foreach (var c in "obx") { card.KeyTextInput(c.ToString()); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(1, card.TotalErrors);
            Assert.Equal("", box.Text);             // the typo wiped the line

            foreach (var c in "obey") { card.KeyTextInput(c.ToString()); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(1, card.CompletedRepeats);
            Assert.Equal(1, card.TotalErrors);
        }
        finally
        {
            s.LockCardResetOnTypo = was;
            LockCardWindow.ForceCloseAll();
        }
        return Task.CompletedTask;
    });
}
