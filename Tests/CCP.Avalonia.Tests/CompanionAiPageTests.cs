using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 48f587200 / 9db154ab6: Companion > AI is a pill of the Companion section and shows the
/// room's live Connection drawer (expanded), the Behaviour + Triggers cells and the memory diary. This
/// head's room is not collapsed, so leaving the page hands every one back to the Chat page.</summary>
public sealed class CompanionAiPageTests
{
    [Fact]
    public System.Threading.Tasks.Task AiPillBorrowsTheLiveZonesAndGivesThemBack() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);   // P52
        s.CompanionSectionOpen.Clear();
        s.AwarenessModeEnabled = false;                         // no v2 consent dialog on the tab

        var oldModel = s.CompanionPrompt.AiModel;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("companion");
            var room = shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single();
            var accordion = room.FindControl<WorkshopAccordion>("WorkshopZone")!;
            var workshop = (WorkshopRuntimeVm)accordion.DataContext!;
            var page = shell.GetLogicalDescendants().OfType<AiPage>().Single();

            // The default: Workshop drawer closed. The page still gets the cells and gives them back,
            // and re-reads the Connection settings changed elsewhere (WPF tab.Vm.Sync).
            Dispatcher.UIThread.RunJobs();
            s.CompanionPrompt.AiModel = "probe-model";
            shell.ShowTab("companionai");
            Dispatcher.UIThread.RunJobs();
            Assert.True(page.IsVisualAncestorOf(workshop.Parts.Behavior));
            Assert.Equal("probe-model", ((EngineRoomVm)room.FindControl<EngineRoomDrawer>("EngineZone")!.DataContext!).OllamaModel);
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            Assert.False(page.IsVisualAncestorOf(workshop.Parts.Behavior));
            Assert.False(page.IsVisualAncestorOf(workshop.Parts.Triggers));

            workshop.IsExpanded = true;                         // the cells sit in their presenters
            Dispatcher.UIThread.RunJobs();
            var engine = room.FindControl<EngineRoomDrawer>("EngineZone")!;
            var diary = room.FindControl<MemoryDiaryView>("MemoryZone")!;
            Control[] zones = { engine, workshop.Parts.Behavior, workshop.Parts.Triggers, diary };
            Assert.All(zones, z => Assert.True(room.IsVisualAncestorOf(z)));
            ((EngineRoomVm)engine.DataContext!).IsExpanded = false;

            // The section strip draws the pill (it only draws pages this head can open).
            var strip = shell.PageStrip!;
            Assert.Contains(strip.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("label_ai_badge"));

            shell.ShowTab("companionai");
            Dispatcher.UIThread.RunJobs();
            Assert.True(page.IsVisible);
            Assert.Equal("companionai", shell.CurrentTab);
            Assert.All(zones, z => Assert.True(page.IsVisualAncestorOf(z)));
            Assert.True(((EngineRoomVm)engine.DataContext!).IsExpanded);   // WPF: Engine.IsExpanded = true

            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            Assert.All(zones, z => Assert.True(room.IsVisualAncestorOf(z)));
            Assert.True(accordion.IsVisualAncestorOf(workshop.Parts.Behavior));
            Assert.Single(room.GetVisualDescendants().OfType<EngineRoomDrawer>());

            // A second visit borrows again (the give-back list was cleared).
            shell.ShowTab("companionai");
            Dispatcher.UIThread.RunJobs();
            Assert.All(zones, z => Assert.True(page.IsVisualAncestorOf(z)));
            await System.Threading.Tasks.Task.CompletedTask;
        }
        finally
        {
            s.CompanionSectionOpen.Clear();
            s.CompanionPrompt.AiModel = oldModel;
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
        }
    });
}
