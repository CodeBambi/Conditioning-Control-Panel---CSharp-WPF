using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 54d020e60 (ChasterRaffleCardLayoutTests): in German the raffle card's counts row
/// ran into itself in the fixed 360 px card; the rows are SplitRowPanels now.</summary>
public sealed class ChasterRaffleCardLayoutTests
{
    [Theory]
    [InlineData("de", 360.0)]
    [InlineData("de", 280.0)]
    [InlineData("en", 360.0)]
    public Task TheDaysAndTheTotalNeverDrawOverEachOther(string lang, double cardWidth) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var was = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage(lang);
            var tab = new ChasterTabView();
            var popup = tab.FindControl<global::Avalonia.Controls.Primitives.Popup>("LadderPopup")!;
            var card = (Border)popup.Child!;
            popup.Child = null;
            card.Width = cardWidth;
            tab.FindControl<StackPanel>("LadderRows")!.IsVisible = true;
            TextBlock T(string n) => tab.FindControl<TextBlock>(n)!;
            T("TxtRaffleDay").Text = Loc.GetF("chaster_raffle_day", 4, 31);
            T("TxtRaffleDays").Text = Loc.GetF("chaster_raffle_days", 0, 25);
            T("TxtRaffleTotal").Text = Loc.GetF("chaster_raffle_total", "00:00", "31:00:00");

            var host = new Window { Width = 1000, Height = 1000,
                Content = new Panel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Children = { card } } };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();

            Rect Box(Control c) => new(c.TranslatePoint(default, host)!.Value, c.Bounds.Size);
            var days = Box(T("TxtRaffleDays"));
            var total = Box(T("TxtRaffleTotal"));
            Assert.True(days.Width > 0 && total.Width > 0);
            Assert.False(days.Intersects(total), $"{lang} @{cardWidth}: days {days} overlaps total {total}");
            if (lang == "en")
                Assert.False(tab.FindControl<global::ConditioningControlPanel.Avalonia.Controls.SplitRowPanel>("RaffleCountsRow")!.IsStacked,
                    "English keeps its one-line look");
            host.Close();
        }
        finally { LocalizationManager.Instance.SetLanguage(was); }
        return Task.CompletedTask;
    });
}
