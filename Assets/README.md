# Assets

Every head's art, audio, models and web content, in one place that no head owns.

Not here: `ConditioningControlPanel/Resources/Theme/`. Those are WPF resource dictionaries, code
that `App.xaml` merges at startup, and they stay with the head that compiles them. The Avalonia
head ported that palette key for key into `CCP.Avalonia/Theme/`; it links nothing from it.

This tree used to live at `ConditioningControlPanel/Resources/`, inside the WPF head. That made
the head being retired the source of truth for the art, so the Avalonia head reached back into it
and any future head would have had to as well. It is a sibling of the projects now, and each head
links what it needs.

## How a head uses it

Link, never copy. One set of bytes on disk, one place to edit them, and `Link` keeps the logical
path identical on every head, so the same reference reads the same everywhere.

WPF, compiled in and addressed with `pack://`:

```xml
<Resource Include="..\Assets\features\awareness.png" Link="Resources\features\awareness.png" />
<!--  pack://application:,,,/Resources/features/awareness.png  -->
```

Avalonia, compiled in and addressed with `avares://`:

```xml
<AvaloniaResource Include="..\Assets\features\*.png" Link="Resources\features\%(Filename)%(Extension)" />
<!--  avares://CCP.Avalonia/Resources/features/awareness.png  -->
```

Loaded off disk at runtime instead of compiled in — audio, models, mod assets, web content — is
`<Content>` with `CopyToOutputDirectory`, on any head. The `Link` puts it under `Resources/` in the
output, so runtime paths that were written against the old layout still resolve.

CCP.VR and a mobile head add their own link with whatever item type their toolkit wants. Nothing
here changes for them, and they do not take a dependency on another head to get a picture.

## What each head should take

Not all of it. The tree is 1.3 GB and 14,313 files, most of it audio and models. Take what your
views actually name, and say in the csproj why anything large is in or out. Two worked examples
from the Avalonia head:

- **Twemoji (4,009 files, 19 MB) is not linked at all.** Avalonia renders COLR/CPAL colour emoji
  natively, so a plain `TextBlock` replaces what WPF needed an SVG for.
- **`achievements/` and `skills/` are listed file by file**, because six of seventy and one of
  twenty-seven are referenced. Globbing them would embed 32 MB nothing asks for.
