using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Pure rules for "is this dropped file a preset?", kept off the window so they can
    /// be unit tested. The catalogue serves <c>Title.preset.json</c>, but a browser that
    /// already has that name saves <c>Title.preset (1).json</c>, and people rename files,
    /// so the name is only the fast path: any other <c>.json</c> is sniffed for the
    /// preset shape (ccp-bugs #1331, where a renamed download fell through to the
    /// enhancement import and nothing visible happened).
    /// </summary>
    public static class PresetDropRules
    {
        /// <summary>Catalogue assets are capped at 1 MB; anything far past that is not a preset.</summary>
        public const long MaxSniffBytes = 2 * 1024 * 1024;

        // ".preset.json" plus the copies browsers and Explorer make of it:
        // "x.preset (1).json", "x.preset(2).json", "x.preset - Copy.json", "x.preset_1.json".
        private static readonly Regex PresetName = new(
            @"\.preset(\s*\(\d+\)|\s*-\s*copy(\s*\(\d+\))?|[ _-]\d+)?\.json$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Root members every exported preset carries (PresetFileService writes camelCase).
        // settings.json is PascalCase (Newtonsoft) and sessions keep these inside a nested
        // object, so neither matches.
        private static readonly string[] Signature =
        {
            "flashEnabled", "flashFrequency", "subliminalEnabled", "mandatoryVideosEnabled",
            "masterVolume", "attentionChecksEnabled", "subAudioEnabled", "imageScale",
        };

        /// <summary>True for a preset file name, including a browser duplicate rename.</summary>
        public static bool IsPresetFileName(string? path)
            => !string.IsNullOrEmpty(path) && PresetName.IsMatch(path);

        /// <summary>True for a path ending in .json (the content sniff's gate).</summary>
        public static bool IsJsonPath(string? path)
            => !string.IsNullOrEmpty(path) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when <paramref name="json"/> is a standalone preset: a JSON object with no
        /// <c>$schema</c> tag (enhancements and catalogue envelopes carry one), a non-empty
        /// string <c>id</c>, and at least two of the preset's own settings at the root.
        /// </summary>
        public static bool LooksLikePresetJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                if (root.TryGetProperty("$schema", out _)) return false;
                if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(id.GetString()))
                    return false;

                int hits = 0;
                foreach (var key in Signature)
                    if (root.TryGetProperty(key, out _) && ++hits >= 2) return true;
                return false;
            }
            catch (JsonException)
            {
                return false; // swallow: not JSON, so not a preset
            }
        }

        // One-entry memo: DragEnter / DragOver ask about the same file many times a second.
        private static readonly object MemoGate = new();
        private static string? _memoPath;
        private static DateTime _memoStamp;
        private static long _memoLength;
        private static bool _memoResult;

        /// <summary>
        /// True when the file at <paramref name="path"/> is a preset: a preset file name, or
        /// any .json whose content passes <see cref="LooksLikePresetJson"/>.
        /// </summary>
        public static bool FileIsPreset(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (IsPresetFileName(path)) return true;
            if (!IsJsonPath(path)) return false;
            if (path.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxSniffBytes) return false;

                lock (MemoGate)
                {
                    if (string.Equals(_memoPath, path, StringComparison.OrdinalIgnoreCase)
                        && _memoStamp == info.LastWriteTimeUtc && _memoLength == info.Length)
                        return _memoResult;
                }

                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                    text = reader.ReadToEnd();

                var result = LooksLikePresetJson(text);
                lock (MemoGate)
                {
                    _memoPath = path;
                    _memoStamp = info.LastWriteTimeUtc;
                    _memoLength = info.Length;
                    _memoResult = result;
                }
                return result;
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "unreadable preset candidate");
                return false;
            }
        }
    }
}
