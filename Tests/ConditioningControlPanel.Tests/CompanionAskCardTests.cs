using System;
using System.Linq;
using ConditioningControlPanel.Services.Companion.Asks;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Companion ask cards: the catalogue never offers anything the user cannot open, pacing and
/// decline backoff hold, cards expire, and a "meh" video is never offered again.
/// </summary>
public class CompanionAskCardTests
{
    private static readonly DateTime T0 = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Clock { public DateTime Now = T0; }

    private static AskPlanner Planner(Clock clock) => new(() => clock.Now, u => u);

    private static AskGate Open(DateTime? lastChat = null) => new(true, true, false, lastChat);

    private static AskSources Sources(bool gameAllowed = true, bool questsAllowed = true, bool sessionAllowed = true,
        string[]? meh = null, string[]? loved = null, string[]? known = null) => new()
    {
        Videos = new[] { new AskVideo("Alpha", "https://a.example/1"), new AskVideo("Bravo", "https://a.example/2"), new AskVideo("Charlie", "https://a.example/3") },
        MehVideos = meh ?? Array.Empty<string>(),
        LovedVideos = loved ?? Array.Empty<string>(),
        Games = new[] { new AskOption("game.free", "Free Game", () => true), new AskOption("game.t2", "Tier Two Game", () => gameAllowed) },
        Sessions = new[] { new AskOption("s1", "Session One", () => sessionAllowed), new AskOption("s2", "Session Two", () => sessionAllowed) },
        Quests = new AskOption("page.quests", "Quests", () => questsAllowed),
        KnownTopics = known ?? Array.Empty<string>(),
        Text = key => key,
    };

    [Fact]
    public void LockedGame_IsNeverOffered()
    {
        var s = Sources(gameAllowed: false);
        for (int seed = 0; seed < 50; seed++)
        {
            var card = AskCatalog.Build(AskKind.Game, s, new Random(seed), T0)!;
            Assert.DoesNotContain(card.Choices, c => c.Target == "game.t2");
        }
    }

    [Fact]
    public void NoAllowedAction_DropsTheCard()
    {
        var s = new AskSources
        {
            Games = new[] { new AskOption("game.t2", "Locked", () => false) },
            Sessions = new[] { new AskOption("s1", "Locked", () => throw new InvalidOperationException()) },
            Quests = new AskOption("page.quests", "Quests", () => false),
        };
        Assert.Null(AskCatalog.Build(AskKind.Game, s, new Random(1), T0));
        Assert.Null(AskCatalog.Build(AskKind.Session, s, new Random(1), T0));
        Assert.Null(AskCatalog.Build(AskKind.Quests, s, new Random(1), T0));
        Assert.Null(AskCatalog.Build(AskKind.Watch, s, new Random(1), T0));
    }

    [Fact]
    public void ActionCards_PutTheRedNoLast_AndCapAtThree()
    {
        foreach (var kind in new[] { AskKind.Watch, AskKind.Game, AskKind.Session })
        {
            var card = AskCatalog.Build(kind, Sources(), new Random(3), T0)!;
            Assert.InRange(card.Choices.Count, 2, 3);
            Assert.Equal(AskTone.No, card.Choices[^1].Tone);
        }
    }

    [Fact]
    public void FavouriteColour_OffersPinkFirst()
    {
        var card = AskCatalog.Build(AskKind.GetToKnow, Sources(known: new[] { "time", "drop" }), new Random(5), T0)!;
        Assert.Equal("colour", card.Subject);
        Assert.Equal("pink", card.Choices[0].Target);
        Assert.Equal(AskTone.Pink, card.Choices[0].Tone);
        Assert.Equal(AskAction.FocusChat, card.Choices[^1].Action);
    }

    [Fact]
    public void AnsweredTopics_AreNotAskedAgain()
    {
        Assert.Null(AskCatalog.Build(AskKind.GetToKnow, Sources(known: AskCatalog.Topics), new Random(1), T0));
    }

    [Fact]
    public void MehVideo_IsNeverOffered_LovedIsPreferred()
    {
        var s = Sources(meh: new[] { "alpha", "Bravo" });
        for (int seed = 0; seed < 50; seed++)
            Assert.Equal("Charlie", AskCatalog.PickVideo(s, new Random(seed))!.Title);

        var loved = Sources(loved: new[] { "Bravo" });
        int bravo = Enumerable.Range(0, 200).Count(i => AskCatalog.PickVideo(loved, new Random(i))!.Title == "Bravo");
        Assert.True(bravo > 100, $"loved picked {bravo}/200");

        Assert.Null(AskCatalog.PickVideo(Sources(meh: new[] { "Alpha", "Bravo", "Charlie" }), new Random(1)));
    }

    [Fact]
    public void AnotherOne_ExcludesTheCurrentVideo()
    {
        var s = Sources();
        for (int seed = 0; seed < 30; seed++)
            Assert.NotEqual("https://a.example/1", AskCatalog.PickVideo(s, new Random(seed), "https://a.example/1")!.Url);
    }

