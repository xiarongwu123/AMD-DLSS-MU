import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { createCompatibilityProxy } from '../compatibility.mjs';

const game = { id: 'game-1', name: 'Local fixture game', steamAppId: '123', developer: null, engine: null };
const date = '2026-09-27T08:00:00+00:00';
const counts = { success: 1, partial: 1, failure: 0, total: 2 };
const summary = { game, counts, status: 'mixed', lastTestedAt: date };
const environment = {
  gpu: { name: 'AMD Radeon RX 7900 XT', vendor: 'AMD', vramMb: 20480, architecture: 'unknown', driverVersion: '32.0.1.2' },
  osVersion: 'Windows 11', systemDirectX: 'DirectX 12', gameVersion: '1.0', renderApi: 'DX12',
  dlssVersion: 'unknown', dlss5Version: '310.8.0.0', toolVersion: '2.0.0-preview.2', settings: 'unknown', otherMods: 'unknown'
};
const report = { id: 'test-1', gameId: game.id, tester: 'tester-0123456789ab', environment,
  result: 'success', failureReason: null, notes: null, createdAt: date };
const detail = { ...summary, gpus: [{ gpuName: environment.gpu.name, counts, status: 'mixed', lastTestedAt: date }], tests: [report] };
const search = { items: [summary] };
const json = (value, status = 200, headers = {}) => new Response(JSON.stringify(value), {
  status, headers: { 'Content-Type': 'application/json', ...headers }
});

async function invoke(handler, url, { method = 'GET', headers = {}, ip = '127.0.0.1' } = {}) {
  let status, responseHeaders, body;
  await handler({ url, method, headers, socket: { remoteAddress: ip } }, {
    destroyed: false, writableEnded: false,
    writeHead(code, value) { status = code; responseHeaders = value; },
    end(value) { body = value; }
  });
  return { status, headers: responseHeaders, body: JSON.parse(body) };
}

test('fixed upstream routes encode filters and strip incoming identity and extra response fields', async () => {
  const requests = [];
  const handler = createCompatibilityProxy({ fetchImpl: async (url, options) => {
    requests.push({ url, options });
    return json({ ...search, userEmail: 'must-not-leak@example.invalid', items: [{ ...summary, userId: 'private-id' }] });
  } });
  const response = await invoke(handler, '/api/compatibility/games?q=Game%20%26%20Beta&gpu=RX%207900%20XT', {
    headers: { cookie: 'session=private', authorization: 'Bearer private', 'x-forwarded-for': '198.51.100.7' }
  });
  assert.equal(response.status, 200);
  assert.equal(requests[0].url.href, 'https://mu-api.claude-api.cn/api/v1/compatibility/public/games?q=Game+%26+Beta&gpu=RX+7900+XT');
  assert.deepEqual(Object.keys(requests[0].options.headers).sort(), ['Accept', 'User-Agent']);
  assert.equal(requests[0].options.credentials, 'omit');
  assert.equal(requests[0].options.redirect, 'manual');
  assert.deepEqual(response.body, search);
  assert.equal(response.headers['Cache-Control'], 'no-store');
  assert.equal(response.headers['X-Content-Type-Options'], 'nosniff');
});

test('detail and GPU contracts remain structured and anonymous', async () => {
  const handler = createCompatibilityProxy({ fetchImpl: async url => json(url.pathname.endsWith('/gpus')
    ? { items: [environment.gpu.name] } : { ...detail, tests: [{ ...report, email: 'private@example.invalid', userId: 'private' }] }) });
  assert.deepEqual((await invoke(handler, '/api/compatibility/games/game-1?gpu=AMD%20Radeon%20RX%207900%20XT')).body, detail);
  assert.deepEqual((await invoke(handler, '/api/compatibility/gpus')).body, { items: [environment.gpu.name] });
});

