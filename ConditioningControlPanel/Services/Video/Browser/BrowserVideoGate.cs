using System;
using System.IO;

namespace ConditioningControlPanel.Services.Video.Browser
{
    /// <summary>
    /// The one place that answers "should THIS file play in the browser engine?". Everything the
    /// hybrid routing decision depends on lives here so VideoService's branch stays a single call
    /// and the LibVLC path keeps its exact shape for every file the browser can't take.
    /// </summary>
    internal static class BrowserVideoGate
    {
        /// <summary>
        /// Containers Chromium decodes on a stock Windows install. Deliberately narrow: .mkv/.avi/
        /// .wmv/.mov are LibVLC's, and a browser attempt on them would only ever cost a fallback.
        /// </summary>
        private static readonly string[] BrowserExtensions = { ".mp4", ".m4v", ".webm" };

        public static bool IsBrowserExtension(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                return Array.IndexOf(BrowserExtensions, ext) >= 0;
            }
            catch { return false; }
        }

        // There is deliberately NO "might a video use the browser?" helper here. Its only caller was
        // BubbleCountService's poison-cooldown skip, and answering it before a file is chosen is
        // what made that skip wrong: the routing decision is per-FILE. Both cooldown checks now run
        // after selection and call ShouldUseBrowser with the real path.

        /// <summary>
        /// True when the mandatory-video pipeline should hand this clip to
        /// <see cref="BrowserVideoEngine"/> instead of LibVLC. Never throws - any doubt routes to
        /// LibVLC, which is the shipped path.
        /// </summary>
        /// <param name="stableKey">Optional content identity for the unsafe-cache lookup. Null
        /// resolves it from <see cref="BrowserUnsafeVideoCache.ResolveStableKey"/>, which is what
        /// keeps a content-pack clip recognisable across its per-play <c>ccp_temp_&lt;GUID&gt;</c>
        /// decrypt path.</param>
        public static bool ShouldUseBrowser(string? path, string? stableKey = null)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                if (App.Settings?.Current?.BrowserVideoEngineEnabled != true) return false;
                if (!IsBrowserExtension(path)) return false;
                // Touching Instance also kicks off the shared environment build on first use, so a
                // later video finds IsAvailable already settled.
                if (!BrowserVideoEngine.Instance.IsAvailable) return false;
                // A file the page cannot serve (outside every mapped virtual host) would 404 and
                // cost a pointless fallback, so it is decided here rather than at runtime.
                if (BrowserVideoEngine.BuildPageUrl(path) == null) return false;
                if (BrowserUnsafeVideoCache.Contains(path, stableKey)) return false;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrowserVideoGate.ShouldUseBrowser threw ({E}) - routing to LibVLC", ex.Message);
                return false;
            }
        }
    }
}
