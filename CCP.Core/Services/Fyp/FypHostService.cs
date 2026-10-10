using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// The head-free half of WPF 7.1.5 Services/Fyp/FypHostService.cs: what the For You page is told on
/// init, what each of its settings frames may change, the remote-fetch gate, the clip XP cap, the
/// stats file and the sub library. The window and the frame dispatch are the head's
/// (CCP.Avalonia Views/Games/GameWindow.Fyp.cs).
///
/// <para>BRIGHT LINE: the online feed goes straight from this machine to the provider. Nothing on
/// this path may involve CC Labs servers, and nothing is fetched unless <see cref="RemoteAllowed"/>.</para>
/// </summary>
internal static class FypHostService
{
    public const int MaxClipXpPerMinute = 12;
    public const int ClipXp = 5;
    public const int AttentionXp = 15;

    /// <summary>Head seams (the window). Unseeded: nothing opens.</summary>
    public static volatile Action? LaunchProvider;
    public static volatile Func<bool>? IsActiveProvider;
    public static volatile Action? CloseProvider;

    public static bool IsActive
    {
        get { try { return IsActiveProvider?.Invoke() == true; } catch { return false; } }
    }

    /// <summary>Open the feed window (the head refocuses a live one).</summary>
    public static void Launch()
    {
        var open = LaunchProvider;
        if (open == null) { Log.Debug("FypHost: no window on this head"); return; }
        open();
    }

    public static void Close()
    {
        try { CloseProvider?.Invoke(); }
        catch (Exception ex) { Log.Debug("FypHost.Close: {E}", ex.Message); }
    }

    /// <summary>Test seam: the stats file (default: fyp_stats.json under UserData).</summary>
    internal static string? StatsFilePathOverride;

    private static string StatsFilePath => StatsFilePathOverride ?? Path.Combine(CorePaths.UserData, "fyp_stats.json");

    public static bool IsRemoteId(string? id) =>
        id != null && id.StartsWith("scrolller/", StringComparison.Ordinal);

    /// <summary>
    /// The feed's effective content source: "library", "online" or "mixed". The feed's own picker
    /// wins whenever it has left "library"; otherwise the app-wide source carries over, gated on the
    /// same consent every other surface reads.
    /// </summary>
    public static string EffectiveFeedSource(AppSettings? s)
    {
        if (s == null) return "library";
        if (s.FypSource != "library") return s.FypSource;
        if (s.MediaSource != "local" && s.HasRemoteMediaConsent) return s.MediaSource;
        return "library";
    }

    /// <summary>The mixed-mode remote share the effective source implies.</summary>
    public static int EffectiveOnlineRatio(AppSettings? s)
    {
        if (s == null) return 30;
        return s.FypSource != "library" ? s.FypOnlineRatio : s.RemoteMediaRatio;
    }

    /// <summary>May the feed fetch remote content right now? Consent AND a non-library source.</summary>
    public static bool RemoteAllowed(AppSettings? s) =>
        s != null && s.HasRemoteMediaConsent && EffectiveFeedSource(s) != "library";

    // ---- clip XP cap ------------------------------------------------------------------------

    private static readonly Queue<DateTime> _clipXpTimes = new();

    /// <summary>At most <see cref="MaxClipXpPerMinute"/> paid clips in any rolling minute.</summary>
    public static bool AllowClipXp() => AllowClipXp(DateTime.UtcNow);

    internal static bool AllowClipXp(DateTime nowUtc)
    {
        lock (_clipXpTimes)
        {
            while (_clipXpTimes.Count > 0 && (nowUtc - _clipXpTimes.Peek()).TotalSeconds > 60)
                _clipXpTimes.Dequeue();
            if (_clipXpTimes.Count >= MaxClipXpPerMinute) return false;
            _clipXpTimes.Enqueue(nowUtc);
            return true;
        }
    }

    internal static void ResetClipXpForTests() { lock (_clipXpTimes) _clipXpTimes.Clear(); }

    // ---- init -------------------------------------------------------------------------------

