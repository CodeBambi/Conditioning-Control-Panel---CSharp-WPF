using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Remote Control toggle is a consent surface: premium gate first, the waiver never promises
/// what a controller cannot do here, and the pairing QR actually draws.</summary>
public sealed class RemoteControlTabTests
{
    [Fact]
    public async Task Toggle_is_gated_waiver_is_honest_and_the_qr_draws()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureAvalonia();
            var (premium, denied) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler);
            var refusals = 0;
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.ShowDeniedHandler = _ => refusals++;
            Window? host = null;
            try
            {
                var view = new RemoteControlTabView();
                host = new Window { Width = 1200, Height = 900, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();

                // Fix wave 2026-10-09 (S6, S7): emotes and the directory opt-in have no route on this relay yet, so
                // neither block is offered (a tick would tell a player they are listed when they are not).
                Assert.False(view.FindControl<Border>("EmotePickerPanel")!.IsVisible);
                Assert.False(view.FindControl<Border>("OptInSectionPanel")!.IsVisible);

                var toggle = view.FindControl<CheckBox>("ChkRemoteControlEnabled")!;
                toggle.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Assert.False(toggle.IsChecked);   // reverted before the refusal
                Assert.Equal(1, refusals);
                Assert.False(RemoteControlTabView.Relay.Value.IsActive);

                Assert.DoesNotContain("panic", RemoteControlTabView.Waiver("full").Split("A controller can never turn your panic key off.")[0], System.StringComparison.OrdinalIgnoreCase);
                Assert.Contains("strict lock", RemoteControlTabView.Waiver("full"));
                Assert.Contains("A controller can never turn your panic key off.", RemoteControlTabView.Waiver("light"));
                Assert.DoesNotContain("videos", RemoteControlTabView.Waiver("light"));

                var qr = RemoteControlTabView.QrCode(RemoteRelay.PairingUrl("ABC123", "0420"));
                Assert.NotNull(qr);
                Assert.True(qr!.PixelSize.Width > 200);
            }
            finally
            {
                host?.Close();
                (CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler) = (premium, denied);
            }
            return Task.CompletedTask;
        });
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
