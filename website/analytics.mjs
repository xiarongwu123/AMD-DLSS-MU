import { DatabaseSync } from 'node:sqlite';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';

export const browserEvents = new Set(['page_view', 'download_click', 'feedback_start', 'hash_copy', 'session_heartbeat']);
const dayMs = 86400000;
const chinaOffset = 8 * 3600000;
const pages = new Set(['/', '/download', '/guide', '/feedback']);
const sources = new Set(['direct', 'github', 'bilibili', 'douyin', 'xiaohongshu', 'search', 'other']);
const entries = new Set(['header', 'hero', 'download_card', 'footer', 'legacy', 'direct']);
const dateKey = value => new Date(value + chinaOffset).toISOString().slice(0, 10);
const text = (value, length = 80) => typeof value === 'string' ? value.slice(0, length) : '';
export const sessionId = value => /^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/i.test(value || '') ? value : '';
export const pageName = value => pages.has(value) ? value : '/';
export const entryName = value => entries.has(value) ? value : 'direct';

export function createAnalytics(dataRoot) {
  const db = new DatabaseSync(join(dataRoot, 'analytics.sqlite'));
  db.exec(`
    PRAGMA journal_mode=WAL;
    PRAGMA busy_timeout=5000;
    CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS events (
      id TEXT PRIMARY KEY, at INTEGER NOT NULL, name TEXT NOT NULL, sid TEXT NOT NULL DEFAULT '',
      page TEXT NOT NULL DEFAULT '', release_tag TEXT NOT NULL DEFAULT '', category TEXT NOT NULL DEFAULT '',
      result TEXT NOT NULL DEFAULT '', entry_point TEXT NOT NULL DEFAULT '', detail TEXT NOT NULL DEFAULT ''
    );
    CREATE INDEX IF NOT EXISTS events_time ON events(at);
    CREATE INDEX IF NOT EXISTS events_name_time ON events(name, at);
    CREATE INDEX IF NOT EXISTS events_session ON events(sid, name, at);
    CREATE TABLE IF NOT EXISTS sessions (
      id TEXT PRIMARY KEY, started_at INTEGER NOT NULL, last_seen INTEGER NOT NULL,
      source TEXT NOT NULL, campaign TEXT NOT NULL DEFAULT ''
    );
    CREATE TABLE IF NOT EXISTS requests (
      id INTEGER PRIMARY KEY, at INTEGER NOT NULL, route TEXT NOT NULL,
      status INTEGER NOT NULL, duration_ms INTEGER NOT NULL
    );
    CREATE INDEX IF NOT EXISTS requests_time ON requests(at);
    CREATE TABLE IF NOT EXISTS feedback (
      id TEXT PRIMARY KEY, at INTEGER NOT NULL, category TEXT NOT NULL, app_version TEXT NOT NULL,
      game TEXT NOT NULL, gpu TEXT NOT NULL, mode TEXT NOT NULL, payload TEXT NOT NULL,
      status TEXT NOT NULL DEFAULT 'new', updated_at INTEGER NOT NULL
    );
    CREATE INDEX IF NOT EXISTS feedback_time ON feedback(at);
    CREATE TABLE IF NOT EXISTS github_assets (
      asset_id INTEGER PRIMARY KEY, tag TEXT NOT NULL, downloads INTEGER NOT NULL,
      published_at TEXT NOT NULL, synced_at INTEGER NOT NULL
    );
    CREATE TABLE IF NOT EXISTS github_snapshots (
      asset_id INTEGER NOT NULL, day TEXT NOT NULL, downloads INTEGER NOT NULL, at INTEGER NOT NULL,
      PRIMARY KEY(asset_id, day)
    );
  `);
  const setMeta = db.prepare('INSERT INTO meta(key,value) VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value');
  const getMeta = key => db.prepare('SELECT value FROM meta WHERE key=?').get(key)?.value;
  if (!getMeta('started_at')) setMeta.run('started_at', String(Date.now()));
  const insertEvent = db.prepare(`INSERT OR IGNORE INTO events
    (id,at,name,sid,page,release_tag,category,result,entry_point,detail) VALUES (?,?,?,?,?,?,?,?,?,?)`);

  function record(name, fields = {}) {
    return insertEvent.run(fields.id || randomUUID(), fields.at || Date.now(), name,
      sessionId(fields.sid), text(fields.page), text(fields.tag, 40), text(fields.category, 40),
      text(fields.result, 40), text(fields.entry, 24), text(fields.detail, 160)).changes > 0;
  }

  function browser(event, sid) {
    if (!browserEvents.has(event.name) || !sessionId(sid) || !sessionId(event.id) || !pages.has(event.page)) return false;
    const source = sources.has(event.source) ? event.source : 'other';
    const campaign = /^[a-zA-Z0-9_-]{1,48}$/.test(event.campaign || '') ? event.campaign : '';
    const now = Date.now();
    if (event.name !== 'session_heartbeat' && !record(event.name, {
      id: event.id, sid, page: event.page, entry: entryName(event.entry), at: now
    })) return false;
    db.prepare(`INSERT INTO sessions(id,started_at,last_seen,source,campaign) VALUES (?,?,?,?,?)
      ON CONFLICT(id) DO UPDATE SET last_seen=excluded.last_seen`).run(sid, now, now, source, campaign);
    return true;
  }

  function feedback(report) {
    const at = Date.parse(report.createdAt);
    if (!report.id || !Number.isFinite(at)) return;
    db.prepare(`INSERT OR IGNORE INTO feedback
      (id,at,category,app_version,game,gpu,mode,payload,updated_at) VALUES (?,?,?,?,?,?,?,?,?)`)
      .run(report.id, at, text(report.category, 40), text(report.appVersion, 40), text(report.game, 120),
        text(report.gpu, 100), text(report.mode, 40), JSON.stringify(report), at);
  }

  const feedbackFile = join(dataRoot, 'feedback.jsonl');
  let feedbackStamp = '';
  function syncFeedback() {
    if (!existsSync(feedbackFile)) return;
    const stat = statSync(feedbackFile);
    const stamp = `${stat.mtimeMs}:${stat.size}`;
    if (stamp === feedbackStamp) return;
    for (const line of readFileSync(feedbackFile, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      try { feedback(JSON.parse(line)); } catch { setMeta.run('feedback_import_warning', 'true'); }
    }
    feedbackStamp = stamp;
  }
  syncFeedback();

  function feedbackList(status = 'all', page = 1) {
    syncFeedback();
    const valid = new Set(['all', 'new', 'in_progress', 'resolved', 'ignored']);
    if (!valid.has(status)) throw new Error('反馈状态无效。');
    const where = status === 'all' ? '' : 'WHERE status=?';
    const args = status === 'all' ? [] : [status];
    const total = db.prepare(`SELECT COUNT(*) AS count FROM feedback ${where}`).get(...args).count;
    const actualPage = Math.max(1, Math.min(Math.trunc(Number(page)) || 1, Math.max(1, Math.ceil(total / 20))));
    const rows = db.prepare(`SELECT id,at,category,app_version,game,gpu,mode,status,updated_at,payload
      FROM feedback ${where} ORDER BY at DESC LIMIT 20 OFFSET ?`).all(...args, (actualPage - 1) * 20);
    return { total, page: actualPage, pages: Math.max(1, Math.ceil(total / 20)), items: rows.map(row => ({
      ...row, payload: JSON.parse(row.payload)
    })) };
  }

  function feedbackStatus(id, status) {
    if (!['new', 'in_progress', 'resolved', 'ignored'].includes(status)) throw new Error('反馈状态无效。');
    const changed = db.prepare('UPDATE feedback SET status=?,updated_at=? WHERE id=?').run(status, Date.now(), id).changes;
    if (!changed) throw new Error('反馈不存在。');
    record('feedback_status_changed', { result: status, detail: id });
  }

  function request(route, status, duration) {
    db.prepare('INSERT INTO requests(at,route,status,duration_ms) VALUES (?,?,?,?)')
      .run(Date.now(), route, status, Math.max(0, Math.round(duration)));
  }

  function overview(days = 7) {
    syncFeedback();
    days = [1, 7, 30, 90].includes(Number(days)) ? Number(days) : 7;
    const now = Date.now();
    const end = Math.floor((now + chinaOffset) / dayMs) * dayMs - chinaOffset + dayMs;
    const start = end - days * dayMs;
    const counts = Object.fromEntries(db.prepare('SELECT name,COUNT(*) AS count FROM events WHERE at>=? AND at<? GROUP BY name')
      .all(start, end).map(row => [row.name, row.count]));
    const visitQuery = `SELECT DISTINCT sid FROM events WHERE name='page_view' AND sid<>'' AND at>=? AND at<?`;
    const visitors = db.prepare(`SELECT COUNT(*) AS count FROM (${visitQuery})`).get(start, end).count;
    const converted = db.prepare(`SELECT COUNT(DISTINCT sid) AS count FROM events WHERE name IN ('download_redirect','download_request')
      AND at>=? AND at<? AND sid IN (${visitQuery})`).get(start, end, start, end).count;
    const active = db.prepare(`SELECT COUNT(*) AS count FROM sessions WHERE last_seen>=? AND id IN
      (SELECT sid FROM events WHERE name='page_view' AND at>=?)`).get(now - 5 * 60000, now - dayMs).count;
    const feedbackCount = db.prepare('SELECT COUNT(*) AS count FROM feedback WHERE at>=? AND at<?').get(start, end).count;
    const pending = db.prepare("SELECT COUNT(*) AS count FROM feedback WHERE status IN ('new','in_progress')").get().count;
    const buckets = new Map(Array.from({ length: days }, (_, index) => {
      const day = dateKey(start + index * dayMs);
      return [day, { day, views: 0, sessions: 0, downloads: 0, feedback: 0 }];
    }));
    const daily = db.prepare(`SELECT strftime('%Y-%m-%d',at/1000,'unixepoch','+8 hours') AS day,
      SUM(name='page_view') AS views,SUM(name IN ('download_redirect','download_request')) AS downloads,
      COUNT(DISTINCT CASE WHEN name='page_view' THEN NULLIF(sid,'') END) AS sessions
      FROM events WHERE at>=? AND at<? GROUP BY day`).all(start, end);
    for (const row of daily) if (buckets.has(row.day)) Object.assign(buckets.get(row.day), row);
    for (const row of db.prepare(`SELECT strftime('%Y-%m-%d',at/1000,'unixepoch','+8 hours') AS day,COUNT(*) AS feedback
      FROM feedback WHERE at>=? AND at<? GROUP BY day`).all(start, end)) if (buckets.has(row.day)) buckets.get(row.day).feedback = row.feedback;
    const sourceRows = db.prepare(`SELECT s.source,COUNT(DISTINCT s.id) AS sessions,
      COUNT(DISTINCT CASE WHEN EXISTS(SELECT 1 FROM events d WHERE d.sid=s.id AND d.name IN ('download_redirect','download_request')
      AND d.at>=? AND d.at<?) THEN s.id END) AS downloads
      FROM sessions s WHERE s.id IN (${visitQuery}) GROUP BY s.source ORDER BY sessions DESC`).all(start, end, start, end);
    const categoryRows = db.prepare('SELECT category,COUNT(*) AS count FROM feedback WHERE at>=? AND at<? GROUP BY category ORDER BY count DESC').all(start, end);
    const versionRows = db.prepare("SELECT release_tag AS tag,COUNT(*) AS count FROM events WHERE name IN ('download_redirect','download_request') AND at>=? AND at<? GROUP BY release_tag ORDER BY count DESC").all(start, end);
    const entryRows = db.prepare("SELECT entry_point AS entry,COUNT(*) AS count FROM events WHERE name IN ('download_redirect','download_request') AND at>=? AND at<? GROUP BY entry_point ORDER BY count DESC").all(start, end);
    const apiStart = now - dayMs;
    const api = db.prepare('SELECT COUNT(*) AS total,COALESCE(SUM(status>=500),0) AS errors,COALESCE(SUM(status>=400 AND status<500),0) AS rejected FROM requests WHERE at>=?').get(apiStart);
    const p95 = api.total ? db.prepare('SELECT duration_ms FROM requests WHERE at>=? ORDER BY duration_ms LIMIT 1 OFFSET ?').get(apiStart, Math.ceil(api.total * 0.95) - 1).duration_ms : null;
    const githubAssets = db.prepare('SELECT * FROM github_assets ORDER BY published_at DESC').all();
    const audit = db.prepare(`SELECT at,name,release_tag,result,detail FROM events WHERE name IN
      ('release_published','release_publish_failed','admin_login_failed','feedback_status_changed') ORDER BY at DESC LIMIT 20`).all();
    const recent = db.prepare('SELECT id,at,category,app_version,game,status FROM feedback ORDER BY at DESC LIMIT 5').all();
    return {
      days, from: dateKey(start), to: dateKey(end - 1), generatedAt: now, startedAt: Number(getMeta('started_at')),
      timezone: 'Asia/Shanghai', retentionDays: 90,
      totals: { views: counts.page_view || 0, sessions: visitors, active, downloads: (counts.download_redirect || 0) + (counts.download_request || 0),
        converted, conversionRate: visitors ? converted / visitors : null, feedback: feedbackCount, pending,
        feedbackStarts: counts.feedback_start || 0, feedbackRejected: counts.feedback_rejected || 0 },
      trend: [...buckets.values()].map(row => ({ ...row, collecting: end > Number(getMeta('started_at')) &&
        Date.parse(`${row.day}T23:59:59+08:00`) >= Number(getMeta('started_at')) })),
      sources: sourceRows, categories: categoryRows, versions: versionRows, entries: entryRows,
      recentFeedback: recent, audit, events: counts,
      github: { assets: githubAssets, total: getMeta('github_synced_at') ? githubAssets.reduce((sum, row) => sum + row.downloads, 0) : null,
        syncedAt: Number(getMeta('github_synced_at')) || null, error: getMeta('github_error') || null },
      health: { api, p95, uptime: Math.round(process.uptime()), memoryBytes: process.memoryUsage().rss,
        databaseBytes: statSync(join(dataRoot, 'analytics.sqlite')).size,
        feedbackImportWarning: getMeta('feedback_import_warning') === 'true' }
    };
  }

  function saveGithub(assets) {
    const now = Date.now();
    db.exec('BEGIN');
    try {
      db.exec('DELETE FROM github_assets');
      const insert = db.prepare('INSERT INTO github_assets(asset_id,tag,downloads,published_at,synced_at) VALUES (?,?,?,?,?)');
      const snapshot = db.prepare('INSERT INTO github_snapshots(asset_id,day,downloads,at) VALUES (?,?,?,?) ON CONFLICT(asset_id,day) DO UPDATE SET downloads=excluded.downloads,at=excluded.at');
      for (const asset of assets) {
        insert.run(asset.id, asset.tag, asset.downloads, asset.publishedAt, now);
        snapshot.run(asset.id, dateKey(now), asset.downloads, now);
      }
      setMeta.run('github_synced_at', String(now));
      setMeta.run('github_error', '');
      db.exec('COMMIT');
    } catch (error) { db.exec('ROLLBACK'); throw error; }
  }

  function prune() {
    const now = Date.now();
    db.prepare('DELETE FROM events WHERE at<?').run(now - 90 * dayMs);
    db.prepare('DELETE FROM sessions WHERE last_seen<?').run(now - 90 * dayMs);
    db.prepare('DELETE FROM requests WHERE at<?').run(now - 14 * dayMs);
    db.prepare('DELETE FROM github_snapshots WHERE at<?').run(now - 365 * dayMs);
  }
  prune();
  return { record, browser, feedback, feedbackList, feedbackStatus, request, overview, saveGithub, prune,
    githubError: () => setMeta.run('github_error', 'GitHub 暂时不可用，保留上次同步值。'), close: () => db.close() };
}

export async function syncGithub(analytics) {
  const assets = [];
  for (let page = 1; page <= 10; page += 1) {
    const response = await fetch(`https://api.github.com/repos/xiarongwu123/AMD-DLSS-MU/releases?per_page=100&page=${page}`, {
      headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'AMD-DLSS-MU-Site/Analytics' },
      signal: AbortSignal.timeout(10000)
    });
    if (!response.ok) throw new Error(`GitHub HTTP ${response.status}`);
    const releases = await response.json();
    if (!Array.isArray(releases)) throw new Error('Invalid GitHub response');
    for (const release of releases) {
      if (release.draft || release.prerelease) continue;
      for (const asset of release.assets || []) {
        if (asset.name === 'AMD-DLSS-MU.exe' && Number.isSafeInteger(asset.id) &&
            Number.isSafeInteger(asset.download_count) && asset.download_count >= 0) {
          assets.push({ id: asset.id, tag: release.tag_name, downloads: asset.download_count, publishedAt: release.published_at });
        }
      }
    }
    if (!response.headers.get('link')?.includes('rel="next"')) { analytics.saveGithub(assets); return; }
  }
  throw new Error('GitHub pagination limit reached');
}
