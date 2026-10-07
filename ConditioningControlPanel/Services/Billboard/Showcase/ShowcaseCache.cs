using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Billboard.Showcase
{
    /// <summary>
    /// The showcase's disk cache: the manifest, the clips and their posters, under one folder in
    /// the user data tree. A clip is downloaded on first need, checked against the manifest's
    /// sha256 before it is kept, and named by its hash so a re-cut clip never collides with the
    /// old file a player may still hold open. Never in the installer.
    /// </summary>
    public sealed class ShowcaseCache
    {
        /// <summary>The manifest itself is small; anything bigger is not ours.</summary>
        public const int MaxManifestBytes = 64 * 1024;

        private static readonly HttpClient SharedHttp = CreateHttp();

        private readonly string _root;
        private readonly Uri _manifestUri;
        private readonly HttpClient _http;
        private readonly ConcurrentDictionary<string, Task<bool>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _verified = new(StringComparer.OrdinalIgnoreCase);

        public ShowcaseCache(string root, Uri manifestUri, HttpClient? http = null)
        {
            _root = root;
            _manifestUri = manifestUri;
            _http = http ?? SharedHttp;
        }

        public string Root => _root;

        private string ClipsDir => Path.Combine(_root, "clips");

        private string ManifestPath => Path.Combine(_root, "showcase.json");

        // ---- paths -------------------------------------------------------------------

        /// <summary>Where a clip lives on disk: id + the first 12 hex of its hash + the file's extension.</summary>
        public string ClipPath(ShowcaseClip clip) =>
            Path.Combine(ClipsDir, CacheName(clip.Id, clip.Sha256, clip.File));

        /// <summary>Where the clip's poster lives, or null when the manifest gives none.</summary>
        public string? PosterPath(ShowcaseClip clip) =>
            clip.Poster == null || clip.PosterSha256 == null
                ? null
                : Path.Combine(ClipsDir, CacheName(clip.Id + "-poster", clip.PosterSha256, clip.Poster));

        internal static string CacheName(string id, string sha, string file)
        {
            var ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext) || ext.Length > 6) ext = ".bin";
            return $"{id}-{sha.Substring(0, 12)}{ext.ToLowerInvariant()}";
        }

        // ---- readiness -----------------------------------------------------------------

        /// <summary>
        /// True once the clip's bytes on disk have been checked against its hash this session.
        /// Never touches the disk itself: <see cref="EnsureAsync"/> does the checking.
        /// </summary>
        public bool IsReady(ShowcaseClip clip) => _verified.ContainsKey(ClipPath(clip));

        /// <summary>The poster path when it is on disk and checked, else null.</summary>
        public string? ReadyPoster(ShowcaseClip clip)
        {
            var p = PosterPath(clip);
            return p != null && _verified.ContainsKey(p) ? p : null;
        }

        /// <summary>
        /// Makes the clip (and, best effort, its poster) ready: checks a file already on disk,
        /// downloads it when missing or wrong. One flight per clip at a time. True when the clip
        /// is ready; a missing poster does not fail it.
        /// </summary>
        public Task<bool> EnsureAsync(ShowcaseClip clip, CancellationToken ct = default)
        {
            var key = ClipPath(clip);
            if (_verified.ContainsKey(key)) return Task.FromResult(true);
            return _inFlight.GetOrAdd(key, _ => RunEnsure(clip, key, ct));
        }

        private async Task<bool> RunEnsure(ShowcaseClip clip, string key, CancellationToken ct)
        {
            try
            {
                bool ok = await EnsureFileAsync(clip.File, key, clip.Sha256, clip.Bytes, ct).ConfigureAwait(false);
                var poster = PosterPath(clip);
                if (ok && poster != null)
                {
                    try { await EnsureFileAsync(clip.Poster!, poster, clip.PosterSha256!, clip.PosterBytes, ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { Log("poster " + clip.Id, ex); }
                }
                return ok;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log("clip " + clip.Id, ex);
                return false;
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        }

        private async Task<bool> EnsureFileAsync(string assetName, string path, string sha, long bytes, CancellationToken ct)
        {
            if (_verified.ContainsKey(path)) return true;

            if (File.Exists(path))
            {
                byte[]? existing = null;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length == bytes) existing = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                }
                catch (IOException) { }
                if (existing != null && HashMatches(existing, sha))
                {
                    _verified[path] = true;
                    return true;
                }
                TryDelete(path);
            }

            var data = await DownloadAsync(new Uri(_manifestUri, assetName), bytes, ct).ConfigureAwait(false);
            if (data == null || data.LongLength != bytes || !HashMatches(data, sha)) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var part = path + ".part";
            await File.WriteAllBytesAsync(part, data, ct).ConfigureAwait(false);
            File.Move(part, path, overwrite: true);
            _verified[path] = true;
            return true;
        }

        /// <summary>Reads a response body up to <paramref name="cap"/> bytes; null when it is bigger or fails.</summary>
        private async Task<byte[]?> DownloadAsync(Uri uri, long cap, CancellationToken ct)
        {
            using var resp = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            if (resp.Content.Headers.ContentLength is long declared && declared > cap) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                if (ms.Length + read > cap) return null;
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }

        internal static bool HashMatches(byte[] data, string sha) =>
            string.Equals(Convert.ToHexString(SHA256.HashData(data)), sha, StringComparison.OrdinalIgnoreCase);

        // ---- manifest -------------------------------------------------------------------

        /// <summary>The last manifest saved to disk, or null. Small, read synchronously.</summary>
        public string? ReadManifestFromDisk()
        {
            try { return File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>Fetches the manifest; saves it to disk when it parses. Null on any failure.</summary>
        public async Task<string?> FetchManifestAsync(CancellationToken ct = default)
        {
            try
            {
                var data = await DownloadAsync(_manifestUri, MaxManifestBytes, ct).ConfigureAwait(false);
                if (data == null) return null;
                var json = System.Text.Encoding.UTF8.GetString(data).TrimStart('﻿');
                if (ShowcaseManifestParser.Parse(json).Version <= 0) return null;
                Directory.CreateDirectory(_root);
                var part = ManifestPath + ".part";
                await File.WriteAllTextAsync(part, json, ct).ConfigureAwait(false);
                File.Move(part, ManifestPath, overwrite: true);
                return json;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log("manifest", ex);
                return null;
            }
        }

        /// <summary>Deletes cached files the manifest no longer names. Best effort: a file a player holds stays.</summary>
        public void Prune(IEnumerable<ShowcaseClip> keep)
        {
            try
            {
                if (!Directory.Exists(ClipsDir)) return;
                var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in keep)
                {
                    wanted.Add(ClipPath(c));
                    var p = PosterPath(c);
                    if (p != null) wanted.Add(p);
                }
                foreach (var f in Directory.EnumerateFiles(ClipsDir).ToList())
                {
                    if (wanted.Contains(f)) continue;
                    _verified.TryRemove(f, out _);
                    TryDelete(f);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ConditioningControlPanel-Showcase/1");
            return http;
        }

        private static void Log(string what, Exception ex)
        {
            try { App.Logger?.Warning("Showcase {What} failed: {Message}", what, ex.Message); }
            catch { }
        }
    }
}
