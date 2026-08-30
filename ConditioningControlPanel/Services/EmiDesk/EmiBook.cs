using System;
using System.Windows;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// HER BOOK: the router in front of <see cref="EmiBookWindow"/>.
///
/// <para><b>What changed, and why.</b> Wave 2 built the book as a codex: a WebView2 window with a
/// spine, a page turn and 450 to 650 words a chapter. The owner rejected the shape on sight
/// (2026-08-30) and asked for something drawn beside her instead, broken down for somebody who
/// will not read a wall of text, with a picture per feature. So the book is now a panel anchored to
/// her body, one card per feature, and every card carries a little 8-bit loop that SHOWS the
/// feature happening. <see cref="EmiCodex"/> still exists and still owns the old bundle; nothing
/// routes to it any more, and wave C deletes it.</para>
///
/// <para><b>It needs her.</b> The panel has no position of its own - it is anchored to
/// <c>BodyScreenRect</c> and follows her Moved and Resized events - so opening it while she is away
/// would put a window in the top-left corner of nowhere. All three entry points (the ? chip, the
/// <c>codex</c> row in her cards, the <c>bookOffer</c> ask) are only reachable while she is out, so
/// this is a guard against a future caller rather than against a live path.</para>
///
/// <para><b>Single instance.</b> Opening the book twice is opening the book once, same as the codex
/// before it. A second Open on a live book navigates it to the requested card instead of building a
/// second window.</para>
/// </summary>
public static class EmiBook
{
    private const string LogTag = "EmiDesk";

    /// <summary>The <see cref="EmiTargets"/> row that opens the book. Kept as <c>codex</c>: the id
    /// is a persisted usage-score key and renaming one silently resets that feature's score.</summary>
    public const string TargetId = "codex";

    /// <summary>The moment her offer to open it is raised on.</summary>
    public const string OfferMoment = "bookOffer";

    /// <summary>The effect verb a YES on that offer carries.</summary>
    public const string OpenEffect = "book:open";

    private static EmiBookWindow? _window;

    /// <summary>True while the book is on screen.</summary>
    public static bool IsOpen => _window != null;

    /// <summary>
    /// Which side of her the panel took: <c>-1</c> her LEFT, <c>+1</c> her RIGHT, <c>0</c> when the
    /// book is not up. Her speech bubble reads this so it can open AWAY from the panel (see the
    /// dodge in <c>EmiDeskWindow.LayoutBubble</c>).
    ///
    /// <para>An int rather than the window on purpose. The coupling runs ONE way: the bubble asks
    /// the book where it landed, and nothing in the book ever reaches back at the bubble.</para>
    /// </summary>
    public static int SideOfHer
    {
        get
        {
            try { return _window?.SideOfHer ?? 0; }
            catch (Exception ex) { Log.Debug(ex, "[{Tag}] book side read failed", LogTag); return 0; }
        }
    }

    /// <summary>
    /// Raised when the book opens, folds, or flips to her other side mid-drag. A broadcast, not a
    /// call: the book says where it landed, and whoever cares re-reads <see cref="SideOfHer"/> for
    /// itself. Nothing on this side knows the bubble exists.
    /// </summary>
    public static event EventHandler? SideChanged;

    /// <summary>Announce a new <see cref="SideOfHer"/>. A subscriber that throws is swallowed here
    /// rather than taking the book's placement down with it.</summary>
    internal static void NoteSideChanged()
    {
        try { SideChanged?.Invoke(null, EventArgs.Empty); }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] book side handler threw", LogTag); }
    }

    /// <summary>The live panel, for the offscreen shot rig ONLY (<c>--shoot-book</c>). Nothing in
    /// the app reaches past this router for the window; see <c>Services/Dev/BookShooter.cs</c>.</summary>
    internal static EmiBookWindow? Live => _window;

    /// <summary>
    /// True when there is anything to read. The cards are compiled in rather than loaded off disk,
    /// so unlike the codex this cannot be false on a broken install - but the offer still asks,
    /// because a wave that ships zero cards must not put a dead chip on her glass.
    /// </summary>
    public static bool HasContent => EmiBookCards.All.Count > 0;

    /// <summary>The card she last had open, or null.</summary>
    public static string? Bookmark
    {
        get
        {
            try
            {
                var id = EmiState.Current.BookCard;
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }
            catch (Exception ex) { Log.Debug(ex, "[{Tag}] book bookmark read failed", LogTag); return null; }
        }
    }

    /// <summary>Remember the open card. Ignores blanks and a repeat of what is already stored, so
    /// flipping back to where you started cannot churn the state file.</summary>
    public static void NoteCard(string? cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return;
        try
        {
            var id = cardId!.Trim();
            var s = EmiState.Current;
            if (string.Equals(s.BookCard, id, StringComparison.Ordinal)) return;
            s.BookCard = id;
            EmiState.SaveSoon();
        }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] book bookmark write failed", LogTag); }
    }

    // =====================================================================================
    //  open / close
    // =====================================================================================

    /// <summary>
    /// Open the book beside her, at <paramref name="cardId"/> or at the bookmark.
    /// </summary>
    public static void Open(string? cardId = null)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => Open(cardId)));
                return;
            }
            if (disp?.HasShutdownStarted == true) return;

            if (!HasContent)
            {
                Log.Warning("[{Tag}] the book has no cards, nothing to open", LogTag);
                return;
            }

            // Already up: go to the card rather than building a second panel.
            if (_window != null)
            {
                _window.GoTo(cardId ?? Bookmark);
                return;
            }

            var svc = App.EmiDesk;
            var owner = svc?.Window;
            if (owner == null || svc?.IsOut != true)
            {
                Log.Information("[{Tag}] the book was asked for while she is away, ignored", LogTag);
                return;
            }

            EmiState.NoteCodexOpened();

            var win = new EmiBookWindow(owner);
            win.Closed += OnWindowClosed;
            _window = win;
            win.OpenBook(cardId ?? Bookmark);

            // The panel's own placement announces a FLIP; this covers the ordinary open, where it
            // landed on her right and nothing about _onHerLeft changed but the answer to
            // SideOfHer did (0 -> +1). A double announce is a re-read, which is free.
            NoteSideChanged();

            Log.Information("[{Tag}] book open at {Card}", LogTag, cardId ?? Bookmark ?? "(first)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{Tag}] the book failed to open", LogTag);
            try { Close(); } catch { /* nothing else to try */ }
        }
    }

    /// <summary>Fold the book. Safe when it is not up.</summary>
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

            var win = _window;
            _window = null;
            if (win == null) return;
            win.Closed -= OnWindowClosed;
            win.Kill();

            // Announced the moment the reference drops, not when the fold animation finishes: a
            // bubble that waits for the fold reads as lag, and the panel is already shrinking away
            // from wherever the bubble is about to go.
            NoteSideChanged();
        }
        catch (Exception ex) { Log.Debug(ex, "[{Tag}] book close failed", LogTag); }
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        // The window can go away without anybody calling Close: she was dismissed, or the app is
        // shutting down. Drop the reference so the next Open builds a live one.
        if (!ReferenceEquals(sender, _window)) return;
        _window = null;
        NoteSideChanged();
    }
}
