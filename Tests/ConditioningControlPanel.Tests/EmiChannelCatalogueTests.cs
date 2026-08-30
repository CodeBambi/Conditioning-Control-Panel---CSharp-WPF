using System;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The glass channel catalogue, pinned. The 2026-08-30 campus port turned five channels into ten
/// plus an intrusion, and the ids live as bare strings in three switches (Feasible, Build, the
/// Savers set) that a typo desyncs without a compile error - a misspelled id is a channel that
/// simply never airs. These pins are the compile error.
/// </summary>
public class EmiChannelCatalogueTests
{
    [Fact]
    public void The_rotation_offers_the_campus_deck_in_order()
    {
        Assert.Equal(
            new[]
            {
                "pong", "spiral", "video", "burst", "rain",
                "browsing", "shop", "reruns", "coderain", "offair",
            },
            EmiChannels.All);
    }

    [Fact]
    public void Savers_watch_and_offers_fire()
    {
        // She is watching these; a tap only puts her face back.
        foreach (var id in new[] { "pong", "browsing", "shop", "reruns", "coderain", "offair", "wrong" })
            Assert.True(EmiChannels.IsSaver(id), id);

        // These are held out to you; a tap takes the offer.
        foreach (var id in new[] { "spiral", "video", "burst", "rain" })
            Assert.False(EmiChannels.IsSaver(id), id);
    }

    [Fact]
    public void Wrong_never_enters_the_rotation()
    {
        // It rides another channel's exit (the glass rolls for it on close) and lands there only.
        Assert.DoesNotContain("wrong", EmiChannels.All);
        Assert.False(EmiChannels.Feasible("wrong"));
    }

    [Fact]
    public void The_owner_unlocked_the_ten_second_rotation()
    {
        // "one every 10 sec or so is fine" (owner, 2026-08-30). The close resets the idle clock,
        // so these constants together are the ~10 s on / ~10 s off television rhythm, and the
        // fidget wheel's screen-beat floor agrees with the glass's own clock.
        Assert.Equal(TimeSpan.FromSeconds(10), EmiChannels.IdleBeforeFlip);
        Assert.Equal(TimeSpan.FromSeconds(10), EmiChannels.ChannelLife);
        Assert.Equal(10_000, EmiAlive.ScreenBeatRestMs);
    }
}
