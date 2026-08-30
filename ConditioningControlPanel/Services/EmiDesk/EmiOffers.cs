using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The effects an offer (or a glass tap) can fire, plus the two library probes both of them need.
///
/// Feasibility is checked at DRAW time and fails SILENTLY (LINES-SCHEMA 4): an offer to play a
/// video on a machine with no videos is never shown at all, rather than shown and then fizzling.
/// That is also why <see cref="EffectFeasible"/> is called from the line engine and not from the
/// window: the engine has to be able to drop the ask before the user ever sees it.
///
/// Nothing in here asks the network. Local assets only, unless the user has ALREADY granted the
/// app-wide remote-media consent, in which case the existing services do their own remote work
/// through their own helpers; this file never fetches anything itself.
/// </summary>
public static class EmiOffers
{
    private static readonly string[] ImageExts = { ".gif", ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    private static readonly string[] VideoExts = { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv", ".m4v" };

    private static readonly Random Rng = new();

    /// <summary>How long the gif rain runs when she fires it (BRIEF 6).</summary>
    public static readonly TimeSpan RainDuration = TimeSpan.FromSeconds(10);

    /// <summary>How long the spiral overlay sits when she fires it (BRIEF 6).</summary>
    public const int SpiralMs = 6000;

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
            if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase))
                return App.EmiDesk?.IsTargetAvailable(e.Substring(5)) == true;

