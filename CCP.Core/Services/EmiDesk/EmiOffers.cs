using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The effects an offer can fire, plus the library probes they need (WPF
/// <c>Services/EmiDesk/EmiOffers.cs</c>). The rules live here; what only a head can do (open a
/// door, start a tour, put a spiral up) arrives through <see cref="Head"/>.
///
/// Feasibility is checked at DRAW time and fails SILENTLY (LINES-SCHEMA 4): an offer whose chip
/// would do nothing is never shown. A head action that is not wired makes its effect infeasible,
/// so a head without that surface never shows a dead chip either.
///
/// Nothing in here asks the network. Local assets only.
/// </summary>
public static class EmiOffers
{
    /// <summary>What the head can do for an offer. Every member is optional; unset = infeasible.</summary>
    public sealed class HeadSeams
    {
        /// <summary>WPF <c>App.EmiDesk.IsTargetAvailable</c>: in the catalogue, shown, not locked.</summary>
        public Func<string, bool>? TargetAvailable { get; init; }
        /// <summary>WPF <c>App.EmiDesk.OpenTarget</c>.</summary>
        public Action<string>? OpenTarget { get; init; }
        /// <summary>WPF <c>App.EmiDesk.PinTop</c>.</summary>
        public Action<string>? PinTop { get; init; }
        /// <summary>The main window is alive (WPF <c>Application.Current.MainWindow is MainWindow</c>).</summary>
        public Func<bool>? MainWindowAlive { get; init; }
        /// <summary>A session is running (WPF <c>SessionEngine.Active?.IsRunning</c>).</summary>
        public Func<bool>? SessionRunning { get; init; }
        /// <summary>A tutorial overlay is up (WPF <c>App.Tutorial?.IsActive</c>).</summary>
        public Func<bool>? TutorialActive { get; init; }
        /// <summary>Start a tour by its <c>TutorialType</c> name. Null = this head has no tours.</summary>
        public Action<string>? StartTour { get; init; }
        /// <summary>The head can start a tour right now (a tour service is seeded). Unset = it can.</summary>
        public Func<bool>? CanStartTour { get; init; }
        /// <summary>WPF <c>EmiBook.IsOpen</c>.</summary>
        public Func<bool>? BookOpen { get; init; }
        /// <summary>WPF <c>EmiBook.Open()</c>.</summary>
        public Action? OpenBook { get; init; }
        /// <summary>The spiral overlay for <paramref name="ms"/> at an opacity 0..1 (WPF <c>ShowOverlayTimed</c>).</summary>
        public Action<int, double>? Spiral { get; init; }
        /// <summary>Play one local video, non-strict (WPF <c>App.Video.PlaySpecificVideo(path, false)</c>).</summary>
        public Action<string>? PlayVideo { get; init; }
        /// <summary>Minutes of a video when genuinely known, else null.</summary>
        public Func<string, int?>? VideoMinutes { get; init; }
        /// <summary>Four flashes at the user's own size (WPF <c>TriggerFlashOnce(4, duration, null, true)</c>).</summary>
        public Action<int?>? Burst { get; init; }
        /// <summary>Gif rain for a span (WPF <c>EmiGifRain.Start</c>).</summary>
        public Action<TimeSpan>? Rain { get; init; }
        /// <summary>She is wider than her minimum.</summary>
        public Func<bool>? CanShrink { get; init; }
        /// <summary>Shrink to the minimum, snap to the nearest corner, save. Fires <c>resized</c> itself.</summary>
        public Action? Shrink { get; init; }
        /// <summary>Open a link in the user's own browser.</summary>
        public Action<string>? OpenUrl { get; init; }
        /// <summary>Bring the app forward on the Assets tab.</summary>
        public Action? OpenAssetsTab { get; init; }
    }

    /// <summary>The head's half. Null = no offers surface, every effect but <c>none</c> and <c>bedtime</c> is refused.</summary>
    public static HeadSeams? Head { get; set; }

    private static readonly string[] ImageExts = { ".gif", ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    private static readonly string[] VideoExts = { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv", ".m4v" };

    private static readonly Random Rng = new();

    /// <summary>How long the gif rain runs when she fires it (BRIEF 6).</summary>
    public static readonly TimeSpan RainDuration = TimeSpan.FromSeconds(10);

    /// <summary>How long the spiral overlay sits when she fires it (BRIEF 6).</summary>
    public const int SpiralMs = 6000;

    private const string ShortWalkVerb = "shortwalk";
    private const string UpgradeVerb = "upgrade";
    private const string BookOpenVerb = "open";

    // ---------------------------------------------------------------- feasibility

    /// <summary>
    /// Can this effect actually happen right now? Unknown effects are refused rather than
    /// swallowed, so a lines-file typo shows up as a missing offer and not as a dead chip.
    /// </summary>
    public static bool EffectFeasible(string? effect)
    {
        if (string.IsNullOrWhiteSpace(effect)) return false;
        try
        {
            var e = effect!.Trim();
            var h = Head;
            if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase))
                return h?.OpenTarget != null && h.TargetAvailable?.Invoke(e.Substring(5)) == true;

            if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase))
            {
                var id = e.Substring(7);
                if (h?.PinTop == null || h.TargetAvailable?.Invoke(id) != true) return false;
                // Pinning what is already pinned is a chip that does nothing. Do not offer it.
                try { if (EmiState.Current.Pins.Contains(id, StringComparer.OrdinalIgnoreCase)) return false; }
                catch { /* no state, treat as unpinned */ }
                return true;
            }

            if (e.StartsWith("tour:", StringComparison.OrdinalIgnoreCase))
                return TourFeasible(e.Substring(5));

            if (e.StartsWith("book:", StringComparison.OrdinalIgnoreCase))
                return BookFeasible(e.Substring(5));

            return e.ToLowerInvariant() switch
            {
                "none" => true,
                "spiral" => h?.Spiral != null,
                "video" => h?.PlayVideo != null && HasVideos(),
                "rain" => h?.Rain != null && HasImages(),
                "burst" => h?.Burst != null && HasImages(),
                "shrink" => h?.Shrink != null && h.CanShrink?.Invoke() == true,
                "bedtime" => !EmiLineEngine.BedtimeSet,
                // A link to the pack forum: the browser is not ours to doubt.
                "packs" => h?.OpenUrl != null,
                // "let me fetch it for you" only makes sense while they are still on local media.
                "assets" => AssetsOfferFeasible(),
                _ => false
            };
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] effect feasibility probe failed for {Effect}", effect);
            return false;
        }
    }

    /// <summary>
    /// Feasible means all of: the head can start tours, the main window is alive, no session is
    /// running, no tutorial overlay is already up, and this tour is not already latched in
    /// <see cref="EmiState.ToursDone"/> (brake 4 of the knock, stated again on the effect side).
    /// </summary>
    private static bool TourFeasible(string? verb)
    {
        try
        {
            var tour = TourNameOf(verb);
            if (tour == null) return false;
            var h = Head;
            if (h?.StartTour == null || h.CanStartTour?.Invoke() == false) return false;
            if (h.MainWindowAlive?.Invoke() != true) return false;
            if (h.SessionRunning?.Invoke() == true) return false;
            if (h.TutorialActive?.Invoke() == true) return false;
            if (EmiState.HasTourDone(tour)) return false;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] tour feasibility probe failed for {Verb}", verb);
            return false;
        }
    }

    /// <summary>The <c>TutorialType</c> NAME a verb maps to, or null for a verb nothing knows.</summary>
    internal static string? TourNameOf(string? verb) => (verb ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        ShortWalkVerb => EmiKnockMachine.ShortWalkTour,
        UpgradeVerb => EmiKnockMachine.UpgradeTour,
        _ => null
    };

    /// <summary>Say yes: run the tour. Also spends the knock (brake 1) before the tour starts.</summary>
    private static void StartTour(string? verb, bool fromAsk)
    {
        try
        {
            var tour = TourNameOf(verb);
            var h = Head;
            if (tour == null) { Log.Debug("[EmiDesk] unknown tour verb {Verb}, ignored", verb); return; }
            if (h?.StartTour == null || h.MainWindowAlive?.Invoke() != true)
            {
                Log.Debug("[EmiDesk] tour effect skipped: no main window");
                return;
            }

            EmiState.NoteKnockAnswered();
            h.StartTour(tour);
            EmiDeskBus.Fire("effectFired", new { channel = "tour", fromAsk });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] tour effect {Verb} failed", verb);
        }
    }

    /// <summary>The book is the manual: never locked, no main-window check. Infeasible only when
    /// there is no book or it is already open.</summary>
    private static bool BookFeasible(string? verb)
    {
        try
        {
            if (!string.Equals((verb ?? string.Empty).Trim(), BookOpenVerb, StringComparison.OrdinalIgnoreCase))
                return false;
            var h = Head;
            if (h?.OpenBook == null) return false;
            return h.BookOpen?.Invoke() != true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book feasibility probe failed for {Verb}", verb);
            return false;
        }
    }

    private static void OpenBook(string? verb, bool fromAsk)
    {
        try
        {
            if (!string.Equals((verb ?? string.Empty).Trim(), BookOpenVerb, StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("[EmiDesk] unknown book verb {Verb}, ignored", verb);
                return;
            }
            var open = Head?.OpenBook;
            if (open == null) return;
            open();
            EmiDeskBus.Fire("effectFired", new { channel = "book", fromAsk });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] book effect {Verb} failed", verb);
        }
    }

    // ---------------------------------------------------------------- firing

    /// <summary>
    /// Run an effect. Best-effort and logged: a failed effect must never take the widget down.
    /// <paramref name="fromAsk"/> rides into the moment the effect raises, and the engine returns
    /// without speaking when it sees it (LINES-SCHEMA 5.6): the offer's own reaction already spoke.
    /// </summary>
    public static void Run(string? effect, bool fromAsk)
    {
        if (string.IsNullOrWhiteSpace(effect)) return;
        var e = effect!.Trim();
        try
        {
            var h = Head;
            if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase)) { h?.OpenTarget?.Invoke(e.Substring(5)); return; }
            if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase)) { h?.PinTop?.Invoke(e.Substring(7)); return; }
            if (e.StartsWith("tour:", StringComparison.OrdinalIgnoreCase)) { StartTour(e.Substring(5), fromAsk); return; }
            if (e.StartsWith("book:", StringComparison.OrdinalIgnoreCase)) { OpenBook(e.Substring(5), fromAsk); return; }

            switch (e.ToLowerInvariant())
            {
                case "none": return;
                case "spiral": FireSpiral(fromAsk); return;
                case "video": FireVideo(fromAsk); return;
                case "burst": FireBurst(fromAsk); return;
                case "rain": FireRain(fromAsk); return;
                case "shrink": h?.Shrink?.Invoke(); return;
                case "bedtime": SetBedtime(fromAsk); return;
                case "packs": h?.OpenUrl?.Invoke(DiscordLinks.PackCatalogue); return;
                case "assets": h?.OpenAssetsTab?.Invoke(); return;
                default:
                    Log.Debug("[EmiDesk] unknown effect {Effect}, ignored", e);
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] effect {Effect} failed", e);
        }
    }

    // ---------------------------------------------------------------- the new-user rescue

    private static bool AssetsOfferFeasible()
    {
        try
        {
            var s = CoreSettings.Current;
            if (s == null) return false;
            if (!string.Equals(s.MediaSource, "local", StringComparison.OrdinalIgnoreCase)) return false;
            var h = Head;
            return h?.OpenAssetsTab != null && h.MainWindowAlive?.Invoke() == true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] assets offer feasibility probe failed");
            return false;
        }
    }

    /// <summary>
    /// THE EMPTY LIBRARY (MOMENTS <c>noMediaYet</c>). True only when the app is still pointed at
    /// LOCAL media and there is no local media to point at.
    /// </summary>
    public static bool LibraryIsEmpty()
    {
        try
        {
            var s = CoreSettings.Current;
            if (s == null) return false;
            if (!string.Equals(s.MediaSource, "local", StringComparison.OrdinalIgnoreCase)) return false;
            return !HasImages() && !HasVideos();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] empty-library probe failed");
            return false;
        }
    }

    /// <summary>Say it, if it is true. The moment's own <c>launch/1</c> limit keeps it to one line.</summary>
    public static void AnnounceEmptyLibrary()
    {
        try { if (LibraryIsEmpty()) EmiDeskBus.Fire("noMediaYet", null); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] noMediaYet fire failed"); }
    }

    /// <summary>The app's own spiral overlay, at the user's own spiral opacity, for six seconds.</summary>
    public static void FireSpiral(bool fromAsk)
    {
        try
        {
            var spiral = Head?.Spiral;
            if (spiral == null) return;
            double opacity = 0.85;
            try
            {
                var s = CoreSettings.Current;
                if (s != null && s.SpiralOpacity > 0) opacity = Math.Clamp(s.SpiralOpacity / 100.0, 0.05, 1.0);
            }
            catch { /* the default is fine */ }

            spiral(SpiralMs, opacity);
            EmiDeskBus.Fire("effectFired", new { channel = "spiral", fromAsk });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] spiral effect failed"); }
    }

    /// <summary>One local video, non-strict: she may not put the user in a mandatory watch.</summary>
    public static void FireVideo(bool fromAsk)
    {
        try
        {
            var play = Head?.PlayVideo;
            if (play == null) return;
            var path = RandomVideo();
            if (path == null)
            {
                Log.Debug("[EmiDesk] video effect skipped: no local videos");
                return;
            }
            play(path);
            EmiDeskBus.Fire("effectFired", new { channel = "video", fromAsk });
            EmiDeskBus.Fire("videoRunning", VideoCtx(path, fromAsk));
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] video effect failed"); }
    }

    /// <summary>
    /// The ctx a videoRunning moment rides on. <c>minutes</c> is present ONLY when the duration is
    /// genuinely known: she never claims a fake number (MOMENTS 3).
    /// </summary>
    public static object VideoCtx(string? path, bool fromAsk)
    {
        int? minutes = null;
        try
        {
            var m = Head?.VideoMinutes?.Invoke(path ?? string.Empty);
            if (m is > 0) minutes = m;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] video duration probe failed"); }

        var target = DisplayName(path);
        return minutes.HasValue
            ? new { target, minutes = minutes.Value, fromAsk }
            : (object)new { target, fromAsk };
    }

    /// <summary>A short burst of flashes, the Flashes tab's own one-shot path.</summary>
    public static void FireBurst(bool fromAsk)
    {
        try
        {
            var burst = Head?.Burst;
            if (burst == null) return;
            int? duration = null;
            try
            {
                var s = CoreSettings.Current;
                if (s != null && s.FlashDuration > 0) duration = s.FlashDuration;
            }
            catch { /* null lets the service use the user's own defaults */ }

            burst(duration);
            EmiDeskBus.Fire("effectFired", new { channel = "burst", fromAsk });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] burst effect failed"); }
    }

    /// <summary>Gif rain for ten seconds.</summary>
    public static void FireRain(bool fromAsk)
    {
        try
        {
            var rain = Head?.Rain;
            if (rain == null) return;
            rain(RainDuration);
            EmiDeskBus.Fire("effectFired", new { channel = "rain", fromAsk });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] rain effect failed"); }
    }

    /// <summary>
    /// Bedtime: offers go quiet until 06:00 local. It NEVER closes the app and never says do not
    /// go (BRIEF 7, MOMENTS 3.7); it only stops her asking for more.
    /// </summary>
    public static void SetBedtime(bool fromAsk)
    {
        try
        {
            var now = DateTime.Now;
            var six = now.Date.AddHours(6);
            if (now >= six) six = six.AddDays(1);
            EmiState.Current.BedtimeUntil = six.ToUniversalTime();
            EmiState.SaveSoon();
            Log.Information("[EmiDesk] bedtime set until {Until:t} local", six);
            EmiDeskBus.Fire("bedtimeSet", new { fromAsk });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] bedtime effect failed"); }
    }

    // ---------------------------------------------------------------- the libraries

    /// <summary>Test seam: the assets root (default <c>CorePaths.EffectiveAssets</c>).</summary>
    internal static Func<string?> AssetsRoot { get; set; } = () => CorePaths.EffectiveAssets;

    private static string? Sub(string name)
    {
        try
        {
            var root = AssetsRoot();
            if (string.IsNullOrEmpty(root)) return null;
            var p = Path.Combine(root, name);
            return Directory.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    /// <summary>The user's images folder, or null when it is not there.</summary>
    public static string? ImagesDir => Sub("images");

    /// <summary>The user's videos folder, or null when it is not there.</summary>
    public static string? VideosDir => Sub("videos");

    /// <summary>True when there is at least one local image or gif to show.</summary>
    public static bool HasImages() => Images().Count > 0;

    /// <summary>True when there is at least one local video to play.</summary>
    public static bool HasVideos() => Videos().Count > 0;

    // Cached for a minute: a folder walk per draw on a big library is real work for a decoration.
    private static List<string> _images = new();
    private static List<string> _videos = new();
    private static DateTime _imagesAt = DateTime.MinValue;
    private static DateTime _videosAt = DateTime.MinValue;
    private static readonly TimeSpan CacheLife = TimeSpan.FromMinutes(1);

    /// <summary>Drop the minute cache (tests, and a library the user just filled).</summary>
    public static void ForgetLibrary()
    {
        _imagesAt = DateTime.MinValue;
        _videosAt = DateTime.MinValue;
    }

    /// <summary>Every local image / gif, cached for a minute.</summary>
    public static IReadOnlyList<string> Images()
    {
        try
        {
            if (DateTime.UtcNow - _imagesAt < CacheLife) return _images;
            _imagesAt = DateTime.UtcNow;
            var dir = ImagesDir;
            _images = dir == null ? new List<string>() : Scan(dir, ImageExts);
            return _images;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] image scan failed");
            return _images;
        }
    }

    /// <summary>Every local video, cached for a minute.</summary>
    public static IReadOnlyList<string> Videos()
    {
        try
        {
            if (DateTime.UtcNow - _videosAt < CacheLife) return _videos;
            _videosAt = DateTime.UtcNow;
            var dir = VideosDir;
            _videos = dir == null ? new List<string>() : Scan(dir, VideoExts);
            return _videos;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] video scan failed");
            return _videos;
        }
    }

    private static List<string> Scan(string dir, string[] exts)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Take(4000)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] scan of {Dir} failed", dir);
            return new List<string>();
        }
    }

    /// <summary>One random local video path, or null.</summary>
    public static string? RandomVideo()
    {
        var v = Videos();
        return v.Count == 0 ? null : v[Rng.Next(v.Count)];
    }

    /// <summary>A file name a line can speak: no path, no extension, lowercase, never empty.</summary>
    public static string DisplayName(string? path)
    {
        try
        {
            var n = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(n)) return "that one";
            n = n.Replace('_', ' ').Replace('-', ' ').Trim();
            if (n.Length > 28) n = n.Substring(0, 28).Trim();
            return n.Length == 0 ? "that one" : n.ToLowerInvariant();
        }
        catch { return "that one"; }
    }
}
