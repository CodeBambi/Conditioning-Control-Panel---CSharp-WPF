using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// ONE BLOCK OF A CHAPTER. The renderer (Resources/web/codex) draws these properly; the native
/// fail-soft reader in <see cref="EmiCodexWindow"/> draws them as plain scrolling text. Every
/// field is optional on purpose - a chapter file written by hand, half-merged, or from a later
/// wave with a block type this build has never heard of must still READ, not throw.
/// </summary>
public sealed class CodexBlock
{
    /// <summary>p | steps | figure | callout | limit. Anything else renders as a paragraph.</summary>
    [JsonProperty("type")] public string? Type { get; set; }

    [JsonProperty("text")] public string? Text { get; set; }

    /// <summary>The ordered lines of a <c>steps</c> block.</summary>
    [JsonProperty("items")] public List<string>? Items { get; set; }

    /// <summary>The figure vocabulary word (stack-drop, pulse, layers...). CSS only, never art.</summary>
    [JsonProperty("kind")] public string? Kind { get; set; }

    [JsonProperty("caption")] public string? Caption { get; set; }
}

/// <summary>EMI in the margin: exactly one reaction per chapter, never an explanation.</summary>
public sealed class CodexMargin
{
    [JsonProperty("t")] public string? T { get; set; }
    [JsonProperty("face")] public string? Face { get; set; }
}

/// <summary>
/// One chapter = one screen, as it is written in <c>Resources/web/codex/chapters/&lt;id&gt;.json</c>.
/// Deserialised by the C# lane ONLY for the fail-soft reader; the page reads the same files itself.
/// </summary>
public sealed class CodexChapter
{
    [JsonProperty("id")] public string? Id { get; set; }
    [JsonProperty("volume")] public int Volume { get; set; }
    [JsonProperty("order")] public int Order { get; set; }
    [JsonProperty("title")] public string? Title { get; set; }
    [JsonProperty("blurb")] public string? Blurb { get; set; }

    /// <summary>An <see cref="EmiTargets"/> id, or null. Drives "TAKE ME THERE".</summary>
    [JsonProperty("target")] public string? Target { get; set; }

    /// <summary>A <c>TutorialType</c> NAME, or null. Never an ordinal.</summary>
    [JsonProperty("tour")] public string? Tour { get; set; }

    [JsonProperty("margin")] public CodexMargin? Margin { get; set; }
    [JsonProperty("blocks")] public List<CodexBlock>? Blocks { get; set; }

    /// <summary>A title that is always safe to put on a list row.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title!.Trim()
        : !string.IsNullOrWhiteSpace(Id) ? Id!.Replace('-', ' ')
        : "untitled";
}

/// <summary>
/// THE BOOK. EMI's copy of the field manual: a pixel-bound page hosted in WebView2, with a native
/// plain-text reader behind it for every way that can fail.
///
/// <para><b>Why this is a service and not a window.</b> There are two possible bodies for one
/// logical window - the hosted page and <see cref="EmiCodexWindow"/> - and exactly one of them is
/// ever up. Single-instance, the bookmark and the bridge therefore live out here, above both, so
/// "open the book" means the same thing whichever body answers.</para>
///
/// <para><b>Why there is no WebView2 plumbing in this file.</b> <see cref="ChaosWebViewHost"/> is
/// the finished host (WAVE2-CONTRACT recon 2): virtual-host mappings, the layered-window trap
/// already solved with <c>AllowsTransparency=false</c>, hardened settings, navigation lockdown, the
/// message bridge and the windowed frame. This file configures it and reads its messages; it does
/// not re-implement any of it.</para>
///
/// <para><b>Fail soft, always.</b> No WebView2 runtime, no bundle on disk, a navigation that
/// reports failure or a browser process that dies each land in the same place: the native reader,
/// showing the same <c>chapters/*.json</c> as scrolling text with the website manual one click
/// away. There is no path here that shows an empty window, and none that throws into a caller.</para>
/// </summary>
public static class EmiCodex
{
    // =====================================================================================
    //  ids and constants the other lanes are wired against
    // =====================================================================================

