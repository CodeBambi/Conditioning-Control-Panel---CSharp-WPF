#!/usr/bin/env bash
# Build the linux-x64 tarball the Flatpak manifest and the AUR PKGBUILD consume
# (docs/avalonia-linux-install.md). Local only: never uploads anything.
#   bash packaging/linux/build-tarball.sh [out-dir]   -> prints the tarball path
# Layout: ConditioningControlPanel/{CCP.Avalonia, LICENSE, Resources/Models/vosk/<model>,
#         share/{applications,icons/hicolor/*/apps,metainfo}/<app-id>.*}  (share/ maps onto /usr/share or /app/share)
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
out=${1:-$root/packaging/linux/out}
ver=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Version.props")
pub=$root/CCP.Avalonia/bin/publish/linux-x64

rm -rf "$pub"
dotnet publish "$root/CCP.Avalonia/CCP.Avalonia.csproj" -c Release -p:PublishProfile=linux-x64 >&2
test -f "$pub/libvosk.so"
# The small model (~40 MB, sha256-pinned), where SpeechEngine.DefaultModelRoots looks first.
model=$(bash "$root/.github/scripts/fetch-vosk-model.sh")
mkdir -p "$pub/Resources/Models/vosk"
cp -r "$model" "$pub/Resources/Models/vosk/"
cp -r "$root/packaging/linux/share" "$pub/"
cp "$root/LICENSE" "$pub/"

mkdir -p "$out"
tar=$out/ConditioningControlPanel-$ver-linux-x64.tar.gz
# Reproducible: sorted names, fixed owner and mtime, no gzip timestamp.
tar --sort=name --owner=0 --group=0 --numeric-owner --mtime=@0 \
  --transform "s:^linux-x64:ConditioningControlPanel:" -C "$pub/.." -cf - linux-x64 | gzip -n > "$tar"
echo "$tar"
