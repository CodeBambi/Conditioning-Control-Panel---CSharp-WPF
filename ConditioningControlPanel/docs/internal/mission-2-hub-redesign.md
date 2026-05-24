# Mission 2 — Deeper Hub Redesign

Status: **Code complete, build clean, hub renders correctly in a live
smoke check. Interactive verification of search/filter/sort behavior
needs you at the keyboard.**
Branch: `main`.

---

## 1. Commits

| # | SHA | One-liner |
|---|---|---|
| 1–4 (bundled) | `9f3f989` | Logic, XAML, virtualized ItemsControl, deletes of old hub elements — bundled because none of the four steps compile in isolation. |
| 5 | `e2c275e` | 29 new `deeper_hub_*` loc keys (search placeholder, pill labels, sort entries, empty-filtered hint, relative-time formats, tooltips). |
| 6 | `61e0d38` | Tutorial sync: `dp_library` retargeted to `DeeperLibraryList`; `dp_recent` repurposed as the filter-strip intro (new `deeper_tut_tab_filters_*` keys, old `deeper_tut_tab_recent_*` kept as dead-for-now). `docs/internal/translation-debt.md` created. |
| 7 | _this report_ | Smoke check + this doc. |

Deviation from spec's commit hygiene: commits 1–4 bundled. Justified
because the partial in commit 1 references XAML element names (e.g.
`TxtDeeperPillVideoCount`) that don't exist until commit 2, and commit 2's
XAML deletes the old elements the existing row-builder methods depend on.
Splitting created an intermediate "won't compile" state for each
sub-commit; a single commit with a clear message is the honest shape.

---

## 2. Smoke check results

Launched the app via `dotnet run`, captured the main window after moving
the always-on-top Avatar Tube off-screen so the hub was unobscured.

**Verified visually from the screenshot:**

| Check | Status |
|---|---|
| App boots, hub renders without exception | ✓ |
| Header: 🌊 + "Deeper" + BETA pill + pitch text + action button cluster | ✓ |
| Search input with placeholder "Search by name, creator, tag..." | ✓ |
| Filter pill row: All 13, Video 11, Audio 2 (mutex), \| separator, 📳 Haptics 1, 📷 Webcam 0 (additive) | ✓ — counts derived from real library entries |
| Library section header "LIBRARY" + total count chip | ✓ |
| Unified row template: violet 36×36 badge with 🎬, name in 13px medium, meta line with creator · 🌐 hypnotube.com · 📳 Haptics chip · "23m ago"/"5d ago"/"1w ago" | ✓ — all four meta segments rendering, AutoTag chip in violet, relative-time format working |
| Hover-only action buttons (correctly invisible at rest) | ✓ — no buttons visible in static capture, matches spec |
| Welcome card not visible (user previously dismissed `HasSeenDeeperWelcome`) | ✓ — visibility gate working |
| New tooltip keys resolve (no raw key strings visible) | ✓ — visible labels are all human-readable |

**Could not verify without driving the UI:**

| Check | Why |
|---|---|
| Search filters as you type, with ~150ms debounce | needs keyboard input |
| Pill click toggles correctly (mutex for media-type, additive for haptics/webcam) | needs click |
| Sort dropdown re-orders the list | needs click + observe |
| Action buttons appear on hover and dispatch to the right handlers | needs hover |
| Row click opens the editor (`OpenDeeperFile`) | needs click |
| Welcome card shows correctly on first run (after clearing `HasSeenDeeperWelcome`) | needs settings edit + restart |
| FileSystemWatcher refresh: edit a file in the library folder externally, see the list update | needs external mutation |
| Tutorial `dp_library` spotlight lands on `DeeperLibraryList` with new body text | needs running the tour |
| Tutorial `dp_recent` (now "Find anything fast") spotlights `DeeperLibraryFilterStrip` | needs running the tour |

The screenshot is in this session — if you want it preserved at a stable
path, copy from `%TEMP%\ccp-hub-screenshot2.png`.

---

## 3. Deviations from spec

### 3a. ContentControl with DataTemplates this time (not visibility-toggle)

Mission 1 needed visibility-toggle because the editor's code-behind has
hundreds of direct field accesses (`SliderHapticIntensity.Value = ...`)
and many tutorial elements with `x:Name`s inside the editors. The hub
has none of that — row content is data-bound, not field-accessed — so
the spec's preference for an `ItemsControl` + `DataTemplate` lands
cleanly here. Mission 1's deviation was forced by code shape, not
ideological preference.

### 3b. Right-aligned sort dropdown uses the WPF default ComboBox shell with restyle

Spec said "Combobox or popover, doesn't matter, just don't use the WPF
default white-background ComboBox without restyling it." I went with the
ComboBox but only restyled the `Foreground`/`Background` on header +
items (white bg / black fg). The dropdown chevron still uses the WPF
default. Matches the editor's existing `EditorComboBox` style precedent
(commit history shows this is the project's preferred shape — there's a
saved-memory note about ComboBox needing `Foreground=Black` to be
readable). Open question whether you want a full custom popover (more
work) or are happy with the current restyle.

