using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The WPF half of <see cref="EmiPixelCanvas"/> (which lives in Core): one Bgra32
/// <see cref="WriteableBitmap"/> the buffer is blitted into on every <c>Commit()</c>.
/// </summary>
internal sealed class EmiPixelBitmap
{
    private readonly WriteableBitmap _bmp;

    public EmiPixelBitmap(EmiPixelCanvas canvas)
    {
        _bmp = new WriteableBitmap(canvas.W, canvas.H, 96, 96, PixelFormats.Bgra32, null);
        var all = new Int32Rect(0, 0, canvas.W, canvas.H);
        canvas.Committed += c => _bmp.WritePixels(all, c.Pixels, c.W * 4, 0);
    }

    /// <summary>The image source to hang on an <c>Image</c>. Stable for the canvas's life.</summary>
    public ImageSource Source => _bmp;
}