    /// <summary>
    /// The one init payload on page-ready: assets + settings + the niche catalog and library + the
    /// persisted stats. <paramref name="eyeControl"/> is what the head can honour (false where it has
    /// no camera path), so the page never paints a toggle "on" that nothing drives.
    /// </summary>
    public static JObject BuildInit(AppSettings? s, IEnumerable<FypAssetManifest.Entry> assets, bool eyeControl)
    {
        var customSubs = (s?.FypOnlineCustomSubs ?? new List<string>()).Select(name =>
        {
            RemoteSubVerdict? v = null;
            s?.FypOnlineSubVerdicts.TryGetValue(name, out v);
            return new { name, ok = (bool?)v?.Ok, videoCount = v?.VideoCount };
        });
        return JObject.FromObject(new
        {
            type = "init",
            assets = assets.ToList(),
            settings = new
            {
                layout = s?.FypLayout ?? "duo",
                includeGifs = s?.FypIncludeGifs ?? true,
                mosaicAutoChange = s?.FypMosaicAutoChange ?? true,
                mosaicChangeSec = s?.FypMosaicChangeSec ?? 10,
                autoAdvance = s?.FypAutoAdvance ?? false,
                muted = s?.FypMuted ?? false,
                volume = s?.FypVolume ?? 100,
                windowOpacity = s?.FypWindowOpacity ?? 0.6,
                audioGlow = s?.FypAudioGlow ?? true,
                eyeControl = eyeControl && (s?.FypEyeControl ?? false),
                eyeGaze = eyeControl && (s?.FypEyeGaze ?? false),
                // Effective, not raw: the app-wide media source reaches the feed too.
                source = EffectiveFeedSource(s),
                onlineRatio = EffectiveOnlineRatio(s),
                // Either consent card (the feed's or the app-wide one) counts.
                onlineConsented = s?.HasRemoteMediaConsent ?? false,
            },
            online = new
            {
                niches = FypOnlineCoordinator.Catalog.Select(n => new
                {
                    id = n.Id,
                    label = n.Label,
                    subs = n.Subs,
                    selected = s?.FypOnlineNiches?.Contains(n.Id) ?? false,
                }),
                customSubs,
                library = BuildLibraryPayload(s),
            },
            stats = LoadStats(),
        });
    }

    // ---- settings-changed ---------------------------------------------------------------------

    /// <summary>What the head still has to do after <see cref="ApplySetting"/> stored a value.</summary>
    public enum SettingEffect
    {
        /// <summary>Stored (or ignored): nothing more.</summary>
        None,
        /// <summary>"clickThrough": ghost mode on / off. Session only, never stored.</summary>
        GhostOn, GhostOff,
        /// <summary>"eyeControl" / "eyeGaze": the camera path, then an eyeStatus frame.</summary>
        EyeControlOn, EyeControlOff, EyeGazeChanged,
        /// <summary>"windowOpacity": stored; the ghost mirror (if any) follows.</summary>
        OpacityChanged,
    }

