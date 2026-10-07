#!/usr/bin/env bash
# Build the PKGBUILD from a locally built tarball and run --smoke from the INSTALLED launcher, without sudo:
# makepkg on an Arch host, the package extracted to a temp root, and that root's /opt bound over /opt with bwrap so
# /usr/bin/conditioning-control-panel runs exactly as installed. `pacman -T` lists any depends= missing on this host.
#   bash packaging/aur/test-local.sh <ConditioningControlPanel-<ver>-linux-x64.tar.gz>
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
ver=$(sed -n 's/^pkgver=//p' "$here/PKGBUILD")
case "$(basename "$1")" in
  "ConditioningControlPanel-$ver-linux-x64.tar.gz") ;;
  *) echo "PKGBUILD pkgver=$ver does not match $(basename "$1")" >&2; exit 1 ;;
esac
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir "$tmp/build" "$tmp/root"
cp "$here/PKGBUILD" "$tmp/build/"
ln "$1" "$tmp/build/" 2>/dev/null || cp "$1" "$tmp/build/"
(cd "$tmp/build" && PKGDEST="$tmp/build" BUILDDIR="$tmp/build" SRCDEST="$tmp/build" LOGDEST="$tmp/build" SRCPKGDEST="$tmp/build" makepkg -f --skipchecksums --nodeps >&2)
pkg=$(ls "$tmp/build"/conditioning-control-panel-bin-"$ver"-*.pkg.tar.*)
tar -xf "$pkg" -C "$tmp/root"
id=io.github.CodeBambi.ConditioningControlPanel
for f in usr/bin/conditioning-control-panel usr/share/applications/$id.desktop usr/share/metainfo/$id.metainfo.xml \
         usr/share/icons/hicolor/256x256/apps/$id.png usr/share/licenses/conditioning-control-panel-bin/LICENSE \
         opt/conditioning-control-panel/CCP.Avalonia opt/conditioning-control-panel/Resources/web; do
  test -e "$tmp/root/$f" || { echo "missing in package: /$f" >&2; exit 1; }
done
deps=$(sed -n "s/^depend = //p" "$tmp/root/.PKGINFO" | tr "\n" " ")
missing=$(pacman -T $deps || true)
[ -z "$missing" ] || { echo "depends missing on this host (pacman -T): $missing" >&2; exit 1; }
echo "depends satisfied: $deps"
# Sandboxed profile: never the real settings or keyring. The model must load from the installed folder.
mkdir -p "$tmp/ud" "$tmp/cfg"
CCP_USERDATA_DIR=$tmp/ud XDG_CONFIG_HOME=$tmp/cfg CCP_SMOKE_BUNDLED_VOSK=1 timeout 300 \
  bwrap --dev-bind / / --tmpfs /opt --bind "$tmp/root/opt/conditioning-control-panel" /opt/conditioning-control-panel \
  "$tmp/root/usr/bin/conditioning-control-panel" --smoke
