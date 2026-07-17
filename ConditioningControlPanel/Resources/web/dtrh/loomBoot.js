/* ============================================================================
 * loomBoot.js - boot for the standalone Loom window (loom.html, hosted by
 * LoomHostService). Mounts the same createLoomStudio the Warren pane uses and
 * wires the same bridge protocol: loom-save/loom-delete/sfx out,
 * loom-list/loom-result in. Nothing else - no game, no three.js.
 * ==========================================================================*/

import * as bridge from './bridge.js';
import { createLoomStudio } from './game/loomStudio.js';

const sfx = (name, scale = 0.45) => bridge.send({ type: 'sfx', name, scale });

const studio = createLoomStudio({ bridge, sfx });

bridge.on('loom-list', (m) => studio.onList(m.spirals || []));
bridge.on('loom-result', (m) => studio.onResult(m));

studio.render(document.getElementById('loom'));

// Host flushes its queued loom-list once we announce.
bridge.announceReady();
