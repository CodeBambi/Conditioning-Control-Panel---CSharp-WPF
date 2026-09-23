using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.FriendsWindows;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// What a delivery does when it arrives: a poke goes through Emi and a floating word (a toast
/// when a game has the screen), an invite or a watch knocks with a card. The rules live in
/// <see cref="LandingRules"/> and <see cref="FriendsLandingRouter"/>; this class reads the world,
/// draws, and runs the button the player pressed. Nothing here ever plays or joins by itself.
///
/// <para><see cref="Start"/> is called once at startup. <c>App.Friends</c> can be null (signed
/// out, or not built yet) and can be replaced on sign in / out, so a light timer re-attaches to
/// whichever instance is current and releases the held queue once a hold ends.</para>
/// </summary>
public static class FriendsLanding
{
    private static DispatcherTimer? _timer;
    private static FriendsLandingRouter? _router;
    private static readonly Sink TheSink = new();

    public static void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    /// <summary>Exit path. Closes every landing window so none can hold the process open.</summary>
    public static void Stop()
    {
        try { _timer?.Stop(); } catch { /* shutting down */ }
        _timer = null;
        _router?.Dispose();
        _router = null;
        try { KnockCard.CloseAll(); } catch { /* shutting down */ }
        try { FriendToast.CloseAll(); } catch { /* shutting down */ }
    }

    private static void Tick()
    {
        try
        {
            var current = App.Friends;
            if (!ReferenceEquals(_router?.Service, current))
            {
                _router?.Dispose();
                _router = current == null
                    ? null
                    : new FriendsLandingRouter(current, ReadWorld, () => DateTimeOffset.UtcNow, TheSink);
            }
            _router?.Release();
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] landing tick: {E}", ex.Message); }
    }

    // ---------------------------------------------------------------- the world

    private static LandingWorld ReadWorld()
    {
        var s = App.Settings?.Current;
        var session = App.IsSessionRunning;
        bool lockdown = false, program = false;
        try { lockdown = App.Lockdown?.IsActive == true; } catch { /* not built yet */ }
        try { program = session && App.Programs?.ActiveEnrollment != null; } catch { /* not built yet */ }

        var mw = App.MainWindowRef;
        var panel = mw is { IsVisible: true } && mw.WindowState != WindowState.Minimized;
        var launcher = LauncherWindow() is { IsVisible: true } lw && lw.WindowState != WindowState.Minimized;

        return new LandingWorld(
            Lockdown: lockdown,
            StrictLock: session && s?.StrictLockEnabled == true,
            ProgramSession: program,
            PanelVisible: panel,
            LauncherVisible: launcher,
            GameHostActive: ActiveGameWindow() != null);
    }

    private static Window? LauncherWindow()
    {
        try
        {
            return Application.Current?.Windows.OfType<ConditioningControlPanel.Launcher.LauncherWindow>().FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>The active window when it belongs to a game host (Back Room, race, goon), else null.
    /// The hosts do not expose their windows, so it is the active window that is neither the panel,
    /// the launcher nor one of ours, while a host is up.</summary>
    private static Window? ActiveGameWindow()
    {
        bool anyHost;
        try
        {
            anyHost = BackRoom.BackRoomHostService.IsActive
                || Chaos.CaucusHostService.IsActive
                || GoonGame.GoonHostService.IsActive;
        }
        catch { return null; }
        if (!anyHost) return null;
        try
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (!w.IsActive) continue;
                if (ReferenceEquals(w, App.MainWindowRef)) return null;
                if (w is ConditioningControlPanel.Launcher.LauncherWindow) return null;
                if (w is KnockCard or FriendToast or FloatingWord) return null;
                return w;
            }
        }
        catch { /* windows collection changed mid-walk */ }
        return null;
    }

    /// <summary>Where a card or a word lands when there is no game: the panel, else the launcher.</summary>
    private static Window? Anchor()
    {
        var mw = App.MainWindowRef;
        if (mw is { IsVisible: true } && mw.WindowState != WindowState.Minimized) return mw;
        var lw = LauncherWindow();
        if (lw is { IsVisible: true } && lw.WindowState != WindowState.Minimized) return lw;
        return null;
    }

    // ---------------------------------------------------------------- words

    internal static string Str(string key, string english)
    {
        try
        {
            var v = Loc.Get(key);
            return string.IsNullOrWhiteSpace(v) || v == key ? english : v;
        }
        catch { return english; }
    }

    internal static string PokeText(string? pokeId)
        => Str("friends_poke_" + pokeId, LandingRules.PokeFallback(pokeId));

    private static string DestinationName(string? dest) => dest switch
    {
        InviteDestination.Goon => Str("friends_land_dest_goon", "the Goon Game"),
        InviteDestination.Remote => Str("friends_land_dest_remote", "Remote Control"),
        InviteDestination.Ramp => Str("friends_land_dest_ramp", "a Ramp link"),
        _ => Str("friends_land_dest_backroom", "the Back Room"),
    };

