using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// SP-027 slice b5: the native ProcessFailed signal — the W17-documented immediate
/// detection route (webview-dtrh-spike.md: AdapterDestroyed NEVER fires on renderer kill;
/// native ProcessFailed via TryGetPlatformHandle is the documented signal). WPF parity:
/// DtrhHostService.cs:123 wires CoreWebView2.ProcessFailed → Recover.
///
/// The pointer chain is VERIFIED, not guessed: NativeWebView.TryGetPlatformHandle()
/// returns the adapter, which implements the PUBLIC
/// Avalonia.Platform.IWindowsWebView2PlatformHandle exposing the raw CoreWebView2
/// (reflection dump of Avalonia.Controls.WebView 12.0.1). The COM facts come from the
/// official WebView2.idl (Microsoft.Web.WebView2 1.0.2535.41): ICoreWebView2
/// 76eceacb-0462-4d94-ac83-423a6793775e with add_ProcessFailed at vtable slot 25 /
/// remove at 26 (cross-checked against the package's own interop metadata order);
/// ICoreWebView2ProcessFailedEventHandler 79e0aea4-990b-42d9-aa1d-0fcc2e5bc7f1 (Invoke
/// at slot 3); ICoreWebView2ProcessFailedEventArgs get_ProcessFailedKind at slot 3.
///
/// Capability honesty (SP-006): where the platform cannot deliver this signal (the
/// Linux WebKitGTK dialog path) the outcome is typed Unavailable and the heartbeat
/// watchdog is the only net — a named limit, never faked.
/// </summary>
public static class DtrhProcessFailed
{
    /// <summary>Typed attach outcome — never throws, never fakes.</summary>
    public abstract record AttachOutcome
    {
        /// <summary>Subscribed; dispose the signal to detach (detach tolerates a dead
        /// browser — remove_ProcessFailed on a zombie returns a failure HRESULT, which is
        /// expected, never an error).</summary>
        public sealed record Attached(DtrhProcessFailedSignal Signal) : AttachOutcome;

        /// <summary>The signal cannot be delivered here. Code is stable for logs/tests
        /// (unsupported-platform / invalid-handle / attach-failed).</summary>
        public sealed record Unavailable(string Code, string Detail) : AttachOutcome;
    }

