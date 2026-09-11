using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services.Dashboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The slot rules, pinned away from any window. The picker, the renderer and the cloud adopt all
/// depend on <see cref="DashboardLayoutRule"/>, and the invariant that matters most is the quiet
/// one: a feature is on the wall at most once, so placing it where it already is moves it.
/// </summary>
public class DashboardLayoutRuleTests
{
    private const string DefaultWire =
        "flash,video|bubblecount,subliminal,bouncingtext,justdrop,spiral|pinkfilter,mindwipe|braindrain,bubbles,lockcard";

    private static DashboardLayout Empty() => new();

    private static IEnumerable<string> Keys(DashboardLayout l) =>
        l.Slots.SelectMany(s => new[] { s.Primary, s.Secondary }).Where(k => k != null)!;

    // ── wire ─────────────────────────────────────────────────────

    [Fact]
    public void Default_round_trips_through_the_wire()
    {
        var wire = DashboardLayoutRule.ToWire(DashboardLayout.Default());
        Assert.Equal(DefaultWire, wire);
        Assert.Equal(9, wire.Split(',').Length);
        Assert.True(wire.Length < 200, $"{wire.Length} bytes");
        var back = DashboardLayoutRule.FromWire(wire);
        Assert.True(back.IsDefault);
        Assert.Equal(wire, DashboardLayoutRule.ToWire(back));
    }

