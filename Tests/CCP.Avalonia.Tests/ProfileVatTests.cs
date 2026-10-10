using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The XP vat on the Trainer Card (WPF MainWindow.ProfileVat.cs + ProfileFaucet.cs). TRI-STATE: no descent
/// block and the jar does not exist; a block arms it; the tap holds earned XP until a full hold pours it.
/// App.Descent and the pour ledger are process-wide, so the class runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ProfileVatTests
{
    private static DescentBlock Block(int todayXp) => DescentReader.Parse(DescentReader.ParseWire(
        "{ \"devotion_days\": 3, \"vat\": { \"cap\": 4000, \"today_xp\": " + todayXp + ", \"fill_lip_pct\": 120 } }"))!;

    [Fact]
    public async Task NoBlockNoJar_ABlockArmsIt_AFullHoldPoursWhatTheTapHeld()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var s = CoreSettings.Current;
            var (xp, day, account) = (s.VatPouredTodayXp, s.VatPouredDayUtc, s.VatPouredAccount);
            var before = global::ConditioningControlPanel.Avalonia.App.Descent;
            var descent = new DescentService(_ => Task.FromResult<Newtonsoft.Json.Linq.JObject?>(null));
            global::ConditioningControlPanel.Avalonia.App.Descent = descent;
            (s.VatPouredTodayXp, s.VatPouredDayUtc, s.VatPouredAccount) = (0, null, null);
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                var page = shell.ProfilePage!;
                var glass = page.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
                var faucet = page.FindControl<Grid>("ProfileVatFaucet")!;
                var readout = page.FindControl<TextBlock>("ProfileVatReadout")!;
                var chip = page.FindControl<Border>("ProfileVatChip")!;

                // No block: the vat does not exist.
                shell.OnProfileVatVisibilityChanged(true);
                Assert.False(glass.IsVisible);
                Assert.False(faucet.IsVisible);
                Assert.False(readout.IsVisible);

                // A block with a vat arms the jar and the tap; the XP is HELD, not drawn.
                descent.SeedForTest(Block(1000));
                Assert.True(glass.IsVisible);
                Assert.True(faucet.IsVisible);
                Assert.True(readout.IsVisible);
                Assert.Equal(1000, shell.FaucetHeldXp);
                Assert.True(chip.IsVisible);
                Assert.NotNull(ToolTip.GetTip(faucet));

                // A short press pours nothing.
                shell.BeginFaucetCharge();
                Assert.True(shell.FaucetCharging);
                shell.AdvanceFaucetCharge(0.5);
                Assert.True(shell.FaucetCharging);
                Assert.Equal(1000, shell.FaucetHeldXp);

                // The full hold pours it all, and the ledger remembers across a restart.
                shell.AdvanceFaucetCharge(1.0);
                Assert.False(shell.FaucetCharging);
                Assert.Equal(0, shell.FaucetHeldXp);
                Assert.Equal(1000, s.VatPouredTodayXp);
                Assert.False(chip.IsVisible);

                // More XP lands: only the new part is held.
                descent.SeedForTest(Block(1400));
                Assert.Equal(400, shell.FaucetHeldXp);

                // The server withdraws the block: the jar is gone and the avatar is back at 104.
                descent.SeedForTest(null);
                Assert.False(glass.IsVisible);
                Assert.False(faucet.IsVisible);
                Assert.False(chip.IsVisible);
            }
            finally
            {
                shell.Close();
                global::ConditioningControlPanel.Avalonia.App.Descent = before;
                (s.VatPouredTodayXp, s.VatPouredDayUtc, s.VatPouredAccount) = (xp, day, account);
                CoreSettings.SaveImmediate();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheChargeArcIsEmptyAtZeroAndAWholeCircleAtOne()
    {
        Assert.Null(MainShellWindow.BuildChargeArc(0));
        Assert.IsType<global::Avalonia.Media.EllipseGeometry>(MainShellWindow.BuildChargeArc(1));
        Assert.IsType<global::Avalonia.Media.StreamGeometry>(MainShellWindow.BuildChargeArc(0.4));
    }
}
