import { readFile, writeFile, copyFile, rename } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join } from 'node:path';

const root = process.argv[2];
if (!root) throw new Error('Usage: node install.mjs SITE_ROOT');
const path = join(root, 'server.mjs');
let server = await readFile(path, 'utf8');
const hash = createHash('sha256').update(server).digest('hex');
if (hash !== 'a15aaabe096a31246f4a6000627aa302a90e9ff8a192c533762449e55a779e93') {
  throw new Error('Website baseline changed; review before deploying');
}
const hook = "    const url = new URL(req.url || '/', 'http://localhost');";
if (server.split(hook).length !== 2) throw new Error('Expected one request hook');
server = "import { createMirrorHandler } from './mirrors.mjs';\n" + server;
server = server.replace('const packages = createPackageStore(dataRoot);',
  'const packages = createPackageStore(dataRoot);\nconst handleMirror = createMirrorHandler(dataRoot);');
server = server.replace(hook, hook + '\n    if (await handleMirror(req, res)) return;');
const dockerPath = join(root, 'Dockerfile');
const docker = await readFile(dockerPath, 'utf8');
const copy = 'COPY server.mjs analytics.mjs survey.mjs packages.mjs compatibility.mjs /app/';
if (docker.split(copy).length !== 2) throw new Error('Unexpected Dockerfile');
await copyFile(new URL('./mirrors.mjs', import.meta.url), join(root, 'mirrors.mjs'));
await writeFile(path + '.mirror-next', server);
await rename(path + '.mirror-next', path);
await writeFile(dockerPath, docker.replace(copy, copy.replace(' /app/', ' mirrors.mjs /app/')));
console.log('Installed fixed, verified Magpie mirror route');
