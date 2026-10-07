using System;
using System.Collections.Generic;
using ConditioningControlPanel.Controls.Billboard;

namespace ConditioningControlPanel.Services.Billboard
{
    /// <summary>The art keys the deck lane registers itself. Providers pick one of these (or a
    /// key another lane registers: "board", "clip").</summary>
    public static class BuiltInArtKeys
    {
        /// <summary>A 16:9 poster. ArtData = a resource path under Resources/, e.g. "billboard/loom.png".</summary>
        public const string Poster = "poster";

        /// <summary>Open tables sliding in. ArtData = optional {count} (IReadOnlyDictionary of string to int, 1..3 rows drawn) or names.</summary>
        public const string Tables = "tables";

        /// <summary>A prize wheel turning. ArtData = optional {done, total}: a pip per slot under it.</summary>
        public const string Wheel = "wheel";

        /// <summary>A four-arm spiral in the card hue. ArtData = optional second hue "#rrggbb", or null.</summary>
        public const string Spiral = "spiral";

        /// <summary>A month of days. ArtData = {counted, need, days, today} (Locktober), {day, days} (a program), or null.</summary>
        public const string Calendar = "calendar";

        /// <summary>Four tiles, one switching on and off under a click ring. ArtData = the tip id (unused).</summary>
        public const string Tip = "tip";

        public static readonly IReadOnlyList<string> All = new[] { Poster, Tables, Wheel, Spiral, Calendar, Tip };
    }

    /// <summary>
    /// The Tonight Board's startup wiring: every art view registered once and the provider list the
    /// deck asks. Started lazily by the Home billboard the first time the fold settles (that is at
    /// load), so it costs nothing in a run that never shows Home.
    ///
    /// <para>The board, showcase and providers lanes land their types separately. Their calls sit
    /// behind <c>BILLBOARD_LANES_MERGED</c> so the deck builds alone; the coordinator removes the
    /// guard after the merge. Names as the lanes handed them back: Board.BoardArtRegistration +
    /// Board.BoardProvider (its service is the static BoardService.Shared), Showcase.ShowcaseArtRegistration
    /// + Showcase.ShowcaseProvider, Providers.BillboardProviders.CreateAll(); all parameterless.</para>
    /// </summary>
    public static class BillboardWiring
    {
        private static readonly object Gate = new();
        private static readonly List<IBillboardProvider> List = new();
        private static bool _started;

        /// <summary>Every provider the deck asks, in a fixed order.</summary>
        public static IReadOnlyList<IBillboardProvider> Providers
        {
            get { lock (Gate) return List.ToArray(); }
        }

        /// <summary>Raised when any provider's state moves. The deck rebuilds at its next change.</summary>
        public static event EventHandler? ProvidersChanged;

        /// <summary>Registers the art and builds the provider list, once. Safe to call again.</summary>
        public static void Start()
        {
            lock (Gate)
            {
                if (_started) return;
                _started = true;

                BuiltInArt.Register();
                Add(new HouseProvider());

#if BILLBOARD_LANES_MERGED
                try { Board.BoardArtRegistration.Register(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Billboard: board art did not register"); }
                try { Showcase.ShowcaseArtRegistration.Register(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Billboard: clip art did not register"); }
                try { Add(new Board.BoardProvider()); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Billboard: board provider did not start"); }
                try { Add(new Showcase.ShowcaseProvider()); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Billboard: showcase provider did not start"); }
                try { foreach (var p in global::ConditioningControlPanel.Services.Billboard.Providers.BillboardProviders.CreateAll()) Add(p); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Billboard: providers did not start"); }
#endif
            }
        }

        /// <summary>The viewer's plan, as the header spark reads it: Prime = Lab access, Basic =
        /// Premium access, else Free.</summary>
        public static BillboardTier CurrentTier()
        {
            try
            {
                var patreon = App.Patreon;
                if (patreon?.HasLabAccess == true) return BillboardTier.Prime;
                if (patreon?.HasPremiumAccess == true) return BillboardTier.Basic;
            }
            catch (Exception ex) { App.Logger?.Debug("Billboard tier read failed: {E}", ex.Message); }
            return BillboardTier.Free;
        }

        /// <summary>What the providers see right now.</summary>
        public static BillboardContext Context() => new(CurrentTier(), DateTime.UtcNow, DateTime.Now);

        private static void Add(IBillboardProvider provider)
        {
            List.Add(provider);
            provider.Changed += (_, _) =>
            {
                try { ProvidersChanged?.Invoke(provider, EventArgs.Empty); }
                catch (Exception ex) { App.Logger?.Debug("Billboard provider change hook failed: {E}", ex.Message); }
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
