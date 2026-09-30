using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Takeover on this head (shell-autonomy): the switch arms Core AutonomyScheduler only with
/// consent + entitlement, the state hero follows it, Lockdown refuses stopping it (#514) and panic
/// stops it (brief safety rule) without unticking the saved switch.</summary>
public sealed class TakeoverTests
{
    [Fact]
    public async Task SwitchArmsItLockdownHoldsItPanicStopsIt()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            s.AutonomyConsentGiven = true;
            s.AutonomyResumeOnStartup = false;
            var shell = new MainShellWindow();
            shell.Show();
            Border Pill() => shell.GetVisualDescendants().OfType<Border>().First(b => b.Name == "TakeoverActivePill");
            string? Text(string name) => shell.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Name == name).Text;
            LockdownService? ld = null;
            try
            {
                // No entitlement: the setting saves, nothing arms.
                CoreEntitlement.HasPremiumProvider = () => false;
                shell.SetAutonomyEnabled(true);
                Assert.False(shell.Autonomy.IsEnabled);

                CoreEntitlement.HasPremiumProvider = () => true;
                Assert.True(shell.SetAutonomyEnabled(true));
                Dispatcher.UIThread.RunJobs();
                Assert.True(shell.Autonomy.IsEnabled);
                Assert.True(Pill().IsVisible);

                // Lockdown: neither the switch nor the panic key stops a running Takeover.
                ld = LockdownService.Current = new LockdownService();
                ld.Activate(TimeSpan.FromMinutes(30));
                var escapes = 0;
                ld.EscapeAttempted += _ => escapes++;
                Assert.True(shell.SetAutonomyEnabled(false));
                Assert.Equal(0, escapes);   // WPF #514: the message only, no Stop tripwire
                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
                Assert.True(shell.Autonomy.IsEnabled);
                Assert.True(s.AutonomyModeEnabled);
                ld.Deactivate();

                // Panic stops her; the saved switch stays as the user set it.
                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 10));
                Dispatcher.UIThread.RunJobs();
                Assert.False(shell.Autonomy.IsEnabled);
                Assert.True(s.AutonomyModeEnabled);
                Assert.False(Pill().IsVisible);
                // The tab shows her stopped, too.
                Assert.Equal("○ DORMANT", Text("TxtTakeoverStatus"));

            }
            finally
            {
                shell.Autonomy.Stop();
                ld?.Dispose();
                LockdownService.Current = null;
                CoreEntitlement.HasPremiumProvider = null;
                s.AutonomyModeEnabled = s.AutonomyConsentGiven = false;
                CoreEngine.Stop();
                shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }
}
