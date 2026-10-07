using System;
using System.Collections.Generic;
using System.Windows;

namespace ConditioningControlPanel.Services.Billboard
{
    // The Tonight Board's shared vocabulary. Every lane builds on these types and nothing
    // else crosses a lane boundary. Change them only through the base branch.
    // Design page: https://claude.ai/artifact/HjF7nno2cxiG6Bopjg8KWr

    /// <summary>What a card is for. Also its place in the deck: lower sorts first.</summary>
    public enum BillboardCardKind
    {
        Live = 0,
        Board = 1,
        Waiting = 2,
        Resume = 3,
        Event = 4,
        Showcase = 5,
        Tip = 6,
        House = 7,
    }

    /// <summary>The viewer's plan, as the deck needs it.</summary>
    public enum BillboardTier { Free, Basic, Prime }

    /// <summary>A small tag drawn in the card's top-left corner.</summary>
    public enum BillboardBadge { None, New, Basic, Prime }

    /// <summary>What the card's one button does.</summary>
    public enum BillboardActionKind
    {
        /// <summary>No button. The card is only something to read.</summary>
        None,

        /// <summary>A <c>ShowTab</c> key in this window.</summary>
        Tab,

        /// <summary>An absolute https address in the user's browser. cclabs.app or the Discord invite only.</summary>
        Link,

        /// <summary>A launcher game id (<c>LauncherCatalogue</c>), started the way the launcher starts it.</summary>
        Launch,

        /// <summary>A provider-owned action, run through <see cref="IBillboardProvider.Invoke"/>.</summary>
        Callback,
    }

    /// <param name="Kind">What pressing the button does.</param>
    /// <param name="Target">Tab key, address, game id, or the provider's own action id.</param>
    /// <param name="Label">The button text, already localised.</param>
    public sealed record BillboardAction(BillboardActionKind Kind, string Target, string Label)
    {
        public static readonly BillboardAction None = new(BillboardActionKind.None, string.Empty, string.Empty);
    }

    /// <summary>
    /// One card, as a provider hands it to the deck. Text arrives already localised and
    /// already filled ("3 open tables"). <see cref="ArtKey"/> picks the art view from
    /// <see cref="BillboardArt"/>; <see cref="ArtData"/> is whatever that view needs.
    /// </summary>
    /// <param name="Id">Stable per card, used for snooze and logs. Never shown.</param>
    /// <param name="Kind">Deck section.</param>
    /// <param name="Priority">Order inside a section, lower first.</param>
    /// <param name="Eyebrow">Small mono line above the title.</param>
    /// <param name="Title">The headline.</param>
    /// <param name="Line">One line under the title. May be empty.</param>
    /// <param name="AccentHex">The card's hue, "#rrggbb": sparks, the push glint, the chip.</param>
    /// <param name="ArtKey">Registered art view key, e.g. "board", "clip", "tables", "poster".</param>
    /// <param name="ArtData">Payload for that art view.</param>
    /// <param name="Action">The card's one button.</param>
    /// <param name="Badge">Corner tag.</param>
    /// <param name="Snoozable">Whether the x shows. Board cards are not snoozable.</param>
    public sealed record BillboardCardSpec(
        string Id,
        BillboardCardKind Kind,
        int Priority,
        string Eyebrow,
        string Title,
        string Line,
        string AccentHex,
        string ArtKey,
        object? ArtData,
        BillboardAction Action,
        BillboardBadge Badge = BillboardBadge.None,
        bool Snoozable = true);

    /// <summary>What a provider may look at when it builds its cards.</summary>
    public sealed record BillboardContext(BillboardTier Tier, DateTime NowUtc, DateTime NowLocal);

    /// <summary>
    /// A source of cards. Providers are cheap to ask: <see cref="Current"/> reads state the
    /// app already holds and never blocks on the network. When their state changes they
    /// raise <see cref="Changed"/> and the deck asks again at the next card change.
    /// </summary>
    public interface IBillboardProvider
    {
        string Id { get; }

        IEnumerable<BillboardCardSpec> Current(BillboardContext context);

        /// <summary>Runs a <see cref="BillboardActionKind.Callback"/> action this provider issued.</summary>
        void Invoke(string actionTarget);

