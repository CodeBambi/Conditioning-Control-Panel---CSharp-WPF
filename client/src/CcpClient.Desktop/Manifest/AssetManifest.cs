using System.Reflection;
using System.Text.Json;
using Avalonia.Platform;

namespace CcpClient.Desktop.Manifest;

/// <summary>
/// Source kinds declared by the asset catalogue (asset-manifest.md §Schema). The schema
/// covers user/mod/copied; instances do not — no loader/resolver exists without a consumer
/// (A-014).
/// </summary>
public enum AssetSource
{
    Embedded,
    Copied,
    User,
    Mod,
}

public sealed record AssetProvenance(string Origin, string License);

public sealed record AssetEntry(
    string Id,
    AssetSource Source,
    string Path,
    bool Required,
    AssetProvenance Provenance,
    string[] Heads,
    string OverridePolicy,
    string Trust);

public sealed record AssetVerificationFailure(string Id, string Path, string Reason);

/// <summary>
/// Parser/validator for <c>Assets/assets.manifest.json</c>. This class IS the schema
/// (asset-manifest.md §Schema) — deliberately no JSON Schema document or validator
/// package (no new package admissions).
/// </summary>
public static class AssetManifest
{
    public const string ManifestAssetPath = "Assets/assets.manifest.json";
    public const string AssemblyAssetUriPrefix = "avares://CcpClient.Desktop/";

    private static readonly string[] OverridePolicies = ["none", "user", "mod"];
    private static readonly string[] TrustClasses = ["full", "user", "mod"];

    public static bool TryParse(Stream json, out AssetEntry[] entries, out IReadOnlyList<string> errors)
    {
        using var document = JsonDocument.Parse(json);
        return TryParse(document.RootElement, out entries, out errors);
    }

    public static bool TryParse(string json, out AssetEntry[] entries, out IReadOnlyList<string> errors)
    {
        using var document = JsonDocument.Parse(json);
        return TryParse(document.RootElement, out entries, out errors);
    }

    private static bool TryParse(JsonElement root, out AssetEntry[] entries, out IReadOnlyList<string> errors)
    {
        var problems = new List<string>();
        entries = [];

        if (root.ValueKind != JsonValueKind.Object)
        {
            errors = ["root: must be a JSON object"];
            return false;
        }

        if (!root.TryGetProperty("version", out var version) || version.GetInt32() != 1)
        {
            problems.Add("version: must be 1 (newer-than-app catalogues are rejected, not silently read)");
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            problems.Add("assets: must be an array");
            errors = problems;
            return false;
        }

        var parsed = new List<AssetEntry>();
        var index = 0;
        foreach (var element in assets.EnumerateArray())
        {
            var where = $"assets[{index}]";
            index++;
            var id = ReadString(element, "id", where, problems);
            var sourceText = ReadString(element, "source", where, problems);
            var path = ReadString(element, "path", where, problems);
            var overridePolicy = ReadString(element, "overridePolicy", where, problems);
            var trust = ReadString(element, "trust", where, problems);

            if (id is not null && parsed.Any(e => e.Id == id))
            {
                problems.Add($"{where}.id: duplicate id '{id}'");
            }

            AssetSource source = default;
            if (sourceText is not null)
            {
                if (!Enum.TryParse(sourceText, ignoreCase: true, out source))
                {
                    problems.Add($"{where}.source: '{sourceText}' is not embedded|copied|user|mod");
                }
            }

            var required = element.TryGetProperty("required", out var req) && req.GetBoolean();

            string? origin = null;
            string? license = null;
            if (!element.TryGetProperty("provenance", out var provenance) || provenance.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}.provenance: required object with origin and license");
            }
            else
            {
                origin = ReadString(provenance, "origin", where + ".provenance", problems);
                license = ReadString(provenance, "license", where + ".provenance", problems);
            }

            var heads = ReadHeads(element, where, problems);

            if (path is not null)
            {
                ValidatePath(path, where, problems);
            }

            if (overridePolicy is not null && !OverridePolicies.Contains(overridePolicy, StringComparer.Ordinal))
            {
                problems.Add($"{where}.overridePolicy: '{overridePolicy}' is not none|user|mod");
            }

            if (trust is not null && !TrustClasses.Contains(trust, StringComparer.Ordinal))
            {
                problems.Add($"{where}.trust: '{trust}' is not full|user|mod");
            }

            // Embedded assets are shipped + verified content: full trust, never overridable,
            // and must live under the one AvaloniaResource glob (Assets/).
            if (sourceText is "embedded" && path is not null && !path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                problems.Add($"{where}.path: embedded assets live under Assets/ (the one AvaloniaResource glob)");
            }

            if (sourceText is "embedded" && trust is not null && trust != "full")
            {
                problems.Add($"{where}.trust: embedded assets are 'full' (shipped and verified)");
            }

            if (sourceText is "embedded" && overridePolicy is not null && overridePolicy != "none")
            {
                problems.Add($"{where}.overridePolicy: embedded assets are 'none' (shipped behavior is not silently replaceable)");
            }

            if (id is null || sourceText is null || path is null || origin is null || license is null
                || heads is null || overridePolicy is null || trust is null)
            {
                continue;
            }

            parsed.Add(new AssetEntry(id, source, path, required,
                new AssetProvenance(origin, license), heads, overridePolicy, trust));
        }

