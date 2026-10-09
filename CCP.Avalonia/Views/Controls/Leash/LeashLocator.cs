// PORTED from WPF 7.1.5 Controls/Leash/LeashLocator.cs: where the leash surfaces find the task
// runner and the leashed side's own day report. The DEBUG FakeLeashService demo is not ported.
// SEAM (main session): set Runner when the port's AppLeashTaskHost / LeashTaskRunner lands
// (WPF App.xaml.cs wires LeashLocator.Runner = () => App.LeashTasks), and LocalReport to the
// service's report builder (WPF: () => App.Leash?.LocalReport()).
using System;
using ConditioningControlPanel.Services.Leash;
using ILeashTaskRunner = ConditioningControlPanel.Controls.Leash.ILeashTaskRunner;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public static class LeashLocator
{
    private static Func<ILeashTaskRunner?> _runner = () => null;
    private static Func<DayReport?> _report = () => (Platform.LeashHead.Service as LeashService)?.LastReport;

    /// <summary>The leash service (the head's LeashHead.Service).</summary>
    public static Func<ILeashService?> Service { get; set; } = () => Platform.LeashHead.Service;

    /// <summary>The gate's task runner, or null (the gate shows the task and no Start; the self card
    /// shows no "Watch it").</summary>
    public static Func<ILeashTaskRunner?> Runner
    {
        get => () => Guard(_runner);
        set => _runner = value ?? (() => null);
    }

    /// <summary>The leashed side's own day as it would be reported; null draws the preview with dashes.</summary>
    public static Func<DayReport?> LocalReport
    {
        get => () => Guard(_report);
        set => _report = value ?? (() => null);
    }

    private static T? Guard<T>(Func<T?> f) where T : class
    {
        try { return f(); } catch { return null; }
    }
}
