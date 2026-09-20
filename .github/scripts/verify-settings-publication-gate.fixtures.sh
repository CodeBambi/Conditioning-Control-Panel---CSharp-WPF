#!/usr/bin/env bash
# Self-check for verify-settings-publication-gate.sh. Fabricates xunit-v3-shaped XML for every
# counterexample the repair review named and asserts the verdict + exit code. Runs anywhere, in
# under a second, and needs no .NET: `bash .github/scripts/verify-settings-publication-gate.fixtures.sh`.
# The load-bearing property: NO fixture other than the genuine red/green pair may PASS.
set -uo pipefail

gate="$(cd "$(dirname "$0")" && pwd)/verify-settings-publication-gate.sh"
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
release=ObservedPublicationFailureThatReleasesTheReaderRecoversAndPublishes
approved_skip=NonTransientPublicationFailureNotifiesOnceAndStaysObservable
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

# The four non-release executable records: one no-reader case, the two persistent-hold theory
# rows, and the later-save case.
others_pass() {
  test_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile Pass
  test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass
  test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass
  test_el LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader Pass
}

# The one approved skip: the directory-target non-transient proof is Linux-only by review.
skip_el() { test_el "${1:-$approved_skip}" Skip; }

green() { xml "$1" 'total="6" passed="5" failed="0" skipped="1" errors="0"' \
  "$(others_pass)" "$(test_el "$release" Pass)" "$(skip_el)"; }

# baseline_xml, with the release test failing for $1 reason
red_with() { # target-file failure-xml [assembly-attrs]
  xml "$1" "${3:-total=\"6\" passed=\"4\" failed=\"1\" skipped=\"1\" errors=\"0\"}" \
    "$(others_pass)" "$(test_el "$release" Fail "$2")" "$(skip_el)"
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

red_with "$tmp/b-nosummary.xml" "$expected_failure" 'total="6" passed="4" failed="1" errors="0"'
check "baseline summary missing skipped" INCONCLUSIVE 2 \
  "$tmp/b-nosummary.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-inconsistent.xml" "$expected_failure" 'total="6" passed="1" failed="1" skipped="1" errors="0"'
check "baseline summary inconsistent" INCONCLUSIVE 2 \
  "$tmp/b-inconsistent.xml" "$tmp/candidate.xml" 1 0

red_with "$tmp/b-errors.xml" "$expected_failure" 'total="6" passed="4" failed="1" skipped="1" errors="1"'
check "baseline assembly-level errors" INCONCLUSIVE 2 \
  "$tmp/b-errors.xml" "$tmp/candidate.xml" 1 0

xml "$tmp/b-skips.xml" 'total="6" passed="0" failed="1" skipped="5" errors="0"' \
  "$(skip_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile)" \
  "$(skip_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile)" \
  "$(skip_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile)" \
  "$(skip_el LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader)" \
  "$(test_el "$release" Fail "$expected_failure")" "$(skip_el)"
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

# 4b. review blocker 4: nonempty but INCOMPLETE XML is not evidence either. Every one of these
# passes the old attribute greps; none may PASS now that the document is really parsed.

# header-only: the exact raw artefact the review quoted, never closed.
printf '%s\n' '<?xml version="1.0" encoding="utf-8"?>' '<assemblies>' \
  '<assembly environment="64-bit .NET 8.0.21" total="6" passed="5" failed="0" skipped="1" errors="0">' \
  >"$tmp/c-headeronly.xml"
check "candidate header-only XML (unclosed)" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-headeronly.xml" 1 0
printf '%s\n' '<?xml version="1.0" encoding="utf-8"?>' '<assemblies>' \
  '<assembly environment="64-bit .NET 8.0.21" total="6" passed="4" failed="1" skipped="1" errors="0">' \
  >"$tmp/b-headeronly.xml"
check "baseline header-only XML (unclosed)" INCONCLUSIVE 2 \
  "$tmp/b-headeronly.xml" "$tmp/candidate.xml" 1 0

# closed but empty: well-formed, claims five results, carries none.
xml "$tmp/c-emptyassembly.xml" 'total="6" passed="5" failed="0" skipped="1" errors="0"'
check "candidate claims 5 passed with no test records" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-emptyassembly.xml" 1 0
xml "$tmp/b-emptyassembly.xml" 'total="6" passed="4" failed="1" skipped="1" errors="0"'
check "baseline claims a failure with no test records" INCONCLUSIVE 2 \
  "$tmp/b-emptyassembly.xml" "$tmp/candidate.xml" 1 0

# truncated immediately after the recognized failure text: every inspected field intact.
sed "s#</stack-trace>.*##" "$tmp/baseline-good.xml" >"$tmp/b-truncated.xml"
grep -qF 'Actual' "$tmp/b-truncated.xml" || { echo "[FAIL] truncation fixture lost its failure text"; fails=$((fails + 1)); }
check "baseline truncated after the failure text" INCONCLUSIVE 2 \
  "$tmp/b-truncated.xml" "$tmp/candidate.xml" 1 0
sed "s#</collection>.*##" "$tmp/candidate.xml" >"$tmp/c-truncated.xml"
check "candidate truncated before its close" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-truncated.xml" 1 0

# fewer real records than the summary claims, and unknown/absent result values.
xml "$tmp/c-short.xml" 'total="6" passed="5" failed="0" skipped="1" errors="0"' \
  "$(test_el "$release" Pass)" "$(test_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile Pass)"
check "candidate has 2 records but claims 6" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-short.xml" 1 0
xml "$tmp/c-unknown.xml" 'total="6" passed="5" failed="0" skipped="1" errors="0"' \
  "$(others_pass)" "$(test_el "$release" NotRun)" "$(skip_el)"
check "candidate record with an unknown result" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-unknown.xml" 1 0
xml "$tmp/b-unknown-result.xml" 'total="6" passed="4" failed="1" skipped="1" errors="0"' \
  "$(others_pass)" "<test name=\"x\" method=\"$release\">$expected_failure</test>" "$(skip_el)"
check "baseline release record with no result attribute" INCONCLUSIVE 2 \
  "$tmp/b-unknown-result.xml" "$tmp/candidate.xml" 1 0

# ambiguous duplicate evidence: the same test reported twice.
xml "$tmp/b-dup.xml" 'total="6" passed="3" failed="2" skipped="1" errors="0"' \
  "$(test_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile Pass)" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass)" \
  "$(test_el LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader Pass)" \
  "$(test_el "$release" Fail "$expected_failure")" "$(test_el "$release" Fail "$expected_failure")" \
  "$(skip_el)"
