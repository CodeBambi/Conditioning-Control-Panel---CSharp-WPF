using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Patreon row in Settings &gt; Account, the health of the grant behind it, and the 409 a
/// reconnect always earns.
///
/// <para>Ticket 1551244367040221235 (Sep 2026): a tier 2 patron lost premium while the server said
/// patron on every sync. Her Patreon OAuth grant had died on her PC, nothing re-stamped the 14-day
/// window, and the old rule HID the Patreon button exactly because the account was already linked
/// server-side. Every surface said "connected" and there was no way back short of signing out.</para>
///
/// <para>Four things are pinned here, because all four compile either way: the row's state table;
/// that a dead grant is told apart from a bad connection (the flag that makes the row reachable at
/// all for that account); the two 409s, which share most of their words; and that the UI really
/// consults the rule. Source-text reads for the last: MainWindow cannot be instantiated here.</para>
/// </summary>
public class PatreonReconnectRuleTests
{
    // ------------------------------------------------------------------ 1. the state table

    /// <summary>No cloud identity: the whole Link Accounts section belongs to signed-in users.</summary>
    [Fact]
    public void NoUnifiedId_IsHidden()
    {
        var row = PatreonReconnectRule.Decide(
            hasUnifiedId: false, linkedServerSide: false, desktopAuthenticated: false,
            hasPremiumNow: false, whitelisted: false);

        Assert.Equal(PatreonLinkAction.Hidden, row.Action);
        Assert.False(row.ShowsButton);
        Assert.False(row.ShowsHint);
    }

    /// <summary>Signed in with Discord only, no Patreon anywhere: the original offer, unchanged.</summary>
    [Fact]
    public void NeverLinked_OffersLink()
    {
        var row = PatreonReconnectRule.Decide(
            hasUnifiedId: true, linkedServerSide: false, desktopAuthenticated: false,
            hasPremiumNow: false, whitelisted: false);

        Assert.Equal(PatreonLinkAction.Link, row.Action);
        Assert.True(row.ShowsButton);
        Assert.True(row.Filled);
        // A first link is an invitation, not a repair, so it carries no expiry line.
        Assert.False(row.ShowsHint);
    }

    /// <summary>The grant works: nothing to fix, and the old behaviour for a linked patron.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DesktopAuthenticated_IsHidden(bool linkedServerSide)
    {
        var row = PatreonReconnectRule.Decide(
            hasUnifiedId: true, linkedServerSide: linkedServerSide, desktopAuthenticated: true,
            hasPremiumNow: true, whitelisted: false);

        Assert.Equal(PatreonLinkAction.Hidden, row.Action);
        Assert.False(row.ShowsButton);
    }

    /// <summary>The ticket's account: linked, no working grant, premium already off. Prominent.</summary>
    [Fact]
    public void LinkedButNoTokenAndPremiumLocked_IsProminentReconnect()
    {
        var row = PatreonReconnectRule.Decide(
            hasUnifiedId: true, linkedServerSide: true, desktopAuthenticated: false,
            hasPremiumNow: false, whitelisted: false);

        Assert.Equal(PatreonLinkAction.Reconnect, row.Action);
        Assert.True(row.ShowsButton);
        Assert.True(row.Prominent);
        Assert.True(row.ShowsHint);
        Assert.True(row.Filled);
    }

    /// <summary>
    /// Same shape, but premium still holds (the window has not lapsed, or SubscribeStar is
    /// carrying them, or they are whitelisted and ProfileSync keeps re-stamping). The button is
    /// there because it is the only way to stop the clock, but it must not shout and must not
    /// claim anything has expired. This row, not the whitelist branch below, is what actually
    /// keeps whitelisted accounts off the prominent state.
    /// </summary>
    [Fact]
    public void LinkedButNoTokenWhilePremiumHolds_IsQuietReconnect()
    {
        var row = PatreonReconnectRule.Decide(
            hasUnifiedId: true, linkedServerSide: true, desktopAuthenticated: false,
            hasPremiumNow: true, whitelisted: false);

        Assert.Equal(PatreonLinkAction.Reconnect, row.Action);
        Assert.True(row.ShowsButton);
        Assert.False(row.Prominent);
        Assert.False(row.ShowsHint);
        Assert.False(row.Filled);
    }

    /// <summary>
    /// A whitelisted account is entitled with no Patreon grant at all, so reconnecting is a chore
    /// with no payoff. A courtesy branch rather than the safety net: <c>IsWhitelisted</c> is an
    /// in-memory flag written by a validate or a sync, so on a launch where neither reached the
    /// server it arrives false and this shape never occurs. See the quiet-reconnect test above.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Whitelisted_IsNeverNagged(bool hasPremiumNow)
    {
        var row = PatreonReconnectRule.Decide(
            hasUnifiedId: true, linkedServerSide: true, desktopAuthenticated: false,
            hasPremiumNow: hasPremiumNow, whitelisted: true);

        Assert.Equal(PatreonLinkAction.Hidden, row.Action);
        Assert.False(row.ShowsButton);
        Assert.False(row.ShowsHint);
    }

