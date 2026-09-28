import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';

const endpoint = 'https://store.steampowered.com/search/results/';
const months = new Map(['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'].map((name, i) => [name.toLowerCase(), i + 1]));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const sleep = ms => new Promise(done => setTimeout(done, ms));
const namedEntities = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ', reg: '®', trade: '™', copy: '©', ndash: '–', mdash: '—' };

export function decodeHtml(value) {
  return value.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/giu, (entity, code) => {
    if (code[0] !== '#') {
      if (!(code in namedEntities)) throw new Error(`Unknown HTML entity: ${entity}`);
      return namedEntities[code];
    }
    const point = code[1].toLowerCase() === 'x' ? Number.parseInt(code.slice(2), 16) : Number.parseInt(code.slice(1), 10);
    if (point < 0 || point > 0x10ffff || (point >= 0xd800 && point <= 0xdfff)) throw new Error('Invalid HTML character reference.');
    return String.fromCodePoint(point);
  });
}

function attribute(html, name) {
  const match = new RegExp(`\\b${name}="([^"]*)"`, 'u').exec(html);
  return match ? decodeHtml(match[1]) : null;
}
function plain(value) { return decodeHtml(value.replace(/<[^>]*>/gu, '')).replace(/\s+/gu, ' ').trim(); }

export function parseReleaseDate(value) {
  let day, month, year, match;
  if ((match = /^(\w{3}) (\d{1,2}), (\d{4})$/u.exec(value))) [, month, day, year] = match;
  else if ((match = /^(\d{1,2}) (\w{3}),? (\d{4})$/u.exec(value))) [, day, month, year] = match;
  else if ((match = /^(\d{4})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日$/u.exec(value))) [, year, month, day] = match;
  else return null;
  month = /^\d+$/u.test(month) ? Number(month) : months.get(month.toLowerCase());
  day = Number(day); year = Number(year);
  if (!month || year < 1970 || year > 2100) return null;
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return null;
  return date.toISOString().slice(0, 10);
}

export function parseSearchPage(raw, observedDate) {
  const document = JSON.parse(raw);
  if (document.success !== 1 || typeof document.results_html !== 'string' || !Number.isSafeInteger(document.total_count))
    throw new Error('Steam search returned an unexpected response.');
  const rows = [], excluded = {};
  const exclude = reason => { excluded[reason] = (excluded[reason] || 0) + 1; };
  let anchorCount = 0;
  for (const match of document.results_html.matchAll(/<a\b[^>]*>[\s\S]*?<\/a>/giu)) {
    const row = match[0];
    if (!/\bsearch_result_row\b/u.test(attribute(row, 'class') || '')) continue;
    anchorCount++;
    try {
      const appId = attribute(row, 'data-ds-appid');
      if (!/^\d{1,10}$/u.test(appId || '') || Number(appId) < 1 || Number(appId) > 4294967295
        || attribute(row, 'data-ds-itemkey') !== `App_${appId}`) { exclude('not_single_app'); continue; }
      const href = new URL(attribute(row, 'href'));
      if (href.origin !== 'https://store.steampowered.com' || !href.pathname.startsWith(`/app/${appId}/`)) { exclude('not_store_app'); continue; }
      if (!/class="platform_img win"/u.test(row)) { exclude('no_windows_platform'); continue; }
      const title = /<span\b[^>]*class="title"[^>]*>([\s\S]*?)<\/span>/iu.exec(row);
      const release = /<div\b[^>]*class="search_released\b[^"]*"[^>]*>([\s\S]*?)<\/div>/iu.exec(row);
      if (!title || !release) { exclude('missing_title_or_date'); continue; }
      const name = plain(title[1]);
      if (!name || name.length > 200) { exclude('invalid_title'); continue; }
      // Conservative title exclusions supplement Steam's Games-only category filter.
      if (/\b(?:demo|playtest|dedicated server|soundtrack)\b|试玩版|试用版|原声音轨|原声音乐集/iu.test(name)) { exclude('demo_or_non_game_title'); continue; }
      const releaseDate = parseReleaseDate(plain(release[1]));
      if (!releaseDate) { exclude('unresolved_release_date'); continue; }
      if (releaseDate > observedDate) { exclude('future_release'); continue; }
      rows.push({ steamAppId: appId, name, releaseDate, storeUrl: `https://store.steampowered.com/app/${appId}/` });
    } catch { exclude('unparseable_row'); }
  }
  if (anchorCount === 0 && document.total_count > document.start) throw new Error('Steam search markup changed: no result rows found.');
  return { rows, excluded, totalCount: document.total_count, anchorCount };
}

