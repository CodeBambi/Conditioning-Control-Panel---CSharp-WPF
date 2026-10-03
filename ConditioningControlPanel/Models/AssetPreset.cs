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
    private HashSet<string> _disabledAssetFolders = new(StringComparer.OrdinalIgnoreCase);
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
    /// Folders unticked as a whole. New files in them stay excluded (ccp-bugs #1231).
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<string> DisabledAssetFolders
    {
        get => _disabledAssetFolders;
        set { _disabledAssetFolders = new HashSet<string>(value ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase); OnPropertyChanged(); }
    }

    /// <summary>
    /// The online (Scrolller) niche ids saved with this preset (ccp-bugs #1142). Null on a preset
    /// saved before presets carried it: applying that preset leaves the current selection alone.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? OnlineNiches { get; set; }

    /// <summary>The user-added subreddits saved with this preset. Null = leave the current ones.</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? OnlineCustomSubs { get; set; }

    /// <summary>
    /// The app-wide media source ("local" / "online" / "mixed") saved with this preset. Null =
    /// leave the current source. Applying never turns online media on without the remote-media
    /// consent (see <c>AssetPresetService.ApplyOnlineChoice</c>).
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? MediaSource { get; set; }

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
    /// The CLOSED ComboBox reads this, not <c>DisplayMemberPath</c> (#976: the Asset Browser's
    /// preset picker showed "ConditioningControlPanel.M" - a clipped type name - once a preset
    /// was chosen and the dropdown shut).
    ///
    /// <para>WPF only wires <c>DisplayMemberPath</c> into a ComboBox's selection box from its
    /// THEME template; <c>ComboBox.SelectionBoxItemTemplate</c> is null unless an explicit
    /// <c>ItemTemplate</c> is set, so every replacement ControlTemplate in this app (the Velvet
    /// Kit implicit style in Resources/Theme/Inputs.xaml, DarkComboBoxStyle, the Deeper editor's)
    /// falls back to ToString() for the closed box while the open dropdown still renders the
    /// path correctly. <see cref="PhrasePreset"/> and AudioService.AudioOutputDevice already
    /// carry the same one-liner for the same reason - this model was the one that missed it.</para>
    /// </summary>
    public override string ToString() => DisplayText;

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
        var preset = new AssetPreset
        {
            Name = name,
            DisabledAssetPaths = new HashSet<string>(App.Settings.Current.DisabledAssetPaths),
            DisabledAssetFolders = new HashSet<string>(App.Settings.Current.DisabledAssetFolders),
            EnabledImageCount = imageCount,
            EnabledVideoCount = videoCount,
            CreatedAt = DateTime.Now,
            LastUsed = DateTime.Now
        };
        preset.CaptureOnlineChoice(App.Settings.Current);
        return preset;
    }

    /// <summary>
    /// Copies the online (Scrolller) selection and the media source off <paramref name="s"/>
    /// (ccp-bugs #1142). Saving a preset is what opts it in to switching them.
    /// </summary>
    public void CaptureOnlineChoice(AppSettings s)
    {
        if (s == null) return;
        OnlineNiches = new List<string>(s.FypOnlineNiches ?? new List<string>());
        OnlineCustomSubs = new List<string>(s.FypOnlineCustomSubs ?? new List<string>());
        MediaSource = s.MediaSource;
    }

    /// <summary>
    /// Apply this preset to the current settings. Same write as
    /// <c>Services.AssetPresetService.Apply</c>, which is the path the app uses.
    /// </summary>
    public void ApplyToSettings()
    {
        App.Settings.Current.DisabledAssetPaths = new HashSet<string>(DisabledAssetPaths);
        App.Settings.Current.DisabledAssetFolders = new HashSet<string>(DisabledAssetFolders);
        Services.AssetPresetService.ApplyOnlineChoice(App.Settings.Current, this);
        LastUsed = DateTime.Now;
    }

    /// <summary>
    /// Update this preset with current settings
    /// </summary>
    public void UpdateFromCurrentSettings(int imageCount, int videoCount)
    {
        DisabledAssetPaths = new HashSet<string>(App.Settings.Current.DisabledAssetPaths);
        DisabledAssetFolders = new HashSet<string>(App.Settings.Current.DisabledAssetFolders);
        CaptureOnlineChoice(App.Settings.Current);
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
