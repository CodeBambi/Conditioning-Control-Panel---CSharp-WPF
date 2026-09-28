using System;
using System.Collections.Concurrent;
using System.IO;
using ConditioningControlPanel.Models.Deeper;
using Serilog;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Background scan of the mandatory / asset video folder
    /// (<c>EffectiveAssetsPath/videos</c>, recursive — matching how
    /// <see cref="VideoService"/> enumerates it) that answers two questions for
    /// the engine-start nudge:
    ///   • does ANY playable video have a resolvable enhancement (embedded /
    ///     sidecar / library)?  → offer to turn <c>VideoEnhanceIfPossible</c> on.
    ///   • does any such enhancement need the webcam tracker (gaze / blink /
    ///     mouth / attention)?  → offer to start the webcam.
    ///
    /// Cost control (the folder can hold thousands of files):
    ///   • Always called off the UI thread (via Task.Run).
    ///   • Short-circuits the instant both answers are known true — a folder with
    ///     a webcam-enhanced video near the top costs almost nothing.
    ///   • Per-file verdicts are cached by path + size + last-write-time, so the
    ///     expensive embedded-metadata tail read happens once per file even when
    ///     the engine is started repeatedly within a launch. Negative results are
    ///     cached too. Embedded extraction only touches bundler-supported
    ///     containers (mp4/m4v/mov/mp3/wav); webm/mkv/avi fall through to the
    ///     cheap sidecar + library checks.
    /// </summary>
    public static class MandatoryVideoEnhancementScanner
    {
        public readonly struct ScanResult
        {
            public bool AnyEnhanced { get; }
            public bool AnyWebcamEnhanced { get; }

            public ScanResult(bool anyEnhanced, bool anyWebcamEnhanced)
            {
                AnyEnhanced = anyEnhanced;
                AnyWebcamEnhanced = anyWebcamEnhanced;
            }

            public static ScanResult None => new(false, false);
        }

        private readonly struct FileVerdict
        {
            public readonly long Size;
            public readonly long Ticks;
            public readonly bool Enhanced;
            public readonly bool Webcam;

            public FileVerdict(long size, long ticks, bool enhanced, bool webcam)
            {
                Size = size;
                Ticks = ticks;
                Enhanced = enhanced;
                Webcam = webcam;
            }
        }

        // Keyed by full path; survives for the app session so repeated engine
        // starts don't re-pay the embedded-metadata read on unchanged files.
        private static readonly ConcurrentDictionary<string, FileVerdict> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public static ScanResult Scan(string videosFolder)
        {
            bool anyEnhanced = false, anyWebcam = false;
            try
            {
                if (string.IsNullOrEmpty(videosFolder) || !Directory.Exists(videosFolder))
                    return ScanResult.None;

                foreach (var path in Directory.EnumerateFiles(videosFolder, "*.*", SearchOption.AllDirectories))
                {
                    if (!EnhancementResolver.IsLocalVideoFile(path)) continue;

                    FileVerdict v;
                    try { v = Classify(path); }
                    catch { continue; }

                    if (v.Enhanced) anyEnhanced = true;
                    if (v.Webcam) { anyWebcam = true; anyEnhanced = true; }

                    if (anyEnhanced && anyWebcam) break; // nothing more to learn
                }
            }
            catch (Exception ex)
            {
                Log.Debug("MandatoryVideoEnhancementScanner: scan failed: {Error}", ex.Message);
            }
            return new ScanResult(anyEnhanced, anyWebcam);
        }

        private static FileVerdict Classify(string path)
        {
            var info = new FileInfo(path);
            long size = info.Length;
            long ticks = info.LastWriteTimeUtc.Ticks;

            if (_cache.TryGetValue(path, out var cached) && cached.Size == size && cached.Ticks == ticks)
                return cached;

            bool enhanced = false, webcam = false;
            var resolved = EnhancementResolver.ResolveForLocalMedia(path);
            if (resolved.Found)
            {
                enhanced = true;
                Enhancement? enh = resolved.Enhancement; // set for embedded matches
                if (enh == null && resolved.FilePath != null)
                {
                    // Sidecar / library match — parse the .ccpenh.json to inspect
                    // its rules. Failure (too large / malformed) just means "we
                    // can't tell it needs the webcam", which is the safe default.
                    try { enh = EnhancementSerializer.LoadFromFile(resolved.FilePath); }
                    catch { enh = null; }
                }
                webcam = EnhancementCapabilities.NeedsWebcam(enh);
            }

            var verdict = new FileVerdict(size, ticks, enhanced, webcam);
            _cache[path] = verdict;
            return verdict;
        }
    }
}
