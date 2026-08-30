using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// THE STUDENT ID PHOTO, on disk. One file and one sidecar, both fixed names:
/// <c>%LOCALAPPDATA%/ConditioningControlPanel/arcademy/avatar.jpg</c> plus
/// <c>avatar.hash</c>, which records the Discord avatar hash those bytes were fetched for AND the
/// encode recipe they were made with.
///
/// <para>JPEG, NOT PNG, AND THAT IS THE WHOLE OF BUG "PHOTO PENDING FOREVER". WPF's
/// <c>PngBitmapEncoder</c> re-encodes an already-optimised CDN avatar LARGER than it arrived:
/// measured against a real 128x128 Discord avatar, a 128px PNG round-trip came back at 33,713
/// bytes and the 96px retry at 19,860 - both over the old 18,414-byte ceiling, so
/// <see cref="Encode"/> returned null on EVERY avatar and the card had never once worn a photo on
/// desktop. <c>JpegBitmapEncoder</c> at <see cref="JpegQuality"/> puts the same 128px frame at
/// about 6 KB. The page was only ever promised "a <c>data:</c> URI"; the format is ours.</para>
///
/// <para>THE SNOWFLAKE RULE (STUDENT ID contract, "Snowflake rule"). The Discord CDN url and the
/// Discord user id NEVER reach the page: the CDN url embeds the snowflake, so this class is the
/// only thing that ever sees it, and what crosses the bridge is a <c>data:image/jpeg;base64,...</c>
/// string of at most <see cref="MaxDataUriChars"/> characters. Nothing here is ever logged with a
/// url in it, for the same reason <c>GoonAvatarCache</c> refuses to.</para>
///
/// <para>SELF-HEALING BY CONSTRUCTION, and the prior art next door is deliberate
/// (<see cref="GoonGame.GoonAvatarCache"/>): every read is a try/catch that returns null, every
/// write is a try/catch that returns false, and a truncated or corrupt file simply fails its next
/// read and is re-fetched. A dead cache costs the card a picture - never a boot, never a class.</para>
///
/// <para>OFFLINE MODE NEVER LEAVES THE MACHINE. <see cref="EnsureCachedAsync"/> short-circuits with
/// no request at all when <c>OfflineMode</c> is set; the file already on disk stays readable, so an
/// offline player keeps the photo they last had.</para>
///
/// <para><see cref="Dir"/> IS SHARED AND IS NEVER DELETED. WebView2 keeps its <c>EBWebView</c>
/// profile folder in the same directory, so nothing here may ever remove the directory itself -
/// only the two (three, counting the retired PNG) fixed file names below.</para>
/// </summary>
internal static class ArcademyAvatarCache
{
    /// <summary>Fixed names. No page string, no server string and no hash ever reaches
    /// <c>Path.Combine</c> here, so traversal is not a thing that can be attempted.</summary>
    private const string AvatarFile = "avatar.jpg";
    private const string HashFile = "avatar.hash";

    /// <summary>The PNG this cache wrote before the encoder switch. It never once existed in the
    /// wild (the PNG re-encode could not fit the cap), but a machine that somehow has one should
    /// not keep an orphan next to the JPEG - so a successful write sweeps it, BY NAME.</summary>
    private const string LegacyAvatarFile = "avatar.png";

    /// <summary>The contract's ceiling for the data URI that rides the bridge (48 KB). It is a cap
    /// on the STRING, not on the file, because the string is what the page is promised.
    ///
    /// <para>It was 24 KB, which the PNG encoder could not meet on any real avatar. JPEG clears the
    /// old figure eight times over, so this doubling is not what fixes the bug - it is the headroom
    /// that stops the next busy avatar from re-opening it.</para></summary>
    public const int MaxDataUriChars = 48 * 1024;

    /// <summary>The prefix costs 23 of those characters and base64 costs 4 per 3 bytes, so this is
    /// the largest image whose data URI still fits under the cap.</summary>
    private static readonly int MaxImageBytes = (MaxDataUriChars - DataUriPrefix.Length) / 4 * 3;

    private const string DataUriPrefix = "data:image/jpeg;base64,";

    /// <summary>What <see cref="Encode"/> asks the JPEG encoder for. 85 is the usual "you cannot
    /// see the difference at 128px" figure and lands a Discord avatar around 6 KB.</summary>
    private const int JpegQuality = 85;

