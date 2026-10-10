using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.KeywordTriggers;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Lane w2: the Awareness page's live half (WPF MainWindow.Awareness.cs pulse feed, fire
/// count, recently-focused app chips) reads the running engine, and the now-playing watcher rules.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class AwarenessLiveFeedTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Task WithView(Action<AwarenessTabView, KeywordTriggerEngine, AppSettings> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var (wasApps, wasScope, wasMaster) = (s.KeywordTriggerApps, s.KeywordTriggerAppScope, s.KeywordTriggersEnabled);
        try
        {
            var engine = new KeywordTriggerEngine { HasAccess = () => true, Settings = () => s };
            engine.Start();
            var view = new AwarenessTabView { Engine = engine };
            body(view, engine, s);
        }
        finally
        {
            s.KeywordTriggerApps = wasApps;
            s.KeywordTriggerAppScope = wasScope;
            s.KeywordTriggersEnabled = wasMaster;
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    private static List<Border> Rows(AwarenessTabView view) =>
        view.FindControl<StackPanel>("AwarenessPulseFeed")!.Children.OfType<Border>().Where(b => (b.Tag as string) == "PulseRow").ToList();

    [Fact]
    public Task PulseFeed_Is_Empty_Until_Something_Fires() => WithView((view, _, _) =>
    {
        view.RefreshAwarenessPulseFeed();
        Assert.Empty(Rows(view));
        Assert.Contains(view.FindControl<TextBlock>("TxtAwarenessPulseEmpty")!, view.FindControl<StackPanel>("AwarenessPulseFeed")!.Children);
        Assert.Equal("", view.FindControl<TextBlock>("TxtAwarenessFireCount")!.Text);
    });

    [Fact]
    public Task PulseFeed_Shows_The_Five_Newest_Fires_And_Counts_All() => WithView((view, engine, _) =>
    {
        engine.FireDemo("obey");
        view.RefreshAwarenessPulseFeed();
        Assert.Single(Rows(view));
        Assert.Equal("1 fire today", view.FindControl<TextBlock>("TxtAwarenessFireCount")!.Text);

        for (var i = 0; i < 6; i++) engine.FireDemo("word" + i);
        view.RefreshAwarenessPulseFeed();
        var rows = Rows(view);
        Assert.Equal(AwarenessTabView.PulseRowsShown, rows.Count);
        Assert.Equal("7 fires today", view.FindControl<TextBlock>("TxtAwarenessFireCount")!.Text);
        // Newest first, the keyword in quotes, the source chip in capitals.
        var texts = ((Grid)rows[0].Child!).GetLogicalDescendantsText();
        Assert.Contains("\"word5\"", texts);
        Assert.Contains("TUTORIAL", texts);
        Assert.DoesNotContain(view.FindControl<TextBlock>("TxtAwarenessPulseEmpty")!, view.FindControl<StackPanel>("AwarenessPulseFeed")!.Children);
    });

    [Fact]
    public void Action_Chips_And_Time_Ago_Read_As_WPF()
    {
        Assert.Equal("Plays an audio clip when the word is detected", AwarenessTabView.ActionChipDisplay("PlayAudio").Tooltip);
        Assert.Equal("Adds 5 minutes to the Chaster lock", AwarenessTabView.ActionChipDisplay("ChasterAddTime:5").Tooltip);
        Assert.Equal("Fires a flash burst image when the word is detected", AwarenessTabView.ActionChipDisplay("VisualEffect:ImageFlash").Tooltip);
        Assert.Equal(("", ""), AwarenessTabView.ActionChipDisplay("AddXp:5"));   // internal mechanic: no chip
        Assert.Equal(("", ""), AwarenessTabView.ActionChipDisplay(null));

        var now = new DateTime(2026, 10, 10, 12, 0, 0);
        Assert.Equal("just now", AwarenessTabView.FormatTimeAgo(now.AddSeconds(-3), now));
        Assert.Equal("42s ago", AwarenessTabView.FormatTimeAgo(now.AddSeconds(-42), now));
        Assert.Equal("7m ago", AwarenessTabView.FormatTimeAgo(now.AddMinutes(-7), now));
        Assert.Equal("3h ago", AwarenessTabView.FormatTimeAgo(now.AddHours(-3), now));
        Assert.Equal("09:30", AwarenessTabView.FormatTimeAgo(new DateTime(2026, 10, 8, 9, 30, 0), now));
    }

    [Fact]
    public Task Seen_App_Chips_Offer_Unlisted_Apps_And_A_Click_Adds_One() => WithView((view, engine, s) =>
    {
        s.KeywordTriggersEnabled = true;
        s.KeywordTriggerAppScope = AwarenessAppScope.ExceptListed;
        s.KeywordTriggerApps = new List<string> { "bank" };
        var app = new ForegroundApp("notepad", false);
        engine.ForegroundResolver = () => app;
        engine.OnChar('a');                      // a key in notepad: the gate resolves the app and remembers it
        app = new ForegroundApp("bank", false);
        engine.OnChar('b');
        app = new ForegroundApp("CCP.Avalonia", true);
        engine.OnChar('c');                      // this app is never offered
        Assert.Equal(new[] { "bank", "notepad" }, engine.GetRecentForegroundApps());

        view.RefreshAwarenessSeenAppChips();
        var panel = view.FindControl<WrapPanel>("AwarenessSeenAppsPanel")!;
        var chip = Assert.IsType<Button>(Assert.Single(panel.Children));   // "bank" is already listed
        Assert.Equal("+ notepad", chip.Content);
        Assert.True(view.FindControl<TextBlock>("TxtAwarenessSeenAppsLabel")!.IsVisible);

        view.AddSeenApp("notepad");
        Assert.Equal(new[] { "bank", "notepad" }, CoreSettings.Current.KeywordTriggerApps);
        Assert.Empty(panel.Children);
        Assert.False(view.FindControl<TextBlock>("TxtAwarenessSeenAppsLabel")!.IsVisible);
        Assert.Contains("notepad", view.FindControl<TextBox>("TxtAwarenessAppList")!.Text);
    });

    // ------------------------------------------------------------------ media awareness

    [Fact]
    public void Media_Rules_Blank_Title_Is_Nothing_And_A_Playing_Player_Wins()
    {
        Assert.Null(MprisMediaWatcher.Sample("  ", "x", "Playing", TimeSpan.Zero));
        var paused = MprisMediaWatcher.Sample("A", " ", "Paused", TimeSpan.Zero)!;
        Assert.Null(paused.Artist);
        Assert.False(paused.IsPlaying);
        var playing = MprisMediaWatcher.Sample("B", "Band", "Playing", MprisMediaWatcher.FromMicroseconds(90_000_000))!;
        Assert.True(playing.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(90), playing.Position);
        Assert.Equal("Unknown", MprisMediaWatcher.Sample("C", null, null, TimeSpan.Zero)!.PlaybackState);

        Assert.Same(playing, MprisMediaWatcher.Pick(new[] { paused, playing }));
        Assert.Same(paused, MprisMediaWatcher.Pick(new[] { paused }));
        Assert.Null(MprisMediaWatcher.Pick(Array.Empty<MediaSample>()));
        Assert.Equal(TimeSpan.Zero, MprisMediaWatcher.FromMicroseconds(-5));
    }

    [Fact]
    public void A_Created_Watcher_Is_Idle_Until_Started()
    {
        using var watcher = MediaAwareness.Create();
        Assert.Equal(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || OperatingSystem.IsLinux(), watcher != null);
        if (watcher == null) return;
        Assert.False(watcher.IsAvailable);
        Assert.Null(watcher.Current);
        watcher.Stop();   // stopping an idle watcher is safe
        Assert.Equal(TimeSpan.FromSeconds(3), MediaAwareness.PollInterval);
    }
}

internal static class AwarenessLiveFeedTestExtensions
{
    internal static List<string> GetLogicalDescendantsText(this Control root)
    {
        var found = new List<string>();
        void Walk(Control c)
        {
            if (c is TextBlock t && t.Text is { } text) found.Add(text);
            if (c is Panel p) foreach (var child in p.Children) Walk(child);
            if (c is Decorator d && d.Child is { } inner) Walk(inner);
        }
        Walk(root);
        return found;
    }
}
