using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>win-pink-rush from startup: StartEngine arms the 10-minute check only for an owner, a roll under
/// 0.50 starts 60 s of 3x XP with the popup, it expires on the stepped clock, and panic ends it
/// (WPF SkillTreeService #region Pink Rush, MainWindow.Enhancements.cs:2121).</summary>
public sealed class PinkRushTests
{
    [Fact]
    public async Task EngineArmsTheRollTheWindowPaysThreeTimesAndPanicEndsIt()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.PlayerLevel, s.PlayerXP, s.OfflineMode, s.OfflineUsername, s.SuppressPerkNotifications,
                s.PanicKeyEnabled, s.PanicKey, s.UnlockedSkills, s.FlashEnabled);
            var (now, roll) = (PinkRushHost.Now, PinkRushHost.Roll);
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            var clock = t0;
            PinkRushHost.Now = () => clock;
            (s.PlayerLevel, s.PlayerXP, s.OfflineMode, s.OfflineUsername) = (1, 0, true, "pink-rush-test");
            (s.SuppressPerkNotifications, s.PanicKeyEnabled, s.PanicKey, s.FlashEnabled) = (false, true, "F8", true);
            s.UnlockedSkills = new() { };
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;

                // Not owned: the engine never arms the check, and a winning roll does nothing.
                shell.StartEngine();
                Assert.False(PinkRushHost.IsChecking);
                PinkRushHost.Roll = () => 0.0;
                PinkRushHost.CheckTick();
                Assert.False(s.PinkRushActive);
                MainShellWindow.StopEngine();

                // Owned: a session start with the engine off arms it too (WPF BtnStart -> StartEngine).
                s.UnlockedSkills = new() { PinkRushRules.SkillId };
                var runner = global::ConditioningControlPanel.Avalonia.App.Sessions =
                    new ConditioningControlPanel.Services.SessionRunner(new ConditioningControlPanel.Services.SessionLogService());
                try
                {
                    shell.StartSession(new ConditioningControlPanel.Models.Session { Id = "pink_rush_test", Name = "Pink Rush Test", Icon = "🧪", DurationMinutes = 5 });
                    Assert.True(PinkRushHost.IsChecking);
                }
                finally
                {
                    runner.Stop();
                    global::ConditioningControlPanel.Avalonia.App.Sessions = null;
                    CoreSession.IsSessionRunningProvider = null;
                }
                MainShellWindow.StopEngine();
                Assert.False(PinkRushHost.IsChecking);

                // Owned: armed by StartEngine; a 0.50 roll loses, 0.49 wins.
                shell.StartEngine();
                Assert.True(PinkRushHost.IsChecking);
                PinkRushHost.Roll = () => 0.50;
                PinkRushHost.CheckTick();
                Assert.False(s.PinkRushActive);
                PinkRushHost.Roll = () => 0.49;
                PinkRushHost.CheckTick();
                Assert.True(s.PinkRushActive);
                Assert.Equal(t0.AddSeconds(60), s.PinkRushEndTime);
                Assert.True(PinkRushHost.Popup?.IsVisible);

                // WPF AddXP: amount * GetTotalXpMultiplier() * cycle; with no additive skill this is 1.0 * 3.0.
                ProgressionBank.Add(10, "Bubble");
                Assert.Equal(10 * (1.0 * 3.0) * ConditioningControlPanel.Services.Descent.DescentCycleXp.XpBonusFor(s), s.PlayerXP, 6);

                // The window ends on the clock, not before; the 3x and the popup go with it.
                clock = t0.AddSeconds(59);
                PinkRushHost.ExpiryTick();
                Assert.True(s.PinkRushActive);
                clock = t0.AddSeconds(60);
                PinkRushHost.ExpiryTick();
                Assert.False(s.PinkRushActive);
                Assert.Null(PinkRushHost.Popup);
                Assert.Equal(1.0, PinkRushRules.XpFactor(s));

                // Suppressed perk announcements: the rush runs, the popup does not show.
                s.SuppressPerkNotifications = true;
                PinkRushHost.CheckTick();
                Assert.True(s.PinkRushActive);
                Assert.Null(PinkRushHost.Popup);
                s.SuppressPerkNotifications = false;
                PinkRushHost.End();

                // An engine stop ends a running rush and disarms the check.
                PinkRushHost.CheckTick();
                Assert.NotNull(PinkRushHost.Popup);
                MainShellWindow.StopEngine();
                Assert.False(s.PinkRushActive);
                Assert.Null(PinkRushHost.Popup);
                Assert.False(PinkRushHost.IsChecking);

                // Panic ends a rush even with no engine to stop (its own PanicSurfaces row).
                PinkRushHost.Begin();
                Assert.NotNull(PinkRushHost.Popup);
                shell.HandlePanicKeyPress(t0.AddMinutes(5));
                Assert.False(s.PinkRushActive);
                Assert.Null(PinkRushHost.Popup);
                Assert.False(PinkRushHost.IsChecking);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                PinkRushHost.Stop();
                (PinkRushHost.Now, PinkRushHost.Roll) = (now, roll);
                (s.PlayerLevel, s.PlayerXP, s.OfflineMode, s.OfflineUsername, s.SuppressPerkNotifications,
                    s.PanicKeyEnabled, s.PanicKey, s.UnlockedSkills, s.FlashEnabled) = saved;
                shell.Close();
            }
            return Task.CompletedTask;
        });
    }
}
