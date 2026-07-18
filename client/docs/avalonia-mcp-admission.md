# AvaloniaUI.MCP — Admission Audit Record

| Field | Value |
|---|---|
| Date | 2026-07-18 |
| Branch | pilot/avalonia-mcp-audit |
| Auditor | Agent-driven pilot |
| Governing docs | `client/docs/port-workflow.md` (Extension admission → Avalonia MCP), `client/docs/architecture.md` (A-013, A-014), `client/docs/first-attempt-systemic-lessons.md` |

> **A-014:** This is a docs/tooling audit. It closes no product capability.

## 1. Version, commit, and hash verification

Audited repo: `E:\Code\AvaloniaUI.MCP`. Launched DLL (per `mcp.json`): `dotnet E:\Code\AvaloniaUI.MCP\src\AvaloniaUI.MCP\bin\Release\net9.0\AvaloniaUI.MCP.dll`.

| Check | Result |
|---|---|
| `git rev-parse HEAD` | `974ec59bff1c2f70e2c00e4820e5723168ac17df` |
| `git log -1` | `2025-06-28 Initial commit` (single-commit repo) |
| `git status --porcelain` | ` M global.json` (only modification; SDK pin file, not source) |
| DLL FileVersion | `1.0.0.0` |
| DLL ProductVersion | `1.0.0+974ec59bff1c2f70e2c00e4820e5723168ac17df` (informational version embeds HEAD) |
| DLL SHA256 | `CE91D45F4BF6C3F34C579B1B6201AA0D9206A263ED9E7D8B52F7BA5F6DFC0445` |

Package pins (Directory.Packages.props): ModelContextProtocol `0.3.0-preview.1`, ModelContextProtocol.Core `0.3.0-preview.1`, Avalonia / Avalonia.Desktop / Avalonia.ReactiveUI / Avalonia.Controls.DataGrid / Avalonia.Fonts.Inter all `11.3.1` (used for project-generation templates), Sentry `5.11.2`, Sentry.Extensions.Logging `5.11.1`. OpenTelemetry present but console exporter only (local).

**Byte-equivalence statement:** the informational version matches HEAD and only `global.json` is dirty, so source provenance is consistent; byte-equivalence to the upstream commit cannot be fully assumed because the build environment file was modified and the build flags are unknown.

## 2. Tool inventory

53 tools discoverable and callable. Grouped:

| Group | Tools | One-line purpose |
|---|---|---|
| Validation | `avalonia_ValidateXaml` | Heuristic XAML well-formedness check (see §5) |
| Diagnostics | `avalonia_DiagnoseCommonIssues` | Heuristic common-issue scan |
| | `avalonia_PerformHealthCheck` | Server self-health report |
| | `avalonia_GetServerInfo` | Server identity/capability string |
| | `avalonia_GetServerMetrics` | Server runtime metrics |
| | `avalonia_TestLogging` | Logging-pipeline self-test |
| | `avalonia_ForceGarbageCollection` | Force server GC |
| | `avalonia_Echo` | Echo round-trip |
| | `avalonia_AnalyzePerformance` | Heuristic performance analysis |
| | `avalonia_GetPerformanceRecommendations` | Heuristic performance advice prose |
| Reference/migration readers (7) | `avalonia_get_getmigrationguide`, `get_getmvvmpatterns`, `get_getmigrationsteps`, `get_getcontrolmappings`, `get_getxamlpatterns`, `get_getnamespaceandbindingchanges`, `get_getcontrolsreference` | Unpinned WPF→Avalonia prose reference |
| WPF conversion | `avalonia_ConvertWpfXamlToAvalonia` | Mechanical WPF XAML → Avalonia conversion |
| Project scaffolding | `avalonia_CreateAvaloniaProject` | Generates a new Avalonia project (11.3.1 templates) |
| Code generators (~34, incl. scaffolding) | `avalonia_Generate{Animation, AttachedProperty, AuthenticationPattern, AsyncDataAccess, AccessibleComponent, ApiModels, BusinessService, ColorScheme, ControlTemplate, CultureFormatting, CustomAnimation, CustomControl, DataSecurityPattern, DDDArchitecture, DebugUtilities, DesignSystem, EFCoreRepository, HttpClientService, LayoutPanel, LocalizationSystem, MocksAndBuilders, MvvmArchitecture, MicroservicesArchitecture, PageTransition, PerformanceTests, PluginArchitecture, ResponsiveDesign, Selectors, Storyboard, Theme, UITests, UnitTests, UXPatterns}` | Code generation; much of it generic .NET codegen |