            if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase))
            {
                var id = e.Substring(7);
                if (App.EmiDesk?.IsTargetAvailable(id) != true) return false;
                // Pinning what is already pinned is a chip that does nothing. Do not offer it.
                try { if (EmiState.Current.Pins.Contains(id, StringComparer.OrdinalIgnoreCase)) return false; }
                catch { /* no state, treat as unpinned */ }
                return true;
            }

            if (e.StartsWith("tour:", StringComparison.OrdinalIgnoreCase))
                return TourFeasible(e.Substring(5));

            return e.ToLowerInvariant() switch
            {
                "none" => true,
                "spiral" => App.Overlay != null,
                "video" => HasVideos(),
                "rain" => HasImages(),
                "burst" => HasImages(),
                "shrink" => CanShrink(),
                "bedtime" => !EmiLineEngine.BedtimeSet,
                _ => false
            };
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] effect feasibility probe failed for {Effect}", effect);
            return false;
        }
    }

    // ---------------------------------------------------------------- the tours (wave 1)

    /// <summary>The <c>tour:</c> verb for the short walk, and the <c>TutorialType</c> behind it.</summary>
    private const string ShortWalkVerb = "shortwalk";

    /// <inheritdoc cref="ShortWalkVerb"/>
    private const string UpgradeVerb = "upgrade";

    /// <summary>
    /// Can this tour actually run, right now, for this user?
    ///
    /// <para>Checked at DRAW time and failing SILENTLY (LINES-SCHEMA 4) is the whole point: an
    /// infeasible tour means the ask is never put on the glass at all. The alternative - showing
    /// "show me around?" and then doing nothing when it is pressed - is a dead chip, and a dead
    /// chip on the very first thing she ever says is the worst possible first impression.</para>
    ///
    /// <para>Feasible means all four: the main window is alive, no session is running, no tutorial
    /// overlay is already up, and this tour is not already latched in
    /// <see cref="EmiState.ToursDone"/>. The last is brake 4 of the knock, stated a second time on
    /// the effect side, because an ask can be authored against any moment and the ceiling has to
    /// hold wherever it is reached from.</para>
    /// </summary>
    private static bool TourFeasible(string? verb)
    {
        try
        {
            var tour = TourNameOf(verb);
            if (tour == null) return false;

            if (Application.Current?.MainWindow is not MainWindow) return false;
            if (SessionEngine.Active?.IsRunning == true) return false;
            if (App.Tutorial?.IsActive == true) return false;
            if (EmiState.HasTourDone(tour)) return false;

            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] tour feasibility probe failed for {Verb}", verb);
            return false;
        }
    }

    /// <summary>
    /// The <c>TutorialType</c> NAME a verb maps to, or null for a verb nothing knows. A name
    /// rather than the enum so the probe above and <see cref="EmiState.ToursDone"/> agree by
    /// construction - the ledger persists names, and an ordinal would move the day somebody
    /// inserted a value into the middle of the enum.
    /// </summary>
    private static string? TourNameOf(string? verb) => (verb ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        ShortWalkVerb => EmiKnockMachine.ShortWalkTour,
        UpgradeVerb => EmiKnockMachine.UpgradeTour,
        _ => null
    };

    /// <summary>
    /// Say yes: run the tour.
    ///
    /// <para>Also spends the knock. They answered - brake 1 - and whether or not they see it
    /// through to the last card, she is not to ask again.</para>
    /// </summary>
    private static void StartTour(string? verb, bool fromAsk)
    {
        try
        {
            var v = (verb ?? string.Empty).Trim().ToLowerInvariant();
            if (Application.Current?.MainWindow is not MainWindow main)
            {
                Log.Debug("[EmiDesk] tour effect skipped: no main window");
                return;
            }

            // The knock is spent the moment they say yes, before the tour is even started: the
            // tutorial overlay owns the screen from here and an exception on the way in must not
            // leave the chip free to knock about it all over again next launch.
            EmiState.NoteKnockAnswered();

            switch (v)
            {
                case ShortWalkVerb:
                    main.StartTutorial(TutorialType.ShortWalk);
                    break;
                case UpgradeVerb:
                    main.StartTutorial(TutorialType.UpgradeTour);
                    break;
                default:
                    Log.Debug("[EmiDesk] unknown tour verb {Verb}, ignored", v);
                    return;
            }

            App.EmiDesk?.Fire("effectFired", new { channel = "tour", fromAsk });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] tour effect {Verb} failed", verb);
        }
    }

    private static bool CanShrink()
    {
        try
        {
            var w = App.EmiDesk?.Window;
            return w != null && w.BodyWidth > EmiDeskWindow.MinBodyWidth + 1;
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- firing

    /// <summary>
    /// Run an effect. Everything here is best-effort and logged: a failed effect must never take
    /// the widget down, because the widget is the only way back out of most of them.
    ///
    /// <paramref name="fromAsk"/> rides into the moment the effect raises, and the engine returns
    /// without speaking when it sees it (LINES-SCHEMA 5.6): the offer's own reaction already
    /// spoke, and two lines about one thing is the failure mode.
    /// </summary>
    public static void Run(string? effect, bool fromAsk)
    {
        if (string.IsNullOrWhiteSpace(effect)) return;
        var e = effect!.Trim();
        try
        {
            if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase))
            {
                App.EmiDesk?.OpenTarget(e.Substring(5));
                return;
            }
            if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase))
            {
                App.EmiDesk?.PinTop(e.Substring(7));
                return;
            }
            if (e.StartsWith("tour:", StringComparison.OrdinalIgnoreCase))
            {
                StartTour(e.Substring(5), fromAsk);
                return;
            }

            switch (e.ToLowerInvariant())
            {
                case "none": return;
                case "spiral": FireSpiral(fromAsk); return;
                case "video": FireVideo(fromAsk); return;
                case "burst": FireBurst(fromAsk); return;
                case "rain": FireRain(fromAsk); return;
                case "shrink": FireShrink(); return;
                case "bedtime": SetBedtime(fromAsk); return;
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

    /// <summary>The app's own spiral overlay, at the user's own spiral opacity, for six seconds.</summary>
    public static void FireSpiral(bool fromAsk)
    {
        try
        {
            double opacity = 0.85;
            try
            {
                var s = App.Settings?.Current;
                if (s != null && s.SpiralOpacity > 0) opacity = Math.Clamp(s.SpiralOpacity / 100.0, 0.05, 1.0);
            }
            catch { /* the default is fine */ }

            App.Overlay?.ShowOverlayTimed("spiral", SpiralMs, opacity);
            App.EmiDesk?.Fire("effectFired", new { channel = "spiral", fromAsk });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] spiral effect failed"); }
    }

    /// <summary>
    /// One local video, played through the app's own player, non-strict (she is not allowed to put
    /// the user in a mandatory watch they did not ask for). She goes on top of it and says her
    /// piece; the ended moment rides the service's own event, wired in EmiDeskWindow.Glass.
    /// </summary>
    public static void FireVideo(bool fromAsk)
    {
        try
        {
            var path = RandomVideo();
            if (path == null)
            {
                Log.Debug("[EmiDesk] video effect skipped: no local videos");
                return;
            }
            App.Video?.PlaySpecificVideo(path, false);
            ReassertTopmost();
            App.EmiDesk?.Fire("effectFired", new { channel = "video", fromAsk });
            App.EmiDesk?.Fire("videoRunning", VideoCtx(path, fromAsk));
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] video effect failed"); }
    }

    /// <summary>
    /// The ctx a videoRunning moment rides on.
    ///
    /// <c>minutes</c> is present ONLY when the duration is genuinely known (a warm metadata cache).
    /// She never claims a fake number (MOMENTS 3): with the key missing, every line carrying the
    /// <c>{minutes}</c> token is skipped at draw time and one of the pool's plain siblings speaks
    /// instead. Rounded UP and floored at 1, because "0 minutes" is not a thing anyone says.
    /// </summary>
    internal static object VideoCtx(string? path, bool fromAsk)
    {
        int? minutes = null;
        try
        {
            var secs = App.Video?.MetadataCache?.TryGetDuration(path ?? string.Empty);
            if (secs is > 0) minutes = Math.Max(1, (int)Math.Ceiling(secs.Value / 60.0));
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
            int? duration = null;
            try
            {
                var s = App.Settings?.Current;
                if (s != null && s.FlashDuration > 0) duration = s.FlashDuration;
            }
            catch { /* null lets the service use the user's own defaults */ }

            // size stays null on purpose: the flash service then uses the user's configured size,
            // which is the one thing about a burst they have already tuned for themselves.
            App.Flash?.TriggerFlashOnce(4, duration, null, true);
            App.EmiDesk?.Fire("effectFired", new { channel = "burst", fromAsk });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] burst effect failed"); }
    }

    /// <summary>Gif rain, the Chaos cascade's own renderer, for ten seconds.</summary>
    public static void FireRain(bool fromAsk)
    {
        try
        {
            EmiGifRain.Start(RainDuration);
            App.EmiDesk?.Fire("effectFired", new { channel = "rain", fromAsk });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] rain effect failed"); }
    }

    /// <summary>Shrink her to the minimum and snap her to the nearest corner of her work area.</summary>
    public static void FireShrink()
    {
        try
        {
            var win = App.EmiDesk?.Window;
            if (win == null) return;
            win.ApplyBodyWidth(EmiDeskWindow.MinBodyWidth);
            win.SnapToNearestCorner();
            win.SavePlacement();
            var s = App.Settings?.Current;
            if (s != null && Math.Abs(s.EmiDeskWidth - EmiDeskWindow.MinBodyWidth) > 0.5)
            {
                s.EmiDeskWidth = EmiDeskWindow.MinBodyWidth;
                App.Settings?.Save();
            }
            App.EmiDesk?.Fire("resized", new { n = (int)EmiDeskWindow.MinBodyWidth, bigger = false });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] shrink effect failed"); }
    }

    /// <summary>
    /// Bedtime: offers and glass channels go quiet until 06:00 local. It NEVER closes the app and
    /// never says do not go (BRIEF 7, MOMENTS 3.7); it only stops her asking for more.
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
            App.EmiDesk?.Fire("bedtimeSet", new { fromAsk });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] bedtime effect failed"); }
    }

    /// <summary>
    /// Put her back on top after another window took the z-order (the video player does). Not a
    /// loop and not a timer: one re-assert on the next dispatcher pass is enough and anything more
    /// would fight the user's own alt-tab.
    /// </summary>
    public static void ReassertTopmost()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(new Action(() =>
            {
                try
                {
                    var win = App.EmiDesk?.Window;
                    if (win == null || win.Visibility != Visibility.Visible) return;
                    win.Topmost = false;
                    win.Topmost = true;
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] topmost re-assert failed"); }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] topmost re-assert schedule failed"); }
    }

    // ---------------------------------------------------------------- the libraries

    /// <summary>The user's images folder, or null when it is not there.</summary>
    public static string? ImagesDir
    {
        get
        {
            try
            {
                var p = Path.Combine(App.EffectiveAssetsPath, "images");
                return Directory.Exists(p) ? p : null;
            }
            catch { return null; }
        }
    }

    /// <summary>The user's videos folder, or null when it is not there.</summary>
    public static string? VideosDir
    {
        get
        {
            try
            {
                var p = Path.Combine(App.EffectiveAssetsPath, "videos");
                return Directory.Exists(p) ? p : null;
            }
            catch { return null; }
        }
    }

    /// <summary>True when there is at least one local image or gif to show.</summary>
    public static bool HasImages() => Images().Count > 0;

    /// <summary>True when there is at least one local video to play.</summary>
    public static bool HasVideos() => Videos().Count > 0;

    // The lists are cached for a minute: the glass asks on every flip and a folder walk per flip
    // on a big library is real work for a decoration.
    private static List<string> _images = new();
    private static List<string> _videos = new();
    private static DateTime _imagesAt = DateTime.MinValue;
    private static DateTime _videosAt = DateTime.MinValue;
    private static readonly TimeSpan CacheLife = TimeSpan.FromMinutes(1);

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

    /// <summary>Up to <paramref name="n"/> distinct random local image paths.</summary>
    public static List<string> RandomImages(int n)
    {
        var all = Images();
        var picks = new List<string>();
        if (all.Count == 0) return picks;
        var used = new HashSet<int>();
        int guard = 0;
        while (picks.Count < n && used.Count < all.Count && guard++ < n * 8)
        {
            int i = Rng.Next(all.Count);
            if (!used.Add(i)) continue;
            picks.Add(all[i]);
        }
        return picks;
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
