using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Snapshots the live <see cref="AppSettings"/>, overwrites them with a session preset's
/// <see cref="SessionSettings"/>, and restores the originals when the session ends.
/// Settings writes only; feature-service start/stop stays in the effect orchestrator.
/// Field list mirrors WPF SessionEngine.SaveCurrentSettings/ApplySessionSettings/RestoreSettings
/// exactly (including the WPF quirk that SubliminalDuration is applied but never restored).
/// </summary>
public sealed class SessionSettingsScope
{
    private AppSettings? _savedSettings;
    private Dictionary<string, bool>? _savedSubliminalPool;
    private Dictionary<string, bool>? _savedBouncingTextPool;
    private Dictionary<string, bool>? _savedLockCardPool;

    /// <summary>True while a snapshot is held (between Apply and Restore).</summary>
    public bool IsActive => _savedSettings != null;

    /// <summary>
    /// Snapshot the current settings and overwrite them with the session's values.
    /// </summary>
    public void Apply(AppSettings current, SessionSettings settings)
    {
        Snapshot(current);

        // Flash Images
        current.FlashEnabled = settings.FlashEnabled;
        if (settings.FlashEnabled)
        {
            current.FlashFrequency = settings.FlashPerHour;
            current.FlashOpacity = settings.FlashOpacity;
            current.SimultaneousImages = settings.FlashImages;
            current.FlashClickable = settings.FlashClickable;
            current.CorruptionMode = settings.FlashHydra;
            current.FlashAudioEnabled = settings.FlashAudioEnabled;
        }

        // Subliminals - override phrases with session-specific ones
        current.SubliminalEnabled = settings.SubliminalEnabled;
        if (settings.SubliminalEnabled)
        {
            current.SubliminalFrequency = settings.SubliminalPerMin;
            current.SubliminalOpacity = settings.SubliminalOpacity;
            current.SubliminalDuration = settings.SubliminalFrames;

            if (settings.SubliminalPhrases.Count > 0)
            {
                OverridePool(current.SubliminalPool, settings.SubliminalPhrases, modAware: true);
                Log.Information("Session: Using subliminal phrases: {Phrases}",
                    string.Join(", ", settings.SubliminalPhrases));
            }
        }

        // Audio Whispers (Sub Audio)
        current.SubAudioEnabled = settings.AudioWhispersEnabled;
        if (settings.AudioWhispersEnabled)
        {
            current.SubAudioVolume = settings.WhisperVolume;
        }

        // Audio Ducking - apply session-specific duck level
        if (settings.AudioDuckLevel > 0)
        {
            current.AudioDuckingEnabled = true;
            current.DuckingLevel = settings.AudioDuckLevel;
        }

        // Bouncing Text - override phrases with session-specific ones
        current.BouncingTextEnabled = settings.BouncingTextEnabled;
        if (settings.BouncingTextEnabled)
        {
            current.BouncingTextSpeed = settings.BouncingTextSpeed;
            current.BouncingTextSize = settings.BouncingTextSize;
            current.BouncingTextOpacity = settings.BouncingTextOpacity;

            if (settings.BouncingTextPhrases.Count > 0)
            {
                OverridePool(current.BouncingTextPool, settings.BouncingTextPhrases, modAware: false);
            }
        }

        // Pink Filter (delayed start - don't enable yet if delayed)
        if (settings.PinkFilterEnabled && settings.PinkFilterStartMinute == 0)
        {
            current.PinkFilterEnabled = true;
            current.PinkFilterOpacity = settings.PinkFilterStartOpacity;
        }
        else
        {
            current.PinkFilterEnabled = false;
        }

        // Spiral (delayed start - don't enable yet if delayed)
        if (settings.SpiralEnabled && settings.SpiralStartMinute == 0)
        {
            current.SpiralEnabled = true;
            current.SpiralOpacity = settings.SpiralOpacity;
        }
        else
        {
            current.SpiralEnabled = false;
        }

        // Bubbles - delayed/intermittent starts stay off until the session tick enables them
        if (settings.BubblesEnabled)
        {
            current.BubblesFrequency = settings.BubblesFrequency;
            current.BubblesClickable = settings.BubblesClickable;
            current.BubblesEnabled = settings.BubblesStartMinute == 0 && !settings.BubblesIntermittent;
        }
        else
        {
            current.BubblesEnabled = false;
        }

        // Interactive Features
        current.MandatoryVideosEnabled = settings.MandatoryVideosEnabled;
        if (settings.MandatoryVideosEnabled && settings.VideosPerHour.HasValue)
        {
            current.VideosPerHour = settings.VideosPerHour.Value;
        }

        current.LockCardEnabled = settings.LockCardEnabled;
        if (settings.LockCardEnabled)
        {
            if (settings.LockCardFrequency.HasValue)
            {
                current.LockCardFrequency = settings.LockCardFrequency.Value;
            }

            if (settings.LockCardPhrases.Count > 0)
            {
                OverridePool(current.LockCardPhrases, settings.LockCardPhrases, modAware: true);
                Log.Information("Session: Using lock card phrases: {Phrases}",
                    string.Join(", ", settings.LockCardPhrases));
            }
        }

        current.BubbleCountEnabled = settings.BubbleCountEnabled;
        if (settings.BubbleCountEnabled && settings.BubbleCountFrequency.HasValue)
        {
            current.BubbleCountFrequency = settings.BubbleCountFrequency.Value;
        }
    }

