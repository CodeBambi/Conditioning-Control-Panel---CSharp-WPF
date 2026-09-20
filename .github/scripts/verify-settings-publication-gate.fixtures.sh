#!/usr/bin/env bash
# Self-check for verify-settings-publication-gate.sh. Fabricates xunit-v3-shaped XML for every
# counterexample the repair review named and asserts the verdict + exit code. Runs anywhere, in
# under a second, and needs no .NET: `bash .github/scripts/verify-settings-publication-gate.fixtures.sh`.
# The load-bearing property: NO fixture other than the genuine red/green pair may PASS.
set -uo pipefail

gate="$(cd "$(dirname "$0")" && pwd)/verify-settings-publication-gate.sh"
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
release=HeldReaderReleaseAllowsPublicationAndCleansItsTempFile
fails=0

# $1 file, $2 assembly attrs, $3.. test elements
xml() {
  local f=$1 attrs=$2; shift 2
  { echo '<?xml version="1.0" encoding="utf-8"?>'
    echo '<assemblies>'
    echo "<assembly environment=\"64-bit .NET 8.0.21\" $attrs>"
    echo '<collection name="c">'
    printf '%s\n' "$@"
    echo '</collection></assembly></assemblies>'
  } >"$f"
}

test_el() { # name(method) result [failure-xml]
  echo "<test name=\"CCP.Core.Settings.Tests.SettingsPublicationTests.$1\" type=\"CCP.Core.Settings.Tests.SettingsPublicationTests\" method=\"$1\" result=\"$2\">${3:-}</test>"
}

failure_el() { # exception-type message stack
  echo "<failure exception-type=\"$1\"><message><![CDATA[$2]]></message><stack-trace><![CDATA[$3]]></stack-trace></failure>"
}

