import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createHash, scryptSync } from 'node:crypto';
import { readWindowsVersion, chunkSize, createUpdateStore } from '../updates.mjs';

function fixture(build = 1, size = 5 * 1024 ** 2) {
  const data = Buffer.alloc(size), pe = 128, optional = pe + 24, section = optional + 240, resource = 1024;
  data.writeUInt16LE(0x5a4d, 0); data.writeUInt32LE(pe, 60);
  data.writeUInt32LE(0x4550, pe); data.writeUInt16LE(0x8664, pe + 4);
  data.writeUInt16LE(1, pe + 6); data.writeUInt16LE(240, pe + 20); data.writeUInt16LE(0x22, pe + 22);
  data.writeUInt16LE(0x20b, optional); data.writeUInt32LE(0x1000, optional + 128); data.writeUInt32LE(512, optional + 132);
  data.writeUInt32LE(0x1000, section + 12); data.writeUInt32LE(512, section + 16); data.writeUInt32LE(resource, section + 20);
  for (let i = 0; i < 3; i++) {
    data.writeUInt16LE(1, resource + i * 24 + 14);
    data.writeUInt32LE(i === 0 ? 16 : 1, resource + i * 24 + 16);
    data.writeUInt32LE(i < 2 ? (0x80000000 + (i + 1) * 24) : 72, resource + i * 24 + 20);
  }
  data.writeUInt32LE(0x1000 + 96, resource + 72); data.writeUInt32LE(192, resource + 76);
  const value = resource + 96;
  data.write('VS_VERSION_INFO\0', value + 6, 'utf16le'); data.writeUInt32LE(0xfeef04bd, value + 40);
  data.writeUInt32LE(1 << 16, value + 48); data.writeUInt32LE(build << 16, value + 52);
  data.write('AMD-DLSS-MU', value + 100, 'utf16le');
  return data;
}
const hash = data => createHash('sha256').update(data).digest('hex');

test('publication rejects rollback and same-version replacement while permitting identical retries', () => {
  const current = { tag: 'v2.0.6', version: '2.0.6', sha256: 'a'.repeat(64) };
  const store = createUpdateStore('/unused', {}, { origin: 'https://example.test', current: () => current });
  assert.doesNotThrow(() => store.assertNew(current));
  assert.doesNotThrow(() => store.assertNew({ tag: 'v2.0.7', sha256: 'b'.repeat(64) }));
  assert.throws(() => store.assertNew({ tag: 'v2.0.6', sha256: 'b'.repeat(64) }));
  assert.throws(() => store.assertNew({ tag: 'v2.0.5', sha256: 'b'.repeat(64) }));
});

test('PE reader rejects non-EXE, wrong architecture, DLLs and malformed resource bounds', async t => {
  const root = await mkdtemp(join(tmpdir(), 'mu-pe-')); t.after(() => rm(root, { recursive: true, force: true }));
  const file = join(root, 'package.exe');
  await writeFile(file, fixture()); assert.equal(await readWindowsVersion(file), '1.0.1');
  for (const change of [b => b.writeUInt16LE(0, 0), b => b.writeUInt16LE(0x14c, 132), b => b.writeUInt16LE(0x2000, 150),
    b => b.writeUInt32LE(0xffffffff, 60), b => b.writeUInt32LE(0xffffff, 128 + 24 + 132), b => b.fill(0, 1024, 1536)]) {
    const bad = fixture(); change(bad); await writeFile(file, bad); await assert.rejects(() => readWindowsVersion(file));
  }
});

