# Tutorial video clips

Short, **low-res, muted** loops played by `HelpVideoWindow` (the in-app "?" video
help system). These are bundled with the build (disk-copied via the
`Resources\tutorial_videos\**\*` `<Content>` group in the `.csproj`) and resolved at
runtime as:

```
Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "tutorial_videos", <ClipFile>)
```

The `<ClipFile>` value is set on the matching `HelpContent` entry in
`Services\HelpContentService.cs`.

## Guidelines
- Format: `.mp4` (H.264 / yuv420p), played muted via LibVLC. No audio track needed.
- Keep them small: ~480x270, low frame rate, a few seconds. They ship in every
  installer, so size matters.
- The window loops them automatically; the source clip does not need to be padded.

## Expected files (Phase 4 — first four surfaces)
These names are placeholders until final clips are recorded:

| Feature             | HelpContent SectionId | Expected file              |
|---------------------|-----------------------|----------------------------|
| Timeline editor     | `SessionEditor`       | `timeline_editor.mp4`      |
| Eye-track calibration | `WebcamCalibration` | `calibration.mp4`          |
| Mod Creator         | `Modding`             | `mod_creator.mp4`          |
| Keyword/trigger editor | `KeywordTriggers`  | `keyword_triggers.mp4`     |

> `SessionEditor` and `WebcamCalibration` are dedicated help topics added so the
> popup titlebar reads correctly for each surface (the timeline editor is not the
> read-only "Details Panel"; the calibration screen is not the "Webcam Games" tile).

## `_test_loop.mp4`
A throwaway 480x270 test pattern generated with ffmpeg for local verification of the
player (loop / mute / dispose). **Not for release** — delete before shipping.
