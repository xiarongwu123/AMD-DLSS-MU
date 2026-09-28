import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import '../public/assets/hardware-check.js';

const api = globalThis.MuHardwareCheck;
const database = JSON.parse(await readFile(new URL('../public/assets/hardware-reference.json', import.meta.url)));
const model = (name, kind = 'gpu') => api.matchHardware(name, database[kind]);
const tier = { graphics: 'NVIDIA GeForce GTX 1060 6GB / AMD Radeon RX 580 8GB', processor: 'Intel Core i7-9700K or AMD Ryzen 5 3600', memoryMb: 16384 };

test('hardware variants require exact desktop identities', () => {
  assert.equal(model('AMD Radeon RX 7900XT').name, 'Radeon RX 7900 XT');
  assert.equal(model('RX 7900 XTX').name, 'Radeon RX 7900 XTX');
  assert.equal(model('RTX 4070 laptop'), null);
  assert.equal(model('RTX 4070M'), null);
  assert.equal(model('RX 9060 XT'), null);
  assert.equal(model('GTX 1060'), null);
  assert.equal(model('RX 9060 XT 8GB').vramMb, 8192);
  assert.equal(model('RX 9060 XT 16GB').vramMb, 16384);
  assert.equal(model('RTX 2060 (6GB)').name, 'GeForce RTX 2060 6GB');
  assert.equal(model('RTX 2060'), null);
  assert.equal(model('RTX 3050'), null);
  assert.equal(model('RTX 3050 6GB'), null);
  assert.equal(model('RTX 3050 8GB').vramMb, 8192);
  assert.equal(model('GTX 1650'), null);
  assert.notEqual(model('GTX 1650 GDDR5').id, model('GTX 1650 GDDR6').id);
  for (const kind of ['gpu', 'cpu']) for (const item of database[kind]) assert.equal(model(item.name, kind)?.id, item.id, item.name + ' must be selectable');
  assert.equal(database.gpu.filter(item => /RTX 3060(?: |$)/.test(item.name) && !/Ti/.test(item.name)).length, 1);
  assert.ok(model('RTX 3060 12GB').scores['gpu-current']);
  assert.ok(model('RTX 3060 12GB').scores['gpu-legacy']);
});

test('user shorthand resolves only a unique complete model, never an ambiguous memory variant', () => {
  for (const [short, full, kind] of [['7900XT', 'RX 7900 XT', 'gpu'], ['9070XT', 'RX 9070 XT', 'gpu'], ['B580', 'Arc B580', 'gpu'], ['7800X3D', 'Ryzen 7 7800X3D', 'cpu']]) {
    assert.equal(model(short, kind)?.id, model(full, kind).id, short);
  }
  for (const short of ['2060', '3050', '3060', '3080', '4060Ti', '9060XT', '7900X', 'RTX 7900XT', '7800X3D']) assert.equal(model(short), null, short);
  assert.equal(model('7800X', 'cpu'), null);
  const duplicate = { ...model('RX 7900 XT'), id: 'other-model', name: 'GeForce RTX 7900 XT' };
  assert.equal(api.matchHardware('7900XT', [...database.gpu, duplicate]), null);
  assert.equal(api.requirementsModels('7900XT', database.gpu).length, 0);
  assert.equal(api.requirementsModels('7800X3D', database.cpu).length, 0);
});

test('published requirement parsing keeps Ti, XTX and memory distinctions', () => {
  const names = api.requirementsModels('RTX 4070 Ti SUPER / RX 7900 XTX', database.gpu).map(m => m.name);
  assert.deepEqual(names, ['GeForce RTX 4070 Ti Super', 'Radeon RX 7900 XTX']);
  assert.equal(api.requirementsModels('RTX 4070 laptop', database.gpu).length, 0);
  assert.equal(api.requirementsModels(tier.graphics, database.gpu).length, 2);
  assert.equal(api.requirementsModels('AMD Ryzen 5 5600X / Intel Core i7-9700K', database.cpu).length, 2);
  assert.equal(api.requirementsModels('Radeon R9 390X', database.gpu).length, 0);
  assert.equal(api.requirementsModels('RTX 4070 TiM', database.gpu).length, 0);
  assert.equal(api.requirementsModels('Radeon RX 7900 XTE', database.gpu).length, 0);
});

