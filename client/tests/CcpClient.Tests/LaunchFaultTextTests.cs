using System.Reflection;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// What a user reads when a launch THREW past the gate. Pure text composition, so it is
/// provable here rather than only through a page.
///
/// <para>The trap this packet was authored against is a catch that renders something and drops the
/// detail — an invisible failure replaced by a silent one. These facts pin the opposite: the
/// exception TYPE survives every path, the message survives except where it is explicitly clamped,
/// and none of the composed text may wear a refusal's words.</para>
/// </summary>
public class LaunchFaultTextTests
{
    [Fact]
    public void TheHeadlinesAreWpfsOwn_MinusTheColonComposeAddsBack()
    {
        // MainWindow/MainWindow.Lab.cs:269 and :336 both concatenate
        // "Couldn't start Down the Rabbit Hole:\n\n" + ex.Message; :164 is the intake's.
        Assert.Equal("Couldn't start Down the Rabbit Hole", LaunchFaultText.DtrhHeadline);
        Assert.Equal("Couldn't start Graded Intake", LaunchFaultText.IntakeHeadline);

        var composed = LaunchFaultText.Compose(LaunchFaultText.DtrhHeadline, new InvalidOperationException("boom"));
        Assert.StartsWith("Couldn't start Down the Rabbit Hole:\n", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void NoStringAPlateCanRENDER_ContainsABlankLine_AndTheSetIsDERIVED_NeverHandListed()
    {
        // A MEASURED AVALONIA 12.1.1 LANDMINE, and the worst failure shape there is: a wrapped
        // TextBlock inside the lock/fault plates whose text contains an EMPTY LINE wedges the
        // layout pass, so the press that raised the band never returns and the suite HANGS
        // instead of failing. Reproduced independently from a throwaway harness with NO product
        // reference: a single-newline body lays out; "A:\n\nB" and a realistic fault body with
        // one blank line both wedge. With and without LineHeight, and identically in the EXISTING
        // refusal plate — a property of the surface, not of the fault feature. Divergence §13 D43.
        //
        // WPF's own failure text uses a blank line ("...:\n\n" + ex.Message,
        // MainWindow/MainWindow.Lab.cs:164,269), so "restore WPF's spacing" is a natural later
        // edit. This is what that edit runs into, in the UNIT suite, loudly.
        //
        // THE SET IS DERIVED, AND THAT IS THE LOAD-BEARING PART. The first version of this guard
        // hand-enumerated eight strings and omitted IntakePassGate.SpentMessageFormat — a string
        // the intake plate renders on six days in seven — while claiming to cover every plate
        // string. A guard that claims total coverage and has a hole is worse than one that claims
        // a sample, because the claim is what stops the next person checking. It is the same
        // defect as a capture harness with a hard-coded list of doors that goes blind the moment
        // a wave adds one. So nothing below is typed out by hand:
        //   (1) every public string member of the three classes that own plate BODY copy, by
        //       reflection, so a constant added next year is swept without anyone remembering to;
        //   (2) every message the two gates can actually PRODUCE, by driving Decide over every
        //       reason code, every tier and every (state, reason) pair — which catches copy that
        //       is composed at runtime and never exists as a constant at all;
        //   (3) the fault bodies the composer builds, including the clamped one.
        var swept = new List<(string Name, string Text)>();

        // (1) Reflection over the three body-copy owners. Consts are read from metadata, statics
        // and static get-only properties by value.
        Type[] plateCopyOwners = [typeof(DtrhGate), typeof(IntakePassGate), typeof(LaunchFaultText)];
        var reflectedNames = new List<string>();
        foreach (var owner in plateCopyOwners)
        {
            foreach (var field in owner.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string))
                {
                    continue;
                }

                var value = field.IsLiteral
                    ? (string?)field.GetRawConstantValue()
                    : (string?)field.GetValue(null);
                reflectedNames.Add(field.Name);
                swept.Add(($"{owner.Name}.{field.Name}", value ?? string.Empty));
            }

            foreach (var property in owner.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType != typeof(string) || property.GetMethod is null ||
                    property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                reflectedNames.Add(property.Name);
                swept.Add((
                    $"{owner.Name}.{property.Name}", (string?)property.GetValue(null) ?? string.Empty));
            }
        }

        // The derivation must be PROVED to see what the hand list missed, or an over-narrow filter
        // would silently sweep nothing and this guard would be vacuous in a new way.
        Assert.Contains(nameof(IntakePassGate.SpentMessageFormat), reflectedNames);
        Assert.Contains(nameof(IntakePassGate.SpentMessageOneDay), reflectedNames);
        Assert.Contains(nameof(DtrhGate.TierRefusalMessage), reflectedNames);     // a static property
        Assert.Contains(nameof(LaunchFaultText.Separator), reflectedNames);
        Assert.True(reflectedNames.Count >= 14,
            $"the reflection sweep found only {reflectedNames.Count} string members across "
            + $"{plateCopyOwners.Length} classes; it has gone blind rather than found nothing to do");

        // (2a) Every reason code the entitlement vocabulary declares, plus one this build has no
        // wording for — the DTRH plate renders whatever Decide returns for each.
        var reasonCodes = typeof(EntitlementReasonCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Append("a-code-this-build-has-never-heard-of")
            .ToArray();
        Assert.True(reasonCodes.Length >= 11,
            $"only {reasonCodes.Length} reason codes were discovered — the sweep has gone blind");

        foreach (var code in reasonCodes)
        {
            swept.Add(($"DtrhGate.UnverifiedMessage({code})", DtrhGate.UnverifiedMessage(code)));
            swept.Add(($"DtrhGate.Explain({code})", DtrhGate.Explain(code)));
            RecordDecision(swept, $"DtrhGate.Decide(Unavailable({code}))",
                DtrhGate.Decide(new EntitlementOutcome.Unavailable(new EntitlementReason(code, "detail"))));
        }

        RecordDecision(swept, "DtrhGate.Decide(NotEntitled)",
            DtrhGate.Decide(new EntitlementOutcome.NotEntitled("no pledge")));
        foreach (var tier in Enum.GetValues<EntitlementTier>())
        {
            RecordDecision(swept, $"DtrhGate.Decide(Entitled({tier}))",
                DtrhGate.Decide(new EntitlementOutcome.Entitled(tier, "detail")));
        }

        // (2b) Every (state, reason) pair the intake gate can be handed, across the day counts
        // that select between its two spent strings. SpentMessageFormat is rendered HERE, which is
        // exactly the string the hand list omitted.
        foreach (var state in Enum.GetValues<IntakePassService.IntakePassState>())
        {
            foreach (var reason in Enum.GetValues<IntakePassService.IntakePassReason>())
            {
                foreach (var days in new[] { 0, 1, 2, 7, 365 })
                {
                    RecordDecision(swept, $"IntakePassGate.Decide({state},{reason},{days})",
                        IntakePassGate.Decide(state, reason, days));
                }
            }
        }

        // (3) The composed fault bodies: ordinary, clamped and empty-message.
        foreach (var headline in new[] { LaunchFaultText.DtrhHeadline, LaunchFaultText.IntakeHeadline })
        {
            swept.Add((
                $"fault body ({headline})",
                LaunchFaultText.Compose(headline, new InvalidOperationException("a real fault"))));
            swept.Add((
                $"clamped fault body ({headline})",
                LaunchFaultText.Compose(headline, new InvalidOperationException(new string('z', 900)))));
            swept.Add((
                $"empty-message fault body ({headline})",
                LaunchFaultText.Compose(headline, new EmptyMessageException())));
        }

        // The framing-(c) pin: an empty set would make the loop below check nothing.
        Assert.NotEmpty(swept);
        Assert.True(swept.Count >= 100, $"only {swept.Count} strings were swept; the derivation has narrowed");

        foreach (var (name, text) in swept)
        {
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);

            Assert.False(
                normalized.Contains("\n\n", StringComparison.Ordinal),
                $"{name} contains a BLANK LINE. That wedges the Avalonia 12.1.1 layout pass on the lock/fault "
                + "plates (measured at wpf-surface-reachability.md §13 D43): the press that raises the "
                + "band never returns, so the app and this suite HANG rather than fail. Use a single newline");

            // A leading or trailing line break is the same empty line at an edge. Applied only to
            // strings that carry actual words, which excludes pure joiners like
            // LaunchFaultText.Separator without naming one — the exclusion is derived too.
            if (normalized.Trim().Length == 0)
            {
                continue;
            }

            Assert.False(
                normalized.StartsWith('\n') || normalized.EndsWith('\n'),
                $"{name} begins or ends with a line break, which renders as a blank line at the edge of the "
                + "plate — the same §13 D43 shape as an interior one");
        }

        // The separator is asserted by value, so the constant cannot drift back to WPF's blank
        // line while everything above still reads green.
        Assert.Equal("\n", LaunchFaultText.Separator);
    }

