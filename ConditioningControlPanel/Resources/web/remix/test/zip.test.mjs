// A dropped zip, opened by hand: every archive here is built byte by byte so
// the parser meets the shapes real writers produce, macOS included.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import zlib from 'node:zlib';
import { unzip, isZipFile, keepsMedia, ZIP_CAP, MEDIA_TYPES } from '../engine/zip.js';

const SIG_LOCAL = 0x04034b50;
const SIG_CENTRAL = 0x02014b50;
const SIG_EOCD = 0x06054b50;
const SIG_DD = 0x08074b50;

const crcOf = buf => zlib.crc32(buf) >>> 0;
const bytes = s => (Buffer.isBuffer(s) ? s : Buffer.from(String(s), 'utf8'));

/**
 * Build a real zip.
 *
 * Per entry: { name, nameBytes, data, method: 0|8, dataDescriptor, utf8,
 *              attrs, localExtra, corrupt, sizeOverride }
 * `corrupt` writes the body raw while still claiming method 8.
 *
 * opts: { comment, count, cdOrder } - `cdOrder` reorders the central
 * directory without moving the local records, which is how the result order
 * gets tested against the right one of the two.
 */
function makeZip(entries, opts = {}) {
  const locals = [];
  const centrals = [];
  let offset = 0;

  for (const e of entries) {
    const name = e.nameBytes || bytes(e.name);
    const plain = bytes(e.data ?? '');
    const method = e.method ?? 0;
    const body = method === 8 && !e.corrupt ? zlib.deflateRawSync(plain) : plain;
    const crc = crcOf(plain);
    const extra = e.localExtra || Buffer.alloc(0);
    let flags = 0;
    if (e.utf8) flags |= 0x800;
    if (e.dataDescriptor) flags |= 0x08;

    const lh = Buffer.alloc(30);
    lh.writeUInt32LE(SIG_LOCAL, 0);
    lh.writeUInt16LE(20, 4);
    lh.writeUInt16LE(flags, 6);
    lh.writeUInt16LE(method, 8);
    // A data descriptor entry leaves crc and both sizes at zero here.
    lh.writeUInt32LE(e.dataDescriptor ? 0 : crc, 14);
    lh.writeUInt32LE(e.dataDescriptor ? 0 : body.length, 18);
    lh.writeUInt32LE(e.dataDescriptor ? 0 : plain.length, 22);
    lh.writeUInt16LE(name.length, 26);
    lh.writeUInt16LE(extra.length, 28);

    const parts = [lh, name, extra, body];
    if (e.dataDescriptor) {
      const dd = Buffer.alloc(16);
      dd.writeUInt32LE(SIG_DD, 0);
      dd.writeUInt32LE(crc, 4);
      dd.writeUInt32LE(body.length, 8);
      dd.writeUInt32LE(plain.length, 12);
      parts.push(dd);
    }
    const chunk = Buffer.concat(parts);

    const ch = Buffer.alloc(46);
    ch.writeUInt32LE(SIG_CENTRAL, 0);
    ch.writeUInt16LE(20, 4);
    ch.writeUInt16LE(20, 6);
    ch.writeUInt16LE(flags, 8);
    ch.writeUInt16LE(method, 10);
    ch.writeUInt32LE(crc, 16);
    ch.writeUInt32LE(e.sizeOverride ?? body.length, 20);
    ch.writeUInt32LE(e.sizeOverride ?? plain.length, 24);
    ch.writeUInt16LE(name.length, 28);
    ch.writeUInt32LE(e.attrs ?? 0, 38);
    ch.writeUInt32LE(offset, 42);
    centrals.push(Buffer.concat([ch, name]));

    locals.push(chunk);
    offset += chunk.length;
  }

  const order = opts.cdOrder || entries.map((_, i) => i);
  const cd = Buffer.concat(order.map(i => centrals[i]));
  const body = Buffer.concat(locals);
  const comment = bytes(opts.comment ?? '');
  const n = opts.count ?? entries.length;

  const eo = Buffer.alloc(22);
  eo.writeUInt32LE(SIG_EOCD, 0);
  eo.writeUInt16LE(n, 8);
  eo.writeUInt16LE(n, 10);
  eo.writeUInt32LE(cd.length, 12);
  eo.writeUInt32LE(opts.cdAt ?? body.length, 16);
  eo.writeUInt16LE(comment.length, 20);

  return Buffer.concat([body, cd, eo, comment]);
}

const zipFile = (entries, opts = {}) =>
  new File([new Uint8Array(makeZip(entries, opts))], opts.filename ?? 'drop.zip', {
    type: opts.type ?? 'application/zip',
  });