test('publisher typography and separated suffixes retain the complete model identity', () => {
  for (const text of ['AMD Radeon™ RX6800 -XT', 'RX 6800–XT', 'RX-6800 XT']) {
    assert.deepEqual(api.requirementsModels(text, database.gpu).map(item => item.name), ['Radeon RX 6800 XT'], text);
  }
  assert.deepEqual(api.requirementsModels('NVIDIA® GeForce®RTX 4070-Ti-SUPER', database.gpu).map(item => item.name), ['GeForce RTX 4070 Ti Super']);
  assert.deepEqual(api.requirementsModels('AMD Ryzen™ 5 3600 XT', database.cpu).map(item => item.name), ['Ryzen 5 3600XT']);
  assert.deepEqual(api.requirementsModels('Intel® Core™ i7-9700 K', database.cpu).map(item => item.name), ['Core i7-9700K']);
  for (const text of ['RX6800 -XTE', 'RTX 4070-TiM', 'RX 6800 -XTX']) assert.equal(api.requirementsModels(text, database.gpu).length, 0, text);
  for (const text of ['Ryzen 5 5600 XT', 'Ryzen 5 3600 U', 'Intel Core i7-9700 HK']) assert.equal(api.requirementsModels(text, database.cpu).length, 0, text);
  const fatekeeper = { ...tier, graphics: 'NVIDIA® GeForce®RTX 3070, or AMD Radeon™ RX6800 -XT with 8GB of VRAM' };
  const result = api.evaluate({ status: 'available', minimum: fatekeeper }, { gpu: 'RX 6800', cpu: 'Ryzen 7 7800X3D', ramGb: 32 }, database);
  assert.doesNotMatch(result.checks.find(item => item.key === 'gpu').text, /与官方参考型号一致/);
});

test('capacity punctuation and following prose do not hide an explicitly identified GPU variant', () => {
  for (const text of ['GTX 1060 6GB or better', 'GTX 1060 (6 GB VRAM)', 'GTX 1060 (VRAM 6GB)', 'GTX 1060-6GB', 'GTX 1060, 6GB']) {
    assert.deepEqual(api.requirementsModels(text, database.gpu).map(item => item.name), ['GeForce GTX 1060 6GB'], text);
  }
  assert.deepEqual(api.requirementsModels('GeForce GTX 970 - 4GB or Radeon RX 470 - 4GB', database.gpu).map(item => item.name), ['GeForce GTX 970']);
  assert.deepEqual(api.requirementsModels('GeForce GT 1030 (GDDR5)', database.gpu).map(item => item.name), ['GeForce GT 1030 GDDR5']);
  assert.deepEqual(api.requirementsModels('AMD Ryzen 7-3700x', database.cpu).map(item => item.name), ['Ryzen 7 3700X']);
  for (const text of ['GTX 1060 5GB', 'GTX 1060', 'RX 5700 6GB', 'RTX 3070 12GB (8GB)', 'GTX 1060 6GBGDDR7']) {
    assert.equal(api.requirementsModels(text, database.gpu).length, 0, text);
  }
});

test('benchmark comparisons never divide scores from different suites', () => {
  const first = { id: 'a', name: 'A', scores: { a: { min: 90, max: 90 } } };
  const second = { id: 'b', name: 'B', scores: { b: { min: 20, max: 20 } } };
  assert.equal(api.compareModels(first, second, []).relation, 'unknown');
  assert.equal(api.compareModels(model('RX 7900 XT'), model('GTX 1060 6GB'), database.gpu).relation, 'higher');
  assert.equal(api.compareModels(model('RX 9070 XT'), model('GTX 1060 6GB'), database.gpu).method, 'shared-model-inference');
});

test('the whole computer is not declared sufficient from a GPU alone', () => {
  const result = api.evaluate({ status: 'available', minimum: tier, recommended: tier }, { gpu: 'RX 7900 XT' }, database);
  assert.equal(result.status, 'incomplete');
  assert.match(result.title, /显卡/);
  assert.equal(result.checks.find(c => c.key === 'cpu').status, 'unknown');
});

