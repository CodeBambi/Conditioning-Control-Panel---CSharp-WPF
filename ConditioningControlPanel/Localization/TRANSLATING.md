# Translating the Conditioning Control Panel

Thank you for helping translate the app! This guide covers everything you need to contribute a translation.

## How It Works

All translatable strings are stored as JSON files in `Localization/Languages/`. Each file is named by language code (e.g., `ja.json` for Japanese, `es.json` for Spanish).

The format is simple key-value pairs:
```json
{
  "btn_cancel": "Cancel",
  "tab_settings": "Settings",
  "msg_level_up": "Level {0}!"
}
```

## How to Contribute

### Option 1: Fork & PR (recommended)
1. Fork this repository
2. Edit your language file in `Localization/Languages/`
3. Open a pull request

### Option 2: Send via Discord
1. Download your language file from the repo
2. Translate the values
3. Send the file in our Discord server

## Rules

1. **Never change keys** — only change the values (right side of the colon)
2. **Preserve `{0}`, `{1}` placeholders** — these are filled in at runtime
   - `"msg_level_up": "Level {0}!"` → `"msg_level_up": "レベル {0}!"`
3. **Keep JSON valid** — watch for unescaped quotes. Use `\"` for quotes inside values
4. **Keep emoji** — emoji in values (like `"⚡ Flash Images"`) should be kept or adapted
5. **Use `\n` for newlines** in tooltip strings

## Key Naming Reference

Keys use prefixes to indicate context:

| Prefix | Context | Example |
|--------|---------|---------|
| `tab_` | Tab button labels | `tab_settings` |
| `btn_` | Button text | `btn_cancel` |
| `label_` | Static labels | `label_version` |
| `section_` | Section headers | `section_flash_images` |
| `setting_` | Setting labels | `setting_opacity` |
| `tooltip_` | Tooltip text | `tooltip_flash_enable` |
| `msg_` | Messages/dialogs | `msg_level_up` |
| `dialog_` | Dialog titles | `dialog_confirm` |
| `login_` | Login flow | `login_welcome` |
| `error_` | Error messages | `error_connection` |

## Untranslated Strings

Skeleton files prefix untranslated values with the language code:
```json
"btn_cancel": "[JA] Cancel"
```

Search for `[XX]` (your language code) to find strings that still need translation. Remove the prefix when you translate:
```json
"btn_cancel": "キャンセル"
```

## Testing Locally

You can test translations without rebuilding:

1. Create the folder: `%LOCALAPPDATA%/ConditioningControlPanel/Languages/`
2. Drop your JSON file there (e.g., `ja.json`)
3. Start the app and select the language from the header bar globe pill

The app checks the user data folder first, so your local file takes priority over the bundled one.

## Available Languages

| Code | Language | Status |
|------|----------|--------|
| `en` | English | Complete (reference) |
| `zh-CN` | 简体中文 | Partial |
| `ja` | 日本語 | Skeleton |
| `ko` | 한국어 | Skeleton |
| `es` | Español | Skeleton |
| `pt-BR` | Português (BR) | Skeleton |
| `fr` | Français | Skeleton |
| `de` | Deutsch | Skeleton |
| `ru` | Русский | Skeleton |

## Questions?

Ask in the Discord server or open a GitHub issue.
