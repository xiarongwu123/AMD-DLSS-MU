import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile, rename, appendFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const delay = milliseconds => new Promise(done => setTimeout(done, milliseconds));
const htmlEntities = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ', reg: '®', trade: '™', copy: '©', ndash: '–', mdash: '—', bull: '•', times: '×' };
const labels = { os: 'os', processor: 'processor', memory: 'memory', graphics: 'graphics', directx: 'directX', storage: 'storage', 'additional notes': 'additionalNotes',
  'operating system': 'os', 'supported os': 'os', cpu: 'processor', ram: 'memory', 'system memory': 'memory',
  video: 'graphics', 'video card': 'graphics', 'graphics card': 'graphics', 'directx version': 'directX',
  'hard drive': 'storage', 'hard disk': 'storage', 'hard drive space': 'storage', 'hard disk space': 'storage' };
const strongLabel = new RegExp('<strong\\b[^>]*>\\s*(' + Object.keys(labels).join('|') + ')\\s*\\*?\\s*:', 'giu');
const checkpointCount = 20;

export function requirementsText(html) {
  if (typeof html !== 'string' || html.length > 100000) return null;
  let text = html.replace(/<!--[\s\S]*?-->/gu, '').replace(/<(script|style|iframe|object|svg|template)\b[^>]*>[\s\S]*?<\/\1\s*>/giu, '')
    .replace(/<(?:br|li)\b[^>]*>|<\/(?:li|p|div|ul|ol|h[1-6])\s*>/giu, '\n')
    .replace(strongLabel, '\n$1:')
    .replace(/<[^>]*>/gu, '');
  text = text.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/giu, (original, code) => {
    if (code[0] !== '#') return htmlEntities[code.toLowerCase()] ?? original;
    const point = code[1].toLowerCase() === 'x' ? Number.parseInt(code.slice(2), 16) : Number.parseInt(code.slice(1), 10);
    return point > 0 && point <= 0x10ffff && !(point >= 0xd800 && point <= 0xdfff) ? String.fromCodePoint(point) : '';
  });
  text = text.replace(/<(script|style|iframe|object|svg|template)\b[^>]*>[\s\S]*?<\/\1\s*>/giu, '').replace(/<[^>]*>/gu, '')
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/gu, '')
    .split(/\r?\n/u).map(line => line.replace(/[\t \u00a0]+/gu, ' ').trim()).filter(Boolean).join('\n');
  const substantive = text.replace(/^(?:Minimum|Recommended)\s*:?\s*/iu, '');
  return substantive && !/^(?:TBD|To be announced|To be determined)[:.!\s]*$/iu.test(substantive) ? text : null;
}

export function parseCapacityMb(value) {
  if (typeof value !== 'string') return null;
  if (/\d\s*[-–—]\s*\d|\d,\d/u.test(value)) return null;
  const quantities = [...value.matchAll(/\b(\d+(?:\.\d+)?)\s*(MB|GB|TB)\b/giu)];
  if (quantities.length !== 1) return null;
  const quantity = quantities[0];
  if (/\d/u.test(value.slice(0, quantity.index) + value.slice(quantity.index + quantity[0].length))) return null;
  const [, amount, unit] = quantities[0];
  const mb = Number(amount) * ({ MB: 1, GB: 1024, TB: 1048576 }[unit.toUpperCase()]);
  return Number.isFinite(mb) && mb > 0 && mb <= 100 * 1048576 ? Math.ceil(mb) : null;
}

export function parseRequirementTier(html) {
  if (typeof html === 'string' && html.length > 100000) throw new Error('requirements_too_large');
  const text = requirementsText(html);
  if (!text) return null;
  const fields = Object.fromEntries(Object.values(labels).map(name => [name, null]));
  let current = null;
  for (const line of text.split('\n')) {
    const match = /^([A-Za-z][A-Za-z ]{0,48}?)\s*\*?\s*:\s*(.*)$/u.exec(line);
    if (match && labels[match[1].trim().toLowerCase()]) {
      current = labels[match[1].trim().toLowerCase()];
      fields[current] = fields[current] ? `${fields[current]}\n${match[2]}` : match[2] || null;
    } else if (/^[A-Za-z][A-Za-z ]{1,40}:/u.test(line)) current = null;
    else if (current) fields[current] = fields[current] ? `${fields[current]}\n${line}` : line;
  }
  if (text.length > 32768 || Object.values(fields).some(field => field?.length > 8192)) throw new Error('requirements_too_large');
  const memoryMb = parseCapacityMb(fields.memory);
  return { text, ...fields, memoryMb: memoryMb !== null && memoryMb <= 1048576 ? memoryMb : null, storageMb: parseCapacityMb(fields.storage) };
}

