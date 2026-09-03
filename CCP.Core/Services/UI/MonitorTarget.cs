namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The sentinel values a per-effect monitor target setting can hold (suggestion #639), e.g.
    /// <see cref="Models.AppSettings.SpiralTargetMonitor"/>. Resolving them to actual screens is a
    /// head's job — enumerating displays is an OS call — but the numbers are PERSISTED, so every
    /// head must agree on what -1 and -2 mean or a settings file written on one platform means
    /// something else on another. 0..N is a monitor index and needs no name.
    /// </summary>
    public static class MonitorTarget
    {
        /// <summary>Follow the global <see cref="Models.AppSettings.DualMonitorEnabled"/> behavior (default).</summary>
        public const int FollowGlobal = -1;

        /// <summary>Render on every connected monitor regardless of DualMonitorEnabled.</summary>
        public const int All = -2;
    }
}
