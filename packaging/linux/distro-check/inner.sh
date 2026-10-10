#!/usr/bin/env bash
# Runs INSIDE a distro container (run.sh drives it). Never run on a host: it starts its own Xvfb.
#   inner.sh install          -> installs packaging/linux/README-deps.md runtime deps + test tooling (network on)
#   inner.sh run /ccp.tar.gz /out   -> extracts the tarball, runs the app under Xvfb, writes /out (network off)
set -uo pipefail
. /etc/os-release
fam=$ID; case " $ID ${ID_LIKE:-} " in *" debian "*|*" ubuntu "*) fam=debian;; *" fedora "*) fam=fedora;; *" arch "*) fam=arch;; *suse*) fam=suse;; esac

install() {
  # Runtime deps: exactly the README-deps.md row for this family. Fonts: none added (distro default only).
  case $fam in
    debian) deps="libwebkit2gtk-4.1-0 libwpewebkit-2.0-1 libwpebackend-fdo-1.0-1 libwpe-1.0-1 libvlc5 vlc-plugin-base libsecret-1-0 libx11-6 libxi6"
            tools="xvfb openbox imagemagick xdotool x11-utils fontconfig procps"
            export DEBIAN_FRONTEND=noninteractive; apt-get update -qq >/dev/null
            add() { apt-get install -y -qq --no-install-recommends "$1" >/dev/null 2>&1; } ;;
    fedora) deps="webkit2gtk4.1 wpewebkit wpebackend-fdo libwpe vlc-libs libsecret libX11 libXi"
            tools="xorg-x11-server-Xvfb openbox ImageMagick xdotool xwininfo fontconfig procps-ng"
            add() { dnf install -y -q --setopt=install_weak_deps=False "$1" >/dev/null 2>&1; } ;;
    arch)   deps="webkit2gtk-4.1 wpewebkit wpebackend-fdo libwpe vlc libsecret libx11 libxi"
            tools="xorg-server-xvfb openbox imagemagick xdotool xorg-xwininfo fontconfig procps-ng"
            pacman -Sy --noconfirm >/dev/null 2>&1
            add() { pacman -S --noconfirm --needed "$1" >/dev/null 2>&1; } ;;
    suse)   deps="libwebkit2gtk-4_1-0 libWPEWebKit-2_0-1 libWPEBackend-fdo-1_0-1 libwpe-1_0-1 libvlc5 vlc-noX libsecret-1-0 libX11-6 libXi6"
            tools="xvfb-run xorg-x11-server-Xvfb openbox ImageMagick xdotool xwininfo fontconfig procps gzip tar gawk"
            add() { zypper -n -q install --no-recommends "$1" >/dev/null 2>&1; } ;;
    *) echo "unknown distro $ID" >&2; exit 2 ;;
  esac
  : > /unavailable.txt
  # One by one, so a package name the distro lacks is recorded instead of failing the whole install.
  for p in $deps; do add "$p" || echo "dep $p" >> /unavailable.txt; done
  for p in $tools; do add "$p" || echo "tool $p" >> /unavailable.txt; done
  command -v openbox >/dev/null || add fluxbox || true
  # The run phase uses the host uid; give it a passwd entry like any real user (openbox segfaults without one).
  getent passwd "$HOST_UID" >/dev/null || echo "user:x:$HOST_UID:$HOST_GID:user:/tmp:/bin/bash" >> /etc/passwd
  command -v Xvfb >/dev/null && command -v import >/dev/null && command -v xdotool >/dev/null
}

shot() { import -window root "$out/$cfg-$1.png" 2>/dev/null
  echo "$cfg-$1 windows: $(for w in $(xdotool search --onlyvisible --name . 2>/dev/null); do printf '[%s] ' "$(xdotool getwindowname "$w")"; done)" >> "$out/log.txt"; }
# Avalonia sets only _NET_WM_NAME, which xwininfo -tree does not print: find by xdotool, record geometry with xwininfo.
waitwin() { local w; for _ in $(seq "$2"); do w=$(xdotool search --onlyvisible --name "$1" 2>/dev/null | head -1)
  [ -n "$w" ] && { xwininfo -id "$w" | grep -E 'Width|Height|Absolute' >> "$d/windows.txt"; return 0; }; sleep 1; done; return 1; }
jarr() { sed 's/"/\\"/g; s/.*/"&"/' | paste -sd, -; }
key() { xdotool search --name "$1" windowactivate --sync key --clearmodifiers "${@:2}" 2>/dev/null || xdotool key "${@:2}"; }

# Click at (dx,dy) DIPs from the client top-left of window "$1"; $scale is the run's render scale.
wclick() { local w; w=$(xdotool search --onlyvisible --name "$1" 2>/dev/null | head -1); [ -n "$w" ] || return 1
  eval "$(xdotool getwindowgeometry --shell "$w")"
  xdotool mousemove $((X + $2 * scale)) $((Y + $3 * scale)) click 1; sleep 2; }