    /// <summary>
    /// One <c>settings-changed</c> frame (WPF ApplySetting). Never trusts the page: an unknown key is
    /// ignored, a non-library source without consent falls back to library, niche ids must be in the
    /// catalog, custom subs are sanitized, de-duplicated and capped at 20, consent is one-way.
    /// </summary>
    public static SettingEffect ApplySetting(AppSettings? s, string? key, JToken? value)
    {
        if (key == null || value == null) return SettingEffect.None;
        if (key == "clickThrough")
        {
            bool on = false;
            try { on = (bool?)value ?? false; } catch { }
            return on ? SettingEffect.GhostOn : SettingEffect.GhostOff;
        }
        if (s == null) return SettingEffect.None;
        try
        {
            switch (key)
            {
                case "layout": s.FypLayout = (string?)value ?? "duo"; break;
                case "includeGifs": s.FypIncludeGifs = (bool?)value ?? true; break;
                case "mosaicAutoChange": s.FypMosaicAutoChange = (bool?)value ?? true; break;
                case "mosaicChangeSec": s.FypMosaicChangeSec = (int?)value ?? 10; break;
                case "autoAdvance": s.FypAutoAdvance = (bool?)value ?? false; break;
                case "muted": s.FypMuted = (bool?)value ?? false; break;
                case "volume": s.FypVolume = (int?)value ?? 100; break;   // clamped again by the property
                case "windowOpacity":
                    s.FypWindowOpacity = (double?)value ?? 1.0;
                    return SettingEffect.OpacityChanged;
                case "audioGlow": s.FypAudioGlow = (bool?)value ?? true; break;   // page-side visual: persist only
                case "eyeControl":
                {
                    bool on = (bool?)value ?? false;
                    s.FypEyeControl = on;
                    return on ? SettingEffect.EyeControlOn : SettingEffect.EyeControlOff;
                }
                case "eyeGaze":
                    s.FypEyeGaze = (bool?)value ?? false;
                    return SettingEffect.EyeGazeChanged;
                case "source":
                {
                    var src = (string?)value ?? "library";
                    // The page shows the consent card first; belt and braces for a stale page.
                    if (src != "library" && !s.HasRemoteMediaConsent) src = "library";
                    s.FypSource = src;
                    break;
                }
                case "onlineRatio": s.FypOnlineRatio = (int?)value ?? 30; break;
                case "onlineConsented":
                    // One-way: the page only ever sends true (accepting the card).
                    if ((bool?)value == true && !s.FypOnlineConsented)
                    {
                        s.FypOnlineConsented = true;
                        Log.Information("FypHost: online-content consent accepted");
                    }
                    break;
                case "onlineNiches":
                    if (value is JArray niches)
                    {
                        var known = new HashSet<string>(FypOnlineCoordinator.Catalog.Select(n => n.Id));
                        s.FypOnlineNiches = niches.Select(t => (string?)t)
                            .Where(id => id != null && known.Contains(id)).Select(id => id!)
                            .Distinct().ToList();
                        FypOnlineCoordinator.Fyp.ResetChannels();
                    }
                    break;
                case "onlineCustomSubs":
                    if (value is JArray subs)
                    {
                        s.FypOnlineCustomSubs = subs.Select(t => FypOnlineCoordinator.SanitizeSub((string?)t))
                            .Where(x => x != null).Select(x => x!)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
                        // The page's DESELECT path: drop verdicts nothing keeps any more. A name still
                        // in the LIBRARY is kept (deselecting a pill is not forgetting it).
                        foreach (var name in s.FypOnlineCustomSubs) s.TryAddLibrarySub(name);
                        var kept = new HashSet<string>(s.FypOnlineCustomSubs, StringComparer.OrdinalIgnoreCase);
                        foreach (var row in s.BuildRemoteSubLibraryView()) kept.Add(row.Name);
                        foreach (var gone in s.FypOnlineSubVerdicts.Keys.Where(k => !kept.Contains(k)).ToList())
                            s.FypOnlineSubVerdicts.Remove(gone);
                        FypOnlineCoordinator.Fyp.ResetChannels();
                    }
                    break;
            }
        }
        catch (Exception ex) { Log.Debug("FypHost: settings-changed {Key} failed: {E}", key, ex.Message); }
        return SettingEffect.None;
    }

    // ---- the sub library ----------------------------------------------------------------------

    /// <summary>The library joined with verdicts and with this surface's selection.</summary>
    public static object[] BuildLibraryPayload(AppSettings? s)
    {
        if (s == null) return Array.Empty<object>();
        var rows = s.BuildRemoteSubLibraryView();
        var payload = new List<object>(rows.Count);
        foreach (var r in rows)
            payload.Add(new { name = r.Name, ok = r.Ok, videoCount = r.VideoCount, stillOnly = r.StillOnly, selected = r.Selected });
        return payload.ToArray();
    }