    /// <summary>The whitelist rule is scoped to the reconnect branch, not to the whole row.</summary>
    [Fact]
    public void WhitelistedAndNeverLinked_StillOffersLink()
    {
        var row = PatreonReconnectRule.Decide(
            hasUnifiedId: true, linkedServerSide: false, desktopAuthenticated: false,
            hasPremiumNow: true, whitelisted: true);

        Assert.Equal(PatreonLinkAction.Link, row.Action);
    }

    /// <summary>Prominence is a property of the reconnect and of nothing else.</summary>
    [Fact]
    public void OnlyReconnectIsEverProminent()
    {
        foreach (var unified in new[] { true, false })
        foreach (var linked in new[] { true, false })
        foreach (var authed in new[] { true, false })
        foreach (var premium in new[] { true, false })
        foreach (var white in new[] { true, false })
        {
            var row = PatreonReconnectRule.Decide(unified, linked, authed, premium, white);
            if (row.Action != PatreonLinkAction.Reconnect)
                Assert.False(row.Prominent, $"{row.Action} claimed prominence");
        }
    }

    // ------------------------------------------------- 2. is the grant dead, or is the wifi

    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime Ago(TimeSpan t) => Now - t;

    private static PatreonRefreshOutcome Classify(
        HttpStatusCode? status, string? oauthError = null, bool threw = false, TimeSpan? expiredFor = null)
        => PatreonGrantHealth.Classify(status, oauthError, threw,
            expiredFor == null ? null : Ago(expiredFor.Value), Now);

    /// <summary>A 4xx is the token turned down: a verdict at once, however fresh the expiry.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void A4xxAnswer_MeansTheGrantIsDead(HttpStatusCode status)
    {
        var outcome = Classify(status, expiredFor: TimeSpan.FromMinutes(1));
        Assert.Equal(PatreonRefreshOutcome.Refused, outcome);
        Assert.True(PatreonGrantHealth.MarksGrantDead(outcome));
    }

    /// <summary>A fresh expiry under a 5xx is a bad afternoon, and nagging would be wrong.</summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void A5xxOverAFreshExpiry_SaysNothing(HttpStatusCode status)
        => Assert.Equal(PatreonRefreshOutcome.Unavailable,
            Classify(status, expiredFor: TimeSpan.FromHours(1)));

    /// <summary>
    /// THIS is the rung that reaches the ticket's user. CCP-Server origin/main maps every refresh
    /// failure - including the invalid_grant a revoked refresh token earns - to a 500, so the 4xx
    /// rung above can never fire in production. A healthy install refreshes on the first launch
    /// after expiry, so an expiry days old while the proxy keeps answering is a dead grant.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public void A5xxOverAStaleExpiry_MeansTheGrantIsDead(HttpStatusCode status)
    {
        var outcome = Classify(status, expiredFor: TimeSpan.FromDays(4));
        Assert.Equal(PatreonRefreshOutcome.Refused, outcome);
        Assert.True(PatreonGrantHealth.MarksGrantDead(outcome));
    }

    /// <summary>Exactly at the threshold is still a bad afternoon; a second past it is not.</summary>
    [Fact]
    public void TheStalenessBoundaryIsStrict()
    {
        Assert.Equal(PatreonRefreshOutcome.Unavailable,
            Classify(HttpStatusCode.InternalServerError, expiredFor: PatreonGrantHealth.StaleAfter));
        Assert.Equal(PatreonRefreshOutcome.Refused,
            Classify(HttpStatusCode.InternalServerError,
                expiredFor: PatreonGrantHealth.StaleAfter + TimeSpan.FromSeconds(1)));
    }

    /// <summary>An unknown expiry can never make a grant dead.</summary>
    [Fact]
    public void A5xxWithNoExpiryToJudge_SaysNothing()
        => Assert.Equal(PatreonRefreshOutcome.Unavailable,
            Classify(HttpStatusCode.InternalServerError, expiredFor: null));

    /// <summary>
    /// A timeout and a 429 are the service asking for less, not a word about the token, so they
    /// stay Unavailable at ANY age. Otherwise a rate-limited install would accuse its own grant.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData((HttpStatusCode)429)]
    public void TimeoutsAndRateLimits_NeverBecomeAVerdict(HttpStatusCode status)
        => Assert.Equal(PatreonRefreshOutcome.Unavailable,
            Classify(status, expiredFor: TimeSpan.FromDays(40)));