    [Fact]
    public void Card_ExpiresAfterThreeMinutes_AndResolvesOnce()
    {
        var card = AskCatalog.Build(AskKind.Game, Sources(), new Random(1), T0)!;
        Assert.True(card.IsOpen(T0 + TimeSpan.FromMinutes(2.9)));
        Assert.True(card.Resolve("play", T0 + TimeSpan.FromMinutes(1)));
        Assert.False(card.Resolve("no", T0 + TimeSpan.FromMinutes(1)));
        Assert.Equal("play", card.ChosenId);

        var late = AskCatalog.Build(AskKind.Game, Sources(), new Random(1), T0)!;
        Assert.False(late.IsOpen(T0 + AskCard.Life));
        Assert.Equal(AskState.Expired, late.State);
        Assert.False(late.Resolve("play", T0 + AskCard.Life));
    }

    [Fact]
    public void Swap_RestartsTheClock()
    {
        var s = Sources();
        var card = AskCatalog.Build(AskKind.Watch, s, new Random(1), T0)!;
        var video = new AskVideo("Bravo", "https://a.example/2");
        Assert.True(card.Swap("q", AskCatalog.WatchChoices(s, video), video.Title, video.Url, T0 + TimeSpan.FromMinutes(2)));
        Assert.True(card.IsOpen(T0 + TimeSpan.FromMinutes(4)));
        Assert.Equal("Bravo", card.Subject);
    }

    [Fact]
    public void Pacing_OneCardPerFifteenMinutes_AndNotWhileBusy()
    {
        var clock = new Clock();
        var p = Planner(clock);
        Assert.True(p.MayAsk(Open()));
        Assert.False(p.MayAsk(new AskGate(true, true, true, null)));
        Assert.False(p.MayAsk(new AskGate(false, true, false, null)));
        Assert.False(p.MayAsk(new AskGate(true, false, false, null)));
        p.NoteShown(unprompted: true);
        clock.Now = T0 + TimeSpan.FromMinutes(14);
        Assert.False(p.MayAsk(Open()));
        clock.Now = T0 + TimeSpan.FromMinutes(15);
        Assert.True(p.MayAsk(Open()));
    }

    [Fact]
    public void RequestedCards_DoNotSpendTheGap()
    {
        var p = Planner(new Clock());
        p.NoteShown(unprompted: false);
        Assert.True(p.MayAsk(Open()));
    }

    [Fact]
    public void NoCard_WithinFiveMinutesOfAChatLine()
    {
        var clock = new Clock();
        var p = Planner(clock);
        Assert.False(p.MayAsk(Open(T0 - TimeSpan.FromMinutes(4))));
        Assert.True(p.MayAsk(Open(T0 - TimeSpan.FromMinutes(5))));
    }

    [Fact]
    public void NotNow_ParksThatKindForAnHour()
    {
        var clock = new Clock();
        var p = Planner(clock);
        p.NoteAnswer(AskKind.Watch, declined: true);
        Assert.False(p.KindReady(AskKind.Watch));
        Assert.True(p.KindReady(AskKind.Game));
        for (int i = 0; i < 20; i++) Assert.Equal(AskKind.Game, p.PickKind(new[] { AskKind.Watch, AskKind.Game }, new Random(i)));
        clock.Now = T0 + TimeSpan.FromMinutes(60);
        Assert.True(p.KindReady(AskKind.Watch));
    }

    [Fact]
    public void ThreeDeclinesInARow_HalveTheRateForTheDay()
    {
        var clock = new Clock();
        var p = Planner(clock);
        p.NoteAnswer(AskKind.Watch, true);
        p.NoteAnswer(AskKind.Game, true);
        p.NoteAnswer(AskKind.Watch, false); // a yes breaks the streak
        p.NoteAnswer(AskKind.Game, true);
        p.NoteAnswer(AskKind.Quests, true);
        Assert.False(p.Halved);
        p.NoteAnswer(AskKind.Session, true);
        Assert.True(p.Halved);
        Assert.Equal(TimeSpan.FromMinutes(30), p.CurrentGap);

        p.NoteShown(true);
        clock.Now = T0 + TimeSpan.FromMinutes(20);
        Assert.False(p.MayAsk(Open()));

        clock.Now = new DateTime(2026, 9, 26, 0, 30, 0, DateTimeKind.Utc);
        Assert.False(p.Halved);
        Assert.Equal(AskPlanner.Gap, p.CurrentGap);
    }

    [Fact]
    public void Feedback_OffersLovedMehSkip()
    {
        var card = AskCatalog.BuildFeedback(Sources(), "Alpha", "https://a.example/1", new Random(1), T0);
        Assert.Equal(new[] { AskAction.Loved, AskAction.Meh, AskAction.Skip }, card.Choices.Select(c => c.Action));
        Assert.Equal("Alpha", card.Subject);
    }
}
