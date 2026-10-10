using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Lane k9 (HA1): the Just Drop door's rules as WPF 7.1.5 has them. The door is the
/// server's flag and nothing else (Tier 0, no tier check), the app's token rides exactly one
/// request, and a drop pays once per order by the host's own clock.</summary>
[Collection(SessionStatics.Name)]
public sealed class JustDropServiceTests
{
    [Theory]
    [InlineData("{\"enabled\":true}", true)]
    [InlineData("{\"enabled\":false}", false)]
    [InlineData("{\"enabled\":\"true\"}", false)]   // only a literal true opens the door
    [InlineData("{\"enabled\":1}", false)]
    [InlineData("{}", false)]
    [InlineData("", false)]
    [InlineData("not json", false)]
    [InlineData(null, false)]
    public void TheDoorOpensOnlyOnALiteralTrue(string? body, bool expected) =>
        Assert.Equal(expected, JustDropService.ParseEnabled(body));

    [Fact]
    public void TheDoorIsShutUntilTheServerSaysSo_AndTheFlagRaisesTheEvent()
    {
        var prevFetch = JustDropService.ConfigFetchOverride;
        int raised = 0;
        EventHandler onChange = (_, _) => raised++;
        JustDropService.AvailabilityChanged += onChange;
        try
        {
            JustDropService.SetServerEnabledForTests(false);
            raised = 0;
            Assert.False(JustDropService.DoorAvailable);

            // A failed read leaves the door as it was.
            JustDropService.ConfigFetchOverride = () => System.Threading.Tasks.Task.FromResult<(bool, string?)>((false, null));
            JustDropService.RefreshAvailabilityAsync().GetAwaiter().GetResult();
            Assert.False(JustDropService.DoorAvailable);
            JustDropService.ConfigFetchOverride = () => throw new InvalidOperationException("offline");
            JustDropService.RefreshAvailabilityAsync().GetAwaiter().GetResult();
            Assert.False(JustDropService.DoorAvailable);
            Assert.Equal(0, raised);

            JustDropService.ConfigFetchOverride = () => System.Threading.Tasks.Task.FromResult<(bool, string?)>((true, "{\"enabled\":true}"));
            JustDropService.RefreshAvailabilityAsync().GetAwaiter().GetResult();
            Assert.True(JustDropService.DoorAvailable);
            Assert.Equal(1, raised);

            // The same answer again raises nothing.
            JustDropService.RefreshAvailabilityAsync().GetAwaiter().GetResult();
            Assert.Equal(1, raised);
        }
        finally
        {
            JustDropService.AvailabilityChanged -= onChange;
            JustDropService.ConfigFetchOverride = prevFetch;
            JustDropService.SetServerEnabledForTests(false);
        }
    }

    [Fact]
    public void SignedOut_TheShopOpensDirectly_SignedIn_ThroughTheHandoff_AndTheTokenIsNeverInTheUrl()
    {
        Assert.Equal("https://app.cclabs.app/dashboard/express", JustDropHostService.BuildStartUrl(JustDropHostService.ShopPath, null, null));
        Assert.Equal("https://app.cclabs.app/dashboard/express", JustDropHostService.BuildStartUrl(JustDropHostService.ShopPath, "u_1", ""));
        Assert.Equal("https://app.cclabs.app/dashboard/express", JustDropHostService.BuildStartUrl(JustDropHostService.ShopPath, "", "tok"));

        var url = JustDropHostService.BuildStartUrl(JustDropHostService.ShopPath, "u 1", "SECRET-TOKEN");
        Assert.Equal("https://app.cclabs.app/api/auth/desktop-session?unified_id=u%201&next=%2Fdashboard%2Fexpress", url);
        Assert.DoesNotContain("SECRET-TOKEN", url);

        var replay = JustDropHostService.BuildStartUrl(JustDropHostService.ReplayPath(" AB 12 "), "u1", "SECRET-TOKEN");
        Assert.Contains("next=%2Fexpress%2Fplay%3Forder%3DAB%252012", replay);
        Assert.DoesNotContain("SECRET-TOKEN", replay);
    }

