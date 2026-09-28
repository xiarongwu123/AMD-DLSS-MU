import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';

export const sourceUrl = 'https://www.nvidia.com/en-us/geforce/news/nvidia-rtx-games-engines-apps/';
export const dataUrl = 'https://www.nvidia.com/content/dam/en-zz/Solutions/geforce/news/nvidia-rtx-games-engines-apps/dlss-rt-games-apps-overrides.json';
export const referenceText = 'NVIDIA 官方功能事实；不代表 AMD 显卡或 AMD-DLSS-MU 兼容';

// Column meanings are different: NV,T in DLAA does not establish native DLAA.
const featureLabels = new Map([
  ['dlss super resolution', new Map([
    ['Yes', 'DLSS 超分：游戏原生支持（GeForce RTX）'],
    ['NV, T', 'DLSS 超分：原生支持，游戏内开启后可经 NVIDIA App 升级模型（GeForce RTX）'],
  ])],
  ['dlss frame generation', new Map([
    ['Yes', 'DLSS 帧生成：游戏原生支持（RTX 40/50）'],
    ['NV, U', 'DLSS 帧生成：原生支持，游戏内开启后可经 NVIDIA App 升级模型（RTX 40/50）'],
  ])],
  ['dlss multi frame generation', new Map([
    ['NV, 4X', 'DLSS 多帧生成：最高 4X（RTX 50）；原生或游戏内开启帧生成后经 NVIDIA App 覆盖'],
    ['NV, 6X', 'DLSS 动态多帧生成：最高 6X（RTX 50）；原生或游戏内开启帧生成后经 NVIDIA App 覆盖'],
  ])],
  ['dlss ray reconstruction', new Map([
    ['Yes', 'DLSS 光线重建：游戏原生支持（GeForce RTX）'],
    ['NV, T', 'DLSS 光线重建：原生支持，游戏内开启后可经 NVIDIA App 升级模型（GeForce RTX）'],
  ])],
  ['dlaa', new Map([
    ['Yes', 'DLAA：游戏原生支持（GeForce RTX）'],
    ['NV, T', 'DLAA：游戏内开启超分后可经 NVIDIA App 开启，不代表原生 DLAA 支持（GeForce RTX）'],
  ])],
  ['ray tracing', new Map([
    ['Yes', '光线追踪：原表列出支持'],
    ['Path Tracing', '路径追踪：原表列出支持'],
  ])],
]);

export function featuresForRow(row) {
  const features = [];
  for (const [field, labels] of featureLabels) {
    const marker = row[field];
    if (marker === undefined || marker === null || marker === '') continue;
    const label = labels.get(marker);
    if (!label) throw new Error(`Unreviewed NVIDIA feature marker in ${field}: ${String(marker)}`);
    features.push(label);
  }
  if (!features.length) features.push('原表未列出可解析的 DLSS / 光追功能');
  features.push(referenceText);
  if (features.length > 20 || features.some(value => value.length > 100)) throw new Error('Feature labels exceed the API contract.');
  return features;
}

export function normalizeTitle(value) {
  return value.replace(/[\u00ae\u2122]/gu, '').normalize('NFKC').toLowerCase()
    .replace(/[\u2018\u2019\u02bc]/gu, "'").replace(/[\u2010-\u2015\u2212]/gu, '-')
    .replace(/\s+/gu, ' ').trim();
}

function validTimestamp(value) {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/u.test(value)
    && Number.isFinite(Date.parse(value));
}

function indexByTitle(rows) {
  const result = new Map();
  for (const row of rows) {
    const title = normalizeTitle(row.name);
    if (!title) throw new Error('A source contains an empty normalized title.');
    const matches = result.get(title) || [];
    matches.push(row);
    result.set(title, matches);
  }
  return result;
}

export function matchReferences(catalog, source, retrievedAt, generatedAt = new Date().toISOString()) {
  if (catalog?.schemaVersion !== 1 || !Array.isArray(catalog.games) || !Array.isArray(source?.data))
    throw new Error('Unexpected catalog or NVIDIA source schema.');
  if (!validTimestamp(retrievedAt) || !validTimestamp(generatedAt)) throw new Error('Invalid source or generation timestamp.');
  const ids = new Set();
  for (const game of catalog.games) {
    if (typeof game?.name !== 'string' || !game.name.trim() || !/^\d{1,10}$/u.test(game.steamAppId || '')
      || Number(game.steamAppId) < 1 || Number(game.steamAppId) > 4294967295 || ids.has(String(game.steamAppId)))
      throw new Error('The Steam catalog contains an invalid or duplicate identity.');
    ids.add(String(game.steamAppId));
  }
  for (const row of source.data) {
    if (typeof row?.name !== 'string' || !row.name.trim() || !['Game', 'App'].includes(row.type))
      throw new Error('Unexpected NVIDIA source row.');
  }
  const steam = indexByTitle(catalog.games);
  const nvidia = indexByTitle(source.data.filter(row => row.type === 'Game'));
  const entries = [];
  const statistics = { steamGames: catalog.games.length, nvidiaGames: 0, excludedApps: 0, ambiguousTitles: 0, matched: 0 };
  statistics.nvidiaGames = source.data.filter(row => row.type === 'Game').length;
  statistics.excludedApps = source.data.length - statistics.nvidiaGames;
  for (const [title, games] of steam) {
    const officialRows = nvidia.get(title);
    if (!officialRows) continue;
    if (games.length !== 1 || officialRows.length !== 1) { statistics.ambiguousTitles++; continue; }
    entries.push({
      steamAppId: String(games[0].steamAppId),
      provider: 'NVIDIA 官方 RTX 列表',
      url: sourceUrl,
      retrievedAt,
      matchedTitle: officialRows[0].name,
      features: featuresForRow(officialRows[0]),
    });
  }
  entries.sort((left, right) => Number(left.steamAppId) - Number(right.steamAppId));
  statistics.matched = entries.length;
  return { document: { schemaVersion: 1, generatedAt, entries }, statistics };
}

export function verifySource(bytes, manifest) {
  const digest = createHash('sha256').update(bytes).digest('hex');
  if (manifest?.sourceUrl !== sourceUrl || manifest?.dataUrl !== dataUrl
    || manifest?.sha256 !== digest || !validTimestamp(manifest?.retrievedAt))
    throw new Error('NVIDIA audit manifest URL, timestamp, or source SHA-256 mismatch.');
}

async function main() {
  const { values } = parseArgs({ options: {
    catalog: { type: 'string', default: 'server/Mu.Server/Data/Catalog/steam-games.json' },
    source: { type: 'string' }, manifest: { type: 'string' },
    output: { type: 'string', default: 'server/Mu.Server/Data/Catalog/technology-references.json' },
    'generated-at': { type: 'string' },
  } });
  if (!values.source || !values.manifest) throw new Error('Supply --source and --manifest from the local audit snapshot; this script makes no network requests.');
  const catalog = JSON.parse(await readFile(resolve(values.catalog), 'utf8'));
  const bytes = await readFile(resolve(values.source));
  const manifest = JSON.parse(await readFile(resolve(values.manifest), 'utf8'));
  verifySource(bytes, manifest);
  const { document, statistics } = matchReferences(catalog, JSON.parse(bytes), manifest.retrievedAt, values['generated-at']);
  if (!document.entries.length) throw new Error('No unique exact matches; refusing to replace the reference file with an empty result.');
  const output = resolve(values.output);
  await mkdir(dirname(output), { recursive: true });
  await writeFile(output, JSON.stringify(document, null, 2) + '\n');
  console.log(JSON.stringify({ output, ...statistics }, null, 2));
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