test('catalog pagination preserves total counts, exact source identities and source dates', async () => {
  const catalog = { provider: 'Steam Store', url: 'https://store.steampowered.com/app/123/', retrievedAt: date,
    localizedName: '本地测试游戏', releaseDate: '2024-01-02', references: [{ provider: 'NVIDIA 官方 RTX 列表',
      url: 'https://www.nvidia.com/en-us/geforce/news/nvidia-rtx-games-engines-apps/', retrievedAt: date,
      matchedTitle: game.name, features: ['收录于官方列表；具体条件请查看来源'] }] };
  const paged = { items: [{ ...summary, game: { ...game, catalog } }], total: 1001, page: 1001, pageSize: 1, catalogTotal: 2001,
    coverage: { requirements: 1723, modCompatibility: 239 } };
  let requested;
  const handler = createCompatibilityProxy({ fetchImpl: async url => { requested = url.href; return json(paged); } });
  const result = await invoke(handler, '/api/compatibility/games?q=game&page=1001&pageSize=1');
  assert.equal(result.status, 200); assert.deepEqual(result.body, paged);
  assert.match(requested, /q=game&page=1001&pageSize=1$/);
  for (const input of [
    { ...paged, total: 1000 }, { ...paged, catalogTotal: 500 }, { ...paged, page: 0 }, { ...paged, pageSize: 51 },
    { ...paged, coverage: { requirements: 9999, modCompatibility: 239 } }, { ...paged, coverage: { requirements: 50, modCompatibility: -1 } },
    { ...paged, items: [{ ...summary, game: { ...game, catalog: { ...catalog, url: 'https://store.steampowered.com/app/999/' } } }] },
    { ...paged, items: [{ ...summary, game: { ...game, catalog: { ...catalog, url: 'javascript:alert(1)' } } }] }
  ]) {
    const invalid = createCompatibilityProxy({ fetchImpl: async () => json(input) });
    assert.equal((await invoke(invalid, '/api/compatibility/games')).status, 502);
  }
  let calls = 0;
  const guarded = createCompatibilityProxy({ fetchImpl: async () => { calls++; return json(search); } });
  for (const parameters of ['page=0', 'page=-1', 'page=1.1', 'page=1&page=2', 'page=', 'pageSize=0', 'pageSize=51', 'page=2147483647&pageSize=50'])
    assert.equal((await invoke(guarded, '/api/compatibility/games?' + parameters)).status, 400, parameters);
  assert.equal(calls, 0);
});

test('successful empty search and untested games remain distinct from unavailable service', async () => {
  const untested = { game, counts: { success: 0, partial: 0, failure: 0, total: 0 }, status: 'untested', lastTestedAt: null };
  const handler = createCompatibilityProxy({ fetchImpl: async url => json(url.searchParams.has('q') ? { items: [] } : { items: [untested] }) });
  const empty = await invoke(handler, '/api/compatibility/games?q=missing');
  assert.equal(empty.status, 200); assert.deepEqual(empty.body, { items: [] });
  const result = await invoke(handler, '/api/compatibility/games');
  assert.equal(result.status, 200); assert.deepEqual(result.body, { items: [untested] });
});

