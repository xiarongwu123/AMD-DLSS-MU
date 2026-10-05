import { constants } from 'node:fs';
import { open } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash } from 'node:crypto';

export const magpie = Object.freeze({
  tag: 'v0.6.8-experimental.1',
  sha256: 'efb41e5177a628c0742566a887660a5a50e159f824cbfbf9f0620b2cc3b6803c',
  size: 489787536,
  name: 'Magpie-Experimental-x64.zip'
});
export const dlssInstaller = Object.freeze({
  kind: 'dlss-installer', tag: 'v0.6.0',
  sha256: '20636c9587e858e2b35b29702ef36bcb0018a985592e24d6ca95b3e1b41d2e71',
  size: 59841536, name: 'dlssnr_on_amd_setup.exe',
  source: 'https://github.com/danielblnc/DLSS-NR-on-AMD/releases/download/v0.6.0/dlssnr_on_amd_setup.exe',
  api: 'https://api.github.com/repos/danielblnc/DLSS-NR-on-AMD/releases/assets/607036746'
});
export const optiscaler = Object.freeze({
  kind: 'optiscaler', tag: 'v0.9.4',
  sha256: '575cb4df866116093df75af607e37fd70e10f5163e0f23fd5c804142e80ef0ad',
  size: 55016448, name: 'Optiscaler_0.9.4-final.20260718._MM.7z',
  source: 'https://github.com/optiscaler/OptiScaler/releases/download/v0.9.4/Optiscaler_0.9.4-final.20260718._MM.7z',
  api: 'https://api.github.com/repos/optiscaler/OptiScaler/releases/assets/481819753'
});
export const mirrorAssets = Object.freeze([magpie, dlssInstaller, optiscaler]);
export const mirrorPath = asset => `/mirrors/${asset.kind || 'magpie'}/${asset.tag}/${asset.sha256}/${asset.name}`;

export function createMirrorHandler(dataRoot, assets = mirrorAssets) {
  const handlers = new Map((Array.isArray(assets) ? assets : [assets]).map(asset =>
    [mirrorPath(asset), createAssetHandler(dataRoot, asset)]));
  return async (req, res) => {
    const path = new URL(req.url, 'http://localhost').pathname;
    if (!path.startsWith('/mirrors/')) return false;
    const handle = handlers.get(path);
    if (handle) return handle(req, res);
    res.writeHead(404, { 'Cache-Control': 'no-store', 'Content-Length': 0 });
    res.end(); return true;
  };
}

function createAssetHandler(dataRoot, asset) {
  const file = join(dataRoot, 'mirrors', asset.kind || 'magpie', asset.tag, asset.name);
  let verifiedStamp;
  let verifying;
  async function checkedHandle() {
    const handle = await open(file, constants.O_RDONLY | constants.O_NOFOLLOW);
    try {
      const stat = await handle.stat();
      if (!stat.isFile() || stat.size !== asset.size) throw new Error('Invalid mirror size');
      const stamp = `${stat.ino}:${stat.size}:${stat.mtimeMs}:${stat.ctimeMs}`;
      if (verifying) await verifying;
      if (verifiedStamp !== stamp) {
        verifying = (async () => {
          const hash = createHash('sha256');
          for await (const chunk of handle.createReadStream({ start: 0, autoClose: false })) hash.update(chunk);
          if (hash.digest('hex') !== asset.sha256) throw new Error('Invalid mirror digest');
          verifiedStamp = stamp;
        })();
        try { await verifying; } finally { verifying = null; }
      }
      return { handle, stat };
    } catch (error) { await handle.close(); throw error; }
  }
  return async function handleMirror(req, res) {
    const pathname = new URL(req.url, 'http://localhost').pathname;
    if (!pathname.startsWith('/mirrors/')) return false;
    function empty(status, headers = {}) {
      res.writeHead(status, { 'Cache-Control': 'no-store', 'Content-Length': 0, ...headers });
      res.end(); return true;
    }
    if (pathname !== mirrorPath(asset)) return empty(404);
    if (!['GET', 'HEAD'].includes(req.method)) return empty(405, { Allow: 'GET, HEAD' });
    let opened;
    try { opened = await checkedHandle(); }
    catch { return empty(503, { 'Retry-After': 60 }); }
    const { handle, stat } = opened;
    const etag = `"${asset.sha256}"`;
    const headers = {
      'Content-Type': asset.name.endsWith('.zip') ? 'application/zip' : 'application/octet-stream',
      'Content-Disposition': `attachment; filename="${asset.name}"`,
      'Content-Length': asset.size,
      'Accept-Ranges': 'bytes', ETag: etag,
      'Last-Modified': stat.mtime.toUTCString(),
      'Cache-Control': 'public, max-age=31536000, immutable, no-transform',
      'X-Content-Type-Options': 'nosniff'
    };
    if (req.headers['if-none-match']?.split(',').map(s => s.trim().replace(/^W\//, '')).some(s => s === '*' || s === etag)) {
      await handle.close(); delete headers['Content-Length']; res.writeHead(304, headers); res.end(); return true;
    }
    let start = 0, end = asset.size - 1, status = 200;
    const ifRange = req.headers['if-range'];
    const rangeAllowed = !ifRange || ifRange === etag ||
      (!ifRange.startsWith('"') && !ifRange.startsWith('W/') && Number.isFinite(Date.parse(ifRange)) &&
        Date.parse(ifRange) >= Math.floor(stat.mtimeMs / 1000) * 1000);
    if (req.method === 'GET' && req.headers.range && rangeAllowed) {
      const match = /^bytes=(\d*)-(\d*)$/.exec(req.headers.range);
      let valid = match && (match[1] || match[2]);
      if (valid) {
        if (!match[1]) {
          const suffix = Number(match[2]);
          valid = Number.isSafeInteger(suffix) && suffix > 0;
          start = Math.max(0, asset.size - suffix);
        } else { start = Number(match[1]); end = match[2] ? Math.min(Number(match[2]), end) : end; }
        valid = valid && Number.isSafeInteger(start) && Number.isSafeInteger(end) && start <= end && start < asset.size;
      }
      if (!valid) { await handle.close(); return empty(416, { 'Content-Range': `bytes */${asset.size}` }); }
      status = 206;
      headers['Content-Range'] = `bytes ${start}-${end}/${asset.size}`;
      headers['Content-Length'] = end - start + 1;
    }
    if (req.method === 'HEAD' || res.destroyed) {
      await handle.close();
      if (!res.destroyed) { res.writeHead(200, headers); res.end(); }
      return true;
    }
    const stream = handle.createReadStream({ start, end, autoClose: true });
    stream.on('error', () => res.destroy());
    res.on('close', () => stream.destroy());
    res.writeHead(status, headers); stream.pipe(res); return true;
  };
}
