using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The WebHost mute and pause seam (ledger P6 / H6 / T7 / T10 / X4). WPF mutes the embedded browser
    /// with CoreWebView2.IsMuted; Avalonia's NativeWebView has no engine mute on either adapter, so this
    /// rides the one channel WebHost does have, <see cref="WebHost.InvokeScriptAsync"/>.
    ///
    /// <para>The script holds a flag on the page, mutes every media element it can reach (the document
    /// and same-origin frames) and keeps new ones muted: a capturing <c>play</c> / <c>volumechange</c>
    /// listener and a MutationObserver re-apply the flag. It must be run again after every navigation
    /// (a new document has no flag); the Home browser card does that from NavigationCompleted.</para>
    ///
    /// <para>Limit, stated rather than hidden: a player inside a CROSS-ORIGIN iframe cannot be reached
    /// from here. BambiCloud and HypnoTube play first-party video elements, which is what the Home
    /// card's button is for.</para>
    /// </summary>
    internal static class WebHostMedia
    {
        /// <summary>The script for a mute state. Idempotent: running it twice installs one observer.</summary>
        internal static string MuteScript(bool muted) =>
            "(function(m){try{" +
            "window.__ccpMuted=m;" +
            "var each=function(d){try{d.querySelectorAll('video,audio').forEach(function(e){e.muted=window.__ccpMuted;});" +
            "d.querySelectorAll('iframe').forEach(function(f){try{if(f.contentDocument)each(f.contentDocument);}catch(x){}});}catch(x){}};" +
            "each(document);" +
            "if(!window.__ccpMuteHooked){window.__ccpMuteHooked=true;" +
            "var keep=function(ev){var t=ev.target;if(t&&window.__ccpMuted&&t.muted===false){t.muted=true;}};" +
            "document.addEventListener('play',keep,true);" +
            "document.addEventListener('volumechange',keep,true);" +
            "try{new MutationObserver(function(){if(window.__ccpMuted)each(document);})" +
            ".observe(document.documentElement,{childList:true,subtree:true});}catch(x){}" +
            "}return m?'muted':'unmuted';}catch(e){return 'error';}})(" + (muted ? "true" : "false") + ");";

        /// <summary>WPF MenuItemPauseBrowser's pause half: every reachable media element stops.</summary>
        internal const string PauseScript =
            "(function(){try{var n=0;var each=function(d){try{d.querySelectorAll('video,audio').forEach(function(e){if(!e.paused){e.pause();n++;}});" +
            "d.querySelectorAll('iframe').forEach(function(f){try{if(f.contentDocument)each(f.contentDocument);}catch(x){}});}catch(x){}};" +
            "each(document);return String(n);}catch(e){return 'error';}})();";

        /// <summary>Applies the mute state to the live page. False when there is no engine or the
        /// script did not run (the caller then says so rather than showing a muted glyph).</summary>
        internal static async Task<bool> SetMutedAsync(this WebHost host, bool muted)
        {
            if (!host.HasEngine) return false;
            var result = await host.InvokeScriptAsync(MuteScript(muted));
            return result != null && !result.Contains("error");
        }

        /// <summary>Pauses every reachable media element on the live page.</summary>
        internal static async Task<bool> PauseMediaAsync(this WebHost host)
        {
            if (!host.HasEngine) return false;
            var result = await host.InvokeScriptAsync(PauseScript);
            return result != null && !result.Contains("error");
        }
    }
}
