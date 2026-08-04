# Avalonia MCP — Identity, Verdict, Usage

Full report: `.spine/mcp-avalonia-deepresearch.md` (committed 2026-08-04).
Usage map addendum: `.spine/handoff.md`.

## Identity

- NOT an npm package — local source build, registered in
  `C:\Users\Micha\.pi\agent\mcp.json` →
  `dotnet E:\Code\AvaloniaUI.MCP\...\AvaloniaUI.MCP.dll` (**desktop-only paths;
  laptop needs its own clone/build/registration**).
- Git tree 974ec59 = upstream HEAD exactly (github.com/AvaloniaUI/AvaloniaUI.MCP).
- 53 tools. Sentry characterized in the deep-research report.

## Usage rule (run rules)

**Advisory-only.** Every call recorded accept/reject + reasons in the using
packet's record.md. Never substitutes: current-docs research (avalonia-research
skill), compilation, K3 pixel verification, headed gates.

## Usage map (owner-requested research 2026-07-22)

Summary of where the 53 tools fit the UX port — see the durable copy in
`.spine/handoff.md` addendum for the full mapping (conversion/validation tooling
grouped by phase, with accept criteria).

## Admission status

SP-036 (staged for wave 4) is the audit task to admit bounded Avalonia MCP use
(A-01...). Until that lands, treat all MCP output as unadmitted research input.
