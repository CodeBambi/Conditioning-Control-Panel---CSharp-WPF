using System;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Local preview only until the next release passes acceptance checks.</summary>
public static class CompanionExperience
{
    public static bool IsV2Enabled
    {
        get
        {
#if DEBUG
            return string.Equals(Environment.GetEnvironmentVariable("CCP_COMPANION_V2"), "1", StringComparison.Ordinal);
#else
            return false;
#endif
        }
    }
}