**Authority-masquerade flag:** every `avalonia_Generate*` tool, `avalonia_ConvertWpfXamlToAvalonia`, and all `get_*` reference readers can produce output that looks like API or design authority — it is not. Many generators are generic .NET codegen (EF Core, microservices, HttpClient) wearing an Avalonia namespace; none compile against the client v12 toolchain. The `get_*` readers return unpinned prose.

## 3. Startup health

The server starts from the `mcp.json` command; all 53 tools are discoverable and callable. `avalonia_GetServerInfo` returned: "AvaloniaUI MCP Server v1.0 - Provides comprehensive AvaloniaUI development assistance including project generation, XAML validation, WPF migration, and more."

`avalonia_PerformHealthCheck` result: **Overall DEGRADED**.

| Component | Status |
|---|---|
| Telemetry Service | Not configured |
| Validation Service | Available (static) |
| Error Handling | Available (static) |
| Resource Cache | Operational |
| Memory | 7.00 MB (normal) |
| File System | R/W ok |

The DEGRADED status is explained entirely by the internal `ITelemetryService` being unconfigured. That service is distinct from the Sentry logging pipeline (§4), which is active regardless of the health-check result.

## 4. Outbound connections and Sentry/telemetry

Program.cs lines 19–32: `builder.Logging.AddSentry` with a **hardcoded DSN** `https://82c12a7f9520219b0fe9f91ac1d14b37@o4509369388761088.ingest.us.sentry.io/4509576978235392` — fixed endpoint host `ingest.us.sentry.io`.

Configuration: `Environment` from the `ENVIRONMENT` env var (default `development`); `TracesSampleRate` 0.1; `ProfilesSampleRate` 0.1; `AutoSessionTracking=true`; `AttachStacktrace=true`; `SendDefaultPii=false`; `MaxBreadcrumbs=100`; `Release` `avalonia-mcp@1.0.0`. `mcp.json` passes no env overrides. OpenTelemetry uses the console exporter only (local, no network egress).

**No configuration flag, env var, or code path disables Sentry.** Disablement is therefore NOT verifiable, and per the task contract the recommendation (footer) must be conditional with an owner follow-up. Owner follow-up options:

1. Block `ingest.us.sentry.io` at firewall/hosts level for the `dotnet` process.
2. Fork-and-patch the DSN out.
3. Restrict use to non-sensitive redacted fragments, accepting the residual risk.

`SendDefaultPii=false`, but `AttachStacktrace=true` and up to 100 breadcrumbs can still carry AXAML fragments or local paths inside error events.

## 5. Seeded AXAML behavior test

Probes run through `avalonia_ValidateXaml` at `validationLevel=strict`:

| Probe | Fragment | Expected | Result | Verdict |
|---|---|---|---|---|
| (a) | Valid v12 fragment (Window + StackPanel + TextBlock.Classes + Button Command/IsVisible compiled-binding shapes) | Pass | PASSED | True positive; also emitted reasonable hints (`x:DataType`, `.axaml` extension) |
| (b) | WPF-only constructs: `Window.Triggers`/`Trigger`/`Setter`, `RoutedCommand`, `pack://application:,,,` URI | Fail | PASSED | **False negative** — none of the three exist in Avalonia |
| (c) | Invented API: `Button QuantumEntanglementMode="Spooky"`, `<UnicornSlider Value="42"/>` | Fail | PASSED | **False negative** — no property- or control-existence checking |
| (d) | Malformed XML (unclosed Button + truncated Window tag) | Fail | FAILED with precise syntax error | True negative |

Tally: false positives **0**; **2 of 4 probes were false negatives**, missing all **4 seeded semantic defects** (3 WPF-only constructs in probe b — `Window.Triggers`, `RoutedCommand`, `pack://` URI — plus the invented control/property pair in probe c). Every seeded semantic defect was a false negative. Interpretation per A-013: `ValidateXaml` is XML well-formedness plus namespace/root-shape checks only — heuristic parsing pinned to Avalonia 11.3.1, **not** the v12 compiler.

