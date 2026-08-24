using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CcpClient.Desktop;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// THE POPUP OPENS CENTRED ON ITS OWNER, not merely somewhere inside the working area.
///
/// <para><b>Why this fact exists now.</b> <c>FeaturePopupHeadlessTests</c> already pins
/// CONTAINMENT — the popup lands inside the owner monitor's working area — and containment is
/// exactly what a popup pinned to the screen's top-left corner also satisfies. That is not a
/// hypothetical: measured on WSLg 2026-08-24, this window opened at root <c>10,10</c> at scale 1
/// and <c>0,0</c> at scale 1.75 and 2.0, while the owner sat at root <c>16,37</c>, because the
/// <c>Position</c> write in <c>OnOpened</c> was dropped by Avalonia's X11 backend. Every existing
/// fact passed throughout.</para>
///
/// <para><b>What this fact does and does not reach.</b> It pins the OUTCOME — the centring
/// arithmetic reaching the window's position — and it reds if that arithmetic or its wiring
/// breaks. It CANNOT red on the Linux defect itself: headless honours the first write, so the
/// deferred re-assert that fixes X11 is invisible from here. That half is headed Linux evidence
/// and is recorded as such, not claimed by a test.</para>
/// </summary>
public class FeaturePopupPlacementHeadlessTests : HeadlessTest
{
    private async Task<(ApplicationHost Host, MainWindow Window)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-popupplacement-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);
        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();
        return (host!, window);
    }

    [AvaloniaFact]
    public async Task Popup_OpensCentredOnItsOwner_NotJustSomewhereInsideTheWorkingArea()
    {
        var (host, window) = await BootAsync();
        var popup = (FeaturePopupWindow)window.Popups.Show();
        popup.UpdateLayout();

        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary!;
        var ownerRect = new PixelRect(
            window.Position, PixelSize.FromSize(window.ClientSize, window.RenderScaling));
        var popupSize = PixelSize.FromSize(popup.ClientSize, popup.RenderScaling);
        var centred = PopupPlacement.CenteredClampedPosition(ownerRect, popupSize, screen.WorkingArea);

        // Named rather than asserted only as an equality: the failure this exists to catch is the
        // popup sitting at the working area's ORIGIN while every containment check still passes.
        Assert.Equal(centred, popup.Position);
        Assert.True(
            popup.Position != new PixelPoint(screen.WorkingArea.X, screen.WorkingArea.Y)
            || centred == new PixelPoint(screen.WorkingArea.X, screen.WorkingArea.Y),
            $"the popup is parked at the working area origin {screen.WorkingArea.X},{screen.WorkingArea.Y} "
            + $"while centring on the owner {ownerRect} would have put it at {centred.X},{centred.Y} — "
            + "which is what a dropped Position write looks like, and what containment alone cannot see");

        await host.ShutdownAsync();
    }
}