    /// <summary>THE RECIPE STAMP, and it is in the sidecar for one reason: the sidecar now records
    /// FAILURES too (see <see cref="EnsureCachedAsync"/>'s negative cache), and a failure recorded
    /// under the old PNG encoder must not outlive the encoder that caused it. Bump this string
    /// whenever the encode changes and every sidecar on every machine - success and failure alike -
    /// reads as stale on the next open, which re-fetches exactly once.</summary>
    private const string EncodeRecipe = "j85-48k-1";

    /// <summary>What the CDN is allowed to hand back before we stop reading. An avatar is tens of
    /// KB; anything past this is not the picture we asked for.</summary>
    private const int MaxFetchBytes = 512 * 1024;

    /// <summary>The two decode widths, in order. 128 is the card's photo well at 2x; 96 is the one
    /// retry for an avatar whose 128px JPEG will not fit the cap. Since the encoder switch the
    /// first rung lands around 6 KB against a 36 KB ceiling, so the second is a rung nothing is
    /// expected to reach. A third would be a smaller picture than the well deserves.</summary>
    private static readonly int[] DecodeWidths = { 128, 96 };

    /// <summary>An avatar may never be on the critical path of anything (the Goon cache's rule,
    /// with a little more room because this one runs off a window-open, not off a match).</summary>
    private static readonly HttpClient Http = BuildHttpClient();

