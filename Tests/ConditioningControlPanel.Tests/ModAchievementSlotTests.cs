using System;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Mod Creator's achievement art slots are derived from <see cref="Achievement.All"/>
/// (<see cref="ModAchievementSlots.Build"/>) rather than hand-listed, because the hand-listed
/// version rotted silently: it froze at 29 entries while the registry grew to 69, so mod authors
/// had no way to ship badge art for two thirds of the Trophy Case, and it still offered a slot
/// for achievements/how_many.png - a badge no achievement claims any more.
///
/// Nothing here needs a Dispatcher: the derivation is a pure function of the registry.
/// </summary>
public class ModAchievementSlotTests
{
    [Fact]
    public void EveryAchievementBadgeGetsASlot()
    {
        var slots = ModAchievementSlots.Build();
        var expected = Achievement.All.Values
            .Select(a => a.ImageName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(expected.Count, slots.Length);
        foreach (var image in expected)
            Assert.Contains(slots, s => s.Key == $"achievements/{image}");
    }

    /// <summary>
    /// Every dictionary in the editor (_imageSlots, _imageControls, _imageNames, _imageNameBoxes)
    /// is keyed by resource key, so a duplicate key would throw on the second CreateImageSlot.
    /// first_week_graduate and daily_maintenance share daily_maintenance.png, which is exactly
    /// the case a naive one-slot-per-achievement derivation would trip over.
    /// </summary>
    [Fact]
    public void SlotKeysAreUnique()
    {
        var slots = ModAchievementSlots.Build();
        var distinct = slots.Select(s => s.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(slots.Length, distinct);
    }

    /// <summary>A shared badge file is labelled with both achievements it feeds.</summary>
    [Fact]
    public void SharedBadgeFileNamesBothAchievements()
    {
        var slot = ModAchievementSlots.Build().Single(s => s.Key == "achievements/daily_maintenance.png");
        Assert.Contains(Achievement.All["first_week_graduate"].Name, slot.Name);
        Assert.Contains(Achievement.All["daily_maintenance"].Name, slot.Name);
    }

    /// <summary>
    /// The dead slot is gone. Art dropped on it could never appear anywhere in the app.
    /// </summary>
    [Fact]
    public void NoSlotForARetiredBadge()
    {
        Assert.DoesNotContain(ModAchievementSlots.Build(), s => s.Key == "achievements/how_many.png");
    }

    /// <summary>
    /// Slots follow registry order, which is roughly progression order - the property the old
    /// hand-written list had and the reason the derivation enumerates the dictionary as-is.
    /// </summary>
    [Fact]
    public void SlotsFollowRegistryOrder()
    {
        var slots = ModAchievementSlots.Build();
        var expected = Achievement.All.Values
            .Select(a => $"achievements/{a.ImageName}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(expected, slots.Select(s => s.Key).ToList());
    }

    /// <summary>
    /// A missing localization key must never print a raw achievement_x_name over an art slot -
    /// LocalizationManager echoes unknown keys back, and only 40 of the 69 achievements carry a
    /// name key today.
    /// </summary>
    [Fact]
    public void NoSlotLabelIsARawLocalizationKey()
    {
        foreach (var slot in ModAchievementSlots.Build())
        {
            Assert.False(string.IsNullOrWhiteSpace(slot.Name));
            Assert.DoesNotContain("achievement_", slot.Name, StringComparison.Ordinal);
        }
    }
}
