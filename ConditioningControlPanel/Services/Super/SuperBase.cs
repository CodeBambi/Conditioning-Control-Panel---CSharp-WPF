using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Super follows its base feature (owner, 2026-10-01): when a feature's main toggle is off,
    /// its Super add-on does not run. The player's own Super switch is never touched, so turning
    /// the base back on brings the add-on back. This class is the map and the rule, with no WPF
    /// and no App: <see cref="SuperAccess.IsOn"/> reads the live settings through it.
    /// </summary>
    public static class SuperBase
    {
        /// <summary>The AppSettings property that is each effect's base feature main toggle.</summary>
        public static string SettingFor(SuperEffect effect) => effect switch
        {
            SuperEffect.FlickerDeck => nameof(AppSettings.FlashEnabled),
            SuperEffect.InnerBloom => nameof(AppSettings.BubblesEnabled),
            SuperEffect.Afterglow => nameof(AppSettings.SubliminalEnabled),
            SuperEffect.Vortex => nameof(AppSettings.SpiralEnabled),
            SuperEffect.Creep => nameof(AppSettings.PinkFilterEnabled),
            SuperEffect.LightsDown => nameof(AppSettings.MandatoryVideosEnabled),
            SuperEffect.Scrawl => nameof(AppSettings.BouncingTextEnabled),
            SuperEffect.Undertow => nameof(AppSettings.BrainDrainEnabled),
            _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, null),
        };

        private static readonly Dictionary<string, SuperEffect> ByProperty = BuildIndex();

        private static Dictionary<string, SuperEffect> BuildIndex()
        {
            var map = new Dictionary<string, SuperEffect>(StringComparer.Ordinal);
            foreach (SuperEffect e in Enum.GetValues(typeof(SuperEffect))) map[SettingFor(e)] = e;
            return map;
        }

        /// <summary>The effect whose base toggle is <paramref name="propertyName"/>, or null.</summary>
        public static SuperEffect? EffectFor(string? propertyName)
            => propertyName != null && ByProperty.TryGetValue(propertyName, out var e) ? e : null;

        /// <summary>Is the base feature switched on in <paramref name="s"/>. No settings = off.</summary>
        public static bool IsBaseOn(SuperEffect effect, AppSettings? s)
        {
            if (s == null) return false;
            return effect switch
            {
                SuperEffect.FlickerDeck => s.FlashEnabled,
                SuperEffect.InnerBloom => s.BubblesEnabled,
                SuperEffect.Afterglow => s.SubliminalEnabled,
                SuperEffect.Vortex => s.SpiralEnabled,
                SuperEffect.Creep => s.PinkFilterEnabled,
                SuperEffect.LightsDown => s.MandatoryVideosEnabled,
                SuperEffect.Scrawl => s.BouncingTextEnabled,
                SuperEffect.Undertow => s.BrainDrainEnabled,
                _ => false,
            };
        }

        /// <summary>
        /// The whole gate: the base is on AND the Super gate says on (tier and switch, or this
        /// week's try). A try follows the base too: with the base off there is nothing to ride.
        /// </summary>
        public static bool IsOn(bool baseOn, bool hasTier, bool switchedOn, SuperEffect effect, SuperEffect? trying)
            => baseOn && SuperPreviewRule.IsOn(hasTier, switchedOn, effect, trying);
    }
}
