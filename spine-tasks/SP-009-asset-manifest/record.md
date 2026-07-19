# SP-009 — define asset and packaged-output manifest: record

**Task:** task-board row 8 (P0). **Worker:** kimi-coding/k3. **Date:** 2026-07-19.

---

## Pre-approach consult (solo Fable 5, 2026-07-19)

Full planned design submitted (JSON catalogue with manifest self-listing; two-direction validation; case-exact ordinal check; schema-covers-policy-not-instances; bounded `--verify-assets` path before startup phases; row-9 publish hook). First response **truncated mid-Q1**; a compact follow-up retrieved the remainder. Verdict received complete after the follow-up: **design is sound — proceed, with one factual correction and four scope pins (all applied below).**

**CORRECTION — enumeration mechanism:** `Assembly.GetManifestResourceNames()` will almost certainly NOT list individual assets — Avalonia packs all `<AvaloniaResource>` items into a single embedded bundle (`!AvaloniaResources` with an internal index). Enumerate through Avalonia's own API: `StandardAssetLoader.GetAssets(new Uri("avares://CcpClient.Desktop/Assets/"), null)` — public API, returns the exact embedded-case URIs (what the ordinal check needs), same-mechanism honesty. Verify `GetAssets` signature/behavior in the 12.1 research — the single most load-bearing research item.

**Q1 — StandardAssetLoader without AppBuilder is honest.** Asset opening is embedded-resource stream work; no windowing/rendering init needed; SP-007's unit test already proved the exact URI form standalone, and SP-007's headed evidence (rendered `avares://` Image in the real app) covers the in-lifetime context. Do NOT move the check inside the lifetime — phases/window for zero evidence gain, and an SP-003 discipline violation. Verify in research that 12.1's `StandardAssetLoader` ctor takes the assembly and needs no `AvaloniaLocator` state.

**Q2 — the .axaml exclusion is probably moot; the real drift hole is elsewhere.** The csproj's only `AvaloniaResource` glob is `Assets\**`; `.axaml` files are compiled to IL by Avalonia.Build.Tasks, not shipped as avares resources. The sweep likely finds nothing to exclude — verify empirically and write the rule from observation. Real drift hole: a sweep rooted at `Assets/` silently misses any FUTURE second `AvaloniaResource` glob outside `Assets/`. Close it: (a) enumerate `GetAssets` at the assembly root `avares://CcpClient.Desktop/` and fail on anything unmanifested (check root enumeration in research), or (b) if root enumeration fails, keep `Assets/` + one stated contract rule (weaker). Prefer (a).

**Q3 — self-listing, no special-case.** A sweep special-case is exactly the hole the sweep exists to close. The manifest is a real embedded asset; it gets a real entry (embedded, required, provenance = authored in-repo). `--verify-assets` opening the manifest to parse it IS the open-test for the self-entry — say so in the contract doc rather than double-opening.

