import knowledge from './knowledge.json';

export type SupportArticle = (typeof knowledge.entries)[number];
export const knowledgeVersion = knowledge.baseVersion;
export const reviewedAt = knowledge.reviewedAt;
const aliases: Record<string, string[]> = {
  'MU-006': ['7000系', 'RX7000', '7800', '7900', '7700', '7600', 'lmxxf-rdna3'],
  'MU-007': ['6000系', 'RX6000', '6750', '6700', '6600', '6800', '6900'],
  'MU-010': ['32位', 'x32', 'x86'],
  'MU-011': ['ANTI_CHEAT', '反作弊', '封号'],
  'MU-012': ['daniel-runtime', 'daniel-weights'],
  'MU-013': ['lmxxf-model', 'native-game-tiled-assets'],
  'MU-014': ['mochizuki-model', 'dlssnr.bin'],
  'MU-016': ['hip运行时', 'hip缺失', '缺少hip'],
  'MU-018': ['API_CONFIRM_REQUIRED', '接口未确认', '确认接口'],
  'MU-019': ['ARCH_INCOMPATIBLE', '架构不兼容'],
  'MU-020': ['MISSING_RESOURCE', 'amd-magpie-resources', '缺资源', '缺少资源'],
  'MU-021': ['API_INCOMPATIBLE'],
  'MU-022': ['DLL_CONFLICT', 'DLL冲突', '代理冲突'],
  'MU-023': ['GAME_RUNNING', '游戏正在运行', '进程占用'],
  'MU-024': ['RESTORE_REQUIRED', '恢复不干净', '恢复配置失败', 'opt文件夹'],
  'MU-025': ['INTEGRITY_FAILED', '备份校验失败', '文件被修改'],
  'MU-026': ['0x00', 'invalid start of a value', '空字节'],
  'MU-027': ['SHA256', '哈希校验失败', '下载失败', '下载出错'],
  'MU-030': ['下载位置', '哪个文件夹', '下载在哪'],
  'MU-032': ['目标窗口的权限高于', '目标窗口权限高于', '权限高于magpie'],
  'MU-033': ['喜鹊没效果', '喜鹊没有效果', 'WINDOW_MODE_UNKNOWN'],
  'MU-034': ['喜鹊已运行'],
  'MU-035': ['游戏库按钮变灰', '下载喜鹊不能'],
  'MU-036': ['神力科莎', 'Assetto Corsa', '244210', 'acs.exe'],
  'MU-038': ['游戏标识无效', '附件上传失败'],
  'MU-040': ['一对一服务', '远程次数', 'services.remote'],
  'MU-041': ['会员没解锁', '会员未解锁', '会员锁住', '开通不生效'],
  'MU-042': ['账户超时', '登录超时', '账户连接波动'],
};
const normalize = (s: string) => s.normalize('NFKC').toLowerCase().replace(/\s+/g, '');
function grams(s: string) {
  return new Set(s.match(/[a-z0-9_][a-z0-9_.-]{2,}|[\u4e00-\u9fff]{2,}/g)?.flatMap(token =>
    /^[\u4e00-\u9fff]+$/.test(token)
      ? Array.from({ length: token.length - 1 }, (_, i) => token.slice(i, i + 2)) : [token]) ?? []);
}
export function searchSupport(input: string): SupportArticle[] {
  const q = normalize(input.slice(0, 4000));
  if (q.length < 2) return [];
  // Exact error identifiers take priority; return every issue in a combined error.
  const explicit = knowledge.entries.filter(e => [e.title, ...(aliases[e.id] ?? [])].some(value => {
    const codes = value.match(/\b[A-Z][A-Z0-9]+(?:_[A-Z0-9]+)+\b/g) ?? [];
    return codes.some(code => new RegExp(`(^|[^a-z0-9_])${code.toLowerCase()}($|[^a-z0-9_])`).test(input.toLowerCase()));
  }));
  if (explicit.length) return explicit.slice(0, 4);
  const queryGrams = grams(q);
  return knowledge.entries.map(article => {
    const terms = [article.title, ...article.keywords.split(/[，,]/), ...(aliases[article.id] ?? [])].map(normalize);
    const exact = terms.filter(t => {
      if (t.length <= 1 || t === '管理员') return false;
      if (/^[a-z0-9_.-]+$/.test(t)) {
        const escaped = t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        return new RegExp(`(^|[^a-z0-9_])${escaped}($|[^a-z0-9_])`).test(q);
      }
      return q.includes(t);
    });
    const tokens = grams(terms.join(' '));
    const common = [...queryGrams].filter(t => tokens.has(t)).length;
    const score = exact.reduce((n, t) => n + Math.min(t.length, 16) * 2, 0) + common;
    return { article, score, eligible: exact.length > 0 || (common >= 3 && common / Math.max(queryGrams.size, 1) >= .28) };
  }).filter(x => x.eligible && x.score >= 5).sort((a, b) => b.score - a.score)
    .filter((x, i, all) => i === 0 || x.score >= all[0].score * .7).slice(0, 3).map(x => x.article);
}
export function supportSummary(question: string, articles: SupportArticle[]): string {
  return [`问题：${question}`, ...articles.map(e => `${e.title}\n${e.answer}\n${e.steps.map((s, i) => `${i + 1}. ${s}`).join('\n')}\n仍未解决：${e.escalate_when}`),
    `知识库基线：MU ${knowledgeVersion}，复核日期：${reviewedAt}`, '联系作者：微信 xrwCoder'].join('\n\n');
}
