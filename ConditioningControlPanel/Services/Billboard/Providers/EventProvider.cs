using System;
using System.Collections.Generic;
using System.Globalization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    // The pure half (RaffleReading, EventCards) lives once in CCP.Core/Board/Providers, shared with the Avalonia head.

    /// <summary>EVENT, the adapter: <see cref="ChasterService.IsLinked"/> (a cached token read) and
    /// <see cref="ChasterService.LastLadderVerify"/> (kept in memory by the service).</summary>
    public sealed class EventProvider : BillboardProviderBase
    {
        private bool _hooked;

        public override string Id => "event";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            var chaster = App.Chaster;
            if (chaster == null) return Array.Empty<BillboardCardSpec>();
            if (!_hooked) { chaster.LinkChanged += RaiseChanged; _hooked = true; }
            var verify = chaster.LastLadderVerify;
            var reading = verify is { Ok: true } ? new RaffleReading(verify.DaysCounted, verify.Seconds) : null;
            var card = EventCards.Locktober(chaster.IsLinked, context.NowUtc, reading, Loc);
            return card == null ? Array.Empty<BillboardCardSpec>() : new[] { card };
        });
    }
}
