import { readFile, writeFile, mkdir, copyFile, stat } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join, resolve } from 'node:path';
import { dlssInstaller, optiscaler, mirrorPath } from '../mirrors.mjs';

const [baseline, prepared, output] = process.argv.slice(2).map(value => resolve(value));
if (!baseline || !prepared || !output) throw new Error('Usage: prepare-component-mirrors.mjs BASELINE_MODULE PREPARED_DATA OUTPUT');
await mkdir(output, { recursive: false });
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
await writeFile(join(output, 'baseline.sha256'), hash(await readFile(baseline)) + '\n');
const module = await readFile(new URL('../mirrors.mjs', import.meta.url));
await writeFile(join(output, 'mirrors.mjs'), module);
const manifest = [`${hash(module)}  mirrors.mjs`], probes = [];
for (const asset of [dlssInstaller, optiscaler]) {
  const relative = join('mirrors', asset.kind, asset.tag, asset.name);
  const source = join(prepared, relative);
  if ((await stat(source)).size !== asset.size || hash(await readFile(source)) !== asset.sha256)
    throw new Error('Invalid prepared mirror: ' + asset.kind);
  await mkdir(join(output, 'data', 'mirrors', asset.kind, asset.tag), { recursive: true });
  await copyFile(source, join(output, 'data', relative));
  manifest.push(`${asset.sha256}  data/${relative}`);
  probes.push([mirrorPath(asset), asset.size, asset.sha256].join('\t'));
}
await writeFile(join(output, 'payload.sha256'), manifest.join('\n') + '\n');
await writeFile(join(output, 'probes.tsv'), probes.join('\n') + '\n');
await copyFile(new URL('./activate-component-mirrors.sh', import.meta.url), join(output, 'activate-component-mirrors.sh'));
console.log(JSON.stringify({ bundle: output, assets: probes.length }));
