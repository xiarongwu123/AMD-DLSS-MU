// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { ContactFloat } from './ContactFloat';
afterEach(cleanup);
function open() {
  render(<ContactFloat/>);
  fireEvent.click(screen.getByRole('button', { name: '遇到问题？智能客服' }));
}
it('完整粘贴报错后显示步骤，不触发网络请求', () => {
  const fetch = vi.spyOn(globalThis, 'fetch');
  open();
  fireEvent.change(screen.getByLabelText('错误码或问题描述'), { target: { value: '[API_CONFIRM_REQUIRED] 当前API仅为静态推断' } });
  fireEvent.click(screen.getByRole('button', { name: '查询解决方案' }));
  expect(screen.getByText('展开上方“确认游戏实际设置”。')).toBeTruthy();
  expect(fetch).not.toHaveBeenCalled();
  fetch.mockRestore();
});
it('未知报错请求补充信息；空输入不可提交', () => {
  open();
  expect((screen.getByRole('button', { name: '查询解决方案' }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.change(screen.getByLabelText('错误码或问题描述'), { target: { value: '<script>alert(1)</script>XYZ_UNKNOWN_FAILURE_123' } });
  fireEvent.keyDown(screen.getByLabelText('错误码或问题描述'), { key: 'Enter' });
  expect(screen.getByText('还需要一些信息')).toBeTruthy();
  expect(document.querySelector('.support-panel script')).toBeNull();
});
it('中文输入法回车不误提交，清空和 Escape 可用', () => {
  open();
  const input = screen.getByLabelText('错误码或问题描述');
  fireEvent.change(input, { target: { value: 'API_CONFIRM_REQUIRED' } });
  fireEvent.keyDown(input, { key: 'Enter', isComposing: true });
  expect(screen.queryByText('找到相关排查建议')).toBeNull();
  fireEvent.keyDown(input, { key: 'Enter' });
  fireEvent.click(screen.getByRole('button', { name: '清空对话' }));
  expect(screen.queryByText('找到相关排查建议')).toBeNull();
  fireEvent.keyDown(input, { key: 'Escape' });
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: '遇到问题？智能客服' }));
});
it('提供人工微信和复制排查记录', async () => {
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
  open();
  fireEvent.click(screen.getByRole('button', { name: 'API_CONFIRM_REQUIRED' }));
  fireEvent.click(screen.getByRole('button', { name: '未解决，联系作者 →' }));
  expect(screen.getByText('xrwCoder')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: '复制排查记录' }));
  expect(writeText.mock.calls[0][0]).toContain('API_CONFIRM_REQUIRED');
  expect(await screen.findByText('排查记录已复制')).toBeTruthy();
});
