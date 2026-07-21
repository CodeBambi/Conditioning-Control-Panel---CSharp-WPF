using Avalonia.Controls;
using Avalonia.Threading;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The covering native video window (b3; WPF mandatory-video outcome — a window that
/// covers the game while a payload video plays). Presents the libvlc vmem frames the
/// <see cref="IDtrhVideoBackend"/> swaps on the UI thread. Frame refresh is event-driven
/// (<see cref="IDtrhVideoBackend.FramePresented"/>), never a render timer.
/// </summary>
public partial class DtrhVideoWindow : Window
{
    private readonly IDtrhVideoBackend _video;

    public DtrhVideoWindow(IDtrhVideoBackend video)
    {
        _video = video;
        InitializeComponent();
        _video.FramePresented += OnFramePresented;
        Closed += (_, _) => _video.FramePresented -= OnFramePresented;
    }

    private void OnFramePresented(object? sender, System.EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnFramePresented(sender, e));
            return;
        }

        var frame = _video.CurrentFrame;
        if (frame is not null && !ReferenceEquals(FrameImage.Source, frame))
        {
            FrameImage.Source = frame;
        }
    }
}
