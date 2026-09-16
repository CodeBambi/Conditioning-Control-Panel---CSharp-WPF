/* ============================================================================
 * race/triggerTheme.js - one table: preset -> bubble kind -> plate theme.
 *
 * A trigger event carries `cue`, which is the preset the trigger catalogue gave
 * its set (`chart/editor/triggerSets.js`). This file is the only place that says
 * what a preset LOOKS like, so the bubble the player drives into and the plate
 * that flies at their face are picked off one row and can never drift apart.
 *
 *   kindForPreset(preset) -> a bubbleKinds.js id
 *   themeFor(event)       -> { preset, kind, theme, color, ink, note } or null
 *
 * WHO READS WHICH HALF. `race/track.js` takes the kind, so a trigger phrase wears
 * the same bubble every time the file is loaded rather than whatever the round
 * robin dealt it. The captions layer takes `theme` and writes one CSS class per
 * row; the animations the rows describe are its business, not this file's.
 *
 * DARK KINDS. `bubbleKinds.js` may darken a row (`spawn: false`) and a chart is
 * never allowed to place one, so a preset that points at a dark kind falls back
 * to FALLBACK_KIND. That check is made here, against the live table, so darkening
 * one more row never needs an edit in this file. It matters more than it reads:
 * `bubbles.js spawnRow` lays NO row at all for a dark kind, so a preset left
 * pointing at one would take the whole unavoidable line off the road.
 *
 * Node-clean: no DOM, no window.
 * ==========================================================================*/

import { KIND_BY_ID } from './bubbleKinds.js';
import { TRIGGER_SETS } from '../chart/editor/triggerSets.js';

/** What a preset that points at a darkened kind wears instead. `pink` since 2026-09-08 (the flash
 *  bubble went dark with it): a preset that needed a fallback is a LOUD one (the tape, the pulse),
 *  so a treat would quietly drop the effect the phrase was written for. Pink is the effect kind
 *  that spawns at any intensity, and it sits in the tint slot of THE MIX, where it never fights
 *  the strobe a `flash-pulse` row pours on the same beat. */
export const FALLBACK_KIND = 'pink';
/** And what an event with no preset at all, or one nobody wrote a row for, wears. */
export const FALLBACK_THEME = 'mark';

/**
 * A ROW MAY NAME A KIND THAT HAS NOT LANDED YET (2026-09-08). blackout, lock, melt and gifwash are
 * four new bubbleKinds.js rows; a row that names one carries `fallback` too, and until the kind is
 * there the road wears the fallback instead. The moment bubbleKinds.js has it, every one of these
 * flips on its own with no edit here. The order is: the kind if it spawns, then the row's own
 * fallback if THAT spawns, then FALLBACK_KIND, which is the last thing standing and always spawns.
 */
function liveKind(row) {
  if (!row) return null;
  if (spawnable(row.kind)) return row.kind;
  if (row.fallback && spawnable(row.fallback)) return row.fallback;
  return FALLBACK_KIND;
}

/**
 * The table. `kind` is the bubble; `theme` is the plate's CSS class; `color` and
 * `ink` are the plate's own two colours where the row fixes them, and `note` is
 * what the plate is supposed to DO, in one line, for whoever writes that class.
 */
