using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Settings ▸ General startup-video picker. Covers the WPF contract at
/// MainWindow.UiUpdates.cs:2005 - title, video filters, effective-assets start folder - and the
/// three ways a pick can end: accepted local path, cancellation, and a pick with no local path.
/// The last two must leave the stored setting and the label untouched.
/// </summary>
public sealed class GeneralStartupVideoTests
{
    [Fact]
    public void PickerOptionsCarryTheWpfTitleAndVideoFilters()
    {
        var options = GeneralSettingsSection.BuildStartupVideoPickerOptions();

        Assert.Equal(Loc.Get("title_select_startup_video"), options.Title);
        Assert.False(options.AllowMultiple);
        var filters = options.FileTypeFilter!;
        Assert.Equal(2, filters.Count);
        Assert.Equal(
            new[] { "*.mp4", "*.mov", "*.avi", "*.wmv", "*.mkv", "*.webm" },
            filters[0].Patterns!.ToArray());
        Assert.Equal(new[] { "*" }, filters[1].Patterns!.ToArray());
    }

    [Fact]
    public void StartFolderFollowsTheEffectiveAssetsProvider()
    {
        var previous = CorePaths.EffectiveAssetsProvider;
        var root = Path.Combine(Path.GetTempPath(), "ccp-startupvideo-assets-" + Guid.NewGuid().ToString("N"));
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            Assert.Equal(Path.Combine(root, "videos"), GeneralSettingsSection.StartupVideoFolder());
        }
        finally { CorePaths.EffectiveAssetsProvider = previous; }
    }

    [Fact]
    public void AcceptedPickPersists_CancellationAndNonLocalPickDoNot_AndReseedKeepsTheStoredValue()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            // CorePaths.UserData is resolved once per process from CCP_USERDATA_DIR, so the
            // harness's isolated profile is the only honest place to read the written settings.
            var profile = CorePaths.UserData;
            Directory.CreateDirectory(profile);
            Window? host = null;
            try
            {
                EnsureAvalonia();
                var service = new SettingsService();
                CoreSettings.ServiceProvider = () => service;
                service.SaveImmediate();
                var settingsPath = Path.Combine(profile, "settings.json");

                GeneralSettingsSection? section = null;
                OnUi(() =>
                {
                    section = new GeneralSettingsSection();
                    host = new Window { Width = 760, Height = 480, Content = section };
                    host.Show();
                    Dispatcher.UIThread.RunJobs();
                });
                var page = section!;
                var label = page.FindControl<TextBlock>("TxtStartupVideo")!;

                // No selection yet: the label reads "(Random)", exactly as WPF's clear path leaves it.
                Assert.Equal(Loc.Get("label_random"), label.Text);

                // Accepted pick: stores the full path, shows only the filename, saves.
                var picked = Path.Combine(profile, "videos", "trance clip.mp4");
                OnUi(() => page.ApplyPickedStartupVideo(picked));
                Assert.Equal("trance clip.mp4", label.Text);
                Assert.Equal(picked, CoreSettings.Current.StartupVideoPath);
                WaitFor(() => File.ReadAllText(settingsPath).Contains("trance clip.mp4", StringComparison.Ordinal));

                var reloaded = new SettingsService();
                Assert.Equal(picked, reloaded.Current.StartupVideoPath);

                // Cancellation (null) and a non-local pick (also null here) must change nothing.
                var before = File.ReadAllBytes(settingsPath);
                var writtenAt = File.GetLastWriteTimeUtc(settingsPath);
                OnUi(() =>
                {
                    page.ApplyPickedStartupVideo(null);
                    page.ApplyPickedStartupVideo("   ");
                });
                Assert.Equal("trance clip.mp4", label.Text);
                Assert.Equal(picked, CoreSettings.Current.StartupVideoPath);
                Thread.Sleep(650); // longer than SettingsService.Save's debounce
                Assert.Equal(before, File.ReadAllBytes(settingsPath));
                Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(settingsPath));

                // Reseeding the section (OnSectionShown, what the host calls on every show) must
                // repaint from the stored value, never overwrite it.
                OnUi(page.OnSectionShown);
                Assert.Equal("trance clip.mp4", label.Text);
                Assert.Equal(picked, CoreSettings.Current.StartupVideoPath);
                Thread.Sleep(650);
                Assert.Equal(before, File.ReadAllBytes(settingsPath));
                Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(settingsPath));

                // A fresh section over the same settings reseeds the filename, not "(Random)".
                OnUi(() =>
                {
                    host!.Content = new GeneralSettingsSection();
                    Dispatcher.UIThread.RunJobs();
                });
                var second = (GeneralSettingsSection)host!.Content!;
                Assert.Equal("trance clip.mp4", second.FindControl<TextBlock>("TxtStartupVideo")!.Text);
                Assert.Equal(picked, CoreSettings.Current.StartupVideoPath);
            }
            finally
            {
                try { if (host is not null) OnUi(host.Close); } catch { }
                CoreSettings.ServiceProvider = null;
                try { File.Delete(Path.Combine(profile, "settings.json")); } catch { }
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

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            Dispatcher.UIThread.RunJobs();
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
            try { if (condition()) return; }
            catch (IOException) { }
            Thread.Sleep(50);
        }
        Assert.True(condition(), "SettingsService did not flush the startup-video selection");
    }
}
