#!/bin/bash
# SP-026 WX session facts (WSL2 Ubuntu, WSLg X11-via-XWayland; ~/ccp-sp026 native ext4,
# never /mnt/e; NO input automation, NO timing claims; Wayland never claimed).
# Run 1: meta/payout/loom through the REAL dispatch path (fx-drive) — banking + loom
# file proof on Linux. Run 2: the probe page renders the SERVED loom gif + staged user
# media → XGetImage session facts (xwd), like SP-025's wx captures.
set -u
ROOT=~/ccp-sp026
EV=$ROOT/sp026-wx
EXE=$ROOT/client/src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop
DATA=~/.config/CcpClient
mkdir -p "$EV"

echo "== run 1: loom + meta banking (fx-drive, real dispatch path) =="
"$EXE" --dtrh-demo --dtrh-quick \
  --dtrh-fx-drive "loom-file:evidence-loom-01.gif@8; run-started@13; run-ended-full@17" \
  --dtrh-auto-close 40 > "$EV/wx-run1.log" 2>&1
echo "run1 EXIT=$?"
grep -E "loom|run complete|manifest" "$EV/wx-run1.log" | head -12
cp "$DATA/dtrh_slot1.json" "$EV/wx-slot1-proof.json" 2>/dev/null
ls -la "$DATA/Spirals" | head -5

echo "== run 2: probe render of SERVED loom gif + user media (XGetImage) =="
"$EXE" --dtrh-demo --dtrh-quick --dtrh-page probe.html --dtrh-auto-close 30 > "$EV/wx-run2.log" 2>&1 &
APP=$!
sleep 12
# settled window id via xprop (resolves fresh per query — SP-013 WSLg lesson)
WID=$(xprop -root _NET_ACTIVE_WINDOW 2>/dev/null | awk '{print $5}')
xprop -name "CCP" WM_NAME 2>/dev/null | head -3
for i in 1 2 3; do
  CAND=$(xwininfo -root -children 2>/dev/null | grep -i "CCP" | head -1 | awk '{print $1}')
  [ -n "$CAND" ] && WID=$CAND && break
  sleep 1
done
echo "capturing window $WID"
xwd -id "$WID" -out "$EV/wx-probe.xwd" 2>/dev/null && convert "$EV/wx-probe.xwd" "$EV/wx-probe-media.png" && echo "captured wx-probe-media.png"
wait $APP
echo "run2 EXIT=$?"
grep -E "probe-img" "$EV/wx-run2.log" | head -8
