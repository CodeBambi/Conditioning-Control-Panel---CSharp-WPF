using System.Security.Cryptography;
using System.Text.Json;

namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>
/// Deterministic generator of the two synthetic AvatarTube packs (SP-015 contract-named
/// deliverable). "Mod switching" in this demonstrator = switching between THESE TWO packs,
/// both routed through the SP-009 embedded-asset manifest (embedded entries, case-exact
/// IDs) — there is no mod loader (SP-009: the schema covers mod sources; instances do not).
///
/// Machine-checkable by construction: every frame carries the <see cref="AvatarStripCodec"/>
/// counter strip (pack + clip + frame index) and every clip's delays are NON-UNIFORM — a
/// uniform-delay asset cannot falsify multiplied-speed. Generation is pure integer math
/// (no PRNG, no wall clock): the same definitions always produce the same pixels, which the
/// determinism test proves by regenerating in memory and comparing against the committed
/// assets. File hashes are recorded in the task record.
/// </summary>
public static class SyntheticAvatarPacks
{
    public const int CellWidth = 96;
    public const int CellHeight = 128;

    // Clip identities (row in the sheet == clipId). 3-bit strip field.
    public const int ClipPoses = 0;
    public const int ClipIdle = 1;
    public const int ClipIdle2 = 2;
    public const int ClipTalk = 3;
    public const int ClipReaction = 4;
    public const int ClipClick = 5;
    public const int ClipFallback = 7;

    /// <summary>Fallback pack/clip identity rendered by the undecodable-asset static fallback (pack field is 2 bits: 0,1 = packs, 3 = fallback).</summary>
    public const int FallbackPackId = 3;

    public const string CircuitSheetPath = "Assets/avatar/pack-circuit.bmp";
    public const string CircuitDefPath = "Assets/avatar/pack-circuit.json";
    public const string PulseSheetPath = "Assets/avatar/pack-pulse.bmp";
    public const string PulseDefPath = "Assets/avatar/pack-pulse.json";
    public const string FallbackPath = "Assets/avatar/avatar-fallback.bmp";

    /// <summary>The two pack definitions — the single source the committed JSON is generated from.</summary>
    public static readonly AvatarPackDef Circuit = new(
        PackId: 0,
        Name: "circuit",
        CellWidth: CellWidth,
        CellHeight: CellHeight,
        SheetPath: CircuitSheetPath,
        Clips:
        [
            new AvatarClipDef(ClipPoses, "poses", 4, [1250, 1000, 1400, 1100]),
            new AvatarClipDef(ClipIdle, "idle", 6, [640, 480, 820, 550, 700, 600]),
            new AvatarClipDef(ClipIdle2, "idle2", 5, [530, 760, 470, 690, 580]),
            new AvatarClipDef(ClipTalk, "talk", 4, [450, 620, 500, 730]),
            new AvatarClipDef(ClipReaction, "reaction", 5, [520, 880, 430, 610, 560]),
            new AvatarClipDef(ClipClick, "click", 4, [500, 660, 440, 590]),
        ]);

    public static readonly AvatarPackDef Pulse = new(
        PackId: 1,
        Name: "pulse",
        CellWidth: CellWidth,
        CellHeight: CellHeight,
        SheetPath: PulseSheetPath,
        Clips:
        [
            new AvatarClipDef(ClipPoses, "poses", 4, [1150, 1350, 950, 1250]),
            new AvatarClipDef(ClipIdle, "idle", 6, [700, 520, 860, 610, 660, 560]),
            new AvatarClipDef(ClipIdle2, "idle2", 5, [590, 680, 510, 720, 620]),
            new AvatarClipDef(ClipTalk, "talk", 4, [540, 470, 680, 590]),
            new AvatarClipDef(ClipReaction, "reaction", 5, [620, 460, 840, 510, 670]),
            new AvatarClipDef(ClipClick, "click", 4, [560, 480, 640, 520]),
        ]);

    public static readonly AvatarPackDef[] All = [Circuit, Pulse];

