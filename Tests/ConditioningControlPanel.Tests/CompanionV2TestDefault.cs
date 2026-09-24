using System;
using System.Runtime.CompilerServices;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Companion v2 is on by default in every build since 2026-09-24. The suite was written against
/// the legacy companion (the old DEBUG default), so it pins CompanionExperience.IsV2Enabled off
/// before any test runs. v2 tests inject their preview gate explicitly and are unaffected.
/// A run that sets CCP_COMPANION_V2 itself keeps its own value.
/// </summary>
internal static class CompanionV2TestDefault
{
    [ModuleInitializer]
    internal static void PinLegacyCompanion()
    {
        if (Environment.GetEnvironmentVariable("CCP_COMPANION_V2") == null)
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", "0");
    }
}
