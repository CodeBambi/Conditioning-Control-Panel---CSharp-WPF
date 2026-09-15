/* serve.mjs - the tiny static server the dev pages want: Resources/web on 127.0.0.1, nothing leaves the machine.
 * The same shape the smoke checks use (backroom/smoke/*-check.mjs), with no Chrome and no fixtures.
 *
 *   node ConditioningControlPanel/Resources/web/backroom/shared/sound/serve.mjs     (SOUND_PORT, default 8940)
 *
 * Then open the audition page it prints, or any other dev page under the same root, dev.html included:
 *   http://127.0.0.1:8940/backroom/shared/sound/audition.html
 *   http://127.0.0.1:8940/backroom/stations/slot/dev.html?lever=A&reel=D
 * Ctrl+C stops it. */
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const WEB = resolve(fileURLToPath(import.meta.url), '../../../..');   // Resources/web
const PORT = Number(process.env.SOUND_PORT || 8940);
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };

createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(body);
  } catch { res.writeHead(404); res.end('no'); }
}).listen(PORT, '127.0.0.1', () => {
  console.log('serving ' + WEB);
  console.log('audition: http://127.0.0.1:' + PORT + '/backroom/shared/sound/audition.html');
  console.log('slot dev: http://127.0.0.1:' + PORT + '/backroom/stations/slot/dev.html');
});