    /// <summary>Generates one pack's sheet pixels (pure, deterministic) — also used by the determinism test.</summary>
    public static byte[] GenerateSheetPixels(AvatarPackDef def, out int sheetWidth, out int sheetHeight)
    {
        var cols = def.Clips.Max(c => c.Frames);
        var rows = def.Clips.Max(c => c.ClipId) + 1;
        sheetWidth = cols * def.CellWidth;
        sheetHeight = rows * def.CellHeight;
        var bgra = new byte[sheetWidth * sheetHeight * 4];
        // Pre-fill the whole grid with the pack background: unused cells (clips with fewer
        // frames than the widest clip) are opaque background, never alpha-0 holes.
        Fill(bgra, sheetWidth, 0, 0, sheetWidth, sheetHeight,
            def.PackId == 0 ? (byte)0x10 : (byte)0x1C,
            def.PackId == 0 ? (byte)0x10 : (byte)0x12,
            def.PackId == 0 ? (byte)0x1C : (byte)0x08);

        foreach (var clip in def.Clips)
        {
            for (var frame = 0; frame < clip.Frames; frame++)
            {
                PaintCell(bgra, sheetWidth, def, clip, frame);
                // The strip writes ONLY its marker+bit blocks into the painted cell (last
                // write wins inside the strip region; the rest of the art is untouched).
                var cell = ReadCell(bgra, sheetWidth, def, clip, frame);
                AvatarStripCodec.Encode(cell, def.CellWidth, def.CellHeight, def.PackId, clip.ClipId, frame);
                BlitCell(bgra, sheetWidth, def, clip, frame, cell);
            }
        }

        return bgra;
    }

    private static byte[] ReadCell(byte[] sheet, int sheetWidth, AvatarPackDef def, AvatarClipDef clip, int frame)
    {
        var cell = new byte[def.CellWidth * def.CellHeight * 4];
        var x0 = frame * def.CellWidth;
        var y0 = clip.ClipId * def.CellHeight;
        for (var y = 0; y < def.CellHeight; y++)
        {
            var src = ((y0 + y) * sheetWidth + x0) * 4;
            Array.Copy(sheet, src, cell, y * def.CellWidth * 4, def.CellWidth * 4);
        }

        return cell;
    }

    /// <summary>Generates the static fallback SHEET (undecodable-asset path): one valid
    /// strip-marked frame at row 7 (row == clipId layout, like the packs; loader grid math
    /// is uniform). Rows 0-6 stay background — they are never displayed.</summary>
    public static byte[] GenerateFallbackPixels(out int width, out int height)
    {
        width = CellWidth;
        height = CellHeight * (ClipFallback + 1);
        var bgra = new byte[width * height * 4];
        Fill(bgra, width, 0, 0, width, height, 0x14, 0x14, 0x18);

        // Dark neutral frame + gray concentric rings: visibly a valid STATIC avatar, and
        // visibly NOT either pack (gray, no neon) so a fallback can never pass as a live pack.
        var yOff = ClipFallback * CellHeight;
        for (var ring = 0; ring < 4; ring++)
        {
            var inset = 10 + ring * 9;
            var shade = (byte)(0x60 + ring * 0x20);
            StrokeRect(bgra, width, inset, yOff + inset + 8, width - inset, yOff + CellHeight - inset - 8, 2, shade, shade, shade);
        }

        var frame = new byte[CellWidth * CellHeight * 4];
        for (var y = 0; y < CellHeight; y++)
        {
            Array.Copy(bgra, ((yOff + y) * width) * 4, frame, y * CellWidth * 4, CellWidth * 4);
        }

        AvatarStripCodec.Encode(frame, CellWidth, CellHeight, FallbackPackId, ClipFallback, 0);
        for (var y = 0; y < CellHeight; y++)
        {
            Array.Copy(frame, y * CellWidth * 4, bgra, ((yOff + y) * width) * 4, CellWidth * 4);
        }

        return bgra;
    }

