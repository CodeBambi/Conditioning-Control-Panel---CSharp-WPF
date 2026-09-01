using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The consent gate (doc 02 §6.3) and the promises the dialog prints.
///
/// <para><b>Why a source scan.</b> The gate is only worth anything if it is the ONLY door: a second
/// place that writes <c>AwarenessModeEnabled</c> would switch her eyes on without ever showing the
/// explanation, and no unit test of the dialog itself would notice. So the shipped source is read
/// and the writers are counted — the same "find every call site, not just the one you changed"
/// posture the security review asked for.</para>
///
/// <para>The dialog itself cannot be constructed headlessly (it is a WPF <c>Window</c>), so what is
/// asserted here is its decision logic, its side effects and the copy it renders.</para>
/// </summary>
public class AwarenessConsentTests
{
    // ===================== the flag =====================

    [Fact]
    public void AFreshInstallHasNotSeenTheDialog()
    {
        var settings = new AppSettings();
        Assert.False(settings.AwarenessConsentShownV2);
        Assert.True(AwarenessConsentDialog.IsRequired(settings));
    }

    [Fact]
    public void AnUpgraderWhoAlreadyHadAwarenessOn_StillGetsTheDialogOnce()
    {
        // The old toggle set AwarenessConsentGiven silently, so that flag proves nothing about whether
        // anyone was ever told what the feature does. Only the v2 flag does.
        var settings = new AppSettings { AwarenessModeEnabled = true, AwarenessConsentGiven = true };
        Assert.True(AwarenessConsentDialog.IsRequired(settings));
    }

    [Fact]
    public void OnceAccepted_TheToggleIsOneClickForever()
    {
        var settings = new AppSettings { AwarenessConsentShownV2 = true };
        Assert.False(AwarenessConsentDialog.IsRequired(settings));
    }

    [Fact]
    public void NullSettingsRead_AsAsk()
        => Assert.True(AwarenessConsentDialog.IsRequired(null));

    [Fact]
    public void NullSettings_CanNeverBeConsentedFor()
        => Assert.False(AwarenessConsentDialog.EnsureConsent(null, null));

    [Fact]
    public void AlreadyConsented_ReturnsTrueWithoutOpeningAnything()
    {
        // If this ever tried to show a dialog it would throw on the headless test thread, which is a
        // perfectly good assertion that it does not.
        var settings = new AppSettings { AwarenessConsentShownV2 = true };
        Assert.True(AwarenessConsentDialog.EnsureConsent(null, settings));
    }

    [Fact]
    public void AcceptingSeedsTheDenyGroupsAndMigratesTheCooldown()
    {
        // The two side effects the accept path performs, asserted through the helpers it calls, so
        // that "the defaults exist before anything is observed" is a tested claim.
        var settings = new AppSettings { AwarenessReactionCooldownSeconds = 15 };

        AwarenessPrivacyRules.EnsureSeeded(settings);
        AwarenessIntensityMigration.EnsureMigrated(settings);

        Assert.True(settings.AwarenessDenySeeded);
        Assert.Contains(AwarenessPrivacyRules.GroupPasswordManagers, settings.AwarenessDenyList);
        Assert.Contains(AwarenessPrivacyRules.GroupBanking, settings.AwarenessDenyList);
        Assert.Contains(AwarenessPrivacyRules.GroupEmailTitles, settings.AwarenessDenyList);
        Assert.Equal(AwarenessIntensity.Unhinged, settings.AwarenessIntensity);
    }

    // ===================== one door =====================

    [Fact]
    public void AwarenessCanOnlyBeSwitchedOnFromTheGatedPath()
    {
        var writers = SourceWriters("AwarenessModeEnabled")
            .Concat(SourceWriters("AwarenessConsentGiven"))
            .Select(SourceRoots.Relative)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // AppSettings declares them; MainWindow.CompanionRoom.cs is the one place that flips them ON,
        // and it calls EnsureConsent first. MainWindow.Patreon.cs is the entitlement-lapse shutoff
        // (#267's EnforceEntitlementLapse, extended for awareness in #1047) and is only allowed here
        // because it can only ever write FALSE — asserted below, so it cannot quietly become a second
        // door. Anything else on this list IS a second door.
        //
        // Keyed on the path relative to the REPO, not on the bare filename. The walk spans every
        // product root now, so a filename is no longer a unique key: a CCP.Core/Models/AppSettings.cs
        // added during the Core split would inherit this exemption and open a second door in
        // silence. Whole-path keys also mean that moving a door to Core fails here loudly, which is
        // the right moment to re-review whether it is still the only way in.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ConditioningControlPanel/Models/AppSettings.cs",
            "ConditioningControlPanel/MainWindow/MainWindow.CompanionRoom.cs",
            "ConditioningControlPanel/MainWindow/MainWindow.Patreon.cs"
        };

