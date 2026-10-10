using System.Runtime.CompilerServices;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Lobby, PvP stakes and the Tonight Board picture live once in CCP.Core and read the WPF app
/// through seams (Services/CoreSeams.cs). The app wires them at startup; no test runs startup,
/// so the suite wires them here, before any test, and sees the same behaviour the app has.
/// </summary>
internal static class CoreSeamsTestDefault
{
    [ModuleInitializer]
    internal static void WireCoreSeams() => Services.CoreSeams.Wire();
}
