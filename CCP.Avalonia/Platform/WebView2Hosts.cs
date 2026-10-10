using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// WPF's WebView2 virtual hosts (<c>https://ccp.game</c>, <c>https://ccp.assets</c>, ...) on this head's
/// web view, so a page has the SAME origin as under WPF: its localStorage / IndexedDB survives a
/// restart and a WPF profile's page storage carries over.
///
/// Avalonia.Controls.WebView 12.1 wraps WebView2 but exposes neither SetVirtualHostNameToFolderMapping
/// nor a way to answer a request (its WebResourceRequested only edits request headers). It does hand
/// out the raw ICoreWebView2 pointer (<c>NativeWebView.AdapterCreated</c> ->
/// <c>IWindowsWebView2PlatformHandle.CoreWebView2</c>), so this class calls the COM vtable itself
/// (slots read from WebView2.h 1.0.2535; published COM interfaces never change).
///
/// It does NOT use SetVirtualHostNameToFolderMapping, on purpose. Measured on a live WebView2
/// (WebOriginTests): a request to a MAPPED host never raises WebResourceRequested, so a mapping
/// serves every file in the folder and no rule can stand in front of it (WPF maps the whole assets
/// folder that way). Here one WebResourceRequested filter over <c>https://ccp.*</c> answers every
/// request from <see cref="WebAssetServer.ResolveVirtual"/>, the same resolver the loopback listener
/// uses: served roots only, no traversal, media extensions only on the media hosts, sub_audio refused
/// where the mod policy says so, no link out of a root. Nothing is mapped, so nothing can bypass it;
/// a refused or unknown <c>ccp.*</c> url gets a 404 here and never reaches the network.
/// Range requests are answered (206), so media seeks as it does under WPF's mapping.
/// Windows only. Any failure returns null and the caller stays on the loopback server.
/// </summary>
internal sealed class WebView2Hosts : IDisposable
{
    // ---- vtable slots (WebView2.h; IUnknown is 0..2) --------------------------------------------
    internal const int Core_Navigate = 5, Core_AddWebMessageReceived = 34,
        Core_AddWebResourceRequested = 55, Core_AddWebResourceRequestedFilter = 57, Core2_GetEnvironment = 67,
        Env_CreateController = 3, Env_CreateWebResourceResponse = 4,
        Args_GetRequest = 3, Args_PutResponse = 5, Request_GetUri = 3, Request_GetMethod = 5, Request_GetHeaders = 9,
        Headers_GetHeader = 3, Headers_Contains = 5,
        Controller_Close = 24, Controller_GetCoreWebView2 = 25, MessageArgs_TryGetString = 5;

    internal static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    internal static readonly Guid IID_Core2 = new("9E8F0CF8-E670-4B5E-B2BC-73E061E3184C");
    internal static readonly Guid IID_ResourceRequestedHandler = new("AB00B74C-15F1-4646-80E8-E76341D25D71");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetPtr(IntPtr self, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int PutPtr(IntPtr self, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int NoArgs(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int AddHandler(IntPtr self, IntPtr handler, out long token);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] internal delegate int StrArg(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] internal delegate int StrGetPtr(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string name, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] internal delegate int StrGetInt(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string name, out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] internal delegate int AddFilter(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string uri, int resourceContext);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] internal delegate int CreateResponse(IntPtr self, IntPtr content, int status, [MarshalAs(UnmanagedType.LPWStr)] string reason, [MarshalAs(UnmanagedType.LPWStr)] string headers, out IntPtr response);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateController(IntPtr self, IntPtr parent, IntPtr handler);

    /// <summary>The method in vtable slot <paramref name="slot"/> of a COM object.</summary>
    internal static T Fn<T>(IntPtr obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));

