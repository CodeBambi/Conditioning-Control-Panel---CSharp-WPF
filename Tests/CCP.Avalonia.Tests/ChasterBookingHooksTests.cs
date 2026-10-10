using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>HC4: the prices Circe's Tab lists book on this head, at the WPF sites with the WPF
/// keys and figures, and nothing adds during the ten-minute hold after a panic. Swaps the
/// process-wide <see cref="ChasterHead.Service"/>, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ChasterBookingHooksTests
{
    private sealed class Quiet : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = r });
    }

    private static readonly string[] Rows =
    {
        "typo", "lockcard", "session", "remote_media", "remote_video", "program_done", "program_skipped",
        NatashasFavourite.EventId, NatashasFavourite.HeldEventId,
    };

    /// <summary>A linked service with every HC4 row on, as <see cref="ChasterHead.Service"/>, and
    /// what it booked. Restores the seam and the token store.</summary>
    private static async Task With(Func<ChasterService, List<(string Id, int Seconds)>, Task> body, bool remoteOpen = false, bool panicArmed = true)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory("ccp-chaster-hooks-").FullName;
            SecretStore.Seed();
            new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
            var chaster = new ChasterService(new ChasterClient(new Quiet()), new SecretChasterTokenStore(),
                Path.Combine(dir, "chaster_tab.json"),
                () => new ChasterOptions(true, "l1", new HashSet<string>(Rows, StringComparer.Ordinal), RemoteOpen: remoteOpen, PanicArmed: panicArmed));
            var booked = new List<(string, int)>();
            chaster.Booked += (id, b) => booked.Add((id, b.AppliedSeconds));
            var before = ChasterHead.Service;
            ChasterHead.Service = chaster;
            try { await body(chaster, booked); }
            finally
            {
                ChasterHead.Service = before;
                chaster.Dispose();
                new SecretChasterTokenStore().Clear();
                try { Directory.Delete(dir, true); } catch { }
            }
        });
    }

    [Fact]
    public Task LockCard_books_every_typo_and_the_card_credit() => With((_, booked) =>
    {
        ChasterHead.NoteLockCard(3);
        Assert.Equal(new[] { ("typo", 90), ("lockcard", -60) }, booked);   // WPF AchievementService.cs:763
        booked.Clear();
        ChasterHead.NoteLockCard(0);   // a clean card: no typo line, the credit only
        Assert.Equal(new[] { ("lockcard", -30) }, booked);   // only what is owed comes off
        return Task.CompletedTask;
    });

    [Fact]
    public Task Session_books_the_session_credit() => With((chaster, booked) =>
    {
        ChasterHead.NoteLockCard(30);   // 15:00 on the tab
        booked.Clear();
        AvApp.SessionCompleted();   // what CoreProgression.TrackSessionCompletedProvider is seeded with
        Assert.Equal(("session", -600), Assert.Single(booked));   // WPF AchievementService.cs:1000
        Assert.Equal(240, chaster.BalanceSeconds);   // 15:00 of typos, 1:00 back for the card, 10:00 back for the session
        return Task.CompletedTask;
    });

    [Fact]
    public Task Remote_flash_and_video_land_on_the_wearers_tab() => With((chaster, booked) =>
    {
        var (flash, note) = (CoreFlash.ShowProvider, RemoteCommands.ChasterNote);
        var shown = 0;
        CoreFlash.ShowProvider = () => shown++;
        RemoteCommands.ChasterNote = id => AvApp.ChasterNote(id);   // the App seed
        try
        {
            Assert.Null(RemoteCommands.Execute("trigger_flash", null));
            Assert.Equal(1, shown);
            Assert.Equal(("remote_media", 60), Assert.Single(booked));   // WPF RemoteControlService.cs:1245
            // A subliminal the controller sends was never priced.
            booked.Clear();
            RemoteCommands.Execute("stop_bubbles", null);
            Assert.Empty(booked);
            // The video row, by the same seam the trigger_video case calls.
            RemoteCommands.ChasterNote!("remote_video");
            Assert.Equal(("remote_video", 300), Assert.Single(booked));   // WPF :1358
        }
        finally { CoreFlash.ShowProvider = flash; RemoteCommands.ChasterNote = note; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_remote_session_with_the_panic_key_off_books_no_add() => With((chaster, booked) =>
    {
        // Remote can never run the tab up while panic is disarmed (Core RemoteRoom).
        Assert.Equal(TabRefusal.Remote, chaster.Note("remote_media").Refusal);
        ChasterHead.NoteLockCard(4);
        Assert.Empty(booked);
        return Task.CompletedTask;
    }, remoteOpen: true, panicArmed: false);

    [Fact]
    public void The_head_reads_the_remote_session_for_the_cap()
    {
        var before = ChasterHead.RemoteState;
        try
        {
            ChasterHead.RemoteState = () => (true, null);
            Assert.True(ChasterHead.RemoteOpenNow());
            ChasterHead.RemoteState = () => (false, DateTime.UtcNow.AddSeconds(-30));   // inside the grace
            Assert.True(ChasterHead.RemoteOpenNow());
            ChasterHead.RemoteState = () => (false, DateTime.UtcNow.AddHours(-1));
            Assert.False(ChasterHead.RemoteOpenNow());
            ChasterHead.RemoteState = () => (false, null);
            Assert.False(ChasterHead.RemoteOpenNow());
        }
        finally { ChasterHead.RemoteState = before; }
    }

    [Fact]
    public Task Program_days_book_done_and_skipped() => With((_, booked) =>
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ccp-programs-hooks-").FullName, "programs.json");
        using var programs = new ProgramService(path, readOnly: true);
        var detach = ChasterHead.AttachPrograms(programs);
        ChasterHead.NoteLockCard(30);   // something for the credit to come off
        booked.Clear();
        Raise(programs, "DayMissed");
        Raise(programs, "DayCompleted");
        Assert.Equal(new[] { ("program_skipped", 1800), ("program_done", -600) }, booked);   // WPF ChasterHooks.cs:70-71
        detach();
        booked.Clear();
        Raise(programs, "DayMissed");
        Assert.Empty(booked);
        return Task.CompletedTask;
    });

    private static void Raise(ProgramService programs, string name)
    {
        var handler = (MulticastDelegate?)typeof(ProgramService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(programs);
        handler?.DynamicInvoke(programs, new ProgramDayEventArgs(new ProgramDefinition(), new ProgramDay(), new ProgramDayRecord()));
    }

    [Fact]
    public Task Nothing_adds_during_the_safety_hold() => With((chaster, booked) =>
    {
        chaster.NoteSafetyExit();   // what PanicSurfaces.ArmSafetyHold calls
        ChasterHead.NoteLockCard(5);
        AvApp.ChasterNote("remote_media");
        AvApp.ChasterNote("program_skipped");
        Assert.DoesNotContain(booked, b => b.Seconds > 0);
        Assert.Equal(0, chaster.BalanceSeconds);
        Assert.Equal(TabRefusal.SafetyExit, chaster.Note("typo", 2).Refusal);
        // And no bubble wears red while the price cannot land.
        var rolled = Enumerable.Range(0, 200).Select(_ => { var b = new AmbientBubble(); BubbleOverlay.MarkIfNatasha(b); return b.IsNatasha; });
        Assert.DoesNotContain(true, rolled);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Only_the_players_own_pop_books_natasha_and_a_hold_books_the_credit() => With((_, booked) =>
    {
        var clock = BubbleOverlay.NowMs;
        long now = 1000;
        BubbleOverlay.NowMs = () => now;
        var s = CoreSettings.Current;
        var (dayKey, paid) = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday);
        try
        {
            // About one in ten is dealt while the row can book.
            var dealt = Enumerable.Range(0, 400).Count(_ => { var b = new AmbientBubble(); BubbleOverlay.MarkIfNatasha(b); return b.IsNatasha; });
            Assert.InRange(dealt, 10, 90);

            AmbientBubble Red()
            {
                var b = new AmbientBubble { Screen = 0, X = 100, Y = 100, Size = 200, Clickable = true, IsNatasha = true };
                BubbleOverlay.Field.Bubbles.Add(b);
                return b;
            }

            BubbleOverlay.Pop(Red());   // the companion, a chain, a sweep: the app's pop
            Assert.Empty(booked);

            var plain = new AmbientBubble { Screen = 0, X = 100, Y = 100, Size = 200, Clickable = true };
            BubbleOverlay.Pop(plain, byPlayer: true);   // not red: never books
            Assert.Empty(booked);

            BubbleOverlay.Pop(Red(), byPlayer: true);
            Assert.Equal((NatashasFavourite.EventId, NatashasFavourite.Seconds), Assert.Single(booked));   // +5:00, WPF BubbleService.cs:663
            booked.Clear();

            // A quick click on the red one is a hold let go early: it pops.
            var clicked = Red();
            BubbleOverlay.BeginResist(clicked);
            now += 200;
            BubbleOverlay.TickResist();
            Assert.InRange(clicked.ResistProgress, 0.1, 0.2);
            Assert.False(clicked.Popping);
            BubbleOverlay.ResistReleased();
            BubbleOverlay.TickResist();
            Assert.True(clicked.Popping);
            Assert.Equal((NatashasFavourite.EventId, NatashasFavourite.Seconds), Assert.Single(booked));
            booked.Clear();

            // Held until the ring fills: resisted, the credit, no burst.
            var held = Red();
            BubbleOverlay.BeginResist(held);
            now += NatashasFavourite.HoldMs + 1;
            BubbleOverlay.TickResist();
            Assert.True(held.Popping && held.Deflating);
            Assert.Equal((NatashasFavourite.HeldEventId, NatashasFavourite.HeldSeconds), Assert.Single(booked));   // -1:00, WPF :664
            booked.Clear();

            // Slid off while down: nothing happens, it floats on.
            var slid = Red();
            BubbleOverlay.BeginResist(slid);
            BubbleOverlay.ResistMoved(0, 900, 900);
            now += 100;
            BubbleOverlay.TickResist();
            Assert.False(slid.Popping);
            now += 5000;
            BubbleOverlay.TickResist();
            Assert.False(slid.Popping);
            Assert.Empty(booked);
        }
        finally
        {
            BubbleOverlay.NowMs = clock;
            BubbleOverlay.Field.Bubbles.Clear();
            s.AmbientBubbleXpDayKey = dayKey; s.AmbientBubbleXpPaidToday = paid;
        }
        return Task.CompletedTask;
    });
}
