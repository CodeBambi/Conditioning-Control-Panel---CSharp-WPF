using System;
using System.Globalization;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The QA switches for EMI Desk, read ONCE from the environment at first touch.
///
/// <para>EMI's cadence is deliberately slow: the glass waits 90 s of stillness before it flips to a
/// channel, a line is gated behind a 45 s global floor and a per-moment cooldown that runs to
/// minutes, and an offer needs a third summon plus 10 minutes since the last one. Those numbers are
/// owner locks (BRIEF 6 / 7 / 8) and none of them are tunable from the UI, which makes the glass,
/// the asks and most of the moments unreachable in a play-test that lasts minutes rather than
/// hours. Rather than edit the constants for a run and risk shipping the edit, both live here
/// behind environment variables that are absent in every normal launch.</para>
///
/// <list type="bullet">
/// <item><b>EMI_DESK_IDLE_MS</b> - milliseconds of stillness before the glass flips to a channel,
/// in place of the locked 90 000. Clamped to 1 000 .. 3 600 000.</item>
/// <item><b>EMI_DESK_DEBUG</b> - set to 1/true/yes/on for QA cadence: the 45 s global floor and the
/// per-moment cooldowns are ignored, the odds roll always passes, and the offer cadence gates (the
/// 10 minute gap and "never before the third summon") are skipped. The gates that exist for the
/// USER are untouched: holds, the panic silence, bedtime, the ignore streak, spice, feasibility and
/// the situational half of the ask gate all still apply.</item>
/// <item><b>EMI_DESK_RESET_ONBOARDING</b> - set to 1/true/yes/on to wipe the three gesture nudge
/// tracks back to a fresh install at startup (pet count, ring opens, the gist latches and the
/// lifetime fire counts), so the tutorial can be play-tested more than once per machine. It resets
/// ONLY the onboarding fields; the rest of the ledger, including the summon count and the streaks,
/// is left alone.</item>
/// </list>
///
/// <para>Nothing here changes behaviour unless the variable is present, so a normal launch reads
/// two environment variables at startup and is otherwise identical.</para>
/// </summary>
public static class EmiDebug
{
    private const int IdleFloorMs = 1_000;
    private const int IdleCeilingMs = 3_600_000;

    /// <summary>Glass idle override in ms, or null when EMI_DESK_IDLE_MS is unset or unusable.</summary>
    public static int? IdleMs { get; }

    /// <summary>True when EMI_DESK_DEBUG asks for the QA cadence.</summary>
    public static bool Enabled { get; }

    /// <summary>True when EMI_DESK_RESET_ONBOARDING asks for the gesture tutorial to replay.</summary>
    public static bool ResetOnboarding { get; }

    static EmiDebug()
    {
        try
        {
            var raw = Environment.GetEnvironmentVariable("EMI_DESK_IDLE_MS");
            if (!string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            {
                IdleMs = Math.Clamp(ms, IdleFloorMs, IdleCeilingMs);
            }

            Enabled = IsOn(Environment.GetEnvironmentVariable("EMI_DESK_DEBUG"));

            ResetOnboarding = IsOn(Environment.GetEnvironmentVariable("EMI_DESK_RESET_ONBOARDING"));

            if (IdleMs != null || Enabled || ResetOnboarding)
            {
                Log.Information(
                    "[EmiDesk] DEBUG overrides active: idleMs={Idle}, qaCadence={Qa}, resetOnboarding={Reset}",
                    IdleMs?.ToString(CultureInfo.InvariantCulture) ?? "default", Enabled, ResetOnboarding);
            }
        }
        catch (Exception ex)
        {
            // A static constructor that throws poisons every touch of the type for the process, so
            // this one cannot be allowed to: a bad environment variable simply means no override.
            try { Log.Debug(ex, "[EmiDesk] debug override probe failed"); } catch { }
            IdleMs = null;
            Enabled = false;
            ResetOnboarding = false;
        }
    }

    /// <summary>The one truthiness reading every switch here shares.</summary>
    private static bool IsOn(string? raw) =>
        !string.IsNullOrWhiteSpace(raw)
        && raw!.Trim() is "1" or "true" or "TRUE" or "True"
                       or "yes" or "YES" or "Yes" or "on" or "ON" or "On";
}
