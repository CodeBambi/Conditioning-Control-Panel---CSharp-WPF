using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Stacking, expiry, action invoke and dismiss of the ported WPF corner toasts.</summary>
public sealed class NotificationServiceTests
{
    [Fact]
    public async Task ToastsQueueStackExpireAndInvokeTheirAction()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var service = new NotificationService();
            var host = new StackPanel();
            var window = new Window { Width = 600, Height = 400, Content = host };
            window.Show();

            // Fired before the host exists: replayed on attach, in order (WPF AttachHost).
            service.Show("first", NotificationType.Info, TimeSpan.FromMilliseconds(150));
            Assert.Empty(host.Children);
            service.AttachHost(host);
            var invoked = 0;
            service.Show("second", NotificationType.Warning, TimeSpan.FromSeconds(30), "See tiers", () => invoked++);
            service.Show("third", NotificationType.Error, TimeSpan.FromSeconds(30));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "first", "second", "third" }, host.Children.Select(Message));
            Assert.Equal(Color.Parse("#FFB347"), ((SolidColorBrush)((Border)host.Children[1]).BorderBrush!).Color);

            // Expiry: 150 ms display + 220 ms fade-out removes the first toast only.
            // Bounded wait, not a fixed sleep: every Avalonia test shares one UI thread, so another class's
            // test can hold it through the gap and the toast's own timer + fade run late.
            await Until(() => host.Children.Count == 2);
            Assert.Equal(new[] { "second", "third" }, host.Children.Select(Message));

            // Action button runs the callback once and the toast leaves; × dismisses the other.
            Find(host.Children[0], "ToastAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find(host.Children[1], "ToastDismiss").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, invoked);
            await Until(() => host.Children.Count == 0);
            Assert.Empty(host.Children);
            window.Close();
        });
    }

    private static async Task Until(Func<bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(15);
        while (!done() && DateTime.UtcNow < limit)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static string Message(Control toast) =>
        ((TextBlock)((Grid)((Border)toast).Child!).Children[0]).Text!;

    private static Button Find(Control toast, string name) =>
        ((Grid)((Border)toast).Child!).Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(c => c.Name == name);
}
