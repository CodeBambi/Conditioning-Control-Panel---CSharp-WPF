using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The guard on <c>client/THIRD-PARTY-NOTICES.md</c>.
///
/// <para><b>Why this exists.</b> A notices file is a legal artefact whose failure mode is silence:
/// it does not stop compiling when a lane adds a dependency, and a file that covers most of what
/// ships is worse than none because it LOOKS complete. This port has learned four times over that a
/// document nobody reads rots — the P2 blink row was closed as a guard rather than prose for exactly
/// that reason. So the notices file's completeness claim is not prose here. It is DERIVED from the
/// shipping bytes on every run, and the document is the DATA.</para>
///
/// <para><b>The three derivations, and what each can and cannot catch.</b>
/// (1) The MANAGED closure comes from the restored dependency graph, not from the csproj's nine
/// direct <c>PackageReference</c>s — a transitive addition redistributes just as much as a direct
/// one, and Newtonsoft.Json is already in this tree by that route alone.
/// (2) The WEB components come from walking the payload trees the csproj actually globs, with the
/// glob roots parsed OUT OF the csproj, so a lane adding a seventh payload extends the sweep without
/// touching this file. Each component is required by its SHIPPING PATH rather than by a bare name:
/// "three" appears in ordinary prose and would pass vacuously, <c>payload/dtrh/vendor/three/</c>
/// cannot.
/// (3) The GAZE digests are re-hashed from the read-only WPF tree, so the notices file cannot claim
/// a SHA-256 those bytes do not have.
/// None of the three can see third-party code bundled INSIDE a native binary (Skia inside
/// libSkiaSharp, the VLC plugin tree, miniaudio inside SoundFlow). Those are hand-listed in the
/// document's §3/§4 and the document says so.</para>
///
/// <para><b>No skips, and no network.</b> A missing restore, a missing notices file or a missing
/// read-only tree is a hard FAILURE with a message naming the cause — none of those is a property of
/// the machine or the OS, so none may be a skip. Nothing here downloads anything: the byte
/// provenance behind §5 needed 6 MB of upstream artifacts and is recorded once, with its method and
/// its negative controls, in <c>client/docs/gaze-model-provenance.md</c>.</para>
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] NoticesParts = ["client", "THIRD-PARTY-NOTICES.md"];
    private static readonly string[] ProjectParts = ["client", "src", "CcpClient.Desktop", "CcpClient.Desktop.csproj"];
    private static readonly string[] AssetsParts = ["client", "src", "CcpClient.Desktop", "obj", "project.assets.json"];
    private static readonly string[] WpfWebParts = ["ConditioningControlPanel", "Resources", "web"];
    private static readonly string[] WpfModelParts = ["ConditioningControlPanel", "Resources", "Models"];

    private const string NoticesRelativePath = "client/THIRD-PARTY-NOTICES.md";

    /// <summary>The three committed gaze models, by the filename the WPF tree uses. The DIGESTS are
    /// deliberately not written here: they are computed from the bytes, and the notices file is what
    /// gets checked against them. Pinning them in both places would only prove the two constants
    /// agree.</summary>
    private static readonly string[] GazeModelFiles =
    [
        "face_detection_short_range.onnx",
        "face_landmark.onnx",
        "iris_landmark.onnx",
    ];

    /// <summary>
    /// Every package the shipping project redistributes — the whole transitive closure — is named in
    /// the notices file.
    ///
    /// <para>This is the fact that reds when a lane adds a dependency and forgets the notice. It
    /// reads the restored graph rather than the csproj because the csproj names 9 packages and 50
    /// actually ship.</para>
    ///
    /// <para><b>Known limit, stated rather than discovered later:</b> a package id that is a
    /// SUBSTRING of one already listed would pass without its own entry. No such pair exists today
    /// and the failure direction is conservative — it under-reports, never invents a violation.</para>
    /// </summary>
    [Fact]
    public void EveryRedistributedPackage_IsNamedInTheNotices()
    {
        var root = FindRepoRoot();
        var notices = ReadNotices(root);
        var packages = RestoredPackageClosure(root);

        Assert.True(
            packages.Count >= 40,
            $"the restored closure parsed only {packages.Count} package(s), which is not a plausible "
            + "closure for this project — the parse is broken or the graph is stale, and a guard that "
            + "checks an empty list proves nothing");

        var missing = packages.Where(p => !notices.Contains(p, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{NoticesRelativePath} does not name {missing.Count} of the {packages.Count} package(s) "
            + "this project redistributes, so it asserts a completeness it does not have: "
            + string.Join(", ", missing)
            + ". Add each one with the licence its own .nuspec (or its own bundled licence FILE) "
            + "declares — read the artifact, never the project's website.");
    }

    /// <summary>
    /// Every third-party library vendored inside a web payload the project globs is named in the
    /// notices file, BY ITS SHIPPING PATH.
    ///
    /// <para>The payload bytes stay owned by the legacy tree and none is forked into
    /// <c>client/</c> — but the project copies six trees into <c>payload/</c> beside the binary, so
    /// the client redistributes them and their vendored libraries are ours to attribute. One of them
    /// (gifenc) ships with its licence banner stripped by whatever build vendored it, which makes
    /// the notices file the only place that attribution can travel at all.</para>
    /// </summary>
    [Fact]
    public void EveryVendoredWebLibrary_IsNamedInTheNotices()
    {
        var root = FindRepoRoot();
        var notices = ReadNotices(root);
        var webRoot = Path.Combine([root, .. WpfWebParts]);

        Assert.True(
            Directory.Exists(webRoot),
            $"the read-only payload tree is not at {webRoot}, so this guard reached no verdict — "
            + "it neither found an unattributed library nor cleared the tree of one. The client "
            + "csproj globs those trees into payload/ beside the binary; a checkout without them "
            + "is broken, not a machine this guard may skip on.");

        var globbedRoots = GlobbedPayloadRoots(root);
        Assert.True(
            globbedRoots.Count > 0,
            "no payload glob was parsed out of the client csproj, so the sweep below would walk "
            + "nothing and pass vacuously. The csproj's Content globs are the definition of what "
            + "this client redistributes; if their shape changed, this parser must change with it.");

        var required = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var payload in globbedRoots)
        {
            var payloadRoot = Path.Combine(webRoot, payload);
            Assert.True(
                Directory.Exists(payloadRoot),
                $"the client csproj globs Resources/web/{payload}/**, but {payloadRoot} does not "
                + "exist — a half-present read-only tree is a corrupt checkout, and this guard "
                + "fails rather than quietly sweeping fewer trees than the product ships.");

            // The root-level `vendor` tree IS a payload glob of its own, so the payload root can
            // itself be the vendor directory; the other five carry theirs somewhere inside.
            var vendorDirs = new List<string>();
            if (Path.GetFileName(payloadRoot).Equals("vendor", StringComparison.OrdinalIgnoreCase))
            {
                vendorDirs.Add(payloadRoot);
            }

            vendorDirs.AddRange(Directory.EnumerateDirectories(payloadRoot, "vendor", SearchOption.AllDirectories));

            foreach (var vendorDir in vendorDirs)
            {
                // A component is a DIRECT child of a vendor/ directory: a subdirectory is one
                // library, and loose files directly inside vendor/ are one library shipped flat
                // (arcademy vendors three.js as two bare .js files).
                foreach (var dir in Directory.GetDirectories(vendorDir))
                {
                    required.Add(ShippingPath(webRoot, dir) + "/");
                }

                if (Directory.GetFiles(vendorDir).Length > 0)
                {
                    required.Add(ShippingPath(webRoot, vendorDir) + "/");
                }
            }
        }

        Assert.True(
            required.Count > 0,
            "the vendor sweep found no components at all across "
            + $"{globbedRoots.Count} globbed payload tree(s), which cannot be right — this checkout "
            + "carries vendored three.js, gifenc, omggif, fflate and mp4-muxer. A sweep that finds "
            + "nothing passes vacuously, so it fails here instead.");

        var missing = required.Where(p => !notices.Contains(p, StringComparison.Ordinal)).ToList();

        Assert.True(
            missing.Count == 0,
            $"{NoticesRelativePath} does not name {missing.Count} of the {required.Count} vendored web "
            + "component(s) the client copies beside its binary: " + string.Join(", ", missing)
            + ". Each needs its origin, copyright and licence — read the file header or the LICENSE "
            + "shipped beside it, and where the vendored copy carries NEITHER, say so in the entry.");
    }

    /// <summary>
    /// The gaze-model SHA-256s the notices file states are the SHA-256s of the bytes it describes.
    ///
    /// <para>The client ships none of these models — the entry is recorded ahead of any admission
    /// because the gaze row made it an acceptance condition. What this fact stops is the specific
    /// failure that row has already made three times: asserting a provenance nobody re-checked. If
    /// the read-only tree's model bytes ever move, the notices file goes red rather than stale.</para>
    /// </summary>
    [Fact]
    public void TheGazeModelDigests_MatchTheCommittedModelBytes()
    {
        var root = FindRepoRoot();
        var notices = ReadNotices(root);
        var modelsDir = Path.Combine([root, .. WpfModelParts]);

        Assert.True(
            Directory.Exists(modelsDir),
            $"the read-only model directory is not at {modelsDir}, so the digests in "
            + $"{NoticesRelativePath} §5 were compared against nothing. Those files are committed "
            + "to this repository; their absence is a broken checkout rather than a machine "
            + "property, so this guard fails instead of skipping.");

        var wrong = new List<string>();
        foreach (var file in GazeModelFiles)
        {
            var path = Path.Combine(modelsDir, file);
            Assert.True(
                File.Exists(path),
                $"{path} is missing, so the {file} digest in {NoticesRelativePath} §5 stands on "
                + "nothing. The three .onnx files are committed directly to the WPF tree.");

            var actual = Sha256Hex(path);
            if (!notices.Contains(actual, StringComparison.OrdinalIgnoreCase))
            {
                wrong.Add($"{file} hashes to {actual}, which {NoticesRelativePath} does not state");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "the notices file's gaze provenance disagrees with the bytes it describes: "
            + string.Join("; ", wrong)
            + ". Either the upstream model bytes rotated — which is a FINDING and needs the "
            + "provenance chain in client/docs/gaze-model-provenance.md re-run against Google's own "
            + "pinned digests — or a digest was transcribed wrongly. Do not edit the document to "
            + "match a byte change without re-establishing where the new bytes came from.");
    }

    // ---- derivations ----

    /// <summary>The transitive package closure, from the restored graph. Every entry typed
    /// <c>package</c> in every target framework; project references are excluded because the client
    /// owns those.</summary>
    private static List<string> RestoredPackageClosure(string root)
    {
        var assetsPath = Path.Combine([root, .. AssetsParts]);
        if (!File.Exists(assetsPath))
        {
            Assert.Fail(
                $"the restored dependency graph is not at {assetsPath}, so the notices file was "
                + "checked against no package list at all. The test project references "
                + "CcpClient.Desktop, so a normal build produces this; run `dotnet restore "
                + "client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` if it is genuinely absent. "
                + "This guard fails rather than skips — an unrestored tree is not a machine property.");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var framework in doc.RootElement.GetProperty("targets").EnumerateObject())
        {
            foreach (var entry in framework.Value.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("type", out var type)
                    && type.GetString() == "package")
                {
                    ids.Add(entry.Name.Split('/')[0]);
                }
            }
        }

        return [.. ids];
    }

    /// <summary>
    /// The notices file travels WITH THE ARTIFACT, which is the only place the obligation binds.
    ///
    /// <para><b>Why this is not a shape check.</b> The first half asserts an OUTCOME: the document is
    /// beside the running test binary. Nothing in this test project copies it there — it can only
    /// arrive by riding the shipping project's own copy wiring out through the ProjectReference, so
    /// deleting that wiring reds this line rather than quietly emptying the artifact.</para>
    ///
    /// <para>The second half is the part an outcome cannot reach from here: build output is not
    /// redistribution, and Apache-2.0 §4 and LGPL-2.1 §1 bind at DISTRIBUTION. A unit test cannot
    /// afford a self-contained publish, so the publish leg is asserted on the project item that
    /// carries it. The real publish is evidence on the row, not here.</para>
    /// </summary>
    [Fact]
    public void TheNoticesFile_TravelsWithTheArtifact_AndNotOnlyTheRepo()
    {
        var shipped = Directory.GetFiles(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        Assert.True(
            shipped.Length == 1,
            $"THIRD-PARTY-NOTICES.md is not beside the binary at {AppContext.BaseDirectory}. It "
            + "reaches this directory only through the shipping project's copy wiring, so its "
            + "absence means the artifact ships without the notices — and a notices file that stays "
            + "in the repository discharges nothing, because the obligation binds on distribution.");

        var root = FindRepoRoot();
        Assert.True(
            File.ReadAllText(shipped[0]) == ReadNotices(root),
            "the THIRD-PARTY-NOTICES.md beside the binary is not the repository's copy. A stale "
            + "notices file is worse than none: it travels with the artifact stating attribution "
            + "and licences that no longer describe what the artifact contains.");

        var csproj = File.ReadAllText(Path.Combine([root, .. ProjectParts]));
        var item = Regex.Match(
            csproj,
            @"<Content\s+Include=""[^""]*THIRD-PARTY-NOTICES\.md""\s*>(.*?)</Content>",
            RegexOptions.Singleline);

        Assert.True(
            item.Success,
            "CcpClient.Desktop.csproj has no <Content> item for THIRD-PARTY-NOTICES.md, so nothing "
            + "puts it into a published artifact even if a stale copy still sits in bin/.");

        Assert.Contains("CopyToPublishDirectory", item.Groups[1].Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The LGPL managed assembly stays OUT of the single-file bundle, so a user can substitute it.
    ///
    /// <para><b>What this guards, and why it is a project-file assertion.</b> The publish strategy is
    /// self-contained single-file, which fuses every managed assembly into the apphost — and an
    /// assembly fused into an executable cannot be replaced, which is exactly the substitution right
    /// LGPL-2.1 §6 preserves. <c>THIRD-PARTY-NOTICES.md</c> §4 now STATES that obligation is
    /// discharged. Delete the target and the document becomes a false statement about a licence,
    /// silently, at the next publish. Proving the outcome needs a real self-contained publish, which
    /// is minutes and gigabytes and belongs to the publish evidence rather than the floor; what is
    /// affordable here is that the claim and the wiring cannot drift apart.</para>
    /// </summary>
    [Fact]
    public void TheLgplManagedAssembly_IsKeptOutOfTheSingleFileBundle()
    {
        var csproj = File.ReadAllText(Path.Combine([FindRepoRoot(), .. ProjectParts]));
        var target = Regex.Match(
            csproj,
            @"<Target\b[^>]*BeforeTargets=""_ComputeFilesToBundle""[^>]*>(.*?)</Target>",
            RegexOptions.Singleline);

        Assert.True(
            target.Success,
            "CcpClient.Desktop.csproj no longer has a target running before the SDK's "
            + "_ComputeFilesToBundle, which is the only place ExcludeFromSingleFile metadata is "
            + "read. Without it the single-file publish bundles LibVLCSharp.dll into the apphost, "
            + "and THIRD-PARTY-NOTICES.md §4 claims an LGPL-2.1 §6 discharge the artifact does not "
            + "have.");

        Assert.Contains("LibVLCSharp.dll", target.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("ExcludeFromSingleFile", target.Groups[1].Value, StringComparison.Ordinal);
    }

    /// <summary>The payload trees the shipping csproj globs out of the read-only tree, parsed from
    /// the csproj itself so a seventh glob extends the sweep automatically.</summary>
    private static List<string> GlobbedPayloadRoots(string root)
    {
        var csproj = File.ReadAllText(Path.Combine([root, .. ProjectParts]));
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(csproj, @"Resources\\web\\([A-Za-z0-9_.-]+)\\\*\*"))
        {
            names.Add(m.Groups[1].Value);
        }

        return [.. names];
    }

    /// <summary>Where a path under the read-only web root lands beside the shipped binary. The
    /// csproj links every one of those trees under <c>payload/</c>, so this is the path a reader of
    /// the notices file can actually go and look at.</summary>
    private static string ShippingPath(string webRoot, string path) =>
        "payload/" + Path.GetRelativePath(webRoot, path).Replace('\\', '/');

    private static string Sha256Hex(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new Xunit.Sdk.XunitException(
                $"ThirdPartyNoticesTests could not READ {path} to hash it, so the gaze provenance "
                + $"fact reached no verdict. The open failed with {ex.GetType().Name}: {ex.Message}. "
                + "Another process holding the file open is the usual cause and is not a product "
                + "defect.",
                ex);
        }
    }

    private static string ReadNotices(string root)
    {
        var path = Path.Combine([root, .. NoticesParts]);
        if (!File.Exists(path))
        {
            Assert.Fail(
                $"{NoticesRelativePath} is missing at {path}. It is not optional: this client "
                + "redistributes Apache-2.0, BSD-3-Clause and LGPL-2.1-or-later works whose licences "
                + "and attribution are obliged to travel with the distribution.");
        }

        return File.ReadAllText(path);
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
            $"repo root (the directory holding {string.Join('/', RepoAnchorParts)}) not found above "
            + $"{AppContext.BaseDirectory} — this guard fails rather than skips");
    }
}