    /// <summary>
    /// Restore the snapshot taken by <see cref="Apply"/>. No-op if no snapshot is held.
    /// </summary>
    public void Restore(AppSettings current)
    {
        if (_savedSettings == null) return;

        current.FlashEnabled = _savedSettings.FlashEnabled;
        current.FlashFrequency = _savedSettings.FlashFrequency;
        current.FlashOpacity = _savedSettings.FlashOpacity;
        current.FlashClickable = _savedSettings.FlashClickable;
        current.CorruptionMode = _savedSettings.CorruptionMode;
        current.FlashAudioEnabled = _savedSettings.FlashAudioEnabled;
        current.ImageScale = _savedSettings.ImageScale;
        current.SimultaneousImages = _savedSettings.SimultaneousImages;

        current.SubliminalEnabled = _savedSettings.SubliminalEnabled;
        current.SubliminalFrequency = _savedSettings.SubliminalFrequency;
        current.SubliminalOpacity = _savedSettings.SubliminalOpacity;

        if (_savedSubliminalPool != null)
        {
            RestorePool(current.SubliminalPool, _savedSubliminalPool);
            _savedSubliminalPool = null;
        }

        current.SubAudioEnabled = _savedSettings.SubAudioEnabled;
        current.SubAudioVolume = _savedSettings.SubAudioVolume;

        current.AudioDuckingEnabled = _savedSettings.AudioDuckingEnabled;
        current.DuckingLevel = _savedSettings.DuckingLevel;

        current.PinkFilterEnabled = _savedSettings.PinkFilterEnabled;
        current.PinkFilterOpacity = _savedSettings.PinkFilterOpacity;

        current.SpiralEnabled = _savedSettings.SpiralEnabled;
        current.SpiralOpacity = _savedSettings.SpiralOpacity;

        current.BubblesEnabled = _savedSettings.BubblesEnabled;
        current.BubblesFrequency = _savedSettings.BubblesFrequency;
        current.BubblesClickable = _savedSettings.BubblesClickable;

        current.BouncingTextEnabled = _savedSettings.BouncingTextEnabled;
        current.BouncingTextSpeed = _savedSettings.BouncingTextSpeed;
        current.BouncingTextSize = _savedSettings.BouncingTextSize;
        current.BouncingTextOpacity = _savedSettings.BouncingTextOpacity;

        if (_savedBouncingTextPool != null)
        {
            RestorePool(current.BouncingTextPool, _savedBouncingTextPool);
            _savedBouncingTextPool = null;
        }

        current.MandatoryVideosEnabled = _savedSettings.MandatoryVideosEnabled;
        current.VideosPerHour = _savedSettings.VideosPerHour;
        current.LockCardEnabled = _savedSettings.LockCardEnabled;
        current.LockCardFrequency = _savedSettings.LockCardFrequency;

        if (_savedLockCardPool != null)
        {
            RestorePool(current.LockCardPhrases, _savedLockCardPool);
            _savedLockCardPool = null;
        }

        current.PopQuizEnabled = _savedSettings.PopQuizEnabled;
        current.PopQuizFrequency = _savedSettings.PopQuizFrequency;
        current.BubbleCountEnabled = _savedSettings.BubbleCountEnabled;
        current.BubbleCountFrequency = _savedSettings.BubbleCountFrequency;

        _savedSettings = null;
    }

