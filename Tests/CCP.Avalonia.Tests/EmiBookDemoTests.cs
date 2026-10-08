using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Her book from the user's side: the ? chip opens it at the bookmark (WPF EmiBook.Open), counts the
/// open, and the card's demo loop (Core EmiBookDemos) animates the stage on a stepped clock and stops
/// with the book.
/// </summary>
public sealed class EmiBookDemoTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public long Ticks;
        public override long GetTimestamp() => Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static int[] Frame(EmiBookWindow book)
    {
        var bmp = (WriteableBitmap)book.FindControl<Image>("Stage")!.Source!;
        using var fb = bmp.Lock();
        var px = new int[fb.Size.Width * fb.Size.Height];
        for (int y = 0; y < fb.Size.Height; y++)
            Marshal.Copy(fb.Address + y * fb.RowBytes, px, y * fb.Size.Width, fb.Size.Width);
        return px;
    }

    [Fact]
    public Task HelpChipOpensAtTheBookmarkAndTheDemoAnimatesOnlyWhileOpen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var st = EmiState.Current;
        string? bookmark = st.BookCard;
        int opens = st.CodexOpens;
        var motion = CoreSettings.Current.MotionLevel;
        var clock = new SteppedClock();
        EmiDeskWindow? desk = null;
        try
        {
            st.BookCard = "flashes";
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            EmiBookWindow.Time = clock;

            desk = new EmiDeskWindow();
            desk.Show();
            desk.FindControl<Button>("BtnHelp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var book = desk.Book!;
            Assert.Equal(opens + 1, st.CodexOpens);                        // NoteCodexOpened
            Assert.Equal("flashes", book.Painter!.Id);                     // opened at the bookmark
            Assert.True(book.ClockRunning);

            book.Tick();
            var a = Frame(book);
            clock.Ticks += TimeSpan.FromMilliseconds(book.Painter.LoopMs * 0.4).Ticks;
            book.Tick();
            Assert.NotEqual(a, Frame(book));                               // the loop moved the stage

            book.FindControl<Button>("BtnNext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(book.Painter!.Id, st.BookCard);                   // the bookmark follows the page
            Assert.NotEqual("flashes", st.BookCard);

            desk.CloseBook();
            Assert.False(book.ClockRunning);                               // no ticks once it folds
        }
        finally
        {
            desk?.Close();
            EmiBookWindow.Time = TimeProvider.System;
            CoreSettings.Current.MotionLevel = motion;
            st.BookCard = bookmark;
            st.CodexOpens = opens;
        }
        return Task.CompletedTask;
    });

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    /// <summary>Reduced motion (WPF MotionFx.AllowAmbientLoops false): the painter's still frame, and no clock.</summary>
    [Fact]
    public Task ReducedMotionShowsAStillFrameWithNoClock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var st = EmiState.Current;
        string? bookmark = st.BookCard;
        var motion = CoreSettings.Current.MotionLevel;
        EmiBookWindow? book = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            book = new EmiBookWindow();
            book.OpenBook("flashes");
            Dispatcher.UIThread.RunJobs();
            Assert.False(book.ClockRunning);
            var still = Frame(book);
            Assert.Contains(still, p => p != still[0]);                    // a drawn frame, not a blank stage
            var expected = new EmiPixelCanvas(96, 72);
            book.Painter!.Draw(expected, book.Painter.StillMs);
            Assert.Equal((int[])(object)expected.Pixels, still);           // exactly the painter's still frame
        }
        finally
        {
            book?.Close();
            CoreSettings.Current.MotionLevel = motion;
            st.BookCard = bookmark;
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF BtnCompleteGuide opens the website manual.</summary>
    [Fact]
    public Task CompleteGuideOpensTheManual() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var previous = EmiBookWindow.OpenManual;
        int opened = 0;
        var book = new EmiBookWindow();
        try
        {
            EmiBookWindow.OpenManual = () => opened++;
            book.FindControl<Button>("BtnCompleteGuide")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, opened);
            Assert.Equal("Complete CCP guides", ((TextBlock)book.FindControl<Button>("BtnCompleteGuide")!.Content!).Text);
        }
        finally { EmiBookWindow.OpenManual = previous; book.Close(); }
        return Task.CompletedTask;
    });
}
