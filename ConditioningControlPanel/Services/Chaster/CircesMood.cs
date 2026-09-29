using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConditioningControlPanel.Services.Chaster;

public enum MoodLevel { Calm = 0, Warm = 1, Hot = 2, Smoking = 3 }

/// <summary>
/// Circe's mood (tab wave 1, 2026-09-29): the heat modifier (<see cref="TabDayEnd.Heated"/>) drawn
/// as a word. The level is how hot the hottest row runs today, less the cooling a credit bought,
/// so the mood is the factor the NEXT repeat of that row pays: CALM x1, WARM x1.5, HOT x2.25,
/// SMOKING x3. Pure; the service reads it off the tab and the page and the rail chip draw it.
///
/// <para><b>Cooling</b>: every credit that books today takes one step off, for every row at once,
/// never more steps than the hottest row has. A credit on a fresh day cools nothing, so good
/// behaviour cannot be banked ahead of a slip. Both reset at local midnight with the heat.</para>
/// </summary>
public readonly record struct CircesMood(MoodLevel Level)
{
    public const int MaxStep = 3;

    public static readonly CircesMood Calm = new(MoodLevel.Calm);

    public int Step => (int)Level;

    /// <summary>What the next repeat of the hottest row costs, times its list price.</summary>
    public double Factor => Math.Min(Math.Pow(TabDayEnd.HeatStep, Step), TabDayEnd.HeatMax);

    /// <summary>"x1", "x1.5", "x2.25", "x3" with the times sign.</summary>
    public string FactorText => "×" + Factor.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The loc key of the mood word.</summary>
    public string WordKey => "chaster_mood_" + Level.ToString().ToLowerInvariant();

    /// <summary>The meter's fill for this level, 0..1. Never empty, so CALM still reads as a meter.</summary>
    public double Fill => Level switch
    {
        MoodLevel.Warm => 0.36,
        MoodLevel.Hot => 0.66,
        MoodLevel.Smoking => 1.0,
        _ => 0.1,
    };

    public static CircesMood FromStep(int step) => new((MoodLevel)Math.Clamp(step, 0, MaxStep));

    /// <summary>The most times any heated row booked a cost today. Rows heat never touches (the
    /// day-end verdicts, "misses", the escape row) do not count: they never pay more.</summary>
    public static int Hottest(IReadOnlyDictionary<string, int>? heat)
    {
        if (heat == null) return 0;
        var top = 0;
        foreach (var (id, n) in heat)
            if (TabDayEnd.HeatApplies(id) && n > top) top = n;
        return top;
    }

    /// <summary>The count a row's next cost is heated by, after cooling. A cool read off disk
    /// below zero is not believed: it would make every slip dearer.</summary>
    public static int Effective(int bookedToday, int cool) => Math.Max(0, bookedToday - Math.Max(0, cool));

    /// <summary>The mood on <paramref name="today"/>. A tab whose heat belongs to another day is calm.</summary>
    public static CircesMood Of(TabState state, string today)
    {
        if (state.HeatDay != today || state.Heat == null) return Calm;
        return FromStep(Hottest(state.Heat) - Math.Max(0, state.HeatCool));
    }

    /// <summary>A credit booked today: one step cooler, never cooler than the hottest row is hot.
    /// A credit on a day with no heat yet starts the day and cools nothing.</summary>
    public static void Cool(TabState state, string today)
    {
        if (state.HeatDay != today || state.Heat == null)
        {
            state.HeatDay = today;
            state.Heat = new Dictionary<string, int>(StringComparer.Ordinal);
            state.HeatCool = 0;
            return;
        }
        state.HeatCool = Math.Min(Math.Max(0, state.HeatCool) + 1, Hottest(state.Heat));
    }
}
