using System;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// A probe that subscribes to an app feed when the observer starts (WPF
    /// <c>AppStateProbe.Attach</c>, which the observer reached through a concrete cast).
    /// </summary>
    public interface IAttachableProbe
    {
        void Attach();
    }

    /// <summary>
    /// The observer's platform seam. In WPF the observer built its own Win32 probes
    /// (<c>Win32ForegroundProbe</c>, <c>Win32InputProbe</c>, <c>WasapiMicrophoneProbe</c>,
    /// <c>SmtcMediaWatcher</c>, <c>AppStateProbe</c>) and a <c>DispatcherTimer</c>. Core cannot name
    /// any of them, so the head registers factories here before it constructs the observer.
    ///
    /// <para>With no factory the observer gets a probe that sees nothing: no foreground window means
    /// no frame is ever cut, which is the fail-closed direction.</para>
    /// </summary>
    public static class AwarenessPlatform
    {
        public static volatile Func<IForegroundProbe>? ForegroundProbeFactory;
        public static volatile Func<IInputProbe>? InputProbeFactory;
        public static volatile Func<IMicrophoneProbe>? MicrophoneProbeFactory;
        public static volatile Func<IMediaWatcher?>? MediaWatcherFactory;
        public static volatile Func<IAppStateProbe>? AppStateProbeFactory;

        /// <summary>
        /// Starts a repeating UI-thread timer (interval, tick) and returns its stop handle. WPF used a
        /// <c>DispatcherTimer</c> at Normal priority; a head with no UI thread leaves this null and the
        /// observer keeps the ledger live without polling, as WPF did with no dispatcher.
        /// </summary>
        public static volatile Func<TimeSpan, Action, IDisposable>? PollTimerFactory;

        internal static IForegroundProbe CreateForegroundProbe() =>
            Make(ForegroundProbeFactory) ?? new BlindForegroundProbe();

        internal static IInputProbe CreateInputProbe() =>
            Make(InputProbeFactory) ?? new BlindInputProbe();

        internal static IMicrophoneProbe CreateMicrophoneProbe() =>
            Make(MicrophoneProbeFactory) ?? new BlindMicrophoneProbe();

        internal static IMediaWatcher? CreateMediaWatcher() => Make(MediaWatcherFactory);

        internal static IAppStateProbe CreateAppStateProbe() =>
            Make(AppStateProbeFactory) ?? new BlindAppStateProbe();

        private static T? Make<T>(Func<T?>? factory) where T : class
        {
            try { return factory?.Invoke(); }
            catch { return null; }
        }

        private sealed class BlindForegroundProbe : IForegroundProbe
        {
            public ForegroundSample? Read() => null;
        }

        private sealed class BlindInputProbe : IInputProbe
        {
            public int IdleSeconds => 0;
            public bool IsTypingBurst => false;
            public void Start() { }
            public void Stop() { }
            public void Dispose() { }
        }

        private sealed class BlindMicrophoneProbe : IMicrophoneProbe
        {
            public bool IsInUse(DateTime at) => false;
        }

        private sealed class BlindAppStateProbe : IAppStateProbe
        {
            public AppStateSample Read(DateTime at) => AppStateSample.Empty;
        }
    }
}
