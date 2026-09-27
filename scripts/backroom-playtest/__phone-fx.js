
(() => {
  const performanceMode = () => window.__backroomQuality?.performance === true;
  const effectTimers = new Set();
  let effectEpoch = 0;
  function later(callback, ms) {
    const epoch = effectEpoch;
    const timer = setTimeout(() => {
      effectTimers.delete(timer);
      if (epoch === effectEpoch) callback();
    }, Math.max(0, ms));
    effectTimers.add(timer);
    return timer;
  }
  function cancelLater(timer) { clearTimeout(timer); effectTimers.delete(timer); }
  function removeEffect(node) {
    cancelLater(node.__drop);
    try { node.getAnimations().forEach(animation => animation.cancel()); } catch {}
    node.remove();
    if (node.tagName === 'IMG') node.removeAttribute('src');
  }
  const WORDS = ['DROP', 'RELAX', 'LET GO', 'SINK', 'DEEPER', 'EMPTY', 'OBEY', 'SOFTER', 'BLANK'];
  const SPIRAL = (preset) => `/backroom/shared/hypno/spirals/${preset === 'wake' ? 'wake' : 'screen'}.gif`;
  const FALLBACK = (n) => `/backroom/stations/slot/fallback/gif${(Number.isFinite(n) ? n : 0) % 4}.webp`;

  const ENDPOINT = 'https://api.scrolller.com/admin';
  const DEFAULT_SUBS = ['EroticHypnosis', 'bimbofication', 'Dronification', 'sissyhypno'];
  const QUERY = `query SubredditQuery($url: String!, $iterator: String, $sortBy: GallerySortBy, $filter: GalleryFilter, $limit: Int!) {
  getSubreddit(data: {url: $url, iterator: $iterator, filter: $filter, limit: $limit, sortBy: $sortBy}) {
    id children { items { id mediaSources { url width height } } } } }`;
  const MAX_W = 1280;
  let config; try { config = JSON.parse(localStorage.getItem('br.media.v1') || 'null'); } catch {}
  config = { mode: 'scrolller', sources: DEFAULT_SUBS, ...config };
  if (!['scrolller','bundled','local'].includes(config.mode)) config.mode = 'scrolller';
  const media = { clips: [], stills: [], on: config.mode === 'scrolller', tried: false, local: [] };
  let generation = 0, fetchController;
  const localUrls = new Set();
  let localBytes = 0;
  const changed = () => {
    try { sessionStorage.setItem('br.media.pool.v1',JSON.stringify({config,clips:media.clips,stills:media.stills})); } catch {}
    window.dispatchEvent(new Event('br-media-changed'));
  };
  async function localFiles(value) {
    const db = await new Promise((resolve,reject)=>{
      const req=indexedDB.open('br.local-media',1);
      req.onupgradeneeded=()=>req.result.createObjectStore('files');
      req.onsuccess=()=>resolve(req.result); req.onerror=()=>reject(req.error);
    });
    try { return await new Promise((resolve,reject)=>{
      const tx=db.transaction('files',value ? 'readwrite':'readonly');
      const req=value ? tx.objectStore('files').put(value,'selected') : tx.objectStore('files').get('selected');
      tx.oncomplete=()=>resolve(req.result); tx.onerror=()=>reject(tx.error); tx.onabort=()=>reject(tx.error);
    }); } finally { db.close(); }
  }
  function adoptFiles(files) {
    media.local=files.map(f=>{localBytes+=f.size;const url=URL.createObjectURL(f);localUrls.add(url);return url;});
  }
  const storeConfig = () => { try { localStorage.setItem('br.media.v1', JSON.stringify(config)); } catch {} };
  const cleanSources = value => [...new Set(String(value).split(/[\s,;]+/).map(s => s.replace(/^https?:\/\/(?:www\.)?(?:scrolller\.com|reddit\.com)\//i,'').replace(/^r\//i,'').replace(/\/$/, '')).filter(s => /^[a-zA-Z0-9_]{2,40}$/.test(s)))].slice(0,8);
  config.sources = cleanSources(Array.isArray(config.sources) ? config.sources.join(',') : config.sources);
  config.disabledSources = cleanSources((config.disabledSources || []).join(',')).filter(n => config.sources.includes(n));

  const pickBest = (sources, re) => {
    const ok = (sources || []).filter((m) => typeof m.url === 'string' && re.test(m.url) && Number(m.width) > 0);
    if (!ok.length) return null;
    const fit = ok.filter((m) => m.width <= MAX_W);
    const pool = fit.length ? fit : ok;
    pool.sort((a, b) => (fit.length ? b.width - a.width : a.width - b.width));
    return pool[0].url;
  };

  async function pull(sub, filter, re, limit, signal) {
    const res = await fetch(ENDPOINT, { method: 'POST', headers: { 'content-type': 'application/json' },
      signal, credentials: 'omit',                                  // a third party never gets a cookie
      body: JSON.stringify({ query: QUERY, authorization: null,
        variables: { url: `/r/${sub}`, iterator: null, sortBy: 'RANDOM', filter, limit } }) });
    if (!res.ok) return [];
    const items = (await res.json())?.data?.getSubreddit?.children?.items;
    return (Array.isArray(items) ? items : []).map((i) => pickBest(i && i.mediaSources, re)).filter(Boolean);
  }

  const WARM_BUDGET_MS = 3500;
  async function warmMedia() {
    if (media.tried || !media.on) return; media.tried = true;
    const epoch = generation, controller = fetchController = new AbortController();
    const timer = setTimeout(() => controller.abort(), WARM_BUDGET_MS);
    try {
      const jobs = config.sources.filter(sub => !config.disabledSources.includes(sub)).flatMap(sub => [
        pull(sub, 'GIF', /\.(?:webm|mp4)(?:\?|$)/i, 15, controller.signal).then(u => { if(epoch === generation) media.clips.push(...u); }),
        pull(sub, 'PICTURE', /\.(?:webp|jpe?g|png)(?:\?|$)/i, 12, controller.signal).then(u => { if(epoch === generation) media.stills.push(...u); }),
      ]);
      await Promise.allSettled(jobs);
    } finally { clearTimeout(timer); }
    if (epoch !== generation) return;
    media.clips = [...new Set(media.clips)]; media.stills = [...new Set(media.stills)];
    window.__fxMediaCount = { clips: media.clips.length, stills: media.stills.length };
    warmCanvas(); changed();
  }

  const HOP = (u, edge) => '/api/clip?u=' + encodeURIComponent(u) + (edge === 640 ? '&e=640' : '');

  const proxied = (u) => '/api/m?u=' + encodeURIComponent(u);
  const warm = (u, edge) => { try { fetch(HOP(u, edge), { method: 'GET', cache: 'force-cache' }).catch(() => {}); } catch (e) {  } };
  const OVERLAY_EDGE = 640;   // a fullscreen or half-screen overlay is DOM: 384 upscaled reads soft
  const RAIN_EDGE = 384;      // a raindrop is 34 vmin and moving; 384 is past enough

  const BURST_WINDOW = 8;        // distinct pictures inside one burst
  const MEDIA_REACH = 24;        // distinct pictures a session ever transcodes at the overlay rung
  const MEDIA_REACH_MAX = 48;    // a ceiling, never a free dial
  const RAIN_HEAD = 6;           // gif-rain stays on the 384 head: 19 raindrops at 640 is a phone's whole budget
  let burstCursor = 0;
  const reachOf = () => Math.min(MEDIA_REACH, MEDIA_REACH_MAX, media.clips.length);

  function warmAhead() {
    const reach = reachOf();
    if (!reach) return;
    for (let i = 0; i < BURST_WINDOW; i++) {
      const u = media.clips[(burstCursor + BURST_WINDOW + i) % reach];
      if (u) warm(u, performanceMode() ? RAIN_EDGE : OVERLAY_EDGE);
    }
  }

  function nextWindow() {
    const reach = reachOf();
    if (reach > BURST_WINDOW) burstCursor = (burstCursor + BURST_WINDOW) % reach;
    warmAhead();
    return burstCursor;
  }

  const srcFor = (key, want) => {
    const n = Number(String(key == null ? '' : key).replace(/^g(?:if)?/, '')) || 0;
    const edge = want === 'rain' || performanceMode() ? RAIN_EDGE : OVERLAY_EDGE;
    if (config.mode === 'local' && media.local.length) return media.local[n % media.local.length];
    if (media.on && media.clips.length) {
      if (want === 'rain') return HOP(media.clips[n % Math.min(RAIN_HEAD, media.clips.length)], edge);
      return HOP(media.clips[(burstCursor + n) % reachOf()], edge);
    }
    if (media.on && media.stills.length) return proxied(media.stills[n % media.stills.length]);
    return FALLBACK(n);
  };

  function mediaNode(cls, key, want) {
    if (performanceMode()) {
      const pictures = [...mount().querySelectorAll('img')];
      // Keep fullscreen beats; replace the oldest peripheral picture first.
      while (pictures.length >= 4) {
        const index = pictures.findIndex(node => !node.classList.contains('fxfull'));
        removeEffect(pictures.splice(index < 0 ? 0 : index, 1)[0]);
      }
    }
    const n = add(cls, 'img');
    n.decoding = 'async';
    n.src = srcFor(key, want);
    // A transcode that will not come back (a dead clip, a cold start past the
    // effect's life) leaves a hole; the bundled loop is better than a hole.
    n.addEventListener('error', () => {
      if (!n.isConnected) return;
      const used = new Set([...document.querySelectorAll('#__fx .fxflash')].filter(e => e !== n).map(e => e.src));
      const fallback = Array.from({length:4}, (_, i) => FALLBACK(i)).find(url => !used.has(new URL(url, location.href).href));
      if (cls === 'fxflash' && !fallback) { n.remove(); return; }
      n.src = fallback || FALLBACK(0);
    }, { once: true });
    if (want === 'rain') armDismiss(n);   // .fxflash and .fxrain only
    return n;
  }
  const gifUrl = srcFor;

  let root = null, tunnelEl = null, hazeEl = null, held = new Map(), tunnelLevel = 0;

  const FLASH_MS = 3800;
  const SPIRAL_STRETCH = 1.8;

  const intensity = () => (window.__fxIntensity || 'normal');
  const kOpacity = () => (intensity() === 'calm' ? 0.5 : 1);          // Calm halves every opacity
  const kDuration = () => (intensity() === 'full' ? 1.3 : 1);         // Full stretches every duration

  function mount() {
    if (root && root.isConnected) return root;
    root = document.createElement('div');
    root.id = '__fx';
    root.style.cssText = 'position:fixed;inset:0;z-index:2147483000;pointer-events:none;overflow:hidden';
    const style = document.createElement('style');
    style.textContent = `
      #__fx > * { position:absolute; pointer-events:none; }
      #__fx .fxword { inset:0; display:grid; place-items:center; font:900 13vh/1.05 "Bahnschrift Condensed","Arial Narrow",Impact,sans-serif;
        color:#fff; text-shadow:0 0 24px rgba(0,0,0,.6); letter-spacing:.02em; text-align:center; padding:0 5vw; }
      #__fx .fxfull { inset:0; width:100%; height:100%; object-fit:cover; }
      #__fx .fxwash { inset:0; }
      #__fx .fxrain { width:34vmin; height:34vmin; object-fit:cover; border-radius:10px; box-shadow:0 6px 26px rgba(0,0,0,.5); }

      #__fx .fxflash { width:58vmin; height:43vmin; left:50%; top:50%; transform:translate(-50%,-50%);
        object-fit:cover; border-radius:12px; box-shadow:0 10px 48px rgba(0,0,0,.55), 0 0 0 2px rgba(255,214,240,.16); }

      #__fx .fxtap { pointer-events:auto; cursor:pointer; touch-action:manipulation; }
      #__fx .fxglitch { inset:0; background:linear-gradient(90deg,#9b6bff,#5fffd0,#ff5fa2); mix-blend-mode:screen; }
      #__fx .fxdim { inset:0; background:#060309; }
      @keyframes fxrainfall { from { transform:translateY(-50vh) rotate(0deg); } to { transform:translateY(130vh) rotate(20deg); } }
    `;
    document.documentElement.append(style);
    document.body.append(root);
    return root;
  }

  const add = (cls, tag = 'div') => { const n = document.createElement(tag); n.className = cls; mount().append(n); return n; };

  const drop = (n, ms) => (n.__drop = later(() => removeEffect(n), ms));
  const anim = (n, frames, ms, easing = 'ease') => {
    try { return n.animate(frames, { duration: Math.max(1, ms), easing, fill: 'forwards' }); } catch (e) { return null; }
  };

  const DISMISS_FLOOR = 0.15;
  function armDismiss(n) {
    n.classList.add('fxtap');
    n.addEventListener('pointerdown', (e) => {
      let live = 1;
      try { live = Number(getComputedStyle(n).opacity); } catch (err) {  }
      if (!(live >= DISMISS_FLOOR)) return;      // mid-fade: let the tap through to whatever is under it
      e.preventDefault(); e.stopPropagation();
      n.classList.remove('fxtap');
      if (n.__drop) cancelLater(n.__drop);      // the life timer must not race the exit
      (window.__fxShatter ? shatter : dissolve)(n);
    });
  }
  const stopAnims = (n) => { try { for (const a of n.getAnimations()) a.cancel(); } catch (e) {  } };

  function dissolve(n) {
    const painted=getComputedStyle(n),base=painted.transform==='none'?'':painted.transform;
    const from=Number(painted.opacity)||.65;
    stopAnims(n);
    anim(n, [
      { opacity: from, filter: 'blur(0px) saturate(1)', transform: base + ' scale(1)' },
      { opacity: from * 0.55, filter: 'blur(3px) saturate(1.35)', transform: base + ' scale(1.05)', offset: 0.35 },
      { opacity: 0, filter: 'blur(16px) saturate(1.7)', transform: base + ' scale(1.16)' },
    ], 480, 'cubic-bezier(.2,.7,.3,1)');
    drop(n, 500);
  }

  function shatter(n) {
    if (performanceMode()) { dissolve(n); return; }
    stopAnims(n);
    const base = n.style.transform || '', SHARDS = 6;
    const pt = (deg) => (50 + 75 * Math.cos((deg * Math.PI) / 180)) + '% ' + (50 + 75 * Math.sin((deg * Math.PI) / 180)) + '%';
    for (let i = 0; i < SHARDS; i++) {
      const piece = n.cloneNode(false);          // same class, same src, already decoded
      piece.classList.remove('fxtap');
      const a0 = (i / SHARDS) * 360, a1 = ((i + 1) / SHARDS) * 360, mid = (((a0 + a1) / 2) * Math.PI) / 180;
      piece.style.clipPath = 'polygon(50% 50%, ' + pt(a0) + ', ' + pt((a0 + a1) / 2) + ', ' + pt(a1) + ')';
      mount().append(piece);
      anim(piece, [
        { transform: base + ' translate(0,0) rotate(0deg) scale(1)', opacity: 1 },
        { transform: base + ' translate(' + (Math.cos(mid) * 26).toFixed(1) + 'vmin, ' + (Math.sin(mid) * 26 + 10).toFixed(1) + 'vmin)'
          + ' rotate(' + ((i % 2 ? 1 : -1) * (18 + i * 7)) + 'deg) scale(.82)', opacity: 0 },
      ], 420, 'cubic-bezier(.22,.9,.3,1)');
      drop(piece, 440);
    }
    removeEffect(n);                          // the original goes at once; the wedges carry the exit
  }
  if (new URLSearchParams(location.search).get('fx') === 'shatter') window.__fxShatter = true;

  // Live Loom playback shares the room's existing renderer and avoids GIF decode stalls.
  const loomReady = import('/backroom/shared/hypno/loom.js');
  const spiralPlayers = new Set(); let spiralCursor=0;
  function spiralFull(ms, alpha, preset, token) {
    if (performanceMode()) for (const stop of [...spiralPlayers]) stop();
    const d=kDuration(),hold=Math.max(0,ms*d*SPIRAL_STRETCH),a=alpha*kOpacity();
    const canvas=add('fxfull','canvas');canvas.style.opacity='0';
    // Match the Firefox playfield path before Loom acquires this context.
    canvas.getContext('2d',{willReadFrequently:/Firefox\//i.test(navigator.userAgent)});
    let kit=null,raf=0,last=-Infinity,closed=false,timer=0;
    const stop=()=>{if(closed)return;closed=true;cancelLater(timer);cancelAnimationFrame(raf);kit?.dispose();canvas.remove();spiralPlayers.delete(stop);};
    const end=()=>{if(closed)return;anim(canvas,[{opacity:a},{opacity:0}],500);timer=later(()=>{stop();canvas.remove();},520);};
    spiralPlayers.add(stop);
    if(token)held.set(token,end);
    loomReady.then(({createLoomKit})=>{
      if(closed||!canvas.isConnected)return;
      kit=createLoomKit();
      const name=preset==='wake'?'wake':['screen','candy','pinwheel','mint','ribbon','star'][spiralCursor++%6];
      let frames=0,shown=false;
      const draw=now=>{
        if(closed||!canvas.isConnected){stop();return;}
        if(now-last>=1000/(performanceMode()?20:30)){
          const state=window.__backroom?.state;
          const still=state?.userStill || state?.reduced || state?.motion==='off' || state?.motion==='still' || state?.intensity==='calm' || matchMedia('(prefers-reduced-motion: reduce)').matches;
          kit.setStill(!!still);
          const edge=performanceMode()?384:512,ratio=innerWidth/innerHeight,w=ratio>=1?edge:Math.round(edge*ratio),h=ratio>=1?Math.round(edge/ratio):edge;
          if(canvas.width!==w||canvas.height!==h){canvas.width=w;canvas.height=h;}
          const painted=kit.paint(canvas,name,{now});last=now;
          if(painted){
            canvas.dataset.frames=String(++frames);
            if(!shown){shown=true;cancelLater(timer);timer=0;anim(canvas,[{opacity:0},{opacity:a}],1250,'ease-in-out');if(!token){cancelLater(timer);timer=later(end,1250+hold);}}
          }
        }
        raf=requestAnimationFrame(draw);
      };
      timer=later(stop,5000);
      draw(performance.now());
    }).catch(()=>{stop();canvas.remove();});
  }

  const previewMotion = import('/backroom/shared/hypno/flash-preview.js').catch(() => null);
  const interactionReady = import('/backroom/shared/hypno/flash-interaction.js');
  const flashMotionAllowed = () => { const s=window.__backroom?.state || {}; return !(s.userStill || s.reduced || s.motion==='off' || s.motion==='still' || s.intensity==='calm' || matchMedia('(prefers-reduced-motion: reduce)').matches); };
  function armShowcase(img) { interactionReady.then(m=>{if(img.isConnected)m.armFlashInteraction(img,{motion:flashMotionAllowed});}).catch(()=>armDismiss(img)); }
  let flashTurn = 0;
  // Keep the central action clear, with flashes inset from the outer edges.
  function flashSpot(i) {
    const W = innerWidth, H = innerHeight, portrait = H > W;
    const w = portrait ? Math.min(W * .29, H * .15) * 1.2 : W * .18 * 1.2;
    const h = portrait ? H * .16 * 1.2 : Math.min(H * .29 * 1.2, w * .9);
    const edge = i % 2, along = [.24, .74, .49][Math.floor(i / 2) % 3];
    return portrait ? { w, h, x: W * along, y: edge ? H * .77 : H * .23 }
      : { w, h, x: edge ? W * .79 : W * .21, y: H * along };
  }
  function flashBurst(n, opacity, gapMs, keys) {
    const d = kDuration(), a = opacity * kOpacity(), life = FLASH_MS * d;
    // A roulette launch carries ONE pocket key. It seeds the burst, not every picture.
    const candidates = [...keys, ...Array.from({length: Math.max(8, reachOf())}, (_, i) => 'g' + i)];
    const seen = new Set(), picks = [];
    for (const key of candidates) {
      const url = srcFor(key, 'quick');
      if (!seen.has(url)) { seen.add(url); picks.push({key,url}); }
      if (picks.length >= n) break;
    }
    const turn = flashTurn++, epoch = effectEpoch;
    picks.forEach(({key,url}, i) => later(async () => {
      const preview = await previewMotion;
      if (epoch !== effectEpoch || document.hidden) return;
      const img = mediaNode('fxflash', key, 'quick'); img.src = url;
      const spot = flashSpot(i + turn * 2);
      Object.assign(img.style, { width:spot.w+'px', height:spot.h+'px', left:spot.x+'px', top:spot.y+'px',
        transform:'translate(-50%,-50%)', pointerEvents:'none' });
      const state = window.__backroom?.state || {};
      const motion = !(state.userStill || state.reduced || state.motion === 'off' || state.motion === 'still' || state.intensity === 'calm' || matchMedia('(prefers-reduced-motion: reduce)').matches);
      const frames = preview?.flashPreviewFrames({ width: spot.w, height: spot.h, portrait: innerHeight > innerWidth, opacity: a, motion, variant: Math.floor(Math.random()*6) })
        || [{ opacity: 0 }, { opacity: a, offset: .07 }, { opacity: a, offset: .86 }, { opacity: 0 }];
      anim(img, frames, life, 'ease-in-out');
      drop(img, life + 40);
      armShowcase(img);
    }, i * gapMs * d));
  }

  function gifRain(n, ms, opacity, keys) {
    const d = kDuration(), a = opacity * kOpacity(), span = ms * d;
    for (let i = 0; i < n; i++) later(() => {
      const img = mediaNode('fxrain', keys[i % Math.max(1, keys.length)], 'rain');
      // A 13-step stride across the width: neighbours in time are far apart in space.
      img.style.left = (2 + ((i * 29) % 84)) + 'vw';
      img.style.top = '0';
      img.style.opacity = String(a);
      // Slower than it was: a drop that crosses the screen in 1.6 s is a flicker, not rain.
      img.style.animation = `fxrainfall ${(2.6 + (i % 5) * 0.3) * d}s linear forwards`;
      drop(img, 4400 * d);
    }, (i / Math.max(1, n)) * span);
  }

  function glitch(n) {
    const d = kDuration(), a = 0.35 * kOpacity();
    for (let i = 0; i < n; i++) later(() => {
      const g = add('fxglitch');
      anim(g, [{ opacity: 0 }, { opacity: a, offset: 0.3 }, { opacity: 0 }], 600 * d);
      drop(g, 620 * d);
    }, i * 600 * d);
  }

  function words(n, gapMs, pool) {
    const d = kDuration(), a = kOpacity();
    for (let i = 0; i < n; i++) later(() => {
      const w = add('fxword'); w.textContent = pool[i % pool.length];
      anim(w, [{ opacity: 0 }, { opacity: a, offset: 80 / 830 }, { opacity: a, offset: 480 / 830 }, { opacity: 0 }], 830 * d);
      drop(w, 850 * d);
    }, i * gapMs * d);
  }

  function gifFull(ms, opacity, keys) {
    const d = kDuration(), a = opacity * 0.7 * kOpacity();   // softer (owner, 2026-09-19: fullscreen pictures lower and faded in and out)
    const img = mediaNode('fxfull', keys[0], 'full');
    anim(img, [{ opacity: 0 }, { opacity: a, offset: 0.25 }, { opacity: a, offset: 0.7 }, { opacity: 0 }], ms * d);
    drop(img, ms * d + 40);
  }

  // Match CCP's blur plus flowing Perlin displacement on the live game surface.
  let stopMelt = () => {};
  function melt(ms) {
    stopMelt();
    const stage=document.querySelector('#br-stage'); if(!stage)return;
    const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');
    svg.setAttribute('width','0');svg.setAttribute('height','0');
    svg.innerHTML='<defs><filter id="br-melting-glass" x="-10%" y="-10%" width="120%" height="120%" color-interpolation-filters="sRGB"><feTurbulence type="fractalNoise" baseFrequency=".008 .016" numOctaves="2" seed="7" result="flow"/><feDisplacementMap in="SourceGraphic" in2="flow" scale="0" xChannelSelector="R" yChannelSelector="G"/><feGaussianBlur stdDeviation="0"/></filter></defs>';
    document.body.append(svg);
    const noise=svg.querySelector('feTurbulence'),warp=svg.querySelector('feDisplacementMap'),blur=svg.querySelector('feGaussianBlur');
    const previous=stage.style.filter,start=performance.now(),duration=ms*kDuration();let raf=0,closed=false,last=-Infinity;
    stage.style.filter='url(#br-melting-glass)';
    stopMelt=()=>{if(closed)return;closed=true;cancelAnimationFrame(raf);stage.style.filter=previous;svg.remove();};
    function tick(now){
      const elapsed=now-start,amount=Math.min(1,elapsed/duration)*Math.max(0,1-Math.max(0,elapsed-duration)/650)*kOpacity();
      if(elapsed>=duration+650){stopMelt();return;}
      const state=window.__backroom?.state||{},still=state.userStill||state.reduced||state.motion==='off'||state.motion==='still'||matchMedia('(prefers-reduced-motion: reduce)').matches;
      if(now-last<1000/(performanceMode()?20:30)){raf=requestAnimationFrame(tick);return;}
      last=now;noise.setAttribute('numOctaves',performanceMode()?'1':'2');
      const phase=still?0:elapsed*.0003;
      noise.setAttribute('baseFrequency',(.008+Math.sin(phase)*.0015)+' '+(.016+Math.cos(phase*.7)*.003));
      warp.setAttribute('scale',String(still?0:amount*48));blur.setAttribute('stdDeviation',String(amount*5));
      raf=requestAnimationFrame(tick);
    }
    raf=requestAnimationFrame(tick);
  }

  function wash(color, strength, keys) {
    const peak = 0.42 * strength * kOpacity(), d = kDuration();
    const n = add('fxwash'); n.style.background = /^#[0-9a-fA-F]{6}$/.test(color || '') ? color : '#9b6bff';
    anim(n, [{ opacity: 0 }, { opacity: peak, offset: 80 / 900 }, { opacity: 0 }], 900 * d);
    drop(n, 920 * d);
    if (keys && keys.length) {
      const img = mediaNode('fxflash', keys[0]);
      armShowcase(img);
      const spot = flashSpot(flashTurn++);
      Object.assign(img.style, {width:spot.w+'px',height:spot.h+'px',left:spot.x+'px',top:spot.y+'px',pointerEvents:'none'});
      previewMotion.then(m=>{if(img.isConnected && m) anim(img,m.flashPreviewFrames({width:spot.w,height:spot.h,opacity:0.85*strength*kOpacity(),motion:flashMotionAllowed(),variant:Math.floor(Math.random()*6)}),900*d,'linear');});
      drop(img, 920 * d);
    }
  }

  function gifFrom(rect, keys, ms, scale) {
    // Owner (2026-09-19): the picture fades in, holds low, fades out; never a hard pop. In and out take a real share of a short moment.
    const d = kDuration(), dur = (ms || 3400) * d, s = scale == null ? 1 : scale;
    const peak = Math.min(1, s) * 0.55 * kOpacity();
    const inF = Math.min(0.4, 450 / dur), outF = Math.min(0.45, 600 / dur);
    const dim = s < 1 ? null : add('fxdim');
    if (dim) { anim(dim, [{ opacity: 0 }, { opacity: 0.35, offset: inF }, { opacity: 0.35, offset: 1 - outF }, { opacity: 0 }], dur); drop(dim, dur + 40); }
    const img = mediaNode('fxfull', keys[0], 'full');
    const r = rect && Number.isFinite(rect.x) ? rect : { x: innerWidth / 2 - 30, y: innerHeight / 2 - 22, w: 60, h: 44 };
    const fromT = `translate(${r.x + r.w / 2 - innerWidth / 2}px, ${r.y + r.h / 2 - innerHeight / 2}px) scale(${Math.max(0.02, r.w / innerWidth)})`;
    const at = 'translate(0,0) scale(1)';
    anim(img, [{ transform: fromT, opacity: 0 }, { transform: at, opacity: peak, offset: inF },
      { transform: at, opacity: peak, offset: 1 - outF }, { transform: at, opacity: 0 }],
      dur, 'cubic-bezier(.4,0,.2,1)');
    drop(img, dur + 40);
  }

  function haze(token, ms) {
    if (!hazeEl) { hazeEl = add('fxdim'); hazeEl.style.background = 'transparent'; }
    anim(hazeEl, [{ backdropFilter: 'blur(0px)' }, { backdropFilter: `blur(${7 * kOpacity()}px)` }], 700);
    const end = () => { if (!hazeEl) return; const n = hazeEl; hazeEl = null; anim(n, [{ backdropFilter: 'blur(7px)' }, { backdropFilter: 'blur(0px)' }], 700); drop(n, 720); };
    if (token) held.set(token, end); else later(end, ms || 4000);
  }

  function tunnel(level) {
    tunnelLevel = Math.max(0, Math.min(1, Number(level) || 0));
    if (!tunnelEl) { tunnelEl = add('fxwash'); tunnelEl.style.transition = 'opacity .28s linear'; }
    const k = tunnelLevel;
    tunnelEl.style.background = `radial-gradient(circle at 50% 50%, rgba(6,3,12,0) ${(1 - 0.72 * k) * 62}%, rgba(6,3,12,${0.94 * Math.min(1, 1.3 * k)}) ${(1.12 - 0.5 * k) * 62}%)`;
    tunnelEl.style.opacity = k < 0.01 ? '0' : String(kOpacity());
  }

  const RECIPES = {
    'fx.jackpot': (keys, pool) => {                       // hero, 4 s
      spiralFull(4000, 0.7, 'screen');
      later(() => { flashBurst(8, 1.0, 300, keys); gifRain(19, 4000, 0.9, keys); glitch(3);
        words(9, 350, pool); later(() => words(9, 350, pool), 9 * 350);
        later(() => gifFull(2000, 0.8, keys), 700); }, 4000 * kDuration() + 1000);
    },
    'fx.gif_storm': (keys) => { flashBurst(5, 1.0, 300, keys); gifRain(14, 3000, 0.9, keys); glitch(1); },
    'fx.sub_cascade': (keys, pool, args) => {
      if (!(args && args.wordsShown)) words(9, 350, pool);   // the page drew them itself
      later(() => gifFull(1500, 0.8, keys), (args && args.wordsShown ? 0 : 9 * 350));
    },
    'fx.spiral_full': () => spiralFull(4000, 0.7, 'screen'),
    'fx.spiral_brief': () => spiralFull(1500, 0.55, 'screen'),
    'fx.gif_burst': (keys, pool, args) => flashBurst(Math.max(1, Math.min(8, (args && args.count) || 5)), 1.0, 300, keys),
    'fx.sub_pair': (keys, pool, args) => {
      if (!(args && args.wordsShown)) words(2, 500, pool);
      later(() => spiralFull(1500, 0.55, 'screen'), (args && args.wordsShown) ? 0 : 1000);
    },
    'fx.sub_single': (keys, pool, args) => { if (!(args && args.wordsShown)) words(1, 500, pool); },
    'fx.melt': () => melt(6000),
    // 10.13.B ids
    'fx.wash': (keys, pool, args) => wash(args && args.color, args && Number.isFinite(args.strength) ? args.strength : 0.7, keys),
    'fx.gif_from': (keys, pool, args) => gifFrom(args && args.from, keys, args && args.ms, args && args.scale),
    'fx.loom_spiral': (keys, pool, args, token) => spiralFull((args && args.ms) || 4200, (args && args.alpha) || 0.85,
      (args && args.preset) || 'screen', args && args.hold ? token : null),
    'fx.haze': (keys, pool, args, token) => haze(args && args.hold ? token : null, args && args.ms),
  };

  window.__renderFx = (m) => {
    try {
      if (document.hidden) return { fired: [], skipped: [m && m.fxId] };
      mount();
      nextWindow();   // one window per fx, so a recipe's primitives all draw from the same set
      const id = m && m.fxId, keys = Array.isArray(m && m.symbols) && m.symbols.length ? m.symbols : ['g0', 'g1', 'g2', 'g3'];
      const pool = (window.__fxWords && window.__fxWords.length) ? window.__fxWords : WORDS;
      const run = RECIPES[id];
      if (!run) return { fired: [], skipped: [id] };       // the host answers `unknown` the same way
      run(keys, pool, m.args || {}, m.token);
      return { fired: [id], skipped: [] };
    } catch (e) { console.warn('[phone-fx]', e); return { fired: [], skipped: [m && m.fxId] }; }
  };
  window.__releaseFx = (token) => { const end = held.get(token); if (end) { held.delete(token); end(); } };
  window.__tunnel = (level) => { try { tunnel(level); } catch (e) {  } };
  window.__fxCancelAll = () => {
    effectEpoch++;
    stopMelt();
    for(const stop of [...spiralPlayers])stop();
    for (const [, end] of held) { try { end(); } catch (e) {  } }
    held.clear(); tunnel(0);
    for (const timer of effectTimers) clearTimeout(timer);
    effectTimers.clear();
    if (root) for (const n of Array.from(root.children)) removeEffect(n);
    tunnelEl = hazeEl = null;
  };
  document.addEventListener('visibilitychange', () => { if (document.hidden) window.__fxCancelAll(); });
  window.addEventListener('pagehide', () => window.__fxCancelAll());
  window.__fxWords = WORDS.slice();
  window.__fxMedia = media;

  let canvasCursor = 0;
  const canvasWarmSeen = new Set(), canvasWarmQueue = [];
  let canvasWarming = 0;
  function pumpCanvasWarm() {
    while (canvasWarming < 2 && canvasWarmQueue.length) {
      const url = canvasWarmQueue.shift(); canvasWarming++;
      fetch(url, {cache:'force-cache', signal:AbortSignal.timeout(15000)})
        .then(r => r.ok ? r.arrayBuffer() : null).catch(() => {})
        .finally(() => { canvasWarming--; pumpCanvasWarm(); });
    }
  }
  function warmCanvasNext() {
    if (!media.on || !media.clips.length) return;
    for (let i=0;i<13;i++) {
      const url=HOP(media.clips[(canvasCursor+i)%media.clips.length],384);
      if (!canvasWarmSeen.has(url)) { canvasWarmSeen.add(url); canvasWarmQueue.push(url); }
    }
    pumpCanvasWarm();
  }
  window.__fxCanvasGifs = (count) => {
    const n = Math.max(1, Math.min(13, Number(count) || 4));
    const clips = media.on ? media.clips : [];
    const stills = media.on ? media.stills : [];
    const start = canvasCursor; canvasCursor += n;
    warmCanvasNext();
    return Array.from({ length: n }, (_, i) => {
      if (config.mode === 'local' && media.local.length) return {key:'g'+i,url:media.local[(start+i)%media.local.length],w:384,h:384,src:'pool'};
      if (clips.length) return { key: 'g' + i, url: HOP(clips[(start + i) % clips.length], 384), w: 384, h: 384, src: 'pool' };
      if (stills.length) return { key: 'g' + i, url: proxied(stills[(start + i) % stills.length]), w: 512, h: 512, src: 'pool' };
      return { key: 'g' + i, url: FALLBACK(i), w: 180, h: 180, src: 'pool' };
    });
  };

  function warmCanvas() {
    // The 3D rung first: the walls, the ceiling and gif-rain ask for it the moment the room opens.
    for (const u of media.clips.slice(0, RAIN_HEAD)) warm(u, 384);
    // Then the FIRST window only. The boot warm gets smaller, not bigger, because warmAhead below keeps the
    // next window a burst ahead of the player from then on.
    for (const u of media.clips.slice(0, BURST_WINDOW)) warm(u, performanceMode() ? RAIN_EDGE : OVERLAY_EDGE);
  }
  window.__brMedia = {
    get: () => ({...config, sources:config.sources.slice(), localCount:media.local.length, loading:media.on && !media.clips.length && !media.stills.length}),
    async set(mode, sources, disabledSources = config.disabledSources) {
      if (!['scrolller','bundled','local'].includes(mode)) throw new Error('Choose a media source.');
      const next = sources == null ? config.sources : cleanSources(sources);
      generation++; fetchController?.abort(); config = {mode, sources:next, disabledSources:cleanSources(disabledSources.join(',')).filter(n => next.includes(n))}; storeConfig();
      media.on = mode === 'scrolller'; media.tried = false; media.clips=[]; media.stills=[];
      canvasWarmQueue.length=0; canvasWarmSeen.clear(); canvasCursor=burstCursor=0;
      window.__fxCancelAll?.(); changed();
      window.__fxReady = warmMedia(); await window.__fxReady;
      return this.get();
    },
    async files(files) {
      const accepted = [...files].filter(f => /\.(gif|webp|png|jpe?g)$/i.test(f.name) && f.size <= 20*1024*1024).slice(0,48);
      if (!accepted.length) throw new Error('Choose GIF, WebP, PNG or JPEG files, up to 20 MB each.');
      // Keep old URLs valid for a pinned hand/reel until page teardown; no files leave this browser.
      if (localUrls.size + accepted.length > 192 || localBytes + accepted.reduce((n,f)=>n+f.size,0) > 160*1024*1024) throw new Error('Local file limit reached. Reload before choosing more files.');
      await localFiles(accepted); adoptFiles(accepted);
      await this.set('local'); return {count:accepted.length, skipped:files.length-accepted.length};
    }
  };
  window.addEventListener('pagehide', event => { if(!event.persisted){fetchController?.abort(); localUrls.forEach(url=>URL.revokeObjectURL(url));} });
  window.__fxSetOnline = on => window.__brMedia.set(on ? 'scrolller' : 'bundled');
  if (new URLSearchParams(location.search).get('media') === 'off') {config.mode='bundled'; media.on=false;}

  window.__fxReady = (async()=>{
    if(config.mode==='local') { try { adoptFiles(await localFiles() || []); } catch {} changed(); return; }
    try {
      const saved=JSON.parse(sessionStorage.getItem('br.media.pool.v1') || 'null');
      if(saved && JSON.stringify(saved.config)===JSON.stringify(config) && (saved.clips?.length || saved.stills?.length)) {
        media.clips=saved.clips; media.stills=saved.stills; media.tried=true; warmCanvas(); changed(); return;
      }
    } catch {}
    await warmMedia();
  })();
})();
