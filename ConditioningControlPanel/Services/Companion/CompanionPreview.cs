using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>What the Customise window says about a mod's companion.</summary>
    public sealed record CompanionPreviewInfo(string Name, int Looks, int Personalities, string? SampleLine);

    /// <summary>
    /// A read-only summary of the companion a mod carries, built from its manifest and its
    /// personality list WITHOUT activating it. The Customise window shows this for a mod the
    /// user is looking at but not using. PURE: callers pass the manifest and the personalities
    /// (<see cref="ModCompanionContent.GetPersonalities"/> reads them off disk).
    /// </summary>
    public static class CompanionPreview
    {
        /// <summary>The stock avatar sets, in the order the tube offers them.</summary>
        private static readonly int[] BaseSets = { 1, 2, 3, 4, 7, 5, 6 };

        /// <summary>
        /// How many looks the mod offers: its supported base sets (all of them when it declares
        /// none) plus its custom sets. A single-emote mod (one animated avatar, no picker) is one.
        /// </summary>
        public static int CountLooks(ModManifest manifest, bool singleEmote)
        {
            if (singleEmote) return 1;
            var supported = manifest.SupportedAvatarSets;
            bool all = supported == null || supported.Count == 0;
            var sets = new HashSet<int>(BaseSets.Where(s => all || supported!.Contains(s)));
            foreach (var c in manifest.CustomAvatarSets ?? new List<CustomAvatarSet>())
                if (all || supported!.Contains(c.SetNumber)) sets.Add(c.SetNumber);
            return sets.Count;
        }

        /// <summary>
        /// Name, counts and one sample line. <paramref name="modPersonalities"/> null or empty means
        /// the mod runs on the stock set, whose voice leads with the neutral default for the
        /// unthemed mod and with BambiSprite for a themed one (as PersonalityService does).
        /// </summary>
        public static CompanionPreviewInfo Build(ModManifest manifest, IReadOnlyList<ModPersonality>? modPersonalities,
            bool singleEmote, bool neutral)
        {
            var name = string.IsNullOrWhiteSpace(manifest.Identity?.CompanionName)
                ? manifest.Name
                : manifest.Identity!.CompanionName!;
            var looks = CountLooks(manifest, singleEmote);

            if (modPersonalities != null && modPersonalities.Count > 0)
            {
                var lead = modPersonalities[0];
                var line = PersonalitySamples.Clean(lead.SampleLines)?.FirstOrDefault()
                           ?? (string.IsNullOrWhiteSpace(lead.Description) ? null : lead.Description!.Trim());
                return new CompanionPreviewInfo(name, looks, modPersonalities.Count, line);
            }

            var stock = PersonalityPresets.ForPicker(PersonalityPresets.GetAllBuiltIn(), neutral);
            var leadId = neutral ? PersonalityPresets.NeutralDefaultId : PersonalityPresets.BambiSpriteId;
            var sample = PersonalitySamples.For(stock.FirstOrDefault(p => p.Id == leadId)).FirstOrDefault();
            return new CompanionPreviewInfo(name, looks, stock.Count, sample);
        }
    }
}
