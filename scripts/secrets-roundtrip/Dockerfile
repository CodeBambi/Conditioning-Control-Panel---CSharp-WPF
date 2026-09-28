# Throwaway Secret Service for the libsecret round trip (scripts/secrets-roundtrip.sh).
# Never the user's real keyring: the daemon runs on the container's own session bus.
FROM mcr.microsoft.com/dotnet/runtime:10.0
RUN apt-get update && apt-get install -y --no-install-recommends \
      gnome-keyring libsecret-1-0 dbus dbus-x11 \
    && rm -rf /var/lib/apt/lists/*
