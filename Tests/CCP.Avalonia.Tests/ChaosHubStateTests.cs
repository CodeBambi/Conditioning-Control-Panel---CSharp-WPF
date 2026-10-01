using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The hub and slot picker read the active save through Core's ChaosMetaStore, not samples.</summary>
public sealed class ChaosHubStateTests
{
    [Fact]
    public async Task Picker_and_hub_draw_the_real_saves()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var old = CoreSettings.Current.ChaosActiveSlot;
            ChaosSlotPickerWindow? picker = null;
            ChaosHubWindow? hub = null;
            try
            {
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                ChaosMetaStore.Save(new ChaosMetaState
                {
                    RunsCompleted = 10, Sparks = 321, Gold = 55,
                    CraftedItems = new Dictionary<string, int> { ["ragdoll"] = 1 },
                }, 1);
                ChaosMetaStore.SetActiveSlot(1);

                picker = new ChaosSlotPickerWindow();
                var text = Texts(picker);
                Assert.Contains("Slipping", text);        // ChaosRanks over slot 1's 10 descents
                Assert.Contains("New Journey", text);     // slot 2 opened by the ragdoll
                Assert.Contains("Stitched Shut", text);   // slot 3: nobody owns the porcelain
                Assert.Contains("✦ 321", text);

                // Erasing Save 2 warns it closes again only when no OTHER save owns the ragdoll (WPF copy).
                Assert.Contains("Save 2 is Stitched Shut again",
                    ChaosSlotPickerWindow.EraseMessage(2, new List<SlotSummary> { new() { Slot = 2, HasRagdoll = true } }));
                Assert.DoesNotContain("Stitched Shut again",
                    ChaosSlotPickerWindow.EraseMessage(2, ChaosMetaStore.AllSummaries()));

                hub = new ChaosHubWindow();
                Assert.Equal("Slipping", hub.FindControl<TextBlock>("MenuRank")!.Text);
                Assert.Equal("10", hub.FindControl<TextBlock>("StRuns")!.Text);
                Assert.Equal("321", hub.FindControl<TextBlock>("MenuSparks")!.Text);

                // The shelves are the real catalogue and their buttons spend the real save.
                Click(hub, "blank_eyes", "Unlock  \u2726120");
                Click(hub, BenchIds.ToyPocket1, "buy  \U0001FA99 50");
                var disk = ChaosMetaStore.Load(1);
                Assert.Equal((201, 1), (disk.Sparks, disk.LifetimeBoonLevels["blank_eyes"]));
                Assert.Equal((5, 1), (disk.Gold, disk.ToyPockets));
                Assert.Equal("201", hub.FindControl<TextBlock>("MenuSparks")!.Text);
            }
            finally
            {
                picker?.Close();
                hub?.Close();
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                ChaosMeta.Init();   // fresh in-memory state for the next test
                CoreSettings.Current.ChaosActiveSlot = old;
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>--render-view/--render-all build the hub: that must not run ChaosMeta.Init's
    /// refund/sanitize save (or any save) against the profile on disk.</summary>
    [Fact]
    public async Task Rendering_the_hub_writes_nothing()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var old = CoreSettings.Current.ChaosActiveSlot;
            ChaosHubWindow? hub = null;
            try
            {
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                // A retired habit: Init would refund it and save the slot.
                ChaosMetaStore.Save(new ChaosMetaState
                {
                    Sparks = 5, PurchasedUpgrades = new HashSet<string> { "magnet" },
                }, 1);
                ChaosMetaStore.SetActiveSlot(1);
                var path = ChaosMetaStore.SlotFilePath(1);
                var before = System.IO.File.ReadAllText(path);
                var files = System.IO.Directory.GetFiles(ChaosMetaStore.SaveFolder, "chaos_meta*");

                global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
                hub = new ChaosHubWindow();

                Assert.Equal(before, System.IO.File.ReadAllText(path));
                Assert.Equal(files, System.IO.Directory.GetFiles(ChaosMetaStore.SaveFolder, "chaos_meta*"));
            }
            finally
            {
                global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = false;
                hub?.Close();
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                CoreSettings.Current.ChaosActiveSlot = old;
                ChaosMeta.Init();
            }
            return Task.CompletedTask;
        });
    }

    private static void Click(Control root, string tag, string label)
    {
        var button = root.GetLogicalDescendants().OfType<Button>()
            .First(b => b.Tag as string == tag && (b.Content as TextBlock)?.Text == label);
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    private static List<string> Texts(Control root) =>
        root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
}