test('official requirements and upstream compatibility retain provenance without becoming MU reports', async () => {
  const tier = { text: 'Minimum:\nMemory: 8 GB RAM', os: 'Windows 10 64-bit', processor: null, memory: '8 GB RAM', graphics: null,
    directX: null, storage: null, additionalNotes: null, memoryMb: 8192, storageMb: null };
  const requirements = { provider: 'Steam Store', status: 'available', reason: null,
    sourceUrl: 'https://store.steampowered.com/api/appdetails?appids=123&cc=us&l=english', sourceRetrievedAt: date,
    sourceSha256: 'a'.repeat(64), minimum: tier, recommended: null };
  const commit = 'b'.repeat(40);
  const adaptation = { id: 'optiscaler-example', name: game.name, sourceProvider: 'OptiScaler project Wiki',
    sourceUrl: 'https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List/' + commit,
    sourceRetrievedAt: date, sourceCommit: commit, status: 'platform_limited', upscalerInputs: ['DLSS'],
    requiredMod: 'none', notes: ['Source condition: Windows only.'], testEnvironment: null, muVerified: false };
  const payload = { ...detail, requirements, modCompatibility: [adaptation] };
  const handler = createCompatibilityProxy({ fetchImpl: async () => json(payload) });
  const response = await invoke(handler, '/api/compatibility/games/game-1');
  assert.equal(response.status, 200);
  assert.deepEqual(response.body, payload);
  for (const bad of [
    { ...payload, requirements: { ...requirements, sourceUrl: requirements.sourceUrl.replace('123', '999') } },
    { ...payload, requirements: { ...requirements, status: 'unavailable' } },
    { ...payload, requirements: { ...requirements, sourceUrl: requirements.sourceUrl + '&appids=123' } },
    { ...payload, game: { ...game, steamAppId: null }, requirements: { ...requirements, sourceUrl: 'https://store.steampowered.com/app/null/' } },
    { ...payload, requirements: { ...requirements, minimum: { ...tier, text: null } } },
    { ...payload, modCompatibility: [{ ...adaptation, muVerified: true }] },
    { ...payload, modCompatibility: [{ ...adaptation, sourceUrl: adaptation.sourceUrl.replace(commit, 'c'.repeat(40)) }] },
    { ...payload, modCompatibility: [{ ...adaptation, sourceUrl: 'https://attacker.invalid/' + commit }] }
  ]) {
    const invalid = createCompatibilityProxy({ fetchImpl: async () => json(bad) });
    assert.equal((await invoke(invalid, '/api/compatibility/games/game-1')).status, 502);
  }
  const listed = { items: [{ ...summary, evidence: { hasRequirements: true, modStatus: 'working' } }] };
  const listing = createCompatibilityProxy({ fetchImpl: async () => json(listed) });
  assert.deepEqual((await invoke(listing, '/api/compatibility/games')).body, listed);
});

test('unknown duplicate long control-character parameters and URL injection do not reach upstream', async () => {
  let calls = 0;
  const handler = createCompatibilityProxy({ fetchImpl: async () => { calls++; return json(search); } });
  for (const path of [
    '/api/compatibility/games?url=https://attacker.invalid', '/api/compatibility/games?q=a&q=b',
    '/api/compatibility/games?gpu=a&gpu=b', `/api/compatibility/games?q=${'a'.repeat(201)}`,
    `/api/compatibility/games?gpu=${'a'.repeat(161)}`, '/api/compatibility/games?q=%00',
    '/api/compatibility/games?q=%0a', '/api/compatibility/gpus?q=a', '/api/compatibility/games/game-1?q=a'
  ]) assert.equal((await invoke(handler, path)).status, 400, path);
  for (const path of [
    '/api/compatibility/tests', '/api/compatibility/games/https://attacker.invalid',
    '/api/compatibility/games/%2F%2Fattacker.invalid', '/api/compatibility/games/%252fprivate',
    '/api/compatibility/games/%2e%2e/tests', `/api/compatibility/games/${'a'.repeat(65)}`
  ]) assert.equal((await invoke(handler, path)).status, 404, path);
  assert.equal(calls, 0);
  assert.throws(() => createCompatibilityProxy({ baseUrl: 'https://user:secret@example.invalid/' }), TypeError);
  assert.throws(() => createCompatibilityProxy({ baseUrl: 'file:///tmp/private' }), TypeError);
});

test('all mutations and HEAD are rejected without anonymous write or network access', async () => {
  let calls = 0;
  const handler = createCompatibilityProxy({ fetchImpl: async () => { calls++; return json(search); } });
  for (const method of ['POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS']) {
    const response = await invoke(handler, '/api/compatibility/tests', { method });
    assert.equal(response.status, 405);
    assert.equal(response.body.code, 'compatibility_read_only');
    assert.equal(response.headers.Allow, 'GET');
  }
  assert.equal(calls, 0);
});

test('404 403 429 and 503 errors stay distinct; upstream error details and headers are not exposed', async () => {
  for (const [status, code] of [[404, 'game_not_found'], [403, 'feature_disabled'], [429, 'rate_limited'], [503, 'database_unavailable']]) {
    let calls = 0;
    const handler = createCompatibilityProxy({ fetchImpl: async () => {
      calls++;
      return json({ code, message: 'PRIVATE backend failure', account: { email: 'private' } }, status,
        { 'Retry-After': '17', 'Set-Cookie': 'backend=secret', Server: 'internal' });
    } });
    const response = await invoke(handler, '/api/compatibility/games');
    assert.equal(response.status, status);
    assert.equal(response.body.code, status === 503 ? 'compatibility_unavailable' : code);
    assert.equal(JSON.stringify(response).includes('PRIVATE'), false);
    assert.equal(response.headers['Set-Cookie'], undefined);
    if (status === 429) { assert.equal(response.headers['Retry-After'], '17'); assert.equal(response.body.retryAfterSeconds, 17); }
    await invoke(handler, '/api/compatibility/games');
    assert.equal(calls, 2, 'errors must not be cached');
  }
});