        entries = parsed.ToArray();
        errors = problems;
        return problems.Count == 0;
    }

    private static void ValidatePath(string path, string where, List<string> problems)
    {
        // Rooted relative paths only (asset-manifest.md §Schema): no traversal, no absolute,
        // no UNC — the same boundary the security constraints apply to file-open arguments.
        if (path.StartsWith('/') || path.StartsWith('\\'))
        {
            problems.Add($"{where}.path: '{path}' must be relative, not rooted");
        }
        else if (path.Length >= 2 && path[1] == ':')
        {
            problems.Add($"{where}.path: '{path}' must be relative, not drive-absolute");
        }
        else if (path.Split('/').Any(segment => segment is ".."))
        {
            problems.Add($"{where}.path: '{path}' must not contain '..'");
        }
        else if (path.Contains('\\', StringComparison.Ordinal))
        {
            problems.Add($"{where}.path: '{path}' uses backslashes; manifest paths are '/'-separated on every platform");
        }
    }

    private static string? ReadString(JsonElement element, string property, string where, List<string> problems)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            problems.Add($"{where}.{property}: required non-empty string");
            return null;
        }

        return value.GetString()!;
    }

    private static string[]? ReadHeads(JsonElement element, string where, List<string> problems)
    {
        if (!element.TryGetProperty("heads", out var value) || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() == 0
            || value.EnumerateArray().Any(h => h.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(h.GetString())))
        {
            problems.Add($"{where}.heads: required non-empty string array");
            return null;
        }

        return value.EnumerateArray().Select(h => h.GetString()!).ToArray();
    }
}

/// <summary>
/// Two-direction verification (asset-manifest.md §Two-direction validation rule).
/// STREAM-ONLY: avares:// opens via <see cref="StandardAssetLoader"/> on the given assembly —
/// never <c>Assembly.Location</c>, never <c>AppContext.BaseDirectory</c>. Single-file publish
/// returns an empty Assembly.Location and loads bundled assemblies from memory while embedded
/// resources keep working, so this verifier runs unchanged against the row-9 published
/// artifact (the row-9 hook is one invocation, zero new test logic).
/// </summary>
public static class AssetVerifier
{
    public static IReadOnlyList<AssetVerificationFailure> Verify(IReadOnlyList<AssetEntry> entries, Assembly assembly)
    {
        var loader = new StandardAssetLoader(assembly);
        var failures = new List<AssetVerificationFailure>();

        // Forward direction (manifest -> binary): every required embedded entry opens.
        foreach (var entry in entries.Where(e => e is { Source: AssetSource.Embedded, Required: true }))
        {
            try
            {
                using var stream = loader.Open(new Uri(AssetManifest.AssemblyAssetUriPrefix + entry.Path));
            }
            catch (Exception ex)
            {
                failures.Add(new AssetVerificationFailure(entry.Id, entry.Path, $"open-failed: {ex.Message}"));
            }
        }

        // Completeness sweep (binary -> manifest): enumerate every embedded asset at the
        // assembly ROOT — GetAssets filters the bundle index by ordinal prefix and returns
        // the exact embedded case (verified against the 12.1.0 source). Rooting at the
        // assembly root, not at Assets/, catches any future AvaloniaResource glob outside
        // Assets/. Match ordinal first, then case-insensitively to NAME case drift.
        var unmatchedActual = loader.GetAssets(new Uri(AssetManifest.AssemblyAssetUriPrefix), null)
            .Select(u => u.AbsolutePath.TrimStart('/'))
            .Where(p => !IsCompilerOwnedBundleEntry(p))
            .ToList();

        foreach (var entry in entries.Where(e => e.Source == AssetSource.Embedded))
        {
            var exact = unmatchedActual.IndexOf(entry.Path);
            if (exact >= 0)
            {
                unmatchedActual.RemoveAt(exact);
                continue;
            }

            var driftIndex = unmatchedActual.FindIndex(
                a => string.Equals(a, entry.Path, StringComparison.OrdinalIgnoreCase));
            if (driftIndex >= 0)
            {
                failures.Add(new AssetVerificationFailure(entry.Id, entry.Path,
                    $"case-mismatch: manifest '{entry.Path}' vs embedded '{unmatchedActual[driftIndex]}'"));
                unmatchedActual.RemoveAt(driftIndex);
            }
            // No match at all: open-failed was already reported for required entries;
            // an absent OPTIONAL entry is not a failure (explicit optionality only).
        }

        foreach (var actual in unmatchedActual)
        {
            failures.Add(new AssetVerificationFailure(actual, actual, "unmanifested-embedded-asset"));
        }

        return failures;
    }