    private static string KnockLine(InboxItem item)
    {
        if (item.Kind == SendKind.Invite)
            return string.Format(Str("friends_land_invite_line", "invites you to {0}"), DestinationName(item.Destination));
        return item.Watch?.Kind == WatchKind.Flavour
            ? Str("friends_land_flavour_line", "sends you a flavour")
            : Str("friends_land_watch_line", "sends you a watch");
    }

    private static string GoLabel(InboxItem item)
    {
        if (item.Kind == SendKind.Invite) return Str("friends_land_join", "Join");
        return item.Watch?.Kind == WatchKind.Flavour
            ? Str("friends_land_try", "Try it")
            : Str("friends_land_watch", "Watch");
    }

    // ---------------------------------------------------------------- the sink

    private sealed class Sink : ILandingSink
    {
        public void Poke(InboxItem item, bool inGame)
        {
            var word = PokeText(item.PokeId);
            var pink = LandingRules.PokeIsPink(item.PokeId);
            if (inGame)
            {
                var game = ActiveGameWindow();
                if (game != null) { FriendToast.Show(game, item.FromName, item.FromAvatarUrl, word, pink); return; }
            }
            EmiSays(string.Format(Str("friends_land_emi_poke", "{0} says {1}"), item.FromName, word),
                LandingRules.PokeFace(item.PokeId));
            var anchor = Anchor();
            if (anchor != null)
                FloatingWord.Throw(anchor, word, pink);
        }

        public void Knock(InboxItem item, bool inGame)
        {
            var anchor = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();
            if (anchor == null) { Inbox(item); return; }
            if (!inGame)
                EmiSays(item.Kind == SendKind.Invite
                        ? Str("friends_land_emi_knock", "someone wants you")
                        : Str("friends_land_emi_present", "a present"),
                    item.Kind == SendKind.Invite ? "o_o" : "^_~");
            KnockCard.Show(anchor, item, KnockLine(item), GoLabel(item), Str("friends_land_later", "Later"), OnKnockDone);
        }

        public void Inbox(InboxItem item)
        {
            var ladder = App.StartupLadder;
            if (ladder == null) return;
            var title = item.Kind == SendKind.Poke
                ? string.Format(Str("friends_land_inbox_poke", "{0}: {1}"), item.FromName, PokeText(item.PokeId))
                : item.FromName;
            var row = new Startup.InboxItem
            {
                Key = "friends:" + item.Id,
                Title = title,
                Summary = item.Kind == SendKind.Poke ? "" : KnockLine(item),
                Glyph = item.Kind == SendKind.Poke ? "✨" : item.Kind == SendKind.Invite ? "🚪" : "🎁",
                Open = () => Reopen(item),
            };
            foreach (var existing in ladder.Inbox)
                if (string.Equals(existing.Key, row.Key, StringComparison.OrdinalIgnoreCase)) return;
            ladder.Inbox.Insert(0, row);
        }

        public void SentBeat(SendKind kind, Friend to)
        {
            var anchor = ActiveGameWindow() ?? Anchor();
            if (anchor == null) return;
            var word = kind switch
            {
                SendKind.Invite => Str("friends_land_knock_sent", "knock sent"),
                SendKind.Watch => Str("friends_land_watch_sent", "watch sent"),
                _ => Str("friends_land_poke_sent", "sent"),
            };
            FloatingWord.Throw(anchor, word, pink: false, small: true);
        }
    }

    /// <summary>An Inbox row opened later: the card again while it is still answerable.</summary>
    private static void Reopen(InboxItem item)
    {
        if (item.IsExpired(DateTimeOffset.UtcNow))
        {
            EmiSays(Str("friends_land_too_late", "that one already left"), ";_;");
            return;
        }
        if (item.Kind == SendKind.Poke) TheSink.Poke(item, inGame: false);
        else TheSink.Knock(item, inGame: false);
    }

    private static void EmiSays(string line, string face)
    {
        try
        {
            var desk = App.EmiDesk;
            if (desk?.IsOut == true && desk.Window != null) desk.Window.Say(line, face);
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] emi say: {E}", ex.Message); }
    }

    private static void OnKnockDone(InboxItem item, KnockOutcome outcome)
    {
        if (outcome == KnockOutcome.Later) return;
        if (outcome == KnockOutcome.RanOut)
        {
            // A watch keeps for a day on the server: it folds into the Inbox rather than vanishing.
            if (item.Kind == SendKind.Watch && !item.IsExpired(DateTimeOffset.UtcNow)) TheSink.Inbox(item);
            return;
        }
        if (item.Kind == SendKind.Invite) Join(item);
        else if (item.Watch != null) Watch(item.Watch);
    }

