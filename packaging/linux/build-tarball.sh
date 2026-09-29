#!/usr/bin/env bash
# Build the linux-x64 tarball the Flatpak manifest and the AUR PKGBUILD consume
# (docs/avalonia-linux-install.md). Local only: never uploads anything.
#   bash packaging/linux/build-tarball.sh [out-dir]   -> prints the tarball path
# Layout: ConditioningControlPanel/{CCP.Avalonia, Resources/Models/vosk/<model>, <app-id>.desktop, <app-id>.png}
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
out=${1:-$root/packaging/linux/out}
id=io.github.CodeBambi.ConditioningControlPanel
ver=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Version.props")
pub=$root/CCP.Avalonia/bin/publish/linux-x64

rm -rf "$pub"
dotnet publish "$root/CCP.Avalonia/CCP.Avalonia.csproj" -c Release -p:PublishProfile=linux-x64 >&2
test -f "$pub/libvosk.so"
# The small model (~40 MB), where SpeechEngine.DefaultModelRoots looks first.
model=$(bash "$root/.github/scripts/fetch-vosk-model.sh")
mkdir -p "$pub/Resources/Models/vosk"
cp -r "$model" "$pub/Resources/Models/vosk/"
cp "$root/packaging/linux/$id.desktop" "$root/packaging/linux/$id.png" "$pub/"

mkdir -p "$out"
tar=$out/ConditioningControlPanel-$ver-linux-x64.tar.gz
tar -czf "$tar" -C "$pub/.." --transform "s:^linux-x64:ConditioningControlPanel:" linux-x64
echo "$tar"
