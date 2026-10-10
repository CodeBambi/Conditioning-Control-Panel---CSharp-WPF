using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Main sync #5 rows fc9640ff7 / abac4fa02 on this head: the bonus is for migrated accounts only (not a bare
/// curve_epoch 1 stamp), the server's ack settles and heals an account migrated on any device, and the own
/// Trainer Card wears the receipt + "(+N%)" readout. This head never takes the offer (no descent_auto).
/// </summary>
public sealed partial class AccountSeedTests
{
    [Fact]
    public void DescentAck_FromTheSync_SettlesTheAccount_AndOnlyThenPaysTheBonus() => WithFreshInstall(async () =>
    {
        var s = CoreSettings.Current;
        s.DescentEpoch = DescentEpochs.AccountDescent;   // a post-Descent record: epoch 1, never migrated
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        (s.DescentCycleXpBonus, s.DescentCycle, s.DescentMigrationChoice) = (1.0, 0, null);

        // No ack yet: a fresh epoch-1 record earns the plain amount (fc9640ff7).
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.False(s.DescentMigrationCompleted);
        var level = s.PlayerLevel;
        var before = s.PlayerXP;
        ProgressionBank.Add(100, "Session");
        Assert.Equal(before + 100, s.PlayerXP, 3);

        // The ack rides a sync: settled, bonus healed, and the ledger is not touched by it.
        wire.SyncReply = """{"success":true,"descent_migration":{"completed":true,"choice":"cycle"}}""";
        var ledgerBefore = (s.PlayerLevel, s.PlayerXP, s.HighestLevelEver);
        sync.UtcNow = () => T0.AddMinutes(1);                  // past the cooldown
        Assert.True(await sync.PushAsync("test"));
        Assert.True(s.DescentMigrationCompleted);
        Assert.Equal(DescentMigrationChoices.Cycle, s.DescentMigrationChoice);
        Assert.Equal(DescentCycleXp.CycleXpBonus, s.DescentCycleXpBonus);
        Assert.Equal(1, s.DescentCycle);
        Assert.Equal(level, s.PlayerLevel);
        Assert.Equal(ledgerBefore, (s.PlayerLevel, s.PlayerXP, s.HighestLevelEver));   // the ack never moves the ledger

        before = s.PlayerXP;
        ProgressionBank.Add(100, "Session");
        Assert.Equal(before + 110, s.PlayerXP, 3);
        Assert.All(wire.Syncs, b => Assert.True((bool)b["descent_auto"]!));   // this head takes the offer silently (restore, no window)
    });

    [Fact]
    public Task DescentReceipt_OwnCardOnly_WithTheBoostedReadout() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var s = CoreSettings.Current;
        var old = (s.OfflineMode, s.OfflineUsername, s.UserDisplayName, s.PlayerLevel, s.PlayerXP,
                   s.DescentMigrationCompleted, s.DescentMigrationChoice, s.PendingDescentMigrationChoice);
        MainShellWindow? shell = null;
        try
        {
            (s.OfflineMode, s.OfflineUsername, s.UserDisplayName, s.PlayerLevel, s.PlayerXP) = (true, "me", "me", 5, 120);
            (s.DescentMigrationCompleted, s.DescentMigrationChoice, s.PendingDescentMigrationChoice) = (true, "restore", null);
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("discord");   // You > Profile (the rail's BtnDiscordTab row is retired)
            for (var i = 0; i < 20; i++) { await Task.Yield(); Dispatcher.UIThread.RunJobs(); }

            var page = shell.ProfilePage!;
            var pill = page.FindControl<Border>("ProfileDescentReceipt")!;
            var xp = page.FindControl<TextBlock>("TxtProfileXpProgress")!;
            var needed = $"{XpCurve.GetXPForLevel(5, s.DescentEpoch):N0}";
            Assert.True(pill.IsVisible);
            Assert.Equal(Loc.Get("profile_cycle_receipt_restore"), page.FindControl<TextBlock>("ProfileDescentReceiptText")!.Text);
            Assert.Equal(Loc.GetF("profile_xp_progress_boosted", "120", needed, "10"), xp.Text);

            shell.SetProfileViewingSelf(false);                    // a searched card never wears your descent
            Assert.False(pill.IsVisible);

            s.DescentMigrationCompleted = false;                   // not acked: no receipt, no suffix
            await page.ViewMyProfileAsync();
            Assert.False(pill.IsVisible);
            Assert.Equal(Loc.GetF("profile_xp_progress", "120", needed), xp.Text);
        }
        finally
        {
            shell?.Close();
            (s.OfflineMode, s.OfflineUsername, s.UserDisplayName, s.PlayerLevel, s.PlayerXP,
             s.DescentMigrationCompleted, s.DescentMigrationChoice, s.PendingDescentMigrationChoice) = old;
        }
    });
}