test('complete core hardware can meet requirements, but RAM and VRAM failures remain', () => {
  const requirements = { status: 'available', minimum: tier, recommended: tier };
  const profile = { gpu: 'RX 7900 XT', cpu: 'Ryzen 7 9800X3D', ramGb: 32 };
  const result = api.evaluate(requirements, profile, database);
  assert.equal(result.status, 'recommended');
  assert.ok(result.sources.length > 0);
  const lowRam = api.evaluate(requirements, { ...profile, ramGb: 8 }, database);
  assert.equal(lowRam.status, 'below_minimum');
  assert.equal(lowRam.title, '内存低于最低配置要求');
  const highMemory = { ...tier, graphics: 'RTX 3060 12GB' };
  const lowVram = api.evaluate({ status: 'available', minimum: highMemory }, { ...profile, gpu: 'RTX 3070', vramGb: 128 }, database);
  assert.equal(lowVram.checks.find(c => c.key === 'gpu').status, 'fail');
  assert.match(lowVram.checks.find(c => c.key === 'gpu').text, /显存 8 GB/);
  assert.equal(api.evaluate(requirements, { ...profile, vramGb: 2 }, database).status, 'recommended');
  assert.equal(api.evaluate(requirements, { ...profile, ramGb: Infinity }, database).status, 'incomplete');
});

test('missing VRAM evidence and extra published VRAM requirements cannot pass', () => {
  const fixture = structuredClone(database);
  fixture.gpu.find(item => item.name === 'GeForce GTX 1060 6GB').vramMb = null;
  const profile = { gpu: 'RX 7900 XT', cpu: 'Ryzen 7 9800X3D', ramGb: 32 };
  const minimum = { ...tier, graphics: 'GTX 1060 6GB' };
  const result = api.evaluate({ status: 'available', minimum }, profile, fixture);
  assert.equal(result.checks.find(item => item.key === 'gpu').status, 'unknown');
  const extraMemory = api.evaluate({ status: 'available', minimum: { ...tier, graphics: 'GTX 1060 6GB; 12 GB VRAM' } }, { ...profile, gpu: 'RTX 3070' }, database);
  assert.equal(extraMemory.checks.find(item => item.key === 'gpu').status, 'fail');
  const incomplete = structuredClone(database);
  incomplete.gpu.find(item => item.name === 'GeForce RTX 5090').vramMb = null;
  const unknownActual = api.evaluate({ status: 'available', minimum }, { ...profile, gpu: 'RTX 5090' }, incomplete);
  assert.equal(unknownActual.checks.find(item => item.key === 'gpu').status, 'unknown');
  const suppliedActual = api.evaluate({ status: 'available', minimum }, { ...profile, gpu: 'RTX 5090', vramGb: 32 }, incomplete);
  assert.equal(suppliedActual.checks.find(item => item.key === 'gpu').status, 'pass');
});

test('a reference card capacity is not silently promoted to a published minimum VRAM requirement', () => {
  const profile = { gpu: 'RTX 4060', cpu: 'Ryzen 5 5600X', ramGb: 16 };
  const windrose = { ...tier, graphics: 'NVIDIA GTX 1080 Ti / AMD Radeon RX 6800' };
  const inferred = api.evaluate({ status: 'available', minimum: windrose }, profile, database);
  assert.equal(inferred.checks.find(item => item.key === 'gpu').status, 'unknown');
  assert.match(inferred.checks.find(item => item.key === 'gpu').text, /官方未明确最低显存/);
  assert.notEqual(inferred.status, 'below_minimum');
  const explicit = api.evaluate({ status: 'available', minimum: { ...tier, graphics: 'GTX 1080 Ti; 11GB VRAM' } }, profile, database);
  assert.equal(explicit.checks.find(item => item.key === 'gpu').status, 'fail');
});

