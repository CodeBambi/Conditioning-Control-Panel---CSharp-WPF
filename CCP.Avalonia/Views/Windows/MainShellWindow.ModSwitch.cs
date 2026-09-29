// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs - the one mod-switching path:
// ModSelectorCombo_SelectionChanged (:2926), ActivateChosenMod (:2998), ApplyActiveModChange
// (:3035), InitializeModSelector (:2481) and RefreshThemeAwareElements' palette (:2259) - plus
// ConditioningControlPanel/Services/PendingModActivation.cs (Attach / ApplyIfReady), whose rules
// already live in Core's PendingModChoice. The combo, the Mod Manager, the pending first-run
// choice and the wizard all go through ActivateMod + ApplyActiveModChange; nothing else switches.
//
// ponytail: ApplyActiveModChange repaints what this head has - the selector, the palette. WPF also
// reloads the logo/takeover/feature art, the achievement grid, skill tree, secret skills, BambiCloud
// radio + browser URL, the Hypnotube link editor, the tube's quick menu and the per-mod default
// presets; each joins here when its surface is live on this head. The combo's "Open Mod Manager"
// footer row (ModManagerEntryId) is not ported; the MOD capsule next to it opens the manager.

using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Which timing applied a pending choice (PendingModActivation.Trigger). Log only.</summary>
        internal enum ModChoiceTrigger { Immediate, PackArrived, RestartResume }

        // True until InitializeModSelector first runs: the XAML's SelectedIndex="0" on the sample row
        // raises SelectionChanged during load, which must not read as a user switch.
        private bool _suppressModSelectorChange = true;

        /// <summary>The window that hosts the switch (PendingModActivation._window).</summary>
        private static MainShellWindow? _modSwitchHost;
        private static ModService? _hookedMods;

        /// <summary>PendingModActivation.Attach: arm the pack-arrived hook and honour a choice whose
        /// download finished while the app was closed. Also paints the saved mod's selector and palette.</summary>
        private void AttachModSwitch()
        {
            if (AvApp.Mods is not { } mods) return;   // headless render: keep the sample chip
            _modSwitchHost = this;
            Closed += (_, _) => { if (_modSwitchHost == this) _modSwitchHost = null; };
            InitializeModSelector();
            RefreshThemeAwareElements();
            if (_hookedMods != mods)
            {
                _hookedMods = mods;
                mods.ModAvailabilityChanged += (_, modOrPackId) =>
                {
                    try { if (PendingModChoice.Matches(PendingModChoice.Pending, modOrPackId)) ApplyPendingModChoice(ModChoiceTrigger.PackArrived); }
                    catch (Exception ex) { Log.Warning(ex, "[ModPicker] Pending activation handler failed"); }
                };
            }
            ApplyPendingModChoice(ModChoiceTrigger.RestartResume);
        }

        /// <summary>PendingModActivation.ApplyIfReady: safe from any thread; no-ops until the bytes land.</summary>
        internal static void ApplyPendingModChoice(ModChoiceTrigger trigger)
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => ApplyPendingModChoice(trigger), DispatcherPriority.Normal);
                    return;
                }

                var pending = PendingModChoice.Pending;
                if (pending == null) return;

                var activeModId = AvApp.Mods?.ActiveModId;
                if (string.Equals(pending, activeModId, StringComparison.OrdinalIgnoreCase))
                {
                    PendingModChoice.Clear("it is already the active mod");
                    return;
                }

                if (!PendingModChoice.ShouldActivate(pending, activeModId,
                        PendingModChoice.IsContentAvailable(pending, AvApp.ReleaseContent))) return;

                _modSwitchHost?.ActivateChosenMod(pending, trigger);   // no window yet: the next signal applies it
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModPicker] Pending activation failed");
            }
        }

        private void ModSelectorCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressModSelectorChange) return;
            if ((sender as ComboBox)?.SelectedItem is not ModSelectorItem item) return;
            if (AvApp.Mods == null || AvApp.Mods.ActiveModId == item.Id) return;

            AvApp.Mods.ActivateMod(item.Id);
            // Deferred: rebuilding the combo's items from inside its own SelectionChanged leaves the
            // closed chip blank on Avalonia, so the whole repaint runs after this handler returns.
            Dispatcher.UIThread.Post(() => ApplyActiveModChange(), DispatcherPriority.Normal);
        }

        /// <summary>WPF ActivateChosenMod: the first-run / picker choice through the same two steps.</summary>
        internal void ActivateChosenMod(string modId, ModChoiceTrigger trigger)
        {
            try
            {
                if (AvApp.Mods == null || string.IsNullOrWhiteSpace(modId)) return;

                AvApp.Mods.ActivateMod(modId);
                if (!string.Equals(AvApp.Mods.ActiveModId, modId, StringComparison.OrdinalIgnoreCase))
                {
                    // ActivateMod refuses ids it doesn't know yet (registration can trail the pack).
                    Log.Warning("[ModPicker] {ModId} could not be activated yet - keeping the choice pending", modId);
                    return;
                }

                ApplyActiveModChange(fromPickerChoice: true);
                PendingModChoice.Clear("activated");
                Log.Information("[ModPicker] Auto-activated the mod chosen in the first-run picker: {ModId} (trigger: {Trigger})",
                    modId, trigger);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModPicker] Failed to activate the chosen mod {ModId}", modId);
            }
        }

        /// <summary>WPF ApplyActiveModChange. A manual switch (fromPickerChoice false) drops a pending choice.</summary>
        internal void ApplyActiveModChange(bool fromPickerChoice = false)
        {
            if (AvApp.Mods is not { } mods) return;

            if (!fromPickerChoice) PendingModChoice.Clear("the user switched mods manually");

            CoreSettings.Current.ActiveModId = mods.ActiveModId;
            CoreSettings.Current.ModChosen = true;
            CoreSettings.Save();

            // audio-base is fetched only for the mod that plays it (no-op when stamped/offline).
            // Same sandbox guard as startup: a restart-resumed choice is not a user action.
            if (ModAudioPolicy.UsesBaselineVoicePack(mods.ActiveModId) && AvApp.ReleaseContent is { } releaseContent
                && !AvApp.SkipStartupFetch(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"), Environment.GetEnvironmentVariable("CCP_CONTENT_BASE_URL")))
                _ = System.Threading.Tasks.Task.Run(() => releaseContent.EnsureBaselineAsync());

            // WPF App.KeywordPresets.NotifyVisibilityChanged: themed presets show only under their mod.
            Named<Tabs.AwarenessTabView>("AwarenessTab")?.RefreshAwarenessPresetCards();

            InitializeModSelector();
            RefreshThemeAwareElements();

            Log.Information("Mod changed to {ModId}", mods.ActiveModId);
        }

        /// <summary>WPF InitializeModSelector: stock mods in canonical order, then user mods A-Z.</summary>
        internal void InitializeModSelector()
        {
            if (AvApp.Mods is not { } mods) return;
            _suppressModSelectorChange = true;
            try
            {
                string[] stockOrder =
                {
                    BuiltInMods.CCPDefaultId, BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId,
                    BuiltInMods.DronificationId, BuiltInMods.LockedId, BuiltInMods.InfectionControlId,
                };
                var rows = stockOrder.Where(mods.InstalledMods.ContainsKey).Select(id => mods.InstalledMods[id])
                    .Concat(mods.InstalledMods.Values.Where(m => !m.IsBuiltIn)
                        .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                    .Select(BuildSelectorItem).ToList();
                // Rebuild only when the mod set changed. Unlike WPF, clearing an Avalonia ComboBox's
                // items from inside its own SelectionChanged leaves the closed chip blank.
                if (!rows.Select(r => (r.Id, r.Name)).SequenceEqual(AvailableMods.Select(r => (r.Id, r.Name))))
                {
                    AvailableMods.Clear();
                    foreach (var row in rows) AvailableMods.Add(row);
                }

                if (Named<ComboBox>("ModSelectorCombo") is { } combo)
                    combo.SelectedItem = AvailableMods.FirstOrDefault(i => i.Id == mods.ActiveModId);
            }
            finally
            {
                _suppressModSelectorChange = false;
            }
        }

        private static ModSelectorItem BuildSelectorItem(ModPackage mod)
        {
            var color = Color.FromRgb(0xE8, 0x43, 0x93);
            try { color = Color.Parse(mod.Manifest.Theme?.AccentColor ?? "#E84393"); } catch { }
            return new ModSelectorItem(mod.Id, mod.Name, new SolidColorBrush(color));
        }

        /// <summary>
        /// The palette half of WPF RefreshThemeAwareElements: the same keys, the same math, written to
        /// the application resources so every DynamicResource repaints. ponytail: the Descent-fuse
        /// dimming (DescentFuseChrome, WPF-only, step 0 on every install without a countdown) and the
        /// per-control writes (title bar, level label, XP bar, banner) are not ported.
        /// </summary>
        private void RefreshThemeAwareElements()
        {
            try
            {
                var mods = AvApp.Mods;
                var res = global::Avalonia.Application.Current?.Resources;
                if (mods == null || res == null) return;

                static Color C(string? hex, string fallback)
                {
                    try { return Color.Parse(hex ?? fallback); } catch { return Color.Parse(fallback); }
                }
                static Color A(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);
                static Color Lighten(Color c, double t) => Color.FromRgb(
                    (byte)Math.Min(255, c.R + (255 - c.R) * t), (byte)Math.Min(255, c.G + (255 - c.G) * t), (byte)Math.Min(255, c.B + (255 - c.B) * t));
                static Color Darken(Color c, double t) => Color.FromRgb(
                    (byte)Math.Max(0, c.R * (1 - t)), (byte)Math.Max(0, c.G * (1 - t)), (byte)Math.Max(0, c.B * (1 - t)));
                static Color Mix(Color bg, Color acc, double t) => Color.FromRgb(
                    (byte)(bg.R + (acc.R - bg.R) * t), (byte)(bg.G + (acc.G - bg.G) * t), (byte)(bg.B + (acc.B - bg.B) * t));

                var accent = C(mods.GetAccentColorHex(), "#FF69B4");
                var dark = C(mods.GetAccentDarkColorHex(), "#FF1493");
                var light = C(mods.GetAccentLightColorHex(), "#FF8FAF");
                var secondary = C(mods.GetSecondaryColorHex(), "#9B59B6");
                var bg = C(mods.GetBackgroundColorHex(), "#1A1A2E");
                var panel = C(mods.GetPanelColorHex(), "#252542");
                var surface = C(mods.GetSurfaceColorHex(), "#1E1E3A");
                var pressed = Color.FromRgb((byte)Math.Max(0, accent.R - 30), (byte)Math.Max(0, accent.G - 30), (byte)Math.Max(0, accent.B - 30));

                void Put(string key, Color c) { res[key] = c; res[key + "Brush"] = new SolidColorBrush(c); }
                Put("DarkerBg", bg);
                Put("PanelBg", panel);
                Put("SurfaceBg", surface);
                Put("PanelAccent", Lighten(panel, 0.15));
                Put("PanelAccentHover", Lighten(panel, 0.25));
                Put("PreviewBg", Darken(bg, 0.15));
                Put("PanelBgTransparent", A(0xB0, panel));
                Put("DarkPink", dark);
                Put("PinkButtonHovered", light);
                Put("TransparentPink", A(0x30, accent));
                Put("TransparentPink20", A(0x20, accent));
                Put("TransparentPink40", A(0x40, accent));
                Put("TransparentPink50", A(0x50, accent));
                Put("AccentPressed", pressed);
                Put("PatreonPurple", secondary);
                Put("AccentTintedBg", Mix(bg, accent, 0.15));
                Put("AccentTintedBgHover", Mix(bg, accent, 0.20));
                Put("AccentMidGradient", Mix(bg, accent, 0.10));
                res["PinkColor"] = accent;
                res["PinkBrush"] = new SolidColorBrush(accent);
                res["SecondaryBrush"] = new SolidColorBrush(secondary);
                res["AccentGradientBrush"] = mods.IsCCPDefault && this.TryFindResource("BrandGradient", out var brand) && brand is IBrush b
                    ? b : new SolidColorBrush(accent);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to refresh some theme-aware elements");
            }
        }
    }
}
