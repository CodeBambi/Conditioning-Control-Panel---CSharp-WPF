# SP-111 — plan (written BEFORE the first product edit)

Branch `lane/SP-111-video-capability`, base `68bbab0a`. Pin **1648 unit / 104 headless**, confirmed
green on the untouched base by one `check-floor.mjs` run before anything was written
(`FLOOR OK: CcpClient.Tests: 1648/1648, 2 skipped; CcpClient.HeadlessTests: 104/104`).

---

## 0. THE MEASUREMENT, before the design

A throwaway console probe (net10.0-windows, scratchpad only, nothing shipped) asked this operating
system what it will actually say about a video frame reaching a surface. Every GUID and vtable slot
in it was read out of the Windows SDK headers on this machine (10.0.26100.0), not recalled:

| what | header |
|---|---|
| `MF_VERSION = (MF_SDK_VERSION << 16) \| MF_API_VERSION` = `0x00020070` | `mfapi.h:31,39,40` |
| `MFVideoFormat_RGB32` = `DEFINE_MEDIATYPE_GUID(D3DFMT_X8R8G8B8 = 22)` | `mfapi.h:2173-2176,2204`, `d3d9types.h:1379` |
| `MFMediaType_Video` | `mfapi.h:3654` |
| `MF_MT_MAJOR_TYPE` / `MF_MT_SUBTYPE` / `MF_MT_FRAME_SIZE` / `MF_MT_FRAME_RATE` / `MF_MT_DEFAULT_STRIDE` | `mfapi.h:2559,2563,3019,3023,3225` |
| `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING`, `MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC`, `MF_SOURCE_READER_MEDIASOURCE = 0xFFFFFFFF`, `MF_SOURCE_READERF_ENDOFSTREAM = 0x2` | `mfreadwrite.h:294,330,331,307` |
| `MF_PD_DURATION` | `mfidl.h:6195` |
| `IMFAttributes` vtable, 33 slots (`GetUINT32`=7, `GetUINT64`=8, `GetGUID`=10, `SetUINT32`=21, `SetGUID`=24) | `mfobjects.h` |
| `IMFMediaType` = IMFAttributes + 5; `IMFSample` = IMFAttributes + 14 (`ConvertToContiguousBuffer` = slot 41) | `mfobjects.h` |
| `IMFMediaBuffer`: `Lock`=3, `Unlock`=4, `GetCurrentLength`=5 | `mfobjects.h` |
| `IMFSourceReader`: `SetStreamSelection`=4, `GetNativeMediaType`=5, `GetCurrentMediaType`=6, `SetCurrentMediaType`=7, `ReadSample`=9, `GetPresentationAttribute`=12 | `mfreadwrite.h:461-547` |

### Q0 — the owner's media directory

```
Z:\CCP Vids exists = False
  drive C:\ type=Fixed ready=True
```

**`Z:\CCP Vids` does not exist on this machine, and neither does the Z: volume.** That is a NAMED
LIMIT, not a blocker and not a skip: the suite synthesises its own media (below) and the record says
plainly that nothing here was ever run against the owner's real library.

### Q1/Q2/Q3 — the OS decodes a real container

Media is synthesised in PURE MANAGED code: an uncompressed 32bpp RIFF/AVI (`BI_RGB`, `DIB `
handler), one solid colour per frame, no encoder and no codec anywhere in the writer. Measured:

```
wrote probe.avi = 921904 bytes, 3 frames 320x240
MFStartup(0x00020070)                = 0x00000000
MFCreateSourceReaderFromURL          = 0x00000000
GetNativeMediaType                   = 0x00000000
  native subtype = 00000016-0000-0010-8000-00aa00389b71   size = 320x240
SetCurrentMediaType(RGB32)           = 0x00000000
current type: 320x240 defaultStride=-1280
frame[0] ts=0        centre B=20 G=30 R=C0
frame[1] ts=1000000  centre B=30 G=C0 R=40
frame[2] ts=2000000  centre B=C0 G=40 R=30
ReadSample[3] end of stream
```

Two things this settles. **The OS's own media stack opens and decodes the file** — that is the
decode link and it is REAL, not a fake file reader. And **the default stride is NEGATIVE (-1280)**:
Media Foundation hands RGB32 back BOTTOM-UP, so a presenter that ignores the sign blits every video
upside down. The probe's solid frames could not have caught that; it is read from
`MF_MT_DEFAULT_STRIDE` and honoured.

### Q4 — what the OS says about the surface

