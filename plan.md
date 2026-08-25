# Lane plan — the 250 mystery, and the WPF settings migration decision

## Mystery census (complete, as of this tree)

Every reference to `VisualsPresetDocument` in `client/src` was enumerated. `ImageScalePercent`
has exactly TWO runtime writers:

1. `Effects/FlashDraw.cs:141` `VisualsDials.SetImageScalePercent` — sole caller
   `Views/Pages/StudioPage.axaml.cs:3175`, the Size slider handler.
2. `Session/ScriptedSessionDials.cs:415` `store.Replace(model)` — restores a snapshot this same
   class captured, so it can only give back a value the document already held.

Ruled out by reading the code, not by inheriting a claim:
- No write-back in `Refresh()` (`StudioPage.axaml.cs:2860+` writes TextBlocks only); the fourth
  `_syncing` raise at `:2730` really does open `LoadDialsFromPreset()` and clear in a `finally`
  at `:2852`, with the scale write at `:2815` inside it.
- The Intensity Ramp cannot reach scale: its three dials are spiral/pink/flash OPACITY
  (`Effects/IntensityDial.cs`), ceilings 100/50/100, and `ReleaseWork` restores them.
- The scripted session cannot reach scale: `ScriptedSessionDials.Apply` writes
  `FlashOpacityPercent` only; `ScriptedSessionRamp.FlashScalePercent` is published and consumed
  by nothing.
- No migration, no deserialization default, no clamp-on-load reaches 250.
- No hard-coded 250 anywhere; `FlashSurfacePresenter.ImageScalePercent` was 100 in every commit.
- Test rigs are all isolated temp roots; none writes the real data root.
- Geometry chain re-derived: `FlashFrameSource.Render` uses the callback's size verbatim.

Arithmetic narrowing nobody has stated: the slider is the only writer and `Slider.Maximum` is
250, so the document reaching exactly 250 means `VisualsScaleSlider.Value` was exactly 250.

Physical evidence is GONE: the client's roaming data root was created 2026-08-25 09:04, last
written 15:18, and is now empty — the documents were deleted programmatically.

## Deliverable

Decision: DO NOT migrate the WPF `settings.json`; say so to the user instead of silently
diverging. Smallest honest shape — an existence check on the shipping settings file (same
directory `Entitlement/ShippingAppDataLocation` already reads for the auth token, read-only,
no content parsed, nothing logged) plus one honest line on the System page.
