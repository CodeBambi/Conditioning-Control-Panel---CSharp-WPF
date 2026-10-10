using Avalonia.Media.Imaging;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>The moving half of an online flash (WPF FlashService heartbeat: window.ClipPath ->
    /// FlashClipPlayer.Start). The poster shows until the clip's first frame lands; the clip dies
    /// with the window.</summary>
    internal sealed partial class FlashOverlayWindow
    {
        private FlashClipPlayer? _clip;

        /// <summary>True once a clip player took this flash.</summary>
        internal bool HasClip => _clip != null;

        /// <summary>Play <paramref name="path"/> silently and looping over the poster, at the GIF
        /// speed <paramref name="speed"/>. False (poster stays) when no player could start.</summary>
        internal bool PlayClip(LibVLC? vlc, string path, double speed)
        {
            if (_clip != null || _closed) return false;
            var size = (_image.Source as Bitmap)?.PixelSize;
            int w = size?.Width ?? 320, h = size?.Height ?? 240;
            _clip = FlashClipPlayer.Start(vlc, path, w, h, frame =>
            {
                if (_closed) return;
                if (!ReferenceEquals(_image.Source, frame)) _image.Source = frame;
                _image.InvalidateVisual();
            }, speed);
            if (_clip == null) return false;
            Closed += (_, _) =>
            {
                _clip?.Dispose();
                if (_image.Source is WriteableBitmap clipFrame) clipFrame.Dispose();
                _clip = null;
            };
            return true;
        }
    }
}
