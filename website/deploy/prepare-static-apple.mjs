import { cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join, resolve } from 'node:path';

const [sourceArg, stageArg] = process.argv.slice(2);
if (!sourceArg || !stageArg) throw new Error('Usage: node prepare-static-apple.mjs SOURCE STAGE');
const root = resolve(import.meta.dirname, '../public');
const stage = resolve(stageArg);
const files = ['apple.css', 'apple.js', 'common.js', 'catalog.js', 'styles.css', 'robots.txt', 'apple-runtime.js', 'apple-runtime.css'];
for (const page of ['index', 'compatibility', 'download', 'guide', 'feedback', 'survey', 'dlss5']) {
  files.push(`apple-${page}.css`, `apple-${page}-attributes.css`, `${page}.html`);
}
for (const page of ['index', 'download', 'guide', 'dlss5']) files.push(`apple-${page}-1.js`);
files.push(...(await readdir(join(sourceArg, 'assets'))).map(file => 'assets/' + file));
files.sort((a, b) => Number(a.endsWith('.html')) - Number(b.endsWith('.html')) || a.localeCompare(b));
const sums = [];
for (const file of files) {
  const path = 'public/' + file;
  await mkdir(join(stage, 'public', file, '..'), { recursive: true });
  await cp(join(root, file), join(stage, path));
  const hash = createHash('sha256').update(await readFile(join(root, file))).digest('hex');
  sums.push(hash + '  ' + path);
}
await cp(join(import.meta.dirname, 'activate-static-apple.sh'), join(stage, 'activate-static-apple.sh'));
await writeFile(join(stage, 'files.txt'), files.map(file => 'public/' + file).join('\n') + '\n');
await writeFile(join(stage, 'SHA256SUMS'), sums.join('\n') + '\n');
console.log('Prepared ' + files.length + ' static files in ' + stage);
