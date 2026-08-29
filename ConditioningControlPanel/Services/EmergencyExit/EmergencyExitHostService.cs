using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Services.EmergencyExit;

/// <summary>
/// Host for the EMERGENCY EXIT - the friction door of Lockdown. Read
/// <c>Services/EmergencyExit/EMERGENCY_EXIT.md</c> first; it is the contract this file implements.
///
/// <para>Shape is deliberately the smallest thing that can be authoritative: ONE
/// <see cref="ChaosWebViewHost"/> window (windowed, owned by MainWindow), the same virtual-host
/// mapping set the Arcademy uses (<c>ccp.game</c> Deny for the page, <c>ccp.assets</c> Allow for the
/// user's own media, plus the content-pack mirror), and a four-message protocol. No heartbeat, no
/// boot deadline, no relaunch ladder: this window is a 60-second gag whose failure mode is "the user
/// closes it and nothing changed", which is already the `abandon` verdict.</para>
///
/// <para><b>The host is authoritative.</b> The page reports what HAPPENED (completed / failed); the
/// verdict is rolled here (<see cref="SendBackChance"/>) and applied to <see cref="LockdownService"/>
/// in the same synchronous turn it is decided. A page that is hand-edited, reloaded or killed
/// mid-outro therefore cannot invent an escape, and cannot dodge a sendback by never playing the
/// outro. The page is told first (<see cref="OnGameFinished"/> explains why that ordering is
/// load-bearing) but it is never ASKED anything.</para>
///
/// <para><b>Nothing here is a wall.</b> Every path out of this window is safe: the X, Esc, a dead
/// render process and app shutdown all land on <see cref="Close"/>, which changes nothing about the
/// lockdown. The real safety valve (click the timer digits 5x, type "let me out") is untouched.</para>
/// </summary>
internal static class EmergencyExitHostService
{
    /// <summary>The four games, in the order EMERGENCY_EXIT.md tabulates them. The pick is random
    /// with no immediate repeat, so a user who keeps pressing the button keeps getting new gags.</summary>
    private static readonly string[] Games = { "labyrinth", "password", "jigsaw", "captcha" };

    /// <summary>Up to this many of the user's own GIFs are handed to the page (the jigsaw skins its
    /// tiles from them). Sampled and shuffled here so the page cannot enumerate the media folder.</summary>
    private const int MaxGifs = 12;

    /// <summary>Hard ceiling on the outro: if the page never posts <c>outro-done</c> (a broken game
    /// file, a render process that died mid-card), the window still goes away.</summary>
    private static readonly TimeSpan OutroFailsafe = TimeSpan.FromSeconds(8);

    private static readonly Random Rng = new();

    private static ChaosWebViewHost? _host;
    private static DispatcherTimer? _failsafe;
    private static string _game = "";
    private static bool _verdictSent;
    /// <summary>True from the moment the verdict is posted to the page until the window is torn
    /// down. While it is set, the LockdownDeactivated handler leaves the window ALONE: an `escape`
    /// verdict deactivates the lockdown, and closing the window from that event would kill the page
    /// before it ever rendered the escape card the user just earned. The outro's `outro-done` (or
    /// <see cref="OutroFailsafe"/>) closes it a beat later instead.</summary>
    private static bool _outroPending;
    private static bool _closing;          // reentrancy: Dispose closes the window -> Closed -> Close()
    private static bool _hooked;           // LockdownDeactivated subscription (one-shot, lazily armed)
    private static bool _preview;          // --emergency-exit-preview: verdicts logged, NEVER applied

    /// <summary>How many times Emergency Exit was opened during THIS lockdown (1-based, reset when
    /// the lockdown ends). The page shows it in the HUD and seeds its glitch RNG from it.</summary>
    private static int _attempt;

    /// <summary>The game picked last time, so the next pick can avoid an immediate repeat. Reset
    /// with <see cref="_attempt"/> when the lockdown ends.</summary>
    private static string _lastGame = "";

    /// <summary>True while the minigame window is up.</summary>
    public static bool IsOpen => _host != null;

    // ============================ open ============================

    /// <summary>
    /// Open the friction door: raise the tripwire, pick a game, put the window up.
    ///
    /// <para>No-op unless a lockdown is actually running - the huge button only exists on the active
    /// panel, but this is the load-bearing check, not the button's visibility. Idempotent: a live
    /// window is re-focused rather than replaced, so double-clicking the button cannot stack two
    /// games (and therefore cannot roll two verdicts).</para>
    /// </summary>
    public static void Open()
    {
        if (App.Lockdown?.IsActive != true)
        {
            App.Logger?.Debug("EmergencyExit: Open ignored - no lockdown is running");
            return;
        }
        OpenCore(null, preview: false);
    }

