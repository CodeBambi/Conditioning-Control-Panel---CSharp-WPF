#!/usr/bin/env bash
# Every clean-distro check, then <outdir>/summary.json + index.html (screenshot grid per distro).
#   bash packaging/linux/distro-check/all.sh <tarball> <outdir>
# index-only mode (CI assembles the per-distro artifacts): bash all.sh --index <outdir>
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
distros="ubuntu:24.04 ubuntu:22.04 debian:12 fedora:latest archlinux:latest opensuse/tumbleweed"
if [ "${1:-}" != --index ]; then
  tar=${1:?tarball}; outdir=${2:?outdir}; rc=0
  for i in $distros; do bash "$here/run.sh" "$i" "$tar" "$outdir" >/dev/null || { echo "FAIL $i" >&2; rc=1; }; done
else outdir=${2:?outdir}; rc=0; fi

cd "$outdir"
{ echo '{'; sep=
  for r in */result.json; do printf '%s"%s":%s' "$sep" "${r%/result.json}" "$(cat "$r")"; sep=$',\n'; done
  echo; echo '}'; } > summary.json
{ echo '<!doctype html><meta charset="utf-8"><title>CCP clean-distro check</title>'
  echo '<style>body{font:14px sans-serif;background:#111;color:#eee}img{width:320px;margin:2px}pre{white-space:pre-wrap;font-size:11px}</style>'
  for d in */; do d=${d%/}; [ -f "$d/result.json" ] || continue
    echo "<h2>$d</h2><pre>$(sed 's/</\&lt;/g' "$d/result.json")</pre>"
    for p in "$d"/*.png; do [ -e "$p" ] && echo "<a href=\"$p\"><img src=\"$p\" title=\"$p\"></a>"; done
    echo "<p><a href=\"$d/log.txt\">log</a> · <a href=\"$d/ldd.txt\">ldd</a> · <a href=\"$d/fonts.txt\">fonts</a></p>"
  done; } > index.html
exit $rc
