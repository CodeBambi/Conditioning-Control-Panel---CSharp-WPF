using System;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// Where the leash surfaces find the service, the task runner and the leashed side's own day
/// report. The CORE lane's App wiring sets these (<c>Service = () =&gt; App.Leash</c>), so the UI
/// never names <c>App.Leash</c> and compiles on its own.
///
/// <para>DEBUG only: with no service wired and <c>CCP_LEASH_DEMO=1</c> in the environment, every
/// surface talks to one shared <see cref="FakeLeashService"/> so the whole loop can be clicked
/// through without a server.</para>
/// </summary>
public static class LeashLocator
{
    private static Func<ILeashService?> _service = () => null;
    private static Func<ILeashTaskRunner?> _runner = () => null;

    /// <summary>The leash service, or null while it is not wired (signed out, flag off).</summary>
    public static Func<ILeashService?> Service
    {
        get => () => Guard(_service) ?? Demo?.Service;
        set => _service = value ?? (() => null);
    }

    /// <summary>The gate's task runner, or null (the gate then shows the task and no Start).</summary>
    public static Func<ILeashTaskRunner?> Runner
    {
        get => () => Guard(_runner) ?? (Guard(_service) == null ? Demo?.Runner : null);
        set => _runner = value ?? (() => null);
    }

    /// <summary>The leashed side's own day as it would be reported (the "what they see" preview).
    /// CORE sets it from its report builder; null draws the preview with dashes.</summary>
    public static Func<DayReport?> LocalReport { get; set; } = () => Demo?.Service.LocalReport;

    private static T? Guard<T>(Func<T?> f) where T : class
    {
        try { return f(); } catch { return null; }
    }

    // ---- the DEBUG demo ---------------------------------------------------------------

    internal sealed record DemoPair(FakeLeashService Service, FakeLeashTaskRunner Runner);

    private static DemoPair? _demo;
    private static bool _demoChecked;

    /// <summary>The demo pair, built once, only in a DEBUG build with CCP_LEASH_DEMO=1.</summary>
    internal static DemoPair? Demo
    {
        get
        {
#if DEBUG
            if (_demoChecked) return _demo;
            _demoChecked = true;
            try
            {
                if (Environment.GetEnvironmentVariable("CCP_LEASH_DEMO") == "1")
                {
                    var svc = FakeLeashService.Sample();
                    _demo = new DemoPair(svc, new FakeLeashTaskRunner());
                }
            }
            catch { _demo = null; }
            return _demo;
#else
            return null;
#endif
        }
    }
}
