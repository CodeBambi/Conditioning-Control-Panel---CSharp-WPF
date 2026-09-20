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
# xunit v3 renders string diffs with the quotes backslash-escaped (Expected: \"fr\"), so the
# value patterns below allow that one optional backslash. Nothing else about them is relaxed.

is_count() { [[ ${1:-} =~ ^[0-9]+$ ]]; }

# A REAL XML parser, not text matching: an incomplete, truncated or malformed results document
# must be unparseable rather than silently yielding the handful of attributes a grep can see.
# xml.etree.ElementTree is Python stdlib, so this needs no package install anywhere.
PARSER_PY='
import sys, xml.etree.ElementTree as ET
def out(k, v): print(k + "=" + " ".join(str(v).split()))
path, rel = sys.argv[1], sys.argv[2]
try:
    root = ET.parse(path).getroot()
except Exception as e:
    out("ok", 0); out("error", repr(e)[:300]); sys.exit(0)
if root.tag not in ("assemblies", "assembly"):
    out("ok", 0); out("error", "unexpected root element <%s>" % root.tag); sys.exit(0)
asms = [root] if root.tag == "assembly" else root.findall("assembly")
if len(asms) != 1:
    out("ok", 0); out("error", "expected exactly one <assembly>, found %d" % len(asms)); sys.exit(0)
a = asms[0]
out("ok", 1)
for k in ("total", "passed", "failed", "skipped", "errors", "environment"):
    out(k, a.get(k, ""))
tests = a.findall(".//test")
seen = {"Pass": 0, "Fail": 0, "Skip": 0, "unknown": 0}
for t in tests:
    r = t.get("result", "")
    seen[r if r in seen else "unknown"] += 1
out("n_tests", len(tests))
out("n_pass", seen["Pass"]); out("n_fail", seen["Fail"])
out("n_skip", seen["Skip"]); out("n_unknown", seen["unknown"])
hits = [t for t in tests if t.get("method") == rel]
out("n_release", len(hits))
if len(hits) == 1:
    t = hits[0]
    text = "".join(t.itertext())
    for f in t.iter("failure"):
        text += " " + (f.get("exception-type") or "")
    out("release_result", t.get("result", ""))
    out("release_text", text)
else:
    out("release_result", ""); out("release_text", "")
'

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

parse_xml() { # var-prefix file -> sets <prefix>_ok/_total/.../_release_text
  local prefix=$1 file=$2 k v
  for k in ok error total passed failed skipped errors environment \
           n_tests n_pass n_fail n_skip n_unknown n_release release_result release_text; do
    printf -v "${prefix}_${k}" '%s' ''
  done
  # Python on Windows opens stdout in text mode, so every line arrives CRLF-terminated and an
  # unstripped "1\r" compares unequal to "1". Strip the CR here, at the one place that reads it.
  while IFS='=' read -r k v; do
    k=${k%$'\r'}; v=${v%$'\r'}
    [[ -n $k ]] && printf -v "${prefix}_${k}" '%s' "$v"
  done < <("$py" -c "$PARSER_PY" "$file" "$release_test" 2>/dev/null)
}

verdict=PASS
notes=("XML parsed with $($py -c 'import sys;print(sys.version.split()[0])') xml.etree.ElementTree via $py")

if [[ ! -s $baseline_xml ]]; then
  verdict=INCONCLUSIVE
  notes+=("baseline produced no results XML (exit $baseline_exit): cannot distinguish regression from host/build failure")
else
  parse_xml b "$baseline_xml"
  b_release=$b_release_result
  b_fail_text=$b_release_text
  notes+=("baseline exit=$baseline_exit total=$b_total passed=$b_passed failed=$b_failed skipped=$b_skipped errors=$b_errors")
  notes+=("baseline runtime banner: $b_environment")
  notes+=("baseline parsed test records: $b_n_tests (pass=$b_n_pass fail=$b_n_fail skip=$b_n_skip unknown=$b_n_unknown)")
  notes+=("baseline $release_test = ${b_release:-<absent>}")

  if [[ $b_ok != 1 ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline results XML is not a complete well-formed xunit document: ${b_error:-unparseable}")
  elif ! is_count "$baseline_exit" || [[ $baseline_exit == 0 ]]; then
    verdict=INCONCLUSIVE
    notes+=("baseline exit code '$baseline_exit' is absent, non-numeric or zero: a red baseline must exit nonzero")
  elif ! is_count "$b_total" || ! is_count "$b_passed" || ! is_count "$b_failed" ||
       ! is_count "$b_skipped" || ! is_count "$b_errors"; then
    verdict=INCONCLUSIVE
    notes+=("baseline summary is incomplete: totals/passed/failed/skipped/errors must all be present")
  elif (( b_passed + b_failed + b_skipped != b_total )) || (( b_total != 5 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline summary is inconsistent or not the whole 5-case suite: $b_passed+$b_failed+$b_skipped vs total $b_total")
  elif (( b_n_tests != b_total )) || (( b_n_pass != b_passed )) || (( b_n_fail != b_failed )) ||
       (( b_n_skip != b_skipped )) || (( b_n_unknown != 0 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline summary does not reconcile with its actual test records: claimed $b_total/$b_passed/$b_failed/$b_skipped vs parsed $b_n_tests/$b_n_pass/$b_n_fail/$b_n_skip (unknown-result records: $b_n_unknown)")
  elif (( b_n_release != 1 )); then
    verdict=INCONCLUSIVE
    notes+=("baseline contains $b_n_release records for $release_test: absent or ambiguous duplicate evidence")
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
       ! grep -qE 'Expected: +\\?"?fr\\?"?' <<<"$b_fail_text" ||
       ! grep -qE 'Actual: +\\?"?ja\\?"?' <<<"$b_fail_text" ||
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
  parse_xml c "$candidate_xml"
  notes+=("candidate runtime banner: $c_environment")
  notes+=("candidate exit=$candidate_exit total=$c_total passed=$c_passed failed=$c_failed skipped=$c_skipped errors=$c_errors")
  notes+=("candidate parsed test records: $c_n_tests (pass=$c_n_pass fail=$c_n_fail skip=$c_n_skip unknown=$c_n_unknown)")
  if [[ $c_ok != 1 ]]; then
    verdict=INCONCLUSIVE
    notes+=("candidate results XML is not a complete well-formed xunit document: ${c_error:-unparseable}")
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
  elif [[ $candidate_exit != 0 || $c_total != 5 || $c_passed != 5 || $c_failed != 0 || $c_skipped != 0 || $c_errors != 0 || $c_n_pass != 5 ]]; then
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
