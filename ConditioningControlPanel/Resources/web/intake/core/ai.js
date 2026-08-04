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

  // Once the server says this run is not entitled - or is out of allowance - STOP ASKING.
  // The engine fires one accent per graded beat and never awaits it, so without this latch a
  // signed-out run pushes ~81 unauthenticated requests at the proxy (the Authorization header
  // above is conditional, so no token means the request still goes, just without it). Ten auth
  // failures inside a minute trips the server's IP blocker, which takes EVERY endpoint away
  // from that address for 30 minutes - login included, so the user cannot sign in to recover.
  // The server now exempts this path from that blocker; this is the same fix at the end that
  // actually knows the asking is pointless.
  let halted = false;

  // One accent in flight at a time, and SKIP rather than queue when one is already out. A queue
  // turns a network blip into a retry storm; a skip costs one accent, which the engine already
  // tolerates - accentFlavor colours a LATER beat and is never awaited or required.
  let inFlight = false;

  async function askAI(req) {
    if (halted || inFlight) return await stub.askAI(req);

    const body = compact(req);
    inFlight = true;
    try {
      // One retry, and only for 429 - the server tells us exactly how long to wait. Jittered so
      // a household behind one address does not re-collide in lockstep on the way back in.
      for (let attempt = 0; ; attempt++) {
        const res = await postOnce(body);

        if (res.ok) return normalize(await res.json());

        // Not entitled (401/403) or allowance spent (402): asking again this run cannot help.
        if (res.status === 401 || res.status === 403 || res.status === 402) {
          halted = true;
          return await stub.askAI(req);
        }

        if (res.status === 429 && attempt === 0) {
          const wait = await retryAfterMs(res);
          if (wait > 0 && wait <= TIMEOUT_MS) {
            await sleep(wait + Math.floor(Math.random() * 250));
            continue;
          }
        }

        // 5xx, a second 429, anything else: degrade to the offline accent, never break the run.
        return await stub.askAI(req);
      }
    } catch (_e) {
      return await stub.askAI(req);   // transport failure -> stub
    } finally {
      inFlight = false;
    }
  }

  async function postOnce(body) {
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), TIMEOUT_MS);
    try {
      return await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(config.authToken ? { 'Authorization': `Bearer ${config.authToken}` } : {}),
        },
        body: JSON.stringify(body),
        signal: ctrl.signal,
        credentials: 'omit',
      });
    } finally {
      clearTimeout(timer);
    }
  }

  return { askAI, mode: 'proxy' };
}

/** Sleep, promisified. Only ever used for a 429 back-off the server asked for. */
function sleep(ms) { return new Promise((r) => setTimeout(r, ms)); }

/**
 * How long the server asked us to wait, in ms. Prefers the JSON body's `retry_after`
 * (seconds - what this proxy sends on every 429) and falls back to the standard
 * Retry-After header. Returns 0 when neither is present or parseable, which the caller
 * treats as "do not retry" rather than guessing an interval.
 */
async function retryAfterMs(res) {
  try {
    const data = await res.clone().json();
    const s = Number(data && data.retry_after);
    if (Number.isFinite(s) && s > 0) return Math.round(s * 1000);
  } catch (_e) { /* not JSON, fall through to the header */ }
  const hdr = Number(res.headers && res.headers.get && res.headers.get('Retry-After'));
  return Number.isFinite(hdr) && hdr > 0 ? Math.round(hdr * 1000) : 0;
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
    // circe = the Locked mod. Its bank is authored in the custody/case-notes voice
    // ("Ownership Mapping", "Restraint suits you. Noted."), so it must not inherit
    // bambi's giggle here - that mismatch is jarring enough to read as a bug.
    circe: ['noted', 'that was expected of you', 'restraint suits you', 'the record is clear'],
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
