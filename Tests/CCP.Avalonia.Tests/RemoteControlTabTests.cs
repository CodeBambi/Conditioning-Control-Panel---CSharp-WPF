using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
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

    /// <summary>WPF BtnEmoteEdit/Save (MainWindow.RemoteControl.cs:441): the popup edits the shared preset;
    /// a send without a session says so instead of failing silently.</summary>
    [Fact]
    public async Task Emote_edit_popup_saves_the_preset_and_a_send_without_a_session_says_so()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var oldProvider = CoreSettings.ServiceProvider;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            Window? host = null;
            try
            {
                var view = new RemoteControlTabView();
                host = new Window { Width = 1200, Height = 900, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                var preset = CoreSettings.Current.RemoteEmotePresets[0];
                var buttons = view.FindControl<ItemsControl>("LstEmotePresets")!.GetVisualDescendants().OfType<Button>()
                    .Where(b => ReferenceEquals(b.Tag, preset)).ToList();
                var edit = buttons.First(b => b.Content as string == "✎");
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(view.FindControl<Popup>("EmoteEditPopup")!.IsOpen);
                var text = view.FindControl<TextBox>("TxtEditEmoteText")!;
                var save = view.FindControl<Button>("BtnEditEmoteSave")!;
                text.Text = "   "; Dispatcher.UIThread.RunJobs();
                Assert.False(save.IsEnabled);
                text.Text = "  Good girl  "; Dispatcher.UIThread.RunJobs();
                Assert.True(save.IsEnabled);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Good girl", preset.Text);
                Assert.False(view.FindControl<Popup>("EmoteEditPopup")!.IsOpen);

                // Enter in the custom box sends (WPF :339); with no session it says so and keeps the text.
                var status = view.FindControl<TextBlock>("TxtEmoteStatus")!;
                var custom = view.FindControl<TextBox>("TxtEmoteCustom")!;
                custom.Text = "hello";
                custom.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
                for (var i = 0; i < 50 && string.IsNullOrEmpty(status.Text); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
                Assert.Equal(Loc.Get("status_emote_no_session"), status.Text);
                Assert.Equal("hello", custom.Text);
                status.Text = "";

                buttons.First(b => b != edit).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 50 && string.IsNullOrEmpty(view.FindControl<TextBlock>("TxtEmoteStatus")!.Text); i++)
                { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
                Assert.Equal(Loc.Get("status_emote_no_session"), view.FindControl<TextBlock>("TxtEmoteStatus")!.Text);
            }
            finally
            {
                host?.Close();
                CoreSettings.ServiceProvider = oldProvider;
            }
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
