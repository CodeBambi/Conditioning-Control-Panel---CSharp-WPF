using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-025 slice b3: the covering video window's draw-level facts (verification-harness
/// evidence-class rule): the frame the backend presents lands on the window's Image, the
/// surface is black (letterbox), topmost covering shape. No rendered frames, no
/// presentation claims — the headed/WX steps own pixels.
/// </summary>
public class DtrhVideoWindowHeadlessTests
{
    [AvaloniaFact]
    public void PresentedFrame_LandsOnTheImage()
    {
        var video = new FakeVideo();
        var window = new DtrhVideoWindow(video);
        window.Show();

        var image = window.GetVisualDescendants().OfType<Image>().Single();
        Assert.Null(image.Source);
        Assert.Equal(Brushes.Black, window.Background);
        Assert.True(window.Topmost);

        var frame = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96));
        video.Current = frame;
        video.RaiseFrame();

        Assert.Same(frame, image.Source);
        Assert.Equal(Stretch.Uniform, image.Stretch);
        window.Close();
    }

    [AvaloniaFact]
    public void Close_Unsubscribes_NoThrowOnLateFrame()
    {
        var video = new FakeVideo();
        var window = new DtrhVideoWindow(video);
        window.Show();
        window.Close();
        // A late backend frame after close must not throw (teardown race class).
        video.RaiseFrame();
    }

    private sealed class FakeVideo : IDtrhVideoBackend
    {
        public WriteableBitmap? Current { get; set; }
        public long FrameCount => 0;
        public double PositionSec => 0;
        public WriteableBitmap? CurrentFrame => Current;
        public event EventHandler? FramePresented;
#pragma warning disable CS0067 // interface surface; this suite raises only FramePresented
        public event EventHandler? PlaybackEnded;
        public event EventHandler? EncounteredError;
#pragma warning restore CS0067
        public bool TryPlay(string path) => true;
        public void SetPaused(bool paused) { }
        public void Stop() { }
        public void RaiseFrame() => FramePresented?.Invoke(this, EventArgs.Empty);
    }
}
