#!/usr/bin/env bash
# Fetch the small English Vosk model the speech tests run against (not in the repo, ~40 MB).
# Prints the model directory; point CCP_VOSK_MODEL at it. Idempotent.
set -euo pipefail
name=vosk-model-small-en-us-0.15
dir="${1:-$HOME/.cache/ccp-test/vosk}"
if [ ! -d "$dir/$name/am" ]; then
  mkdir -p "$dir"
  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT
  curl -fsSL --max-time 300 -o "$tmp/m.zip" "https://alphacephei.com/vosk/models/$name.zip"
  unzip -q "$tmp/m.zip" -d "$dir"
fi
echo "$dir/$name"
