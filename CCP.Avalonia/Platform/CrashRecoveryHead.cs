using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// WPF App.xaml.cs:1730 / :2107 / :5651 on this head (hunt3 IC7): the dirty-shutdown sentinel is
/// armed while the engine runs (CoreEngine, once <see cref="CoreEngine.CrashSentinel"/> is on), a
/// clean stop or a clean exit clears it, and a file that survived to the next launch means the
/// last run died with the engine on. That is logged, and EMI hears <c>crashRecovered</c> once.
/// </summary>
internal static class CrashRecoveryHead
{
    /// <summary>Test seam: read and clear what the last run left (default: the Core sentinel).</summary>
    internal static Func<bool> Consume { get; set; } = () => EngineCrashSentinel.ConsumeAndReport(Serilog.Log.Logger);

    /// <summary>Startup, after EMI's sink is wired. True when the last run crashed. Never throws.</summary>
    internal static bool Start()
    {
        var crashed = false;
        try { crashed = Consume(); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Crash sentinel read failed"); }
        CoreEngine.CrashSentinel = true;   // from here on the engine arms it
        if (crashed) EmiDeskBus.Fire("crashRecovered");
        return crashed;
    }

    /// <summary>A clean shutdown with the engine still running is not a crash.</summary>
    internal static void CleanExit()
    {
        try { if (CoreEngine.CrashSentinel) EngineCrashSentinel.Clear(); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Crash sentinel clear failed"); }
    }
}
