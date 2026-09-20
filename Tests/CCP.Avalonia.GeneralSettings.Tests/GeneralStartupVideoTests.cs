using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.GeneralSettings.Tests;

/// <summary>
/// Settings ▸ General startup-video picker. Covers the WPF contract at
/// MainWindow.UiUpdates.cs:2005 - title, video filters, effective-assets start folder - and the
/// three ways a pick can end: accepted local path, cancellation, and a pick with no local path.
/// The last two must leave the stored setting and the label untouched.
/// </summary>
public sealed class GeneralStartupVideoTests
{
    public GeneralStartupVideoTests() => GeneralSettingsTestProfile.AssertOwned();

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
    public async Task PickerButtonHandlesLocalCancelNonLocalAndProviderFailure()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            LocalizationManager.Instance.Initialize("en");

            var previousProvider = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = null;
            Window? host = null;
            var previousFeedback = GeneralSettingsSection.PickerFeedbackOverride;
            var feedback = new List<(string Title, string Message)>();
            try
            {
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
                var button = page.FindControl<Button>("BtnSelectStartupVideo")!;
                var baseline = Path.Combine(Path.GetTempPath(), "previous-startup-video.mp4");
                var localPath = Path.Combine(Path.GetTempPath(), "picked-startup-video.mp4");
                CoreSettings.Current.StartupVideoPath = baseline;
                OnUi(page.OnSectionShown);

                GeneralSettingsSection.PickerFeedbackOverride = (_, title, message) =>
                {
                    feedback.Add((title, message));
                    return Task.CompletedTask;
                };

                var (provider, providerControl) = NewStorageProvider();
                using var locatorScope = BindStorageProvider(provider);
                Assert.Same(provider, host!.StorageProvider);

                // This is the button's actual event handler, not ApplyPickedStartupVideo.
                providerControl.Open = _ => new IStorageFile[] { NewStorageFile(new Uri(localPath)) };
                OnUi(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Assert.Equal(localPath, CoreSettings.Current.StartupVideoPath);
                Assert.Equal(Path.GetFileName(localPath), label.Text);
                Assert.Empty(feedback);

                void RestoreBaseline()
                {
                    CoreSettings.Current.StartupVideoPath = baseline;
                    OnUi(page.OnSectionShown);
                    feedback.Clear();
                }

                // An empty result is the provider's cancellation contract and stays silent.
                RestoreBaseline();
                providerControl.Open = _ => Array.Empty<IStorageFile>();
                OnUi(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Assert.Equal(baseline, CoreSettings.Current.StartupVideoPath);
                Assert.Equal(Path.GetFileName(baseline), label.Text);
                Assert.Empty(feedback);

                // A picked item that cannot become a filesystem path is an actionable error.
                RestoreBaseline();
                providerControl.Open = _ => new IStorageFile[]
                    { NewStorageFile(new Uri("https://example.invalid/startup-video.mp4")) };
                OnUi(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Assert.Equal(baseline, CoreSettings.Current.StartupVideoPath);
                Assert.Equal(Path.GetFileName(baseline), label.Text);
                Assert.Single(feedback);
                Assert.Equal(Loc.Get("title_select_startup_video"), feedback[0].Title);
                Assert.Equal(Loc.Get("msg_startup_video_requires_local_file"), feedback[0].Message);

                // Provider failure is also visible, without exposing an exception or path.
                RestoreBaseline();
                providerControl.Open = _ => throw new IOException("picker unavailable");
                OnUi(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Assert.Equal(baseline, CoreSettings.Current.StartupVideoPath);
                Assert.Equal(Path.GetFileName(baseline), label.Text);
                Assert.Single(feedback);
                Assert.Equal(Loc.Get("msg_startup_video_picker_failed"), feedback[0].Message);
            }
            finally
            {
                GeneralSettingsSection.PickerFeedbackOverride = previousFeedback;
                CoreSettings.ServiceProvider = null;
                CoreSettings.Current.StartupVideoPath = null;
                try { if (host is not null) OnUi(host.Close); } catch { }
                CoreSettings.ServiceProvider = previousProvider;
            }
        });
    }

    [Fact]
    public void AcceptedPickPersists_CancellationAndNonLocalPickDoNot_AndReseedKeepsTheStoredValue()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            // CorePaths.UserData is resolved once per process from CCP_USERDATA_DIR, so the
            // harness's owned profile is the only honest place to read the written settings.
            using var settingsScope = GeneralSettingsTestProfile.BeginSettingsScope();
            var profile = GeneralSettingsTestProfile.Root;
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
            }
        });
    }

    [Fact]
    public void SavingInOwnedProfileLeavesExternalSentinelUntouched()
    {
        var externalRoot = Path.Combine(Path.GetTempPath(),
            "ccp-general-settings-external-sentinel-" + Guid.NewGuid().ToString("N"));
        var sentinelPath = Path.Combine(externalRoot, "settings.json");
        const string sentinel = "do-not-touch";
        Directory.CreateDirectory(externalRoot);
        File.WriteAllText(sentinelPath, sentinel);

        try
        {
            using var settingsScope = GeneralSettingsTestProfile.BeginSettingsScope();
            var service = new SettingsService();
            service.Current.StartupVideoPath = Path.Combine(GeneralSettingsTestProfile.Root, "kept.mp4");
            service.SaveImmediate();

            Assert.Equal(GeneralSettingsTestProfile.Root, CorePaths.UserData);
            Assert.Equal(sentinel, File.ReadAllText(sentinelPath));
        }
        finally
        {
            Assert.Equal(sentinel, File.ReadAllText(sentinelPath));
            Directory.Delete(externalRoot, recursive: true);
        }
    }

    private sealed class FakeStorageProviderFactory : IStorageProviderFactory
    {
        private readonly IStorageProvider _provider;

        internal FakeStorageProviderFactory(IStorageProvider provider) => _provider = provider;

        public IStorageProvider CreateProvider(TopLevel topLevel) => _provider;
    }

    private static IDisposable BindStorageProvider(IStorageProvider provider)
    {
        // Avalonia 12 exposes the mutable locator from its runtime assembly, while its net8
        // reference facade omits it. Keep this test on the public IStorageProvider seam without
        // adding a production dependency on that facade-only mismatch.
        var locatorType = typeof(AvaloniaObject).Assembly.GetType("Avalonia.AvaloniaLocator")!;
        var locator = locatorType.GetProperty("CurrentMutable")!.GetValue(null)!;
        var scope = (IDisposable)locator.GetType().GetMethod("EnterScope")!.Invoke(locator, null)!;
        var bind = locator.GetType().GetMethods()
            .Single(method => method.Name == "Bind" && method.IsGenericMethodDefinition
                              && method.GetGenericArguments().Length == 1
                              && method.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(IStorageProviderFactory));
        var registration = bind.Invoke(locator, null)!;
        var toConstant = registration.GetType().GetMethods()
            .Single(method => method.Name == "ToConstant" && method.IsGenericMethodDefinition
                              && method.GetGenericArguments().Length == 1
                              && method.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(FakeStorageProviderFactory));
        toConstant.Invoke(registration, new object[] { new FakeStorageProviderFactory(provider) });
        return scope;
    }

    private static (IStorageProvider Provider, StorageProviderProxy Control) NewStorageProvider()
    {
        var provider = DispatchProxy.Create<IStorageProvider, StorageProviderProxy>();
        return (provider, (StorageProviderProxy)(object)provider);
    }

    private static IStorageFile NewStorageFile(Uri path)
    {
        var file = DispatchProxy.Create<IStorageFile, StorageFileProxy>();
        ((StorageFileProxy)(object)file).PathValue = path;
        return file;
    }

    // Avalonia marks its native storage interfaces NotClientImplementable. DispatchProxy still
    // lets this test feed the public interface to the real button handler without a second picker
    // abstraction or a dependency on a platform's file dialog.
    private class StorageProviderProxy : DispatchProxy
    {
        internal Func<FilePickerOpenOptions, IReadOnlyList<IStorageFile>> Open { get; set; } =
            _ => Array.Empty<IStorageFile>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_CanOpen" => true,
                "get_CanSave" => false,
                "get_CanPickFolder" => false,
                "OpenFilePickerAsync" => Task.FromResult(Open((FilePickerOpenOptions)args![0]!)),
                "TryGetFileFromPathAsync" => Task.FromResult<IStorageFile?>(null),
                "TryGetFolderFromPathAsync" => Task.FromResult<IStorageFolder?>(null),
                "TryGetWellKnownFolderAsync" => Task.FromResult<IStorageFolder?>(null),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }
    }

    private class StorageFileProxy : DispatchProxy
    {
        internal Uri PathValue { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_Name" => System.IO.Path.GetFileName(PathValue.LocalPath),
                "get_Path" => PathValue,
                "get_CanBookmark" => false,
                "Dispose" => null,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }
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
