/* ============================================================================
 * __phone-options.js - the quick options sheet for the web build.
 *
 * The room already owns an Options panel (10.14: effects intensity, tunnel,
 * melt, the floor-bell opt-in) and a Motion pill. This sheet is the SHELL's
 * half - the settings the WPF host owns in the app and a web page otherwise has
 * no way to reach:
 *
 *   Motion     -> the `settings` frame's motion/reduced (Full / Reduced / Still)
 *   Effects    -> intensity Calm / Normal / Full, the same three the room shows
 *   Overlays   -> the 10.13.A gates, one switch each
 *   HUD        -> how much room chrome is painted; Back never hides (Law VI)
 *   Volume     -> a real master gain in front of the page's audio
 *   Media      -> the Scrolller feed on or off
 *   Ledger     -> the balance, a test top-up, and a full wipe
 *
 * It loads before room/main.js so the audio patch is in place before the first
 * AudioContext exists.
 * ==========================================================================*/
(() => {
  const LS = 'br.opt.v1';
  const DEFAULTS = { motion: 'full', intensity: 'normal', hud: 'full', volume: 0.8, musicVolume: 0.15, media: true,
    gates: { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true } };
  let opt = { ...DEFAULTS };
  try { opt = { ...DEFAULTS, ...(JSON.parse(localStorage.getItem(LS) || '{}') || {}) }; } catch (e) { /* noop */ }
  opt.gates = { ...DEFAULTS.gates, ...(opt.gates || {}) };
  const save = () => { try { localStorage.setItem(LS, JSON.stringify(opt)); } catch (e) { /* noop */ } };

  /* ---- MASTER VOLUME -------------------------------------------------------
   * The room's sound kit builds its own AudioContext and its own master gain, so
   * the only place a shell can sit is in front of `ctx.destination`. Every
   * context the page makes gets a gain node handed to it in destination's place;
   * the real output is downstream. Media elements are covered too, for the fx
   * clips that are not muted. */
  let music = null;
  const musicReady = import('/__phone-music.js').then(m => { music = m.getMusic({volume:opt.musicVolume,master:opt.volume}); opt.musicVolume = music.volume; music.setVolume(music.volume,opt.volume); return music; }).catch(() => null);
  const qualityReady = import('/backroom/shared/quality.js');
  const gains = new Set();
  const Ctor = window.AudioContext || window.webkitAudioContext;
  if (Ctor) {
    const Patched = function (...args) {
      const ctx = new Ctor(...args);
      let node = null;
      try {
        node = ctx.createGain();
        node.gain.value = opt.volume;
        node.connect(ctx.destination);
        gains.add(node);
        Object.defineProperty(ctx, 'destination', { get: () => node, configurable: true });
        ctx.__brMasterGain = node;
      } catch (e) { /* an old engine keeps the real destination */ }
      return ctx;
    };
    Patched.prototype = Ctor.prototype;
    window.AudioContext = Patched;
    if (window.webkitAudioContext) window.webkitAudioContext = Patched;
  }
  function applyVolume() {
    music?.setVolume(opt.musicVolume,opt.volume);
    for (const g of gains) { try { g.gain.value = opt.volume; } catch (e) { gains.delete(g); } }
    document.querySelectorAll('video,audio').forEach((n) => { if (!n.muted && !n.dataset.brMusic) n.volume = opt.volume; });
  }

  /* ---- HUD -----------------------------------------------------------------
   * Three steps, and Back survives all three: the exits are sacred (Law VI), so
   * "off" hides chrome, never the way out. */
  const HUD_CSS = `
    html[data-brhud="lean"] .br-bell, html[data-brhud="lean"] .br-hint,
    html[data-brhud="lean"] .br-map-list, html[data-brhud="lean"] .br-crosshair { display:none !important; }
    html[data-brhud="off"] .br-bell, html[data-brhud="off"] .br-hint, html[data-brhud="off"] .br-map-list,
    html[data-brhud="off"] .br-crosshair, html[data-brhud="off"] .br-nav,
    html[data-brhud="off"] .br-sp { display:none !important; }`;
  const applyHud = () => document.documentElement.setAttribute('data-brhud', opt.hud);

  /* ---- the sheet ----------------------------------------------------------- */
  const CSS = `
    #__opt-btn { position:fixed; top:calc(env(safe-area-inset-top,0px) + 10px); right:10px; z-index:2147482000;
      width:42px; height:42px; border-radius:50%; border:1px solid #6b4a78; background:rgba(28,16,32,.82);
      color:#f6ecff; font:20px/1 sans-serif; -webkit-backdrop-filter:blur(6px); backdrop-filter:blur(6px); }
    /* The gear is this shim's, not the room's, so the gear is what gets out of the way. It sat on top of the
       SP readout and cut the number in half (owner, 2026-09-16). Two selectors deep beats room.css's one, so
       no !important is needed, and the station's own top-right chips move with it. */
    body .br-hud { right: 64px; }
    /* room.css keeps .br-nav clear of the SP chip with right: 120px. The chip has just moved 64px left to get
       out from under this gear, so the reservation has to move with it, or Options ends up underneath the chip
       (owner screenshot, 2026-09-16: "Optio|SP 1000"). Above 600px only: below that room.css gives .br-nav its
       own row and this would drag it off the right edge. */
    @media (min-width: 601px) { body .br-nav { right: 260px; } }
    #__opt { position:fixed; inset:auto 0 0 0; z-index:2147482001; max-height:76vh; overflow:auto;
      background:#1b1020; color:#f6ecff; border-top:1px solid #6b4a78; border-radius:16px 16px 0 0;
      padding:14px 16px calc(env(safe-area-inset-bottom,0px) + 18px);
      font:14px/1.4 system-ui,-apple-system,"Segoe UI",sans-serif; transform:translateY(102%);
      transition:transform .22s cubic-bezier(.2,.9,.3,1); }
    #__opt[data-open="1"] { transform:translateY(0); }
    #__opt h3 { margin:2px 0 10px; font-size:15px; letter-spacing:.02em; }
    #__opt .row { display:flex; align-items:center; justify-content:space-between; gap:12px; margin:11px 0; }
    #__opt .row > span { opacity:.88; }
    #__opt .segs { display:flex; gap:6px; flex-wrap:wrap; }
    #__opt button.seg { border:1px solid #6b4a78; background:transparent; color:#f6ecff; border-radius:999px;
      padding:7px 13px; font:13px system-ui,sans-serif; min-height:34px; }
    #__opt button.seg[aria-pressed="true"] { background:#7b4bd8; border-color:#a07ce8; }
    #__opt .gates { display:grid; grid-template-columns:1fr 1fr; gap:6px; }
    #__opt input[type=range] { width:150px; }
    #__opt button.br-options-back { position:sticky; top:0; z-index:2; min-height:46px; min-width:138px;
      padding:10px 18px; margin-bottom:10px; color:#301c39; background:linear-gradient(#ffe9aa,#efbc64);
      border:2px solid #fff0bf; border-radius:14px; font:800 15px system-ui;
      box-shadow:0 3px 0 #70412b,0 0 16px #efbc6440; }
    #__opt .note { opacity:.62; font-size:12px; margin:8px 0 2px; }
    #__opt hr { border:0; border-top:1px solid #3a2444; margin:14px 0 6px; }
    #__opt .act { display:flex; gap:8px; flex-wrap:wrap; margin-top:8px; }
    #__opt button.act-btn { border:1px solid #6b4a78; background:#2a1733; color:#f6ecff; border-radius:9px;
      padding:9px 12px; font:13px system-ui,sans-serif; }`;

  const nicheStyle = document.createElement('style');nicheStyle.textContent=`
    #__opt .niche-entry { display:flex;align-items:center;gap:8px;max-width:520px;margin:8px 0 12px; }
    #__opt .niche-entry > span { font-size:18px; }
    #__opt .niche-entry input { min-width:0;flex:1;padding:11px;border:1px solid #b795dc;border-radius:10px;background:#281635;color:#fff0dc;font:16px system-ui; }
    #__opt .niche-pills { display:flex;flex-wrap:wrap;gap:8px; }
    #__opt .niche-pill { display:inline-flex;border:1px solid #6b4a78;border-radius:24px;overflow:hidden; }
    #__opt .niche-pill .seg { border:0;border-radius:0;min-height:44px; }
    #__opt .niche-remove { min-width:40px;border:0;border-left:1px solid #6b4a78;background:#2a1733;color:#f6ecff;font-size:21px; }
    #__opt .niche-pill button:focus-visible { outline:2px solid #ffe9aa;outline-offset:-3px; }
  `;document.head.append(nicheStyle);

  const seg = (value, label, current, onPick) => {
    const b = document.createElement('button');
    b.type = 'button'; b.className = 'seg'; b.textContent = label;
    b.setAttribute('aria-pressed', String(value === current));
    b.addEventListener('click', () => onPick(value));
    return b;
  };
  const row = (label, node) => {
    const r = document.createElement('div'); r.className = 'row';
    const s = document.createElement('span'); s.textContent = label;
    r.append(s, node); return r;
  };
  const segRow = (label, pairs, current, onPick) => {
    const box = document.createElement('div'); box.className = 'segs';
    pairs.forEach(([v, t]) => box.append(seg(v, t, current, onPick)));
    return row(label, box);
  };

  let sheet = null;
  function paint() {
    if (!sheet) return;
    const input=sheet.querySelector('#__media-sources');
    const draft=input?.value||'', focused=document.activeElement===input, caret=input?.selectionStart;
    sheet.innerHTML = '';
    const h = document.createElement('h3'); h.textContent = 'Options';
    const back=document.createElement('button');back.type='button';back.className='act-btn br-options-back';back.textContent='← Back to game';back.onclick=()=>sheet.dataset.open='0';sheet.append(back,h);

    const policy=window.__backroomQuality;
    sheet.append(segRow('Quality', [['auto','Auto'],['full','Full'],['performance','Performance']],policy?.mode || 'auto', v => { qualityReady.then(({quality}) => { quality.setMode(v); paint(); }); }));
    sheet.append(segRow('Motion', [['full', 'Full'], ['reduced', 'Reduced'], ['still', 'Still']], opt.motion, (v) => {
      opt.motion = v; save(); push(); paint();
    }));
    sheet.append(segRow('Effects', [['calm', 'Calm'], ['normal', 'Normal'], ['full', 'Full']], opt.intensity, (v) => {
      opt.intensity = v; save(); push(); paint();
    }));
    sheet.append(segRow('HUD', [['full', 'Full'], ['lean', 'Lean'], ['off', 'Off']], opt.hud, (v) => {
      opt.hud = v; save(); applyHud(); paint();
    }));

    const vol = document.createElement('input');
    vol.type = 'range'; vol.min = '0'; vol.max = '1'; vol.step = '0.05'; vol.value = String(opt.volume);
    vol.addEventListener('input', () => { opt.volume = Number(vol.value); applyVolume(); });
    vol.addEventListener('change', save);
    vol.setAttribute('aria-label','Master volume');
    sheet.append(row('Master volume', vol));
    const mv=document.createElement('input');mv.type='range';mv.min='0';mv.max='1';mv.step='.01';mv.value=String(opt.musicVolume);mv.setAttribute('aria-label','Music volume');
    const ml=document.createElement('span');ml.textContent=Math.round(opt.musicVolume*100)+'%';
    const mb=document.createElement('div');mb.append(mv,ml);
    mv.addEventListener('input',()=>{opt.musicVolume=Number(mv.value);ml.textContent=Math.round(opt.musicVolume*100)+'%';music?.setVolume(opt.musicVolume,opt.volume);});
    mv.addEventListener('change',save);sheet.append(row('Music',mb));

    const gatesBox = document.createElement('div'); gatesBox.className = 'gates';
    [['flash', 'Flash'], ['subliminal', 'Subliminal'], ['spiral', 'Spiral'], ['brainDrain', 'Brain drain'], ['tunnel', 'Tunnel']]
      .forEach(([k, name]) => gatesBox.append(seg(k, name, opt.gates[k] ? k : null, () => {
        opt.gates[k] = !opt.gates[k]; save(); push(); paint();
      })));
    const gr = document.createElement('div'); gr.className = 'row';
    const gs = document.createElement('span'); gs.textContent = 'Overlays';
    gr.append(gs, gatesBox); sheet.append(gr);

    const source = window.__brMedia?.get();
    if (source) {
      const heading=document.createElement('h3');heading.textContent='Pictures and GIFs';sheet.append(heading);
      const status=document.createElement('p');status.className='note';status.setAttribute('role','status');
      const apply=async(mode,names)=>{try {status.textContent='Loading media...';await window.__brMedia.set(mode,names);paint();}catch(e){status.textContent=e.message;}};
      sheet.append(segRow('Source', [['scrolller','Scrolller'],['local','My files'],['bundled','Built-in']],source.mode,v=>apply(v)));
      if(source.mode==='scrolller') {
        const label=document.createElement('label');label.textContent='Add a niche';label.htmlFor='__media-sources';sheet.append(label);
        const form=document.createElement('form');form.className='niche-entry';
        const prefix=document.createElement('span');prefix.textContent='r/';prefix.setAttribute('aria-hidden','true');
        const names=document.createElement('input');names.id='__media-sources';names.type='text';names.placeholder='Community name';names.autocomplete='off';names.spellcheck=false;names.autocapitalize='none';
        const add=document.createElement('button');add.type='submit';add.className='act-btn';add.textContent='Add';
        form.append(prefix,names,add);sheet.append(form);
        const pills=document.createElement('div');pills.className='niche-pills';pills.setAttribute('aria-label','Your niches');sheet.append(pills);
        const update=(next,disabled)=>{window.__brMedia.set('scrolller',next.join(','),disabled).catch(e=>{status.textContent=e.message;});};
        for(const name of source.sources){
          const pill=document.createElement('span');pill.className='niche-pill';
          const active=!(source.disabledSources||[]).includes(name);
          const toggle=seg(name,'r/'+name,active?name:null,()=>{const current=window.__brMedia.get();const off=new Set(current.disabledSources||[]);off.has(name)?off.delete(name):off.add(name);update(current.sources,[...off]);});
          const remove=document.createElement('button');remove.type='button';remove.className='niche-remove';remove.textContent='×';remove.setAttribute('aria-label','Remove r/'+name);
          remove.onclick=()=>{const current=window.__brMedia.get();update(current.sources.filter(n=>n!==name),(current.disabledSources||[]).filter(n=>n!==name));};
          pill.append(toggle,remove);pills.append(pill);
        }
        form.onsubmit=e=>{e.preventDefault();const value=names.value.trim().replace(/^https?:\/\/(?:www\.)?(?:reddit\.com|scrolller\.com)\//i,'').replace(/^\/?r\//i,'').replace(/\/$/,'');
          const current=window.__brMedia.get();
          if(!/^[a-zA-Z0-9_]{2,40}$/.test(value)){status.textContent='Enter one community name.';names.focus();return;}
          if(current.sources.some(n=>n.toLowerCase()===value.toLowerCase())){status.textContent='That niche is already added.';names.focus();return;}
          if(current.sources.length>=8){status.textContent='You can keep up to 8 niches. Remove one to add another.';return;}
          names.value='';update([...current.sources,value],current.disabledSources||[]);sheet.querySelector('#__media-sources')?.focus();
        };
        status.textContent='Tap a niche to turn it on or off. Up to 8 niches.';
      } else if(source.mode==='local') {
        const files=document.createElement('input');files.type='file';files.multiple=true;files.accept='.gif,.webp,.png,.jpg,.jpeg';files.id='__media-files';files.setAttribute('aria-label','Choose local pictures and GIFs');files.style.maxWidth='100%';
        files.onchange=async()=>{if(!files.files.length)return;try{const result=await window.__brMedia.files(files.files);paint();const feedback=sheet.querySelector('[role=status]');if(feedback && result.skipped)feedback.textContent+=` ${result.skipped} unsupported, oversized or extra files skipped.`;}catch(e){status.textContent=e.message;}};
        sheet.append(files);status.textContent=`${source.localCount} files selected. GIF, WebP, PNG and JPEG, up to 48 files, 20 MB each and 160 MB per page session. Files stay on this device and must be selected again after reloading. Built-in art is used until then.`;
      } else status.textContent='Bundled pictures, no online feed required.';
      sheet.append(status);
      const timing=document.createElement('p');timing.className='note';timing.textContent='Walls and new flashes update now. Game artwork updates on the next visit; your current hand and prepaid spins are kept.';sheet.append(timing);
    }

    sheet.append(document.createElement('hr'));
    const led = window.__brServer && window.__brServer.ledger;
    const bal = document.createElement('p'); bal.className = 'note';
    bal.textContent = led
      ? `Balance ${led.sp()} SP - the real paytable, the real odds, a ledger kept on this device only.`
      : 'Ledger starting up...';
    sheet.append(bal);
    const acts = document.createElement('div'); acts.className = 'act';
    const add = document.createElement('button'); add.type = 'button'; add.className = 'act-btn';
    add.textContent = '+500 SP (test)';
    add.addEventListener('click', () => {
      if (!led) return;
      const sp = led.grant(500);
      if (window.__brSettings) window.__brSettings.balance(sp);
      paint();
    });
    const wipe = document.createElement('button'); wipe.type = 'button'; wipe.className = 'act-btn';
    wipe.textContent = 'Reset ledger';
    wipe.addEventListener('click', () => {
      if (!led || !confirm('Wipe this device’s balance, tapes, melt, jar and prizes?')) return;
      led.reset();
      location.reload();
    });
    acts.append(add, wipe);
    sheet.append(acts);
    const restored=sheet.querySelector('#__media-sources');if(restored){restored.value=draft;if(focused){restored.focus();restored.setSelectionRange(caret,caret);}}
  }

  /** Hand the page the settings frame it already knows how to read. */
  function push() {
    if (!window.__brSettings) return;
    window.__brSettings.set({
      motion: opt.motion === 'still' ? 'off' : opt.motion,
      intensity: opt.intensity,
      gates: opt.gates,
    });
  }

  window.addEventListener('DOMContentLoaded', () => {
    const style = document.createElement('style');
    style.textContent = CSS + HUD_CSS;
    document.head.append(style);

    const btn = document.createElement('button');
    btn.id = '__opt-btn'; btn.type = 'button'; btn.textContent = '⚙';
    btn.setAttribute('aria-label', 'Options');

    sheet = document.createElement('div');
    sheet.id = '__opt'; sheet.dataset.open = '0';
    // Options own keyboard input, including game shortcuts while seated.
    for (const type of ['keydown','keyup']) window.addEventListener(type,e=>{
      if(sheet.dataset.open!=='1') return;
      e.stopImmediatePropagation();
      if(type==='keydown' && e.key==='Enter' && e.target?.id==='__media-sources'){e.preventDefault();e.target.form?.requestSubmit();return;}
      if(e.key==='Escape'){e.preventDefault();sheet.dataset.open='0';btn.focus();}
    },true);
    btn.addEventListener('click', () => {
      const open = sheet.dataset.open === '1';
      sheet.dataset.open = open ? '0' : '1';
      if (!open) paint();
    });
    document.addEventListener('pointerdown', (e) => {
      if (sheet.dataset.open === '1' && !sheet.contains(e.target) && !btn.contains(e.target)) sheet.dataset.open = '0';
    }, true);

    document.body.append(btn, sheet);
    applyHud();
    applyVolume();
    window.__fxIntensity = opt.intensity;

    // The page is up by now, so the first settings frame lands on a live room.
    setTimeout(push, 300);
    paint();
  });

  window.__brOptions = { get: () => ({ ...opt }), paint: () => paint(), openMedia() { if(sheet){sheet.dataset.open='1';paint();sheet.querySelector('#__media-sources, #__media-files')?.scrollIntoView({block:'nearest'});} } };
  window.addEventListener('br-media-changed', () => { if(sheet?.dataset.open==='1') paint(); });
})();
