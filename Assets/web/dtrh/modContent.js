/* ============================================================================
 * modContent.js - the active mod's OWN DTRH content (creator mods).
 *
 * The host discovers an installed mod's resources/dtrh folder, maps it as the
 * ccp.mod virtual host, and describes it in the init message's `modContent`
 * field (null = the mod ships nothing; every consumer falls back to today's
 * shipped assets). boot.js stores it here before the engine starts, so every
 * consumer (driftChain voice pools, scene drone bed, vnPortrait tint/name/
 * frames) can read it at construction time without threading it through
 * constructors.
 *
 * Shape (see C# DtrhModContent.BuildInitPayload):
 *   { id, companion, tint: {a:'r,g,b', b:'r,g,b'}|null, droneUrl|null,
 *     drift: { mode: 'replace'|'mix',
 *              pools: { drift|sensory|descent|babble|resolve: [{file,url}] } },
 *     portrait: { defaultUrl, emotes: {name:url} }|null }
 * ==========================================================================*/

let mc = null;

export function setModContent(m) { mc = m || null; }

export const modContentId = () => (mc && mc.id) || null;
export const modCompanion = () => (mc && mc.companion) || null;
export const modTint      = () => (mc && mc.tint) || null;
export const modDroneUrl  = () => (mc && mc.droneUrl) || null;
export const modDrift     = () => (mc && mc.drift) || null;
export const modPortrait  = () => (mc && mc.portrait) || null;
