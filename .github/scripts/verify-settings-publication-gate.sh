#!/usr/bin/env bash
# Classifies one baseline/candidate pair of xunit v3 XML results for the settings-publication
# red/green validation. It NEVER re-runs anything: it only reads exit codes and raw XML that
# already exist, so a timing artefact cannot be retried into a green.
#
# PASS  - the baseline reproduced the WHOLE expected identity/outcome matrix (see EXPECTED
#   MATRIX below), including the release-recovery regression failing for a POSITIVELY identified
#   reason (the post-release `Assert.Equal("fr", …)` publication assertion, attributed by stack
#   trace to that test) AND candidate is 5 passed / 0 failed / 1 skipped, the single skip being
#   the one approved Linux-only case. The baseline is INSTRUMENTED (observer seam only, no
#   retry), so its persistent-hold attempt-count failures are expected - but they can never
#   substitute for the release assertion, and no OTHER failure may appear anywhere in the suite.
#   Any other skip, a missing/misnamed/duplicated/unknown record, or an absent release record is
#   INCONCLUSIVE, never PASS.
#
# EXPECTED MATRIX (baseline, per record identity - counts alone are not enough):
#   NoReader…                              Pass
#   Observed…ReleasesTheReader…            Fail, EqualException fr vs ja, own stack frame
#   Persistent…(share: Read)               Fail, EqualException 6 vs 1, own method frame
#   Persistent…(share: Read | Delete)      Fail, EqualException 6 vs 1, own method frame
#   LaterSaveWins…                         Pass, or Fail with EqualException de vs ja on its own
#                                          frame (the baseline has no retries, so the earlier
#                                          save's value can be the one that survives)
#   NonTransient…                          Skip (the one approved Linux-only case)
# INCONCLUSIVE (exit 2) - baseline passed unexpectedly, failed only because the test never
#   observed the transient temp file (the timing hole called out in the repair review), failed for
#   any setup/seed/profile/open/cancel/timeout or otherwise unrecognized reason, produced an
#   incomplete or self-inconsistent summary, produced a zero/absent exit code, or the host died
#   before producing results. That is a failed validation gate, not a retry signal.
# FAIL (exit 1) - candidate did not reach 5 passed / 0 failed / 1 approved skip.
# Missing exit files are passed in as anything non-numeric (e.g. "missing") and are never treated
# as valid evidence.
set -uo pipefail

baseline_xml=$1
candidate_xml=$2
baseline_exit=$3
candidate_exit=$4

# The release-recovery regression: the reader is released mid-publication, so a bounded retry
# must succeed. This is the one case whose baseline failure is the point of the exercise.
release_test='ObservedPublicationFailureThatReleasesTheReaderRecoversAndPublishes'
# The ONE skip either revision may report: the directory-target proof is Linux-only by review.
approved_skip='NonTransientPublicationFailureNotifiesOnceAndStaysObservable'
# The whole suite: 5 Windows-executable cases plus that one approved skip.
expected_total=6
expected_pass=5
# The six record identities of the suite, as slot:identity pairs. The identity is the xunit
# `method` attribute plus the parameter list rendered in `name` for theory rows, so a theory row
# with a substituted parameter is a DIFFERENT identity and fails closed.
hold_test='PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile'
later_test='LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader'
noreader_test='NoReaderPublishesTheLatestSettingsAndCleansItsTempFile'
expected_slots=(
  "noreader:$noreader_test"
  "release:$release_test"
  "hold_read:$hold_test(share: Read)"
  "hold_read_delete:$hold_test(share: Read | Delete)"
  "later:$later_test"
  "approved_skip:$approved_skip"
)
# The helper recognizes the two documented transient-temp messages on the individual release
# failure node; all other setup/temp/timeout text remains unrecognized and fail-closed.
# The ONLY baseline failure this gate accepts: after the reader is released the baseline never
# publishes, so the re-read language is still the seeded "ja" where the test demands "fr". The
# Python parser owns the exact exception, message fields, and attributed stack-frame contract.

is_count() { [[ ${1:-} =~ ^[0-9]+$ ]]; }

# A REAL XML parser, not text matching: an incomplete, truncated or malformed results document
# must be unparseable rather than silently yielding the handful of attributes a grep can see.
# xml.etree.ElementTree is Python stdlib, so this needs no package install anywhere. The helper
# also validates each test's failure-node shape and compares assertion fields structurally.
parser="$(cd "$(dirname "$0")" && pwd)/verify-settings-publication-gate.py"

