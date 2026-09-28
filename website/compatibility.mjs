import { isIP } from 'node:net';

export const DEFAULT_COMPATIBILITY_API_BASE = 'https://mu-api.claude-api.cn/api/v1/compatibility/public/';
const nvidiaReferenceUrl = 'https://www.nvidia.com/en-us/geforce/news/nvidia-rtx-games-engines-apps/';
const statuses = new Set(['untested', 'success', 'partial', 'failure', 'mixed']);
const reasons = new Set(['startup_crash', 'load_failed', 'menu_missing', 'game_update', 'anti_cheat', 'visual_artifacts', 'performance', 'unknown']);
class InvalidResponse extends Error {}

function object(value) { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function requireValue(condition) { if (!condition) throw new InvalidResponse('Invalid compatibility response.'); }
function text(value, maximum, empty = false) {
  requireValue(typeof value === 'string' && value.length <= maximum && (empty || value.length > 0)
    && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(value));
  return value;
}
function optional(value, maximum) { return value == null ? null : text(value, maximum, true); }
function identifier(value) { requireValue(typeof value === 'string' && /^[A-Za-z0-9_-]{1,64}$/u.test(value)); return value; }
function date(value, nullable = true) {
  if (value === null && nullable) return null;
  requireValue(typeof value === 'string' && value.length <= 40 && /^\d{4}-\d{2}-\d{2}T/u.test(value) && Number.isFinite(Date.parse(value)));
  return value;
}
function counts(value) {
  requireValue(object(value));
  const { success, partial, failure, total } = value;
  requireValue([success, partial, failure, total].every(n => Number.isSafeInteger(n) && n >= 0)
    && Number.isSafeInteger(success + partial + failure) && total === success + partial + failure);
  return { success, partial, failure, total };
}
function statusFor(value) {
  return value.total === 0 ? 'untested' : value.success === value.total ? 'success'
    : value.failure === value.total ? 'failure' : value.partial === value.total ? 'partial' : 'mixed';
}
function game(value) {
  requireValue(object(value));
  const steamAppId = optional(value.steamAppId, 12);
  requireValue(steamAppId === null || /^\d{1,12}$/u.test(steamAppId));
  return { id: identifier(value.id), name: text(value.name, 200), steamAppId,
    developer: optional(value.developer, 200), engine: optional(value.engine, 200),
    ...(value.catalog == null ? {} : { catalog: catalog(value.catalog, steamAppId) }) };
}
function sourceUrl(value, hosts) {
  let url;
  try { url = new URL(text(value, 1000)); } catch { throw new InvalidResponse(); }
  requireValue(url.protocol === 'https:' && !url.username && !url.password && !url.port && !url.search && !url.hash && hosts.includes(url.hostname));
  return url.href;
}
function catalog(value, appId) {
  requireValue(object(value) && value.provider === 'Steam Store' && Array.isArray(value.references) && value.references.length <= 10);
  const url = sourceUrl(value.url, ['store.steampowered.com']);
  requireValue(new URL(url).pathname === `/app/${appId}/`);
  const releaseDate = optional(value.releaseDate, 10);
  requireValue(releaseDate === null || /^\d{4}-\d{2}-\d{2}$/u.test(releaseDate));
  return { provider: value.provider, url, retrievedAt: date(value.retrievedAt, false), localizedName: optional(value.localizedName, 200), releaseDate,
    references: value.references.map(reference => {
      requireValue(object(reference) && Array.isArray(reference.features) && reference.features.length <= 20);
      const referenceUrl = sourceUrl(reference.url, ['www.nvidia.com']);
      requireValue(referenceUrl === nvidiaReferenceUrl);
      return { provider: text(reference.provider, 64), url: referenceUrl,
        retrievedAt: date(reference.retrievedAt, false), matchedTitle: text(reference.matchedTitle, 200), features: reference.features.map(feature => text(feature, 100)) };
    }) };
}
function aggregate(value, gpu = false) {
  requireValue(object(value));
  const resultCounts = counts(value.counts);
  requireValue(statuses.has(value.status) && value.status === statusFor(resultCounts));
  const lastTestedAt = date(value.lastTestedAt);
  requireValue((resultCounts.total === 0) === (lastTestedAt === null));
  return { ...(gpu ? { gpuName: text(value.gpuName, 160) } : { game: game(value.game) }),
    counts: resultCounts, status: value.status, lastTestedAt,
    ...(!gpu && value.evidence != null ? { evidence: evidence(value.evidence) } : {}) };
}
function evidence(value) {
  requireValue(object(value) && typeof value.hasRequirements === 'boolean'
    && (value.modStatus === null || ['working', 'not_working', 'platform_limited', 'mixed'].includes(value.modStatus)));
  return { hasRequirements: value.hasRequirements, modStatus: value.modStatus };
}
function requirementTier(value) {
  if (value == null) return null;
  requireValue(object(value));
  const result = { text: optional(value.text, 32768) };
  for (const key of ['os', 'processor', 'memory', 'graphics', 'directX', 'storage', 'additionalNotes']) result[key] = optional(value[key], 8192);
  for (const [key, limit] of [['memoryMb', 1048576], ['storageMb', 1073741824]]) {
    requireValue(value[key] === null || (Number.isSafeInteger(value[key]) && value[key] > 0 && value[key] <= limit));
    result[key] = value[key];
  }
  requireValue(typeof result.text === 'string' && result.text.trim().length > 0);
  requireValue((result.memoryMb === null || result.memory) && (result.storageMb === null || result.storage));
  return result;
}
function requirements(value, appId) {
  requireValue(typeof appId === 'string' && /^[1-9]\d{0,9}$/u.test(appId) && Number(appId) <= 4294967295);
  requireValue(object(value) && value.provider === 'Steam Store' && ['available', 'not_provided', 'unavailable'].includes(value.status));
  let url;
  try { url = new URL(text(value.sourceUrl, 2048)); } catch { throw new InvalidResponse(); }
  requireValue(url.protocol === 'https:' && url.hostname === 'store.steampowered.com' && !url.port && !url.username && !url.password && !url.hash);
  const params = [...url.searchParams];
  requireValue((url.pathname === `/app/${appId}/` && !url.search)
    || (url.pathname === '/api/appdetails' && url.searchParams.get('appids') === appId
      && params.every(([key, entry]) => ['appids', 'cc', 'l'].includes(key) && entry.length <= 30 && url.searchParams.getAll(key).length === 1)));
  requireValue(value.sourceSha256 === null || /^[a-f0-9]{64}$/u.test(value.sourceSha256));
  const minimum = requirementTier(value.minimum), recommended = requirementTier(value.recommended);
  const hasContent = Boolean(minimum?.text?.trim() || recommended?.text?.trim());
  requireValue(value.status === 'available' ? hasContent && value.sourceSha256
    : minimum === null && recommended === null && typeof value.reason === 'string' && value.reason.trim().length > 0);
  requireValue(value.status !== 'not_provided' || value.sourceSha256 !== null);
  return { provider: value.provider, status: value.status, reason: optional(value.reason, 1000), sourceUrl: url.href,
    sourceRetrievedAt: date(value.sourceRetrievedAt, false), sourceSha256: value.sourceSha256, minimum, recommended };
}
function adaptation(value) {
  requireValue(object(value) && /^[a-f0-9]{40}$/u.test(value.sourceCommit)
    && ['working', 'not_working', 'platform_limited'].includes(value.status)
    && ['none', 'third_party_upscaler', 'luma_ue'].includes(value.requiredMod) && value.muVerified === false
    && Array.isArray(value.upscalerInputs) && value.upscalerInputs.length <= 20
    && Array.isArray(value.notes) && value.notes.length <= 64);
  const url = sourceUrl(value.sourceUrl, ['github.com']);
  requireValue(new RegExp('^/optiscaler/OptiScaler/wiki/[^/]+/' + value.sourceCommit + '$').test(new URL(url).pathname));
  const env = value.testEnvironment;
  requireValue(env === null || object(env));
  return { id: text(value.id, 100), name: text(value.name, 300), sourceProvider: text(value.sourceProvider, 100), sourceUrl: url,
    sourceRetrievedAt: date(value.sourceRetrievedAt, false), sourceCommit: value.sourceCommit, status: value.status,
    upscalerInputs: value.upscalerInputs.map(item => text(item, 100)), requiredMod: value.requiredMod,
    notes: value.notes.map(item => text(item, 2048)), muVerified: false,
    testEnvironment: env === null ? null : { optiscalerVersion: optional(env.optiscalerVersion, 1000), gpu: optional(env.gpu, 1000), os: optional(env.os, 1000) } };
}
function environment(value) {
  requireValue(object(value) && object(value.gpu));
  const gpu = value.gpu;
  requireValue(gpu.vramMb === null || (Number.isSafeInteger(gpu.vramMb) && gpu.vramMb >= 0 && gpu.vramMb <= 1048576));
  requireValue(['unknown', 'DX11', 'DX12', 'Vulkan'].includes(value.renderApi));
  return {
    gpu: { name: text(gpu.name, 160), vendor: text(gpu.vendor, 64), vramMb: gpu.vramMb,
      architecture: optional(gpu.architecture, 64), driverVersion: text(gpu.driverVersion, 100) },
    osVersion: text(value.osVersion, 200), systemDirectX: text(value.systemDirectX, 100),
    gameVersion: text(value.gameVersion, 100), renderApi: value.renderApi,
    dlssVersion: text(value.dlssVersion, 100), dlss5Version: text(value.dlss5Version, 100),
    toolVersion: text(value.toolVersion, 100), settings: text(value.settings, 2000, true), otherMods: text(value.otherMods, 1000, true)
  };
}
function result(value, gameId) {
  requireValue(object(value) && value.gameId === gameId && /^tester-[a-f0-9]{12}$/u.test(value.tester)
    && ['success', 'partial', 'failure'].includes(value.result));
  const failureReason = optional(value.failureReason, 32);
  requireValue(failureReason === null || reasons.has(failureReason));
  return { id: identifier(value.id), gameId, tester: value.tester, environment: environment(value.environment),
    result: value.result, failureReason, notes: optional(value.notes, 2000), createdAt: date(value.createdAt, false) };
}
function validateSuccess(value, kind, gameId) {
  requireValue(object(value));
  if (kind === 'games') {
    requireValue(Array.isArray(value.items) && value.items.length <= 50);
    const items = value.items.map(item => aggregate(item));
    if (value.total === undefined && value.page === undefined && value.pageSize === undefined && value.catalogTotal === undefined) return { items };
    const { total, page, pageSize, catalogTotal } = value;
    requireValue([total, page, pageSize, catalogTotal].every(Number.isSafeInteger) && total >= 0 && catalogTotal >= total
      && page >= 1 && pageSize >= 1 && pageSize <= 50 && (page - 1) * pageSize <= 2147483647
      && items.length === Math.min(pageSize, Math.max(0, total - (page - 1) * pageSize)));
    let coverage;
    if (value.coverage != null) {
      requireValue(object(value.coverage) && ['requirements', 'modCompatibility'].every(key => Number.isSafeInteger(value.coverage[key])
        && value.coverage[key] >= 0 && value.coverage[key] <= catalogTotal));
      coverage = { requirements: value.coverage.requirements, modCompatibility: value.coverage.modCompatibility };
    }
    return { items, total, page, pageSize, catalogTotal, ...(coverage ? { coverage } : {}) };
  }
  if (kind === 'gpus') {
    requireValue(Array.isArray(value.items) && value.items.length <= 200);
    return { items: value.items.map(item => text(item, 160)) };
  }
  const summary = aggregate(value);
  requireValue(summary.game.id === gameId);
  requireValue(Array.isArray(value.gpus) && Array.isArray(value.tests) && value.tests.length <= 50 && value.gpus.length <= 10000);
  requireValue(value.modCompatibility == null || (Array.isArray(value.modCompatibility) && value.modCompatibility.length <= 50));
  return { ...summary, gpus: value.gpus.map(item => aggregate(item, true)), tests: value.tests.map(item => result(item, summary.game.id)),
    ...(value.requirements == null ? {} : { requirements: requirements(value.requirements, summary.game.steamAppId) }),
    ...(value.modCompatibility == null ? {} : { modCompatibility: value.modCompatibility.map(adaptation) }) };
}

function error(status, code, message, retryAfterSeconds) {
  return { status, body: JSON.stringify({ code, message, ...(retryAfterSeconds ? { retryAfterSeconds } : {}) }),
    headers: retryAfterSeconds ? { 'Retry-After': String(retryAfterSeconds) } : {} };
}
const unavailable = () => error(503, 'compatibility_unavailable', '兼容性查询暂时不可用，请稍后重试。');
const badResponse = () => error(502, 'compatibility_bad_response', '兼容性服务返回了无效数据，请稍后重试。');
const invalidQuery = () => error(400, 'invalid_compatibility_query', '查询参数无效，请检查游戏名称或显卡型号。');

function routeFor(rawUrl) {
  if (rawUrl.length > 4096) return null;
  let url;
  try { url = new URL(rawUrl, 'http://localhost'); } catch { return null; }
  let kind, route, gameId;
  if (url.pathname === '/api/compatibility/games') { kind = 'games'; route = 'games'; }
  else if (url.pathname === '/api/compatibility/gpus') { kind = 'gpus'; route = 'gpus'; }
  else {
    const match = /^\/api\/compatibility\/games\/([A-Za-z0-9_-]{1,64})$/u.exec(url.pathname);
    if (!match) return { error: error(404, 'compatibility_route_not_found', '查询入口不存在。') };
    kind = 'detail'; gameId = match[1]; route = `games/${gameId}`;
  }
  const allowed = kind === 'games' ? new Map([['q', 200], ['gpu', 160], ['page', 10], ['pageSize', 2]])
    : kind === 'detail' ? new Map([['gpu', 160]]) : new Map();
  for (const [key, value] of url.searchParams) {
    if (!allowed.has(key) || url.searchParams.getAll(key).length !== 1 || value.length > allowed.get(key)
      || /[\u0000-\u001f\u007f]/u.test(value)) return null;
  }
  const query = new URLSearchParams();
  for (const key of allowed.keys()) {
    const value = url.searchParams.get(key)?.trim();
    if (value !== undefined && ['page', 'pageSize'].includes(key)
      && (!/^[1-9]\d*$/u.test(value) || !Number.isSafeInteger(Number(value)) || Number(value) > (key === 'pageSize' ? 50 : 2147483647))) return null;
    if (value) query.set(key, value);
  }
  if ((Number(query.get('page') || 1) - 1) * Number(query.get('pageSize') || 50) > 2147483647) return null;
  return { kind, gameId, route: route + (query.size ? `?${query}` : '') };
}

async function readJson(response, maximum) {
  const type = response.headers.get('content-type')?.split(';', 1)[0].trim().toLowerCase();
  if (type !== 'application/json' && !/^application\/[a-z0-9.+-]+\+json$/u.test(type || '')) throw new InvalidResponse();
  const size = response.headers.get('content-length');
  if (size !== null && (!/^\d+$/u.test(size) || Number(size) > maximum)) throw new InvalidResponse();
  if (!response.body) throw new InvalidResponse();
  const reader = response.body.getReader();
  const chunks = [];
  let length = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      length += value.byteLength;
      if (length > maximum) { void reader.cancel().catch(() => {}); throw new InvalidResponse(); }
      chunks.push(Buffer.from(value));
    }
  } finally { reader.releaseLock(); }
  try { return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks, length))); }
  catch { throw new InvalidResponse(); }
}

