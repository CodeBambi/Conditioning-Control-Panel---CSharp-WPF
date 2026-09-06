/* ============================================================================
 * editor/triggerSets.js - the trigger catalogue, as data (chart/EDITOR.md, PR U5).
 *
 * Data only: no imports, no DOM, no window, so rules.js, triggers.js and
 * model.js can all read it without a cycle. Every phrase was counted over the
 * eleven aligned Bambi Sleep tracks, so a count of 0 in the tab means the file
 * really does not say it, never that the phrase is wrong. One fixed hue per
 * set, so a marker reads the same in every project (UX appendix B). Every hue
 * here clears 5.2:1 against the ink `labelInk` gives it (PR U8).
 *
 * Shared with the Track Maker: chart/maker/triggers.js re-exports this whole
 * catalogue, so a trigger means the same phrase, the same colour and the same
 * cue on both pages. Change a set here and both tools change with it.
 * ==========================================================================*/

export const TRIGGER_SETS = [
  { id: 'bambi-sleep', name: 'bambi sleep', group: 'named', phrase: 'bambi sleep', mode: 'exact', color: '#b080ff', preset: 'blackout' },
  { id: 'good-girl', name: 'good girl', group: 'named', phrase: 'good girl', mode: 'exact', color: '#ff69b4', preset: 'pink-blink' },
  { id: 'bimbo-doll', name: 'bimbo doll', group: 'named', phrase: 'bimbo doll', mode: 'exact', color: '#ff3da5', preset: 'pink-blink', cluster: 1.5 },
  { id: 'bambi-freeze', name: 'bambi freeze', group: 'named', phrase: 'bambi freeze', mode: 'exact', color: '#8ae6ff', preset: 'freeze-snap' },
  { id: 'bambi-reset', name: 'bambi reset', group: 'named', phrase: 'bambi reset', mode: 'exact', color: '#ffffff', preset: 'snap-shake' },
  { id: 'uniform-lock', name: 'bambi uniform lock', group: 'named', phrase: 'uniform lock', mode: 'exact', color: '#ffd166', preset: 'glitch-shake' },
  { id: 'drip-drop', name: 'drip drop', group: 'named', phrase: 'drip drop', mode: 'exact', color: '#78ffbe', preset: 'golden-rain' },
  { id: 'snap-forget', name: 'snap and forget', group: 'named', phrase: 'snap and forget', mode: 'exact', color: '#f4f2ff', preset: 'snap-shake' },
  { id: 'zap-cock-drain', name: 'zap cock drain', group: 'named', phrase: 'zap cock drain', mode: 'exact', color: '#ff5c6c', preset: 'flash-pulse' },
  { id: 'primped', name: 'primped and pampered', group: 'named', phrase: 'primped and pampered', mode: 'exact', color: '#ffb3d9', preset: 'treats' },
  { id: 'does-as-told', name: 'bambi does as she is told', group: 'named', phrase: "does as she(?:'s| is) told", mode: 'regex', color: '#c8a8ff', preset: 'spiral-air' },
  { id: 'cockslut', name: 'bambi cockslut', group: 'named', phrase: 'cock ?slut', mode: 'regex', color: '#ee7078', preset: 'video' },
  { id: 'takeover', name: 'bambi takeover', group: 'named', phrase: 'take(?:s|n)? ?over', mode: 'regex', color: '#4060c0', preset: 'melt' },
  { id: 'awaken', name: 'bambi awaken', group: 'named', phrase: 'awaken|wake up|wide awake', mode: 'regex', color: '#ffd6a0', preset: 'flash-pulse' },
  { id: 'giggle-time', name: 'giggle time', group: 'named', phrase: 'giggle ?time', mode: 'regex', color: '#ffe066', preset: 'treats' },
  { id: 'sleep-now', name: 'sleep now', group: 'named', phrase: 'sleep now', mode: 'exact', color: '#6f4f9c', preset: 'blackout' },
  { id: 'w-blank', name: 'blank', group: 'words', phrase: 'blank', mode: 'exact', color: '#ece8ff', preset: 'mark' },
  { id: 'w-pink', name: 'pink', group: 'words', phrase: 'pink', mode: 'exact', color: '#ff3da5', preset: 'pink-wall' },
  { id: 'w-obey', name: 'obey', group: 'words', phrase: 'obey', mode: 'exact', color: '#b080ff', preset: 'mark' },
  { id: 'w-empty', name: 'empty', group: 'words', phrase: 'empty', mode: 'exact', color: '#8fd0ff', preset: 'mark' },
  { id: 'w-sleep', name: 'sleep', group: 'words', phrase: 'sleep', mode: 'exact', color: '#6b8cff', preset: 'mark' },
  { id: 'w-drop', name: 'drop', group: 'words', phrase: 'drop', mode: 'exact', color: '#78ffbe', preset: 'spiral-air' },
  { id: 'w-dumb', name: 'dumb', group: 'words', phrase: 'dumb', mode: 'exact', color: '#ffd166', preset: 'mark' },
  { id: 'w-mindless', name: 'mindless', group: 'words', phrase: 'mindless', mode: 'exact', color: '#c8a8ff', preset: 'mark' },
  { id: 'w-forget', name: 'forget', group: 'words', phrase: 'forget', mode: 'exact', color: '#9a97b8', preset: 'mark' },
  { id: 'w-trance', name: 'trance', group: 'words', phrase: 'trance', mode: 'exact', color: '#40d0c0', preset: 'mark' },
  { id: 'w-relax', name: 'relax', group: 'words', phrase: 'relax', mode: 'exact', color: '#b8ffd9', preset: 'mark' },
  { id: 'w-accept', name: 'accept', group: 'words', phrase: 'accept', mode: 'exact', color: '#ffb3d9', preset: 'mark' },
  { id: 'w-giggle', name: 'giggle', group: 'words', phrase: 'giggl', mode: 'contains', color: '#ffe066', preset: 'treats' },
  { id: 'w-lock', name: 'lock', group: 'words', phrase: 'lock', mode: 'exact', color: '#ffd6a0', preset: 'mark' },
  { id: 'deeper', name: 'deeper and deeper', group: 'sequence', phrase: 'deeper and deeper', mode: 'exact', color: '#6b8cff', preset: 'melt' },
  { id: 'countdown', name: 'countdown', group: 'sequence', mode: 'regex', color: '#ffd166', preset: 'blackout',
    phrase: '\\b(?:ten|nine|eight|seven|six|five|four|three|two|one|10|9|8|7|6|5|4|3|2|1)\\b(?:[ ,]+\\b(?:nine|eight|seven|six|five|four|three|two|one|zero|9|8|7|6|5|4|3|2|1|0)\\b){2,}' },
  { id: 'chant', name: 'chant (a word said 3+ times in a row)', group: 'sequence', mode: 'regex', color: '#ff69b4', preset: 'pink-wall',
    phrase: '\\b(\\w+(?: \\w+)?)\\b(?: \\1\\b){2,}' },
];

