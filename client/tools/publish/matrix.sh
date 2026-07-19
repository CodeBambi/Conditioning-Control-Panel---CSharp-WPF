#!/usr/bin/env bash
# CCP greenfield artifact evidence matrix — Linux/WSL2 (release-publish-gates.md §3).
# Mirrors matrix.ps1 gate-for-gate. Run from a native ext4 dir (never /mnt/e —
# SP-005/007/008/009 pattern). Requires: python3 + libX11 (wmclose.py/xgetimage.py
# mechanism, SP-008 proven), WSLg session for headed gates.
# Gates: 1 startup+graceful-shutdown exit 0 (WM_DELETE_WINDOW ClientMessage — kill is
# never the success path; negative control proves the message caused the exit),
# 2 --verify-assets, 3 --version derivation, 4 fresh-profile, 5 corrupt-settings
# quarantine (original bytes preserved), 6 data-path identity, 7 logs-absence,
# 8 native-deps floor (published: ldd per shipped .so + /proc/<pid>/maps runtime floor).
# Usage: ./matrix.sh [debug|release|published|all]
set -uo pipefail

MODE="${1:-all}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
CSPROJ="$ROOT/src/CcpClient.Desktop/CcpClient.Desktop.csproj"
WMCLOSE="$ROOT/tools/verify/wmclose.py"
XGETIMAGE="$ROOT/tools/verify/xgetimage.py"
ARTDIR="$ROOT/tools/verify/artifacts"
mkdir -p "$ARTDIR"

VERSION="$(dotnet msbuild "$CSPROJ" -nologo -getProperty:Version | tr -d '[:space:]')"
[ -n "$VERSION" ] || { echo "FAIL: Version authority broken"; exit 1; }

CFGROOT="${XDG_CONFIG_HOME:-$HOME/.config}"
CFGDIR="$CFGROOT/CcpClient"
CFGBAK="$CFGDIR.sp010-bak"
SETTINGS="$CFGDIR/settings.json"
FAILURES=0
QUAR_DIRS=()

fail() { echo "GATE $1: FAIL — $2"; FAILURES=$((FAILURES + 1)); }

save_config() { [ -d "$CFGDIR" ] && mv "$CFGDIR" "$CFGBAK" || true; }
restore_config() {
  [ -d "$CFGDIR" ] && rm -rf "$CFGDIR" || true
  [ -d "$CFGBAK" ] && mv "$CFGBAK" "$CFGDIR" || true
}
# Restore a backup orphaned by an interrupted earlier run, then start clean.
[ -d "$CFGBAK" ] && restore_config

# Gate 1 worker: launch headed, require the layout-probe stderr needle + XGetImage
# capture (render evidence), negative control, then real WM_DELETE_WINDOW and wait on
# the real PID for exit 0. Never pkill on the success path.
headed_run() {
  local exe="$1" tag="$2"
  local errlog
  errlog="$(mktemp /tmp/ccp-sp010-stderr.XXXX.log)"
  "$exe" 2>"$errlog" &
  local pid=$!
  local found=1
  for _ in $(seq 1 50); do
    if ! kill -0 "$pid" 2>/dev/null; then found=2; break; fi
    if python3 "$WMCLOSE" "CCP Client" --check >/dev/null 2>&1; then found=0; break; fi
    sleep 0.5
  done
  if [ "$found" != 0 ]; then
    kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
    echo "headed $tag: FAIL — no protocol-advertising window (state $found)"
    rm -f "$errlog"; return 1
  fi
  sleep 1
  python3 "$XGETIMAGE" "CCP Client" "$ARTDIR/wslg-matrix-$tag.bmp" >/dev/null 2>&1 \
    && echo "headed $tag: XGetImage capture -> $ARTDIR/wslg-matrix-$tag.bmp"
  # Negative control: malformed ClientMessage must be IGNORED.
  python3 "$WMCLOSE" "CCP Client" --negative >/dev/null
  sleep 2
  if ! kill -0 "$pid" 2>/dev/null; then
    wait "$pid"; local ec=$?
    echo "headed $tag: FAIL — negative control coincided with exit $ec (close not attributable)"
    rm -f "$errlog"; return 1
  fi
  # Real graceful close.
  python3 "$WMCLOSE" "CCP Client" >/dev/null
  local i
  for i in $(seq 1 40); do
    kill -0 "$pid" 2>/dev/null || break
    sleep 0.5
  done
  if kill -0 "$pid" 2>/dev/null; then
    kill -9 "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
    echo "headed $tag: FAIL — ignored WM_DELETE_WINDOW (killed)"
    rm -f "$errlog"; return 1
  fi
  wait "$pid"; local ec=$?
  if [ "$ec" != 0 ]; then
    echo "headed $tag: FAIL — graceful close exit $ec"
    rm -f "$errlog"; return 1
  fi
  local probe
  probe="$(grep 'layout-probe:' "$errlog" | tail -1 || true)"
  rm -f "$errlog"
  [ -n "$probe" ] || { echo "headed $tag: FAIL — layout-probe needle missing on stderr"; return 1; }
  echo "headed $tag: graceful exit 0; $probe"
  return 0
}

