using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralHelpPopoverTests
{
    // DIAGNOSTIC TRIAL ONLY: the away-back variant is owner-authorized probe input, not shipped behavior.
    private const string InputVariantEnvironment = "CCP_SPIRAL_HELP_INPUT_VARIANT";
    private const string TracePathEnvironment = "CCP_SPIRAL_HELP_INPUT_TRACE";

    private static bool UseAwayBackVariant =>
        string.Equals(
            Environment.GetEnvironmentVariable(InputVariantEnvironment),
            "away-back",
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task MountedSpiralHelpUsesLocalizedTopicAndCleansUpAcrossVisibilityAndReload()
    {
        var localization = LocalizationManager.Instance;
        var previousLanguage = localization.CurrentLanguage;
        // EnsureAvalonia initializes Avalonia and normalizes the language, so capture/restore surrounds it.
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            Window? host = null;
            SpiralTabView? view = null;
            Button? button = null;
            PointerTrace? trace = null;
            EventHandler<PointerEventArgs>? pointerEntered = null;
            EventHandler<PointerEventArgs>? pointerExited = null;

            try
            {
                localization.SetLanguage("fr");
                var expected = new[]
                {
                    Loc.Get("help_descent_title"),
                    Loc.Get("help_descent_what"),
                    Loc.Get("help_descent_tip_1"),
                    Loc.Get("help_descent_tip_2"),
                    Loc.Get("help_descent_tip_3"),
                    Loc.Get("help_descent_how"),
                };

                view = new SpiralTabView { Width = 980, Height = 620 };
                button = view.FindControl<Button>("BtnSpiralHelp");
                Assert.NotNull(button);
                host = new Window
                {
                    Width = 980,
                    Height = 620,
                    Content = view,
                };

                trace = PointerTrace.Start();
                pointerEntered = (_, e) => trace.RecordPointerEvent("PointerEntered", e, host, view, button);
                pointerExited = (_, e) => trace.RecordPointerEvent("PointerExited", e, host, view, button);
                button.PointerEntered += pointerEntered;
                button.PointerExited += pointerExited;
                trace.State("constructed", host, view, button);

                host.Show();
                Dispatcher.UIThread.RunJobs();
                trace.State("after-host-show", host, view, button);
                Assert.True(view.IsShowingSpiral, "the mounted view did not enter its current spiral presentation path");
                Assert.True(button!.IsVisible);

                Move(host, button);
                trace.State("after-initial-move", host, view, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                trace.State("after-initial-delay", host, view, button);
                Assert.True(HelpPopover.IsOpen(button));
                var firstPopup = HelpPopover.PopupContent(button);
                Assert.NotNull(firstPopup);
                var firstTexts = PopupTexts(firstPopup!);
                foreach (var text in expected) Assert.Contains(text, firstTexts);
                Assert.DoesNotContain("help_descent_title", firstTexts);

                // Re-entry uses the real current view entrypoint. A repaint must not replace an open
                // attachment or create a second popup.
                view.OnTabShown();
                Dispatcher.UIThread.RunJobs();
                trace.State("after-tab-shown-open", host, view, button);
                Assert.True(HelpPopover.IsOpen(button));
                Assert.Same(firstPopup, HelpPopover.PopupContent(button));

                // The real fog predicate is still WPF-only on this branch. This presentation-only
                // invocation exercises the existing false gate without inventing a state setter.
                var applyHelp = typeof(SpiralTabView).GetMethod(
                    "ApplyHelpChip", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(applyHelp);
                applyHelp!.Invoke(view, new object[] { false });
                Dispatcher.UIThread.RunJobs();
                trace.State("after-gate-false", host, view, button);
                Assert.False(button.IsVisible);
                Assert.False(HelpPopover.IsOpen(button));
                Assert.Null(HelpPopover.PopupContent(button));

                // The normal entrypoint reattaches the topic after the gate opens again.
                view.OnTabShown();
                Dispatcher.UIThread.RunJobs();
                trace.State("after-gate-reopen", host, view, button);
                Assert.True(button.IsVisible);
                host.MouseMove(new Point(1, 1), RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                trace.State("after-existing-gate-away-move", host, view, button);
                Move(host, button);
                trace.State("after-existing-gate-move", host, view, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                trace.State("after-gate-reopen-delay", host, view, button);
                Assert.True(HelpPopover.IsOpen(button));
                Assert.NotSame(firstPopup, HelpPopover.PopupContent(button));

                // The tab's own hidden lifecycle closes and clears an open card.
                view.IsVisible = false;
                Dispatcher.UIThread.RunJobs();
                trace.State("after-view-hide", host, view, button);
                Assert.False(button.IsVisible);
                Assert.False(HelpPopover.IsOpen(button));
                Assert.Null(HelpPopover.PopupContent(button));
                view.IsVisible = true;
                Dispatcher.UIThread.RunJobs();
                trace.State("after-view-show", host, view, button);
                if (UseAwayBackVariant)
                {
                    host.MouseMove(new Point(1, 1), RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                    trace.State("after-diagnostic-away-move", host, view, button);
                }
                Move(host, button);
                trace.State("after-failing-stage-move", host, view, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                trace.State("after-failing-stage-delay", host, view, button);
                Assert.True(HelpPopover.IsOpen(button));

                // Unload/reload must close the old popup and reattach a fresh localized one.
                host.Content = null;
                Dispatcher.UIThread.RunJobs();
                trace.State("after-unload", host, view, button);
                Assert.False(HelpPopover.IsOpen(button));
                Assert.Null(HelpPopover.PopupContent(button));
                host.Content = view;
                Dispatcher.UIThread.RunJobs();
                trace.State("after-reload", host, view, button);
                Assert.True(button.IsVisible);
                Move(host, button);
                trace.State("after-reload-move", host, view, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                trace.State("after-reload-delay", host, view, button);
                Assert.True(HelpPopover.IsOpen(button));
                var reloadedPopup = HelpPopover.PopupContent(button);
                Assert.NotNull(reloadedPopup);
                foreach (var text in expected) Assert.Contains(text, PopupTexts(reloadedPopup!));

                // Save the open, mounted Spiral card, not just its chip.
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
                Dispatcher.UIThread.RunJobs();
                var frame = host.GetLastRenderedFrame();
                Assert.NotNull(frame);
                var evidenceDirectory = Path.Combine(
                    FindRepositoryRoot(), ".pi", "avalonia-port", "spiral-help-evidence");
                Directory.CreateDirectory(evidenceDirectory);
                frame!.Save(
                    Path.Combine(evidenceDirectory, "spiral-help-open.png"),
                    PngBitmapEncoderOptions.Default);
            }
            finally
            {
                trace?.State("finally-before-cleanup", host, view, button);
                HelpPopover.CloseActive();
                if (view is not null)
                    HelpPopover.Clear(view.FindControl<Button>("BtnSpiralHelp")!);
                host?.Close();
                Dispatcher.UIThread.RunJobs();
                trace?.State("finally-after-cleanup", host, view, button);
                if (button is not null)
                {
                    if (pointerEntered is not null) button.PointerEntered -= pointerEntered;
                    if (pointerExited is not null) button.PointerExited -= pointerExited;
                }
                trace?.Dispose();
            }
        });
        }
        finally
        {
            if (localization.CurrentLanguage != previousLanguage)
                localization.SetLanguage(previousLanguage);
        }
    }

    [Fact]
    public async Task MountedSpiralHelpPreservesCallerLanguageStartingInGerman()
    {
        var localization = LocalizationManager.Instance;
        var previousLanguage = localization.CurrentLanguage;
        try
        {
            localization.SetLanguage("de");
            await MountedSpiralHelpUsesLocalizedTopicAndCleansUpAcrossVisibilityAndReload();
            Assert.Equal("de", localization.CurrentLanguage);
        }
        finally
        {
            if (localization.CurrentLanguage != previousLanguage)
                localization.SetLanguage(previousLanguage);
        }
    }

    private sealed class PointerTrace : IDisposable
    {
        private readonly StreamWriter? _writer;

        private PointerTrace(StreamWriter? writer) => _writer = writer;

        internal static PointerTrace Start()
        {
            StreamWriter? writer = null;
            try
            {
                var path = Environment.GetEnvironmentVariable(TracePathEnvironment);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var fullPath = Path.GetFullPath(path);
                    var directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    writer = new StreamWriter(fullPath, append: false) { AutoFlush = true };
                }
            }
            catch
            {
                // Diagnostics must never change the test result or hide the lifecycle assertion.
            }

            var trace = new PointerTrace(writer);
            trace.Write($"kind=runtime variant={Environment.GetEnvironmentVariable(InputVariantEnvironment) ?? "original"} " +
                        $"framework={RuntimeInformation.FrameworkDescription} version={Environment.Version} " +
                        $"os={RuntimeInformation.OSDescription} arch={RuntimeInformation.OSArchitecture} " +
                        $"process={Environment.ProcessId}");
            return trace;
        }

        internal void State(string stage, Window? host, SpiralTabView? view, Button? button)
        {
            try
            {
                var bounds = button is null
                    ? "null"
                    : $"{button.Bounds.X:R},{button.Bounds.Y:R},{button.Bounds.Width:R},{button.Bounds.Height:R}";
                Write($"kind=state stage={stage} " +
                     $"viewVisible={view?.IsVisible.ToString() ?? "null"} " +
                     $"viewSpiral={view?.IsShowingSpiral.ToString() ?? "null"} " +
                     $"buttonVisible={button?.IsVisible.ToString() ?? "null"} " +
                     $"buttonPointerOver={button?.IsPointerOver.ToString() ?? "null"} " +
                     $"popupOpen={(button is not null && HelpPopover.IsOpen(button)).ToString()} " +
                     $"popupContent={(button is not null && HelpPopover.PopupContent(button) is not null).ToString()} " +
                     $"hostVisible={host?.IsVisible.ToString() ?? "null"} " +
                     $"hostHasContent={(host?.Content is not null).ToString()} bounds={bounds}");
            }
            catch (Exception ex)
            {
                Write($"kind=state-error stage={stage} error={ex.GetType().Name}:{ex.Message}");
            }
        }

        internal void RecordPointerEvent(
            string eventName,
            PointerEventArgs e,
            Window? host,
            SpiralTabView? view,
            Button? button)
        {
            try
            {
                Point? point = button is null ? null : e.GetCurrentPoint(button).Position;
                var pointText = point is null ? "null" : $"{point.Value.X:R},{point.Value.Y:R}";
                Write($"kind=pointer event={eventName} point={pointText}");
                State($"event-{eventName}", host, view, button);
            }
            catch (Exception ex)
            {
                Write($"kind=pointer-error event={eventName} error={ex.GetType().Name}:{ex.Message}");
            }
        }

        private void Write(string line)
        {
            try
            {
                _writer?.WriteLine($"{DateTimeOffset.UtcNow:O} {line}");
                _writer?.Flush();
            }
            catch
            {
                // A failed diagnostic write cannot be allowed to replace the lifecycle result.
            }
        }

        public void Dispose()
        {
            try { _writer?.Flush(); } catch { }
            try { _writer?.Dispose(); } catch { }
        }
    }

    private static void EnsureAvalonia()
    {
        Assert.True(AvaloniaTestDispatcher.IsDispatcherThread);
        if (Application.Current is null)
        {
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        LocalizationManager.Instance.SetLanguage("en");
    }

    private static void Move(TopLevel host, Control target)
    {
        Dispatcher.UIThread.RunJobs();
        var point = target.TranslatePoint(
            new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host);
        Assert.True(point.HasValue, $"could not translate {target.Name ?? target.GetType().Name} into host");
        host.MouseMove(point!.Value, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static string[] PopupTexts(Control popup) => Descendants(popup)
        .OfType<TextBlock>()
        .Select(text => text.Text ?? string.Empty)
        .Where(text => !string.IsNullOrWhiteSpace(text))
        .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory.Parent is not null &&
               !File.Exists(Path.Combine(directory.FullName, "ConditioningControlPanel.sln")))
            directory = directory.Parent;
        return directory.FullName;
    }

    private static IEnumerable<Visual> Descendants(Visual root)
    {
        foreach (var child in root.GetLogicalChildren().OfType<Visual>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
