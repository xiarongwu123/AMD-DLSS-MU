import { readFile, writeFile, mkdir, stat } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

// Retain numeric observations only; tables from different test suites stay separate.
const specifications = [
  ['gpu-current', 'gpu', '2026 GPU / 1080p Ultra', 'gpu-hierarchy,4388.html', 0, 2],
  ['gpu-legacy', 'gpu', '2022–2024 GPU / 1080p Ultra', 'gpu-hierarchy,4388-2.html', 0, 1],
  ['gpu-legacy-medium', 'gpu', '2022–2024 GPU / 1080p Medium', 'gpu-hierarchy,4388-2.html', 0, 2],
  ['cpu-current', 'cpu', '2026 CPU / 1080p Gaming', 'cpu-hierarchy,4312.html', 0, 2],
  ['cpu-legacy-win11', 'cpu', '2020–2022 CPU / Windows 11 / 1080p', 'cpu-hierarchy,4312-2.html', 0, 1],
  ['cpu-legacy-win10', 'cpu', '2020–2022 CPU / Windows 10 / 1080p', 'cpu-hierarchy,4312-2.html', 1, 1],
];
const plain = html => html.replace(/<[^>]*>/g, ' ').replace(/&nbsp;|&#160;/g, ' ').replace(/&amp;/g, '&').replace(/\s+/g, ' ').trim();
const key = name => name.toLowerCase().replace(/[^a-z0-9]/g, '');
const memoryVariants = /^(?:GeForce (?:GTX (?:1050|1060)|RTX (?:2060|3050|3060|3080|4060 Ti|5060 Ti))|Radeon RX (?:550|560|570|580|5500 XT|6500 XT|9060 XT)|Intel Arc A770)$/i;
const tableRows = html => [...html.matchAll(/<tr\b[^>]*>([\s\S]*?)<\/tr>/gi)].map(row => [...row[1].matchAll(/<t[dh]\b[^>]*>([\s\S]*?)<\/t[dh]>/gi)].map(cell => plain(cell[1])));
const uniqueField = (rows, label) => {
  const matches = rows.filter(row => row[0] === label);
  if (matches.length !== 1 || matches[0].length !== 2) throw new Error('Missing or ambiguous specification field: ' + label);
  return matches[0][1];
};

export function parseNvidiaMemorySpecs(html) {
  const tables = [...html.matchAll(/<table\b[^>]*>[\s\S]*?<\/table>/gi)].map(match => tableRows(match[0]));
  const candidates = tables.filter(rows => rows[0]?.includes('GeForce RTX 5090'));
  if (candidates.length !== 1) throw new Error('Missing or ambiguous NVIDIA desktop 50-series table');
  const rows = candidates[0], headers = rows[0];
  const expected = ['GeForce RTX 5090', 'GeForce RTX 5080', 'GeForce RTX 5070 Ti', 'GeForce RTX 5070', 'GeForce RTX 5060 Ti', 'GeForce RTX 5060', 'GeForce RTX 5050'];
  if (headers[0] !== '' || headers.length !== expected.length + 1 || expected.some(name => headers.filter(header => header === name).length !== 1)) throw new Error('Unexpected NVIDIA desktop model columns');
  const memory = rows.filter(row => row[0] === 'Standard Memory Config');
  if (memory.length !== 1 || memory[0].length !== headers.length) throw new Error('Unexpected NVIDIA memory row');
  const result = new Map();
  for (let column = 1; column < headers.length; column++) {
    const value = memory[0][column];
    if (!/^\d+ GB(?: \/ \d+ GB)* GDDR[67]$/.test(value)) throw new Error('Ambiguous NVIDIA memory configuration: ' + headers[column]);
    const capacities = [...value.matchAll(/(\d+) GB/g)].map(match => Number(match[1]) * 1024);
    if (capacities.some(capacity => capacity <= 0 || capacity > 128 * 1024) || new Set(capacities).size !== capacities.length) throw new Error('Invalid NVIDIA memory capacity');
    result.set(headers[column], capacities);
  }
  return result;
}

export function parseIntelMemorySpecs(html, modelNumber) {
  const rows = tableRows(html);
  if (uniqueField(rows, 'Model Number') !== modelNumber || uniqueField(rows, 'Vertical Segment') !== 'Desktop') throw new Error('Unexpected Intel desktop identity');
  const memory = /^([1-9]\d*) GB GDDR6$/.exec(uniqueField(rows, 'Memory'));
  if (!memory) throw new Error('Ambiguous Intel memory capacity');
  return Number(memory[1]);
}

export function parseAmdMemorySpecs(html, name) {
  const rows = [...html.matchAll(/<dt\b[^>]*>([\s\S]*?)<\/dt>\s*<dd\b[^>]*>([\s\S]*?)<\/dd>/gi)].map(match => [plain(match[1]), plain(match[2])]);
  if (uniqueField(rows, 'Name').replace(/[®™]/g, '').replace(/^AMD /, '') !== name || uniqueField(rows, 'Board Type') !== 'Desktop') throw new Error('Unexpected AMD desktop identity');
  const memory = /^([1-9]\d*) GB$/.exec(uniqueField(rows, 'Max Memory Size'));
  if (!memory) throw new Error('Ambiguous AMD memory capacity');
  return Number(memory[1]);
}

export function gpuIdentity(name, specificationsText, cohort) {
  const namedMemory = name.match(/\b(\d+)\s*GB\b/i);
  const specifiedMemory = specificationsText?.match(/\b(\d+)\s*GB\b/i);
  if (namedMemory && specifiedMemory && namedMemory[1] !== specifiedMemory[1]) throw new Error('Conflicting memory specification: ' + name);
  let vramMb = Number(namedMemory?.[1] || specifiedMemory?.[1]) * 1024 || null;
  // This source row has an incorrect 8 GB specification; the manufacturer is read below.
  if (name === 'Radeon RX 5600 XT') vramMb = null;
  if (memoryVariants.test(name)) {
    // An older row with this display name cannot establish a newer row's memory variant.
    if (!vramMb) return null;
    name += ' ' + vramMb / 1024 + 'GB';
  }
  if (/^GeForce (?:GT 1030|GTX 1650)$/i.test(name)) {
    const type = specificationsText?.match(/\b(GDDR[56]|DDR4)\b/i)?.[1];
    if (!type) return null;
    name += ' ' + type.toUpperCase();
  }
  return { id: key(name), name, vramMb, ...(vramMb ? { memorySourceId: cohort } : {}) };
}

export function parseBenchmarkTable(html, { id, kind, tableIndex, scoreIndex }) {
  const table = [...html.matchAll(/<table\b[^>]*>[\s\S]*?<\/table>/gi)][tableIndex]?.[0];
  if (!table) throw new Error('Missing source table: ' + id);
  const rows = tableRows(table);
  const expectedHeader = kind === 'cpu' ? '1080p Gaming Score' : id === 'gpu-legacy-medium' ? '1080p Medium' : '1080p Ultra';
  if (rows[0]?.[scoreIndex] !== expectedHeader) throw new Error('Unexpected score column: ' + id);
  if (kind === 'gpu' && id.startsWith('gpu-legacy') && !rows[0]?.[5]?.startsWith('Specifications')) throw new Error('Unexpected memory column: ' + id);
  const observations = [];
  for (const cells of rows.slice(1)) {
    if (!/^\d+(?:\.\d+)?%/.test(cells[scoreIndex] || '')) continue;
    let name = cells[0].replace(/^\$\d+\s*-\s*/, '').replace(/\s*\(\$\d+\)$/, '').replace(/^(?:AMD |Intel )(?=Ryzen|Core)/, '').replace(/\s+DDR[45](?:\s*\/\s*DDR[45])?$/, '');
    // The source mislabels this SKU as Ryzen 7; omit instead of silently correcting it.
    if (name === 'Ryzen 7 7900X3D') continue;
    if (!/^(?:GeForce |Radeon |Intel Arc |Ryzen |Core )/.test(name) || /OC|PBO|overclock|@|ABT|\//i.test(name)) continue;
    name = name.replace(/\*$/, '');
    const scores = [...cells[scoreIndex].matchAll(/(\d+(?:\.\d+)?)%/g)].map(m => Number(m[1]));
    if (!scores.length || scores.some(score => score <= 0 || score > 200)) throw new Error('Invalid score: ' + name);
    const identity = kind === 'gpu' ? gpuIdentity(name, id.startsWith('gpu-legacy') ? cells[5] : null, id) : { id: key(name), name, vramMb: null };
    if (!identity) continue;
    observations.push({ ...identity, score: { min: Math.min(...scores), max: Math.max(...scores) } });
  }
  return observations;
}

export async function generate(directory, destination) {
const database = { schemaVersion: 1, generatedAt: new Date().toISOString(), method: 'separate-gaming-benchmark-cohorts', sources: [], gpu: [], cpu: [] };
const collected = { gpu: new Map(), cpu: new Map() };
for (const [id, kind, label, path, tableIndex, scoreIndex] of specifications) {
  const filename = id.startsWith('cpu-legacy') ? 'cpu-legacy.html' : id.startsWith('gpu-legacy') ? 'gpu-legacy.html' : id + '.html';
  const html = await readFile(resolve(directory, filename), 'utf8');
  const observations = parseBenchmarkTable(html, { id, kind, tableIndex, scoreIndex });
  let rows = 0;
  for (const observation of observations) {
    const { id: modelId, name, vramMb, memorySourceId, score } = observation;
    let model = collected[kind].get(modelId);
    if (!model) {
      model = { id: modelId, name, vramMb: null, scores: {} };
      collected[kind].set(modelId, model);
    }
    if (Object.hasOwn(model.scores, id)) throw new Error('Duplicate model in cohort: ' + name);
    model.scores[id] = score;
    if (vramMb != null) {
      if (model.vramMb != null && model.vramMb !== vramMb) throw new Error('Conflicting model memory across cohorts: ' + name);
      model.vramMb = vramMb;
      model.memorySourceId = memorySourceId;
    }
    ++rows;
  }
  if (rows < 15) throw new Error('Too few observations in ' + id);
  database.sources.push({ id, provider: "Tom's Hardware", label, url: 'https://www.tomshardware.com/reviews/' + path,
    retrievedAt: (await stat(resolve(directory, filename))).mtime.toISOString(), sha256: createHash('sha256').update(html).digest('hex'), observations: rows });
}
for (const [id, name, provider, url, expected] of [
  ['amd-rx9070xt', 'Radeon RX 9070 XT', 'AMD', 'https://www.amd.com/en/products/graphics/desktops/radeon/9000-series/amd-radeon-rx-9070xt.html', 16],
  ['amd-rx9070', 'Radeon RX 9070', 'AMD', 'https://www.amd.com/en/products/graphics/desktops/radeon/9000-series/amd-radeon-rx-9070.html', 16],
  ['amd-rx9070gre', 'Radeon RX 9070 GRE', 'AMD', 'https://www.amd.com/en/products/graphics/desktops/radeon/9000-series/amd-radeon-rx-9070-gre.html', 12],
  ['intel-arcb580', 'Intel Arc B580', 'Intel', 'https://www.intel.com/content/www/us/en/products/sku/241598/intel-arc-b580-graphics/specifications.html', 12],
  ['intel-arcb570', 'Intel Arc B570', 'Intel', 'https://www.intel.com/content/www/us/en/products/sku/241676/intel-arc-b570-graphics/specifications.html', 10],
  ['xfx-rx5600xt', 'Radeon RX 5600 XT', 'XFX', 'https://www.xfxforce.com/gpus/xfx-amd-radeon-tm-rx-5600-xt-6gb-gddr6-raw-ii', 6],
]) {
  const path = resolve(directory, id + '.html');
  const html = await readFile(path, 'utf8');
  const value = provider === 'AMD' ? parseAmdMemorySpecs(html, name) : provider === 'Intel' ? parseIntelMemorySpecs(html, name.split(' ').at(-1)) : plain(html).match(/RX 5600 XT\s+(\d+)GB/)?.[1];
  if (Number(value) !== expected) throw new Error('Unexpected manufacturer specification: ' + id);
  const model = collected.gpu.get(key(name));
  if (!model) throw new Error('Missing specification model: ' + name);
  if (model.vramMb != null && model.vramMb !== expected * 1024) throw new Error('Conflicting manufacturer memory: ' + name);
  model.vramMb = expected * 1024;
  model.memorySourceId = id;
  database.sources.push({ id, provider, label: name + ' / 显存规格', url, retrievedAt: (await stat(path)).mtime.toISOString(), sha256: createHash('sha256').update(html).digest('hex'), sourceFile: id + '.html' });
}
const nvidiaPath = resolve(directory, 'nvidia-geforce-compare.html');
const nvidiaHtml = await readFile(nvidiaPath, 'utf8');
const nvidiaMemory = parseNvidiaMemorySpecs(nvidiaHtml);
const nvidiaSourceId = 'nvidia-geforce-desktop-memory';
for (const [name, expected] of [['GeForce RTX 5090', 32], ['GeForce RTX 5080', 16], ['GeForce RTX 5070 Ti', 16], ['GeForce RTX 5070', 12], ['GeForce RTX 5060', 8], ['GeForce RTX 5050', 8]]) {
  const capacities = nvidiaMemory.get(name);
  if (capacities?.length !== 1 || capacities[0] !== expected * 1024) throw new Error('Unexpected NVIDIA memory variant: ' + name);
  const model = collected.gpu.get(key(name));
  if (!model || model.vramMb != null && model.vramMb !== capacities[0]) throw new Error('Missing or conflicting NVIDIA model: ' + name);
  model.vramMb = capacities[0];
  model.memorySourceId = nvidiaSourceId;
}
database.sources.push({ id: nvidiaSourceId, provider: 'NVIDIA', label: 'GeForce RTX 50 桌面系列 / 显存规格', url: 'https://www.nvidia.com/en-us/geforce/graphics-cards/compare/',
  retrievedAt: (await stat(nvidiaPath)).mtime.toISOString(), sha256: createHash('sha256').update(nvidiaHtml).digest('hex'), sourceFile: 'nvidia-geforce-compare.html' });
for (const kind of ['gpu', 'cpu']) database[kind] = [...collected[kind].values()].sort((a, b) => a.name.localeCompare(b.name, 'en'));
await mkdir(resolve(destination, '..'), { recursive: true });
await writeFile(destination, JSON.stringify(database, null, 2) + '\n');
console.log(JSON.stringify({ destination, gpuModels: database.gpu.length, cpuModels: database.cpu.length, sources: database.sources.map(s => ({ id: s.id, observations: s.observations })) }));
return database;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  await generate(resolve(process.argv[2] || '../.tools/hardware-reference-20260928'), resolve(process.argv[3] || 'website/public/assets/hardware-reference.json'));
}
