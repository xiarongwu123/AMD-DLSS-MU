import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, dirname } from 'node:path';

const [baselineArg, outputArg] = process.argv.slice(2);
if (!baselineArg || !outputArg) throw new Error('Usage: node prepare-website-patch.mjs BASELINE_DIRECTORY OUTPUT_DIRECTORY');
const baseline = resolve(baselineArg), output = resolve(outputArg), site = resolve('website');
const files = ['server.mjs', 'compatibility.mjs', 'Dockerfile', 'compose.yml',
  'public/index.html', 'public/download.html', 'public/guide.html', 'public/feedback.html', 'public/survey.html', 'public/compatibility.html',
  'public/assets/compatibility.css', 'public/assets/compatibility.js', 'public/assets/hardware-check.js', 'public/assets/hardware-reference.json'];
const dependencies = ['analytics.mjs', 'survey.mjs', 'packages.mjs', 'mirrors.mjs', 'public/assets/app.css', 'public/assets/app.js'];
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
await mkdir(output, { recursive: false });
const manifest = [], checks = [];
for (const file of files) {
  const bytes = await readFile(resolve(site, file));
  let before;
  try { before = digest(await readFile(resolve(baseline, file))); }
  catch (error) { if (error.code !== 'ENOENT') throw error; before = 'MISSING'; }
  await mkdir(dirname(resolve(output, 'payload', file)), { recursive: true });
  await writeFile(resolve(output, 'payload', file), bytes);
  manifest.push([file, before, digest(bytes)].join('\t'));
}
for (const file of dependencies) {
  const bytes = await readFile(resolve(baseline, file));
  if (digest(bytes) !== digest(await readFile(resolve(site, file)))) throw new Error('Unmerged live dependency: ' + file);
  checks.push([file, digest(bytes)].join('\t'));
}
await writeFile(resolve(output, 'manifest.tsv'), manifest.join('\n') + '\n');
await writeFile(resolve(output, 'baseline-checks.tsv'), checks.join('\n') + '\n');
await copyFile(resolve(site, 'deploy/activate-website-compatibility.sh'), resolve(output, 'activate-website-compatibility.sh'));
console.log(JSON.stringify({ output, files: files.length, dependencies: dependencies.length,
  changed: manifest.filter(line => { const [, before, after] = line.split('\t'); return before !== after; }).map(line => line.split('\t')[0]) }));
