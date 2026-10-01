using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>This host is a CCP_USERDATA_DIR sandbox (TestUserDataProfile): a real site is never handed to the shell,
/// loopback and local folders still are. The shell seam records instead of launching anything.</summary>
[Collection("ExternalOpener")]
public sealed class ExternalOpenerTests
{
    [Theory]
    [InlineData("https://app.cclabs.app/policies/prohibited-content", false)]
    [InlineData("https://discord.gg/example", false)]
    [InlineData("http://loopback:3001/", false)]
    [InlineData("http://127.0.0.1:3001/auth", true)]
    [InlineData("http://localhost:3001/", true)]
    public async Task OnlyLoopbackOrLocalReachesTheShell(string target, bool opened)
    {
        var launched = new List<string>();
        var previous = ExternalOpener.Shell;
        ExternalOpener.Shell = t => { launched.Add(t); return true; };
        try
        {
            Assert.Equal(opened, ExternalOpener.Open(target));
            Assert.Equal(opened, await ExternalOpener.OpenAsync(null, target));
            Assert.Equal(opened ? 2 : 0, launched.Count);
        }
        finally { ExternalOpener.Shell = previous; }
    }

    [Fact]
    public void LocalFoldersStillOpen()
    {
        var launched = new List<string>();
        var previous = ExternalOpener.Shell;
        ExternalOpener.Shell = t => { launched.Add(t); return true; };
        try { Assert.True(ExternalOpener.Open(Path.GetTempPath())); }
        finally { ExternalOpener.Shell = previous; }
        Assert.Equal(new[] { Path.GetTempPath() }, launched);
    }

    [Theory]
    [InlineData("relative/notes.txt", false, true)]        // production: the shell decides, as before
    [InlineData("mailto:someone@example.com", false, true)]
    [InlineData("https://app.cclabs.app/", false, true)]
    [InlineData(@"\\server\share\x.txt", true, false)]   // sandbox: a UNC path is a network share
    [InlineData("//server/share/x.txt", true, false)]
    [InlineData("file://server/share/x.txt", true, false)]
    [InlineData("file:///tmp/x.txt", true, true)]
    [InlineData("/tmp", true, true)]
    [InlineData("relative/notes.txt", true, false)]
    public void SandboxRefusesSharesAndProductionKeepsTheShell(string target, bool sandboxed, bool allowed) =>
        Assert.Equal(allowed, ExternalOpener.Allowed(target, sandboxed));

    /// <summary>A click on a SafeHyperlinkButton launches once, through ExternalOpener, never through the base
    /// HyperlinkButton's own launcher call. The window's platform impl is wrapped so TopLevel.Launcher is a counter.</summary>
    [Fact]
    public async Task SafeHyperlinkButtonLaunchesOnlyThroughExternalOpener()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var link = new SafeHyperlinkButton
            {
                NavigateUri = new Uri("http://127.0.0.1:9/page"),   // loopback: allowed in this sandbox
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
                Content = "link",
            };
            var w = new Window { Width = 200, Height = 100, Content = link };
            w.Show();
            var field = typeof(TopLevel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(f => f.FieldType == typeof(ITopLevelImpl));
            var real = field.GetValue(w)!;
            var launcher = new CountingLauncher();
            var proxy = DispatchProxy.Create<IWindowImpl, LauncherSwap>();
            ((LauncherSwap)(object)proxy).Inner = real;
            ((LauncherSwap)(object)proxy).Launcher = launcher;
            field.SetValue(w, proxy);
            try
            {
                Assert.Same(launcher, w.Launcher);
                // The automation Invoke is a real click (Button.OnClick); headless mouse input needs the unwrapped impl.
                ((global::Avalonia.Automation.Provider.IInvokeProvider)
                    global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(link)).Invoke();
                for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
                Assert.Equal(new[] { new Uri("http://127.0.0.1:9/page") }, launcher.Uris);
                Assert.True(link.IsVisited);
                Assert.Equal(new Uri("http://127.0.0.1:9/page"), link.NavigateUri);   // restored after the click
            }
            finally { field.SetValue(w, real); w.Close(); }
        });
    }

    private sealed class CountingLauncher : ILauncher
    {
        public readonly List<Uri> Uris = new();
        public Task<bool> LaunchUriAsync(Uri uri) { Uris.Add(uri); return Task.FromResult(true); }
        public Task<bool> LaunchFileAsync(IStorageItem storageItem) => Task.FromResult(false);
    }

    public class LauncherSwap : DispatchProxy
    {
        internal object Inner = null!;
        internal ILauncher Launcher = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "TryGetFeature" && args is [Type t] && t == typeof(ILauncher)) return Launcher;
            try { return method.Invoke(Inner, args); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }
    }
}
