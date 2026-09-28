#!/usr/bin/env bash
# Linux speech capture, end to end without a human and without a real microphone:
# a throwaway null sink, the Vosk test WAV played into it (pw-play, else paplay), parec capturing its .monitor through
# the real PulseMicSource + Core SpeechEngine (CCP.Avalonia --speech-check).
# Passes when the spoken phrase matches, a wrong phrase exits non-zero, and no parec is left.
# Usage: scripts/speech-capture-check.sh [modelDir]   (default: fetch-vosk-model.sh's cache)
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_ROLL_FORWARD=Major
model="${1:-$(bash .github/scripts/fetch-vosk-model.sh)}"
wav=Tests/CCP.Core.Tests/Fixtures/vosk-test.wav
sink="ccp_speech_check_$$"
mod=$(pactl load-module module-null-sink "sink_name=$sink")
trap 'pactl unload-module "$mod" || true' EXIT
dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release -v q -nologo > /dev/null
app=(dotnet run --no-build --project CCP.Avalonia/CCP.Avalonia.csproj -c Release -- --speech-check "$(dirname "$model")")

run() { # $1 phrase; echoes the app's exit code
  timeout 60 "${app[@]}" "$sink.monitor" "$1" >&2 & pid=$!
  for _ in $(seq 150); do pgrep -f "parec.*$sink" > /dev/null && break; sleep 0.2; done
  sleep 0.5 # parec is up; give it a moment to connect to the monitor
  # pw-play: on pipewire-pulse (1.6.9) paplay --device into a fresh null sink reached the monitor silent
  if command -v pw-play > /dev/null; then pw-play --target="$sink" "$wav"; else paplay --device="$sink" "$wav"; fi
  local rc=0; wait "$pid" || rc=$?
  echo "$rc"
}

ok=$(run "one zero zero zero one")
bad=$(run "hello my darling")
sleep 1
stray=$(pgrep -fc "parec.*$sink" || true)
echo "match exit=$ok  wrong-phrase exit=$bad  stray parec=$stray"
test "$ok" = 0
test "$bad" = 1
test "$stray" = 0
echo "speech capture check passed"
