# Avalonia MCP — deep research (orchestrator, 2026-07-22 evening)

Owner-requested deep dive before shutdown. Feeds: SP-036's bounded admission record (wave-4 resume), future UX packets, and the owner's Sentry decision. Durable on disk; key facts also in Engram #230.

## 1. Identity & provenance

- **NOT an npm package.** The `avalonia` MCP server is a **LOCAL SOURCE BUILD**: registered in `C:\Users\Micha\.pi\agent\mcp.json` as `dotnet E:\Code\AvaloniaUI.MCP\src\AvaloniaUI.MCP\bin\Release\net9.0\AvaloniaUI.MCP.dll`.
- Source tree: `E:\Code\AvaloniaUI.MCP` — git HEAD `974ec59` ("Initial commit"), remote `https://github.com/decriptor/AvaloniaUI.MCP.git`, **`git ls-remote origin HEAD` = the SAME hash** → local tree exactly matches upstream (one local modification: `global.json`, SDK pin for the build).
- Upstream posture (live GitHub API, 2026-07-22): **MIT license**, 36 stars / 8 forks, **created 2025-06-27, last push 2025-06-30** — a single-commit, dormant community project (~13 months unmaintained at research time).
- **Consequence:** the artifact is fully owner-controlled — rebuildable, patchable, forkable. There is no package publisher to wait on.

## 2. Telemetry (the Sentry question, fully characterized)

**Config (`src/AvaloniaUI.MCP/Program.cs:19-33`):**
- `builder.Logging.AddSentry(o => { ... })` — **UNCONDITIONAL. No disable path: no env-var gate, no appsettings check.**
- DSN (hardcoded, line 22): `https://82c12a7f9520219b0fe9f91ac1d14b37@o4509369388761088.ingest.us.sentry.io/4509576978235392` — live US Sentry ingest.
- `SendDefaultPii = false` (no user/IP); `AttachStacktrace = true`; `TracesSampleRate = 0.1`; `ProfilesSampleRate = 0.1`; `AutoSessionTracking = true`; `MaxBreadcrumbs = 100`; `Release = "avalonia-mcp@1.0.0"`; `Environment` from `ENVIRONMENT` env var (default `development`).
- Packages: `Sentry`, `Sentry.Extensions.Logging`, plus OpenTelemetry (console exporter).

**What leaves the box:**
- **Error+ logs → Sentry events** (with stack traces); **Info+ → breadcrumbs** (≤100); 10% sampled transactions + profiles; session-tracking pings.
- Content classes observed (`Services/TelemetryService.cs`): `Tool execution failed: {ToolName} - Error: {ErrorMessage}` (Warning), `Validation failed: {ValidationType} - Details: {ErrorDetails}` (Warning), `Server event: {EventType} - Properties: {@Properties}`.
- **No full XAML/documents are logged by design.** Residual content risk: XML parser messages can embed element/attribute NAMES from user XAML inside error strings; tool names and error summaries flow on every failing call.
- `AVALONIA_MCP_LOG_LEVEL` env var adjusts the pipeline minimum (can throttle volume to Critical-only) but is **not a disable**.

**The local-build escape hatch:** a ~2-line source edit (delete or condition the `AddSentry` block) + `dotnet build -c Release` produces a zero-telemetry server. Fork-versus-config choice for the owner: (a) patch-and-rebuild (cleanest — full disable, we own the artifact); (b) use as-is accepting the flows above; (c) stop using it. **This is the SP-036 Sentry finding's actionable answer.**

## 3. Implementation quality (what the advice is actually worth)

**Everything is static/deterministic C# — NO LLM-backed generation:**

| Surface | Implementation | Verdict for the port |
|---|---|---|
| `ValidateXaml` | 401 lines hand-written: XML syntax parse + 4 rule-based validators (Avalonia-specific, new-features, common patterns, performance) | **Useful pre-compile lint.** Deterministic, matches its good record in this run. Keep as advisory. |
| `ConvertWpfXamlToAvalonia` | Mechanical string-transform method (namespace/known-rename replacements; ~90 lines inside XamlValidationTool.cs) | First-draft converter for simple popups only. Corrections governed by dashboard-design grammar + wpf-parity outcomes. Never trusted blind. |
| `get*` reference tools (migration guide/steps/control mappings/namespace+binding changes/xaml patterns/mvvm patterns/controls reference) | Static JSON served from `Data/controls.json`, `Data/migration-guide.json`, `Data/xaml-patterns.json` — a **frozen 2025-06 snapshot** | Cross-check against the avalonia-research skill's current official docs. **Docs win on any discrepancy** (the snapshot predates Avalonia 12.1-era deltas). |
| Generators (Theme/ColorScheme/DesignSystem/CustomControl/ControlTemplate/AttachedProperty/LayoutPanel/ResponsiveDesign/Animation/Storyboard/PageTransition) | Config → template-string emitters (e.g., `ThemingTool`: ThemeConfiguration → GenerateThemeXaml/Code) | Boilerplate starters. Value = speed on boring scaffolding; the five-theme dark-neon grammar + pixels govern the result. |
| `AnalyzePerformance` | Generic heuristics (rejected **twice** in this run as self-contradictory — SP-013/SP-014) | **Not-admitted.** |
| Server ops (`GetServerInfo`, `PerformHealthCheck`, `GetServerMetrics`, `Echo`, `TestLogging`) | Live server introspection | SP-036 audit instruments. |
| Out-of-scope generators (microservices/EF/HTTP/DDD/auth/business+domain/plugin/API models/CreateAvaloniaProject/localization) | Same template class | Irrelevant to a desktop port. |

## 4. Recommended usage posture (for the owner's decision)

1. **Patch-and-rebuild without Sentry** (2-line edit; the run can execute this as a small task — it's a local build, zero external dependency). Until then: usage stays minimal + advisory, with the flows in §2 accepted knowingly.
2. **Admitted advisory set (post-fix):** ValidateXaml (lint), ConvertWpfXamlToAvalonia (first drafts), get* references (cross-checks, docs win), template generators (scaffolding speed), DiagnoseCommonIssues (debugging reference).
3. **Not-admitted:** AnalyzePerformance; all out-of-scope generators.
4. **Standing boundary (unchanged):** MCP output never substitutes for official docs, compilation, K3 screenshots, or headed gates; every call recorded accept/reject + reasons in the using packet's record.md.

## 5. Convergence with SP-036's in-flight audit

The lane's own findings (53-tool inventory, 0 FP / 3 FN probe matrix, "Sentry live, no disable path", redaction posture) are consistent with and deepened by this research: the DSN/config/flows (§2) and the patch-and-rebuild option (§4.1) are the additions. SP-036's admission record at resume should fold §2 and §4 in as the actionable layer over its empirical audit.
