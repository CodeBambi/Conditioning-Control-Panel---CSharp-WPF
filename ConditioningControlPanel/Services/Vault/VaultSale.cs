using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Vault;

/// <summary>A live first-month sale, as <c>GET /config/vault-sale</c> describes it.</summary>
public sealed record VaultSaleInfo(int Percent, DateTime? EndsAtUtc, IReadOnlyCollection<int> Tiers);

/// <summary>
/// The pure half of the unlock card's sale prices. The server switches a sale on with
/// <c>{"active":true,"percent":50,"ends_at":"...","tiers":["basic","prime"]}</c>; the discount
/// itself happens on Patreon and covers the FIRST MONTH ONLY, so the card only changes its text.
/// Everything here fails closed: anything odd means no sale and the normal prices.
/// </summary>
public static class VaultSale
{
    public const int MinPercent = 5;
    public const int MaxPercent = 90;

    /// <summary>The sale in a config body, or null when there is none, it is off, or the body is odd.</summary>
    public static VaultSaleInfo? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            // Dates stay strings: Json.NET would otherwise guess a kind for an offset-less stamp.
            using var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None };
            if (JToken.ReadFrom(reader) is not JObject o) return null;
            if (o["active"]?.Type != JTokenType.Boolean || !o["active"]!.Value<bool>()) return null;

            var p = o["percent"];
            if (p?.Type != JTokenType.Integer) return null;
            var percent = p.Value<long>();
            if (percent < MinPercent || percent > MaxPercent) return null;

            DateTime? ends = null;
            var e = o["ends_at"];
            if (e != null && e.Type != JTokenType.Null)
            {
                if (e.Type == JTokenType.String && DateTime.TryParse(e.Value<string>(), CultureInfo.InvariantCulture,
                             DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                    ends = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                else return null;
            }

            if (o["tiers"] is not JArray list) return null;
            var tiers = new HashSet<int>();
            foreach (var t in list)
            {
                if (t.Type != JTokenType.String) continue;
                var tier = TierFor(t.Value<string>());
                if (tier > 0) tiers.Add(tier);
            }
            return tiers.Count == 0 ? null : new VaultSaleInfo((int)percent, ends, tiers);
        }
        catch (Exception ex) { Diag.Swallowed(ex, "vault sale body not JSON"); return null; }
    }

    /// <summary>"basic" is tier 1, "prime" tier 2, anything else 0.</summary>
    public static int TierFor(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "basic" => 1,
        "prime" => 2,
        _ => 0,
    };

    /// <summary>Whether the sale covers this tier right now. Yearly billing never has a sale.</summary>
    public static bool AppliesTo(VaultSaleInfo? sale, int tier, DateTime nowUtc, bool yearly = false)
    {
        if (sale == null || yearly) return false;
        if (sale.EndsAtUtc is DateTime end && nowUtc >= end) return false;
        return sale.Tiers.Contains(tier >= 2 ? 2 : 1);
    }

    /// <summary>The first month's price in cents, rounded to the cent.</summary>
    public static int FirstMonthCents(int monthlyCents, int percent)
        => (int)Math.Round(monthlyCents * (100 - Math.Clamp(percent, 0, 100)) / 100m, MidpointRounding.AwayFromZero);

    /// <summary>The sale's end as the card prints it, in local time ("Sat 10 Oct"), or null for an open sale.</summary>
    public static string? EndsText(VaultSaleInfo sale, TimeZoneInfo? zone = null, CultureInfo? culture = null)
    {
        if (sale.EndsAtUtc is not DateTime end) return null;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(end, DateTimeKind.Utc), zone ?? TimeZoneInfo.Local);
        return local.ToString("ddd d MMM", culture ?? CultureInfo.CurrentCulture);
    }
}