test('bad JSON, unexpected structures, total/status mismatches and identified testers return 502', async () => {
  const responses = [
    () => new Response('<html>private</html>', { headers: { 'Content-Type': 'text/html' } }),
    () => new Response('{broken', { headers: { 'Content-Type': 'application/json' } }),
    () => json({ items: null }), () => json({ items: [] }, 503),
    () => json({ items: [{ ...summary, counts: { ...counts, total: 99 } }] }),
    () => json({ items: [{ ...summary, status: 'success' }] }),
    () => json({ items: [{ ...summary, lastTestedAt: null }] }),
    () => json({ items: [{ ...summary, game: { ...game, id: '../../private' } }] })
  ];
  for (const response of responses) {
    const handler = createCompatibilityProxy({ fetchImpl: async () => response() });
    const result = await invoke(handler, '/api/compatibility/games');
    assert.equal(result.status, 502);
    assert.equal(result.body.code, 'compatibility_bad_response');
    assert.equal(result.body.items, undefined, 'bad responses must not become empty successful data');
  }
  const handler = createCompatibilityProxy({ fetchImpl: async () => json({ ...detail, tests: [{ ...report, tester: 'email@example.invalid' }] }) });
  assert.equal((await invoke(handler, '/api/compatibility/games/game-1')).status, 502);
});

test('response limits apply to declared and streamed body size', async () => {
  const declared = createCompatibilityProxy({ maxResponseBytes: 64, fetchImpl: async () => json(search, 200, { 'Content-Length': '1000' }) });
  assert.equal((await invoke(declared, '/api/compatibility/games')).status, 502);
  const streamed = createCompatibilityProxy({ maxResponseBytes: 64, fetchImpl: async () => json({ items: [], padding: 'x'.repeat(100) }) });
  assert.equal((await invoke(streamed, '/api/compatibility/games')).status, 502);
});

test('upstream Retry-After accepts dates and clamps untrusted durations', async () => {
  const now = Date.UTC(2026, 8, 27, 8);
  for (const [header, expected] of [[new Date(now + 30000).toUTCString(), '30'], ['999999999', '3600'], ['0', '1']]) {
    const handler = createCompatibilityProxy({ now: () => now, fetchImpl: async () => json(
      { code: 'rate_limited', message: 'limited' }, 429, { 'Retry-After': header }) });
    const result = await invoke(handler, '/api/compatibility/games');
    assert.equal(result.status, 429); assert.equal(result.headers['Retry-After'], expected);
  }
});

test('timeout bounds fetch and body consumption; transport failure is 503', async () => {
  let signal;
  const timeout = createCompatibilityProxy({ timeoutMs: 25, fetchImpl: async (_, options) => {
    signal = options.signal;
    return new Promise(() => {});
  } });
  const keepAlive = setTimeout(() => {}, 1000);
  try {
    assert.equal((await invoke(timeout, '/api/compatibility/games')).status, 503);
    assert.equal(signal.aborted, true);
    const bodyTimeout = createCompatibilityProxy({ timeoutMs: 25, fetchImpl: async () => new Response(new ReadableStream({ start() {} }), { headers: { 'Content-Type': 'application/json' } }) });
    assert.equal((await invoke(bodyTimeout, '/api/compatibility/games')).status, 503);
  } finally { clearTimeout(keepAlive); }
  const offline = createCompatibilityProxy({ fetchImpl: async () => { throw new TypeError('offline'); } });
  assert.equal((await invoke(offline, '/api/compatibility/games')).status, 503);
});

