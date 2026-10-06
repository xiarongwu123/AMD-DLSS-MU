import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { once } from 'node:events';
import { createMirrorHandler, mirrorPath } from './mirrors.mjs';

test('mirror serves verified original bytes, cached/ranged requests and rejects corrupt files', async () => {
  const root = await mkdtemp(join(tmpdir(), 'mu-mirror-'));
  const bytes = Buffer.from('PK verified upstream zip fixture');
  const asset = { tag: 'v1', name: 'test.zip', size: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') };
  const dir = join(root, 'mirrors', 'magpie', asset.tag);
  await mkdir(dir, { recursive: true });
  const file = join(dir, asset.name);
  await writeFile(file, bytes);
  const handler = createMirrorHandler(root, asset);
  const server = createServer(async (req, res) => { if (!await handler(req, res)) { res.writeHead(404); res.end(); } });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const url = `http://127.0.0.1:${server.address().port}${mirrorPath(asset)}`;
  try {
    const responses = await Promise.all([fetch(url), fetch(url)]);
    for (const response of responses) {
      assert.equal(response.status, 200);
      assert.deepEqual(Buffer.from(await response.arrayBuffer()), bytes);
      assert.match(response.headers.get('cache-control'), /public.*immutable/);
      assert.equal(response.headers.get('etag'), `"${asset.sha256}"`);
    }
    const head = await fetch(url, { method: 'HEAD' });
    assert.equal(head.status, 200); assert.equal(Number(head.headers.get('content-length')), bytes.length);
    for (const [range, expected] of [['bytes=0-1', bytes.subarray(0, 2)], ['bytes=-3', bytes.subarray(-3)], ['bytes=3-', bytes.subarray(3)]]) {
      const response = await fetch(url, { headers: { Range: range } });
      assert.equal(response.status, 206); assert.deepEqual(Buffer.from(await response.arrayBuffer()), expected);
    }
    for (const range of ['bytes=999-', 'bytes=-0', 'bytes=2-1', 'bytes=0-1,3-4']) {
      assert.equal((await fetch(url, { headers: { Range: range } })).status, 416);
    }
    const mismatch = await fetch(url, { headers: { Range: 'bytes=0-1', 'If-Range': '"old"' } });
    assert.equal(mismatch.status, 200); await mismatch.arrayBuffer();
    assert.equal((await fetch(url, { headers: { 'If-None-Match': `"${asset.sha256}"` } })).status, 304);
    assert.equal((await fetch(url.replace(asset.sha256, 'wrong'))).status, 404);
    assert.equal((await fetch(url, { method: 'POST' })).status, 405);
    await writeFile(file, Buffer.alloc(bytes.length));
    const corrupt = await fetch(url);
    assert.equal(corrupt.status, 503); assert.equal(corrupt.headers.get('cache-control'), 'no-store');
    await rm(file); assert.equal((await fetch(url)).status, 503);
  } finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); await rm(root, { recursive: true, force: true }); }
});