    /// <summary>Nothing answered, so there is nothing to read - at any age.</summary>
    [Fact]
    public void AThrownRequest_SaysNothingAboutTheGrant()
    {
        Assert.Equal(PatreonRefreshOutcome.Unavailable, Classify(null, threw: true));
        Assert.Equal(PatreonRefreshOutcome.Unavailable,
            Classify(null, threw: true, expiredFor: TimeSpan.FromDays(40)));
        Assert.Equal(PatreonRefreshOutcome.Unavailable,
            Classify(HttpStatusCode.OK, "invalid_grant", threw: true, expiredFor: TimeSpan.FromDays(40)));
    }

    /// <summary>The proxy can pass Patreon's refusal through with a 200 and an error field.</summary>
    [Theory]
    [InlineData("invalid_grant")]
    [InlineData("invalid_request")]
    public void AnOauthErrorBody_IsStillARefusal(string error)
        => Assert.Equal(PatreonRefreshOutcome.Refused,
            Classify(HttpStatusCode.OK, error, expiredFor: TimeSpan.FromMinutes(1)));

    [Fact]
    public void ACleanAnswer_IsARefresh()
    {
        var outcome = Classify(HttpStatusCode.OK, expiredFor: TimeSpan.FromDays(40));
        Assert.Equal(PatreonRefreshOutcome.Refreshed, outcome);
        Assert.False(PatreonGrantHealth.MarksGrantDead(outcome));
    }

    /// <summary>
    /// A Local-kind expiry off the JSON round-trip must be converted, not assumed UTC, or the
    /// threshold moves by the user's offset.
    /// </summary>
    [Fact]
    public void ALocalKindExpiryIsConvertedNotAssumed()
    {
        var justInsideUtc = Now - PatreonGrantHealth.StaleAfter + TimeSpan.FromMinutes(1);
        Assert.Equal(PatreonRefreshOutcome.Unavailable, PatreonGrantHealth.Classify(
            HttpStatusCode.InternalServerError, null, false,
            justInsideUtc.ToLocalTime(), Now));
    }

    /// <summary>
    /// The flag's transitions as the service applies them. The outage row in BOTH directions is
    /// the whole reason the outcome is three-valued: a bool either nags on every dropped
    /// connection or clears the flag on one.
    /// </summary>
    [Fact]
    public void AnOutageNeverMovesTheFlagEitherWay()
    {
        static bool Apply(bool dead, PatreonRefreshOutcome outcome)
        {
            if (outcome == PatreonRefreshOutcome.Refreshed) return false;
            if (PatreonGrantHealth.MarksGrantDead(outcome)) return true;
            return dead;
        }

        Assert.True(Apply(false, PatreonRefreshOutcome.Refused));
        Assert.False(Apply(true, PatreonRefreshOutcome.Refreshed));
        Assert.True(Apply(true, PatreonRefreshOutcome.Unavailable));
        Assert.False(Apply(false, PatreonRefreshOutcome.Unavailable));
    }

    /// <summary>
    /// The service applies that table and clears the flag on all four paths back to a working
    /// grant. Source read: reaching those branches needs the network.
    /// </summary>
    [Fact]
    public void ThePatreonService_KeepsTheFlag()
    {
        var source = ReadSource("Services", "Account", "PatreonService.cs");

        Assert.Contains("public bool GrantLooksDead { get; private set; }", source, StringComparison.Ordinal);
        Assert.Contains("PatreonGrantHealth.Classify", source, StringComparison.Ordinal);
        Assert.Contains("PatreonGrantHealth.MarksGrantDead", source, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(source, @"GrantLooksDead = false").Count);

        // The staleness rung is useless without the expiry, and both refresh call sites have the
        // stored tokens in hand: pass it, never null.
        Assert.Equal(2, Regex.Matches(source, @"RefreshTokensAsync\(tokens\.RefreshToken, tokens\.ExpiresAt\)").Count);
    }

    // ------------------------------------------------------------------ 3. the two 409s

    /// <summary>
    /// "Already linked, to yourself" - the normal answer to a reconnect, since the OAuth tokens are
    /// stored BEFORE the link call. It has to read as a success or the repair looks like a failure.
    /// </summary>
    [Theory]
    [InlineData("Patreon already linked to this account")]
    [InlineData("Discord already linked to this account")]
    [InlineData("patreon ALREADY LINKED TO THIS ACCOUNT")]
    public void SameAccountConflict_ReadsAsSuccess(string error)
        => Assert.True(ProviderLinkResponseRules.IsAlreadyLinkedToThisAccount(error));

