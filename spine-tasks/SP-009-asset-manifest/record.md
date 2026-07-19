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

## Design decisions

- **JSON catalogue over typed registry** (packet pin): readable by pack/verify tooling, SP-008 checks.json pattern. The catalogue is embedded via the same `Assets\**` glob it catalogues and **self-lists** (consult Q3) — the sweep covers it, so it must be declared.
- **Sweep roots at the assembly root** `avares://CcpClient.Desktop/` (consult Q2 option (a), verified viable): `GetAssets` with path `/` matches every bundle entry (keys are rooted). Any future `<AvaloniaResource>` glob outside `Assets/` fails the sweep.
- **Compiler-owned `!`-prefix exclusion, rule from observation (consult Q2):** the first root sweep FAILED on `!AvaloniaResourceXamlInfo` — Avalonia.Build.Tasks packs compiled-XAML metadata (`ClassToResourcePathIndex`, confirmed in `src/Markup/Avalonia.Markup.Xaml/PortableXaml/AvaloniaResourceXamlInfo.cs` @ 12.1.0) into the bundle. The `.axaml`-not-in-bundle prediction was half-wrong: per-axaml resources do NOT appear, but one compiler-owned metadata entry DOES. The rule: final path segment starting with `!` = compiler-owned (same marker as `!AvaloniaResources` itself), excluded; every non-`!` asset must be manifest-listed. Stated in asset-manifest.md §Two-direction validation rule.
- **Open is already case-sensitive on every platform** (research): the bundle index dictionary uses the default ordinal comparer, so `Open` fails on case drift even on Windows. The named ordinal check (manifest path vs `GetAssets`-enumerated case) remains the explicit assertion and is the check that will matter for future COPIED assets, where the filesystem (NTFS vs ext4) decides case behavior.
- **Verifier is stream-only** (consult pin 3): no `Assembly.Location`, no `AppContext.BaseDirectory`, no output-relative paths — the row-9 hook stays "one invocation, zero new test logic" under single-file publish.
- **No JSON Schema library** (consult pin 2): `AssetManifest.TryParse` IS the schema; schema-validation tests drive synthetic user/mod/copied/invalid documents through it.
- **Copied direction assert-empty** (consult pin 4): `CopiedDirection_ManifestDeclaresNoCopiedEntries` — no output-directory convention without a consumer (A-014).
- **`StandardAssetLoader` despite `[Unstable]`** (consult Q1): the static `AssetLoader` needs `AvaloniaLocator` (app init), unavailable pre-lifetime; `StandardAssetLoader(assembly)` is confirmed by the 12.1.0 source to need no locator state; SP-007 landed the same pattern; 0 warnings. Recorded in asset-manifest.md.
- **Self-check output channel:** `Console.Out`. WinExe has no attached console, but shell redirection delivers stdout on Windows (verified: `CcpClient.Desktop.exe --verify-assets > file` captures all lines); Linux apphost stdout works normally. Exit code is the primary contract; diagnostic lines are for humans/CI logs.

## Step 2/3 evidence summary

- Solution build 0W/0E; CcpClient.Tests **115/115** (94 landed + 21 new) on Windows; same 115/115 + HeadlessTests 3/3 on WSL2 (below).
- Negative tests prove both sweep directions and the named case check: synthetic unmanifested asset → `unmanifested-embedded-asset` naming the path; synthetic case drift → `case-mismatch: manifest 'Assets/DEMO-status-ticker.png' vs embedded 'Assets/demo-status-ticker.png'`; synthetic missing path → `open-failed:`.
- Real-binary sanity (Windows Debug): exit 0, per-asset OK lines + PASS summary (full transcripts below).

## Step 4 — Debug+Release output runs, WSL2 gate, budgets

### Windows (SDK 10.0.302)
- Debug build 0W/0E; `bin/Debug/net10.0/CcpClient.Desktop.exe --verify-assets` → **exit 0**:
  `asset OK demo.status-ticker.icon Assets/demo-status-ticker.png` / `asset OK asset.manifest Assets/assets.manifest.json` / `verify-assets: PASS (2 manifest entries, all required embedded assets open)`
- Release build 0W/0E; same invocation against `bin/Release/net10.0/` → **exit 0**, identical output.

### WSL2 gate (Ubuntu 26.04, SDK 10.0.110, native `~/ccp-sp009` copy, never /mnt/e)
- Full contract testCommand green: solution build 0W/0E; CcpClient.Tests **115/115**; CcpClient.HeadlessTests **3/3**.
- `bin/Debug/net10.0/CcpClient.Desktop --verify-assets` → **exit 0** (identical 3-line output); `bin/Release/net10.0/CcpClient.Desktop --verify-assets` → **exit 0** (identical).
- Case-exactness on ext4: the ordinal sweep + case-mismatch negative tests run in the 115 on ext4 (embedded case is filesystem-independent by bundle design — recorded in the contract; the ext4-meaningful case is the future copied direction, stated).
- Session facts: `WAYLAND_DISPLAY=wayland-0`, `DISPLAY=:0`, `XDG_SESSION_TYPE` empty (WSLg Wayland session with X11 via XWayland — X11 facts only, no Wayland claim, §5.1 owner question unchanged); kernel 6.6.114.1-microsoft-standard-WSL2 x86_64.