    /// <summary>The <see cref="EmiTargets"/> id of the book. Seventh in the catalogue, always
    /// available, never locked - see the row itself for why both of those are load-bearing.</summary>
    public const string TargetId = "codex";

    /// <summary>The moment behind her two-chip "want the book?" ask.</summary>
    public const string OfferMoment = "bookOffer";

    /// <summary>The <see cref="EmiOffers"/> effect verb that opens the book.</summary>
    public const string OpenEffect = "book:open";

    /// <summary>The website manual, opened in the user's own browser. The fail-soft panel's escape
    /// hatch: whatever went wrong locally, the same words are still readable somewhere.</summary>
    public const string ManualUrl = "https://cclabs.app/guide.html";

    /// <summary>Virtual host the bundle is served from. Same origin name every other local page
    /// uses, and navigation is locked to it by the host.</summary>
    private const string PageHost = "ccp.game";

    /// <summary>The one page the window ever navigates to.</summary>
    private const string StartUrl = "https://" + PageHost + "/codex/index.html";

    private const string LogTag = "EmiCodex";

    // =====================================================================================
    //  where the bundle lives
    // =====================================================================================

    /// <summary><c>{exe}/Resources/web</c> - the origin root, exactly as the intake and DtRH hosts
    /// map it. The bundle is a folder under it, so codex/index.html resolves against the same
    /// origin as anything else the app ships.</summary>
    public static string WebRoot
    {
        get
        {
            try { return Path.Combine(AppContext.BaseDirectory, "Resources", "web"); }
            catch { return string.Empty; }
        }
    }

    /// <summary><c>{exe}/Resources/web/codex</c>.</summary>
    public static string BundleRoot
    {
        get
        {
            var root = WebRoot;
            return string.IsNullOrEmpty(root) ? string.Empty : Path.Combine(root, "codex");
        }
    }

    /// <summary><c>{exe}/Resources/web/codex/chapters</c>, the folder BOTH readers read.</summary>
    public static string ChaptersDir
    {
        get
        {
            var root = BundleRoot;
            return string.IsNullOrEmpty(root) ? string.Empty : Path.Combine(root, "chapters");
        }
    }