export function parseAppRequirements(raw, steamAppId) {
  const body = JSON.parse(raw);
  if (body === null || typeof body !== 'object' || Array.isArray(body)) throw new Error('invalid_response');
  const matches = Object.entries(body).filter(([, value]) => value?.success === true && value.data?.steam_appid === Number(steamAppId));
  if (matches.length !== 1) throw new Error(matches.length > 1 ? 'ambiguous_identity' : 'app_unavailable_or_identity_mismatch');
  const [rootKey, { data }] = matches[0];
  if (data.type !== 'game') throw new Error('not_a_game');
  if (data.platforms?.windows !== true) throw new Error('not_a_windows_game');
  if (typeof data.name !== 'string' || !data.name.trim() || data.name.length > 200) throw new Error('invalid_name');
  const minimum = parseRequirementTier(data.pc_requirements?.minimum);
  const recommended = parseRequirementTier(data.pc_requirements?.recommended);
  return { name: requirementsText(data.name) ?? data.name, minimum, recommended,
    status: minimum || recommended ? 'available' : 'not_provided', rootKey, rootKeyMismatch: rootKey !== steamAppId };
}

async function atomic(path, bytes) {
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.next-${process.pid}`;
  await writeFile(temporary, bytes); await rename(temporary, path);
}

function apiUrl(steamAppId) {
  const url = new URL('https://store.steampowered.com/api/appdetails');
  for (const [key, value] of Object.entries({ appids: steamAppId, filters: 'basic,pc_requirements,platforms', l: 'english', cc: 'us' })) url.searchParams.set(key, value);
  return url.href;
}

async function main() {
  const { values } = parseArgs({ options: {
    catalog: { type: 'string', default: 'server/Mu.Server/Data/Catalog/steam-games.json' },
    references: { type: 'string', default: 'server/Mu.Server/Data/Catalog/technology-references.json' },
    adaptations: { type: 'string', default: 'server/Mu.Server/Data/Catalog/optiscaler-compatibility.json' },
    output: { type: 'string', default: 'server/Mu.Server/Data/Catalog/steam-requirements.json' },
    'raw-dir': { type: 'string', default: '../.tools/steam-requirements-source' },
    'resume-dir': { type: 'string' }, 'interval-ms': { type: 'string', default: '1600' },
    'max-games': { type: 'string' }, 'retry-unavailable': { type: 'boolean', default: false },
    'replay-only': { type: 'boolean', default: false }
  } });
  const intervalMs = Number(values['interval-ms']);
  if (!Number.isFinite(intervalMs) || intervalMs < 1500) throw new Error('interval-ms must be at least 1500.');
  const maxGames = values['max-games'] ? Number(values['max-games']) : Number.MAX_SAFE_INTEGER;
  if (!Number.isInteger(maxGames) || maxGames < 1) throw new Error('Invalid max-games.');
  const catalogBytes = await readFile(resolve(values.catalog));
  const catalog = JSON.parse(catalogBytes);
  if (catalog.schemaVersion !== 1 || !Array.isArray(catalog.games)) throw new Error('Unsupported catalog.');
  const catalogIds = new Map(catalog.games.map(game => [game.steamAppId, game]));
  if (catalogIds.size !== catalog.games.length || [...catalogIds.keys()].some(id => !/^\d{1,10}$/u.test(id))) throw new Error('Invalid catalog AppIDs.');
  const output = resolve(values.output);
  const parserSha256 = hash(await readFile(fileURLToPath(import.meta.url)));
  let run, rawDirectory;
  if (values['resume-dir']) {
    rawDirectory = resolve(values['resume-dir']);
    run = JSON.parse(await readFile(join(rawDirectory, 'run.json'), 'utf8'));
    if (run.catalogSha256 !== hash(catalogBytes)) throw new Error('Catalog changed; use a new capture directory instead of mixing identities.');
  } else {
    if (values['replay-only']) throw new Error('Replay requires resume-dir.');
    const startedAt = new Date().toISOString();
    rawDirectory = join(resolve(values['raw-dir']), startedAt.replace(/[:.]/gu, '-'));
    run = { schemaVersion: 1, startedAt, catalogSha256: hash(catalogBytes), catalogGames: catalogIds.size,
      method: 'Steam Store appdetails, basic/pc_requirements/platforms, English locale, US region; serial requests with >=1.6s start spacing.' };
    await mkdir(rawDirectory, { recursive: true });
    await atomic(join(rawDirectory, 'run.json'), JSON.stringify(run, null, 2) + '\n');
  }
  await mkdir(join(rawDirectory, 'responses'), { recursive: true });
  await mkdir(join(rawDirectory, 'records'), { recursive: true });
  const records = new Map();
  for (const id of catalogIds.keys()) {
    let record;
    try {
      record = JSON.parse(await readFile(join(rawDirectory, 'records', `${id}.json`), 'utf8'));
    } catch (error) { if (error.code === 'ENOENT') continue; throw error; }
    if (record.entry.steamAppId !== id) throw new Error('Saved identity mismatch.');
    if (record.provenance?.rawFile) {
      const bytes = await readFile(join(rawDirectory, record.provenance.rawFile));
      if (hash(bytes) !== record.entry.sourceSha256) throw new Error(`Saved response hash mismatch for ${id}.`);
      if (record.entry.status !== 'unavailable') {
        const parsed = parseAppRequirements(bytes.toString('utf8'), id);
        record.entry = { ...record.entry, name: parsed.name, status: parsed.status, reason: parsed.status === 'not_provided' ? 'requirements_not_published' : null, minimum: parsed.minimum, recommended: parsed.recommended };
        record.provenance = { ...record.provenance, rootKey: parsed.rootKey, verifiedSteamAppId: id, rootKeyMismatch: parsed.rootKeyMismatch };
      }
    } else if (record.entry.status !== 'unavailable') throw new Error(`Missing saved response for ${id}.`);
    records.set(id, record);
  }
  let checkpointAt = null;
  async function checkpoint() {
    const rows = [...records.values()].sort((a, b) => Number(a.entry.steamAppId) - Number(b.entry.steamAppId));
    const games = rows.map(row => row.entry);
    const coverage = {
      catalogGames: catalogIds.size, processed: games.length, available: games.filter(game => game.status === 'available').length,
      minimum: games.filter(game => game.minimum !== null).length, recommended: games.filter(game => game.recommended !== null).length,
      notProvided: games.filter(game => game.status === 'not_provided').length, unavailable: games.filter(game => game.status === 'unavailable').length,
      pending: catalogIds.size - games.length, rootKeyMismatches: rows.filter(row => row.provenance?.rootKeyMismatch).length
    };
    checkpointAt = rows.map(row => row.entry.sourceRetrievedAt).filter(Boolean).sort().at(-1) ?? run.startedAt;
    const provenance = { ...run, generatedAt: checkpointAt, parserSha256, coverage,
      interpretation: 'Publisher-stated Windows requirements observed on Steam, not measured performance or a compatibility conclusion. Root map keys are not trusted; data.steam_appid must uniquely match the requested AppID.',
      entries: rows.map(row => ({ steamAppId: row.entry.steamAppId, ...row.provenance })) };
    const manifest = Buffer.from(JSON.stringify(provenance, null, 2) + '\n');
    const data = { schemaVersion: 1, generatedAt: checkpointAt,
      source: { provider: 'Steam Store', language: 'english', region: 'US', catalogSha256: run.catalogSha256, manifestSha256: hash(manifest) },
      coverage, games };
    const bytes = Buffer.from(JSON.stringify(data, null, 2) + '\n');
    await atomic(join(dirname(output), 'steam-requirements.provenance.json'), manifest);
    await atomic(output, bytes);
    await atomic(output + '.sha256', `${hash(bytes)}  steam-requirements.json\n`);
    console.log(JSON.stringify({ checkpoint: true, ...coverage, output, rawDirectory, sha256: hash(bytes) }));
  }
  if (values['replay-only']) { await checkpoint(); return; }
  let stopping = false, currentController, lastStart = 0, consecutiveRateLimited = 0;
  process.once('SIGINT', () => { stopping = true; currentController?.abort(); });
  process.once('SIGTERM', () => { stopping = true; currentController?.abort(); });
  async function capture(game) {
    const url = apiUrl(game.steamAppId);
    let latest = null, reason = 'network_failure';
    for (let attempt = 0; attempt < 4 && !stopping; attempt++) {
      await delay(Math.max(0, lastStart + intervalMs - Date.now()));
      if (stopping) break;
      lastStart = Date.now(); currentController = new AbortController();
      const timer = setTimeout(() => currentController.abort(), 25000);
      try {
        const response = await fetch(url, { headers: { Accept: 'application/json', 'User-Agent': 'AMD-DLSS-MU-Requirements/1.0 (public publisher metadata)' },
          redirect: 'error', signal: currentController.signal });
        const bytes = Buffer.from(await response.arrayBuffer());
        if (bytes.length > 4 * 1024 * 1024) throw new Error('response_too_large');
        const digest = hash(bytes), rawFile = `responses/${digest}.json`, retrievedAt = new Date().toISOString();
        await writeFile(join(rawDirectory, rawFile), bytes);
        latest = { url, retrievedAt, sha256: digest, bytes: bytes.length, httpStatus: response.status, rawFile };
        await appendFile(join(rawDirectory, 'attempts.jsonl'), JSON.stringify({ steamAppId: game.steamAppId, attempt: attempt + 1, ...latest }) + '\n');
        if (response.status === 429) {
          reason = 'rate_limited'; consecutiveRateLimited++;
          if (consecutiveRateLimited >= 4) { stopping = true; break; }
          const retry = response.headers.get('retry-after');
          await delay(Math.min(180, Math.max(30 * 2 ** attempt, /^\d+$/u.test(retry || '') ? Number(retry) : 0)) * 1000);
          continue;
        }
        consecutiveRateLimited = 0;
        if (response.status >= 500) { reason = 'upstream_unavailable'; await delay(3000 * 2 ** attempt); continue; }
        if (!response.ok) { reason = `http_${response.status}`; break; }
        let parsed;
        try { parsed = parseAppRequirements(bytes.toString('utf8'), game.steamAppId); }
        catch (error) { reason = error.message; break; }
        return {
          entry: { steamAppId: game.steamAppId, name: parsed.name, status: parsed.status, reason: parsed.status === 'not_provided' ? 'requirements_not_published' : null,
            sourceUrl: `https://store.steampowered.com/app/${game.steamAppId}/`, sourceRetrievedAt: retrievedAt, sourceSha256: digest,
            minimum: parsed.minimum, recommended: parsed.recommended },
          provenance: { ...latest, rootKey: parsed.rootKey, verifiedSteamAppId: game.steamAppId, rootKeyMismatch: parsed.rootKeyMismatch }
        };
      } catch (error) {
        reason = stopping ? 'interrupted' : error.message === 'response_too_large' ? 'response_too_large' : 'network_failure';
        if (!stopping && attempt < 3) await delay(3000 * 2 ** attempt);
      } finally { clearTimeout(timer); currentController = null; }
    }
    return {
      entry: { steamAppId: game.steamAppId, name: game.name, status: 'unavailable', reason,
        sourceUrl: `https://store.steampowered.com/app/${game.steamAppId}/`, sourceRetrievedAt: latest?.retrievedAt ?? new Date().toISOString(),
        sourceSha256: latest?.sha256 ?? null, minimum: null, recommended: null },
      provenance: latest ?? { url, retrievedAt: new Date().toISOString(), reason }
    };
  }
  async function priorities() {
    let references = [], adaptations = [];
    try { references = JSON.parse(await readFile(resolve(values.references), 'utf8')).entries ?? []; } catch {}
    try { adaptations = JSON.parse(await readFile(resolve(values.adaptations), 'utf8')).entries ?? []; } catch {}
    return [...new Set(['1091500', '271590', '3240220', '2358720',
      ...references.filter(row => /NVIDIA/iu.test(row.provider || '')).map(row => row.steamAppId),
      ...references.filter(row => /OptiScaler/iu.test(row.provider || '')).map(row => row.steamAppId),
      ...adaptations.map(row => row.steamAppId),
      ...catalogIds.keys()])].filter(id => catalogIds.has(id));
  }
  console.log(JSON.stringify({ started: true, rawDirectory, output, resumed: records.size, total: catalogIds.size, intervalMs }));
  let captured = 0;
  const tried = new Set();
  while (!stopping && captured < maxGames) {
    const queue = await priorities();
    const id = queue.find(appId => !tried.has(appId) && (!records.has(appId) || (values['retry-unavailable'] && records.get(appId).entry.status === 'unavailable')));
    if (!id) break;
    tried.add(id);
    const record = await capture(catalogIds.get(id));
    records.set(id, record);
    await atomic(join(rawDirectory, 'records', `${id}.json`), JSON.stringify(record, null, 2) + '\n');
    captured++;
    if (captured === 4 || captured % checkpointCount === 0) await checkpoint();
  }
  await checkpoint();
  console.log(JSON.stringify({ stopped: stopping, capturedThisRun: captured, processed: records.size, pending: catalogIds.size - records.size, rawDirectory }));
  if (stopping) process.exitCode = 75;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
}
