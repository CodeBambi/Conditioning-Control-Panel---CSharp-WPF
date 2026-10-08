using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The EmiDesk ring on this head: Core EmiSuggester over the Avalonia catalogue, opened by the desk.</summary>
public sealed class EmiRingWiringTests
{
    private static async Task WithFreshState(System.Action body)
    {
        var st = EmiState.Current;
        var pins = st.Pins.ToList();
        var score = new Dictionary<string, double>(st.OpenScore);
        var at = new Dictionary<string, long>(st.UsageAt);
        int streak = st.RingIgnoreStreak;
        string? bookmark = st.BookCard;
        try
        {
            st.Pins.Clear(); st.OpenScore.Clear(); st.UsageAt.Clear(); st.RingIgnoreStreak = 0;
            body();
        }
        finally
        {
            st.Pins.Clear(); st.Pins.AddRange(pins);
            st.OpenScore.Clear(); foreach (var kv in score) st.OpenScore[kv.Key] = kv.Value;
            st.UsageAt.Clear(); foreach (var kv in at) st.UsageAt[kv.Key] = kv.Value;
            st.RingIgnoreStreak = streak;
            st.BookCard = bookmark;
        }
        await Task.CompletedTask;
    }

    [Fact]
    public Task CatalogueHidesDoorsWithNoSurfaceAndComposesInTableOrder() => WithFreshState(() =>
    {
        var ids = EmiTargets.All.Select(t => t.Id).ToList();
        // Table order, minus exactly the doors this head cannot open.
        var hidden = new[] { "arcademy", "fyp", "dtrh", "intake", "spiral", "goon", "backroom", "justdrop" };
        Assert.Equal(EmiDoors.All.Select(d => d.Id).Except(hidden), ids);

        // A brand new user: no pins, no usage, so the first six available doors in order.
        var ring = EmiSuggester.Compose(EmiTargets.All).Select(s => s.Target.Id);
        Assert.Equal(new[] { "loom", "sessions", "flashes", "codex", "videos", "subliminals" }, ring);

        // A pick scores the door (WPF Pick -> NoteOpen), and a pin takes slot one.
        EmiTargets.Find("vault")!.Open();
        Assert.True(EmiSuggester.ScoreOf("vault") > 0);
        EmiSuggester.PinToTop("mindwipe");
        var again = EmiSuggester.Compose(EmiTargets.All);
        Assert.Equal("mindwipe", again[0].Target.Id);
        Assert.True(again[0].Pinned);
        Assert.Equal("vault", again[1].Target.Id);
    });

    [Fact]
    public Task DeskOpensTheRingAndCountsADismissal() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        return WithFreshState(() =>
        {
            var desk = new EmiDeskWindow();
            desk.Show();
            int opens = EmiState.Current.RingOpens;
            desk.ToggleRing();
            Dispatcher.UIThread.RunJobs();
            Assert.True(desk.RingOpen);
            Assert.Equal(opens + 1, EmiState.Current.RingOpens);

            desk.ToggleRing();                 // folded without a pick: one dismissal
            Dispatcher.UIThread.RunJobs();
            Assert.False(desk.RingOpen);
            Assert.Equal(1, EmiState.Current.RingIgnoreStreak);
            desk.Close();
        });
    });

    [Fact]
    public Task BookTakeMeThereOpensTheDoorThroughThePick() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        return WithFreshState(() =>
        {
            var book = new EmiBookWindow();
            book.GoTo("the-panic-key");                   // Target "settings"
            var go = book.FindControl<global::Avalonia.Controls.Button>("BtnGo")!;
            Assert.True(go.IsVisible && go.IsEnabled);
            go.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            Assert.True(EmiSuggester.ScoreOf("settings") > 0);
            book.Close();
        });
    });

    /// <summary>Privacy: the desktop-wide keymap is never kept. No field of X11Pointer may hold a buffer.</summary>
    [Fact]
    public void PointerReaderKeepsNoKeyState()
    {
        var t = typeof(global::ConditioningControlPanel.Avalonia.App).Assembly
            .GetType("ConditioningControlPanel.Avalonia.Platform.X11Pointer")!;
        var fields = t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance
                                 | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        Assert.NotEmpty(fields);
        Assert.DoesNotContain(fields, f => f.FieldType.IsArray || f.FieldType == typeof(System.Memory<byte>));
    }

    [Fact]
    public Task OptionsStopPollingOnCloseAndSurviveANullRead() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        int reads = 0;
        var win = new EmiOptionsWindow { ReadPointer = () => { reads++; return null; } };
        async Task Wait() { for (int i = 0; i < 10; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); } }

        win.OpenPanel();
        await Wait();
        int first = reads;
        Assert.True(first > 0);
        await Wait();
        Assert.True(reads > first);          // a null read (another X screen) does not stop the poll
        win.ClosePanel();
        int closed = reads;
        await Wait();
        Assert.Equal(closed, reads);         // closed: no more reads
        win.Close();
    });

    [Fact]
    public Task OptionsFoldOnAClickAwayOrEscapeButNotOnItself() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        (PixelPoint, bool, bool)? now = null;
        var win = new EmiOptionsWindow { ReadPointer = () => now };
        win.OpenPanel();
        Dispatcher.UIThread.RunJobs();
        var inside = win.Position + new PixelPoint(5, 5);
        var outside = new PixelPoint(-5000, -5000);

        now = (outside, true, false); win.PollClickAway();   // still the opening click: ignored
        Assert.True(win.IsOpen);
        now = (outside, false, false); win.PollClickAway();
        now = (inside, true, false); win.PollClickAway();    // a click on the panel itself
        Assert.True(win.IsOpen);
        now = (inside, false, false); win.PollClickAway();
        now = (outside, true, false); win.PollClickAway();   // a click away
        Assert.False(win.IsOpen);

        win.OpenPanel();
        Dispatcher.UIThread.RunJobs();
        now = (inside, false, false); win.PollClickAway();
        now = (inside, false, true); win.PollClickAway();    // Escape anywhere
        Assert.False(win.IsOpen);
        win.Close();
        return Task.CompletedTask;
    });
}