    /// <summary>The whole library, replace never patch (the frame after any change).</summary>
    public static JObject LibraryFrame(AppSettings? s) =>
        JObject.FromObject(new { type = "library", library = BuildLibraryPayload(s) });

    /// <summary>The X on a pill: the entry, its verdict and its feed membership go together, then
    /// every consumer's rotation resets. True when something was removed.</summary>
    public static bool RemoveLibrarySub(AppSettings? s, string? rawName)
    {
        try
        {
            if (s == null || string.IsNullOrWhiteSpace(rawName)) return false;
            if (!s.RemoveLibrarySub(rawName)) return false;
            CoreSettings.Save();
            FypOnlineCoordinator.ResetAllChannels();
            Log.Information("[FYP online] r/{Sub} removed from the library", rawName!.Trim());
            return true;
        }
        catch (Exception ex) { Log.Warning("FypHost: library remove failed: {E}", ex.Message); return false; }
    }

    /// <summary>
    /// A finished probe of a typed subreddit (WPF ProbeCustomSub's UI half). A transport failure
    /// taught us nothing, so no verdict is written; any real answer is remembered, and a found sub
    /// is kept in the library and added to the feed (20 at most). Returns the sub-probe frame.
    /// </summary>
    public static JObject CommitProbe(AppSettings? s, string clean, SubProbe probe)
    {
        if (s != null && probe.Error == null)
        {
            s.FypOnlineSubVerdicts[clean] = new RemoteSubVerdict
            {
                Ok = probe.Ok,
                VideoCount = probe.VideoCount,
                CheckedAtUtc = DateTime.UtcNow,
            };
            if (probe.Ok)
            {
                s.TryAddLibrarySub(clean);
                var subs = s.FypOnlineCustomSubs.ToList();
                if (!subs.Contains(clean, StringComparer.OrdinalIgnoreCase) && subs.Count < 20)
                {
                    subs.Add(clean);
                    s.FypOnlineCustomSubs = subs;
                    // The sub list is app-wide (flashes, videos, intake, DTRH all resolve from it).
                    FypOnlineCoordinator.ResetAllChannels();
                    Log.Information("[FYP online] custom sub r/{Sub} verified ({N} videos) and added", clean, probe.VideoCount);
                }
            }
            CoreSettings.Save();
        }
        return JObject.FromObject(new { type = "sub-probe", sub = clean, ok = probe.Ok, videoCount = probe.VideoCount, error = probe.Error });
    }

    /// <summary>The frame for a name that is not a subreddit at all (echoes the raw text back).</summary>
    public static JObject InvalidProbeFrame(string? rawSub) =>
        JObject.FromObject(new { type = "sub-probe", sub = rawSub ?? "", ok = false, videoCount = (int?)null, error = "invalid" });

    /// <summary>The two frames after a remote batch: the entries (when any), then the status.</summary>
    public static IEnumerable<JObject> BatchFrames(FypOnlineCoordinator.FeedBatch batch)
    {
        int count = batch.Entries?.Count ?? 0;
        if (count > 0) yield return JObject.FromObject(new { type = "assets-append", assets = batch.Entries });
        // "added" is the FRESH count: reporting the raw page kept the page's dry backoff from arming.
        yield return JObject.FromObject(new
        {
            type = "online-status",
            ok = batch.Error == null,
            error = batch.Error,
            added = count,
            fresh = count,
            dry = batch.Dry,
            poolTotal = batch.PoolTotal,
        });
    }

    // ---- stats --------------------------------------------------------------------------------

    public static JObject? LoadStats()
    {
        try
        {
            if (File.Exists(StatsFilePath))
                return JObject.Parse(File.ReadAllText(StatsFilePath));
        }
        catch (Exception ex) { Log.Warning("FypHost: stats load failed: {E}", ex.Message); }
        return null;
    }

    /// <summary>The page owns the stats shape; the blob is persisted verbatim.</summary>
    public static void SaveStats(JObject stats)
    {
        try
        {
            var path = StatsFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, stats.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { Log.Warning("FypHost: stats save failed: {E}", ex.Message); }
    }
}
