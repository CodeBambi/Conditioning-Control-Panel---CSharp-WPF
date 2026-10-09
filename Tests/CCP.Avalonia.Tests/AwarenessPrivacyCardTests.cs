using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using AwarenessIntensity = ConditioningControlPanel.Avalonia.Views.Controls.Companion.AwarenessIntensity;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Z5 "What she can see" over real settings (WPF AwarenessPrivacyRuntimeVm), driven from a
/// shown shell: the dial's ON edge goes entitlement -> consent dialog, decline leaves her eyes shut,
/// accept turns awareness on and raises the older-pipeline band; chips, pause and wipe write for real.
/// The desktop is never read: X11ActiveWindow is disabled for the whole test assembly.</summary>
public sealed class AwarenessPrivacyCardTests
{
    [Fact]
    public void TestsNeverReadTheRealDesktop() => Assert.Equal("", X11ActiveWindow.ReadTitle());

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 400 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
    }

    private static async Task<bool> PressDial(MainShellWindow shell, AwarenessPrivacyViewModel vm,
        AwarenessIntensity stop, string? answer)
    {
        vm.Intensity = stop;
        if (answer != null)
        {
            await Until(() => shell.OwnedWindows.OfType<AwarenessConsentDialog>().Any());
            shell.OwnedWindows.OfType<AwarenessConsentDialog>().Single().FindControl<Button>(answer)!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        await vm.Pending;
        Dispatcher.UIThread.RunJobs();
        return CoreSettings.Current.AwarenessModeEnabled;
    }

    [Fact]
    public Task DialConsentBandChipsPauseAndWipeAreReal() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);   // no intro card over the shell (P52)
        s.AwarenessModeEnabled = false;
        s.AwarenessConsentGiven = false;
        s.AwarenessConsentShownV2 = false;
        s.AwarenessTitleAllowList = new();
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var ledger = Path.Combine(CorePaths.UserData, "awareness_ledger.json");
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.GetLogicalDescendants().OfType<ConditioningControlPanel.Avalonia.Views.Tabs.CompanionTabView>().Single().IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            var card = shell.GetLogicalDescendants().OfType<AwarenessPrivacyView>().Single();
            var vm = card.ViewModel!;
            Assert.Same(shell, TopLevel.GetTopLevel(card));
            Assert.Equal(AwarenessIntensity.Off, vm.Intensity);
            Assert.False(vm.IsLegacyPipeline);

            // No entitlement: the door is refused before any privacy explanation.
            CoreEntitlement.HasPremiumProvider = () => false;
            Assert.False(await PressDial(shell, vm, AwarenessIntensity.BroadStrokes, null));
            Assert.Empty(shell.OwnedWindows.OfType<AwarenessConsentDialog>());

            // Entitled: the consent dialog asks; declining writes nothing.
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.False(await PressDial(shell, vm, AwarenessIntensity.BroadStrokes, "BtnDecline"));
            Assert.False(s.AwarenessConsentGiven);
            Assert.Equal(AwarenessIntensity.Off, vm.Intensity);

            // Accepting opens her eyes, and the card says straight that the v2 protections are not here.
            Assert.True(await PressDial(shell, vm, AwarenessIntensity.BroadStrokes, "BtnAccept"));
            AvApp.WindowAwareness.Stop();
            Assert.Equal(AwarenessIntensity.BroadStrokes, vm.Intensity);
            Assert.True(vm.IsLegacyPipeline);
            Assert.False(vm.IsWireLive);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("companion_awareness_wire_legacy"), vm.WireLine);

            // Deny chips are the effective (seeded) list; removing one writes it.
            var first = vm.DenyList.First();
            int before = AwarenessPrivacyRules.EffectiveDenyList(s).Count;
            first.RemoveCommand.Execute(null);
            Assert.Equal(before - 1, AwarenessPrivacyRules.EffectiveDenyList(s).Count);
            Assert.Equal(before - 1, vm.DenyList.Count);

            // Pause is a check that lifts again.
            vm.PauseCommand.Execute(null);
            Assert.True(AwarenessPause.IsPaused());
            Assert.True(vm.IsPaused);
            vm.PauseCommand.Execute(null);
            Assert.False(AwarenessPause.IsPaused());

            // Wipe only through the two-step confirm.
            File.WriteAllText(ledger, "{}");
            card.WipeConfirm.ConfirmCommand.Execute(null);
            Assert.True(File.Exists(ledger));
            card.WipeConfirm.ArmCommand.Execute(null);
            card.WipeConfirm.ConfirmCommand.Execute(null);
            Assert.False(File.Exists(ledger));

            // Off is never gated.
            CoreEntitlement.HasPremiumProvider = () => false;
            Assert.False(await PressDial(shell, vm, AwarenessIntensity.Off, null));
            Assert.False(vm.IsLegacyPipeline);
        }
        finally
        {
            CoreEntitlement.HasPremiumProvider = oldPremium;
            AwarenessPause.Resume();
            AvApp.WindowAwareness.Stop();
            s.AwarenessModeEnabled = false;
            s.AwarenessConsentGiven = false;
            File.Delete(ledger);
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public Task RefreshTicksOnlyWhileTheRoomIsShown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tab = shell.GetLogicalDescendants().OfType<ConditioningControlPanel.Avalonia.Views.Tabs.CompanionTabView>().Single();
            var card = tab.GetLogicalDescendants().OfType<AwarenessPrivacyView>().Single();
            tab.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsRefreshing);
            tab.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.IsRefreshing);
        }
        finally
        {
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });
}
