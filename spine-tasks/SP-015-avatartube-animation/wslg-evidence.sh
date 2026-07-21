#!/usr/bin/env bash
# SP-015 Step 5 — AvatarTube demonstrator WSLg/X11 gate.
# SESSION FACTS ONLY (SP-008 named limit: NO input automation on WSLg; WSLg capture jitter
# supports frame-deltas + no-blanks, NEVER cadence/timing — cadence evidence is
# Windows-headed only per the packet). Wayland stays §5.1 untouched (WSLg is XWayland).
#   1. contract testCommand green on WSL2 (native-dir build, never /mnt/e)
#   2. tube renders (XGetImage full-window captures of the animated demo)
#   3. frames advance + no blanks via full-window strip scan (--avatar-strip-decode --scan)
#      and the CcpVerify evaluator (frames-advance / no-blank / float-liveness verdicts)
# The demo opens already-animated via --avatar-animate (no input automation needed).
# xgetimage.py resolves the window by NAME per call (SP-013 settled-tree/id-churn lesson).
set -u -o pipefail
WT=/mnt/e/Code/Conditioning-Control-Panel/.worktrees/spine-20260720T072956/lane-1
DST=~/ccp-sp015
ART="$WT/spine-tasks/SP-015-avatartube-animation/evidence/wslg"
TITLE="AvatarTube DEMONSTRATOR"

echo "=== SP-015 AvatarTube demonstrator (WSLg/X11) ==="
echo "session facts: WAYLAND_DISPLAY=${WAYLAND_DISPLAY:-unset} DISPLAY=${DISPLAY:-unset} XDG_SESSION_TYPE=${XDG_SESSION_TYPE:-unset} kernel=$(uname -r)"

mkdir -p "$DST" "$ART"
rsync -a --delete --exclude 'bin/' --exclude 'obj/' --exclude 'spikes/' "$WT/client/" "$DST/client/"

cd "$DST/client"
echo "--- contract: dotnet build CcpClient.sln ---"
dotnet build CcpClient.sln -c Debug --nologo 2>&1 | tail -2 || exit 1
echo "--- contract: CcpClient.Tests ---"
dotnet test tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo 2>&1 | tail -3 || exit 1
echo "--- contract: CcpClient.HeadlessTests ---"
dotnet test tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo 2>&1 | tail -3 || exit 1

DLL="$DST/client/src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.dll"
PACK="$DST/client/src/CcpClient.Desktop/Assets/avatar/pack-circuit.json"
[ -f "$DLL" ] || { echo "FAIL: dll missing"; exit 1; }

pkill -f "CcpClient[.]Desktop" 2>/dev/null; sleep 1
rm -f "$HOME/.config/CcpClient/settings.json"
echo "--- launch: --avatartube-demo --avatar-animate ---"
dotnet "$DLL" --avatartube-demo --avatar-animate 2>"$ART/wslg-stderr.log" &
APP_PID=$!
sleep 8
if ! python3 "$DST/client/tools/verify/xgetimage.py" "$TITLE" /tmp/sp015-probe.bmp >/dev/null 2>&1; then
  echo "FAIL: tube window not found on X (title '$TITLE')"; tail -5 "$ART/wslg-stderr.log"; kill $APP_PID 2>/dev/null; exit 1
fi
echo "[boot] tube X window present (probe capture ok)"

SAMPLES="$ART/wslg-samples.jsonl"
: > "$SAMPLES"
echo "--- capture: 16 XGetImage shots @ ~0.4s sleep (decode batched AFTER — per-shot app"
echo "    spawn decode inflates the period past a frame hold and starves same-frame pairs) ---"
SHOTS=()
for i in $(seq 1 16); do
  # GNU date's %3N is not honored on this WSL image (full %N printed — garbage epoch-ms
  # overflowed the evaluator's elapsed math). Compose epoch-ms explicitly; %N CAN carry a
  # leading zero — bash reads that as OCTAL and 8/9 abort the expansion ("value too great
  # for base", killed earlier runs mid-loop). 10# forces base-10.
  N=$(date +%N)
  T=$(( $(date +%s) * 1000 + 10#$N / 1000000 ))
  CAP="$ART/wslg-cap-$T.bmp"
  python3 "$DST/client/tools/verify/xgetimage.py" "$TITLE" "$CAP" || { kill $APP_PID 2>/dev/null; exit 1; }
  SHOTS+=("$CAP")
  sleep 0.4
done
for CAP in "${SHOTS[@]}"; do
  T=$(basename "$CAP" .bmp); T=${T#wslg-cap-}
  LINE=$(dotnet "$DLL" --avatar-strip-decode --capture "$CAP" --scan) || true
  [ -n "$LINE" ] || { echo "FAIL: strip-decode empty for $CAP"; kill $APP_PID 2>/dev/null; exit 1; }
  echo "$LINE" | sed "s/\"T\":0/\"T\":$T/" >> "$SAMPLES"
done
kill $APP_PID 2>/dev/null; sleep 1

echo "--- evaluate: frames-advance / no-blank / float-liveness (session facts; cadence NOT claimed) ---"
dotnet "$DLL" --avatar-sequence "$SAMPLES" --pack "$PACK" | tee "$ART/wslg-verdicts.log"
FAILS=$(grep -c "^FAIL " "$ART/wslg-verdicts.log" || true)
for v in frames-advance no-blank float-liveness; do
  grep -q "^PASS $v" "$ART/wslg-verdicts.log" || { echo "GATE FAIL: $v not PASS"; exit 1; }
done
echo "WSLG EVIDENCE DONE — frames-advance + no-blank + float-liveness PASS (session facts; cadence/timing Windows-headed only)"
