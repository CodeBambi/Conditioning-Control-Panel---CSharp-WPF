/* ============================================================================
 * race/hostWords.js - the shipped transcript, laid on a desktop host's chart.
 *
 *   wordHostChart(chart, { log, fetch }) -> Promise<chart | null>
 *
 * WHY. The web lane builds its road in race/cloudChart.js: the energy walk AND the
 * whisper transcript from race/words/, one word a bubble. The desktop host charts
 * the audio in C# instead, and its only word pass is Vosk, which is not shipped, so
 * a desktop road came back with the curve and no words at all, even for the eleven
 * levels whose transcripts sit right here next to this file.
 *
 * WHAT. A host chart that carries no words and matches a words/index.json row (by
 * cloudId, then hash) is laid again through the web's own wordedRoad(), with the
 * host's energy curve standing in for the peaks. Same builder, same triggers, same
 * offset + nudge, so a level drives the same on both hosts. Anything else answers
 * null and the host's chart stands as it was: an authored chart, a chart that
 * already has words, a track with no transcript, or any failure on the way.
 * ==========================================================================*/

import { normalizeChart } from './chart.js';
import { wordedRoad } from './cloudChart.js';
import { loadWords } from './words.js';
import { shiftRoad, readNudge } from './wordSync.js';

/**
 * The host's energy curve as the peaks pair list energyFromPeaks reads ([min, max] per
 * slice, amplitude (max - min) / 2), one slice per host bin. It renormalises on the way
 * back in, so a curve that is already 0..1 comes out as itself.
 */
function peaksFromEnergy(energy) {
  const out = new Float32Array(energy.length * 2);
  for (let i = 0; i < energy.length; i++) {
    const a = Math.max(0, Math.min(1, Number(energy[i]) || 0));
    out[i * 2] = -a; out[i * 2 + 1] = a;
  }
  return out;
}

export async function wordHostChart(chart, { log = null, fetch: f = null } = {}) {
  const say = (m) => { try { if (log) log('host words: ' + m); } catch (e) { /* no log */ } };
  try {
    if (!chart || chart.hand === true) return null;
    if (Array.isArray(chart.words) && chart.words.length) return null;
    const src = chart.source || {};
    const hash = String(src.hash || ''), cloudId = String(src.cloudId || '');
    const energy = Array.isArray(chart.energy) ? chart.energy : [];
    const binSec = Number(chart.binSec) > 0 ? Number(chart.binSec) : 0.5;
    const durationSec = Number(src.durationSec) > 0 ? Number(src.durationSec) : energy.length * binSec;
    if (!energy.length || !(durationSec > 0)) return null;

    const words = await loadWords({ cloudId, hash, fetch: f, log });
    if (!words || !words.words || !words.words.length) return null;

    const road = wordedRoad({ peaks: peaksFromEnergy(energy), perSec: 1 / binSec, durationSec, name: src.name || 'track', hash, words });
    const key = words.cloudId || cloudId || hash;
    const sec = (Number(words.offsetSec) || 0) + readNudge(key);
    const out = normalizeChart(shiftRoad(normalizeChart(road), sec, { trackId: key }));
    say(`${road.words.length} words and ${road.analysis.lexicon.length} triggers on ${src.name || key}`);
    return out;
  } catch (err) {
    say((err && err.message) || String(err));
    return null;
  }
}

export default wordHostChart;
