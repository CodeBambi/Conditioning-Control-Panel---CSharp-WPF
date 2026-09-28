#!/usr/bin/env bash
# libsecret acceptance for CCP.Avalonia/Platform/SecretStore.cs, in a throwaway container:
#   1. round trip against a fresh gnome-keyring on the container's own session bus;
#   2. no bus at all: reads null, nothing written under HOME / the profile.
# Never touches the host keyring: the container has no access to the host's D-Bus.
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_ROLL_FORWARD=Major
dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -v q -nologo
docker build -q -t ccp-secrets-roundtrip:local scripts/secrets-roundtrip >/dev/null
run='dotnet CCP.Avalonia.Tests.dll -noLogo -method CCP.Avalonia.Tests.SecretStoreTests.'
docker run --rm -v "$PWD/Tests/CCP.Avalonia.Tests/bin/Release/net10.0:/t:ro" -w /t \
  -e DOTNET_ROLL_FORWARD=Major ccp-secrets-roundtrip:local sh -euc "
    CCP_SECRETS_ROUNDTRIP=1 dbus-run-session -- sh -euc '
      echo -n pw | gnome-keyring-daemon --unlock --components=secrets >/dev/null
      ${run}LibsecretRoundTrip | tee /tmp/rt.txt; grep -q \"Total: 1, Errors: 0, Failed: 0, Skipped: 0\" /tmp/rt.txt'
    home=\$(mktemp -d)
    HOME=\$home CCP_SECRETS_NOBUS=1 DBUS_SESSION_BUS_ADDRESS=unix:path=/nonexistent/bus \
      ${run}NoBusReadsNullAndWritesNoFile | tee /tmp/nb.txt; grep -q 'Total: 1, Errors: 0, Failed: 0, Skipped: 0' /tmp/nb.txt
    test -z \"\$(find \$home -type f)\" && echo 'no file under HOME'"