```
CreateWindowExW = 0x263092A            IsWindowVisible = True
GetWindowRect   = 463,554..783,794     our exStyle = 0x08080088
screen DC: HORZRES=1646 DESKTOPHORZRES=2880  VERTRES=1029 DESKTOPVERTRES=1800
hit test at centre = 0x130938 class="HwndWrapper[ConditioningControlPanel;;019386f6-…]"  ours=0x263092A
DwmIsCompositionEnabled = True
frame 0: expected=0x2030C0 windowDC=0x2030C0 desktopDC=0x26171F printWindow=0x2030C0
frame 1: expected=0x30C040 windowDC=0x30C040 desktopDC=0x26171F printWindow=0x30C040
frame 2: expected=0xC04030 windowDC=0xC04030 desktopDC=0x26171F printWindow=0xC04030
  window DC  frame0->1 changed = True    frame1->2 changed = True
  desktop DC frame0->1 changed = False   frame1->2 changed = False
  whole-screen scan: 0 of 5184000 pixels are the frame's colour
```

**Three findings, and the third nearly ate the packet.**

1. **The window-content differential works and is exact.** The OS's own copy of the surface's pixels
   equals the DECODED frame byte for byte at the sampled point, and it CHANGES when the next frame
   is presented. `PrintWindow` — a different OS path, into a bitmap of the caller's — agrees at
   every frame. This is the packet's own named target ("a read-back of the target surface showing
   it changed between frames") and it is available unconditionally.