export function parseAppDetails(raw, appId, observedDate) {
  const entry = JSON.parse(raw)[appId];
  const data = entry?.data;
  if (entry?.success !== true || data?.steam_appid !== Number(appId) || data.type !== 'game'
    || data.platforms?.windows !== true || data.release_date?.coming_soon !== false)
    throw new Error(`Steam did not verify ${appId} as a released Windows game.`);
  const releaseDate = parseReleaseDate(data.release_date.date);
  const name = typeof data.name === 'string' ? data.name.trim() : '';
  if (!releaseDate || releaseDate > observedDate || !name || name.length > 200
    || /[\u0000-\u001f]/u.test(name) || /\b(?:demo|playtest|dedicated server|soundtrack)\b/iu.test(name))
    throw new Error(`Invalid game identity or release date for ${appId}.`);
  return { steamAppId: appId, name, releaseDate, storeUrl: `https://store.steampowered.com/app/${appId}/` };
}

function searchUrl(start, language) {
  const url = new URL(endpoint);
  for (const [key, value] of Object.entries({ query: '', start, count: 100, dynamic_data: '', sort_by: '_ASC',
    category1: 998, os: 'win', filter: 'topsellers', infinite: 1, cc: 'us', l: language })) url.searchParams.set(key, String(value));
  return url.href;
}

async function retrieve(url, directory, delayMs) {
  for (let attempt = 0; attempt < 4; attempt++) {
    await sleep(delayMs);
    let response;
    try {
      response = await fetch(url, { headers: { Accept: 'application/json', 'User-Agent': 'AMD-DLSS-MU-Catalog/1.0 (public catalog metadata)' },
        redirect: 'error', signal: AbortSignal.timeout(30000) });
    } catch (error) {
      if (attempt === 3) throw error;
      await sleep(3000 * 2 ** attempt); continue;
    }
    if ((response.status === 429 || response.status >= 500) && attempt < 3) {
      const retry = response.headers.get('retry-after');
      const seconds = /^\d+$/u.test(retry || '') ? Math.min(60, Number(retry)) : 3 * 2 ** attempt;
      await response.body?.cancel(); await sleep(seconds * 1000); continue;
    }
    if (!response.ok) throw new Error(`Steam responded ${response.status}: ${url}`);
    const bytes = Buffer.from(await response.arrayBuffer());
    if (bytes.length > 4 * 1024 * 1024) throw new Error('Unexpectedly large Steam search response.');
    const digest = sha256(bytes);
    const relativeFile = `responses/${digest}.json`;
    await writeFile(join(directory, relativeFile), bytes);
    return { url, retrievedAt: new Date().toISOString(), sha256: digest, bytes: bytes.length, file: relativeFile };
  }
  throw new Error('Steam retry budget exhausted.');
}