test('authenticated chunk upload, validation, atomic publication and immutable client download', async t => {
  const root = await mkdtemp(join(tmpdir(), 'mu-upload-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const oldBytes = fixture(0), old = { tag: 'v1.0.0', version: '1.0.0', file: 'AMD-DLSS-MU.exe', channel: 'stable', size: oldBytes.length, sha256: hash(oldBytes) };
  await mkdir(join(root, 'packages'));
  await writeFile(join(root, 'packages', `${old.tag}-${old.sha256}.exe`), oldBytes);
  await writeFile(join(root, 'release.json'), JSON.stringify(old));
  const origin = 'http://127.0.0.1:9999', password = 'fixture-password', salt = 'a'.repeat(32);
  const child = spawn(process.execPath, [new URL('../server.mjs', import.meta.url).pathname], { env: { ...process.env,
    PORT: '0', HOST: '127.0.0.1', PUBLIC_ORIGIN: origin, PUBLIC_ROOT: new URL('../public', import.meta.url).pathname,
    DATA_ROOT: root, GITHUB_SYNC_DISABLED: '1', ADMIN_PASSWORD_HASH: salt + ':' + scryptSync(password, Buffer.from(salt, 'hex'), 64).toString('hex') },
    stdio: ['ignore', 'pipe', 'pipe'] });
  t.after(async () => { const ended = once(child, 'exit'); child.kill(); await ended; });
  let log = '';
  const base = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(log)), 10000);
    child.on('error', reject); child.stderr.on('data', chunk => { log += chunk; });
    child.stdout.on('data', chunk => { log += chunk; const match = log.match(/listening on 127\.0\.0\.1:(\d+)/); if (match) { clearTimeout(timer); resolve(`http://127.0.0.1:${match[1]}`); } });
  });
  let cookie = '';
  const call = (path, method = 'GET', body, headers = {}) => fetch(base + path, { method, headers: {
    Origin: origin, Cookie: cookie, ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}), ...headers
  }, body: body === undefined ? undefined : Buffer.isBuffer(body) ? body : JSON.stringify(body) });
  const initial = await (await call('/api/updates/latest')).json();
  assert.equal(initial.tag, old.tag); assert.ok(initial.downloadUrl.includes(`/updates/${old.tag}/${old.sha256}/`));
  const input = { fileName: 'AMD-DLSS-MU.exe', size: fixture().length, notes: 'Fixed login <script>text only</script>' };
  assert.equal((await call('/api/admin/uploads', 'POST', input)).status, 401);
  const login = await call('/api/admin/login', 'POST', { password }); assert.equal(login.status, 200);
  cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
  assert.equal((await call('/api/admin/uploads', 'POST', input, { Origin: 'https://evil.example' })).status, 403);
  assert.equal((await call('/api/admin/uploads', 'POST', { ...input, fileName: '../package.exe' })).status, 400);
  assert.equal((await call('/api/admin/uploads', 'POST', { ...input, size: 1024 ** 3 + 1 })).status, 400);
  const created = await call('/api/admin/uploads', 'POST', input); assert.equal(created.status, 201);
  const upload = await created.json(), path = `/api/admin/uploads/${upload.id}`, bytes = fixture();
  assert.equal(upload.chunkSize, chunkSize);
  assert.equal((await call(path, 'GET', undefined, { Cookie: '' })).status, 401);
  const anotherLogin = await call('/api/admin/login', 'POST', { password });
  const anotherCookie = anotherLogin.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
  assert.equal((await call(path, 'GET', undefined, { Cookie: anotherCookie })).status, 400, 'upload belongs to its creating session');
  assert.equal((await call(path + '/complete', 'POST', {})).status, 400);
  assert.equal((await call(path + '/publish', 'POST', {})).status, 400);
  const put = (offset, body, headers) => call(path + `?offset=${offset}`, 'PUT', body, { 'Content-Type': 'application/octet-stream', ...headers });
  assert.equal((await put(0, bytes.subarray(0, 1024), { Origin: 'https://evil.example' })).status, 403);
  assert.equal((await put(10, bytes.subarray(0, 1024))).status, 400);
  assert.equal((await put(0, bytes.subarray(0, chunkSize + 1))).status, 400);
  assert.equal((await put(0, bytes.subarray(0, chunkSize))).status, 200);
  assert.equal((await (await call(path)).json()).offset, chunkSize);
  assert.equal((await put(0, bytes.subarray(0, 1024))).status, 400);
  assert.equal((await put(chunkSize, bytes.subarray(chunkSize))).status, 200);
  const complete = await call(path + '/complete', 'POST', {}); assert.equal(complete.status, 200);
  const candidate = (await complete.json()).release; assert.equal(candidate.sha256, hash(bytes)); assert.equal(candidate.tag, 'v1.0.1');
  assert.equal((await (await call(path + '/complete', 'POST', {})).json()).release.sha256, candidate.sha256);
  assert.equal((await (await call('/api/updates/latest')).json()).tag, old.tag, 'staging must not publish');
  assert.equal((await call(path + '/publish', 'POST', {})).status, 202);
  for (let i = 0; i < 100; i++) {
    const job = (await (await call('/api/admin/release-status')).json()).job;
    if (job.state !== 'running') { assert.equal(job.state, 'complete', job.message); break; }
    await new Promise(resolve => setTimeout(resolve, 10));
  }
  const latest = await (await call('/api/updates/latest')).json();
  assert.equal(latest.tag, candidate.tag); assert.equal(latest.notes, input.notes);
  const download = new URL(latest.downloadUrl).pathname;
  const full = await call(download); assert.equal(hash(Buffer.from(await full.arrayBuffer())), candidate.sha256);
  const partial = await call(download, 'GET', undefined, { Range: 'bytes=0-1' }); assert.equal(partial.status, 206); assert.equal(await partial.text(), 'MZ');
  assert.equal(Number((await call(download, 'HEAD')).headers.get('content-length')), bytes.length);
  assert.equal((await call(download, 'GET', undefined, { Range: `bytes=${bytes.length}-` })).status, 416);
  assert.equal(hash(Buffer.from(await (await call(new URL(initial.downloadUrl).pathname)).arrayBuffer())), old.sha256, 'old immutable URLs survive publication');
  assert.equal(JSON.parse(await readFile(join(root, 'release.json'), 'utf8')).tag, candidate.tag);
  const badUpload = await (await call('/api/admin/uploads', 'POST', { ...input, size: 1048576 })).json();
  const badPath = `/api/admin/uploads/${badUpload.id}`;
  assert.equal((await call(badPath + '?offset=0', 'PUT', Buffer.alloc(1048576), { 'Content-Type': 'application/octet-stream' })).status, 200);
  assert.equal((await call(badPath + '/complete', 'POST', {})).status, 400);
  assert.equal((await (await call('/api/updates/latest')).json()).tag, candidate.tag);
  assert.equal((await call(badPath, 'DELETE', undefined, { Origin: 'https://evil.example' })).status, 403);
  assert.equal((await call(badPath, 'DELETE')).status, 200);
  await writeFile(join(root, 'packages', `${candidate.tag}-${candidate.sha256}.exe`), Buffer.alloc(bytes.length));
  assert.equal((await call(download)).status, 503, 'tampered packages cannot be served');
  assert.equal((await call('/api/updates/latest')).status, 503, 'unavailable website package triggers client fallback');
});
