using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The host's answer to <c>CoreWebView2.PermissionRequested</c>: <b>DENY</b>, so the
/// browser's own permission prompt never reaches the user and the port's stated posture becomes a
/// mechanism instead of an argument (closes D250).
///
/// <para>WPF PARITY IS DELIBERATELY INVERTED, AND ONLY IN THE DIRECTION OF LESS.
/// <c>GoonHostService.cs:477-503</c> answers <c>Microphone</c> with <c>Allow</c> (<c>:489</c>) and
/// leaves every other kind at the browser default; <c>:431</c> is the ONLY
/// <c>PermissionRequested</c> subscription in the shipping app. This port answers <b>Deny</b>, on
/// every kind but one, on three hosts. Denying is strictly narrower than granting and narrower than
/// prompting: it opens no capture device, grants nothing, and removes the one path by which the
/// embedded browser could still ask on this host's behalf.</para>
///
/// <para>THE ONE CARVE-OUT IS AUTOPLAY (kind 9), LEFT AT THE BROWSER DEFAULT — untouched, never
/// answered. Every WebView2 host in this client already passes
/// <c>--autoplay-policy=no-user-gesture-required</c> (<c>DtrhHostWindow.axaml.cs:637</c>,
/// <c>GoonHostWindow.axaml.cs:312</c>, <c>DtrhLoomWindow.axaml.cs:162</c>,
/// <c>IntakeHostWindow.axaml.cs:246</c>) and both payloads start their own media programmatically
/// (<c>dtrh/engine/audioBus.js</c>, <c>goon/exec/videos.js</c>, <c>goon/ui/hud.js</c>). Denying
/// autoplay would silence the pages and contradict a policy this port has already set: a functional
/// regression, not a tightening. Every OTHER kind — including one this build has never seen and a
/// kind a future runtime adds — falls into the deny arm by construction.</para>
///
/// <para>THE PROMPT IS SUPPRESSED BY THE STATE WRITE, NOT BY <c>Handled</c>.
/// <c>COREWEBVIEW2_PERMISSION_STATE_DEFAULT</c> is documented as <i>"the default browser behavior is
/// used, which normally prompts users for decision"</i>, so writing <c>DENY</c> is both the answer
/// and the suppression. <c>ICoreWebView2PermissionRequestedEventArgs2::Handled</c> is PROPAGATION
/// control — it stops the remaining CoreWebView2 handlers from being invoked — NOT UI suppression;
/// the shipping app's comment at <c>GoonHostService.cs:490-492</c> says otherwise and is wrong. It
/// is not needed here and is not written.</para>
///
/// <para>AND THE ANSWER IS NOT BANKED. <c>GoonHostService.cs:440-446</c> documents that WebView2
/// remembers permission decisions PER PROFILE and then serves them <i>without raising the event at
/// all</i>. An unbanked answer is one this code owns on every request; a banked one is a file in
/// <c>browser_data_*</c> that outlives it. So <c>SavesInProfile</c> is written FALSE through
/// <c>ICoreWebView2PermissionRequestedEventArgs3</c> — upstream's own
/// <c>try { e.SavesInProfile = false; } catch { }</c> at <c>:499</c>, for the same stated reason. A
/// runtime too old to offer that interface answers <c>E_NOINTERFACE</c>: the deny still lands, but
/// it is then banked and the event may stop being raised — no weaker than the deny, and named
/// rather than implied.</para>
///
/// <para>THE COM FACTS ARE DERIVED, NEVER COUNTED BY EYE (the model is
/// <see cref="DtrhProcessFailed"/>, which reaches <c>add_ProcessFailed</c> the same way). The
/// <c>ICoreWebView2Vtbl</c> struct in the published MIDL header (Microsoft.Web.WebView2
/// <c>build/native/include/WebView2.h</c>, enumerated in BOTH 1.0.2535.41 and 1.0.3179.45 — 61
/// slots, identical order) puts <c>add_PermissionRequested</c> at slot 23 and <c>remove</c> at 24,
/// with <c>add_ProcessFailed</c> at 25 / <c>remove</c> at 26 — the pair this tree already ships, and
/// therefore the check that the enumeration is right. Avalonia 12.0.1 corroborates from metadata
/// that is not a positional count at all: its generated
/// <c>Avalonia.Controls.Win.WebView2.Interop.ICoreWebView2</c> vtable NAMES its fields with the slot
/// index (<c>add_PermissionRequested_23</c>, <c>remove_PermissionRequested_24</c>,
/// <c>add_ProcessFailed_25</c>) and carries them at those field indices. Handler IID
/// 15e1c6a3-c72a-4df3-91d7-d097fbec6bfd (Invoke at slot 3); args <c>get_PermissionKind</c> slot 4 /
/// <c>put_State</c> slot 7; args3 e61670bc-3dce-4177-86d2-c629ae3cb6ac <c>put_SavesInProfile</c>
/// slot 12; <c>COREWEBVIEW2_PERMISSION_STATE_DENY</c> = 2.</para>
///
/// <para>Capability honesty: where the platform cannot deliver this hook — the Linux
/// WebKitGTK dialog path, whose <c>permission-request</c> signal Avalonia exposes nowhere — the
/// outcome is typed <c>Unavailable</c> and the caller SAYS SO. A failed attach never becomes
/// silence: the port must not be quieter about this than it was when it had no handler at all.</para>
/// </summary>
public static class WebViewPermissionDeny
{
    /// <summary>What the handler did with one request. Reported to the host for its diagnostic
    /// line; a write that FAILED is a distinct outcome and is never reported as a deny.</summary>
    public enum Decision
    {
        /// <summary>The request was answered Deny, so the browser will not prompt.</summary>
        Denied,

