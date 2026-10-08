# Linux install experience (user request, 2026-09-28) — DO NOT DROP

Goal: installing CCP on Linux must be easy for a non-technical user. Installing CCP installs every dependency; the user
never has to find packages by hand. This belongs to the installer / Linux package phase (phase 5) and must be done before
"retire WPF from shipping" counts as finished.

## Why this came up
On a fresh box, web features show "WebKitGtk library is not installed. Install webkit2gtk 4.0+ package." The user had to
install `webkit2gtk-4.1` by hand. Avalonia.Controls.WebView 12.1.0 loads the GTK3 builds only (libwebkit2gtk-4.0.so.37 or
libwebkit2gtk-4.1.so.0 + libjavascriptcoregtk-4.1 + libsoup-3.0/2.4); webkitgtk-6.0 (GTK4) does NOT work.

## Runtime dependencies to declare (verify the list again at packaging time)
| Need | Arch / CachyOS | Debian / Ubuntu | Fedora |
|---|---|---|---|
| Web views (WebHost) | webkit2gtk-4.1 | libwebkit2gtk-4.1-0 | webkit2gtk4.1 |
| Web views, WPE path (Avalonia WebView also loads libwpe-1.0.so.1, libWPEBackend-fdo-1.0.so.1, libWPEWebKit-2.0.so.1; user hit "WPE WebKit libs not installed") | wpewebkit wpebackend-fdo libwpe | libwpewebkit-2.0-1 libwpebackend-fdo-1.0-1 libwpe-1.0-1 (verify names) | wpewebkit wpebackend-fdo libwpe (verify) |
| Audio / video (LibVLCSharp) | vlc (libvlc) | libvlc5 + vlc-plugin-base | vlc-libs |
| Sign-in token store | libsecret | libsecret-1-0 | libsecret |
| Overlays, panic key (X11/XInput2) | libx11 libxi | libx11-6 libxi6 | libX11 libXi |
.NET: publish self-contained, so no runtime dependency.

## Plan (channel order CONFIRMED by the user 2026-09-28: own Flatpak repo + AUR first; Flathub after a content-policy check)
1. **Flatpak (primary, every distro, one click).** GNOME runtime already has WebKitGTK; bundle libVLC and the WPE libs (wpewebkit, wpebackend-fdo, libwpe) if the runtime lacks them; self-contained .NET.
   finish-args: `--socket=x11` (overlays need X11/XWayland; not fallback-x11), `--talk-name=org.kde.StatusNotifierWatcher`
   (tray), portals for notifications, GlobalShortcuts and Secret (libsecret uses the Secret portal inside Flatpak).
   Updates come from the store. Host: own Flatpak repo + `.flatpakref` on the website first; Flathub only after checking
   its content policy for this app.
2. **AUR package** (Arch/CachyOS): `depends=(webkit2gtk-4.1 wpewebkit wpebackend-fdo libwpe vlc libsecret libx11 libxi)`.
3. Later, on demand: `.deb` / `.rpm` with the same dependency lists; AppImage only as a fallback (cannot pull WebKit).

## In-app work that goes with it
- Distro-aware missing-dependency message: read `/etc/os-release` and show the exact command with a Copy button
  (pacman / apt / dnf) instead of the generic "Install webkit2gtk 4.0+ package". Keep it as the fallback for tarball users.
- Desktop integration: `.desktop` file, icon, fixed app id (also fixes KDE filing the portal panic shortcut under the
  launching app; call `org.freedesktop.host.portal.Registry.Register`), optional "start on login".
- Updater on Linux: "update available — update via your store / package manager" only; never replace files in place.

## Acceptance (when this is built)
- Fresh VM/container per target (Flatpak on a non-Arch distro, AUR on Arch): install with one command/click, launch, and
  web views, audio/video, tray, overlays, panic shortcut and sign-in token storage all work with no manual package installs.
- Record a decision row in docs/avalonia-decisions.md and a ledger row for the installer.

## Carry into the repo
The first branch after this note that touches docs must commit this file as `docs/avalonia-linux-install.md` (it lives in
~/ccp-port/briefs only because every worktree had a worker writing to it when the note was made).

## Package verification (avalonia-port/package-verify, 2026-10-07, 7.0.5)
Evidence: ~/ccp-port/evidence/package-verify/.
- **Tarball:** `packaging/linux/build-tarball.sh` (contents not listed in the evidence; includes Resources/web, libvosk,
  libOpenCvSharpExtern, onnxruntime) then `smoke-tarball.sh`: extracted, sandboxed (`CCP_USERDATA_DIR`), `--smoke` exit 0
  with LibVLC and the bundled Vosk model passing (`build-tarball.log`, `smoke-tarball.log`). `smoke-tarball.sh` now also
  fails when `packaging/aur/PKGBUILD` pkgver differs from the tarball version (it was 6.11.3 under 7.0.5; fixed;
  `smoke-tarball-pkgver-failproof.log`).
- **AUR:** Docker on the dev box cannot start any container (overlayfs mount `invalid argument`, alpine included), so the
  archlinux-container run was replaced by `packaging/aur/test-local.sh` on the CachyOS host: `makepkg` (no install, no sudo),
  package extracted to a temp root, its `/opt` bound with `bwrap`, and the installed `/usr/bin/conditioning-control-panel
  --smoke` passes with the model loading from `/opt/conditioning-control-panel` (`aur-local.log`). It fails on a pkgver
  mismatch, a `depends=` entry the host lacks (`pacman -T`) and a missing packaged file (`aur-local-failproofs.log`).
  `ldd` over every bundled `.so`: all owning packages are inside the `pactree` closure of `depends=` (zlib via
  zlib-ng-compat's provide); only `liblttng-ust.so.0` (optional .NET tracing) is unresolved (`aur-native-deps.txt`).
  A clean-container proof of `depends=` sufficiency is still owed: the host has far more than the closure installed.
- **Flatpak:** no flatpak-builder and no org.gnome.Sdk//50 on the box and Docker is down, so the manifest is still unbuilt.
  Substitute proof: the extracted tarball run inside the installed `org.gnome.Platform//50` runtime (`flatpak run
  --command=...`) passes every smoke check except LibVLC (`libvlc` absent), which is exactly the module the manifest
  bundles (`flatpak-runtime-smoke.log`). The runtime has webkit2gtk-4.1, javascriptcoregtk-4.1, soup-3, libsecret, libX11,
  libXi, libpulse, ICU and `parec`; it lacks libvlc and the WPE trio (`flatpak-runtime-libs.txt`).
- **Desktop/metainfo:** `desktop-file-validate` clean; `appstreamcli validate --pedantic` passes with two pedantic notes
  (uppercase in the component id, which is the fixed app id, and no `<releases>`) (`validate.txt`).
