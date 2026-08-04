# SP-036 record — audit and admit bounded Avalonia MCP use (A-013 evidence packet)

| Field | Value |
|---|---|
| Date | 2026-08-04 |
| Packet | `spine-tasks/SP-036-avalonia-mcp-audit/PROMPT.md` |
| Board row | `client/docs/task-board.md` P0 "Audit and admit bounded Avalonia MCP use" (OPEN) |
| Review Level | 2 (plan + code; code review runs on the engine after .DONE) |
| Environment | Host-runtime (Windows laptop) evidence; platform-neutral audit (WSL2 note: nothing here is platform-gated) |

## 1. Owner decree (verbatim, with source)

From `client/docs/task-board.md`, owner-decision ledger entry dated **2026-07-21** ("OWNER DECISION: ALL GATES LIFTED"):

> "Avalonia MCP admission (Sentry-mitigation decision made — proceed per the conditional recommendation)"

Source chat line (same ledger entry, verbatim): "I approve to continue as the owner lift all gates!"

This packet produces the audit evidence + the bounded admission record. It does **not** re-decide the admission. The "conditional recommendation" the decree references is the 2026-07-18 pilot record `client/docs/avalonia-mcp-admission.md`, whose §4 lists three owner follow-up options: (1) block `ingest.us.sentry.io` at firewall/hosts level, (2) fork-and-patch the DSN out, (3) restrict use to non-sensitive redacted fragments, accepting residual risk.

## 2. Installation inventory (three seats — all probed live on this box, 2026-08-04)

The packet's original single-server premise was corrected 2026-08-04: the Pi registration on this laptop is THREE seats in `C:\Users\Micha\.pi\agent\mcp.json` (read-only inspected; file reproduced here in full — no secrets present):

```json
{
  "mcpServers": {
    "avalonia-docs": { "type": "http", "url": "https://docs-mcp.avaloniaui.net/mcp" },
    "avalonia-live": { "type": "http", "url": "http://127.0.0.1:3001" },
    "avalonia-ui":   { "type": "stdio", "command": "dotnet",
      "args": ["C:\\Code\\AvaloniaUI.MCP\\src\\AvaloniaUI.MCP\\bin\\Release\\net9.0\\AvaloniaUI.MCP.dll"] }
  }
}
```

### Seat 1 — avalonia-docs (official hosted)

- Hosted HTTP MCP run by the Avalonia project; no local process, no local package. Endpoint **live-probed**: reachable (HTTP 406 without MCP protocol headers = expected protocol negotiation, not an error); `mcp({connect:"avalonia-docs"})` succeeded and listed **8 tools live** (matches the gateway cache).
- No version/commit/hash applies (hosted service); identity = the URL itself.

### Seat 2 — avalonia-live (Keincheck, embedded in the client app)

- Package `Keincheck 0.11.0`, referenced at `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj:31`; license **MIT** verified from the cached nuspec (`~/.nuget/packages/keincheck/0.11.0/keincheck.nuspec`: `<license type="expression">MIT</license>`); project URL `https://github.com/DVSProductions/Keincheck`.
- Seam: `client/src/CcpClient.Desktop/Program.cs:238-243` — `UseMcpServer()` only when env `CCP_MCP=1`; comment in source: opt-in per run so tests and normal runs never bind the port.
- Loopback only (`http://127.0.0.1:3001`). Live probe: port not bound (`curl` → connection failure) — **expected**, no app running with `CCP_MCP=1`.
- **Tool inventory UNVERIFIED** (requires launching the desktop app with the env gate; not exercised in this headless audit). Admission therefore PROVISIONAL (§7).

### Seat 3 — avalonia-ui (decriptor/AvaloniaUI.MCP, local stdio build)