## 6. Safe-redaction rules

**May send** (only after official v12 research): small redacted AXAML fragments, selectors, bindings, layout questions, accessibility questions, heuristic-performance questions.

**Never send:** secrets; user data; camera data; private URLs/endpoints; absolute local paths; sensitive logs; proprietary code beyond minimal redacted fragments.

**Extra rule (justified by §4):** because Sentry breadcrumbs and stack traces may echo tool inputs to an external endpoint, treat every MCP call as potentially exfiltrating its arguments — redact BEFORE calling, not just when pasting results.

## 7. Tool-by-tool admission table

| Tool(s) | Recommendation | Evidence |
|---|---|---|
| `avalonia_ValidateXaml` | **ADMIT-AS-ADVISORY** (syntax-only) | §5: correct on well-formedness in both directions; false negatives on all 4 seeded semantic defects (2 probes) — never citable as API-validity proof |
| `avalonia_DiagnoseCommonIssues` | **ADMIT-AS-ADVISORY** | Heuristic diagnostics; advisory role per port-workflow |
| `avalonia_PerformHealthCheck`, `avalonia_GetServerInfo`, `avalonia_GetServerMetrics` | **ADMIT-AS-ADVISORY** (diagnostics) | §3: server self-report only |
| `get_*` reference readers (7) | **ADMIT-AS-ADVISORY** (loose orientation only) | §2: unpinned 11.3.1-era prose; never authority over official v12 docs |
| All `avalonia_Generate*` (~33) | **REJECT** for production use | §2: generic .NET codegen in an Avalonia namespace; none compile against the client v12 toolchain |
| `avalonia_ConvertWpfXamlToAvalonia` | **REJECT** | WPF-conversion authority is a prohibited role per port-workflow |
| `avalonia_CreateAvaloniaProject` | **REJECT** | §2: scaffolds 11.3.1 templates, not the client toolchain |
| `avalonia_Echo`, `avalonia_TestLogging`, `avalonia_ForceGarbageCollection`, `avalonia_AnalyzePerformance`, `avalonia_GetPerformanceRecommendations` | **REJECT** (no evidence value) | Server self-tests and heuristic prose; output can never be cited as evidence about client code or Avalonia v12 behavior, and the diagnostic surface is already covered by the admitted health/metrics tools |

## 8. Non-bypass statement

MCP output never substitutes for: official Avalonia v12 documentation; real compilation with the client toolchain; tests/profiling; K3 rendered-image review; or headed Windows/Linux gates. MCP unavailability never blocks a task — skip and continue with official sources (failure policy per port-workflow). Only concise accepted/rejected findings are ever recorded in client docs — never full transcripts.

---

## Overall recommendation

**CONDITIONAL.** The bounded advisory set in §7 is admissible ONLY after the owner chooses a Sentry mitigation from §4; until then, all use stays limited to non-sensitive redacted fragments (§6).

Task-board row: moves to WIP — "audit complete, owner admission decision pending". A-013 admission is an owner decision; only the owner flips the row to DONE.

**Pre-approach consult** (bpx-consult, solo, 2026-07-18): verdict "proceed with named fixes" — (1) VERIFY must grep captured MCP evidence, not call MCP live (the pi-task verify child is read+bash only); (2) the audit row ends WIP "owner admission decision pending" and never self-DONEs; (3) council deferred to owner-review time because bpx-consult council seats are not yet probed (task-board row "Probe bpx-consult council and task integration"). All three fixes were applied to this task.

**Pre-completion solo consult** (bpx-consult, solo, 2026-07-18): verdict "fix-first — two small items, then commit", both applied before commit: (1) commit message must not overclaim an admission that is still owner-pending (record and board row both say CONDITIONAL/pending); (2) this placeholder had to be filled and the doc re-linted before committing. The review confirmed the §5 tally fix, hash/commit/DSN citations, diff scope (two doc files only), and the gate-history honesty about this being an agent-driven pilot that does not close the still-OPEN pi-task pipeline pilot row.