    /// <summary>Adds whatever user-facing message a decision carries, so the sweep covers copy the
    /// gates compose at runtime rather than only copy that exists as a constant.</summary>
    private static void RecordDecision(List<(string Name, string Text)> into, string name, object decision)
    {
        switch (decision)
        {
            case DtrhGateDecision.RefusedNotEntitled refused:
                into.Add((name, refused.Message));
                break;
            case DtrhGateDecision.RefusedUnverified unverified:
                into.Add((name, unverified.Message));
                break;
            case IntakePassDecision.RefusedSpent spent:
                into.Add((name, spent.Message));
                break;
            case IntakePassDecision.RefusedNeedsAccount needsAccount:
                into.Add((name, needsAccount.Message));
                break;
            case IntakePassDecision.RefusedUndeterminable undeterminable:
                into.Add((name, undeterminable.Message));
                break;
            default:
                // Proceed carries no user-facing copy; nothing to sweep.
                break;
        }
    }

    [Fact]
    public void TheDetailKeepsTheExceptionType_AndTheMessage()
    {
        // WPF shows ex.Message (Lab.cs:269). The port shows the type as well, because the type is
        // what survives a paraphrase in a bug report — and because the packet's named trap is a
        // catch that keeps neither.
        var detail = LaunchFaultText.Detail(new TimeoutException("the descent never answered"));

        Assert.Equal("TimeoutException: the descent never answered", detail);
        Assert.Contains(nameof(TimeoutException), detail, StringComparison.Ordinal);
        Assert.Contains("the descent never answered", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyMessage_StillNamesTheType_AndNeverEmitsADanglingColon()
    {
        // Plenty of framework exceptions carry only a type by the time they reach a UI.
        var detail = LaunchFaultText.Detail(new EmptyMessageException());

        Assert.Equal(nameof(EmptyMessageException), detail);
        Assert.DoesNotContain(":", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewlineRiddenMessage_IsFlattened_SoTheNotADecisionLineCannotFallBelowTheFold()
    {
        // The plate is a fixed panel inside a card, not a MessageBox that grows to fit. A message
        // with its own line breaks would push the "this is a fault, not a decision" sentence out
        // of view, which is exactly how the user would end up reading a fault as a refusal again.
        var detail = LaunchFaultText.Detail(
            new InvalidOperationException("first line\r\nsecond line\nthird line"));

        Assert.Equal("InvalidOperationException: first line second line third line", detail);
        Assert.DoesNotContain('\n', detail);
        Assert.DoesNotContain('\r', detail);
    }

    [Fact]
    public void AnEnormousMessage_IsClamped_AndSaysSo()
    {
        var detail = LaunchFaultText.Detail(new InvalidOperationException(new string('x', 5_000)));

        Assert.EndsWith(LaunchFaultText.ClampMarker, detail, StringComparison.Ordinal);
        Assert.Equal(LaunchFaultText.MaxDetailLength + LaunchFaultText.ClampMarker.Length, detail.Length);
        // The type is at the FRONT, so a clamp can never be what removes it.
        Assert.StartsWith("InvalidOperationException: ", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageExactlyAtTheLimit_IsNotClamped()
    {
        var prefix = nameof(InvalidOperationException) + ": ";
        var message = new string('y', LaunchFaultText.MaxDetailLength - prefix.Length);

        var detail = LaunchFaultText.Detail(new InvalidOperationException(message));

        Assert.Equal(LaunchFaultText.MaxDetailLength, detail.Length);
        Assert.DoesNotContain(LaunchFaultText.ClampMarker, detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheComposedBody_SaysAFailureHappened_AndThatNothingWasRefused()
    {
        var body = LaunchFaultText.Compose(LaunchFaultText.IntakeHeadline, new IOException("disk went away"));

        Assert.Contains("Couldn't start Graded Intake", body, StringComparison.Ordinal);
        Assert.Contains("IOException: disk went away", body, StringComparison.Ordinal);
        Assert.Contains(LaunchFaultText.NotADecisionLine, body, StringComparison.Ordinal);
        Assert.Contains("not a decision about your account", body, StringComparison.Ordinal);
        // "The button still works" is the port's replacement for dismissing WPF's dialog: the card
        // is live either way, and saying so is what stops the band reading as a dead end.
        Assert.Contains("try again", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NoComposedFailure_EverWearsARefusalsWORDS()
    {
        // THE SECOND TRAP, mechanically. The refusal surfaces on these same two cards already mean
        // "we could not determine your entitlement" and "you have had your run this week". A
        // failure that borrowed any of those sentences would teach a user that a broken app and an
        // unknown subscription are one event — so no refusal string may appear in a fault body,
        // whatever a later edit does to either side.
        string[] refusalWords =
        [
            DtrhGate.TierRefusalMessage,
            DtrhGate.CouldNotVerifyHeader,
            DtrhGate.CouldNotVerifyFooter,
            IntakePassGate.NeedsAccountMessage,
            IntakePassGate.SpentMessageOneDay,
            IntakePassGate.UndeterminableMessage,
            "Tier 2 perk",
            "upgrade your pledge",
            "You've taken your free Graded Intake",
        ];

        // The framing-(c) pin: every assertion below lives inside a loop, so an empty
        // vocabulary would make this fact pass while checking nothing.
        Assert.NotEmpty(refusalWords);

        foreach (var headline in new[] { LaunchFaultText.DtrhHeadline, LaunchFaultText.IntakeHeadline })
        {
            var body = LaunchFaultText.Compose(headline, new InvalidOperationException("a real fault"));
            foreach (var refusal in refusalWords)
            {
                Assert.DoesNotContain(refusal, body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ARefusalsWordsAreNotTheFailuresWords_InTheOtherDirectionToo()
    {
        // And the headlines must not leak into the refusal copy either — the two vocabularies are
        // checked from both ends so one file's edit cannot quietly merge them.
        string[] refusals =
        [
            DtrhGate.TierRefusalMessage,
            DtrhGate.UnverifiedMessage(EntitlementReasonCodes.TierAuthorityAbsent),
            IntakePassGate.UndeterminableMessage,
            IntakePassGate.NeedsAccountMessage,
            IntakePassGate.SpentMessageOneDay,
        ];

        // The framing-(c) pin: the assertions below are all inside the loop.
        Assert.NotEmpty(refusals);

        foreach (var refusal in refusals)
        {
            Assert.DoesNotContain(LaunchFaultText.DtrhHeadline, refusal, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(LaunchFaultText.IntakeHeadline, refusal, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(LaunchFaultText.NotADecisionLine, refusal, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ComposeRefusesNothing_RatherThanRenderingAnEmptyFailure()
    {
        // A blank headline would produce a plate saying ":\n\n…" — visible, and meaningless. The
        // surface may not be raised with nothing to say.
        Assert.Throws<ArgumentNullException>(() => LaunchFaultText.Detail(null!));
        Assert.Throws<ArgumentNullException>(
            () => LaunchFaultText.Compose(LaunchFaultText.DtrhHeadline, null!));
        Assert.Throws<ArgumentException>(
            () => LaunchFaultText.Compose("   ", new InvalidOperationException("x")));
    }

    /// <summary>An exception whose message really is empty — <c>Exception</c> itself synthesises
    /// one, so the case needs a type that does not.</summary>
    private sealed class EmptyMessageException : Exception
    {
        public override string Message => string.Empty;
    }
}
