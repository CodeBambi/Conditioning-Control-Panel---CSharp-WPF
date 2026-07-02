namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// WPF facade for <see cref="Core.Services.DisplayChangeCoordinator"/>. The spawn-suppression logic
    /// now lives in CCP.Core so the Avalonia head can share it; this static keeps the legacy
    /// <c>Services.UI.DisplayChangeCoordinator</c> call sites working unchanged.
    /// </summary>
    public static class DisplayChangeCoordinator
    {
        /// <summary>Optional diagnostic sink — wired through to the Core implementation.</summary>
        public static System.Action<string>? DebugLog
        {
            get => Core.Services.DisplayChangeCoordinator.DebugLog;
            set => Core.Services.DisplayChangeCoordinator.DebugLog = value;
        }

        public static void NotifyDisplayChange(string reason) =>
            Core.Services.DisplayChangeCoordinator.NotifyDisplayChange(reason);

        public static bool SpawnsSuppressed =>
            Core.Services.DisplayChangeCoordinator.SpawnsSuppressed;
    }
}
