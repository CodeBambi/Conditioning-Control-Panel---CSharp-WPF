# CCP Modding Guide

A mod (`.ccpmod`) is a creator-made content pack for the Conditioning Control
Panel: a companion personality with its own name, theme colors, avatar art,
voiced bark lines, mantras, portraits/emotes, and optionally a Down The Rabbit
Hole descent pack. This guide covers where mods live, how to make one, and how
sharing works.

> **The one thing to know about sharing:** mods are **user-created and
> user-hosted**. CC Labs does not host mod files. You upload your `.ccpmod` to
> MEGA yourself; the community catalogue lists your mod's name, preview, and
> description with a link to *your* MEGA. If you take the file down, the
> download stops working.

## Where mods end up

| What | Where |
|------|-------|
| Installed mods | `%LOCALAPPDATA%\ConditioningControlPanel\mods\<mod-id>\` (one folder per mod, extracted) |
| Built-in mods (Bambi Sleep, Sissy Hypno, CCP Default, Dronification, Infection Control, Circe's Lock) | `%LOCALAPPDATA%\ConditioningControlPanel\builtin_mods\` (managed by the app, not editable) |
| Exported `.ccpmod` files | Wherever you chose to save them in the export dialog |
| Shared mods (the catalogue) | https://app.cclabs.app/catalogue/mods |

## Installing a mod

Two ways, same result:

1. **Drag & drop** - drag a `.ccpmod` file onto the main window. The app shows
   the mod's name and author and asks you to confirm before installing.
2. **Manage Mods → Install From File** - pick the `.ccpmod` in a file dialog.

After installing, open **Manage Mods** and click **Activate** on the mod.
Only install mods from creators you trust - a mod changes your companion's
personality, voice lines, and visuals.

To find mods, click **🌐 Get Mods** in Manage Mods (or browse
https://app.cclabs.app/catalogue/mods). Download the `.ccpmod` from the
creator's MEGA link, then drag it onto the app.

## Creating a mod

Open **Manage Mods → Create New** to launch the Mod Creator. The sidebar
sections, in order:

- **Info** - id, name, author, version, description, tags, minimum app
  version, preview image.
- **Theme** - accent/background/panel colors and the ambient FX palette.
- **Identity** - companion name, what the companion calls you, mode display
  name, the Talk To / Takeover button labels.
- **Achievements / Features / Skills** - badge, feature-icon and skill-node
  art.
- **Avatars** - the four-pose avatar sets (default plus the level-gated ones).
- **UI Assets** - bubble, tube, spiral GIF, logo.
- **UI Art** - the rest of the app's artwork: nav rail doors, Play wall
  heroes, vault/dashboard tiles, session and intake cards. See
  [Art overrides](#art-overrides) below.
- **Audio** - companion sounds, bubble pops, lucky chimes, voice lines.
- **Browser / Triggers / Messages / Phrases** - links, fullscreen trigger
  text, minigame messages, and the companion's speech-bubble phrase pools.
- **Text Replacements** - find-and-replace pairs applied across the UI's
  wording. The most powerful re-skinning tool in the editor.
- **Pools & Triggers / Personalities / Advanced** - image pools, alternate
  personalities, avatar-tube offsets and skill-tree label overrides.
- **Barks** - the companion's spoken one-liners per trigger, as text or
  `{text, audio}` pairs with pre-made mp3s.
- **Mantras / Event audio / Portraits / Animated emotes** - the rest of the
  content surfaces. Emotes ship as `resources/emotes/set{N}/` folders with an
  `emotes.json` manifest.

**Export** writes everything into a single `.ccpmod` (a zip with `mod.json`
at the root). Reinstalling a newer export of the same mod id upgrades it in
place.

## Art overrides

Mod art is **path shadowing**, and that is the whole contract. Any file you
put at `resources/<path>` inside your mod replaces the app's own
`Resources/<same path>`. There is no registry to declare, no manifest entry
to add, and nothing to name: `resources/features/vault.png` becomes the
Velvet Vault tile, full stop. Leave a file out and the built-in art stays.

Three consequences worth knowing:

- **The path is the compatibility surface, so the app never renames one.**
  Several files still carry historical names -
  `features/lab_gaze_hero.png`, `features/lab_focusgaze_hero.png` and
  `features/lab_aimemory_hero.png` say "lab" but now hang on the Play and
  Companion doors. Ship them under those exact names.
- **One file can feed several places.** `features/dtrh.png` paints both the
  dashboard tile and the Play wall hero; `features/lab_quiz_hero.png` paints
  the Play card, the Graded Intake header and the quick-launch rail chip.
  One upload repaints all of them.
- **Format matters, not just the picture.** The filename inside the package
  is fixed, so a JPEG saved as `.png` ships bytes that lie about their
  format. The editor now rejects a picked file whose extension does not match
  the slot, and warns above 4 MB - most of the art below is 1376x768 or
  smaller and comfortably under that.

The **UI Art** section of the editor covers these slots (the dimmed art
behind each slot in the editor is the file you are replacing; sizes are the
shipped art's real pixels, not a hard requirement):

| Group | Slots | Geometry |
|-------|-------|----------|
| Nav doors | `nav/door_home.png`, `nav/door_play.png`, `nav/door_companion.png`, `nav/door_library.png`, `nav/door_studio.png`, `nav/door_you.png`, `nav/door_settings.png` | Square transparent PNG, 64x64 shipped (128 for HiDPI) |
| Play wall | `features/dtrh.png`, `features/loom.png`, `features/goon_game.png`, `features/lab_gaze_hero.png`, `features/lab_focusgaze_hero.png`, `features/lab_quiz_hero.png`, `features/lab_aimemory_hero.png` | Wide 16:9, 1376x768 (`goon_game.png` ships 1024 square) |
| Vault & dashboard | `features/vault.png`, `features/mysterybox.png`, `features/justdrop.png`, `features/fyp.png`, `features/fyp_banner.png`, `features/awareness.png`, `features/remote_control.png`, `features/blink_trainer.png`, `features/deeper.png`, `lockdown_icon.png`, `exclusives/vault_backdrop.png` | Wide 16:9, 1376x768; the strips are wider - `fyp_banner.png` 1376x459, `lockdown_icon.png` 1527x343 |
| Session & intake | `Cards/fireworks.png`, `Cards/hearth.png`, `Cards/spotlight.png`, `intake/pass_card.png`, `intake/pass_card_bambi.png`, `intake/pass_card_sissy.png`, `intake/pass_card_circe.png`, `intake/pass_card_drone.png` | Square - session cards ~512, pass cards 1024 |

`lockdown_icon.png` lives at the **root** of `resources/`, not under
`features/`. `intake/pass_card.png` is the catch-all: supply it and it wins
for every niche, so the four per-niche cards are never reached.

Everything the older sections cover shadows the same way -
`resources/achievements/`, `resources/features/`, `resources/skills/`,
`resources/spirals/`, `resources/Cards/`, `resources/nav/`,
`resources/programs/`, `resources/quests/`, `resources/intake/`,
`resources/exclusives/` and the avatar poses at the root. The mod template
generator scaffolds all of those folders empty, and the Manage Mods details
panel shows a per-folder count of what an installed mod actually overrides.

## Sharing your mod to the catalogue

1. Install your own exported `.ccpmod` locally (the Share button only appears
   on installed, non-built-in mods).
2. Upload the `.ccpmod` file to **MEGA** (mega.nz) and copy the share link.
   MEGA is the only host the catalogue accepts in v1.
3. In **Manage Mods**, select your mod and click **Share**.
4. Fill in your creator name and tags, paste the MEGA link, affirm the
   guidelines, and submit.

What happens next:

- Your submission goes into a **review queue**. The status pill on your mod's
  row in Manage Mods tracks it (pending → approved/rejected).
- Once approved, your mod is listed publicly at
  **https://app.cclabs.app/catalogue/mods** - name, preview thumb,
  description, version, size, and a "Download on MEGA" button that goes to
  your link.
- The catalogue never stores or re-hosts the `.ccpmod` itself. Keep the MEGA
  file up (and the link unchanged) for as long as you want your mod
  downloadable. If you rotate the link, re-share with a bumped mod version.

Rules of thumb for what gets approved: your own work (or clearly credited
remixes), nothing targeting real people without consent, and compliance with
the catalogue guidelines (https://app.cclabs.app/catalogue/guidelines).

## Updating a shared mod

Bump the `version` in your mod, export, upload the new `.ccpmod` to MEGA, and
share again. Re-submitting the same version is rejected as a duplicate - the
version bump is what tells the catalogue (and users) something changed.
