/** Opt-in preview diagnostics. Bounded local logs; no media URLs or network uploads. */
export function createPerfPanel(container = document.body) {
  const started = performance.now(), logs = [], batch = [];
  const flags = { freezeMedia: false, skipPost: false };
  let previous = 0, updated = 0, latest = null, observer = null, longTasks = 0;
  const panel = document.createElement('div');
  panel.style.cssText = 'background:#12111e;color:#eee;border:1px solid #877799;border-radius:8px;padding:9px;font:12px/1.45 monospace;min-width:0;pointer-events:auto';
  panel.innerHTML = `<strong>Performance</strong><pre style="margin:5px 0;white-space:pre-wrap">Collecting frames...</pre><div><button data-action="mark">Mark stutter</button> <button data-action="save">Save log</button> <button data-action="fold">Hide</button></div><label style="display:block"><input type="checkbox" data-flag="freezeMedia"> Freeze media updates</label><label style="display:block"><input type="checkbox" data-flag="skipPost"> Disable post effects</label><div><button data-action="canvas">Check canvas</button> <button data-action="software">Software canvas</button></div><pre data-surface style="white-space:pre-wrap;margin:4px 0"></pre><img data-capture hidden style="width:240px;max-width:100%;border:1px solid #ffcf6b" alt="Captured game canvas"><small>Compare one switch at a time. Timings are CPU only.</small>`;
  container.appendChild(panel);
  const readout = panel.querySelector('pre');
  function log(type, data = {}) {
    logs.push({ t: Math.round(performance.now() - started), type, ...data });
    if (logs.length > 2400) logs.splice(0, logs.length - 2400);
  }
  function report() {
    return { version: 1, created: new Date().toISOString(), elapsedMs: Math.round(performance.now() - started),
      environment: { build: '2026-09-20-soundtrack-restore-18', browser: navigator.userAgent, viewport: [innerWidth, innerHeight], dpr: devicePixelRatio,
        canvasMode: document.querySelector('.bo-stage')?.getContext('2d')?.getContextAttributes?.().willReadFrequently ? 'software' : 'default' },
      note: 'CPU submission timings exclude GPU completion. Frozen media may still decode in background. Logs contain no media URLs.',
      flags: { ...flags }, latest, logs: logs.slice() };
  }
  function save() {
    log('export');
    const url = URL.createObjectURL(new Blob([JSON.stringify(report(), null, 2)], { type: 'application/json' }));
    const a = document.createElement('a'); a.href = url; a.download = 'breakout-performance-' + Date.now() + '.json'; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  panel.addEventListener('click', e => {
    const action = e.target.dataset.action;
    if (action === 'software') {
      const url=new URL(location.href);url.searchParams.set('software','1');location.href=url.href;
    }
    if (action === 'canvas') {
      const c=document.querySelector('.bo-stage'),out=panel.querySelector('[data-surface]');
      if(!c){out.textContent='No playfield canvas';return;}
      const g=c.getContext('2d'),rect=c.getBoundingClientRect(),style=getComputedStyle(c);
      const top=document.elementFromPoint(rect.x+rect.width/2,rect.y+rect.height/2);
      const state={size:[c.width,c.height],cssSize:[rect.width,rect.height],
        visible:style.visibility,display:style.display,opacity:style.opacity,
        coveredBy:top===c?'none':`${top?.tagName||'?'} ${top?.className||''}`,
        contextLost:g.isContextLost?.()??'unsupported',mode:g.getContextAttributes?.().willReadFrequently?'software':'default',
        alpha:g.globalAlpha,composite:g.globalCompositeOperation,filter:g.filter,
        transform:g.getTransform().toString()};
      try {
        state.centre=[...g.getImageData(Math.floor(c.width/2),Math.floor(c.height/2),1,1).data];
        const capture=panel.querySelector('[data-capture]');capture.src=c.toDataURL();capture.hidden=false;
      } catch(e) {state.readError=e.name;}
      log('canvas-check',state);
      out.textContent=`Canvas ${state.size.join('x')} | ${state.mode}\nLost: ${state.contextLost} | ${state.visible}\nCovered by: ${state.coveredBy}\nCentre: ${state.centre?.join(',')||state.readError}\nAlpha ${state.alpha} | ${state.composite}\nFilter: ${state.filter}`;
    }
    if (action === 'save') save();
    if (action === 'mark') { log('user-stutter', { latest }); e.target.textContent = 'Marked'; setTimeout(() => { e.target.textContent = 'Mark stutter'; }, 700); }
    if (action === 'fold') panel.hidden=true;
  });
  panel.addEventListener('change', e => { const key = e.target.dataset.flag; if (key in flags) { flags[key] = e.target.checked; log('switch', { key, value: flags[key] }); } });
  try {
    if (PerformanceObserver.supportedEntryTypes.includes('longtask')) {
      observer = new PerformanceObserver(list => { for (const item of list.getEntries()) { longTasks++; log('long-task', { startMs: Math.round(item.startTime - started), durationMs: +item.duration.toFixed(2) }); } });
      observer.observe({ type: 'longtask', buffered: false });
    }
  } catch (_) { /* Not all browsers expose long tasks. */ }
  const visibility = () => { previous = 0; batch.length = 0; log('visibility', { hidden: document.hidden }); };
  document.addEventListener('visibilitychange', visibility);
  log('start', { longTaskSupported: !!observer });
  return {
    flags, log, report,
    toggle() { panel.hidden=!panel.hidden; },
    idle() { previous = 0; batch.length = 0; },
    frame(ts, times, state) {
      const gap = previous ? ts - previous : 0; previous = ts;
      if (gap <= 0 || document.hidden) return;
      const sample = { gap, ...times, renderTimings: { ...state.renderTimings } };
      batch.push(sample);
      if (batch.length > 500) batch.shift();
      if (gap > 40) log('slow-frame', { gapMs: +gap.toFixed(2), ...times, ...state, flags: { ...flags } });
      if (ts - updated < 1000) return;
      updated = ts;
      const gaps = batch.map(x => x.gap).sort((a, b) => a - b);
      const avg = key => +(batch.reduce((n, x) => n + x[key], 0) / batch.length).toFixed(2);
      const renderTimings = Object.fromEntries(Object.keys(state.renderTimings || {}).map(key => [key,
        +(batch.reduce((n, x) => n + (x.renderTimings[key] || 0), 0) / batch.length).toFixed(2)]));
      latest = { fps: +(1000 / avg('gap')).toFixed(1), frameP95: +gaps[Math.min(gaps.length - 1, Math.floor(gaps.length * .95))].toFixed(2),
        worstMs: +gaps[gaps.length - 1].toFixed(2), simMs: avg('simMs'), mediaMs: avg('mediaMs'), drawMs: avg('drawMs'), totalMs: avg('totalMs'),
        longTasks, ...state, renderTimings, flags: { ...flags } };
      log('sample', latest);
      readout.textContent = `${latest.fps} FPS | p95 ${latest.frameP95} ms\nWorst ${latest.worstMs} ms | long tasks ${longTasks}\nCPU sim ${latest.simMs} | media ${latest.mediaMs}\nDraw ${latest.drawMs} | total ${latest.totalMs} ms\nWall ${state.wall} | ${state.phase} | time x${state.timeScale}\nBalls ${state.balls} | bubbles ${state.bubbles}`;
      batch.length = 0;
    },
    dispose() { observer?.disconnect(); document.removeEventListener('visibilitychange', visibility); panel.remove(); },
  };
}
