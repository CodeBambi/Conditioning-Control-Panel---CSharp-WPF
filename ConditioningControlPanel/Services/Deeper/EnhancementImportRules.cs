using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Models.Deeper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Pure rules behind the library's Import path, kept off the Window so they
    /// can be unit tested: which paths are worth trying, whether a JSON file is
    /// actually a Deeper enhancement (a dropped settings.json used to produce an
    /// "Import failed" modal), and a whitespace-insensitive content hash used to
    /// spot a re-import of a file that is already in the library.
    /// </summary>
    public static class EnhancementImportRules
    {
        /// <summary>How much of a file the schema sniff reads. The $schema tag is
        /// the first member the serializer writes, so 4 KB is generous.</summary>
        public const int SniffBytes = 4096;

        public static bool IsImportablePath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when <paramref name="jsonHead"/> (the first few KB of a file) carries
        /// the enhancement schema tag. Mirrors the check EnhancementSerializer.Load
        /// performs, without parsing the whole document.
        /// </summary>
        public static bool LooksLikeEnhancementJson(string? jsonHead)
        {
            if (string.IsNullOrWhiteSpace(jsonHead)) return false;
            var i = jsonHead.IndexOf("\"$schema\"", StringComparison.Ordinal);
            if (i < 0) return false;
            return jsonHead.IndexOf(Enhancement.SchemaTag, i, StringComparison.Ordinal) >= 0;
        }

        public static bool FileLooksLikeEnhancement(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[SniffBytes];
                int n = fs.Read(buf, 0, buf.Length);
                return LooksLikeEnhancementJson(Encoding.UTF8.GetString(buf, 0, n));
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "unreadable import candidate");
                return false;
            }
        }

        /// <summary>
        /// SHA-256 of the JSON with formatting stripped, so an exported copy that
        /// was re-indented still matches its library twin. Falls back to hashing
        /// the raw text when it does not parse.
        /// </summary>
        public static string NormalizedContentHash(string json)
        {
            string canon;
            try { canon = JToken.Parse(json).ToString(Formatting.None); }
            catch (JsonException) { canon = json ?? ""; } // swallow: not JSON, hash it verbatim
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canon));
            return Convert.ToHexString(bytes);
        }

        /// <summary>True when <paramref name="path"/> resolves to a file directly inside <paramref name="folder"/>.</summary>
        public static bool IsInsideFolder(string? path, string? folder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(dir, f, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex);
                return false;
            }
        }
    }
}
