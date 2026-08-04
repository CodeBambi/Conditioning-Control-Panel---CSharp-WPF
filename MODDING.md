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
| Installed mods | `%APPDATA%\ConditioningControlPanel\mods\<mod-id>\` (one folder per mod, extracted) |
| Built-in mods (Bambi/Sissy/Circe) | `%APPDATA%\ConditioningControlPanel\builtin_mods\` (managed by the app, not editable) |
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

Open **Manage Mods → Create New** to launch the Mod Creator. Tabs cover:

- **Basics** - id, name, author, version, description, tags, minimum app
  version, theme color, preview image.
- **Personality** - companion name, prompt/personality text, affirmation and
  rank subject lines.
- **Barks** - the companion's spoken one-liners per trigger, as text or
  `{text, audio}` pairs with pre-made mp3s.
- **Mantras / Event audio / Portraits / Emotes** - the rest of the content
  surfaces. Emotes ship as `resources/emotes/set{N}/` folders with an
  `emotes.json` manifest.
- **Pools / Advanced** - image pools and expert knobs.

**Export** writes everything into a single `.ccpmod` (a zip with `mod.json`
at the root). Reinstalling a newer export of the same mod id upgrades it in
place.

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
