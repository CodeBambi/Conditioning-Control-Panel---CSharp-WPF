using System;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// THE LOOM as a main-app window (WPF Services/Chaos/LoomHostService): the spiral weaver, open to
    /// everyone from the Spiral card. A stripped-down sibling of the descent's host: the page
    /// <c>dtrh/loom.html</c> speaking only the loom subset of the bridge (loom-save / loom-delete /
    /// loom-reveal / sfx out of the page, loom-result / loom-list in).
    ///
    /// <para>File authority stays in <see cref="DtrhLoomStore"/>: every name and slug the page sends
    /// is whitelisted there (lowercase letters, digits, dash, underscore; fixed <c>loom_</c> prefix and
    /// <c>.gif</c> / <c>.json</c> extensions the page never chooses), so a frame cannot name a path.
    /// The saved gifs reach the page on the asset server's <c>ccp.spirals</c> route, the same request
    /// gate every other host uses (flat folder, gif only), never a WebView2 folder mapping.</para>
    ///
    /// <para>It is a <see cref="GameWindow"/> spec, so panic closes it with the other games
    /// (PanicSurfaces "games" -> CloseAllForPanic) and a second press on the button focuses the live
    /// window (WPF Launch: idempotent, refocuses).</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string LoomId = "loom";

        private bool _loomHooked;

        /// <summary>WPF Launch: the folder exists before the page asks for a thumbnail, and the
        /// spirals route is registered on the shared server.</summary>
        private void OpenLoom()
        {
            try { Directory.CreateDirectory(DtrhLoomStore.SpiralsFolder); }
            catch (Exception ex) { Log.Debug("LoomHost: spirals dir create failed: {E}", ex.Message); }
            try { WebAssetServer.Shared.Hosts.TryAdd(ArcSpiralsHost, () => DtrhLoomStore.SpiralsFolder); }
            catch (Exception ex) { Log.Debug("LoomHost: hosts: {E}", ex.Message); }
            if (!_loomHooked) { DtrhLoomStore.Changed += OnLoomStoreChanged; _loomHooked = true; }
        }

        private void CloseLoom()
        {
            if (!_loomHooked) return;
            try { DtrhLoomStore.Changed -= OnLoomStoreChanged; } catch { }
            _loomHooked = false;
        }

        /// <summary>A spiral woven or deleted somewhere else (the descent's own Loom pane): the rack follows.</summary>
        private void OnLoomStoreChanged()
        {
            try { Dispatcher.UIThread.Post(() => { if (!IsClosedOrClosing && IsReady) PostDtrhLoomList(); }); }
            catch (Exception ex) { Log.Debug("LoomHost: changed: {E}", ex.Message); }
        }

        /// <summary>The Loom's frames (WPF LoomHostService.OnPageMessage + OnReady). True = claimed.</summary>
        private bool HandleLoom(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "ready":
                    // WPF OnReady = PostLoomList: this page reads no init and no manifest.
                    IsReady = true;
                    PostDtrhLoomList();
                    return true;
                case "loom-save":
                case "loom-delete":
                case "loom-reveal":
                    // One implementation for both doors (the descent's Loom pane and this window):
                    // the store validates, answers loom-result, and a success re-posts the list.
                    HandleDtrhHostFrame(o);
                    return true;
                case "sfx":
                    // WPF: ChaosSfx.Play(name, scale ?? 0.45). PlayFrame clamps the scale to 0..1.
                    ChaosSfx.PlayFrame((string?)o["name"], (float?)o["scale"] ?? 0.45f);
                    return true;
                default:
                    return false;
            }
        }
    }
}
