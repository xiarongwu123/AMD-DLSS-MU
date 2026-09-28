import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { requirementsText, parseCapacityMb, parseRequirementTier, parseAppRequirements } from './collect-steam-requirements.mjs';

const minimum = '<strong>Minimum:</strong><br><ul><li><strong>OS *:</strong> Windows 10<br></li><li><strong>Processor:</strong> Core i7-6700 or Ryzen 5 1600</li><li><strong>Memory:</strong> 12 GB RAM</li><li><strong>Graphics:</strong> GeForce GTX 1060 6GB or Radeon RX 580 8GB</li><li><strong>DirectX:</strong> Version 12</li><li><strong>Storage:</strong> 70 GB available space</li><li><strong>Additional Notes:</strong> SSD required.</li></ul>';

test('publisher requirement labels and original wording survive safe text extraction', () => {
  const result = parseRequirementTier(minimum);
  assert.equal(result.os, 'Windows 10'); assert.equal(result.processor, 'Core i7-6700 or Ryzen 5 1600');
  assert.equal(result.graphics, 'GeForce GTX 1060 6GB or Radeon RX 580 8GB');
  assert.equal(result.memoryMb, 12288); assert.equal(result.storageMb, 71680);
  assert.equal(result.directX, 'Version 12'); assert.equal(result.additionalNotes, 'SSD required.');
  assert.ok(!result.text.includes('<')); assert.equal(parseRequirementTier(null), null);
});

test('unambiguous capacities normalize units without guessing between alternatives', () => {
  assert.equal(parseCapacityMb('512 MB RAM'), 512); assert.equal(parseCapacityMb('1.5 GB RAM'), 1536);
  assert.equal(parseCapacityMb('4 GB (8 GB recommended)'), null); assert.equal(parseCapacityMb('unknown'), null);
  assert.equal(parseCapacityMb('0 GB'), null);
  assert.equal(parseCapacityMb('8-16 GB'), null);
  assert.equal(parseCapacityMb('1,024 MB'), null);
  for (const source of ['1,650 MB', '2 048 MB RAM', '8 to 16 GB', '8/16 GB', '8 or 16 GB', '8 (or 16 GB)']) assert.equal(parseCapacityMb(source), null, source);
});

test('legacy publisher labels map only literal fields and placeholders stay absent', () => {
  const result = parseRequirementTier('<strong>Operating System:</strong> Windows XP<br>CPU: Pentium 4<br>RAM: 1 GB<br>Video Card: NVIDIA 6600<br>Hard Drive Space: 8GB<br>DirectX Version: 9.0c');
  assert.equal(result.os, 'Windows XP'); assert.equal(result.processor, 'Pentium 4'); assert.equal(result.memoryMb, 1024);
  assert.equal(result.graphics, 'NVIDIA 6600'); assert.equal(result.storageMb, 8192); assert.equal(result.directX, '9.0c');
  assert.equal(parseRequirementTier('<strong>Minimum:</strong><br>TBD'), null);
  assert.equal(parseRequirementTier('Recommended: To be determined'), null);
  assert.equal(parseRequirementTier('Memory: 2 TB').memoryMb, null);
  assert.throws(() => parseRequirementTier('Graphics: ' + 'x'.repeat(8193)), /requirements_too_large/);
});

test('script tags, attributes, encoded markup and comments are never emitted as executable HTML', () => {
  const text = requirementsText('<script>alert(1)</script><p onclick="attack()">CPU &amp; GPU</p><!-- private --><img src=x onerror=attack()>&lt;script&gt;attack()&lt;/script&gt;');
  assert.equal(text, 'CPU & GPU'); assert.equal(requirementsText('<strong>Minimum:</strong>'), null);
});

