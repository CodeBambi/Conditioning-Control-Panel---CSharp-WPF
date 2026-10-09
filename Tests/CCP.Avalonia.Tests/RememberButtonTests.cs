using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The bottom bar's Remember button (WPF MainWindow.Remember.cs): first click snapshots the
/// setup and fills the star, the next click recalls it over later changes, right-click re-saves, and
/// mid-session both directions are refused without touching the slot or the settings.</summary>
public sealed class RememberButtonTests
{
    [Fact]
    public async Task ClickSnapshotsThenRecallsRightClickResavesSessionRefuses()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var s = CoreSettings.Current;
            var (json, freq, muted) = (s.RememberedConfigJson, s.FlashFrequency, s.BrowserVideoMuted);
            var sessionProvider = CoreSession.IsSessionRunningProvider;
            s.RememberedConfigJson = "{}";   // a slot saved last run: the star is filled at startup
            MainShellWindow? shell = null;
            try
            {
                shell = new MainShellWindow();
                shell.Show();
                var btn = shell.Named<Button>("BtnRemember")!;
                var icon = shell.Named<TextBlock>("TxtRememberIcon")!;
                void Click() => btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("★", icon.Text);
                Assert.Equal(Loc.Get("tooltip_remember_recall"), ToolTip.GetTip(btn));

                s.RememberedConfigJson = null;
                shell.SyncRememberButton();
                Assert.Equal("☆", icon.Text);
                Assert.Equal(Loc.Get("tooltip_remember_save"), ToolTip.GetTip(btn));

                // Empty slot: snapshot.
                s.FlashFrequency = 7;
                s.BrowserVideoMuted = true;
                Click();
                Assert.False(string.IsNullOrEmpty(s.RememberedConfigJson));
                Assert.Equal("★", icon.Text);
                Assert.Equal(Loc.Get("tooltip_remember_recall"), ToolTip.GetTip(btn));

                // Filled slot: recall over later changes.
                s.FlashFrequency = 3;
                s.BrowserVideoMuted = false;
                Click();
                Assert.Equal(7, s.FlashFrequency);
                Assert.True(s.BrowserVideoMuted);

                // Right-click overwrites the slot with the current setup.
                s.FlashFrequency = 5;
                shell.UpdateLayout();
                var centre = btn.TranslatePoint(new Point(btn.Bounds.Width / 2, btn.Bounds.Height / 2), shell)!.Value;
                shell.MouseDown(centre, MouseButton.Right);
                shell.MouseUp(centre, MouseButton.Right);
                s.FlashFrequency = 9;
                Click();
                Assert.Equal(5, s.FlashFrequency);

                // Mid-session: both directions refused, slot and settings untouched.
                CoreSession.IsSessionRunningProvider = () => true;
                var slot = s.RememberedConfigJson;
                s.FlashFrequency = 9;
                // Right-click first: the click's refusal dialog is modal and would block the mouse.
                // The hover tooltip the first right-click opened sits over the button in a headless
                // overlay layer and would swallow this one.
                ToolTip.SetIsOpen(btn, false);
                Dispatcher.UIThread.RunJobs();
                shell.MouseDown(centre, MouseButton.Right);
                shell.MouseUp(centre, MouseButton.Right);
                Assert.Equal(slot, s.RememberedConfigJson);
                Click();
                Assert.Equal(9, s.FlashFrequency);
                Assert.Equal(slot, s.RememberedConfigJson);
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                CoreSession.IsSessionRunningProvider = sessionProvider;
                foreach (var w in shell?.OwnedWindows.ToList() ?? new()) w.Close();
                shell?.Close();
                (s.RememberedConfigJson, s.FlashFrequency, s.BrowserVideoMuted) = (json, freq, muted);
            }
            return Task.CompletedTask;
        });
    }
}
