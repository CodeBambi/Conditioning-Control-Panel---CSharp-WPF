using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// Shared plumbing for the "pick any font installed on Windows" pickers (Bouncing Text and
    /// Subliminals). Three jobs:
    /// <list type="bullet">
    ///   <item>enumerate the installed families ONCE per session (<see cref="GetInstalledFontNames"/>),</item>
    ///   <item>turn a stored family name back into a <see cref="FontFamily"/> that degrades to a
    ///         sane fallback when the user uninstalls the font (<see cref="Resolve"/>),</item>
    ///   <item>fill a <see cref="ComboBox"/> so each row previews in its own face (<see cref="Populate"/>).</item>
    /// </list>
    ///
    /// <para>The bundled Fredoka face ships as a pack:// Resource rather than an installed family,
    /// so it can never come back from <c>Fonts.SystemFontFamilies</c>. It is offered as the
    /// sentinel <see cref="BundledFredoka"/> and resolved through the same pack URI the exclusives
    /// header uses (MainWindow/MainWindow.Exclusives.cs).</para>
    /// </summary>
    public static class FontPickerHelper
    {
        /// <summary>Stored value that means "the Fredoka face bundled with the app".</summary>
        public const string BundledFredoka = "Fredoka (bundled)";

        /// <summary>
        /// Last-resort list when font enumeration throws (it reads the font directory, which can
        /// fail on a locked-down or mid-install machine). Faces every Windows install has.
        /// </summary>
        private static readonly string[] FallbackFonts =
        {
            "Arial", "Calibri", "Comic Sans MS", "Consolas", "Georgia",
            "Impact", "Segoe UI", "Tahoma", "Times New Roman", "Verdana"
        };

        private static IReadOnlyList<string>? _cachedNames;
        private static FontFamily? _fredoka;

        /// <summary>The bundled Fredoka family, built from the pack URI (single-file safe).</summary>
        public static FontFamily FredokaFamily =>
            _fredoka ??= new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka");

        /// <summary>
        /// Every font family name the user can pick, with <see cref="BundledFredoka"/> first.
        /// Enumerated once and cached: <c>Fonts.SystemFontFamilies</c> plus the per-family
        /// culture lookup is expensive enough to feel it if a ComboBox re-populates on every
        /// settings reload.
        /// </summary>
        public static IReadOnlyList<string> GetInstalledFontNames()
        {
            if (_cachedNames != null) return _cachedNames;

            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var family in Fonts.SystemFontFamilies)
                {
                    var name = DisplayNameOf(family);
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }

                if (names.Count == 0)
                    foreach (var f in FallbackFonts) names.Add(f);

                var sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                sorted.Insert(0, BundledFredoka);
                _cachedNames = sorted;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "FontPickerHelper: font enumeration failed, using the fallback list");
                var sorted = new List<string> { BundledFredoka };
                sorted.AddRange(FallbackFonts);
                _cachedNames = sorted;
            }

            return _cachedNames;
        }

        /// <summary>
        /// The en-US family name when the font declares one, else the raw <c>Source</c>. WPF
        /// hands localized names back for CJK faces; storing the en-US spelling keeps a settings
        /// file portable between display languages.
        /// </summary>
        private static string DisplayNameOf(FontFamily family)
        {
            try
            {
                var names = family.FamilyNames;
                if (names != null)
                {
                    var enUs = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
                    if (names.TryGetValue(enUs, out var en) && !string.IsNullOrWhiteSpace(en))
                        return en;
                }
            }
            catch { /* fall through to Source */ }

            return family.Source ?? string.Empty;
        }

        /// <summary>
        /// The <see cref="FontFamily"/> for a stored picker value. Unknown/empty names and the
        /// user uninstalling their pick both degrade to <paramref name="fallback"/> because the
        /// family is built as a comma-separated chain - WPF walks it and takes the first face it
        /// can actually load (same idiom as Services/Video/AttentionTargets.cs).
        /// </summary>
        public static FontFamily Resolve(string? name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(fallback)) fallback = "Segoe UI";

            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return new FontFamily(fallback);

                name = name.Trim();
                if (string.Equals(name, BundledFredoka, StringComparison.OrdinalIgnoreCase))
                    return FredokaFamily;

                // A comma in the stored name would turn into a second link in the fallback chain
                // and silently pick a different face - drop it rather than mis-resolve.
                if (name.Contains(',')) name = name.Split(',')[0].Trim();
                if (string.IsNullOrWhiteSpace(name)) return new FontFamily(fallback);

                return string.Equals(name, fallback, StringComparison.OrdinalIgnoreCase)
                    ? new FontFamily(fallback)
                    : new FontFamily($"{name}, {fallback}");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FontPickerHelper.Resolve({N}) failed: {E}", name, ex.Message);
                try { return new FontFamily(fallback); }
                catch { return new FontFamily("Segoe UI"); }
            }
        }

        /// <summary>
        /// Fills <paramref name="cmb"/> with every installed family and selects
        /// <paramref name="current"/> (or <paramref name="fallback"/> when the stored pick is
        /// gone). Each row renders in its own face so the list reads as a real font preview.
        ///
        /// <para>Hundreds of items: the panel is forced to a virtualizing one and recycling is on,
        /// so only the visible rows ever realize a Typeface. Items are plain
        /// <see cref="ComboBoxItem"/>s (Content = display name, Tag = the value to store) rather
        /// than a bound DataTemplate - the containers ARE the items here, so an ItemContainerStyle
        /// setter could not bind the per-row family.</para>
        /// </summary>
        public static void Populate(ComboBox cmb, string? current, string fallback = "Segoe UI")
        {
            if (cmb == null) return;

            try
            {
                var wanted = string.IsNullOrWhiteSpace(current) ? fallback : current!.Trim();

                // Cheap path: the settings hook re-runs LoadFromSettings on EVERY property in the
                // feature's chain (a slider drag included), and rebuilding several hundred
                // ComboBoxItems each time would stutter. The list never changes mid-session, so
                // once it is built only the selection moves.
                if (cmb.Items.Count > 0 && ReferenceEquals(cmb.Tag, _populatedMarker))
                {
                    Select(cmb, wanted, fallback);
                    ApplySelectedFamily(cmb, fallback);
                    return;
                }

                cmb.Items.Clear();

                // Realize only what is on screen - a non-virtualizing default panel would build
                // a Typeface for every installed family on first drop-down.
                var panel = new ItemsPanelTemplate(
                    new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
                panel.Seal();
                cmb.ItemsPanel = panel;
                VirtualizingStackPanel.SetIsVirtualizing(cmb, true);
                VirtualizingStackPanel.SetVirtualizationMode(cmb, VirtualizationMode.Recycling);

                ComboBoxItem? match = null;
                ComboBoxItem? fallbackItem = null;

                foreach (var name in GetInstalledFontNames())
                {
                    var item = new ComboBoxItem
                    {
                        Content = name,
                        Tag = name,
                        FontFamily = Resolve(name, fallback),
                        FontSize = 14
                    };
                    cmb.Items.Add(item);

                    if (match == null && string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                        match = item;
                    if (fallbackItem == null && string.Equals(name, fallback, StringComparison.OrdinalIgnoreCase))
                        fallbackItem = item;
                }

                cmb.SelectedItem = match ?? fallbackItem;
                if (cmb.SelectedItem == null && cmb.Items.Count > 0) cmb.SelectedIndex = 0;
                cmb.Tag = _populatedMarker;
                ApplySelectedFamily(cmb, fallback);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "FontPickerHelper.Populate failed");
            }
        }

        /// <summary>Marks a ComboBox this helper has already filled - see the cheap path in Populate.</summary>
        private static readonly object _populatedMarker = new();

        /// <summary>Moves the selection to <paramref name="wanted"/>, else to <paramref name="fallback"/>.</summary>
        private static void Select(ComboBox cmb, string wanted, string fallback)
        {
            ComboBoxItem? fallbackItem = null;
            foreach (var obj in cmb.Items)
            {
                if (obj is not ComboBoxItem item || item.Tag is not string name) continue;
                if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    cmb.SelectedItem = item;
                    return;
                }
                if (fallbackItem == null && string.Equals(name, fallback, StringComparison.OrdinalIgnoreCase))
                    fallbackItem = item;
            }
            if (fallbackItem != null) cmb.SelectedItem = fallbackItem;
        }

        /// <summary>
        /// Paints the CLOSED box in the picked face. The template's ContentPresenter renders
        /// SelectionBoxItem - the item's Content string, not the item - so it inherits the
        /// ComboBox's family; each row still wins with its own local value.
        /// </summary>
        private static void ApplySelectedFamily(ComboBox cmb, string fallback)
        {
            var name = SelectedName(cmb);
            if (!string.IsNullOrWhiteSpace(name)) cmb.FontFamily = Resolve(name, fallback);
        }

        /// <summary>The stored value behind a ComboBox row, or null when nothing is selected.</summary>
        public static string? SelectedName(ComboBox cmb)
            => (cmb?.SelectedItem as ComboBoxItem)?.Tag as string;
    }
}