    /// <summary>Serializes a pack definition to its committed JSON form (indented for reviewable diffs).</summary>
    public static string SerializeDef(AvatarPackDef def)
    {
        var doc = new
        {
            version = 1,
            packId = def.PackId,
            name = def.Name,
            cellWidth = def.CellWidth,
            cellHeight = def.CellHeight,
            sheet = def.SheetPath,
            clips = def.Clips.Select(c => new { clipId = c.ClipId, name = c.Name, frames = c.Frames, delaysMs = c.DelaysMs }),
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>
    /// Parses and validates a pack definition. Delays are per-frame and MUST be non-empty
    /// and one-per-frame; non-uniformity is asserted by the test suite (a uniform clip
    /// cannot falsify multiplied-speed — packet rule), not enforced at parse.
    /// </summary>
    public static bool TryParseDef(string json, out AvatarPackDef? def, out string? error)
    {
        def = null;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "root: must be an object";
                return false;
            }

            if (!root.TryGetProperty("version", out var version) || version.GetInt32() != 1)
            {
                error = "version: must be 1";
                return false;
            }

            var packId = root.GetProperty("packId").GetInt32();
            var name = root.GetProperty("name").GetString() ?? "";
            var cellWidth = root.GetProperty("cellWidth").GetInt32();
            var cellHeight = root.GetProperty("cellHeight").GetInt32();
            var sheet = root.GetProperty("sheet").GetString() ?? "";
            if (packId is < 0 or >= 1 << AvatarStripCodec.PackBits || string.IsNullOrWhiteSpace(name)
                || cellWidth < AvatarStripCodec.StripWidth || cellHeight < AvatarStripCodec.StripHeight + 8
                || string.IsNullOrWhiteSpace(sheet))
            {
                error = "identity/geometry fields out of range";
                return false;
            }

            var clips = new List<AvatarClipDef>();
            foreach (var element in root.GetProperty("clips").EnumerateArray())
            {
                var clipId = element.GetProperty("clipId").GetInt32();
                var clipName = element.GetProperty("name").GetString() ?? "";
                var frames = element.GetProperty("frames").GetInt32();
                var delays = element.GetProperty("delaysMs").EnumerateArray().Select(d => d.GetInt32()).ToArray();
                if (clipId is < 0 or >= 1 << AvatarStripCodec.FrameBits
                    || frames < 1 || frames > 1 << AvatarStripCodec.FrameBits
                    || delays.Length != frames || delays.Any(d => d < 1))
                {
                    error = $"clip {clipId}: frames/delays invalid";
                    return false;
                }

                if (clips.Any(c => c.ClipId == clipId))
                {
                    error = $"clip {clipId}: duplicate";
                    return false;
                }

                clips.Add(new AvatarClipDef(clipId, clipName, frames, delays));
            }

            if (clips.Count == 0)
            {
                error = "clips: empty";
                return false;
            }

            def = new AvatarPackDef(packId, name, cellWidth, cellHeight, sheet, clips.ToArray());
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            error = $"parse: {ex.Message}";
            return false;
        }
    }

    /// <summary>SHA-256 hex of a file — the generation hash recorded in the task record.</summary>
    public static string FileSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>Writes both packs' sheets + defs and the fallback frame into <paramref name="directory"/>.</summary>
    public static IReadOnlyList<string> WriteAll(string directory)
    {
        var written = new List<string>();
        foreach (var def in All)
        {
            var pixels = GenerateSheetPixels(def, out var width, out var height);
            var sheetFile = Path.Combine(directory, Path.GetFileName(def.SheetPath));
            File.WriteAllBytes(sheetFile, BmpCodec.Encode32(width, height, pixels));
            written.Add(sheetFile);

            var defFile = Path.Combine(directory, Path.GetFileName(def.SheetPath).Replace(".bmp", ".json", StringComparison.Ordinal));
            File.WriteAllText(defFile, SerializeDef(def));
            written.Add(defFile);
        }

        var fallback = GenerateFallbackPixels(out var fw, out var fh);
        var fallbackFile = Path.Combine(directory, Path.GetFileName(FallbackPath));
        File.WriteAllBytes(fallbackFile, BmpCodec.Encode32(fw, fh, fallback));
        written.Add(fallbackFile);
        return written;
    }

    // ---- deterministic art (pure integer math; no PRNG, no clock) ----

