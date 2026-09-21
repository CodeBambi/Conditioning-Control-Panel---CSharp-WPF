using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Patreon row in Settings &gt; Account, and the 409 that a reconnect always earns.
///
/// <para>Ticket 1551244367040221235 (Sep 2026): a tier 2 patron lost premium while the server
/// record said patron on every sync. Her Patreon OAuth grant had died on her PC, so nothing
/// re-stamped the 14-day premium window, and the old rule HID the Patreon button exactly because
/// the account was already linked server-side. Every surface said "connected" and there was no way
/// back short of signing out.</para>
///
/// <para>Three things are pinned here, because all three compile either way:</para>
/// <list type="number">
/// <item>the row's full state table, including the two people who must never be nagged - a
/// whitelisted account (entitled without any Patreon grant) and a patron whose premium is still
/// on (SubscribeStar, or the grace window not yet lapsed);</item>
/// <item>the two 409s <c>/v2/auth/link</c> answers with. They share most of their words, and
/// mistaking the different-user conflict for the harmless one would silently tell somebody they
/// had linked an account that belongs to a stranger;</item>
/// <item>that the code-behind actually consults the rule. Source-text read: MainWindow cannot be
/// instantiated in a unit test.</item>
/// </list>
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

    /// <summary>Signed in with Discord only, no Patreon anywhere: the original offer.</summary>
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

    /// <summary>
    /// The grant works. Whatever the server record says, there is nothing here to fix and the
    /// button stays gone - which is also the old behaviour for the ordinary linked patron.
    /// </summary>
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

    /// <summary>
    /// The ticket's account, exactly: linked server-side, no token on this PC, premium already off.
    /// Prominent, because the loss is being felt right now.
    /// </summary>
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
    /// Same shape, but the grace window has not lapsed yet (or SubscribeStar is carrying them).
    /// The button is there - it is the only way to stop the clock - but it does not shout, and it
    /// must not claim anything has expired.
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
    /// A whitelisted account is entitled with no Patreon grant at all, so a missing token costs it
    /// nothing. Reconnecting would be a chore with no payoff: no button, in either premium state.
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

    /// <summary>
    /// A whitelisted account that never linked Patreon still gets the plain Link offer - the
    /// whitelist rule is scoped to the reconnect branch, not to the whole row.
    /// </summary>
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

    // ------------------------------------------------------------------ 2. the two 409s

    /// <summary>
    /// The server's exact sentence for "you are already linked, to yourself". This is the normal
    /// answer to a reconnect - the OAuth tokens are stored BEFORE the link call - so it has to read
    /// as a success or the repair looks like a failure.
    /// </summary>
    [Theory]
    [InlineData("Patreon already linked to this account")]
    [InlineData("Discord already linked to this account")]
    [InlineData("patreon ALREADY LINKED TO THIS ACCOUNT")]
    public void SameAccountConflict_ReadsAsSuccess(string error)
        => Assert.True(ProviderLinkResponseRules.IsAlreadyLinkedToThisAccount(error));

    /// <summary>
    /// The other 409, and the one that must never be swallowed: this provider identity belongs to
    /// somebody else's record.
    /// </summary>
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

    // ------------------------------------------------------------------ 3. the wiring

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

    /// <summary>
    /// The account row's visibility must come from the rule and not from a second opinion. The bug
    /// was precisely a hand-written "linked, therefore hide it" in this method.
    /// </summary>
    [Fact]
    public void UpdateAccountLinkingUi_AsksTheRule()
    {
        var source = ReadSource("MainWindow", "MainWindow.Patreon.cs");
        var start = source.IndexOf("private void UpdateAccountLinkingUI()", StringComparison.Ordinal);
        Assert.True(start > 0, "UpdateAccountLinkingUI is gone");
        // Far enough to cover the method, short enough not to reach the next one.
        var body = source.Substring(start, Math.Min(2400, source.Length - start));

        Assert.Contains("PatreonReconnectRule.Decide", body, StringComparison.Ordinal);
        Assert.Contains("ShowsButton", body, StringComparison.Ordinal);
        Assert.Contains("btn_reconnect_patreon", body, StringComparison.Ordinal);
        Assert.Contains("TxtPatreonReconnectHint", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hint line has to exist in the section's XAML, collapsed, or the code-behind above is
    /// setting a property on nothing.
    /// </summary>
    [Fact]
    public void TheHintLineIsInTheXamlAndStartsCollapsed()
    {
        var xaml = ReadSource("Views", "Controls", "AppSettings", "AccountSettingsSection.xaml");
        var at = xaml.IndexOf("x:Name=\"TxtPatreonReconnectHint\"", StringComparison.Ordinal);
        Assert.True(at > 0, "the linking section has no reconnect hint line");
        var element = xaml.Substring(at, Math.Min(400, xaml.Length - at));
        Assert.Contains("label_patreon_reconnect_hint", element, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", element, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every new key exists in all nine language files. English text in a non-English file is
    /// allowed; a missing key is not.
    /// </summary>
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
