import { cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';

const [sourceArg, releaseArg] = process.argv.slice(2);
if (!sourceArg || !releaseArg) throw new Error('Usage: node import-apple-site.mjs SOURCE LIVE_RELEASE_JSON');
const source = resolve(sourceArg);
const target = resolve(import.meta.dirname, '../public');
const release = JSON.parse(await readFile(releaseArg, 'utf8'));
const pages = ['index', 'compatibility', 'download', 'guide', 'feedback', 'survey', 'dlss5'];
const pending = [];
await mkdir(target, { recursive: true });
for (const file of ['apple.css', 'apple.js', 'common.js', 'catalog.js', 'styles.css', 'robots.txt']) {
  await cp(join(source, file), join(target, file));
}
const appleScript = (await readFile(join(target, 'apple.js'), 'utf8'))
  .replace("a.href=official+'/compatibility?q='+encodeURIComponent(item.game.name)", "a.dataset.gameId=item.game.id;a.href='#game-detail'");
await writeFile(join(target, 'apple.js'), appleScript);
await mkdir(join(target, 'assets'), { recursive: true });
for (const file of await readdir(join(source, 'assets'))) {
  if (/^(admin|dashboard|metrics)/.test(file)) throw new Error('Protected asset: ' + file);
  await cp(join(source, 'assets', file), join(target, 'assets', file));
}
for (const name of pages) {
  let html = await readFile(join(source, name + '.html'), 'utf8');
  const old = JSON.parse(await readFile(join(source, 'release.json'), 'utf8'));
  if (name === 'index' || name === 'download' || name === 'feedback') {
    const [current, history] = html.split('<div class="timeline reveal">');
    html = current.replaceAll(old.sha256, release.sha256).replaceAll(old.version, release.version)
      + (history === undefined ? '' : '<div class="timeline reveal">' + history);
  }
  const css = [];
  const attributeCss = [];
  let scriptIndex = 0;
  html = html.replace(/<style\b[^>]*>([\s\S]*?)<\/style>/gi, (_, body) => {
    css.push(body);
    return `<link rel="stylesheet" href="/apple-${name}.css">`;
  });
  html = html.replace(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi, (tag, attrs, body) => {
    if (/\bsrc\s*=/.test(attrs)) return tag;
    if (/application\/ld\+json/.test(attrs)) return '';
    const file = `apple-${name}-${++scriptIndex}.js`;
    pending.push(writeFile(join(target, file), body));
    return `<script src="/${file}"></script>`;
  });
  let styleIndex = 0;
  html = html.replace(/<[^>]+>/g, tag => {
    const match = /\sstyle=("[^"]*"|'[^']*')/i.exec(tag);
    if (!match) return tag;
    const className = `apple-inline-${++styleIndex}`;
    const style = match[1].slice(1, -1).replaceAll('&quot;', '"').replaceAll('&amp;', '&');
    attributeCss.push(`.${className}{${style}}`);
    tag = tag.replace(match[0], '');
    return /\bclass=("[^"]*"|'[^']*')/.test(tag)
      ? tag.replace(/\bclass=("[^"]*"|'[^']*')/, (_, value) => `class=${value[0]}${value.slice(1, -1)} ${className}${value[0]}`)
      : tag.replace(/\s*\/?>$/, ending => ` class="${className}"${ending}`);
  });
  for (const page of pages) html = html.replaceAll(`href="${page}.html`, `href="${page === 'index' ? '/' : '/' + page}`);
  if (name === 'compatibility') html = html.replace('</body>', '<script src="/assets/hardware-check.js"></script></body>');
  html = html.replace('</head>', `<link rel="icon" href="/assets/favicon.svg" type="image/svg+xml"><link rel="stylesheet" href="/apple-${name}-attributes.css"><link rel="stylesheet" href="/apple-runtime.css"></head>`);
  html = html.replace('</body>', '<script src="/apple-runtime.js"></script></body>');
  await writeFile(join(target, `apple-${name}.css`), css.join('\n'));
  await writeFile(join(target, `apple-${name}-attributes.css`), attributeCss.join('\n'));
  await writeFile(join(target, name + '.html'), html);
  console.log(name + ': ' + styleIndex + ' styles, ' + scriptIndex + ' scripts');
}
await Promise.all(pending);
