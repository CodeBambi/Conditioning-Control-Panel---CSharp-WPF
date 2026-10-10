using System;
using System.Linq;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Goon Game's online pictures (WPF GoonHostService.cs :1873-2110): the player's own flavour
    /// pick (<c>media-flavour</c> / <c>media-more</c> -> <c>online-media</c>), the opponent's niches
    /// (<c>peer-niches</c> -> <c>peer-media</c>) and the Sort duel's noise boards (<c>noise-want</c> ->
    /// <c>noise-media</c>). Consent is WPF's, rule for rule: the pick itself is the opt-in for THIS
    /// window only; the opponent's niches are fetched unless this player switched Goon online pictures
    /// off; a noise board needs the session pick or the app-wide remote consent with a non-local source.
    /// Only niche NAMES cross between players, re-validated here; never a url, an id or a byte.
    /// </summary>
    internal sealed partial class GameWindow
    {
        /// <summary>WPF serves {assets}/.temp as https://ccp.assets/; this head serves it off the
        /// loopback asset server.</summary>
        internal static string GoonPageUrl(string url)
        {
            const string assets = "https://ccp.assets/";
            return url.StartsWith(assets, StringComparison.OrdinalIgnoreCase)
                ? Platform.WebAssetServer.Shared.AssetUrl(Uri.UnescapeDataString(url[assets.Length..]))
                : url;
        }

        private static object GoonItems(System.Collections.Generic.IReadOnlyList<GoonOnlineMedia.Item> items)
            => items.Select(i => new { name = i.Name, url = GoonPageUrl(i.Url) }).ToList();

        private void OnGoonMediaFlavour(JObject o)
        {
            try
            {
                GoonHostService.OnMediaFlavour(o);
                StartGoonOnlineFromSettings();
            }
            catch (Exception ex) { Log.Warning("[Goon] media-flavour: {E}", ex.Message); }
        }

        private void StartGoonOnlineFromSettings()
        {
            try
            {
                if (IsClosedOrClosing) return;
                var s = CoreSettings.Current;
                bool online = s.GoonMediaOnline;
                var flavour = GoonOnlineMediaRules.CleanFlavour(s.GoonMediaFlavour);
                var subs = GoonOnlineMediaRules.SplitSubs(s.GoonMediaSubs);
                // No pick this session = no fetch and no word to the page: its flavour card is up.
                if (!GoonHostService.SessionOptIn) return;
                _goonOnline ??= new GoonOnlineMedia(PostGoonOnline);
                if (!online) { _goonOnline.Off(); return; }
                if (!GoonOnlineMediaRules.ShouldFetch(online, flavour, subs))
                {
                    // A pick with no niches left (every pill switched off) is an honest "empty".
                    if (flavour != "") _goonOnline.Start(Array.Empty<string>());
                    return;
                }
                _goonOnline.Start(subs);
            }
            catch (Exception ex) { Log.Warning("[Goon] start online media: {E}", ex.Message); }
        }

        private void OnGoonMediaMore()
        {
            try
            {
                var s = CoreSettings.Current;
                var flavour = GoonOnlineMediaRules.CleanFlavour(s.GoonMediaFlavour);
                var subs = GoonOnlineMediaRules.SplitSubs(s.GoonMediaSubs);
                if (!GoonHostService.SessionOptIn || _goonOnline == null
                    || !GoonOnlineMediaRules.ShouldFetch(s.GoonMediaOnline, flavour, subs)) return;
                if (_goonOnline.More()) Log.Information("[Goon] media-more, next wave");
            }
            catch (Exception ex) { Log.Warning("[Goon] media-more: {E}", ex.Message); }
        }

        private void OnGoonPeerNiches(JObject o)
        {
            try
            {
                if (IsClosedOrClosing) return;
                var subs = GoonOnlineMediaRules.CleanSubs(
                    (o["subs"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string?)t : null));
                _goonPeer ??= GoonOnlineMedia.ForPeer(PostGoonPeer);
                if (subs.Count == 0) { _goonPeer.Off(); return; }
                if (!GoonHostService.PeerFetchAllowed(CoreSettings.Current.GoonMediaOnline))
                {
                    _goonPeer.Off();
                    Post(new { type = "peer-media", state = "declined", subs = Array.Empty<string>(),
                        images = Array.Empty<object>(), videos = Array.Empty<object>() });
                    return;
                }
                Log.Information("[Goon] peer niches ({N})", subs.Count);
                _goonPeer.Start(subs);
            }
            catch (Exception ex) { Log.Warning("[Goon] peer-niches: {E}", ex.Message); }
        }

        private void OnGoonNoiseWant(JObject o)
        {
            try
            {
                if (IsClosedOrClosing) return;
                var set = o["set"]?.Type == JTokenType.String ? (string?)o["set"] : null;
                if (string.IsNullOrEmpty(set)) { ReleaseGoonNoise(); return; }
                if (GoonNoiseSets.SubFor(set) == null) return;
                var s = CoreSettings.Current;
                if (!GoonNoiseSets.FetchAllowed(s.GoonMediaOnline, GoonHostService.SessionOptIn, s.MediaSource, s.HasRemoteMediaConsent))
                {
                    Post(new { type = "noise-media", set, state = "declined", images = Array.Empty<object>() });
                    return;
                }
                if (!_goonNoise.TryGetValue(set, out var pool))
                {
                    var id = set;
                    var made = GoonOnlineMedia.ForNoise(id, snap => Post(new
                    {
                        type = "noise-media",
                        set = id,
                        state = snap.State,
                        images = GoonItems(snap.Images),
                        progress = new { have = snap.Have, want = snap.Want },
                    }));
                    if (made == null) return;
                    _goonNoise[id] = pool = made;
                }
                Log.Information("[Goon] noise board {Set}", set);
                pool.Start(new[] { GoonNoiseSets.SubFor(set)! });
            }
            catch (Exception ex) { Log.Warning("[Goon] noise-want: {E}", ex.Message); }
        }

        private void ReleaseGoonNoise()
        {
            foreach (var p in _goonNoise.Values) { try { p.Dispose(); } catch { } }
            _goonNoise.Clear();
        }

        private void PostGoonOnline(GoonOnlineMedia.Snapshot snap) => Post(GoonMediaFrame("online-media", snap));
        private void PostGoonPeer(GoonOnlineMedia.Snapshot snap) => Post(GoonMediaFrame("peer-media", snap));

        private static object GoonMediaFrame(string type, GoonOnlineMedia.Snapshot snap) => new
        {
            type,
            state = snap.State,
            subs = snap.Subs,
            images = GoonItems(snap.Images),
            videos = GoonItems(snap.Videos),
            progress = new { have = snap.Have, want = snap.Want },
        };
    }
}
