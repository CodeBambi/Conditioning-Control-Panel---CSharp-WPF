/** The grey world's tempo, soundtrack and generated bed alike (owner, by ear: .72 was not slow enough). */
export const GREY_RATE = .66;
const TRACKS = ['velvet-roulette.mp3','midnight-jackpot-alt.mp3','midnight-jackpot.mp3','coin-arpeggio.mp3','neon-jackpot.mp3'];

/** Each shuffled round plays every track once, with three other tracks before a repeat. */
export function createRotation(tracks = TRACKS, random = Math.random) {
  let queue = [], history = [];
  const gap = Math.min(3, tracks.length - 1);
  function round(left, tail) {
    if (!left.length) return [];
    const choices = left.filter(x => !tail.includes(x));
    for (let i = choices.length - 1; i > 0; i--) { const j = Math.floor(random() * (i + 1)); [choices[i],choices[j]] = [choices[j],choices[i]]; }
    for (const x of choices) {
      const rest = round(left.filter(y => y !== x), [...tail,x].slice(-gap));
      if (rest) return [x,...rest];
    }
    return null;
  }
  return { next() {
    if (!tracks.length) return null;
    if (!queue.length) queue = round([...new Set(tracks)], history) || [];
    const next = queue.shift(); history = [...history,next].slice(-gap); return next;
  } };
}

export function createMusic({ volume = .15, master = .8, AudioCtor = globalThis.Audio,
  host = globalThis.window, page = globalThis.document, storage = null } = {}) {
  const rotation = createRotation();
  let audio, context = null, gain = null, drive = null, scene = 'normal', armed = false, disposed = false;
  let failures = 0, retry = 0, pageAway = false, needsNext = false, contextUnavailable = false, hostSuspended = false;
  const clamp = value => Math.max(0, Math.min(1, Number(value) || 0));
  let level = clamp(volume); master = clamp(master);
  const blocked = () => disposed || scene === 'freeze' || hostSuspended || pageAway || page.hidden || !level;
  const silenceContext = operation => { try { context?.[operation]()?.catch?.(() => {}); } catch {} };
  const cancelRetry = () => { clearTimeout(retry); retry = 0; };
  const apply = () => {
    if (gain) {
      // The options host may already own master gain at this context's destination.
      audio.volume = 1;
      gain.gain.value = level * (context.__brMasterGain ? 1 : master);
    } else audio.volume = level * master;
  };
  function applyScene() {
    audio.playbackRate=scene==='grey'?GREY_RATE:1;
    audio.preservesPitch=scene!=='grey';
    if(drive) {
      if(scene==='grey') {const curve=new Float32Array(256);for(let i=0;i<256;i++){const x=i/127.5-1;curve[i]=Math.tanh(x*1.4)/Math.tanh(1.4);}drive.curve=curve;}
      else drive.curve=null;
    }
  }
  function attachAudio() {
    audio = new AudioCtor(); audio.preload = 'none'; audio.dataset.brMusic = '1'; applyScene();
    audio.addEventListener('ended', ended); audio.addEventListener('error', error); audio.addEventListener('playing', playing);
  }
  function detachAudio() {
    audio.removeEventListener('ended', ended); audio.removeEventListener('error', error); audio.removeEventListener('playing', playing);
    audio.pause(); audio.removeAttribute('src'); audio.load();
  }
  function connect() {
    if (context || disposed || contextUnavailable) return;
    let candidate = null, source = null;
    try {
      const Ctor = host.AudioContext || host.webkitAudioContext;
      if (!Ctor) return;
      candidate = new Ctor();
      const node = candidate.createGain(); node.connect(candidate.destination);
      source = candidate.createMediaElementSource(audio);
      if(typeof candidate.createWaveShaper==='function') {drive=candidate.createWaveShaper();source.connect(drive);drive.connect(node);}
      else source.connect(node);
      context = candidate; gain = node; applyScene(); apply();
    } catch {
      try { candidate?.close()?.catch?.(() => {}); } catch {}
      // Once captured by WebAudio an element cannot return to native output.
      if (source) { const url = audio.getAttribute('src'); detachAudio(); attachAudio(); if (url) audio.src = url; }
      context = gain = null; contextUnavailable = true; apply();
    }
  }
  async function play() {
    if (!armed || blocked() || failures >= TRACKS.length) return;
    if (needsNext || !audio.getAttribute('src')) { needsNext = false; audio.src = new URL('../../music/' + rotation.next(), import.meta.url).href; }
    try {
      // Both calls happen in the input event, without awaiting away its activation.
      const resumed = context?.resume();
      const started = audio.play();
      await Promise.allSettled([resumed, started]);
      if (blocked()) { audio.pause(); silenceContext('suspend'); }
    } catch { /* A later gesture can retry a browser autoplay refusal. */ }
  }
  function arm() { if (disposed) return; armed = true; connect(); play(); }
  function ended() { needsNext = true; play(); }
  function error() {
    if (disposed) return;
    needsNext = true; cancelRetry();
    if (++failures >= TRACKS.length || blocked()) return;
    retry = setTimeout(() => { retry = 0; play(); }, 500);
  }
  function playing() { failures = 0; }
  function pause() { cancelRetry(); audio.pause(); silenceContext('suspend'); }
  function visibility() { if (page.hidden) pause(); else play(); }
  function pagehide(event) { if (event.persisted) { pageAway = true; pause(); } else api.dispose(); }
  function pageshow(event) { if (event.persisted && !disposed) { pageAway = false; play(); } }
  attachAudio();
  host.addEventListener('pointerdown', arm, {passive:true}); host.addEventListener('keydown', arm);
  page.addEventListener('visibilitychange', visibility);
  const api = {
    setScene(next) {
      if(disposed)return;
      scene=['freeze','grey'].includes(next)?next:'normal';applyScene();
      if(scene==='freeze')pause();else play();
    },
    get volume() { return level; },
    get disposed() { return disposed; },
    suspend(on) { if (disposed) return; hostSuspended = !!on; if (hostSuspended) pause(); else play(); },
    setVolume(value, masterVolume = master) {
      if (disposed) return;
      level = clamp(value); master = clamp(masterVolume); apply();
      try { storage?.setItem('br.music.v1', String(level)); } catch {}
      if (!level) pause(); else { failures = 0; play(); }
    },
    dispose() {
      if (disposed) return;
      disposed = true; cancelRetry(); detachAudio(); silenceContext('close');
      host.removeEventListener('pointerdown', arm); host.removeEventListener('keydown', arm);
      page.removeEventListener('visibilitychange', visibility);
      host.removeEventListener('pagehide', pagehide); host.removeEventListener('pageshow', pageshow);
    },
  };
  host.addEventListener('pagehide', pagehide); host.addEventListener('pageshow', pageshow);
  apply(); return api;
}

let singleton = null;
/** One soundtrack per room document, shared by desktop chrome and the phone shell. */
export function getMusic(options = {}) {
  if (singleton && !singleton.disposed) return singleton;
  let storage = options.storage || null, volume = options.volume ?? .15;
  try {
    storage ||= globalThis.localStorage;
    const saved = storage?.getItem('br.music.v1');
    if (saved != null && saved !== '' && Number.isFinite(Number(saved))) volume = Math.max(0, Math.min(1, Number(saved)));
  } catch {}
  singleton = createMusic({ ...options, volume, storage });
  return singleton;
}

/** Scene effects never create another soundtrack or change saved volume. */
export function setMusicScene(scene) { singleton?.setScene(scene); }

/** Read the current soundtrack without creating or starting a player. */
export function currentMusic() { return singleton && !singleton.disposed ? singleton : null; }
