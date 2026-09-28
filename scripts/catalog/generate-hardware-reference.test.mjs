import test from 'node:test';
import assert from 'node:assert/strict';
import { gpuIdentity, parseBenchmarkTable, parseNvidiaMemorySpecs, parseIntelMemorySpecs, parseAmdMemorySpecs } from './generate-hardware-reference.mjs';

test('GPU variants merge only when the same capacity is explicitly sourced', () => {
  const explicit = gpuIdentity('GeForce RTX 3060 12GB', null, 'gpu-current');
  const specified = gpuIdentity('GeForce RTX 3060', 'GA106, 12GB GDDR6', 'gpu-legacy');
  assert.equal(explicit.id, specified.id);
  assert.equal(gpuIdentity('GeForce RTX 3050', null, 'gpu-current'), null);
  assert.equal(gpuIdentity('GeForce RTX 3080', null, 'gpu-current'), null);
  assert.equal(gpuIdentity('GeForce RTX 4060 Ti', '8GB GDDR6', 'gpu-legacy').id, gpuIdentity('GeForce RTX 4060 Ti 8GB', null, 'gpu-current').id);
  assert.notEqual(gpuIdentity('GeForce RTX 4060 Ti 16GB', null, 'gpu-current').id, gpuIdentity('GeForce RTX 4060 Ti 8GB', null, 'gpu-current').id);
  assert.throws(() => gpuIdentity('GeForce RTX 3060 12GB', '8GB GDDR6', 'gpu-legacy'), /Conflicting/);
});

test('memory technology is not collapsed and unknown capacity remains unknown', () => {
  const slow = gpuIdentity('GeForce GT 1030', '2GB DDR4', 'gpu-legacy');
  const fast = gpuIdentity('GeForce GT 1030', '2GB GDDR5', 'gpu-legacy');
  assert.notEqual(slow.id, fast.id);
  assert.equal(gpuIdentity('GeForce GT 1030', '2GB', 'gpu-legacy'), null);
  assert.equal(gpuIdentity('Radeon RX 5600 XT', '8GB GDDR6', 'gpu-legacy').vramMb, null);
  assert.equal(gpuIdentity('GeForce RTX 5090', null, 'gpu-current').vramMb, null);
});

test('only the intended benchmark column is accepted, not theoretical or other-resolution scores', () => {
  const table = '<table><tr><th>Graphics Card</th><th>MSRP</th><th>1080p Ultra</th></tr><tr><td>GeForce RTX 4060 Ti 8GB</td><td>399</td><td>43.2% (88.0)</td></tr></table>';
  const specification = { id: 'gpu-current', kind: 'gpu', tableIndex: 0, scoreIndex: 2 };
  assert.deepEqual(parseBenchmarkTable(table, specification)[0].score, { min: 43.2, max: 43.2 });
  assert.throws(() => parseBenchmarkTable(table.replace('1080p Ultra', '1440p Ultra'), specification), /score column/);
  assert.throws(() => parseBenchmarkTable(table.replace('1080p Ultra', 'GFLOPS'), specification), /score column/);
  assert.throws(() => parseBenchmarkTable(table.replace('43.2%', '0%'), specification), /Invalid score/);
});

test('NVIDIA memory uses named desktop columns and retains multiple capacities', () => {
  const models = ['GeForce RTX 5090', 'GeForce RTX 5080', 'GeForce RTX 5070 Ti', 'GeForce RTX 5070', 'GeForce RTX 5060 Ti', 'GeForce RTX 5060', 'GeForce RTX 5050'];
  const capacities = ['32 GB GDDR7', '16 GB GDDR7', '16 GB GDDR7', '12 GB GDDR7', '16 GB / 8 GB GDDR7', '8 GB GDDR7', '8 GB GDDR6'];
  const table = (names, memory) => '<table><tr><th></th>' + names.map(name => '<th>' + name + '</th>').join('') + '</tr><tr><td>Standard Memory Config</td>' + memory.map(value => '<td>' + value + '</td>').join('') + '</tr></table>';
  const normal = table(models, capacities);
  const reordered = table([...models].reverse(), [...capacities].reverse());
  assert.deepEqual(parseNvidiaMemorySpecs(normal).get('GeForce RTX 5060 Ti'), [16384, 8192]);
  assert.equal(parseNvidiaMemorySpecs(reordered).get('GeForce RTX 5090')[0], 32768);
  assert.equal(parseNvidiaMemorySpecs(reordered).get('GeForce RTX 5070')[0], 12288);
  assert.throws(() => parseNvidiaMemorySpecs(normal + normal), /ambiguous/);
  assert.throws(() => parseNvidiaMemorySpecs(normal.replace('32 GB GDDR7', 'Up to 32 GB GDDR7')), /Ambiguous/);
  assert.throws(() => parseNvidiaMemorySpecs(normal.replace('GeForce RTX 5070 Ti', 'GeForce RTX 5070 Laptop')), /model columns/);
  assert.throws(() => parseNvidiaMemorySpecs(table(models, capacities.slice(1))), /memory row/);
});

test('Intel memory is tied to the exact desktop model and a unique capacity field', () => {
  const html = '<table><tr><th>Model Number</th><td>B580</td></tr><tr><th>Vertical Segment</th><td>Desktop</td></tr><tr><th>Memory</th><td>12 GB GDDR6</td></tr></table>';
  assert.equal(parseIntelMemorySpecs(html, 'B580'), 12);
  assert.throws(() => parseIntelMemorySpecs(html, 'B570'), /identity/);
  assert.throws(() => parseIntelMemorySpecs(html.replace('Desktop', 'Mobile'), 'B580'), /identity/);
  assert.throws(() => parseIntelMemorySpecs(html.replace('12 GB GDDR6', '12 GB / 24 GB GDDR6'), 'B580'), /Ambiguous/);
  assert.throws(() => parseIntelMemorySpecs(html + '<tr><th>Memory</th><td>24 GB GDDR6</td></tr>', 'B580'), /ambiguous/);
});

test('AMD GRE specifications cannot be confused with the other 9070 models', () => {
  const html = '<dl><dt>Name</dt><dd>AMD Radeon™ RX 9070 GRE</dd><dt>Board Type</dt><dd>Desktop</dd><dt>Max Memory Size</dt><dd>12 GB</dd></dl>';
  assert.equal(parseAmdMemorySpecs(html, 'Radeon RX 9070 GRE'), 12);
  assert.throws(() => parseAmdMemorySpecs(html, 'Radeon RX 9070'), /identity/);
  assert.throws(() => parseAmdMemorySpecs(html.replace('12 GB', '12 GB / 16 GB'), 'Radeon RX 9070 GRE'), /Ambiguous/);
});