    private static HttpClient BuildHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        try { c.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}"); }
        catch { /* a header we could not set is not worth failing a static ctor over */ }
        return c;
    }

    /// <summary>Serialises the fetch: the window-open kick and a link that just completed can both
    /// arrive at once, and two writers on one file is the only way this cache corrupts itself.</summary>
    private static readonly SemaphoreSlim FetchGate = new(1, 1);

    public static string Dir => Path.Combine(App.UserDataPath, "arcademy");

    private static string? PathFor(string bare)
    {
        try
        {
            var dir = Dir;
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, bare);
        }
        catch (Exception ex)
        {
            // WARNING, not Debug: a directory we cannot make is a card that will never have a
            // photo, and a permanent no-photo state has to be audible at the default level.
            App.Logger?.Warning("ArcademyAvatarCache.PathFor({F}): {E}", bare, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// The avatar hash these bytes belong to, tagged with WHICH hash it is: the per-guild avatar
    /// and the global one are different pictures under the same account, and
    /// <c>DiscordService.GetAvatarUrl</c> prefers the guild one. Without the tag, a player who
    /// leaves the server keeps a stale server picture forever.
    /// </summary>
    private static string? CurrentTag()
    {
        var d = App.Discord;
        if (d == null || !d.IsAuthenticated) return null;
        if (!string.IsNullOrEmpty(d.GuildAvatar)) return "g:" + d.GuildAvatar;
        if (!string.IsNullOrEmpty(d.Avatar)) return "u:" + d.Avatar;
        return null;   // a default avatar is not a picture we can fetch
    }

    /// <summary>Said once per launch and no more. A player on a default Discord avatar would
    /// otherwise earn a Warning on every single window open, and a line that cries every time is
    /// a line nobody reads.</summary>
    private static int _noTagWarned;

    /// <summary>
    /// WHAT THE SIDECAR SAYS, in one line: <c>&lt;recipe&gt;|&lt;ok|no&gt;|&lt;tag&gt;</c>.
    ///
    /// <para><c>ok</c> means "these bytes are on disk for this hash"; <c>no</c> is THE NEGATIVE
    /// CACHE - "this exact picture was fetched and would not encode, do not fetch it again". Before
    /// it existed, <c>KickAvatarRefresh</c> re-downloaded and re-failed on the hopeless avatar every
    /// time the window opened. Three things still invalidate it, which is the whole point of writing
    /// the recipe and the tag INTO the line rather than keeping a bare hash: a different avatar (the
    /// tag moves), a different encoder (<see cref="EncodeRecipe"/> moves) and a deleted sidecar.</para>
    /// </summary>
    private static string Sidecar(string tag, bool ok) => EncodeRecipe + (ok ? "|ok|" : "|no|") + tag;

    /// <summary>The sidecar line as written, or null. A line from an older build is simply a line
    /// that matches neither <see cref="Sidecar"/> form, so it re-fetches exactly once.</summary>
    private static string? ReadTag()
    {
        try
        {
            var p = PathFor(HashFile);
            if (p == null || !File.Exists(p)) return null;
            var tag = File.ReadAllText(p).Trim();
            return tag.Length == 0 || tag.Length > 160 ? null : tag;
        }
        catch { return null; }
    }

    /// <summary>Best-effort sidecar write. A sidecar we could not write only costs the next open a
    /// re-fetch, so this never fails anything above it.</summary>
    private static void WriteSidecar(string tag, bool ok)
    {
        try
        {
            var hp = PathFor(HashFile);
            if (hp != null) WriteAtomic(hp, System.Text.Encoding.UTF8.GetBytes(Sidecar(tag, ok)));
        }
        catch { /* a missing sidecar only costs the next open a re-fetch */ }
    }

    /// <summary>
    /// The cached photo as a data URI, WITHOUT touching the network - this is what
    /// <c>init</c> can afford to read synchronously.
    ///
    /// <para>It deliberately does NOT check the hash. A stale-but-real picture beats a blank well
    /// while the refresh is in the air (and offline, the stale one is all there will ever be); the
    /// sidecar's only job is deciding whether <see cref="EnsureCachedAsync"/> owes a fetch.</para>
    /// </summary>
    public static string? ReadDataUri()
    {
        try
        {
            var p = PathFor(AvatarFile);
            if (p == null || !File.Exists(p)) return null;
            var fi = new FileInfo(p);
            if (fi.Length == 0 || fi.Length > MaxImageBytes) return null;
            var bytes = File.ReadAllBytes(p);
            if (!IsJpeg(bytes)) return null;
            var uri = DataUriPrefix + Convert.ToBase64String(bytes);
            return uri.Length > MaxDataUriChars ? null : uri;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyAvatarCache.ReadDataUri: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Fetch the player's Discord avatar when the cache is missing or belongs to a different hash,
    /// re-encode it to a small JPEG and store it atomically. Returns <c>true</c> only when the bytes
    /// on disk CHANGED, which is the caller's cue to push a <c>profile</c> frame.
    ///
    /// <para>Never throws and never blocks a UI thread: the decode and the encode run on whatever
    /// thread the continuation lands on, against a frozen <see cref="BitmapImage"/>.</para>
    ///
    /// <para>TWO EARLY-OUTS ARE NOW DISTINGUISHABLE IN THE LOG, because for five days they were not
    /// and the wrong one was blamed: no fetchable avatar hash warns once per launch, and an avatar
    /// that will not encode warns once per hash (see the negative cache in <see cref="Sidecar"/>).
    /// Anything that leaves the card without a photo says so at the default level.</para>
    /// </summary>
    public static async Task<bool> EnsureCachedAsync()
    {
        try
        {
            var d = App.Discord;
            if (d == null || !d.IsAuthenticated) return false;
            var tag = CurrentTag();
            if (tag == null)
            {
                // A DEFAULT DISCORD AVATAR HAS NO HASH, so there is genuinely nothing to fetch -
                // but "nothing to fetch" and "fetched and could not encode" are the two ways this
                // card ends up with PHOTO PENDING forever, and the log has to name which.
                if (Interlocked.Exchange(ref _noTagWarned, 1) == 0)
                {
                    App.Logger?.Warning(
                        "ArcademyAvatarCache: no fetchable Discord avatar hash (default avatar?) - student photo stays pending");
                }
                return false;
            }
            var file = PathFor(AvatarFile);
            if (file == null) return false;
            var side = ReadTag();
            if (File.Exists(file) && string.Equals(side, Sidecar(tag, true), StringComparison.Ordinal)) return false;
            // THE NEGATIVE CACHE. This exact picture, under this exact encoder, has already been
            // fetched and rejected once; re-downloading it on every window open only re-fails.
            if (string.Equals(side, Sidecar(tag, false), StringComparison.Ordinal))
            {
                App.Logger?.Debug("ArcademyAvatarCache: this avatar is already known not to fit - no re-fetch");
                return false;
            }

            // OFFLINE MODE: no request at all. The file already on disk stays usable.
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("ArcademyAvatarCache: refresh declined - offline mode");
                return false;
            }

            var url = d.GetAvatarUrl(128);
            if (string.IsNullOrEmpty(url)) return false;
            // An animated avatar comes back as .gif; the card wants the still, and the CDN will
            // serve any avatar as .png. What WE re-encode it to afterwards is a separate question.
            url = url!.Replace(".gif?", ".png?");

            if (!await FetchGate.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)) return false;
            try
            {
                // Re-check under the gate: the other caller may have just fetched this exact hash,
                // or just recorded it as hopeless.
                var side2 = ReadTag();
                if (File.Exists(file) && string.Equals(side2, Sidecar(tag, true), StringComparison.Ordinal)) return false;
                if (string.Equals(side2, Sidecar(tag, false), StringComparison.Ordinal)) return false;

                byte[] raw;
                using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                           CancellationToken.None).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return false;
                    var ctype = resp.Content.Headers.ContentType?.MediaType ?? "";
                    if (!ctype.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return false;
                    var len = resp.Content.Headers.ContentLength;
                    if (len.HasValue && len.Value > MaxFetchBytes) return false;
                    raw = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
                if (raw.Length == 0 || raw.Length > MaxFetchBytes) return false;

                var img = Encode(raw);
                if (img == null)
                {
                    // WARNING, not Debug. This exact line at Debug is why a card that had never once
                    // shown a photo looked, from the logs, like a card that was working.
                    App.Logger?.Warning(
                        "ArcademyAvatarCache: avatar would not fit {Cap} chars at any decode width - no student photo",
                        MaxDataUriChars);
                    WriteSidecar(tag, false);        // do not come back for this one until it changes
                    return false;
                }

                if (!WriteAtomic(file, img)) return false;
                WriteSidecar(tag, true);
                // The retired PNG, BY NAME. Never the directory - WebView2 lives in it (see Dir).
                try
                {
                    var legacy = PathFor(LegacyAvatarFile);
                    if (legacy != null && File.Exists(legacy)) File.Delete(legacy);
                }
                catch { /* an orphan we could not sweep is not worth failing a cached photo over */ }

                App.Logger?.Information("ArcademyAvatarCache: student photo cached ({N} bytes)", img.Length);
                return true;
            }
            finally { FetchGate.Release(); }
        }
        catch (Exception ex)
        {
            // NEVER the url: a Discord CDN avatar url carries the snowflake.
            App.Logger?.Debug("ArcademyAvatarCache.EnsureCachedAsync failed: {E}", ex.Message);
            return false;
        }
    }

    /// <summary>Decode whatever the CDN sent and re-encode it as a JPEG small enough for the cap,
    /// trying <see cref="DecodeWidths"/> in order. Null when even the smallest will not fit.
    ///
    /// <para>THE ENCODER IS THE FIX. <c>PngBitmapEncoder</c> was here and it INFLATED every real
    /// avatar past the ceiling - a lossless re-encode of an already-optimised PNG is bigger than
    /// what it started with, so the 128px rung and the 96px rung both failed and the card had never
    /// worn a photo. A photo is a photo; JPEG at <see cref="JpegQuality"/> is what it wanted all
    /// along, and the 96px rung below is now a rung nothing is expected to reach.</para></summary>
    private static byte[]? Encode(byte[] raw)
    {
        foreach (var width in DecodeWidths)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;      // the stream may close right after
                bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bmp.DecodePixelWidth = width;                    // the decoder does the resize
                bmp.StreamSource = new MemoryStream(raw, writable: false);
                bmp.EndInit();
                bmp.Freeze();                                    // usable off the UI thread

                // JPEG HAS NO ALPHA, so say what happens to it here rather than leaving it to the
                // encoder. Discord serves an OPAQUE square (measured: 0 of 16,384 pixels on a real
                // avatar carry alpha - the circle is the page's crop, not the picture's), so in
                // practice this converts nothing; it is here so a transparent avatar degrades to a
                // defined dark corner instead of an encoder's opinion. NOT a RenderTargetBitmap:
                // this runs on whatever pool thread the continuation landed on, and this conversion
                // needs no dispatcher.
                var flat = new FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Bgr24, null, 0);
                flat.Freeze();

                var enc = new JpegBitmapEncoder { QualityLevel = JpegQuality };
                enc.Frames.Add(BitmapFrame.Create(flat));
                using var ms = new MemoryStream();
                enc.Save(ms);
                var bytes = ms.ToArray();
                if (bytes.Length > 0 && bytes.Length <= MaxImageBytes) return bytes;
                App.Logger?.Debug("ArcademyAvatarCache.Encode({W}): {N} bytes is over {Cap}",
                    width, bytes.Length, MaxImageBytes);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ArcademyAvatarCache.Encode({W}): {E}", width, ex.Message);
            }
        }
        return null;
    }

    /// <summary>Temp file then <c>File.Move(overwrite)</c>: a half-written avatar.jpg read by the
    /// next <c>init</c> is exactly the corruption the self-healing read would have to eat.</summary>
    private static bool WriteAtomic(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            // WARNING, not Debug: a write we cannot land is the other permanent no-photo state.
            App.Logger?.Warning("ArcademyAvatarCache.WriteAtomic: {E}", ex.Message);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }

    /// <summary>The bytes decide what they are, never the label they arrived with. JPEG's SOI
    /// marker, plus the 0xFF that opens whichever marker follows it.</summary>
    private static bool IsJpeg(byte[] b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
}
