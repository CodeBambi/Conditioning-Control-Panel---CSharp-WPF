using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The settings side of "apply this asset preset", kept off the window so the launcher's Media
/// dialog can switch presets while the panel is created but tray-hidden. The panel's own preset
/// combo (MainWindow.Assets.cs) goes through the same call and then repaints its tree; the
/// launcher goes through it and then asks the panel only for the pool invalidation. One truth
/// for what a switch WRITES, two callers for what it repaints.
/// </summary>
public static class AssetPresetService
{
    /// <summary>The preset the games are drawing from right now: the current id when it still
    /// exists, else the default "All Assets" row, else the first preset, else null.</summary>
    public static AssetPreset? Active(AppSettings s)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        var presets = s.AssetPresets ?? new List<AssetPreset>();
        return Find(presets, s.CurrentAssetPresetId)
            ?? presets.FirstOrDefault(p => p.IsDefault)
            ?? presets.FirstOrDefault();
    }

    public static AssetPreset? Find(IEnumerable<AssetPreset>? presets, string? id)
    {
        if (presets == null || string.IsNullOrEmpty(id)) return null;
        return presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Makes <paramref name="presetId"/> the active preset: its disabled set replaces the app's,
    /// it is stamped as last used, and the id is remembered. Returns the preset, or null when
    /// the id names nothing, in which case nothing is written. Saving and cache invalidation
    /// are the caller's (the panel's InvalidateAssetPoolsAfterSelectionChange does both).
    /// </summary>
    public static AssetPreset? Apply(AppSettings s, string? presetId)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        var preset = Find(s.AssetPresets, presetId);
        if (preset == null) return null;

        s.DisabledAssetPaths = new HashSet<string>(preset.DisabledAssetPaths ?? new HashSet<string>());
        preset.LastUsed = DateTime.Now;
        s.CurrentAssetPresetId = preset.Id;
        return preset;
    }
}