        /// <summary>Autoplay — left untouched at the browser default, on purpose.</summary>
        LeftAtBrowserDefault,

        /// <summary>The state write failed; the browser keeps its default behaviour for this
        /// request, which may include prompting. Reported, never swallowed.</summary>
        WriteFailed,
    }

    /// <summary>Typed attach outcome — never throws, never fakes.</summary>
    public abstract record AttachOutcome
    {
        /// <summary>Subscribed; dispose the signal to detach (detach tolerates a dead browser —
        /// remove_PermissionRequested on a zombie returns a failure HRESULT, which is expected,
        /// never an error).</summary>
        public sealed record Attached(PermissionDenySignal Signal) : AttachOutcome;

        /// <summary>The hook cannot be installed here. Code is stable for logs/tests
        /// (unsupported-platform / invalid-handle / attach-failed).</summary>
        public sealed record Unavailable(string Code, string Detail) : AttachOutcome;
    }

    /// <summary>COREWEBVIEW2_PERMISSION_KIND_AUTOPLAY (WebView2.h, both SDKs) — the one kind this
    /// host does not answer.</summary>
    public const int AutoplayKind = 9;

    /// <summary>COREWEBVIEW2_PERMISSION_STATE_DENY (WebView2.h, both SDKs).</summary>
    public const int DenyState = 2;

    /// <summary>Reported when the runtime would not say which kind was asked for. Such a request is
    /// DENIED — an unreadable question is not a reason to let the browser ask.</summary>
    public const int UnreadableKind = -1;

    /// <summary>Subscribe the denying handler to CoreWebView2.PermissionRequested on a raw pointer.
    /// MUST be called on the thread that owns the WebView2 (UI thread — COM apartment binding); the
    /// callback then arrives on that thread.</summary>
    /// <param name="coreWebView2">The raw ICoreWebView2 from IWindowsWebView2PlatformHandle.</param>
    /// <param name="onDecision">Optional per-request report (kind, what was done) for the host's
    /// diagnostic log. Never given a URI: this handler does not read the requesting origin.</param>
    public static AttachOutcome TryAttach(IntPtr coreWebView2, Action<int, Decision>? onDecision = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new AttachOutcome.Unavailable("unsupported-platform",
                "PermissionRequested requires the Windows WebView2 surface (IWindowsWebView2PlatformHandle); "
                + "the Linux WebKitGTK dialog path exposes no permission hook through Avalonia — the browser "
                + "still owns that decision there (named limit, never faked)");
        }

        if (coreWebView2 == IntPtr.Zero)
        {
            return new AttachOutcome.Unavailable("invalid-handle",
                "CoreWebView2 pointer is zero — the adapter did not deliver a platform handle");
        }

