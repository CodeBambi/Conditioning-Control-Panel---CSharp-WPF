using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using CcpClient.Desktop.Features.Chaos;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-061: the tunnel backdrop window's declared policy (unit level, where the API allows —
/// the headed run owns pixels, z-order, and activation proofs): opaque black, borderless,
/// non-topmost, non-activating, non-focusable, no taskbar, no resize. The webview surface is
/// never attached here (that is the headed evidence's job).
/// </summary>
public class ChaosTunnelWindowHeadlessTests
{
    [AvaloniaFact]
    public void DeclaredPolicy_MatchesTheLayeringContract()
    {
        var window = new ChaosTunnelWindow(_ => { }, Path.Combine(Path.GetTempPath(), "ccp-sp061-headless-profile"));
        try
        {
            Assert.False(window.Topmost);                    // below every Topmost surface
            Assert.False(window.ShowInTaskbar);              // no taskbar button
            Assert.False(window.ShowActivated);              // never steals activation
            Assert.False(window.Focusable);                  // never takes focus
            Assert.False(window.CanResize);                  // WPF ResizeMode.NoResize parity
            Assert.Equal(WindowDecorations.None, window.WindowDecorations); // WPF WindowStyle.None parity
            Assert.Equal(Brushes.Black, window.Background);  // opaque black clear
            Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
            Assert.Equal(new PixelPoint(0, 0), window.Position);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShowAndSink_AreSafeWithoutAnAttachedSurface()
    {
        var window = new ChaosTunnelWindow(_ => { }, Path.Combine(Path.GetTempPath(), "ccp-sp061-headless-profile"));
        window.Show();
        window.SinkToBottom(); // handle may be null headless — the guard makes it a no-op
        window.Close();
    }
}
