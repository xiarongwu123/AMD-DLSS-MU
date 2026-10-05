// Exercises the same authenticated, chunked HTTPS API used by the admin page.
// Supply the existing admin password on stdin; it is never logged or persisted.
import { open, readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const [exe, notesFile] = process.argv.slice(2);
if (!exe || !notesFile) throw new Error('Usage: publish-upload.mjs EXE NOTES_FILE < password');
const origin = 'https://amd-dlss-mu.claude-api.cn';
let password = '';
for await (const bytes of process.stdin) password += bytes;
let cookie = '';
async function request(route, method = 'GET', body) {
  const binary = Buffer.isBuffer(body);
  const response = await fetch(origin + route, { method, redirect: 'error', signal: AbortSignal.timeout(120000), headers: {
    Origin: origin, Cookie: cookie, ...(body === undefined ? {} : { 'Content-Type': binary ? 'application/octet-stream' : 'application/json' })
  }, body: body === undefined ? undefined : binary ? body : JSON.stringify(body) });
  if (route === '/api/admin/login' && response.ok) cookie = response.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
  const result = await response.json();
  if (!response.ok) throw new Error(`HTTP ${response.status}: ${result.message}`);
  return result;
}
await request('/api/admin/login', 'POST', { password: password.trim() }); password = '';
const file = await open(exe, 'r');
try {
  const size = (await file.stat()).size;
  const notes = await readFile(notesFile, 'utf8');
  const digest = createHash('sha256');
  for await (const bytes of file.createReadStream({ autoClose: false })) digest.update(bytes);
  const expectedHash = digest.digest('hex');
  const upload = await request('/api/admin/uploads', 'POST', { fileName: 'AMD-DLSS-MU.exe', size, notes });
  const route = `/api/admin/uploads/${upload.id}`;
  let offset = 0;
  while (offset < size) {
    const bytes = Buffer.alloc(Math.min(upload.chunkSize, size - offset));
    if ((await file.read(bytes, 0, bytes.length, offset)).bytesRead !== bytes.length) throw new Error('Local file changed during upload.');
    for (let attempt = 0; ; attempt++) {
      try { offset = (await request(`${route}?offset=${offset}`, 'PUT', bytes)).offset; break; }
      catch (error) {
        const state = await request(route);
        if (state.offset === offset + bytes.length) { offset = state.offset; break; }
        if (state.offset !== offset || attempt >= 2) throw error;
      }
    }
    if (offset % (20 * 1024 ** 2) === 0 || offset === size) console.log(`Uploaded ${offset}/${size} bytes`);
  }
  const { release } = await request(route + '/complete', 'POST', {});
  if (release.sha256 !== expectedHash || release.size !== size) throw new Error('Server upload hash/size differs from local package.');
  console.log(JSON.stringify({ verifiedUpload: release.tag, size, sha256: release.sha256 }));
  const { job } = await request(route + '/publish', 'POST', {});
  for (let attempt = 0; attempt < 90; attempt++) {
    const result = (await request('/api/admin/release-status')).job;
    if (result?.id !== job.id) throw new Error('Publication job changed.');
    if (result.state === 'failed') throw new Error(result.message);
    if (result.state === 'complete') { console.log(JSON.stringify({ published: result.release })); break; }
    if (attempt === 89) throw new Error('Publication status timed out.');
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
} finally { await file.close(); await request('/api/admin/logout', 'POST', {}).catch(() => {}); }