async function generate(run, rawDirectory, target) {
  const english = new Map(), chinese = new Map(), supplements = new Map();
  const statistics = { inspectedRows: 0, rejectedRows: {}, englishCandidates: 0, supplementalGames: 0, selected: 0, localizedNames: 0 };
  for (const request of run.requests) {
    const bytes = await readFile(join(rawDirectory, request.file));
    if (sha256(bytes) !== request.sha256) throw new Error(`Raw response hash mismatch: ${request.file}`);
    if (request.kind === 'appdetails') {
      const row = parseAppDetails(bytes.toString('utf8'), request.steamAppId, run.observedDate);
      (request.language === 'english' ? supplements : chinese).set(row.steamAppId, { ...row, retrievedAt: request.retrievedAt });
      continue;
    }
    const page = parseSearchPage(bytes.toString('utf8'), run.observedDate);
    statistics.inspectedRows += page.anchorCount;
    for (const [reason, number] of Object.entries(page.excluded)) statistics.rejectedRows[reason] = (statistics.rejectedRows[reason] || 0) + number;
    const destination = request.language === 'english' ? english : chinese;
    for (const row of page.rows) if (!destination.has(row.steamAppId)) destination.set(row.steamAppId, { ...row, retrievedAt: request.retrievedAt });
  }
  statistics.englishCandidates = english.size;
  statistics.supplementalGames = supplements.size;
  const selected = new Map([...english.values()].slice(0, target).map(row => [row.steamAppId, row]));
  for (const row of supplements.values()) selected.set(row.steamAppId, row);
  const games = [...selected.values()].map(row => {
    const localized = chinese.get(row.steamAppId);
    return { steamAppId: row.steamAppId, name: row.name,
      nameZhCn: localized && localized.name !== row.name ? localized.name : null,
      releaseDate: row.releaseDate, storeUrl: row.storeUrl, source: 'steam-store',
      sourceRetrievedAt: localized && localized.retrievedAt > row.retrievedAt ? localized.retrievedAt : row.retrievedAt };
  }).sort((left, right) => Number(left.steamAppId) - Number(right.steamAppId));
  statistics.selected = games.length;
  statistics.localizedNames = games.filter(game => game.nameZhCn !== null).length;
  return { games, statistics };
}

