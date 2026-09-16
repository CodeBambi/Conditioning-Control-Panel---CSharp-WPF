using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HelpPopoverTests
{
    [Fact]
    public async Task HeadlessPointerKeyboardAndRenderProofCoverPopoverLifecycle()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var content = HelpContentService.GetContent("Scheduler");
            var button = new Button
            {
                Name = "HelpButton",
                Content = "?",
                Width = 44,
                Height = 44,
            };
            var outside = new Border
            {
                Width = 240,
                Height = 180,
                Background = Brushes.Transparent,
            };
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 24,
                Margin = new Thickness(24),
            };
            row.Children.Add(button);
            row.Children.Add(outside);
            var host = new DeactivationProbeWindow
            {
                Width = 720,
                Height = 420,
                Content = row,
            };

            try
            {
                HelpPopover.Attach(button, content);
                host.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);

                // A real headless pointer move starts the same hover timer as a desktop pointer.
                Move(host, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                Assert.True(HelpPopover.IsOpen(button));
                Assert.False(HelpPopover.IsPinned(button));

                var popup = HelpPopover.PopupContent(button);
                Assert.NotNull(popup);
                var texts = Descendants(popup!).OfType<TextBlock>()
                    .Select(text => text.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray();
                Assert.Contains(content.Title, texts);
                Assert.Contains(content.WhatItDoes, texts);
                Assert.Contains("What it does", texts);
                Assert.Contains("Tips", texts);
                Assert.Contains("How it works", texts);

                // Deliberate pre-fix receipt: detach the still-open host and then reattach it
                // without Clear. The panel-hosting implementation throws while the ancestor is
                // iterating its cached visual children; the native logical parent fix must not.
                host.Content = null;
                Dispatcher.UIThread.RunJobs();
                host.Content = row;
                Dispatcher.UIThread.RunJobs();
                Move(host, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                Assert.True(HelpPopover.IsOpen(button), "open popup did not survive host reattach");

                // The popup root is on the host overlay layer, so this is a real pointer transfer
                // in host coordinates.
                MoveIntoPopup(popup);
                await Task.Delay(320);
                Dispatcher.UIThread.RunJobs();
                Assert.True(HelpPopover.IsOpen(button), "pointer transfer into the card lost the hover grace");

                // Leaving the actual popup bounds exercises the close-grace timer used by a
                // desktop pointer.
                MoveOutOfPopup(popup);
                await Task.Delay(320);
                Dispatcher.UIThread.RunJobs();
                Assert.False(HelpPopover.IsOpen(button), "unpinned card ignored pointer leave");

                // Reopen by hover and verify a host click-away closes an unpinned card too.
                Move(host, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                Assert.True(HelpPopover.IsOpen(button));
                ClickAt(host, OutsidePopup(popup, host));
                Assert.False(HelpPopover.IsOpen(button), "unpinned card ignored click-away");

                // A click pins the card; a second click unpins/closes it.
                Move(host, button);
                Click(host, button);
                Assert.True(HelpPopover.IsOpen(button));
                Assert.True(HelpPopover.IsPinned(button));
                MoveOutOfPopup(popup);
                await Task.Delay(320);
                Dispatcher.UIThread.RunJobs();
                Assert.True(HelpPopover.IsOpen(button), "pinned card closed on pointer leave");
                Click(host, button);
                Assert.False(HelpPopover.IsOpen(button));
                Assert.False(HelpPopover.IsPinned(button));

                // Keyboard dismissal is sent through the headless input backend, not a direct event.
                Click(host, button);
                Assert.True(HelpPopover.IsPinned(button));
                button.Focus();
                host.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                Dispatcher.UIThread.RunJobs();
                Assert.False(HelpPopover.IsOpen(button));
                Assert.False(HelpPopover.IsPinned(button));

                // A real deactivation event is the other window-level dismissal path.
                Click(host, button);
                Assert.True(HelpPopover.IsPinned(button));
                host.RaiseDeactivatedForTest();
                Dispatcher.UIThread.RunJobs();
                Assert.False(HelpPopover.IsOpen(button));
                Assert.False(HelpPopover.IsPinned(button));

                // Replacing content while attached closes the old card and uses the new Core topic.
                HelpPopover.Attach(button, HelpContentService.GetContent("IntensityRamp"));
                Click(host, button);
                Assert.True(HelpPopover.IsPinned(button));
                HelpPopover.Attach(button, content);
                Assert.False(HelpPopover.IsOpen(button));
                Assert.False(HelpPopover.IsPinned(button));

                // Detach/reload is idempotent and leaves no popup or active timer behind.
                HelpPopover.Clear(button);
                HelpPopover.Clear(button);
                row.Children.Remove(button);
                Dispatcher.UIThread.RunJobs();
                row.Children.Add(button);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(HelpPopover.PopupContent(button));
                Move(host, button);
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                Assert.False(HelpPopover.IsOpen(button), "clear/reload left a stale timer handler");

                // Exercise the real FeatureCard.Unloaded -> HelpPopover.Clear path while its
                // button's popover is open, then prove Loaded re-attaches it without panel churn.
                var featureCard = new FeatureCard
                {
                    HelpSectionId = "Scheduler",
                    Width = 220,
                    Height = 180,
                };
                var featureHost = new DeactivationProbeWindow
                {
                    Width = 300,
                    Height = 260,
                    Content = featureCard,
                };
                try
                {
                    featureHost.Show();
                    Dispatcher.UIThread.RunJobs();
                    var featureButton = featureCard.FindControl<Button>("BtnHelp");
                    Assert.NotNull(featureButton);
                    Assert.True(featureButton!.IsVisible);
                    Move(featureHost, featureButton);
                    await Task.Delay(150);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(HelpPopover.IsOpen(featureButton));

                    featureHost.Content = null;
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(HelpPopover.IsOpen(featureButton));
                    Assert.Null(HelpPopover.PopupContent(featureButton));

                    featureHost.Content = featureCard;
                    Dispatcher.UIThread.RunJobs();
                    Move(featureHost, featureButton);
                    await Task.Delay(150);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(HelpPopover.IsOpen(featureButton), "FeatureCard.Loaded did not reattach help");
                }
                finally
                {
                    HelpPopover.CloseActive();
                    featureHost.Close();
                    Dispatcher.UIThread.RunJobs();
                }

                // Save the actual headless frame while the rich card is visible for independent review.
                HelpPopover.Attach(button, content);
                Click(host, button);
                Assert.True(HelpPopover.IsOpen(button));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
                Dispatcher.UIThread.RunJobs();
                var frame = host.GetLastRenderedFrame();
                Assert.NotNull(frame);
                var evidenceDirectory = Path.Combine(
                    FindRepositoryRoot(), ".pi", "avalonia-port", "help-popover-evidence");
                Directory.CreateDirectory(evidenceDirectory);
                frame!.Save(
                    Path.Combine(evidenceDirectory, "rich-help-popover.png"),
                    PngBitmapEncoderOptions.Default);
            }
            finally
            {
                HelpPopover.Clear(button);
                host.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
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

    private static void MoveIntoPopup(Control popup)
    {
        var popupHost = TopLevel.GetTopLevel(popup);
        Assert.NotNull(popupHost);
        var localPoint = new Point(popup.Bounds.Width / 2, popup.Bounds.Height / 2);
        var point = popup.TranslatePoint(localPoint, popupHost!);
        Assert.True(point.HasValue, "could not translate popup center into its host");
        var popupRect = PopupBoundsInHost(popup, popupHost!);
        Assert.True(popupRect.Contains(point!.Value), $"popup center {point.Value} was outside {popupRect}");
        popupHost.MouseMove(point.Value, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void MoveOutOfPopup(Control popup)
    {
        var popupHost = TopLevel.GetTopLevel(popup);
        Assert.NotNull(popupHost);
        popupHost!.MouseMove(OutsidePopup(popup, popupHost), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static Point OutsidePopup(Control popup, TopLevel popupHost)
    {
        var popupRect = PopupBoundsInHost(popup, popupHost);
        var width = popupHost.Bounds.Width;
        var height = popupHost.Bounds.Height;
        var candidates = new[]
        {
            new Point(1, 1),
            new Point(width - 1, 1),
            new Point(1, height - 1),
            new Point(width - 1, height - 1),
        };
        foreach (var candidate in candidates)
        {
            if (candidate.X >= 0 && candidate.Y >= 0 &&
                candidate.X < width && candidate.Y < height &&
                !popupRect.Contains(candidate))
            {
                Assert.False(popupRect.Contains(candidate), $"away point {candidate} was inside {popupRect}");
                return candidate;
            }
        }

        throw new Xunit.Sdk.XunitException($"no host point was outside popup bounds {popupRect}");
    }

    private static Rect PopupBoundsInHost(Control popup, TopLevel popupHost)
    {
        var origin = popup.TranslatePoint(new Point(0, 0), popupHost);
        Assert.True(origin.HasValue, "could not translate popup origin into its host");
        return new Rect(origin!.Value, popup.Bounds.Size);
    }

    private static void Click(TopLevel host, Control target)
    {
        Move(host, target);
        var point = target.TranslatePoint(
            new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host);
        Assert.True(point.HasValue);
        ClickAt(host, point!.Value);
    }

    private static void ClickAt(TopLevel host, Point point)
    {
        host.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        host.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory.Parent is not null &&
               !File.Exists(Path.Combine(directory.FullName, "ConditioningControlPanel.sln")))
            directory = directory.Parent;
        return directory.FullName;
    }

    private sealed class DeactivationProbeWindow : Window
    {
        internal void RaiseDeactivatedForTest()
        {
            var method = typeof(WindowBase).GetMethod(
                "HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(this, null);
        }
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
