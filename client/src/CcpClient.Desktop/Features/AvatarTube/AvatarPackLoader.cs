namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>A decoded pack: definition + per-clip BGRA frame cells (pure data, no platform bitmaps).</summary>
public sealed record AvatarPack(AvatarPackDef Def, IReadOnlyDictionary<int, byte[][]> Frames)
{
    public byte[] Frame(int clipId, int index) => Frames[clipId][index];
}

/// <summary>Pack sheet decode/slicing failed — the typed undecodable-asset input (never swallowed).</summary>
public sealed class AvatarPackLoadException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Loads a pack sheet through an injected stream opener (production: StandardAssetLoader
/// avares:// open; tests: in-memory/corrupt streams) and slices it into per-frame cells.
/// Pure — no Avalonia — so unit tests exercise the real decode/slice path; the platform
/// bitmaps are created from these cells one layer up.
/// </summary>
public static class AvatarPackLoader
{
    public static AvatarPack Load(AvatarPackDef def, Func<string, Stream> openSheet)
    {
        int width;
        int height;
        byte[] bgra;
        try
        {
            using var stream = openSheet(def.SheetPath);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            (width, height, bgra) = BmpCodec.Decode(memory.ToArray());
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
        {
            throw new AvatarPackLoadException($"pack '{def.Name}' sheet '{def.SheetPath}' is undecodable: {ex.Message}", ex);
        }

        var cols = def.Clips.Max(c => c.Frames);
        var rows = def.Clips.Max(c => c.ClipId) + 1;
        if (width != cols * def.CellWidth || height != rows * def.CellHeight)
        {
            throw new AvatarPackLoadException(
                $"pack '{def.Name}' sheet {width}x{height} does not match declared grid {cols * def.CellWidth}x{rows * def.CellHeight}");
        }

        var frames = new Dictionary<int, byte[][]>();
        foreach (var clip in def.Clips)
        {
            var cells = new byte[clip.Frames][];
            for (var frame = 0; frame < clip.Frames; frame++)
            {
                var cell = new byte[def.CellWidth * def.CellHeight * 4];
                for (var y = 0; y < def.CellHeight; y++)
                {
                    var src = ((clip.ClipId * def.CellHeight + y) * width + frame * def.CellWidth) * 4;
                    Array.Copy(bgra, src, cell, y * def.CellWidth * 4, def.CellWidth * 4);
                }

                // Self-check: a loaded frame's strip must round-trip to its own identity —
                // a generator/slicing drift fails loudly here instead of faking evidence later.
                if (!AvatarStripCodec.TryDecode(cell, def.CellWidth, def.CellHeight, out var packId, out var clipId, out var index, out var failure)
                    || packId != def.PackId || clipId != clip.ClipId || index != frame)
                {
                    throw new AvatarPackLoadException(
                        $"pack '{def.Name}' clip {clip.ClipId} frame {frame} strip self-check failed: {failure ?? "identity mismatch"}");
                }

                cells[frame] = cell;
            }

            frames[clip.ClipId] = cells;
        }

        return new AvatarPack(def, frames);
    }
}
