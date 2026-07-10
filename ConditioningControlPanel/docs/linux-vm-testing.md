# Linux VM Testing Guide

This guide explains how to build and run the Avalonia cross-port of Conditioning Control Panel inside a Linux VirtualBox VM.

## What You Can Test on Linux

- `CCP.Core` — pure .NET 8 engine/models/services.
- `CCP.Avalonia.Desktop.Linux` — Avalonia desktop head (`CCP.Desktop.Linux` executable).
- Headless unit tests in `tests/CCP.Core.Tests`.

**Not yet supported on Linux:**
- Windows-only features: global low-level keyboard hooks, system-key suppression, desktop wallpaper override, GDI screen capture, WebView2 browser, NAudio/WASAPI ducking.
- Android head (requires the .NET Android workload).

## Prerequisites on the Linux VM

### 1. Install .NET 8 SDK

```bash
# Ubuntu/Debian example
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

For other distros, see https://learn.microsoft.com/en-us/dotnet/core/install/linux

### 2. Install Avalonia / Skia Runtime Dependencies

```bash
sudo apt-get update
sudo apt-get install -y \
    libicu-dev \
    libfontconfig1 \
    libfreetype6 \
    libgl1 \
    libgtk-3-0 \
    libice6 \
    libsm6 \
    libx11-6 \
    libxcursor1 \
    libxext6 \
    libxi6 \
    libxinerama1 \
    libxrandr2 \
    libxss1 \
    libxt6 \
    libxxf86vm1
```

### 3. Install LibVLC (for video playback)

```bash
sudo apt-get install -y libvlc-dev libvlccore-dev vlc
```

This gives the Avalonia/LibVLCSharp video stack the native engine it needs.

## Transfer Project to the VM

### Option A: VirtualBox Shared Folder (easiest)

1. In VirtualBox Manager, select the VM → Settings → Shared Folders.
2. Add a new shared folder:
   - Folder Path: `E:\Code\Conditioning-Control-Panel\ConditioningControlPanel`
   - Folder Name: `ccp`
   - Check **Auto-mount** and **Make Permanent**
3. Start the VM and mount it:

```bash
sudo mkdir -p /mnt/ccp
sudo mount -t vboxsf ccp /mnt/ccp
```

If you get permission errors, add your user to the `vboxsf` group and reboot:

```bash
sudo usermod -aG vboxsf $USER
```

4. Work from `/mnt/ccp`:

```bash
cd /mnt/ccp
```

### Option B: SCP / SFTP

From the Windows host (PowerShell, inside the repo folder):

```powershell
scp -r . user@linux-vm-ip:/home/user/ccp
```

Then inside the VM:

```bash
cd ~/ccp
```

### Option C: Git

Push the repo to a remote and clone it inside the VM.

## Build and Test on Linux

A convenience script is provided at the repo root. It checks for dependencies
(.NET 8 SDK, Avalonia/Skia libraries, LibVLC) and installs them automatically
on Ubuntu/Debian:

```bash
cd /mnt/ccp   # or wherever you copied the project
./build-linux.sh
```

If your user needs a sudo password, pass it via the `SUDO_PASSWORD` variable so
the prompt is not swallowed by the log tee:

```bash
SUDO_PASSWORD='1234' ./build-linux.sh
```

This script will:
1. Install the .NET 8 SDK (using Microsoft's distro-agnostic installer) if missing.
2. Install Avalonia / Skia runtime libraries if missing.
3. Install LibVLC if missing.
4. Build `CCP.Core`.
5. Run `CCP.Core.Tests`.
6. Build `CCP.Avalonia.Desktop.Linux`.
7. Run the Avalonia Linux head for up to 30 seconds to verify it launches (the script uses `timeout 30s`; corrected 2026-07-10 — the previously documented "15 seconds" never matched `build-linux.sh`).

Logs are written to `logs/linux-build-*.log` and `logs/linux-run-*.log`.

### Manual Steps

If you prefer to run commands manually:

```bash
# Build Core engine
dotnet build CCP.Core/CCP.Core.csproj

# Run unit tests
dotnet test tests/CCP.Core.Tests/CCP.Core.Tests.csproj

# Build Linux desktop head
dotnet build CCP.Avalonia.Desktop.Linux/CCP.Avalonia.Desktop.Linux.csproj

# Run the app
dotnet run --project CCP.Avalonia.Desktop.Linux/CCP.Avalonia.Desktop.Linux.csproj
```

## Expected Results

- `CCP.Core` builds with 0 errors.
- `CCP.Core.Tests` passes.
- `CCP.Avalonia.Desktop.Linux` builds with 0 errors.
- The Avalonia window opens and shows the tab shell placeholder.

## Troubleshooting

### `libvlc` not found at runtime

```bash
export LD_LIBRARY_PATH="$LD_LIBRARY_PATH:/usr/lib/x86_64-linux-gnu"
```

Or install `vlc` which places the libraries in the standard path.

### Skia / OpenGL errors in the VM

VirtualBox 3D acceleration can be flaky. Try:

```bash
export AVALONIA_D3D11_DISABLED=1
export LIBGL_ALWAYS_SOFTWARE=1
dotnet run --project CCP.Avalonia.Desktop.Linux/CCP.Avalonia.Desktop.Linux.csproj
```

Or disable 3D acceleration in VirtualBox settings and use software rendering.

### Missing `libX11`, `libXcursor`, etc.

Install the full Avalonia runtime dependency list shown in step 2.

### File permissions from Windows shared folder

If files show as executable incorrectly or line endings break scripts:

```bash
# Fix line endings for shell scripts
sudo apt-get install -y dos2unix
dos2unix build-linux.sh
```

## CI Alignment

The same Linux commands can be used in GitHub Actions / GitLab CI:

```yaml
- run: sudo apt-get update && sudo apt-get install -y libvlc-dev libvlccore-dev vlc
- run: dotnet build CCP.Core/CCP.Core.csproj
- run: dotnet test tests/CCP.Core.Tests/CCP.Core.Tests.csproj
- run: dotnet build CCP.Avalonia.Desktop.Linux/CCP.Avalonia.Desktop.Linux.csproj
```
