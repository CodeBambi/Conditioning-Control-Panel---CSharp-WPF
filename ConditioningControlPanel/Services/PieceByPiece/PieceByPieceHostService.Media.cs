using System;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

// ============================ online pictures (2026-09-28) ============================
//
// Distraction mode used to draw ONLY from the player's own library. It now takes Scrolller
// pictures too, through the Goon Game's pool (GoonOnlineMedia.ForGame("pbp")): stills and clips
// materialised under {assets}\.temp, which ccp.assets already maps, so every one is a plain
// https://ccp.assets/.temp/... url the page can draw. The consent gate and the niche choice are
// PbpMediaRules; the flavour table and the niche editing are the Goon page's ui/flavours.js.
//
// The page keeps the online set BESIDE its local deck and mixes them at `share` percent (see
// ramp/media.js). The online set is replaced whole on every frame, so a picture the pool retires
// leaves the page too; the page asks for the next wave (`pbp:media-more`) once most of the set
// has been on screen.
internal static partial class PieceByPieceHostService
{
    private const string OnlineTenant = "pbp";

    private static GoonOnlineMedia? _onlineMedia;
    /// <summary>The player picked a flavour inside the game this window. Never stored as consent.</summary>
    private static bool _sessionOptIn;
    private static int _onlineSharePct = PbpMediaRules.PickedSharePct;

    /// <summary>host -&gt; page <c>pbp:media-state</c>: what the picker shows. <c>flavour</c> is the
    /// ACTIVE pick ("" until the player picks this session, unless the app-wide online source is
    /// consented), <c>last</c> the stored preselection.</summary>
    private static void PostMediaState()
    {
        try
        {
            var s = App.Settings?.Current;
            var stored = GoonOnlineMediaRules.CleanFlavour(s?.PbpMediaFlavour);
            bool appWide = PbpMediaRules.AppWideOnline(s?.MediaSource, s?.HasRemoteMediaConsent == true);
            _host?.Post(new
            {
                type = "pbp:media-state",
                flavour = _sessionOptIn || appWide ? stored : "",
                last = stored,
                custom = GoonOnlineMediaRules.ParseCustom(s?.PbpMediaCustom),
                online = s?.PbpMediaOnline ?? true,
                appWide,
            });
        }
        catch (Exception ex) { App.Logger?.Debug("PieceByPiece: media-state post failed: {E}", ex.Message); }
    }

    /// <summary>page -&gt; host <c>pbp:media-flavour { flavour, custom, subs, online }</c>: store the
    /// pick, make it this window's opt-in, and restart the fetch. <c>online:false</c> is the
    /// player's "Own pictures only".</summary>
    private static void OnMediaFlavour(JObject o)
    {
        var s = App.Settings?.Current;
        if (s == null) return;
        var flavour = GoonOnlineMediaRules.CleanFlavour((string?)o["flavour"]);
        var subs = GoonOnlineMediaRules.CleanSubs(
            (o["subs"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string?)t : null));
        s.PbpMediaFlavour = flavour;
        if (o["custom"] is JObject) s.PbpMediaCustom = GoonOnlineMediaRules.CleanCustom(o["custom"]);
        s.PbpMediaSubs = GoonOnlineMediaRules.JoinSubs(subs);
        if (o["online"]?.Type == JTokenType.Boolean) s.PbpMediaOnline = (bool)o["online"]!;
        _sessionOptIn = GoonOnlineMediaRules.IsSessionOptIn(s.PbpMediaOnline, flavour);
        try { App.Settings?.Save(); } catch (Exception ex) { App.Logger?.Debug("PieceByPiece: media save: {E}", ex.Message); }
        App.Logger?.Information("PieceByPiece: media-flavour {F} ({N} niches, online {O})",
            flavour == "" ? "(none)" : flavour, subs.Count, s.PbpMediaOnline);
        PostMediaState();
        StartOnlineMedia();
    }

    /// <summary>page -&gt; host <c>pbp:media-more</c>: the next wave for the same niches.</summary>
    private static void OnMediaMore()
    {
        if (_onlineMedia?.More() == true) App.Logger?.Debug("PieceByPiece: media-more, next wave");
    }

    /// <summary>Fetch (or stop) from what is stored and consented. Nothing allowed = post 'off' and
    /// hold nothing: the page then deals from the local library alone, as before.</summary>
    private static void StartOnlineMedia()
    {
        try
        {
            if (_host == null) return;
            var s = App.Settings?.Current;
            bool consent = s?.HasRemoteMediaConsent == true;
            _onlineSharePct = PbpMediaRules.SharePct(s?.MediaSource, s?.RemoteMediaRatio ?? 30, _sessionOptIn);
            var subs = PbpMediaRules.ChannelsFor(s?.PbpMediaOnline ?? true, _sessionOptIn, s?.MediaSource, consent,
                s?.PbpMediaFlavour, GoonOnlineMediaRules.SplitSubs(s?.PbpMediaSubs),
                FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs));
            if (subs.Count == 0)
            {
                if (_onlineMedia != null) _onlineMedia.Off();
                else PostOnlineMedia(new GoonOnlineMedia.Snapshot("off", Array.Empty<string>(),
                    Array.Empty<GoonOnlineMedia.Item>(), Array.Empty<GoonOnlineMedia.Item>(), 0, 0));
                return;
            }
            _onlineMedia ??= GoonOnlineMedia.ForGame(OnlineTenant, PostOnlineMedia);
            _onlineMedia.Start(subs);
        }
        catch (Exception ex) { App.Logger?.Warning("PieceByPiece: online media start failed: {E}", ex.Message); }
    }

    /// <summary>Worker thread -&gt; UI thread -&gt; page <c>pbp:online-media</c>. The whole current
    /// list every time: stills as <c>images</c>, gif clips (webm/mp4) as <c>clips</c>.</summary>
    private static void PostOnlineMedia(GoonOnlineMedia.Snapshot snap)
    {
        var frame = new
        {
            type = "pbp:online-media",
            state = snap.State,
            subs = snap.Subs,
            share = _onlineSharePct,
            images = snap.Images.Select(i => i.Url).ToList(),
            clips = snap.Videos.Select(i => i.Url).ToList(),
            have = snap.Have,
            want = snap.Want,
        };
        RunOnUi(() =>
        {
            try { _host?.Post(frame); } catch (Exception ex) { Diag.Swallowed(ex); }
        });
    }

    /// <summary>Close: stop fetching and hand back every temp file this window owned.</summary>
    private static void DisposeOnlineMedia()
    {
        try { _onlineMedia?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _onlineMedia = null;
        _sessionOptIn = false;
    }
}
