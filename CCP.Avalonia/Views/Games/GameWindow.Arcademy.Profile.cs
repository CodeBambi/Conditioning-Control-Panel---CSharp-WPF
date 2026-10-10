using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Arcademy;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Arcademy host, the student ID half (WPF 7.1.5 ArcademyHostService.cs): the cached Discord
    /// photo behind <c>profile.avatarUrl</c> (KickAvatarRefresh :4366, ArcademyAvatarCache in Core, the
    /// JPEG encode done with Skia here), <c>link-discord</c> (:4397, one click is enough: a finished
    /// link applies the <c>discord</c> rung itself) and <c>share-image</c> (:5518, the report card PNG
    /// onto the clipboard, exactly one reply always).
    /// </summary>
    internal sealed partial class GameWindow
    {
        /// <summary>WPF LinkDeadline.</summary>
        internal static readonly TimeSpan ArcLinkDeadline = TimeSpan.FromSeconds(120);
        /// <summary>A share card is ~1-2 MB of PNG; the bridge is not a file transfer (WPF MaxShareImageChars).</summary>
        internal const int ArcMaxShareImageChars = 4_400_000;

        private int _arcLinkInFlight;
        private bool _arcLinkCancelled;
        private CancellationTokenSource? _arcLinkCts;

        /// <summary>Test seams: the link flow (true = linked) and the clipboard write.</summary>
        internal Func<CancellationToken, Task<bool>>? ArcLinkFlowOverride;
        internal Func<byte[], Task<bool>>? ArcClipboardOverride;

        // ---- the student photo ------------------------------------------------------------------

        /// <summary>Wire the Core cache to this head once: the Discord account and a Skia JPEG encoder.</summary>
        private static void EnsureArcademyAvatarCache()
        {
            ArcademyAvatarCache.Discord ??= () => AccountSeed.Discord;
            ArcademyAvatarCache.Encoder ??= ArcEncodeAvatarJpeg;
        }

        /// <summary>Decode whatever the CDN sent, scale to <paramref name="width"/> px wide, flatten any
        /// alpha onto black (JPEG has none; Discord serves an opaque square), encode JPEG. Null on failure.</summary>
        internal static byte[]? ArcEncodeAvatarJpeg(byte[] raw, int width, int quality)
        {
            SKBitmap? decoded;
            try { decoded = SKBitmap.Decode(raw); }
            catch (Exception) { return null; }   // Skia throws on bytes that are no image at all
            using var src = decoded;
            if (src == null || src.Width <= 0 || src.Height <= 0) return null;
            int w = Math.Min(width, src.Width);
            int h = Math.Max(1, (int)Math.Round(src.Height * (w / (double)src.Width)));
            using var flat = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using (var canvas = new SKCanvas(flat))
            {
                canvas.Clear(SKColors.Black);
                using var image = SKImage.FromBitmap(src);
                canvas.DrawImage(image, new SKRect(0, 0, w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }
            using var img = SKImage.FromBitmap(flat);
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, quality);
            return data?.ToArray();
        }

        /// <summary>The cached photo as a data: uri, only at the <c>discord</c> rung of a linked account
        /// (WPF BuildProfile :1067). The picture is the campus ghost's, so any lower rung sends none.</summary>
        private static string? ArcademyAvatarDataUri(bool linked, string share)
        {
            if (!linked || share != "discord") return null;
            try { EnsureArcademyAvatarCache(); return ArcademyAvatarCache.ReadDataUri(); }
            catch (Exception ex) { Log.Debug("[Game] arcademy avatar read: {E}", ex.Message); return null; }
        }

        /// <summary>Bring the cached photo up to date off the UI thread and push a <c>profile</c> frame
        /// only if the bytes changed. Generation-guarded like every other continuation here.</summary>
        private void KickArcademyAvatarRefresh()
        {
            int epoch = Volatile.Read(ref _arcGeneration);
            EnsureArcademyAvatarCache();
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await ArcademyAvatarCache.EnsureCachedAsync().ConfigureAwait(false)) return;
                    Dispatcher.UIThread.Post(() => { if (ArcLive(epoch)) PushArcademyProfile(); });
                }
                catch (Exception ex) { Log.Debug("[Game] arcademy avatar refresh: {E}", ex.Message); }
            });
        }

        // ---- link-discord -----------------------------------------------------------------------

        /// <summary>The port's Discord link, as Settings &gt; Account runs it: sign in through the browser,
        /// then link the Discord to the signed-in account. Refused (false) with no account to link to.</summary>
        private async Task<bool> ArcademyLinkFlowAsync(CancellationToken ct)
        {
            var d = AccountSeed.Discord;
            var s = CoreSettings.Current;
            if (d == null) throw new InvalidOperationException("no Discord account on this head");
            if (string.IsNullOrEmpty(s?.UnifiedId) || string.IsNullOrEmpty(s?.AuthToken))
                throw new InvalidOperationException("sign in to the app first");
            Action<string> open = url => { if (ExternalOpener.Allowed(url)) _ = ExternalOpener.OpenAsync(this, url); };
            await d.SignInAsync(open, ct);
            ct.ThrowIfCancellationRequested();
            if (!d.IsAuthenticated) return false;
            var (outcome, error) = await AccountLink.LinkAsync("discord");
            if (outcome is AccountLink.Outcome.Linked or AccountLink.Outcome.AlreadyLinked) return true;
            throw new InvalidOperationException("link refused: " + outcome + " " + error);
        }

        private async void OnArcademyLinkDiscord()
        {
            if (Interlocked.CompareExchange(ref _arcLinkInFlight, 1, 0) != 0)
            {
                Log.Debug("[Game] arcademy: link-discord ignored - a link is already open");
                return;
            }
            _arcLinkCancelled = false;
            int epoch = Volatile.Read(ref _arcGeneration);
            var cts = _arcLinkCts = new CancellationTokenSource();
            try
            {
                EnsureArcademyAvatarCache();
                var d = AccountSeed.Discord;
                if (ArcLinkFlowOverride == null && d == null) { PushArcademyProfile("failed"); return; }

                // Already linked: the short path. A stale page must not be able to strand its own chip.
                if (ArcLinkFlowOverride == null && d!.IsAuthenticated)
                {
                    ApplyArcademyDiscordRung();
                    await ArcademyAvatarCache.EnsureCachedAsync();
                    if (ArcLive(epoch)) PushArcademyProfile("linked");
                    return;
                }

                Log.Information("[Game] arcademy: student ID chip is opening the Discord link-up");
                var flow = (ArcLinkFlowOverride ?? ArcademyLinkFlowAsync)(cts.Token);
                // Observe a fault whichever way the race lands.
                _ = flow.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                var done = await Task.WhenAny(flow, Task.Delay(ArcLinkDeadline, cts.Token));
                if (done != flow)
                {
                    if (_arcLinkCancelled) return;   // panic / teardown already answered the page
                    Log.Information("[Game] arcademy: Discord link timed out after {S}s - cancelled", ArcLinkDeadline.TotalSeconds);
                    _arcLinkCancelled = true;
                    try { cts.Cancel(); } catch { }
                    if (ArcLive(epoch)) PushArcademyProfile("cancelled");
                    return;
                }
                bool linked = await flow;   // rethrows whatever the flow threw
                if (_arcLinkCancelled || !ArcLive(epoch)) return;
                if (!linked) { PushArcademyProfile("cancelled"); return; }

                ApplyArcademyDiscordRung();
                try { await ArcademyAvatarCache.EnsureCachedAsync(); } catch (Exception ex) { Log.Debug("[Game] arcademy avatar: {E}", ex.Message); }
                Log.Information("[Game] arcademy: Discord linked from the student ID - photo rung applied");
                if (ArcLive(epoch)) PushArcademyProfile("linked");
            }
            catch (OperationCanceledException)
            {
                Log.Information("[Game] arcademy: Discord link cancelled");
                if (!_arcLinkCancelled && ArcLive(epoch)) PushArcademyProfile("cancelled");
            }
            catch (Exception ex)
            {
                if (_arcLinkCancelled) Log.Debug("[Game] arcademy: link-up threw after we cancelled it: {E}", ex.Message);
                else
                {
                    Log.Warning("[Game] arcademy: Discord link failed: {E}", ex.Message);
                    if (ArcLive(epoch)) PushArcademyProfile("failed");
                }
            }
            finally
            {
                if (ReferenceEquals(_arcLinkCts, cts)) _arcLinkCts = null;
                try { cts.Dispose(); } catch { }
                Interlocked.Exchange(ref _arcLinkInFlight, 0);
            }
        }

        /// <summary>Raise the consent rung to <c>discord</c> the ordinary way, through AppSettings, so the
        /// presence service's hook and the page's <c>setting</c> echo both fire.</summary>
        private static void ApplyArcademyDiscordRung()
        {
            try
            {
                var s = CoreSettings.Current;
                if (s == null) return;
                if (!string.Equals(ArcademyPresenceShare(s), "discord", StringComparison.Ordinal))
                {
                    s.ArcademyPresenceShare = "discord";
                    CoreSettings.Save();
                }
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy discord rung: {E}", ex.Message); }
        }

        /// <summary>Panic and teardown close an open link-up. Tells the page only when there is one to hear it.</summary>
        private void CancelArcademyLink(string why, bool tellPage)
        {
            if (Volatile.Read(ref _arcLinkInFlight) == 0) return;
            Log.Information("[Game] arcademy: cancelling the open Discord link-up ({Why})", why);
            _arcLinkCancelled = true;
            try { _arcLinkCts?.Cancel(); } catch { }
            if (tellPage) PushArcademyProfile("cancelled");
        }

        // ---- share-image ------------------------------------------------------------------------

        /// <summary>The validated PNG bytes of a share card, or null: size cap, base64, the PNG signature.</summary>
        internal static byte[]? ArcDecodeSharePng(string? png)
        {
            if (string.IsNullOrEmpty(png) || png!.Length > ArcMaxShareImageChars) return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(png); }
            catch (FormatException) { return null; }
            if (bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) return null;
            return bytes;
        }

        /// <summary>The page drew the report card and hands the PNG over as base64; the host puts it on
        /// the clipboard as an image. Exactly ONE reply always goes back: on a false the page falls to its
        /// own download rung, so a clipboard that takes no image (some Linux sessions) still shares.</summary>
        private async void OnArcademyShareImage(string? png)
        {
            int epoch = Volatile.Read(ref _arcGeneration);
            bool ok = false;
            try
            {
                var bytes = ArcDecodeSharePng(png);
                if (bytes != null) ok = await (ArcClipboardOverride ?? ArcPutPngOnClipboard)(bytes);
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy share-image: {E}", ex.Message); }
            if (!ArcLive(epoch)) return;
            try { Post(new { type = "share-image-result", ok }); }
            catch (Exception ex) { Log.Debug("[Game] arcademy share-image reply: {E}", ex.Message); }
        }

        private async Task<bool> ArcPutPngOnClipboard(byte[] bytes)
        {
            var clip = Clipboard;
            if (clip == null) return false;
            Bitmap bmp;
            try { using var ms = new MemoryStream(bytes, writable: false); bmp = new Bitmap(ms); }
            catch (Exception ex) { Log.Debug("[Game] arcademy: share image would not decode: {E}", ex.Message); return false; }
            // Another process may hold the clipboard for a moment: one retry, as everything else does.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try { await clip.SetBitmapAsync(bmp); return true; }
                catch (Exception ex)
                {
                    Log.Debug("[Game] arcademy: clipboard refused the share image: {E}", ex.Message);
                    await Task.Delay(60);
                }
            }
            return false;
        }
    }
}
