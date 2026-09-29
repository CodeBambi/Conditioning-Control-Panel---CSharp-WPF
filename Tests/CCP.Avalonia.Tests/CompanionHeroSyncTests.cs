using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Companion room's hero reads real state and its quick actions reach the shell
/// (WPF CompanionHeroRuntimeVm): no more "Bambi, level 41" exhibit.</summary>
public sealed class CompanionHeroSyncTests
{
    [Fact]
    public Task HeroSyncsFromSettingsAndQuickActionsWriteThroughTheShell() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.ActiveCompanionId = 2;
        s.CompanionProgressData[2] = new CompanionProgress { Level = 3, CurrentXP = 10 };
        s.AvatarMuted = false;
        s.AvatarEnabled = true;
        s.AwarenessModeEnabled = false;
        s.AiChatEnabled = false;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");   // the user's path in
            Dispatcher.UIThread.RunJobs();
            var vm = global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(shell).OfType<CompanionHeroCard>().Single().ViewModel!;
            Assert.Same(shell, vm.Shell);

            Assert.Equal(3, vm.Level);
            Assert.Equal(Loc.GetF("companion_hero_next_level_fmt", 4), vm.NextLevelLabel);
            Assert.Contains(CompanionDefinition.GetById(2).GetDisplayName(false), vm.Name);
            Assert.Equal(Loc.Get("companion_hero_pill_ai_off"), vm.AiPillText);
            Assert.Equal(Loc.Get("companion_hero_pill_eyes_closed"), vm.AwarenessPillText);

            vm.ToggleMuteCommand.Execute(null);
            Assert.True(s.AvatarMuted);
            Assert.True(vm.IsMuted);

            vm.ToggleShownCommand.Execute(null);
            Assert.False(s.AvatarEnabled);
            Assert.False(vm.IsCompanionShown);

            shell.ShowTab("achievements");
            Dispatcher.UIThread.RunJobs();
            s.CompanionProgressData[2].Level = 5;   // changed while the tab was hidden
            shell.ShowTab("companion");             // re-read on return, not on a timer
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, vm.Level);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
