import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, existsSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { randomUUID, randomBytes, scryptSync, createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { once } from 'node:events';
import { createAnalytics } from '../analytics.mjs';

const root = resolve(import.meta.dirname, '..');

test('analytics deduplicates events, separates download requests from converted sessions, and retains feedback workflow', async t => {
  const data = mkdtempSync(join(tmpdir(), 'mu-analytics-test-'));
  const packageBytes = Buffer.from('MZ local analytics download fixture; not a runnable executable.\n');
  const release = {
    ...JSON.parse(readFileSync(join(root, 'public', 'release.json'), 'utf8')),
    size: packageBytes.length,
    sha256: createHash('sha256').update(packageBytes).digest('hex')
  };
  mkdirSync(join(data, 'packages'));
  writeFileSync(join(data, 'packages', `${release.tag}-${release.sha256}.exe`), packageBytes);
  writeFileSync(join(data, 'release.json'), JSON.stringify(release));
  const reservation = createServer();
  reservation.listen(0, '127.0.0.1');
  await once(reservation, 'listening');
  const port = reservation.address().port;
  await new Promise(resolveClose => reservation.close(resolveClose));
  const base = `http://127.0.0.1:${port}`;
  const password = randomBytes(20).toString('hex');
  const salt = randomBytes(16);
  const hash = `${salt.toString('hex')}:${scryptSync(password, salt, 64).toString('hex')}`;
  let child;
  let output = '';
  async function start() {
    child = spawn(process.execPath, [join(root, 'server.mjs')], { env: {
      ...process.env, HOST: '127.0.0.1', PORT: String(port), PUBLIC_ROOT: join(root, 'public'),
      DATA_ROOT: data, PUBLIC_ORIGIN: base, ADMIN_PASSWORD_HASH: hash, GITHUB_SYNC_DISABLED: '1'
    }, stdio: ['ignore', 'pipe', 'pipe'] });
    child.stdout.on('data', chunk => { output += chunk; });
    child.stderr.on('data', chunk => { output += chunk; });
    for (let retry = 0; retry < 100; retry += 1) {
      try { if ((await fetch(`${base}/api/health`)).ok) return; } catch { /* Wait for the bound socket. */ }
      await new Promise(done => setTimeout(done, 20));
    }
    throw new Error(`Server did not start: ${output}`);
  }
  async function stop() {
    const exited = once(child, 'exit');
    child.kill('SIGTERM');
    await exited;
  }
  t.after(async () => {
    if (child.exitCode === null && child.signalCode === null) await stop();
    rmSync(data, { recursive: true, force: true });
  });
  await start();
  const headers = { 'Content-Type': 'application/json', Origin: base, 'User-Agent': 'Mozilla/5.0 AcceptanceCheck' };
  const sid = randomUUID();
  const visit = { ...headers, Cookie: `mu_visit=${sid}` };
  const send = (path, payload, requestHeaders = headers) => fetch(`${base}${path}`, { method: 'POST', headers: requestHeaders, body: JSON.stringify(payload) });
  const event = { id: randomUUID(), name: 'page_view', page: '/', source: 'bilibili', campaign: 'release_130', password: 'never-store-this', at: 1 };

  assert.equal((await fetch(`${base}/api/admin/overview`)).status, 401);
  assert.equal((await send('/api/events', { events: [event] })).status, 204, 'no consent cookie means no browser collection');
  assert.equal((await send('/api/events', { events: [event] }, { ...visit, Origin: 'https://invalid.example' })).status, 403);
  assert.equal((await (await send('/api/events', { events: [event] }, visit)).json()).accepted, 1);
  assert.equal((await (await send('/api/events', { events: [event] }, visit)).json()).accepted, 0, 'retry is deduplicated');
  assert.equal((await (await send('/api/events', { events: [{ ...event, id: randomUUID(), name: 'release_published' }] }, visit)).json()).accepted, 0, 'browser cannot forge operational events');
  await send('/api/events', { events: [{ id: randomUUID(), name: 'session_heartbeat', page: '/' }] }, visit);
  for (let index = 0; index < 2; index += 1) {
    const response = await fetch(`${base}/download/file?entry=download_card`, { headers: visit, redirect: 'manual' });
    assert.equal(response.status, 200, 'verified local package is served directly');
    assert.equal(response.headers.get('location'), null, 'download no longer redirects to GitHub');
    assert.equal(response.headers.get('content-type'), 'application/vnd.microsoft.portable-executable');
    assert.equal(response.headers.get('content-disposition'), 'attachment; filename="AMD-DLSS-MU.exe"');
    assert.equal(response.headers.get('content-length'), String(packageBytes.length));
    assert.equal(response.headers.get('etag'), `"${release.sha256}"`);
    assert.deepEqual(Buffer.from(await response.arrayBuffer()), packageBytes);
  }
  const anonymous = await fetch(`${base}/download/file`, { headers, redirect: 'manual' });
  assert.equal(anonymous.status, 200);
  assert.deepEqual(Buffer.from(await anonymous.arrayBuffer()), packageBytes);
  const head = await fetch(`${base}/download/file`, { method: 'HEAD', headers: visit, redirect: 'manual' });
  assert.equal(head.status, 200);
  assert.equal((await head.arrayBuffer()).byteLength, 0, 'HEAD checks do not start or count downloads');
  const resumed = await fetch(`${base}/download/file`, { headers: { ...visit, Range: 'bytes=8-' }, redirect: 'manual' });
  assert.equal(resumed.status, 206);
  assert.equal(resumed.headers.get('content-range'), `bytes 8-${packageBytes.length - 1}/${packageBytes.length}`);
  assert.deepEqual(Buffer.from(await resumed.arrayBuffer()), packageBytes.subarray(8), 'nonzero resume does not count a second download');
  for (const excludedHeaders of [
    { ...visit, 'User-Agent': 'testbot' }, { ...visit, Cookie: `${visit.Cookie}; mu_metrics_exclude=1` }
  ]) {
    const excluded = await fetch(`${base}/download/file`, { headers: excludedHeaders, redirect: 'manual' });
    assert.equal(excluded.status, 200);
    assert.deepEqual(Buffer.from(await excluded.arrayBuffer()), packageBytes);
  }

  const login = await send('/api/admin/login', { password });
  assert.equal(login.status, 200);
  const cookie = login.headers.getSetCookie().map(item => item.split(';')[0]).join('; ');
  assert.match(cookie, /mu_metrics_exclude=1/);
  const admin = { ...headers, Cookie: cookie };
  const overview = async () => (await fetch(`${base}/api/admin/overview?days=7`, { headers: admin })).json();
  let metrics = await overview();
  assert.equal(metrics.totals.views, 1);
  assert.equal(metrics.totals.sessions, 1);
  assert.equal(metrics.totals.active, 1);
  assert.equal(metrics.totals.downloads, 3);
  assert.equal(metrics.totals.converted, 1);
  assert.equal(metrics.totals.conversionRate, 1);
  assert.equal(metrics.sources[0].source, 'bilibili');
  assert.equal(metrics.trend.filter(day => !day.collecting).length, 6);
  assert.equal(metrics.github.total, null, 'unavailable GitHub count is not shown as zero');
  assert.ok(!JSON.stringify(metrics).includes('never-store-this'));

  assert.equal((await send('/api/feedback', { details: 'invalid' }, visit)).status, 400);
  const report = { category: 'compatibility', appVersion: '1.3.0', game: 'Local test game', gpu: 'Test GPU', mode: '2',
    details: '<img src=x onerror=alert(1)> Local-only acceptance fixture.', contact: 'local-test@example.invalid', privacyConfirmed: true };
  const accepted = await send('/api/feedback', report, visit);
  assert.equal(accepted.status, 201);
  const id = (await accepted.json()).id;
  metrics = await overview();
  assert.equal(metrics.totals.feedback, 1);
  assert.equal(metrics.totals.pending, 1);
  assert.equal(metrics.totals.feedbackRejected, 1);
  assert.ok(!JSON.stringify(metrics).includes('local-test@example.invalid'), 'big screen never includes contact or report body');
  assert.equal((await send('/api/admin/feedback/status', { id, status: 'resolved' }, headers)).status, 401);
  assert.equal((await send('/api/admin/feedback/status', { id, status: 'resolved' }, { ...admin, Origin: 'https://invalid.example' })).status, 403);
  assert.equal((await send('/api/admin/feedback/status', { id, status: 'resolved' }, admin)).status, 200);
  assert.equal((await overview()).totals.pending, 0);
  assert.equal((await send('/api/admin/feedback/status', { id, status: 'invented' }, admin)).status, 400);
  assert.ok(existsSync(join(data, 'feedback.jsonl')));
  assert.equal(readFileSync(join(data, 'feedback.jsonl'), 'utf8').trim().split('\n').length, 1);
  const list = await (await fetch(`${base}/api/admin/feedback?status=resolved`, { headers: admin })).json();
  assert.equal(list.items[0].payload.contact, report.contact);
  assert.equal(list.items[0].status, 'resolved');
  assert.equal((await fetch(`${base}/data/analytics.sqlite`)).status, 404);

  await stop();
  await start();
  const again = await send('/api/admin/login', { password });
  const nextCookie = again.headers.getSetCookie().map(item => item.split(';')[0]).join('; ');
  const persisted = await (await fetch(`${base}/api/admin/overview`, { headers: { ...headers, Cookie: nextCookie } })).json();
  assert.equal(persisted.totals.downloads, 3);
  assert.equal(persisted.totals.feedback, 1);
  assert.equal(persisted.totals.pending, 0, 'feedback reimport must preserve resolved status');
  assert.equal(persisted.totals.sessions, 1);
});

test('GitHub totals and source sessions remain distinct; expired event data is pruned', () => {
  const data = mkdtempSync(join(tmpdir(), 'mu-analytics-store-'));
  const analytics = createAnalytics(data);
  try {
    analytics.saveGithub([{ id: 1, tag: 'v1.3.0', downloads: 132, publishedAt: '2026-09-16T14:30:16Z' }]);
    analytics.record('download_redirect', { tag: 'v1.3.0', at: Date.now() - 91 * 86400000 });
    analytics.record('download_redirect', { tag: 'v1.3.0' });
    analytics.prune();
    const data = analytics.overview(90);
    assert.equal(data.github.total, 132);
    assert.equal(data.totals.downloads, 1);
    assert.equal(data.totals.conversionRate, null, 'no consented visits means conversion is unknown');
    assert.equal(data.totals.sessions, 0);
    analytics.githubError();
    assert.equal(analytics.overview().github.total, 132, 'upstream error retains last known count');
    assert.ok(analytics.overview().github.error);
  } finally { analytics.close(); rmSync(data, { recursive: true, force: true }); }
});
