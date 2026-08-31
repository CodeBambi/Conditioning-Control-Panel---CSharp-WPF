using System;

namespace ConditioningControlPanel.Services.Video.Browser
{
    /// <summary>
    /// Decides what audio-output device label rides a player-page <c>load</c> message (#938
    /// plumbing). The page matches the label against <c>enumerateDevices()</c> and applies
    /// <c>setSinkId</c>; every failure path page-side reports back and leaves audio on the
    /// Windows default, so a wrong or stale label can never silence a clip.
    ///
    /// This is deliberately dormant in the field today: <see cref="BrowserVideoGate"/> still
    /// routes device-pinned users to LibVLC (#938), so a real user with a chosen device never
    /// reaches the player page. The label still flows for the surfaces the gate does not govern
    /// once that demotion is retired — a separate change, made only after this path has proven
    /// itself. Until then the test-override environment variable is the way to exercise it
    /// ears-on without touching the gate.
    /// </summary>
    internal static class BrowserSinkLabel
    {
        /// <summary>Manual-verification override: set to a device's friendly name (or a prefix of
        /// it) and the player page routes there even with the picker on "System default" — which
        /// is the one configuration where the browser engine actually runs today (see the gate
        /// note above). Absent in production use.</summary>
        public const string TestOverrideVariable = "CCP_BROWSER_SINK_TEST_LABEL";

        /// <summary>Label from live settings + environment; null when no routing is wanted.</summary>
        public static string? Resolve()
        {
            string? env = null;
            try { env = Environment.GetEnvironmentVariable(TestOverrideVariable); } catch { }
            var s = App.Settings?.Current;
            return Resolve(s?.AudioOutputDeviceId, s?.AudioOutputDeviceName, env);
        }

        /// <summary>
        /// Pure core, one rule per line so the tests can pin them: a non-blank test override wins
        /// outright; an empty device id is the "System default" picker choice and asks for no
        /// routing; a chosen id with no persisted friendly name yields null too, because the page
        /// can only match by label. Trimmed, never empty-string (null is the "don't" signal).
        /// </summary>
        public static string? Resolve(string? deviceId, string? deviceName, string? testOverride)
        {
            if (!string.IsNullOrWhiteSpace(testOverride)) return testOverride.Trim();
            if (string.IsNullOrEmpty(deviceId)) return null;
            if (string.IsNullOrWhiteSpace(deviceName)) return null;
            return deviceName.Trim();
        }

        /// <summary>Only the audio-bearing surface gets a label: secondaries are permanently muted
        /// (one WASAPI session per clip), and handing them one would spend a mic-probe stream per
        /// monitor for audio nobody hears.</summary>
        public static string? ForWindow(bool primary, string? resolved) => primary ? resolved : null;
    }
}
