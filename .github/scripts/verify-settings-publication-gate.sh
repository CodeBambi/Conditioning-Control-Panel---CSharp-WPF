#!/usr/bin/env bash
# Classifies one baseline/candidate pair of xunit v3 XML results for the settings-publication
# red/green validation. It NEVER re-runs anything: it only reads exit codes and raw XML that
# already exist, so a timing artefact cannot be retried into a green.
#
# PASS  - baseline failed the release-recovery regression for a real reason AND candidate is 5/5.
# INCONCLUSIVE (exit 2) - baseline passed unexpectedly, or failed only because the test never
#   observed the transient temp file (the timing hole called out in the repair review), or the
#   host died before producing results. That is a failed validation gate, not a retry signal.
# FAIL (exit 1) - candidate did not reach 5 passed / 0 failed / 0 skipped.
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
  b_skipped=$(assembly_attr "$baseline_xml" skipped)
  notes+=("baseline exit=$baseline_exit total=$(assembly_attr "$baseline_xml" total) passed=$(assembly_attr "$baseline_xml" passed) failed=$(assembly_attr "$baseline_xml" failed) skipped=$b_skipped errors=$(assembly_attr "$baseline_xml" errors)")
  notes+=("baseline runtime banner: $(assembly_attr "$baseline_xml" environment)")
  notes+=("baseline $release_test = ${b_release:-<absent>}")

  if [[ ${b_skipped:-0} != 0 ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline skipped $b_skipped case(s): Windows-only regressions did not execute")
  elif [[ $b_release != Fail ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline release-recovery regression did not fail: unexpected pass needs timing investigation, not a weaker assertion")
  elif test_failure_text "$baseline_xml" "$release_test" | grep -qF "$missed_temp"; then
    verdict=INCONCLUSIVE
    notes+=("baseline failure is only a missed transient-temp observation: no contention was proven")
  else
    notes+=("baseline failure is a meaningful held-reader release regression")
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
  if [[ $candidate_exit != 0 || $c_total != 5 || $c_passed != 5 || $c_failed != 0 || $c_skipped != 0 || $c_errors != 0 ]]; then
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