    [Theory]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress", true)]
    [InlineData("https://APP.cclabs.app/api/auth/desktop-session", true)]
    [InlineData("http://app.cclabs.app/api/auth/desktop-session", false)]            // never in the clear
    [InlineData("https://app.cclabs.app:8443/api/auth/desktop-session", false)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session/extra", false)]
    [InlineData("https://app.cclabs.app/api/auth/Desktop-Session", false)]
    [InlineData("https://app.cclabs.app/dashboard/express", false)]                  // no other request carries it
    [InlineData("https://app.cclabs.app/api/orders", false)]
    [InlineData("https://cclabs.app/api/auth/desktop-session", false)]
    [InlineData("https://app.cclabs.app.evil.example/api/auth/desktop-session", false)]
    [InlineData("https://evil.example/api/auth/desktop-session?x=app.cclabs.app", false)]
    [InlineData("https://user@app.cclabs.app/api/auth/desktop-session", false)]
    public void TheTokenRidesOnlyTheHandoffRequest(string request, bool carries)
    {
        Assert.Equal(carries ? "tok" : null, JustDropHostService.AuthHeaderFor(new Uri(request), "tok"));
        Assert.Null(JustDropHostService.AuthHeaderFor(new Uri(request), ""));      // signed out: never a header
    }

    [Theory]
    [InlineData("https://app.cclabs.app/dashboard/express", true)]
    [InlineData("https://app.cclabs.app/express/play?order=A", true)]
    [InlineData("http://app.cclabs.app/dashboard/express", false)]
    [InlineData("https://cclabs.app/", false)]
    [InlineData("https://app.cclabs.app.evil.example/", false)]
    [InlineData("https://ccp.game/fyp/index.html", false)]
    public void TheWindowStaysOnTheSite(string url, bool allowed) =>
        Assert.Equal(allowed, JustDropHostService.IsSiteUrl(new Uri(url)));

    [Theory]
    // size, host seconds, drops already paid today, level -> xp, taste, diminished
    [InlineData("M", 900.0, 0, 1, 650, false, false)]
    [InlineData("M", 720.0, 0, 1, 650, false, false)]      // 80% of the size's length is trusted
    [InlineData("M", 719.0, 0, 1, 40, true, false)]
    [InlineData("S", 300.0, 0, 1, 300, false, false)]
    [InlineData("S", 119.0, 0, 1, 40, true, false)]        // under two minutes is always a taste
    [InlineData("L", 1800.0, 0, 1, 1000, false, false)]
    [InlineData("XXL", 3600.0, 0, 1, 1400, false, false)]
    [InlineData("xxl", 3600.0, 0, 1, 1400, false, false)]
    [InlineData("HUGE", 900.0, 0, 1, 650, false, false)]   // unknown size = M
    [InlineData(null, 900.0, 0, 1, 650, false, false)]
    [InlineData("S", 300.0, 2, 1, 300, false, false)]      // the third drop of the day still pays whole
    [InlineData("S", 300.0, 3, 1, 75, false, true)]        // the fourth pays a quarter
    [InlineData("M", 900.0, 0, 80, 975, false, false)]     // level multiplier (x1.5 at 80)
    public void ADropSettlesBySizeClockDayAndLevel(string? size, double seconds, int before, int level, int xp, bool taste, bool dim)
    {
        Assert.Equal(xp, JustDropService.SettleDropXp(size, seconds, before, level, out var quick, out var diminished));
        Assert.Equal(taste, quick);
        Assert.Equal(dim, diminished);
    }

    [Fact]
    public void ACompletionTheHostNeverTimedIsATaste()
    {
        Assert.Equal(JustDropService.QuickTasteXp, JustDropService.SettleDropXp("XXL", null, 0, 1, out var quick, out _));
        Assert.True(quick);
    }

    [Fact]
    public void AnOrderIsCreditedOnce_AcrossAReload()
    {
        var file = Path.Combine(Path.GetTempPath(), "k9-credited-" + Guid.NewGuid().ToString("N") + ".json");
        var prev = CreditedOrders.FilePathOverride;
        try
        {
            CreditedOrders.FilePathOverride = file;
            Assert.False(CreditedOrders.TryCredit(" "));
            Assert.True(CreditedOrders.TryCredit(" JD-1 "));
            Assert.False(CreditedOrders.TryCredit("JD-1"));
            Assert.True(CreditedOrders.IsCredited("JD-1"));
            Assert.False(CreditedOrders.IsCredited("jd-1"));       // codes are exact

            CreditedOrders.FilePathOverride = file;                 // drops the cache: read from disk again
            Assert.False(CreditedOrders.TryCredit("JD-1"));
            Assert.True(CreditedOrders.TryCredit("JD-2"));
        }
        finally
        {
            CreditedOrders.FilePathOverride = prev;
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public void TheBridge_PaysAFirstPlayOnce_ByTheHostClock_AndIgnoresForeignFrames()
    {
        var file = Path.Combine(Path.GetTempPath(), "k9-bridge-" + Guid.NewGuid().ToString("N") + ".json");
        var (prevFile, prevXp, prevNow) = (CreditedOrders.FilePathOverride, CoreProgression.AddXPProvider, JustDropService.UtcNow);
        var s = CoreSettings.Current;
        var (prevDay, prevCount, prevLevel) = (s.JustDropXpDayKey, s.JustDropCreditedToday, s.PlayerLevel);
        var paid = new List<(double Xp, string Source)>();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            CreditedOrders.FilePathOverride = file;
            CoreProgression.AddXPProvider = (xp, source) => paid.Add((xp, source));
            JustDropService.UtcNow = () => now;
            s.JustDropXpDayKey = "";
            s.JustDropCreditedToday = 0;
            s.PlayerLevel = 1;

            static string Frame(string type, string code, string source = "justdrop", int v = 1) =>
                "{\"source\":\"" + source + "\",\"v\":" + v + ",\"type\":\"" + type + "\",\"payload\":{\"orderCode\":\"" + code
                + "\",\"sizeId\":\"S\",\"durationSec\":99999}}";

            // Not ours, a version we do not speak, junk: nothing happens.
            JustDropService.HandleWebMessage(Frame("session-complete", "A", source: "site"));
            JustDropService.HandleWebMessage(Frame("session-complete", "A", v: 2));
            JustDropService.HandleWebMessage("not json");
            JustDropService.HandleWebMessage(null);
            Assert.Empty(paid);
            Assert.False(CreditedOrders.IsCredited("A"));

            // A timed run: the page's durationSec is never trusted, the host's five minutes are.
            JustDropService.HandleWebMessage(Frame("session-start", "A"));
            now = now.AddSeconds(300);
            JustDropService.HandleWebMessage(Frame("session-complete", "A"));
            Assert.Equal(new[] { (300.0, "Other") }, paid);
            Assert.Equal(300, JustDropService.LastAwardedXp);
            Assert.Equal(1, s.JustDropCreditedToday);

            // The same order again is a replay: nothing, however long it ran.
            JustDropService.HandleWebMessage(Frame("session-start", "A"));
            now = now.AddSeconds(300);
            JustDropService.HandleWebMessage(Frame("session-complete", "A"));
            Assert.Single(paid);
            Assert.Equal(0, JustDropService.LastAwardedXp);

            // A completion with no start the host saw is a taste, whatever the page claims.
            JustDropService.HandleWebMessage(Frame("session-complete", "B"));
            Assert.Equal((40.0, "Other"), paid[^1]);
        }
        finally
        {
            CreditedOrders.FilePathOverride = prevFile;
            CoreProgression.AddXPProvider = prevXp;
            JustDropService.UtcNow = prevNow;
            (s.JustDropXpDayKey, s.JustDropCreditedToday, s.PlayerLevel) = (prevDay, prevCount, prevLevel);
            try { File.Delete(file); } catch { }
        }
    }
}