**Q4 — one over-build risk, one row-9 gap:**
- Over-build: "schema" must NOT grow into a JSON Schema document + validator library (no-new-packages). Schema = the C# parser/validator class in CcpClient.Desktop; schema-validation tests deserialize synthetic user/mod/copied entries through THAT class and assert field-level acceptance/rejection.
- Row-9 gap: single-file publish makes `Assembly.Location` empty and changes on-disk layout. The verifier survives ONLY if it is pure-stream (avares:// opens; no `Assembly.Location`, no `AppContext.BaseDirectory`-relative paths for embedded entries). Pin as a stated constraint in code comments + the contract's row-9 hook section — the difference between row 9 being "one new invocation" and "rewrite the verifier".

**Four scope pins (all applied):**
1. Enumeration via `GetAssets`, not `GetManifestResourceNames` — verify in 12.1 research first.
2. No JSON Schema library — parser-class-based validation only.
3. Verifier is stream-only — no `Assembly.Location`, no output-dir-relative resolution for embedded assets (single-file-publish safety).
4. Copied-source sweep: assert-empty, don't invent — zero copied assets exist; no output-directory asset convention with no consumer (A-014). The copied-direction test asserts the manifest declares no copied entries; the directory convention arrives with the first copied consumer.

**Blocking unknown named by the advisor:** `GetAssets` behavior in 12.1 — resolve before writing the sweep test; if it cannot enumerate at all (unlikely), re-consult before improvising a bundle-format reader.

## v12 AssetLoader research (avalonia-research protocol)

All sources fetched 2026-07-19. Baseline: Avalonia **12.1.0** (project-pinned). Primary evidence = source at the exact release tag.

- https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.0/src/Avalonia.Base/Platform/StandardAssetLoader.cs — VERIFIED:
  - `public StandardAssetLoader(Assembly? assembly = null)` — ctor takes the assembly directly (`new AssemblyDescriptorResolver()`); **no `AvaloniaLocator`, no app/platform initialization required**. SP-007's standalone unit-test open is confirmed by source. (Class is `[Unstable("…use AssetLoader static class instead")]` — recorded; the static `AssetLoader` resolves `IAssetLoader` via `AvaloniaLocator.Current.GetRequiredService` and therefore needs app init, which the pre-lifetime self-check cannot have. `StandardAssetLoader` is the only honest choice for the self-check; pinned baseline, same usage as the landed SP-007 test; builds with 0 warnings.)
  - `GetAssets(Uri uri, Uri? baseUri)` — filters `AvaloniaResources` keys by **ordinal** `StartsWith(path)` and returns `new Uri($"avares://{assembly.Name}{x.Key}")` — the **exact embedded case** is preserved in the returned URIs. Root enumeration (`avares://CcpClient.Desktop/` → path `/`, every key is rooted `/…`) returns ALL assets in the bundle — consult option (a) is viable, the sweep roots at the assembly root, not at `Assets/`.
- https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.0/src/Avalonia.Base/Platform/Internal/AssemblyDescriptor.cs — VERIFIED:
  - `<AvaloniaResource>` items are packed into ONE embedded resource `!AvaloniaResources` (int32 index length + index + concatenated bytes). `Assembly.GetManifestResourceNames()` lists only the bundle name, never individual assets — the consult correction is confirmed by source.
  - `AvaloniaResources = index.ToDictionary(GetPathRooted, …)` — **default string comparer = ordinal case-sensitive**. `Open`/`Exists` therefore already fail on case-drifted paths on EVERY platform (in-memory dictionary, not the filesystem). The named ordinal sweep-check remains the explicit assertion; the real works-on-Windows/breaks-on-ext4 hazard lands on future COPIED assets (filesystem case-sensitivity), stated in the contract.
- https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.0/src/Avalonia.Base/Platform/AssetLoader.cs + IAssetLoader.cs — VERIFIED: static `AssetLoader` needs `AvaloniaLocator` (app init); `IAssetLoader.GetAssets(Uri, Uri?)` signature confirmed public surface.
- https://docs.avaloniaui.net/docs/basics/user-interface/assets (official v12 doc set, fetched 2026-07-19) — VERIFIED: assets are included via `<AvaloniaResource Include="Assets\**" />` in the csproj (the greenfield csproj already does exactly this).
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview (fetched 2026-07-19) — VERIFIED (row-9 hook research, NOT implementation): single-file bundles managed DLLs loaded **in memory** (no extraction by default) — embedded resources incl. the `!AvaloniaResources` bundle keep working through `GetManifestResourceStream`. `Assembly.Location` returns **empty**, `Assembly.GetFile(s)` throw; `AppContext.BaseDirectory` reaches files next to the exe. Consequence (consult pin 3): the verifier is stream-only — no `Assembly.Location`, no output-dir-relative resolution for embedded assets — so the row-9 invocation is literally the same command against the published artifact.
- Open question from consult Q2 answered by source: `.axaml` files are compiled to IL by Avalonia.Build.Tasks, not packed into `!AvaloniaResources` — the sweep's expected content is exactly the `Assets/**` glob; verified empirically in Step 2 (rule written from observation).