py=
for c in python3 python py; do
  if command -v "$c" >/dev/null 2>&1 && "$c" -c "import xml.etree.ElementTree" >/dev/null 2>&1; then
    py=$c; break
  fi
done
if [[ -z $py ]]; then
  # No parser means no verified evidence. Never fall back to text matching, never PASS.
  printf '%s\n' "VERDICT: INCONCLUSIVE" \
    "no Python 3 with xml.etree available to parse the results XML: cannot verify the artefacts"
  exit 2
fi

parse_xml() { # var-prefix file -> sets <prefix>_ok/_total/.../_release_summary
  local prefix=$1 file=$2 k v
  for k in ok error structure error_nodes total passed failed skipped errors environment \
           n_tests n_pass n_fail n_skip n_unknown n_release release_result release_shape \
           release_signature release_missed_temp release_summary skip_names unmatched missing invalid \
           slot_noreader slot_release slot_hold_read slot_hold_read_delete slot_later \
           slot_approved_skip failure_shape_noreader failure_shape_release failure_shape_hold_read \
           failure_shape_hold_read_delete failure_shape_later failure_shape_approved_skip \
           failure_signature_noreader failure_signature_release failure_signature_hold_read \
           failure_signature_hold_read_delete failure_signature_later failure_signature_approved_skip \
           failure_missed_temp_noreader failure_missed_temp_release failure_missed_temp_hold_read \
           failure_missed_temp_hold_read_delete failure_missed_temp_later failure_missed_temp_approved_skip \
           failure_summary_noreader failure_summary_release failure_summary_hold_read \
           failure_summary_hold_read_delete failure_summary_later failure_summary_approved_skip; do
    printf -v "${prefix}_${k}" '%s' ''
  done
  # Python on Windows opens stdout in text mode, so every line arrives CRLF-terminated and an
  # unstripped "1\r" compares unequal to "1". Strip the CR here, at the one place that reads it.
  while IFS='=' read -r k v; do
    k=${k%$'\r'}; v=${v%$'\r'}
    [[ -n $k ]] && printf -v "${prefix}_${k}" '%s' "$v"
  done < <("$py" "$parser" "$file" "$release_test" "${expected_slots[@]}" 2>/dev/null)
}

# The Python parser has already validated one direct failure node, one message/stack pair,
# exact exception/value fields, and a complete own-method stack frame. Bash only consumes its
# boolean contract result; it never pools or regex-matches failure text.
hold_row_ok() { # result signature
  [[ $1 == Fail && $2 == 1 ]]
}

# LaterSaveWins… is end-state only: with no retries the earlier save's value can be the one that
# survives, so either a Pass or precisely the de-vs-ja assertion on its own frame is expected.
later_row_ok() { # result signature
  [[ $1 == Pass ]] || [[ $1 == Fail && $2 == 1 ]]
}

verdict=PASS
notes=("XML parsed with $($py -c 'import sys;print(sys.version.split()[0])') xml.etree.ElementTree via $py")

if [[ ! -s $baseline_xml ]]; then
  verdict=INCONCLUSIVE
  notes+=("baseline produced no results XML (exit $baseline_exit): cannot distinguish regression from host/build failure")
