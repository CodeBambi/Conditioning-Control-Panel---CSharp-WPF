using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.CompanionFx.cs OnCompanionTabVisibilityChanged, driven through ShowTab:
/// leaving the tab writes the drawers' state, entering restores it and asks an upgrader for v2
/// consent, a mod switch while shown repaints the roster, and quitting from the tab persists too.</summary>
public sealed class CompanionTabVisibilityTests
{
    [Fact]
    public Task TabEdgesPersistRestoreConsentAndModRepaint() => AvaloniaTestDispatcher.RunAsync(async () =>
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
        s.ActiveCompanionId = 0;
        s.CompanionProgressData[0] = new CompanionProgress { Level = 2 };
        // An upgrader: awareness already on under the legacy consent, v2 never explained.
        s.UseAwarenessV2 = true;
        s.AwarenessModeEnabled = true;
        s.AwarenessConsentGiven = true;
        s.AwarenessConsentShownV2 = false;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(shell.OwnedWindows.OfType<AwarenessConsentDialog>());   // not before the tab

            shell.ShowTab("companion");
            for (var i = 0; i < 200 && !shell.OwnedWindows.OfType<AwarenessConsentDialog>().Any(); i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Assert.Single(shell.OwnedWindows.OfType<AwarenessConsentDialog>());
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            Dispatcher.UIThread.RunJobs();

            var room = shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single();
            var workshop = (WorkshopRuntimeVm)room.FindControl<WorkshopAccordion>("WorkshopZone")!.DataContext!;
            var engine = (EngineRoomVm)room.FindControl<EngineRoomDrawer>("EngineZone")!.DataContext!;
            Assert.False(workshop.IsExpanded);

            // Leaving writes the drawers; asked once per process, so no second dialog on return.
            workshop.IsExpanded = true;
            shell.ShowTab("achievements");
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.CompanionSectionOpen["Workshop"]);
            Assert.False(s.CompanionSectionOpen["EngineRoom"]);

            // Entering restores them.
            s.CompanionSectionOpen["Workshop"] = false;
            s.CompanionSectionOpen["EngineRoom"] = true;
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            Assert.False(workshop.IsExpanded);
            Assert.True(engine.IsExpanded);
            Assert.Empty(shell.OwnedWindows.OfType<AwarenessConsentDialog>());

            // A mod switch while shown repaints the roster (WPF OnCompanionFxModChanged).
            var level = workshop.Parts.Roster.FindControl<TextBlock>("TxtCompanion0Level")!;
            Assert.Equal("Lv.2", level.Text);
            s.CompanionProgressData[0].Level = 7;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Lv.7", level.Text);

            // Quitting from the tab (WPF WindowChrome.cs:176). Asserted, not fail-proven: the close
            // also hides the room, so the hide edge writes the same state.
            engine.IsExpanded = false;
            workshop.IsExpanded = true;
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.CompanionSectionOpen["Workshop"]);
            Assert.False(s.CompanionSectionOpen["EngineRoom"]);
        }
        finally
        {
            s.AwarenessModeEnabled = false;
            s.AwarenessConsentGiven = false;
            s.CompanionSectionOpen.Clear();
            s.CompanionProgressData.Remove(0);
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
        }
    });
}
