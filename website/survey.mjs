import { DatabaseSync } from 'node:sqlite';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';

export const surveyDefinition = {
  version: 'pro-v1',
  features: [
    { id: 'benchmark_lab', name: '自动 Benchmark Lab', description: '自动跑 Native / FSR / OptiScaler / FG 多组方案，输出 Avg FPS、1% Low、Frame Time。' },
    { id: 'performance_presets', name: '一键性能预设', description: '画质优先 / 平衡 / 高帧 / 低延迟，全部采用固定规则和配置模板。' },
    { id: 'game_guard', name: '游戏版本守卫', description: '游戏更新后检测 EXE / DLL hash 变化，提醒当前插件方案可能失效。' },
    { id: 'driver_guard', name: '驱动版本守卫', description: 'AMD 驱动升级后记录前后版本，提示哪些游戏配置需要重新验证。' },
    { id: 'time_machine', name: 'Time Machine', description: '保存每次 DLL、INI、插件版本、游戏版本快照，一键恢复任意历史状态。' },
    { id: 'advanced_hud', name: '高级实时 HUD', description: '显示真实 FPS、1% Low、Frame Time、FG 状态、Upscaler、输入 / 输出分辨率等。' },
    { id: 'config_diff', name: '配置对比器', description: '两套 OptiScaler / FSR 配置直接 Diff，告诉你具体哪里不同。' },
    { id: 'batch_management', name: '批量游戏管理', description: '20 个游戏统一检测插件版本、异常文件、更新状态。' },
    { id: 'template_library', name: '配置模板库', description: '提供 UE5 通用模板、RE Engine 模板、Cyberpunk 模板等。' },
    { id: 'launch_profiles', name: '启动方案', description: '启动游戏时自动应用对应配置，退出后恢复特定状态。' },
    { id: 'conflict_detection', name: '冲突检测 Pro', description: '检查 ReShade、Special K、RTSS、OptiScaler、其他 proxy DLL 冲突。' },
    { id: 'recovery_center', name: '高级恢复中心', description: '安装中断、文件被修改、游戏更新后，仍能尝试重建正确状态。' }
  ],
  payments: [
    { id: 'willing', name: '愿意为实用的功能付费' },
    { id: 'depends', name: '取决于价格和实际效果' },
    { id: 'free_only', name: '只考虑免费功能' },
    { id: 'unsure', name: '暂时不确定' }
  ]
};

export function validateSurvey(input) {
  if (!input || input.version !== surveyDefinition.version) throw new Error('问卷版本已变化，请刷新页面后重新填写。');
  const ids = new Set(surveyDefinition.features.map(item => item.id));
  const features = input.features;
  if (!Array.isArray(features) || features.length < 1 || features.length > ids.size ||
      new Set(features).size !== features.length || features.some(id => !ids.has(id) && id !== 'none')) {
    throw new Error('请选择感兴趣的功能，或选择“暂时没有感兴趣的功能”。');
  }
  const none = features.includes('none');
  if (none && features.length !== 1) throw new Error('“暂时没有”不能与其他功能同时选择。');
  if (typeof input.priority !== 'string' || (none ? input.priority !== '' : !features.includes(input.priority))) {
    throw new Error('请从已选功能中选择最希望优先开发的一项。');
  }
  if (!surveyDefinition.payments.some(item => item.id === input.payment)) throw new Error('请选择付费意愿。');
  if (typeof input.suggestion !== 'string' || input.suggestion.length > 1000) throw new Error('建议最多填写 1,000 字。');
  return { features: [...features].sort(), priority: input.priority, payment: input.payment, suggestion: input.suggestion.trim() };
}

