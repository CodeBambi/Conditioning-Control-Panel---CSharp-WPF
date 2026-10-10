using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Launcher;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF's own single-instance talk (App.xaml.cs:1430-1610), spoken by this head on Windows so a
    /// WPF 7.1.5 instance and a port instance are the same app to each other during the cutover:
    /// two named events (show, ack) and the <c>fileopen.pending</c> handoff file. The pipe in
    /// <see cref="SingleInstance"/> stays the port-to-port channel (and the only one on Linux).
    /// <list type="bullet">
    ///   <item>Port launched while WPF runs: <see cref="AskWpfToShow"/> writes the handoff, sets WPF's
    ///         show signal and waits for its ack, exactly as a second WPF would.</item>
    ///   <item>WPF launched while the port runs: <see cref="Listen"/> owns the two events, answers the
    ///         signal by routing the handoff and acks, so the WPF launch exits instead of waiting 8 s
    ///         or treating this process as a wedged zombie.</item>
    /// </list>
    /// Names carry the sandbox suffix, so a CCP_USERDATA_DIR run never signals the live app.
    /// </summary>
    internal static class WpfInstanceBridge
    {
        /// <summary>The payload that means "read the handoff file" (never a LauncherHandoff value).</summary>
        public const string HandoffFileMarker = "fileopen";

        /// <summary>True until the UI thread pumps for the first time (WPF _startupPhase): a healthy
        /// primary that is still starting acks from the listener thread, a wedged one never does.</summary>
        public static volatile bool StartupPhase = true;

        /// <summary>true = a WPF primary acked, false = it holds the events but never acked (wedged),
        /// null = no WPF listener at all.</summary>
        public static bool? AskWpfToShow(string suffix, string userData, string? payload, int ackTimeoutMs)
        {
            if (!OperatingSystem.IsWindows()) return null;
            EventWaitHandle? show = null, ack = null;
            try
            {
                if (!EventWaitHandle.TryOpenExisting(AppIdentity.ShowSignalName + suffix, out show)) return null;
                // The surface rides WPF's handoff file; a play/edit launch already wrote it (the marker).
                if (payload != null && payload != HandoffFileMarker)
                    FileOpenHandoff.Write(userData, LauncherHandoff.Action, payload);
                if (!EventWaitHandle.TryOpenExisting(AppIdentity.ShowAckSignalName + suffix, out ack))
                {
                    // A build from before the ack handshake: poke it; the caller's mutex wait decides.
                    show!.Set();
                    return null;
                }
                ack!.Reset();
                show!.Set();
                return ack.WaitOne(ackTimeoutMs);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Single-instance: asking a WPF instance to show failed");
                return null;
            }
            finally
            {
                show?.Dispose();
                ack?.Dispose();
            }
        }

        /// <summary>The primary's half. Returns null off Windows or when the events cannot be made
        /// (the pipe still serves port launches). Dispose stops the listener.</summary>
        public static IDisposable? Listen(string suffix, Func<string?, Task> show)
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AppIdentity.ShowSignalName + suffix);
                // ManualReset: the second instance Reset()s it before signalling (WPF App.xaml.cs:1541).
                var ackSignal = new EventWaitHandle(false, EventResetMode.ManualReset, AppIdentity.ShowAckSignalName + suffix);
                return new Listener(showSignal, ackSignal, show);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Single-instance: WPF show/ack signals unavailable");
                return null;
            }
        }

        private sealed class Listener : IDisposable
        {
            private readonly EventWaitHandle _show, _ack;
            private readonly Func<string?, Task> _route;
            private volatile bool _stopped;

            public Listener(EventWaitHandle show, EventWaitHandle ack, Func<string?, Task> route)
            {
                _show = show;
                _ack = ack;
                _route = route;
                new Thread(Run) { IsBackground = true, Name = "ShowWindowSignalListener" }.Start();
            }

            private void Run()
            {
                while (!_stopped)
                {
                    try
                    {
                        if (!_show.WaitOne(1000) || _stopped) continue;
                        if (StartupPhase) try { _ack.Set(); } catch { }
                        // The marker makes the router read the handoff file (none = a bare relaunch).
                        try { _route(HandoffFileMarker).GetAwaiter().GetResult(); }
                        catch (Exception ex) { Serilog.Log.Warning(ex, "Single-instance show (WPF signal) failed"); }
                        // After the attempt, as WPF acks from its dispatcher callback: a UI thread that
                        // never ran the route never acks, and the second launch treats us as wedged.
                        try { _ack.Set(); } catch { }
                    }
                    catch (ObjectDisposedException) { return; }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Single-instance WPF signal listener");
                        Thread.Sleep(1000);
                    }
                }
            }

            public void Dispose()
            {
                _stopped = true;
                try { _show.Dispose(); } catch { }
                try { _ack.Dispose(); } catch { }
            }
        }
    }
}
