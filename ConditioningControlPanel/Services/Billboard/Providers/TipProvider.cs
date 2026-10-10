using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>TIP, the adapter: nothing to read but the tier.</summary>
    public sealed class TipProvider : BillboardProviderBase
    {
        public override string Id => "tip";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) =>
            Safe(() => TipCards.Decide(context.Tier, context.NowUtc, Loc));
    }
}
