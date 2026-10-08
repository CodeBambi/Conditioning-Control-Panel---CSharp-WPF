using System;
using System.Linq;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Back Room prize ownership on this head. WPF asks <c>PrizeGrants.IsGranted</c>, backed by
    /// OwnershipService snapshots from BackRoomApi; neither exists here yet (parity row
    /// svc-prize-ownership), so every account is unowned - exactly what WPF shows an account that
    /// bought nothing. The one exception is WPF's DEBUG-only <c>CCP_PRIZE_GRANTS</c> override
    /// (OwnershipService.ReadDebugOverride): same variable, same patterns (exact id,
    /// <c>prefix.*</c>, <c>*</c>), and a Release build never reads it.
    /// </summary>
    internal static class PrizeOwnership
    {
        /// <summary>WPF <c>PrizeGrants.FlashDriftBounce</c>: a wire id, spelled as the server does.</summary>
        internal const string FlashDriftBounce = "fx.flash.drift_bounce";

        /// <summary>The DEBUG override spec, read once; always null in Release.</summary>
        internal static readonly string? OverrideSpec = ReadOverride();

        /// <summary>Test seam: the owned-check every caller uses.</summary>
        internal static Func<string, bool> IsGranted = id => Matches(OverrideSpec, id);

        private static string? ReadOverride()
        {
#if DEBUG
            return Environment.GetEnvironmentVariable("CCP_PRIZE_GRANTS");
#else
            return null;
#endif
        }

        /// <summary>WPF OwnershipService.ParseOverride + MatchesOverride over one comma list.</summary>
        internal static bool Matches(string? spec, string id) =>
            !string.IsNullOrEmpty(spec) && spec.Split(',').Select(p => p.Trim()).Any(p =>
                p == "*" || p == id
                || (p.Length > 2 && p.EndsWith(".*", StringComparison.Ordinal) && p.IndexOf('*') == p.Length - 1
                    && p[^3] != '.' && id.StartsWith(p[..^1], StringComparison.Ordinal)));
    }
}
