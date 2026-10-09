using System;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>The app's tab: <c>App.Chaster</c>, the same NoteSeconds path an Awareness trigger uses.
/// The row ids are literal on purpose (the price-row wiring test reads them).</summary>
public sealed class ChasterLeashTab : ILeashTab
{
    public int BookPunish(int seconds)
    {
        if (seconds <= 0) return 0;
        try { return App.Chaster?.NoteSeconds("leash", seconds).AppliedSeconds ?? 0; }
        catch (Exception ex) { Serilog.Log.Debug("Leash tab punish failed: {E}", ex.Message); return 0; }
    }

    public int BookCredit(int seconds)
    {
        if (seconds <= 0) return 0;
        try { return App.Chaster?.NoteSeconds("leash_credit", -seconds).AppliedSeconds ?? 0; }
        catch (Exception ex) { Serilog.Log.Debug("Leash tab credit failed: {E}", ex.Message); return 0; }
    }
}
