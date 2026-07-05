#!/usr/bin/env bash
# =============================================================================
# run-gates.sh - the ONE command that validates any port change.
# Usage (from repo root, git-bash on Windows or any POSIX shell):
#   ./Tools/run-gates.sh            # all 4 gates
#   ./Tools/run-gates.sh --fast     # skip the smoke test (builds + unit tests)
#
# Exit code 0 = ALL gates passed. Non-zero = at least one failed; read output.
# A mechanical-tier model should run this after EVERY change and before EVERY
# commit. Floors live here in one place; raise them when tests are added,
# never lower them.
# =============================================================================
set -u

CORE_TEST_FLOOR=540          # raise when adding tests; NEVER lower
SMOKE_TABS_EXPECTED=44
SMOKE_FINDINGS_EXPECTED=5

cd "$(dirname "$0")/../.." || exit 1
FAIL=0
say() { printf '%s\n' "$*"; }

say "=== GATE 1/4: Avalonia desktop solution build ==="
if dotnet build ConditioningControlPanel/CCP.Desktop.slnf -clp:ErrorsOnly --nologo -v q 2>&1 | grep -qE "Build succeeded"; then
  say "PASS: slnf build 0 errors"
else
  say "FAIL: CCP.Desktop.slnf build has errors (re-run without -v q to see them)"; FAIL=1
fi

say "=== GATE 2/4: legacy WPF solution build ==="
if dotnet build ConditioningControlPanel.sln -clp:ErrorsOnly --nologo -v q 2>&1 | grep -qE "Build succeeded"; then
  say "PASS: WPF sln build 0 errors"
else
  say "FAIL: ConditioningControlPanel.sln build has errors"; FAIL=1
fi

say "=== GATE 3/4: Core unit tests (floor: ${CORE_TEST_FLOOR}) ==="
TEST_OUT=$(dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj --nologo -v q 2>&1 | tail -3)
PASSED=$(printf '%s' "$TEST_OUT" | grep -oE "Passed:[ ]*[0-9]+" | grep -oE "[0-9]+" | head -1)
FAILED=$(printf '%s' "$TEST_OUT" | grep -oE "Failed:[ ]*[0-9]+" | grep -oE "[0-9]+" | head -1)
if [ "${FAILED:-1}" = "0" ] && [ "${PASSED:-0}" -ge "$CORE_TEST_FLOOR" ]; then
  say "PASS: ${PASSED} tests, 0 failed (floor ${CORE_TEST_FLOOR})"
else
  say "FAIL: tests passed=${PASSED:-?} failed=${FAILED:-?} (need 0 failed and >= ${CORE_TEST_FLOOR} passed)"; FAIL=1
fi

if [ "${1:-}" = "--fast" ]; then
  say "=== GATE 4/4: SKIPPED (--fast). Run the full script before committing. ==="
else
  say "=== GATE 4/4: Windows head smoke test (takes a few minutes) ==="
  SMOKE_LOG="${TMPDIR:-/tmp}/ccp-smoke-$$.log"
  dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --smoke-test > "$SMOKE_LOG" 2>&1
  TABS=$(grep -oE "Tabs visited: [0-9]+" "$SMOKE_LOG" | grep -oE "[0-9]+" | head -1)
  FINDINGS=$(grep -oE "Findings: [0-9]+" "$SMOKE_LOG" | grep -oE "[0-9]+" | head -1)
  UNHANDLED=$(grep -c "Unhandled exception" "$SMOKE_LOG")
  if [ "${TABS:-0}" = "$SMOKE_TABS_EXPECTED" ] && [ "${FINDINGS:-99}" = "$SMOKE_FINDINGS_EXPECTED" ] && [ "${UNHANDLED}" = "0" ]; then
    say "PASS: smoke ${TABS} tabs / Findings: ${FINDINGS} / 0 unhandled"
  elif [ "${UNHANDLED}" != "0" ] && grep -q "SerializeChanges" "$SMOKE_LOG"; then
    say "FLAKE?: known cross-thread brush crash signature (task-board row). Re-run this script ONCE."
    say "        Two crashes in a row = STOP and file a blocker. Log: $SMOKE_LOG"
    FAIL=1
  else
    say "FAIL: smoke tabs=${TABS:-?} findings=${FINDINGS:-?} unhandled=${UNHANDLED}. Log: $SMOKE_LOG"
    FAIL=1
  fi
fi

say ""
if [ "$FAIL" = "0" ]; then say ">>> ALL GATES PASSED <<<"; else say ">>> GATE FAILURE - do not commit. Fix or file a blocker per the mechanical-port-work skill. <<<"; fi
exit $FAIL
