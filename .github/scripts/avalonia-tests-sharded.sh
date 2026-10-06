#!/usr/bin/env bash
# Runs Tests/CCP.Avalonia.Tests in several testhosts instead of one.
#
# Why: each MainShellWindow a test builds leaves ~250 MB of native memory behind (a known leak,
# docs/avalonia-decisions.md), so one testhost for the whole suite peaked at 12-15 GB RSS and
# the 16 GB ubuntu runner died mid-suite ("runner has received a shutdown signal", MSB4166).
# Shards are by the first letter of the test class; the last shard is the complement of all the
# others, so a test cannot fall between shards. The guard below still proves it: the shards'
# totals must add up to the suite's own test list.
set -euo pipefail

project=Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj
prefix=CCP.Avalonia.Tests.
shards=("A B C D E" "F I J K L" "G H M N O P Q R" "S T")

dotnet build "$project" -c Release

contains() { local f="" l; for l in $1; do f+="${f:+|}FullyQualifiedName~$prefix$l"; done; echo "$f"; }
filters=()
rest=""
for s in "${shards[@]}"; do
  filters+=("$(contains "$s")")
  for l in $s; do rest+="${rest:+&}FullyQualifiedName!~$prefix$l"; done
done
filters+=("$rest")

expected=$(dotnet test "$project" -c Release --no-build --list-tests | grep -cE "^\s+$prefix")
ran=0
for f in "${filters[@]}"; do
  out=$(mktemp)
  if dotnet test "$project" -c Release --no-build --filter "$f" 2>&1 | tee "$out"; then status=0; else status=$?; fi
  total=$(grep -oE 'Total: +[0-9]+' "$out" | grep -oE '[0-9]+' | tail -1 || true)
  rm -f "$out"
  [ "$status" -eq 0 ] || exit "$status"
  ran=$((ran + ${total:-0}))
done

echo "avalonia shards ran $ran of $expected tests"
if [ "$ran" -ne "$expected" ]; then
  echo "::error::Avalonia test shards ran $ran tests but the suite lists $expected"
  exit 1
fi
