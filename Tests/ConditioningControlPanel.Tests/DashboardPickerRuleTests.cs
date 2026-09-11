using System;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services.Dashboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The edit-mode rules, away from the window that draws them. Every question the picker asks the
/// user is decided here first, so "does this slot offer Split?" is answerable without a wall, a
/// mouse or a session - and so Phase F's rolodex answers it the same way rather than growing a
/// second opinion behind a bridge.
///
/// <para>One fact does a lot of work in these cases: the shipped wall already holds all eleven FX
/// keys, so <c>focusgaze</c> is the only splittable feature that is NOT on it. That makes it the
/// one key that can test "a new FX landing on an occupied slot" without the move rule catching it
/// first.</para>
/// </summary>
public class DashboardPickerRuleTests
{
    // ── the pencil ───────────────────────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void The_pencil_is_shown_unless_a_session_is_running(bool sessionLocked, bool shown)
        => Assert.Equal(shown, DashboardPickerRule.ShowPencil(sessionLocked));

    // ── the prompt matrix ────────────────────────────────────────

    [Fact]
    public void An_empty_slot_places_with_no_question()
    {
        var layout = DashboardLayout.Default();
        DashboardLayoutRule.Clear(layout, 0);
        Assert.Equal(PickPrompt.Place, DashboardPickerRule.Decide(layout, 0, "focusgaze"));
    }

    [Fact]
    public void An_occupied_single_that_can_share_offers_replace_or_split()
    {
        // Slot 0 is flash, one FX; focusgaze is another. Two FX in one cell is a split tile.
        Assert.Equal(PickPrompt.AskReplaceOrSplit,
            DashboardPickerRule.Decide(DashboardLayout.Default(), 0, "focusgaze"));
    }

    [Fact]
    public void An_occupied_single_that_cannot_share_offers_replace_only()
    {
        var layout = DashboardLayout.Default();

        // A door cannot be half of a tile, in either direction: not as the occupant...
        Assert.Equal(PickPrompt.AskReplaceOnly, DashboardPickerRule.Decide(layout, 4, "focusgaze"));
        // ... and not as the pick.
        Assert.Equal(PickPrompt.AskReplaceOnly, DashboardPickerRule.Decide(layout, 0, "fyp"));
    }

    [Fact]
    public void An_already_split_slot_is_full_and_offers_replace_only()
    {
        // Slot 1 is video|bubblecount. A third feature in one cell is not a tile.
        Assert.Equal(PickPrompt.AskReplaceOnly,
            DashboardPickerRule.Decide(DashboardLayout.Default(), 1, "focusgaze"));
    }

    [Fact]
    public void A_feature_already_on_the_wall_moves_without_a_question()
    {
        var layout = DashboardLayout.Default();

        // lockcard sits in slot 8; dropping it on an occupied slot 0 is a move, not a replacement
        // question, because the user is vacating a tile in the same gesture.
        Assert.Equal(PickPrompt.Move, DashboardPickerRule.Decide(layout, 0, "lockcard"));
        // Even onto an empty slot, and even from the far half of a split tile.
        DashboardLayoutRule.Clear(layout, 0);
        Assert.Equal(PickPrompt.Move, DashboardPickerRule.Decide(layout, 0, "bubblecount"));
    }

    [Fact]
    public void An_unknown_key_asks_rather_than_acting()
    {
        // Place refuses it downstream; what matters here is that nothing happens silently.
        Assert.Equal(PickPrompt.AskReplaceOnly,
            DashboardPickerRule.Decide(DashboardLayout.Default(), 0, "nonsense"));
        Assert.Equal(PickPrompt.AskReplaceOnly,
            DashboardPickerRule.Decide(DashboardLayout.Default(), 0, null));
    }

    [Fact]
    public void Every_prompt_the_matrix_can_produce_is_reachable()
    {
        // A guard against a fifth outcome being added and never wired to a button.
        var seen = new System.Collections.Generic.HashSet<PickPrompt>();
        var empty = DashboardLayout.Default();
        DashboardLayoutRule.Clear(empty, 0);

        seen.Add(DashboardPickerRule.Decide(empty, 0, "focusgaze"));
        seen.Add(DashboardPickerRule.Decide(DashboardLayout.Default(), 0, "focusgaze"));
        seen.Add(DashboardPickerRule.Decide(DashboardLayout.Default(), 1, "focusgaze"));
        seen.Add(DashboardPickerRule.Decide(DashboardLayout.Default(), 0, "lockcard"));

        Assert.Equal(Enum.GetValues<PickPrompt>().Length, seen.Count);
    }

    [Fact]
    public void A_slot_outside_the_wall_throws_rather_than_wrapping()
    {
        var layout = DashboardLayout.Default();
        Assert.Throws<ArgumentOutOfRangeException>(() => DashboardPickerRule.Decide(layout, -1, "flash"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DashboardPickerRule.Decide(layout, DashboardLayout.SlotCount, "flash"));
    }

    // ── split availability ───────────────────────────────────────

    [Fact]
    public void Split_is_offered_only_where_the_pair_can_actually_form()
    {
        var layout = DashboardLayout.Default();

        Assert.True(DashboardPickerRule.CanOfferSplit(layout, 0, "focusgaze"));   // FX + FX
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 1, "focusgaze"));  // already split
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 4, "focusgaze"));  // occupant is a door
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 0, "fyp"));        // pick is a door
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 0, "flash"));      // itself, both halves
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 0, "nonsense"));

        DashboardLayoutRule.Clear(layout, 0);
        Assert.False(DashboardPickerRule.CanOfferSplit(layout, 0, "focusgaze"));  // nothing to share with
    }

    [Fact]
    public void What_the_rule_offers_is_what_Place_accepts()
    {
        // The two must not drift: an offered Split that Place refuses is a dead button.
        foreach (var key in new[] { "focusgaze", "fyp", "flash", "lockcard" })
            for (int slot = 0; slot < DashboardLayout.SlotCount; slot++)
            {
                var probe = DashboardLayout.Default();
                var offered = DashboardPickerRule.CanOfferSplit(probe, slot, key);
                var outcome = DashboardLayoutRule.Place(probe, slot, key, split: true);
                Assert.Equal(offered, outcome == PlaceOutcome.Split);
            }
    }

    // ── what an edit writes ──────────────────────────────────────

    [Fact]
    public void Reset_writes_the_default_wire_and_stays_touched()
    {
        var (wire, touched) = DashboardPickerRule.Reset();

        Assert.Equal(DashboardLayoutRule.ToWire(DashboardLayout.Default()), wire);
        Assert.True(DashboardLayoutRule.FromWire(wire).IsDefault);
        // Touched records that the user has had an opinion, not that their wall differs from the
        // shipped one - the cloud adopt reads it, so a reset must not re-open the door to another
        // machine's layout.
        Assert.True(touched);
    }

    [Fact]
    public void Commit_writes_the_live_wall_and_marks_it_touched()
    {
        var layout = DashboardLayout.Default();
        DashboardLayoutRule.Place(layout, 0, "fyp", split: false);

        var (wire, touched) = DashboardPickerRule.Commit(layout);

        Assert.Equal(DashboardLayoutRule.ToWire(layout), wire);
        Assert.StartsWith("fyp,", wire, StringComparison.Ordinal);
        Assert.True(touched);
        Assert.False(DashboardLayoutRule.FromWire(wire).IsDefault);
    }
}