        var strays = writers.Where(f => !allowed.Contains(f)).ToList();
        Assert.True(strays.Count == 0,
            "awareness is switched on outside the consent gate in: " + string.Join(", ", strays));
    }

    [Fact]
    public void TheLapseShutoffOnlyEverClosesHerEyes()
    {
        // The shutoff shares the allow list above with the consent gate, so its one privilege has
        // to be pinned: it may write the enable false, and it may never write it true.
        var source = SourceRoots.ReadProductFile("MainWindow", "MainWindow.Patreon.cs");

        // (char)10 is the line feed, spelled without an escape so the split survives however this
        // file's own line endings land.
        var writes = source.Split((char)10)
            .Select(l => l.Trim())
            .Where(l => l.Contains("AwarenessModeEnabled =", StringComparison.Ordinal)
                        && !l.Contains("AwarenessModeEnabled ==", StringComparison.Ordinal)
                        && !l.StartsWith("//", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(writes);
        Assert.All(writes, w => Assert.Contains("= false", w, StringComparison.Ordinal));
    }

    // ===================== the entitlement term (#1047) =====================

    [Fact]
    public void TheObserverPredicateAsksAboutEntitlementAndNotOnlySettings()
    {
        // Awareness is sold at tier 1, its page wears the premium veil, and until #1047 the engine's
        // own predicate read four settings flags and nothing else — so a lapsed account was still
        // observed, behind a padlock that covered the only toggle on that page. A source scan rather
        // than a call, because App.Patreon cannot be stood up headlessly and the regression this
        // guards is a term going MISSING from an expression.
        var source = SourceRoots.ReadProductFile("Services", "Awareness", "AwarenessObserver.cs");

        Assert.Contains("HasEntitlement && s.UseAwarenessV2", source, StringComparison.Ordinal);
        Assert.Contains("App.Patreon?.HasPremiumAccess == true", source, StringComparison.Ordinal);
        // The free day has to be OR'd in or the ? box would advertise a door it cannot open.
        Assert.Contains("IsFreeToday(\"awareness\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoEntitlementServiceMeansNoWatching()
    {
        // Fail closed: headless, App.Patreon and App.DailyFree are both null, and a missing gate must
        // read as a shut door rather than an open one.
        Assert.False(AwarenessObserver.HasEntitlement);
        Assert.False(AwarenessObserver.IsEnabled);
    }

    [Fact]
    public void BothAwarenessPollsReCheckEntitlementOnEveryTick()
    {
        // Start()-time checks are not enough: entitlement lapses mid-run (midnight rotation, an
        // expiring subscription, a logout) and both of these timers outlive the check that armed them.
        var observer = SourceRoots.ReadProductFile("Services", "Awareness", "AwarenessObserver.cs");
        var legacy = SourceRoots.ReadProductFile("Services", "UI", "WindowAwarenessService.cs");

        var observerTick = observer[observer.IndexOf("private void OnPollTick", StringComparison.Ordinal)..];
        Assert.Contains("if (!IsEnabled) return;", observerTick[..1400], StringComparison.Ordinal);

        var legacyTick = legacy[legacy.IndexOf("private void OnPollTick", StringComparison.Ordinal)..];
        Assert.Contains("AwarenessObserver.HasEntitlement", legacyTick[..1400], StringComparison.Ordinal);

        // …and it has to be ahead of the title read, or a lapsed account is observed and then discarded.
        var guard = legacyTick.IndexOf("AwarenessObserver.HasEntitlement", StringComparison.Ordinal);
        var read = legacyTick.IndexOf("GetActiveWindowTitle()", StringComparison.Ordinal);
        Assert.True(guard >= 0 && read > guard, "the entitlement guard must precede the window-title read");
    }

    [Fact]
    public void TheLapseShutoffStopsTheEngineAndNotJustTheFlag()
    {
        // Flipping the setting without stopping the service would leave the poll running until the
        // next relaunch — the half-fix that would make #1047 look repaired while the eyes stayed open.
        // Stop() on the legacy service is what chains through to the observer's Stop() and the ledger.
        var source = SourceRoots.ReadProductFile("MainWindow", "MainWindow.Patreon.cs");

        var write = source.IndexOf("settings.AwarenessModeEnabled = false;", StringComparison.Ordinal);
        Assert.True(write > 0, "the lapse shutoff no longer switches Awareness Mode off");

        var block = source.Substring(write, Math.Min(400, source.Length - write));
        Assert.Contains("App.WindowAwareness?.Stop()", block, StringComparison.Ordinal);
        Assert.Contains("Entitlement lapsed: Awareness Mode", block, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGatedPathActuallyCallsTheGate()
    {
        var source = SourceRoots.ReadProductFile("MainWindow", "MainWindow.CompanionRoom.cs");

        Assert.Contains("AwarenessConsentDialog.EnsureConsent", source, StringComparison.Ordinal);
        // …and a decline has to be able to stop it, which a void method could not express.
        Assert.Contains("internal bool SetAwarenessEnabled(bool enabled)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOldSilentAutoConsentIsGone()
    {
        var source = SourceRoots.ReadProductFile("MainWindow", "MainWindow.Patreon.cs");

        // The comment that documented the auto-consent must not outlive the behaviour: a stale comment
        // describing data handling the code no longer does was the single most repeated finding of the
        // 2026-08-06 reviews.
        Assert.DoesNotContain("keeps the auto-consent write", source, StringComparison.Ordinal);
    }

    // ===================== the copy =====================

    [Theory]
    [InlineData("awareness_consent_title")]
    [InlineData("awareness_consent_intro")]
    [InlineData("awareness_consent_watch_head")]
    [InlineData("awareness_consent_watch_body")]
    [InlineData("awareness_consent_leaves_head")]
    [InlineData("awareness_consent_leaves_body")]
    [InlineData("awareness_consent_leaves_body_legacy")]
    [InlineData("awareness_consent_never_head")]
    [InlineData("awareness_consent_never_body")]
    [InlineData("awareness_consent_never_note")]
    [InlineData("awareness_consent_control_head")]
    [InlineData("awareness_consent_control_body")]
    [InlineData("awareness_consent_retention_fmt")]
    [InlineData("awareness_consent_footer")]
    [InlineData("awareness_consent_accept")]
    [InlineData("awareness_consent_decline")]
    public void EveryConsentLineReachedAllNineLanguages(string key)
    {
        foreach (var language in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(language);
            Assert.True(file.TryGetValue(key, out var value), $"{language}.json is missing {key}");
            Assert.False(string.IsNullOrWhiteSpace(value), $"{language}.json '{key}' is empty");
            Assert.False(value!.Contains('\n') || value.Contains('\r'),
                $"{language}.json '{key}' carries a literal line break — escape it as \\n");
        }
    }

    [Fact]
    public void TheRetentionLineTakesItsNumberFromTheSetting()
    {
        // Writing "30 days" into the sentence is how copy goes stale the moment someone picks 7.
        foreach (var language in CompanionLocMasters.Languages)
        {
            var value = CompanionLocMasters.For(language)["awareness_consent_retention_fmt"];
            Assert.Contains("{0}", value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheConsentCopyDoesNotPromiseWhatThePrivacyLayerDoesNotDo()
    {
        var english = CompanionLocMasters.English;
        var never = english["awareness_consent_never_body"];

        // The dialog says titles stay here "unless you name an app yourself" rather than "never",
        // because the allow list exists — and the allow list is empty on a fresh install.
        Assert.Contains("unless you name an app", never, StringComparison.OrdinalIgnoreCase);
        Assert.False(AwarenessPrivacyRules.IsTitleAllowed("chrome", "Chrome", null, new AppSettings()));

        // The incognito claim is absolute, and so is the code.
        Assert.True(AwarenessPrivacyRules.LooksIncognito("x — Private Browsing"));

        // The adult-cluster claim.
        Assert.Contains("adult", english["awareness_consent_leaves_body"], StringComparison.OrdinalIgnoreCase);

        // And the keystroke/screenshot claim is scoped by the note beside it rather than pretending
        // the Triggers engine does not exist.
        Assert.Contains("Triggers", english["awareness_consent_never_note"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheWorkshopPrivacyNoticeFollowsThePipelineThatIsActuallyRunning()
    {
        // The shipped notice ends "No data is stored permanently." That is true of the legacy
        // pipeline and false of v2, which keeps local counters for the retention period — so v2 has
        // its own wording and the cell picks between them. A notice that describes the other
        // pipeline's data handling is the exact failure shape the 2026-08-06 reviews kept finding.
        var english = CompanionLocMasters.English;

        var legacy = english["label_this_feature_reads_the_name_of_the_active_win"];
        Assert.Contains("No data is stored permanently", legacy, StringComparison.OrdinalIgnoreCase);

        var v2 = english["label_awareness_privacy_notice_v2"];
        Assert.DoesNotContain("No data is stored permanently", v2, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retention", v2, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("incognito", v2, StringComparison.OrdinalIgnoreCase);

        foreach (var language in CompanionLocMasters.Languages)
        {
            Assert.True(CompanionLocMasters.For(language).ContainsKey("label_awareness_privacy_notice_v2"),
                $"{language}.json is missing the v2 privacy notice");
        }
    }

    // ===================== helpers =====================

    /// <summary>Files that ASSIGN <paramref name="property"/> (i.e. "Property =", not "Property ==").</summary>
    private static IEnumerable<string> SourceWriters(string property)
    {
        // Every product root, not just the WPF head: a consent gate that moves to CCP.Core has to
        // stay under this guard. Build output and nested worktrees are excluded by the helper.
        foreach (var file in SourceRoots.EnumerateProductSources("*.cs"))
        {
            foreach (var line in File.ReadLines(file))
            {
                var i = line.IndexOf(property + " =", StringComparison.Ordinal);
                if (i < 0) continue;
                if (line.Contains(property + " ==", StringComparison.Ordinal)) continue;
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                yield return file;
                break;
            }
        }
    }
}