    internal static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException("WebView2 " + what + " failed", hr);
    }

    /// <summary>A string the callee allocated with CoTaskMemAlloc (every WebView2 [out] LPWSTR).</summary>
    internal static string? TakeString(IntPtr p)
    {
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>
    /// A COM object WebView2 can call back: IUnknown + one <c>Invoke(a, b)</c>, which is the shape of
    /// every WebView2 event and completion handler. Built by hand (four function pointers in native
    /// memory) so the head needs neither unsafe code nor the runtime's built-in COM interop.
    /// </summary>
    internal sealed class Callback : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr ppv);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint RefFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int InvokeFn(IntPtr self, IntPtr a, IntPtr b);

        readonly Guid _iid;
        readonly Func<IntPtr, IntPtr, int> _invoke;
        readonly Delegate[] _keep;   // the thunks must outlive the native vtable
        readonly IntPtr _vtbl;
        public IntPtr Pointer { get; }
        /// <summary>Rooted until disposed: the browser calls these thunks long after the caller's locals are gone.</summary>
        static readonly HashSet<Callback> Alive = new();

        public Callback(Guid iid, Func<IntPtr, IntPtr, int> invoke)
        {
            _iid = iid;
            _invoke = invoke;
            QueryInterfaceFn qi = QueryInterface;
            RefFn addRef = _ => 2, release = _ => 1;   // lifetime is the owner's (Dispose), never the caller's
            InvokeFn call = Invoke;
            _keep = new Delegate[] { qi, addRef, release, call };
            _vtbl = Marshal.AllocHGlobal(4 * IntPtr.Size);
            for (int i = 0; i < 4; i++) Marshal.WriteIntPtr(_vtbl, i * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_keep[i]));
            Pointer = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(Pointer, _vtbl);
            lock (Alive) Alive.Add(this);
        }

        int QueryInterface(IntPtr self, ref Guid iid, out IntPtr ppv)
        {
            if (iid == IID_IUnknown || iid == _iid) { ppv = self; return 0; }
            ppv = IntPtr.Zero;
            return unchecked((int)0x80004002);   // E_NOINTERFACE
        }

        int Invoke(IntPtr self, IntPtr a, IntPtr b)
        {
            try { return _invoke(a, b); }
            catch (Exception ex) { Log.Debug(ex, "WebView2Hosts: callback failed"); return 0; }
        }

        /// <summary>Call only once the web view that holds the pointer is gone (it may still call it).</summary>
        public void Dispose()
        {
            lock (Alive) if (!Alive.Remove(this)) return;
            Marshal.FreeHGlobal(Pointer);
            Marshal.FreeHGlobal(_vtbl);
            GC.KeepAlive(_keep);
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attributes, [MarshalAs(UnmanagedType.Bool)] bool create, IntPtr template, out IntPtr stream);

    [DllImport("shlwapi.dll")]
    static extern IntPtr SHCreateMemStream(byte[] init, uint length);

    /// <summary>One filter for every host: the handler answers 404 for a name it does not serve, so a
    /// look-alike (<c>https://ccp.evil.example</c>) fails closed instead of loading.</summary>
    internal const string Filter = "https://ccp.*";

    /// <summary>The most one ranged answer carries; the player asks for the next piece.</summary>
    internal const int RangeChunk = 4 * 1024 * 1024;

    readonly WebAssetServer _server;
    readonly IntPtr _core, _env;
    readonly Callback _handler;
    bool _disposed;

    WebView2Hosts(WebAssetServer server, IntPtr core, IntPtr env)
    {
        _server = server; _core = core; _env = env;
        _handler = new Callback(IID_ResourceRequestedHandler, OnResourceRequested);
    }

    /// <summary>Requests the gate refused / served (tests, logs).</summary>
    internal int Refused, Served;

    /// <summary>Why the gate last failed to answer a request the normal way (tests, logs).</summary>
    internal string? LastError;

    /// <summary>
    /// Puts <paramref name="server"/>'s hosts on a live ICoreWebView2. Null (and a logged reason) when
    /// the runtime is too old or any call fails: the caller then keeps the loopback origin. Call on
    /// the web view's own (UI) thread, as every WebView2 call must be.
    /// </summary>
    public static WebView2Hosts? TryInstall(IntPtr coreWebView2, WebAssetServer server)
    {
        if (coreWebView2 == IntPtr.Zero || !OperatingSystem.IsWindows()) return null;
        IntPtr core2 = IntPtr.Zero, env = IntPtr.Zero;
        try
        {
            var iid2 = IID_Core2;
            Check(Marshal.QueryInterface(coreWebView2, in iid2, out core2), "ICoreWebView2_2");
            Check(Fn<GetPtr>(core2, Core2_GetEnvironment)(core2, out env), "get_Environment");
            Marshal.AddRef(coreWebView2);
            var hosts = new WebView2Hosts(server, coreWebView2, env);
            env = IntPtr.Zero;   // owned by the instance now
            try
            {
                Check(Fn<AddHandler>(hosts._core, Core_AddWebResourceRequested)(hosts._core, hosts._handler.Pointer, out _), "add_WebResourceRequested");
                Check(Fn<AddFilter>(hosts._core, Core_AddWebResourceRequestedFilter)(hosts._core, Filter, 0 /* every resource kind */), "AddWebResourceRequestedFilter");
                return hosts;
            }
            catch { hosts.ReleasePointers(); throw; }   // the handler may already be registered: its memory stays
        }
        catch (Exception ex)
        {
            Log.Warning("WebView2Hosts: virtual hosts unavailable ({Error}); pages stay on the loopback origin", ex.Message);
            return null;
        }
        finally
        {
            if (core2 != IntPtr.Zero) Marshal.Release(core2);
            if (env != IntPtr.Zero) Marshal.Release(env);
        }
    }

    int OnResourceRequested(IntPtr sender, IntPtr args)
    {
        if (_disposed || args == IntPtr.Zero) return 0;
        IntPtr request = IntPtr.Zero;
        try
        {
            Check(Fn<GetPtr>(args, Args_GetRequest)(args, out request), "get_Request");
            Check(Fn<GetPtr>(request, Request_GetUri)(request, out var uriPtr), "get_Uri");
            var raw = TakeString(uriPtr);
            Check(Fn<GetPtr>(request, Request_GetMethod)(request, out var methodPtr), "get_Method");
            var method = TakeString(methodPtr) ?? "";
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) { Refused++; Respond(args, 404, IntPtr.Zero, ""); return 0; }
            if (method != "GET" && method != "HEAD") { Refused++; Respond(args, 405, IntPtr.Zero, ""); return 0; }

            var file = _server.ResolveVirtual(uri, out _, out var cors);
            if (file == null) { Refused++; Respond(args, 404, IntPtr.Zero, ""); return 0; }
            Serve(args, file, cors, RangeOf(request));
            Served++;
        }
        catch (Exception ex)
        {
            // Never let a ccp.* request fall through to the network.
            LastError = ex.Message;
            Log.Debug("WebView2Hosts: request refused: {Error}", ex.Message);
            try { Respond(args, 404, IntPtr.Zero, ""); } catch (Exception inner) { Log.Debug("WebView2Hosts: no response: {Error}", inner.Message); }
        }
        finally { if (request != IntPtr.Zero) Marshal.Release(request); }
        return 0;
    }

    /// <summary>The request's Range header, or null.</summary>
    static string? RangeOf(IntPtr request)
    {
        IntPtr headers = IntPtr.Zero;
        try
        {
            if (Fn<GetPtr>(request, Request_GetHeaders)(request, out headers) < 0 || headers == IntPtr.Zero) return null;
            if (Fn<StrGetInt>(headers, Headers_Contains)(headers, "Range", out var has) < 0 || has == 0) return null;
            return Fn<StrGetPtr>(headers, Headers_GetHeader)(headers, "Range", out var value) < 0 ? null : TakeString(value);
        }
        catch (Exception) { return null; }
        finally { if (headers != IntPtr.Zero) Marshal.Release(headers); }
    }

    /// <summary>
    /// The bytes a <c>Range: bytes=a-b</c> header asks for out of <paramref name="length"/>, capped at
    /// <paramref name="chunk"/>; null when there is no (usable, single) range and the whole file goes.
    /// </summary>
    internal static (long From, long To)? ParseRange(string? header, long length, int chunk = RangeChunk)
    {
        if (string.IsNullOrWhiteSpace(header) || length <= 0) return null;
        var h = header.Trim();
        if (!h.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || h.Contains(',')) return null;
        var parts = h[6..].Split('-');
        if (parts.Length != 2) return null;
        long from, to;
        if (parts[0].Trim().Length == 0)
        {
            // bytes=-n : the last n bytes
            if (!long.TryParse(parts[1].Trim(), out var tail) || tail <= 0) return null;
            from = Math.Max(0, length - tail); to = length - 1;
        }
        else
        {
            if (!long.TryParse(parts[0].Trim(), out from) || from < 0 || from >= length) return null;
            if (parts[1].Trim().Length == 0) to = length - 1;
            else if (!long.TryParse(parts[1].Trim(), out to) || to < from) return null;
            to = Math.Min(to, length - 1);
        }
        if (to - from + 1 > chunk) to = from + chunk - 1;
        return (from, to);
    }

    void Serve(IntPtr args, string file, bool cors, string? rangeHeader)
    {
        var headers = "Content-Type: " + WebAssetServer.ContentType(file) + "\r\nAccept-Ranges: bytes";
        // WPF's access kinds: the media hosts are Allow (any page origin may read them), the page root is Deny.
        if (cors) headers += "\r\nAccess-Control-Allow-Origin: *";
        IntPtr stream = IntPtr.Zero;
        try
        {
            long length = new FileInfo(file).Length;
            if (ParseRange(rangeHeader, length) is { } range)
            {
                var buffer = new byte[range.To - range.From + 1];
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Seek(range.From, SeekOrigin.Begin);
                    fs.ReadExactly(buffer);
                }
                stream = SHCreateMemStream(buffer, (uint)buffer.Length);
                if (stream == IntPtr.Zero) throw new IOException("no memory stream");
                Respond(args, 206, stream, headers + $"\r\nContent-Range: bytes {range.From}-{range.To}/{length}");
                return;
            }
            // STGM_READ | STGM_SHARE_DENY_NONE: a clip the pool is still writing can be read.
            Check(SHCreateStreamOnFileEx(file, 0x40, 0, false, IntPtr.Zero, out stream), "open " + Path.GetFileName(file));
            Respond(args, 200, stream, headers);
        }
        finally { if (stream != IntPtr.Zero) Marshal.Release(stream); }
    }

    void Respond(IntPtr args, int status, IntPtr content, string headers)
    {
        IntPtr response = IntPtr.Zero;
        try
        {
            const string always = "Cache-Control: no-cache\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer";
            var reason = status switch { 200 => "OK", 206 => "Partial Content", 405 => "Method Not Allowed", _ => "Not Found" };
            Check(Fn<CreateResponse>(_env, Env_CreateWebResourceResponse)(_env, content, status, reason,
                headers.Length == 0 ? always : always + "\r\n" + headers, out response), "CreateWebResourceResponse");
            Check(Fn<PutPtr>(args, Args_PutResponse)(args, response), "put_Response");
        }
        finally { if (response != IntPtr.Zero) Marshal.Release(response); }
    }

    void ReleasePointers()
    {
        if (_disposed) return;
        _disposed = true;
        Marshal.Release(_env);
        Marshal.Release(_core);
    }

    /// <summary>The web view is going away: drop the references. The handler's few bytes of native
    /// memory are left in place on purpose (the dying browser may still call it once).</summary>
    public void Dispose() => ReleasePointers();
}
