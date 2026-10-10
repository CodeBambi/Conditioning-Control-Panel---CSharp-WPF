using System;
using System.Collections.Generic;
using ConditioningControlPanel.Controls.Billboard;

namespace ConditioningControlPanel.Services.Billboard
{
    /// <summary>
    /// The Tonight Board's startup wiring: every art view registered once and the provider list the
    /// deck asks. Started lazily by the Home billboard the first time the fold settles (that is at
    /// load), so it costs nothing in a run that never shows Home.
    ///
    /// <para>Each provider starts on its own: one that throws is logged and the rest still load.</para>
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
                HouseProvider.OpenBackRoom = OpenBackRoomInApp;
                Add(new HouseProvider());

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
            }
        }

        /// <summary>
        /// The room's own door, the one the Play tab uses (its open-failed prompt included).
        /// Signed out, the sign-in dialog comes first, the way the live join card does it.
        /// </summary>
        private static void OpenBackRoomInApp()
        {
            if (BackRoom.BackRoomApi.AppIdentity() == null) { App.MainWindowRef?.OpenUnifiedLoginDialog(); return; }
            if (App.MainWindowRef is { } main) main.LaunchPlayBackRoom();
            else BackRoom.BackRoomHostService.Launch();
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
