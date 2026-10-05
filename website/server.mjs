import { createMirrorHandler } from './mirrors.mjs';
import { createServer } from 'node:http';
import { createReadStream, existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
import { appendFile, rename, writeFile, unlink } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';
import { randomBytes, scryptSync, timingSafeEqual, createHash } from 'node:crypto';
import { createAnalytics, syncGithub, sessionId, entryName } from './analytics.mjs';
import { createSurveyStore, surveyDefinition, validateSurvey } from './survey.mjs';
import { createPackageStore, sourceUrl } from './packages.mjs';
import { createCompatibilityProxy } from './compatibility.mjs';
import { publicPages, canonicalRedirect, sitemapXml, renderSeoPage } from './seo.mjs';
import { createUpdateStore } from './updates.mjs';

const port = Number(process.env.PORT || 8080);
const host = process.env.HOST || '0.0.0.0';
const publicRoot = resolve(process.env.PUBLIC_ROOT || '/app/public');
const dataRoot = resolve(process.env.DATA_ROOT || '/app/data');
const feedbackFile = join(dataRoot, 'feedback.jsonl');
const releaseFile = join(dataRoot, 'release.json');
const defaultReleaseFile = join(publicRoot, 'release.json');
const publicOrigin = process.env.PUBLIC_ORIGIN || `http://${host}:${port}`;
const adminPasswordHash = process.env.ADMIN_PASSWORD_HASH || '';
const maxBody = 32 * 1024;
const rateWindowMs = 10 * 60 * 1000;
const rateLimit = 5;
const attempts = new Map();
const loginAttempts = new Map();
const sessions = new Map();
const metricAttempts = new Map();
const surveyAttempts = new Map();
const handleCompatibility = createCompatibilityProxy();

process.umask(0o077);
mkdirSync(dataRoot, { recursive: true });
const packages = createPackageStore(dataRoot);
const updates = createUpdateStore(dataRoot, packages, { origin: publicOrigin, current: readRelease });
const handleMirror = createMirrorHandler(dataRoot);
let releaseJob = null;
let analytics;
let analyticsErrorAt = null;
try { analytics = createAnalytics(dataRoot); }
catch (error) { analyticsErrorAt = Date.now(); console.error('Analytics initialization failed:', error.message); }
let surveys;
try { surveys = createSurveyStore(dataRoot); }
catch (error) { console.error('Survey initialization failed:', error.message); }

function measure(action) {
  if (!analytics) return;
  try { return action(analytics); }
  catch (error) { analyticsErrorAt = Date.now(); console.error('Analytics write failed:', error.message); }
}

function cookieValue(req, name) {
  return req.headers.cookie?.split(';').map(part => part.trim()).find(part => part.startsWith(`${name}=`))?.slice(name.length + 1) || '';
}

function isBot(req) { return /bot|spider|crawl|headless|curl|wget|MU-Healthcheck|Playwright/i.test(req.headers['user-agent'] || ''); }
function excluded(req) { return cookieValue(req, 'mu_metrics_exclude') === '1' || isBot(req); }

function visitId(req) { return sessionId(cookieValue(req, 'mu_visit')); }

let githubRefreshing = false;
async function refreshGithub() {
  if (!analytics || githubRefreshing) return;
  githubRefreshing = true;
  try { await syncGithub(analytics); }
  catch { measure(store => store.githubError()); }
  finally { githubRefreshing = false; }
}
if (process.env.GITHUB_SYNC_DISABLED !== '1') {
  refreshGithub();
  setInterval(refreshGithub, 15 * 60 * 1000).unref();
}
setInterval(() => {
  measure(store => store.prune());
  const now = Date.now();
  for (const [token, expires] of sessions) if (expires <= now) sessions.delete(token);
  for (const [ip, times] of metricAttempts) if (now - times[0] > rateWindowMs) metricAttempts.delete(ip);
}, 60 * 60 * 1000).unref();

const mime = {
  '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8', '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml', '.png': 'image/png', '.exe': 'application/vnd.microsoft.portable-executable',
  '.txt': 'text/plain; charset=utf-8', '.mp4': 'video/mp4'
};

function send(res, status, body, type = 'application/json; charset=utf-8') {
  res.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
  res.end(type.startsWith('application/json') ? JSON.stringify(body) : body);
}

function readRelease() {
  return localRelease(JSON.parse(readFileSync(existsSync(releaseFile) ? releaseFile : defaultReleaseFile, 'utf8')));
}

function localRelease(release) {
  return { ...release, sourceUrl: sourceUrl(release), downloadUrl: `${publicOrigin}/download/file`, delivery: 'server' };
}

function startRelease(release) {
  const job = { id: randomBytes(12).toString('hex'), tag: release.tag, state: 'running', phase: 'preparing', bytes: 0, total: release.size };
  releaseJob = job;
  (async () => {
    let temp;
    try {
      updates.assertNew(release);
      await packages.ensure(release, progress => Object.assign(job, progress));
      job.phase = 'publishing';
      const previous = readRelease();
      const active = localRelease(release);
      await updates.remember(active);
      temp = `${releaseFile}.tmp-${randomBytes(8).toString('hex')}`;
      await writeFile(temp, `${JSON.stringify(active, null, 2)}\n`, { mode: 0o600, flag: 'wx' });
      await rename(temp, releaseFile);
      job.state = 'complete'; job.phase = 'ready'; job.release = active;
      measure(store => store.record('release_published', { tag: release.tag, result: 'success', detail: `${previous.tag} → ${release.tag} · server` }));
      if (process.env.GITHUB_SYNC_DISABLED !== '1') refreshGithub();
    } catch (error) {
      job.state = 'failed'; job.message = `${error.message || '服务器同步失败。'} 旧下载版本保持不变。`;
      measure(store => store.record('release_publish_failed', { tag: release.tag, result: 'mirror_or_write_failed' }));
    } finally {
      if (temp) await unlink(temp).catch(() => {});
    }
  })();
  return job;
}

function sessionToken(req) {
  const cookie = req.headers.cookie?.split(';').map(part => part.trim()).find(part => part.startsWith('mu_admin='));
  return cookie?.slice('mu_admin='.length) || '';
}

function authenticated(req) {
  const token = sessionToken(req);
  const expires = sessions.get(token);
  if (!expires) return false;
  if (expires < Date.now()) { sessions.delete(token); return false; }
  return true;
}

function sameOrigin(req) {
  return req.headers.origin === publicOrigin;
}

function setSessionCookie(res, token, maxAge) {
  const secure = publicOrigin.startsWith('https:') ? '; Secure' : '';
  res.setHeader('Set-Cookie', [
    `mu_admin=${token}; HttpOnly; SameSite=Strict; Path=/api/admin; Max-Age=${maxAge}${secure}`,
    `mu_metrics_exclude=${maxAge ? '1' : ''}; HttpOnly; SameSite=Strict; Path=/; Max-Age=${maxAge}${secure}`
  ]);
}

function passwordMatches(password) {
  if (!adminPasswordHash || typeof password !== 'string' || password.length > 200) return false;
  const [salt, expected] = adminPasswordHash.split(':');
  if (!/^[a-f0-9]{32}$/.test(salt || '') || !/^[a-f0-9]{128}$/.test(expected || '')) return false;
  const actual = scryptSync(password, Buffer.from(salt, 'hex'), 64);
  return timingSafeEqual(actual, Buffer.from(expected, 'hex'));
}

async function collectSmallJson(req) {
  if (!req.headers['content-type']?.startsWith('application/json')) throw new Error('仅支持 JSON 请求。');
  const chunks = [];
  let length = 0;
  for await (const chunk of req) {
    length += chunk.length;
    if (length > 8192) throw new Error('请求内容过大。');
    chunks.push(chunk);
  }
  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

async function githubRelease(tag) {
  if (typeof tag !== 'string' || !/^v\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$/.test(tag)) throw new Error('版本标签格式无效。');
  const response = await fetch(`https://api.github.com/repos/xiarongwu123/AMD-DLSS-MU/releases/tags/${encodeURIComponent(tag)}`, {
    headers: { 'Accept': 'application/vnd.github+json', 'User-Agent': 'AMD-DLSS-MU-Site/1.0' },
    signal: AbortSignal.timeout(10000)
  });
  if (!response.ok) throw new Error(response.status === 404 ? 'GitHub 上找不到这个正式发布版本。' : '暂时无法读取 GitHub Release。');
  const release = await response.json();
  const asset = release.assets?.find(item => item.name === 'AMD-DLSS-MU.exe');
  const downloadUrl = `https://github.com/xiarongwu123/AMD-DLSS-MU/releases/download/${tag}/AMD-DLSS-MU.exe`;
  if (release.draft || release.prerelease || release.tag_name !== tag || !asset ||
      asset.browser_download_url !== downloadUrl || !Number.isSafeInteger(asset.size) ||
      asset.size < 1024 * 1024 || asset.size > 2 * 1024 * 1024 * 1024 ||
      !/^sha256:[a-f0-9]{64}$/i.test(asset.digest || '')) throw new Error('Release 附件缺失或校验信息无效。');
  const sizeDisplay = `${(asset.size / 1024 / 1024).toFixed(1)} MiB`;
  return {
    version: tag.slice(1), tag, channel: 'stable', platform: 'Windows x64',
    file: 'AMD-DLSS-MU.exe', size: asset.size, sizeDisplay,
    sha256: asset.digest.slice(7).toLowerCase(),
    notes: typeof release.body === 'string' ? release.body.slice(0, 4000) : '',
    publishedAt: release.published_at.slice(0, 10), downloadUrl,
    releaseUrl: `https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/${tag}`
  };
}

async function handleAdmin(req, res, pathname) {
  if (!adminPasswordHash) return send(res, 503, { message: '管理员后台尚未配置。' });
  if (['POST', 'PUT', 'DELETE'].includes(req.method) && !sameOrigin(req)) return send(res, 403, { message: '请求来源无效。' });
  if (pathname === '/api/admin/login' && req.method === 'POST') {
    const ip = clientIp(req);
    const now = Date.now();
    const recent = (loginAttempts.get(ip) || []).filter(time => now - time < 15 * 60 * 1000);
    if (recent.length >= 5) return send(res, 429, { message: '尝试过多，请 15 分钟后重试。' });
    try {
      const input = await collectSmallJson(req);
      if (!passwordMatches(input.password)) {
        recent.push(now); loginAttempts.set(ip, recent);
        measure(store => store.record('admin_login_failed', { result: 'invalid_password' }));
        return send(res, 401, { message: '密码错误。' });
      }
      loginAttempts.delete(ip);
      const token = randomBytes(32).toString('hex');
      sessions.set(token, now + 12 * 60 * 60 * 1000);
      setSessionCookie(res, token, 12 * 60 * 60);
      return send(res, 200, { ok: true });
    } catch { return send(res, 400, { message: '登录请求无效。' }); }
  }
  if (pathname === '/api/admin/session' && req.method === 'GET' && !authenticated(req)) return send(res, 200, { ok: false });
  if (!authenticated(req)) return send(res, 401, { message: '请先登录管理员后台。' });
  if (pathname === '/api/admin/uploads' && req.method === 'POST') {
    try { return send(res, 201, await updates.create(await collectSmallJson(req), sessionToken(req))); }
    catch (error) { return send(res, 400, { message: error.message }); }
  }
  const uploadRoute = /^\/api\/admin\/uploads\/([a-f0-9]{32})(?:\/(complete|publish))?$/.exec(pathname);
  if (uploadRoute) {
    const [, id, action] = uploadRoute, owner = sessionToken(req);
    try {
      if (!action && req.method === 'GET') return send(res, 200, updates.status(id, owner));
      if (!action && req.method === 'DELETE') { await updates.discard(id, owner); return send(res, 200, { ok: true }); }
      if (!action && req.method === 'PUT') {
        const offset = new URL(req.url, publicOrigin).searchParams.get('offset');
        if (!/^\d+$/.test(offset || '')) throw new Error('分片进度无效。');
        return send(res, 200, await updates.append(id, owner, Number(offset), req));
      }
      if (action === 'complete' && req.method === 'POST') return send(res, 200, { release: localRelease(await updates.complete(id, owner)) });
      if (action === 'publish' && req.method === 'POST') {
        if (releaseJob?.state === 'running') return send(res, 409, { message: '已有版本正在发布，请等待完成。' });
        const release = updates.status(id, owner).release;
        if (!release) throw new Error('请先完成上传和校验。');
        updates.assertNew(release);
        return send(res, 202, { job: startRelease(release) });
      }
      return send(res, 405, { message: 'Method not allowed' });
    } catch (error) { return send(res, 400, { message: error.message || '上传处理失败，线上版本未变更。' }); }
  }
  if (pathname === '/api/admin/session' && req.method === 'GET') return send(res, 200, { ok: true, release: readRelease() });
  if (pathname === '/api/admin/release-status' && req.method === 'GET') return send(res, 200, { job: releaseJob });
  if (pathname === '/api/admin/survey' && req.method === 'GET') {
    if (!surveys) return send(res, 503, { message: '问卷数据库暂时不可用。' });
    const query = new URL(req.url, 'http://localhost').searchParams;
    if (!['all', '1', '7', '30', '90'].includes(query.get('days') || 'all')) return send(res, 400, { message: '时间范围无效。' });
    try { return send(res, 200, surveys.report(query.get('days') || 'all', query.get('page'))); }
    catch { return send(res, 503, { message: '读取问卷失败，请稍后重试。' }); }
  }
  if (pathname === '/api/admin/overview' && req.method === 'GET') {
    if (!analytics) return send(res, 503, { message: '统计数据库暂时不可用。' });
    const query = new URL(req.url, 'http://localhost').searchParams;
    return send(res, 200, { ...analytics.overview(query.get('days')), release: readRelease(), analyticsErrorAt });
  }
  if (pathname === '/api/admin/feedback' && req.method === 'GET') {
    if (!analytics) return send(res, 503, { message: '反馈索引暂时不可用。' });
    const query = new URL(req.url, 'http://localhost').searchParams;
    return send(res, 200, analytics.feedbackList(query.get('status') || 'all', query.get('page')));
  }
  if (pathname === '/api/admin/feedback/status' && req.method === 'POST') {
    if (!analytics) return send(res, 503, { message: '反馈索引暂时不可用。' });
    try {
      const input = await collectSmallJson(req);
      analytics.feedbackStatus(clean(input.id, 80), input.status);
      return send(res, 200, { ok: true });
    } catch (error) { return send(res, 400, { message: error.message || '状态更新失败。' }); }
  }
  if (pathname === '/api/admin/logout' && req.method === 'POST') {
    sessions.delete(sessionToken(req));
    setSessionCookie(res, '', 0);
    return send(res, 200, { ok: true });
  }
  if ((pathname === '/api/admin/preview' || pathname === '/api/admin/release') && req.method === 'POST') {
    try {
      const input = await collectSmallJson(req);
      if (pathname === '/api/admin/release' && releaseJob?.state === 'running') return send(res, 409, { message: '已有安装包正在同步，请等待完成。' });
      const release = await githubRelease(input.tag);
      if (pathname === '/api/admin/release') {
        if (releaseJob?.state === 'running') return send(res, 409, { message: '已有安装包正在同步，请等待完成。' });
        return send(res, 202, { job: startRelease(release) });
      }
      return send(res, 200, { release: localRelease(release) });
    } catch (error) {
      if (pathname === '/api/admin/release') measure(store => store.record('release_publish_failed', { result: 'validation_or_write_failed' }));
      return send(res, 400, { message: error.message || '无法更新版本。' });
    }
  }
  return send(res, 405, { message: 'Method not allowed' });
}

function clean(value, max) {
  return typeof value === 'string' ? value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, '').trim().slice(0, max) : '';
}

function clientIp(req) {
  const cfIp = req.headers['cf-connecting-ip'];
  return (typeof cfIp === 'string' ? cfIp : req.socket.remoteAddress || 'unknown').trim();
}

function allowed(ip) {
  const now = Date.now();
  const recent = (attempts.get(ip) || []).filter((time) => now - time < rateWindowMs);
  if (recent.length >= rateLimit) return false;
  recent.push(now);
  attempts.set(ip, recent);
  return true;
}

async function collectJson(req) {
  return await new Promise((resolveBody, reject) => {
    let size = 0;
    const chunks = [];
    req.on('data', (chunk) => {
      size += chunk.length;
      if (size > maxBody) {
        reject(new Error('请求内容过大。'));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => {
      try { resolveBody(JSON.parse(Buffer.concat(chunks).toString('utf8'))); }
      catch { reject(new Error('请求格式无效。')); }
    });
    req.on('error', reject);
  });
}

async function handleFeedback(req, res) {
  if (!req.headers['content-type']?.startsWith('application/json')) return send(res, 415, { message: '仅支持 JSON 请求。' });
  const ip = clientIp(req);
  if (!allowed(ip)) return send(res, 429, { message: '提交过于频繁，请十分钟后再试。' });
  try {
    const input = await collectJson(req);
    if (clean(input.website, 200)) return send(res, 200, { id: 'accepted' });
    const report = {
      id: `MU-${new Date().toISOString().slice(0, 10).replaceAll('-', '')}-${randomBytes(3).toString('hex').toUpperCase()}`,
      createdAt: new Date().toISOString(),
      category: clean(input.category, 40), appVersion: clean(input.appVersion, 30),
      game: clean(input.game, 120), gpu: clean(input.gpu, 100), driver: clean(input.driver, 80),
      mode: clean(input.mode, 30), resolution: clean(input.resolution, 40),
      fpsBefore: clean(input.fpsBefore, 12), fpsAfter: clean(input.fpsAfter, 12),
      details: clean(input.details, 4000), contact: clean(input.contact, 120)
    };
    if (!report.category || !report.appVersion || !report.game || !report.gpu || !report.mode || report.details.length < 10 || input.privacyConfirmed !== true) {
      if (!excluded(req)) measure(store => store.record('feedback_rejected', { sid: visitId(req), result: 'validation' }));
      return send(res, 400, { message: '请补全必填项，并确认隐私检查。' });
    }
    await appendFile(feedbackFile, `${JSON.stringify(report)}\n`, { encoding: 'utf8', mode: 0o600 });
    measure(store => store.feedback(report));
    if (!excluded(req)) measure(store => store.record('feedback_accepted', { sid: visitId(req), category: report.category, tag: report.appVersion }));
    return send(res, 201, { id: report.id });
  } catch (error) {
    return send(res, 400, { message: error.message || '请求处理失败。' });
  }
}

async function handleEvents(req, res) {
  if (!sameOrigin(req)) return send(res, 403, { message: '请求来源无效。' });
  if (excluded(req) || !visitId(req)) { res.writeHead(204, { 'Cache-Control': 'no-store' }); return res.end(); }
  const ip = clientIp(req);
  const now = Date.now();
  const recent = (metricAttempts.get(ip) || []).filter(time => now - time < rateWindowMs);
  if (recent.length >= 1200) return send(res, 429, { message: '请求过于频繁。' });
  recent.push(now);
  if (metricAttempts.size >= 5000 && !metricAttempts.has(ip)) metricAttempts.delete(metricAttempts.keys().next().value);
  metricAttempts.set(ip, recent);
  try {
    const input = await collectSmallJson(req);
    if (!Array.isArray(input.events) || input.events.length > 20) return send(res, 400, { message: '事件格式无效。' });
    let accepted = 0;
    for (const event of input.events) {
      if (event && typeof event === 'object' && measure(store => store.browser(event, visitId(req)))) accepted += 1;
    }
    return send(res, 202, { accepted });
  } catch { return send(res, 400, { message: '事件格式无效。' }); }
}

async function handleSurvey(req, res) {
  if (!['GET', 'POST'].includes(req.method)) return send(res, 405, { message: 'Method not allowed' });
  if ((req.headers.origin && !sameOrigin(req)) || req.headers['sec-fetch-site'] === 'cross-site' ||
      (req.method === 'POST' && !sameOrigin(req))) return send(res, 403, { message: '请求来源无效。' });
  if (!surveys) return send(res, 503, { message: '问卷暂时无法保存或读取，请稍后重试。' });
  res.setHeader('Vary', 'Cookie');
  let token = cookieValue(req, 'mu_survey');
  const validToken = /^[a-f0-9]{64}$/.test(token);
  if (req.method === 'GET') {
    if (!validToken) token = randomBytes(32).toString('hex');
    res.setHeader('Set-Cookie', `mu_survey=${token}; HttpOnly; SameSite=Strict; Path=/api/survey; Max-Age=31536000${publicOrigin.startsWith('https:') ? '; Secure' : ''}`);
  } else {
    if (!validToken) return send(res, 428, { message: '请启用本站必要 Cookie 并重新加载问卷；当前填写内容不会被清空。' });
    if (!req.headers['content-type']?.startsWith('application/json')) return send(res, 415, { message: '仅支持 JSON 请求。' });
    const now = Date.now();
    const ip = clientIp(req);
    for (const [key, times] of surveyAttempts) if (now - times.at(-1) >= rateWindowMs) surveyAttempts.delete(key);
    const recent = (surveyAttempts.get(ip) || []).filter(time => now - time < rateWindowMs);
    if (recent.length >= 30 || (!surveyAttempts.has(ip) && surveyAttempts.size >= 5000)) {
      res.setHeader('Retry-After', '600');
      return send(res, 429, { message: '提交过于频繁，请十分钟后再试。' });
    }
    recent.push(now); surveyAttempts.set(ip, recent);
  }
  const hash = createHash('sha256').update(token).digest('hex');
  let input;
  if (req.method === 'POST') {
    try { input = await collectSmallJson(req); validateSurvey(input); }
    catch (error) { return send(res, 400, { message: error.message || '问卷内容无效。' }); }
  }
  try {
    if (req.method === 'GET') return send(res, 200, { definition: surveyDefinition, response: surveys.get(hash) });
    // Unlike optional analytics, a survey write must commit before acknowledgement.
    return send(res, 200, { response: surveys.save(hash, input) });
  } catch (error) {
    console.error('Survey storage failed:', error.code || 'database_error');
    return send(res, 503, { message: '问卷存储暂时不可用。填写内容已保留，请稍后重试。' });
  }
}

function serveFile(req, res, pathname) {
  const relative = publicPages.get(pathname)?.file || pathname.replace(/^\/+/, '');
  const file = resolve(publicRoot, normalize(relative));
  if (!file.startsWith(`${publicRoot}/`) || !existsSync(file) || !statSync(file).isFile()) return send(res, 404, '404 · SIGNAL LOST', 'text/plain; charset=utf-8');
  const extension = extname(file).toLowerCase();
  if (extension === '.exe') return send(res, 404, '请使用官网下载入口。', 'text/plain; charset=utf-8');
  const fileSize = statSync(file).size;
  const rendered = extension === '.html' && publicPages.has(pathname)
    ? renderSeoPage(readFileSync(file, 'utf8'), pathname, {
      google: process.env.GOOGLE_SITE_VERIFICATION, baidu: process.env.BAIDU_SITE_VERIFICATION
    }) : null;
  const headers = {
    'Content-Type': mime[extension] || 'application/octet-stream',
    'X-Content-Type-Options': 'nosniff',
    'X-Frame-Options': 'DENY',
    'Referrer-Policy': 'strict-origin-when-cross-origin',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
    'Content-Security-Policy': `default-src 'self'; img-src 'self'; style-src 'self'; script-src 'self'${rendered?.scriptHash ? ` 'sha256-${rendered.scriptHash}'` : ''}; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`,
    'Cache-Control': pathname.startsWith('/assets/') ? 'public, max-age=86400' : 'no-cache',
    'Content-Length': rendered ? Buffer.byteLength(rendered.html) : fileSize
  };
  let status = 200;
  let start = 0;
  let end = fileSize - 1;
  if (extension === '.mp4') {
    headers['Accept-Ranges'] = 'bytes';
    if (req.method === 'GET' && req.headers.range && !req.headers['if-range']) {
      const match = /^bytes=(\d*)-(\d*)$/.exec(req.headers.range);
      if (match && (match[1] || match[2])) {
        start = match[1] ? Number(match[1]) : Math.max(0, fileSize - Number(match[2]));
        end = match[1] && match[2] ? Math.min(Number(match[2]), fileSize - 1) : fileSize - 1;
      }
      if (!match || (!match[1] && !match[2]) || !Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start > end || start >= fileSize) {
        res.writeHead(416, { ...headers, 'Content-Range': `bytes */${fileSize}`, 'Content-Length': 0 });
        return res.end();
      }
      status = 206;
      headers['Content-Range'] = `bytes ${start}-${end}/${fileSize}`;
      headers['Content-Length'] = end - start + 1;
    }
  }
  res.writeHead(status, headers);
  if (req.method === 'HEAD') return res.end();
  if (rendered) return res.end(rendered.html);
  const stream = createReadStream(file, extension === '.mp4' ? { start, end } : undefined);
  stream.on('error', () => res.destroy());
  res.on('close', () => stream.destroy());
  return stream.pipe(res);
}

createServer(async (req, res) => {
  try {
    const url = new URL(req.url || '/', 'http://localhost');
    if (url.pathname === '/admin' || url.pathname === '/admin.html' || url.pathname.startsWith('/api/') ||
        url.pathname.startsWith('/download/') || url.pathname.startsWith('/files/') || url.pathname.startsWith('/mirrors/') || url.pathname.startsWith('/updates/')) {
      res.setHeader('X-Robots-Tag', 'noindex, nofollow');
    }
    if (req.method === 'GET' || req.method === 'HEAD') {
      const redirect = canonicalRedirect(url.pathname);
      if (redirect !== null) {
        res.writeHead(301, { Location: redirect + url.search, 'Cache-Control': 'public, max-age=3600' });
        return res.end();
      }
      if (url.pathname === '/sitemap.xml') {
        const xml = sitemapXml();
        res.writeHead(200, { 'Content-Type': 'application/xml; charset=utf-8', 'Cache-Control': 'public, max-age=3600', 'Content-Length': Buffer.byteLength(xml), 'X-Content-Type-Options': 'nosniff' });
        return res.end(req.method === 'HEAD' ? undefined : xml);
      }
    }
    if (await handleMirror(req, res)) return;
    const measuredRoutes = new Set(['/api/survey', '/api/feedback', '/api/admin/login', '/api/admin/preview', '/api/admin/release', '/download/file', '/files/AMD-DLSS-MU.exe']);
    if (req.method !== 'HEAD' && measuredRoutes.has(url.pathname) && !isBot(req) &&
        (!excluded(req) || url.pathname.startsWith('/api/admin/'))) {
      const started = performance.now();
      res.once('finish', () => measure(store => store.request(url.pathname, res.statusCode, performance.now() - started)));
    }
    if (req.method === 'GET' && url.pathname === '/api/health') return send(res, 200, { ok: true, service: 'amd-dlss-mu' });
    if (url.pathname === '/api/compatibility' || url.pathname.startsWith('/api/compatibility/')) return await handleCompatibility(req, res);
    if (req.method === 'POST' && url.pathname === '/api/events') return await handleEvents(req, res);
    if (req.method === 'POST' && url.pathname === '/api/feedback') return await handleFeedback(req, res);
    if (url.pathname === '/api/survey') return await handleSurvey(req, res);
    if (url.pathname.startsWith('/api/admin/')) return await handleAdmin(req, res, url.pathname);
    if (url.pathname === '/api/updates/latest' && req.method === 'GET') {
      try {
        const release = readRelease();
        await packages.check(release);
        await updates.remember(release);
        return send(res, 200, updates.describe(release));
      } catch { return send(res, 503, { message: '官网更新暂不可用，请稍后重试。' }); }
    }
    const updateRoute = /^\/updates\/(v\d+\.\d+\.\d+)\/([a-f0-9]{64})\/AMD-DLSS-MU\.exe$/.exec(url.pathname);
    if (updateRoute && ['GET', 'HEAD'].includes(req.method)) {
      try { return await packages.serve(req, res, await updates.find(updateRoute[1], updateRoute[2])); }
      catch { return send(res, 404, { message: '该更新包不存在。' }); }
    }
    if (req.method === 'GET' && url.pathname === '/release.json') return send(res, 200, readRelease());
    if ((req.method === 'GET' || req.method === 'HEAD') &&
        url.pathname === '/download/AMD-DLSS-MU-v2.0.0-preview.1-e3b9b13.exe') {
      return await packages.serve(req, res, {
        tag: 'v2.0.0-preview.1-e3b9b13', size: 193311743,
        sha256: 'df34a28255a3a68e244096d742c508001f488767cb46d6f5a904f450cf9a3070'
      });
    }
    if ((req.method === 'GET' || req.method === 'HEAD') &&
        (url.pathname === '/download/file' || url.pathname === '/files/AMD-DLSS-MU.exe')) {
      const release = readRelease();
      return await packages.serve(req, res, release, () => { if (!excluded(req)) measure(store => store.record('download_request', {
        sid: visitId(req), tag: release.tag,
        entry: url.pathname.startsWith('/files/') ? 'legacy' : entryName(url.searchParams.get('entry'))
      })); });
    }
    if (req.method !== 'GET' && req.method !== 'HEAD') return send(res, 405, { message: 'Method not allowed' });
    serveFile(req, res, decodeURIComponent(url.pathname === '/admin' ? '/admin.html' : url.pathname));
  } catch {
    if (!res.headersSent) send(res, 500, { message: '服务暂时不可用。' });
    else res.destroy();
  }
}).listen(port, host, function () { console.log(`AMD DLSS MU website listening on ${host}:${this.address().port}`); });
