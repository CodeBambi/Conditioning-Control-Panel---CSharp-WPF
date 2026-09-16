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
                var button = view.FindControl<Button>("BtnSpiralHelp");
                Assert.NotNull(button);
                host = new Window
                {
                    Width = 980,
                    Height = 620,
                    Content = view,
                };

                host.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(view.IsShowingSpiral, "the mounted view did not enter its current spiral presentation path");
                Assert.True(button!.IsVisible);

                Move(host, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
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
                Assert.True(HelpPopover.IsOpen(button));
                Assert.Same(firstPopup, HelpPopover.PopupContent(button));

                // The real fog predicate is still WPF-only on this branch. This presentation-only
                // invocation exercises the existing false gate without inventing a state setter.
                var applyHelp = typeof(SpiralTabView).GetMethod(
                    "ApplyHelpChip", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(applyHelp);
                applyHelp!.Invoke(view, new object[] { false });
                Dispatcher.UIThread.RunJobs();
                Assert.False(button.IsVisible);
                Assert.False(HelpPopover.IsOpen(button));
                Assert.Null(HelpPopover.PopupContent(button));

                // The normal entrypoint reattaches the topic after the gate opens again.
                view.OnTabShown();
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.IsVisible);
                host.MouseMove(new Point(1, 1), RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Move(host, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                Assert.True(HelpPopover.IsOpen(button));
                Assert.NotSame(firstPopup, HelpPopover.PopupContent(button));

                // The tab's own hidden lifecycle closes and clears an open card.
                view.IsVisible = false;
                Dispatcher.UIThread.RunJobs();
                Assert.False(button.IsVisible);
                Assert.False(HelpPopover.IsOpen(button));
                Assert.Null(HelpPopover.PopupContent(button));
                view.IsVisible = true;
                Dispatcher.UIThread.RunJobs();
                Move(host, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                Assert.True(HelpPopover.IsOpen(button));

                // Unload/reload must close the old popup and reattach a fresh localized one.
                host.Content = null;
                Dispatcher.UIThread.RunJobs();
                Assert.False(HelpPopover.IsOpen(button));
                Assert.Null(HelpPopover.PopupContent(button));
                host.Content = view;
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.IsVisible);
                Move(host, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
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
                HelpPopover.CloseActive();
                if (view is not null)
                    HelpPopover.Clear(view.FindControl<Button>("BtnSpiralHelp")!);
                host?.Close();
                Dispatcher.UIThread.RunJobs();
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