| Check | Result (empirical, 2026-08-04) |
|---|---|
| Local clone | `C:\Code\AvaloniaUI.MCP` |
| `git rev-parse HEAD` | `974ec59bff1c2f70e2c00e4820e5723168ac17df` ("Initial commit", single-commit repo) |
| Upstream equivalence | `git ls-remote origin HEAD refs/heads/main` → same `974ec59bff…` — **local HEAD == upstream HEAD, verified live** |
| `git status --porcelain` | **clean** (the 2026-07-18 pilot's `E:\` clone had ` M global.json`; this `C:\` clone has zero modifications) |
| DLL | `bin\Release\net9.0\AvaloniaUI.MCP.dll`, built 2026-08-04 12:45 local |
| DLL FileVersion / ProductVersion | `1.0.0.0` / `1.0.0+974ec59bff1c2f70e2c00e4820e5723168ac17df` (informational version embeds HEAD — source provenance consistent) |
| DLL SHA256 | `42DAE31D177240260B5259C09B164F0C04E5DF07A7CC16FE98AEE7273CBBCC24` |
| Pilot comparison | Pilot (E:\ clone) recorded SHA256 `CE91D45F4BF6C3F34C579B1B6201AA0D9206A263ED9E7D8B52F7BA5F6DFC0445` — **MISMATCH, expected**: this DLL is a rebuild (Aug 4) in a different clone/build environment; same commit, not same binary. Recorded, not resolved further — provenance is pinned by commit + informational version, binary is build-environment-specific. |
| Live process | `dotnet.exe` PID 47796, started 2026-08-04 15:23 local, cmdline matches the `mcp.json` registration |
| Tool count | **53**, method: `tools.search({server:"avalonia-ui"})` total=53/count=53 (mechanical). The packet's "46 tools verified 2026-08-04" figure is **stale**; live = 53, matching the pilot's 53. |

## 3. Config audit (READ-ONLY; sensitive values presence+shape only)

- Registration: three seats as above; no env overrides passed to any seat; no Sentry/telemetry-related settings in `mcp.json`.
- **avalonia-ui Sentry posture — verified empirically, NOT transcribed from the deep-research report:** `C:\Code\AvaloniaUI.MCP\src\AvaloniaUI.MCP\Program.cs:19-32` calls `builder.Logging.AddSentry` **unconditionally** with a **hardcoded DSN** (US Sentry ingest endpoint; org-specific host `o4509369388761088.ingest.us.sentry.io`; DSN string present in source — recorded as presence+shape, not reproduced here beyond the host). Config: `TracesSampleRate 0.1`, `ProfilesSampleRate 0.1`, `AutoSessionTracking=true`, `AttachStacktrace=true`, `SendDefaultPii=false`, `MaxBreadcrumbs=100`, `Release=avalonia-mcp@1.0.0`, `Environment` from env var `ENVIRONMENT` (default `development`). `Sentry.dll` + `Sentry.Extensions.Logging.dll` present in the build output directory. **No configuration flag, env var, or code path disables Sentry** (grep of Program.cs + csproj: no conditional). This confirms the deep-research report's claim empirically on THIS laptop's build: the build is unpatched upstream (HEAD == upstream HEAD, clean tree).
- Hosts-file check: no `sentry` entry in `C:\Windows\System32\drivers\etc\hosts` — no hosts-level block.
- avalonia-live: no telemetry surface identified (loopback listener only); avalonia-docs: hosted service, its server-side posture is the vendor's, not auditable from here.

## 4. Runtime health (Step 2 evidence)

- Startup/probe cycle via the Pi MCP gateway: `mcp({connect:"avalonia-ui"})` — server spawned and listed tools without error; `avalonia-ui` GetServerInfo returned: "AvaloniaUI MCP Server v1.0 - Provides comprehensive AvaloniaUI development assistance including project generation, XAML validation, WPF migration, and more." (verbatim, matches pilot).
- `PerformHealthCheck`: **Overall DEGRADED**, explained entirely by the internal `ITelemetryService` ("Not configured"); Validation Service / Error Handling / Resource Cache / Memory (7.00 MB) / File System all OK. Same result as the pilot §3. The DEGRADED telemetry component is the server's own OTel-internal service (console exporter only) — distinct from the Sentry logging pipeline, which is active regardless (§5).
- Error/log surface: no startup errors observed via the gateway; server logs to console (stdio transport).

## 5. Outbound connections (Step 2 evidence — telemetry endpoints specifically answered)

`Get-NetTCPConnection -OwningProcess 47796` (the live avalonia-ui server process):

| Local | Remote | State | Identification |
|---|---|---|---|
| 192.168.50.196:58681 | `34.160.81.0:443` | ESTABLISHED | PTR `0.81.160.34.bc.googleusercontent.com`; **`nslookup o4509369388761088.ingest.us.sentry.io` resolves to `34.160.81.0`** (and `2600:1901:0:5e8a::`) — the remote endpoint IS the hardcoded DSN's org-specific Sentry ingest host |

**Exactly one outbound connection, and it is Sentry ingest.** No other remote endpoints for the server process. Conclusion: **Sentry is empirically LIVE on this laptop** — the mitigation named in the decree (per the pilot's §4 options) is **not in effect by option 1 (no hosts/firewall block — the socket is established) nor option 2 (build is unpatched upstream HEAD)**. The effective posture is **option 3 (restrict to redacted fragments) by default, not by recorded choice** — it is the only option requiring no action. **Owner question surfaced, NOT resolved:** was de-facto option 3 the intended mitigation, or was patch-and-rebuild / hosts-block expected? (Packet Do-NOT: never silently resolve this.)

avalonia-docs: outbound is made by Pi itself over HTTPS to `docs-mcp.avaloniaui.net` when the seat is used. avalonia-live: binds loopback only.

## 6. Tool inventory + classification (Step 2 evidence)

### avalonia-docs (8 tools, live-probed 2026-08-04)

`search_avalonia_docs` (docs search), `lookup_avalonia_api` (API lookup), `get_avalonia_expert_rules` (dev-rules doc), `migrate_diagnostics`, `migrate_to_xpf`, `analyze_wpf_project`, `migrate_to_avalonia`, `lookup_wpf_to_avalonia_mapping` (migration-playbook family). Classification: docs-search convenience — output is the vendor's own documentation index, but **never authority over the run's own docs research** (advisory-only).

### avalonia-live (UNVERIFIED — provisional seat)

Tool inventory **NEVER ENUMERATED in this audit** — the gateway reported the seat `not connected` throughout; live enumeration requires launching the desktop app with `CCP_MCP=1`, out of scope for this headless audit. (The Pi session-start server metadata lists 19 tool names for this seat — harness-claimed metadata, not a live enumeration; recorded as UNVERIFIED either way.) Admission is provisional pending live enumeration (§7).

### avalonia-ui (53 tools, live — method `tools.search`, 2026-08-04)

| Group | Tools | One-line purpose | Classification |
|---|---|---|---|
| Validation | `ValidateXaml` | Heuristic XAML well-formedness check (XDocument + hand rules; NOT the v12 compiler) | **ADMIT-AS-ADVISORY** (syntax-only; §7 probe matrix) |
| Diagnostics | `DiagnoseCommonIssues` | Heuristic common-issue scan | **ADMIT-AS-ADVISORY** |
| Server self-report | `PerformHealthCheck`, `GetServerInfo`, `GetServerMetrics` | Server health/identity/metrics | **ADMIT-AS-ADVISORY** (diagnostics about the server only) |
| Reference readers (7) | `read_getmigrationguide`, `read_getmvvmpatterns`, `read_getmigrationsteps`, `read_getcontrolmappings`, `read_getxamlpatterns`, `read_getnamespaceandbindingchanges`, `read_getcontrolsreference` | Unpinned WPF→Avalonia prose reference | **ADMIT-AS-ADVISORY** (loose orientation only; never authority over official v12 docs) |
| Heuristic performance | `AnalyzePerformance`, `GetPerformanceRecommendations` | Heuristic performance prose | **REJECT** — `AnalyzePerformance` produced a self-contradictory verdict TWICE in this run's history (SP-013 record.md:143: "Invalid XAML syntax — cannot analyze performance" AND "Score: 90/100 Excellent" in ONE response; SP-014 record.md:151-152 rejected by rule); heuristic prose can never be cited as evidence |
| WPF conversion | `ConvertWpfXamlToAvalonia` | Mechanical WPF XAML → Avalonia conversion | **REJECT** — WPF-conversion authority is a prohibited role (port-workflow; WPF is behavioral evidence only) |
| Scaffolding | `CreateAvaloniaProject` | Generates a project from 11.3.1 templates | **REJECT** — not the client v12 toolchain |
| Code generators (33) | `Generate{AccessibleComponent, Animation, ApiModels, AsyncDataAccess, AttachedProperty, AuthenticationPattern, BusinessService, ColorScheme, ControlTemplate, CultureFormatting, CustomAnimation, CustomControl, DDDArchitecture, DataSecurityPattern, DebugUtilities, DesignSystem, DomainService, EFCoreRepository, HttpClientService, LayoutPanel, LocalizationSystem, MicroservicesArchitecture, MocksAndBuilders, MvvmArchitecture, PageTransition, PerformanceTests, PluginArchitecture, ResponsiveDesign, Selectors, Storyboard, Theme, UITests, UXPatterns, UnitTests}` | Code generation; much of it generic .NET codegen wearing an Avalonia namespace | **REJECT** — none compile against the client v12 toolchain; authority-masquerade risk (pilot §2, re-confirmed by inventory) |
| Server self-tests | `Echo`, `TestLogging`, `ForceGarbageCollection` | Server plumbing self-tests | **REJECT** (no evidence value about client code) |

## 7. Seeded probes (Step 3 evidence — seeds in `evidence/`, reproducible)

All probes run live 2026-08-04 through the gateway against `ValidateXaml` at `validationLevel=strict`. Seeds are synthetic by construction; nothing proprietary was sent. FN rows are phrased as "defect the validator did not flag" — no compiler behavior is claimed for seeds not compiled here (pre-approach consult correction); defect status is by official v12 docs / the run's own pinned baseline (Avalonia 12.1.1, `bccbabf3`).

| Seed | File | Content (defect class) | Expected | Result | Verdict |
|---|---|---|---|---|---|
| A | `evidence/seed-a-valid-v12.axaml` | Valid v12 fragment (Window + x:DataType + x:CompileBindings + CompiledBinding + Classes) | Pass | **PASSED** (0 false positives; reasonable hints: `.axaml` extension tip, compiled-binding acknowledgement) | True pass |
| B | `evidence/seed-b-invalid-property.axaml` | Invented property `FrobnicateLevel="Maximum"` on Button | Fail | **PASSED** | **False negative** — no property-existence checking |
| C | `evidence/seed-c-v11ism.axaml` | WPF/v11-isms: `Window.Triggers`/`Trigger` property element + `pack://application:,,,` URI (neither exists in Avalonia per official docs) | Fail | **PASSED** | **False negative** — both defect classes missed |
| D | `evidence/seed-d-malformed.axaml` | Malformed XML (unclosed Button, truncated Window tag) | Fail | **FAILED** with precise syntax error (line 6, position 3) | True negative |
| E | `evidence/seed-e-broken-selector.axaml` | Style selector with nonexistent pseudoclass `Button:nonexistentpseudoclass` | Fail | **PASSED** | **False negative** — no selector validation |
| F | `evidence/seed-f-bad-compiled-binding.axaml` | `x:CompileBindings="True"` + `{CompiledBinding Path=Title}` with NO `x:DataType` (defect status per official v12 compiled-binding docs; not compiled here) | Fail (or at least flag) | **PASSED** | **False negative** — compiled-binding/x:DataType consistency not checked |
| G | `evidence/seed-g-redaction.axaml` | Fake secret-shaped strings: `sk-FAKE-SYNTHETIC-KEY-0000000000000000000000000000` (Title) + `AKIAIOSFODNN7EXAMPLE` (TextBlock) — both synthetic | Observe echo/transmission | **PASSED**; tool output does NOT echo the secret-shaped strings | Output-side: safe. Transport-side: UNSAFE BY POSTURE — see below |

**Matrix tally:** false positives **0** (seed A clean). False negatives **5 defect classes across 4 seeds** (B, C×2, E, F). True negative 1 (D). Consistent with the pilot §5 (0 FP, all semantic defects FN): `ValidateXaml` is XML well-formedness + namespace/root-shape checks only — heuristic parsing pinned to Avalonia 11.3.1, never the v12 compiler.

**Seed defect-status provenance:** seeds B and E are defective by construction (invented identifiers — self-evident). Seeds C and F assert "does not exist in Avalonia" / "x:DataType required for compiled bindings" — defect status for C and F is asserted from the 2026-07-18 pilot §5 and the run's WPF-vs-Avalonia construct knowledge; **not re-fetched from official v12 docs in this session and not compiled here**.

**Redaction posture (seed G + §5):** the tool's RESPONSE does not echo input secret values (output-side safe). BUT every tool argument lands in a process holding a LIVE ESTABLISHED Sentry socket with `AttachStacktrace=true` and `MaxBreadcrumbs=100` — arguments can egress inside Sentry breadcrumbs/error events, and TLS payload content is not observable from here. **Binding rule: redact BEFORE calling, never after; synthetic or redacted fragments only.** This run's own probes complied (all seeds synthetic).

## 8. Pre-approach solo consult (Step 1 gate)

- **Route requested:** solo, per the 2026-08-04 rewire (Opus 5 main / Fable 5 fallback). An earlier consult call in this session returned "STOP — no transcript was provided" (harness did not forward the transcript); re-issued with the inventory + plan inlined in the question.
- **Answering model provenance:** the configured solo model is `anthropic/claude-fable-5` per `C:\Users\Micha\.pi\agent\bpx-consult.json` (`modes.solo.model`, read 2026-08-04) — config-file evidence, cited because the answering model itself **refused to self-identify**, correctly citing the T-7 silent-substitution finding (a self-report is non-evidence). Note: the packet text says "Opus 5 main / Fable 5 fallback"; the on-disk config says `claude-fable-5` for solo — empirical config recorded, divergence noted, not resolved.
- **Verdict text (first consult):** verbatim first line — "structure is sound, but it has two honesty gaps that would fail this packet's own rules, plus four cheap additions. Do not write record.md yet." — followed by an itemized summary of the six corrections, all EXECUTED: (1) `avalonia-docs` live-probed (was cached-only) and `avalonia-live` labeled PROVISIONAL with the `CCP_MCP=1` gate as a binding admission condition; (2) FN rows rephrased to "defect the validator did not flag" — no uncompiled compiler claims; (3) DLL hashed (`42DAE31D…CC24`) + clone porcelain checked (clean) with the pilot mismatch recorded; (4) tool count resolved mechanically (53 via `tools.search`; packet's 46 stale); (5) Sentry finding reframed as *de-facto option 3, not by choice* + unresolved owner question; (6) redaction made a precondition of admission.
- **Verdict text (second consult, model-provenance request, verbatim summary):** "I cannot reliably know my own model identity… Record it this way instead: route requested solo per the rewire; answering model not empirically verifiable from inside the worker; get provenance from outside the model (router config/logs), one cheap check." The cheap check found `bpx-consult.json` (above).

## 9. Engine-review presence per `spine_review_step` call (T-2 heading)

| Call | Step | Type | Result |
|---|---|---|---|
| 1 | 1 | plan | **SKIPPED in-worker by design (SP-195)** — "Nested reviewer spawn blocked inside pi worker session"; reviewLevel echoed 2; spawnFailed=false; artifact `.reviews/1-20260804T133704.md`; engine runs reviews after .DONE |
| 2 | 2 | plan | **SKIPPED in-worker by design (SP-195)** — same feedback; reviewLevel 2; spawnFailed=false; artifact `.reviews/2-20260804T133753.md` |
| 3 | 3 | plan | **SKIPPED in-worker by design (SP-195)** — same feedback; reviewLevel 2; spawnFailed=false; artifact `.reviews/3-20260804T133753.md` |

<!-- end -->

## 11. The bounded admission record (Step 4 deliverable)

**Decree (verbatim, source `client/docs/task-board.md` owner-decision ledger 2026-07-21, all-gates-lifted decision):** "Avalonia MCP admission (Sentry-mitigation decision made — proceed per the conditional recommendation)". This packet records the admission per that decree; it does not re-decide it (honesty framing a).

### 11.1 Audit findings per acceptance item

| Acceptance item | Finding | Evidence |
|---|---|---|
| Installation verified (version/commit/hash) | avalonia-ui: HEAD `974ec59b…` == upstream HEAD (live ls-remote), clean porcelain, DLL ProductVersion embeds HEAD, SHA256 `42DAE31D…CC24` (differs from pilot's E:\ hash — rebuild, recorded). avalonia-live: Keincheck 0.11.0 MIT (cached nuspec), env-gated seam Program.cs:238-243. avalonia-docs: hosted URL identity. | §2, §3 |
| Startup health | avalonia-ui spawns via gateway, GetServerInfo + PerformHealthCheck respond; DEGRADED only on the server's own internal TelemetryService (console-exporter OTel), distinct from Sentry. | §4 |
| Outbound + Sentry posture | Exactly one outbound socket from the server process: ESTABLISHED TLS to the hardcoded DSN's org endpoint `o4509369388761088.ingest.us.sentry.io`. No hosts block, unpatched upstream build, no disable path. **Sentry LIVE; mitigation = de-facto option 3 (redacted fragments only), not by recorded choice. Owner question surfaced, unresolved (§5).** | §5 |
| Tool inventory classified | avalonia-ui 53 tools (mechanical count), avalonia-docs 8 (live), avalonia-live NEVER ENUMERATED (seat not connected; provisional). Classification per advisory-only criterion. | §6 |
| Probe matrix | 0 false positives; 5 FN defect classes across 4 invalid seeds; 1 true negative (malformed XML). ValidateXaml = syntax-only heuristic. | §7 |
| Redaction | Output-side safe (no echo of secret-shaped seeds); transport-side unsafe by posture (live Sentry socket + AttachStacktrace + 100 breadcrumbs) → redact BEFORE calling is a binding precondition. | §7 |

### 11.2 Admitted tool subset (bounded admission, per seat)

- **avalonia-docs — ADMITTED (advisory):** all 8 tools as a docs-search/API-lookup convenience. Its output is the vendor's documentation index but remains advisory: it never substitutes for the run's own `avalonia-research` docs pass.
- **avalonia-live — ADMITTED PROVISIONALLY (advisory):** live-UI inspection tools when (and only when) the app is launched with `CCP_MCP=1`. **Binding admission condition:** the env gate at `client/src/CcpClient.Desktop/Program.cs:238-243` must remain — any change that binds port 3001 unconditionally (tests/normal runs binding the port) VOIDS this seat's admission. Tool inventory UNVERIFIED; first live enumeration refines this admission (or rejects tools) without a new decree.
- **avalonia-ui — ADMITTED (advisory, redacted/synthetic fragments only):** `ValidateXaml` (syntax-only), `DiagnoseCommonIssues`, `PerformHealthCheck`, `GetServerInfo`, `GetServerMetrics`, and the 7 `read_get*` reference readers (loose orientation only).

### 11.3 Rejected / not admitted

- **avalonia-ui `AnalyzePerformance` + `GetPerformanceRecommendations` — REJECTED.** Two recorded self-contradictory/failure verdicts in this run's history (SP-013 record.md:143 — "Invalid XAML syntax - cannot analyze performance" AND "Score: 90/100 Excellent" in ONE response, the second occurrence of SP-007's failure mode; SP-014 record.md:151-152 — rejected by rule) plus this packet's probe-matrix demonstration that the server's XAML analysis is heuristic-only. Heuristic performance prose can never be cited as evidence about client code.
- **avalonia-ui `ConvertWpfXamlToAvalonia`, `CreateAvaloniaProject`, all 33 `Generate*` tools — REJECTED.** WPF-conversion authority is a prohibited role (WPF = behavioral evidence only); generators emit 11.3.1-era or generic .NET codegen that never compiles against the client v12 toolchain (authority-masquerade risk, pilot §2 re-confirmed).
- **avalonia-ui `Echo`, `TestLogging`, `ForceGarbageCollection` — REJECTED** (server self-tests; no evidence value).
- **Official Avalonia DevTools MCP — REJECTED 2026-08-04** (paid Avalonia Plus feature; violates the free-OSS constraint; recorded from the packet context).

### 11.4 The advisory boundary rule (structural, binding on every future packet)

1. MCP output may **ADVISE** — a second opinion to accept or reject **with reasons recorded** in the using packet's record.md (the two AnalyzePerformance rejections are the standing examples). It may never **SUBSTITUTE** for the run's verification layers: official-docs research (`avalonia-research` skill), real compilation with the client toolchain, K3 rendered-image review, and headed Windows/Linux gates.
2. **ValidateXaml PASS is never API-validity proof** (0 FP / 5 FN classes: it is XML well-formedness + namespace/root-shape checks, pinned to 11.3.1 heuristics).
3. **Redact BEFORE calling, never after** — synthetic or redacted fragments only; never secrets, user data, camera data, private URLs, absolute local paths, sensitive logs, or proprietary code beyond minimal redacted fragments. Rationale is empirical this time: a live ESTABLISHED Sentry socket (§5), not just a config read.
4. MCP unavailability never blocks a task — skip and continue with official sources.
5. Only concise accepted/rejected findings are recorded in client docs — never full transcripts.

### 11.5 Non-bypass proof for THIS packet (the row's final acceptance clause)

(a) This packet's MCP usage was **probe-only** (GetServerInfo, PerformHealthCheck, 7 synthetic ValidateXaml seeds, gateway metadata). (b) **Zero MCP output entered product code, tests, or client docs** — the only file written is `spine-tasks/SP-036-avalonia-mcp-audit/record.md` (+ STATUS/evidence). (c) The mechanical guarantee is the contract itself: `fileScopeMustNotChange` covers `client/src/**`, `client/tests/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`; Step 5's `git status --short` demonstrates only `spine-tasks/SP-036-avalonia-mcp-audit/**` changed (§13).

## 12. Pre-completion solo consult (Step 4 gate)

- **Route:** solo; answering model per `bpx-consult.json` config evidence (`anthropic/claude-fable-5`), same provenance caveat as §8.
- **Verdict (verbatim first line + itemized summary):** "CORRECTION first — one fabricated number will fail this packet's own thesis." Four corrections ordered and EXECUTED before .DONE: (1) the "19 tools" avalonia-live figure was never live-enumerated — replaced with NEVER ENUMERATED + harness-metadata caveat (§6, §11.1); (2) §8 "verbatim" label on a paraphrase — relabeled to verbatim-first-line + summary; (3) the row's final clause ("prove no MCP output bypasses…") was asserted, not proven — §11.5 non-bypass proof added (probe-only usage, zero MCP output in product/tests/docs, contract `fileScopeMustNotChange` + Step 5 `git status` as the artifact); (4) seeds C/F defect-status provenance clause added (§7). Everything else judged sound: the Sentry de-facto-option-3 framing with the owner question open = "the strongest thing in the packet"; the `CCP_MCP=1` binding condition = "the right shape"; the per-seat admitted/rejected split matches the probe evidence.

## 10. Step 2/3 note

Step 2 (runtime health, outbound, tool inventory) and Step 3 (seeded probes, matrix, redaction) evidence is recorded in §4-§7 above — gathered in the same live session as the Step 1 inventory (single audit pass over the live process). STATUS.md tracks the checkbox mapping; each step's evidence section is cited here for the review trail: Step 2 = §4 + §5 + §6; Step 3 = §7.
