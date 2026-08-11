using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// <see cref="CompanionPromptSettings.Clone"/> used to stop copying at CustomDomains, silently
/// dropping the five CCBill acknowledgement fields. Callers such as
/// PersonalityService.MigrateFromLegacy and CommunityPrompt clone settings and write the clone
/// back, so a user who had already accepted the explicit-content gate was re-prompted and the
/// recorded ack timestamp/locale (the audit trail) was erased. These tests pin the whole copy.
/// </summary>
public class CompanionPromptCloneTests
{
    private static CompanionPromptSettings BuildAcknowledged() => new()
    {
        ExplicitContentAcknowledged = true,
        ExplicitAcknowledgedVersion = CompanionPromptSettings.ExplicitAcknowledgementVersion,
        ExplicitAcknowledgedAt = "2026-08-05T12:34:56.7890000Z",
        ExplicitAcknowledgedLocale = "de-DE",
        PromptEditorDisclaimerAcknowledged = true
    };

    [Fact]
    public void Clone_PreservesAcknowledgementAuditTrail()
    {
        var clone = BuildAcknowledged().Clone();

        Assert.True(clone.ExplicitContentAcknowledged);
        Assert.Equal(CompanionPromptSettings.ExplicitAcknowledgementVersion, clone.ExplicitAcknowledgedVersion);
        Assert.Equal("2026-08-05T12:34:56.7890000Z", clone.ExplicitAcknowledgedAt);
        Assert.Equal("de-DE", clone.ExplicitAcknowledgedLocale);
        Assert.True(clone.PromptEditorDisclaimerAcknowledged);
    }

    [Fact]
    public void Clone_KeepsCopyingTheEarlierFields()
    {
        // Regression guard: the ack fields were appended after CustomDomains, so make sure
        // the pre-existing tail of the copy still works.
        var source = BuildAcknowledged();
        source.OutputRules = "SHORT. Max 15 words.";
        source.CustomDomains["example.com"] = "Browsing";

        var clone = source.Clone();

        Assert.Equal("SHORT. Max 15 words.", clone.OutputRules);
        Assert.Equal("Browsing", clone.CustomDomains["example.com"]);
        // Deep copy: mutating the clone's dictionary must not touch the source.
        clone.CustomDomains["other.com"] = "Gaming";
        Assert.False(source.CustomDomains.ContainsKey("other.com"));
    }
}