    /// <summary>The other 409: this provider identity belongs to somebody else's record.</summary>
    [Theory]
    [InlineData("Patreon account already linked to a different user")]
    [InlineData("Discord account already linked to a different user")]
    [InlineData("This account is already linked to a different user")]
    public void DifferentUserConflict_StaysAnError(string error)
        => Assert.False(ProviderLinkResponseRules.IsAlreadyLinkedToThisAccount(error));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("HTTP 500")]
    [InlineData("account_merged")]
    [InlineData("Invalid access token")]
    public void EverythingElse_StaysAnError(string? error)
        => Assert.False(ProviderLinkResponseRules.IsAlreadyLinkedToThisAccount(error));

    // ------------------------------------------------------------------ 4. the wiring

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    /// <summary>The source of a method, from its signature to the start of the next doc comment.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " is gone");
        var end = source.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        if (end < 0) end = source.Length;
        return source.Substring(start, end - start);
    }

    /// <summary>
    /// The row must come from the rule and not from a second opinion - the bug was a hand-written
    /// "linked, therefore hide it" right here. <c>GrantLooksDead</c> is asserted too: without it
    /// the rule is handed a bare <c>IsAuthenticated</c>, which is true for the ticket's account and
    /// always will be, so the whole feature would be dead code for the people it was built for.
    /// </summary>
    [Fact]
    public void UpdateAccountLinkingUi_AsksTheRule()
    {
        var body = MethodBody(ReadSource("MainWindow", "MainWindow.Patreon.cs"),
            "private void UpdateAccountLinkingUI()");

        Assert.Contains("PatreonReconnectRule.Decide", body, StringComparison.Ordinal);
        Assert.Contains("GrantLooksDead", body, StringComparison.Ordinal);
        Assert.Contains("ShowsButton", body, StringComparison.Ordinal);
        Assert.Contains("btn_reconnect_patreon", body, StringComparison.Ordinal);
        Assert.Contains("TxtPatreonReconnectHint", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The gate's reconnect must land on Settings . Account. <c>ShowTab("settings")</c> is the
    /// DASHBOARD (the Settings door is keyed "appsettings" - see the NavDoorMap note in
    /// MainWindow.TabNavigation.cs), so the obvious spelling sends a locked-out patron to Home with
    /// the promised row nowhere on screen.
    /// </summary>
    [Fact]
    public void TheGateReconnect_OpensAccountSettings()
    {
        var body = MethodBody(ReadSource("MainWindow", "MainWindow.Patreon.cs"),
            "internal void StartPatreonReconnectFromGate()");

        Assert.Contains("ShowAccountSettings()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowTab(\"settings\")", body, StringComparison.Ordinal);
        // A second click on the lingering toast would re-run OAuth with the dead token.
        Assert.Contains("IsEnabled == false", body, StringComparison.Ordinal);
    }

    /// <summary>Or a patron with a dead grant keeps being told to upgrade a pledge they hold.</summary>
    [Fact]
    public void TierGateRefusal_AsksTheRule()
    {
        var source = ReadSource("Services", "TierGate.cs");
        Assert.Contains("PatreonReconnectRule.Decide", source, StringComparison.Ordinal);
        Assert.Contains("tiergate_denied_reconnect", source, StringComparison.Ordinal);
        Assert.Contains("StartPatreonReconnectFromGate", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Content assignment would replace the XAML's live <c>{loc:Str}</c> binding on the first
    /// paint for EVERY user, so a language switch would leave the button in the old tongue.
    /// </summary>
    [Fact]
    public void ThePatreonButtonLabelStaysBound()
    {
        var source = ReadSource("MainWindow", "MainWindow.Patreon.cs");
        Assert.Contains("BindingOperations.SetBinding", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BtnLinkPatreon.Content =", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The row is painted at window load, while the startup validate - half of what it decides - is
    /// still in flight. Without a repaint the offer would only ever appear one launch late.
    /// </summary>
    [Fact]
    public void TheRowIsRepaintedAfterTheStartupValidate()
    {
        var app = ReadSource("App.xaml.cs");
        var at = app.IndexOf("await Patreon.InitializeAsync();", StringComparison.Ordinal);
        Assert.True(at > 0, "the startup Patreon validate has moved");
        var after = app.Substring(at, Math.Min(900, app.Length - at));

        Assert.Contains("RefreshAccountLinkingRow", after, StringComparison.Ordinal);
        // Loaded is starved on this path and would silently never run.
        Assert.Contains("DispatcherPriority.Normal", after, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Loaded", after, StringComparison.Ordinal);
    }

    /// <summary>Every new key exists in all nine language files.</summary>
    [Fact]
    public void TheNewKeysAreInAllNineLanguages()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(9, files.Length);

        string[] keys =
        {
            "btn_reconnect_patreon",
            "label_patreon_reconnect_hint",
            "account_patreon_reconnected",
            "tiergate_denied_reconnect",
            "tiergate_reconnect_action"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var key in keys)
                Assert.True(text.Contains("\"" + key + "\"", StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} is missing {key}");
        }
    }
}
