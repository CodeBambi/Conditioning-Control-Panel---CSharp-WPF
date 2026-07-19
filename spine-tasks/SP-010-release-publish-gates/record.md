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

## Step 2 evidence

- `client/Directory.Build.props` created with single `<Version>0.1.0</Version>`; `dotnet msbuild -getProperty:Version` against the app csproj returns `0.1.0` (authority flows through the SDK).
- `VersionSelfCheck` reads `AssemblyInformationalVersionAttribute` via reflection; `--version` flag wired next to `--verify-assets` at the top of `Main`. Real binary: `version: 0.1.0+304f8e66ff21995be4e295d76efe8c50da607fb6` — the `+<SourceRevisionId>` suffix is LIVE, confirming derivation-not-equality was the correct test design.
- Publish scripts (`client/tools/publish/publish.ps1|.sh`) derive the artifact name from the authority; empty Version = hard failure.
- `VersionDerivationTests` (3 tests): informational-exists+prefix-derives, AssemblyVersion==FileVersion numerically, self-check exit 0 + exact line. Full suite **118/118** (115 landed + 3 new), 0W/0E.
- `git check-ignore` audit: `client/tools/publish/` ignored by root `.gitignore` line 167 `tools/` (SP-008 trap) — force-added the two scripts, staged list audited (no bin/obj swept). `client/artifacts/publish/` ignored by line 94 `publish/` — correct (outputs, not source).
- Engine review Step 2: `spine_review_step` → **skipped=true** (T-2).

## Step 3 evidence — Windows artifact matrix

**Publish (win-x64):** `pwsh client/tools/publish/publish.ps1` → `CcpClient.Desktop-0.1.0-win-x64/` — apphost 82.8 MB + native sidecars `av_libglesv2.dll` 5.4 MB / `libHarfBuzzSharp.dll` 1.8 MB / `libSkiaSharp.dll` 11.6 MB (+PDBs). Matches the researched SDK-default layout (natives beside the exe, managed bundled in-memory).

**Matrix (`pwsh client/tools/publish/matrix.ps1`), 2026-07-19 — MATRIX PASS (windows):**

- Gate 2 `--verify-assets`: PASS exit 0 on **debug, release, published** — **row-8 publish third DISCHARGED (Windows)**.
- Gate 3 `--version` prefix == authority 0.1.0: PASS all three (debug `+304f8e66…`, release/published `+95b9257c…` — suffix differs by build commit as designed).
- Gate 4 fresh-profile headed: PASS all three — graceful exit 0 via `CloseMainWindow`, layout-probe observed (`card 488.0x77.0 DIP @ scale 1`), **no settings.json created**.
- Gate 5 corrupt-settings: PASS all three — graceful exit 0, `settings.corrupt-*.json` preserved the exact seeded garbage bytes (`07B 07B 00 FF 41`); the typed-Degraded stderr seam line observed (`persistence: settings file unreadable; preserved at … before falling back to flagged defaults`) — quarantine is observable, never silent.
- Gate 6 data-path identity: PASS — identical `C:\Users\Micha\AppData\Roaming\CcpClient` across all three modes; the published mode ran from a MOVED copy at `%TEMP%\ccp-sp010-portable` (location independence proven).
- Gate 7 logs-absence: PASS all three — no `*.log` beside artifact or in config dir.
- Gate 8 native-deps floor (published): PASS — sidecars recorded above; research expectation vs observed reality match exactly.
- First matrix run caught two SCRIPT bugs (not product): Release binary not pre-built (publish uses the RID-scoped intermediate dir — script now fails with the exact build command) and gate 6 compared timestamped quarantine filenames instead of the directory (fixed to compare `DirectoryName`). Re-run: full PASS.
- Engine review Step 3: `spine_review_step` → skipped=true (T-2).

## Step 4 evidence — WSL2 gate: Linux matrix + WSLg headed smoke

**Environment:** WSL2 Ubuntu 26.04, SDK 10.0.110, kernel 6.6.114.1-microsoft-standard-WSL2 x86_64; native ext4 copy `~/ccp-sp010` (rsync from /mnt/e, bin/obj/artifacts excluded — never built on /mnt/e). Session facts: `WAYLAND_DISPLAY=wayland-0`, `DISPLAY=:0` (WSLg; X11 via XWayland — X11 facts only, no Wayland claim, §5.1 owner question unchanged).

**Contract testCommand on WSL2:** solution build 0W/0E; **CcpClient.Tests 118/118; CcpClient.HeadlessTests 3/3.**

