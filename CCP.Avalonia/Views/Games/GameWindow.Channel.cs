using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// Host -&gt; page on the channel the pages actually listen on. Every game bridge
    /// (backroom/bridge.js, dtrh/bridge.js, arcademy/bridge.js) reads
    /// <c>window.chrome.webview.addEventListener('message')</c>, which only WebView2's own
    /// <c>CoreWebView2.PostWebMessageAsJson</c> raises (WPF ChaosWebViewHost.Post). On Windows the
    /// native CoreWebView2 comes off <see cref="NativeWebView"/>'s platform handle and the frame goes
    /// through its vtable (ICoreWebView2 slot 32, PostWebMessageAsJson); elsewhere (WebKitGTK) the
    /// script carrier stays: the string push (web-shim's __ccpRnPush) or a message event on
    /// chrome.webview where that object takes dispatchEvent.
    /// </summary>
    internal sealed partial class GameWindow
    {
        /// <summary>ICoreWebView2 vtable: IUnknown (3) + 29 members before PostWebMessageAsJson.</summary>
        internal const int PostWebMessageAsJsonSlot = 32;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PostJsonFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string json);

        private PostJsonFn? _postJson;
        private IntPtr _core;

        /// <summary>Frames posted, by carrier; a headless test's view of the channel.</summary>
        internal int NativePosts { get; private set; }
        internal int ScriptPosts { get; private set; }

        /// <summary>Last frame posted (JSON), for headless tests.</summary>
        internal string? LastPosted { get; private set; }

        /// <summary>Every frame posted (JSON), raised before the carrier runs; headless tests listen here.</summary>
        internal event Action<string>? Posted;

        /// <summary>Host -&gt; page (WPF ChaosWebViewHost.Post). Always on the UI thread.</summary>
        internal void Post(object message)
        {
            var json = message as string ?? JsonConvert.SerializeObject(message);
            LastPosted = json;
            try { Posted?.Invoke(json); } catch { }
            if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => PostJson(json));
                return;
            }
            PostJson(json);
        }

        private void PostJson(string json)
        {
            if (IsClosedOrClosing) return;
            if (TryPostNative(json)) { NativePosts++; return; }
            ScriptPosts++;
            var script = "(function(m){try{if(typeof window.__ccpRnPush==='function'){window.__ccpRnPush(JSON.stringify(m));return;}"
                + "var w=window.chrome&&window.chrome.webview;if(w&&w.dispatchEvent){w.dispatchEvent(new MessageEvent('message',{data:m}));}}catch(e){}})("
                + json + ");";
            _ = Web.InvokeScriptAsync(script);
        }

        private bool TryPostNative(string json)
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                if (_postJson == null || _core == IntPtr.Zero)
                {
                    var native = Web.GetVisualDescendants().OfType<NativeWebView>().FirstOrDefault();
                    if (native?.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle h) return false;
                    IntPtr core = h.CoreWebView2;
                    if (core == IntPtr.Zero) return false;
                    var vtbl = Marshal.ReadIntPtr(core);
                    var fn = Marshal.ReadIntPtr(vtbl, PostWebMessageAsJsonSlot * IntPtr.Size);
                    _postJson = Marshal.GetDelegateForFunctionPointer<PostJsonFn>(fn);
                    _core = core;
                }
                int hr = _postJson(_core, json);
                if (hr < 0) { Log.Debug("[Game] {Id}: PostWebMessageAsJson hr=0x{Hr:X8}", Spec.Id, hr); _postJson = null; _core = IntPtr.Zero; return false; }
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("[Game] {Id}: native post failed, using script: {E}", Spec.Id, ex.Message);
                _postJson = null; _core = IntPtr.Zero;
                return false;
            }
        }

        /// <summary>Page -&gt; host bodies arrive as the posted string, or (an object posted on
        /// WebView2) as its JSON; a JSON string literal carrying JSON is unwrapped once.</summary>
        internal static string Unwrap(string body)
        {
            var t = body.Trim();
            if (t.Length > 1 && t[0] == '"')
            {
                try { return JsonConvert.DeserializeObject<string>(t) ?? body; } catch { }
            }
            return body;
        }
    }
}
