using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.Report.Tests;

internal static class TestProfile
{
    internal static readonly string DirectoryPath = Path.Combine(
        Path.GetTempPath(), "ccp-report-" + Guid.NewGuid().ToString("N"));

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(DirectoryPath);
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", DirectoryPath);
    }
}

/// <summary>
/// Process-isolated, headless coverage for the real report dialog. HttpMessageHandler is the only
/// transport seam: every request stays in memory and still travels through BugReportService.
/// </summary>
public sealed class BugReportWorkflowTests
{
    [Fact]
    public async Task PreviewIsTheSubmittedSnapshot_And200ShowsToken()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var version = 1;
            var oldTail = BugReportService.DiagnosticTailProvider;
            BugReportService.DiagnosticTailProvider = _ => "diagnostic-snapshot-" + version;
            var handler = new RecordingHandler(_ => Task.FromResult(Response(HttpStatusCode.OK,
                "{\"token\":\"BUG-2000000001\"}")));
            var (owner, dialog, closed) = OpenDialog(new BugReportService(handler), ReportKind.Bug);
            try
            {
                var description = dialog.FindControl<TextBox>("TxtDescription")!;
                var includeLog = dialog.FindControl<CheckBox>("ChkIncludeAppLog")!;
                description.Text = "A real pointer-driven report";
                includeLog.IsChecked = true;
                var preview = dialog.FindControl<TextBox>("TxtPreview")!.Text;
                Assert.Contains("diagnostic-snapshot-1", preview);
                Assert.False(dialog.FindControl<Button>("BtnSend")!.IsEnabled);

                // The two-second delay is a real DispatcherTimer, not a test-only enable hook.
                Click(dialog, dialog.FindControl<Button>("BtnSend")!);
                await Task.Delay(100);
                Assert.Equal(0, handler.Count);

                version = 2; // must not leak into the already displayed/consented draft
                await WaitUntilAsync(() => dialog.FindControl<Button>("BtnSend")!.IsEnabled);
                Click(dialog, dialog.FindControl<Button>("BtnSend")!);
                await WaitUntilAsync(() => handler.Count == 1);
                await WaitUntilAsync(() => dialog.FindControl<Border>("SuccessPanel")!.IsVisible);

                Assert.DoesNotContain("diagnostic-snapshot-2", handler.LastBody);
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(preview!), JsonNode.Parse(handler.LastBody)),
                    "the exact preview payload must be the payload submitted");
                Assert.Equal("BUG-2000000001", dialog.FindControl<TextBox>("TxtSuccessToken")!.Text);
                Assert.True(dialog.FindControl<Grid>("SuccessTokenRow")!.IsVisible);
            }
            finally
            {
                BugReportService.DiagnosticTailProvider = oldTail;
                Close(owner, dialog);
                await closed;
            }
        });
    }

    [Fact]
    public async Task SuggestionOmitsLogsAndSteps_AndUsesSuggestionMarker()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var callbackCount = 0;
            var oldTail = BugReportService.DiagnosticTailProvider;
            BugReportService.DiagnosticTailProvider = _ => { callbackCount++; return "must-not-leave"; };
            var handler = new RecordingHandler(_ => Task.FromResult(Response(HttpStatusCode.OK,
                "{\"token\":\"BUG-SUGGEST001\"}")));
            var (owner, dialog, closed) = OpenDialog(new BugReportService(handler), ReportKind.Suggestion);
            try
            {
                Assert.False(dialog.FindControl<TextBox>("TxtSteps")!.IsVisible);
                Assert.False(dialog.FindControl<CheckBox>("ChkIncludeAppLog")!.IsVisible);
                Assert.False(dialog.FindControl<TextBlock>("TxtScrubberCounts")!.IsVisible);
                dialog.FindControl<TextBox>("TxtDescription")!.Text = "Add a softer landing page";
                await WaitUntilAsync(() => dialog.FindControl<Button>("BtnSend")!.IsEnabled);
                Click(dialog, dialog.FindControl<Button>("BtnSend")!);
                await WaitUntilAsync(() => handler.Count == 1);
                await WaitUntilAsync(() => dialog.FindControl<Border>("SuccessPanel")!.IsVisible);

                using var payload = JsonDocument.Parse(handler.LastBody);
                var root = payload.RootElement;
                Assert.StartsWith("[SUGGESTION] ", root.GetProperty("description").GetString());
                Assert.Equal(string.Empty, root.GetProperty("steps").GetString());
                Assert.Equal(string.Empty, root.GetProperty("crash_log").GetString());
                Assert.Equal(string.Empty, root.GetProperty("app_log").GetString());
                Assert.Equal(0, callbackCount);
                Assert.Contains("idea", dialog.FindControl<TextBlock>("TxtSuccessHeadline")!.Text,
                    StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                BugReportService.DiagnosticTailProvider = oldTail;
                Close(owner, dialog);
                await closed;
            }
        });
    }

    [Fact]
    public async Task SavedPendingWithoutTokenShowsNoEmptyPlaceholder()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var handler = new RecordingHandler(_ => Task.FromResult(Response(HttpStatusCode.Accepted, "{}")));
            var (owner, dialog, closed) = OpenDialog(new BugReportService(handler), ReportKind.Bug);
            try
            {
                await WaitUntilAsync(() => dialog.FindControl<Button>("BtnSend")!.IsEnabled);
                Click(dialog, dialog.FindControl<Button>("BtnSend")!);
                await WaitUntilAsync(() => handler.Count == 1);
                await WaitUntilAsync(() => dialog.FindControl<Border>("SuccessPanel")!.IsVisible);

                Assert.False(dialog.FindControl<Grid>("SuccessTokenRow")!.IsVisible);
                Assert.False(dialog.FindControl<TextBlock>("LblSuccessTokenLabel")!.IsVisible);
                Assert.DoesNotContain("()", dialog.FindControl<TextBlock>("TxtSuccessHeadline")!.Text);
                Assert.Contains("saved", dialog.FindControl<TextBlock>("TxtSuccessHeadline")!.Text,
                    StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Close(owner, dialog);
                await closed;
            }
        });
    }

    [Fact]
    public async Task ValidationFailureShowsRealError_AllowsRetry_AndNetworkFailureIsTyped()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var handler = new RecordingHandler(_ => Task.FromResult(Response(HttpStatusCode.BadRequest, "invalid")));
            // The response sequence stays in-memory while exercising the real retry path.
            handler.Response = _ => Task.FromResult(handler.Count == 1
                ? Response(HttpStatusCode.BadRequest, "invalid")
                : Response(HttpStatusCode.OK, "{\"token\":\"BUG-RETRY00001\"}"));
            var (owner, dialog, closed) = OpenDialog(new BugReportService(handler), ReportKind.Bug);
            try
            {
                await WaitUntilAsync(() => dialog.FindControl<Button>("BtnSend")!.IsEnabled);
                Click(dialog, dialog.FindControl<Button>("BtnSend")!);
                var error = await WaitForOwnedAsync<MessageDialog>(dialog);
                Assert.Contains("Could not send", error.FindControl<TextBlock>("TxtMessage")!.Text);
                Assert.Contains("HTTP 400", error.FindControl<TextBlock>("TxtMessage")!.Text);
                Click(error, error.FindControl<Button>("BtnOk")!);
                await WaitUntilAsync(() => dialog.FindControl<Button>("BtnSend")!.IsEnabled);

                Click(dialog, dialog.FindControl<Button>("BtnSend")!);
                await WaitUntilAsync(() => handler.Count == 2);
                await WaitUntilAsync(() => dialog.FindControl<Border>("SuccessPanel")!.IsVisible);
                Assert.Equal("BUG-RETRY00001", dialog.FindControl<TextBox>("TxtSuccessToken")!.Text);
            }
            finally
            {
                Close(owner, dialog);
                await closed;
            }

            var network = new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("offline-test")));
            var result = await new BugReportService(network)
                .SubmitAsync(new BugReportService(network).CreateDraft("network", "", false));
            Assert.Equal(BugReportService.SubmitOutcome.NetworkError, result.Outcome);
            Assert.Contains("offline-test", result.ErrorMessage);
        });
    }

    [Fact]
    public async Task DuplicateInFlightClickIsIgnored_AndCloseStopsTimerAndLateUiWork()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new RecordingHandler(_ => release.Task);
            var (owner, dialog, closed) = OpenDialog(new BugReportService(handler), ReportKind.Bug);
            try
            {
                var send = dialog.FindControl<Button>("BtnSend")!;
                await WaitUntilAsync(() => send.IsEnabled);
                Click(dialog, send);
                await WaitUntilAsync(() => handler.Count == 1);
                Assert.False(send.IsEnabled);
                Assert.False(dialog.FindControl<Button>("BtnCancel")!.IsEnabled);
                Click(dialog, send); // disabled pointer path must not create a second request
                Assert.Equal(1, handler.Count);

                dialog.Close();
                Dispatcher.UIThread.RunJobs();
                release.SetResult(Response(HttpStatusCode.OK, "{\"token\":\"BUG-LATE000001\"}"));
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
                Assert.False(dialog.FindControl<Border>("SuccessPanel")!.IsVisible,
                    "a completed request must not repaint a closed window");
            }
            finally
            {
                release.TrySetResult(Response(HttpStatusCode.OK, "{\"token\":\"BUG-LATE000001\"}"));
                Close(owner, dialog);
                await closed;
            }

            var (timerOwner, timerDialog, timerClosed) = OpenDialog(
                new BugReportService(new RecordingHandler(_ => Task.FromResult(Response(HttpStatusCode.OK, "{}")))),
                ReportKind.Bug);
            try
            {
                var send = timerDialog.FindControl<Button>("BtnSend")!;
                timerDialog.Close();
                await timerClosed;
                await Task.Delay(2200);
                Assert.False(send.IsEnabled, "the enable timer was not allowed to outlive the window");
            }
            finally
            {
                Close(timerOwner, timerDialog);
                await timerClosed;
            }
        });
    }

    [Fact]
    public async Task AppInfoButtonsOpenSuggestionBugAndRecentReportModals()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var owner = new Window { Width = 900, Height = 500 };
            var appInfo = new AppInfoFeatureControl();
            owner.Content = appInfo;
            owner.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Click(owner, appInfo.FindControl<Button>("BtnSuggestion")!);
                var suggestion = await WaitForOwnedAsync<BugReportWindow>(owner);
                Assert.Contains("Suggest", suggestion.Title, StringComparison.OrdinalIgnoreCase);
                suggestion.Close();
                await WaitUntilAsync(() => !suggestion.IsVisible);

                Click(owner, appInfo.FindControl<Button>("BtnReportBug")!);
                var bug = await WaitForOwnedAsync<BugReportWindow>(owner);
                Assert.Contains("Bug", bug.Title, StringComparison.OrdinalIgnoreCase);
                bug.Close();
                await WaitUntilAsync(() => !bug.IsVisible);

                Click(owner, appInfo.FindControl<Button>("BtnMyReports")!);
                var reports = await WaitForOwnedAsync<MyReportsWindow>(owner);
                reports.Close();
                await WaitUntilAsync(() => !reports.IsVisible);
            }
            finally
            {
                foreach (var child in owner.OwnedWindows.ToArray()) child.Close();
                owner.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public async Task ShellTitleAndHelpButtonsOpenOwnedReportModals()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var settings = new SettingsService();
            settings.RestoreFrom(new AppSettings { Welcomed = true, Language = "en" });
            var oldSettings = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => settings;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                Click(shell, shell.FindControl<Button>("BtnTitleBarBugReport")!);
                var titleDialog = await WaitForOwnedAsync<BugReportWindow>(shell);
                Assert.Same(shell, titleDialog.Owner);
                titleDialog.Close();
                await WaitUntilAsync(() => !titleDialog.IsVisible);

                Click(shell, shell.FindControl<Button>("BtnMainHelp")!);
                var tutorialOverlay = shell.Named<Border>("MainTutorialOverlay")!;
                Assert.True(tutorialOverlay.IsVisible);
                var tutorialScroll = shell.Named<ScrollViewer>("MainTutorialScroll")!;
                tutorialScroll.Offset = new Vector(0, double.MaxValue);
                Dispatcher.UIThread.RunJobs();
                Click(shell, shell.FindControl<Button>("BtnTutorialReportBug")!);
                var helpDialog = await WaitForOwnedAsync<BugReportWindow>(shell);
                Assert.False(tutorialOverlay.IsVisible);
                Assert.Same(shell, helpDialog.Owner);
                helpDialog.Close();
                await WaitUntilAsync(() => !helpDialog.IsVisible);
            }
            finally
            {
                foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
                shell.Close();
                Dispatcher.UIThread.RunJobs();
                CoreSettings.ServiceProvider = oldSettings;
            }
        });
    }

    private static void EnsureAvalonia()
    {
        Assert.True(AvaloniaTestDispatcher.IsDispatcherThread);
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    private static (Window Owner, BugReportWindow Dialog, Task<bool?> Closed) OpenDialog(
        BugReportService service, ReportKind kind)
    {
        var owner = new Window { Width = 900, Height = 700 };
        owner.Show();
        var dialog = new BugReportWindow(kind, service);
        var closed = dialog.ShowDialog<bool?>(owner);
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.IsVisible, "report dialog did not open");
        return (owner, dialog, closed);
    }

    private static void Close(Window owner, Window dialog)
    {
        foreach (var child in dialog.OwnedWindows.ToArray()) child.Close();
        if (dialog.IsVisible) dialog.Close();
        foreach (var child in owner.OwnedWindows.ToArray()) child.Close();
        if (owner.IsVisible) owner.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Window root, Button button)
    {
        Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0,
            $"{button.Name ?? "button"} was not laid out");
        var point = button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), root);
        Assert.True(point.HasValue, "could not translate pointer into root window");
        root.MouseMove(point!.Value, RawInputModifiers.None);
        root.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        root.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<T> WaitForOwnedAsync<T>(Window owner) where T : Window
    {
        T? found = null;
        await WaitUntilAsync(() =>
        {
            found = owner.OwnedWindows.OfType<T>().SingleOrDefault(w => w.IsVisible);
            return found is not null;
        });
        return found!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.True(condition(), "condition did not become true");
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private int _count;
        private readonly List<string> _bodies = new();

        internal Func<HttpRequestMessage, Task<HttpResponseMessage>> Response { get; set; }
        internal int Count => Volatile.Read(ref _count);
        internal string LastBody { get { lock (_gate) return _bodies[^1]; } }

        internal RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) => Response = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_gate) _bodies.Add(body);
            Interlocked.Increment(ref _count);
            return await Response(request);
        }
    }
}
