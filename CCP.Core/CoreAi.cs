using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The AI-availability seam. Engine code that shapes content by whether an AI provider is
    /// usable (the keyword-preset installer marks AI-only triggers) asks here. The providers
    /// themselves, cloud identity and Patreon access stay in the head. Unseeded is "no AI".
    /// </summary>
    public static class CoreAi
    {
        public static volatile Func<bool>? IsAvailableProvider;

        public static bool IsAvailable
        {
            get { try { return IsAvailableProvider?.Invoke() ?? false; } catch { return false; } }
        }
    }
}
