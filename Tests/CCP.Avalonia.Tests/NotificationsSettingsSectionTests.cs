using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using ConditioningControlPanel;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NotificationsSettingsSectionTests
{
    [Fact]
    public void BothNotificationPreferencesRoundTripWithoutLoadOrRebindWrites()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            var profile = Path.Combine(Path.GetTempPath(), "ccp-notifications-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var previousProfile = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", profile);
        Window? host = null;

        try
        {
            EnsureAvalonia();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.SaveImmediate();

            var settingsPath = Path.Combine(profile, "settings.json");
            var before = File.ReadAllBytes(settingsPath);
            var writtenAt = File.GetLastWriteTimeUtc(settingsPath);
            NotificationsSettingsSection? section = null;
            OnUi(() =>
            {
                section = new NotificationsSettingsSection();
                host = new Window { Width = 760, Height = 220, Content = section };
                host.Show();
                Dispatcher.UIThread.RunJobs();
            });

            var page = section!;
            var intake = page.FindControl<CheckBox>("ChkIntakeNudge")!;
            var suppress = page.FindControl<CheckBox>("ChkSuppressPerkNotifications")!;
            Assert.True(intake.IsChecked == true);
            Assert.False(suppress.IsChecked == true);
            Thread.Sleep(650); // longer than SettingsService.Save's 500ms debounce
            Assert.Equal(before, File.ReadAllBytes(settingsPath));
            Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(settingsPath));

            OnUi(() =>
            {
                intake.IsChecked = false;
                suppress.IsChecked = true;
            });
            WaitFor(() => HasValues(settingsPath, intake: false, suppress: true));

            var reloaded = new SettingsService();
            Assert.False(reloaded.Current.IntakeNudgeEnabled);
            Assert.True(reloaded.Current.SuppressPerkNotifications);

            OnUi(() => suppress.IsChecked = false);
            WaitFor(() => HasValues(settingsPath, intake: false, suppress: false));
            var falseValues = new SettingsService();
            Assert.False(falseValues.Current.IntakeNudgeEnabled);
            Assert.False(falseValues.Current.SuppressPerkNotifications);

            // Restore writes once itself; processing CurrentReplaced must only repaint, not write again.
            var restored = new AppSettings
            {
                IntakeNudgeEnabled = true,
                SuppressPerkNotifications = true,
            };
            OnUi(() => service.RestoreFrom(restored), drain: false);
            var afterRestore = File.ReadAllBytes(settingsPath);
            var restoreWrittenAt = File.GetLastWriteTimeUtc(settingsPath);
            OnUi(() => { }); // drain the posted CurrentReplaced repaint
            Assert.True(intake.IsChecked == true);
            Assert.True(suppress.IsChecked == true);
            Thread.Sleep(650);
            Assert.Equal(afterRestore, File.ReadAllBytes(settingsPath));
            Assert.Equal(restoreWrittenAt, File.GetLastWriteTimeUtc(settingsPath));

            // Detach and reattach against a fresh service: reattachment must repaint and still not save.
            File.WriteAllText(settingsPath, "{}");
            var defaults = new SettingsService();
            CoreSettings.ServiceProvider = () => defaults;
            var beforeReattach = File.ReadAllBytes(settingsPath);
            var reattachWrittenAt = File.GetLastWriteTimeUtc(settingsPath);
            OnUi(() =>
            {
                host!.Content = null;
                Dispatcher.UIThread.RunJobs();
                host.Content = page;
                Dispatcher.UIThread.RunJobs();
            });
            Assert.True(intake.IsChecked == true);
            Assert.False(suppress.IsChecked == true);
            Thread.Sleep(650);
            Assert.Equal(beforeReattach, File.ReadAllBytes(settingsPath));
            Assert.Equal(reattachWrittenAt, File.GetLastWriteTimeUtc(settingsPath));
            Assert.True(defaults.Current.IntakeNudgeEnabled);
            Assert.False(defaults.Current.SuppressPerkNotifications);
        }
        finally
        {
            try
            {
                if (host is not null) OnUi(host.Close);
            }
            catch { }
            CoreSettings.ServiceProvider = null;
            Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", previousProfile);
            try { Directory.Delete(profile, recursive: true); } catch { }
            }
        });
    }

    private static void EnsureAvalonia()
    {
        Assert.True(AvaloniaTestDispatcher.IsDispatcherThread, "Avalonia setup did not stay on the test dispatcher");
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    private static void OnUi(Action action, bool drain = true)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            if (drain) Dispatcher.UIThread.RunJobs();
            return;
        }

        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        Dispatcher.UIThread.Post(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        });
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "Avalonia UI dispatcher did not run the test action");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            Thread.Sleep(50);
        }
        Assert.True(condition(), "SettingsService did not flush the control edit");
    }

    private static bool HasValues(string path, bool intake, bool suppress)
    {
        try
        {
            var json = File.ReadAllText(path);
            return json.Contains($"\"IntakeNudgeEnabled\": {intake.ToString().ToLowerInvariant()}", StringComparison.Ordinal)
                && json.Contains($"\"SuppressPerkNotifications\": {suppress.ToString().ToLowerInvariant()}", StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
    }
}
