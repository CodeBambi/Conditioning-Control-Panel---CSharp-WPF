using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Serilog;

namespace ConditioningControlPanel.Services.Billboard
{
    /// <summary>
    /// The deck's snoozes on disk. WPF 7.1.5 kept them in AppSettings.BillboardSnoozedUntil; the
    /// port's AppSettings has no such field yet, so they live in one small JSON file under the
    /// user-data board folder (card id to the UTC moment it may show again). Machine-local, never
    /// synced, exactly like the WPF field. A file that does not parse reads as no snoozes.
    /// </summary>
    public sealed class BillboardSnoozeStore
    {
        private readonly string _path;
        private readonly object _gate = new();

        public BillboardSnoozeStore(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

        /// <summary>The default file: &lt;user data&gt;/board/snoozes.json.</summary>
        public static BillboardSnoozeStore ForUserData() =>
            new(Path.Combine(CorePaths.UserData, "board", "snoozes.json"));

        public string FilePath => _path;

        public Dictionary<string, DateTime> Load()
        {
            var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            try
            {
                lock (_gate)
                {
                    if (!File.Exists(_path)) return result;
                    var raw = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(_path));
                    if (raw == null) return result;
                    foreach (var (id, until) in raw)
                        if (!string.IsNullOrWhiteSpace(id) && id.Length <= 200)
                            result[id] = DateTime.SpecifyKind(until.ToUniversalTime(), DateTimeKind.Utc);
                }
            }
            catch (Exception ex) { Log.Debug("Billboard snoozes unreadable, starting clean: {E}", ex.Message); }
            return result;
        }

        public void Save(IReadOnlyDictionary<string, DateTime> snoozes)
        {
            try
            {
                lock (_gate)
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var tmp = _path + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(snoozes));
                    File.Move(tmp, _path, overwrite: true);
                }
            }
            catch (Exception ex) { Log.Debug("Billboard snooze save failed: {E}", ex.Message); }
        }
    }
}