test('recent desktop GPUs have manufacturer-sourced memory without manual input', () => {
  for (const [name, capacity, provider] of [['RTX 5090', 32, 'NVIDIA'], ['RTX 5080', 16, 'NVIDIA'], ['RTX 5070 Ti', 16, 'NVIDIA'], ['RTX 5070', 12, 'NVIDIA'], ['RTX 5060', 8, 'NVIDIA'], ['RTX 5050', 8, 'NVIDIA'], ['Arc B580', 12, 'Intel'], ['Arc B570', 10, 'Intel'], ['RX 9070 GRE', 12, 'AMD']]) {
    const gpu = model(name);
    assert.equal(gpu.vramMb, capacity * 1024, name);
    const source = database.sources.find(item => item.id === gpu.memorySourceId);
    assert.equal(source.provider, provider);
    assert.match(source.sha256, /^[a-f0-9]{64}$/);
    assert.ok(Number.isFinite(Date.parse(source.retrievedAt)));
  }
  const result = api.evaluate({ status: 'available', minimum: tier }, { gpu: 'RTX 5090', cpu: 'Ryzen 7 9800X3D', ramGb: 32 }, database);
  assert.equal(result.status, 'minimum');
  assert.ok(result.sources.some(source => source.provider === 'NVIDIA'));
});

test('contradictory bridges, insufficient margins and malformed scores stay unknown', () => {
  const item = (id, scores) => ({ id, name: id, scores: Object.fromEntries(Object.entries(scores).map(([key, value]) => [key, { min: value, max: value }])) });
  const actual = item('actual', { a: 90, b: 10 });
  const reference = item('reference', { c: 10, d: 90 });
  const higher = item('higher', { a: 50, c: 50 });
  const lower = item('lower', { b: 20, d: 50 });
  assert.equal(api.compareModels(actual, reference, [higher]).relation, 'higher');
  assert.equal(api.compareModels(actual, reference, [higher, lower]).relation, 'unknown');
  assert.equal(api.compareModels(item('a', { a: 60 }), item('b', { b: 40 }), [item('c', { a: 50, b: 60 })]).relation, 'unknown');
  assert.equal(api.compareModels(item('a', { a: 200 }), item('b', { a: 0 }), []).relation, 'unknown');
});

test('unrecognized third alternatives cannot prove failure and minimum failures take priority', () => {
  const profile = { gpu: 'RX 7900 XT', cpu: 'Ryzen 7 9800X3D', ramGb: 8 };
  const requirements = { status: 'available', minimum: tier, recommended: { ...tier, memoryMb: 8192 } };
  assert.equal(api.evaluate(requirements, profile, database).status, 'below_minimum');
  const three = { ...tier, graphics: 'RTX 4090 / RX 7900 XTX / RTX 9999' };
  const result = api.evaluate({ status: 'available', minimum: three }, { ...profile, gpu: 'GTX 1060 6GB' }, database);
  assert.equal(result.checks.find(item => item.key === 'gpu').status, 'unknown');
});

test('missing and unparseable source requirements stay unresolved', () => {
  assert.equal(api.evaluate(null, {}, database).status, 'unknown');
  const result = api.evaluate({ status: 'available', minimum: { graphics: 'DirectX 11 compatible GPU', processor: 'Quad core CPU' } }, { gpu: 'RX 7900 XT', cpu: 'Ryzen 7 9800X3D', ramGb: 32 }, database);
  assert.equal(result.status, 'incomplete');
  assert.ok(result.checks.every(item => item.status === 'unknown'));
});

