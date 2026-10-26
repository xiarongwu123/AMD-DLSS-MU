import { describe, expect, it } from 'vitest';
import { searchSupport, supportSummary } from './search';

describe('本地客服检索', () => {
  it('完整错误码优先于上下文中的型号和其他关键词', () => {
    expect(searchSupport('RX 6750 上显示 [API_CONFIRM_REQUIRED] 当前 API 仅为静态推断')[0].id).toBe('MU-018');
  });
  it('不把相似前缀误认成真实错误码', () => {
    expect(searchSupport('API_CONFIRM_REQUIRED_EXTRA').some(e => e.id === 'MU-018')).toBe(false);
  });
  it('混合报错保留每个独立问题', () => {
    expect(searchSupport('[ARCH_INCOMPATIBLE] RDNA3 [WINDOW_MODE_UNKNOWN] 窗口模式 [MISSING_RESOURCE] amd-magpie-resources').map(e => e.id)).toEqual(expect.arrayContaining(['MU-019', 'MU-020', 'MU-033']));
  });
  it.each([
    ['目标窗口的权限高于 Magpie', 'MU-032'],
    ["'0x00' is an invalid start of a value. Path: $", 'MU-026'],
    ['会员有效但方案没解锁', 'MU-041'],
    ['喜鹊下载在哪个文件夹', 'MU-030'],
    ['Mochizuki 缺少 dlssnr.bin', 'MU-014'],
    ['神力科莎搜索不到', 'MU-036'],
  ])('识别自然问题：%s', (question, id) => expect(searchSupport(question)[0]?.id).toBe(id));
  it.each(['', '报错', '怎么回事', '今天吃什么', 'XYZ_UNKNOWN_FAILURE_123'])('无证据时不编造答案：%s', q => expect(searchSupport(q)).toEqual([]));
  it('不向用户公开管理端 CDK 知识', () => expect(searchSupport('管理员如何生成和停用CDK')).toEqual([]));
  it('复制记录包括原报错、步骤和知识版本', () => {
    const q = '[GAME_RUNNING]';
    expect(supportSummary(q, searchSupport(q))).toContain('1. 正常退出游戏');
    expect(supportSummary(q, searchSupport(q))).toContain(q);
    expect(supportSummary(q, [])).toContain('xrwCoder');
  });
});
