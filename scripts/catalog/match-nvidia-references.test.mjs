import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { dataUrl, featuresForRow, matchReferences, normalizeTitle, referenceText, sourceUrl, verifySource } from './match-nvidia-references.mjs';

const retrievedAt = '2026-09-27T15:38:26.179Z';
const catalog = games => ({ schemaVersion: 1, games });
const game = (steamAppId, name) => ({ steamAppId, name });
const source = rows => ({ data: rows.map(([name, type = 'Game']) => ({ name, type, 'dlss super resolution': 'NV, T' })) });

test('normalization changes typography only and preserves editions and punctuation', () => {
  assert.equal(normalizeTitle('  MARVEL’S Spider-Man\u2122 Remastered  '), "marvel's spider-man remastered");
  assert.equal(normalizeTitle('Game\u00ae \u2013 Edition'), 'game - edition');
  assert.notEqual(normalizeTitle('Game: Edition'), normalizeTitle('Game Edition'));
  assert.notEqual(normalizeTitle('Game Remastered'), normalizeTitle('Game'));
});

test('unique complete names retain source identity and column-specific facts', () => {
  const result = matchReferences(catalog([game('1', 'Game\u2122'), game('2', 'Game Enhanced'), game('3', 'Editor')]),
    source([['Game'], ['Editor', 'App'], ['Game Remastered']]), retrievedAt, retrievedAt);
  assert.deepEqual(result.document, { schemaVersion: 1, generatedAt: retrievedAt, entries: [{
    steamAppId: '1', provider: 'NVIDIA 官方 RTX 列表', url: sourceUrl, retrievedAt,
    matchedTitle: 'Game', features: ['DLSS 超分：原生支持，游戏内开启后可经 NVIDIA App 升级模型（GeForce RTX）', referenceText],
  }] });
  assert.equal(result.statistics.excludedApps, 1);
});

test('native features retain RTX generation conditions without claiming MU support', () => {
  const facts = featuresForRow({ 'dlss super resolution': 'Yes', 'dlss frame generation': 'Yes',
    'dlss ray reconstruction': 'Yes', dlaa: 'Yes', 'ray tracing': 'Yes' });
  assert.ok(facts.includes('DLSS 超分：游戏原生支持（GeForce RTX）'));
  assert.ok(facts.includes('DLSS 帧生成：游戏原生支持（RTX 40/50）'));
  assert.ok(facts.includes('DLSS 光线重建：游戏原生支持（GeForce RTX）'));
  assert.ok(facts.includes('DLAA：游戏原生支持（GeForce RTX）'));
  assert.ok(facts.includes('光线追踪：原表列出支持'));
  assert.equal(facts.at(-1), referenceText);
  assert.ok(!facts.join(' ').includes('DLSS 5'));
});

test('NV,T distinguishes native model upgrades from optional DLAA activation', () => {
  const facts = featuresForRow({ 'dlss super resolution': 'NV, T', 'dlss ray reconstruction': 'NV, T', dlaa: 'NV, T' });
  assert.match(facts[0], /超分：原生支持.*NVIDIA App 升级模型/u);
  assert.match(facts[1], /光线重建：原生支持.*NVIDIA App 升级模型/u);
  assert.match(facts[2], /DLAA：游戏内开启超分后可经 NVIDIA App 开启，不代表原生 DLAA 支持/u);
});

test('frame generation overrides preserve native-or-override and input conditions', () => {
  for (const multiplier of ['4X', '6X']) {
    const facts = featuresForRow({ 'dlss frame generation': 'NV, U', 'dlss multi frame generation': `NV, ${multiplier}` });
    assert.match(facts[0], /原生支持，游戏内开启后可经 NVIDIA App 升级模型（RTX 40\/50）/u);
    assert.ok(facts[1].includes(`最高 ${multiplier}（RTX 50）`));
    assert.ok(facts[1].includes('原生或游戏内开启帧生成后经 NVIDIA App 覆盖'));
    assert.equal(facts[1].includes('动态'), multiplier === '6X');
    assert.ok(!facts[1].includes('DLSS 5'));
  }
});

test('blank cells are not converted to unsupported and unknown markers fail closed', () => {
  assert.deepEqual(featuresForRow({ 'dlss super resolution': '', 'ray tracing': null }),
    ['原表未列出可解析的 DLSS / 光追功能', referenceText]);
  assert.deepEqual(featuresForRow({ 'ray tracing': 'Path Tracing' }), ['路径追踪：原表列出支持', referenceText]);
  for (const [field, marker] of [['dlss super resolution', 'NV, U'], ['dlaa', 'NV, 4X'], ['ray tracing', 'Future'],
    ['dlss multi frame generation', 'NV, 8X'], ['dlss frame generation', true]])
    assert.throws(() => featuresForRow({ [field]: marker }), /Unreviewed NVIDIA feature marker/u);
});

test('duplicate normalized names on either side are excluded without fuzzy matching', () => {
  const result = matchReferences(catalog([game('1', 'Same'), game('2', 'SAME\u2122'), game('3', 'Other'), game('4', 'Unique')]),
    source([['Same'], ['Other'], ['Other\u00ae'], ['Unique'], ['Unique DLC']]), retrievedAt);
  assert.deepEqual(result.document.entries.map(row => row.steamAppId), ['4']);
  assert.equal(result.statistics.ambiguousTitles, 2);
});

test('invalid identities, schema, source types and timestamps fail before output', () => {
  assert.throws(() => matchReferences(catalog([game('1', 'Same'), game('1', 'Other')]), source([]), retrievedAt));
  assert.throws(() => matchReferences(catalog([game('0', 'Same')]), source([]), retrievedAt));
  assert.throws(() => matchReferences(catalog([]), source([['Same', 'Unknown']]), retrievedAt));
  assert.throws(() => matchReferences(catalog([]), { data: [] }, 'bad date'));
  assert.throws(() => matchReferences({ games: [] }, source([]), retrievedAt));
});

test('audit manifest must match source bytes and the exact official URLs', () => {
  const bytes = Buffer.from(JSON.stringify(source([['Game']])));
  const manifest = { sourceUrl, dataUrl, retrievedAt, sha256: createHash('sha256').update(bytes).digest('hex') };
  assert.doesNotThrow(() => verifySource(bytes, manifest));
  assert.throws(() => verifySource(Buffer.concat([bytes, Buffer.from(' ')]), manifest));
  assert.throws(() => verifySource(bytes, { ...manifest, sourceUrl: 'https://example.invalid/' }));
});
