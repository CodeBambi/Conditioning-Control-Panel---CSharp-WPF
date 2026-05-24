# Translation debt

Keys that landed in `en.json` without being mirrored to the 8 non-en
locale files (`de.json`, `es.json`, `fr.json`, `ja.json`, `ko.json`,
`pt-BR.json`, `ru.json`, `zh-CN.json`).

Loc.Get falls back to en when a key is **missing**, but **stale** values
in the target locale (key present, English text changed) keep showing
the old translation. For those keys, the user-visible string is wrong
in non-en builds until a translator updates them.

---

## Mission 1 (editor restructure)

### Body rewrites — stale translations need refresh (8 locales × 3 keys = 24 strings):

- `deeper_tut_ed_timeline_body` — old text mentioned "top lane R" / "bottom lane H"; new text references the four-lane chrome (Rules, Regions, Haptics, Effects).
- `deeper_tut_ed_rules_body` — old text referenced the removed `RulesList` sidebar section; new text refers to the timeline Rules lane.
- `deeper_tut_ed_selected_body` — old text said "any rule above"; new text references the selection summary strip + timeline click.

### New keys — fall back to en until translated (~50 keys):

- `deeper_editor_tip_*` namespace (commit 6, 26 keys): tooltips on every interactive editor control.
- `deeper_editor_tt_sidebar_splitter`, `deeper_editor_tt_metadata_drawer`.
- `deeper_editor_selection_*` (none / kind_rule / kind_region / kind_haptic / kind_*_plural / multi_total).
- `deeper_friendly_effect_*` (flash / bubble / subliminal / overlay / haptic).
- `deeper_editor_lane_*` (rules / regions / haptics / effects).
- `deeper_editor_tt_lane_*` (rules / regions / haptics / effects).

---

## Mission 2 (hub redesign)

### Body rewrite — stale translation needs refresh (8 locales × 1 key = 8 strings):

- `deeper_tut_tab_library_body`
  - **Was**: "Everything in your Deeper folder shows up here. Double-click to edit, right-click for play/reveal/delete.\n\nThe bundled \"Welcome to Deeper\" demo is here on first launch — open it to see a working example with regions, haptics, and rules."
  - **Now**: "Everything in your Deeper folder shows up here. Click any row to open it in the editor. Filter by type, tag, or search by name. The bundled \"Welcome to Deeper\" demo is here on first launch — open it to see a working example with regions, haptics, and rules."

### Stale key, no rewrite — note for translator:

- `deeper_tut_tab_recent_title` / `deeper_tut_tab_recent_body` are no longer
  used by any tutorial step (Mission 2 retargeted the dp_recent step to the
  new filter strip and reads `deeper_tut_tab_filters_title` / `*_body`
  instead). The recent keys can stay around for backwards-compatibility or
  be deleted at the next locale-housekeeping pass — they're dead in en.json
  the moment the user runs on a build where the step uses the new key.

### New keys — fall back to en until translated (32 keys):

#### Tutorial step (2):
- `deeper_tut_tab_filters_title` — "Find anything fast"
- `deeper_tut_tab_filters_body` — "Search by name or creator. Filter by type or tag. Sort by Recent, Name, or Creator. With a growing library this is how you find things fast."

#### Hub UI strings (11):
- `deeper_hub_search_placeholder` — "Search by name, creator, tag..."
- `deeper_hub_pill_all` / `_video` / `_audio` / `_haptics` / `_webcam`
- `deeper_hub_sort_label` — "Sort:"
- `deeper_hub_sort_recent` / `_name` / `_creator`
- `deeper_hub_empty_filtered` — "Nothing matches your filters. Loosen up..."

#### Relative timestamps (7):
- `deeper_hub_time_just_now`
- `deeper_hub_time_minutes_ago` (`{0}m ago`)
- `deeper_hub_time_hours_ago` (`{0}h ago`)
- `deeper_hub_time_days_ago` (`{0}d ago`)
- `deeper_hub_time_weeks_ago` (`{0}w ago`)
- `deeper_hub_time_months_ago` (`{0}mo ago`)
- `deeper_hub_time_years_ago` (`{0}y ago`)

#### Tooltips (12):
- `deeper_hub_tip_search`
- `deeper_hub_tip_pill_all` / `_video` / `_audio` / `_haptics` / `_webcam`
- `deeper_hub_tip_sort`
- `deeper_hub_tip_row_open` / `_play` / `_delete`

---

## How to clear an entry

When a translator picks one up:
1. Mirror the en value (or refine for the target locale) into each of the 8 locale files.
2. Strike the entry from this file (don't delete the section heading — keeps history).
3. Note the date the entry cleared (next to the strikethrough is fine).