check "baseline reports the release test twice" INCONCLUSIVE 2 \
  "$tmp/b-dup.xml" "$tmp/candidate.xml" 1 0

# not XML at all / wrong document.
printf 'MSBuild version 17\nerror MSB1009\n' >"$tmp/b-notxml.xml"
check "baseline artefact is not XML" INCONCLUSIVE 2 \
  "$tmp/b-notxml.xml" "$tmp/candidate.xml" 1 0
printf '%s\n' '<?xml version="1.0"?><testsuite tests="5" failures="1"/>' >"$tmp/b-wrongroot.xml"
check "baseline artefact has the wrong root element" INCONCLUSIVE 2 \
  "$tmp/b-wrongroot.xml" "$tmp/candidate.xml" 1 0

# 5. a genuinely red candidate is still a FAIL, not an INCONCLUSIVE.
xml "$tmp/c-red.xml" 'total="6" passed="4" failed="1" skipped="1" errors="0"' \
  "$(others_pass)" "$(test_el "$release" Fail "$expected_failure")" "$(skip_el)"
check "candidate 4/5" FAIL 1 "$tmp/baseline-good.xml" "$tmp/c-red.xml" 1 1

# 5b. the skip contract: exactly one skip, exactly the approved case, on BOTH revisions. An
# extra, missing, misnamed or duplicated skip is unexplained evidence - never a PASS.
xml "$tmp/c-noskip.xml" 'total="6" passed="6" failed="0" skipped="0" errors="0"' \
  "$(others_pass)" "$(test_el "$release" Pass)" "$(test_el "$approved_skip" Pass)"
check "candidate has no skip at all" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-noskip.xml" 1 0

xml "$tmp/c-misnamed-skip.xml" 'total="6" passed="5" failed="0" skipped="1" errors="0"' \
  "$(others_pass)" "$(test_el "$release" Pass)" \
  "$(skip_el NonTransientPublicationFailureNotifiesOnce)"
check "candidate skip has a different name" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-misnamed-skip.xml" 1 0

xml "$tmp/c-extra-skip.xml" 'total="6" passed="4" failed="0" skipped="2" errors="0"' \
  "$(test_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile Pass)" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass)" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass)" \
  "$(test_el "$release" Pass)" \
  "$(skip_el LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader)" "$(skip_el)"
