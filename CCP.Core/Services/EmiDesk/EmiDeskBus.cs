using System;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>A moment as the bus carries it (WPF EmiDeskService.cs:16).</summary>
public sealed record EmiMoment(string Id, object? Context);

/// <summary>
/// The Core end of WPF <c>App.EmiDesk.Fire(...)</c> / <c>App.EmiDesk.ReleaseHold(...)</c>. Core
/// services and the head's hosts call this at the same moments WPF fires them; the head's
/// EmiDeskService is the one sink. With no sink (a head without the desk, a test) every call is a
/// cheap no-op, exactly as WPF's null service reads. Never throws.
/// </summary>
public static class EmiDeskBus
{
    /// <summary>The head's EmiDeskService.Fire. Null = no desk.</summary>
    public static Action<string, object?>? Sink { get; set; }

    /// <summary>The head's EmiDeskService.ReleaseHold. Null = release straight on the engine.</summary>
    public static Action<string>? ReleaseSink { get; set; }

    /// <summary>WPF EmiBarkBridge's <c>App.Video?.LastVideoTitle ?? LastVideoPath</c>.</summary>
    public static Func<string?>? VideoTitleProbe { get; set; }

    /// <summary>Tell EMI something happened. Safe from any thread.</summary>
    public static void Fire(string momentId, object? ctx = null)
    {
        if (string.IsNullOrWhiteSpace(momentId)) return;
        try { Sink?.Invoke(momentId, ctx); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bus Fire({Moment}) failed", momentId); }
    }

    /// <summary>Let a holdUntilReleased hold go. Releasing a hold nobody holds is a no-op.</summary>
    public static void ReleaseHold(string momentId)
    {
        if (string.IsNullOrWhiteSpace(momentId)) return;
        try
        {
            if (ReleaseSink != null) ReleaseSink(momentId);
            else EmiLineEngine.Instance.ReleaseHold(momentId);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bus ReleaseHold({Moment}) failed", momentId); }
    }
}
