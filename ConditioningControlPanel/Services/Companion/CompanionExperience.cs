using System;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>
/// Companion v2 ships ON for everyone (owner, 2026-09-24). The old chat stays the fallback when the
/// server's /v2/companion/chat route is off. DEBUG builds can turn it off with CCP_COMPANION_V2=0.
/// </summary>
public static class CompanionExperience
{
    /// <summary>
    /// Test seam: the unit suite pins CCP_COMPANION_V2=0 and CI builds Release, where the variable
    /// is otherwise ignored, so every legacy-companion test ran against v2 there and failed. Only the
    /// test project's module initializer sets this; a shipped app never reads the variable.
    /// </summary>
    internal static bool HonourEnvironmentForTests;

    public static bool IsV2Enabled
    {
        get
        {
#if DEBUG
            return !string.Equals(Environment.GetEnvironmentVariable("CCP_COMPANION_V2"), "0", StringComparison.Ordinal);
#else
            if (HonourEnvironmentForTests)
                return !string.Equals(Environment.GetEnvironmentVariable("CCP_COMPANION_V2"), "0", StringComparison.Ordinal);
            return true;
#endif
        }
    }
}