export const THEME_BY_PRESET = {
  'blackout':     { kind: 'blackout',   fallback: 'braindrain', theme: 'ink', color: '#0f0f16', ink: '#f4f2ff', note: 'near black card, white text, the screen dims for 300 ms' },
  'pink-blink':   { kind: 'pink',       theme: 'blink',  color: '#ff3da5', ink: '#0f0f1c', note: 'hot pink, double blink' },
  'pink-wall':    { kind: 'pink',       theme: 'wall',   color: '#ff69b4', ink: '#0f0f1c', note: 'pink, wide letter spacing, slides in from both sides' },
  'freeze-snap':  { kind: 'freeze',     theme: 'frost',  color: '#8ae6ff', ink: '#0f0f1c', note: 'ice blue, frost crackle, hard stop at scale 1' },
  'snap-shake':   { kind: 'glitch',     theme: 'snap',   color: '#f4f2ff', ink: '#0f0f1c', note: 'white, one violent shake' },
  'glitch-shake': { kind: 'glitch',     theme: 'split',  color: '#ffd166', ink: '#0f0f1c', note: 'yellow, chromatic split' },
  // the flash bubble is dark (2026-09-08) and this preset is the one that MEANT it: the row is a
  // line of plain word faces now, and cues.js pours the real flash through THE MIX on the beat, so
  // the phrase still lights the room up and the strobe slot still has a way to be lit.
  'flash-pulse':  { kind: 'treat',      theme: 'pulse',  color: '#ffffff', ink: '#0f0f1c', note: 'white flash behind, three pulses' },
  'golden-rain':  { kind: 'golden',     theme: 'gold',   color: '#ffd700', ink: '#0f0f1c', note: 'gold, sparkle shards fall off the letters' },
  'spiral-air':   { kind: 'spiral',     theme: 'spiral', color: '#c8a8ff', ink: '#0f0f1c', note: 'lilac, slow rotate while it zooms' },
  'melt':         { kind: 'melt',       fallback: 'pink', theme: 'melt', color: '#4060c0', ink: '#f4f2ff', note: 'deep blue, letters sag and blur downward' },
  // THE FOUR THE CATALOGUE WAVE NEEDED (2026-09-08). The praise and the command words say
  // themselves back at the player; the blank words go soft at the edges; the doll words hold
  // still; and the cock words cover the screen, which is the owner's "we should also use often
  // the fullscreen gif overlay". Every kind named here is one that spawns today.
  'card-whisper': { kind: 'subliminal', theme: 'card',   color: '#b080ff', ink: '#0f0f1c', note: 'lilac, the words fade up one at a time and hang' },
  'drain-fog':    { kind: 'braindrain', theme: 'fog',    color: '#7d8bd0', ink: '#f4f2ff', note: 'grey blue, the letters lose their edges and drift apart' },
  'lock-hold':    { kind: 'lock',       fallback: 'freeze', theme: 'hold', color: '#ff9ecb', ink: '#0f0f1c', note: 'satin pink frame, the plate stops dead at scale 1 and holds' },
  'gif-wash':     { kind: 'gifwash',    fallback: 'gifrain', theme: 'wash', color: '#ff8a3d', ink: '#0f0f1c', note: 'the picture covers the screen and the letters sit on top of it' },
  'video':        { kind: 'video',      theme: 'scan',   color: '#ff5c6c', ink: '#f4f2ff', note: 'red, scanlines' },
  // gif rain (2026-09-08): the pictures come DOWN, so the plate comes down with them. Gold, the
  // gifrain bubble's own tint, and a shade colder than golden-rain's so the two never read alike.
  'gif-rain':     { kind: 'gifrain',    theme: 'rain',   color: '#ffc83d', ink: '#0f0f1c', note: 'gold, the letters fall in and keep falling out the bottom' },
  // A row of treats is not an effect: the road is what happens, and the plate is warm rather than loud.
  'treats':       { kind: 'treat',      theme: 'bounce', color: '#ffb3d9', ink: '#0f0f1c', note: 'warm pink, bouncy letters' },
  // A marked word, not a trigger moment: small, no zoom.
  'mark':         { kind: 'treat',      theme: 'mark',   color: '#ece8ff', ink: '#0f0f1c', note: 'soft white, small, no zoom' },
};

const spawnable = (id) => { const k = KIND_BY_ID[id]; return !!k && k.spawn !== false; };
/** The four kinds this table is ahead of; the smoke reports which of them have landed. */
export const AHEAD_OF_KINDS = Object.entries(THEME_BY_PRESET)
  .filter(([, r]) => r.fallback && !KIND_BY_ID[r.kind]).map(([, r]) => r.kind);
const SET_BY_ID = new Map(TRIGGER_SETS.map((s) => [s.id, s]));
const SET_BY_NAME = new Map(TRIGGER_SETS.map((s) => [String(s.name).toLowerCase(), s]));

/** The bubble a preset wears, with a darkened kind swapped for the fallback. */
export function kindForPreset(preset) {
  const row = THEME_BY_PRESET[String(preset || '')];
  if (!row) return null;
  return liveKind(row);
}

/**
 * The whole row for one trigger event: `cue` first, then the event's `setId`, then
 * its label read back off the catalogue, so an event that lost its preset on the
 * way through still lands on the theme its phrase belongs to.
 *
 * `color` is the row's own where the row fixes one and the SET's colour where it
 * does not, which is the plate glow for anything the table never named.
 */
export function themeFor(event) {
  if (!event || typeof event !== 'object') return null;
  const set = SET_BY_ID.get(String(event.setId || '')) || SET_BY_NAME.get(String(event.label || '').toLowerCase()) || null;
  const want = String(event.cue || (set && set.preset) || '');
  const preset = THEME_BY_PRESET[want] ? want : FALLBACK_THEME;
  const row = THEME_BY_PRESET[preset];
  return {
    preset,
    kind: liveKind(row),
    theme: row.theme,
    // The row's own colour, and the set's when this event fell through to the fallback row.
    color: (preset === want ? row.color : (set && set.color)) || row.color,
    ink: row.ink,
    note: row.note,
  };
}

/** Every preset the catalogue uses. The smoke holds the two lists level. */
export const PRESETS_IN_USE = [...new Set(TRIGGER_SETS.map((s) => s.preset))].sort();

// self-check: node race/smoke/lyrics-road-check.mjs walks every row of the table.
