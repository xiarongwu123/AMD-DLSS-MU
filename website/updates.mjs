import { mkdir, open, readFile, writeFile, rename, unlink, readdir, stat, statfs } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash, randomBytes } from 'node:crypto';

export const chunkSize = 4 * 1024 ** 2;
export const maxPackageSize = 1024 ** 3;
const ttl = 2 * 60 * 60 * 1000;
const tagPattern = /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
export function updatePath(release) {
  if (!tagPattern.test(release.tag) || !/^[a-f0-9]{64}$/.test(release.sha256)) throw new Error('正式版本或 SHA-256 无效。');
  return `/updates/${release.tag}/${release.sha256}/AMD-DLSS-MU.exe`;
}

// Read the PE resource tree without loading or executing the uploaded program.
export async function readWindowsVersion(file) {
  const handle = await open(file, 'r');
  try {
    const size = (await handle.stat()).size;
    async function bytes(offset, count) {
      if (!Number.isSafeInteger(offset) || offset < 0 || count < 0 || count > 4 * 1024 ** 2 || offset + count > size)
        throw new Error('EXE 文件结构无效。');
      const buffer = Buffer.alloc(count);
      if ((await handle.read(buffer, 0, count, offset)).bytesRead !== count) throw new Error('EXE 文件不完整。');
      return buffer;
    }
    const dos = await bytes(0, 64);
    if (dos.readUInt16LE(0) !== 0x5a4d) throw new Error('请选择 Windows EXE 文件。');
    const peOffset = dos.readUInt32LE(60);
    const pe = await bytes(peOffset, 24);
    if (pe.readUInt32LE(0) !== 0x4550 || pe.readUInt16LE(4) !== 0x8664 || (pe.readUInt16LE(22) & 0x2000))
      throw new Error('安装包必须是 Windows x64 EXE，不能是 DLL。');
    const sectionCount = pe.readUInt16LE(6), optionalSize = pe.readUInt16LE(20);
    if (sectionCount < 1 || sectionCount > 96 || optionalSize < 136) throw new Error('EXE PE 头无效。');
    const optional = await bytes(peOffset + 24, optionalSize);
    if (optional.readUInt16LE(0) !== 0x20b) throw new Error('需要 64 位 Windows 安装包。');
    const resourceRva = optional.readUInt32LE(128), resourceSize = optional.readUInt32LE(132);
    const sections = await bytes(peOffset + 24 + optionalSize, sectionCount * 40);
    function fileOffset(rva, count) {
      for (let i = 0; i < sectionCount; i++) {
        const start = i * 40, address = sections.readUInt32LE(start + 12), rawSize = sections.readUInt32LE(start + 16);
        if (rva >= address && rva - address + count <= rawSize) return sections.readUInt32LE(start + 20) + rva - address;
      }
      throw new Error('EXE 版本资源地址无效。');
    }
    const resource = await bytes(fileOffset(resourceRva, resourceSize), resourceSize);
    function child(directory, id, isDirectory) {
      if (directory + 16 > resource.length) throw new Error('EXE 版本资源无效。');
      const count = resource.readUInt16LE(directory + 12) + resource.readUInt16LE(directory + 14);
      if (count > 1024 || directory + 16 + count * 8 > resource.length) throw new Error('EXE 版本资源无效。');
      for (let i = 0; i < count; i++) {
        const entry = directory + 16 + i * 8, value = resource.readUInt32LE(entry + 4);
        if ((id === null || resource.readUInt32LE(entry) === id) && Boolean(value & 0x80000000) === isDirectory)
          return value & 0x7fffffff;
      }
      throw new Error('EXE 缺少内部版本信息。');
    }
    const leaf = child(child(child(0, 16, true), null, true), null, false);
    if (leaf + 16 > resource.length) throw new Error('EXE 版本资源无效。');
    const length = resource.readUInt32LE(leaf + 4);
    if (length < 92 || length > 65536) throw new Error('EXE 版本资源大小无效。');
    const version = await bytes(fileOffset(resource.readUInt32LE(leaf), length), length);
    if (version.subarray(6, 38).toString('utf16le') !== 'VS_VERSION_INFO\0' || version.readUInt32LE(40) !== 0xfeef04bd ||
        !version.includes(Buffer.from('AMD-DLSS-MU', 'utf16le'))) throw new Error('请选择 AMD-DLSS-MU 客户端安装包。');
    const majorMinor = version.readUInt32LE(48), buildRevision = version.readUInt32LE(52);
    if ((buildRevision & 65535) !== 0) throw new Error('正式版内部修订号必须为 0。');
    return `${majorMinor >>> 16}.${majorMinor & 65535}.${buildRevision >>> 16}`;
  } finally { await handle.close(); }
}