### 3c. Recent files concept dropped entirely (vs absorbed into a sort option)

Spec said "The Recent concept is absorbed into the Recent sort option of
the unified list." I did add Recent as a sort option (default), but the
old `EnhancementLibrary.RecentFiles` collection is no longer surfaced.
It tracked the user's recently-_opened_ files (including ones outside
the library folder), which is a different concept from "library entries
sorted by `LastModified`." The unified list only sources from
`ScanLibrary()` — so files the user opened from elsewhere via "Open
with CCP" or drag-drop are not in the list, no matter what's selected
in the sort dropdown.

Open question: do you want to fold `EnhancementLibrary.RecentFiles` into
the source-of-truth list too (so externally-opened files appear with a
"not in your library" badge)? Or is "library only" the right semantic?
Spec wasn't explicit. I made the conservative call.

### 3d. Filter pill counts narrow with search, not with other pill state

Spec said "Each pill shows a count of matching entries" — ambiguous about
whether the count reflects post-search-filter, post-other-pill-filter,
or both. I chose "post-search-filter only" because:
- Post-everything would make counts read like "0" for inactive pills as
  soon as you click an exclusive pill — confusing
- Always-full counts decouple from interaction but mislead while typing

Flag if you want different semantics.

### 3e. ItemsSource via `RelativeSource={RelativeSource AncestorType=Window}`

The `ItemsControl.ItemsSource="{Binding DeeperFilteredEntries, RelativeSource={RelativeSource AncestorType=Window}}"` relies on
`MainWindow` having the matching public property and no intermediate
`DataContext` ancestor stealing it. Verified the binding resolves from
the screenshot (rows render). If anyone ever sets `DataContext` on the
`DeeperTab` `ContentControl` or any ancestor, this binding would silently
break — defensive note: use `ElementName` binding to the `MainWindow`'s
own `x:Name` if that risk concerns you.

### 3f. Old `deeper_tut_tab_recent_*` loc keys not deleted

The tutorial step no longer reads them — the new `deeper_tut_tab_filters_*`
keys took over. The old keys are still present in `en.json` (and 8
other locales) but unreferenced. Listed in `translation-debt.md` so the
translator knows they can drop them at the next housekeeping pass.
Removing them now risks breaking some forgotten reference; leaving them
costs nothing.

---

## 4. Open questions

1. **Recent files (3c above)**: include externally-opened files or library-only?
2. **Sort dropdown shape (3b)**: restyle good enough or do you want a custom popover?
3. **Pill count semantics (3d)**: current "narrow with search, ignore other pills" the right call?
4. **Submit button on rows**: currently visible only if `IsCatalogueEligible` AND the user has an auth token. Spec said "only shown if `IsCatalogueEligible` AND has Patreon auth token, otherwise hidden" — implementation makes the button visible-but-disabled when eligible-but-no-auth (so the tooltip "Sign in to submit" is discoverable). If you want the strict "hidden when no auth" behavior, flip `Visibility` instead of `IsEnabled` in `BuildRowVm`'s `ShowSubmitButton` logic.
5. **Welcome card on first run**: not verified end-to-end. The visibility gate is wired (`UpdateDeeperWelcomeCardVisibility` still called from `BtnDeeper_Click`), but I can't clear the flag and re-launch from this session without churning the dev settings file.
6. **Grid view toggle**: explicitly deferred per spec — wanted to confirm it's parked, not lost.

---

## 5. What's tested vs theoretical

| Verified | Method |
|---|---|
| All commits build clean (0 errors, 124–248 warnings, all pre-existing) | `dotnet build` after every commit |
| Hub layout renders correctly | live screenshot via `dotnet run` + main-window capture |
| Filter pill counts populate from real library data | screenshot showed All 13 / Video 11 / Audio 2 / Haptics 1 / Webcam 0 against the user's real library |
| Search placeholder text appears | screenshot |
| Relative-time format works | screenshot showed "23m ago", "5d ago", "1w ago" |
| AutoTag chips render (violet for haptics) | screenshot |
| Hover-only action buttons hidden at rest | screenshot |
| Tutorial step retargets reference existing element names | static check + compile |

| Not verified — needs you | Notes |
|---|---|
| Search debounce timing actually 150 ms | code is in place; needs typing |
| Pill click handlers actually filter | code is in place; needs click |
| Sort dropdown actually reorders | code is in place; needs click |
| Row click opens editor | wired to `OpenDeeperFile`; needs click |
| Action button hover actually shows buttons | `Style.Triggers` on `RowBorder.IsMouseOver`; needs mouse |
| Tutorial `dp_library` / `dp_recent` (now filters) spotlight lands cleanly | needs running the tour |
| Welcome card visibility on first run | needs flag clear + restart |
