import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, dirname } from 'node:path';
const [baseline, output] = process.argv.slice(2);
if (!baseline || !output) throw new Error('Usage: prepare-client-updates.mjs BASELINE OUTPUT');
const files = ['server.mjs', 'packages.mjs', 'updates.mjs', 'Dockerfile', 'public/admin.html', 'public/assets/admin.js', 'public/assets/admin.css'];
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
await mkdir(output, { recursive: false });
const before = [], after = [];
for (const file of files) {
  let old;
  try { old = digest(await readFile(resolve(baseline, file))); }
  catch (error) { if (error.code !== 'ENOENT') throw error; old = 'MISSING'; }
  const bytes = await readFile(resolve('website', file));
  const target = resolve(output, 'payload', file);
  await mkdir(dirname(target), { recursive: true }); await writeFile(target, bytes);
  before.push(`${file}\t${old}`); after.push(`${digest(bytes)}  ${file}`);
}
await writeFile(resolve(output, 'baseline.tsv'), before.join('\n') + '\n');
await writeFile(resolve(output, 'payload.sha256'), after.join('\n') + '\n');
await copyFile('website/deploy/activate-client-updates.sh', resolve(output, 'activate-client-updates.sh'));
console.log(JSON.stringify({ output, files }));
