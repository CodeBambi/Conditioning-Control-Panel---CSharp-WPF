using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// One named viewmodel per exhibit in the mockup's state gallery, so every user-visible state
    /// is reachable without a service, a login, or a train landing.
    ///
    /// <para>Use it from a scratch harness window, from the designer, or from a test:</para>
    /// <code>
    /// var card = new ChatThresholdView { DataContext = CompanionMockGallery.Get("chat.locked") };
    /// </code>
    ///
    /// <para>Keys are stable — the builders and the play-test scripts index into this. Adding a
    /// state means adding a key here too; that is the point of the type.</para>
    /// </summary>
    public static class CompanionMockGallery
    {
        /// <summary>Every exhibit, keyed "zone.state".</summary>
        public static IReadOnlyDictionary<string, Func<object>> Exhibits { get; } =
            new Dictionary<string, Func<object>>(StringComparer.OrdinalIgnoreCase)
            {
                // Z1 hero
                ["hero.default"] = () => MockCompanionHeroCardVm.Default(),
                ["hero.fullyAlive"] = () => MockCompanionHeroCardVm.FullyAlive(),
                ["hero.asleep"] = () => MockCompanionHeroCardVm.Asleep(),
                ["hero.aiOff"] = () => MockCompanionHeroCardVm.AiOff(),
                ["hero.freshUser"] = () => MockCompanionHeroCardVm.FreshUser(),
                ["hero.freeTier"] = () => MockCompanionHeroCardVm.FreeTier(),
                ["hero.noHeader"] = () => MockCompanionHeroCardVm.NoHeader(),

                // Z1 constellation
                ["constellation.live"] = () => MockRelationshipConstellationVm.Live(),
                ["constellation.dormant"] = () => MockRelationshipConstellationVm.Dormant(),
                ["constellation.freshlyMet"] = () => MockRelationshipConstellationVm.FreshlyMet(),
                ["constellation.inevitable"] = () => MockRelationshipConstellationVm.Inevitable(),

                // Z2 chat
                ["chat.live"] = () => MockChatThresholdVm.Live(),
                ["chat.dormant"] = () => MockChatThresholdVm.Dormant(),
                ["chat.aiOff"] = () => MockChatThresholdVm.AiOff(),
                ["chat.locked"] = () => MockChatThresholdVm.Locked(),
                ["chat.thinking"] = () => MockChatThresholdVm.Thinking(),

                // Z3 memory
                ["memory.populated"] = () => MockMemoryDiaryVm.Populated(),
                ["memory.empty"] = () => MockMemoryDiaryVm.Empty(),
                ["memory.dormant"] = () => MockMemoryDiaryVm.Dormant(),
                ["memory.boundariesFilter"] = () => MockMemoryDiaryVm.BoundariesFilter(),

                // Z4 personality
                ["personality.dormant"] = () => MockMakeHerYoursVm.Dormant(),
                ["personality.live"] = () => MockMakeHerYoursVm.Live(),
                ["personality.interviewed"] = () => MockMakeHerYoursVm.Interviewed(),
                ["personality.handEdited"] = () => MockMakeHerYoursVm.HandEdited(),

                // Z5 awareness
                ["awareness.live"] = () => MockAwarenessPrivacyVm.Live(),
                ["awareness.dormant"] = () => MockAwarenessPrivacyVm.Dormant(),
                ["awareness.eyesClosed"] = () => MockAwarenessPrivacyVm.EyesClosed(),

                // Z6 attention
                ["attention.plenty"] = () => MockAttentionGaugeVm.Plenty(),
                ["attention.saving"] = () => MockAttentionGaugeVm.Saving(),
                ["attention.whispering"] = () => MockAttentionGaugeVm.Whispering(),
                ["attention.drained"] = () => MockAttentionGaugeVm.Drained(),

                // Z7 engine room
                ["engine.cloud"] = () => MockEngineRoomDrawerVm.Cloud(),
                ["engine.loggedOut"] = () => MockEngineRoomDrawerVm.LoggedOut(),
                ["engine.localOllama"] = () => MockEngineRoomDrawerVm.LocalOllama(),
                ["engine.off"] = () => MockEngineRoomDrawerVm.Off(),
                ["engine.collapsed"] = () => MockEngineRoomDrawerVm.Collapsed(),

                // Z8 workshop
                ["workshop.collapsed"] = () => MockWorkshopAccordionVm.Collapsed(),
                ["workshop.expanded"] = () => MockWorkshopAccordionVm.Expanded()
            };

        /// <summary>Builds the exhibit for <paramref name="key"/>, or null when there is no such key.</summary>
        public static object? Get(string key)
            => Exhibits.TryGetValue(key, out var factory) ? factory() : null;
    }
}
