using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// One-time "first X" spark bonuses, each awarded once over the DtRH meta bridge's
/// <c>first-time</c> op. The amount table is C#-owned so the page can't invent values.
/// This is the portable data-only copy (ids + <see cref="Amounts"/> + <see cref="Labels"/>);
/// the head keeps a behavior copy (TryAward + Awarded event) in <c>ChaosLessons.cs</c> until the
/// native run is decommissioned (row #6 phase 8). Values verbatim from the head/WPF ChaosFirstTimes.
/// </summary>
public static class ChaosFirstTimes
{
    public const string Taste = "first_taste";
    public const string Snap = "first_snap";
    public const string Whisper = "first_whisper";
    public const string Yes = "first_yes";
    public const string Play = "first_play";

    /// <summary>Bonus id -> spark amount granted the first time it fires.</summary>
    public static readonly IReadOnlyDictionary<string, int> Amounts = new Dictionary<string, int>
    {
        [Taste] = 5, [Snap] = 10, [Whisper] = 10, [Yes] = 15, [Play] = 15,
    };

    /// <summary>Bonus id -> human label (recap / bark surface).</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Taste] = "first taste", [Snap] = "first snap", [Whisper] = "first whisper",
        [Yes] = "first yes", [Play] = "first play",
    };
}
