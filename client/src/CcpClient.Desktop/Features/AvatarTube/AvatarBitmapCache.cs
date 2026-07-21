using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>
/// Per-pack cache of platform bitmaps for the decoded frame cells. Pack switch disposes the
/// old pack's bitmaps BEFORE the replacement activates (WPF disposal parity — never two
/// avatars); the fallback pack gets its own cache entry like any other.
/// </summary>
public sealed class AvatarBitmapCache : IDisposable
{
    private readonly Dictionary<(int Pack, int Clip, int Frame), WriteableBitmap> _bitmaps = [];
    private int _packId = -1;

    public WriteableBitmap Get(AvatarPack pack, int clipId, int frameIndex)
    {
        if (_packId != pack.Def.PackId)
        {
            DisposeBitmaps();
            _packId = pack.Def.PackId;
        }

        var key = (pack.Def.PackId, clipId, frameIndex);
        if (_bitmaps.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var cell = pack.Frame(clipId, frameIndex);
        var bitmap = new WriteableBitmap(
            new PixelSize(pack.Def.CellWidth, pack.Def.CellHeight),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
        {
            for (var y = 0; y < pack.Def.CellHeight; y++)
            {
                MarshalCopyRow(cell, y * pack.Def.CellWidth * 4, fb.Address + y * fb.RowBytes, pack.Def.CellWidth * 4);
            }
        }

        _bitmaps[key] = bitmap;
        return bitmap;
    }

    /// <summary>Frames currently held — the disposal-count leak signal for tests.</summary>
    public int CachedCount => _bitmaps.Count;

    private static void MarshalCopyRow(byte[] src, int srcOffset, IntPtr dst, int bytes)
    {
        System.Runtime.InteropServices.Marshal.Copy(src, srcOffset, dst, bytes);
    }

    private void DisposeBitmaps()
    {
        foreach (var bitmap in _bitmaps.Values)
        {
            bitmap.Dispose();
        }

        _bitmaps.Clear();
    }

    public void Dispose() => DisposeBitmaps();
}
