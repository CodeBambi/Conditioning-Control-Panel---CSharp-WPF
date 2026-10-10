using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>
    /// The Tonight Board's app-side providers (lane 4): Live, Waiting, Resume, Event and Tip.
    /// Each one is a pure decision (a static <c>*Cards</c> class, tested) plus a thin adapter
    /// over state the app already holds. None of them calls the network, and none of them blocks:
    /// what needs a disk read is read once in the background and cached.
    /// </summary>
    public static class BillboardProviders
    {
        /// <summary>Every provider of this lane, in deck order. Call once, on the UI thread, after
        /// the App services exist. A provider whose service is missing just has nothing to say.</summary>
        public static IReadOnlyList<IBillboardProvider> CreateAll() => new IBillboardProvider[]
        {
            new LiveProvider(),
            new WaitingProvider(),
            new ResumeProvider(),
            new EventProvider(),
            new TipProvider(),
        };
    }

    /// <summary>The card hues, one per section (the mockup's).</summary>
    public static class CardHues
    {
        public const string Live = "#5fe3ff";
        public const string Waiting = "#ffc94a";
        public const string Resume = "#ff4fa8";
        public const string Event = "#ff4fa8";
        public const string Tip = "#9b7bff";
    }

    /// <summary>The art keys a provider may name (registered by the deck lane).</summary>
    public static class CardArt
    {
        public const string Tables = "tables";
        public const string Wheel = "wheel";
        public const string Spiral = "spiral";
        public const string Calendar = "calendar";
        public const string Tip = "tip";
        public const string Poster = "poster";
        public const string Invite = "invite";
        public const string Quests = "quests";

        public static readonly IReadOnlyList<string> All = new[] { Tables, Wheel, Spiral, Calendar, Tip, Poster, Invite, Quests };
    }

    /// <summary>Text helpers for the pure halves. <c>loc</c> is <c>Loc.Get</c> in the app, identity in tests.</summary>
    internal static class CardText
    {
        public static string F(Func<string, string> loc, string key, params object[] args)
        {
            var pattern = loc(key);
            try { return string.Format(CultureInfo.CurrentCulture, pattern, args); }
            catch (FormatException) { return pattern; }
        }

        /// <summary>"a", "a and b", "a, b and c".</summary>
        public static string JoinAnd(Func<string, string> loc, IReadOnlyList<string> parts)
        {
            if (parts.Count == 0) return string.Empty;
            if (parts.Count == 1) return parts[0];
            var head = string.Join(", ", System.Linq.Enumerable.Take(parts, parts.Count - 1));
            return F(loc, "billboard_card_and", head, parts[parts.Count - 1]);
        }
    }

    /// <summary>Shared plumbing for the adapters: Changed is raised on the UI thread when there is one.</summary>
    public abstract class BillboardProviderBase : IBillboardProvider
    {
        public abstract string Id { get; }

        public abstract IEnumerable<BillboardCardSpec> Current(BillboardContext context);

        public virtual void Invoke(string actionTarget) { }

        public event EventHandler? Changed;

        protected void RaiseChanged()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess()) { Fire(); return; }
                dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Fire));
            }
            catch (Exception ex) { App.Logger?.Debug("Billboard provider {Id}: Changed failed: {E}", Id, ex.Message); }
        }

        private void Fire()
        {
            try { Changed?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Debug("Billboard provider {Id}: Changed handler threw: {E}", Id, ex.Message); }
        }

        /// <summary>Wraps a provider body so a broken service never takes the deck down.</summary>
        protected IEnumerable<BillboardCardSpec> Safe(Func<IEnumerable<BillboardCardSpec>> body)
        {
            try { return body() ?? Array.Empty<BillboardCardSpec>(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("Billboard provider {Id}: Current threw: {E}", Id, ex.Message);
                return Array.Empty<BillboardCardSpec>();
            }
        }

        protected static Func<string, string> Loc => global::ConditioningControlPanel.Localization.Loc.Get;
    }
}