test('only a unique inner Steam AppID matches even when root map keys are wrong', () => {
  const raw = JSON.stringify({ '2441600': { success: true, data: { steam_appid: 1091500, name: 'Cyberpunk 2077', type: 'game', platforms: { windows: true }, pc_requirements: { minimum } } } });
  const parsed = parseAppRequirements(raw, '1091500');
  assert.equal(parsed.rootKeyMismatch, true); assert.equal(parsed.status, 'available');
  assert.equal(parsed.recommended, null);
  assert.throws(() => parseAppRequirements(raw, '2441600'), /identity_mismatch/u);
  const duplicate = JSON.parse(raw); duplicate.other = duplicate['2441600'];
  assert.throws(() => parseAppRequirements(JSON.stringify(duplicate), '1091500'), /ambiguous_identity/u);
});

test('absent publisher requirements stay distinct from invalid type or platform', () => {
  const data = { steam_appid: 220, name: 'Half-Life 2', type: 'game', platforms: { windows: true }, pc_requirements: [] };
  const pack = value => JSON.stringify({ '323140': { success: true, data: value } });
  assert.equal(parseAppRequirements(pack(data), '220').status, 'not_provided');
  assert.throws(() => parseAppRequirements(pack({ ...data, type: 'dlc' }), '220'), /not_a_game/u);
  assert.throws(() => parseAppRequirements(pack({ ...data, platforms: { windows: false } }), '220'), /not_a_windows_game/u);
});

test('offline CLI replay reparses verified source bytes and rejects tampered or missing responses', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'mu-requirements-'));
  const hash = bytes => createHash('sha256').update(bytes).digest('hex');
  try {
    await mkdir(join(directory, 'records')); await mkdir(join(directory, 'responses'));
    const catalog = JSON.stringify({ schemaVersion: 1, games: [{ steamAppId: '220', name: 'Half-Life 2' }] });
    const raw = JSON.stringify({ wrongRoot: { success: true, data: { steam_appid: 220, type: 'game', name: 'Half-Life 2', platforms: { windows: true }, pc_requirements: { minimum: 'Storage: 1,650 MB' } } } });
    await writeFile(join(directory, 'catalog.json'), catalog);
    await writeFile(join(directory, 'run.json'), JSON.stringify({ schemaVersion: 1, startedAt: '2026-01-01T00:00:00.000Z', catalogSha256: hash(catalog) }));
    await writeFile(join(directory, 'responses/source.json'), raw);
    await writeFile(join(directory, 'records/220.json'), JSON.stringify({ entry: { steamAppId: '220', name: 'Half-Life 2', status: 'available', reason: null,
      sourceUrl: 'https://store.steampowered.com/app/220/', sourceRetrievedAt: '2026-01-01T00:00:00.000Z', sourceSha256: hash(raw),
      minimum: { storageMb: 650 }, recommended: null }, provenance: { rawFile: 'responses/source.json', sha256: hash(raw) } }));
    const args = [fileURLToPath(new URL('./collect-steam-requirements.mjs', import.meta.url)), '--catalog', join(directory, 'catalog.json'), '--resume-dir', directory, '--replay-only', '--output', join(directory, 'out/steam-requirements.json')];
    const replay = () => spawnSync(process.execPath, args, { encoding: 'utf8', timeout: 10000 });
    const first = replay(); assert.equal(first.status, 0, first.stderr);
    const output = await readFile(join(directory, 'out/steam-requirements.json'), 'utf8');
    const parsed = JSON.parse(output); assert.equal(parsed.games[0].minimum.storageMb, null); assert.equal(parsed.games[0].minimum.storage, '1,650 MB');
    assert.equal(parsed.coverage.rootKeyMismatches, 1);
    assert.equal(replay().status, 0);
    assert.equal(await readFile(join(directory, 'out/steam-requirements.json'), 'utf8'), output);
    await writeFile(join(directory, 'responses/source.json'), raw + ' ');
    const tampered = replay(); assert.notEqual(tampered.status, 0); assert.match(tampered.stderr, /hash mismatch/);
    await rm(join(directory, 'responses/source.json'));
    const missing = replay(); assert.notEqual(missing.status, 0); assert.match(missing.stderr, /ENOENT/);
  } finally { await rm(directory, { recursive: true, force: true }); }
});
