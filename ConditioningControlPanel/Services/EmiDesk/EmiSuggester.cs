using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>One card's worth of ring: what it points at, and the two flags that paint it.</summary>
/// <param name="Target">The catalogue entry.</param>
/// <param name="Pinned">The user nailed this card to this slot. Usage never moves it.</param>
/// <param name="Locked">The tier gate refuses it today. It still shows, with a padlock.</param>
public sealed record EmiRingSlot(EmiTarget Target, bool Pinned, bool Locked);

/// <summary>
/// The suggester: six slots, no machine learning, one formula.
///
/// <para><b>Score.</b> A target's score is the sum, over every open ever recorded, of
/// <c>0.5 ^ (ageInDays / 7)</c>: an open is worth 1 today, half a point next week, a quarter the
/// week after. <see cref="EmiState.NoteUsage"/> keeps that sum incrementally (score decayed forward,
/// plus one) so a target costs one double and one timestamp rather than a list, and
/// <see cref="ScoreOf"/> finishes the decay from the last open to now on read.</para>
///
/// <para><b>Fill.</b> Pins take their slots first, in pin order. The rest go to the top scores, ties
/// broken by catalogue order, which is also what a brand new user sees because every score is then
/// zero. Unavailable targets are skipped entirely. At most ONE locked card is ever in the ring, and
/// only when the user has not already earned six unlocked ones.</para>
///
/// <para>Every open counts, wherever it came from: the ring, the nav rail, a hotkey, a host launch.
/// The counter lives on <c>App.EmiDesk.NoteOpen</c>, which the app calls from
/// <c>MainWindow.ShowTab</c>, <c>OpenStudioModule</c> and each host's <c>Launch</c>.</para>
/// </summary>
public static class EmiSuggester
{
    /// <summary>Six cards, ever. No scroll, no second page.</summary>
    public const int Slots = 6;

    /// <summary>A pin per slot is the ceiling: pinning all six turns the suggester off, deliberately.</summary>
    public const int MaxPins = 6;

    /// <summary>Half-life of an open, in days.</summary>
    public const double HalfLifeDays = 7.0;

    private static IReadOnlyList<EmiRingSlot> _last = Array.Empty<EmiRingSlot>();

    /// <summary>The most recently composed ring. Used for the <c>pickIsTop</c> moment payload.</summary>
    public static IReadOnlyList<EmiRingSlot> Last => _last;

