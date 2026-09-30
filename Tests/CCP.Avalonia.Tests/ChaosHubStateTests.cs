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
            try
            {
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                ChaosMetaStore.Save(new ChaosMetaState
                {
                    RunsCompleted = 10, Sparks = 321, Gold = 55,
                    CraftedItems = new Dictionary<string, int> { ["ragdoll"] = 1 },
                }, 1);
                ChaosMetaStore.SetActiveSlot(1);

                var picker = new ChaosSlotPickerWindow();
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

                var hub = new ChaosHubWindow();
                Assert.Equal("Slipping", hub.FindControl<TextBlock>("MenuRank")!.Text);
                Assert.Equal("10", hub.FindControl<TextBlock>("StRuns")!.Text);
                Assert.Equal("321", hub.FindControl<TextBlock>("MenuSparks")!.Text);
            }
            finally
            {
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                CoreSettings.Current.ChaosActiveSlot = old;
            }
            return Task.CompletedTask;
        });
    }

    private static List<string> Texts(Control root) =>
        root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
}
