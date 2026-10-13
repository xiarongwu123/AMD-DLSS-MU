// @vitest-environment jsdom
import { afterEach, it, expect, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import App from './App';
import { AccountBadge } from './AccountMotion';
import type { State, Account } from './types';
const bridge = vi.hoisted(() => ({ listener: null as null | ((event: string, value: unknown) => void), state: {} as State }));
vi.mock('./bridge', () => ({
  rpc: vi.fn(async () => bridge.state),
  subscribe: (listener: typeof bridge.listener) => { bridge.listener = listener; return () => { bridge.listener = null; }; },
  isDesktop: true, isPreview: false,
  emptyState: { settings: { dark: true }, games: [], downloads: [] },
}));
vi.mock('./VisualEffects', () => ({ useVisualEffects: () => ({ active: true, enabled: true, change: vi.fn() }), InteractiveSurface: ({ children }: {children: React.ReactNode}) => children }));
vi.mock('./reactbits/ClickSpark', () => ({ default: ({children}: {children: React.ReactNode}) => children }));
vi.mock('./reactbits/Lanyard', () => ({ default: () => <div data-testid="lanyard" /> }));
vi.mock('./reactbits/GridScan', () => ({ default: () => <div data-testid="grid-scan" /> }));
vi.mock('./HomeLanding', () => ({ HomeLanding: () => <h1>首页</h1> }));
const account: Account = { id: 'test', email: 'test@example.com', membership: {tier: 'standard', expiresAt: null}, features: [], paymentsEnabled: false };
async function setup(restored = false) {
  bridge.state = { page: 'library', settings: {dark: true}, games: [], downloads: [], online: true, viewAvailable: true, account: restored ? account : null } as unknown as State;
  render(<App />);
  await screen.findByRole('heading', {name: '游戏库'});
}
function publish(change: Partial<State>) { act(() => { bridge.state = {...bridge.state, ...change}; bridge.listener?.('state', bridge.state); }); }
afterEach(() => { cleanup(); vi.useRealTimers(); });
it('恢复已登录账户不播放进入动画，账户入口直接打开工牌，设置仍走原账户页', async () => {
  await setup(true);
  expect(screen.queryByRole('status', {name: '登录成功，正在进入 MU'})).toBeNull();
  fireEvent.click(screen.getByRole('button', {name: '我的账户'}));
  expect(screen.getByRole('complementary', {name: '我的账户工牌'})).toBeTruthy();
  expect(screen.queryByRole('dialog', {name: '我的账户'})).toBeNull();
  fireEvent.click(screen.getByRole('button', {name: '账户设置'}));
  expect(screen.queryByRole('complementary', {name: '我的账户工牌'})).toBeNull();
});
it('仅本次登录确认成功才播放一次，未完成验证和后续状态刷新不重复播放', async () => {
  await setup();
  fireEvent(window, new Event('mu-login-attempt'));
  publish({account, online: false});
  expect(screen.queryByRole('status', {name: '登录成功，正在进入 MU'})).toBeNull();
  vi.useFakeTimers();
  publish({online: true});
  expect(screen.getByRole('status', {name: '登录成功，正在进入 MU'})).toBeTruthy();
  act(() => { vi.advanceTimersByTime(1600); });
  expect(screen.queryByRole('status', {name: '登录成功，正在进入 MU'})).toBeNull();
  publish({account: {...account}});
  expect(screen.queryByRole('status', {name: '登录成功，正在进入 MU'})).toBeNull();
});
it('失败的登录尝试不会在后续账户更新中错误播放动画', async () => {
  await setup();
  fireEvent(window, new Event('mu-login-attempt'));
  fireEvent(window, new Event('mu-login-cancel'));
  publish({account});
  expect(screen.queryByRole('status', {name: '登录成功，正在进入 MU'})).toBeNull();
});
it('工牌保持独立悬挂，遮罩和 Esc 均可收起，账户设置保留', () => {
  const onClose = vi.fn(), onSettings = vi.fn();
  render(<AccountBadge account={account} active={false} onClose={onClose} onSettings={onSettings} />);
  expect(screen.getByRole('img', {name: 'MU 账户工牌'})).toBeTruthy();
  expect(document.querySelector('.scrim')).toBeNull();
  fireEvent.click(screen.getByRole('button', {name: '收起账户工牌'}));
  expect(onClose).toHaveBeenCalledOnce();
  fireEvent.keyDown(window, {key: 'Escape'});
  expect(onClose).toHaveBeenCalledTimes(2);
  fireEvent.click(screen.getByRole('button', {name: '账户设置'}));
  expect(onSettings).toHaveBeenCalledOnce();
});
