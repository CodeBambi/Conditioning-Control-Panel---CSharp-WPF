# Asset and packaged-output manifest

**Status:** active deliverable of task-board row 8 (SP-009). Owner decisions applied: A-014 (schema and policy exist only with a real consumer — no mod loader, no runtime override resolution, no copied-asset convention before the first copied consumer), the Release rule (Debug, Release, and published artifacts are separate gates), and `first-attempt-systemic-lessons.md` §"Asset presence, lookup, and packaging need one manifest" (one typed catalogue; build tests open every required asset from real output, case-sensitive).

## Purpose and scope

ONE catalogue — [`client/src/CcpClient.Desktop/Assets/assets.manifest.json`](../src/CcpClient.Desktop/Assets/assets.manifest.json) — declares every asset the client ships or consumes: logical ID, source kind, case-sensitive path, optionality, provenance/license, target heads, and the override/trust policy for non-embedded sources.

**The schema covers `user`, `mod`, and `copied` sources; instances do NOT.** No mod loader, no runtime override resolution, no user-content directories, and no copied-asset output convention exist — there is no consumer (A-014). Localization entries arrive with the localization row, not here. The first slice has exactly TWO embedded entries: the one real product asset and the manifest itself (self-listing — see below).

## Catalogue placement and self-listing

The catalogue lives under `Assets/` and is embedded through the same `<AvaloniaResource Include="Assets\**" />` glob it catalogues, so the `--verify-assets` self-check reads it through the exact mechanism it validates (`avares://` open). The manifest is therefore itself an embedded asset and **lists itself as a required entry** — a completeness sweep that special-cased the manifest would be exactly the "unmanifested asset the sweep ignores" hole the sweep exists to close. Opening the manifest to parse it IS the open-test for the self-entry; the self-check does not double-open it.

## Schema

`version: 1`. "Schema" means the C# parser/validator class (`CcpClient.Desktop.Manifest.AssetManifest`) — there is no JSON Schema document or validator package (no new packages). Per-asset fields:

