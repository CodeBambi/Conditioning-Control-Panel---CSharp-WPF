using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Transfer;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// Goon own-media transfer, the window half (WPF GoonHostService: the cache boot in Launch :244, the
    /// bridge attach in OnPageReady :516, OnRecvVerb :857, the close steps :2126). The stores and the
    /// page protocol are Core's: <see cref="TransferCacheStore"/> / <see cref="TransferCompressionService"/>
    /// (what this player may send: their own library, re-encoded and stripped of metadata),
    /// <see cref="GoonCacheBridge"/> (cache-req / cache-put / encode-done and the cache-* feed) and
    /// <see cref="TransferInboxStore"/> (goon-recv-*: what a duel partner sent).
    ///
    /// <para>Rules kept from WPF: sending is a patron perk (caps.mediaTransfer = TransferAllowed) and
    /// stays off until both players tick the lobby box for that match; a received file is a claim until
    /// commit, where the host hashes what it wrote and the magic bytes pick the extension (a mismatch is
    /// a rejection, never a relabel), under the 64 MB per-file and 2 GB inbox caps; received files live
    /// only in transfer-cache/recv, are only ever handed to the page as a url, and are wiped on every
    /// page boot and window close. Nothing received is opened or run by the app.</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        private static bool _goonTransferSeeded;
        private bool _goonCacheAttached;

        /// <summary>The head's doors for the Core transfer cache. First seeding wins.</summary>
        internal static void SeedGoonTransfer()
        {
            if (_goonTransferSeeded) return;
            _goonTransferSeeded = true;
            TransferPlatform.ActivePool = () =>
                GameMediaManifest.EnumerateActive(CorePaths.EffectiveAssets, CoreSettings.Current.DisabledAssetPaths);
            TransferPlatform.GifFrameCount = Platform.TransferStillEncoder.GifFrameCount;
            TransferPlatform.CacheUrlBase = () => Platform.WebAssetServer.Shared.CacheUrlBase;
            TransferPlatform.AssetUrl = rel => Platform.WebAssetServer.Shared.AssetUrl(rel.Replace('/', Path.DirectorySeparatorChar));
            TransferPlatform.OnUi = a => Dispatcher.UIThread.Post(a);   // always queued, as WPF BeginInvoke: the feed never cuts into the ready frames
            StillCompressLane.Encoder ??= Platform.TransferStillEncoder.Compress;
            SeedGoonVideoEngine();
        }

        /// <summary>The video engine behind Core's <see cref="VideoTranscodeLane"/> seams. Windows: the same
        /// WinRT MediaTranscoder WPF uses (Platform/WinRtVideoTranscoder.cs). Linux: none yet, so a clip
        /// that needs shrinking is refused as no-decoder and never offered (docs/avalonia-linux-exceptions.md).
        /// First seeding wins, so a test's fake stays put.</summary>
        internal static void SeedGoonVideoEngine()
        {
#if CCP_WINRT
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
            VideoTranscodeLane.Probe ??= Platform.WinRtVideoTranscoder.ProbeAsync;
            VideoTranscodeLane.Transcode ??= Platform.WinRtVideoTranscoder.TranscodeAsync;
            VideoTranscodeLane.Preview ??= Platform.WinRtVideoTranscoder.PreviewAsync;
#endif
        }

        /// <summary>WPF Launch :244: the cache folders exist before the page can ask for a file, and the
        /// plan refresh is kicked now, so a first match after a cold start does not reach Live with an
        /// empty sendable list.</summary>
        private void OpenGoonTransfer()
        {
            try
            {
                SeedGoonTransfer();
                TransferCacheStore.Instance.EnsureRoot();
                TransferCompressionService.Instance.Initialize();   // idempotent
                TransferCompressionService.Instance.ReleaseHostHold();   // the last window's close held the queue
                _ = Task.Run(() => TransferCompressionService.Instance.RefreshAsync());
            }
            catch (Exception ex) { Log.Warning("[Goon] transfer cache init: {E}", ex.Message); }
        }

        /// <summary>WPF OnPageReady :495-516: the ephemeral inbox is wiped BEFORE the manifest lists it
        /// (so <c>received</c> is always empty and a past partner's media never re-primes the pool), and
        /// the cache feed attaches after the manifest so the page has its pool first.</summary>
        private JArray GoonReceivedForManifest()
        {
            try
            {
                TransferInboxStore.Instance.PurgeCommittedSafe("page boot");
                return JArray.FromObject(TransferInboxStore.Instance.ListForManifest());
            }
            catch (Exception ex)
            {
                Log.Warning("[Goon] received list failed: {E}", ex.Message);
                return new JArray();
            }
        }

        private void AttachGoonCache()
        {
            try
            {
                if (_goonCacheAttached) GoonCacheBridge.Detach();   // a reload's second "ready": a fresh feed
                GoonCacheBridge.Attach(Post);
                _goonCacheAttached = true;
            }
            catch (Exception ex) { Log.Warning("[Goon] cache bridge attach: {E}", ex.Message); }
        }

        /// <summary>WPF DisposeAll :2126: unbind the feed before the page goes, never leave the queue paused
        /// by a match that ended with the window, and the session's received files go with the session.</summary>
        private void CloseGoonTransfer()
        {
            try { GoonCacheBridge.Detach(); } catch { }
            _goonCacheAttached = false;
            // Window close and panic (panic closes the window): every encode in flight is cancelled and its
            // temp file removed, and nothing new starts until the Goon window is open again.
            try { TransferCompressionService.Instance.HoldForHostClose(); } catch { }
            try { TransferCompressionService.Instance.ResumeAfterMatch(); } catch { }
            try { TransferInboxStore.Instance.PurgeCommittedSafe("window closed"); } catch { }
        }

        /// <summary>WPF OnRecvVerb: the page's disk backend for artifacts a duel partner sent. Every verb
        /// answers with the one <c>goon-recv-result { id, ok, url, bytes, error }</c> shape. Error
        /// vocabulary: bad-name | too-big | bad-format | hash-mismatch | cap-reached | io-failed |
        /// bad-seq | unknown-job.</summary>
        private void OnGoonRecvVerb(JObject o)
        {
            var type = (string?)o["type"] ?? "";
            var id = (string?)o["id"] ?? "";
            var store = TransferInboxStore.Instance;
            try
            {
                switch (type)
                {
                    case "goon-recv-begin":
                    {
                        var err = store.Begin(id, (string?)o["sha256"], (string?)o["mime"],
                            (long?)o["bytes"] ?? 0, (string?)o["origin"]);
                        ReplyGoonRecv(id, err == null, null, 0, err);
                        break;
                    }
                    case "goon-recv-chunk":
                    {
                        var err = store.AppendChunk(id, (int?)o["seq"] ?? -1, (string?)o["b64"]);
                        ReplyGoonRecv(id, err == null, null, 0, err);
                        break;
                    }
                    case "goon-recv-commit":
                        // Off the UI thread: a full SHA-256 over up to 64 MB.
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                var r = store.Commit(id);
                                ReplyGoonRecv(id, r.Ok, r.Url, r.Ok ? GoonRecvLength(r.Sha, r.Ext) : 0, r.Error);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning("[Goon] goon-recv-commit: {E}", ex.Message);
                                ReplyGoonRecv(id, false, null, 0, "io-failed");
                            }
                        });
                        break;
                    case "goon-recv-abort":
                        store.Abort(id);
                        ReplyGoonRecv(id, true, null, 0, null);
                        break;
                    case "goon-recv-drop":
                    {
                        var sha = (string?)o["sha256"];
                        var ok = store.Drop(sha);
                        ReplyGoonRecv(id.Length > 0 ? id : sha ?? "", ok, null, 0, ok ? null : "bad-name");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Goon] {Type}: {E}", type, ex.Message);
                ReplyGoonRecv(id, false, null, 0, "io-failed");
            }
        }

        private static long GoonRecvLength(string sha, string ext)
        {
            try
            {
                var p = Path.Combine(TransferInboxStore.Instance.RecvDir, sha + "." + ext);
                return File.Exists(p) ? new FileInfo(p).Length : 0;
            }
            catch { return 0; }
        }

        private void ReplyGoonRecv(string id, bool ok, string? url, long bytes, string? error) =>
            Post(new { type = "goon-recv-result", id, ok, url, bytes, error });
    }
}
