using System.Reflection;
using Avalonia.Platform;
using CcpClient.Desktop.Manifest;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-009 asset-manifest proofs (asset-manifest.md §Two-direction validation rule):
/// forward opens, root completeness sweep, the case-exactness NAMED check (ordinal), the
/// assert-empty copied direction, and schema validation of synthetic user/mod/copied
/// entries. All against the REAL built CcpClient.Desktop assembly — no mocks.
/// </summary>
public class AssetManifestTests
{
    private static Assembly DesktopAssembly => typeof(MainWindow).Assembly;

    private static AssetEntry[] LoadRealManifest()
    {
        var loader = new StandardAssetLoader(DesktopAssembly);
        using var stream = loader.Open(new Uri(AssetManifest.AssemblyAssetUriPrefix + AssetManifest.ManifestAssetPath));
        Assert.True(AssetManifest.TryParse(stream, out var entries, out var errors),
            "embedded manifest must parse: " + string.Join("; ", errors));
        return entries!;
    }

    [Fact]
    public void Manifest_Embedded_ParsesAndSelfLists()
    {
        var entries = LoadRealManifest();
        Assert.Contains(entries, e => e.Id == "demo.status-ticker.icon"
            && e.Source == AssetSource.Embedded && e.Required && e.Path == "Assets/demo-status-ticker.png");
        // Self-listing (asset-manifest.md §Catalogue placement): the manifest is a real
        // embedded asset with a real entry — never a sweep special-case.
        Assert.Contains(entries, e => e.Id == "asset.manifest"
            && e.Source == AssetSource.Embedded && e.Required && e.Path == AssetManifest.ManifestAssetPath);
    }

    [Fact]
    public void Forward_EveryRequiredEmbeddedAsset_Opens()
    {
        var entries = LoadRealManifest();
        var loader = new StandardAssetLoader(DesktopAssembly);
        var required = entries.Where(e => e is { Source: AssetSource.Embedded, Required: true }).ToArray();
        Assert.NotEmpty(required);
        foreach (var entry in required)
        {
            using var stream = loader.Open(new Uri(AssetManifest.AssemblyAssetUriPrefix + entry.Path));
            Assert.True(stream.Length > 0, $"{entry.Id} must have content");
        }
    }

    [Fact]
    public void TwoDirection_Verifier_ReportsNoFailures_OnRealManifest()
    {
        var failures = AssetVerifier.Verify(LoadRealManifest(), DesktopAssembly);
        Assert.Empty(failures);
    }

