using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The Trainer Card's pure text and lookup rules, lifted from WPF MainWindow.Browser.cs (FormatNumber, the Videos
/// readout, the rank plate, SearchAndDisplayProfile's match order). Both heads call these.
/// </summary>
public static class TrainerCardText
{
    /// <summary>1.2M / 3.4k / 999 (WPF FormatNumber).</summary>
    public static string Number(double n) =>
        n >= 1_000_000 ? $"{n / 1_000_000:F1}M" : n >= 1_000 ? $"{n / 1_000:F1}k" : n.ToString("N0");

    /// <summary>Hours with one decimal from an hour up, whole minutes below.</summary>
    public static string Video(double minutes) => minutes >= 60 ? $"{minutes / 60:F1}h" : $"{minutes:F0}m";

    /// <summary>"#12", or "#-" when unranked.</summary>
    public static string Rank(int? rank) => rank > 0 ? $"#{rank}" : "#-";

    /// <summary>Exact display name first, then the first partial match, both case-insensitive.</summary>
    public static T? Find<T>(IEnumerable<T>? entries, string name) where T : LeaderboardEntryData =>
        entries?.FirstOrDefault(e => e.DisplayName?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
        ?? entries?.FirstOrDefault(e => e.DisplayName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true);
}
