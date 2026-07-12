using System;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Core.Services.AvatarTube;

/// <summary>
/// The real <see cref="IWhisperService"/> implementation: muting sub-audio whispers from the
/// AvatarTube companion menu. <c>IsMuted</c> is the inverse of <c>AppSettings.SubAudioEnabled</c>
/// and persists on set, byte-faithful to the window's pre-impl fallback
/// (AvatarTubeWindow.axaml.cs:909-914: <c>currentlyMuted = SubAudioEnabled != true</c>;
/// <c>SubAudioEnabled = !muted; Save()</c>). Portable so the Avalonia DI registers one impl and
/// the unit tests can exercise it without a live DI container.
/// </summary>
public sealed class SettingsBackedWhisperService : IWhisperService
{
    private readonly ISettingsService _settings;

    public SettingsBackedWhisperService(ISettingsService settings)
        => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>True when whispers are disabled (SubAudioEnabled is false/null).</summary>
    public bool IsMuted
    {
        get => _settings.Current?.SubAudioEnabled != true;
        set
        {
            var current = _settings.Current;
            if (current == null) return;
            // muted ⇔ whispers disabled (AvatarTubeWindow.axaml.cs:913).
            current.SubAudioEnabled = !value;
            _settings.Save();
        }
    }
}
