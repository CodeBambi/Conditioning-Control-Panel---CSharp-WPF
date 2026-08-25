using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// THE STUDENT ID PHOTO, on disk. One file and one sidecar, both fixed names:
/// <c>%LOCALAPPDATA%/ConditioningControlPanel/arcademy/avatar.png</c> plus
/// <c>avatar.hash</c>, which records the Discord avatar hash those bytes were fetched for.
///
/// <para>THE SNOWFLAKE RULE (STUDENT ID contract, "Snowflake rule"). The Discord CDN url and the
/// Discord user id NEVER reach the page: the CDN url embeds the snowflake, so this class is the
/// only thing that ever sees it, and what crosses the bridge is a <c>data:image/png;base64,...</c>
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
/// </summary>
internal static class ArcademyAvatarCache
{
    /// <summary>Fixed names. No page string, no server string and no hash ever reaches
    /// <c>Path.Combine</c> here, so traversal is not a thing that can be attempted.</summary>
    private const string AvatarFile = "avatar.png";
    private const string HashFile = "avatar.hash";

    /// <summary>The contract's ceiling for the data URI that rides the bridge (24 KB). It is a cap
    /// on the STRING, not on the file, because the string is what the page is promised.</summary>
    public const int MaxDataUriChars = 24 * 1024;

    /// <summary>The prefix costs 22 of those characters and base64 costs 4 per 3 bytes, so this is
    /// the largest PNG whose data URI still fits under the cap.</summary>
    private static readonly int MaxPngBytes = (MaxDataUriChars - DataUriPrefix.Length) / 4 * 3;

    private const string DataUriPrefix = "data:image/png;base64,";

    /// <summary>What the CDN is allowed to hand back before we stop reading. An avatar is tens of
    /// KB; anything past this is not the picture we asked for.</summary>
    private const int MaxFetchBytes = 512 * 1024;

    /// <summary>The two decode widths, in order. 128 is the card's photo well at 2x; 96 is the one
    /// retry for an avatar whose 128px PNG will not fit the 24 KB cap (a busy animated frame). A
    /// third rung would be a smaller picture than the well deserves, so there is not one.</summary>
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
            App.Logger?.Debug("ArcademyAvatarCache.PathFor({F}): {E}", bare, ex.Message);
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

    private static string? ReadTag()
    {
        try
        {
            var p = PathFor(HashFile);
            if (p == null || !File.Exists(p)) return null;
            var tag = File.ReadAllText(p).Trim();
            return tag.Length == 0 || tag.Length > 128 ? null : tag;
        }
        catch { return null; }
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
            if (fi.Length == 0 || fi.Length > MaxPngBytes) return null;
            var bytes = File.ReadAllBytes(p);
            if (!IsPng(bytes)) return null;
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
    /// re-encode it to a small PNG and store it atomically. Returns <c>true</c> only when the bytes
    /// on disk CHANGED, which is the caller's cue to push a <c>profile</c> frame.
    ///
    /// <para>Never throws and never blocks a UI thread: the decode and the encode run on whatever
    /// thread the continuation lands on, against a frozen <see cref="BitmapImage"/>.</para>
    /// </summary>
    public static async Task<bool> EnsureCachedAsync()
    {
        try
        {
            var d = App.Discord;
            if (d == null || !d.IsAuthenticated) return false;
            var tag = CurrentTag();
            if (tag == null) return false;                       // default avatar: nothing to fetch
            var file = PathFor(AvatarFile);
            if (file == null) return false;
            if (File.Exists(file) && string.Equals(ReadTag(), tag, StringComparison.Ordinal)) return false;

            // OFFLINE MODE: no request at all. The file already on disk stays usable.
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("ArcademyAvatarCache: refresh declined - offline mode");
                return false;
            }

            var url = d.GetAvatarUrl(128);
            if (string.IsNullOrEmpty(url)) return false;
            // An animated avatar comes back as .gif; the card wants the still, and the contract's
            // data URI is specified as image/png either way.
            url = url!.Replace(".gif?", ".png?");

            if (!await FetchGate.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)) return false;
            try
            {
                // Re-check under the gate: the other caller may have just fetched this exact hash.
                if (File.Exists(file) && string.Equals(ReadTag(), tag, StringComparison.Ordinal)) return false;

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

                var png = Encode(raw);
                if (png == null)
                {
                    App.Logger?.Debug("ArcademyAvatarCache: avatar would not fit {Cap} chars - no photo",
                        MaxDataUriChars);
                    return false;
                }

                if (!WriteAtomic(file, png)) return false;
                try
                {
                    var hp = PathFor(HashFile);
                    if (hp != null) WriteAtomic(hp, System.Text.Encoding.UTF8.GetBytes(tag));
                }
                catch { /* a missing sidecar only costs the next open a re-fetch */ }

                App.Logger?.Information("ArcademyAvatarCache: student photo cached ({N} bytes)", png.Length);
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

    /// <summary>Decode whatever the CDN sent and re-encode it as a PNG small enough for the cap,
    /// trying <see cref="DecodeWidths"/> in order. Null when even the smallest will not fit.</summary>
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

                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                enc.Save(ms);
                var bytes = ms.ToArray();
                if (bytes.Length > 0 && bytes.Length <= MaxPngBytes) return bytes;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ArcademyAvatarCache.Encode({W}): {E}", width, ex.Message);
            }
        }
        return null;
    }

    /// <summary>Temp file then <c>File.Move(overwrite)</c>: a half-written avatar.png read by the
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
            App.Logger?.Debug("ArcademyAvatarCache.WriteAtomic: {E}", ex.Message);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }

    /// <summary>The bytes decide what they are, never the label they arrived with.</summary>
    private static bool IsPng(byte[] b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
}