runcfg() {  # cfg W H scale
  cfg=$1; local d=$tmp/$cfg n=$((${#cfgs_done}+90)); cfgs_done+=x
  mkdir -p "$d/home" "$d/config" "$d/userdata"
  Xvfb ":$n" -screen 0 "${2}x${3}x24" -nolisten tcp >/dev/null 2>&1 & local xvfb=$!
  for _ in $(seq 50); do [ -e "/tmp/.X11-unix/X$n" ] && break; sleep 0.2; done
  export DISPLAY=":$n"
  (openbox >/dev/null 2>&1 || fluxbox >/dev/null 2>&1) & local wm=$!
  local env=(HOME="$d/home" XDG_CONFIG_HOME="$d/config" CCP_USERDATA_DIR="$d/userdata")
  [ "$4" = 2 ] && env+=(GDK_SCALE=2 AVALONIA_GLOBAL_SCALE_FACTOR=2)
  env "${env[@]}" "$app/CCP.Avalonia" >"$d/stdout.txt" 2>"$d/stderr.txt" & local pid=$!
  local win=false wiz=false wizdone=false; scale=$4
  waitwin "Conditioning Control Panel" 120 && win=true
  sleep 8; shot 1-first
  # First-run order on a fresh profile: the Free Feature card opens over the "First run" wizard.
  waitwin "Free Feature" 20 && { key "Free Feature" Escape; sleep 2; waitwin "Free Feature" 1 && key "Free Feature" Return; sleep 2; }
  # The wizard is fixed-size (~900x680 DIPs); its age checkbox and Enter button are clicked like a user would.
  if waitwin "^First run" 30; then wiz=true; shot 2-wizard
    wclick "^First run" 53 500; wclick "^First run" 835 640; sleep 2; shot 3-wizard-2
    wclick "^First run" 835 640; sleep 2; shot 3-wizard-3
    wclick "^First run" 835 640; sleep 3
    waitwin "^First run" 1 || wizdone=true
  fi
  shot 4-home
  key "^Conditioning Control Panel$" ctrl+k; sleep 3; shot 5-dialog
  xdotool type --delay 80 "settings" 2>/dev/null; sleep 1; xdotool key Return 2>/dev/null; sleep 4; shot 6-settings
  # Loaded system libs (libsecret/libvlc/webkit load lazily) before the app is stopped.
  [ -r "/proc/$pid/maps" ] && awk '$6 ~ /\.so/ {print $6}' "/proc/$pid/maps" | sort -u > "$d/maps.txt"
  local alive=false code=null
  if kill -0 "$pid" 2>/dev/null; then alive=true; kill "$pid"; fi
  wait "$pid"; code=$?
  kill "$wm" "$xvfb" 2>/dev/null; wait "$wm" "$xvfb" 2>/dev/null
  { echo "== $cfg stdout"; cat "$d/stdout.txt"; echo "== $cfg stderr"; cat "$d/stderr.txt"
    echo "== $cfg app log"; cat "$d"/userdata/logs/*.log 2>/dev/null; } >> "$out/log.txt"
  cat "$d/maps.txt" 2>/dev/null >> "$tmp/maps.txt"
  runs+="\"$cfg\":{\"window\":$win,\"wizard\":$wiz,\"wizard_done\":$wizdone,\"alive_at_end\":$alive,\"exit_code\":$code},"
}

run() {
  out=$2; tmp=$(mktemp -d); cfgs_done=; runs=
  export HOME=$tmp XDG_CACHE_HOME=$tmp/cache  # the host uid has no home in the image; fontconfig wants a cache dir
  tar -xzf "$1" -C "$tmp"; app=$tmp/ConditioningControlPanel
  : > "$out/log.txt"
  runcfg fhd 1920 1080 1
  runcfg hd 1366 768 1
  runcfg hidpi 1920 1080 2
  { for f in "$app"/*.so $(grep -iE 'secret|vlc|webkit|wpe' "$tmp/maps.txt" 2>/dev/null | sort -u); do
      echo "== $f"; ldd "$f" 2>&1; done; } > "$out/ldd.txt"
  { echo "fonts installed: $(fc-list | wc -l)"
    for fam in Fredoka "Segoe UI" "Segoe UI Emoji" Consolas "Courier New" "Liberation Sans" sans-serif monospace emoji; do
      echo "$fam -> $(fc-match "$fam")"; done
    echo "== fc-list families"; fc-list : family | sort -u; } > "$out/fonts.txt"
  local missing exc shots= s
  # libcoreclrtraceptprovider's liblttng-ust is optional .NET tracing, absent on every stock distro: not a gap.
  missing=$(awk '/^== /{n=split($2,a,"/"); f=a[n]} /not found/ && f !~ /traceptprovider/ {print f": "$1}' "$out/ldd.txt" | sort -u | jarr)
  exc=$(grep -cE 'Exception|\[ERR\]|\[FTL\]' "$out/log.txt")
  for p in "$out"/*.png; do [ -e "$p" ] || continue
    s=$($(command -v magick || echo convert) "$p" -colorspace Gray -format '%[fx:standard_deviation]' info: 2>/dev/null || echo 0)
    shots+="\"$(basename "$p")\":{\"stddev\":$s,\"nonblank\":$(awk -v s="$s" 'BEGIN{print (s>0.02)?"true":"false"}')},"; done
  unav=$(jarr < /unavailable.txt)
  printf '{"distro":"%s","runs":{%s},"missing_libs":[%s],"unavailable_packages":[%s],"log_exception_lines":%s,"screenshots":{%s}}\n' \
    "$PRETTY_NAME" "${runs%,}" "$missing" "$unav" "$exc" "${shots%,}" > "$out/result.json"
  rm -rf "$tmp"
}

case ${1:-} in install) install ;; run) run "$2" "$3" ;; *) echo "usage: inner.sh install|run <tar> <out>" >&2; exit 2 ;; esac
