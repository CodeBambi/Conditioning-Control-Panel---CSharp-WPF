using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Arcademy host, the media half (WPF 7.1.5 ArcademyHostService.cs :4580-5370 and the init
    /// builders :1362-1700): <c>assets-request</c> (the app-wide remote pull and SORT's tagged piles),
    /// <c>local-sample-request</c>, <c>probe-sub</c>, plus what init says about the player's own media
    /// (<c>triggers[].audio</c>, <c>settings.localAssets</c>, <c>localFolders</c>, <c>loomSpirals</c>).
    ///
    /// <para>One media pipeline, the port's own: remote rows come from Core's FypOnlineCoordinator
    /// (the same tenant ids as WPF), local files are served by <see cref="WebAssetServer"/> on the page's
    /// own origin. WPF's virtual hosts are loopback paths here (<c>ccp.assets/</c>, <c>ccp.subaudio/</c>,
    /// <c>ccp.modaudio/</c>, <c>ccp.spirals/</c>); the page counts those as local
    /// (provider/inventory.js isLocalUrl).</para>
    ///
    /// <para>The consent rule is WPF's, resolved host side on every ask: remote media only when
    /// <c>MediaSource != "local" &amp;&amp; HasRemoteMediaConsent</c> and the app is not in offline mode.
    /// A closed gate answers an empty batch, never silence.</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        private const string ArcRemoteConsumerId = "arcademy";
        private const int ArcRemoteBatchCap = 24;   // per reply; the page asks again if it wants more

        /// <summary>One servable row. Tag and Src are null on the app-wide path and set on the tagged
        /// path, where the tag IS the answer key the class grades on.</summary>
        private sealed record ArcAssetUrl(string Url, string Kind, string Mime, string? Tag = null, string? Src = null, string? Poster = null);

        /// <summary>Prewarmed rows: keyed by media kind (app-wide) or <c>sort:tag|kind</c> (a pile), so a
        /// pile can never be answered out of the app-wide buffer.</summary>
        private readonly Dictionary<string, List<ArcAssetUrl>> _arcRemoteBuffer = new(StringComparer.Ordinal);
        private int _arcRemoteFetchInFlight;   // 0/1 via Interlocked
        private bool _arcNichesIgnoredLogged, _arcTaggedSubsEmptyLogged, _arcProbeScopeLogged;

        /// <summary>Live sub list per tag. Static on purpose: the coordinator caches the FIRST provider it
        /// is handed for a consumer id and keeps it forever, so the provider closes over this table.</summary>
        private static readonly Dictionary<string, List<string>> ArcTaggedChannels = new(StringComparer.Ordinal);
        private readonly HashSet<string> _arcTaggedFetchesInFlight = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<(string ReqId, string Tag, int Want, int Epoch)>> _arcTaggedWaiters = new(StringComparer.Ordinal);
        private readonly HashSet<string> _arcProbesInFlight = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Test seams: the provider calls (pile tag or "" for the app-wide pull; a sub name).</summary>
        internal Func<string, FeedMediaKind, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>>? ArcFetchBatchOverride;
        internal Func<string, Task<SubProbe>>? ArcProbeSubOverride;
        /// <summary>Test seam: the assets root (the live library otherwise).</summary>
        internal static string? ArcademyAssetsRootOverride;

        private static string? ArcAssetsRoot => ArcademyAssetsRootOverride ?? CorePaths.EffectiveAssets;

        private static string? PosterFor(FypAssetManifest.Entry e, string kind)
            => kind == "loop" && !string.IsNullOrEmpty(e.PosterUrl) ? e.PosterUrl : null;

        private static bool ArcRemoteAllowed()
            => ArcademyRemoteMediaEnabled() && CoreSettings.Current?.OfflineMode != true;

        private static IReadOnlyList<string> ArcRemoteChannels()
        {
            var s = CoreSettings.Current;
            return FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
        }

        // ============================ assets-request (app-wide) ============================

        private void OnArcademyAssetsRequest(JObject o)
        {
            var reqId = (string?)o["reqId"] ?? "";
            int count = Math.Clamp(ArcInt(o, "count", 8), 1, ArcRemoteBatchCap);
            var kind = ((string?)o["kind"] ?? "still").Trim();
            if (kind != "loop" && kind != "still") kind = "still";

            // OPT-IN: a request that names its own subs is SORT asking for one of the two piles the
            // player picked, served from that pile alone. No `subs` = the app-wide pull below.
            var subs = ArcademyLocalMedia.ReadRequestSubs(o);
            if (subs != null) { OnArcademyTaggedAssetsRequest(o, reqId, count, kind, subs); return; }

            if (o["niches"] != null && !_arcNichesIgnoredLogged)
            {
                _arcNichesIgnoredLogged = true;
                Log.Information("[Game] arcademy: assets-request carried 'niches' - ignored, the app-wide selection wins");
            }

            if (!ArcRemoteAllowed())
            {
                Post(new { type = "assets", reqId, urls = Array.Empty<object>(), done = true });
                return;
            }

            var served = ArcTakeBuffered(kind, count);
            bool satisfied = served.Count >= count;
            Post(new
            {
                type = "assets",
                reqId,
                urls = served.Select(u => new { url = u.Url, kind = u.Kind, mime = u.Mime, poster = u.Poster }).ToArray(),
                done = satisfied,
            });
            if (!satisfied) ArcServeRemoteBatch(reqId, kind, count - served.Count);
        }

        private List<ArcAssetUrl> ArcTakeBuffered(string key, int count)
        {
            var taken = new List<ArcAssetUrl>();
            lock (_arcRemoteBuffer)
            {
                if (!_arcRemoteBuffer.TryGetValue(key, out var buf)) return taken;
                while (buf.Count > 0 && taken.Count < count)
                {
                    taken.Add(buf[0]);
                    buf.RemoveAt(0);
                }
            }
            return taken;
        }

        private void ArcBufferRest(string key, IEnumerable<ArcAssetUrl> rest)
        {
            lock (_arcRemoteBuffer)
            {
                if (!_arcRemoteBuffer.TryGetValue(key, out var buf)) _arcRemoteBuffer[key] = buf = new List<ArcAssetUrl>();
                buf.AddRange(rest);
                if (buf.Count > 120) buf.RemoveRange(0, buf.Count - 120);
            }
        }

        private Task<(List<FypAssetManifest.Entry> Entries, string? Error)> ArcFetch(string tag, FeedMediaKind mediaKind)
        {
            if (ArcFetchBatchOverride != null) return ArcFetchBatchOverride(tag, mediaKind);
            var coord = tag.Length == 0
                ? FypOnlineCoordinator.For(ArcRemoteConsumerId, ArcRemoteChannels, FeedMediaKind.Any)
                : ArcTaggedCoordinator(tag);
            return coord.FetchBatchAsync(mediaKind, CancellationToken.None);
        }

        /// <summary>The window still alive and still the one that asked (a fetch can outlive its window).</summary>
        private bool ArcLive(int epoch) => !IsClosedOrClosing && _arcMeta != null && Volatile.Read(ref _arcGeneration) == epoch;

        /// <summary>Fetch one batch and post it under the original reqId. Single-flight; never throws, and
        /// always posts a terminating message so the page's in-flight latch clears.</summary>
        private async void ArcServeRemoteBatch(string reqId, string kind, int want)
        {
            int epoch = Volatile.Read(ref _arcGeneration);
            if (Interlocked.CompareExchange(ref _arcRemoteFetchInFlight, 1, 0) != 0)
            {
                // Another fetch owns the provider: end THIS exchange rather than leaving it open.
                Post(new { type = "assets", reqId, urls = Array.Empty<object>(), done = true });
                return;
            }
            try
            {
                var mediaKind = kind == "loop" ? FeedMediaKind.Video : FeedMediaKind.Image;
                var (entries, error) = await ArcFetch("", mediaKind).ConfigureAwait(false);

                var fresh = new List<ArcAssetUrl>();
                foreach (var e in entries)
                {
                    if (!RemoteMediaFormats.Validate(e, mediaKind, out var reason))
                    {
                        Log.Debug("[Game] arcademy: rejected remote entry {Id}: {Reason}", e.Id, reason);
                        continue;
                    }
                    // A card is not a feed tile: the smaller rendition loads in a fraction of the time.
                    var url = e.SmallUrl ?? e.Url;
                    fresh.Add(new ArcAssetUrl(url, kind, ArcademyLocalMedia.MimeFor(url, kind), Poster: PosterFor(e, kind)));
                    if (fresh.Count >= ArcRemoteBatchCap) break;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Consent may have been withdrawn while the fetch was in the air: nothing remote goes out then.
                    if (!ArcLive(epoch)) return;
                    if (!ArcRemoteAllowed()) fresh.Clear();
                    var send = fresh.Take(want).ToList();
                    ArcBufferRest(kind, fresh.Skip(send.Count));
                    Post(new
                    {
                        type = "assets",
                        reqId,
                        urls = send.Select(u => new { url = u.Url, kind = u.Kind, mime = u.Mime, poster = u.Poster }).ToArray(),
                        done = true,   // an empty pool must end the exchange, not restart it
                        error,
                    });
                });
            }
            catch (Exception ex)
            {
                Log.Warning("[Game] arcademy: remote batch failed: {E}", ex.Message);
                if (ArcLive(epoch))
                {
                    try { Post(new { type = "assets", reqId, urls = Array.Empty<object>(), done = true }); } catch { }
                }
            }
            finally { Interlocked.Exchange(ref _arcRemoteFetchInFlight, 0); }
        }

        // ============================ tagged assets (SORT's piles) ============================

        private static FypOnlineCoordinator ArcTaggedCoordinator(string tag)
            => FypOnlineCoordinator.For(ArcRemoteConsumerId + ":sort:" + tag, () => ArcTaggedChannelsFor(tag), FeedMediaKind.Any);

        private static IReadOnlyList<string> ArcTaggedChannelsFor(string tag)
        {
            lock (ArcTaggedChannels)
            {
                return ArcTaggedChannels.TryGetValue(tag, out var subs)
                    ? new List<string>(subs)
                    : (IReadOnlyList<string>)Array.Empty<string>();
            }
        }

        /// <summary>Point a pile at its subs. When the set changes, the pile's prewarmed rows go with it:
        /// those rows carry the OLD src, and src is what the class grades on.</summary>
        private void ArcSetTaggedChannels(string tag, List<string> subs)
        {
            bool changed;
            lock (ArcTaggedChannels)
            {
                changed = !ArcTaggedChannels.TryGetValue(tag, out var current)
                    || current.Count != subs.Count
                    || current.Where((c, i) => !string.Equals(c, subs[i], StringComparison.OrdinalIgnoreCase)).Any();
                if (changed) ArcTaggedChannels[tag] = new List<string>(subs);
            }
            if (!changed) return;
            lock (_arcRemoteBuffer)
            {
                _arcRemoteBuffer.Remove(ArcademyLocalMedia.TaggedBufferKey(tag, "loop"));
                _arcRemoteBuffer.Remove(ArcademyLocalMedia.TaggedBufferKey(tag, "still"));
            }
            if (ArcFetchBatchOverride == null)
            {
                try { ArcTaggedCoordinator(tag).ResetChannels(); }
                catch (Exception ex) { Log.Debug("[Game] arcademy: tagged rotation reset failed: {E}", ex.Message); }
            }
            Log.Information("[Game] arcademy: pile '{Tag}' = {Subs}", tag, string.Join(", ", subs));
        }

        private void OnArcademyTaggedAssetsRequest(JObject o, string reqId, int count, string kind, List<string> subs)
        {
            var tag = ArcademyLocalMedia.ReadTag(o);
            if (!ArcRemoteAllowed())
            {
                PostArcTaggedAssets(reqId, tag, Array.Empty<ArcAssetUrl>(), true);
                return;
            }
            if (subs.Count == 0)
            {
                // An empty pile is answered empty rather than falling back to the app-wide pull: a sort
                // dealt from subs the player never picked is a lie, not a fallback.
                if (!_arcTaggedSubsEmptyLogged)
                {
                    _arcTaggedSubsEmptyLogged = true;
                    Log.Information("[Game] arcademy: tagged assets-request '{Tag}' carried no usable subs", tag);
                }
                PostArcTaggedAssets(reqId, tag, Array.Empty<ArcAssetUrl>(), true);
                return;
            }

            ArcSetTaggedChannels(tag, subs);
            var key = ArcademyLocalMedia.TaggedBufferKey(tag, kind);
            var served = ArcTakeBuffered(key, count);
            bool satisfied = served.Count >= count;
            PostArcTaggedAssets(reqId, tag, served, satisfied);
            if (!satisfied) ArcServeTaggedBatch(reqId, tag, kind, count - served.Count);
        }

        /// <summary>The tagged reply: the same <c>assets</c> envelope, with tag and src on every row.</summary>
        private void PostArcTaggedAssets(string reqId, string tag, IReadOnlyList<ArcAssetUrl> rows, bool done)
        {
            Post(new
            {
                type = "assets",
                reqId,
                tag,
                urls = rows.Select(u => new
                {
                    url = u.Url,
                    kind = u.Kind,
                    mime = u.Mime,
                    tag = u.Tag ?? tag,
                    src = u.Src ?? "",
                    poster = u.Poster,
                }).ToArray(),
                done,
            });
        }

        private void ArcDrainTaggedWaiters(string key)
        {
            List<(string ReqId, string Tag, int Want, int Epoch)>? list;
            lock (_arcTaggedFetchesInFlight)
            {
                if (!_arcTaggedWaiters.Remove(key, out list) || list == null) return;
            }
            int epoch = Volatile.Read(ref _arcGeneration);
            foreach (var w in list)
            {
                if (w.Epoch != epoch) continue;
                try { PostArcTaggedAssets(w.ReqId, w.Tag, ArcTakeBuffered(key, w.Want), true); } catch { }
            }
        }

        private async void ArcServeTaggedBatch(string reqId, string tag, string kind, int want)
        {
            int epoch = Volatile.Read(ref _arcGeneration);
            var key = ArcademyLocalMedia.TaggedBufferKey(tag, kind);

            lock (_arcTaggedFetchesInFlight)
            {
                if (!_arcTaggedFetchesInFlight.Add(key))
                {
                    // The fetch this ask wants is already on its way: queue behind it (an empty reply
                    // here was the road to a QUICK SORT, a different game).
                    if (!_arcTaggedWaiters.TryGetValue(key, out var waiters)) _arcTaggedWaiters[key] = waiters = new();
                    waiters.Add((reqId, tag, want, epoch));
                    return;
                }
            }
            try
            {
                var allowed = new HashSet<string>(ArcTaggedChannelsFor(tag), StringComparer.OrdinalIgnoreCase);
                if (allowed.Count == 0)
                {
                    PostArcTaggedAssets(reqId, tag, Array.Empty<ArcAssetUrl>(), true);
                    ArcDrainTaggedWaiters(key);
                    return;
                }

                var mediaKind = kind == "loop" ? FeedMediaKind.Video : FeedMediaKind.Image;
                var (entries, error) = await ArcFetch(tag, mediaKind).ConfigureAwait(false);

                var fresh = new List<ArcAssetUrl>();
                foreach (var e in entries)
                {
                    if (!RemoteMediaFormats.Validate(e, mediaKind, out var reason))
                    {
                        Log.Debug("[Game] arcademy: rejected tagged entry {Id}: {Reason}", e.Id, reason);
                        continue;
                    }
                    // Folder is "r/<sub>": the src the page shows. A row we cannot place in the pile is dropped.
                    var folder = (e.Folder ?? "").Trim();
                    var bare = folder.StartsWith("r/", StringComparison.OrdinalIgnoreCase) ? folder[2..] : folder;
                    if (bare.Length == 0 || !allowed.Contains(bare)) continue;
                    var url = e.SmallUrl ?? e.Url;
                    fresh.Add(new ArcAssetUrl(url, kind, ArcademyLocalMedia.MimeFor(url, kind), tag, "r/" + bare, PosterFor(e, kind)));
                    if (fresh.Count >= ArcRemoteBatchCap) break;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ArcLive(epoch)) return;
                    if (!ArcRemoteAllowed()) fresh.Clear();
                    var send = fresh.Take(want).ToList();
                    ArcBufferRest(key, fresh.Skip(send.Count));
                    if (error != null) Log.Debug("[Game] arcademy: tagged batch '{Tag}' error {E}", tag, error);
                    PostArcTaggedAssets(reqId, tag, send, true);
                    ArcDrainTaggedWaiters(key);
                });
            }
            catch (Exception ex)
            {
                Log.Warning("[Game] arcademy: tagged batch failed: {E}", ex.Message);
                if (ArcLive(epoch))
                {
                    try { PostArcTaggedAssets(reqId, tag, Array.Empty<ArcAssetUrl>(), true); } catch { }
                }
                ArcDrainTaggedWaiters(key);
            }
            finally { lock (_arcTaggedFetchesInFlight) _arcTaggedFetchesInFlight.Remove(key); }
        }

        // ============================ local sample ============================

        /// <summary>A library file's url on the page's own origin (WPF https://ccp.assets/&lt;rel&gt;).</summary>
        internal static string ArcAssetsUrl(string rel, bool animated = false)
            => WebAssetServer.Shared.AssetUrl(rel) + (animated ? ArcademyLocalMedia.AnimatedImageHint : "");

        private async void OnArcademyLocalSampleRequest(JObject o)
        {
            var reqId = (string?)o["reqId"] ?? "";
            int count = Math.Clamp(ArcInt(o, "count", 8), 1, ArcRemoteBatchCap);
            var kind = ((string?)o["kind"] ?? "still").Trim();
            if (kind != "loop" && kind != "still") kind = "still";
            var tag = ArcademyLocalMedia.ReadTag(o);
            var folders = ArcademyLocalMedia.ReadStringArray(o["folders"]);
            var presetId = ((string?)o["presetId"] ?? "").Trim();
            int epoch = Volatile.Read(ref _arcGeneration);

            List<ArcAssetUrl> rows;
            try
            {
                var s = CoreSettings.Current;
                var root = ArcAssetsRoot;
                var disabled = s?.DisabledAssetPaths?.ToList();
                var presets = s?.AssetPresets?.ToList();
                // A big library is a slow walk and the UI thread is holding a web view: enumerate off it.
                var sampled = await Task.Run(() => ArcademyLocalMedia.SampleLocalAssets(root, reqId, count, kind, folders, presetId, disabled, presets))
                    .ConfigureAwait(false);
                rows = sampled.Select(r =>
                {
                    var url = ArcAssetsUrl(r.Rel, r.Animated);
                    return new ArcAssetUrl(url, r.Kind, ArcademyLocalMedia.MimeFor(url, r.Kind), tag, r.Src);
                }).ToList();
            }
            catch (Exception ex)
            {
                Log.Warning("[Game] arcademy: local sample failed: {E}", ex.Message);
                rows = new List<ArcAssetUrl>();
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ArcLive(epoch)) return;
                PostArcTaggedAssets(reqId, tag, rows, true);
            });
        }

        // ============================ probe-sub ============================

        /// <summary>The SORT door's search box: is r/&lt;name&gt; real, and how much video does it hold.
        /// Every path replies (the door awaits a promise), and a verified name lands in the LIBRARY only,
        /// never in the app-wide feed selection. The probe goes straight from this machine to the provider.</summary>
        private async void OnArcademyProbeSub(JObject o)
        {
            var reqId = (string?)o["reqId"] ?? "";
            var raw = (string?)o["name"] ?? (string?)o["sub"] ?? "";
            var clean = FypOnlineCoordinator.SanitizeSub(raw);
            if (clean == null)
            {
                PostArcSubProbe(reqId, (raw ?? "").Trim(), false, null, "invalid");
                return;
            }

            // Read, logged once, acted on nowhere: the library add never enrols a sub in the feed on this host.
            var scope = ((string?)o["scope"] ?? "").Trim();
            if (scope.Length > 0 && !_arcProbeScopeLogged)
            {
                _arcProbeScopeLogged = true;
                Log.Information("[Game] arcademy: probe-sub carried scope '{Scope}' / pile '{Pile}' - nothing to honour on this host",
                    scope, ((string?)o["pile"] ?? "").Trim());
            }

            var s = CoreSettings.Current;
            // Cached for a week, the same window both pickers trust.
            if (s != null && !s.SubVerdictIsStale(clean)
                && s.FypOnlineSubVerdicts.TryGetValue(clean, out var cached) && cached != null)
            {
                if (cached.Ok && s.TryAddLibrarySub(clean)) { CoreSettings.Save(); PushArcademyLibrary(); }
                PostArcSubProbe(reqId, clean, cached.Ok, cached.VideoCount, null);
                return;
            }

            if (!ArcRemoteAllowed())
            {
                PostArcSubProbe(reqId, clean, false, null, "offline");
                return;
            }

            lock (_arcProbesInFlight)
            {
                if (!_arcProbesInFlight.Add(clean))
                {
                    // Answer rather than drop: a silent duplicate is a promise that never settles.
                    PostArcSubProbe(reqId, clean, false, null, "busy");
                    return;
                }
            }

            int epoch = Volatile.Read(ref _arcGeneration);
            try
            {
                var probe = ArcProbeSubOverride != null
                    ? await ArcProbeSubOverride(clean).ConfigureAwait(false)
                    : await Task.Run(() => FypOnlineCoordinator.ProbeSubAsync(clean, CancellationToken.None)).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ArcLive(epoch)) return;
                    var st = CoreSettings.Current;
                    bool libraryMoved = false;
                    // A transport failure taught us nothing about the sub, so no verdict is written.
                    if (st != null && probe.Error == null)
                    {
                        st.FypOnlineSubVerdicts[clean] = new RemoteSubVerdict
                        {
                            Ok = probe.Ok,
                            VideoCount = probe.VideoCount,
                            CheckedAtUtc = DateTime.UtcNow,
                        };
                        if (probe.Ok)
                        {
                            libraryMoved = st.TryAddLibrarySub(clean);
                            if (libraryMoved)
                                Log.Information("[Game] arcademy: r/{Sub} verified ({N} videos) and kept in the library", clean, probe.VideoCount);
                        }
                        CoreSettings.Save();
                    }
                    PostArcSubProbe(reqId, clean, probe.Ok, probe.VideoCount, probe.Error);
                    if (libraryMoved) PushArcademyLibrary();
                });
            }
            catch (Exception ex)
            {
                Log.Warning("[Game] arcademy: sub probe failed: {E}", ex.Message);
                if (ArcLive(epoch))
                {
                    try { PostArcSubProbe(reqId, clean, false, null, "offline"); } catch { }
                }
            }
            finally { lock (_arcProbesInFlight) _arcProbesInFlight.Remove(clean); }
        }

        private void PostArcSubProbe(string reqId, string name, bool ok, int? videoCount, string? error)
            => Post(new
            {
                type = "sub-probe",
                reqId,
                name,
                ok,
                videoCount,
                // videoCount 0 with ok:true is a real answer: the sub exists and has stills only.
                stillOnly = ok && videoCount.GetValueOrDefault() == 0,
                error,
            });

        /// <summary>Close: the piles belong to the class that picked them; the next launch names its own.</summary>
        private void ClearArcademyMedia()
        {
            lock (_arcRemoteBuffer) _arcRemoteBuffer.Clear();
            lock (ArcTaggedChannels) ArcTaggedChannels.Clear();
            lock (_arcTaggedFetchesInFlight) _arcTaggedWaiters.Clear();
        }

        // ============================ what init says about local media ============================

        internal const string ArcSubAudioHost = "ccp.subaudio";
        internal const string ArcModAudioHost = "ccp.modaudio";
        internal const string ArcSpiralsHost = "ccp.spirals";

        private static string ArcSharedSubAudioDir => Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio");

        private static string? ArcModAudioDir()
        {
            try
            {
                var installed = CoreMods.ActiveModPackage?.InstalledPath;
                if (string.IsNullOrEmpty(installed)) return null;
                var root = Path.Combine(installed, "resources", "sounds", "flashes_audio");
                return Directory.Exists(root) ? root : null;
            }
            catch { return null; }
        }

        /// <summary>WPF's three audio / spiral virtual hosts as routes on the asset server. Read per
        /// request, so a mod switch moves them, and the Bambi <c>sub_audio</c> folder is refused at the
        /// route itself for a mod <see cref="ModAudioPolicy"/> does not allow (CCP Default, Locked).</summary>
        internal static void RegisterArcademyHosts(WebAssetServer server)
        {
            server.Hosts[ArcSubAudioHost] = () => ModAudioPolicy.UsesSharedSubAudio(CoreMods.ActiveModId) ? ArcSharedSubAudioDir : null;
            server.Hosts[ArcModAudioHost] = ArcModAudioDir;
            server.Hosts[ArcSpiralsHost] = () => DtrhLoomStore.SpiralsFolder;
        }

        /// <summary>The day's phrases WITH their whisper clips: <c>[{text, audio}]</c>. Recorded clips
        /// only (no synthetic speech): a phrase with no file is a text row. Gated on SubAudioAudible, the
        /// app-wide whisper mute, and on the mod audio policy.</summary>
        internal static object[] BuildArcademyTriggers(string[] phrases, Func<string, string, string>? hostUrl = null)
        {
            if (phrases == null || phrases.Length == 0) return Array.Empty<object>();
            hostUrl ??= WebAssetServer.Shared.HostUrl;
            var audible = CoreSettings.Current?.SubAudioAudible == true;
            var (modDir, sharedDir) = ArcademyLocalMedia.TriggerAudioDirs(audible, CoreMods.ActiveModId,
                CoreMods.ActiveModPackage?.InstalledPath, ArcSharedSubAudioDir);
            var rows = new List<object>(phrases.Length);
            foreach (var text in phrases)
            {
                string? url = null;
                if (audible)
                {
                    try
                    {
                        var hit = ArcademyLocalMedia.ResolveTriggerAudio(text, modDir, sharedDir);
                        if (hit != null) url = hostUrl(hit.Value.FromMod ? ArcModAudioHost : ArcSubAudioHost, hit.Value.FileName);
                    }
                    catch (Exception ex)
                    {
                        // A phrase whose clip cannot be resolved is a TEXT row, never a missing row.
                        Log.Debug("[Game] arcademy triggers: clip resolve failed for a {Chars}-char phrase: {E}", (text ?? "").Length, ex.Message);
                    }
                }
                rows.Add(new { text, audio = url });
            }
            return rows.ToArray();
        }

        private static JObject BuildArcademyLocalAssets()
        {
            var (gifs, stills) = ArcademyLocalMedia.BuildLocalAssets(ArcAssetsRoot, CoreSettings.Current?.DisabledAssetPaths);
            return new JObject
            {
                ["gifs"] = new JArray(gifs.Select(g => (object)ArcAssetsUrl(g.Rel, g.Animated)).ToArray()),
                ["stills"] = new JArray(stills.Select(r => (object)ArcAssetsUrl(r)).ToArray()),
            };
        }

        private static JArray BuildArcademyLoomSpirals()
        {
            try { Directory.CreateDirectory(DtrhLoomStore.SpiralsFolder); } catch { }
            return new JArray(DtrhLoomStore.List()
                .Select(sp => (object)WebAssetServer.Shared.HostUrl(ArcSpiralsHost, "loom_" + sp.Slug + ".gif")).ToArray());
        }
    }
}