### Measured budgets (cold precondition verified: all bin/obj deleted, zero remaining confirmed)
Recorded in asset-manifest.md §Measured budgets. Windows: cold build 2.4 s; validation tests cold 4.0 s / incremental 2.3 s; self-check 0.24 s first-run / 0.09 s warm (Release 0.31 s after rebuild). WSL2: cold build 9.5 s; validation tests cold 4.9 s / incremental 3.6 s; self-check Debug 0.10 s / warm 0.14 s / Release 0.086 s.

### Harness integration decision
**The self-check invocation is NOT added to `verification-harness.md`; it stays in `asset-manifest.md`.** Justification: the harness's tier 1 is explicitly "never launches the app" and tiers 2–3 are headed capture/pixel-assertion machinery; the `--verify-assets` mode launches the binary (though never the lifetime), so it fits no tier. Its natural gate is row 9's release/publish lane (same invocation against the published artifact), which the harness doc does not own. Adding it to tier 1 would silently redefine the tier's scope.

## Surprises

1. **`!AvaloniaResourceXamlInfo` IS in the bundle** (see design decisions) — the consult's ".axaml exclusion is probably moot" was right about per-axaml resources but the compiler still adds one metadata entry; the first sweep run caught it immediately, which is the sweep working as designed.
2. **WinExe stdout redirection works** — a GUI-subsystem exe has no console, but `> file` redirection delivers `Console.Out` anyway; the self-check's diagnostic lines are capturable on Windows without native interop (AttachConsole was the alternative and is forbidden).
3. **A deleted-for-cold-measurement Release binary bit once** — after the bin/obj clean, a Release run returned 127 (file gone). Rebuild-then-run is the order; recorded so future budget passes don't misread 127 as a product failure.

## Follow-ups for the orchestrator

- port-lessons candidates (durable surprises; SP-007/SP-008 precedent — left here for the port-lessons owner row, not appended): (1) `!AvaloniaResourceXamlInfo` compiler metadata rides in the `!AvaloniaResources` bundle — any root-level bundle sweep must expect `!`-prefixed root entries; (2) WinExe stdout redirection works without a console (`> file` captures `Console.Out`) — diagnostic flags on the real binary need no AttachConsole interop; (3) budget passes must rebuild before running a configuration whose bin/obj was cleaned (127 = file gone, not a product failure).
- Row 9 (Release/publish gates) owns the publish-mode invocation of `--verify-assets` against the published artifact — the hook is ready, zero new test logic needed.

## Pre-completion consult (solo Fable 5, 2026-07-19)

Full as-built summary + diff submitted. Verdict received complete: **proceed to land after ONE small tightening (the `!` rule — a real but tiny drift hole). No rework of evidence, schema, or tests otherwise. Deviations (a) double-open and (b) ordinal-bundle-index finding accepted as recorded.**

1. **`!`-prefix rule tightened (APPLIED pre-land):** the first implementation excluded a `!`-prefixed FINAL SEGMENT anywhere (`path.Split('/').Last().StartsWith('!')`), but the observed evidence covers only bundle-ROOT entries — a future `Assets/!notes.png` would have been silently filtered (exactly the unmanifested-asset hole the sweep exists to close). Fixed to root-level only (`path.StartsWith('!') && !path.Contains('/')`) in code + contract doc; full contract testCommand re-run green after the fix (115/115 + 3/3, 0W/0E). A hypothetical compiler entry under a subdirectory now fails loudly — the correct failure mode.
2. **Board-row evidence (APPLIED):** row → WIP citing record.md, publish-mode named as row 9's deferred gate (annotate, never rewrite — the acceptance's "publish tests" third stands). The two required additions are in the row text: (1) "Schema covers user/mod/copied/override/trust; instances do NOT — no mod loader, no override resolution, no localization entries (no consumer, A-014)" (schema fields must not read as implemented capability — the assets-present-means-supported failure class); (2) named numbers (Debug+Release × Windows+WSL2 exit 0, 115/115 + 3/3 both platforms, two-direction + ordinal case-exact tests with negative proofs).
3. **Q2 — no over-build, no under-proof:** TryParse-as-schema kept the validator boundary tight; copied assert-empty resisted the invented-convention trap; the stream-only constraint is the entire row-9 surface; embedded-policy consistency checks (embedded ⇒ full/none/Assets-rooted) are cheap and load-bearing; all three failure vocabularies demonstrated by negative tests — drift protection demonstrated, not asserted.
4. **Deviation (b) endorsed:** the source-verified ordinal bundle index (case drift fails on EVERY platform for embedded assets) STRENGTHENS the row over the pre-approach framing; the contract correctly relocates the ext4 hazard to the future copied direction.

## Engine reviews

- Step 1 plan review: `spine_review_step` → **skipped=true, reviewLevel=0, spawnFailed=false** (ninth consecutive batch with zero engine reviews; T-2 remains open). Fable solo consults are the active quality gate per the packet.
- Step 2 plan review: **skipped=true** (T-2).
- Step 3 plan review: **skipped=true** (T-2).
- Step 4 plan review: **skipped=true** (T-2).
- Step 5: plan review **skipped=true** (T-2) — 5/5 calls skipped this batch.