| Field | Type | Rule |
|---|---|---|
| `id` | string | Stable logical identity. Consumers reference IDs, never paths. Never localized text, never positional. |
| `source` | `embedded` \| `copied` \| `user` \| `mod` | `embedded` = compiled into the binary via `<AvaloniaResource>`; `copied` = file copied to output next to the binary; `user` = user-supplied content; `mod` = third-party mod content. |
| `path` | string | **Case-sensitive.** For `embedded`: project-relative under `Assets/`, mapping to `avares://CcpClient.Desktop/<path>`. For `copied`: output-relative (convention defined by the first copied consumer). For `user`/`mod`: search-root-relative (policy only, unresolved today). Rooted relative paths only — no `..`, no UNC, no drive-absolute paths. |
| `required` | bool | A required asset must open in every packaged-output self-check; its absence fails the check. Optional assets may be absent without failure (explicit optionality only — the capability contract's fallback rule). |
| `provenance` | `{ origin, license }` | Where the asset came from and its license. Both required, free text, never invented. |
| `heads` | string[] | Target heads. The greenfield client has ONE head (`desktop`) covering Windows and Linux; the field exists so future per-head or per-platform assets declare their scope. |
| `overridePolicy` | `none` \| `user` \| `mod` | Which non-embedded source may override this asset's logical ID. `none` for embedded shipped assets. Meaningful only once a resolver exists (stated policy, unimplemented). |
| `trust` | `full` \| `user` \| `mod` | Trust class of the content (below). |

## Override policy and trust rule (STATED POLICY — unimplemented, no consumer)

- Override resolution, when a consumer exists, is by **logical ID**: a `user` or `mod` source may supply content for an ID only when that ID's `overridePolicy` names its source class. Embedded required assets default to `overridePolicy: none` — shipped behavior cannot be silently replaced.
- Trust classes: `full` = embedded, shipped, verified by this manifest's self-check. `user` = user-supplied, **untrusted input**: decode-validated at load, size-capped, never executed. `mod` = third-party, **untrusted input** with the same decode validation plus the mod row's admission contract. Non-embedded content is never trusted by location or extension.
- No resolver, loader, or directory scan implements any of this today. The schema fields exist so the first consumer's row validates policy-shaped entries instead of redesigning the catalogue.

## Two-direction validation rule

Both directions run as unit tests (`CcpClient.Tests`) against the real built assembly:

1. **Forward (manifest → binary):** every required `embedded` entry opens as a stream via `StandardAssetLoader` on the entry assembly (`avares://CcpClient.Desktop/<path>`). `StandardAssetLoader(assembly)` needs no Avalonia app/platform initialization (verified against the 12.1.0 source; SP-007 landed the same standalone pattern). The class is `[Unstable]` — recorded; the static `AssetLoader` requires `AvaloniaLocator` (app init) and is unusable pre-lifetime, so the pinned-baseline `StandardAssetLoader` is the honest mechanism here.
2. **Completeness sweep (binary → manifest):** enumerate every embedded asset at the assembly ROOT (`StandardAssetLoader.GetAssets(new Uri("avares://CcpClient.Desktop/"), null)` — ordinal prefix filter, exact embedded case preserved, verified 12.1.0) and FAIL on any embedded asset with no manifest entry. Drift protection: an asset added to `Assets/` without a manifest entry breaks the build's test suite. **Any future `<AvaloniaResource>` glob outside `Assets/` is caught by the same root sweep** — rooting at the assembly root, not at `Assets/`, is deliberate.
3. **Case-exactness (named check):** for every `embedded` entry the manifest `path` must match an enumerated asset's path **ordinal case-exactly** (`StringComparison.Ordinal`), not merely open successfully. Avalonia packs assets into the `!AvaloniaResources` bundle with an ordinal case-sensitive index, so `Open` already fails on case drift on every platform — the named check pins the manifest text to the embedded case explicitly, and it is the check that will matter for future `copied` assets where the filesystem (ext4 vs NTFS) decides case behavior. Works-on-Windows/breaks-on-ext4 is the drift this row exists to prevent.
4. **Copied direction (assert-empty):** the set of manifest entries with `source: copied` must be empty (none exist; no output-directory convention is invented without a consumer — A-014). The first copied consumer's row defines the convention and extends this direction to real file existence checks.

Schema-validation tests deserialize synthetic `user`/`mod`/`copied` entries through the parser class and assert field-level acceptance/rejection. Loading those sources is unimplemented and recorded as such.

## `--verify-assets` self-check contract

The real binary carries a diagnostic mode:

```
CcpClient.Desktop --verify-assets
```

- Runs as a **bounded path at the top of `Main`, before the startup phases** — no window, no lifetime, no participants (SP-003 phase discipline: nothing starts that asset opening does not need).
- Reads the embedded manifest through `StandardAssetLoader` (the parse IS the self-entry open), then opens every required `embedded` entry through the same `avares://` mechanism.
- Prints **one diagnostic line per failure** (`asset FAIL <id> <path>: <reason>`); a success line per opened asset plus a summary goes to stdout. Exit `0` when the manifest parses and every required asset opens; non-zero otherwise (unreadable/invalid manifest = non-zero).
- **Stream-only constraint (row-9 integrity):** the verifier never touches `Assembly.Location`, `AppContext.BaseDirectory`, or output-relative paths for embedded assets — `avares://` stream opens only. Single-file publish returns an empty `Assembly.Location` and loads bundled assemblies from memory; embedded resources keep working. This constraint is what makes row 9 "one new invocation" instead of "rewrite the verifier".
- The self-check never weakens startup invariants: the normal path (no flag) is byte-identical to before; teardown, phases, and no-constructor-started-work are untouched.

## Row-9 publish hook (deferred gate — named, never rewritten)

Publish strategy is task-board row 9's open decision; this row runs NO publish (a throwaway publish against a mode row 9 may reject is misleading evidence — the Wayland-gate class). The hook: row 9 runs **the same `--verify-assets` invocation against the published artifact** (single-file, trimmed, or whatever shape it selects) on Windows and Linux. Zero new test logic; the stream-only constraint above is the entire compatibility surface. Publish-mode asset evidence is row 9's named gate and is explicitly NOT claimed here.

## Evidence classes (this row)

- Claimed here: Debug AND Release build-output runs of `--verify-assets` on Windows AND WSL2 Linux, plus the two-direction unit tests on both platforms. The case-exactness check is meaningful on ext4 for the copied-asset future; on embedded assets the ordinal bundle index already enforces it cross-platform (verified, 12.1.0 source).
- Deferred: published-artifact runs (row 9, hook above); headed/rendered asset display (SP-007 already landed the rendered `avares://` Image evidence; not re-claimed here).

## Measured budgets

(filled by SP-009 Step 4 — cold/incremental, Windows + WSL2; cold precondition verified by actually deleting `bin/obj`, SP-008 surprise #5)

## Research citations

All fetched 2026-07-19, baseline Avalonia 12.1.0:

- `StandardAssetLoader.cs` @ tag 12.1.0 — ctor takes the assembly, no locator/app init; `GetAssets` = ordinal prefix filter over the bundle index returning exact-case `avares://` URIs: https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.0/src/Avalonia.Base/Platform/StandardAssetLoader.cs
- `AssemblyDescriptor.cs` @ tag 12.1.0 — assets packed into one `!AvaloniaResources` embedded resource; index dictionary is ordinal case-sensitive: https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.0/src/Avalonia.Base/Platform/Internal/AssemblyDescriptor.cs
- Official assets guide (csproj `<AvaloniaResource>` mechanics): https://docs.avaloniaui.net/docs/basics/user-interface/assets
- Single-file deployment (`Assembly.Location` empty, in-memory bundled assemblies, embedded resources unaffected): https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