export function createUpdateStore(dataRoot, packages, { origin, current }) {
  const root = join(dataRoot, 'uploads'), manifests = join(dataRoot, 'releases');
  const uploads = new Map();
  let creating = false;
  function get(id, owner) {
    const upload = uploads.get(id);
    if (!upload || upload.owner !== owner || upload.expires < Date.now()) throw new Error('上传已过期或不属于当前登录，请重新上传。');
    upload.expires = Date.now() + ttl;
    return upload;
  }
  const describe = release => ({ ...release, channel: 'stable', downloadUrl: origin + updatePath(release), notes: release.notes || '' });
  async function remember(release) {
    updatePath(release);
    await mkdir(manifests, { recursive: true, mode: 0o700 });
    const file = join(manifests, `${release.tag}-${release.sha256}.json`);
    try { await writeFile(file, JSON.stringify(release), { flag: 'wx', mode: 0o600 }); }
    catch (error) { if (error.code !== 'EEXIST') throw error; }
  }
  async function find(tag, hash) {
    updatePath({ tag, sha256: hash });
    const release = JSON.parse(await readFile(join(manifests, `${tag}-${hash}.json`), 'utf8'));
    if (release.tag !== tag || release.sha256 !== hash) throw new Error('版本记录不匹配。');
    return release;
  }
  function assertNew(release) {
    updatePath(release);
    const old = current();
    if (old.tag === release.tag && old.sha256 === release.sha256) return;
    const previous = String(old.version || old.tag?.slice(1)).split('.').map(Number);
    const next = release.tag.slice(1).split('.').map(Number);
    let comparison = 0;
    for (let i = 0; i < 3 && comparison === 0; i++) comparison = next[i] - (previous[i] || 0);
    if (comparison <= 0) throw new Error('新包版本必须高于当前线上版本；请先提高客户端版本号再打包。');
  }
  async function cleanup() {
    await mkdir(root, { recursive: true, mode: 0o700 });
    for (const [id, item] of uploads) if (!item.busy && item.expires < Date.now()) {
      uploads.delete(id); await unlink(item.file).catch(() => {});
    }
    for (const name of await readdir(root)) {
      if (!/^[a-f0-9]{32}\.part$/.test(name) || uploads.has(name.slice(0, 32))) continue;
      const file = join(root, name);
      if ((await stat(file)).mtimeMs < Date.now() - ttl) await unlink(file).catch(() => {});
    }
  }
  const timer = setInterval(() => cleanup().catch(() => {}), 10 * 60 * 1000); timer.unref();
  return {
    describe, remember, find, assertNew,
    async create(input, owner) {
      if (creating) throw new Error('正在准备上传，请稍后重试。');
      creating = true;
      try {
      if (input.fileName !== 'AMD-DLSS-MU.exe' || !Number.isSafeInteger(input.size) || input.size < 1024 ** 2 || input.size > maxPackageSize)
        throw new Error('请选择 AMD-DLSS-MU.exe，大小须为 1 MiB 至 1 GiB。');
      if (typeof input.notes !== 'string' || input.notes.length > 4000) throw new Error('更新说明不能超过 4000 字。');
      await cleanup();
      if (uploads.size >= 3) throw new Error('待处理上传过多，请取消旧上传后重试。');
      const space = await statfs(root);
      const reserved = [...uploads.values()].reduce((sum, item) => sum + (item.size - item.offset), 0);
      if (space.bavail * space.bsize < input.size + reserved + 256 * 1024 ** 2) throw new Error('服务器空间不足，未开始上传。');
      const id = randomBytes(16).toString('hex'), file = join(root, `${id}.part`);
      const handle = await open(file, 'wx', 0o600); await handle.close();
      uploads.set(id, { id, file, owner, size: input.size, offset: 0, notes: input.notes.trim(), expires: Date.now() + ttl, busy: false });
      return { id, offset: 0, chunkSize };
      } finally { creating = false; }
    },
    status(id, owner) { const item = get(id, owner); return { id, offset: item.offset, size: item.size, release: item.release }; },
    async append(id, owner, offset, req) {
      const item = get(id, owner), length = Number(req.headers['content-length']);
      if (item.busy || item.release) throw new Error('上传正在处理或已完成。');
      if (req.headers['content-type'] !== 'application/octet-stream' || !Number.isSafeInteger(offset) || offset !== item.offset ||
          !Number.isSafeInteger(length) || length < 1 || length > chunkSize || offset + length > item.size)
        throw new Error('分片大小或进度不匹配，请重试。');
      item.busy = true;
      let handle;
      try {
        handle = await open(item.file, 'r+');
        let received = 0;
        for await (const chunk of req) {
          if (received + chunk.length > length) throw new Error('分片超过声明大小。');
          let written = 0;
          while (written < chunk.length) written += (await handle.write(chunk, written, chunk.length - written, offset + received + written)).bytesWritten;
          received += chunk.length;
        }
        if (received !== length) throw new Error('分片上传中断，请重试。');
        await handle.sync(); item.offset += received;
        return { id, offset: item.offset };
      } catch (error) { if (handle) await handle.truncate(offset); throw error; }
      finally { if (handle) await handle.close(); item.busy = false; }
    },
    async complete(id, owner) {
      const item = get(id, owner);
      if (item.busy) throw new Error('上传正在处理。');
      if (item.release) return item.release;
      if (item.offset !== item.size) throw new Error('安装包尚未上传完整。');
      item.busy = true;
      try {
        const version = await readWindowsVersion(item.file);
        const handle = await open(item.file, 'r');
        const digest = createHash('sha256');
        try { for await (const chunk of handle.createReadStream()) digest.update(chunk); } finally { await handle.close(); }
        const release = { version, tag: `v${version}`, channel: 'stable', platform: 'Windows x64', file: 'AMD-DLSS-MU.exe',
          size: item.size, sizeDisplay: `${(item.size / 1024 ** 2).toFixed(1)} MiB`, sha256: digest.digest('hex'),
          publishedAt: new Date().toISOString().slice(0, 10), notes: item.notes, source: 'upload', releaseUrl: origin + '/download' };
        assertNew(release);
        await mkdir(join(dataRoot, 'packages'), { recursive: true, mode: 0o700 });
        await rename(item.file, packages.path(release));
        item.release = release;
        return release;
      } finally { item.busy = false; }
    },
    async discard(id, owner) {
      const item = get(id, owner);
      if (item.busy) throw new Error('上传正在处理。');
      await unlink(item.file).catch(error => { if (error.code !== 'ENOENT') throw error; });
      uploads.delete(id);
    }
  };
}