**Publish (linux-x64):** `./client/tools/publish/publish.sh` → `CcpClient.Desktop-0.1.0-linux-x64/` (93 MB): apphost + `libHarfBuzzSharp.so` 2.8 MB + `libSkiaSharp.so` 11.2 MB (no av_libglesv2 — Windows ANGLE only).

**Matrix (`./client/tools/publish/matrix.sh`), 2026-07-19 — MATRIX PASS (wsl2):**

- Gate 2 `--verify-assets`: PASS exit 0 on **debug, release, published** — **row-8 publish third DISCHARGED (Linux; both platforms now done).**
- Gate 3 `--version` prefix == authority: PASS all three. WSL copies have NO `.git` → SourceRevisionId absent → prints plain `0.1.0` (derivation test tolerates; recorded as a fact about SourceLink requiring git presence).
- Gate 4 fresh-profile: PASS all three — graceful exit 0, no settings.json created.
- Gate 5 corrupt-settings: PASS all three — `settings.corrupt-*.json` preserved the exact seeded bytes (`7B 7B 00 FF 41`); typed-Degraded stderr seam line observed.
- Gate 6 data-path identity: PASS — `/home/mich/.config/CcpClient` identical across modes; published ran from MOVED `/tmp/ccp-sp010-portable`.
- Gate 7 logs-absence: PASS all three.
- Gate 8 native-deps floor (published): recorded in release-publish-gates.md §1 — ldd-per-.so ∪ `/proc/<pid>/maps`: system fontconfig/freetype/expat/z/bz2/png16/brotli + libX11 family + libGL/Mesa (WSLg) + libdbus + ICU 78. `ldd` on the apphost alone shows none of this (consult Q1 confirmed empirically).
- Gate 1 graceful close on every headed run: **WM_DELETE_WINDOW ClientMessage → exit 0** (negative control first: malformed message ignored, process alive at 2 s; protocol advertisement asserted via `XGetWMProtocols` before every send; XGetImage BMP capture per run under `client/tools/verify/artifacts/wslg-matrix-*.bmp`). **Rows 2/3 headed-Linux (WSLg) smoke debt DISCHARGED; SP-007's WSLg graceful-close gap DISCHARGED** (the libX11 WM_DELETE_WINDOW path works — no rename needed).

**XDG correction (empirical):** with `XDG_CONFIG_HOME=/tmp/xdg-sp010` the quarantine landed under it — the store honors `XDG_CONFIG_HOME` via .NET's Unix `ApplicationData` mapping. The pre-approach consult's framing ("~/.config literal, not XDG") was wrong; release-publish-gates.md gate 6 + the `CompositionRoot.DefaultSettingsPath` doc comment corrected to the observed truth (advisor advisory < empirical evidence, per authority order).

**Measured budgets (cold precondition VERIFIED: all bin/obj deleted, zero remaining confirmed):**

| Measurement | Windows | WSL2 |
|---|---|---|
| publish cold | 3.05 s | 6.67 s (true zero-bin/obj re-measure) |
| publish incremental | 1.73 s | 1.38 s |
| matrix run 1 | 9.06 s | 27.4 s |
| matrix run 2 | 6.96 s | 27.3 s |

(WSL2 matrix is dominated by six headed runs × (launch + window-wait + negative-control 2 s + close-wait); Windows headed close is near-instant. Publish cold on Windows is 3 s because the solution is small and NuGet is cache-warm; SP-008/009 measured the same machine class.)

**Surprises (Step 3/4):**

1. **Incremental `dotnet publish` into an existing single-file output dir silently DROPS native sidecars** — reproduced deterministically (cold: libSkiaSharp/libHarfBuzzSharp/av_libglesv2 present; second run: gone; app dies with `BadImageFormatException 0x8007000B`). Fixed in both publish scripts: always publish to a CLEAN output dir. The matrix caught this within minutes of first failure — the gates working as designed. **port-lessons candidate.**
2. **Bash ERE has no `\S`** — the GATE3 version regex silently never-matched on WSL (PCRE-ism); fixed to `[[:space:]]`/`[^[:space:]]` classes. Windows PS regex unaffected.
3. **Transient `Internal CLR error (0x80131506)`** on the first WSL Debug build; clean retry succeeded. Recorded, no recurrence.
4. **WSL `--version` prints unsuffixed `0.1.0`** — no `.git` in the native copy → no SourceLink → no SourceRevisionId. Confirms the suffix is git-presence-dependent; derivation-not-equality testing was the correct call.
5. **XDG_CONFIG_HOME is honored** (see above) — doc-level correction, not a behavior change.
- Engine review Step 4: `spine_review_step` → skipped=true (T-2).