function retryAfter(response, body, now) {
  const header = response.headers.get('retry-after');
  let seconds = header && /^\d+$/u.test(header) ? Number(header)
    : header && Number.isFinite(Date.parse(header)) ? Math.ceil((Date.parse(header) - now) / 1000)
      : Number.isFinite(body.retryAfterSeconds) ? body.retryAfterSeconds : 60;
  return Math.min(3600, Math.max(1, Math.ceil(seconds)));
}

export function createCompatibilityProxy({
  baseUrl = process.env.COMPATIBILITY_API_BASE || DEFAULT_COMPATIBILITY_API_BASE,
  fetchImpl = globalThis.fetch, timeoutMs = 8000, maxResponseBytes = 2 * 1024 * 1024,
  rateLimit = 120, rateWindowMs = 60000, maxClients = 5000,
  cacheTtlMs = 15000, maxCacheEntries = 64, maxCacheBytes = 8 * 1024 * 1024, maxPending = 32,
  trustCloudflareIp = process.env.COMPATIBILITY_TRUST_CF_IP === '1', now = Date.now
} = {}) {
  const base = new URL(baseUrl);
  if (!['https:', 'http:'].includes(base.protocol) || base.username || base.password || base.search || base.hash)
    throw new TypeError('COMPATIBILITY_API_BASE must be a fixed HTTP(S) base URL without credentials, query, or fragment.');
  if (!base.pathname.endsWith('/')) base.pathname += '/';
  const attempts = new Map(), cache = new Map(), pending = new Map();
  let nextPrune = 0, cacheBytes = 0;

  function removeCached(key) { cacheBytes -= cache.get(key).bytes; cache.delete(key); }
  function prune(time) {
    if (time >= nextPrune || attempts.size >= maxClients) {
      for (const [ip, bucket] of attempts) if (bucket.until <= time) attempts.delete(ip);
      nextPrune = time + Math.min(rateWindowMs, 10000);
    }
    for (const [key, entry] of cache) if (entry.until <= time) removeCached(key);
  }
  function limit(req, time) {
    const supplied = req.headers['cf-connecting-ip'];
    let ip = trustCloudflareIp && typeof supplied === 'string' && isIP(supplied.trim()) ? supplied.trim() : req.socket.remoteAddress || 'unknown';
    if (ip.startsWith('::ffff:') && isIP(ip.slice(7)) === 4) ip = ip.slice(7);
    let bucket = attempts.get(ip);
    if (bucket?.until <= time) { attempts.delete(ip); bucket = null; }
    if (!bucket && attempts.size >= maxClients) return Math.ceil(rateWindowMs / 1000);
    if (!bucket) { bucket = { count: 0, until: time + rateWindowMs }; attempts.set(ip, bucket); }
    if (++bucket.count > rateLimit) return Math.max(1, Math.ceil((bucket.until - time) / 1000));
    return 0;
  }
  async function load(route) {
    const controller = new AbortController();
    let timer;
    try {
      const deadline = new Promise((_, reject) => {
        timer = setTimeout(() => { controller.abort(); reject(new Error('Compatibility upstream timeout.')); }, timeoutMs);
        timer.unref?.();
      });
      return await Promise.race([deadline, (async () => {
        const response = await fetchImpl(new URL(route.route, base), {
          method: 'GET', headers: { Accept: 'application/json', 'User-Agent': 'AMD-DLSS-MU-Compatibility/1.0' },
          redirect: 'manual', credentials: 'omit', signal: controller.signal
        });
        const body = await readJson(response, maxResponseBytes);
        if (response.status === 200) return { status: 200, body: JSON.stringify(validateSuccess(body, route.kind, route.gameId)), headers: {} };
        requireValue(object(body) && typeof body.code === 'string' && /^[a-z][a-z0-9_]{0,99}$/u.test(body.code)
          && typeof body.message === 'string' && body.message.length > 0 && body.message.length <= 2000);
        if (response.status === 404) return error(404, body.code, '未找到该游戏的兼容性记录。');
        if (response.status === 403) return error(403, body.code, '兼容性公开查询暂不可用，请稍后重试。');
        if (response.status === 429) return error(429, body.code, '查询过于频繁，请稍后重试。', retryAfter(response, body, now()));
        if (response.status === 400) return invalidQuery();
        return response.status >= 500 ? unavailable() : badResponse();
      })()]);
    } catch (failure) { return failure instanceof InvalidResponse ? badResponse() : unavailable(); }
    finally { clearTimeout(timer); controller.abort(); }
  }
  async function retrieve(route) {
    const key = route.route;
    const cached = cache.get(key);
    if (cached && cached.until > now()) {
      cache.delete(key); cache.set(key, cached);
      return cached.response;
    }
    if (cached) removeCached(key);
    if (pending.has(key)) return pending.get(key);
    if (pending.size >= maxPending) return unavailable();
    const work = load(route).then(response => {
      const bytes = Buffer.byteLength(response.body);
      if (response.status === 200 && cacheTtlMs > 0 && maxCacheEntries > 0 && bytes <= maxCacheBytes) {
        while (cache.size >= maxCacheEntries || cacheBytes + bytes > maxCacheBytes) removeCached(cache.keys().next().value);
        cache.set(key, { response, bytes, until: now() + cacheTtlMs }); cacheBytes += bytes;
      }
      return response;
    }).finally(() => pending.delete(key));
    pending.set(key, work);
    return work;
  }
  return async function handleCompatibility(req, res) {
    function send(response) {
      if (res.destroyed || res.writableEnded) return;
      res.writeHead(response.status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store',
        'X-Content-Type-Options': 'nosniff', 'Cross-Origin-Resource-Policy': 'same-origin', ...response.headers });
      res.end(response.body);
    }
    const time = now();
    prune(time);
    const retry = limit(req, time);
    if (retry) return send(error(429, 'rate_limited', '查询过于频繁，请稍后重试。', retry));
    if (req.method !== 'GET') return send({ ...error(405, 'compatibility_read_only', '官网仅提供查询，请使用客户端提交实测结果。'), headers: { Allow: 'GET' } });
    const route = routeFor(req.url || '/');
    if (!route) return send(invalidQuery());
    if (route.error) return send(route.error);
    send(await retrieve(route));
  };
}