# The genuine baseline red: after the reader is released the value is still the seeded "ja".
expected_failure=$(failure_el 'Xunit.Sdk.EqualException' \
  'Assert.Equal() Failure: Strings differ
Expected: "fr"
Actual:   "ja"' \
  "   at CCP.Core.Settings.Tests.SettingsPublicationTests.$release() in D:\\a\\x\\SettingsPublicationTests.cs:line 118")

others_pass() {
  test_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile Pass
  test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass
  test_el LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader Pass
  test_el ConcurrentSavesPublishOnceAndLeaveNoTempFiles Pass
}

green() { xml "$1" 'total="5" passed="5" failed="0" skipped="0" errors="0"' \
  "$(others_pass)" "$(test_el "$release" Pass)"; }

# baseline_xml, with the release test failing for $1 reason
red_with() { # target-file failure-xml [assembly-attrs]
  xml "$1" "${3:-total=\"5\" passed=\"4\" failed=\"1\" skipped=\"0\" errors=\"0\"}" \
    "$(others_pass)" "$(test_el "$release" Fail "$2")"
}

check() { # label expected-verdict expected-exit baseline_xml candidate_xml baseline_exit candidate_exit
  local label=$1 want=$2 want_exit=$3; shift 3
  local out rc
  out=$(bash "$gate" "$@" 2>&1); rc=$?
  local got=${out%%$'\n'*}
  if [[ $got == "VERDICT: $want" && $rc == "$want_exit" ]]; then
    printf '[ok]   %-46s %s exit=%s\n' "$label" "$got" "$rc"
  else
    fails=$((fails + 1))
    printf '[FAIL] %-46s got "%s" exit=%s (wanted VERDICT: %s exit=%s)\n' \
      "$label" "$got" "$rc" "$want" "$want_exit"
    printf '%s\n' "$out" | sed 's/^/         | /'
  fi
}

green "$tmp/candidate.xml"
red_with "$tmp/baseline-good.xml" "$expected_failure"

# 1. the only PASS: genuine red baseline, nonzero exit, green candidate.
check "genuine red baseline + green candidate" PASS 0 \
  "$tmp/baseline-good.xml" "$tmp/candidate.xml" 1 0

# 2. review blocker 1: setup / seed / profile-ownership exception is NOT the regression.
red_with "$tmp/b-setup.xml" "$(failure_el 'System.InvalidOperationException' \
  'CorePaths.UserData is not the owned settings-publication profile: C:\Users\runneradmin\AppData\Roaming\CCP' \
  "   at CCP.Core.Settings.Tests.SettingsPublicationTestProfile.AssertOwned()
   at CCP.Core.Settings.Tests.SettingsPublicationTests.Seed(String language)")"
check "setup/profile-ownership exception" INCONCLUSIVE 2 \
  "$tmp/b-setup.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-open.xml" "$(failure_el 'System.IO.IOException' \
  'The process cannot access the file settings.json because it is being used by another process.' \
  "   at System.IO.FileStream.ValidateFileHandle()
   at CCP.Core.Settings.Tests.SettingsPublicationTests.HoldReader(FileShare share)")"
check "file-open IOException during setup" INCONCLUSIVE 2 \
  "$tmp/b-open.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-timeout.xml" "$(failure_el 'System.TimeoutException' \
  'The operation has timed out.' \
  "   at System.Threading.Tasks.Task.WaitAsync(TimeSpan timeout)
   at CCP.Core.Settings.Tests.SettingsPublicationTests.$release()")"
check "task timeout awaiting the save" INCONCLUSIVE 2 \
  "$tmp/b-timeout.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-cancel.xml" "$(failure_el 'System.OperationCanceledException' \
  'The operation was canceled.' \
  "   at CCP.Core.Settings.Tests.SettingsPublicationTests.$release()")"
check "cancellation" INCONCLUSIVE 2 \
  "$tmp/b-cancel.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-missedtemp.xml" "$(failure_el 'Xunit.Sdk.XunitException' \
  'publication did not leave its flushed temp file behind within 2s' \
  "   at CCP.Core.Settings.Tests.SettingsPublicationTests.WaitForPublicationTemp()")"
check "missed transient temp observation" INCONCLUSIVE 2 \
  "$tmp/b-missedtemp.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-unknown.xml" "$(failure_el 'System.Exception' 'something else went wrong' '   at Unknown()')"
check "unrecognized failure signature" INCONCLUSIVE 2 \
  "$tmp/b-unknown.xml" "$tmp/candidate.xml" 1 0

# fr/ja mismatch, but raised from a different test: not attributable to the regression.
red_with "$tmp/b-wrongtest.xml" "$(failure_el 'Xunit.Sdk.EqualException' \
  'Assert.Equal() Failure: Strings differ
Expected: "fr"
Actual:   "ja"' \
  '   at CCP.Core.Settings.Tests.SettingsPublicationTests.NoReaderPublishesTheLatestSettingsAndCleansItsTempFile()')"
check "fr/ja assertion from a different test" INCONCLUSIVE 2 \
  "$tmp/b-wrongtest.xml" "$tmp/candidate.xml" 1 0

# 3. review blocker 2: exit-code and summary-completeness requirements.
check "failing XML but baseline exit 0" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/candidate.xml" 0 0
check "baseline exit file missing" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/candidate.xml" missing 0

red_with "$tmp/b-nosummary.xml" "$expected_failure" 'total="5" passed="4" failed="1" errors="0"'
check "baseline summary missing skipped" INCONCLUSIVE 2 \
  "$tmp/b-nosummary.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-inconsistent.xml" "$expected_failure" 'total="5" passed="1" failed="1" skipped="0" errors="0"'
check "baseline summary inconsistent" INCONCLUSIVE 2 \
  "$tmp/b-inconsistent.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-errors.xml" "$expected_failure" 'total="5" passed="4" failed="1" skipped="0" errors="1"'
check "baseline assembly-level errors" INCONCLUSIVE 2 \
  "$tmp/b-errors.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-skips.xml" "$expected_failure" 'total="5" passed="0" failed="1" skipped="4" errors="0"'
check "baseline skipped the Windows-only cases" INCONCLUSIVE 2 \
  "$tmp/b-skips.xml" "$tmp/candidate.xml" 1 0

check "baseline unexpectedly green" INCONCLUSIVE 2 \
  "$tmp/candidate.xml" "$tmp/candidate.xml" 1 0

# 4. review blocker 3: missing artefacts after a build failure / step timeout are not evidence.
: >"$tmp/empty.xml"
check "baseline XML absent (build/run skipped)" INCONCLUSIVE 2 \
  "$tmp/absent.xml" "$tmp/candidate.xml" missing 0
check "candidate XML empty (run timed out)" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/empty.xml" 1 missing
check "candidate exit file missing" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/candidate.xml" 1 missing
check "both revisions produced nothing" INCONCLUSIVE 2 \
  "$tmp/absent.xml" "$tmp/absent.xml" missing missing

# 5. a genuinely red candidate is still a FAIL, not an INCONCLUSIVE.
xml "$tmp/c-red.xml" 'total="5" passed="4" failed="1" skipped="0" errors="0"' \
  "$(others_pass)" "$(test_el "$release" Fail "$expected_failure")"
check "candidate 4/5" FAIL 1 "$tmp/baseline-good.xml" "$tmp/c-red.xml" 1 1

echo
if (( fails )); then echo "$fails fixture(s) FAILED"; exit 1; fi
echo "all fixtures behaved as specified"
