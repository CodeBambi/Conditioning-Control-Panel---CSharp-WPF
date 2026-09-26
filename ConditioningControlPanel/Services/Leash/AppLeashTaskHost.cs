using System;
using System.IO;
using System.Windows;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// The real features a gate task drives. Every entry point is one the app already has: lock cards
/// through <c>App.LockCard.ShowLockCard</c>, sessions through the same start path the remote and
/// the Programs tab use, bubbles through <c>App.Bubbles</c>, a Hypnotube video in the panel's
/// browser, a catalogue enhancement in the Deeper player. Nothing here enables Strict Lock,
/// touches the panic key or asks for a strict lock card.
/// </summary>
public sealed class AppLeashTaskHost : ILeashTaskHost, IDisposable
{
    /// <summary>Bubbles per minute while a bubble task runs and the player had them off.</summary>
    public const int BubblesPerMinute = 20;

    private BrowserVideoTimeSource? _source;
    private object? _sourceView;
    private string? _watchPath;
    private bool _hooked;
    private bool _hookedDeeper;

    public event Action? BubblePopped;
    public event Action<LeashWatch>? WatchFinished;
    private LeashWatch? _watching;

    public AppLeashTaskHost() => Hook();

    private void Hook()
    {
        // Services come up in their own order at startup; each hook is taken the first time it can be.
        try
        {
            if (!_hooked && App.Bubbles != null) { App.Bubbles.OnBubblePopped += OnPopped; _hooked = true; }
            if (!_hookedDeeper && App.DeeperHost != null) { App.DeeperHost.EnhancementCompleted += OnEnhancementCompleted; _hookedDeeper = true; }
        }
        catch (Exception ex) { App.Logger?.Debug("Leash host hook failed: {E}", ex.Message); }
    }

    private void OnPopped() => BubblePopped?.Invoke();

    private void OnEnhancementCompleted(object? sender, EnhancementCompletedEventArgs e)
    {
        var w = _watching;
        if (w == null || w.Kind != "catalogue" || _watchPath == null) return;
        if (!string.Equals(App.DeeperHost?.LoadedFilePath, _watchPath, StringComparison.OrdinalIgnoreCase)) return;
        WatchFinished?.Invoke(w);
    }

    // ---- lock cards ----

    public int LockCardsCompleted => App.Achievements?.Progress?.TotalLockCardsCompleted ?? 0;

    public bool LockCardOpen
    {
        get { try { return LockCardWindow.IsAnyOpen(); } catch { return false; } }
    }

    public bool ShowLockCard()
    {
        var svc = App.LockCard;
        if (svc == null) return false;
        try
        {
            // Never strict, never a test card (a test card does not count). The player's own phrases.
            svc.ShowLockCard(customStrict: false, isTest: false);
            return true;
        }
        catch (Exception ex) { App.Logger?.Warning("Leash lock card failed: {E}", ex.Message); return false; }
    }

    // ---- sessions ----

    public bool SessionRunning => App.IsSessionRunning;

    public bool StartSession(PunishKind kind, int minutes)
    {
        var mw = App.MainWindowRef;
        if (mw == null || App.IsSessionRunning) return false;
        try
        {
            mw.StartSessionFromRemote(BuildSession(kind, minutes));
            return true;
        }
        catch (Exception ex) { App.Logger?.Warning("Leash session failed: {E}", ex.Message); return false; }
    }

    /// <summary>A leash session: the player's own effects, like the remote's generic session, and
    /// for a pink session the pink filter from start to end. No strict lock, ever.</summary>
    internal static Models.Session BuildSession(PunishKind kind, int minutes)
    {
        var cur = App.Settings?.Current;
        var pink = kind == PunishKind.Pink;
        var id = pink ? "leash_pink" : "leash_detention";
        var session = new Models.Session
        {
            Id = id,
            Name = pink ? "Pink session" : "Detention",
            DurationMinutes = Math.Clamp(minutes, 1, 60),
            Difficulty = Models.SessionDifficulty.Easy,
            BonusXP = 0,
            Settings = new Models.SessionSettings
            {
                FlashEnabled = cur?.FlashEnabled ?? true,
                FlashPerHour = cur?.FlashFrequency ?? 10,
                FlashOpacity = cur?.FlashOpacity ?? 100,
                FlashImages = cur?.SimultaneousImages ?? 1,
                FlashClickable = cur?.FlashClickable ?? false,
                FlashAudioEnabled = cur?.FlashAudioEnabled ?? false,
                SubliminalEnabled = cur?.SubliminalEnabled ?? true,
                SubliminalPerMin = cur?.SubliminalFrequency ?? 5,
                SubliminalOpacity = cur?.SubliminalOpacity ?? 100,
                SubliminalFrames = cur?.SubliminalDuration ?? 5,
                MandatoryVideosEnabled = false,
                BubblesEnabled = cur?.BubblesEnabled ?? false,
                PinkFilterEnabled = pink,
                PinkFilterStartMinute = 0,
                PinkFilterEndMinute = -1,
                PinkFilterStartOpacity = 25,
                PinkFilterEndOpacity = 25,
            },
        };
        return session;
    }