    private void Snapshot(AppSettings current)
    {
        // Field list mirrors WPF SessionEngine.SaveCurrentSettings.
        _savedSettings = new AppSettings
        {
            FlashEnabled = current.FlashEnabled,
            FlashFrequency = current.FlashFrequency,
            FlashOpacity = current.FlashOpacity,
            FlashClickable = current.FlashClickable,
            CorruptionMode = current.CorruptionMode,
            FlashAudioEnabled = current.FlashAudioEnabled,
            ImageScale = current.ImageScale,
            SimultaneousImages = current.SimultaneousImages,

            SubliminalEnabled = current.SubliminalEnabled,
            SubliminalFrequency = current.SubliminalFrequency,
            SubliminalOpacity = current.SubliminalOpacity,

            SubAudioEnabled = current.SubAudioEnabled,
            SubAudioVolume = current.SubAudioVolume,

            AudioDuckingEnabled = current.AudioDuckingEnabled,
            DuckingLevel = current.DuckingLevel,

            PinkFilterEnabled = current.PinkFilterEnabled,
            PinkFilterOpacity = current.PinkFilterOpacity,

            SpiralEnabled = current.SpiralEnabled,
            SpiralOpacity = current.SpiralOpacity,

            BubblesEnabled = current.BubblesEnabled,
            BubblesFrequency = current.BubblesFrequency,
            BubblesClickable = current.BubblesClickable,

            BouncingTextEnabled = current.BouncingTextEnabled,
            BouncingTextSpeed = current.BouncingTextSpeed,
            BouncingTextSize = current.BouncingTextSize,
            BouncingTextOpacity = current.BouncingTextOpacity,

            MandatoryVideosEnabled = current.MandatoryVideosEnabled,
            VideosPerHour = current.VideosPerHour,
            LockCardEnabled = current.LockCardEnabled,
            LockCardFrequency = current.LockCardFrequency,

            PopQuizEnabled = current.PopQuizEnabled,
            PopQuizFrequency = current.PopQuizFrequency,
            BubbleCountEnabled = current.BubbleCountEnabled,
            BubbleCountFrequency = current.BubbleCountFrequency,
        };

        _savedSubliminalPool = new Dictionary<string, bool>(current.SubliminalPool);
        _savedBouncingTextPool = new Dictionary<string, bool>(current.BouncingTextPool);
        _savedLockCardPool = new Dictionary<string, bool>(current.LockCardPhrases);
    }

    private static void OverridePool(Dictionary<string, bool> pool, List<string> sessionPhrases, bool modAware)
    {
        foreach (var key in pool.Keys.ToList())
        {
            pool[key] = false;
        }

        foreach (var phrase in sessionPhrases)
        {
            var effective = modAware ? CoreApp.Mods?.MakeModAware(phrase) ?? phrase : phrase;
            pool[effective] = true;
        }
    }

    private static void RestorePool(Dictionary<string, bool> pool, Dictionary<string, bool> saved)
    {
        pool.Clear();
        foreach (var kvp in saved)
        {
            pool[kvp.Key] = kvp.Value;
        }
    }
}