const names = files => files.map(f => f.name);
const textOf = async file => Buffer.from(await file.arrayBuffer()).toString('utf8');

test('a stored entry comes out with its bytes intact', async () => {
  const files = await unzip(zipFile([{ name: 'one.gif', data: 'GIF89a stored' }]));
  assert.equal(files.length, 1);
  assert.equal(files[0].name, 'one.gif');
  assert.equal(files[0].type, 'image/gif');
  assert.equal(await textOf(files[0]), 'GIF89a stored');
});

test('a deflated entry is inflated back to the original', async () => {
  const long = 'GIF89a' + 'wide awake and drifting '.repeat(200);
  const buf = makeZip([{ name: 'two.gif', data: long, method: 8 }]);
  const files = await unzip(new File([new Uint8Array(buf)], 'a.zip'));
  assert.equal(files.length, 1);
  assert.equal(await textOf(files[0]), long);
  // and it really was compressed, so the deflate path was the one taken
  assert.ok(buf.length < long.length);
});

test('a data descriptor entry, local sizes zero, still round trips', async () => {
  const data = 'GIF89a written by a tool that streamed it';
  const files = await unzip(
    zipFile([{ name: 'mac.gif', data, method: 8, dataDescriptor: true, utf8: true }]),
  );
  assert.equal(files.length, 1);
  assert.equal(await textOf(files[0]), data);
});

test('the data start comes from the local header, not the central one', async () => {
  // A local extra field the central record knows nothing about: reading the
  // central lengths would land the parser mid stream.
  const files = await unzip(
    zipFile([{ name: 'x.gif', data: 'GIF89a offset', localExtra: Buffer.alloc(37, 7) }]),
  );
  assert.equal(await textOf(files[0]), 'GIF89a offset');
});

test('folders, Apple junk, dotfiles and documents are all left behind', async () => {
  const files = await unzip(
    zipFile([
      { name: 'pics/', data: '', attrs: 0x10 },
      { name: 'pics/keep.gif', data: 'a' },
      { name: '__MACOSX/pics/._keep.gif', data: 'b' },
      { name: 'pics/._keep.gif', data: 'c' },
      { name: 'pics/.DS_Store', data: 'd' },
      { name: '.hidden/also.gif', data: 'e' },
      { name: 'readme.txt', data: 'f' },
      { name: 'pics/second.png', data: 'g' },
    ]),
  );
  assert.deepEqual(names(files), ['keep.gif', 'second.png']);
});

test('a folder row with no trailing slash is still caught by its attrs', async () => {
  await assert.rejects(
    unzip(zipFile([{ name: 'pics.gif', data: '', attrs: 0x10 }])),
    /nothing the room can use/,
  );
});

test('the result follows the central directory, not the order on disk', async () => {
  const entries = [
    { name: 'a.gif', data: 'a' },
    { name: 'b.gif', data: 'b' },
    { name: 'c.gif', data: 'c' },
  ];
  const files = await unzip(zipFile(entries, { cdOrder: [2, 0, 1] }));
  assert.deepEqual(names(files), ['c.gif', 'a.gif', 'b.gif']);
  assert.equal(await textOf(files[0]), 'c');
});

test('the cap stops the drop and the rest are ignored quietly', async () => {
  const entries = Array.from({ length: 12 }, (_, i) => ({ name: `g${i}.gif`, data: `${i}` }));
  const three = await unzip(zipFile(entries), { cap: 3 });
  assert.deepEqual(names(three), ['g0.gif', 'g1.gif', 'g2.gif']);
  assert.equal((await unzip(zipFile(entries))).length, 12);
  assert.equal(ZIP_CAP, 100);
});

test('a nested name keeps only its last segment, and its type', async () => {
  const files = await unzip(
    zipFile([
      { name: 'pics/a/b.GIF', data: '1' },
      { name: 'clips/deep/c.MP4', data: '2' },
      { name: 'clips/d.mov', data: '3' },
      { name: 'shots/e.jpeg', data: '4' },
    ]),
  );
  assert.deepEqual(names(files), ['b.GIF', 'c.MP4', 'd.mov', 'e.jpeg']);
  assert.deepEqual(files.map(f => f.type), ['image/gif', 'video/mp4', 'video/quicktime', 'image/jpeg']);
});

test('every extension the room takes has a type', () => {
  for (const ext of ['gif', 'png', 'jpg', 'jpeg', 'webp', 'avif', 'bmp', 'mp4', 'webm', 'mov', 'm4v']) {
    assert.ok(MEDIA_TYPES[ext], ext);
    assert.ok(keepsMedia(`folder/thing.${ext.toUpperCase()}`), ext);
  }
  assert.equal(keepsMedia('readme.txt'), false);
  assert.equal(keepsMedia('nodots'), false);
});