check "candidate skipped an extra Windows case" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-extra-skip.xml" 1 0

xml "$tmp/c-skipped-release.xml" 'total="6" passed="4" failed="0" skipped="2" errors="0"' \
  "$(others_pass)" "$(skip_el "$release")" "$(skip_el)"
check "candidate skipped the release regression" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-skipped-release.xml" 1 0

xml "$tmp/c-dup-skip.xml" 'total="6" passed="4" failed="0" skipped="2" errors="0"' \
  "$(test_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile Pass)" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass)" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass)" \
  "$(test_el "$release" Pass)" "$(skip_el)" "$(skip_el)"
check "candidate reports the approved skip twice" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-dup-skip.xml" 1 0

xml "$tmp/b-misnamed-skip.xml" 'total="6" passed="4" failed="1" skipped="1" errors="0"' \
  "$(others_pass)" "$(test_el "$release" Fail "$expected_failure")" \
  "$(skip_el LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader)"
check "baseline skip has a different name" INCONCLUSIVE 2 \
  "$tmp/b-misnamed-skip.xml" "$tmp/candidate.xml" 1 0

# 5c. the INSTRUMENTED baseline: the observer seam is applied, no retry. Its persistent-hold
# attempt-count failures are expected - and cannot stand in for the fr/ja release assertion.
hold_failure=$(failure_el 'Xunit.Sdk.EqualException' \
  'Assert.Equal() Failure: Values differ
Expected: 6
Actual:   1' \
  '   at CCP.Core.Settings.Tests.SettingsPublicationTests.PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile(FileShare share)')

xml "$tmp/b-instrumented.xml" 'total="6" passed="2" failed="3" skipped="1" errors="0"' \
  "$(test_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile Pass)" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Fail "$hold_failure")" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Fail "$hold_failure")" \
  "$(test_el LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader Pass)" \
  "$(test_el "$release" Fail "$expected_failure")" "$(skip_el)"
check "instrumented baseline: hold failures + fr/ja" PASS 0 \
  "$tmp/b-instrumented.xml" "$tmp/candidate.xml" 1 0

# same run WITHOUT the release failure: attempt-count reds alone prove nothing.
xml "$tmp/b-holdonly.xml" 'total="6" passed="3" failed="2" skipped="1" errors="0"' \
  "$(test_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile Pass)" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Fail "$hold_failure")" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Fail "$hold_failure")" \
  "$(test_el LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader Pass)" \
  "$(test_el "$release" Pass)" "$(skip_el)"
check "baseline: hold failures but release passed" INCONCLUSIVE 2 \
  "$tmp/b-holdonly.xml" "$tmp/candidate.xml" 1 0

# the observer never fired, so the test failed as a declared SETUP failure: not the regression.
xml "$tmp/b-noobserver.xml" 'total="6" passed="4" failed="1" skipped="1" errors="0"' \
  "$(others_pass)" \
  "$(test_el "$release" Fail "$(failure_el 'Xunit.Sdk.TrueException' \
     'setup failure: the held reader did not make any publication attempt fail, so this run proves nothing.' \
     "   at CCP.Core.Settings.Tests.SettingsPublicationTests.$release()")")" "$(skip_el)"
check "baseline observed no publication failure" INCONCLUSIVE 2 \
  "$tmp/b-noobserver.xml" "$tmp/candidate.xml" 1 0

# the old 5/0 shape (no skip record at all) must no longer be accepted on either side.
xml "$tmp/b-oldshape.xml" 'total="5" passed="4" failed="1" skipped="0" errors="0"' \
  "$(others_pass)" "$(test_el "$release" Fail "$expected_failure")"
xml "$tmp/c-oldshape.xml" 'total="5" passed="5" failed="0" skipped="0" errors="0"' \
  "$(others_pass)" "$(test_el "$release" Pass)"
check "old 5/0 baseline shape" INCONCLUSIVE 2 \
  "$tmp/b-oldshape.xml" "$tmp/candidate.xml" 1 0
check "old 5/0 candidate shape" INCONCLUSIVE 2 \
  "$tmp/baseline-good.xml" "$tmp/c-oldshape.xml" 1 0

# 6. what the real Windows run actually emitted (run 35540142196), which the Linux-only fixtures
# above did not reproduce.

