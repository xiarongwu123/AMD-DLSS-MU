import { createReadStream } from 'node:fs';
import { readFile, writeFile, rename, mkdir, copyFile, stat } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join } from 'node:path';

const [site, sha256, sizeText] = process.argv.slice(2);
const size = Number(sizeText);
if (!site || !/^[a-f0-9]{64}$/.test(sha256 || '') || !Number.isSafeInteger(size) || size < 100000000) {
  throw new Error('Usage: node publish-v2.0.2.mjs SITE_ROOT SHA256 SIZE');
}
const packagePath = join(site, 'data/packages', `v2.0.2-${sha256}.exe`);
if ((await stat(packagePath)).size !== size) throw new Error('Package size mismatch');
const digest = createHash('sha256');
for await (const chunk of createReadStream(packagePath)) digest.update(chunk);
if (digest.digest('hex') !== sha256) throw new Error('Package digest mismatch');
const original = new Map();
const changes = new Map();
for (const path of ['public/index.html', 'public/download.html', 'public/assets/app.js', 'public/release.json', 'data/release.json']) {
  original.set(path, await readFile(join(site, path), 'utf8'));
}
if (JSON.parse(original.get('data/release.json')).tag !== 'v2.0.1') throw new Error('Active release changed');
function once(text, before, after) {
  if (text.split(before).length !== 2) throw new Error('Page baseline changed: ' + before);
  return text.replace(before, after);
}
for (const path of ['public/index.html', 'public/download.html']) {
  let html = original.get(path).replaceAll('data-release-version>v2.0.1<', 'data-release-version>v2.0.2<')
    .replaceAll('data-release-number>2.0.1<', 'data-release-number>2.0.2<');
  if (path.endsWith('index.html')) {
    html = once(html, 'id="release-spotlight-title">AMD DLSS MU 2.0.1 下载加速更新', 'id="release-spotlight-title">AMD DLSS MU 2.0.2 兼容库更新');
    html = once(html, 'href="/download#release-v2-0-1"', 'href="/download#release-v2-0-2"');
    html = once(html, '<p>大力喜鹊优先使用 MU 自有镜像，首次下载无需直连 GitHub。</p>', '<p>新增客户端游戏兼容性入口，配合官网显卡与游戏配置查询，保留大力喜鹊镜像加速。</p>');
  } else {
    html = once(html, '<title>下载 AMD DLSS MU v2.0.1 · 正式版</title>', '<title>下载 AMD DLSS MU v2.0.2 · 正式版</title>');
    html = html.replace('下载 AMD DLSS MU 2.0.1 Windows x64 正式版', '下载 AMD DLSS MU 2.0.2 Windows x64 正式版');
    html = once(html, '    <section id="release-v2-0-1"', `    <section id="release-v2-0-2" class="section-shell release-section release-announcement" aria-labelledby="release-compatibility-title">
      <div class="release-heading reveal"><p class="eyebrow">RELEASE INTEL / 2.0.2 正式版</p><h2 id="release-compatibility-title">显卡与游戏兼容库更新</h2><p>新增客户端游戏兼容性入口，保留大力喜鹊镜像下载加速。</p></div>
      <div class="notice-panel reveal"><b>客户端查询 · 官网硬件参考 · 镜像加速</b><p>客户端登录后可搜索游戏、按显卡筛选已有记录并查看测试环境。官网支持输入 GPU、CPU 与内存，对照官方配置查看硬件参考结论；目录包含 2,001 款游戏、2,000 份官方配置与 239 条匹配的 OptiScaler 适配记录。</p><p>官方配置、上游适配与 MU / DLSS5 实测分别展示，资料不足时保留未知。已有配置和大力喜鹊安装继续保留。</p><a href="/compatibility">查询我的电脑与游戏配置 →</a></div>
    </section>
    <section id="release-v2-0-1"`);
    html = html.replaceAll('data-release-notes href="https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/v2.0.1"', 'data-release-notes href="https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/v2.0.2"');
  }
  changes.set(path, html);
}
changes.set('public/assets/app.js', once(original.get('public/assets/app.js'), "let currentReleaseVersion = '2.0.1';", "let currentReleaseVersion = '2.0.2';"));
const release = {
  version: '2.0.2', tag: 'v2.0.2', channel: 'stable', platform: 'Windows x64', file: 'AMD-DLSS-MU.exe',
  size, sizeDisplay: `${(size / 1024 ** 2).toFixed(1)} MiB`, sha256,
  publishedAt: new Date().toISOString().slice(0, 10),
  downloadUrl: 'https://amd-dlss-mu.claude-api.cn/download/file',
  releaseUrl: 'https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/v2.0.2',
  sourceUrl: 'https://github.com/xiarongwu123/AMD-DLSS-MU/releases/download/v2.0.2/AMD-DLSS-MU.exe', delivery: 'server'
};
changes.set('public/release.json', JSON.stringify(release, null, 2) + '\n');
changes.set('data/release.json', JSON.stringify(release, null, 2) + '\n');
const backup = join(site, 'backups', 'release-v2.0.2-' + Date.now());
await mkdir(backup, { recursive: true });
for (const [path, body] of changes) {
  await copyFile(join(site, path), join(backup, path.replaceAll('/', '--')));
  await writeFile(join(site, path + '.v202-next'), body, { mode: 0o644 });
}
for (const [path, body] of original) {
  if (await readFile(join(site, path), 'utf8') !== body) throw new Error('Concurrent website edit: ' + path);
}
// Switch the active package metadata last; existing downloads keep their open handles.
for (const [path] of changes) await rename(join(site, path + '.v202-next'), join(site, path));
console.log(JSON.stringify({ tag: release.tag, sha256, size, backup }));