    /// <summary>
    /// Dev entry point for <c>--emergency-exit-preview &lt;game&gt;</c>: the real host window against
    /// the real page, with a synthetic <c>init</c> and no lockdown. Verdicts are rolled and LOGGED
    /// but never applied, so the rig can be run unattended - the one rule the Possession preview also
    /// obeys (a real lockdown is meant to be hard to escape; a verification rig must never arm one).
    /// </summary>
    internal static void OpenPreview(string game) => OpenCore(NormalizeGame(game), preview: true);

    private static void OpenCore(string? forcedGame, bool preview)
    {
        // Idempotent. A second press while a game is up is the user being impatient, not a request
        // for a second verdict.
        if (_host != null) { _host.FocusWeb(); return; }

        try
        {
            EnsureHooked();
            _preview = preview;
            _verdictSent = false;
            _outroPending = false;
            _closing = false;

            if (!preview)
            {
                _attempt++;
                // The tripwire fires BEFORE the window: the Possession director's reaction to
                // "they pressed the big button" should land while the panel is still on screen.
                try { App.Lockdown?.NotifyEscapeAttempt(EscapeKinds.EmergencyExit); }
                catch (Exception ex) { App.Logger?.Debug("EmergencyExit: tripwire failed: {E}", ex.Message); }
            }
            else if (_attempt <= 0) _attempt = 1;

            _game = forcedGame ?? PickGame();
            _lastGame = _game;

            // EMI Desk (MOMENTS 4.B): a HOLD. Someone is trying to get out of a lockdown; she is
            // not a commentator on that. Dropped in Close(), which every exit path lands in.
            try { App.EmiDesk?.Fire("emergencyExitOpened", null); } catch { }

            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                // The shell + the four games. Deny: the page never needs to be read cross-origin.
                ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                // The user's own media. Allow, because the jigsaw draws a GIF into a canvas and a
                // Deny origin taints it - the same reason the Arcademy maps it this way.
                ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                ChaosWebViewHost.ContentMapping(),
            };

            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/emergency-exit/index.html?game=" + Uri.EscapeDataString(_game),
                PrimaryHost = "ccp.game",
                Mappings = mappings,
                UserDataFolderName = "emergency-exit",
                InputEnabled = true,
                StartFullscreen = false,
                // Native ownership, not Topmost: a lockdown is exactly the state in which other
                // things (barks, overlays, the panel itself) keep raising MainWindow, and a friction
                // door buried behind it reads as a hang.
                OwnedByMainWindow = true,
                WindowedWidth = 960,
                WindowedHeight = 640,
                CenterOnMainWindow = true,
                WindowTitle = WindowTitle(),
                LogTag = "EmergencyExit",
                // The games have stingers and the outro card has a chime; neither gets a click first.
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = OnProcessFailed,
            });

            _host.Show();
            // Title-bar X = `abandon`. Nothing changes; the timer keeps running.
            if (_host.Window != null) _host.Window.Closed += (_, _) => Close();

            App.Logger?.Information("EmergencyExit: opened (game {Game}, attempt {Attempt}{Preview})",
                _game, _attempt, preview ? ", PREVIEW" : "");

            if (!preview)
            {
                try { App.Bark?.NotifyEmergencyExitOpened(_game, _attempt); }
                catch (Exception ex) { App.Logger?.Debug("EmergencyExit: opened bark failed: {E}", ex.Message); }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "EmergencyExitHostService.Open failed");
            Close();
        }
    }

    /// <summary>The window title, localized. Loc.Get returns the KEY when a string is missing (it is
    /// how untranslated keys stay visible in development), and a taskbar entry reading
    /// "ee_window_title" is worse than an untranslated one - so the English text is the fallback
    /// until loc-additions/host.json has been merged into Localization/Languages.</summary>
    private static string WindowTitle()
    {
        var s = Loc.Get("ee_window_title");
        return string.IsNullOrWhiteSpace(s) || s == "ee_window_title" ? "Emergency Exit" : s;
    }

    /// <summary>Random game, never the same one twice in a row within a lockdown.</summary>
    private static string PickGame()
    {
        var pool = Games.Where(g => !string.Equals(g, _lastGame, StringComparison.Ordinal)).ToArray();
        if (pool.Length == 0) pool = Games;
        return pool[Rng.Next(pool.Length)];
    }

    private static string NormalizeGame(string? game)
    {
        var g = (game ?? "").Trim().ToLowerInvariant();
        return Games.Contains(g) ? g : Games[0];
    }

    /// <summary>
    /// Arm the one subscription this service needs. Lazily, from the first Open: a static class with
    /// no startup wiring cannot be forgotten in App.OnStartup, and there is nothing to hook before
    /// the button that reaches here has been pressed once.
    ///
    /// <para>LockdownDeactivated is the single reset point: it closes a window that outlived its
    /// lockdown (the secret phrase, a panic key, the timer simply expiring while a game is open) and
    /// clears the per-lockdown attempt counter and no-repeat memory.</para>
    ///
    /// <para>With ONE exception, which is the whole reason <see cref="_outroPending"/> exists: an
    /// `escape` verdict deactivates the lockdown itself, and this event fires synchronously from
    /// inside that call. Closing here would tear the window down between the verdict being posted
    /// and the page painting the escape card, so the player who just won would watch the window
    /// vanish instead. The counters still reset (the lockdown IS over); only the window survives,
    /// until `outro-done` or the failsafe closes it.</para>
    /// </summary>
    private static void EnsureHooked()
    {
        if (_hooked || App.Lockdown == null) return;
        _hooked = true;
        App.Lockdown.LockdownDeactivated += () =>
        {
            if (!_outroPending) Close();
            _attempt = 0;
            _lastGame = "";
        };
    }

    // ============================ protocol ============================

    private static void OnPageReady()
    {
        try
        {
            _host?.FocusWeb();
            _host?.Post(BuildInit());
            App.Logger?.Debug("EmergencyExit: sent init (game {Game})", _game);
        }
        catch (Exception ex) { App.Logger?.Warning("EmergencyExit.OnPageReady: {E}", ex.Message); }
    }

    private static object BuildInit()
    {
        var ld = App.Lockdown;
        var remaining = ld?.IsActive == true ? (int)Math.Max(0, ld.Remaining.TotalSeconds) : 0;
        var duration = ld?.IsActive == true ? (int)Math.Max(0, ld.Duration.TotalSeconds) : 0;
        // Preview has no lockdown to read, so it gets a plausible clock rather than 0/0 - a HUD
        // reading "0:00 left" would make every timed game look broken in the rig.
        if (_preview && duration == 0) { duration = 600; remaining = 412; }

        return new
        {
            type = "init",
            game = _game,
            attempt = Math.Max(1, _attempt),
            restarts = ld?.RestartCount ?? 0,
            remainingSec = remaining,
            durationSec = duration,
            photosafe = App.Settings?.Current?.LockdownPhotosafe ?? false,
            lang = LocalizationManager.Instance.CurrentLanguage,
            mod = BuildMod(),
            assets = new { gifs = BuildGifs() },
        };
    }

    /// <summary>
    /// The mod's address book, so the captcha can say "Confirm you are NOT a good girl" in the right
    /// voice and the outro cards can substitute {honorific} / {subject}.
    ///
    /// <para>The three builtin packs are mapped explicitly (read off their own bark packs' lockdown
    /// lines, which is where each mod's second-person address actually lives). A creator mod falls
    /// back to its manifest identity's petName when it declares one, and to "good girl" plus the
    /// mod's display name otherwise - never to a Windows username, and never to anything the user
    /// typed about themselves.</para>
    /// </summary>
    private static object BuildMod()
    {
        string id = "", name = "";
        string? petName = null;
        try
        {
            id = App.Mods?.ActiveModId ?? "";
            var mod = App.Mods?.ActiveMod;
            name = mod?.Manifest?.Name ?? "";
            petName = mod?.Manifest?.Identity?.PetName;
        }
        catch (Exception ex) { App.Logger?.Debug("EmergencyExit: mod read failed: {E}", ex.Message); }

        string honorific, subject;
        switch (id)
        {
            case "builtin-bambisleep":
                honorific = "good girl"; subject = "Bambi"; break;
            case "builtin-sissyhypno":
                honorific = "sissy"; subject = "sissy"; break;
            // builtin-locked's warden calls the user "pet" / "good boy" / "sweet thing" in every
            // lockdown_on/off/tick line; "pet" is the one that is gender-neutral and reads as a
            // NAME in "Confirm you are NOT a pet", which is what the captcha needs it for.
            case "builtin-locked":
                honorific = "pet"; subject = "pet"; break;
            default:
                honorific = string.IsNullOrWhiteSpace(petName) ? "good girl" : petName!.Trim();
                subject = string.IsNullOrWhiteSpace(name) ? "good girl" : name;
                break;
        }

        return new { id, name = string.IsNullOrWhiteSpace(name) ? id : name, honorific, subject };
    }

    /// <summary>Up to <see cref="MaxGifs"/> of the user's own GIFs as ccp.assets URLs, shuffled.
    /// Empty when the media folder has none, which every game must survive (the jigsaw falls back to
    /// a drawn pattern) - a fresh install has no images at all.</summary>
    private static string[] BuildGifs()
    {
        try
        {
            var assetsRoot = App.EffectiveAssetsPath;
            var imagesRoot = Path.Combine(assetsRoot, "images");
            if (!Directory.Exists(imagesRoot)) return Array.Empty<string>();

            var pool = Directory.EnumerateFiles(imagesRoot, "*.gif", SearchOption.AllDirectories).ToList();
            // Partial Fisher-Yates: a random slice without shuffling a folder that may hold thousands.
            for (int i = 0; i < Math.Min(MaxGifs, pool.Count); i++)
            {
                int j = Rng.Next(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            return pool.Take(MaxGifs).Select(f => ToAssetsUrl(assetsRoot, f)).ToArray();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("EmergencyExit: gif scan failed: {E}", ex.Message);
            return Array.Empty<string>();
        }
    }

    private static string ToAssetsUrl(string assetsRoot, string file)
    {
        var rel = Path.GetRelativePath(assetsRoot, file).Replace('\\', '/');
        var escaped = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
        return "https://ccp.assets/" + escaped;
    }

    private static void OnPageMessage(JObject msg)
    {
        try
        {
            var type = msg["type"]?.ToString() ?? "";
            switch (type)
            {
                case "game-started":
                    App.Logger?.Debug("EmergencyExit: game-started ({Game})", msg["game"]?.ToString() ?? _game);
                    break;

                case "game-finished":
                    OnGameFinished(msg);
                    break;

                case "outro-done":
                    App.Logger?.Debug("EmergencyExit: outro-done ({Outcome})", msg["outcome"]?.ToString() ?? "");
                    Close();
                    break;

                case "quit":
                    // `abandon`: nothing changes, the timer keeps running. The user may press the
                    // big button again immediately - it is friction, not a wall.
                    App.Logger?.Information("EmergencyExit: abandoned by the user (game {Game})", _game);
                    Close();
                    break;

                default:
                    App.Logger?.Debug("EmergencyExit: unhandled page message '{Type}'", type);
                    break;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("EmergencyExit.OnPageMessage: {E}", ex.Message); }
    }

    /// <summary>
    /// The one decision this service exists to make. Roll, TELL THE PAGE, then apply.
    ///
    /// <para>The post has to come first, and the reason is not cosmetic. Applying an `escape` calls
    /// <see cref="LockdownService.Deactivate"/>, which raises LockdownDeactivated synchronously,
    /// which used to run <see cref="Close"/> and dispose the WebView - all before the verdict message
    /// was ever posted, so the post landed on a null host and the winning player's window simply
    /// disappeared. Posting first, plus the <see cref="_outroPending"/> guard on the deactivation
    /// handler, is what lets the page show the card it has always been able to render.</para>
    ///
    /// <para>The apply still happens synchronously right after, and is NOT deferred onto the
    /// dispatcher: the host is authoritative, and a verdict queued behind a dispatcher that is
    /// shutting down is a verdict that silently never happens.</para>
    /// </summary>
    private static void OnGameFinished(JObject msg)
    {
        if (_verdictSent) { App.Logger?.Debug("EmergencyExit: duplicate game-finished ignored"); return; }
        _verdictSent = true;

        var game = NormalizeGame(msg["game"]?.ToString() ?? _game);
        var result = (msg["result"]?.ToString() ?? "failed").Trim().ToLowerInvariant();

        // A failed game is ALWAYS a sendback: running the clock out, getting locked in or giving up
        // inside the game is not a way to leave. The roll only exists for a completed one.
        double chance = SendBackChance(game);
        double roll = Rng.NextDouble();
        bool sendback = result != "completed" || roll < chance;
        var outcome = sendback ? "sendback" : "escape";

        App.Logger?.Information(
            "EmergencyExit: verdict (game {Game}, result {Result}, outcome {Outcome}, roll {Roll:0.###}/{Chance:0.###})",
            game, result, outcome, roll, chance);

        // Tell the page, arm its deadline, and only then change the world. The outro is only
        // "pending" once the failsafe that will close the window is actually on its way: without one
        // (no window, or a dispatcher that is already shutting down) the deactivation handler stays
        // the thing that tears it down, exactly as before.
        var host = _host;
        try { host?.Post(new { type = "verdict", outcome }); }
        catch (Exception ex) { App.Logger?.Warning("EmergencyExit: posting the verdict failed: {E}", ex.Message); }
        _outroPending = ArmOutroFailsafe(host);

        if (_preview)
        {
            App.Logger?.Information("EmergencyExit: PREVIEW - verdict NOT applied");
        }
        else
        {
            try
            {
                if (sendback) App.Lockdown?.RestartTimer(game);
                else App.Lockdown?.Deactivate();
            }
            catch (Exception ex) { App.Logger?.Error(ex, "EmergencyExit: applying verdict {Outcome} failed", outcome); }

            try { App.Bark?.NotifyEmergencyExitVerdict(game, outcome); }
            catch (Exception ex) { App.Logger?.Debug("EmergencyExit: verdict bark failed: {E}", ex.Message); }
        }
    }

    /// <summary>
    /// Probability that a COMPLETED game still sends the user back. The labyrinth is the gag - you
    /// are so good at quitting that you always come back - so it is 1.0 and its "win" is cosmetic;
    /// the other three are a two-in-three exit. The roll lives here and never in JS, because JS is
    /// the one part of this feature the user can edit.
    /// </summary>
    private static double SendBackChance(string game) => game switch
    {
        "labyrinth" => 1.0,
        "password" => 0.33,
        "jigsaw" => 0.33,
        "captcha" => 0.33,
        _ => 0.33,
    };

    /// <summary>The outro card gets <see cref="OutroFailsafe"/> to play and post <c>outro-done</c>.
    /// After that the window closes regardless: the verdict is already applied, so the card is
    /// courtesy and must never be able to strand a window over the app.
    ///
    /// <para>The timer belongs to the window instance that armed it. It is armed on the dispatcher
    /// (one turn later than the verdict), and by the time it fires 8 s after that, <see cref="_host"/>
    /// may hold a completely different window - the outro finished, the user pressed the big button
    /// again, and the next lockdown's game is up. An untethered failsafe closes THAT one. So the host
    /// is captured and compared, both when arming and when firing.</para></summary>
    /// <returns>True when a failsafe is on its way, i.e. when the window is guaranteed to close
    /// even if the page never answers.</returns>
    private static bool ArmOutroFailsafe(ChaosWebViewHost? host)
    {
        if (host == null) return false;
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return false;
        disp.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!ReferenceEquals(_host, host)) return;   // that window is already gone
                CancelOutroFailsafe();
                var timer = new DispatcherTimer { Interval = OutroFailsafe };
                _failsafe = timer;
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (!ReferenceEquals(_host, host)) return;
                    App.Logger?.Information("EmergencyExit: outro failsafe fired - closing");
                    Close();
                };
                timer.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("EmergencyExit: failsafe arm failed: {E}", ex.Message); }
        }));
        return true;
    }

    private static void CancelOutroFailsafe()
    {
        try { _failsafe?.Stop(); } catch { }
        _failsafe = null;
    }

    /// <summary>A dead browser/render process is an <c>abandon</c>, not a verdict: the page cannot
    /// tell us what happened, so the honest reading is "the attempt did not finish".</summary>
    private static void OnProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        App.Logger?.Warning("EmergencyExit: WebView2 process failed ({Kind}) - closing", kind);
        Close();
    }

    // ============================ close ============================

    /// <summary>
    /// Tear the window down. Idempotent, safe from any thread, and safe to call when nothing is
    /// open - App shutdown, lockdown deactivation, the title-bar X, Esc and the outro all land here.
    /// Closing NEVER changes the lockdown: by the time a verdict exists it has already been applied.
    /// </summary>
    public static void Close()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp != null && !disp.CheckAccess() && !disp.HasShutdownStarted)
        {
            try { disp.BeginInvoke(new Action(Close)); } catch { }
            return;
        }
        if (_closing) return;   // _host.Dispose() closes the window, re-raising Closed -> here
        _closing = true;

        // EMI Desk: the escape game is over, so the HOLD comes off.
        try { App.EmiDesk?.ReleaseHold("emergencyExitOpened"); } catch { }
        try
        {
            CancelOutroFailsafe();
            if (_host != null)
            {
                try { _host.Post(new { type = "close" }); } catch { }
                try { _host.Dispose(); } catch { }
                _host = null;
                App.Logger?.Information("EmergencyExit: closed (game {Game})", _game);
            }
            _verdictSent = false;
            _outroPending = false;
            _preview = false;
        }
        catch (Exception ex) { App.Logger?.Debug("EmergencyExitHostService.Close: {E}", ex.Message); }
        finally { _closing = false; }
    }
}
