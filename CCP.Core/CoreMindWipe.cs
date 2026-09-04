using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The mind-wipe surface seam: the six things the Mind Wipe card asks of the playing service.
    /// Deciding <em>when</em> a clip fires is portable and lives in
    /// <see cref="Services.MindWipeSchedule"/>; playing one is not, and stays in the head.
    ///
    /// <para><b>Why not <see cref="CoreAudio"/>.</b> Mind wipe's loop is a two-player crossfade
    /// whose interval comes from the clip's own duration, it never ducks, and its Clean Slate
    /// award is measured from the moment the loop started. <c>CoreAudio.PlayOneShot</c> can express
    /// none of that: unseeded it fires <c>onFinished</c> immediately, so a loop faked out of
    /// one-shots would play once and go quiet while the feature reported itself as looping.
    /// Looping playback is a genuine addition to a head's audio surface, not a re-spelling of the
    /// one-shot, so it is asked for here - shaped like the mind-wipe service - rather than bolted
    /// onto the shared audio seam where every other caller would inherit it.</para>
    ///
    /// <para><b>Unseeded means "no mind-wipe audio on this head".</b> Every action is a no-op,
    /// <see cref="IsLooping"/> is false and <see cref="ClipCount"/> is 0. That is the truth on a
    /// head with no mind-wipe service, and it is the safe direction on the only member that gates
    /// anything: a card that asks <see cref="IsLooping"/> before restarting a loop is told "no
    /// loop is running", so it starts nothing rather than claiming a silent one.</para>
    ///
    /// <para>Volume crosses as a 0..1 fraction, matching the service; the settings model stores
    /// 0..100, so every call site divides by 100 exactly as the WPF card does.</para>
    /// </summary>
    public static class CoreMindWipe
    {
        /// <summary>Play one clip now. Deliberately usable with the service stopped - that is what
        /// the card's Test button and the gaze-minigame reward both do on Windows.</summary>
        public static volatile Action? TriggerOnceProvider;

        /// <summary>Start the continuous background loop at the given 0..1 volume.</summary>
        public static volatile Action<double>? StartLoopProvider;

        /// <summary>Stop the continuous background loop.</summary>
        public static volatile Action? StopLoopProvider;

        /// <summary>True only while a loop is actually playing.</summary>
        public static volatile Func<bool>? IsLoopingProvider;

        /// <summary>Live-apply frequency (plays per hour) and 0..1 volume to the running service.</summary>
        public static volatile Action<double, double>? UpdateSettingsProvider;

        /// <summary>Re-scan the clip folders after the user picked or cleared a custom file.</summary>
        public static volatile Action? ReloadClipsProvider;

        /// <summary>How many clips the service currently has to choose from.</summary>
        public static volatile Func<int>? ClipCountProvider;

        public static void TriggerOnce() { try { TriggerOnceProvider?.Invoke(); } catch { } }
        public static void StartLoop(double volume) { try { StartLoopProvider?.Invoke(volume); } catch { } }
        public static void StopLoop() { try { StopLoopProvider?.Invoke(); } catch { } }
        public static void UpdateSettings(double frequencyPerHour, double volume)
        {
            try { UpdateSettingsProvider?.Invoke(frequencyPerHour, volume); } catch { }
        }
        public static void ReloadClips() { try { ReloadClipsProvider?.Invoke(); } catch { } }

        public static bool IsLooping
        {
            get { try { return IsLoopingProvider?.Invoke() ?? false; } catch { return false; } }
        }

        public static int ClipCount
        {
            get { try { return ClipCountProvider?.Invoke() ?? 0; } catch { return 0; } }
        }
    }
}
