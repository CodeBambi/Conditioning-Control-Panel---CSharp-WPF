#!/usr/bin/env bash
# Classifies one baseline/candidate pair of xunit v3 XML results for the settings-publication
# red/green validation. It NEVER re-runs anything: it only reads exit codes and raw XML that
# already exist, so a timing artefact cannot be retried into a green.
#
# PASS  - baseline failed the release-recovery regression for a POSITIVELY identified reason
#   (the post-release `Assert.Equal("fr", …)` publication assertion, attributed by stack trace to
#   that test) AND candidate is 5/5.
# INCONCLUSIVE (exit 2) - baseline passed unexpectedly, failed only because the test never
#   observed the transient temp file (the timing hole called out in the repair review), failed for
#   any setup/seed/profile/open/cancel/timeout or otherwise unrecognized reason, produced an
#   incomplete or self-inconsistent summary, produced a zero/absent exit code, or the host died
#   before producing results. That is a failed validation gate, not a retry signal.
# FAIL (exit 1) - candidate did not reach 5 passed / 0 failed / 0 skipped.
# Missing exit files are passed in as anything non-numeric (e.g. "missing") and are never treated
# as valid evidence.
set -uo pipefail

baseline_xml=$1
candidate_xml=$2
baseline_exit=$3
candidate_exit=$4

# The release-recovery regression: the reader is released mid-publication, so a bounded retry
# must succeed. This is the one case whose baseline failure is the point of the exercise.
release_test='HeldReaderReleaseAllowsPublicationAndCleansItsTempFile'
# Emitted by WaitForPublicationTemp; means the test never saw contention, i.e. proves nothing.
missed_temp='did not leave its flushed temp file behind'
# The ONLY baseline failure this gate accepts: after the reader is released the baseline never
# publishes, so the re-read language is still the seeded "ja" where the test demands "fr".
expected_exception='EqualException'

is_count() { [[ ${1:-} =~ ^[0-9]+$ ]]; }

flat() { tr '\n' ' ' <"$1"; }

assembly_attr() { # file attr
  flat "$1" | grep -o "<assembly [^>]*" | head -1 | grep -o "$2=\"[^\"]*\"" | head -1 |
    sed "s/^$2=\"//; s/\"$//"
}

test_result() { # file test-method-name
  flat "$1" | tr '<' '\n' | grep "^test .*method=\"$2\"" | grep -o 'result="[^"]*"' | head -1 |
    sed 's/^result="//; s/"$//'
}

test_failure_text() { # file test-method-name -> everything from that test element to its close
  flat "$1" | sed "s/<test /\n<test /g" | grep "method=\"$2\""
}

verdict=PASS
notes=()

if [[ ! -s $baseline_xml ]]; then
  verdict=INCONCLUSIVE
  notes+=("baseline produced no results XML (exit $baseline_exit): cannot distinguish regression from host/build failure")
