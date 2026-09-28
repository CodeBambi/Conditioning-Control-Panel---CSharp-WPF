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

            foreach (var c in "don't stop... \"obey\"")   // what a keyboard can type
                card.KeyTextInput(c.ToString());
            Dispatcher.UIThread.RunJobs();
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
}