MODES=(debug release published)
[ "$MODE" != "all" ] && MODES=("$MODE")

for m in "${MODES[@]}"; do
  echo "--- mode: $m"
  if [ "$m" = "published" ]; then
    PUBDIR="$ROOT/artifacts/publish/CcpClient.Desktop-$VERSION-linux-x64"
    [ -f "$PUBDIR/CcpClient.Desktop" ] || { fail "$m/publish" "published artifact missing: $PUBDIR (run publish.sh first)"; continue; }
    # Gate 6 (location independence): run the publish dir from a MOVED location.
    EXEDIR="/tmp/ccp-sp010-portable"
    rm -rf "$EXEDIR"; cp -r "$PUBDIR" "$EXEDIR"
  else
    CFGNAME=$([ "$m" = "debug" ] && echo Debug || echo Release)
    EXEDIR="$ROOT/src/CcpClient.Desktop/bin/$CFGNAME/net10.0"
    [ -f "$EXEDIR/CcpClient.Desktop" ] || { fail "$m" "binary missing: $EXEDIR — build first: dotnet build client/CcpClient.sln -c $CFGNAME"; continue; }
  fi
  EXE="$EXEDIR/CcpClient.Desktop"
  echo "ARTIFACT $m: $EXE"

  # Gate 2: --verify-assets (published run = row-8 deferred third)
  OUT="$("$EXE" --verify-assets 2>&1)"; EC=$?
  if [ $EC -eq 0 ] && grep -q 'verify-assets: PASS' <<< "$OUT"; then
    echo "GATE2 $m: PASS — verify-assets exit 0 ($(grep 'verify-assets:' <<< "$OUT"))"
  else fail "GATE2 $m" "verify-assets exit $EC: $OUT"; fi

  # Gate 3: --version derives from the authority (prefix before any +sha == msbuild Version)
  OUT="$("$EXE" --version 2>&1)"; EC=$?
  if [ $EC -eq 0 ] && [[ "$OUT" =~ version:[[:space:]]+([^[:space:]]+) ]]; then
    PREFIX="${BASH_REMATCH[1]%%+*}"
    if [ "$PREFIX" = "$VERSION" ]; then
      echo "GATE3 $m: PASS — ${BASH_REMATCH[1]} (prefix == authority $VERSION)"
    else fail "GATE3 $m" "printed prefix '$PREFIX' != authority '$VERSION'"; fi
  else fail "GATE3 $m" "--version exit $EC: $OUT"; fi

  save_config
  # Gate 4: fresh-profile headed run — no config-only crash, NO settings.json created
  rm -rf "$CFGDIR"
  if headed_run "$EXE" "$m-fresh"; then
    if [ ! -f "$SETTINGS" ]; then
      echo "GATE4 $m: PASS — fresh-profile graceful exit 0; no settings.json created"
    else fail "GATE4 $m" "settings.json created on a defaults run (defaults must never auto-save)"; fi
  else fail "GATE4 $m" "headed run failed"; fi

  # Gate 5: corrupt-settings — quarantine file with the ORIGINAL BYTES preserved
  mkdir -p "$CFGDIR"
  printf '\x7b\x7b\x00\xff\x41' > "$SETTINGS"
  if headed_run "$EXE" "$m-corrupt"; then
    QUAR="$(ls "$CFGDIR"/settings.corrupt-*.json 2>/dev/null | head -1 || true)"
    if [ -z "$QUAR" ]; then
      fail "GATE5 $m" "no settings.corrupt-*.json quarantine file"
    elif printf '\x7b\x7b\x00\xff\x41' | cmp -s - "$QUAR"; then
      QUAR_DIRS+=("$(dirname "$QUAR")")
      echo "GATE5 $m: PASS — corrupt-settings graceful exit 0; quarantine preserved original bytes at $QUAR"
    else fail "GATE5 $m" "quarantine bytes differ from the seeded original"; fi
  else fail "GATE5 $m" "headed run failed"; fi

  # Gate 7: logs-absence — no log files beside the artifact or in the config dir
  LOGS="$(find "$EXEDIR" "$CFGDIR" -name '*.log' 2>/dev/null || true)"
  if [ -z "$LOGS" ]; then
    echo "GATE7 $m: PASS — no log files beside artifact or in config dir (logging honestly absent)"
  else fail "GATE7 $m" "log files exist: $LOGS"; fi
  restore_config

  # Gate 8: native-deps floor (published only)
  if [ "$m" = "published" ]; then
    echo "GATE8 published: shipped natives:"
    find "$EXEDIR" -maxdepth 1 \( -name '*.so' -o -name '*.so.*' \) -printf '    %f %s bytes\n' | sort
    for so in "$EXEDIR"/*.so; do
      [ -e "$so" ] || continue
      echo "    --- ldd $(basename "$so")"
      ldd "$so" 2>&1 | sed 's/^/        /'
    done
    # Runtime floor: dlopened system libraries visible only at runtime.
    "$EXE" 2>/dev/null &
    PID8=$!
    for _ in $(seq 1 50); do
      python3 "$WMCLOSE" "CCP Client" --check >/dev/null 2>&1 && break
      kill -0 "$PID8" 2>/dev/null || break
      sleep 0.5
    done
    if kill -0 "$PID8" 2>/dev/null; then
      echo "    --- runtime-loaded system libraries (/proc/$PID8/maps minus artifact dir):"
      awk '{print $6}' "/proc/$PID8/maps" | grep '\.so' | sort -u | grep -v "^$EXEDIR" | sed 's/^/        /'
      python3 "$WMCLOSE" "CCP Client" >/dev/null 2>&1 || true
    fi
    wait "$PID8" 2>/dev/null || true
    echo "GATE8 published: PASS — floor recorded above"
  fi
done

# Gate 6: data-path identity across modes
if [ "${#QUAR_DIRS[@]}" -ge 2 ]; then
  DISTINCT="$(printf '%s\n' "${QUAR_DIRS[@]}" | sort -u | wc -l)"
  if [ "$DISTINCT" = 1 ]; then
    echo "GATE6 all: PASS — data path identical across modes (${QUAR_DIRS[0]}); published ran from MOVED dir /tmp/ccp-sp010-portable"
  else fail "GATE6 all" "data paths differ: ${QUAR_DIRS[*]}"; fi
fi

if [ "$FAILURES" -gt 0 ]; then echo "MATRIX FAIL (wsl2): $FAILURES gate failure(s)"; exit 1; fi
echo "MATRIX PASS (wsl2)"
