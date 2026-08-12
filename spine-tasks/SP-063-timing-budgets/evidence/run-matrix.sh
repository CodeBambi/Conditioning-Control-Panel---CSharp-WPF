#!/usr/bin/env bash
# SP-063 Step 3: 10 consecutive full-suite runs; run 1 is a fresh-checkout first-ever build.
set -uo pipefail
LANE="C:/Code/Conditioning-Control-Panel---CSharp-WPF/.worktrees/spine-20260812T224057/lane-1"
EV="$LANE/spine-tasks/SP-063-timing-budgets/evidence"
COLD="C:/Code/ccp-sp063-cold"
SUMMARY="$EV/runs/summary.tsv"
: > "$SUMMARY"
echo -e "run\tworktree\tcold\twall_s\tunit\tunit_skip\theadless\theadless_skip\tnamed_test" >> "$SUMMARY"

run_suite () { # $1=runno $2=dir $3=cold|warm
  local n="$1" dir="$2" cold="$3" start end u us h hs named
  start=$(date +%s)
  dotnet test "$dir/client/tests/CcpClient.Tests/CcpClient.Tests.csproj" -c Debug --nologo \
    --logger "trx;LogFileName=sp063-run$n-unit.trx" --results-directory "$EV/trx" \
    > "$EV/runs/run$n-unit.log" 2>&1
  local urc=$?
  dotnet test "$dir/client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj" -c Debug --nologo \
    --logger "trx;LogFileName=sp063-run$n-headless.trx" --results-directory "$EV/trx" \
    > "$EV/runs/run$n-headless.log" 2>&1
  local hrc=$?
  end=$(date +%s)
  u=$(grep -oE "Passed: +[0-9]+" "$EV/runs/run$n-unit.log" | tail -1 | grep -oE "[0-9]+")
  us=$(grep -oE "Skipped: +[0-9]+" "$EV/runs/run$n-unit.log" | tail -1 | grep -oE "[0-9]+")
  h=$(grep -oE "Passed: +[0-9]+" "$EV/runs/run$n-headless.log" | tail -1 | grep -oE "[0-9]+")
  hs=$(grep -oE "Skipped: +[0-9]+" "$EV/runs/run$n-headless.log" | tail -1 | grep -oE "[0-9]+")
  if grep -q 'testName="[^"]*Truncated_PrefixCut_NeverSurfaced_TypedUnavailable"[^>]*outcome="Passed"' "$EV/trx/sp063-run$n-unit.trx" 2>/dev/null \
     || grep -q 'Truncated_PrefixCut_NeverSurfaced_TypedUnavailable' "$EV/trx/sp063-run$n-unit.trx" 2>/dev/null && ! grep -q 'testName="[^"]*Truncated_PrefixCut[^"]*"[^>]*outcome="Failed"' "$EV/trx/sp063-run$n-unit.trx" 2>/dev/null; then
    named=green
  else
    named=NOT-GREEN
  fi
  [ $urc -ne 0 -o $hrc -ne 0 ] && named="$named RC=$urc/$hrc"
  echo -e "$n\t$dir\t$cold\t$((end-start))\t$u\t$us\t$h\t$hs\t$named" | tee -a "$SUMMARY"
}

# Run 1: genuinely cold — fresh checkout, first-ever build.
git -C "$LANE" worktree remove --force "$COLD" 2>/dev/null || true
git -C "$LANE" worktree add --detach "$COLD" HEAD > /dev/null 2>&1
{
  dotnet build "$COLD/client/CcpClient.sln" -c Debug --nologo
} > "$EV/runs/run01-cold-firstever-build.log" 2>&1
run_suite 01 "$COLD" cold

# Runs 2-10: warm, in the lane.
for n in 02 03 04 05 06 07 08 09 10; do
  run_suite "$n" "$LANE" warm
done

git -C "$LANE" worktree remove --force "$COLD"
echo "ALL RUNS DONE"
