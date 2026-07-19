# SP-010 — establish Release and publish gates: record

**Task:** task-board row 9 (P0, final Phase 1 row). **Worker:** kimi-coding/k3. **Date:** 2026-07-19.

---

## Pre-approach consult (solo Fable 5, 2026-07-19)

Planned publish strategy, version-authority design, evidence matrix, and WSLg debt-discharge plan submitted (4 pointed questions). First response answered Q1–Q3 and **truncated before Q4**; a compact follow-up retrieved Q4. Verdict received complete after the follow-up: **plan is sound — proceed, with the sharpenings below (all applied to the contract doc and scripts).**

### Q1 — publish shape (self-contained single-file per RID)

- **`IncludeNativeLibrariesForSelfExtract` default is false** → with plain `PublishSingleFile=true` the native libs (SkiaSharp/HarfBuzz) ship **beside** the exe; setting it true bundles them and they **extract to a temp dir on first run** (`DOTNET_BUNDLE_EXTRACT_BASE_DIR` controls location; per-bundle-hash subdirectories; fails on noexec/read-only mounts on Linux where `dlopen` is blocked). **Whether "single-file" means natives-bundled-and-extracted or natives-left-beside is a product decision that must be documented** — recorded in the contract with the observed evidence.
- **`SourceRevisionId`:** since .NET 8 the SDK appends the git SHA to `InformationalVersion` when source-control info is available — the derivation tests must tolerate a `+<sha>` suffix (already planned; pinned as an explicit rule).
- **`ldd` on the published exe shows only the apphost's own deps** — runtime-dlopened natives (libX11, fontconfig, ICU) are INVISIBLE to it. **Honest floor = `ldd` on each shipped native lib PLUS runtime load observation** (`/proc/<pid>/maps` or equivalent). A floor from `ldd`-the-exe alone would lie.
- **ICU:** `InvariantGlobalization` is not set → `libicuuc`/`libicui18n` are hard runtime dependencies, only visible at runtime (matters for the owner's "which distributions" decision).
- **WinExe stdout redirection on the published single-file exe** must be verified in the matrix (the `--verify-assets` and `--version` diagnostic lines depend on it; SP-009 proved it for the framework-dependent build, single-file apphost must be re-proven).
- Data path: the store uses `SpecialFolder.UserProfile` + `.config` on Linux, NOT `$XDG_CONFIG_HOME` — pre-existing behavior, do not change here; the matrix records XDG expectations honestly rather than claiming compliance.

### Q2 — version authority

- Derivation rules to pin explicitly: `AssemblyVersion`/`FileVersion` derive from `Version` (numeric, 4-part); `InformationalVersion` = `Version` + optional `+<SourceRevisionId>` suffix. **Tests assert the inter-attribute derivation rules and never hardcode the version VALUE**; the artifact-name ↔ `Version` agreement is a publish-script/external check, not a unit test.
- `client/Directory.Build.props` **stops the MSBuild upward walk** (MSBuild walks toward the drive root and stops at the first props found) — insulation from any future repo-root props is a benefit. It applies to every project under `client/` (tests + tools included) — keep it minimal.

### Q3 — evidence matrix

- **Graceful-close vs kill is the right distinction:** SIGTERM/SIGKILL bypasses Avalonia's close path (exit 143, no guarded teardown, no flush); `WM_DELETE_WINDOW` on X11 / `CloseMainWindow` on Windows route through the real close pipeline. The matrix must assert **exit 0 via graceful close**, never "process gone".
- **Corrupt-settings run:** assert the **quarantine file exists with the original bytes preserved** (`settings.corrupt-*.json`) — the observable Degraded proof, not just exit 0.
- **Fresh-profile run:** the data path is fixed, so isolate by **moving/backing up the real config dir**, running, then restoring — never touch the real user profile destructively.

### Q4 — WSLg graceful close (rows 2/3 debt + SP-007 gap)

Three real silent-failure modes, all avoidable:

1. **Message shape — Avalonia silently ignores malformed variants.** Handled only when `message_type == WM_PROTOCOLS` atom, `format == 32`, `data.l[0] == WM_DELETE_WINDOW` atom. `XSendEvent` returns queuing success, not delivery — wrong atom/format/XID all "succeed" sender-side. Must `XFlush`/`XSync` after send or the event can sit in the client-side queue when python exits. **No pkill fallback on the success path.**
2. **Wrong-XID trap under WSLg RAIL reparenting.** `WM_NAME` lives on the client window so the title search usually finds the right XID — but "usually" is not evidence. **Pin it: call `XGetWMProtocols` on the candidate XID and assert it advertises `WM_DELETE_WINDOW` before sending** (proves the client window, not a frame). If the check fails, walk children for the XID owning `WM_PROTOCOLS` rather than sending blind.
3. **Discriminator:** `wait` on the real launched PID for the exit code — exit 0 is reachable only via the lifetime returning normally (Exit event → guarded teardown). Window-disappearing ≠ exit 0. **Negative control:** first send a deliberately malformed ClientMessage (wrong atom), assert the process still alive after ~2 s; then the correct one, assert exit 0 — proves the delete message, not coincidence or cleanup, caused shutdown.
4. Published binary is native: launch `./CcpClient.Desktop` directly; reuse the same wait-based exit capture for Debug/Release `dotnet` invocations so all three modes share one evidence shape.

---

## Current-docs research (avalonia-research protocol)

All fetched **2026-07-19**. Baselines: Avalonia 12.1.0, .NET SDK 10.0.302 (Windows) / 10.0.110 (WSL2).

- **(a) Single-file publish semantics** — https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview (fetched 2026-07-19). VERIFIED: only managed DLLs are bundled by default (loaded in-memory at start — embedded resources incl. `!AvaloniaResources` keep working, matching SP-009's stream-only constraint); the core runtime's native binaries are separate files beside the exe. `IncludeNativeLibrariesForSelfExtract=true` embeds natives for first-run extraction; extraction target = `DOTNET_BUNDLE_EXTRACT_BASE_DIR`, else `$HOME/.net` (Linux/macOS) / `%TEMP%/.net` (Windows); systemd `$HOME`-undefined caveat + tamper-permissions warning documented. Consult Q1 confirmed.
- **(b) API behavior under single-file** — same page, "API incompatibility" table. VERIFIED: `Assembly.Location` returns EMPTY; `Assembly.GetFile(s)` throw `IOException`; `Assembly.CodeBase` throws `PlatformNotSupportedException`; `AppContext.BaseDirectory` is the documented API for files next to the exe; `Environment.ProcessPath` for the exe path. The data path never uses either — `SpecialFolder.UserProfile` + `.config` (CompositionRoot) is unaffected by artifact mode. `--verify-assets` is stream-only (SP-009) so it is unaffected.
- **(c) Residual Linux deps** — https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual#dependencies (fetched 2026-07-19). VERIFIED: .NET runtime deps stay system-provided even for self-contained (libicu, libstdc++, openssl-libs, krb5-libs, zlib-class). Avalonia's X11/fontconfig natives are runtime-dlopened and invisible to `ldd` on the apphost (consult Q1) — the honest floor is `ldd` on every shipped `.so` + `/proc/<pid>/maps` at runtime. Empirical floor lands in Step 4.
- **(d) Avalonia 12.1 publish guidance** — https://docs.avaloniaui.net/docs/deployment + https://docs.avaloniaui.net/docs/deployment/native-aot (fetched 2026-07-19). VERIFIED: Native AOT is the documented opt-in native path and requires trimmer-root-descriptor work for reflection — consistent with the packet's `PublishTrimmed` exclusion; no Avalonia-specific single-file guidance contradicts the .NET docs. `docs/deployment/trimming` is 404 (no separate trimming page in the v12 set).
- **Version derivation** — https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props (fetched 2026-07-19). VERIFIED: `SourceRevisionId` auto-populates from the commit hash when Source Link data is present (.NET 8+); `IncludeSourceRevisionInInformationalVersion` defaults **true** → `InformationalVersion` may carry a `+<sha>` suffix. Derivation-not-equality testing pinned (contract §2/§4).

## Step 1 decisions (applied to `client/docs/release-publish-gates.md`)

- **Publish strategy:** self-contained single-file per RID (`win-x64`, `linux-x64`), `IncludeNativeLibrariesForSelfExtract` NOT set (natives beside the exe; artifact = publish directory as a unit). Revisit trigger: first real distribution consumer. Consult Q1's "document the natives decision" applied.
- **Version number: `0.1.0`** — honest greenfield pre-Milestone-1 number; deliberately NOT the WPF product's 6.2.7 (the greenfield client must not impersonate it). Recorded per the packet's "record the choice" requirement.
- **Matrix:** graceful-close-only exit-0 (no kill on the success path); corrupt-settings asserts the quarantine FILE with original bytes; fresh-profile asserts NO settings.json created (persistence §5 rule 2); data-path identity proven by moving the publish dir + identical quarantine landing path across modes; native-deps floor via per-.so ldd + runtime maps.
