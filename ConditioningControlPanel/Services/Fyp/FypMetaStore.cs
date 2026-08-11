using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// Per-asset media metadata the feed page probed at playback time (duration + pixel
/// dimensions), persisted to <c>fyp_meta.json</c> so the next launch can build the full
/// segment grid and know orientation up front. Keyed by manifest id (relative path).
/// Purely additive quality data — losing the file just means the page re-probes.
/// </summary>
internal sealed class FypMetaStore
{
    private static string FilePath => Path.Combine(App.UserDataPath, "fyp_meta.json");
    private const int MaxEntries = 10000;

    public sealed class Meta
    {
        public long? DurationMs { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        /// <summary>Load/decode failures the page reported for this asset (#562). Null until
        /// the first strike so the JSON stays additive for old files.</summary>
        public int? FailStrikes { get; set; }
    }

    /// <summary>Strikes needed before the manifest stops serving an asset.</summary>
    public const int FailStrikeLimit = 2;

    private readonly Dictionary<string, Meta> _byId;
    private readonly object _lock = new();
    private bool _dirty;

    public FypMetaStore()
    {
        _byId = Load();
    }

    public Meta? Get(string id)
    {
        lock (_lock) return _byId.TryGetValue(id, out var m) ? m : null;
    }

    /// <summary>Merge a page probe. Idempotent; junk values are ignored.</summary>
    public void Update(string? id, long? durationMs, int? width, int? height)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (_lock)
        {
            if (!_byId.TryGetValue(id, out var m))
            {
                if (_byId.Count >= MaxEntries) return;
                m = new Meta();
                _byId[id] = m;
            }
            if (durationMs is > 0 && m.DurationMs != durationMs) { m.DurationMs = durationMs; _dirty = true; }
            if (width is > 0 && height is > 0 && (m.Width != width || m.Height != height))
            {
                m.Width = width;
                m.Height = height;
                _dirty = true;
            }
        }
    }

    /// <summary>
    /// One failure strike for an asset the page could not decode/load (#562). The page
    /// de-dupes per session, so a strike ≈ one session that failed on this file — at
    /// <see cref="FailStrikeLimit"/> the manifest stops serving it. Saved eagerly: strikes
    /// are rare and losing one to a crash would reset the counter.
    /// </summary>
    public void RecordFailure(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (_lock)
        {
            if (!_byId.TryGetValue(id, out var m))
            {
                if (_byId.Count >= MaxEntries) return;
                m = new Meta();
                _byId[id] = m;
            }
            m.FailStrikes = (m.FailStrikes ?? 0) + 1;
            _dirty = true;
        }
        Save();
    }

    public void Save()
    {
        lock (_lock)
        {
            if (!_dirty) return;
            try
            {
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(_byId));
                _dirty = false;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("FypMetaStore: save failed: {E}", ex.Message);
            }
        }
    }

    private static Dictionary<string, Meta> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonConvert.DeserializeObject<Dictionary<string, Meta>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("FypMetaStore: load failed: {E}", ex.Message);
        }
        return new();
    }
}
