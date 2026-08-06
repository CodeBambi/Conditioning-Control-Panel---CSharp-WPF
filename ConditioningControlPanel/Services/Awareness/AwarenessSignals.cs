using System;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// What the foreground window is at one instant, before any privacy rule has been applied.
    ///
    /// <para><b>This is the most sensitive object in the feature.</b> <see cref="Title"/> is a raw
    /// window title: it may contain a document name, an email subject, a bank's name or the title of
    /// an adult video. It exists for exactly three purposes — cluster matching, incognito detection,
    /// and (only for a user-allow-listed app) the sanitised title on the frame — and it is never
    /// written to disk, never logged, and never placed on a <see cref="ContextFrame"/> except through
    /// <see cref="AwarenessObserverPolicy.SanitizeAllowedTitle"/>. Nothing downstream of
    /// <see cref="AwarenessObserverPolicy.EvaluatePrivacy"/> ever sees it.</para>
    /// </summary>
    /// <param name="Handle">Foreground HWND, or <see cref="IntPtr.Zero"/> when there is none.</param>
    /// <param name="Title">Raw window title. Transient — see the class remarks.</param>
    /// <param name="ProcessName">Owning process name without the extension, lowercased ("chrome", "hades").</param>
    /// <param name="IsFullscreen">The window covers a whole monitor (and is not the shell/desktop).</param>
    public sealed record ForegroundSample(IntPtr Handle, string Title, string ProcessName, bool IsFullscreen);

    /// <summary>
    /// Reads the foreground window. Behind an interface so the observer's whole pipeline — dwell gate,
    /// transitions, privacy drops, DND — is testable without a desktop.
    /// </summary>
    public interface IForegroundProbe
    {
        /// <summary>The current foreground window, or null when there is nothing usable.</summary>
        ForegroundSample? Read();
    }

    /// <summary>
    /// Real user-input state. "The title stopped changing" is not idleness and never was
    /// (doc 02 §1.4); this is <c>GetLastInputInfo</c>, the same source
    /// <see cref="ActivityTracker"/> uses for anti-cheat.
    /// </summary>
    public interface IInputProbe : IDisposable
    {
        /// <summary>Seconds since the last keyboard or mouse input, machine-wide.</summary>
        int IdleSeconds { get; }

        /// <summary>
        /// True while the user is in a sustained typing burst — the "don't interrupt someone
        /// mid-sentence" gate (doc 02 §4.2).
        ///
        /// <para><b>What this actually measures, precisely, because the privacy copy must not
        /// overstate it:</b> input-event samples, not keystrokes. The probe samples
        /// <c>GetLastInputInfo</c> and the cursor position on a short cadence and counts samples in
        /// which fresh input arrived while the cursor stood still. It cannot see which keys were
        /// pressed, cannot see text, and installs no hook of any kind.</para>
        /// </summary>
        bool IsTypingBurst { get; }

        /// <summary>Arms the sampler.</summary>
        void Start();

        /// <summary>Disarms the sampler.</summary>
        void Stop();
    }

    /// <summary>
    /// Whether any capture endpoint has an active session — i.e. something is using the microphone.
    /// Half of the meeting-DND test (the other half is the foreground process).
    /// </summary>
    public interface IMicrophoneProbe
    {
        /// <summary>True when the mic is in use. Implementations cache; this is called on every poll.</summary>
        bool IsInUse(DateTime at);
    }

    /// <summary>
    /// What Windows says is playing, from SMTC.
    ///
    /// <para><b>Local only.</b> Track titles and artists are machine-local signal: they may ride the
    /// local Ollama projection, they never reach the cloud projection (which carries the repeat COUNT
    /// and nothing else), and they are never written to the ledger — <see cref="ActivityLedger"/> has
    /// no parameter that could carry one.</para>
    /// </summary>
    /// <param name="PlaybackState">"Playing", "Paused", "Stopped", … as reported by SMTC.</param>
    /// <param name="Position">Playback position, when the session reports a timeline. Used to spot loops.</param>
    public sealed record MediaSample(string Title, string? Artist, string PlaybackState, TimeSpan Position)
    {
        /// <summary>True when this is actually playing rather than paused/stopped.</summary>
        public bool IsPlaying =>
            string.Equals(PlaybackState, "Playing", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Now-playing watcher. Optional by construction: SMTC is denied outright on some machines and
    /// absent on others, and awareness losing one signal must never be more than one missing joke.
    /// </summary>
    public interface IMediaWatcher : IDisposable
    {
        /// <summary>The last successful read, or null when nothing is playing / SMTC is unavailable.</summary>
        MediaSample? Current { get; }

        /// <summary>True once a session manager was obtained. False means the feature is simply off.</summary>
        bool IsAvailable { get; }

        /// <summary>Begins watching. Never throws; failure leaves <see cref="IsAvailable"/> false.</summary>
        void Start();

        /// <summary>Stops watching.</summary>
        void Stop();
    }

    /// <summary>
    /// CCP's own state at cut time — the "you levelled up and immediately opened Reddit as a reward?"
    /// material, plus the surfaces during which she must not butt in.
    /// </summary>
    /// <param name="BlockingSurfaceActive">
    /// A mandatory video, a lock card or a DtRH run is on screen. She already has scripted lines
    /// there; awareness talking over them is the two-mouths bug wearing a different hat.
    /// </param>
    public sealed record AppStateSample(
        bool SessionRunning,
        int UserLevel,
        int LoginStreakDays,
        string? RecentAchievementId,
        bool BlockingSurfaceActive)
    {
        /// <summary>What a headless or half-built app reads as: nothing interesting, nothing blocking.</summary>
        public static AppStateSample Empty { get; } = new(false, 0, 0, null, false);
    }

    /// <summary>Reads <see cref="AppStateSample"/>. Injectable so the frame builder tests headlessly.</summary>
    public interface IAppStateProbe
    {
        /// <summary>Current in-app state. Must never throw.</summary>
        AppStateSample Read(DateTime at);
    }
}
