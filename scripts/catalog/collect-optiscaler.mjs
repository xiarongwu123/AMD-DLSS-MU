import { createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { normalizeTitle } from './match-nvidia-references.mjs';
import { reviewedCommit, reviewedNotes } from './optiscaler-notes.mjs';

const run = promisify(execFile);
const wikiBase = 'https://github.com/optiscaler/OptiScaler/wiki/';
const gitRemote = 'https://github.com/optiscaler/OptiScaler.wiki.git';
const statuses = new Map([['✅', 'working'], ['❌', 'not_working'], ['➖', 'platform_limited']]);
const digest = value => createHash('sha256').update(value).digest('hex');
const pageUrl = (page, commit) => `${wikiBase}${encodeURIComponent(page)}/${commit}`;
const notesByTitle = new Map(Object.entries(reviewedNotes).map(([name, notes]) => [normalizeTitle(name), notes]));

function text(value) {
  return value.replace(/<s>[\s\S]*?<\/s>/giu, '').replace(/~~[\s\S]*?~~/gu, '')
    .replace(/\[([^\]]+)\]\([^\n]*?\)/gu, '$1').replace(/https?:\/\/\S+\[([^\]]+)\]/gu, '$1')
    .replace(/<br\s*\/?>/giu, '; ').replace(/<[^>]*>/gu, '').replace(/\*\*|`|\+\+\+/gu, '')
    .replace(/\s+/gu, ' ').trim();
}

export function parseCompatibility(markdown) {
  const rows = [];
  let section = 'general', inTable = false;
  const source = markdown.replace(/<!--[\s\S]*?-->/gu, block => block.replace(/[^\n]/gu, ' '));
  for (const [index, original] of source.split(/\r?\n/u).entries()) {
    const line = original.trim();
    if (line === '## Upscaler mods support') { section = 'upscaler_mod'; inTable = false; }
    if (line === '## Luma Unreal Engine') { section = 'luma_ue'; inTable = false; }
    if (/^\| Game \| Compatibility \|/u.test(line)) { inTable = true; continue; }
    if (!inTable || !line.includes('|')) continue;
    const cells = line.replace(/^\|/u, '').replace(/\|$/u, '').split(/(?<!\\)\|/u).map(cell => cell.trim());
    if (/^[-: ]+$/u.test(cells[0])) continue;
    if (!statuses.has(cells[1])) {
      if (line.startsWith('|')) throw new Error(`Unknown compatibility row at line ${index + 1}.`);
      continue;
    }
    const minimum = section === 'general' ? 5 : 4;
    if (cells.length < minimum || cells.length > 7) throw new Error(`Unexpected table columns at line ${index + 1}.`);
    const link = /^\[([^\]]+)\]\((.+)\)$/u.exec(cells[0]);
    const name = text(link ? link[1] : cells[0]);
    if (!name || name.length > 200) throw new Error('Invalid upstream game title.');
    let page = null;
    if (link) {
      const url = new URL(link[2], wikiBase);
      if (url.origin !== 'https://github.com' || !url.pathname.startsWith('/optiscaler/OptiScaler/wiki/') || url.search)
        throw new Error('Game detail link points outside the official wiki.');
      page = decodeURIComponent(url.pathname.slice('/optiscaler/OptiScaler/wiki/'.length));
      if (!page || page.includes('/') || page === '.' || page === '..') throw new Error('Invalid wiki page identity.');
    }
    const upscalerInputs = [...new Set(cells[2].split(/,|<br\s*\/?>/iu).map(text).filter(Boolean))];
    if (upscalerInputs.some(value => value.length > 100)) throw new Error('Unexpected upscaler input value.');
    rows.push({ name, section, status: statuses.get(cells[1]), upscalerInputs,
      page, notes: cells[section === 'general' ? 4 : 3], line: index + 1 });
  }
  if (!rows.length) throw new Error('No compatibility rows found.');
  return rows;
}

export function parseEnvironment(asciidoc) {
  const fields = {};
  const headings = [...asciidoc.matchAll(/^\|\*\*([^*\r\n]+)\*\*\s*\|?\s*$/gmu)];
  for (const [index, heading] of headings.entries()) {
    const end = headings[index + 1]?.index ?? asciidoc.indexOf('\n|===', heading.index + heading[0].length);
    const raw = asciidoc.slice(heading.index + heading[0].length, end < 0 ? undefined : end).trim();
    fields[heading[1]] = text(raw.replace(/^(?:a\|\s*\n|\|)/u, '').replace(/^\*+\s*/gmu, ''));
  }
  const value = key => {
    const content = fields[key];
    return content && !/^[-_ ]+$/u.test(content) ? content : null;
  };
  const environment = { optiscalerVersion: value('Last Tested Version'), gpu: value('GPU'), os: value('OS') };
  if (Object.values(environment).some(value => value?.length > 1000)) throw new Error('Unexpected environment field length.');
  return { environment: Object.values(environment).some(Boolean) ? environment : null,
    hasConditions: ['Known Issues', 'Notes', 'Settings', 'Game Settings', 'FG-Settings'].some(key => value(key) !== null),
    conditionText: ['Known Issues', 'Notes', 'Settings', 'Game Settings', 'FG-Settings'].map(key => value(key) || '').join(' ') };
}

function buildNotes(row, detail) {
  const curated = notesByTitle.get(normalizeTitle(row.name));
  const notes = curated ? [...curated] : [];
  if (row.section === 'upscaler_mod') notes.unshift('这一路径依赖第三方超分辨率 Mod 提供输入，不是游戏原生支持。');
  if (row.section === 'luma_ue') notes.unshift('这一路径依赖 Luma Unreal Engine Mod，并要求 DX11；还需满足该游戏的 Luma 配置。');
  const conditions = text(row.notes) + ' ' + (detail?.conditionText || '');
  if (!curated && conditions.trim()) {
    if (/REFramework/iu.test(conditions)) notes.push('还需要 REFramework 相关组件；请按上游说明选择分支和加载顺序。');
    if (/Engine\.ini/iu.test(conditions)) notes.push('上游指定了额外 Engine.ini 配置；不应直接套用默认安装。');
    if (/anti.?cheat|BattlEye|\bEAC\b/iu.test(conditions)) notes.push('上游列有反作弊相关接入限制；不能把此适配条目视为受保护联机模式可用。');
    if (/crash/iu.test(conditions)) notes.push('上游记录了特定配置下的崩溃条件与处理方法。');
    if (/flicker|shimmer|artifact|ghosting|corrupt|black screen/iu.test(conditions)) notes.push('上游记录了特定输入或设置下的画面异常。');
    if (/spoof/iu.test(conditions)) notes.push('显卡伪装设置有游戏特定条件，请查看原始配置。');
    notes.push('上游还列有安装、输入选择或设置条件；使用前请查看该固定版本来源。');
  }
  return [...new Set(notes)];
}

export async function buildCatalog(markdown, steamCatalog, files, source) {
  const rows = parseCompatibility(markdown);
  const steam = new Map();
  for (const game of steamCatalog.games) {
    const key = normalizeTitle(game.name);
    steam.set(key, [...(steam.get(key) || []), game]);
  }
  const detailFiles = new Map([...files.keys()].filter(name => name.endsWith('.asciidoc')).map(name => [name.slice(0, -9).normalize('NFC').toLowerCase(), name]));
  const pageTitles = new Map();
  for (const row of rows) {
    if (!row.page) continue;
    const page = row.page.normalize('NFC').toLowerCase();
    if (!pageTitles.has(page)) pageTitles.set(page, new Set());
    pageTitles.get(page).add(normalizeTitle(row.name));
  }
  const seen = new Map(), entries = [], audit = [];
  let duplicates = 0;
  for (const row of rows) {
    const key = `${row.section}:${normalizeTitle(row.name)}`;
    const signature = JSON.stringify({ ...row, line: undefined });
    if (seen.has(key)) {
      if (seen.get(key) !== signature) throw new Error(`Conflicting upstream duplicate: ${row.name}`);
      duplicates++; continue;
    }
    seen.set(key, signature);
    const candidates = steam.get(normalizeTitle(row.name)) || [];
    const detailFile = row.page && detailFiles.get(row.page.normalize('NFC').toLowerCase());
    const detail = detailFile ? parseEnvironment(files.get(detailFile).toString('utf8')) : null;
    const sharedDetail = row.page && pageTitles.get(row.page.normalize('NFC').toLowerCase()).size > 1;
    const conditionText = text(row.notes) + ' ' + (detail?.conditionText || '');
    const requiredMod = row.section === 'luma_ue' ? 'luma_ue'
      : row.section === 'upscaler_mod' || /requires?\s+(?:using\s+)?(?:the\s+)?REFramework/iu.test(conditionText) ? 'third_party_upscaler' : 'none';
    const entry = {
      id: 'optiscaler-' + digest(key).slice(0, 20),
      steamAppId: candidates.length === 1 ? candidates[0].steamAppId : null,
      name: row.name,
      sourceProvider: 'OptiScaler 项目 Wiki',
      sourceUrl: pageUrl(detailFile ? detailFile.slice(0, -9) : 'Compatibility-List', source.commit),
      sourceRetrievedAt: source.retrievedAt,
      sourceCommit: source.commit,
      status: row.status,
      upscalerInputs: row.upscalerInputs,
      requiredMod,
      notes: [...buildNotes(row, detail), ...(sharedDetail ? ['链接为多款游戏共用的配置说明；其中的 GPU/系统记录不归属本游戏。'] : [])],
      testEnvironment: sharedDetail ? null : detail?.environment || null,
      muVerified: false,
    };
    entries.push(entry);
    audit.push({ id: entry.id, tableLine: row.line, detailFile: detailFile || null, matchMethod: entry.steamAppId ? 'unique-normalized-complete-title' : 'unmatched',
      curatedConditions: notesByTitle.has(normalizeTitle(row.name)) });
  }
  entries.sort((a, b) => a.id.localeCompare(b.id, 'en'));
  const counts = entries.reduce((result, entry) => {
    result[entry.status] = (result[entry.status] || 0) + 1;
    return result;
  }, {});
  return { entries, audit, statistics: { sourceRows: rows.length, duplicateRows: duplicates, entries: entries.length,
    matchedSteamGames: entries.filter(entry => entry.steamAppId).length,
    entriesWithEnvironment: entries.filter(entry => entry.testEnvironment).length,
    matchedWithEnvironment: entries.filter(entry => entry.steamAppId && entry.testEnvironment).length, ...counts } };
}

async function main() {
  const { values } = parseArgs({ options: {
    'wiki-dir': { type: 'string' }, 'audit-dir': { type: 'string' },
    catalog: { type: 'string', default: 'server/Mu.Server/Data/Catalog/steam-games.json' },
    output: { type: 'string', default: 'server/Mu.Server/Data/Catalog/optiscaler-compatibility.json' },
    'generated-at': { type: 'string' },
  } });
  if (!values['wiki-dir'] || !values['audit-dir']) throw new Error('Supply a clean official --wiki-dir checkout and an external --audit-dir.');
  const directory = resolve(values['wiki-dir']), auditDirectory = resolve(values['audit-dir']);
  const git = async (...args) => (await run('git', ['-C', directory, ...args])).stdout.trim();
  if (await git('remote', 'get-url', 'origin') !== gitRemote || await git('status', '--porcelain')) throw new Error('Use an unchanged official wiki checkout.');
  const commit = await git('rev-parse', 'HEAD');
  if (commit !== reviewedCommit) throw new Error('This wiki revision needs a review of factual note paraphrases before import.');
  const fileNames = (await readdir(directory)).filter(name => name.endsWith('.asciidoc') || ['Compatibility-List.md', 'FSR4-Compatibility-List.md'].includes(name)).sort();
  const files = new Map();
  for (const name of fileNames) files.set(name, await readFile(join(directory, name)));
  const bytes = files.get('Compatibility-List.md');
  const retrievedAt = (await stat(join(directory, 'Compatibility-List.md'))).mtime.toISOString();
  const source = { provider: 'OptiScaler 项目 Wiki', url: pageUrl('Compatibility-List', commit), commit, retrievedAt, sha256: digest(bytes) };
  const steamBytes = await readFile(resolve(values.catalog));
  const steamCatalog = JSON.parse(steamBytes);
  if (steamCatalog.schemaVersion !== 1 || !Array.isArray(steamCatalog.games)) throw new Error('Invalid Steam identity catalog.');
  const result = await buildCatalog(bytes.toString('utf8'), steamCatalog, files, source);
  if (result.entries.length < 500 || result.statistics.matchedSteamGames < 100) throw new Error('Unexpected upstream coverage; refusing output.');
  const generatedAt = values['generated-at'] || new Date().toISOString();
  if (!Number.isFinite(Date.parse(generatedAt))) throw new Error('Invalid generation timestamp.');
  const document = { schemaVersion: 1, generatedAt, source, statistics: result.statistics, entries: result.entries };
  await mkdir(auditDirectory, { recursive: true });
  const manifest = { schemaVersion: 1, generatedAt, source, wikiGitUrl: gitRemote, steamCatalogSha256: digest(steamBytes),
    files: [...files].map(([file, bytes]) => ({ file, sha256: digest(bytes), bytes: bytes.length })), entries: result.audit };
  await writeFile(join(auditDirectory, 'optiscaler-provenance.json'), JSON.stringify(manifest, null, 2) + '\n');
  const output = resolve(values.output);
  await mkdir(dirname(output), { recursive: true });
  await writeFile(output, JSON.stringify(document, null, 2) + '\n');
  console.log(JSON.stringify({ output, auditDirectory, commit, ...result.statistics }, null, 2));
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
