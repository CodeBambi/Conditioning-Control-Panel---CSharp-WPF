namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// What the Mind Wipe service should do when its GLOBAL flags change (ccp-bugs #1304: "Mind
    /// Wipe audio keeps firing"). Before this, only <c>StartEngine</c> and <c>StopEngine</c> ever
    /// started or stopped the service: unticking Mind Wipe in its panel, switching it off on the
    /// dashboard wall, or loading a preset without it all wrote the flag and left the scheduler
    /// firing clips until the engine stopped, and the loop box started a loop even with the
    /// engine off. PURE.
    /// <list type="bullet">
    /// <item>a running session owns Mind Wipe (its own settings, not the global ones), so a global
    /// flag change does nothing to it;</item>
    /// <item>off always stops, engine or not;</item>
    /// <item>on only starts while the engine runs, the same rule as every other feature;</item>
    /// <item>the loop follows its box, but only on top of an enabled, running Mind Wipe.</item>
    /// </list>
    /// </summary>
    public static class MindWipeRunRule
    {
        /// <summary>The rule itself moved to Core (<see cref="CoreMindWipe.ForFlags"/>) so every head shares it.</summary>
        public static CoreMindWipe.RunPlan ForFlags(bool engineRunning, bool sessionRunning, bool enabled, bool loop,
            bool serviceRunning, bool looping) =>
            CoreMindWipe.ForFlags(engineRunning, sessionRunning, enabled, loop, serviceRunning, looping);

        /// <summary>Reads the live flags and service and carries out <see cref="ForFlags"/>. Call it
        /// after anything writes <c>MindWipeEnabled</c> or <c>MindWipeLoop</c>.</summary>
        public static void ApplyToService()
        {
            var s = App.Settings?.Current;
            var mw = App.MindWipe;
            if (s == null || mw == null) return;
            try
            {
                var plan = ForFlags(App.IsEngineRunning, App.IsSessionRunning, s.MindWipeEnabled, s.MindWipeLoop,
                    mw.IsRunning, mw.IsLooping);
                var volume = s.MindWipeVolume / 100.0;
                if (plan.Stop) mw.Stop();
                if (plan.StopLoop) mw.StopLoop();
                if (plan.Start) mw.Start(s.MindWipeFrequency, volume);
                if (plan.StartLoop) mw.StartLoop(volume);
            }
            catch (System.Exception ex)
            {
                App.Logger?.Warning(ex, "MindWipe: applying its flags to the service failed");
            }
        }
    }
}
