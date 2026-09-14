/* shared/hypno/tests/probe-station.js - a station that does nothing but hand its ctx to the test that
 * opened it (globalThis.__probe). loader.test.mjs imports it in node; kit-check.mjs serves it in place of
 * the slot station so the real room page's ctx can be driven over CDP. */

export async function mount(ctx) {
  const seen = { ctx, keys: Object.keys(ctx), opened: 0, closed: 0, destroyed: 0 };
  globalThis.__probe = seen;
  return {
    async open() { seen.opened++; },
    async close() { seen.closed++; },
    suspend() {},
    destroy() { seen.destroyed++; },
  };
}