test('per-IP rate limit ignores forged proxy headers and releases bounded slots after expiry', async () => {
  let time = 1000;
  const handler = createCompatibilityProxy({ rateLimit: 2, rateWindowMs: 1000, maxClients: 2, now: () => time, fetchImpl: async () => json(search) });
  assert.equal((await invoke(handler, '/api/compatibility/games', { headers: { 'cf-connecting-ip': '198.51.100.1' } })).status, 200);
  assert.equal((await invoke(handler, '/api/compatibility/games', { headers: { 'x-forwarded-for': '198.51.100.2' } })).status, 200);
  const limited = await invoke(handler, '/api/compatibility/games', { headers: { 'cf-connecting-ip': '198.51.100.3' } });
  assert.equal(limited.status, 429);
  assert.equal(limited.headers['Retry-After'], '1');
  assert.equal((await invoke(handler, '/api/compatibility/games', { ip: '198.51.100.4' })).status, 200);
  assert.equal((await invoke(handler, '/api/compatibility/games', { ip: '198.51.100.5' })).status, 429, 'new clients do not evict live limiter buckets');
  time += 1001;
  assert.equal((await invoke(handler, '/api/compatibility/games', { ip: '198.51.100.5' })).status, 200);
});

test('short successful cache coalesces requests, expires, and bounds key count and bytes', async () => {
  let time = 1000, calls = 0;
  const handler = createCompatibilityProxy({ now: () => time, maxCacheEntries: 2, cacheTtlMs: 100,
    fetchImpl: async () => { calls++; await new Promise(done => setTimeout(done, 10)); return json(search); } });
  const responses = await Promise.all(Array.from({ length: 5 }, () => invoke(handler, '/api/compatibility/games?q=a')));
  assert.ok(responses.every(response => response.status === 200));
  assert.equal(calls, 1);
  await invoke(handler, '/api/compatibility/games?q=a'); assert.equal(calls, 1);
  await invoke(handler, '/api/compatibility/games?q=b');
  await invoke(handler, '/api/compatibility/games?q=c');
  await invoke(handler, '/api/compatibility/games?q=a'); assert.equal(calls, 4, 'oldest key is evicted');
  time += 101;
  await invoke(handler, '/api/compatibility/games?q=a'); assert.equal(calls, 5, 'expired result is fetched again');
  let byteCalls = 0;
  const noRoom = createCompatibilityProxy({ maxCacheBytes: 1, fetchImpl: async () => { byteCalls++; return json(search); } });
  await invoke(noRoom, '/api/compatibility/games'); await invoke(noRoom, '/api/compatibility/games');
  assert.equal(byteCalls, 2, 'oversized cache entries are never retained');
});

test('distinct pending requests are bounded while the same key can still join the running request', async () => {
  let complete, calls = 0;
  const handler = createCompatibilityProxy({ maxPending: 1, fetchImpl: async () => {
    calls++;
    return new Promise(done => { complete = done; });
  } });
  const first = invoke(handler, '/api/compatibility/games?q=one');
  const joined = invoke(handler, '/api/compatibility/games?q=one');
  const overflow = await invoke(handler, '/api/compatibility/games?q=two');
  assert.equal(overflow.status, 503); assert.equal(calls, 1);
  complete(json(search));
  assert.equal((await first).status, 200); assert.equal((await joined).status, 200);
});

async function listen(server) {
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  return `http://127.0.0.1:${server.address().port}`;
}
async function close(server) { server.closeAllConnections(); await new Promise(done => server.close(done)); }

