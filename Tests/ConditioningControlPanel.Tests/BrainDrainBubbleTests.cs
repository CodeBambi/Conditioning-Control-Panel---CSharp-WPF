using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Bubbles v2, wave 2: the Brain Drain bubble. Roll gating, the overlay choice per MotionLevel,
/// the no-overlap rule and the persisted default. All of it pure - no WPF, no App, no compositor.
/// </summary>
public class BrainDrainBubbleTests
{
    private static readonly List<string> DefaultIds =
        new() { "flash", "subliminal", "pink", "spiral", "glitch", "htlink", "video" };

    // ---- roll gating ----

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Unowned_or_switched_off_never_enters_the_pool(bool owned, bool on)
    {
        var pool = BrainDrainBubble.RollPool(DefaultIds, owned, on);
        Assert.DoesNotContain(BrainDrainBubble.VariantId, pool);
        Assert.Equal(DefaultIds, pool);
    }

    [Fact]
    public void Owned_and_on_adds_exactly_one_entry()
    {
        var pool = BrainDrainBubble.RollPool(DefaultIds, v2Owned: true, settingOn: true);
        Assert.Equal(DefaultIds.Count + 1, pool.Count);
        Assert.Single(pool, id => id == BrainDrainBubble.VariantId);
        // The user's own choices survive intact and in order.
        Assert.Equal(DefaultIds, pool.Take(DefaultIds.Count));
    }

    [Fact]
    public void Weight_is_one_share_of_the_pool()
    {
        // Equal odds among the picked ids is what the trigger roll does (ids[rng.Next(Count)]),
        // so "comparable to the other trigger variants" means exactly 1/(n+1).
        var pool = BrainDrainBubble.RollPool(DefaultIds, true, true);
        int hits = 0;
        for (int i = 0; i < pool.Count; i++) if (pool[i] == BrainDrainBubble.VariantId) hits++;
        Assert.Equal(1, hits);
        Assert.Equal(1.0 / (DefaultIds.Count + 1), hits / (double)pool.Count, 10);
    }

    [Fact]
    public void An_empty_variant_list_still_gets_the_bubble_when_eligible()
    {
        // Someone who unticked every effect type but owns the prize: the pool is the drain alone,
        // which is the honest reading of "on" - not silently nothing.
        var pool = BrainDrainBubble.RollPool(new List<string>(), true, true);
        Assert.Equal(new[] { BrainDrainBubble.VariantId }, pool);
        // ...and a null list (a hand-edited settings.json) does not throw.
        Assert.Empty(BrainDrainBubble.RollPool(null, false, true));
    }

    [Fact]
    public void Already_present_id_is_not_duplicated()
    {
        var ids = new List<string> { "flash", BrainDrainBubble.VariantId };
        Assert.Equal(2, BrainDrainBubble.RollPool(ids, true, true).Count);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void IsEligible_needs_both(bool owned, bool on, bool expected)
        => Assert.Equal(expected, BrainDrainBubble.IsEligible(owned, on));

    // ---- overlay choice ----

    [Fact]
    public void Full_motion_melts_and_the_rest_do_not()
    {
        Assert.Equal(BrainDrainBubble.MeltKind, BrainDrainBubble.OverlayKindFor(MotionLevel.Full));
        Assert.Equal(BrainDrainBubble.PlainKind, BrainDrainBubble.OverlayKindFor(MotionLevel.Reduced));
        Assert.Equal(BrainDrainBubble.PlainKind, BrainDrainBubble.OverlayKindFor(MotionLevel.Off));
    }

    [Fact]
    public void Both_kinds_are_ones_the_overlay_primitive_accepts()
    {
        // ShowOverlayTimed switches on these two literals; a typo here would be a silent no-op.
        Assert.Equal("braindrain_melt", BrainDrainBubble.MeltKind);
        Assert.Equal("braindrain", BrainDrainBubble.PlainKind);
        Assert.Equal(BrainDrainBubble.MeltKind, BrainDrainBubble.VariantId);
    }

    [Fact]
    public void The_pop_holds_the_drain_for_ten_seconds()
        => Assert.Equal(10000, BrainDrainBubble.OverlayMs);

    [Theory]
    [InlineData(50, 0.50)]
    [InlineData(1, 0.01)]
    [InlineData(100, 1.00)]
    // Floored at 0 since 2026-09-21, not 1: the blur slider reaches 0 now and 0 means "no
    // picture", so clamping it up here would have left the last path that could blur a screen
    // which had asked for none. BrainDrainMeltPayload checks the dial and skips the overlay
    // outright, so the pop still pays its XP and shows nothing.
    [InlineData(0, 0.00)]
    [InlineData(250, 1.00)]
    [InlineData(-5, 0.00)]
    public void Opacity_tracks_the_users_blur_strength(int strength, double expected)
        => Assert.Equal(expected, BrainDrainBubble.OverlayOpacity(strength), 6);

    // ---- no overlap ----

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]    // a timed drain is still in flight
    [InlineData(false, true, false)]    // the user's own drain is up: do not fight it
    [InlineData(true, true, false)]
    public void Only_a_clear_screen_gets_the_overlay(bool timed, bool userDrain, bool expected)
        => Assert.Equal(expected, BrainDrainBubble.ShouldPlayOverlay(timed, userDrain));

    // ---- the breathe ----

    [Fact]
    public void The_pulse_stays_faint_and_never_brightens_the_bubble()
    {
        double min = 1.0, max = 0.0;
        for (int i = 0; i < 2000; i++)
        {
            double v = BrainDrainBubble.PulseAt(i * 0.02);
            min = System.Math.Min(min, v);
            max = System.Math.Max(max, v);
        }
        Assert.True(max <= 1.0 + 1e-9, "the pulse must never exceed the frame's own opacity");
        Assert.True(min >= 1.0 - BrainDrainBubble.PulseDepth - 1e-9);
        // It really does swing - a constant would be a dead feature that still passed the bounds.
        Assert.True(max - min > BrainDrainBubble.PulseDepth * 0.9);
    }

    // ---- settings ----

    [Fact]
    public void FreshInstall_BrainDrainBubbleOn()
        => Assert.True(new AppSettings().BubbleBrainDrainEnabled);

    [Fact]
    public void A_settings_file_without_the_key_still_lands_on_on()
    {
        var s = JsonConvert.DeserializeObject<AppSettings>("{}", new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        })!;
        Assert.True(s.BubbleBrainDrainEnabled);
    }

    [Fact]
    public void A_saved_off_is_honoured()
    {
        var s = JsonConvert.DeserializeObject<AppSettings>("{\"BubbleBrainDrainEnabled\": false}")!;
        Assert.False(s.BubbleBrainDrainEnabled);
    }
}
