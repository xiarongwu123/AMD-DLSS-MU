import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const site = process.argv[2] || 'https://amd-dlss-mu.claude-api.cn';
const account = process.argv[3] || 'https://mu-api.claude-api.cn';
const source = JSON.parse(await readFile(new URL('../../server/Mu.Server/Data/Catalog/steam-requirements.json', import.meta.url)));
const mods = JSON.parse(await readFile(new URL('../../server/Mu.Server/Data/Catalog/optiscaler-compatibility.json', import.meta.url)));
assert.equal(source.coverage.pending, 0, 'Use the frozen, fully collected snapshot');
const modCoverage = new Set(mods.entries.filter(item => item.steamAppId).map(item => item.steamAppId)).size;
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
let checks = 0;
async function request(path, options = {}, origin = site) {
  return fetch(new URL(path, origin), { redirect: 'manual', signal: AbortSignal.timeout(20000), ...options,
    headers: { 'User-Agent': 'MU-Healthcheck/metadata-release', ...options.headers } });
}
async function json(path, origin = site) {
  const response = await request(path, {}, origin);
  assert.equal(response.status, 200, path);
  checks++;
  return response.json();
}
const listing = await json('/api/compatibility/games?page=1&pageSize=50');
assert.equal(listing.catalogTotal, source.coverage.catalogGames);
assert.equal(listing.total, source.coverage.catalogGames);
assert.deepEqual(listing.coverage, { requirements: source.coverage.available, modCompatibility: modCoverage });
assert.equal(listing.items.length, 50);
const second = await json('/api/compatibility/games?page=2&pageSize=50');
assert.equal(second.items.length, 50);
assert.equal(new Set([...listing.items, ...second.items].map(item => item.game.id)).size, 100);
const last = await json('/api/compatibility/games?page=' + Math.ceil(listing.total / 50) + '&pageSize=50');
assert.equal(last.items.length, (listing.total - 1) % 50 + 1);
for (const query of ['赛博朋克', '黑神话', '271590']) {
  const result = await json('/api/compatibility/games?q=' + encodeURIComponent(query));
  assert.ok(result.items.length > 0, query);
  assert.deepEqual(result.coverage, listing.coverage);
}
assert.equal((await json('/api/compatibility/games?q=__mu_metadata_no_matching_title__')).total, 0);
for (const [id, steamAppId] of Object.entries({ 'cyberpunk-2077': '1091500', 'black-myth-wukong': '2358720', 'gta-v-enhanced': '3240220', 'gta-v-legacy': '271590' })) {
  const detail = await json('/api/compatibility/games/' + id);
  assert.equal(detail.game.id, id);
  assert.equal(detail.game.steamAppId, steamAppId);
  const original = source.games.find(item => String(item.steamAppId) === detail.game.steamAppId);
  assert.equal(detail.requirements.sourceSha256, original.sourceSha256);
  assert.equal(detail.requirements.status, original.status);
  assert.equal(detail.requirements.minimum?.graphics, original.minimum?.graphics);
  assert.equal(detail.requirements.minimum?.processor, original.minimum?.processor);
  assert.ok(detail.modCompatibility.every(item => item.muVerified === false));
  if (id !== 'gta-v-legacy') assert.ok(detail.modCompatibility.length > 0);
  assert.equal(detail.counts.total, detail.counts.success + detail.counts.partial + detail.counts.failure);
}
const missingListing = await json('/api/compatibility/games?q=3028330');
assert.equal(missingListing.items.length, 1);
assert.equal(missingListing.items[0].game.steamAppId, '3028330');
const missing = await json('/api/compatibility/games/' + missingListing.items[0].game.id);
assert.equal(missing.game.steamAppId, '3028330');
assert.equal(missing.requirements.status, 'not_provided');
assert.equal(missing.requirements.minimum, null);
assert.equal(missing.requirements.recommended, null);
for (const asset of ['hardware-reference.json', 'hardware-check.js', 'compatibility.js', 'compatibility.css', 'app.js']) {
  const response = await request('/assets/' + asset + '?v=20260928-hardware');
  assert.equal(response.status, 200, asset);
  const live = Buffer.from(await response.arrayBuffer());
  assert.equal(digest(live), digest(await readFile(new URL('../public/assets/' + asset, import.meta.url))), asset);
  checks++;
}
const page = await request('/compatibility');
assert.equal(page.status, 200);
const html = await page.text();
assert.ok(html.includes('我的电脑能玩吗'));
assert.ok(!/开始测试|提交测试结果|帮助测试兼容性/.test(html));
checks++;
for (const path of ['/', '/download']) {
  const response = await request(path);
  assert.equal(response.status, 200, path);
  const text = await response.text();
  assert.ok(text.includes('2.0.1') && text.includes('正式版') && text.includes('大力喜鹊'), path);
  checks++;
}
assert.equal((await request('/api/v1/account/me', {}, account)).status, 401);
assert.equal((await request('/api/admin/overview')).status, 401);
checks += 2;
const release = await json('/release.json');
assert.equal(release.channel, 'stable');
assert.equal(release.version, '2.0.1');
assert.equal(release.size, 201095999);
assert.equal(release.sha256, 'f30315f9bdef2701c4a6d91c96e0dc43a6d8ad26152a01441e24ac0e9e26fd95');
const head = await request('/download/file', { method: 'HEAD' });
assert.equal(head.status, 200);
assert.equal(Number(head.headers.get('content-length')), release.size);
const range = await request('/download/file', { headers: { Range: 'bytes=0-1' } });
assert.equal(range.status, 206);
assert.equal(range.headers.get('content-range'), 'bytes 0-1/' + release.size);
assert.equal(Buffer.from(await range.arrayBuffer()).toString('ascii'), 'MZ');
const mirror = await request('/mirrors/magpie/v0.6.8-experimental.1/efb41e5177a628c0742566a887660a5a50e159f824cbfbf9f0620b2cc3b6803c/Magpie-Experimental-x64.zip', { method: 'HEAD' });
assert.equal(mirror.status, 200);
assert.equal(Number(mirror.headers.get('content-length')), 489787536);
checks += 3;
console.log(JSON.stringify({ site, checkedAt: new Date().toISOString(), checks, catalog: listing.catalogTotal,
  coverage: listing.coverage, sourceSha256: digest(await readFile(new URL('../../server/Mu.Server/Data/Catalog/steam-requirements.json', import.meta.url))),
  download: { channel: release.channel, size: release.size, sha256: release.sha256 }, preservedMagpieBytes: 489787536 }, null, 2));
