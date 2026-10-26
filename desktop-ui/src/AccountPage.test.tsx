// @vitest-environment jsdom
import { afterEach, describe, it, expect, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { AccountPage } from "./AccountPage";
import { emptyState, rpc } from "./bridge";
vi.mock("./bridge", () => ({
  rpc: vi.fn(),
  emptyState: { settings: {}, downloads: [], busy: false },
}));
const account = {
  id: "test",
  email: "test@example.com",
  membership: { tier: "standard", expiresAt: null },
  features: [],
  paymentsEnabled: false,
};
afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});
describe("账户页保留原有操作", () => {
  it("兑换提交修剪后的 CDK，成功清空输入并反馈", async () => {
    vi.mocked(rpc).mockResolvedValue(null);
    const notify = vi.fn();
    render(
      <AccountPage
        state={{ ...emptyState, account, online: true }}
        notify={notify}
      />,
    );
    const input = screen.getByRole("textbox", { name: "CDK 兑换会员" });
    fireEvent.change(input, { target: { value: " ABCD-1234 " } });
    fireEvent.click(screen.getByRole("button", { name: "兑换会员" }));
    await waitFor(() =>
      expect(rpc).toHaveBeenCalledWith("redeemMembership", {
        code: "ABCD-1234",
      }),
    );
    await waitFor(() => expect((input as HTMLInputElement).value).toBe(""));
    expect(notify).toHaveBeenCalledWith("兑换成功，会员状态已更新。");
  });
  it("兑换失败保留输入并显示服务端错误", async () => {
    vi.mocked(rpc).mockRejectedValue(new Error("兑换码已使用"));
    render(
      <AccountPage
        state={{ ...emptyState, account, online: true }}
        notify={() => {}}
      />,
    );
    const input = screen.getByRole("textbox", { name: "CDK 兑换会员" });
    fireEvent.change(input, { target: { value: "ABCD-1234" } });
    fireEvent.click(screen.getByRole("button", { name: "兑换会员" }));
    await waitFor(() =>
      expect(screen.getByRole("alert").textContent).toContain("兑换码已使用"),
    );
    expect((input as HTMLInputElement).value).toBe("ABCD-1234");
  });
  it("进入首页与刷新会员是两个独立按钮，并调用对应命令", async () => {
    vi.mocked(rpc).mockResolvedValue(null);
    render(
      <AccountPage
        state={{ ...emptyState, account, online: true }}
        notify={() => {}}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "进入首页 →" }));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("enterHome", {}));
    await waitFor(() =>
      expect(
        (
          screen.getByRole("button", {
            name: "刷新会员状态",
          }) as HTMLButtonElement
        ).disabled,
      ).toBe(false),
    );
    fireEvent.click(screen.getByRole("button", { name: "刷新会员状态" }));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("refreshAccount", {}));
  });
  it("断线时允许原重新连接按钮，失败显示错误，恢复后显示已联网验证", async () => {
    vi.mocked(rpc).mockRejectedValue(new Error("账户请求超时"));
    const { rerender } = render(
      <AccountPage
        state={{ ...emptyState, account, online: false, reconnecting: true }}
        notify={() => {}}
      />,
    );
    expect(screen.getByText("账户连接暂时波动，正在重试…")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "重新连接账户服务" }));
    await waitFor(() =>
      expect(screen.getByRole("alert").textContent).toContain("账户请求超时"),
    );
    rerender(
      <AccountPage
        state={{ ...emptyState, account, online: true, reconnecting: false }}
        notify={() => {}}
      />,
    );
    expect(screen.getByText("已联网验证")).toBeTruthy();
    expect(screen.queryByText("账户连接暂时波动，正在重试…")).toBeNull();
  });
});
