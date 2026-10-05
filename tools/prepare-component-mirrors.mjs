import { mkdir, open, rename, stat, unlink } from 'node:fs/promises';
import { createHash, randomUUID } from 'node:crypto';
import { join, resolve } from 'node:path';
import { dlssInstaller, optiscaler } from '../website/mirrors.mjs';

const root = resolve(process.argv[2] || 'artifacts/component-mirrors');
async function verify(file, asset) {
  const handle = await open(file, 'r');
  try {
    if ((await handle.stat()).size !== asset.size) throw new Error('Unexpected size');
    const hash = createHash('sha256');
    for await (const bytes of handle.createReadStream({ autoClose: false })) hash.update(bytes);
    if (hash.digest('hex') !== asset.sha256) throw new Error('Unexpected SHA-256');
  } finally { await handle.close(); }
}
for (const asset of [dlssInstaller, optiscaler]) {
  const folder = join(root, 'mirrors', asset.kind, asset.tag);
  const destination = join(folder, asset.name);
  await mkdir(folder, { recursive: true });
  if (await stat(destination).catch(() => null)) {
    await verify(destination, asset);
    console.log(`Verified existing ${asset.kind}: ${asset.sha256}`);
    continue;
  }
  let complete = false;
  for (const source of [asset.source, asset.api]) {
    const partial = destination + '.' + randomUUID() + '.partial';
    try {
      const response = await fetch(source, { signal: AbortSignal.timeout(300000), headers: {
        'User-Agent': 'AMD-DLSS-MU-Mirror/1.0', Accept: 'application/octet-stream'
      } });
      if (!response.ok || new URL(response.url).protocol !== 'https:') throw new Error(`HTTP ${response.status}`);
      const file = await open(partial, 'wx');
      try {
        let size = 0;
        for await (const bytes of response.body) {
          size += bytes.length;
          if (size > asset.size) throw new Error('Exceeded pinned size');
          await file.writeFile(bytes);
        }
      } finally { await file.close(); }
      await verify(partial, asset);
      await rename(partial, destination);
      console.log(`Prepared ${asset.kind}: ${asset.size} bytes, ${asset.sha256}`);
      complete = true; break;
    } catch (error) {
      console.error(`${asset.kind} ${new URL(source).host}: ${error.message}`);
    } finally { await unlink(partial).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
  }
  if (!complete) throw new Error('No verified mirror prepared for ' + asset.kind);
}
