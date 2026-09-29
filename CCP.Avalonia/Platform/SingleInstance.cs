using System;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF's single-instance gate (App.xaml.cs:1819-1922) on both OSes. The mutex name is WPF's, because the
    /// installer's AppMutex and InitializeSetup look for it. WPF's named EventWaitHandles do not exist on Unix,
    /// so the show request and its ack travel over a named pipe (a Unix socket on Linux) instead.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        public const string MutexName = "ConditioningControlPanel_SingleInstance_Mutex";
        const int ShowAckTimeoutMs = 10000;                          // WPF App.xaml.cs:68
        static readonly TimeSpan LegacyTakeoverWait = TimeSpan.FromSeconds(8);   // WPF App.xaml.cs:1862
        static readonly TimeSpan StaleTakeoverWait = TimeSpan.FromSeconds(3);    // WPF App.xaml.cs:1914

        readonly Mutex _mutex;
        readonly bool _owned;
        readonly CancellationTokenSource _cts = new();

        SingleInstance(Mutex mutex, bool owned, string pipe, Func<Task> show)
        {
            _mutex = mutex;
            _owned = owned;
            _ = Task.Run(() => ListenAsync(pipe, show, _cts.Token));
        }

        /// <summary>A sandboxed profile (CCP_USERDATA_DIR) is a different instance, so kc and tests never
        /// wake or block the user's real app. A shipped build never sets it and holds WPF's exact name.</summary>
        public static string SandboxSuffix()
        {
            var dir = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
            return string.IsNullOrEmpty(dir) ? "" : "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir)))[..12];
        }

        /// <summary>Null means a live primary acknowledged the show request and this launch must exit.</summary>
        public static SingleInstance? Claim(string suffix, Func<Task> show)
        {
            var pipe = "ConditioningControlPanel_ShowWindow_Signal_" + Environment.UserName + suffix;
            var mutex = new Mutex(true, MutexName + suffix, out bool owned);
            if (!owned)
            {
                bool? acked = RequestShow(pipe);
                if (acked == true) { mutex.Dispose(); return null; }
                // No listener: a primary still exiting mid-update (#466) - wait for it, else exit (WPF 1861-1870).
                // Listener but no ack: wedged primary. ponytail: WPF also kills it (KillStaleInstances);
                // here this launch runs without the mutex after the short wait, as WPF does when the kill fails.
                try { owned = mutex.WaitOne(acked == null ? LegacyTakeoverWait : StaleTakeoverWait); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned && acked == null) { mutex.Dispose(); return null; }
            }
            return new SingleInstance(mutex, owned, pipe, show);
        }

        /// <summary>true = acked, false = connected but no ack in time, null = nobody listening.</summary>
        static bool? RequestShow(string pipe)
        {
            try
            {
                using var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                c.Connect(1000);
                c.WriteByte(1);
                c.Flush();
                using var cts = new CancellationTokenSource(ShowAckTimeoutMs);
                var buf = new byte[1];
                return c.ReadAsync(buf, cts.Token).AsTask().GetAwaiter().GetResult() == 1;
            }
            catch (TimeoutException) { return null; }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Single-instance show request failed"); return null; }
        }

        static async Task ListenAsync(string pipe, Func<Task> show, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var s = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await s.WaitForConnectionAsync(ct);
                    await s.ReadExactlyAsync(new byte[1], ct);
                    await show();                       // the ack proves the UI thread ran it (WPF 1985)
                    await s.WriteAsync(new byte[] { 1 }, ct);
                    await s.FlushAsync(ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Single-instance listener");
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
                }
            }
        }

        /// <summary>Call on the thread that claimed: a mutex is released by its owning thread (WPF OnExit 6135).</summary>
        public void Dispose()
        {
            _cts.Cancel();
            if (_owned) try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
    }
}
