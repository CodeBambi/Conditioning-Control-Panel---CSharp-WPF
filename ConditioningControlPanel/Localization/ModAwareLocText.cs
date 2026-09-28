using System;
using System.Globalization;
using System.Windows.Data;

namespace ConditioningControlPanel.Localization
{
    /// <summary>
    /// The rule behind <c>MainWindow.ModAwareLabel</c>, for a label that stays BOUND to the
    /// localization indexer: when the active mod's text replacements change the English wording
    /// of the key, the mod's wording wins; otherwise the localized string shows as before.
    ///
    /// <para>Why the nav rail needs it (ticket 2026-09-24): its labels are plain
    /// <c>{loc:Str}</c> bindings, which never see a mod, so with Circe active the header and the
    /// dashboard spoke Circe while the side rail still said the stock names. Setting
    /// <c>TextBlock.Text</c> from code would fix the word and kill the binding (a language switch
    /// would then leave it stale), so the rail keeps its binding and gains this converter.</para>
    /// </summary>
    public static class ModAwareLocText
    {
        /// <summary>Pure: the text to show for one key.</summary>
        /// <param name="localized">The key in the active language.</param>
        /// <param name="english">The key's English text, or null when en.json lacks it.</param>
        /// <param name="makeModAware">The active mod's replacements, or null with no mod.</param>
        public static string Resolve(string localized, string? english, Func<string, string>? makeModAware)
        {
            if (string.IsNullOrEmpty(english) || makeModAware == null) return localized;
            var modded = makeModAware(english);
            return !string.IsNullOrEmpty(modded) && modded != english ? modded : localized;
        }

        /// <summary>Converter for a <c>[key]</c> binding on <see cref="LocalizationManager.Instance"/>.</summary>
        public sealed class Converter : IValueConverter
        {
            private readonly string _key;
            public Converter(string key) { _key = key; }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var localized = value as string ?? string.Empty;
                try
                {
                    var mods = App.Mods;
                    return Resolve(localized, LocalizationManager.Instance.GetEnglish(_key),
                        mods == null ? null : mods.MakeModAware);
                }
                catch
                {
                    return localized;
                }
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => Binding.DoNothing;
        }
    }
}