test('real local website and upstream servers verify routing, CSP, read-only access and no redirected credential forwarding', async t => {
  const requests = [];
  const upstream = createServer((req, res) => {
    requests.push({ url: req.url, headers: req.headers, method: req.method });
    const url = new URL(req.url, 'http://localhost');
    if (url.searchParams.get('q') === 'redirect') { res.writeHead(302, { Location: '/private' }); return res.end(); }
    if (url.searchParams.get('q') === 'disabled') { res.writeHead(403, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ code: 'feature_disabled', message: 'disabled' })); }
    if (url.searchParams.get('q') === 'limited') { res.writeHead(429, { 'Content-Type': 'application/json', 'Retry-After': '23' }); return res.end(JSON.stringify({ code: 'rate_limited', message: 'limited' })); }
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(url.pathname.endsWith('/gpus') ? { items: [environment.gpu.name] }
      : url.pathname.endsWith('/game-1') ? detail : search));
  });
  const upstreamBase = await listen(upstream);
  t.after(() => close(upstream));
  const reservation = createServer();
  const base = await listen(reservation);
  const port = reservation.address().port;
  await close(reservation);
  const temporary = await mkdtemp(join(tmpdir(), 'mu-compatibility-website-test-'));
  const publicRoot = join(temporary, 'public');
  await mkdir(publicRoot);
  await writeFile(join(publicRoot, 'compatibility.html'), '<!doctype html><html><head><title>Fixture</title></head><body>Compatibility route fixture</body></html>');
  const child = spawn(process.execPath, [resolve(import.meta.dirname, '../server.mjs')], { env: {
    ...process.env, HOST: '127.0.0.1', PORT: String(port), PUBLIC_ROOT: publicRoot, DATA_ROOT: join(temporary, 'data'),
    PUBLIC_ORIGIN: base, COMPATIBILITY_API_BASE: `${upstreamBase}/api/v1/compatibility/public/`,
    COMPATIBILITY_TRUST_CF_IP: '0', GITHUB_SYNC_DISABLED: '1'
  }, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  child.stdout.on('data', value => { output += value; });
  child.stderr.on('data', value => { output += value; });
  t.after(async () => {
    if (child.exitCode === null && child.signalCode === null) { const exit = once(child, 'exit'); child.kill('SIGTERM'); await exit; }
    await rm(temporary, { recursive: true, force: true });
  });
  let started = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    try { if ((await fetch(`${base}/api/health`)).ok) { started = true; break; } } catch {}
    await new Promise(done => setTimeout(done, 20));
  }
  assert.ok(started, output);
  const page = await fetch(`${base}/compatibility`);
  assert.equal(page.status, 200);
  assert.match(await page.text(), /Compatibility route fixture/u);
  assert.match(page.headers.get('content-security-policy'), /script-src 'self' 'sha256-[A-Za-z0-9+/=]+'; connect-src 'self'/u);
  assert.ok(!page.headers.get('content-security-policy').includes('unsafe-inline'));
  assert.equal(page.headers.get('x-frame-options'), 'DENY');

  const found = await fetch(`${base}/api/compatibility/games?q=Local%20%26%20fixture`, {
    headers: { Cookie: 'private=session', Authorization: 'Bearer private', 'X-Forwarded-For': '198.51.100.42' }
  });
  assert.equal(found.status, 200);
  assert.deepEqual(await found.json(), search);
  assert.equal(requests.at(-1).url, '/api/v1/compatibility/public/games?q=Local+%26+fixture');
  assert.equal(requests.at(-1).headers.cookie, undefined);
  assert.equal(requests.at(-1).headers.authorization, undefined);
  assert.equal(requests.at(-1).headers['x-forwarded-for'], undefined);
  assert.equal((await fetch(`${base}/api/compatibility/games/game-1`)).status, 200);
  assert.deepEqual(await (await fetch(`${base}/api/compatibility/gpus`)).json(), { items: [environment.gpu.name] });
  assert.equal((await fetch(`${base}/api/compatibility/games?q=disabled`)).status, 403);
  const limited = await fetch(`${base}/api/compatibility/games?q=limited`);
  assert.equal(limited.status, 429); assert.equal(limited.headers.get('retry-after'), '23');
  assert.equal((await fetch(`${base}/api/compatibility/games?q=redirect`)).status, 502);
  assert.equal(requests.some(request => request.url === '/private'), false, 'upstream redirects are never followed');
  const before = requests.length;
  assert.equal((await fetch(`${base}/api/compatibility/tests`, { method: 'POST', body: '{}' })).status, 405);
  assert.equal((await fetch(`${base}/api/compatibility/games`, { method: 'HEAD' })).status, 405);
  assert.equal((await fetch(`${base}/api/compatibility/games?url=http://127.0.0.1/private`)).status, 400);
  assert.equal(requests.length, before, 'rejected requests never reach upstream');
});
