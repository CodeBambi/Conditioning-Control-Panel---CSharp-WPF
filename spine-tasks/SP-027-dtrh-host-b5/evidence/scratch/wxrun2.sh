#!/bin/bash
# SP-027 WX renderer-kill run (WSL2 WSLg): engine live on the WebKitGTK dialog, then
# kill the WebKit web process -> heartbeat silence -> watchdog relaunch-ONCE -> second
# engine live -> graceful close via auto-close. Bounded; log persists either way.
cd ~/ccp-sp027/client/src/CcpClient.Desktop
rm -f ~/ccp-sp027/wx-run2.log
dotnet bin/Debug/net10.0/CcpClient.Desktop.dll --dtrh-demo --dtrh-quick --dtrh-auto-close 100 2> ~/ccp-sp027/wx-run2.log &
APP_PID=$!
live=0
for i in $(seq 1 45); do
  sleep 1
  if grep -q "ENGINE LIVE" ~/ccp-sp027/wx-run2.log 2>/dev/null; then live=1; break; fi
done
echo "engine live observed: $live"
pkill -f WebKitWebProcess
echo "renderer killed (WebKitWebProcess)"
rec=0
for i in $(seq 1 70); do
  sleep 1
  if grep -q "relaunching ONCE" ~/ccp-sp027/wx-run2.log 2>/dev/null; then rec=1; break; fi
  if grep -q "EXHAUSTED" ~/ccp-sp027/wx-run2.log 2>/dev/null; then rec=2; break; fi
done
echo "recovery observed: $rec (1=relaunch-once 2=exhausted 0=none)"
sleep 20
if kill -0 $APP_PID 2>/dev/null; then kill $APP_PID 2>/dev/null; fi
wait $APP_PID
echo "APP-EXIT=$?"
grep -v loopback ~/ccp-sp027/wx-run2.log | grep -iE "watchdog|relaunch|silent|ProcessFailed|ENGINE LIVE|graceful|exit-done|flow ended|teardown|AdapterDestroyed" | head -30
