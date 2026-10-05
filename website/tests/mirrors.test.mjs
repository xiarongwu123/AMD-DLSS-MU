import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { createMirrorHandler, mirrorPath, mirrorAssets, dlssInstaller, optiscaler, magpie } from '../mirrors.mjs';

test('reviewed component mirrors preserve the existing Magpie URL and independent origins', () => {
  assert.equal(mirrorAssets.length, 3);
  assert.ok(mirrorPath(magpie).startsWith('/mirrors/magpie/'));
  assert.ok(mirrorPath(dlssInstaller).startsWith('/mirrors/dlss-installer/v0.6.0/'));
  assert.ok(mirrorPath(optiscaler).startsWith('/mirrors/optiscaler/v0.9.4/'));
});

test('multiple verified mirrors serve exact bytes, ranges and HEAD; tampered files fail closed', async t => {
  const root = await mkdtemp(join(tmpdir(), 'mu-mirrors-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const bytes = Buffer.from('MZ reviewed component bytes');
  const assets = [dlssInstaller, optiscaler, magpie].map(a => ({ ...a, size: bytes.length,
    sha256: createHash('sha256').update(bytes).digest('hex') }));
  for (const asset of assets) {
    const directory = join(root, 'mirrors', asset.kind || 'magpie', asset.tag);
    await mkdir(directory, { recursive: true }); await writeFile(join(directory, asset.name), bytes);
  }
  const handler = createMirrorHandler(root, assets);
  const server = createServer((req, res) => handler(req, res).then(handled => {
    if (!handled) { res.writeHead(404); res.end(); }
  }).catch(error => res.destroy(error)));
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  t.after(() => new Promise(resolve => server.close(resolve)));
  const origin = `http://127.0.0.1:${server.address().port}`;
  for (const asset of assets) {
    const url = origin + mirrorPath(asset), etag = `"${asset.sha256}"`;
    const head = await fetch(url, { method: 'HEAD' });
    assert.equal(head.status, 200); assert.equal(head.headers.get('content-length'), String(bytes.length));
    assert.equal(head.headers.get('etag'), etag);
    assert.deepEqual(Buffer.from(await (await fetch(url)).arrayBuffer()), bytes);
    const part = await fetch(url, { headers: { Range: 'bytes=0-1', 'If-Range': etag } });
    assert.equal(part.status, 206); assert.equal(await part.text(), 'MZ');
    const suffix = await fetch(url, { headers: { Range: 'bytes=-5' } });
    assert.equal(suffix.status, 206); assert.equal(await suffix.text(), bytes.subarray(-5).toString());
    assert.equal((await fetch(url, { headers: { Range: 'bytes=999-' } })).status, 416);
    const changedRange = await fetch(url, { headers: { Range: 'bytes=0-1', 'If-Range': '"old"' } });
    assert.equal(changedRange.status, 200); await changedRange.arrayBuffer();
    assert.equal((await fetch(url, { headers: { 'If-None-Match': etag } })).status, 304);
    assert.equal((await fetch(url, { method: 'POST' })).status, 405);
    await writeFile(join(root, 'mirrors', asset.kind || 'magpie', asset.tag, asset.name), Buffer.alloc(bytes.length));
    assert.equal((await fetch(url)).status, 503);
  }
  assert.equal((await fetch(origin + '/mirrors/dlss-installer/unknown/file.exe')).status, 404);
});
