import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

const [stage, releaseFile] = process.argv.slice(2);
const origin = 'https://amd-dlss-mu.claude-api.cn';
const expected = JSON.parse(await readFile(releaseFile, 'utf8'));
const paths = (await readFile(join(stage, 'files.txt'), 'utf8')).trim().split('\n');
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const get = async (path, options) => fetch(origin + path, { signal: AbortSignal.timeout(30000), ...options });
let checked = 0;
for (let offset = 0; offset < paths.length; offset += 4) {
  await Promise.all(paths.slice(offset, offset + 4).map(async file => {
    const path = '/' + file.slice('public/'.length);
    const response = await get(path);
    assert.equal(response.status, 200, path);
    const body = Buffer.from(await response.arrayBuffer());
    if (path.endsWith('.html')) {
      assert.ok(body.includes(Buffer.from('apple-runtime.js')), path);
      assert.ok(!/<style\b|\sstyle=|<script>/.test(body.toString()), path);
    } else assert.equal(digest(body), digest(await readFile(join(stage, file))), path);
    checked++;
  }));
}
const admin = await get('/admin');
assert.equal(admin.status, 200);
assert.match(admin.headers.get('x-robots-tag'), /noindex/);
assert.equal((await get('/api/admin/overview')).status, 401);
const release = await (await get('/release.json')).json();
const update = await (await get('/api/updates/latest')).json();
for (const current of [release, update]) {
  assert.equal(current.tag, expected.tag);
  assert.equal(current.sha256, expected.sha256);
  assert.equal(current.size, expected.size);
}
const download = await get('/download/file', { headers: { Range: 'bytes=0-1' } });
assert.equal(download.status, 206);
assert.equal(await download.text(), 'MZ');
const video = await get('/assets/hero-motion.mp4?v=20261006logo', { headers: { Range: 'bytes=0-31' } });
assert.equal(video.status, 206);
assert.equal((await video.arrayBuffer()).byteLength, 32);
console.log(JSON.stringify({ staticFiles: checked, admin: 'unchanged files verified on host; public 200 and API 401',
  release: release.tag, sha256: release.sha256, downloadRange: 206, videoRange: 206 }, null, 2));
