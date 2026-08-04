# Avalonia MCP — Identity, Verdict, Usage

Full report: `.spine/mcp-avalonia-deepresearch.md` (committed 2026-08-04).
Usage map addendum: `.spine/handoff.md`.

## Identity

- NOT an npm package — local source build, registered in
  `C:\Users\Micha\.pi\agent\mcp.json` (pi-global discovery). **Per-machine paths:**
  desktop `E:\Code\AvaloniaUI.MCP`; laptop `C:\Code\AvaloniaUI.MCP` (cloned + built
  2026-08-04, `dotnet C:\Code\AvaloniaUI.MCP\src\AvaloniaUI.MCP\bin\Release\net9.0\AvaloniaUI.MCP.dll`,
  init + 46 tools verified live).
- Git tree 974ec59 = upstream HEAD exactly (github.com/decriptor/AvaloniaUI.MCP).
- 46 tools (laptop build). Sentry characterized in the deep-research report.

## Three-seat registration (2026-08-04, laptop; mcp.json was MISSING → recreated)

| Seat | Server | Type | Cost | Status |
|---|---|---|---|---|
| `avalonia-docs` | official Build MCP | http `https://docs-mcp.avaloniaui.net/mcp` | free | initialize verified (`Avalonia Documentation 2.0.0`) |
| `avalonia-live` | **Keincheck 0.11.0 embedded** (DVSProductions, MIT, Avalonia-12-native, net8) | http `http://127.0.0.1:3001` | free OSS | initialize verified (`CcpClient.Desktop 0.1.0.0`) — app-side seam: `CCP_MCP=1` env-gated `UseMcpServer()` in Program.cs |
| `avalonia-ui` | decriptor/AvaloniaUI.MCP local build | stdio dotnet DLL (above) | MIT | initialize + tools/list verified (46 tools) |

**Official DevTools MCP (`avdt mcp`) REJECTED 2026-08-04:** MCP is a paid feature
(Avalonia Plus key, `AVALONIA_TOOLS_LICENSE_KEY`), not in the Community license —
violates the owner's free-OSS constraint. Runner-up adirh3/AvaloniaMcp rejected:
needs .NET 10 Preview SDK and targets Avalonia 11.2+.
**Machine note:** this laptop needed the .NET 10 SDK for the net10.0 client
(installed 10.0.302 via winget 2026-08-04); desktop already has it (10.0.110+).

## Usage rule (run rules)

**Advisory-only.** Every call recorded accept/reject + reasons in the using
packet's record.md. Never substitutes: current-docs research (avalonia-research
skill), compilation, K3 pixel verification, headed gates.

## Usage map (owner-requested research 2026-07-22)

Summary of where the 53 tools fit the UX port — see the durable copy in
`.spine/handoff.md` addendum for the full mapping (conversion/validation tooling
grouped by phase, with accept criteria).

## Admission status

**SP-036 LANDED 2026-08-04 (wave-4 lane-2) — bounded admission recorded in `spine-tasks/SP-036-avalonia-mcp-audit/record.md` §11:** avalonia-docs (8 tools) admitted advisory; **avalonia-live ADMITTED PROVISIONALLY → refined 2026-08-04 by first live enumeration (27 tools, end-to-end `describe_screen` proof on the dashboard — inspection/drive evidence tools, admitted-advisory; binding `CCP_MCP=1` env-gate condition stands — any unconditional port binding VOIDS the seat);** avalonia-ui: `ValidateXaml`/`DiagnoseCommonIssues`/server self-reports/7 `read_get*` admitted advisory; `AnalyzePerformance` + `GetPerformanceRecommendations` + `ConvertWpfXamlToAvalonia` + `CreateAvaloniaProject` + all 33 `Generate*` REJECTED. **Sentry empirically LIVE (one outbound TLS socket to the org ingest endpoint; unpatched build, no disable path) — mitigation = de-facto option 3 (redacted fragments only), OWNER QUESTION OPEN.** Binding rules run-wide: advisory-only; redact-BEFORE-calling; ValidateXaml PASS ≠ API-validity proof; unavailability never blocks; usage recorded accept/reject + reasons per use.