/** The order the tab lists the groups in, and what it calls them. */
export const SET_GROUPS = [['named', 'named triggers'], ['words', 'words'], ['sequence', 'sequences'], ['custom', 'yours']];

/** The rotation a custom set takes its colour from, and the swatch cycles through. */
export const SET_COLORS = ['#ff69b4', '#78ffbe', '#8fd0ff', '#ffd166', '#b080ff', '#ff5c6c',
  '#ffe066', '#40d0c0', '#ffb3d9', '#6b8cff', '#c8a8ff', '#ece8ff'];

const MODES = ['exact', 'contains', 'regex'];

/** One custom set, filled out and made safe. Junk in the file gives null, not a broken row. */
export function normalizeSet(s, i = 0) {
  if (!s || typeof s !== 'object' || !s.phrase) return null;
  return { id: String(s.id || 'c-' + i), name: String(s.name || s.phrase), group: 'custom',
    phrase: String(s.phrase), mode: MODES.includes(s.mode) ? s.mode : 'exact',
    color: /^#[0-9a-f]{6}$/i.test(String(s.color)) ? String(s.color) : SET_COLORS[i % SET_COLORS.length],
    cluster: Number(s.cluster) > 0 ? Number(s.cluster) : 0 };
}

/** `project.triggers`, filled out: what is ticked, the author's own sets, the per-set options. */
export function normalizeTriggers(json) {
  const j = json && typeof json === 'object' ? json : {};
  const seen = new Set();
  const custom = (Array.isArray(j.custom) ? j.custom : [])
    .map((s, i) => normalizeSet(s, i))
    .filter((s) => s && !seen.has(s.id) && (seen.add(s.id), true));
  const opts = {};
  const src = j.opts && typeof j.opts === 'object' ? j.opts : {};
  for (const k of Object.keys(src)) if (src[k] && typeof src[k] === 'object') opts[k] = { ...src[k] };
  const on = [...new Set((Array.isArray(j.on) ? j.on : []).filter((id) => typeof id === 'string' && id))];
  return { version: 1, on, custom, opts };
}