    /// <summary>
    /// Is there a page to host? False means the renderer lane's bundle is not in this build - a
    /// perfectly ordinary state while wave 2 is being built, and one the window must survive.
    /// Missing index.html is checked as well as the folder: WebView2 maps a folder that exists and
    /// then serves a 404 for the page, which reads to the user as an empty white hole.
    /// </summary>
    public static bool BundlePresent
    {
        get
        {
            try
            {
                var root = BundleRoot;
                return !string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, "index.html"));
            }
            catch (Exception ex) { Log.Debug(ex, "[{Tag}] bundle probe failed", LogTag); return false; }
        }
    }

    /// <summary>
    /// Is there anything to READ, by either body? True when the hosted page exists OR at least one
    /// chapter file does. It is what <see cref="EmiOffers"/> asks before she is allowed to offer
    /// the book: a chip that opens a window with nothing in it is a dead chip.
    /// </summary>
    public static bool HasContent
    {
        get
        {
            try
            {
                if (BundlePresent) return true;
                var dir = ChaptersDir;
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir)
                       && Directory.EnumerateFiles(dir, "*.json").Any();
            }
            catch (Exception ex) { Log.Debug(ex, "[{Tag}] content probe failed", LogTag); return false; }
        }
    }

    // =====================================================================================
    //  the fail-soft chapter reader
    // =====================================================================================

    /// <summary>
    /// Every chapter on disk, in reading order, skipping anything unreadable.
    ///
    /// <para>NOTHING here throws and nothing here is cached: the folder is a handful of small files
    /// and it is read at most once per window. One malformed chapter costs that chapter and no
    /// more - the alternative (a parse that gives up on the folder) turns one bad merge into an
    /// empty book, which is the exact failure this whole file exists to prevent.</para>
    ///
    /// <para>Order is volume, then <c>order</c>, then id, so a chapter that forgot its <c>order</c>
    /// still lands somewhere stable instead of moving every time the directory is enumerated.</para>
    /// </summary>
    public static IReadOnlyList<CodexChapter> ReadChapters()
    {
        var list = new List<CodexChapter>();
        try
        {
            var dir = ChaptersDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Log.Debug("[{Tag}] no chapters folder at {Dir}", LogTag, dir);
                return list;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    var ch = JsonConvert.DeserializeObject<CodexChapter>(json);
                    if (ch == null) continue;
                    // A file with no id is unreachable by the bookmark and by "take me there";
                    // fall back to the file name rather than dropping the words on the floor.
                    if (string.IsNullOrWhiteSpace(ch.Id)) ch.Id = Path.GetFileNameWithoutExtension(file);
                    list.Add(ch);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[{Tag}] chapter {File} is unreadable, skipped", LogTag, Path.GetFileName(file));
                }
            }

            list.Sort((a, b) =>
            {
                int v = a.Volume.CompareTo(b.Volume);
                if (v != 0) return v;
                int o = a.Order.CompareTo(b.Order);
                if (o != 0) return o;
                return string.CompareOrdinal(a.Id ?? string.Empty, b.Id ?? string.Empty);
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{Tag}] chapter scan failed", LogTag);
        }
        return list;
    }

    // =====================================================================================
    //  the bookmark
    // =====================================================================================

    /// <summary>
    /// The chapter she last had open, or null. Persisted in <see cref="EmiState"/> and restored on
    /// open, so the book falls open where it was left.
    /// </summary>
    public static string? Bookmark
    {
        get
        {
            try
            {
                var id = EmiState.Current.CodexChapter;
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }
            catch (Exception ex) { Log.Debug(ex, "[{Tag}] bookmark read failed", LogTag); return null; }
        }
    }

    /// <summary>Remember the open chapter. Ignores blank ids and a repeat of what is already
    /// stored, so a page that re-announces its chapter cannot churn the state file.</summary>
    public static void NoteChapter(string? chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId)) return;
        try
        {
            var id = chapterId!.Trim();
            var s = EmiState.Current;
            if (string.Equals(s.CodexChapter, id, StringComparison.Ordinal)) return;
            s.CodexChapter = id;
            EmiState.SaveSoon();
        }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] bookmark write failed", LogTag); }
    }

    // =====================================================================================
    //  open / close, single instance
    // =====================================================================================

    private static ChaosWebViewHost? _host;
    private static EmiCodexWindow? _plain;

    /// <summary>True while either body is up. One book, one window, ever.</summary>
    public static bool IsOpen => _host != null || _plain != null;

    /// <summary>
    /// Open the book, or focus the copy that is already open.
    ///
    /// <para>The route is decided BEFORE anything is built, in the order that costs least when it
    /// fails: no WebView2 runtime and no bundle both skip straight to the native reader without
    /// ever constructing a control or creating a browser user-data folder (the probe-first pattern
    /// from <c>Controls/SpiralEmbedView</c>). Failures that can only appear later - a navigation
    /// that reports failure, a browser process that dies - fall back to the same reader from their
    /// own handlers.</para>
    /// </summary>
    /// <param name="chapterId">Chapter to open at. Null uses the bookmark.</param>
    public static void Open(string? chapterId = null)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => Open(chapterId)));
                return;
            }
            if (disp?.HasShutdownStarted == true) return;

            NoteChapter(chapterId);

            // SINGLE INSTANCE. Opening the book twice is opening the book once.
            if (_host != null) { _host.FocusWeb(); return; }
            if (_plain != null) { FocusPlain(); return; }

            EmiState.NoteCodexOpened();

            if (!BundlePresent)
            {
                Log.Information("[{Tag}] no bundle at {Root}, opening the plain reader", LogTag, BundleRoot);
                OpenPlain("bundle");
                return;
            }

            // PROBE BEFORE BUILDING (SpiralEmbedView:139). An install with no WebView2 runtime
            // must reach the reader without an exception trace and without a user-data folder.
            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (string.IsNullOrEmpty(version))
                {
                    Log.Information("[{Tag}] no WebView2 runtime, opening the plain reader", LogTag);
                    OpenPlain("runtime");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Information("[{Tag}] WebView2 runtime not installed ({E}), opening the plain reader",
                    LogTag, ex.Message);
                OpenPlain("runtime");
                return;
            }

            OpenHosted();
        }
        catch (Exception ex)
        {
            // Even the router fails soft: whatever went wrong above, the words are still readable.
            Log.Warning(ex, "[{Tag}] open failed, falling back to the plain reader", LogTag);
            try { OpenPlain("error"); } catch { }
        }
    }

    /// <summary>Close whichever body is up. Safe when nothing is.</summary>
    public static void Close()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(Close));
                return;
            }
            var host = _host;
            _host = null;
            if (host != null) { try { host.Dispose(); } catch { } }

            var plain = _plain;
            _plain = null;
            if (plain != null) { try { plain.Close(); } catch { } }
        }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] close failed", LogTag); }
    }

    // =====================================================================================
    //  the hosted body
    // =====================================================================================

    private static void OpenHosted()
    {
        var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
        {
            // Deny, not Allow: the book fetches only its own files from its own origin. Nothing in
            // it is uploaded to WebGL or decoded through WebAudio, so there is no CORS case to
            // answer and no reason to widen the mapping.
            (PageHost, WebRoot, CoreWebView2HostResourceAccessKind.Deny),
        };

        _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
        {
            StartUrl = StartUrl,
            PrimaryHost = PageHost,
            Mappings = mappings,
            UserDataFolderName = "browser_data_codex",
            InputEnabled = true,
            // A book is a titled, resizable, alt-tabbable window, never a screen-owner.
            StartFullscreen = false,
            WindowedWidth = 1000,
            WindowedHeight = 760,
            CenterOnMainWindow = true,
            // Deliberately NOT OwnedByMainWindow. "Take me there" navigates the control panel, and
            // a window glued above main would sit on top of the very thing it just pointed at.
            OwnedByMainWindow = false,
            WindowTitle = SafeLoc("emi_codex_title", "The Book"),
            LogTag = LogTag,
            OnCoreCreated = OnCoreCreated,
            OnMessage = OnPageMessage,
            OnProcessFailed = OnHostProcessFailed,
        });

        _host.Show();
        if (_host.Window != null)
            _host.Window.Closed += (_, _) => OnHostWindowClosed();

        Log.Information("[{Tag}] the book is open (hosted), bookmark={Chapter}", LogTag, Bookmark ?? "-");
    }

    /// <summary>
    /// The one seam before the first byte is fetched.
    ///
    /// <para>Two things ride here. The BOOKMARK goes in as a document-created global rather than as
    /// a message, because a message is a race the page can lose: <c>window.CCP_CODEX</c> is there
    /// before the page's first script runs, so the book can fall open at the right chapter on its
    /// very first paint. And <c>NavigationCompleted</c> is hooked here because the host does not
    /// forward it, and a navigation that reports failure is the one fault the fail-soft ladder
    /// cannot otherwise see (the page never loads, so it never says <c>codex:ready</c> either).</para>
    /// </summary>
    private static void OnCoreCreated(CoreWebView2 core)
    {
        try
        {
            core.NavigationCompleted += OnNavigationCompleted;
            _ = core.AddScriptToExecuteOnDocumentCreatedAsync(BootScript());
        }
        catch (Exception ex)
        {
            // Not fatal on its own: the page still loads and can ask for the bookmark over the
            // bridge. Logged loudly because losing the navigation hook loses a fail-soft rung.
            Log.Warning(ex, "[{Tag}] core seam failed", LogTag);
        }
    }

    /// <summary>
    /// <c>window.CCP_CODEX</c>, injected before the page's own scripts.
    /// Serialised through Newtonsoft so a chapter id with a quote in it cannot break the script.
    /// </summary>
    internal static string BootScript()
    {
        bool reduced = false;
        try { reduced = App.Settings?.Current?.MotionLevel == Models.MotionLevel.Off; }
        catch { /* the default (full motion) is right when settings are not up yet */ }
        return BuildBootScript(Bookmark, reduced);
    }

    /// <summary>
    /// The pure half of <see cref="BootScript"/>, split out so the script can be asserted without
    /// a settings singleton or a user state file behind it. Serialised through Newtonsoft rather
    /// than concatenated: a chapter id is a merged writer's file name, and a quote or a backslash
    /// in one must not be able to break the page's first script.
    /// </summary>
    internal static string BuildBootScript(string? bookmark, bool reducedMotion)
    {
        var payload = JsonConvert.SerializeObject(new
        {
            bookmark,
            manualUrl = ManualUrl,
            reducedMotion,
        });
        return "window.CCP_CODEX = " + payload + ";";
    }

    private static void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            if (e.IsSuccess) return;
            Log.Warning("[{Tag}] navigation failed ({Status}), falling back to the plain reader",
                LogTag, e.WebErrorStatus);
            FallBackToPlain("navigation");
        }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] navigation-completed handler failed", LogTag); }
    }

    private static void OnHostProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        Log.Warning("[{Tag}] browser process failed ({Kind}), falling back to the plain reader", LogTag, kind);
        FallBackToPlain("process");
    }

    /// <summary>The window was closed by the user (or by us). Drop the host so the next open
    /// builds a fresh one rather than focusing a corpse.</summary>
    private static void OnHostWindowClosed()
    {
        if (_host == null) return;
        var host = _host;
        _host = null;
        try { host.Dispose(); } catch { }
        Log.Debug("[{Tag}] the book closed", LogTag);
    }

    /// <summary>
    /// Tear the hosted body down and put the reader up in its place. Marshalled to the UI thread
    /// because both callers (a navigation result, a dead browser process) can arrive off it.
    /// </summary>
    private static void FallBackToPlain(string why)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => FallBackToPlain(why)));
                return;
            }
            if (_host == null && _plain == null) return;   // already closed; nothing to rescue

            var host = _host;
            _host = null;
            if (host != null) { try { host.Dispose(); } catch { } }

            if (_plain == null) OpenPlain(why);
        }
        catch (Exception ex) { Log.Warning(ex, "[{Tag}] fallback failed", LogTag); }
    }

    // =====================================================================================
    //  the native body
    // =====================================================================================

    private static void OpenPlain(string why)
    {
        try
        {
            if (_plain != null) { FocusPlain(); return; }
            var win = new EmiCodexWindow(why);
            _plain = win;
            win.Closed += (_, _) => { if (ReferenceEquals(_plain, win)) _plain = null; };

            // Owner, when there is a live one, so the reader is not lost behind the app. Set before
            // Show and inside its own try: a main window that is closing throws here, and a book
            // with no owner is a great deal better than no book.
            try
            {
                var main = Application.Current?.MainWindow;
                if (main != null && main.IsLoaded && !ReferenceEquals(main, win)) win.Owner = main;
            }
            catch (Exception ex) { Log.Debug(ex, "[{Tag}] owner not set on the plain reader", LogTag); }

            win.Show();
            Log.Information("[{Tag}] the book is open (plain reader, {Why})", LogTag, why);
        }
        catch (Exception ex)
        {
            _plain = null;
            Log.Warning(ex, "[{Tag}] the plain reader failed to open", LogTag);
        }
    }

    private static void FocusPlain()
    {
        try
        {
            var w = _plain;
            if (w == null) return;
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] focus failed", LogTag); }
    }

    /// <summary>Hand the website manual to the user's own browser. The reader's escape hatch, and
    /// the only network the book ever touches - by the OS, not by us.</summary>
    public static void OpenManualInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ManualUrl) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warning(ex, "[{Tag}] could not open the manual in a browser", LogTag); }
    }

    // =====================================================================================
    //  the bridge (page -> host)
    // =====================================================================================
    //
    // WAVE2-CONTRACT: exactly five messages, and every handler validates its payload against the
    // real catalogue before it does anything. A page that asks for a target nobody has heard of, or
    // a tour that does not parse, does NOTHING - it is never an exception and never a guess.
    //
    // Note that `ready` (the handshake ChaosWebViewHost consumes itself) and `codex:ready` are two
    // different messages. The contract's word is codex:ready, so it arrives here as an ordinary
    // envelope; the host's own IsReady flag stays false and its Post() queue is never used. That is
    // why the bookmark is injected as a document global instead (see BootScript).

    internal const string MsgReady = "codex:ready";
    internal const string MsgOpen = "codex:open";
    internal const string MsgTarget = "codex:target";
    internal const string MsgTour = "codex:tour";
    internal const string MsgClose = "codex:close";

    private static void OnPageMessage(JObject o)
    {
        try { HandleMessage((string?)o["type"], o); }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] bridge message failed", LogTag); }
    }

    /// <summary>
    /// The bridge, split from the transport so it can be tested without a browser.
    /// Returns true when the message was one of the five and was acted on.
    /// </summary>
    internal static bool HandleMessage(string? type, JObject payload)
    {
        switch (type)
        {
            case MsgReady:
                Log.Information("[{Tag}] the page is ready", LogTag);
                return true;

            case MsgOpen:
                NoteChapter((string?)payload["chapter"]);
                return true;

            case MsgTarget:
                return TakeMeThere((string?)payload["id"]);

            // `tour`, NOT `type`. WAVE2-CONTRACT's first bridge table said the tour name arrived as
            // `type`, which is the ENVELOPE's own key - line 624 above already dispatched on it, so
            // WalkMeThrough was handed the string "codex:tour" every time and the walk button was
            // dead in a way no test caught (an envelope that never parses still swallows quietly,
            // which is exactly what a "junk is ignored" test asserts). The contract is corrected:
            // `type` names the message and nothing else, and the tour member travels as `tour`.
            case MsgTour:
                return WalkMeThrough(TourNameOf(payload));

            case MsgClose:
                Close();
                return true;

            default:
                Log.Debug("[{Tag}] unknown page message {Type}, ignored", LogTag, type);
                return false;
        }
    }

    /// <summary>
    /// "TAKE ME THERE". Tier-aware by construction: an id nothing knows about, or a door this
    /// build does not have, does nothing at all, and a LOCKED door shows the app's own refusal
    /// instead of opening.
    ///
    /// <para>The refusal is not re-implemented here - the target's own <c>Open</c> action goes
    /// through <c>EmiTargets.Pick</c>, which re-probes the lock, raises the tier gate and fires
    /// <c>lockedCardTapped</c>. One law, one place. All this method decides is whether the door is
    /// reachable at all, and it answers the page either way so a chapter can grey its own button.</para>
    /// </summary>
    internal static bool TakeMeThere(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var target = EmiTargets.Find(id);
        if (target == null)
        {
            Log.Debug("[{Tag}] page asked for unknown target {Id}, ignored", LogTag, id);
            return false;
        }
        if (!target.Available)
        {
            Log.Debug("[{Tag}] target {Id} is not part of this build, ignored", LogTag, id);
            return false;
        }

        bool locked = target.Locked;
        try { target.Open(); }
        catch (Exception ex) { Log.Warning(ex, "[{Tag}] target {Id} failed to open", LogTag, id); return false; }

        Log.Information("[{Tag}] take me there: {Id} (locked={Locked})", LogTag, id, locked);
        return !locked;
    }

    /// <summary>
    /// "WALK ME THROUGH IT". The tour arrives as a <c>TutorialType</c> NAME, never an ordinal - an
    /// ordinal moves the day somebody inserts a value into the middle of the enum, and a book that
    /// is a build behind would then start the wrong tour. Unparseable names do nothing.
    ///
    /// <para>The book closes on the way: the tutorial overlay owns the screen from here, and a
    /// window sitting over the thing being pointed at is the whole failure mode.</para>
    /// </summary>
    internal static bool WalkMeThrough(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        if (!TryParseTour(typeName, out var type))
        {
            Log.Debug("[{Tag}] page asked for unknown tour {Type}, ignored", LogTag, typeName);
            return false;
        }
        // Application.MainWindow is dispatcher-affine and THROWS across threads rather than
        // answering null. Bridge messages arrive on the UI thread today, so this is belt and
        // braces - but this file's law is that no path throws into a caller, and an internal
        // method the tests drive directly deserves the same promise the transport gets.
        MainWindow? main = null;
        try { main = Application.Current?.MainWindow as MainWindow; }
        catch (Exception ex)
        {
            Log.Debug(ex, "[{Tag}] tour {Type} skipped: no main window reachable from here", LogTag, type);
            return false;
        }
        if (main is null)
        {
            Log.Debug("[{Tag}] tour {Type} skipped: no main window", LogTag, type);
            return false;
        }

        Close();
        try { main.StartTutorial(type); }
        catch (Exception ex) { Log.Warning(ex, "[{Tag}] tour {Type} failed to start", LogTag, type); return false; }
        Log.Information("[{Tag}] walk me through it: {Type}", LogTag, type);
        return true;
    }

    /// <summary>
    /// Reads the tour name out of a bridge envelope. Named and separate so the collision above
    /// is pinnable: every other route to it returns false in a test either way (no main window),
    /// so a test that only asserts "junk is ignored" passes whether the key is right or wrong.
    /// </summary>
    internal static string? TourNameOf(JObject payload) => (string?)payload["tour"];

    /// <summary>
    /// Strict, case-insensitive <c>TutorialType</c> name parsing. Strict about DIGITS on purpose:
    /// <c>Enum.TryParse</c> happily accepts "12" and any other number as a valid enum value, which
    /// would let a page start whichever tour happened to sit at that ordinal today.
    /// </summary>
    internal static bool TryParseTour(string? name, out TutorialType type)
    {
        type = default;
        var s = (name ?? string.Empty).Trim();
        if (s.Length == 0) return false;
        if (s.Any(c => char.IsDigit(c) || c == '-' || c == '+' || c == ',')) return false;
        return Enum.TryParse(s, ignoreCase: true, out type) && Enum.IsDefined(typeof(TutorialType), type);
    }

    // =====================================================================================
    //  her offer
    // =====================================================================================

    /// <summary>
    /// Fire the two-chip "want the book?" ask, if she is allowed to.
    ///
    /// <para>Three brakes, and the moment's own <c>limit: ever/1</c> in desk-lines.json is a
    /// fourth that this method cannot reach around: there has to BE a book (an offer that opens an
    /// empty window is the dead chip the whole feasibility law exists to prevent), it must not
    /// already be open, and she does not offer a book the user has already been reading.</para>
    ///
    /// <para>WIRED FROM <c>EmiTourNarrator.OnFinished</c>, through <see cref="MaybeOfferSoon"/>, on
    /// any tour ending - walked or skipped. Both are the right beat: somebody who took the walk has
    /// just been shown the app and is the likeliest reader there will ever be, and somebody who
    /// skipped it has just said they would rather not be walked, which is precisely what a book is
    /// for. The summon greeting was the other candidate and is the wrong one: it already fires two
    /// moments back to back and lets the floor arbitrate, and a ceremony-priority offer ignores
    /// that floor, so it would take the greeting's place rather than follow it.</para>
    /// </summary>
    /// <summary>
    /// The gap between whatever just spoke and her offer of the book.
    ///
    /// <para>It cannot be zero, and that is the whole reason this method exists.
    /// <c>bookOffer</c> is priority 3, which makes it a CEREMONY to the engine and therefore
    /// exempt from the 45-second global floor (<c>EmiLineEngine.DrawCore</c> step 6). An offer
    /// fired in the same breath as the line before it does not queue politely behind that line -
    /// it lands on top of it, and the beat it was meant to follow is never read.</para>
    /// </summary>
    private const int OfferDelayMs = 20_000;

    private static System.Windows.Threading.DispatcherTimer? _offerTimer;

    /// <summary>
    /// Offer the book one beat after something else has finished speaking. Additive by design:
    /// the caller still fires its own moment first and this rides behind it, so a caller can never
    /// trade a line it was already going to say for silence.
    ///
    /// <para>Deliberately NOT a suppress-and-replace. The engine takes a moment's limit at step 4
    /// and applies the floor at step 6, so a fire the floor swallows still spends the budget - and
    /// <c>bookOffer</c> is <c>limit: ever/1</c>, a budget of exactly one for the life of an
    /// account. Anything that suppresses the surrounding line to make room has to be able to prove
    /// the offer landed, and <c>Fire</c> returns void, so it cannot.</para>
    ///
    /// <para>Self-healing: <c>Fire</c> drops a moment outright when she is not out, BEFORE the
    /// engine draws, so an offer that arrives while she is away costs nothing and the next caller
    /// tries again. The re-arm is on purpose too - a second tour ending inside the window replaces
    /// the pending offer rather than queueing a second one.</para>
    /// </summary>
    public static void MaybeOfferSoon(string? why = null, int delayMs = OfferDelayMs)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => MaybeOfferSoon(why, delayMs)));
                return;
            }

            // The only pre-check worth making here. Everything else that could stop the offer can
            // still change during the delay, so it is asked at the tick instead - a book that is
            // not in this build cannot arrive in twenty seconds.
            if (!HasContent) return;

            _offerTimer?.Stop();
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(0, delayMs))
            };
            timer.Tick += (_, _) =>
            {
                try
                {
                    timer.Stop();
                    if (ReferenceEquals(_offerTimer, timer)) _offerTimer = null;
                    MaybeOffer(why);
                }
                catch (Exception ex) { Log.Debug(ex, "[{Tag}] the delayed offer failed", LogTag); }
            };
            _offerTimer = timer;
            timer.Start();
        }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] could not schedule the offer", LogTag); }
    }

    public static void MaybeOffer(string? why = null)
    {
        try
        {
            if (!HasContent) { Log.Debug("[{Tag}] no offer: there is no book in this build", LogTag); return; }
            if (IsOpen) { Log.Debug("[{Tag}] no offer: the book is already open", LogTag); return; }
            if (EmiState.Current.CodexOpens > 0) { Log.Debug("[{Tag}] no offer: they have read it", LogTag); return; }

            App.EmiDesk?.Fire("bookOffer", new { why = why ?? "idle" });
        }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] offer failed", LogTag); }
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================

    /// <summary>A localised string that can never throw and never comes back blank. The window
    /// title is read before <c>InitializeComponent</c> on the plain path and during host setup on
    /// the other, both of which can precede a loaded language file.</summary>
    internal static string SafeLoc(string key, string fallback)
    {
        try
        {
            var s = Localization.Loc.Get(key);
            return string.IsNullOrWhiteSpace(s) || string.Equals(s, key, StringComparison.Ordinal) ? fallback : s;
        }
        catch { return fallback; }
    }
}