    // ---- bubbles ----

    public bool StartBubbles()
    {
        var b = App.Bubbles;
        if (b == null) return false;
        Hook();
        if (b.IsRunning) return false;
        try { b.Start(bypassLevelCheck: true, frequency: BubblesPerMinute); return true; }
        catch (Exception ex) { App.Logger?.Warning("Leash bubbles failed: {E}", ex.Message); return false; }
    }

    public void StopBubbles()
    {
        try { App.Bubbles?.Stop(); } catch (Exception ex) { App.Logger?.Debug("Leash bubbles stop failed: {E}", ex.Message); }
    }

    // ---- video ----

    public bool OpenWatch(LeashWatch watch)
    {
        if (!LeashGrammar.ValidWatch(watch)) return false;
        Hook();
        EndWatch();
        _watching = watch;
        try
        {
            if (watch.Kind == "ht")
            {
                var url = Friends.LandingRules.HtUrl(watch.Id);
                var mw = App.MainWindowRef;
                if (url == null || mw == null) return false;
                Launcher.LauncherHost.OpenPanel();
                return mw.NavigateToUrlInBrowser(url, autoPlayFullscreen: true, userInitiated: true);
            }
            var path = CataloguePath(watch.Id);
            if (path == null) return false;
            _watchPath = path;
            Views.Deeper.EnhancementPlayerWindow.ShowOrActivate(App.MainWindowRef, w => w.LoadEnhancementFile(path));
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Leash watch failed: {E}", ex.Message);
            return false;
        }
    }

    /// <summary>The local file for a catalogue id, the same lookup a friend's watch uses.</summary>
    private static string? CataloguePath(string catalogueId)
    {
        var subs = App.Settings?.Current?.DeeperSubmissions;
        if (subs == null) return null;
        foreach (var kv in subs)
            if (string.Equals(kv.Value?.CatalogueId, catalogueId, StringComparison.Ordinal) && File.Exists(kv.Key))
                return kv.Key;
        return null;
    }

    public LeashWatchSample? SampleWatch(LeashWatch watch)
    {
        if (watch.Kind != "ht") return null;   // catalogue finishes by its own event
        try
        {
            var mw = App.MainWindowRef;
            var view = mw?.GetBrowserWebView();
            var core = view?.CoreWebView2;
            var url = Friends.LandingRules.HtUrl(watch.Id);
            if (view == null || core == null || url == null || !SamePage(core.Source, url)) return null;
            if (!ReferenceEquals(view, _sourceView))
            {
                _source?.Dispose();
                _source = new BrowserVideoTimeSource(view);
                _source.Attach();
                _sourceView = view;
            }
            var window = Window.GetWindow(view);
            var visible = view.IsVisible && window is { IsVisible: true } && window.WindowState != WindowState.Minimized;
            return new LeashWatchSample(_source!.GetCurrentTimeSeconds(), _source.GetDurationSeconds(), visible);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Leash watch sample failed: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>Same host (a leading www. ignored) and same path, query ignored.</summary>
    internal static bool SamePage(string? pageUrl, string? targetUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)) return false;
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var target)) return false;
        static string Host(Uri u) => u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        return string.Equals(Host(page), Host(target), StringComparison.OrdinalIgnoreCase)
            && string.Equals(page.AbsolutePath.TrimEnd('/'), target.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    public void EndWatch()
    {
        _watching = null;
        _watchPath = null;
        try { _source?.Dispose(); } catch { }
        _source = null;
        _sourceView = null;
    }

    public void Dispose()
    {
        EndWatch();
        try
        {
            if (_hooked && App.Bubbles != null) App.Bubbles.OnBubblePopped -= OnPopped;
            if (_hookedDeeper && App.DeeperHost != null) App.DeeperHost.EnhancementCompleted -= OnEnhancementCompleted;
        }
        catch { }
    }
}