    // ---------------------------------------------------------------- join

    private static void Join(InboxItem item)
    {
        App.Logger?.Information("[Friends] joining {Dest} from a knock", item.Destination);
        switch (item.Destination)
        {
            case InviteDestination.Goon:
                // The duel lobby lives in the goon page and takes the code there; there is no
                // app-level GoonGameService to hand it to. Copy it, open the game.
                if (LandingRules.JoinCodeOk(item.Code))
                {
                    try { Clipboard.SetText(item.Code!); } catch (Exception ex) { App.Logger?.Debug("[Friends] clipboard: {E}", ex.Message); }
                    var anchor = Anchor();
                    if (anchor != null)
                        FloatingWord.Throw(anchor, Str("friends_land_code_copied", "code copied"), pink: false, small: true);
                }
                try { GoonGame.GoonHostService.Launch(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[Friends] goon launch failed"); }
                return;

            case InviteDestination.Remote:
                if (!LandingRules.JoinCodeOk(item.Code)) return;
                Helpers.BrowserLauncher.OpenUrlOrPrompt(LandingRules.RemoteUrl(item.Code!), "Remote Control");
                return;

            case InviteDestination.BackRoom:
                try { BackRoom.BackRoomHostService.Launch(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[Friends] back room launch failed"); }
                return;

            case InviteDestination.Ramp:
                // Link to Ramp lives on the Ramp card in the Studio rack; one tab away, not one call.
                OpenTab("studio");
                return;
        }
    }

    // ---------------------------------------------------------------- watch

    private static void Watch(WatchRef watch)
    {
        if (!watch.IsValid()) return;
        App.Logger?.Information("[Friends] watch {Kind} from a knock", watch.Kind);
        switch (watch.Kind)
        {
            case WatchKind.Catalogue: WatchCatalogue(watch.Id); return;
            case WatchKind.Flavour: WatchFlavour(watch.Id); return;
            case WatchKind.Ht: WatchHt(watch.Id); return;
        }
    }

    private static void WatchCatalogue(string catalogueId)
    {
        string? path = null;
        try
        {
            var subs = App.Settings?.Current?.DeeperSubmissions;
            if (subs != null)
                foreach (var kv in subs)
                    if (string.Equals(kv.Value?.CatalogueId, catalogueId, StringComparison.Ordinal) && File.Exists(kv.Key))
                    { path = kv.Key; break; }
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] catalogue lookup: {E}", ex.Message); }

        if (path == null) { OpenTab("deeper"); return; }
        var mw = App.MainWindowRef;
        try { Views.Deeper.EnhancementPlayerWindow.ShowOrActivate(mw, w => w.LoadEnhancementFile(path)); }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "[Friends] deeper player failed");
            OpenTab("deeper");
        }
    }

    private static void WatchFlavour(string flavour)
    {
        var s = App.Settings?.Current;
        if (s == null) return;
        var niches = LandingRules.FlavourNiches(flavour);
        if (niches.Count == 0) return;
        try
        {
            // The launcher's own apply path. A local-only player keeps their source: pulling from
            // Reddit is a consent the panel asks for, never one a friend's watch grants.
            var source = s.MediaSource;
            if (!Launcher.LauncherMediaSettings.IsKnownSource(source)) source = Launcher.LauncherMediaSettings.SourceLocal;
            if (source == Launcher.LauncherMediaSettings.SourceLocal && s.HasRemoteMediaConsent)
                source = Launcher.LauncherMediaSettings.SourceMixed;
            Launcher.LauncherMediaSettings.Apply(s, source, s.RemoteMediaRatio, niches.ToList());
            Fyp.Online.FypOnlineCoordinator.ResetAllChannels();
            var mw = App.MainWindowRef;
            if (mw != null) mw.InvalidateAssetPoolsAfterSelectionChange();
            else App.Settings?.Save();

            var anchor = ActiveGameWindow() ?? Anchor();
            if (anchor != null)
                FloatingWord.Throw(anchor, Str("friends_land_flavour_on", "flavour on"), pink: true, small: true);
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "[Friends] flavour apply failed"); }
    }

    private static void WatchHt(string id)
    {
        var url = LandingRules.HtUrl(id);
        if (url == null) return;
        var mw = App.MainWindowRef;
        if (mw == null) return;
        try
        {
            Launcher.LauncherHost.OpenPanel();
            mw.NavigateToUrlInBrowser(url, autoPlayFullscreen: false, userInitiated: true);
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "[Friends] ht watch failed"); }
    }

    private static void OpenTab(string tab)
    {
        try { Launcher.LauncherHost.OpenPanelTab(tab); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] open tab {Tab}: {E}", tab, ex.Message); }
    }
}
