using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Represents a saved companion phrase configuration preset.
/// Stores disabled/removed phrases, custom phrases, and audio overrides.
/// </summary>
public class PhrasePreset : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "New Preset";
    private DateTime _createdAt = DateTime.Now;
    private DateTime _lastUsed = DateTime.Now;
    private int _activePhraseCount;
    private HashSet<string> _disabledPhraseIds = new();
    private HashSet<string> _removedPhraseIds = new();
    private List<CustomCompanionPhrase> _customPhrases = new();
    private Dictionary<string, string> _phraseAudioOverrides = new();

    [JsonProperty]
    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    [JsonProperty]
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    [JsonProperty]
    public DateTime CreatedAt
    {
        get => _createdAt;
        set { _createdAt = value; OnPropertyChanged(); }
    }

    [JsonProperty]
    public DateTime LastUsed
    {
        get => _lastUsed;
        set { _lastUsed = value; OnPropertyChanged(); }
    }

    [JsonProperty]
    public int ActivePhraseCount
    {
        get => _activePhraseCount;
        set { _activePhraseCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<string> DisabledPhraseIds
    {
        get => _disabledPhraseIds;
        set { _disabledPhraseIds = value ?? new(); OnPropertyChanged(); }
    }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<string> RemovedPhraseIds
    {
        get => _removedPhraseIds;
        set { _removedPhraseIds = value ?? new(); OnPropertyChanged(); }
    }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<CustomCompanionPhrase> CustomPhrases
    {
        get => _customPhrases;
        set { _customPhrases = value ?? new(); OnPropertyChanged(); }
    }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, string> PhraseAudioOverrides
    {
        get => _phraseAudioOverrides;
        set { _phraseAudioOverrides = value ?? new(); OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string DisplayText => $"{Name} ({ActivePhraseCount} phrases)";

    /// <summary>
    /// Create a preset from current settings
    /// </summary>
    public static PhrasePreset FromCurrentSettings(string name, int activePhraseCount)
    {
        var settings = App.Settings.Current;
        return new PhrasePreset
        {
            Name = name,
            ActivePhraseCount = activePhraseCount,
            DisabledPhraseIds = new HashSet<string>(settings.DisabledPhraseIds),
            RemovedPhraseIds = new HashSet<string>(settings.RemovedPhraseIds),
            CustomPhrases = settings.CustomCompanionPhrases
                .Select(p => new CustomCompanionPhrase
                {
                    Id = p.Id,
                    Text = p.Text,
                    Category = p.Category,
                    AudioFileName = p.AudioFileName,
                    Enabled = p.Enabled
                }).ToList(),
            PhraseAudioOverrides = new Dictionary<string, string>(settings.PhraseAudioOverrides),
            CreatedAt = DateTime.Now,
            LastUsed = DateTime.Now
        };
    }

    /// <summary>
    /// Apply this preset to the current settings
    /// </summary>
    public void ApplyToSettings()
    {
        var settings = App.Settings.Current;
        settings.DisabledPhraseIds = new HashSet<string>(DisabledPhraseIds);
        settings.RemovedPhraseIds = new HashSet<string>(RemovedPhraseIds);
        settings.CustomCompanionPhrases = CustomPhrases
            .Select(p => new CustomCompanionPhrase
            {
                Id = p.Id,
                Text = p.Text,
                Category = p.Category,
                AudioFileName = p.AudioFileName,
                Enabled = p.Enabled
            }).ToList();
        settings.PhraseAudioOverrides = new Dictionary<string, string>(PhraseAudioOverrides);
        LastUsed = DateTime.Now;
    }

    /// <summary>
    /// Update this preset with current settings
    /// </summary>
    public void UpdateFromCurrentSettings(int activePhraseCount)
    {
        var settings = App.Settings.Current;
        DisabledPhraseIds = new HashSet<string>(settings.DisabledPhraseIds);
        RemovedPhraseIds = new HashSet<string>(settings.RemovedPhraseIds);
        CustomPhrases = settings.CustomCompanionPhrases
            .Select(p => new CustomCompanionPhrase
            {
                Id = p.Id,
                Text = p.Text,
                Category = p.Category,
                AudioFileName = p.AudioFileName,
                Enabled = p.Enabled
            }).ToList();
        PhraseAudioOverrides = new Dictionary<string, string>(settings.PhraseAudioOverrides);
        ActivePhraseCount = activePhraseCount;
        LastUsed = DateTime.Now;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
