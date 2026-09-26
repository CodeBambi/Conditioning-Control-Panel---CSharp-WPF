# Tutorial video clips

This folder holds no clips any more. Every "?" that used to play one now plays a drawn
help loop (`Controls/HelpLoops/`, native WPF, no LibVLC), or falls back to its text.

The plumbing stays in case a clip is ever wanted again: set `ClipFile` on the topic's
`HelpContent` entry in `Services\HelpContentService.cs` and drop the file here. It is
disk-copied by the `Resources\tutorial_videos\**\*` `<Content>` group in the `.csproj`
and resolved at runtime as:

```
Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "tutorial_videos", <ClipFile>)
```

A drawn loop always wins over a clip (`HelpVideoWindow.TryShowLoop`, `HelpPopover`).
`HelpLoopsTests.NoHelpTopic_PointsAtAMissingClip` fails if a topic names a file that is
not here.

If a clip comes back: `.mp4` (H.264 / yuv420p), muted, about 480x270, a few seconds.
It ships in every installer, so size matters.
