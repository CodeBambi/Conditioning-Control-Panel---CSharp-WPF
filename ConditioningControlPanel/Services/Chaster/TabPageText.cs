using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>What the Circe's tab page prints, kept out of the view so it is tested without WPF:
/// which loc key names a row, how a price reads, and how the rows split into what costs and what
/// earns back.</summary>
public static class TabPageText
{
    public static string NameKey(string eventId) => "chaster_price_" + eventId;

    /// <summary>"+0:15", "-10:00", or "+0:10 each" for a per-unit row. <paramref name="eachFormat"/>
    /// is the localised "{0} each".</summary>
    public static string Price(TabPrice price, string eachFormat)
    {
        var figure = CircesTab.Format(price.Seconds);
        return price.PerUnit ? string.Format(eachFormat, figure) : figure;
    }

    /// <summary>Costs first (biggest first), then what earns time back (biggest first). Two short
    /// lists read at a glance; one list sorted by id does not.</summary>
    public static (IReadOnlyList<TabPrice> Costs, IReadOnlyList<TabPrice> EarnBacks) Split(IEnumerable<TabPrice> prices)
    {
        var all = prices.ToList();
        return (all.Where(p => p.Seconds > 0).OrderByDescending(p => p.Seconds).ToList(),
                all.Where(p => p.Seconds < 0).OrderBy(p => p.Seconds).ToList());
    }
}
