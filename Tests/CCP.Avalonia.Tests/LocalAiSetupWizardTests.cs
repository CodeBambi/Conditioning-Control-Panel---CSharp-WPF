using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using Status = ConditioningControlPanel.Services.AIService.OllamaSetupService.InstallStatus;

namespace CCP.Avalonia.Tests;

/// <summary>dialogs-local-ai-setup-wizard: the wizard drives OllamaSetupService exactly like WPF
/// LocalAiSetupWizard.xaml.cs, with every service call faked (no real ollama, pull or network). Off
/// Windows it never downloads an installer: Continue re-runs detection.</summary>
public sealed class LocalAiSetupWizardTests
{
    private sealed class Fakes : IDisposable
    {
        private readonly (Delegate, Delegate, Delegate, Delegate, Delegate, Delegate, bool) _saved =
            (LocalAiSetupWizard.Detect, LocalAiSetupWizard.StartService, LocalAiSetupWizard.DownloadInstaller,
             LocalAiSetupWizard.RunInstaller, LocalAiSetupWizard.PullModel, LocalAiSetupWizard.SmokeTest,
             LocalAiSetupWizard.CanAutoInstall);
        public readonly List<string> Calls = new();
        public readonly Queue<Status> DetectResults = new();
        public bool ServiceStarts = true;
        public readonly TaskCompletionSource PullGate = new();
        public CancellationToken PullToken;

        public Fakes(bool canAutoInstall)
        {
            LocalAiSetupWizard.CanAutoInstall = canAutoInstall;
            LocalAiSetupWizard.Detect = (model, _) =>
            {
                Calls.Add("detect:" + model);
                var s = DetectResults.Count > 0 ? DetectResults.Dequeue() : Status.NotInstalled;
                return Task.FromResult(new OllamaSetupService.StatusSnapshot { Status = s, TargetModelInstalled = s == Status.Ready });
            };
            LocalAiSetupWizard.StartService = _ => { Calls.Add("start"); return Task.FromResult(ServiceStarts); };
            LocalAiSetupWizard.DownloadInstaller = (p, _) =>
            {
                Calls.Add("download");
                p.Report(new OllamaSetupService.DownloadProgress { BytesReceived = 512, TotalBytes = 1024 });
                return Task.FromResult("/nonexistent/OllamaSetup.exe");
            };
            LocalAiSetupWizard.RunInstaller = (path, _) => { Calls.Add("install:" + path); return Task.FromResult(true); };
            LocalAiSetupWizard.PullModel = async (model, p, ct) =>
            {
                Calls.Add("pull:" + model);
                PullToken = ct;
                p.Report(new OllamaSetupService.PullProgress { Status = "pulling layer", Total = 2048, Completed = 1024 });
                await PullGate.Task.WaitAsync(ct);
            };
            LocalAiSetupWizard.SmokeTest = (model, _) =>
            {
                Calls.Add("smoke:" + model);
                return Task.FromResult((true, TimeSpan.FromSeconds(3), "hi"));
            };
        }

        public void Dispose()
        {
            LocalAiSetupWizard.Detect = (Func<string, CancellationToken, Task<OllamaSetupService.StatusSnapshot>>)_saved.Item1;
            LocalAiSetupWizard.StartService = (Func<CancellationToken, Task<bool>>)_saved.Item2;
            LocalAiSetupWizard.DownloadInstaller = (Func<IProgress<OllamaSetupService.DownloadProgress>, CancellationToken, Task<string>>)_saved.Item3;
            LocalAiSetupWizard.RunInstaller = (Func<string, CancellationToken, Task<bool>>)_saved.Item4;
            LocalAiSetupWizard.PullModel = (Func<string, IProgress<OllamaSetupService.PullProgress>, CancellationToken, Task>)_saved.Item5;
            LocalAiSetupWizard.SmokeTest = (Func<string, CancellationToken, Task<(bool ok, TimeSpan elapsed, string reply)>>)_saved.Item6;
            LocalAiSetupWizard.CanAutoInstall = _saved.Item7;
        }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.CompanionPrompt.AiProvider = AiProviderType.Cloud;
        CoreSettings.Current.CompanionPrompt.AiModel = "test-model:1b";
    }

