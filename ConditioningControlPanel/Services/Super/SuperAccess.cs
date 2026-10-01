using System;
using System.Linq;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// The eight Super effects. Each is an ADD-ON layered on a base effect (or, for Creep, a new
    /// effect beside the pink filter), never a replacement. Order is stable: it is the weekly free
    /// rotation's wheel, so append, never reorder. See Services/Super/CONTRACT.md.
    /// </summary>
    public enum SuperEffect
    {
        FlickerDeck,   // flashes
        InnerBloom,    // bubbles
        Afterglow,     // subliminals
        Vortex,        // spiral overlay
        Creep,         // pink fog, new, beside the pink filter
        LightsDown,    // mandatory video
        Scrawl,        // bouncing text
        Undertow,      // brain drain and melt
    }

    /// <summary>
    /// The one answer to "may Super X run right now, and has the player switched it on".
    /// Effect lanes call <see cref="IsOn"/> at their spawn points and nothing else; the tier
    /// bar, the weekly free preview and the switch UI all live behind it.
    /// </summary>
    public static class SuperAccess
    {
        /// <summary>Raised when a switch flips, the tier changes or the weekly preview starts or ends.</summary>
        public static event Action<SuperEffect>? Changed;

        /// <summary>
        /// Basic (tier 1) or higher, or this week's free preview while its 10 s try runs
        /// (<see cref="SuperPreview"/>). DEBUG honours CCP_SUPER_ALL=1 so a lane can run an effect
        /// without an account.
        /// </summary>
        public static bool IsUnlocked(SuperEffect effect)
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("CCP_SUPER_ALL") == "1") return true;
#endif
            return SuperPreviewRule.IsUnlocked(TierGate.HasPremium, effect, SuperPreview.Trying);
        }

        /// <summary>
        /// Unlocked AND switched on AND its base feature is on (<see cref="SuperBase"/>). Cheap
        /// enough to call per spawn. A base switched off reads false here and leaves the player's
        /// own Super switch alone, so the add-on comes back with the base.
        /// </summary>
        public static bool IsOn(SuperEffect effect)
        {
            EnsureHooks();
            if (!IsBaseOn(effect)) return false;
            return IsSelected(effect);
        }

        /// <summary>
        /// Unlocked AND switched on (or this week's try), ignoring the base feature. The switch UI
        /// draws this, so turning the base off never makes the Super switch look flipped.
        /// </summary>
        public static bool IsSelected(SuperEffect effect)
        {
#if DEBUG
            // Unlocks the gate only: the player's own switch still decides.
            if (Environment.GetEnvironmentVariable("CCP_SUPER_ALL") == "1") return IsSwitchedOn(effect);
#endif
            var on = App.Settings?.Current?.SuperEffectsOn;
            bool switchedOn = on != null && on.Contains(effect.ToString());
            return SuperPreviewRule.IsOn(TierGate.HasPremium, switchedOn, effect, SuperPreview.Trying);
        }

        /// <summary>Is the effect's base feature main toggle on right now.</summary>
        public static bool IsBaseOn(SuperEffect effect) => SuperBase.IsBaseOn(effect, App.Settings?.Current);

        private static readonly object BaseGate = new();
        private static bool _baseHooked;
        private static Models.AppSettings? _baseSettings;

        /// <summary>Both hooks, idempotent and cheap: a reference compare once hooked.</summary>
        private static void EnsureHooks()
        {
            if (!_tierHooked) HookTierEvents();
            HookBaseEvents();
        }

        /// <summary>
        /// Fire <see cref="Changed"/> for an effect when its base toggle flips, so a running add-on
        /// tears down with its base (Creep stops with the pink filter). Follows a settings swap
        /// (cloud restore, reset). Idempotent; <see cref="IsOn"/> calls it, so the first spawn
        /// question wires it even if no panel was ever opened.
        /// </summary>
        public static void HookBaseEvents()
        {
            var svc = App.Settings;
            if (svc == null) return;
            if (_baseHooked && ReferenceEquals(_baseSettings, svc.Current)) return;
            bool swapped;
            lock (BaseGate)
            {
                if (!_baseHooked)
                {
                    _baseHooked = true;
                    try { svc.CurrentReplaced += OnSettingsReplaced; }
                    catch (Exception ex) { App.Logger?.Debug("SuperAccess.HookBaseEvents: {E}", ex.Message); }
                }
                swapped = RebindBase();
            }
            if (swapped) RaiseAll();
        }

        private static void OnSettingsReplaced()
        {
            bool swapped;
            lock (BaseGate) swapped = RebindBase();
            if (swapped) RaiseAll();
        }

        /// <summary>Under <see cref="BaseGate"/>: point the hook at the live settings object.
        /// True when it moved from one object to another (every base may have changed).</summary>
        private static bool RebindBase()
        {
            var current = App.Settings?.Current;
            if (ReferenceEquals(_baseSettings, current)) return false;
            bool hadOne = _baseSettings != null;
            if (_baseSettings != null) _baseSettings.PropertyChanged -= OnBasePropertyChanged;
            _baseSettings = current;
            if (current != null) current.PropertyChanged += OnBasePropertyChanged;
            return hadOne;
        }

        private static void OnBasePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (SuperBase.EffectFor(e.PropertyName) is SuperEffect effect) Changed?.Invoke(effect);
        }

        /// <summary>The player's own switch position, ignoring the gate (the switch UI draws it).</summary>
        public static bool IsSwitchedOn(SuperEffect effect)
            => App.Settings?.Current?.SuperEffectsOn?.Contains(effect.ToString()) == true;

        /// <summary>For <see cref="SuperPreview"/>: a try started or ended.</summary>
        internal static void RaiseChanged(SuperEffect effect) => Changed?.Invoke(effect);

        private static bool _tierHooked;

        /// <summary>
        /// Fire <see cref="Changed"/> for every effect when the tier or the account changes, so a
        /// lapse tears every Super effect down. Idempotent; the switch calls it when it loads.
        /// </summary>
        public static void HookTierEvents()
        {
            if (_tierHooked) return;
            // Latch only once the tier services exist: a switch loaded before App wires them must
            // not leave the lapse teardown unhooked for the whole session.
            if (App.Patreon == null) return;
            _tierHooked = true;
            try
            {
                App.Patreon.TierChanged += (_, _) => RaiseAll();
                if (App.SubscribeStar != null) App.SubscribeStar.TierChanged += (_, _) => RaiseAll();
                App.UnifiedIdentityChanged += (_, _) => RaiseAll();
            }
            catch (Exception ex) { App.Logger?.Debug("SuperAccess.HookTierEvents: {E}", ex.Message); }
        }

        private static void RaiseAll()
        {
            foreach (SuperEffect e in Enum.GetValues(typeof(SuperEffect))) Changed?.Invoke(e);
        }

        /// <summary>The player's switch. Does not check the gate; the UI refuses before calling this.</summary>
        public static void Set(SuperEffect effect, bool on)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var list = s.SuperEffectsOn.ToList();
            var name = effect.ToString();
            if (on && !list.Contains(name)) list.Add(name);
            else if (!on) list.Remove(name);
            else return;
            s.SuperEffectsOn = list;
            Changed?.Invoke(effect);
        }
    }
}
