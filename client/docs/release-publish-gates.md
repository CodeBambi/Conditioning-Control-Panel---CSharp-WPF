# Release and publish gates

**Status:** active deliverable of task-board row 9 (SP-010). Owner decisions applied: A-014 Release rule (Debug, Release, and published artifacts are separate gates; one version source drives assemblies, update metadata, and packaging) and YAGNI constraint (no installer, no updater, no packaging mechanism without a consumer). This contract instantiates `architecture-proposal.md` §6 (row-9 column: version authority location, publish strategy). It discharges two inherited named gates: row 8's deferred publish third (same `--verify-assets` invocation against the published artifact) and rows 2/3's headed-Linux (WSLg) smoke debt, including SP-007's graceful-close gap.

---

## 1. Named publish strategy

**Decision: self-contained single-file per RID — `win-x64` and `linux-x64` — with the SDK-default native-library layout.**

```
dotnet publish client/src/CcpClient.Desktop/CcpClient.Desktop.csproj -c Release -r <RID> --self-contained true -p:PublishSingleFile=true
```

- **Self-contained** removes dotnet-runtime presence as a variable (matches the WPF product's self-contained distribution shape).
- **`PublishSingleFile=true`** bundles all managed assemblies into the apphost (loaded in-memory; embedded resources — including the `!AvaloniaResources` bundle the asset manifest rides in — keep working; verified per current .NET docs, §7).
- **Native-library layout (product decision, recorded per pre-approach consult):** `IncludeNativeLibrariesForSelfExtract` is **NOT set** (SDK default: false). Native binaries (the .NET runtime's own natives plus SkiaSharp/HarfBuzz natives) ship **beside the exe in the publish directory** — the artifact is the publish directory as a unit, not literally one file. Alternatives rejected for these gates: bundling natives for first-run extraction adds a writable-extraction-directory dependency (`DOTNET_BUNDLE_EXTRACT_BASE_DIR`, `$HOME/.net`, `%TEMP%/.net`), fails on noexec/read-only mounts, and has the systemd `$HOME`-undefined caveat — with zero consumer for a literally-one-file artifact today (A-014).
- **Excluded explicitly:** `PublishTrimmed` (Avalonia uses reflection; the official Native-AOT doc requires trimmer-root-descriptor work — no consumer), `ReadyToRun` (optional size/startup trade, not needed for gates), installer/packaging (no Inno; no board row), auto-update mechanism (§5 documents metadata shape only).

**Revisit trigger:** the first real distribution consumer (an installer/archive row, or a distribution channel requiring a literal single file). That row re-evaluates `IncludeNativeLibrariesForSelfExtract` (with the extraction-dir failure modes above as its test matrix) and records the decision here.

## 2. One version authority

**Rule: one `<Version>` in `client/Directory.Build.props`. Every version surface DERIVES from it. Nothing else anywhere declares a version.**

- `client/Directory.Build.props` is a NEW file at the client root. It stops the MSBuild upward walk (MSBuild walks toward the drive root and stops at the first `Directory.Build.props`), insulating the client from any future repo-root props. It applies to every project under `client/` (app, tests, tools) — deliberately minimal.
- **The number: `0.1.0`** — the honest greenfield number. The client has shipped nothing; semver 0.x signals initial development, 0.1.0 is the first-milestone line. It deliberately does NOT inherit the WPF product's version (6.2.7): the greenfield client is not that product and must not impersonate it. **Revisit trigger:** the first release row assigns 1.0.0 semantics.
- **Derivation contract** (SDK defaults, verified §7): `AssemblyVersion` = the numeric `Version` padded to four parts (`0.1.0.0`); `FileVersion` = same; `InformationalVersion` = `Version` with an optional `+<SourceRevisionId>` suffix (SDK auto-appends the commit hash when Source Link data is present; `IncludeSourceRevisionInInformationalVersion` defaults true). Tests assert these **derivation rules** — never blind full-string equality and never a hardcoded version value.
- **Canonical display = `InformationalVersion`** (read from the `AssemblyInformationalVersionAttribute` on the entry assembly via reflection). **Binding identity = `AssemblyVersion`.** `FileVersion` is the Windows file-property surface. All three must agree by derivation.
- **Forbidden reads:** never `FileVersionInfo` from a file path, never `Assembly.Location` (EMPTY under single-file — verified §7), never a path-derived version. The runtime surface is the attribute, reachable in-memory in every artifact mode.
- **Runtime surface:** a `--version` diagnostic line on the existing bounded-self-check path pattern at the top of `Program.Main` (same shape as `--verify-assets`: no window, no lifetime, no participants). Prints the InformationalVersion to stdout and exits 0.
- **Publish tooling derives artifact naming from the authority** via `dotnet msbuild -getProperty:Version` — never a hardcoded or reparsed version string.

## 3. Artifact evidence matrix

Three artifact modes × two platforms. Modes: **Debug** (`bin/Debug/net10.0/`), **Release** (`bin/Release/net10.0/`), **Published** (publish directory per §1). Platforms: **Windows**, **WSL2 Linux** (native ext4 dir, never `/mnt/e`). Debug/Release run through the apphost/`dotnet <dll>`; the published artifact runs as the native binary directly — all three modes share one evidence shape: launch, observe, graceful close, `wait` for the real exit code.

Every cell of the matrix proves, for that mode and platform:

| # | Gate | Pass criterion |
|---|------|----------------|
| 1 | Startup + graceful shutdown | App launches headed, renders its window, closes through the REAL close path (Windows: `CloseMainWindow()` → WM_CLOSE; Linux/X11: `WM_DELETE_WINDOW` ClientMessage — §6), process exits **0** via `wait` on the real PID. Kill signals (SIGTERM/pkill/taskkill) are forbidden as the success path — they bypass the guarded teardown (exit 143, no flush). |
| 2 | `--verify-assets` | Exit 0 with the PASS summary (asset-manifest.md self-check contract). **The published-mode runs are row 8's deferred publish third — zero new test logic, the same invocation.** WinExe stdout redirection is re-verified on the single-file apphost. |
| 3 | `--version` | Exit 0; printed value parses as a version and agrees with the assembly attributes by the §2 derivation rules (checked against the real artifact, not hardcoded). |
| 4 | Fresh-profile run | Real config dir moved aside → headed run → graceful close → exit 0; **no `settings.json` created** (defaults are never auto-saved — persistence contract §5 rule 2); no configuration-only crash. Config dir restored after. |
| 5 | Corrupt-settings run | Garbage bytes seeded at `settings.json` → headed run → graceful close → exit 0 (typed Degraded, never silent); **`settings.corrupt-*.json` exists with the original garbage bytes preserved** (the observable quarantine proof — exit code alone is insufficient). Config dir restored after. |
| 6 | Data-path identity | The store path resolves identically across the three modes (`%APPDATA%\CcpClient` Windows, `~/.config/CcpClient` Linux — derived from `SpecialFolder.UserProfile`, never from `AppContext.BaseDirectory` or the artifact location). Evidence: the quarantine file from gate 5 lands at the same absolute path in every mode. The publish directory is MOVED before its run to prove the artifact is location-independent. XDG note: the store uses `~/.config` literally, not `$XDG_CONFIG_HOME` — recorded, not claimed as XDG compliance (pre-existing behavior, out of this row's scope). |
| 7 | Logs-absence | No logging subsystem exists (the SP-003 seam writes to stderr only — startup-shutdown-contract §9, framework admission explicitly deferred). Gate: after every run, no log files exist beside the artifact or in the config dir. **The absence is verified, never invented into existence.** |
| 8 | Native-deps floor | Windows: record which native files ship beside the exe (research expectation: runtime natives + `libSkiaSharp.dll`/`libHarfBuzzSharp.dll`-class Avalonia natives — observed reality recorded). Linux: `ldd` on every shipped `.so` **plus** `/proc/<pid>/maps` of the running process — `ldd` on the apphost alone CANNOT see runtime-dlopened libraries (libX11, fontconfig, ICU load via dlopen). The union is the recorded system-library floor. It feeds the owner's "which Linux distributions" decision WITHOUT settling it. |

Matrix scripts live under `client/tools/publish/` (Windows PowerShell + WSL bash sharing the same gate definitions); the X11 graceful-close sender extends the proven `client/tools/verify/xgetimage.py` ctypes mechanism.

## 4. Version-derivation tests (unit tier)

`CcpClient.Tests` asserts against the real built assembly:

1. `AssemblyInformationalVersionAttribute` exists on the entry assembly; its value parses and its **prefix before any `+`** equals the `Version` prefix shared by `AssemblyVersion`/`FileVersion` (derivation, not equality — the `+<sha>` suffix is allowed by §2).
2. `AssemblyVersion` and `FileVersion` agree numerically (both derive from the same authority).
3. The publish script's artifact naming uses the `dotnet msbuild -getProperty:Version` output (script-level check; the unit tier never shells out — it asserts the attributes only, and the matrix gate 3 checks the real artifact's `--version` line).

## 5. Update/package metadata shape (documented; mechanism excluded)

No updater and no packaging mechanism exist (explicit exclusion). The shape a future update row consumes is recorded so it derives from the same authority:

```
name:      CcpClient.Desktop
version:   <InformationalVersion minus any +suffix>   ← client/Directory.Build.props <Version>
rid:       win-x64 | linux-x64
artifact:  CcpClient.Desktop-<version>-<rid>          ← publish directory as a unit (§1)
```

The future row owns: where metadata is published, how a running client compares versions, signing, and channels. None of that is built here.

## 6. WSLg graceful-close contract (rows 2/3 debt + SP-007 gap)

The headed-Linux smoke debt is discharged by gate 1 on WSL2 against the published linux-x64 artifact (plus Debug/Release): the window renders for real (XGetImage capture, SP-007 pattern) AND the process exits 0 through the real close path.

The close mechanism (pre-approach consult, three silent-failure modes pinned):

1. **Message shape:** X11 `ClientMessage` (type 33), `message_type` = `WM_PROTOCOLS` atom, `format` = 32, `data.l[0]` = `WM_DELETE_WINDOW` atom, `propagate` = False, `XFlush` after send. Malformed variants are silently ignored by Avalonia — so the gate sends a **deliberately malformed message first (negative control: process must still be alive)**, then the correct one.
2. **Right XID:** the candidate window (found by title, xgetimage.py pattern) must **advertise `WM_DELETE_WINDOW` via `XGetWMProtocols`** before anything is sent — proves the client window under WSLg's RAIL reparenting, not a frame.
3. **Discriminator:** `wait` on the real PID for the exit code; window-disappearance is not evidence. No pkill fallback on the success path.

If this mechanism cannot be made to work, the gap is **renamed honestly** (e.g., "WSLg graceful close unproven — no input automation") rather than discharged by a kill.

## 7. Research citations

All fetched 2026-07-19. Baselines: Avalonia 12.1.0, .NET SDK 10.0.302 (Windows) / 10.0.110 (WSL2).

- Single-file deployment (native libraries separate by default; `IncludeNativeLibrariesForSelfExtract` embeds for extraction; extraction to `DOTNET_BUNDLE_EXTRACT_BASE_DIR` / `$HOME/.net` / `%TEMP%/.net`; systemd `$HOME` caveat; managed DLLs loaded in-memory so embedded resources keep working): https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- Single-file API incompatibilities (`Assembly.Location` returns empty; `Assembly.GetFile(s)` throw; use `AppContext.BaseDirectory` for files next to the exe; `Environment.ProcessPath` for the exe path): same page, "API incompatibility" table.
- Version attribute derivation (`SourceRevisionId` auto-populated from the commit hash when Source Link is present, .NET 8+; `IncludeSourceRevisionInInformationalVersion` default true; appended to `InformationalVersion`): https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props (assembly attribute properties).
- Linux runtime dependencies remain system-provided for self-contained apps (libicu, libstdc++, openssl-libs, krb5, zlib-class floor; the apphost is not a static binary): https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual#dependencies
- Avalonia 12 deployment docs (Native AOT documented as the opt-in native path requiring trimmer root descriptors for reflection — consistent with the `PublishTrimmed` exclusion; no Avalonia-specific single-file guidance contradicting the .NET docs): https://docs.avaloniaui.net/docs/deployment and https://docs.avaloniaui.net/docs/deployment/native-aot

## Honest absences (verified, never invented)

- **Logging:** no logging subsystem exists. The SP-003 seam (`ILogSink`) writes to stderr only; framework admission is explicitly deferred by startup-shutdown-contract §9. Matrix gate 7 verifies the absence of log files.
- **Localization:** no localization entries exist (asset-manifest.md: entries arrive with the localization row; no consumer — A-014). The matrix's asset gate is the `--verify-assets` invocation, which catalogues exactly what ships.
- **Installer/updater:** none (§1 exclusions, §5 shape-only).
- **XDG compliance:** the store path is `~/.config` literal, not `$XDG_CONFIG_HOME` (gate 6 note).