else
  parse_xml b "$baseline_xml"
  b_release=$b_release_result
  notes+=("baseline exit=$baseline_exit total=$b_total passed=$b_passed failed=$b_failed skipped=$b_skipped errors=$b_errors")
  notes+=("baseline runtime banner: $b_environment")
  notes+=("baseline parsed test records: $b_n_tests (pass=$b_n_pass fail=$b_n_fail skip=$b_n_skip unknown=$b_n_unknown)")
  notes+=("baseline $release_test = ${b_release:-<absent>}")

  if [[ $b_ok != 1 ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline results XML is not a complete well-formed xunit document: ${b_error:-unparseable}")
  elif [[ -n $b_structure ]]; then
    # Unknown or misplaced elements anywhere in the document - e.g. a failure payload hanging
    # off <assembly> rather than owned by a Fail record - are unrecognized evidence, not a red.
    verdict=INCONCLUSIVE
    notes+=("baseline results XML has elements outside the xunit result structure: $b_structure")
  elif [[ ${b_error_nodes:-0} != 0 ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline reported $b_error_nodes assembly-level <error> node(s): host/collection failure, not the regression")
  elif ! is_count "$baseline_exit" || [[ $baseline_exit == 0 ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline exit code '$baseline_exit' is absent, non-numeric or zero: a red baseline must exit nonzero")
  elif ! is_count "$b_total" || ! is_count "$b_passed" || ! is_count "$b_failed" ||
       ! is_count "$b_skipped" || ! is_count "$b_errors"; then
    verdict=INCONCLUSIVE
    notes+=("baseline summary is incomplete: totals/passed/failed/skipped/errors must all be present")
  elif (( b_passed + b_failed + b_skipped != b_total )) || (( b_total != expected_total )); then
    verdict=INCONCLUSIVE
    notes+=("baseline summary is inconsistent or not the whole $expected_total-case suite: $b_passed+$b_failed+$b_skipped vs total $b_total")
  elif (( b_n_tests != b_total )) || (( b_n_pass != b_passed )) || (( b_n_fail != b_failed )) ||
       (( b_n_skip != b_skipped )) || (( b_n_unknown != 0 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline summary does not reconcile with its actual test records: claimed $b_total/$b_passed/$b_failed/$b_skipped vs parsed $b_n_tests/$b_n_pass/$b_n_fail/$b_n_skip (unknown-result records: $b_n_unknown)")
  elif (( b_n_release != 1 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline contains $b_n_release records for $release_test: absent or ambiguous duplicate evidence")
  elif [[ -n $b_invalid ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline contains structurally invalid test evidence: $b_invalid")
  elif (( b_errors != 0 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline reported $b_errors assembly-level error(s): host/collection failure, not the regression")
  elif [[ $b_skip_names != "$approved_skip" ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline skips are not exactly the one approved Linux-only case: got '${b_skip_names:-<none>}', expected '$approved_skip' (a Windows-only regression that did not execute proves nothing)")
  elif [[ -n $b_unmatched || -n $b_missing ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline records are not exactly the six expected identities: ${b_unmatched:-no unexpected records}; missing: ${b_missing:-none} (a substituted, duplicated or unknown record is unexplained evidence)")
  elif (( b_failed == 0 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline reported no failures despite a nonzero exit: nothing was proven")
  elif [[ $b_release != Fail ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline release-recovery regression did not fail (result: ${b_release:-<absent>}): an unexpected pass, skip or absence needs investigation, not a weaker assertion")
  elif [[ $b_release_missed_temp == 1 ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline release failure is a missed transient-temp observation: no contention was proven")
  elif [[ $b_release_signature != 1 ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline release failure is not the exact post-release publication assertion (EqualException fr vs ja attributed to $release_test): setup failure, absent observer notification, persistent-hold attempt-count failure, open/cancel/timeout or unrecognized signature - none of which substitutes for it")
  elif [[ $b_slot_noreader != Pass ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline $noreader_test is '${b_slot_noreader:-<absent>}', not the expected Pass: the no-contention control did not hold")
  elif ! hold_row_ok "$b_slot_hold_read" "$b_failure_signature_hold_read"; then
    verdict=INCONCLUSIVE
    notes+=("baseline $hold_test(share: Read) is not the exact retry deficit (Fail, EqualException, Expected: 6 / Actual: 1, own method frame): got '${b_slot_hold_read:-<absent>}' (${b_failure_summary_hold_read:-no structured failure}) - a setup/HoldReader/timeout failure or different counts there is unexplained, not an expected deficit")
  elif ! hold_row_ok "$b_slot_hold_read_delete" "$b_failure_signature_hold_read_delete"; then
    verdict=INCONCLUSIVE
    notes+=("baseline $hold_test(share: Read | Delete) is not the exact retry deficit (Fail, EqualException, Expected: 6 / Actual: 1, own method frame): got '${b_slot_hold_read_delete:-<absent>}' (${b_failure_summary_hold_read_delete:-no structured failure})")
  elif ! later_row_ok "$b_slot_later" "$b_failure_signature_later"; then
    verdict=INCONCLUSIVE
    notes+=("baseline $later_test is neither a Pass nor the exact de-vs-ja loss on its own frame: got '${b_slot_later:-<absent>}' (${b_failure_summary_later:-no structured failure}) - any other failure there is unexplained")
  else
    notes+=("baseline failure is the expected post-release publication regression: Assert.Equal expected \"fr\", actual \"ja\" in $release_test")
    notes+=("baseline matrix matched: $noreader_test=Pass, both $hold_test rows=Fail 6-vs-1, $later_test=$b_slot_later, single approved skip")
  fi
fi

if [[ ! -s $candidate_xml ]]; then
  verdict=INCONCLUSIVE
  notes+=("candidate produced no results XML (exit $candidate_exit)")
else
  parse_xml c "$candidate_xml"
  notes+=("candidate runtime banner: $c_environment")
  notes+=("candidate exit=$candidate_exit total=$c_total passed=$c_passed failed=$c_failed skipped=$c_skipped errors=$c_errors")
  notes+=("candidate parsed test records: $c_n_tests (pass=$c_n_pass fail=$c_n_fail skip=$c_n_skip unknown=$c_n_unknown)")
  notes+=("candidate skips: ${c_skip_names:-<none>}; $release_test = ${c_release_result:-<absent>}")
  if [[ $c_ok != 1 ]]; then
    verdict=INCONCLUSIVE
    notes+=("candidate results XML is not a complete well-formed xunit document: ${c_error:-unparseable}")
  elif [[ -n $c_structure ]]; then
    verdict=INCONCLUSIVE
    notes+=("candidate results XML has elements outside the xunit result structure: $c_structure")
  elif [[ ${c_error_nodes:-0} != 0 ]] || { is_count "$c_errors" && (( c_errors != 0 )); }; then
    verdict=INCONCLUSIVE
    notes+=("candidate reported assembly-level errors (attribute='${c_errors}', nodes=${c_error_nodes:-0}): host/collection failure, not a green run")
  elif ! is_count "$candidate_exit"; then
    verdict=INCONCLUSIVE
    notes+=("candidate exit code '$candidate_exit' is absent or non-numeric: no valid evidence of a green run")
  elif ! is_count "$c_total" || ! is_count "$c_passed" || ! is_count "$c_failed" ||
       ! is_count "$c_skipped" || ! is_count "$c_errors"; then
    verdict=INCONCLUSIVE
    notes+=("candidate summary is incomplete: totals/passed/failed/skipped/errors must all be present")
  elif (( c_n_tests != c_total )) || (( c_n_pass != c_passed )) || (( c_n_fail != c_failed )) ||
       (( c_n_skip != c_skipped )) || (( c_n_unknown != 0 )) || (( c_n_release != 1 )); then
    verdict=INCONCLUSIVE
    notes+=("candidate summary does not reconcile with its actual test records: claimed $c_total/$c_passed/$c_failed/$c_skipped vs parsed $c_n_tests/$c_n_pass/$c_n_fail/$c_n_skip (unknown=$c_n_unknown, $release_test records=$c_n_release)")
  elif [[ -n $c_invalid ]]; then
    verdict=INCONCLUSIVE
    notes+=("candidate contains structurally invalid test evidence: $c_invalid")
  elif [[ -n $c_unmatched || -n $c_missing ]]; then
    verdict=INCONCLUSIVE
    notes+=("candidate records are not exactly the six expected identities: ${c_unmatched:-no unexpected records}; missing: ${c_missing:-none}")
  elif [[ $c_skip_names != "$approved_skip" || $c_release_result == Skip || -z $c_release_result ]]; then
    # An extra, missing or misnamed skip - or a release regression that never executed - is
    # unexplained evidence rather than a red: report it as inconclusive, never as PASS.
    verdict=INCONCLUSIVE
    notes+=("candidate skips are not exactly the one approved Linux-only case, or the release regression did not execute: skips='${c_skip_names:-<none>}' (expected '$approved_skip'), $release_test=${c_release_result:-<absent>}")
  elif [[ $candidate_exit != 0 || $c_total != $expected_total || $c_passed != $expected_pass ||
          $c_failed != 0 || $c_skipped != 1 || $c_errors != 0 || $c_n_pass != $expected_pass ]]; then
    [[ $verdict == PASS ]] && verdict=FAIL
    notes+=("candidate did not reach $expected_pass passed / 0 failed / 1 approved skip on Windows")
  fi
fi

printf '%s\n' "VERDICT: $verdict" "${notes[@]}"

case $verdict in
  PASS) exit 0 ;;
  INCONCLUSIVE) exit 2 ;;
  *) exit 1 ;;
esac
