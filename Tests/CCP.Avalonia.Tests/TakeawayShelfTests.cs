using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Takeaway strip (WPF MainWindow.Takeaway.cs): three receipts pinned, the rest behind
/// "+n more", the "order a drop" door only while the server's door is open, and WPF's empty state
/// when there is neither. The door flag is process-wide, so this runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class TakeawayShelfTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static JustDropOrdersService.Order Order(int i, string size = "M", string name = "") => new()
    {
        Id = "o" + i, Code = "CODE" + i, Name = name, SizeId = size, At = DateTimeOffset.Now.AddDays(-i),
    };

    private static T Part<T>(PresetsTabView tab, string name) where T : Control => tab.FindControl<T>(name)!;

    [Fact]
    public Task ThreeArePinned_TheRestWaitInTheTray_AndTheDoorComesLast() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        bool door = JustDropService.DoorAvailable;
        try
        {
            JustDropService.SetServerEnabledForTests(true);
            var tab = new PresetsTabView();
            var orders = new[] { Order(0, "S", "Soft Start"), Order(1), Order(2, "XXL"), Order(3), Order(4, "?") };
            tab.TakeawayFetch = () => Task.FromResult<IReadOnlyList<JustDropOrdersService.Order>>(orders);

            await tab.LoadTakeawayShelfAsync();

            var chips = tab.TakeawayChips;
            Assert.Equal(PresetsTabView.TakeawayShelfCap + 2, chips.Count);                  // 3 receipts, "+2 more", the door
            Assert.Equal(new[] { "CODE0", "CODE1", "CODE2" },
                chips.Take(3).Select(c => ((JustDropOrdersService.Order)c.Tag!).Code));
            Assert.Null(chips[^1].Tag);                                                       // the door is not a receipt
            Assert.Equal(5, tab.TakeawayTrayRows);                                            // the tray lists every one
            Assert.False(tab.TakeawayTrayIsOpen);
            Assert.False(Part<Border>(tab, "TakeawayTrayHost").IsVisible);
            Assert.Equal(Loc.GetF("sd_takeaway_kept", 5), Part<TextBlock>(tab, "TxtTakeawayCount").Text);
            Assert.False(Part<TextBlock>(tab, "TxtTakeawayEmpty").IsVisible);
            Assert.True(Part<StackPanel>(tab, "TakeawayShelf").IsVisible);

            tab.ToggleTakeawayTrayForTest();
            Assert.True(Part<Border>(tab, "TakeawayTrayHost").IsVisible);
            tab.ToggleTakeawayTrayForTest();
            Assert.False(Part<Border>(tab, "TakeawayTrayHost").IsVisible);                    // it closes again

            Assert.Equal("Soft Start", PresetsTabView.SafeOrderName(orders[0]));
            Assert.Equal(Loc.Get("sd_takeaway_order_fallback"), PresetsTabView.SafeOrderName(orders[1]));
            Assert.Equal(Loc.GetF("takeaway_row_min", 60), PresetsTabView.FormatTakeawayMinutes(orders[2]));
            Assert.Equal("", PresetsTabView.FormatTakeawayMinutes(orders[4]));                // an unknown size claims no length
            Assert.Equal(Loc.Get("takeaway_today"), PresetsTabView.FormatTakeawayAge(orders[0]));
            Assert.Equal(Loc.GetF("takeaway_days_ago", 3), PresetsTabView.FormatTakeawayAge(orders[3]));
        }
        finally { JustDropService.SetServerEnabledForTests(door); }
    });

    [Fact]
    public Task NoOrdersAndNoDoor_IsTheEmptyState_AndTheDoorAloneIsForEveryone() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        bool door = JustDropService.DoorAvailable;
        try
        {
            JustDropService.SetServerEnabledForTests(false);
            var tab = new PresetsTabView();
            tab.TakeawayFetch = () => Task.FromResult<IReadOnlyList<JustDropOrdersService.Order>>(Array.Empty<JustDropOrdersService.Order>());
            await tab.LoadTakeawayShelfAsync();
            Assert.Empty(tab.TakeawayChips);
            Assert.True(Part<TextBlock>(tab, "TxtTakeawayEmpty").IsVisible);
            Assert.False(Part<StackPanel>(tab, "TakeawayShelf").IsVisible);
            Assert.Equal("", Part<TextBlock>(tab, "TxtTakeawayCount").Text);

            JustDropService.SetServerEnabledForTests(true);                                   // no tier is asked anywhere
            await tab.LoadTakeawayShelfAsync();
            var only = Assert.Single(tab.TakeawayChips);
            var words = ((StackPanel)only.Child!).Children.OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(Loc.Get("sd_takeaway_order"), words);
            Assert.DoesNotContain(words, w => (w ?? "").Contains("Prime", StringComparison.OrdinalIgnoreCase));
            Assert.False(Part<TextBlock>(tab, "TxtTakeawayEmpty").IsVisible);
        }
        finally { JustDropService.SetServerEnabledForTests(door); }
    });

    [Fact]
    public void TheDrawerDropsAReceiptWithNoCode_AndSizesMapToMinutes()
    {
        var parsed = JustDropOrdersService.Parse(
            "{\"orders\":[{\"id\":\"a\",\"code\":\"AAA\",\"name\":\"One\",\"sizeId\":\"L\",\"at\":1760000000000,\"paidOut\":true}," +
            "{\"id\":\"b\",\"name\":\"no code\"},{\"id\":\"c\",\"code\":\"CCC\",\"sizeId\":\"S\"}]}");
        Assert.Equal(new[] { "AAA", "CCC" }, parsed.Select(o => o.Code));
        Assert.Equal(30, parsed[0].Minutes);
        Assert.True(parsed[0].PaidOut);
        Assert.Equal(5, parsed[1].Minutes);
        Assert.Empty(JustDropOrdersService.Parse("not json"));
        Assert.Empty(JustDropOrdersService.Parse("{}"));
    }
}