async function main() {
  const { values } = parseArgs({ options: {
    output: { type: 'string', default: 'server/Mu.Server/Data/Catalog/steam-games.json' },
    'raw-dir': { type: 'string', default: '../.tools/steam-catalog-source' },
    count: { type: 'string', default: '2000' }, minimum: { type: 'string', default: '1500' },
    'max-pages': { type: 'string', default: '40' }, 'delay-ms': { type: 'string', default: '1100' },
    'replay-dir': { type: 'string' }, 'supplement-run': { type: 'string' },
    'include-appids': { type: 'string', default: '271590' }
  } });
  const target = Number(values.count), minimum = Number(values.minimum), maxPages = Number(values['max-pages']), delayMs = Number(values['delay-ms']);
  if (!Number.isInteger(target) || target < 1500 || target > 5000 || !Number.isInteger(minimum) || minimum < 1500 || minimum > target
    || !Number.isInteger(maxPages) || maxPages < 1 || maxPages > 60 || !Number.isInteger(delayMs) || delayMs < 1000)
    throw new Error('Use target 1500-5000, minimum 1500..target, max-pages 1-60, and delay-ms >= 1000.');
  if (values['replay-dir'] && values['supplement-run']) throw new Error('Replay is offline and cannot be combined with online supplementation.');
  const includeAppIds = [...new Set(values['include-appids'].split(',').filter(Boolean))];
  if (includeAppIds.length > 10 || includeAppIds.some(id => !/^\d{1,10}$/u.test(id) || Number(id) < 1 || Number(id) > 4294967295))
    throw new Error('Use at most 10 explicit, valid Steam AppIDs.');
  let run, rawDirectory;
  if (values['replay-dir'] || values['supplement-run']) {
    rawDirectory = resolve(values['replay-dir'] || values['supplement-run']);
    run = JSON.parse(await readFile(join(rawDirectory, 'run.json'), 'utf8'));
    if (values['supplement-run']) {
      await writeFile(join(rawDirectory, `run-before-supplement-${Date.now()}.json`), JSON.stringify(run, null, 2) + '\n');
    }
  } else {
    const startedAt = new Date().toISOString();
    rawDirectory = join(resolve(values['raw-dir']), startedAt.replace(/[:.]/gu, '-'));
    await mkdir(join(rawDirectory, 'responses'), { recursive: true });
    run = { schemaVersion: 1, startedAt, observedDate: startedAt.slice(0, 10), target, requests: [] };
    const observed = new Set();
    for (let pageNumber = 0; pageNumber < maxPages; pageNumber++) {
      let totalCount = 0;
      for (const language of ['english', 'schinese']) {
        const record = await retrieve(searchUrl(pageNumber * 100, language), rawDirectory, delayMs);
        const parsed = parseSearchPage(await readFile(join(rawDirectory, record.file), 'utf8'), run.observedDate);
        if (language === 'english') for (const game of parsed.rows) observed.add(game.steamAppId);
        totalCount = parsed.totalCount;
        run.requests.push({ ...record, language, start: pageNumber * 100, count: 100 });
        await writeFile(join(rawDirectory, 'run.json'), JSON.stringify(run, null, 2) + '\n');
      }
      console.log(`Page ${pageNumber + 1}: ${observed.size} unique released Windows game candidates.`);
      if (observed.size >= target || (pageNumber + 1) * 100 >= totalCount) break;
    }
  }
  if (!values['replay-dir']) {
    delete run.completedAt;
    run.includeAppIds = [...new Set([...(run.includeAppIds || []), ...includeAppIds])];
    for (const steamAppId of includeAppIds) for (const language of ['english', 'schinese']) {
      if (run.requests.some(request => request.kind === 'appdetails' && request.steamAppId === steamAppId && request.language === language)) continue;
      const url = new URL('https://store.steampowered.com/api/appdetails');
      for (const [key, value] of Object.entries({ appids: steamAppId, l: language, cc: 'us', filters: 'basic,platforms,release_date' })) url.searchParams.set(key, value);
      const record = await retrieve(url.href, rawDirectory, delayMs);
      const game = parseAppDetails(await readFile(join(rawDirectory, record.file), 'utf8'), steamAppId, run.observedDate);
      run.requests.push({ ...record, kind: 'appdetails', language, steamAppId });
      await writeFile(join(rawDirectory, 'run.json'), JSON.stringify(run, null, 2) + '\n');
      console.log(`Verified explicit ${steamAppId} (${language}): ${game.name}`);
    }
    run.completedAt = new Date().toISOString();
    await writeFile(join(rawDirectory, 'run.json'), JSON.stringify(run, null, 2) + '\n');
  }
  if (!run.completedAt) throw new Error('Replay requires a completed run.');
  const { games, statistics } = await generate(run, rawDirectory, target);
  if (games.length < minimum) throw new Error(`Only ${games.length} eligible games; refusing to write a catalog below ${minimum}. Raw capture: ${rawDirectory}`);
  const provenance = {
    schemaVersion: 1, startedAt: run.startedAt, completedAt: run.completedAt, observedDate: run.observedDate,
    method: 'Steam Store public top-sellers search, Games category 998, Windows platform, US store region, English and Simplified Chinese locales. Explicit continuity AppIDs are additionally verified through official appdetails type, platform, and release fields.',
    exclusions: 'Bundles/packages/multiple AppIDs, non-Windows entries, demo/playtest/soundtrack/dedicated-server titles, missing or ambiguous release dates, and releases after observedDate are excluded.',
    scope: 'Catalog identity and release metadata only. No compatibility, rendering API, DLSS support, game build/version, developer, or engine is inferred.',
    explicitAppIds: run.includeAppIds || [], statistics, requests: run.requests
  };
  const manifestBytes = Buffer.from(JSON.stringify(provenance, null, 2) + '\n');
  const catalog = { schemaVersion: 1, generatedAt: run.completedAt,
    source: { provider: 'Steam Store', region: 'US', method: 'public-top-sellers-search-and-explicit-appdetails',
      snapshotSha256: sha256(JSON.stringify(games)), manifestSha256: sha256(manifestBytes) }, games };
  const output = resolve(values.output);
  await mkdir(dirname(output), { recursive: true });
  const catalogBytes = Buffer.from(JSON.stringify(catalog, null, 2) + '\n');
  await writeFile(output, catalogBytes);
  await writeFile(join(dirname(output), 'steam-games.provenance.json'), manifestBytes);
  await writeFile(output + '.sha256', `${sha256(catalogBytes)}  steam-games.json\n`);
  console.log(JSON.stringify({ output, rawDirectory, ...statistics, sha256: sha256(catalogBytes) }, null, 2));
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
