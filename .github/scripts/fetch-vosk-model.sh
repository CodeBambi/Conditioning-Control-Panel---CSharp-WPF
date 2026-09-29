#!/usr/bin/env bash
# Fetch the small English Vosk model the speech tests run against (not in the repo, ~40 MB).
# Prints the model directory; point CCP_VOSK_MODEL at it. Idempotent.
set -euo pipefail
name=vosk-model-small-en-us-0.15
# Pinned: a changed upstream zip fails here instead of shipping unreviewed bytes. CI cache keys include it.
sha256=30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498
dir="${1:-$HOME/.cache/ccp-test/vosk}"
if [ ! -d "$dir/$name/am" ]; then
  mkdir -p "$dir"
  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT
  curl -fsSL --retry 3 --retry-delay 5 --max-time 300 -o "$tmp/m.zip" "https://alphacephei.com/vosk/models/$name.zip"
  echo "$sha256  $tmp/m.zip" | sha256sum -c --quiet -
  unzip -q "$tmp/m.zip" -d "$dir"
fi
echo "$dir/$name"