    private static void PaintCell(byte[] sheet, int sheetWidth, AvatarPackDef def, AvatarClipDef clip, int frame)
    {
        var cell = new byte[def.CellWidth * def.CellHeight * 4];
        var w = def.CellWidth;
        var h = def.CellHeight;

        // Pack palettes: circuit = cyan/magenta on blue-black; pulse = amber/green on warm black.
        byte bgR, bgG, bgB, neonR, neonG, neonB, accR, accG, accB;
        if (def.PackId == 0)
        {
            (bgR, bgG, bgB) = (0x10, 0x10, 0x1C);
            (neonR, neonG, neonB) = (0x00, 0xE5, 0xFF);
            (accR, accG, accB) = (0xE0, 0x66, 0xFF);
        }
        else
        {
            (bgR, bgG, bgB) = (0x1C, 0x12, 0x08);
            (neonR, neonG, neonB) = (0xFF, 0xB3, 0x00);
            (accR, accG, accB) = (0x00, 0xE6, 0x76);
        }

        Fill(cell, w, 0, 0, w, h, bgR, bgG, bgB);

        // Clip-identifying pattern family with a per-frame phase so every frame differs
        // visibly (K3) and pixel-countably. Strip region (bottom-left 40x4) is overwritten
        // after; art avoids pure magenta everywhere.
        switch (clip.ClipId)
        {
            case ClipPoses:
                // Concentric rectangles; count grows with pose index.
                for (var ring = 0; ring <= frame; ring++)
                {
                    var inset = 8 + ring * 10;
                    StrokeRect(cell, w, inset, inset, w - inset, h - inset, 2, neonR, neonG, neonB);
                }

                break;
            case ClipIdle:
            case ClipIdle2:
                // Sweeping vertical bar; x advances with the frame (direction differs per idle clip).
                var sweep = clip.ClipId == ClipIdle ? frame : clip.Frames - 1 - frame;
                var barX = 6 + sweep * ((w - 20) / Math.Max(1, clip.Frames - 1));
                Fill(cell, w, barX, 10, barX + 6, h - 14, neonR, neonG, neonB);
                StrokeRect(cell, w, 4, 4, w - 4, h - 8, 2, accR, accG, accB);
                break;
            case ClipTalk:
                // "Mouth" bars whose open height alternates with the frame.
                var open = 6 + (frame % 2 == 0 ? frame * 4 : 30 - frame * 3);
                StrokeRect(cell, w, 12, 20, w - 12, h - 20, 2, neonR, neonG, neonB);
                Fill(cell, w, w / 2 - 14, h / 2 - open / 2, w / 2 + 14, h / 2 + open / 2, accR, accG, accB);
                break;
            case ClipReaction:
                // Radiating diagonal from a corner; corner rotates with the frame.
                for (var i = 0; i < 26; i += 2)
                {
                    var px = (frame * 13 + i * 7) % w;
                    var py = (frame * 17 + i * 11) % (h - 12);
                    Fill(cell, w, px, py, Math.Min(w, px + 3), Math.Min(h - 8, py + 3), i % 4 == 0 ? neonR : accR, i % 4 == 0 ? neonG : accG, i % 4 == 0 ? neonB : accB);
                }

                break;
            case ClipClick:
                // Expanding square outline; inset shrinks with the frame (pop feel).
                var grow = 20 - frame * 4;
                StrokeRect(cell, w, Math.Max(4, grow), Math.Max(8, grow), w - Math.Max(4, grow), h - Math.Max(8, grow), 3, accR, accG, accB);
                Fill(cell, w, w / 2 - 4, h / 2 - 4, w / 2 + 4, h / 2 + 4, neonR, neonG, neonB);
                break;
        }

        // Pack-distinguishing scan mark (horizontal for circuit, vertical for pulse).
        if (def.PackId == 0)
        {
            Fill(cell, w, 0, 12 + frame * 3 % (h - 30), w, 14 + frame * 3 % (h - 30), accR, accG, accB);
        }
        else
        {
            Fill(cell, w, 12 + frame * 5 % (w - 24), 0, 14 + frame * 5 % (w - 24), h - 10, accR, accG, accB);
        }

        BlitCell(sheet, sheetWidth, def, clip, frame, cell);
    }

    private static void BlitCell(byte[] sheet, int sheetWidth, AvatarPackDef def, AvatarClipDef clip, int frame, byte[] cell)
    {
        var x0 = frame * def.CellWidth;
        var y0 = clip.ClipId * def.CellHeight;
        for (var y = 0; y < def.CellHeight; y++)
        {
            var src = y * def.CellWidth * 4;
            var dst = ((y0 + y) * sheetWidth + x0) * 4;
            Array.Copy(cell, src, sheet, dst, def.CellWidth * 4);
        }
    }

    private static void Fill(byte[] bgra, int width, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var offset = (y * width + x) * 4;
                bgra[offset] = b;
                bgra[offset + 1] = g;
                bgra[offset + 2] = r;
                bgra[offset + 3] = 255;
            }
        }
    }

    private static void StrokeRect(byte[] bgra, int width, int x0, int y0, int x1, int y1, int thickness, byte r, byte g, byte b)
    {
        Fill(bgra, width, x0, y0, x1, Math.Min(y1, y0 + thickness), r, g, b);
        Fill(bgra, width, x0, Math.Max(y0, y1 - thickness), x1, y1, r, g, b);
        Fill(bgra, width, x0, y0, Math.Min(x1, x0 + thickness), y1, r, g, b);
        Fill(bgra, width, Math.Max(x0, x1 - thickness), y0, x1, y1, r, g, b);
    }
}

/// <summary>One clip inside a pack: identity, frame count, and per-frame delays (non-uniform by construction).</summary>
public sealed record AvatarClipDef(int ClipId, string Name, int Frames, int[] DelaysMs)
{
    /// <summary>Single-pass duration of the clip (sum of per-frame delays).</summary>
    public int PassDurationMs => DelaysMs.Sum();
}

/// <summary>A synthetic avatar pack definition (pack.json). The sheet layout: row = clipId, column = frame index.</summary>
public sealed record AvatarPackDef(
    int PackId, string Name, int CellWidth, int CellHeight, string SheetPath, AvatarClipDef[] Clips)
{
    public AvatarClipDef Clip(int clipId) =>
        Clips.FirstOrDefault(c => c.ClipId == clipId)
        ?? throw new ArgumentException($"pack '{Name}' has no clip {clipId}", nameof(clipId));
}