2. **The composited-desktop read did NOT see the surface at all — and the hit test says why.**
   `WindowFromPoint` at the surface's own centre answers
   `HwndWrapper[ConditioningControlPanel;;…]`: **the shipping WPF product is running on this desktop
   right now and owns that point with its own topmost window.** This is precisely the residue
   `RealDesktopCollection` already names ("a FOREIGN topmost window — the shipping WPF product
   re-asserting HWND_TOPMOST on a cadence … can still own a point on the real desktop, and no
   in-process mechanism can exclude one"). It is not a defect in the design; it is the machine.
   Corroborated: `FlashDrawTests.ARealImageFile_ReachesTheCompositedDesktop_…` — a LANDED fact,
   untouched by me — failed once and passed on the next run, with the failure text reporting a live
   5 184 000-pixel capture carrying 0 pixels of the flash's colour.

   **Consequence for the design:** the composited-desktop leg is real evidence and it is
   MACHINE-CONDITIONAL. It follows SP-100's own precedent exactly — the leg is gated on a MEASURED
   machine fact (the instrument can see its OWN control window in its own desktop capture), never on
   a skip, and the expectation flips with the machine rather than being asserted away.

3. **`DwmGetCompositionTimingInfo` is NOT usable and is dropped, with the reason measured.** It
   returns `0x88980090` = `MILERR_MISMATCHED_SIZE` (`winerror.h:60848`) for the struct the SDK
   header declares. Brute-forcing `cbSize` shows the shipping `dwmapi.dll` on this machine
   **accepts `cbSize=292`**, while `dwmapi.h:168-339` describes a 320-byte struct (the header's tail
   — `cPixelsReceived`, `cPixelsDrawn`, `cBuffersEmpty` — is not in the runtime's). Reading
   `cFramesDisplayed` at a header-derived offset out of a struct the runtime declares to be a
   different size would be a guess dressed as a measurement, and the counter is SYSTEM-WIDE anyway:
   any other window animating advances it, so it can never be a fact about THIS process's frames.
   **`DwmIsCompositionEnabled` is kept** (a documented `BOOL*` out-parameter, no struct, measured
   `True`) as the composition precondition.

---

## 1. THE PROVABLE CHAIN, and where it stops

| # | Fact | API | Grade |
|---|---|---|---|
| **V1** | the OS's own media stack opens the container, reports a video stream and its native frame size, and yields decoded RGB32 frames | `MFStartup` / `MFCreateSourceReaderFromURL` / `GetNativeMediaType` / `SetCurrentMediaType` / `ReadSample` | **decodable — the TRAP level, named as such** |
| **V2** | the OS holds the surface: it exists, is visible, holds the requested rectangle, and its own z-order walk puts it above every ordinary window | `IsWindow` / `IsWindowVisible` / `GetWindowRect` / `GetTopWindow`+`GetWindow` | window state |
| **V3** | **the window manager routes a point inside the surface TO the surface** — nothing is covering it there | `WindowFromPoint` | **not occluded**, and it BIT in the probe |
| **V4** | **the OS's own copy of the surface's pixels equals the DECODED frame, over a painted letterbox that reads back exactly its own colour, and DIFFERS from the previous frame's read-back** | `GetDC(hwnd)` + `GetPixel` differential | **frame-on-surface** |
| **V5** | the composited desktop carries the decoded frame at the surface's screen rectangle | `BitBlt(SRCCOPY\|CAPTUREBLT)` from the screen DC, DPI-mapped — `FlashPixelProbe`'s, the test's | **desktop-composited**, machine-conditional |

**Where it stops.** `watched-verified` — that a human saw a video — is a MANUAL gate and no automated
step on any platform discharges it. Nor does any of this prove the frames arrived in TIME, in
ORDER, at the right RATE, or with sound: there is no sound (see §3).

## 2. `Z:\CCP Vids`

Does not exist (Q0). Synthesised media only. Named limit.

## 3. WHICH MODULE — **Mandatory Video**, and it ships as a SILENT half-row

Of the three rows video blocks: Bubble Count needs video PLUS an interactive counting surface plus
result windows; Visuals is WPF's own dot-less row; **Mandatory Video is the pure video module**, so
every piece of novelty lands on the capability instead of on a second subsystem. It is PACED
(`Services/Video/VideoService.cs:2216-2229`), so `PacedSessionEffect<TFiring>` fits.

**It ships as its VIDEO half only.** Upstream plays the clip's soundtrack; `Audio/**` is closed to
this packet and A/V synchronisation is a subsystem, not a line. That is SP-109's Brain Drain shape
and it gets SP-109's three loud declarations: the row is titled so the user reads the scope first,
the panel LEADS with the missing half, and `Ready()` returns `Degraded` on EVERY run however
healthy, because the absence is a property of the BUILD and not of the run.

## 4. THE DOT'S SEVENTH MEANING — **MOTION**

Clock, screen, change, custody, reach, demand. The seventh is **MOTION: the operating system's own
copy of the surface keeps CHANGING.**

```
Live = a firing is on the clock
    && the OS says this process can put a video surface on a display
    && ( nothing is playing  OR  the OS's read-back of the surface ADVANCED on the last frame )
```

Not "the decoder is fed" — that is the packet's central trap in dot form. Not merely "a frame is on
screen" — upstream's own worst bug is exactly that state: "the window stays white and MediaEnded
never fires, wedging cleanup" (`VideoService.cs:2680-2688`) and a frozen final frame after wake
(`:1394-1397`), with every call still returning success. Upstream watches the same thing from the
other side (`_voutLostSinceTicks` `:104`, `_frameReady` `:3788`, `StartBlurFrameWatchdog` `:3265`).

**MOTION is distinct from the third meaning (CHANGE).** SP-098's "moving" is a claim about this
process's own animation state, which this process authors and knows. MOTION is a claim about what
the OS is HOLDING, read back — the first meaning that can be false while every call this process
made succeeded.

## 5. Files, and the two closed doors

New `client/src/CcpClient.Desktop/Video/`: `IVideoPresence.cs` (+ observations), `VideoReasonCodes.cs`,
`VideoFrame.cs`, `IVideoClipSource.cs`, `MediaFoundationInterop.cs`, `MediaFoundationClipSource.cs`,
`Win32VideoInterop.cs`, `Win32VideoPresence.cs`, `UnsupportedVideoPresence.cs`,
`VideoPresenceFactory.cs`.
New under `Effects/`: `MandatoryVideoEffect.cs`, `MandatoryVideoSchedule.cs`, `VideoClipPool.cs`,
`VideoSurfacePresenter.cs`. `Session/MandatoryVideoPresetDocument.cs`,
`Views/Pages/VideoPanelNotices.cs`. Changed: `Session/EffectReasonCodes.cs`,
`Session/SessionParticipant.cs`, `Views/Pages/StudioPage.axaml(.cs)`.

**`Overlay/**`, `Input/**` and `Audio/**` are CONSUMED, never edited.** The video surface owns its
own hwnd for the same reason the card does: the capability's whole content is the OS's answer about
frames on THAT surface, and `IOverlayPresence` cannot be asked it without editing a closed file.
Coexistence is proven with SP-099's own `OverlayWindowProbe` and SP-110's own `InputWindowProbe`,
both unmodified, around a real video surface opening and closing.

**The surface does NOT take the foreground** (`WS_EX_NOACTIVATE`) and is **not fullscreen**, both
divergences from `VideoService.cs:2617-2636` (`ShowActivated`, `Topmost`, full screen bounds), for
trap 1 and on SP-110's own D110 precedent.
