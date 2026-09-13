/* backroom/smoke/mock-station.js - a stand-in for stations/slot/station.js that
 * the room smoke check serves at that path. No glb, no WebGL: it proves the room
 * mounts a station through the section 7 shape and the ctx works. */

export async function mount(ctx) {
  const seen = { opened: false, closed: false, destroyed: false, suspended: null, state: null };
  window.__mockStation = { seen, ctxKeys: Object.keys(ctx) };
  let panel = null;
  return {
    async open() {
      panel = document.createElement('div');
      panel.className = 'mock-slot';
      panel.style.cssText = 'position:absolute;inset:18% 28%;display:grid;place-items:center;border-radius:18px;'
        + 'background:#2a1838;border:2px solid #ffcf6b;color:#f4e8ff;font:600 22px Segoe UI,sans-serif;text-align:center';
      panel.textContent = ctx.lex('br_station_slot', 'Slot') + ' (mock) - SP ' + ctx.sp();
      ctx.root.appendChild(panel);
      seen.opened = true;
      seen.state = await ctx.request('state', {});
      panel.textContent += ' - state ' + (seen.state.ok ? 'ok' : seen.state.reason);
    },
    close() { seen.closed = true; return new Promise((r) => setTimeout(r, 30)); },
    suspend(on) { seen.suspended = on; },
    destroy() { seen.destroyed = true; if (panel) panel.remove(); },
  };
}