    [Fact]
    public void CompletenessSweep_RootEnumeration_MatchesManifestExactly()
    {
        // Empirical rule-from-observation (consult Q2): the bundle contains exactly the
        // Assets/** glob plus compiler-owned '!'-prefixed metadata (observed:
        // !AvaloniaResourceXamlInfo) — excluded by the NAMED rule, never special-cased per
        // entry. Rooting at the assembly root, not at Assets/, so a future second
        // AvaloniaResource glob lands here and fails.
        var loader = new StandardAssetLoader(DesktopAssembly);
        var enumerated = loader.GetAssets(new Uri(AssetManifest.AssemblyAssetUriPrefix), null)
            .Select(u => u.AbsolutePath.TrimStart('/'))
            .Where(p => !AssetVerifier.IsCompilerOwnedBundleEntry(p))
            .ToArray();
        var manifestPaths = LoadRealManifest()
            .Where(e => e.Source == AssetSource.Embedded)
            .Select(e => e.Path)
            .ToArray();
        Assert.Equal(
            manifestPaths.Order(StringComparer.Ordinal),
            enumerated.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CaseExactness_NamedCheck_ManifestPathsMatchEmbeddedCaseOrdinalExactly()
    {
        // The row's highest-value assertion: ordinal comparison, never case-tolerant.
        // A case-drifted manifest path must produce a NAMED case-mismatch failure.
        var real = LoadRealManifest();
        var drifted = real
            .Select(e => e.Path == "Assets/demo-status-ticker.png"
                ? e with { Path = "Assets/DEMO-status-ticker.png" }
                : e)
            .ToArray();
        var failures = AssetVerifier.Verify(drifted, DesktopAssembly);
        Assert.Contains(failures, f => f.Reason.StartsWith("case-mismatch:", StringComparison.Ordinal)
            && f.Reason.Contains("Assets/DEMO-status-ticker.png", StringComparison.Ordinal)
            && f.Reason.Contains("Assets/demo-status-ticker.png", StringComparison.Ordinal));
        Assert.DoesNotContain(failures, f => f.Reason == "unmanifested-embedded-asset"
            && f.Path == "Assets/demo-status-ticker.png");
    }

    [Fact]
    public void CompletenessSweep_SyntheticUnmanifestedAsset_FailsAndNamesIt()
    {
        var dropped = LoadRealManifest().Where(e => e.Path != "Assets/demo-status-ticker.png").ToArray();
        var failures = AssetVerifier.Verify(dropped, DesktopAssembly);
        Assert.Contains(failures, f => f.Reason == "unmanifested-embedded-asset"
            && f.Path == "Assets/demo-status-ticker.png");
    }

    [Fact]
    public void Forward_SyntheticMissingAsset_ReportsOpenFailed()
    {
        var real = LoadRealManifest();
        var missing = real
            .Select(e => e.Path == "Assets/demo-status-ticker.png"
                ? e with { Path = "Assets/no-such-asset.png" }
                : e)
            .ToArray();
        var failures = AssetVerifier.Verify(missing, DesktopAssembly);
        Assert.Contains(failures, f => f.Path == "Assets/no-such-asset.png"
            && f.Reason.StartsWith("open-failed:", StringComparison.Ordinal));
    }

    [Fact]
    public void CopiedDirection_RealManifest_AllCopiedEntriesPresentCaseExact_SweepClean()
    {
        // SP-023 (first copied consumer — the documented extension of the assert-empty
        // direction): 3682 copied entries (1542 DTRH payload + 2 product overlay + 2138
        // intake payload, SP-054's flagged glue bundle) verified against the REAL output
        // directory — existence, ordinal case-exactness, sweep.
        //
        // 2137 -> 2138 at the SP-054 land (orchestrator, upstream sync v6.6.3 -> v6.7.4,
        // merge 42286638): upstream added `intake/core/accents.js` while SP-054 was in
        // flight, so the manifest generated from the pre-sync tree was one entry short of
        // the tree the glob now copies. The count is the tripwire that caught it — bump it
        // WITH the reason, never to silence a sweep failure.
        var entries = LoadRealManifest();
        var copied = entries.Where(e => e.Source == AssetSource.Copied).ToArray();
        Assert.Equal(3682, copied.Length);
        Assert.Contains(copied, e => e.Id == "dtrh.payload/bridge.js"
            && e.Path == "payload/dtrh/bridge.js" && e.Required && e.Trust == "full");
        Assert.Contains(copied, e => e.Id == "dtrh.overlay/bridge.js"
            && e.Path == "payload-overlay/bridge.js");
        Assert.Contains(copied, e => e.Id == "intake.payload/web-shim.js"
            && e.Path == "payload/intake/web-shim.js" && e.Required && e.Trust == "full");
        var outputRoot = Path.GetDirectoryName(DesktopAssembly.Location)!;
        var failures = AssetVerifier.VerifyCopied(entries, outputRoot);
        Assert.Empty(failures);
    }

    [Fact]
    public void CopiedDirection_CaseDrift_IsNamedFailure()
    {
        // ext4 vs NTFS drift protection (SP-009 §3): a differently-cased on-disk file must
        // fail the ordinal walk even though File.Exists would tolerate it on NTFS.
        var root = Path.Combine(Path.GetTempPath(), "ccp-sp023-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "PAYLOAD"));
        File.WriteAllText(Path.Combine(root, "PAYLOAD", "File.PNG"), "x");
        var entries = new[]
        {
            new AssetEntry("test.copied", AssetSource.Copied, "payload/File.PNG", true,
                new AssetProvenance("synthetic", "test"), ["desktop"], "none", "full"),
        };
        var failures = AssetVerifier.VerifyCopied(entries, root);
        Assert.Contains(failures, f => f.Reason == "copied-missing-or-case-drift");
    }

    [Fact]
    public void CopiedDirection_UnmanifestedFile_SweepFailsAndNamesIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-sp023-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "payload"));
        File.WriteAllText(Path.Combine(root, "payload", "listed.png"), "x");
        File.WriteAllText(Path.Combine(root, "payload", "sneaky.png"), "x");
        var entries = new[]
        {
            new AssetEntry("test.copied", AssetSource.Copied, "payload/listed.png", true,
                new AssetProvenance("synthetic", "test"), ["desktop"], "none", "full"),
        };
        var failures = AssetVerifier.VerifyCopied(entries, root);
        Assert.Contains(failures, f => f.Reason == "unmanifested-copied-asset"
            && f.Path == "payload/sneaky.png");
        Assert.DoesNotContain(failures, f => f.Path == "payload/listed.png");
    }

