// PORTED from AvatarTube/AvatarTubeWindow.Avatar.cs (WPF 7.1.5): :197 GetAvatarSetForLevel /
// GetUnlockedAvatarSets, :243 UpdateAvatarForLevel, :395 SwitchToAvatarSet, :819 SwitchToCompanionAvatar,
// :866 UpdateNavigationArrows, :978/:989 the arrow clicks; CirceEmotes.cs:235 IsSingleEmoteAvatarMod.
// Ledger row tube#T12. 7.1.5 unlocks every set at every level (GetAvatarSetForLevel = 7); a mod whose
// only avatar is its one emote set (CCP Default, Bambi Sleep, Sissy) shows no arrows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private int _maxUnlockedSet = 7;

        /// <summary>WPF 7.1.5: every set unlocks at level 1.</summary>
        internal static int GetAvatarSetForLevel(int level) => 7;
        internal static bool IsAvatarSetUnlocked(int setNumber, int level) => true;

        private static bool IsAvatarSetSupported(int set)
        {
            var supported = CoreMods.ActiveModPackage?.Manifest?.SupportedAvatarSets;
            return supported == null || supported.Count == 0 || supported.Contains(set);
        }

        /// <summary>WPF GetUnlockedAvatarSets: 1,2,3,4,7,5,6 in that order, then the mod's custom sets by unlock level.</summary>
        internal static int[] GetUnlockedAvatarSets(int level)
        {
            var unlocked = new List<int>();
            foreach (int set in new[] { 1, 2, 3, 4, 7, 5, 6 })
                if (IsAvatarSetUnlocked(set, level) && IsAvatarSetSupported(set)) unlocked.Add(set);
            var custom = CoreMods.ActiveModPackage?.Manifest?.CustomAvatarSets;
            if (custom != null)
                foreach (var cs in custom.OrderBy(c => c.UnlockLevel))
                    if (IsAvatarSetUnlocked(cs.SetNumber, level) && IsAvatarSetSupported(cs.SetNumber)) unlocked.Add(cs.SetNumber);
            return unlocked.ToArray();
        }

        /// <summary>The emote sets the active mod has: its own resources/emotes/set{N}, plus the registry.</summary>
        private int[] EmoteSetsForActiveMod()
        {
            var sets = new SortedSet<int>();
            var modId = CoreMods.ActiveModId;
            try
            {
                var installed = CoreMods.ActiveModPackage?.InstalledPath;
                var root = string.IsNullOrEmpty(installed) ? null : Path.Combine(installed, "resources", "emotes");
                if (root != null && Directory.Exists(root))
                    foreach (var d in Directory.GetDirectories(root, "set*"))
                        if (int.TryParse(Path.GetFileName(d).Substring(3), out var n) && File.Exists(Path.Combine(d, "emotes.json")))
                            sets.Add(n);
            }
            catch { }
            foreach (var e in LoadEmoteRegistry())
                if (string.Equals(e.modId, modId, StringComparison.OrdinalIgnoreCase)) sets.Add(e.set);
            return sets.ToArray();
        }

        /// <summary>WPF IsSingleEmoteAvatarMod: the mod's sole avatar IS its one emote set.</summary>
        private bool IsSingleEmoteAvatarMod(out int set)
        {
            var sets = EmoteSetsForActiveMod();
            if (sets.Length == 1)
            {
                var m = CoreMods.ActiveModPackage?.Manifest;
                bool other = (m?.SupportedAvatarSets?.Any(s => s != sets[0]) ?? false)
                             || (m?.CustomAvatarSets?.Any(c => c.SetNumber != sets[0]) ?? false);
                if (!other) { set = sets[0]; return true; }
            }
            set = 0;
            return false;
        }

        internal int[] EffectiveAvatarSets() =>
            IsSingleEmoteAvatarMod(out int only) ? new[] { only } : GetUnlockedAvatarSets(CoreSettings.Current.PlayerLevel);

        /// <summary>WPF UpdateNavigationArrows: prev only when there is a set before, next only after.</summary>
        internal void UpdateNavigationArrows()
        {
            var sets = EffectiveAvatarSets();
            int i = Array.IndexOf(sets, _currentAvatarSet);
            bool many = sets.Length > 1;
            _btnPrevAvatar.IsVisible = many && i > 0;
            _btnNextAvatar.IsVisible = many && i >= 0 && i < sets.Length - 1;
        }

        /// <summary>The title-box arrows (WPF BtnPrev/NextAvatar_Click).</summary>
        private void StepAvatarSet(int delta)
        {
            var sets = EffectiveAvatarSets();
            int i = Array.IndexOf(sets, _currentAvatarSet);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= sets.Length) return;
            SelectAvatarSet(sets[j]);
        }

        /// <summary>WPF SelectAvatarSet: false when the set is not pickable or already on.</summary>
        internal bool SelectAvatarSet(int setNumber)
        {
            if (!EffectiveAvatarSets().Contains(setNumber) || setNumber == _currentAvatarSet) return false;
            SwitchToAvatarSet(setNumber);
            return true;
        }

        /// <summary>
        /// WPF SwitchToAvatarSet: persist the pick (SelectedAvatarSet + the per-mod look), switch the
        /// companion behind a persona set (4+) through the CoreModsHooks seam, reload the art, the
        /// emote set and the caption.
        /// </summary>
        private void SwitchToAvatarSet(int setNumber)
        {
            if (!IsAvatarSetUnlocked(setNumber, CoreSettings.Current.PlayerLevel)) return;
            _currentAvatarSet = setNumber;
            try
            {
                var s = CoreSettings.Current;
                s.SelectedAvatarSet = setNumber;
                var modId = CoreMods.ActiveModId;
                if (!string.IsNullOrEmpty(modId)) s.ModAvatarSet[modId] = setNumber;
                CoreSettings.Save();
            }
            catch (Exception ex) { Log.Debug(ex, "Avatar set save failed"); }

            var companion = CompanionForAvatarSet(setNumber);
            if (companion.HasValue)
            {
                try { CoreModsHooks.SwitchCompanion?.Invoke(companion.Value); }
                catch (Exception ex) { Log.Debug(ex, "SwitchCompanion seam failed"); }
            }

            _poseTimer.Stop();
            ApplyAvatarSet();
            TryUpdateEmoteMode();
            ApplyTubeLayoutOffsets();
            UpdateNavigationArrows();
        }

        /// <summary>WPF SwitchToCompanionAvatar: the companion lane calls this when the active companion changes.</summary>
        public void SwitchToCompanionAvatar(CompanionId companionId) => RunOnAvatar(() =>
        {
            if (IsSingleEmoteAvatarMod(out _)) return;
            int target = companionId switch
            {
                CompanionId.OGBambiSprite => 3,
                CompanionId.CultBunny => 4,
                CompanionId.BrainParasite => 5,
                CompanionId.BambiTrainer => 6,
                CompanionId.BimboCow => 7,
                _ => _currentAvatarSet,
            };
            if (target == _currentAvatarSet || !EffectiveAvatarSets().Contains(target)) { RefreshCompanionDisplay(); return; }
            _currentAvatarSet = target;
            try { CoreSettings.Current.SelectedAvatarSet = target; CoreSettings.Save(); } catch { }
            _poseTimer.Stop();
            ApplyAvatarSet();
            TryUpdateEmoteMode();
            ApplyTubeLayoutOffsets();
            UpdateNavigationArrows();
        });

        /// <summary>WPF UpdateAvatarForLevel (from OnLevelUp): a newly unlocked set is switched to; the caption refreshes.</summary>
        private void OnPlayerLevelChanged(int level)
        {
            int newMax = GetAvatarSetForLevel(level);
            if (newMax > _maxUnlockedSet && !IsSingleEmoteAvatarMod(out _) && EffectiveAvatarSets().Contains(newMax))
            {
                _maxUnlockedSet = newMax;
                SwitchToAvatarSet(newMax);
                return;
            }
            RefreshCompanionDisplay();
        }

        /// <summary>WPF RefreshCompanionDisplay: the caption (persona name + level) and the arrows.</summary>
        public void RefreshCompanionDisplay() => RunOnAvatar(() =>
        {
            UpdateTitleDisplay();
            UpdateNavigationArrows();
        });
    }
}