    [Fact]
    public void An_empty_slot_is_an_empty_field()
    {
        var l = DashboardLayout.Default();
        DashboardLayoutRule.Clear(l, 0);
        DashboardLayoutRule.Clear(l, 8);
        var wire = DashboardLayoutRule.ToWire(l);
        Assert.StartsWith(",", wire);
        Assert.EndsWith(",", wire);
        Assert.Equal(9, wire.Split(',').Length);
        Assert.Null(DashboardLayoutRule.FromWire(wire).Slots[0].Primary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,,,,,,")]              // every slot cleared promotes back to the default
    [InlineData("nonsense,rubbish")]
    public void An_unusable_wire_answers_with_the_default_wall(string? wire)
        => Assert.True(DashboardLayoutRule.FromWire(wire).IsDefault);

    [Fact]
    public void A_tenth_field_is_dropped_an_oversize_string_is_refused_and_spacing_survives()
    {
        var ten = DashboardLayoutRule.FromWire(DefaultWire + ",fyp");
        Assert.True(ten.IsDefault);
        Assert.DoesNotContain("fyp", Keys(ten));
        Assert.True(DashboardLayoutRule.FromWire(new string('a', 257)).IsDefault);

        var loose = DashboardLayoutRule.FromWire(" FLASH | Bubbles ,,,,,,,,fyp");
        Assert.Equal("flash", loose.Slots[0].Primary);
        Assert.Equal("bubbles", loose.Slots[0].Secondary);
        Assert.Equal("fyp", loose.Slots[8].Primary);
    }

    // ── sanitize ─────────────────────────────────────────────────

    [Fact]
    public void Sanitize_of_nothing_is_the_default_wall()
    {
        Assert.True(DashboardLayoutRule.Sanitize(null).IsDefault);
        Assert.True(DashboardLayoutRule.Sanitize(Empty()).IsDefault);
    }

    [Fact]
    public void Sanitize_blanks_unknown_keys()
    {
        var raw = Empty();
        raw.Slots[0].Primary = "flash";
        raw.Slots[1].Primary = "a_feature_from_a_newer_build";
        raw.Slots[2].Primary = "vault";   // a fixed cell is not a catalog row
        var clean = DashboardLayoutRule.Sanitize(raw);
        Assert.Equal("flash", clean.Slots[0].Primary);
        Assert.Null(clean.Slots[1].Primary);
        Assert.Null(clean.Slots[2].Primary);
    }

    [Fact]
    public void Sanitize_keeps_only_the_first_copy_of_a_key()
    {
        var raw = Empty();
        raw.Slots[0].Primary = "flash";
        raw.Slots[3].Primary = "flash";
        raw.Slots[3].Secondary = "bubbles";
        var clean = DashboardLayoutRule.Sanitize(raw);
        Assert.Equal("flash", clean.Slots[0].Primary);
        // The duplicate is blanked and the surviving half promoted, not dropped with it.
        Assert.Equal("bubbles", clean.Slots[3].Primary);
        Assert.Null(clean.Slots[3].Secondary);
        Assert.Equal(Keys(clean).Count(), Keys(clean).Distinct().Count());
    }

    [Fact]
    public void Sanitize_strips_a_secondary_that_cannot_be_a_split_half_and_leaves_the_input_alone()
    {
        var raw = Empty();
        raw.Slots[0].Primary = "justdrop"; raw.Slots[0].Secondary = "flash";  // a door has no half
        raw.Slots[1].Primary = "spiral";   raw.Slots[1].Secondary = "fyp";    // and is never one
        var clean = DashboardLayoutRule.Sanitize(raw);
        Assert.Equal("justdrop", clean.Slots[0].Primary);
        Assert.Null(clean.Slots[0].Secondary);
        Assert.Equal("spiral", clean.Slots[1].Primary);
        Assert.Null(clean.Slots[1].Secondary);
        Assert.Equal("flash", raw.Slots[0].Secondary);
    }

    // ── place ────────────────────────────────────────────────────

    [Fact]
    public void Place_into_an_empty_slot_is_Placed_and_over_an_occupant_is_Replaced()
    {
        var l = Empty();
        Assert.Equal(PlaceOutcome.Placed, DashboardLayoutRule.Place(l, 4, "fyp", false));
        Assert.Equal("fyp", l.Slots[4].Primary);
        Assert.Equal(PlaceOutcome.Replaced, DashboardLayoutRule.Place(l, 4, "flash", false));
        Assert.Equal("flash", l.Slots[4].Primary);
        Assert.DoesNotContain("fyp", Keys(l));
    }

    [Fact]
    public void Place_of_something_already_on_the_wall_is_MovedFrom()
    {
        var l = DashboardLayout.Default();
        Assert.Equal(PlaceOutcome.MovedFrom, DashboardLayoutRule.Place(l, 0, "lockcard", false));
        Assert.Equal("lockcard", l.Slots[0].Primary);
        Assert.True(l.Slots[8].IsEmpty);          // its old slot is now a hole
        Assert.DoesNotContain("flash", Keys(l));  // and the tile it landed on is gone
    }

    [Fact]
    public void Moving_the_primary_of_a_split_promotes_the_other_half()
    {
        var l = DashboardLayout.Default();
        Assert.Equal(PlaceOutcome.MovedFrom, DashboardLayoutRule.Place(l, 0, "video", false));
        Assert.Equal("bubblecount", l.Slots[1].Primary);
        Assert.Null(l.Slots[1].Secondary);
    }

    [Fact]
    public void Place_split_on_two_fx_is_Split_and_moves_the_half_off_its_old_slot()
    {
        var l = DashboardLayout.Default();
        Assert.Equal(PlaceOutcome.Split, DashboardLayoutRule.Place(l, 0, "bubbles", true));
        Assert.Equal("flash", l.Slots[0].Primary);
        Assert.Equal("bubbles", l.Slots[0].Secondary);
        Assert.True(l.Slots[0].IsSplit);
        Assert.True(l.Slots[7].IsEmpty);
    }

    [Theory]
    [InlineData("justdrop", "flash")]   // the occupant is a door
    [InlineData("flash", "justdrop")]   // the newcomer is a door
    [InlineData("flash", "flash")]      // one feature cannot be both halves
    public void Place_split_that_cannot_form_a_pair_is_RefusedNotSplittable(string sitting, string arriving)
    {
        var l = Empty();
        DashboardLayoutRule.Place(l, 5, sitting, false);
        Assert.Equal(PlaceOutcome.RefusedNotSplittable, DashboardLayoutRule.Place(l, 5, arriving, true));
        Assert.Null(l.Slots[5].Secondary);
    }

    [Fact]
    public void Place_split_on_an_empty_or_already_split_slot_is_RefusedNotSplittable()
    {
        var l = Empty();
        Assert.Equal(PlaceOutcome.RefusedNotSplittable, DashboardLayoutRule.Place(l, 3, "flash", true));
        DashboardLayoutRule.Place(l, 3, "flash", false);
        DashboardLayoutRule.Place(l, 3, "bubbles", true);
        Assert.Equal(PlaceOutcome.RefusedNotSplittable, DashboardLayoutRule.Place(l, 3, "spiral", true));
        Assert.Equal("bubbles", l.Slots[3].Secondary);
    }

    [Fact]
    public void Place_of_an_unknown_key_is_RefusedUnknownKey_and_changes_nothing()
    {
        var l = DashboardLayout.Default();
        var before = DashboardLayoutRule.ToWire(l);
        Assert.Equal(PlaceOutcome.RefusedUnknownKey, DashboardLayoutRule.Place(l, 0, "vault", false));
        Assert.Equal(PlaceOutcome.RefusedUnknownKey, DashboardLayoutRule.Place(l, 0, "vault", true));
        Assert.Equal(before, DashboardLayoutRule.ToWire(l));
    }

    [Fact]
    public void Every_outcome_is_reachable()
    {
        var l = Empty();
        var seen = new HashSet<PlaceOutcome>
        {
            DashboardLayoutRule.Place(l, 0, "flash", false),    // Placed
            DashboardLayoutRule.Place(l, 0, "spiral", false),   // Replaced
            DashboardLayoutRule.Place(l, 0, "bubbles", true),   // Split
            DashboardLayoutRule.Place(l, 1, "spiral", false),   // MovedFrom
            DashboardLayoutRule.Place(l, 1, "fyp", true),       // RefusedNotSplittable
            DashboardLayoutRule.Place(l, 1, "nope", false),     // RefusedUnknownKey
        };
        Assert.Equal(Enum.GetValues<PlaceOutcome>().OrderBy(o => o), seen.OrderBy(o => o));
    }

    [Fact]
    public void Place_never_leaves_the_same_key_in_two_slots()
    {
        var l = DashboardLayout.Default();
        var keys = FeatureCatalog.All.Select(f => f.Key).ToList();
        for (int i = 0; i < keys.Count; i++)
        {
            DashboardLayoutRule.Place(l, i % DashboardLayout.SlotCount, keys[i], i % 3 == 0);
            var placed = Keys(l).ToList();
            Assert.Equal(placed.Count, placed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.True(placed.Count <= DashboardLayout.SlotCount * 2);
        }
    }

    // ── clear, bounds and the layout itself ──────────────────────

    [Fact]
    public void Clear_empties_both_halves()
    {
        var l = DashboardLayout.Default();
        DashboardLayoutRule.Clear(l, 1);
        Assert.True(l.Slots[1].IsEmpty);
        Assert.False(l.IsDefault);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void A_slot_outside_the_grid_throws(int slot)
    {
        var l = DashboardLayout.Default();
        Assert.Throws<ArgumentOutOfRangeException>(() => DashboardLayoutRule.Place(l, slot, "flash", false));
        Assert.Throws<ArgumentOutOfRangeException>(() => DashboardLayoutRule.Clear(l, slot));
    }

    [Fact]
    public void A_layout_is_always_nine_slots_however_it_is_assigned()
    {
        var stub = new DashboardLayout { Slots = new[] { new DashboardSlot { Primary = "flash" } } };
        Assert.Equal(DashboardLayout.SlotCount, stub.Slots.Length);
        Assert.Equal("flash", stub.Slots[0].Primary);
        Assert.All(stub.Slots, s => Assert.NotNull(s));
    }
}