        event EventHandler? Changed;
    }

    /// <summary>
    /// The art views a card can wear, by key. Each lane registers its own views once at
    /// startup; the deck asks for a fresh view per card. A view that implements
    /// <see cref="IBillboardArtView"/> gets play/pause and is told when it leaves.
    /// </summary>
    public static class BillboardArt
    {
        private static readonly Dictionary<string, Func<object?, FrameworkElement>> Factories =
            new(StringComparer.OrdinalIgnoreCase);

        public static void Register(string key, Func<object?, FrameworkElement> factory) => Factories[key] = factory;

        public static bool IsRegistered(string key) => Factories.ContainsKey(key);

        /// <summary>A new view for the key, or null when no lane registered it.</summary>
        public static FrameworkElement? Create(string key, object? data) =>
            Factories.TryGetValue(key, out var make) ? make(data) : null;

        /// <summary>Test seam.</summary>
        internal static void ResetForTests() => Factories.Clear();
    }

    /// <summary>Optional lifecycle for an art view. All calls come on the UI thread.</summary>
    public interface IBillboardArtView
    {
        /// <summary>The card is on screen and motion is allowed: start clocks, players, loops.</summary>
        void Play();

        /// <summary>Hover, another tab, or Motion Off: hold still on the current frame.</summary>
        void Pause();

        /// <summary>The card left the screen for good. Release clocks, players, bitmaps.</summary>
        void Release();

        /// <summary>A pointer press on the art, in 0..1 coordinates of the view. Most views ignore it.</summary>
        void Touch(Point normalized);
    }

    // ---- The pixel board, as the server sends it -------------------------------------

    /// <summary>Who a board post is for.</summary>
    public enum BoardAudience { Everyone, Free, Patrons }

    /// <summary>
    /// The board block on <c>GET /config/marquee</c>. The picture itself is
    /// <c>GET /config/board/{Version}.png</c>, immutable, fetched once per version.
    /// The PNG is RGBA, at most 64 x 36 per frame; <see cref="Frames"/> frames sit side by side.
    /// </summary>
    /// <param name="Version">Bumps on every post. The cache key.</param>
    /// <param name="Fx">Effect names, see <see cref="BoardFx"/>. Unknown names are ignored.</param>
    /// <param name="UntilUtc">The post retires itself at this time. Null = until cleared.</param>
    /// <param name="Link">Button target: "lobby", "premium", "discord", "tab:&lt;key&gt;", a cclabs.app address, or null for no button.</param>
    /// <param name="Audience">Who sees it.</param>
    /// <param name="Frames">Frames in the strip, 1..8.</param>
    /// <param name="Fps">Frame rate for a strip, 1..12.</param>
    /// <param name="Width">Width of ONE frame in pixels.</param>
    /// <param name="Height">Height in pixels.</param>
    public sealed record BoardPost(
        int Version,
        IReadOnlyList<string> Fx,
        DateTime? UntilUtc,
        string? Link,
        BoardAudience Audience,
        int Frames,
        int Fps,
        int Width,
        int Height);

    /// <summary>
    /// Effect names a post may carry. All of them are computed in the app from the picture's
    /// own colours, never from palette indices, so any PNG works.
    /// </summary>
    public static class BoardFx
    {
        /// <summary>A crest of lifted tiles rolls left to right through the mosaic.</summary>
        public const string Ola = "ola";

        /// <summary>Bright pixels (luma over 0.8) shimmer on their own clocks.</summary>
        public const string Twinkle = "twinkle";

        /// <summary>Saturated pixels walk through the hue wheel in a band moving across.</summary>
        public const string Cycle = "cycle";

        /// <summary>A diagonal glint sweeps the lit tiles.</summary>
        public const string Shine = "shine";

        /// <summary>Lit tiles on the outer edge light up in a running chase.</summary>
        public const string Chase = "chase";

        /// <summary>Columns bob up and down like a flag.</summary>
        public const string Wave = "wave";

        /// <summary>Scanlines, a vignette and a faint flicker over the tiles.</summary>
        public const string Crt = "crt";

        public static readonly IReadOnlyList<string> All = new[] { Ola, Twinkle, Cycle, Shine, Chase, Wave, Crt };
    }
}