        var handler = new PermissionRequestedHandler(onDecision);
        var handlerPtr = IntPtr.Zero;
        var addRefd = false;
        try
        {
            // Hold our own ref so the COM object outlives any adapter teardown order while we are
            // subscribed (DtrhProcessFailed.cs:63-66 discipline).
            Marshal.AddRef(coreWebView2);
            addRefd = true;
            handlerPtr = Marshal.GetComInterfaceForObject(handler, typeof(ICoreWebView2PermissionRequestedHandlerNative));
            var add = VtableSlot<AddPermissionRequestedDelegate>(coreWebView2, AddPermissionRequestedSlot);
            var hr = add(coreWebView2, handlerPtr, out var token);
            if (hr < 0)
            {
                Cleanup();
                return new AttachOutcome.Unavailable("attach-failed",
                    $"add_PermissionRequested returned HRESULT 0x{hr:X8}");
            }

            return new AttachOutcome.Attached(
                new PermissionDenySignal(coreWebView2, handler, handlerPtr, token));
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or ArgumentException or NotSupportedException)
        {
            Cleanup();
            return new AttachOutcome.Unavailable("attach-failed",
                $"PermissionRequested subscription threw {ex.GetType().Name}: {ex.Message}");
        }

        void Cleanup()
        {
            // Failure paths release what they took; the Attached path transfers ownership to the
            // signal object.
            try { if (handlerPtr != IntPtr.Zero) Marshal.Release(handlerPtr); } catch { /* best-effort */ }
            try { if (addRefd) Marshal.Release(coreWebView2); } catch { /* best-effort */ }
        }
    }

    /// <summary>The published kind names (WebView2.h COREWEBVIEW2_PERMISSION_KIND_*, identical in
    /// 1.0.2535.41 and 1.0.3179.45), for diagnostics. A kind this build does not know is reported by
    /// number — and denied.</summary>
    public static string KindName(int kind) => kind switch
    {
        UnreadableKind => "unreadable",
        0 => "unknown-permission",
        1 => "microphone",
        2 => "camera",
        3 => "geolocation",
        4 => "notifications",
        5 => "other-sensors",
        6 => "clipboard-read",
        7 => "multiple-automatic-downloads",
        8 => "file-read-write",
        AutoplayKind => "autoplay",
        10 => "local-fonts",
        11 => "midi-system-exclusive-messages",
        12 => "window-management",
        _ => $"kind-{kind}",
    };

    // vtable slots (WebView2.h ICoreWebView2Vtbl / ...EventArgsVtbl; 0-based including IUnknown's 3)
    private const int AddPermissionRequestedSlot = 23;
    private const int RemovePermissionRequestedSlot = 24;
    private const int QueryInterfaceSlot = 0;
    private const int GetPermissionKindSlot = 4;
    private const int PutStateSlot = 7;
    private const int PutSavesInProfileSlot = 12;

    /// <summary>ICoreWebView2PermissionRequestedEventArgs3 (WebView2.h:39785) — the interface that
    /// carries SavesInProfile. Absent on an older runtime; that is expected, not an error.</summary>
    private static readonly Guid PermissionRequestedEventArgs3Iid =
        new("e61670bc-3dce-4177-86d2-c629ae3cb6ac");

    private static T VtableSlot<T>(IntPtr com, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(com);
        var fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AddPermissionRequestedDelegate(IntPtr com, IntPtr handler, out long token);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RemovePermissionRequestedDelegate(IntPtr com, long token);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(IntPtr com, ref Guid iid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPermissionKindDelegate(IntPtr args, out int kind);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PutStateDelegate(IntPtr args, int state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PutSavesInProfileDelegate(IntPtr args, int savesInProfile);

    /// <summary>The handler IID from WebView2.h:39951. ComImport + InterfaceIsIUnknown puts Invoke
    /// at vtable slot 3 — the CCW the runtime builds answers WebView2's QI for this IID with exactly
    /// the vtable it expects.</summary>
    [ComImport]
    [Guid("15e1c6a3-c72a-4df3-91d7-d097fbec6bfd")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2PermissionRequestedHandlerNative
    {
        [PreserveSig]
        int Invoke(IntPtr sender, IntPtr args);
    }

    /// <summary>
    /// THE ANSWER ITSELF, deliberately separated from the subscription. This half is pure vtable
    /// traffic on a pointer somebody else owns — no CCW, no Windows-only COM facility — so it is
    /// exercisable on EVERY platform, while <see cref="TryAttach"/>'s
    /// <c>GetComInterfaceForObject</c> is Windows-only by construction. The split is what lets the
    /// deny decision be a cross-platform fact instead of a Windows-only one.
    /// </summary>
    private static void AnswerRequest(IntPtr args, Action<int, Decision>? onDecision)
    {
        if (args == IntPtr.Zero)
        {
            return;
        }

        // get_PermissionKind (args slot 4). A read failure denies: an unreadable question is not a
        // reason to hand the decision back to the browser.
        var kind = UnreadableKind;
        var getKind = VtableSlot<GetPermissionKindDelegate>(args, GetPermissionKindSlot);
        if (getKind(args, out var k) >= 0)
        {
            kind = k;
        }

        if (kind == AutoplayKind)
        {
            onDecision?.Invoke(kind, Decision.LeftAtBrowserDefault);
            return;
        }

        // put_State = DENY (args slot 7). Leaving State at DEFAULT is what makes the browser prompt,
        // so this write IS the suppression.
        var putState = VtableSlot<PutStateDelegate>(args, PutStateSlot);
        var hr = putState(args, DenyState);
        if (hr < 0)
        {
            onDecision?.Invoke(kind, Decision.WriteFailed);
            return;
        }

        TrySuppressProfileBanking(args);
        onDecision?.Invoke(kind, Decision.Denied);
    }

    /// <summary>SavesInProfile = FALSE through args3, when the runtime offers it. E_NOINTERFACE is
    /// the expected answer on an older runtime and is NOT an error: the deny still stands, it is
    /// simply banked in the profile from then on.</summary>
    private static void TrySuppressProfileBanking(IntPtr args)
    {
        var args3 = IntPtr.Zero;
        try
        {
            var queryInterface = VtableSlot<QueryInterfaceDelegate>(args, QueryInterfaceSlot);
            var iid = PermissionRequestedEventArgs3Iid;
            var hr = queryInterface(args, ref iid, out args3);
            if (hr < 0 || args3 == IntPtr.Zero)
            {
                return;   // E_NOINTERFACE on an older runtime — expected
            }

            var putSaves = VtableSlot<PutSavesInProfileDelegate>(args3, PutSavesInProfileSlot);
            _ = putSaves(args3, 0);   // BOOL FALSE
        }
        catch
        {
            // best-effort by contract — never let this cost the deny
        }
        finally
        {
            try { if (args3 != IntPtr.Zero) Marshal.Release(args3); } catch { /* best-effort */ }
        }
    }

    [ComVisible(true)]
    private sealed class PermissionRequestedHandler : ICoreWebView2PermissionRequestedHandlerNative
    {
        private readonly Action<int, Decision>? _onDecision;

        public PermissionRequestedHandler(Action<int, Decision>? onDecision) => _onDecision = onDecision;

        public int Invoke(IntPtr sender, IntPtr args)
        {
            try
            {
                AnswerRequest(args, _onDecision);
            }
            catch
            {
                // A COM event callback must NEVER let a managed exception escape into native code.
                // The browser's own default (which may prompt) is the outcome then, and the host's
                // diagnostic line is the only honest record of it.
            }

            return 0; // S_OK
        }
    }

    /// <summary>One live subscription. Detach is idempotent and NEVER throws (the browser may
    /// already be dead — DtrhProcessFailed's DisposeAll idempotence parity).</summary>
    public sealed class PermissionDenySignal : IDisposable
    {
        private readonly IntPtr _com;
        private readonly object _handler;   // strong ref — roots the CCW for the session
        private readonly IntPtr _handlerPtr;
        private long _token;
        private bool _detached;

        internal PermissionDenySignal(IntPtr com, object handler, IntPtr handlerPtr, long token)
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
                    // A failure HRESULT is EXPECTED on a zombie browser — tolerated, never thrown.
                    var remove = VtableSlot<RemovePermissionRequestedDelegate>(_com, RemovePermissionRequestedSlot);
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
            GC.KeepAlive(_handler);
        }
    }
}
