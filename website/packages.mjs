import { createWriteStream } from 'node:fs';
import { mkdir, open, rename, statfs, unlink } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash, randomBytes } from 'node:crypto';
import { Readable, Transform } from 'node:stream';
import { pipeline } from 'node:stream/promises';

const trustedHosts = new Set(['github.com', 'release-assets.githubusercontent.com', 'objects.githubusercontent.com', 'github-releases.githubusercontent.com']);
export function sourceUrl(release) {
  if (!/^v\d+\.\d+(?:\.\d+)?(?:-[A-Za-z0-9.-]+)?$/.test(release.tag || '') ||
      !/^[a-f0-9]{64}$/.test(release.sha256 || '') || !Number.isSafeInteger(release.size) || release.size <= 0 || release.size > 2 * 1024 ** 3) {
    throw new Error('安装包版本、大小或校验信息无效。');
  }
  return `https://github.com/xiarongwu123/AMD-DLSS-MU/releases/download/${release.tag}/AMD-DLSS-MU.exe`;
}

export function createPackageStore(dataRoot, { fetchImpl = fetch } = {}) {
  const root = join(dataRoot, 'packages');
  const verified = new Map();
  function path(release) { sourceUrl(release); return join(root, `${release.tag}-${release.sha256}.exe`); }
  async function checkedHandle(release) {
    const file = path(release);
    const handle = await open(file, 'r');
    try {
      const stat = await handle.stat();
      if (!stat.isFile() || stat.size !== release.size) throw new Error('安装包大小校验失败。');
      const stamp = `${stat.ino}:${stat.size}:${stat.mtimeMs}:${stat.ctimeMs}`;
      if (verified.get(file) !== stamp) {
        const digest = createHash('sha256');
        for await (const chunk of handle.createReadStream({ start: 0, autoClose: false })) digest.update(chunk);
        if (digest.digest('hex') !== release.sha256) throw new Error('安装包 SHA-256 校验失败。');
        if (verified.size >= 64) verified.delete(verified.keys().next().value);
        verified.set(file, stamp);
      }
      return { handle, stat };
    } catch (error) { await handle.close(); throw error; }
  }
  async function ensure(release, progress = () => {}) {
    const file = path(release);
    await mkdir(root, { recursive: true, mode: 0o700 });
    try {
      const { handle } = await checkedHandle(release); await handle.close();
      progress({ phase: 'ready', bytes: release.size, total: release.size });
      return file;
    } catch (error) { if (error.code && error.code !== 'ENOENT') throw error; }
    const space = await statfs(root);
    if (space.bavail * space.bsize < release.size + 64 * 1024 ** 2) throw new Error('服务器可用磁盘空间不足，旧下载版本未变更。');
    const temp = `${file}.part-${randomBytes(8).toString('hex')}`;
    const signal = AbortSignal.timeout(15 * 60 * 1000);
    let url = sourceUrl(release);
    try {
      let response;
      for (let redirects = 0; redirects <= 5; redirects++) {
        const target = new URL(url);
        if (target.protocol !== 'https:' || target.username || target.password || !trustedHosts.has(target.hostname)) throw new Error('安装包下载来源不受信任。');
        response = await fetchImpl(url, { redirect: 'manual', signal,
          headers: { 'User-Agent': 'AMD-DLSS-MU-Site/Mirror', 'Accept-Encoding': 'identity' } });
        if (![301, 302, 303, 307, 308].includes(response.status)) break;
        const location = response.headers.get('location');
        await response.body?.cancel();
        if (!location || redirects === 5) throw new Error('安装包下载重定向异常。');
        url = new URL(location, url).href;
      }
      if (!response.ok || !response.body) throw new Error(`上游安装包下载失败（HTTP ${response.status}）。`);
      const length = response.headers.get('content-length');
      if (length && Number(length) !== release.size) { await response.body.cancel(); throw new Error('上游安装包大小与发布信息不一致。'); }
      let bytes = 0;
      const digest = createHash('sha256');
      progress({ phase: 'downloading', bytes, total: release.size });
      const verify = new Transform({ transform(chunk, encoding, callback) {
        bytes += chunk.length;
        if (bytes > release.size) return callback(new Error('上游安装包超过预期大小。'));
        digest.update(chunk); progress({ phase: 'downloading', bytes, total: release.size }); callback(null, chunk);
      } });
      await pipeline(Readable.fromWeb(response.body), verify, createWriteStream(temp, { flags: 'wx', mode: 0o600 }), { signal });
      progress({ phase: 'verifying', bytes, total: release.size });
      if (bytes !== release.size || digest.digest('hex') !== release.sha256) throw new Error('安装包大小或 SHA-256 校验失败，旧下载版本未变更。');
      const handle = await open(temp, 'r+');
      try { await handle.sync(); } finally { await handle.close(); }
      await rename(temp, file);
      verified.delete(file);
      progress({ phase: 'ready', bytes, total: release.size });
      return file;
    } finally { await unlink(temp).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
  }
  async function serve(req, res, release, started = () => {}) {
    let opened;
    try { opened = await checkedHandle(release); }
    catch {
      res.writeHead(503, { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store', 'Retry-After': '60' });
      res.end(req.method === 'HEAD' ? undefined : '安装包暂时不可用，请稍后重试。'); return;
    }
    const { handle, stat } = opened;
    const etag = `"${release.sha256}"`;
    const headers = { 'Content-Type': 'application/vnd.microsoft.portable-executable',
      'Content-Disposition': 'attachment; filename="AMD-DLSS-MU.exe"', 'Content-Length': release.size,
      'Accept-Ranges': 'bytes', ETag: etag, 'Last-Modified': stat.mtime.toUTCString(),
      'Cache-Control': 'private, no-store, no-transform', 'X-Content-Type-Options': 'nosniff' };
    let start = 0; let end = release.size - 1; let status = 200;
    const ifRange = req.headers['if-range'];
    const rangeAllowed = !ifRange || ifRange === etag || (!ifRange.startsWith('"') && !ifRange.startsWith('W/') &&
      Number.isFinite(Date.parse(ifRange)) && Date.parse(ifRange) >= Math.floor(stat.mtimeMs / 1000) * 1000);
    if (req.method === 'GET' && req.headers.range && rangeAllowed) {
      const match = /^bytes=(\d*)-(\d*)$/.exec(req.headers.range);
      let valid = match && (match[1] || match[2]);
      if (valid) {
        if (!match[1]) { const suffix = Number(match[2]); valid = Number.isSafeInteger(suffix) && suffix > 0; start = Math.max(0, release.size - suffix); }
        else { start = Number(match[1]); end = match[2] ? Math.min(Number(match[2]), end) : end; }
        valid = valid && Number.isSafeInteger(start) && Number.isSafeInteger(end) && start <= end && start < release.size;
      }
      if (!valid) { await handle.close(); res.writeHead(416, { ...headers, 'Content-Length': 0, 'Content-Range': `bytes */${release.size}` }); res.end(); return; }
      status = 206; headers['Content-Range'] = `bytes ${start}-${end}/${release.size}`; headers['Content-Length'] = end - start + 1;
    }
    if (req.method === 'HEAD') { await handle.close(); res.writeHead(200, headers); res.end(); return; }
    if (res.destroyed) { await handle.close(); return; }
    const stream = handle.createReadStream({ start, end, autoClose: true });
    stream.on('error', () => res.destroy()); res.on('close', () => stream.destroy());
    res.writeHead(status, headers);
    if (start === 0) started();
    stream.pipe(res);
  }
  return { path, ensure, serve };
}
