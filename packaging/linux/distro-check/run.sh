#!/usr/bin/env bash
# Clean-distro check of the shipped linux-x64 tarball, one distro per call, entirely inside Docker.
#   bash packaging/linux/distro-check/run.sh <image> <tarball> <outdir>   e.g. ubuntu:24.04
# Phase 1 (network on): install docs/avalonia-linux-install.md runtime deps + Xvfb/openbox/ImageMagick/xdotool/xwininfo, docker commit.
# Phase 2 (--network none, no devices, host uid): run the app under Xvfb at 1920x1080, 1366x768 and 1920x1080 @2x
# with a throwaway HOME/XDG_CONFIG_HOME/CCP_USERDATA_DIR. Writes <outdir>/<distro>/{*.png,log.txt,ldd.txt,fonts.txt,result.json}.
set -euo pipefail
image=${1:?image}; tar=$(realpath "${2:?tarball}"); outdir=${3:?outdir}
here=$(cd "$(dirname "$0")" && pwd)
name=$(echo "$image" | sed 's/:latest$//; s#[/:]#-#g')
out=$(mkdir -p "$outdir/$name" && realpath "$outdir/$name")
tag=ccp-distro-check:$name
ctr=ccp-distro-check-$name-$$
trap 'docker rm -f "$ctr" >/dev/null 2>&1 || true' EXIT
trap 'exit 2' ERR  # any harness step failing (pull, commit, run phase) is exit 2, never mistaken for an app failure

docker pull -q "$image" >/dev/null
if ! docker run --name "$ctr" -e HOST_UID="$(id -u)" -e HOST_GID="$(id -g)" -v "$here:/dc:ro" "$image" bash /dc/inner.sh install; then
  echo "{\"distro\":\"$name\",\"install_failed\":true}" > "$out/result.json"; exit 2
fi
docker commit "$ctr" "$tag" >/dev/null
docker rm "$ctr" >/dev/null

chmod a+rwx "$out"
docker run --rm --init --network none --shm-size 512m -u "$(id -u):$(id -g)" \
  -v "$here:/dc:ro" -v "$tar:/ccp.tar.gz:ro" -v "$out:/out" "$tag" \
  timeout 900 bash /dc/inner.sh run /ccp.tar.gz /out  # --init: coreutils 9.10 timeout exits 125 as PID 1 (fedora:44)
cat "$out/result.json"
# Exit 1 = the app never showed its window at 1920x1080; everything else is a finding in result.json.
trap - ERR
grep -q '"fhd":{"window":true' "$out/result.json" || exit 1
