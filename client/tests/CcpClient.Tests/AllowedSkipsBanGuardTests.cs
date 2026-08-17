using System.Reflection;
using System.Text.Json;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-090: the two <c>allowedSkips</c> PERMANENT BANS, made mechanical.
///
/// <para>WHAT THIS BINDS. <c>client/tests/floor/floor.json</c> is the port's one mechanical
/// pre-land gate and its integrity rests entirely on <c>allowedSkips</c> being honest. Its
/// <c>admissionRule</c> names two tests that may NEVER be listed there, because a skip of
/// either hides the exact defect the pin exists to catch: the SP-057 pin (its skip means
/// someone exported CCP_DATA_ROOT process-wide — the vacuous 896/1 green SP-062 closed) and
/// the named privacy flake (route classes only, never a filename or query). Until this file
/// both bans were TEXT. The only other place they appear is
/// <c>check-floor.mjs:236-238</c>, and that is an error-MESSAGE string, not a check — it does
/// not even contain either test name, naming them only by description ("the SP-057 pin", "the
/// named privacy flake"), so a reader hitting that failure cannot check the list without going
/// back to the JSON.</para>
///
/// <para>WHY THE BAN LIST IS HARD-CODED HERE AND NOT READ FROM THE FILE. The obvious guard
/// reads the banned names out of <c>admissionRule</c> and checks them against
/// <c>allowedSkips</c>. That guard is defeated by ONE edit: delete a name from
/// <c>admissionRule</c>, add it to <c>allowedSkips</c>, and the guard passes with nothing left
/// to compare. A guard whose own rule lives in the file it guards is not machinery, it is a
/// longer piece of prose. So the two names are <c>const</c> literals here, and
/// <c>admissionRule</c> is parsed only to ask whether it still CONTAINS each of them. Drift
/// therefore reds in both directions, and the redundancy is the mechanism rather than
/// duplication to be factored away: each copy of the two names is held by a DIFFERENT owner
/// (the JSON, this file's constants, this file's seeded fixtures, and the suite itself through
/// <see cref="BothBannedNames_StillResolveToARealFactInThisAssembly"/>). Factoring any two
/// together restores the single point of edit this guard exists to remove.</para>
///
/// <para>FAIL CLOSED, AT THE ASSERTION AND NOT ONLY IN THE FUNCTION. Every shape
/// <see cref="PinViolations"/> cannot read becomes a <see cref="ViolationKind.Structural"/>
/// violation through the ONE <see cref="Structural"/> constructor, and BOTH live facts admit
/// that kind (<see cref="MembershipFactSees"/> and <see cref="DeclarationFactSees"/> each
/// include it). A pin that cannot be parsed is a refusal of every question asked of it, so it
/// reds both facts rather than neither. Classification is the typed <c>Kind</c> field, never a
/// substring of rendered message text, so rewording a message can never empty a filter
/// (idiom: UpstreamPayloadInventoryTests.cs:71-77).</para>
///
/// <para>HONESTY. This guard binds the two names it holds and nothing else. It does not police
/// the admission rule's other requirement (that a listing commit name the machine class), it
/// cannot see a ban that is renamed AND re-declared AND re-listed in one coordinated edit, and
/// it proves nothing about whether the remaining <c>allowedSkips</c> entries are honest. Shape
/// precedent: FloorWrapperGuardTests / ProcessEnvCollectionGuardTests — repo-root walk, never
/// skips, fails closed on input it cannot parse, aggregated violations.</para>
/// </summary>
public sealed class AllowedSkipsBanGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] PinParts = ["client", "tests", "floor", "floor.json"];

    /// <summary>The pin's repo-relative path, asserted against the resolved path in both live
    /// facts so a substituted reader cannot quietly point them at a fixture.</summary>
    private const string PinRelativePath = "client/tests/floor/floor.json";

    /// <summary>Permanent ban (1): the SP-057 pin. HARD-CODED — never read from the file.</summary>
    private const string BannedSp057Pin =
        "CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault";

    /// <summary>Permanent ban (2): the named privacy flake. HARD-CODED — never read from the
    /// file. SP-085 fixed it at source; it stays banned regardless.</summary>
    private const string BannedPrivacyFlake =
        "CcpClient.Tests.ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery";

    private static readonly string[] PermanentlyBanned = [BannedSp057Pin, BannedPrivacyFlake];

    /// <summary>Named NEGATIVE control for the reflection resolver: a name that must never
    /// resolve. Without it, a resolver stubbed to return true would leave
    /// <see cref="BothBannedNames_StillResolveToARealFactInThisAssembly"/> green forever.</summary>
    private const string AbsentResolverControl =
        "CcpClient.Tests.DataRootOverrideTests.NoSuchFact_NegativeControlForTheBanNameResolver";

    // ------------------------------------------------------------------ seeded fixtures
    //
    // Held as class-level literals on purpose (idiom: ProcessEnvCollectionGuardTests.cs:86-89,
    // :553-555) so no detector shape lands in a [Fact] body, and — more importantly — so both
    // drift directions AND the unreadable-pin refusal are proven WITHOUT moving one byte of
    // client/tests/floor/floor.json.
    //
    // The two banned names are spelled VERBATIM below, independently of the constants above.
    // That is deliberate: a fixture generated from the rule it tests proves nothing, whereas an
    // independent spelling means editing a ban constant reds this fact alongside F2 and F3.

    private const string SeededControl =
        """
        {
          "projects": {
            "CcpClient.Tests": {
              "total": 3,
              "allowedSkips": [
                "CcpClient.Tests.SeededFixtureTests.AMachineClassGatedFact"
              ]
            }
          },
          "admissionRule": "seeded fixture rule. Two named permanent bans: (1) CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault; (2) CcpClient.Tests.ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery."
        }
        """;

    /// <summary>Drift direction (a): a banned name has entered <c>allowedSkips</c>. The
    /// declaration is intact, so exactly one violation, of kind
    /// <see cref="ViolationKind.Membership"/>, must come back.</summary>
    private const string SeededMembershipDrift =
        """
        {
          "projects": {
            "CcpClient.Tests": {
              "total": 3,
              "allowedSkips": [
                "CcpClient.Tests.SeededFixtureTests.AMachineClassGatedFact",
                "CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault"
              ]
            }
          },
          "admissionRule": "seeded fixture rule. Two named permanent bans: (1) CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault; (2) CcpClient.Tests.ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery."
        }
        """;

    /// <summary>Drift direction (b): the declaration itself has been quietly trimmed — the
    /// privacy flake is no longer named. The list is clean, so exactly one violation, of kind
    /// <see cref="ViolationKind.Declaration"/>, must come back.</summary>
    private const string SeededDeclarationDrift =
        """
        {
          "projects": {
            "CcpClient.Tests": {
              "total": 3,
              "allowedSkips": [
                "CcpClient.Tests.SeededFixtureTests.AMachineClassGatedFact"
              ]
            }
          },
          "admissionRule": "seeded fixture rule. One named permanent ban: (1) CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault."
        }
        """;

    /// <summary>The third refusal: a pin that exists but cannot be read (the control document
    /// truncated mid-array). Fail-open here would be silent and total, so this fixture is
    /// asserted through the LIVE facts' own filter delegates.</summary>
    private const string SeededUnreadablePin =
        """
        {
          "projects": {
            "CcpClient.Tests": {
              "total": 3,
              "allowedSkips": [
                "CcpClient.Tests.SeededFixtureTests.AMachineClassGatedFact"
        """;

    // ------------------------------------------------------------------ the four facts

    [Fact]
    public void TheCommittedPin_ListsNeitherPermanentlyBannedTest_InAnyProjectsAllowedSkips()
    {
        var pin = CommittedPin(); // throws if the anchor or the pin is absent — never a skip
        Assert.True(
            pin.FullPath.Replace('\\', '/').EndsWith(PinRelativePath, StringComparison.Ordinal),
            $"the committed pin resolved to {pin.FullPath}, which does not end with {PinRelativePath} — " +
            "this fact must read the SHARED pin, never a fixture standing in for it");

        var violations = PinViolations(pin.Text, PinRelativePath, out var census);

        // Non-vacuity pin on the live document (idiom: VacuousShapeGuardTests.cs:47,
        // ProcessEnvCollectionGuardTests.cs:247): a scan that examined no project entry examined
        // nothing, and "no violations" from it would mean nothing. An empty projects object is
        // also a Structural violation, so this assertion can never red an honest tree.
        Assert.True(census.ProjectCount > 0,
            $"the membership scan of {PinRelativePath} read {census.ProjectCount} project entr(y/ies) and " +
            $"{census.ScannedNameCount} allowedSkips name(s) — a scan that examined no project examined nothing");

        var offending = violations.Where(MembershipFactSees).ToList();
        Assert.True(offending.Count == 0,
            $"PERMANENTLY BANNED test name(s) present in allowedSkips (or {PinRelativePath} unreadable). " +
            "A ban entering that list is a silent, permanent blinding of the port's one mechanical pre-land " +
            "gate: the pin then reads green on a machine where the very defect it exists to catch is live. " +
            "The remedy is to fix the precondition so the test runs, never to list it." + Environment.NewLine +
            string.Join(Environment.NewLine, offending.Select(v => v.Message)));
    }

    [Fact]
    public void TheCommittedPin_AdmissionRule_StillDeclaresBothPermanentBans()
    {
        var pin = CommittedPin(); // throws if the anchor or the pin is absent — never a skip
        Assert.True(
            pin.FullPath.Replace('\\', '/').EndsWith(PinRelativePath, StringComparison.Ordinal),
            $"the committed pin resolved to {pin.FullPath}, which does not end with {PinRelativePath} — " +
            "this fact must read the SHARED pin, never a fixture standing in for it");

        var violations = PinViolations(pin.Text, PinRelativePath, out var census);

        // Same non-vacuity pin, on the field THIS fact reads. A missing, non-string, or blank
        // admissionRule is itself a Structural violation, so this can never red an honest tree.
        Assert.True(census.AdmissionRuleLength > 0,
            $"the declaration scan of {PinRelativePath} read an admissionRule of {census.AdmissionRuleLength} " +
            "character(s) — a scan that read no rule read nothing");

        var offending = violations.Where(DeclarationFactSees).ToList();
        Assert.True(offending.Count == 0,
            $"the admissionRule in {PinRelativePath} no longer names a PERMANENT BAN (or the pin is unreadable). " +
            "This is the other half of the trap: deleting a name from the declaration is the first move of the " +
            "two-edit dodge that would otherwise let it be listed in allowedSkips with nothing left to compare. " +
            "The names are hard-coded in this guard precisely so that edit reds HERE rather than passing " +
            "vacuously." + Environment.NewLine +
            string.Join(Environment.NewLine, offending.Select(v => v.Message)));
    }

    [Fact]
    public void BothBannedNames_StillResolveToARealFactInThisAssembly()
    {
        // The THIRD door, which the delete/add trap leaves open: RENAME. Rename the SP-057 pin
        // and the membership fact still passes (the old name is still absent), the declaration
        // fact still passes (admissionRule still carries the old text), and the NEW name may
        // then be listed with impunity — the ban evaporates with no red anywhere. Both banned
        // tests live in THIS assembly and both are top-level, non-nested, non-generic, so the
        // pinned string equals Type.FullName + "." + MethodName exactly and reflection is an
        // exact resolver (the criterion at ProcessEnvCollectionGuardTests.cs:22-30 selects
        // reflection here: neither of its two disqualifiers applies).
        var unresolved = new List<string>();
        foreach (var banned in PermanentlyBanned)
        {
            if (!ResolvesToAFact(banned))
            {
                unresolved.Add($"{banned} — no method of that name carrying [Fact] exists in " +
                    $"{typeof(AllowedSkipsBanGuardTests).Assembly.GetName().Name}");
            }
        }

        Assert.False(ResolvesToAFact(AbsentResolverControl),
            $"the negative control {AbsentResolverControl} RESOLVED — the resolver is not discriminating, so " +
            "the positive half of this fact proves nothing");
        Assert.True(unresolved.Count == 0,
            "a PERMANENTLY BANNED test name no longer names a real fact. Either the test was renamed — in which " +
            "case the ban has silently evaporated, because the other two facts keep passing while the NEW name is " +
            "free to enter allowedSkips — or it was deleted. Neither is a reason to edit this guard's constants " +
            "alone: update the constant, the admissionRule in " + PinRelativePath + ", and the seeded fixtures " +
            "below together, in one commit, or the ban is not a ban." + Environment.NewLine +
            string.Join(Environment.NewLine, unresolved));
    }

    [Fact]
    public void TheChecker_RefusesBothDriftDirections_AndAnUnreadablePin_AndPassesTheUnmutatedControl()
    {
        // On today's clean tree the two live facts above would ALSO pass with the mechanism
        // reverted, which is exactly the hazard this fact exists to remove: it drives the same
        // entry point, and the same two filter delegates, over documents that are NOT clean.

        var membership = PinViolations(SeededMembershipDrift, "seeded:membership-drift", out _);
        Assert.True(membership.Count == 1,
            $"seeded membership drift produced {membership.Count} violation(s), expected exactly 1 — " +
            "the membership rule no longer discriminates");
        Assert.Equal(ViolationKind.Membership, membership[0].Kind);
        Assert.True(membership[0].Message.Contains(BannedSp057Pin, StringComparison.Ordinal),
            $"the membership violation must NAME the offending test; it read: {membership[0].Message}");

        var declaration = PinViolations(SeededDeclarationDrift, "seeded:declaration-drift", out _);
        Assert.True(declaration.Count == 1,
            $"seeded declaration drift produced {declaration.Count} violation(s), expected exactly 1 — " +
            "the admissionRule containment rule no longer discriminates");
        Assert.Equal(ViolationKind.Declaration, declaration[0].Kind);
        Assert.True(declaration[0].Message.Contains(BannedPrivacyFlake, StringComparison.Ordinal),
            $"the declaration violation must NAME the ban that went missing; it read: {declaration[0].Message}");

        var control = PinViolations(SeededControl, "seeded:control", out var controlCensus);
        Assert.True(control.Count == 0,
            "the UNMUTATED control produced violation(s), so a rule fires unconditionally and the two live " +
            "facts are proving nothing:" + Environment.NewLine +
            string.Join(Environment.NewLine, control.Select(v => v.Message)));
        Assert.Equal(1, controlCensus.ProjectCount);
        Assert.Equal(1, controlCensus.ScannedNameCount);

        // The fail-closed half, asserted through the LIVE facts' own delegates rather than a
        // copy of them: if either filter is later narrowed to its own kind, a pin that cannot
        // be parsed would red NEITHER live fact, and this is the assertion that catches it.
        var unreadable = PinViolations(SeededUnreadablePin, "seeded:unreadable", out _);
        Assert.True(unreadable.Any(v => v.Kind == ViolationKind.Structural),
            "an unparseable pin produced no Structural violation — the checker failed OPEN, which is total and " +
            "silent: every question asked of the pin would then be answered by an empty list");
        Assert.True(unreadable.Any(MembershipFactSees),
            "an unparseable pin produced nothing the MEMBERSHIP fact can see — a pin that cannot be read is a " +
            "refusal of every question asked of it, so it must red that fact rather than pass it");
        Assert.True(unreadable.Any(DeclarationFactSees),
            "an unparseable pin produced nothing the DECLARATION fact can see — a pin that cannot be read is a " +
            "refusal of every question asked of it, so it must red that fact rather than pass it");
    }

    // ------------------------------------------------------------------ the checker

    private enum ViolationKind
    {
        /// <summary>The document could not be read as a floor pin. Admitted by BOTH live
        /// filters: an unreadable pin refuses every question, not none of them.</summary>
        Structural,

        /// <summary>A banned name is present in some project's <c>allowedSkips</c>.</summary>
        Membership,

        /// <summary>The <c>admissionRule</c> no longer names a banned test.</summary>
        Declaration,
    }

    private sealed record Violation(ViolationKind Kind, string Message);

    /// <summary>What the scan actually examined, so the live facts can pin that they read a
    /// real document rather than silently agreeing with an empty one.</summary>
    private sealed record PinCensus(int ProjectCount, int ScannedNameCount, int AdmissionRuleLength);

    private sealed record PinDocument(string FullPath, string Text);

    /// <summary>The ONE construction site for <see cref="ViolationKind.Structural"/>. Every
    /// unreadable shape routes through here, which is what makes a single seeded unparseable
    /// document sufficient to pin the routing for the whole class.</summary>
    private static Violation Structural(string source, string message) =>
        new(ViolationKind.Structural, $"{source}: {message}");

    /// <summary>The membership fact's filter. Structural is included deliberately.</summary>
    private static bool MembershipFactSees(Violation violation) =>
        violation.Kind is ViolationKind.Structural or ViolationKind.Membership;

    /// <summary>The declaration fact's filter. Structural is included deliberately.</summary>
    private static bool DeclarationFactSees(Violation violation) =>
        violation.Kind is ViolationKind.Structural or ViolationKind.Declaration;

    /// <summary>
    /// The whole rule, parameterized on the document text so the seeded fixtures can pin every
    /// branch without touching the shared pin (idiom: UpstreamPayloadInventoryTests.cs:116-119).
    /// Never throws on bad content and never returns "clean" for input it could not read: every
    /// such shape becomes a <see cref="Structural"/> violation.
    /// </summary>
    /// <param name="floorJsonText">The candidate pin document.</param>
    /// <param name="source">Label used in messages (the pin's path, or a fixture name).</param>
    /// <param name="census">What was actually examined.</param>
    private static IReadOnlyList<Violation> PinViolations(string floorJsonText, string source, out PinCensus census)
    {
        var violations = new List<Violation>();
        var projectCount = 0;
        var scannedNames = 0;
        var admissionRuleLength = 0;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(floorJsonText);
        }
        catch (JsonException ex)
        {
            violations.Add(Structural(source, $"not parseable as JSON ({ex.Message}) — an unreadable floor pin " +
                "is a VIOLATION, never a pass and never a skip"));
            census = new PinCensus(0, 0, 0);
            return violations;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                violations.Add(Structural(source, $"root is {root.ValueKind}, not a JSON object"));
                census = new PinCensus(0, 0, 0);
                return violations;
            }

            if (!root.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
            {
                violations.Add(Structural(source, "has no \"projects\" object, so no allowedSkips list can be read"));
            }
            else
            {
                foreach (var project in projects.EnumerateObject())
                {
                    projectCount++;
                    if (project.Value.ValueKind != JsonValueKind.Object)
                    {
                        violations.Add(Structural(source, $"projects.{project.Name} is {project.Value.ValueKind}, not an object"));
                        continue;
                    }

                    if (!project.Value.TryGetProperty("allowedSkips", out var skips) || skips.ValueKind != JsonValueKind.Array)
                    {
                        violations.Add(Structural(source, $"projects.{project.Name} has no \"allowedSkips\" array"));
                        continue;
                    }

                    var index = -1;
                    foreach (var entry in skips.EnumerateArray())
                    {
                        index++;
                        if (entry.ValueKind != JsonValueKind.String)
                        {
                            violations.Add(Structural(source,
                                $"projects.{project.Name}.allowedSkips[{index}] is {entry.ValueKind}, not a string"));
                            continue;
                        }

                        scannedNames++;
                        // Compared on the trimmed pre-'(' portion (the wrapper's own matching
                        // semantics, check-floor.mjs:68-74) and case-INSENSITIVELY. An entry that
                        // differs from a banned name only by casing or by a theory-argument suffix
                        // can never match a real TRX name, so it can never be honest; flagging it
                        // closes two spelling-shaped dodges at the cost of one unreachable false
                        // positive (two real tests whose full names differ only in case).
                        var compared = entry.GetString()!.Split('(')[0].Trim();
                        foreach (var banned in PermanentlyBanned)
                        {
                            if (!string.Equals(compared, banned, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            violations.Add(new Violation(ViolationKind.Membership,
                                $"{source}: projects.{project.Name}.allowedSkips[{index}] lists the PERMANENTLY " +
                                $"BANNED test {banned}"));
                        }
                    }
                }

                if (projectCount == 0)
                {
                    violations.Add(Structural(source, "\"projects\" is empty, so there is no allowedSkips list to police"));
                }
            }

            if (!root.TryGetProperty("admissionRule", out var rule) || rule.ValueKind != JsonValueKind.String)
            {
                violations.Add(Structural(source, "has no \"admissionRule\" string, so the declaration of the two " +
                    "permanent bans cannot be read at all"));
            }
            else
            {
                var ruleText = rule.GetString()!;
                admissionRuleLength = ruleText.Trim().Length;
                if (admissionRuleLength == 0)
                {
                    violations.Add(Structural(source, "\"admissionRule\" is blank"));
                }
                else
                {
                    foreach (var banned in PermanentlyBanned)
                    {
                        // Ordinal on purpose: "still names this test" means verbatim.
                        if (ruleText.Contains(banned, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        violations.Add(new Violation(ViolationKind.Declaration,
                            $"{source}: the admissionRule no longer names the PERMANENTLY BANNED test {banned}"));
                    }
                }
            }
        }

        census = new PinCensus(projectCount, scannedNames, admissionRuleLength);
        return violations;
    }

    /// <summary>True when <paramref name="fullyQualifiedName"/> names a method in this assembly
    /// that carries <see cref="FactAttribute"/>. Metadata-only: this runs no type initializer
    /// and constructs no test class, so it cannot perturb the scheduling or the port
    /// registration of the classes it inspects.</summary>
    private static bool ResolvesToAFact(string fullyQualifiedName)
    {
        var lastDot = fullyQualifiedName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == fullyQualifiedName.Length - 1)
        {
            return false;
        }

        var type = typeof(AllowedSkipsBanGuardTests).Assembly
            .GetType(fullyQualifiedName[..lastDot], throwOnError: false, ignoreCase: false);
        if (type is null)
        {
            return false;
        }

        var methodName = fullyQualifiedName[(lastDot + 1)..];
        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        foreach (var method in type.GetMethods(Flags))
        {
            if (string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves and reads the committed pin. The existence refusal lives HERE rather
    /// than in a fact body, matching SP-086 (ProcessEnvCollectionGuardTests.cs:553-555) — and
    /// against the opposite, equally live precedent recorded for
    /// FloorWrapperGuardTests.PacketsAtOrAboveSp073 in vacuous-shape-ledger.json, which keeps
    /// its checkpoint in the body so the lexical detector can SEE it. The consequence of the
    /// choice made here is stated in record.md: this guard's never-skip checkpoint is invisible
    /// to that ledger by construction, and is instead proven by the fact that a missing pin
    /// throws out of every fact.</summary>
    private static PinDocument CommittedPin()
    {
        var path = Path.Combine([FindRepoRoot(), .. PinParts]);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"floor pin not found at {path} — the allowedSkips ban guard refuses to skip");
        }

        return new PinDocument(path, File.ReadAllText(path));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} " +
            $"(anchor: {string.Join('/', RepoAnchorParts)}) — the allowedSkips ban guard refuses to skip");
    }
}
