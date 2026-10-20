// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { rpc } from "./bridge";
import { LibraryEntrances } from "./LibraryEntrances";
import type { State } from "./types";

vi.mock("./bridge", () => ({ rpc: vi.fn(async () => true) }));
const pro = { viewAvailable: true, account: { membership: { tier: "pro", kind: "plus", expiresAt: "2099-01-01T00:00:00Z", plusExpiresAt: "2099-01-01T00:00:00Z" }, features: [{ key: "services.remote", allowed: true }] } } as State;
afterEach(() => { cleanup(); vi.useRealTimers(); vi.mocked(rpc).mockReset().mockResolvedValue(true); });
it("普通账户可进入支持游戏遮罩，但远程服务引导开通且不打开微信", () => {
  const onUpgrade = vi.fn(), onSupported = vi.fn();
  render(<LibraryEntrances state={{ ...pro, account: { ...pro.account!, membership: { tier: "standard", expiresAt: null } } }} supported={false} onSupported={onSupported} onUpgrade={onUpgrade}/>);
  fireEvent.click(screen.getByRole("button", { name: /支持游戏/ }));
  expect(onSupported).toHaveBeenCalledWith(true);
  fireEvent.click(screen.getByRole("button", { name: /一对一远程服务/ }));
  expect(onUpgrade).toHaveBeenCalledOnce();
  expect(screen.queryByText("xrwCoder")).toBeNull();
});
it("高级 Plus 验证服务端权限后打开微信，权限失效后立即关闭", async () => {
  const onUpgrade = vi.fn();
  const { rerender } = render(<LibraryEntrances state={pro} supported={false} onSupported={()=>{}} onUpgrade={onUpgrade}/>);
  fireEvent.click(screen.getByRole("button", { name: /一对一远程服务/ }));
  expect(await screen.findByText("xrwCoder")).toBeTruthy();
  expect(screen.getByAltText("作者微信二维码")).toBeTruthy();
  expect(onUpgrade).not.toHaveBeenCalled();
  expect(rpc).toHaveBeenCalledWith("remoteService");
  rerender(<LibraryEntrances state={{ ...pro, viewAvailable: false }} supported={false} onSupported={()=>{}} onUpgrade={onUpgrade}/>);
  expect(screen.queryByText("xrwCoder")).toBeNull();
});
it("普通高级权益仍有效，但 Plus 到期后隐藏微信", async () => {
  vi.useFakeTimers(); vi.setSystemTime(new Date("2026-10-18T00:00:00Z"));
  render(<LibraryEntrances state={{ ...pro, account: { ...pro.account!, membership: { tier: "pro", kind: "plus", expiresAt: "2099-01-01T00:00:00Z", plusExpiresAt: "2026-10-18T00:00:01Z" } } }} supported={false} onSupported={()=>{}} onUpgrade={()=>{}}/>);
  fireEvent.click(screen.getByRole("button", { name: /一对一远程服务/ }));
  await act(async () => {});
  expect(screen.getByText("xrwCoder")).toBeTruthy();
  act(() => vi.advanceTimersByTime(1002));
  expect(screen.queryByText("xrwCoder")).toBeNull();
});

it.each(["advanced", "lifetime"] as const)("%s 会员不允许打开远程服务", kind => {
  const onUpgrade = vi.fn();
  render(<LibraryEntrances state={{ ...pro, account: { ...pro.account!, membership: { tier: "pro", kind, expiresAt: "9999-12-31T23:59:59Z" } } }} supported={false} onSupported={()=>{}} onUpgrade={onUpgrade}/>);
  fireEvent.click(screen.getByRole("button", { name: /一对一远程服务/ }));
  expect(onUpgrade).toHaveBeenCalledOnce(); expect(rpc).not.toHaveBeenCalled();
  expect(screen.queryByText("xrwCoder")).toBeNull();
});
it("客户端显示 Plus 但服务器拒绝时不打开微信", async () => {
  vi.mocked(rpc).mockRejectedValueOnce(new Error("Plus 权益已失效"));
  render(<LibraryEntrances state={pro} supported={false} onSupported={()=>{}} onUpgrade={()=>{}}/>);
  fireEvent.click(screen.getByRole("button", { name: /一对一远程服务/ }));
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Plus 权益已失效");
  expect(screen.queryByText("xrwCoder")).toBeNull();
});
it.each([['lifetime',3],['standard',1]] as const)('%s 拥有指导次数时验证后可打开微信，查看不会扣次',async(kind,remaining)=>{
 const state={...pro,account:{...pro.account!,membership:{tier:kind==='standard'?'standard':'pro',kind,expiresAt:null,remoteSessionsRemaining:remaining}}};
 render(<LibraryEntrances state={state} supported={false} onSupported={()=>{}} onUpgrade={()=>{}}/>);
 fireEvent.click(screen.getByRole('button',{name:/一对一远程服务/}));await screen.findByText('xrwCoder');
 expect(rpc).toHaveBeenCalledWith('remoteService');expect(state.account.membership.remoteSessionsRemaining).toBe(remaining);
});
