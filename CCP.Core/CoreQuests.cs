using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The quest seam: what <c>Services/Progression/QuestService.cs</c> (Core) still needs from a
    /// head after its move. Everything else it reads through existing seams - settings via
    /// <see cref="CoreSettings"/>, premium via <see cref="CoreEntitlement"/>, the account via
    /// <see cref="CoreAccount"/>, XP via <see cref="CoreProgression"/>, the UI thread via
    /// <see cref="CoreDispatch"/>.
    ///
    /// <para>Unseeded is the WPF "service is null" state for each member: no shield, no bonus,
    /// no program tracking, no sound/haptics, the entitlement unresolved (fail-safe: premium
    /// quests are never dropped) and every device read as present (the probe's own fail-open).
    /// Delegates, no interface, matching <see cref="CoreProgression"/>; volatile for the same
    /// reason.</para>
    /// </summary>
    public static class CoreQuests
    {
        /// <summary>WPF <c>App.Patreon?.IsVerifying</c>: null when there is no Patreon service.</summary>
        public static volatile Func<bool?>? PatreonVerifyingProvider;

        /// <summary>WPF <c>App.SubscribeStar?.IsVerifying == true</c>.</summary>
        public static volatile Func<bool>? SubscribeStarVerifyingProvider;

        /// <summary>WPF <c>App.SkillTree?.UseStreakShield() == true</c>.</summary>
        public static volatile Func<bool>? UseStreakShieldProvider;

        /// <summary>WPF <c>App.SkillTree?.CheckPerfectWeekBonus() ?? 0</c>.</summary>
        public static volatile Func<int>? CheckPerfectWeekBonusProvider;

        /// <summary>WPF <c>App.Programs?.TrackVerifier(category, amount)</c>.</summary>
        public static volatile Action<QuestCategory, int>? TrackProgramVerifierProvider;

        /// <summary>Completion sound + haptic event (System.Media and the toy stay in the head).</summary>
        public static volatile Action? PlayCompletionEffectsProvider;

        /// <summary>Camera / microphone presence probes for <c>QuestHardwareGate</c>. Read at probe
        /// time, so a head may seed them after the gate's static instance exists.</summary>
        public static volatile Func<bool>? CameraProbe;
        public static volatile Func<bool>? MicrophoneProbe;
    }
}
