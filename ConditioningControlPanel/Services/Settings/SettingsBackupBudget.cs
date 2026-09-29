using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Keeps the cloud settings backup under the server's body limit.
    ///
    /// <para>Why: <c>/v2/user/backup-settings</c> takes a JSON body of at most 1 MB, and the backup is
    /// the whole settings object gzipped and base64'd. A big local library makes it bigger than that:
    /// <see cref="Models.AppSettings.DisabledAssetPaths"/>, the legacy
    /// <see cref="Models.AppSettings.ActiveAssetPaths"/> and every <c>AssetPreset</c> hold one relative
    /// path per file, with no bound. Sep 2026 telemetry: ~3k 413s a day, every one of them retried five
    /// minutes later with the same body.</para>
    ///
    /// <para>The rule: encode; if the base64 is over <see cref="MaxEncodedBytes"/>, drop the bulky
    /// machine-local lists in <see cref="TrimOrder"/> one at a time until it fits. Those are per-file
    /// path lists relative to one PC's folder, the least portable thing in the backup, and a restore
    /// without them simply leaves every file active. Still over after that: the backup is skipped and
    /// the caller logs <see cref="LargestProperties"/> once so the next culprit has a name.</para>
    /// </summary>
    public static class SettingsBackupBudget
    {
        /// <summary>
        /// Base64 bytes we are willing to send. The server refuses the JSON body at 1 MB, and from
        /// CCP-Server #233 on refuses settings_data itself past 700 KB; this stays under both.
        /// </summary>
        public const int MaxEncodedBytes = 660_000;

        /// <summary>Dropped in this order, only while the backup is still over budget. The named
        /// presets go last: they carry names and settings as well as a path list.</summary>
        public static readonly IReadOnlyList<string> TrimOrder = new[]
        {
            "ActiveAssetPaths",      // legacy whitelist, superseded by DisabledAssetPaths
            "DisabledAssetPaths",
            "AssetPresets",          // each preset carries its own copy of a path list
        };

        public sealed class Result
        {
            public byte[] Compressed { get; init; } = Array.Empty<byte>();
            public string Base64 { get; init; } = "";
            public List<string> Trimmed { get; init; } = new();
            public bool Fits => Base64.Length <= MaxEncodedBytes;
        }

        /// <summary>
        /// Encode <paramref name="settings"/> (already stripped of excluded keys), trimming it in place
        /// when it is over <paramref name="budget"/>.
        /// </summary>
        public static Result Encode(JObject settings, int budget = MaxEncodedBytes)
        {
            var trimmed = new List<string>();
            var removed = new List<JProperty>();
            var (gz, b64) = Pack(settings);
            foreach (var key in TrimOrder)
            {
                if (b64.Length <= budget) break;
                var prop = settings.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
                if (prop == null) continue;
                prop.Remove();
                removed.Add(prop);
                trimmed.Add(prop.Name);
                (gz, b64) = Pack(settings);
            }
            // Put back whatever still fits: only the last list dropped is known to be too big, so a
            // library whose presets are the bulk keeps its own path lists. The server keeps one copy.
            for (int i = removed.Count - 2; i >= 0 && b64.Length <= budget; i--)
            {
                settings.Add(removed[i]);
                var (g, b) = Pack(settings);
                if (b.Length <= budget) { (gz, b64) = (g, b); trimmed.Remove(removed[i].Name); }
                else removed[i].Remove();
            }
            return new Result { Compressed = gz, Base64 = b64, Trimmed = trimmed };
        }

        /// <summary>The <paramref name="count"/> largest top-level properties by serialized length.</summary>
        public static List<(string Name, int Bytes)> LargestProperties(JObject settings, int count = 5)
            => settings.Properties()
                .Select(p => (p.Name, Bytes: Encoding.UTF8.GetByteCount(p.Value.ToString(Formatting.None))))
                .OrderByDescending(x => x.Bytes)
                .Take(count)
                .ToList();

        private static (byte[] Gz, string B64) Pack(JObject settings)
        {
            var json = settings.ToString(Formatting.None);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                gzip.Write(bytes, 0, bytes.Length);
            }
            var gz = output.ToArray();
            return (gz, Convert.ToBase64String(gz));
        }
    }
}
