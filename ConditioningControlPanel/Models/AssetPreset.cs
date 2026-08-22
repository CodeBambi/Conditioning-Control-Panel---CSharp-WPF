using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Represents a saved asset selection preset.
/// Stores which assets are disabled (blacklist approach).
/// </summary>
public class AssetPreset : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "New Preset";
    private DateTime _createdAt = DateTime.Now;
    private DateTime _lastUsed = DateTime.Now;
    private HashSet<string> _disabledAssetPaths = new();
    private int _enabledImageCount;
    private int _enabledVideoCount;

    /// <summary>
    /// Unique identifier for this preset
    /// </summary>
    [JsonProperty]
    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Display name for the preset
    /// </summary>
    [JsonProperty]
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    /// <summary>
    /// When this preset was created
    /// </summary>
    [JsonProperty]
    public DateTime CreatedAt
    {
        get => _createdAt;
        set { _createdAt = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// When this preset was last loaded/used
    /// </summary>
    [JsonProperty]
    public DateTime LastUsed
    {
        get => _lastUsed;
        set { _lastUsed = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Set of relative paths to disabled assets.
    /// Files NOT in this set are active/enabled.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<string> DisabledAssetPaths
    {
        get => _disabledAssetPaths;
        set { _disabledAssetPaths = value ?? new(); OnPropertyChanged(); }
    }

    /// <summary>
    /// Number of enabled images when this preset was saved
    /// </summary>
    [JsonProperty]
    public int EnabledImageCount
    {
        get => _enabledImageCount;
        set { _enabledImageCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    /// <summary>
    /// Number of enabled videos when this preset was saved
    /// </summary>
    [JsonProperty]
    public int EnabledVideoCount
    {
        get => _enabledVideoCount;
        set { _enabledVideoCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    /// <summary>
    /// Display text for ComboBox showing name and counts
    /// </summary>
    [JsonIgnore]
    public string DisplayText => $"{Name} ({EnabledImageCount} img, {EnabledVideoCount} vid)";

    /// <summary>
    /// Whether this is the default "All Assets" preset
    /// </summary>
    [JsonIgnore]
    public bool IsDefault => Id == "default-all";

    /// <summary>
    /// Create a preset from current settings
    /// </summary>
    public static AssetPreset FromCurrentSettings(string name, int imageCount, int videoCount)
    {
        return new AssetPreset
        {
            Name = name,
            DisabledAssetPaths = new HashSet<string>(App.Settings.Current.DisabledAssetPaths),
            EnabledImageCount = imageCount,
            EnabledVideoCount = videoCount,
            CreatedAt = DateTime.Now,
            LastUsed = DateTime.Now
        };
    }

    /// <summary>
    /// Apply this preset to the current settings
    /// </summary>
    public void ApplyToSettings()
    {
        App.Settings.Current.DisabledAssetPaths = new HashSet<string>(DisabledAssetPaths);
        LastUsed = DateTime.Now;
    }

    /// <summary>
    /// Update this preset with current settings
    /// </summary>
    public void UpdateFromCurrentSettings(int imageCount, int videoCount)
    {
        DisabledAssetPaths = new HashSet<string>(App.Settings.Current.DisabledAssetPaths);
        EnabledImageCount = imageCount;
        EnabledVideoCount = videoCount;
        LastUsed = DateTime.Now;
    }

    /// <summary>
    /// Create the default "All Assets" preset
    /// </summary>
    public static AssetPreset CreateDefault()
    {
        return new AssetPreset
        {
            Id = "default-all",
            Name = "All Assets",
            DisabledAssetPaths = new HashSet<string>(),
            EnabledImageCount = 0,
            EnabledVideoCount = 0,
            CreatedAt = DateTime.Now,
            LastUsed = DateTime.Now
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
