# Step 1 probe log (verbatim)

All probes against the pinned stack: xunit.v3 3.2.2 + Microsoft.NET.Test.Sdk 17.10.0 +
xunit.runner.visualstudio 3.1.5 (both projects), .NET SDK 10.0.303, no global.json,
no runsettings, no xunit.runner.json, no TestingPlatform* MSBuild properties anywhere
under client/ (grep-verified).

## Probe A — `dotnet test --help`
Invocation: `dotnet test --help` (SDK 10.0.303)
Response: option list contains only VSTest surface (`--logger`, `--results-directory`,
`--collect`, `--blame*`, `-c/-f/-r/-a`). **No `--minimum-expected-tests`, no MTP options
listed anywhere in the help text.**

## Probe B — skip baseline, no config
Invocation: `CCP_DATA_ROOT="$TMPD/x" dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo --no-build --filter "FullyQualifiedName~DefaultSettingsPath_EnvUnset"`
Response:
```
[xUnit.net ...]     CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault [SKIP]
Skipped! - Failed:     0, Passed:     0, Skipped:     1, Total:     1, Duration: 48 ms - CcpClient.Tests.dll (net10.0)
```
**Exit code: 0** (measured via PIPESTATUS, not tail). An unexpected skip exits ZERO. This is the defect.

## Probe C — xunit v3 `failSkips` via runsettings
Invocation: same as B plus `--settings "$TMPD/probe.runsettings"` where the file is:
```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <xunit>
    <failSkips>true</failSkips>
  </xunit>
</RunSettings>
```
Response:
```
[xUnit.net ...]     CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault [SKIP]
Skipped! - Failed:     0, Passed:     0, Skipped:     1, Total:     1, Duration: 39 ms
```
**Exit code: 0. NOT honored** in this configuration (VSTest adapter 3.1.5 does not read
`<xunit>` runsettings nodes; test still reports [SKIP]).

## Probe D — xunit v3 `failSkips` via xunit.runner.json beside the test assembly
Invocation: `printf '{"failSkips": true}' > client/tests/CcpClient.Tests/bin/Debug/net10.0/xunit.runner.json`,
then same as B; file removed afterwards.
Response:
```
FAIL_SKIP : CCP_DATA_ROOT override is active at the guard checkpoint (leak class: runner-set override in the external process environment) ...
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 43 ms
```
**Exit code: 1. HONORED** — the skip converts to a failure. (First attempt at this probe
raced a parallel call and was re-run serially; the clean re-run is what is recorded.)

## Probe E — MTP `--minimum-expected-tests` reachability
Invocation: `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo --no-build --filter "FullyQualifiedName~DefaultSettingsPath_EnvUnset" -- --minimum-expected-tests 9999`
Response:
```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 46 ms
```
**Exit code: 0 with ONE test run against a floor of 9999. The flag is silently swallowed**
(it travels as a runsettings argument in VSTest mode). **MTP is NOT reachable in this
configuration** — no global.json `test.runner` opt-in, no `TestingPlatformDotnetTestSupport`
property, help lists no MTP options. An MTP flag here would be false confidence.

## Probe F — TRX shape for post-processing
Invocation: `dotnet test ... --results-directory "$TMPD" --logger "trx;LogFileName=results.trx"`
Response: exactly one `results.trx` written to the out-of-worktree dir; relevant verbatim:
```xml
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="..." name="..." runUser="..." xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Times creation="2026-08-13T12:38:07.3188169+09:00" queuing="...
<ResultSummary outcome="Completed">
<Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
```
(skipped tests surface as `notExecuted`). `--results-directory` accepts an absolute path
outside the worktree.

## Probe hygiene note
Probe A and B's first exit-code reads used `cmd | tail; echo $?` which reports TAIL's exit
code — caught and re-measured with `${PIPESTATUS[0]}`. All exit codes above are the real
dotnet-test exit codes.