test('a custom keep decides on the full path inside the archive', async () => {
  const seen = [];
  const files = await unzip(zipFile([{ name: 'in/a.gif', data: '1' }, { name: 'out/b.gif', data: '2' }]), {
    keep: name => {
      seen.push(name);
      return name.startsWith('in/');
    },
  });
  assert.deepEqual(names(files), ['a.gif']);
  assert.deepEqual(seen, ['in/a.gif', 'out/b.gif']);
});

test('isZipFile reads the type or falls back to the name', () => {
  assert.equal(isZipFile({ type: 'application/zip', name: 'nope' }), true);
  assert.equal(isZipFile({ type: 'application/x-zip-compressed', name: 'nope' }), true);
  assert.equal(isZipFile({ type: 'application/octet-stream', name: 'Pics.ZIP' }), true);
  assert.equal(isZipFile({ type: '', name: 'pics.zip' }), true);
  assert.equal(isZipFile({ type: 'image/gif', name: 'loop.gif' }), false);
  assert.equal(isZipFile({ name: 'zipper.gif' }), false);
  assert.equal(isZipFile(null), false);
});

test('a file with no directory record is not a zip', async () => {
  await assert.rejects(
    unzip(new File([new Uint8Array([0x47, 0x49, 0x46, 0x38, 0x39, 0x61])], 'a.zip')),
    /^Error: That is not a zip file$/,
  );
  await assert.rejects(unzip(new File([], 'empty.zip')), /That is not a zip file/);
});

test('a zip of nothing usable says so', async () => {
  await assert.rejects(
    unzip(zipFile([{ name: 'readme.txt', data: 'hi' }, { name: 'notes/', data: '' }])),
    /^Error: That zip has nothing the room can use$/,
  );
});

test('a scrambled entry loses itself, not its siblings', async () => {
  const files = await unzip(
    zipFile([
      { name: 'good.gif', data: 'GIF89a fine', method: 8 },
      { name: 'bad.gif', data: Buffer.from([0xff, 0xff, 0xff, 0xff]), method: 8, corrupt: true },
      { name: 'also.gif', data: 'GIF89a also fine', method: 8 },
    ]),
  );
  assert.deepEqual(names(files), ['good.gif', 'also.gif']);
  assert.equal(await textOf(files[1]), 'GIF89a also fine');
});

test('an entry compressed some other way is skipped', async () => {
  const files = await unzip(
    zipFile([{ name: 'lzma.gif', data: 'x', method: 14 }, { name: 'ok.gif', data: 'y' }]),
  );
  assert.deepEqual(names(files), ['ok.gif']);
});

test('a comment after the directory record does not hide it', async () => {
  const files = await unzip(
    zipFile([{ name: 'one.gif', data: 'GIF89a' }], { comment: 'made on a phone '.repeat(400) }),
  );
  assert.deepEqual(names(files), ['one.gif']);
});

test('names decode as utf8 with bit 11, and as latin1 without it', async () => {
  const wide = await unzip(zipFile([{ name: 'pics/ネコ loop.gif', data: '1', utf8: true }]));
  assert.equal(wide[0].name, 'ネコ loop.gif');

  const old = await unzip(zipFile([{ nameBytes: Buffer.from([0x63, 0x61, 0x66, 0xe9, 0x2e, 0x67, 0x69, 0x66]), data: '2' }]));
  assert.equal(old[0].name, 'café.gif');
});

test('ZIP64 is refused rather than half read', async () => {
  await assert.rejects(
    unzip(zipFile([{ name: 'a.gif', data: '1' }], { count: 0xffff })),
    /^Error: That zip is too big for the room$/,
  );
  await assert.rejects(
    unzip(zipFile([{ name: 'a.gif', data: '1', sizeOverride: 0xffffffff }])),
    /That zip is too big for the room/,
  );
});

test('a central record pointing nowhere drops that entry alone', async () => {
  const buf = makeZip([{ name: 'a.gif', data: 'one' }, { name: 'b.gif', data: 'two' }]);
  // point the first central record's local offset past the end of the file
  const cdAt = buf.readUInt32LE(buf.length - 6);
  buf.writeUInt32LE(0xfffffff, cdAt + 42);
  const files = await unzip(new File([new Uint8Array(buf)], 'a.zip'));
  assert.deepEqual(names(files), ['b.gif']);
});
