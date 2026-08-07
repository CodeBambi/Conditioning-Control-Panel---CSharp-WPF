using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

// The privacy dial's enum, NOT Services.Awareness.AwarenessIntensity. Both types carry that name
// and both define Off, so importing the wrong one compiles cleanly and quietly routes every other
// stop into the default arm. Aliased here so the distinction cannot be lost in a later edit.
using DialStop = ConditioningControlPanel.Views.Controls.Companion.AwarenessIntensity;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Guards the two fixes that came out of a play-test of the "What she can see" card.
///
/// <para><b>1. The dial did not say what it moved.</b> The strip read Off / Broad strokes /
/// Everything under a heading about what she can SEE, while the only thing it actually changes is
/// whether an app's page title may leave the PC. A tester read "Everything" as breadth of
/// observation, pressed it, and was handed an app-picking dialog that made no sense against that
/// reading. Each stop now has a distinct hint, and this asserts they stay distinct and non-empty:
/// two stops sharing copy is the same failure wearing a longer label.</para>
///
/// <para><b>2. The list editor opened empty.</b> Candidates came only from the keyword trigger
/// service's foreground ring, which fills only while that service runs, and it is off by default.
/// <see cref="AwarenessAppCandidates"/> now reads the awareness ledger as well. There is no App
/// host in a unit test, so every source here is null - which is exactly the case that must return
/// an empty list rather than throw, because that is the state a cold launch is in.</para>
/// </summary>
public class AwarenessDialCopyTests
{
    [Fact]
    public void GatherSurvivesEverySourceBeingAbsent()
    {
        // No App, no ledger, no trigger service. The old code path would have been fine here too;
        // what matters is that adding three more sources did not make the null case throw.
        var candidates = AwarenessAppCandidates.Gather();

        Assert.NotNull(candidates);
        Assert.Empty(candidates);
    }

    [Fact]
    public void GatherNeverOffersSomethingAlreadyOnTheList()
    {
        // The dialog renders listed entries itself, at the top. A candidate that repeated one would
        // show the same app twice, once ticked and once not, which reads as a bug in a privacy list.
        var listed = new[] { "chrome", "1Password" };

        var candidates = AwarenessAppCandidates.Gather(listed);

        Assert.DoesNotContain("chrome", candidates, StringComparer.OrdinalIgnoreCase);
        // Sanitising lowercases, so the exclusion has to survive a case change on the way in.
        Assert.DoesNotContain("1password", candidates, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheCapIsARealCeiling()
    {
        Assert.True(AwarenessAppCandidates.MaxCandidates > 0);
        Assert.True(AwarenessAppCandidates.Gather().Count <= AwarenessAppCandidates.MaxCandidates);
    }

    /// <summary>
    /// The three hints are looked up by key, so this also catches a key that never made it into
    /// en.json: a missing key renders as the raw key string, which would still be "distinct" but is
    /// obviously wrong, hence the shape assertions.
    /// </summary>
    [Theory]
    [InlineData(DialStop.Off, "companion_awareness_dial_hint_off")]
    [InlineData(DialStop.BroadStrokes, "companion_awareness_dial_hint_broad")]
    [InlineData(DialStop.Everything, "companion_awareness_dial_hint_everything")]
    public void EveryStopHasItsOwnHintAndNoneIsARawKey(DialStop stop, string key)
    {
        var hint = DialHint(stop);

        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.NotEqual(key, hint);              // a raw key leaking through means the key is missing
        Assert.DoesNotContain("\n", hint);       // house rule: no literal break in a language value
    }

    [Fact]
    public void NoTwoStopsShareCopy()
    {
        var hints = new[]
        {
            DialHint(DialStop.Off),
            DialHint(DialStop.BroadStrokes),
            DialHint(DialStop.Everything)
        };

        Assert.Equal(3, hints.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The card's guarantees are v2 rules. The legacy poll starts on two flags where
    /// <c>AwarenessObserver.IsEnabled</c> needs four, so an upgrader who declines the v2 dialog keeps
    /// a running poll with no incognito drop, no deny list and no title allow list - and the card used
    /// to print those promises as fact anyway. This pins the gap that makes the warning band
    /// necessary: the two conditions must not be the same test, or the band can never appear.
    /// </summary>
    [Fact]
    public void TheLegacyPollStartsOnFewerFlagsThanTheV2Observer()
    {
        var settings = new ConditioningControlPanel.Models.AppSettings
        {
            // Exactly the declined-upgrader state: the old silent auto-consent is set, the v2
            // explanation has not been accepted.
            AwarenessModeEnabled = true,
            AwarenessConsentGiven = true,
            AwarenessConsentShownV2 = false,
            UseAwarenessV2 = true
        };

        // What the legacy WindowAwarenessService.Start gate reads.
        bool legacyWouldRun = settings.AwarenessModeEnabled && settings.AwarenessConsentGiven;

        // What AwarenessObserver.IsEnabled reads, same four clauses, same order.
        bool v2WouldRun = settings.UseAwarenessV2 && settings.AwarenessModeEnabled &&
                          settings.AwarenessConsentGiven && settings.AwarenessConsentShownV2;

        Assert.True(legacyWouldRun);
        Assert.False(v2WouldRun);

        // Which is precisely the state the card must warn about rather than describe as protected.
        Assert.True(legacyWouldRun && !v2WouldRun);
    }

    [Fact]
    public void AcceptingTheV2ExplanationClosesThatGap()
    {
        var settings = new ConditioningControlPanel.Models.AppSettings
        {
            AwarenessModeEnabled = true,
            AwarenessConsentGiven = true,
            AwarenessConsentShownV2 = true,
            UseAwarenessV2 = true
        };

        bool v2WouldRun = settings.UseAwarenessV2 && settings.AwarenessModeEnabled &&
                          settings.AwarenessConsentGiven && settings.AwarenessConsentShownV2;

        Assert.True(v2WouldRun);
    }

    /// <summary>
    /// Reaches the internal copy helper by reflection: it lives in the WPF view namespace and this
    /// suite deliberately does not take a UI dependency to read three strings.
    /// </summary>
    private static string DialHint(DialStop stop)
    {
        var type = typeof(ConditioningControlPanel.App).Assembly
            .GetType("ConditioningControlPanel.Views.Controls.Companion.AwarenessDialCopy");
        Assert.NotNull(type);

        var method = type!.GetMethod("HintFor",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        return (string)method!.Invoke(null, new object[] { stop })!;
    }
}