# 6a. xunit v3 escapes the quotes in a string diff: `Expected: \"fr\"`, not `Expected: "fr"`.
escaped_failure=$(failure_el 'Xunit.Sdk.EqualException' \
  'Assert.Equal() Failure: Strings differ
           ↓ (pos 0)
Expected: \"fr\"
Actual:   \"ja\"
           ↑ (pos 0)' \
  "   at CCP.Core.Settings.Tests.SettingsPublicationTests.$release() in D:\\a\\x\\SettingsPublicationTests.cs:line 114")
red_with "$tmp/b-escaped.xml" "$escaped_failure"
check "red baseline with xunit-escaped quotes" PASS 0 \
  "$tmp/b-escaped.xml" "$tmp/candidate.xml" 1 0

# and the escaping must not smuggle in a different expectation: the real baseline's OTHER failure
# was de/ja in LaterSaveWins…, which is not the authorized substitute for the release assertion.
red_with "$tmp/b-escaped-de.xml" "$(failure_el 'Xunit.Sdk.EqualException' \
  'Assert.Equal() Failure: Strings differ
Expected: \"de\"
Actual:   \"ja\"' \
  "   at CCP.Core.Settings.Tests.SettingsPublicationTests.$release()")"
check "escaped quotes but de/ja, not fr/ja" INCONCLUSIVE 2 \
  "$tmp/b-escaped-de.xml" "$tmp/candidate.xml" 1 0

# the real observed pair: release test failed only on the transient temp (FailException), while a
# DIFFERENT test carried the de/ja EqualException. Two failures, still no proven contention.
xml "$tmp/b-real.xml" 'total="6" passed="3" failed="2" skipped="1" errors="0"' \
  "$(test_el NoReaderPublishesTheLatestSettingsAndCleansItsTempFile Pass)" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass)" \
  "$(test_el PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile Pass)" \
  "$(test_el LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader Fail "$(failure_el 'Xunit.Sdk.EqualException' \
      'Assert.Equal() Failure: Strings differ
Expected: \"de\"
Actual:   \"ja\"' '   at CCP.Core.Settings.Tests.SettingsPublicationTests.LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader()')")" \
  "$(test_el "$release" Fail "$(failure_el 'Xunit.Sdk.FailException' \
      'SaveImmediate did not leave its flushed temp file behind while the settings reader was held.' \
      "   at CCP.Core.Settings.Tests.SettingsPublicationTests.WaitForPublicationTemp()
   at CCP.Core.Settings.Tests.SettingsPublicationTests.$release()")")" "$(skip_el)"
check "real run 35540142196 baseline shape" INCONCLUSIVE 2 \
  "$tmp/b-real.xml" "$tmp/candidate.xml" 1 0

# 6b. Windows Python writes stdout in text mode, so every parser line arrives CRLF-terminated.
# Simulated with a python3 shim on PATH that appends CR; verdicts must be identical to 1 and 5.
mkdir -p "$tmp/bin"
real_py=$(command -v python3 || command -v python)
if [[ -n ${real_py:-} ]]; then
  cat >"$tmp/bin/python3" <<EOF
#!/usr/bin/env bash
"$real_py" "\$@" | sed -e 's/\$/\r/'
exit \${PIPESTATUS[0]}
EOF
  chmod +x "$tmp/bin/python3"
  "$tmp/bin/python3" -c 'print("x")' | grep -q $'\r' ||
    { echo "[FAIL] CRLF shim does not actually emit CR"; fails=$((fails + 1)); }
  crlf_check() { local old=$PATH; PATH="$tmp/bin:$PATH"; check "$@"; PATH=$old; }
  crlf_check "CRLF parser output: genuine red + green" PASS 0 \
    "$tmp/baseline-good.xml" "$tmp/candidate.xml" 1 0
  crlf_check "CRLF parser output: real baseline shape" INCONCLUSIVE 2 \
    "$tmp/b-real.xml" "$tmp/candidate.xml" 1 0
  crlf_check "CRLF parser output: candidate 4/5" FAIL 1 \
    "$tmp/baseline-good.xml" "$tmp/c-red.xml" 1 1
  crlf_check "CRLF parser output: truncated baseline" INCONCLUSIVE 2 \
    "$tmp/b-truncated.xml" "$tmp/candidate.xml" 1 0
else
  echo "[FAIL] no python3/python available to build the CRLF shim"; fails=$((fails + 1))
fi

echo
if (( fails )); then echo "$fails fixture(s) FAILED"; exit 1; fi
echo "all fixtures behaved as specified"
