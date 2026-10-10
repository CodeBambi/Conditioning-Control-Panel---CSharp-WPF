// PORTED from ConditioningControlPanel/Services/PieceByPiece/PieceByPieceHostService.Media.cs at WPF 7.1.5.
// Online pictures for Distraction mode: the Goon Game's pool (Core GoonOnlineMedia.ForGame("pbp")), stills
// and clips materialised under {assets}/.temp, which the loopback server serves under ccp.assets. The
// consent gate and the niche choice are Core PbpMediaRules; nothing is fetched unless that gate opens.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        private const string PbpOnlineTenant = "pbp";

        private GoonOnlineMedia? _pbpOnline;
        /// <summary>The chess game's own opt-in: a flavour the player picked in the game, this window or
        /// saved from an earlier one. Never app-wide consent.</summary>
        private bool _pbpSessionOptIn;
        private int _pbpSharePct = PbpMediaRules.PickedSharePct;

        /// <summary>At page ready: the saved pick stands as the opt-in (owner, 2026-09-30).</summary>
        private void AdoptSavedPbpMediaChoice()
        {
            var s = CoreSettings.Current;
            _pbpSessionOptIn = s != null && PbpMediaRules.SavedOptIn(s.PbpMediaOnline, s.PbpMediaFlavour);
        }

        /// <summary>host -&gt; page <c>pbp:media-state</c>: what the picker shows.</summary>
        private void PostPbpMediaState()
        {
            try
            {
                var s = CoreSettings.Current;
                var stored = GoonOnlineMediaRules.CleanFlavour(s?.PbpMediaFlavour);
                bool appWide = PbpMediaRules.AppWideOnline(s?.MediaSource, s?.HasRemoteMediaConsent == true);
                bool online = s?.PbpMediaOnline ?? true;
                Post(new JObject
                {
                    ["type"] = "pbp:media-state",
                    ["flavour"] = _pbpSessionOptIn || appWide ? stored : "",
                    ["last"] = stored,
                    ["custom"] = GoonOnlineMediaRules.ParseCustom(s?.PbpMediaCustom),
                    ["online"] = online,
                    ["appWide"] = appWide,
                    ["chosen"] = PbpMediaRules.HasSavedChoice(s?.PbpMediaChosen == true, online, stored),
                    ["library"] = true,
                    ["canOnline"] = true,
                });
            }
            catch (Exception ex) { Log.Debug("PieceByPiece: media-state post failed: {E}", ex.Message); }
        }

        /// <summary>page -&gt; host <c>pbp:media-flavour { flavour, custom, subs, online, chosen }</c>: store
        /// the pick as the saved choice, make it the opt-in, and restart the fetch.</summary>
        private void OnPbpMediaFlavour(JObject o)
        {
            var s = CoreSettings.Current;
            if (s == null) return;
            var flavour = GoonOnlineMediaRules.CleanFlavour(o["flavour"]?.Type == JTokenType.String ? (string?)o["flavour"] : null);
            var subs = GoonOnlineMediaRules.CleanSubs(
                (o["subs"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string?)t : null));
            s.PbpMediaFlavour = flavour;
            if (o["custom"] is JObject) s.PbpMediaCustom = GoonOnlineMediaRules.CleanCustom(o["custom"]);
            s.PbpMediaSubs = GoonOnlineMediaRules.JoinSubs(subs);
            if (o["online"]?.Type == JTokenType.Boolean) s.PbpMediaOnline = (bool)o["online"]!;
            s.PbpMediaChosen = true;
            _pbpSessionOptIn = GoonOnlineMediaRules.IsSessionOptIn(s.PbpMediaOnline, flavour);
            try { CoreSettings.Save(); } catch (Exception ex) { Log.Debug("PieceByPiece: media save: {E}", ex.Message); }
            Log.Information("PieceByPiece: media-flavour {F} ({N} niches, online {O})",
                flavour == "" ? "(none)" : flavour, subs.Count, s.PbpMediaOnline);
            PostPbpMediaState();
            StartPbpOnlineMedia();
        }

        /// <summary>page -&gt; host <c>pbp:media-more</c>: the next wave for the same niches.</summary>
        private void OnPbpMediaMore()
        {
            if (_pbpOnline?.More() == true) Log.Debug("PieceByPiece: media-more, next wave");
        }

        /// <summary>Fetch (or stop) from what is stored and consented. Nothing allowed = post 'off' and
        /// hold nothing: the page then deals from the local library alone.</summary>
        private void StartPbpOnlineMedia()
        {
            try
            {
                if (!_pbpOpen) return;
                var s = CoreSettings.Current;
                bool consent = s?.HasRemoteMediaConsent == true;
                _pbpSharePct = PbpMediaRules.SharePct(s?.MediaSource, s?.RemoteMediaRatio ?? 30, _pbpSessionOptIn);
                var subs = PbpMediaRules.ChannelsFor(s?.PbpMediaOnline ?? true, _pbpSessionOptIn, s?.MediaSource, consent,
                    s?.PbpMediaFlavour, GoonOnlineMediaRules.SplitSubs(s?.PbpMediaSubs),
                    FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs));
                if (subs.Count == 0)
                {
                    if (_pbpOnline != null) _pbpOnline.Off();
                    else PostPbpOnlineMedia(new GoonOnlineMedia.Snapshot("off", Array.Empty<string>(),
                        Array.Empty<GoonOnlineMedia.Item>(), Array.Empty<GoonOnlineMedia.Item>(), 0, 0));
                    return;
                }
                _pbpOnline ??= PbpOnlinePool(PbpOnlineTenant, PostPbpOnlineMedia);
                _pbpOnline.Start(subs);
            }
            catch (Exception ex) { Log.Warning("PieceByPiece: online media start failed: {E}", ex.Message); }
        }

        /// <summary>Test seam: the pool a window fetches through.</summary>
        internal static Func<string, Action<GoonOnlineMedia.Snapshot>, GoonOnlineMedia> PbpOnlinePool { get; set; } = GoonOnlineMedia.ForGame;

        /// <summary>Worker thread -&gt; UI thread -&gt; page <c>pbp:online-media</c>. The whole current list
        /// every time: stills as <c>images</c>, gif clips (webm/mp4) as <c>clips</c>.</summary>
        private void PostPbpOnlineMedia(GoonOnlineMedia.Snapshot snap)
        {
            var frame = new JObject
            {
                ["type"] = "pbp:online-media",
                ["state"] = snap.State,
                ["subs"] = new JArray(snap.Subs.Cast<object>().ToArray()),
                ["share"] = _pbpSharePct,
                ["images"] = new JArray(snap.Images.Select(i => PbpPageUrl(i.Url)).Where(u => u != null).Cast<object>().ToArray()),
                ["clips"] = new JArray(snap.Videos.Select(i => PbpPageUrl(i.Url)).Where(u => u != null).Cast<object>().ToArray()),
                ["have"] = snap.Have,
                ["want"] = snap.Want,
            };
            PbpOnUi(() => Post(frame));
        }

        /// <summary>Close: stop fetching and hand back every temp file this window owned.</summary>
        private void DisposePbpOnlineMedia()
        {
            try { _pbpOnline?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _pbpOnline = null;
            _pbpSessionOptIn = false;
        }

        private static void SeedPbpMediaSeams()
        {
            GoonOnlineMedia.Materialize ??= PbpTempCache.MaterializeAsync;
            GoonOnlineMedia.ReleaseTempFile ??= PbpTempCache.Release;
        }
    }

    /// <summary>
    /// This head's stand-in for WPF RemoteMediaCache.MaterializeAsync(url, ct, ownerReleases: true) /
    /// ReleaseTempFile, for the pool's stills and clips: the bytes of one https picture land as
    /// <c>{assets}/.temp/ccp_remote_*</c> (WPF App.GetMediaTempPath), which WebAssetServer serves under
    /// ccp.assets. Media extensions only, capped; null on any failure (the pool skips that entry). The
    /// owner (the pool) releases every file it took on a niche change or close.
    /// </summary>
    internal static class PbpTempCache
    {
        internal const string Prefix = "ccp_remote_";
        internal const long MaxBytes = 40L * 1024 * 1024;
        private static readonly string[] Exts = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".mp4", ".webm", ".m4v" };
        private static HttpClient? _http;
        private static HttpClient Http => _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        internal static bool IsFetchable(string? url, out Uri? uri, out string ext)
        {
            ext = "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
            ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            return Array.IndexOf(Exts, ext) >= 0;
        }

        internal static async Task<string?> MaterializeAsync(string url, CancellationToken ct)
        {
            if (!IsFetchable(url, out var uri, out var ext) || !SandboxNet.Allows(uri)) return null;
            var root = CorePaths.EffectiveAssets;
            if (string.IsNullOrEmpty(root)) return null;
            var dir = Path.Combine(root, Platform.WebAssetServer.TempFolder);
            string path = Path.Combine(dir, $"{Prefix}{Guid.NewGuid():N}{ext}");
            try
            {
                Directory.CreateDirectory(dir);
                using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength is > MaxBytes) return null;
                await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var dst = File.Create(path))
                {
                    var buf = new byte[81920];
                    long total = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                    {
                        total += n;
                        if (total > MaxBytes) throw new IOException("picture over the cap");
                        await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    }
                }
                return path;
            }
            catch (Exception ex)
            {
                Log.Debug("PieceByPiece: online picture download failed ({Type})", ex.GetType().Name);
                Release(path);
                return null;
            }
        }

        internal static void Release(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (Path.GetFileName(path).StartsWith(Prefix, StringComparison.Ordinal) && File.Exists(path)) File.Delete(path);
            }
            catch { /* still open in the page: left for the next sweep */ }
        }
    }
}
