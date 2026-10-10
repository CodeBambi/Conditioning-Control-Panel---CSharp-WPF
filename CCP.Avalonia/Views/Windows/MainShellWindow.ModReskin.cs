// PORTED from WPF 7.1.5 ConditioningControlPanel/MainWindow: the live re-skin a mod switch runs.
//   MainWindow.xaml.cs      ApplyActiveModChange (:3127), LoadLogo (:2300), LoadFeatureImages (:2706),
//                           ModTileVariant (:2667) and the five ModChanged subscriptions at :3699-3723
//   MainWindow.UiUpdates.cs ApplyModFeatureNames (:95), ModAwareLabel (:361), StripLeadingGlyph (:370),
//                           RefreshModAwareSurfaces (:262, the stale-surface sweep)
//   MainWindow.NavRail.cs   ApplyDoorArt (:825)
//   MainWindow.QuestStamps.cs OnQuestStampsModChanged (:253)
//   MainWindow.ProfileCard.cs the stat badge hook (:162)
//
// WPF's rule (comment at :3711): ModChanged is the authoritative signal, ApplyActiveModChange is not
// on every path (uninstalling the worn mod activates CCP Default by itself). So the whole pass hangs
// off CoreMods.ModChanged, and ApplyActiveModChange runs it too; it is idempotent (every step is a
// repaint from the active mod, nothing accumulates).
//
// Surfaces that already repaint from their own CoreMods.ModChanged hook and are NOT repeated here:
// the launcher tiles, the tray labels, the avatar tube (set, glass, quick menu), the companion hero
// card and Library cell, the Premium shelf, the Studio rack, the skill tree, Programs, Play, Deeper,
// Haptics, Voice, the takeover page, the favourites drawer and the ambient FX canvases.
//
// Motion: nothing here starts a loop. The logo dial restarts only through its own Refresh, which
// reads AmbientFxCanvas.Env.AllowAmbientLoops, so Motion Off keeps the still frame after a switch.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF TileDecodeWidth / WideTileDecodeWidth / NavDoorArtDecodeWidth.
        private const int TileDecodeWidth = 768;
        private const int WideTileDecodeWidth = 1024;
        private const int NavDoorArtDecodeWidth = 128;

        private bool _modReskinHooked;

        /// <summary>The steps the last pass ran, in order, with whether each finished (tests).</summary>
        internal List<(string Step, bool Ok)> LastModReskin { get; } = new();

        /// <summary>How many passes ran (tests).</summary>
        internal int ModReskinPasses { get; private set; }

        private void AttachModReskin()
        {
            if (_modReskinHooked) return;
            _modReskinHooked = true;
            EventHandler<ModPackage> onMod = (_, _) =>
            {
                if (Dispatcher.UIThread.CheckAccess()) RunModReskin();
                else Dispatcher.UIThread.Post(RunModReskin, DispatcherPriority.Normal);
            };
            CoreMods.ModChanged += onMod;
            Closed += (_, _) => CoreMods.ModChanged -= onMod;   // a static event must not pin a closed shell
        }

        /// <summary>Repaint every mod-dressed surface this window owns from the active mod.</summary>
        internal void RunModReskin()
        {
            LastModReskin.Clear();
            ModReskinPasses++;
            Step("palette", RefreshThemeAwareElements);
            Step("logo", LoadLogo);
            Step("tile art", LoadFeatureImages);
            Step("tile names", ApplyModFeatureNames);
            Step("door art", ApplyDoorArt);
            Step("quest stamps", () =>
            {
                Tabs.QuestsTabView.ClearQuestArtCache();
                HideStampPopup();
                RefreshQuestStamps();
            });
            Step("quests tab", () => Named<Tabs.QuestsTabView>("QuestsTab")?.RefreshQuestUI());
            Step("achievement grid", () => Named<Tabs.AchievementsTabView>("AchievementsTab")?.RefreshAll());
            Step("profile badges", () => Named<Tabs.DiscordTabView>("DiscordTab")?.RefreshProfileStatBadges());
            Step("companion roster", RefreshCompanionRosterForMod);
            Step("wall states", RefreshWallActiveStates);
            Step("intake pass", RefreshIntakePassTile);   // WPF :1435, the card art follows the mod's niche
        }

        private void Step(string name, Action body)
        {
            try { body(); LastModReskin.Add((name, true)); }
            catch (Exception ex)
            {
                LastModReskin.Add((name, false));
                Log.Debug("Mod reskin step {Step} failed: {E}", name, ex.Message);
            }
        }

        // ---- logo ---------------------------------------------------------------------------

        /// <summary>WPF LoadLogo's file choice: logo2.png is the neutral wordmark (CCP Default and
        /// Sissy), logo.png the Bambi-branded one.</summary>
        internal static string LogoFileForActiveMod() =>
            CoreMods.IsCCPDefault || CoreSettings.Current.IsSissyMode ? "logo2.png" : "logo.png";

        /// <summary>WPF LoadLogo + AnimatedLogoImage.SetArtwork: the bundled wordmark is the animated
        /// dial; a mod that ships its own logo shows that picture, still.</summary>
        private void LoadLogo()
        {
            var dial = SettingsPage?.FindControl<global::ConditioningControlPanel.Avalonia.Controls.Home.AnimatedLogoDial>("ImgLogo");
            if (dial == null) return;
            var file = LogoFileForActiveMod();
            Bitmap? own = null;
            var overridePath = CoreModArt.OverridePath(file);
            if (overridePath != null)
            {
                try { if (System.IO.File.Exists(overridePath)) own = ModArt.TryLoad(file, 640); }
                catch (Exception ex) { Log.Debug("Mod logo {File} would not load: {E}", file, ex.Message); }
            }
            dial.SetArtwork(own);
        }

        // ---- Home tiles ---------------------------------------------------------------------

        /// <summary>WPF ModTileVariant: a built-in mod's themed face by filename convention, unless
        /// the mod overrides the base path (ModArt.TryLoad answers the override first).</summary>
        internal static Bitmap? ModTileVariant(string baseName, int decodeWidth)
        {
            var basePath = $"features/{baseName}.png";
            var suffix = CoreMods.ActiveModId switch
            {
                BuiltInMods.BambiSleepId => "_bambi",
                BuiltInMods.SissyHypnoId => "_sissy",
                BuiltInMods.DronificationId => "_drone",
                BuiltInMods.LockedId => "_locked",
                _ => null,
            };
            if (suffix != null && !CoreModArt.HasOverride(basePath))
            {
                var themed = ModArt.TryLoad($"features/{baseName}{suffix}.png", decodeWidth);
                if (themed != null) return themed;
            }
            return ModArt.TryLoad(basePath, decodeWidth);
        }

        /// <summary>WPF LoadFeatureImages: the Home wall's tiles, per half on the split ones. A null
        /// (no such art anywhere) leaves the tile as it is, as WPF does.</summary>
        private void LoadFeatureImages()
        {
            if (SettingsPage is not { } dash) return;

            static void Set(FeatureCard? card, Bitmap? art) { if (card != null && art != null) card.Icon = art; }
            static void SetSplit(SplitFeatureCard? card, string a, string b)
            {
                if (card == null) return;
                if (ModArt.TryLoad(a, TileDecodeWidth) is { } artA) card.IconA = artA;
                if (ModArt.TryLoad(b, TileDecodeWidth) is { } artB) card.IconB = artB;
            }

            Set(dash.CardFlash, ModArt.TryLoad("features/flash.png", TileDecodeWidth));
            Set(dash.CardSubliminal, ModArt.TryLoad("features/subliminal.png", TileDecodeWidth));
            Set(dash.CardBouncingText, ModArt.TryLoad("features/bouncing_text.png", TileDecodeWidth));
            Set(dash.CardBubblePop, ModArt.TryLoad("features/Bubble_pop.png", TileDecodeWidth));
            Set(dash.CardLockCard, ModArt.TryLoad("features/Phrase_Lock.png", TileDecodeWidth));
            Set(dash.FindControl<FeatureCard>("CardDeeperEditor"), ModArt.TryLoad("features/deeper_editor.png", TileDecodeWidth));
            Set(dash.CardMystery, ModTileVariant("mysterybox", TileDecodeWidth));
            Set(dash.CardVault, ModTileVariant("vault", WideTileDecodeWidth));
            SetSplit(dash.ComboVideoBubble, "features/mandatory_videos.png", "features/Bubble_count.png");
            SetSplit(dash.ComboSpiralPink, "features/spiral_overlay.png", "features/Pink_filter.png");
            SetSplit(dash.ComboMindDrain, "features/Mind_Wipers.png", "features/brain_drain.png");
        }

        /// <summary>WPF MainWindow.ModAwareLabel: the mod's own wording when it renames the English
        /// phrase, else the localised key.</summary>
        internal static string ModAwareLabel(string englishText, string locKey) =>
            CoreMods.MakeModAware(englishText) is { } modText && modText != englishText ? modText : Loc.Get(locKey);

        /// <summary>WPF StripLeadingGlyph: the shared section keys carry an emoji, the tiles draw art.</summary>
        internal static string StripLeadingGlyph(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var i = 0;
            while (i < text.Length && !char.IsLetterOrDigit(text, i))
                i += char.IsSurrogatePair(text, i) ? 2 : 1;
            return i > 0 && i < text.Length ? text.Substring(i) : text;
        }

        /// <summary>The Home wall half of WPF ApplyModFeatureNames (the rack rows, presets and
        /// takeover labels repaint from their own views on this head).</summary>
        private void ApplyModFeatureNames()
        {
            if (SettingsPage is not { } dash) return;
            static string Card(string english, string key) => StripLeadingGlyph(ModAwareLabel(english, key));

            dash.CardFlash.Title = Card("Flash Images", "section_flash_images");
            dash.CardSubliminal.Title = Card("Subliminals", "section_subliminals_2");
            dash.CardBouncingText.Title = Card("Bouncing Text", "label_bouncing_text");
            dash.CardBubblePop.Title = Card("Bubble Pop", "label_bubble_pop");
            dash.CardLockCard.Title = Card("Lock Card", "label_lock_card");
            dash.CardVault.Title = Card("The Vault", "tab_exclusives");
            dash.ComboVideoBubble.TitleA = Card("Mandatory Video", "section_mandatory_video");
            dash.ComboVideoBubble.TitleB = Card("Bubble Count", "label_bubble_count");
            dash.ComboSpiralPink.TitleA = Card("Spiral Overlay", "label_spiral_overlay");
            dash.ComboSpiralPink.TitleB = Card("Pink Filter", "label_pink_filter");
            dash.ComboMindDrain.TitleA = Card("Mind Wipe", "label_mind_wipe");
            dash.ComboMindDrain.TitleB = Card("Brain Drain", "label_brain_drain");
        }

        // ---- rail ---------------------------------------------------------------------------

        internal static readonly (string Image, string Path)[] DoorArt =
        {
            ("ImgDoorHome", "nav/door_home.png"),
            ("ImgDoorStudio", "nav/door_studio.png"),
            ("ImgDoorCompanion", "nav/door_companion.png"),
            ("ImgDoorPlay", "nav/door_play.png"),
            ("ImgDoorSocial", "nav/door_social.png"),
            ("ImgDoorYou", "nav/door_you.png"),
            ("ImgDoorLibrary", "nav/door_library.png"),
            ("ImgDoorSettings", "nav/door_settings.png"),
        };

        /// <summary>WPF ApplyDoorArt: the rail's door medallions are mod art too (nav/door_*.png).</summary>
        private void ApplyDoorArt()
        {
            foreach (var (name, path) in DoorArt)
            {
                if (Named<Image>(name) is not { } img) continue;
                var art = ModArt.TryLoad(path, NavDoorArtDecodeWidth);
                if (art != null) img.Source = art;
            }
        }

        // ---- companion ----------------------------------------------------------------------

        /// <summary>The picker and the roster list personas for the active mod (niche personas stay
        /// off the CCP Default list: Core PersonalityPresets decides, these only re-read it).</summary>
        private void RefreshCompanionRosterForMod()
        {
            foreach (var node in this.GetLogicalDescendants().ToArray())
            {
                try
                {
                    if (node is global::ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages.CompanionPickerCard picker) picker.Refresh();
                    else if (node is global::ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime.WorkshopRosterCell roster) roster.Refresh();
                }
                catch (Exception ex) { Log.Debug("Mod reskin: companion list refresh failed: {E}", ex.Message); }
            }
        }
    }
}
