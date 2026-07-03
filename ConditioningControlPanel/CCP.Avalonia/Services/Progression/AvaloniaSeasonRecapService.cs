using System;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Services.Progression;

/// <summary>
/// Avalonia season-recap recorder. Records the client-sampled season peak rank into local
/// settings so the Season Recap card can show "best rank this season". Mirrors the legacy WPF
/// <c>SeasonRecapService.SampleRank</c> (decision #1: client-side peak, no server field).
/// </summary>
public sealed class AvaloniaSeasonRecapService : ISeasonRecapService
{
    private readonly ISettingsService _settingsService;

    public AvaloniaSeasonRecapService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <summary>
    /// Keeps the lowest (best) rank number seen this season and the user count at that moment.
    /// Ignores non-positive ranks. Does not Save() — the periodic leaderboard refresh is frequent
    /// and the value is snapshotted at rollover / read live for the card.
    /// </summary>
    public void SampleRank(int rank, int total)
    {
        if (rank <= 0) return;
        var s = _settingsService.Current;
        if (s == null) return;

        if (s.SeasonPeakRank == 0 || rank < s.SeasonPeakRank)
        {
            s.SeasonPeakRank = rank;
            s.SeasonPeakRankTotal = total;
        }
    }
}
