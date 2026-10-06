import { createReadStream } from 'node:fs';
import { readFile, writeFile, rename, mkdir, copyFile, stat } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join } from 'node:path';

const [site, sha256, sizeText] = process.argv.slice(2);
const size = Number(sizeText);
if (!site || !/^[a-f0-9]{64}$/.test(sha256 || '') || !Number.isSafeInteger(size) || size < 100000000) {
  throw new Error('Usage: node publish-v2.0.1.mjs SITE_ROOT SHA256 SIZE');
}
const packagePath = join(site, 'data', 'packages', `v2.0.1-${sha256}.exe`);
if ((await stat(packagePath)).size !== size) throw new Error('Package size mismatch');
const digest = createHash('sha256');
for await (const chunk of createReadStream(packagePath)) digest.update(chunk);
if (digest.digest('hex') !== sha256) throw new Error('Package digest mismatch');
const current = JSON.parse(await readFile(join(site, 'data/release.json'), 'utf8'));
if (current.tag !== 'v2.0') throw new Error('Active release changed; review before publishing');
const backup = join(site, 'backups', 'release-v2.0.1-' + Date.now());
await mkdir(backup, { recursive: true });
const changes = new Map();
function replaceOnce(text, before, after) {
  if (text.split(before).length !== 2) throw new Error('Page baseline changed: ' + before);
  return text.replace(before, after);
}
for (const name of ['index.html', 'download.html']) {
  const path = join('public', name);
  let html = await readFile(join(site, path), 'utf8');
  html = html.replaceAll('data-release-version>v2.0<', 'data-release-version>v2.0.1<')
    .replaceAll('data-release-number>2.0<', 'data-release-number>2.0.1<')
    .replaceAll('当前 v2.0 正式下载包', '当前 v2.0.1 正式下载包');
  if (name === 'index.html') {
    html = replaceOnce(html, 'id="release-spotlight-title">AMD DLSS MU 2.0 更新公告', 'id="release-spotlight-title">AMD DLSS MU 2.0.1 下载加速更新');
    html = replaceOnce(html, '<p>性能提升约 40%！新增「大力喜鹊」！界面与交互全面升级！</p>', '<p>大力喜鹊优先使用 MU 自有镜像，首次下载无需直连 GitHub。</p>');
    html = replaceOnce(html, 'DLSS-NR 上游性能对比；实际游戏帧率提升因硬件和设置而异。', '保留上游原包与 SHA-256 校验，镜像异常自动回退。');
    html = replaceOnce(html, 'href="/download#release-v2-0"', 'href="/download#release-v2-0-1"');
  } else {
    html = replaceOnce(html, '<title>下载 AMD DLSS MU v2.0 · 正式版</title>', '<title>下载 AMD DLSS MU v2.0.1 · 正式版</title>');
    html = replaceOnce(html, '下载 AMD DLSS MU 2.0 Windows x64 正式版，查看 DLSS-NR 性能提升、大力喜鹊窗口缩放和全新界面更新公告，并核对文件 SHA-256。', '下载 AMD DLSS MU 2.0.1 Windows x64 正式版，大力喜鹊优先使用自有镜像下载，并核对文件 SHA-256。');
    html = replaceOnce(html, '    <section id="release-v2-0"', `    <section id="release-v2-0-1" class="section-shell release-section release-announcement" aria-labelledby="release-mirror-title">
      <div class="release-heading reveal"><p class="eyebrow">RELEASE INTEL / 2.0.1 正式版</p><h2 id="release-mirror-title">大力喜鹊下载加速</h2><p>优先使用 MU 自有 VPS 镜像，首次下载无需直连 GitHub。</p></div>
      <div class="notice-panel reveal"><b>完整原包 · 校验后使用 · 自动回退</b><p>镜像保存上游 v0.6.8-experimental.1 完整发布包（约 467 MiB），不重新打包；所有来源均核对文件大小与 SHA-256。镜像不可用时自动回退 GitHub Releases 与 API。已有安装和用户配置继续复用。</p><p>安装 MU v2.0.1 后生效。实际速度取决于网络线路。</p></div>
    </section>
    <section id="release-v2-0"`);
    html = html.replaceAll('data-release-notes href="https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/v2.0"', 'data-release-notes href="https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/v2.0.1"');
  }
  changes.set(path, html);
}
const appPath = 'public/assets/app.js';
changes.set(appPath, replaceOnce(await readFile(join(site, appPath), 'utf8'), "let currentReleaseVersion = '2.0';", "let currentReleaseVersion = '2.0.1';"));
const release = {
  version: '2.0.1', tag: 'v2.0.1', channel: 'stable', platform: 'Windows x64', file: 'AMD-DLSS-MU.exe',
  size, sizeDisplay: `${(size / 1024 ** 2).toFixed(1)} MiB`, sha256,
  publishedAt: new Date().toISOString().slice(0, 10),
  downloadUrl: 'https://amd-dlss-mu.claude-api.cn/download/file',
  releaseUrl: 'https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/v2.0.1',
  sourceUrl: 'https://github.com/xiarongwu123/AMD-DLSS-MU/releases/download/v2.0.1/AMD-DLSS-MU.exe', delivery: 'server'
};
changes.set('public/release.json', JSON.stringify(release, null, 2) + '\n');
changes.set('data/release.json', JSON.stringify(release, null, 2) + '\n');
for (const [path] of changes) await copyFile(join(site, path), join(backup, path.replaceAll('/', '--')));
for (const [path, body] of changes) {
  await writeFile(join(site, path + '.next'), body, { mode: 0o644 });
  await rename(join(site, path + '.next'), join(site, path));
}
console.log(JSON.stringify({ tag: release.tag, sha256, size, backup }));