    /// <summary>Subscribe to CoreWebView2.ProcessFailed on a raw pointer. MUST be called
    /// on the thread that owns the WebView2 (UI thread — COM apartment binding, the same
    /// class as SP-023 surprise #4). The callback then arrives on that thread.</summary>
    public static AttachOutcome TryAttach(IntPtr coreWebView2, Action<int> onProcessFailed)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new AttachOutcome.Unavailable("unsupported-platform",
                "native ProcessFailed requires the Windows WebView2 surface (IWindowsWebView2PlatformHandle); "
                + "the Linux WebKitGTK dialog path delivers no process-failure event (named limit — heartbeat watchdog only)");
        }

        if (coreWebView2 == IntPtr.Zero)
        {
            return new AttachOutcome.Unavailable("invalid-handle",
                "CoreWebView2 pointer is zero — the adapter did not deliver a platform handle");
        }

        var handler = new ProcessFailedHandler(onProcessFailed);
        var handlerPtr = IntPtr.Zero;
        var addRefd = false;
        try
        {
            // Hold our own ref so the COM object outlives any adapter teardown order while
            // we are subscribed (detach then talks to a live object or a dead-ref we own).
            Marshal.AddRef(coreWebView2);
            addRefd = true;
            handlerPtr = Marshal.GetComInterfaceForObject(handler, typeof(ICoreWebView2ProcessFailedHandlerNative));
            var add = VtableSlot<AddProcessFailedDelegate>(coreWebView2, AddProcessFailedSlot);
            var hr = add(coreWebView2, handlerPtr, out var token);
            if (hr < 0)
            {
                Cleanup();
                return new AttachOutcome.Unavailable("attach-failed",
                    $"add_ProcessFailed returned HRESULT 0x{hr:X8}");
            }

            return new AttachOutcome.Attached(
                new DtrhProcessFailedSignal(coreWebView2, handler, handlerPtr, token));
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or ArgumentException)
        {
            Cleanup();
            return new AttachOutcome.Unavailable("attach-failed",
                $"ProcessFailed subscription threw {ex.GetType().Name}: {ex.Message}");
        }

        void Cleanup()
        {
            // Failure paths release what they took; the Attached path transfers ownership
            // to the signal object.
            try { if (handlerPtr != IntPtr.Zero) Marshal.Release(handlerPtr); } catch { /* best-effort */ }
            try { if (addRefd) Marshal.Release(coreWebView2); } catch { /* best-effort */ }
        }
    }

    // vtable slots (WebView2.idl; 0-based including IUnknown's 3)
    private const int AddProcessFailedSlot = 25;
    private const int RemoveProcessFailedSlot = 26;
    private const int GetProcessFailedKindSlot = 3;

    private static T VtableSlot<T>(IntPtr com, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(com);
        var fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AddProcessFailedDelegate(IntPtr com, IntPtr handler, out long token);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RemoveProcessFailedDelegate(IntPtr com, long token);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetProcessFailedKindDelegate(IntPtr args, out int kind);

    /// <summary>The handler IID from WebView2.idl. ComImport + InterfaceIsIUnknown puts
    /// Invoke at vtable slot 3 — the CCW the runtime builds answers the runtime's QI for
    /// this IID with exactly the vtable WebView2 expects.</summary>
    [ComImport]
    [Guid("79e0aea4-990b-42d9-aa1d-0fcc2e5bc7f1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2ProcessFailedHandlerNative
    {
        [PreserveSig]
        int Invoke(IntPtr sender, IntPtr args);
    }

    [ComVisible(true)]
    private sealed class ProcessFailedHandler : ICoreWebView2ProcessFailedHandlerNative
    {
        private readonly Action<int> _onProcessFailed;

        public ProcessFailedHandler(Action<int> onProcessFailed) => _onProcessFailed = onProcessFailed;

        public int Invoke(IntPtr sender, IntPtr args)
        {
            try
            {
                var kind = -1;
                if (args != IntPtr.Zero)
                {
                    // get_ProcessFailedKind (WebView2.idl — the args interface's only
                    // method, slot 3). A failure here must never kill the callback.
                    var getKind = VtableSlot<GetProcessFailedKindDelegate>(args, GetProcessFailedKindSlot);
                    if (getKind(args, out var k) >= 0)
                    {
                        kind = k;
                    }
                }

                _onProcessFailed(kind);
            }
            catch
            {
                // A COM event callback must not let a managed exception escape into
                // native code — the failure path is the heartbeat watchdog's net.
            }

            return 0; // S_OK
        }
    }

    /// <summary>One live subscription. Detach is idempotent and NEVER throws (the
    /// browser may already be dead — WPF DisposeAll idempotence parity).</summary>
    public sealed class DtrhProcessFailedSignal : IDisposable
    {
        private readonly IntPtr _com;
        private readonly object _handler;   // strong ref — roots the CCW for the session
        private readonly IntPtr _handlerPtr;
        private long _token;
        private bool _detached;

        internal DtrhProcessFailedSignal(IntPtr com, object handler, IntPtr handlerPtr, long token)
        {
            _com = com;
            _handler = handler;
            _handlerPtr = handlerPtr;
            _token = token;
        }

        public void Dispose()
        {
            if (_detached)
            {
                return;
            }

            _detached = true;
            try
            {
                if (_token != 0)
                {
                    // Failure HRESULT is EXPECTED on a zombie browser (consult trap 3) —
                    // tolerated, never thrown, still logged by the caller if it cares.
                    var remove = VtableSlot<RemoveProcessFailedDelegate>(_com, RemoveProcessFailedSlot);
                    _ = remove(_com, _token);
                    _token = 0;
                }
            }
            catch
            {
                // detach is best-effort by contract
            }

            try { if (_handlerPtr != IntPtr.Zero) Marshal.Release(_handlerPtr); } catch { /* best-effort */ }
            try { Marshal.Release(_com); } catch { /* releases our AddRef */ }
        }
    }
}