else
  b_release=$(test_result "$baseline_xml" "$release_test")
  b_total=$(assembly_attr "$baseline_xml" total)
  b_passed=$(assembly_attr "$baseline_xml" passed)
  b_failed=$(assembly_attr "$baseline_xml" failed)
  b_skipped=$(assembly_attr "$baseline_xml" skipped)
  b_errors=$(assembly_attr "$baseline_xml" errors)
  b_fail_text=$(test_failure_text "$baseline_xml" "$release_test")
  notes+=("baseline exit=$baseline_exit total=$b_total passed=$b_passed failed=$b_failed skipped=$b_skipped errors=$b_errors")
  notes+=("baseline runtime banner: $(assembly_attr "$baseline_xml" environment)")
  notes+=("baseline $release_test = ${b_release:-<absent>}")

  if ! is_count "$baseline_exit" || [[ $baseline_exit == 0 ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline exit code '$baseline_exit' is absent, non-numeric or zero: a red baseline must exit nonzero")
  elif ! is_count "$b_total" || ! is_count "$b_passed" || ! is_count "$b_failed" ||
       ! is_count "$b_skipped" || ! is_count "$b_errors"; then
    verdict=INCONCLUSIVE
    notes+=("baseline summary is incomplete: totals/passed/failed/skipped/errors must all be present")
  elif (( b_passed + b_failed + b_skipped != b_total )) || (( b_total != 5 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline summary is inconsistent or not the whole 5-case suite: $b_passed+$b_failed+$b_skipped vs total $b_total")
  elif (( b_errors != 0 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline reported $b_errors assembly-level error(s): host/collection failure, not the regression")
  elif (( b_skipped != 0 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline skipped $b_skipped case(s): Windows-only regressions did not execute")
  elif (( b_failed == 0 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline reported no failures despite a nonzero exit: nothing was proven")
  elif [[ $b_release != Fail ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline release-recovery regression did not fail: unexpected pass needs timing investigation, not a weaker assertion")
  elif grep -qF "$missed_temp" <<<"$b_fail_text"; then
    verdict=INCONCLUSIVE
    notes+=("baseline failure is only a missed transient-temp observation: no contention was proven")
  elif ! grep -qF "$expected_exception" <<<"$b_fail_text" ||
       ! grep -qE 'Assert\.Equal\(\) Failure' <<<"$b_fail_text" ||
       ! grep -qE 'Expected: +"?fr"?' <<<"$b_fail_text" ||
       ! grep -qE 'Actual: +"?ja"?' <<<"$b_fail_text" ||
       ! grep -qE "at CCP\.Core\.Settings\.Tests\.SettingsPublicationTests\.$release_test" <<<"$b_fail_text"; then
    verdict=INCONCLUSIVE
    notes+=("baseline failure is not the expected post-release publication assertion (EqualException fr vs ja attributed to $release_test): setup/seed/profile/open/cancel/timeout or unrecognized signature")
  else
    notes+=("baseline failure is the expected post-release publication regression: Assert.Equal expected \"fr\", actual \"ja\" in $release_test")
  fi
fi

if [[ ! -s $candidate_xml ]]; then
  verdict=INCONCLUSIVE
  notes+=("candidate produced no results XML (exit $candidate_exit)")
else
  c_total=$(assembly_attr "$candidate_xml" total)
  c_passed=$(assembly_attr "$candidate_xml" passed)
  c_failed=$(assembly_attr "$candidate_xml" failed)
  c_skipped=$(assembly_attr "$candidate_xml" skipped)
  c_errors=$(assembly_attr "$candidate_xml" errors)
  notes+=("candidate runtime banner: $(assembly_attr "$candidate_xml" environment)")
  notes+=("candidate exit=$candidate_exit total=$c_total passed=$c_passed failed=$c_failed skipped=$c_skipped errors=$c_errors")
  if ! is_count "$candidate_exit"; then
    verdict=INCONCLUSIVE
    notes+=("candidate exit code '$candidate_exit' is absent or non-numeric: no valid evidence of a green run")
  elif ! is_count "$c_total" || ! is_count "$c_passed" || ! is_count "$c_failed" ||
       ! is_count "$c_skipped" || ! is_count "$c_errors"; then
    verdict=INCONCLUSIVE
    notes+=("candidate summary is incomplete: totals/passed/failed/skipped/errors must all be present")
  elif [[ $candidate_exit != 0 || $c_total != 5 || $c_passed != 5 || $c_failed != 0 || $c_skipped != 0 || $c_errors != 0 ]]; then
    [[ $verdict == PASS ]] && verdict=FAIL
    notes+=("candidate did not reach 5 passed / 0 failed / 0 skipped on Windows")
  fi
fi

printf '%s\n' "VERDICT: $verdict" "${notes[@]}"

case $verdict in
  PASS) exit 0 ;;
  INCONCLUSIVE) exit 2 ;;
  *) exit 1 ;;
esac