    /// <summary>
    /// Bundle entries whose final segment starts with '!' are Avalonia compiler-owned
    /// metadata (observed 2026-07-19 on 12.1.0: <c>!AvaloniaResourceXamlInfo</c>, the
    /// compiled-XAML ClassToResourcePathIndex; the bundle itself is <c>!AvaloniaResources</c>).
    /// They are build machinery, never product assets, and are excluded from the sweep by
    /// this NAMED rule (asset-manifest.md §Two-direction validation rule).
    /// </summary>
    public static bool IsCompilerOwnedBundleEntry(string path) =>
        path.Split('/').Last().StartsWith('!');
}

/// <summary>
/// The <c>--verify-assets</c> diagnostic self-check (asset-manifest.md §--verify-assets
/// self-check contract): a bounded path that runs BEFORE the startup phases — no window,
/// no lifetime, no participants (SP-003 phase discipline: nothing starts that asset
/// opening does not need). Opens the embedded manifest and every required embedded asset
/// through the same avares:// mechanism the app uses, prints one diagnostic line per
/// failure, exit 0 on full pass, non-zero otherwise.
/// </summary>
public static class AssetSelfCheck
{
    public const string Flag = "--verify-assets";

    public static int Run(Assembly assembly, TextWriter output)
    {
        AssetEntry[] entries;
        try
        {
            var loader = new StandardAssetLoader(assembly);
            using var manifestStream = loader.Open(
                new Uri(AssetManifest.AssemblyAssetUriPrefix + AssetManifest.ManifestAssetPath));
            if (!AssetManifest.TryParse(manifestStream, out entries, out var errors))
            {
                foreach (var error in errors)
                {
                    output.WriteLine(
                        $"asset FAIL asset.manifest {AssetManifest.ManifestAssetPath}: manifest-invalid: {error}");
                }

                output.WriteLine("verify-assets: FAIL (manifest invalid)");
                return 1;
            }
        }
        catch (Exception ex)
        {
            output.WriteLine(
                $"asset FAIL asset.manifest {AssetManifest.ManifestAssetPath}: open-failed: {ex.Message}");
            output.WriteLine("verify-assets: FAIL (manifest unreadable)");
            return 1;
        }

        var failures = AssetVerifier.Verify(entries, assembly);
        foreach (var entry in entries.Where(e => e is { Source: AssetSource.Embedded, Required: true }))
        {
            if (failures.Any(f => f.Id == entry.Id && f.Path == entry.Path))
            {
                continue;
            }

            output.WriteLine($"asset OK {entry.Id} {entry.Path}");
        }

        foreach (var failure in failures)
        {
            output.WriteLine($"asset FAIL {failure.Id} {failure.Path}: {failure.Reason}");
        }

        output.WriteLine(failures.Count == 0
            ? $"verify-assets: PASS ({entries.Length} manifest entries, all required embedded assets open)"
            : $"verify-assets: FAIL ({failures.Count} failure(s))");
        return failures.Count == 0 ? 0 : 1;
    }
}