export function createSurveyStore(dataRoot) {
  const db = new DatabaseSync(join(dataRoot, 'analytics.sqlite'));
  db.exec(`
    PRAGMA journal_mode=WAL;
    PRAGMA busy_timeout=5000;
    CREATE TABLE IF NOT EXISTS survey_responses (
      id TEXT PRIMARY KEY, version TEXT NOT NULL, respondent_hash TEXT NOT NULL,
      created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL,
      features TEXT NOT NULL, priority TEXT NOT NULL, payment TEXT NOT NULL, suggestion TEXT NOT NULL,
      UNIQUE(version, respondent_hash)
    );
    CREATE INDEX IF NOT EXISTS survey_created ON survey_responses(version, created_at);
  `);
  const version = surveyDefinition.version;
  const fields = 'id,version,created_at,updated_at,features,priority,payment,suggestion';
  const decode = row => row ? { ...row, features: JSON.parse(row.features) } : null;
  const get = hash => decode(db.prepare(`SELECT ${fields} FROM survey_responses WHERE version=? AND respondent_hash=?`).get(version, hash));
  function save(hash, input) {
    const answer = validateSurvey(input);
    const now = Date.now();
    db.exec('BEGIN IMMEDIATE');
    try {
      db.prepare(`INSERT INTO survey_responses
        (id,version,respondent_hash,created_at,updated_at,features,priority,payment,suggestion) VALUES (?,?,?,?,?,?,?,?,?)
        ON CONFLICT(version,respondent_hash) DO UPDATE SET updated_at=excluded.updated_at,
        features=excluded.features,priority=excluded.priority,payment=excluded.payment,suggestion=excluded.suggestion`)
        .run(randomUUID(), version, hash, now, now, JSON.stringify(answer.features), answer.priority, answer.payment, answer.suggestion);
      const response = get(hash);
      db.exec('COMMIT');
      return response;
    } catch (error) { db.exec('ROLLBACK'); throw error; }
  }
  function report(days = 'all', page = 1) {
    if (!['all', '1', '7', '30', '90'].includes(String(days))) throw new Error('时间范围无效。');
    const offset = 8 * 3600000;
    const day = 86400000;
    const end = Math.floor((Date.now() + offset) / day) * day - offset + day;
    const start = days === 'all' ? 0 : end - Number(days) * day;
    const where = 'version=? AND created_at>=? AND created_at<?';
    const args = [version, start, end];
    db.exec('BEGIN');
    try {
      const total = db.prepare(`SELECT COUNT(*) AS count FROM survey_responses WHERE ${where}`).get(...args).count;
      const votes = new Map(db.prepare(`SELECT j.value AS feature,COUNT(*) AS count FROM survey_responses,json_each(features) j
        WHERE ${where} GROUP BY j.value`).all(...args).map(row => [row.feature, row.count]));
      const firsts = new Map(db.prepare(`SELECT priority,COUNT(*) AS count FROM survey_responses WHERE ${where} GROUP BY priority`)
        .all(...args).map(row => [row.priority, row.count]));
      const payments = new Map(db.prepare(`SELECT payment,COUNT(*) AS count FROM survey_responses WHERE ${where} GROUP BY payment`)
        .all(...args).map(row => [row.payment, row.count]));
      const pages = Math.max(1, Math.ceil(total / 20));
      const actualPage = Math.max(1, Math.min(Math.trunc(Number(page)) || 1, pages));
      const items = db.prepare(`SELECT ${fields} FROM survey_responses WHERE ${where} ORDER BY created_at DESC,id DESC LIMIT 20 OFFSET ?`)
        .all(...args, (actualPage - 1) * 20).map(decode);
      const ratio = count => total ? count / total : 0;
      const ranking = surveyDefinition.features.map(feature => ({ ...feature, votes: votes.get(feature.id) || 0,
        priorityVotes: firsts.get(feature.id) || 0, ratio: ratio(votes.get(feature.id) || 0) }))
        .sort((a, b) => b.votes - a.votes || b.priorityVotes - a.priorityVotes);
      const result = { version, days: String(days), total, page: actualPage, pages, items,
        generatedAt: Date.now(), none: votes.get('none') || 0, ranking,
        willingRatio: total ? ratio(payments.get('willing') || 0) : null,
        payments: surveyDefinition.payments.map(item => ({ ...item, count: payments.get(item.id) || 0, ratio: ratio(payments.get(item.id) || 0) })) };
      db.exec('COMMIT');
      return result;
    } catch (error) { db.exec('ROLLBACK'); throw error; }
  }
  return { get, save, report, close: () => db.close() };
}