    /// <summary>True when <paramref name="id"/> was sitting in the first slot of the last ring.</summary>
    public static bool TopSlotIs(string? id)
    {
        try
        {
            var l = _last;
            return l.Count > 0 && !string.IsNullOrEmpty(id)
                   && string.Equals(l[0].Target.Id, id, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    // ---- score -----------------------------------------------------------------

    /// <summary>
    /// A target's decayed open score as of NOW. The stored number is only current as of that
    /// target's last open, so the remaining decay is applied here rather than on a timer.
    /// </summary>
    public static double ScoreOf(string? id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        try
        {
            var st = EmiState.Current;
            if (!st.OpenScore.TryGetValue(id, out double score) || score <= 0) return 0;
            if (!st.UsageAt.TryGetValue(id, out long lastAt) || lastAt <= 0) return score;

            double days = (DateTime.UtcNow - new DateTime(lastAt, DateTimeKind.Utc)).TotalDays;
            if (days <= 0) return score;

            double v = score * Math.Pow(0.5, days / HalfLifeDays);
            return double.IsNaN(v) || double.IsInfinity(v) || v < 0 ? 0 : v;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] score read failed for {Target}", id);
            return 0;
        }
    }

    // ---- pins ------------------------------------------------------------------

    /// <summary>Is this target nailed to a slot?</summary>
    public static bool IsPinned(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        try { return EmiState.Current.Pins.Contains(id, StringComparer.Ordinal); }
        catch { return false; }
    }

    /// <summary>
    /// Pin or unpin, persisted. Returns the state the target ended in, which is not always the
    /// state that was asked for: pinning a seventh card is refused rather than silently evicting
    /// one the user put there on purpose.
    /// </summary>
    public static bool TogglePin(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        try
        {
            var st = EmiState.Current;
            if (st.Pins.Contains(id, StringComparer.Ordinal))
            {
                st.Pins.RemoveAll(p => string.Equals(p, id, StringComparison.Ordinal));
                EmiState.SaveSoon();
                Log.Information("[EmiDesk] unpinned {Target}", id);
                return false;
            }

            if (st.Pins.Count >= MaxPins)
            {
                Log.Information("[EmiDesk] pin refused for {Target}, all {Max} slots are pinned", id, MaxPins);
                return false;
            }

            st.Pins.Add(id);
            EmiState.SaveSoon();
            Log.Information("[EmiDesk] pinned {Target} ({Count}/{Max})", id, st.Pins.Count, MaxPins);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] pin toggle failed for {Target}", id);
            return IsPinned(id);
        }
    }

    /// <summary>
    /// Drop every pin, which is the settings picker's "let her choose" button: the ring goes back
    /// to being six scored suggestions. Returns how many pins were cleared, so the caller can skip
    /// a save and a re-fan when there was nothing to clear.
    /// </summary>
    public static int ClearPins()
    {
        try
        {
            var st = EmiState.Current;
            int n = st.Pins.Count;
            if (n == 0) return 0;

            st.Pins.Clear();
            EmiState.SaveSoon();
            Log.Information("[EmiDesk] pins cleared ({Count}), the ring is hers again", n);
            return n;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ClearPins failed");
            return 0;
        }
    }

    /// <summary>Move a target into the first pinned slot (the "front row" offer effect).</summary>
    public static void PinToTop(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        try
        {
            var st = EmiState.Current;
            st.Pins.RemoveAll(p => string.Equals(p, id, StringComparison.Ordinal));
            st.Pins.Insert(0, id);
            while (st.Pins.Count > MaxPins) st.Pins.RemoveAt(st.Pins.Count - 1);
            EmiState.SaveSoon();
            Log.Information("[EmiDesk] pinned {Target} to the top slot", id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] PinToTop failed for {Target}", id);
        }
    }

    // ---- the fill --------------------------------------------------------------

    /// <summary>
    /// Build the ring. Never throws, never returns the same target twice, never returns more than
    /// <see cref="Slots"/> slots, and returns fewer only when the app genuinely has fewer doors.
    /// </summary>
    public static IReadOnlyList<EmiRingSlot> Compose()
    {
        var slots = new List<EmiRingSlot>(Slots);
        try
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);

            // 1. pins keep their slots, in pin order. A pin whose door went dark is skipped but
            //    stays in state: the door may come back, and losing the pin would be rude.
            foreach (var id in EmiState.Current.Pins.ToList())
            {
                if (slots.Count >= Slots) break;
                if (!taken.Add(id)) continue;
                var t = EmiTargets.Find(id);
                if (t == null || !t.Available) continue;
                slots.Add(new EmiRingSlot(t, true, t.Locked));
            }

            int free = Slots - slots.Count;
            if (free > 0)
            {
                var pool = EmiTargets.All
                    .Where(t => !taken.Contains(t.Id) && t.Available)
                    .Select(t => (Target: t, Score: ScoreOf(t.Id), Locked: t.Locked))
                    .ToList();

                var unlocked = pool.Where(p => !p.Locked)
                    .OrderByDescending(p => p.Score)
                    .ThenBy(p => EmiTargets.OrderOf(p.Target.Id))
                    .ToList();

                var fill = unlocked.Select(p => new EmiRingSlot(p.Target, false, false)).ToList();

                // 2. at most ONE locked card, and only when the user has not already earned six
                //    unlocked ones. A pinned locked card is already that one.
                bool lockedAlready = slots.Any(s => s.Locked);
                if (!lockedAlready)
                {
                    var top = pool.Where(p => p.Locked)
                        .OrderByDescending(p => p.Score)
                        .ThenBy(p => EmiTargets.OrderOf(p.Target.Id))
                        .Select(p => p.Target)
                        .FirstOrDefault();

                    if (top != null)
                    {
                        int earned = unlocked.Count(p => p.Score > 0);
                        if (fill.Count < free)
                        {
                            // Not enough unlocked doors to fill the ring: the locked one takes the gap.
                            fill.Add(new EmiRingSlot(top, false, true));
                        }
                        else if (earned > 0 && earned < Slots)
                        {
                            // She has habits, but not six of them: the locked card goes in right
                            // after the earned ones, never in front of them and never at slot one
                            // on a fresh install.
                            int at = Math.Min(Math.Min(earned, free - 1), fill.Count);
                            fill.Insert(Math.Max(at, 0), new EmiRingSlot(top, false, true));
                        }
                    }
                }

                foreach (var s in fill)
                {
                    if (slots.Count >= Slots) break;
                    if (!taken.Add(s.Target.Id)) continue;
                    slots.Add(s);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring compose failed, falling back to the catalogue head");
            try
            {
                slots = EmiTargets.All.Where(t => t.Available).Take(Slots)
                    .Select(t => new EmiRingSlot(t, false, false)).ToList();
            }
            catch { slots = new List<EmiRingSlot>(); }
        }

        _last = slots;
        return slots;
    }
}