    [Fact]
    public void SelfCheck_RealAssembly_ExitZero_WithPerAssetLines()
    {
        // The --verify-assets path exercised from unit tests (SP-009 Step 3): same code
        // the real binary runs, same assembly, captured output.
        var output = new StringWriter();
        var exit = AssetSelfCheck.Run(DesktopAssembly, output);
        Assert.Equal(0, exit);
        var text = output.ToString();
        Assert.Contains("asset OK demo.status-ticker.icon Assets/demo-status-ticker.png", text, StringComparison.Ordinal);
        Assert.Contains($"asset OK asset.manifest {AssetManifest.ManifestAssetPath}", text, StringComparison.Ordinal);
        Assert.Contains("verify-assets: PASS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("asset FAIL", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_ValidUserModCopiedEntries_Parse()
    {
        // Schema covers policy-shaped entries; loading is unimplemented and recorded.
        const string json = """
        {
          "version": 1,
          "assets": [
            {
              "id": "some.user.image", "source": "user", "path": "images/photo.png",
              "required": false,
              "provenance": { "origin": "user-supplied", "license": "user-owned" },
              "heads": [ "desktop" ], "overridePolicy": "none", "trust": "user"
            },
            {
              "id": "some.mod.spiral", "source": "mod", "path": "spirals/custom.png",
              "required": false,
              "provenance": { "origin": "third-party mod", "license": "cc-by-4.0" },
              "heads": [ "desktop" ], "overridePolicy": "mod", "trust": "mod"
            },
            {
              "id": "some.copied.lang", "source": "copied", "path": "Localization/en.json",
              "required": true,
              "provenance": { "origin": "shipped content file", "license": "project-internal" },
              "heads": [ "desktop" ], "overridePolicy": "user", "trust": "full"
            }
          ]
        }
        """;
        Assert.True(AssetManifest.TryParse(json, out var entries, out var errors),
            string.Join("; ", errors));
        Assert.Equal(3, entries!.Length);
        Assert.Equal(AssetSource.User, entries[0].Source);
        Assert.Equal(AssetSource.Mod, entries[1].Source);
        Assert.Equal(AssetSource.Copied, entries[2].Source);
    }

    [Theory]
    [InlineData("{\"version\": 2, \"assets\": []}", "version")]
    [InlineData("{\"version\": 1}", "assets")]
    [InlineData("{\"version\": 1, \"assets\": [{\"id\": \"a\", \"source\": \"embedded\", \"path\": \"Assets/x.png\", \"required\": true, \"provenance\": {\"origin\": \"o\", \"license\": \"l\"}, \"heads\": [\"desktop\"], \"overridePolicy\": \"none\", \"trust\": \"full\"}, {\"id\": \"a\", \"source\": \"embedded\", \"path\": \"Assets/y.png\", \"required\": true, \"provenance\": {\"origin\": \"o\", \"license\": \"l\"}, \"heads\": [\"desktop\"], \"overridePolicy\": \"none\", \"trust\": \"full\"}]}", "duplicate id")]
    public void Schema_InvalidDocuments_AreRejected(string json, string expectedProblem)
    {
        Assert.False(AssetManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains(expectedProblem, StringComparison.Ordinal));
    }

    [Theory]
    // path boundary rules (asset-manifest.md §Schema — rooted relative only)
    [InlineData("Assets/../secret.txt", "'..'")]
    [InlineData("/absolute/path.png", "rooted")]
    [InlineData("C:/abs/path.png", "drive-absolute")]
    [InlineData("Assets\\\\backslash.png", "'/'-separated")]
    // embedded policy consistency
    [InlineData("Elsewhere/x.png", "under Assets/")]
    public void Schema_BadPaths_AreRejected(string badPath, string expectedProblem)
    {
        var json = $$"""
        {
          "version": 1,
          "assets": [
            {
              "id": "bad", "source": "embedded", "path": "{{badPath}}",
              "required": true,
              "provenance": { "origin": "o", "license": "l" },
              "heads": [ "desktop" ], "overridePolicy": "none", "trust": "full"
            }
          ]
        }
        """;
        Assert.False(AssetManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains(expectedProblem, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("alien", "full", "none", "source")]
    [InlineData("embedded", "user", "none", "trust")]
    [InlineData("embedded", "full", "mod", "overridePolicy")]
    public void Schema_EmbeddedPolicyViolations_AreRejected(string source, string trust, string overridePolicy, string expectedProblem)
    {
        var json = $$"""
        {
          "version": 1,
          "assets": [
            {
              "id": "bad", "source": "{{source}}", "path": "Assets/x.png",
              "required": true,
              "provenance": { "origin": "o", "license": "l" },
              "heads": [ "desktop" ], "overridePolicy": "{{overridePolicy}}", "trust": "{{trust}}"
            }
          ]
        }
        """;
        Assert.False(AssetManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains(expectedProblem, StringComparison.Ordinal));
    }
}
