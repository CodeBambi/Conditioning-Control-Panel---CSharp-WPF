using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>Entry.Kind values — what the SOURCE file is, not what the artifact became.</summary>
    internal static class TransferKinds
    {
        public const string Video = "video";
        public const string Gif = "gif";      // animated gif / animated webp
        public const string Image = "image";  // any still
    }

    /// <summary>Entry.State values. Anything not in the index is simply "not ready".</summary>
    internal static class TransferStates
    {
        /// <summary>A compressed artifact exists at art/&lt;sha&gt;.&lt;ext&gt;.</summary>
        public const string Ready = "ready";
        /// <summary>Small enough (or already small enough) to send as-is; <c>Sha</c> is the ORIGINAL's hash.</summary>
        public const string Exempt = "exempt";
        /// <summary>Compression was attempted and gave up; see <c>Fail</c>. Dropped after 14 days.</summary>
        public const string Failed = "failed";
    }

    /// <summary>Entry.Lane values — which pipeline produced (or would produce) the artifact.</summary>
    internal static class TransferLanes
    {
        /// <summary>WinRT MediaTranscoder, host side.</summary>
        public const string HostMt = "host-mt";
        /// <summary>SkiaSharp stills, host side.</summary>
        public const string HostSkia = "host-skia";
        /// <summary>WebCodecs in the goon page — dispatched by the host, executed by the page.</summary>
        public const string PageWc = "page-wc";
        /// <summary>No work needed.</summary>
        public const string Exempt = "exempt";
    }

    /// <summary>Well-known <see cref="TransferCacheEntry.Fail"/> reasons (the page renders these as word badges).</summary>
    internal static class TransferFailReasons
    {
        public const string NoDecoder = "no-decoder";
        public const string EncodeFailed = "encode-failed";
        public const string TooBig = "too-big";
        public const string Missing = "missing";
        public const string IoFailed = "io-failed";
    }

    /// <summary>
    /// One source file's compression record. Keyed in the index by
    /// <see cref="TransferCacheHash.ComputeSrcKey"/> — path+size+mtime, so an edited or replaced
    /// file gets a new key and is re-compressed instead of silently serving stale bytes
    /// (VideoMetadataCache precedent).
    ///
    /// <c>Sha</c> is ALWAYS computed host-side over the bytes that will actually go on the wire
    /// (the artifact for ready entries, the original for exempt ones) — it is the transfer
    /// identity, never a claim from the page.
    /// </summary>
    internal sealed class TransferCacheEntry
    {
        /// <summary>Path relative to App.EffectiveAssetsPath, forward-slashed — same shape as
        /// AppSettings.DisabledAssetPaths, so preset membership is a set lookup, not a tree walk.</summary>
        [JsonProperty("rel")] public string Rel { get; set; } = "";

        /// <summary>Source file length in bytes.</summary>
        [JsonProperty("size")] public long Size { get; set; }

        /// <summary>Source LastWriteTimeUtc ticks.</summary>
        [JsonProperty("mtime")] public long Mtime { get; set; }

        /// <summary>One of <see cref="TransferKinds"/>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = TransferKinds.Image;

        /// <summary>One of <see cref="TransferStates"/>.</summary>
        [JsonProperty("state")] public string State { get; set; } = TransferStates.Failed;

        /// <summary>One of <see cref="TransferLanes"/>.</summary>
        [JsonProperty("lane")] public string Lane { get; set; } = TransferLanes.Exempt;

        /// <summary>sha256 of the sendable bytes, lowercase hex. The wire identity.</summary>
        [JsonProperty("sha")] public string Sha { get; set; } = "";

        /// <summary>Artifact extension WITHOUT the dot (mp4/webp/jpg/gif/png...).</summary>
        [JsonProperty("ext")] public string Ext { get; set; } = "";

        /// <summary>Artifact size in bytes (== <see cref="Size"/> for exempt entries).</summary>
        [JsonProperty("bytes")] public long Bytes { get; set; }

        /// <summary>Micro-preview size in bytes; 0 when there is no preview.</summary>
        [JsonProperty("prevBytes")] public long PrevBytes { get; set; }

        [JsonProperty("w")] public int W { get; set; }
        [JsonProperty("h")] public int H { get; set; }

        /// <summary>Duration in ms for video/animated entries; 0 for stills.</summary>
        [JsonProperty("durMs")] public int DurMs { get; set; }

        /// <summary>Artifact codec label ("avc1", "webp", "jpeg", "orig").</summary>
        [JsonProperty("codec")] public string Codec { get; set; } = "";

        [JsonProperty("madeAt")] public DateTime MadeAt { get; set; } = DateTime.UtcNow;

        /// <summary>Touched on every send / explicit use. Drives the LRU.</summary>
        [JsonProperty("lastUsed")] public DateTime LastUsed { get; set; } = DateTime.UtcNow;

        /// <summary>Failure reason for <see cref="TransferStates.Failed"/> entries; null otherwise.</summary>
        [JsonProperty("fail", NullValueHandling = NullValueHandling.Ignore)]
        public string? Fail { get; set; }

        [JsonIgnore] public bool IsReady => State == TransferStates.Ready;
        [JsonIgnore] public bool IsExempt => State == TransferStates.Exempt;
        [JsonIgnore] public bool IsFailed => State == TransferStates.Failed;

        /// <summary>True when the sendable bytes live in the cache (rather than being the original file).</summary>
        [JsonIgnore] public bool HasArtifact => State == TransferStates.Ready && Sha.Length > 0;

        public TransferCacheEntry Clone() => (TransferCacheEntry)MemberwiseClone();
    }

    /// <summary>
    /// index.json: the on-disk shape of the own-artifact cache. Newtonsoft (the app's serializer)
    /// with explicit [JsonProperty] names so a future rename can't silently orphan a user's cache.
    /// </summary>
    internal sealed class TransferCacheIndex
    {
        public const int CurrentVersion = 1;

        [JsonProperty("version")] public int Version { get; set; } = CurrentVersion;

        /// <summary>Sum of Bytes+PrevBytes across entries. Recomputed on load — never trusted from disk.</summary>
        [JsonProperty("totalBytes")] public long TotalBytes { get; set; }

        [JsonProperty("entries")]
        public Dictionary<string, TransferCacheEntry> Entries { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>Recompute <see cref="TotalBytes"/> from the entries. Returns the new total.</summary>
        public long Recount()
        {
            long total = 0;
            foreach (var e in Entries.Values)
            {
                // Exempt entries cost nothing on disk (the original IS the payload); only
                // artifacts and previews occupy the cache budget.
                if (e.State == TransferStates.Ready) total += e.Bytes;
                total += e.PrevBytes;
            }
            TotalBytes = total;
            return total;
        }
    }

    /// <summary>Hashing helpers shared by the cache, the planner and the commit path.</summary>
    internal static class TransferCacheHash
    {
        /// <summary>
        /// "Have I already compressed this exact file?" key:
        /// <c>sha256("v1|" + relLowerInvariant + "|" + size + "|" + mtimeUtcTicks)</c>, lowercase hex.
        /// Path-relative (not absolute) so moving the whole assets folder doesn't invalidate the cache,
        /// and lower-invariant because Windows paths are case-insensitive.
        /// </summary>
        public static string ComputeSrcKey(string rel, long size, long mtimeUtcTicks)
        {
            var norm = (rel ?? "").Replace('\\', '/').ToLowerInvariant();
            var payload = "v1|" + norm + "|"
                + size.ToString(CultureInfo.InvariantCulture) + "|"
                + mtimeUtcTicks.ToString(CultureInfo.InvariantCulture);
            return ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        }

        /// <summary>Streaming sha256 of a file, lowercase hex. Null when it can't be read.</summary>
        public static string? Sha256File(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1024 * 128, useAsync: false);
                using var sha = SHA256.Create();
                return ToHex(sha.ComputeHash(fs));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("TransferCacheHash.Sha256File({Path}): {E}", path, ex.Message);
                return null;
            }
        }

        public static string Sha256Bytes(byte[] bytes) => ToHex(SHA256.HashData(bytes));

        /// <summary>Lowercase hex — the wire form. Every sha in the system is this shape.</summary>
        public static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>64 lowercase hex chars. Everything that crosses the bridge is checked with this.</summary>
        public static bool IsShaHex(string? s)
        {
            if (s == null || s.Length != 64) return false;
            foreach (var c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