test('independent core and instruction-set requirements cannot be proved by gaming scores', () => {
  const profile = { gpu: 'RTX 4060', cpu: 'Ryzen 5 5600X', ramGb: 16 };
  const doom = { ...tier,
    processor: 'AMD Zen 2 or Intel 10th Generation CPU @3.2Ghz with 8 cores / 16 threads or better (examples: AMD Ryzen 7 3700X or better, or Intel Core i7 10700K or better)',
    graphics: 'NVIDIA or AMD hardware Raytracing-capable GPU with 8GB dedicated VRAM or better (examples: NVIDIA RTX 2060 SUPER or better, AMD RX 6600 or better)' };
  const result = api.evaluate({ status: 'available', minimum: doom }, profile, database);
  assert.equal(result.status, 'incomplete');
  assert.equal(result.checks.find(item => item.key === 'cpu').status, 'unknown');
  assert.match(result.checks.find(item => item.key === 'cpu').text, /核心数／线程数/);
  assert.match(result.checks.find(item => item.key === 'gpu').text, /强制硬件光追/);
  const borderlands = { ...tier, processor: 'Intel Core i7-9700 / AMD Ryzen 7 2700X', additionalNotes: 'Requires 8 CPU Cores for processor. Requires 8 GB VRAM for graphics. SSD storage required' };
  assert.equal(api.evaluate({ status: 'available', minimum: borderlands }, profile, database).checks.find(item => item.key === 'cpu').status, 'unknown');
  const hexa = { ...tier, processor: 'Intel Core i5-9600K (hexa-core) / AMD Ryzen 5 3600 (hexa-core)' };
  assert.equal(api.evaluate({ status: 'available', minimum: hexa }, profile, database).checks.find(item => item.key === 'cpu').status, 'unknown');
  for (const condition of ['CPU must support AVX2', 'A CPU with SSE4.2 support is required', 'Requires AVX-512 instructions']) {
    const checks = api.evaluate({ status: 'available', minimum: { ...tier, additionalNotes: condition } }, profile, database).checks;
    assert.equal(checks.find(item => item.key === 'cpu').status, 'unknown', condition);
    assert.match(checks.find(item => item.key === 'cpu').text, /指令集/);
  }
});

test('mandatory GPU features remain unverified even when the recommendation omits them', () => {
  const profile = { gpu: 'RX 7900 XT', cpu: 'Ryzen 7 7800X3D', ramGb: 32 };
  for (const additionalNotes of ['GPU Hardware Ray Tracing Required', 'Graphics cards with Shader Model 6.6 support required', 'GPU with DX12.1 support required', 'Mesh shaders required']) {
    const result = api.evaluate({ status: 'available', minimum: { ...tier, additionalNotes }, recommended: tier }, profile, database);
    assert.equal(result.status, 'incomplete', additionalNotes);
    assert.equal(result.checks.find(item => item.key === 'gpu').status, 'unknown');
    assert.match(result.checks.find(item => item.key === 'gpu').text, /官方另有限定/);
  }
  const restricted = { ...tier, graphics: 'RX 6600 (RX 6600 or above required) / RTX 2060 (RTX series required)' };
  const result = api.evaluate({ status: 'available', minimum: restricted, recommended: tier }, profile, database);
  assert.equal(result.checks.find(item => item.key === 'gpu').status, 'unknown');
  const cpuInherited = api.evaluate({ status: 'available', minimum: { ...tier, additionalNotes: 'Requires 8 CPU Cores' }, recommended: tier }, profile, database);
  assert.notEqual(cpuInherited.status, 'recommended');
  assert.equal(cpuInherited.checks.find(item => item.key === 'cpu').status, 'unknown');
  const visibleReason = api.evaluate({ status: 'available', minimum: { ...tier, processor: 'Old processor', graphics: 'Old GPU' }, recommended: { ...tier, additionalNotes: 'GPU with DX12 support required' } }, profile, database);
  assert.equal(visibleReason.status, 'incomplete');
  assert.match(visibleReason.checks.find(item => item.key === 'gpu').text, /图形 API 的硬件支持/);
});

test('optional rendering paths and software API fields do not become mandatory hardware features', () => {
  const profile = { gpu: 'RX 7900 XT', cpu: 'Ryzen 7 7800X3D', ramGb: 32 };
  for (const additionalNotes of ['RX 6700 XT or RTX 2070 required to support ray tracing.', 'High 1080p @ 60 FPS, DX12, Ray Tracing OFF', '12GB VRAM or above recommended to play on 4k resolution.']) {
    const result = api.evaluate({ status: 'available', minimum: { ...tier, directX: 'Version 12', additionalNotes } }, profile, database);
    assert.equal(result.status, 'minimum', additionalNotes);
  }
  const insufficient = api.evaluate({ status: 'available', minimum: { ...tier, additionalNotes: 'CPU must support AVX2' } }, { ...profile, ramGb: 8 }, database);
  assert.equal(insufficient.status, 'below_minimum');
  assert.equal(insufficient.title, '内存低于最低配置要求');
});
