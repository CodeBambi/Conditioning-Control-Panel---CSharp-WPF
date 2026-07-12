using System.Collections.Generic;
using ConditioningControlPanel.Core.Services.AvatarTube;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the real <see cref="IWhisperService"/> implementation
/// (<see cref="SettingsBackedWhisperService"/>): <c>IsMuted</c> is the inverse of
/// <c>AppSettings.SubAudioEnabled</c> and persists on set — byte-faithful to the AvatarTubeWindow
/// fallback it replaces (AvatarTubeWindow.axaml.cs:909-914). The Avalonia DI now resolves
/// <c>IWhisperService</c> to this type (ServiceCollectionExtensions); actual DI resolution is
/// exercised by the head's smoke test.
/// </summary>
public class SettingsBackedWhisperServiceTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public int SaveCalls;
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { SaveCalls++; }
        public void Save(bool suppressCloudBackup = false) { SaveCalls++; }
        public void SaveImmediate(bool suppressCloudBackup = false) { SaveCalls++; }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    [Fact]
    public void IsMuted_DefaultsToTrue_WhenWhispersDisabled()
    {
        // new AppSettings() has SubAudioEnabled = false ⇒ whispers muted by default.
        var settings = new FakeSettingsService();
        var sut = new SettingsBackedWhisperService(settings);

        Assert.True(sut.IsMuted);
    }

    [Fact]
    public void IsMuted_ReadsLiveSubAudioEnabled()
    {
        var settings = new FakeSettingsService();
        var sut = new SettingsBackedWhisperService(settings);

        settings.Current.SubAudioEnabled = true;
        Assert.False(sut.IsMuted);

        settings.Current.SubAudioEnabled = false;
        Assert.True(sut.IsMuted);
    }

    [Fact]
    public void IsMuted_SetTrue_DisablesWhispersAndPersists()
    {
        var settings = new FakeSettingsService();
        settings.Current.SubAudioEnabled = true;       // currently unmuted
        var sut = new SettingsBackedWhisperService(settings);

        sut.IsMuted = true;                             // mute ⇒ whispers off + save (AvatarTubeWindow.axaml.cs:913-914)

        Assert.False(settings.Current.SubAudioEnabled);
        Assert.True(sut.IsMuted);
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void IsMuted_SetFalse_EnablesWhispersAndPersists()
    {
        var settings = new FakeSettingsService();
        var sut = new SettingsBackedWhisperService(settings);

        sut.IsMuted = false;                            // unmute ⇒ whispers on + save

        Assert.True(settings.Current.SubAudioEnabled);
        Assert.False(sut.IsMuted);
        Assert.Equal(1, settings.SaveCalls);
    }
}
