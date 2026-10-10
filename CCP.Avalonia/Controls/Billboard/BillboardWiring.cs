using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Avalonia.Controls.Billboard.Scenes;
using ConditioningControlPanel.Services.Billboard.Showcase;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// The Tonight Board's startup wiring (WPF 7.1.5 Services/Billboard/BillboardWiring): every art
    /// view registered once and the provider list the deck asks. Started lazily by the Home
    /// billboard the first time it shows, so it costs nothing in a run that never shows Home.
    ///
    /// <para>Each provider starts on its own: one that throws is logged and the rest still load.
    /// The order is WPF's: House, Board, Showcase, then the head's adapters (Live, Waiting, Resume,
    /// Event), then Tip. The Showcase provider reaches the network (its manifest and clips sit on a
    /// GitHub release), so it joins only in a running app, never in a headless test host.</para>
    /// </summary>
    public static class BillboardWiring
    {
        private static readonly object Gate = new();
        private static readonly List<IBillboardProvider> List = new();
        private static bool _started, _artRegistered;

        /// <summary>Every provider the deck asks, in a fixed order.</summary>
        public static IReadOnlyList<IBillboardProvider> Providers
        {
            get { lock (Gate) return List.ToArray(); }
        }

        /// <summary>Raised when any provider's state moves. The deck rebuilds at its next change.</summary>
        public static event EventHandler? ProvidersChanged;

        /// <summary>The head's extra providers (the adapters over this head's services), set once by
        /// the shell before <see cref="Start"/>. Null = none.</summary>
        public static Func<IEnumerable<IBillboardProvider>>? HeadProviders { get; set; }

        /// <summary>Registers every art view this head draws. Idempotent.</summary>
        public static void RegisterArt()
        {
            lock (Gate)
            {
                if (_artRegistered) return;
                _artRegistered = true;
                BoardPng.Install();
                try { BillboardArt.Register(BoardProvider.ArtKey, data => new BoardTileView(data)); }
                catch (Exception ex) { Log.Warning(ex, "Billboard: board art did not register"); }
                // WPF ShowcaseArtRegistration.Register: the "clip" key.
                try { BillboardArt.Register(ShowcaseRules.ArtKey, data => new ClipArtView(data as ShowcaseClipArt)); }
                catch (Exception ex) { Log.Warning(ex, "Billboard: clip art did not register"); }
                try { BuiltInArt.Register(); }
                catch (Exception ex) { Log.Warning(ex, "Billboard: built-in art did not register"); }
            }
        }

        /// <summary>Registers the art and builds the provider list, once. Safe to call again.</summary>
        public static void Start()
        {
            RegisterArt();
            lock (Gate)
            {
                if (_started) return;
                _started = true;

                Add(new HouseProvider());
                try { Add(new BoardProvider()); }
                catch (Exception ex) { Log.Warning(ex, "Billboard: board provider did not start"); }
                try { if (ShowcaseLive()) Add(new ShowcaseProvider()); }
                catch (Exception ex) { Log.Warning(ex, "Billboard: showcase provider did not start"); }
                try
                {
                    if (HeadProviders?.Invoke() is { } extra)
                        foreach (var p in extra) Add(p);
                }
                catch (Exception ex) { Log.Warning(ex, "Billboard: head providers did not start"); }
                try { Add(new TipProvider()); }
                catch (Exception ex) { Log.Warning(ex, "Billboard: tip provider did not start"); }
            }
        }

        /// <summary>
        /// Whether the Showcase provider may join: only a running app (a controlled lifetime). A
        /// headless test host has none, so a test never fetches the manifest or a clip. WPF asks no
        /// media consent for these (they are the app's own clips, not the user's media source), and
        /// neither does this head.
        /// </summary>
        internal static Func<bool> ShowcaseLive { get; set; } = () =>
            global::Avalonia.Application.Current?.ApplicationLifetime
                is global::Avalonia.Controls.ApplicationLifetimes.IControlledApplicationLifetime;

        /// <summary>The viewer's plan, as the header spark reads it: Prime = Lab access, Basic =
        /// Premium access, else Free. The shell sets the reader (it owns the Patreon service).</summary>
        public static Func<BillboardTier>? TierReader { get; set; }

        public static BillboardTier CurrentTier()
        {
            try { return TierReader?.Invoke() ?? BillboardTier.Free; }
            catch (Exception ex) { Log.Debug("Billboard tier read failed: {E}", ex.Message); return BillboardTier.Free; }
        }

        /// <summary>What the providers see right now.</summary>
        public static BillboardContext Context() => new(CurrentTier(), DateTime.UtcNow, DateTime.Now);

        private static void Add(IBillboardProvider provider)
        {
            List.Add(provider);
            provider.Changed += (_, _) =>
            {
                try { ProvidersChanged?.Invoke(provider, EventArgs.Empty); }
                catch (Exception ex) { Log.Debug("Billboard provider change hook failed: {E}", ex.Message); }
            };
        }

        /// <summary>Test seam: forget the providers and the start.</summary>
        internal static void ResetForTests()
        {
            lock (Gate)
            {
                List.Clear();
                _started = false;
                ProvidersChanged = null;
            }
        }
    }
}
