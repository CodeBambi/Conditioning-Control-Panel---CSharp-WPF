using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Bureau
{
    /// <summary>
    /// The Bureau's local evidence catalogue: a sha256(plaintext bytes) → installed-pack-file
    /// map over every image in the user's installed content packs. The labeling game's server
    /// catalogue is keyed by the same hashes (computed over the master pack zips), so this index
    /// is the join that turns a server target ("&lt;sha256&gt;:&lt;frame&gt;") back into local pixels —
    /// which is what keeps image data off the server entirely.
    ///
    /// Hashing means decrypting every pack image once, so results are cached in
    /// bureau_hash_index.json keyed by "packId/obfuscatedName". Obfuscated names are minted at
    /// install time and pack content is immutable after install, so a cache hit needs no
    /// mtime/size validation; a reinstall mints new names and re-hashes naturally.
    /// </summary>
    internal static class BureauIndexService
    {
        private const int MaxSide = 1280;   // decode cap: keeps bridge messages light; boxes are normalized so labels are unaffected

        private static readonly object Gate = new();
        private static Task? _buildTask;
        private static Dictionary<string, (string PackId, PackFileEntry Entry)> _byHash = new();
        private static Dictionary<string, int> _packCounts = new();

        public static bool IsReady { get; private set; }
        public static int Done { get; private set; }
        public static int Total { get; private set; }

        /// <summary>Indexed (hashable) image count per installed packId — the lockers UI's numbers.</summary>
        public static IReadOnlyDictionary<string, int> PackCounts => _packCounts;

        /// <summary>Re-run the build (after a pack install). Cached hashes make old packs instant.</summary>
        public static Task RebuildAsync(Action<int, int>? progress = null)
        {
            lock (Gate) { _buildTask = null; }
            return EnsureBuiltAsync(progress);
        }

        private static string CachePath => Path.Combine(App.UserDataPath, "bureau_hash_index.json");

        /// <summary>Kick (or join) the one-time index build. Safe to call repeatedly.</summary>
        public static Task EnsureBuiltAsync(Action<int, int>? progress = null)
        {
            lock (Gate)
            {
                if (_buildTask != null) return _buildTask;
                _buildTask = Task.Run(() => Build(progress));
                return _buildTask;
            }
        }

        public static bool TryResolve(string sha256, out string packId, out PackFileEntry? entry)
        {
            packId = "";
            entry = null;
            if (!_byHash.TryGetValue(sha256, out var hit)) return false;
            packId = hit.PackId;
            entry = hit.Entry;
            return true;
        }

        private static void Build(Action<int, int>? progress)
        {
            try
            {
                var packs = App.ContentPacks;
                if (packs == null) { IsReady = true; return; }

                // Collect every image entry across all installed packs (not just active ones:
                // labeling availability shouldn't depend on which packs are toggled for flashes).
                var work = new List<(string PackId, PackFileEntry Entry)>();
                foreach (var packId in packs.InstalledPacks.ToList())
                {
                    foreach (var entry in packs.GetPackFiles(packId, "image"))
                    {
                        var ext = (entry.Extension ?? "").TrimStart('.').ToLowerInvariant();
                        if (ext is "png" or "jpg" or "jpeg" or "gif" or "webp" or "bmp")
                            work.Add((packId, entry));
                    }
                }

                Total = work.Count;
                Done = 0;
                var cache = LoadCache();
                var map = new Dictionary<string, (string, PackFileEntry)>(work.Count, StringComparer.Ordinal);
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                var dirty = 0;

                foreach (var (packId, entry) in work)
                {
                    var key = packId + "/" + entry.ObfuscatedName;
                    if (!cache.TryGetValue(key, out var hash))
                    {
                        hash = HashEntry(packId, entry);
                        if (hash != null) { cache[key] = hash; dirty++; }
                    }
                    if (hash != null)
                    {
                        map[hash] = (packId, entry);
                        counts[packId] = counts.TryGetValue(packId, out var c) ? c + 1 : 1;
                    }

                    Done++;
                    if ((Done % 25) == 0 || Done == Total)
                    {
                        progress?.Invoke(Done, Total);
                        if (dirty >= 200) { SaveCache(cache); dirty = 0; }
                    }
                }

                if (dirty > 0) SaveCache(cache);
                _byHash = map;
                _packCounts = counts;
                App.Logger?.Information("BureauIndex: {Mapped} images indexed across {Packs} packs ({Total} entries scanned)",
                    map.Count, packs.InstalledPacks.Count, Total);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BureauIndex: build failed");
            }
            finally
            {
                IsReady = true;
            }
        }

        private static string? HashEntry(string packId, PackFileEntry entry)
        {
            try
            {
                using var stream = App.ContentPacks?.GetPackFileStream(packId, entry);
                if (stream == null) return null;
                using var sha = SHA256.Create();
                var digest = sha.ComputeHash(stream);
                return Convert.ToHexString(digest).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BureauIndex: hash failed for {Name}: {E}", entry.OriginalName, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Decode one keyframe of a catalogued image as a JPEG data URI. GDI+ SelectActiveFrame
        /// composites GIF frames the same cumulative way the catalogue's PIL sampler did, so
        /// frame indices line up; a count mismatch (divergent decoder) returns null and the
        /// target is simply skipped client-side.
        /// </summary>
        public static (string DataUri, int W, int H)? DecodeFrame(string sha256, int frameIndex)
        {
            if (!TryResolve(sha256, out var packId, out var entry) || entry == null) return null;
            try
            {
                using var stream = App.ContentPacks?.GetPackFileStream(packId, entry);
                if (stream == null) return null;
                using var img = System.Drawing.Image.FromStream(stream);

                if (frameIndex > 0)
                {
                    if (img.FrameDimensionsList == null || img.FrameDimensionsList.Length == 0) return null;
                    var fd = new FrameDimension(img.FrameDimensionsList[0]);
                    int count;
                    try { count = img.GetFrameCount(fd); } catch { count = 1; }
                    if (frameIndex >= count) return null;
                    img.SelectActiveFrame(fd, frameIndex);
                }

                var scale = Math.Min(1.0, (double)MaxSide / Math.Max(img.Width, img.Height));
                var dw = Math.Max(1, (int)Math.Round(img.Width * scale));
                var dh = Math.Max(1, (int)Math.Round(img.Height * scale));

                using var bmp = new Bitmap(dw, dh, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    // Charcoal behind any transparency — matches the game's light-table.
                    g.Clear(System.Drawing.Color.FromArgb(14, 13, 26));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, dw, dh);
                }

                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                using var ms = new MemoryStream();
                bmp.Save(ms, codec, ep);
                return ("data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray()), dw, dh);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BureauIndex: decode failed for {Hash}:{Frame}: {E}", sha256, frameIndex, ex.Message);
                return null;
            }
        }

        // ============================ cache ============================

        private static Dictionary<string, string> LoadCache()
        {
            try
            {
                if (File.Exists(CachePath))
                {
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(CachePath));
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("BureauIndex: cache load failed: {E}", ex.Message); }
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static void SaveCache(Dictionary<string, string> cache)
        {
            try
            {
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(CachePath, JsonConvert.SerializeObject(cache));
            }
            catch (Exception ex) { App.Logger?.Debug("BureauIndex: cache save failed: {E}", ex.Message); }
        }
    }
}
