/* ============================================================================
 * ai.js — askAI() interface for "Graded Intake".
 *
 * Transport policy (LOCKED, BUILD_PLAN.md §0): AI goes through the CCP-Server
 * proxy `POST /intake/ai` (Agent H). The key, the entitlement gate, and BOTH
 * input+output moderation live SERVER-SIDE. The page never holds a model key
 * and never talks to OpenRouter directly.
 *
 * Requests are COMPACT (route/depth/band/last-3-answers/tag-tallies) — never
 * the full transcript. contracts.js `AiRequest` is the schema; the server
 * rejects anything larger.
 *
 * Phase 0 ships a deterministic OFFLINE STUB so the whole page is runnable with
 * no network and before Agent H exists. `createAI(null)` -> stub. `createAI({
 * serverBase, authToken })` -> real proxy fetch, with the stub as the fallback
 * on any transport/gate error (a dead endpoint must never wedge the run).
 * ==========================================================================*/

import { AiWant, hash01, NICHES } from './contracts.js';

const AI_PATH = '/intake/ai';
const TIMEOUT_MS = 8000;

/**
 * @param {{serverBase?:string, authToken?:string}|null} config
 * @returns {{ askAI:(req:import('./contracts.js').AiRequest)=>Promise<import('./contracts.js').AiResponse>, mode:string }}
 */
export function createAI(config) {
  const hasServer = !!(config && config.serverBase);
  const stub = createStubAI();

  if (!hasServer) {
    return { askAI: stub.askAI, mode: 'stub' };
  }

  const base = String(config.serverBase).replace(/\/+$/, '');
  const url = base + AI_PATH;

  async function askAI(req) {
    const body = compact(req);
    let ctrl, timer;
    try {
      ctrl = new AbortController();
      timer = setTimeout(() => ctrl.abort(), TIMEOUT_MS);
      const res = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(config.authToken ? { 'Authorization': `Bearer ${config.authToken}` } : {}),
        },
        body: JSON.stringify(body),
        signal: ctrl.signal,
        credentials: 'omit',
      });
      clearTimeout(timer);
      if (!res.ok) {
        // 401/403 = not entitled, 429 = over quota, 5xx = server issue.
        // The run must degrade gracefully to the offline accent, never break.
        return await stub.askAI(req);
      }
      const data = await res.json();
      return normalize(data);
    } catch (_e) {
      if (timer) clearTimeout(timer);
      return await stub.askAI(req);   // transport failure -> stub
    }
  }

  return { askAI, mode: 'proxy' };
}

/** Strip a request down to the compact contract the server accepts. */
function compact(req) {
  const last = Array.isArray(req.lastAnswers) ? req.lastAnswers.slice(-3).map((a) => ({
    promptId: a.promptId, correct: !!a.correct, tags: (a.tags || []).slice(0, 6),
  })) : [];
  return {
    niche: NICHES.includes(req.niche) ? req.niche : NICHES[0],
    want: req.want === AiWant.Synthesis ? AiWant.Synthesis : AiWant.Accent,
    route: req.route || null,
    depth: clamp01(req.depth),
    band: req.band || null,
    lastAnswers: last,
    tagTallies: req.tagTallies || {},
  };
}

/** Coerce a server payload to the AiResponse shape. */
function normalize(data) {
  const out = {};
  if (Array.isArray(data.beats)) out.beats = data.beats;
  if (data.profile && typeof data.profile === 'object') out.profile = data.profile;
  if (typeof data.synthesis === 'string') out.synthesis = data.synthesis;
  if (data.moderated != null) out.moderated = !!data.moderated;
  return out;
}

const clamp01 = (n) => (n < 0 ? 0 : n > 1 ? 1 : (n || 0));

/* ----------------------------------------------------------------------------
 * OFFLINE STUB — deterministic, no network. Good enough to develop render/engine
 * against and to keep a run alive when the proxy is unreachable. It never claims
 * to be moderated (there is no model behind it); it just recolors copy in-voice.
 * -------------------------------------------------------------------------- */
export function createStubAI() {
  const VOICE = {
    bambi: ['good girl', 'so soft now', 'let it get bubbly', 'thinking is hard, dropping is easy'],
    drone: ['compliance is calm', 'the pattern is clear', 'obey and quiet', 'unit accepts input'],
    sissy: ['be honest with yourself', 'you already know', 'prettier when you agree', 'let the pink in'],
  };
  async function askAI(req) {
    const niche = NICHES.includes(req.niche) ? req.niche : NICHES[0];
    const pool = VOICE[niche] || VOICE.bambi;
    if (req.want === AiWant.Synthesis) {
      const arche = req.route && req.route.primaryArchetypeId ? req.route.primaryArchetypeId : niche;
      return {
        synthesis: `You read as ${arche}. ${pool[Math.floor(hash01(arche) * pool.length)]}.`,
        profile: { primary: arche, note: 'offline-stub' },
      };
    }
    // Accent: return one in-voice flavor keyed deterministically to the last answer.
    const seed = (req.lastAnswers && req.lastAnswers.length)
      ? req.lastAnswers[req.lastAnswers.length - 1].promptId
      : String(req.depth);
    const line = pool[Math.floor(hash01(seed + niche) * pool.length)];
    return { beats: [{ flavor: line }] };
  }
  return { askAI };
}
