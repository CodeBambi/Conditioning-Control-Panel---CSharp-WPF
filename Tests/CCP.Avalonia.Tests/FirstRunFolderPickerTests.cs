using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// First-run wizard "Choose a content folder": the picker opens on the click itself, owned by the
/// wizard (not deferred until the wizard closes, which a user read as "does nothing"), and the
/// folder it returns is applied like the shell's picker and shown on the button.
/// </summary>
public sealed class FirstRunFolderPickerTests
{
    [Fact]
    public Task WelcomeButtonOpensThePickerOnTheWizardAndAppliesTheFolder() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureAvalonia();
        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        var chosen = Path.Combine(TestUserDataProfile.Root, "my-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chosen);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;

        var provider = DispatchProxy.Create<IStorageProvider, PickerProxy>();
        var picker = (PickerProxy)(object)provider;
        picker.Result = NewFolder(new Uri(chosen));
        var factory = new Factory(provider);
        FirstRunWizard? wizard = null;
        try
        {
            using var scope = BindStorageProvider(factory);
            wizard = new FirstRunWizard();
            wizard.Show();
            Dispatcher.UIThread.RunJobs();

            var button = wizard.FindControl<Button>("BtnPickFolder")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // Synchronously, before anything closes: the picker has been asked, on the wizard.
            Assert.Equal(1, picker.Calls);
            Assert.Equal("Select a folder for your custom assets (images and videos)", picker.Title);
            Assert.Same(wizard, factory.Owner);

            // The shell's follow-ups: settings written, images/ and videos/ made, then the
            // "folder set" dialog owned by the wizard. Dismiss it and the button shows the folder.
            await WaitFor(() => wizard.OwnedWindows.Count > 0);
            Assert.Equal(chosen, CoreSettings.Current.CustomAssetsPath);
            Assert.True(Directory.Exists(Path.Combine(chosen, "images")));
            Assert.True(Directory.Exists(Path.Combine(chosen, "videos")));
            wizard.OwnedWindows[0].Close();
            await WaitFor(() => button.IsEnabled);
            Assert.Equal(chosen, wizard.FindControl<TextBlock>("TxtPickFolder")!.Text);
            await WaitFor(() => ReadShared(settingsPath).Contains(Path.GetFileName(chosen)));
        }
        finally
        {
            wizard?.Close();
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
    });

    // Windows refuses an open that lands mid-publish (sharing violation) where Linux does not, and a
    // reader holding the file can fail the writer's replace, so open only when the write time moved
    // (metadata, no handle) and count a refused open as "not yet".
    private static DateTime _seen;
    private static string _last = "";
    private static string ReadShared(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);   // a fixed sentinel while the file is absent
        if (stamp == _seen) return _last;
        try { _last = File.Exists(path) ? File.ReadAllText(path) : ""; _seen = stamp; }
        catch (IOException) { }
        return _last;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Assert.True(condition(), "timed out");
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    private sealed class Factory(IStorageProvider provider) : IStorageProviderFactory
    {
        internal TopLevel? Owner;
        public IStorageProvider CreateProvider(TopLevel topLevel) { Owner = topLevel; return provider; }
    }

    // Same locator binding as GeneralStartupVideoTests: Avalonia 12's net8 facade omits it.
    private static IDisposable BindStorageProvider(IStorageProviderFactory factory)
    {
        var locatorType = typeof(AvaloniaObject).Assembly.GetType("Avalonia.AvaloniaLocator")!;
        var locator = locatorType.GetProperty("CurrentMutable")!.GetValue(null)!;
        var scope = (IDisposable)locator.GetType().GetMethod("EnterScope")!.Invoke(locator, null)!;
        var registration = locator.GetType().GetMethods()
            .Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(IStorageProviderFactory)).Invoke(locator, null)!;
        registration.GetType().GetMethods()
            .Single(m => m.Name == "ToConstant" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(factory.GetType()).Invoke(registration, new object[] { factory });
        return scope;
    }

    private static IStorageFolder NewFolder(Uri path)
    {
        var folder = DispatchProxy.Create<IStorageFolder, FolderProxy>();
        ((FolderProxy)(object)folder).PathValue = path;
        return folder;
    }

    // Avalonia's storage interfaces are NotClientImplementable; DispatchProxy feeds them anyway.
    private class PickerProxy : DispatchProxy
    {
        internal IStorageFolder? Result;
        internal int Calls;
        internal string? Title;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_CanPickFolder": return true;
                case "TryGetFolderFromPathAsync": return Task.FromResult<IStorageFolder?>(null);
                case "OpenFolderPickerAsync":
                    Calls++;
                    Title = ((FolderPickerOpenOptions)args![0]!).Title;
                    return Task.FromResult<IReadOnlyList<IStorageFolder>>(new[] { Result! });
                default: throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    private class FolderProxy : DispatchProxy
    {
        internal Uri PathValue = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Name" => Path.GetFileName(PathValue.LocalPath),
            "get_Path" => PathValue,
            "get_CanBookmark" => false,
            "Dispose" => null,
            _ => throw new NotSupportedException(targetMethod?.Name),
        };
    }
}