    private static void Pump()
    {
        for (int i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
    }

    private static T C<T>(Window w, string name) where T : Control => w.FindControl<T>(name)!;

    private static void Click(Window w, string name)
    {
        C<Button>(w, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    [Fact]
    public Task LinuxNotInstalledShowsManualInstallAndContinueReDetectsWithoutDownloading() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var fakes = new Fakes(canAutoInstall: false);
        var w = new LocalAiSetupWizard();
        try
        {
            w.Show();
            Pump();
            Assert.True(C<StackPanel>(w, "PanelConsent").IsVisible);
            Assert.Equal(Loc.Get("label_local_ai_consent_install_ollama_manual"), C<TextBlock>(w, "TxtConsentLine1").Text);
            Assert.Equal(Loc.Get("label_local_ai_consent_disk_note_linux"), C<TextBlock>(w, "TxtConsentDiskNote").Text);
            Assert.True(C<TextBlock>(w, "LinkManualInstall").Focusable);
            // Keyboard reach like the WPF Hyperlink: Enter on the focused link takes the open path. The test
            // profile is sandboxed, so ExternalOpener refuses ollama.com and no browser starts.
            var opened = new List<string>();
            var shell = ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell;
            ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = t => { opened.Add(t); return true; };
            var enter = new global::Avalonia.Input.KeyEventArgs
                { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter };
            try { C<TextBlock>(w, "LinkManualInstall").RaiseEvent(enter); }
            finally { ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = shell; }
            Assert.True(enter.Handled);
            Assert.Empty(opened);

            // Continue while Ollama is still missing says so instead of silently showing Consent again.
            Click(w, "BtnPrimary");
            Assert.True(C<StackPanel>(w, "PanelError").IsVisible);
            Assert.Equal(Loc.Get("error_local_ai_not_found_linux"), C<TextBox>(w, "TxtErrorDetail").Text);
            Click(w, "BtnPrimary"); // Retry: still missing -> back to Consent
            Assert.True(C<StackPanel>(w, "PanelConsent").IsVisible);

            // The user installed Ollama meanwhile: Continue detects it running without the model.
            fakes.DetectResults.Enqueue(Status.RunningNoModel);
            Click(w, "BtnPrimary");
            Assert.Equal(new[] { "detect:test-model:1b", "detect:test-model:1b", "detect:test-model:1b", "detect:test-model:1b", "pull:test-model:1b" }, fakes.Calls);
            Assert.True(C<StackPanel>(w, "PanelPullModel").IsVisible);
        }
        finally { w.Close(); CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WindowsPathInstallsPullsSmokeTestsAndSavesLocal() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var fakes = new Fakes(canAutoInstall: true);
        var w = new LocalAiSetupWizard();
        try
        {
            w.Show();
            Pump();
            Assert.Equal(Loc.Get("label_local_ai_consent_install_ollama"), C<TextBlock>(w, "TxtConsentLine1").Text);
            Click(w, "BtnPrimary");
            Assert.Equal(new[] { "detect:test-model:1b", "download", "install:/nonexistent/OllamaSetup.exe", "pull:test-model:1b" }, fakes.Calls);
            Assert.Equal(Loc.GetF("label_local_ai_pulling_model_named", "test-model:1b"), C<TextBlock>(w, "TxtPullHeader").Text);
            Assert.Equal("pulling layer", C<TextBlock>(w, "TxtPullStatus").Text);
            Assert.EndsWith("(50%)", C<TextBlock>(w, "TxtPullDetail").Text);
            Assert.Equal(AiProviderType.Cloud, CoreSettings.Current.CompanionPrompt.AiProvider);

            fakes.PullGate.SetResult();
            Pump();
            Assert.True(C<StackPanel>(w, "PanelDone").IsVisible);
            Assert.Equal(Loc.GetF("label_local_ai_done_detail", "test-model:1b", 3), C<TextBlock>(w, "TxtDoneDetail").Text);
            Assert.True(w.LocalAiReady);
            Assert.Equal(AiProviderType.Local, CoreSettings.Current.CompanionPrompt.AiProvider);
        }
        finally { w.Close(); CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ServiceStartFailureShowsErrorAndCancelDuringPullStopsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var fakes = new Fakes(canAutoInstall: false) { ServiceStarts = false };
        fakes.DetectResults.Enqueue(Status.InstalledNotRunning);
        var w = new LocalAiSetupWizard();
        try
        {
            w.Show();
            Pump();
            Assert.True(C<StackPanel>(w, "PanelError").IsVisible);
            Assert.Equal(Loc.Get("error_local_ai_start_service_failed_linux"), C<TextBox>(w, "TxtErrorDetail").Text);

            // Retry re-detects; the service now comes up and the model is missing -> pull.
            fakes.ServiceStarts = true;
            fakes.DetectResults.Enqueue(Status.InstalledNotRunning);
            fakes.DetectResults.Enqueue(Status.RunningNoModel);
            Click(w, "BtnPrimary");
            Assert.True(C<StackPanel>(w, "PanelPullModel").IsVisible);

            Click(w, "BtnSecondary");
            Assert.True(fakes.PullToken.IsCancellationRequested);
            Assert.False(w.IsVisible);
            Assert.False(w.LocalAiReady);
            Assert.Equal(AiProviderType.Cloud, CoreSettings.Current.CompanionPrompt.AiProvider);
        }
        finally { w.Close(); CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });
}
