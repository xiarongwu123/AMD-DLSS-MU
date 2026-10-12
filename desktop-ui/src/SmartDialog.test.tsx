// @vitest-environment jsdom
import { afterEach, describe, it, expect, vi } from "vitest";
import {
  render,
  screen,
  fireEvent,
  waitFor,
  cleanup,
} from "@testing-library/react";
import { SmartDialog } from "./SmartDialog";
import { emptyState, rpc } from "./bridge";
vi.mock("./bridge", () => ({
  rpc: vi.fn(),
  subscribe: () => () => {},
  emptyState: { downloads: [], status: "", busy: false },
}));
const game = {
  id: "test-game",
  title: "游戏",
  cover: null,
  state: "Unconfigured",
  mode: null,
  steamAppId: null,
};
const recommendation = {
  gpu: { name: "AMD RX 7900 XT", vramMb: null },
  width: 2560,
  height: 1440,
  recommendation: {
    qualityMode: 2,
    performanceMode: 2,
    availability: 2,
    notices: [],
    qualityReason: "信息未确认",
    performanceReason: "信息未确认",
  },
  hipSetupPending: false,
};
afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});
describe("推荐目标与真实配置命令", () => {
  it("检测时显示动态进度，检测结束后不因显存未确认禁用选择", async () => {
    let resolve!: (v: unknown) => void;
    vi.mocked(rpc).mockImplementation(
      () =>
        new Promise((r) => {
          resolve = r;
        }),
    );
    render(
      <SmartDialog
        game={game}
        state={emptyState}
        onClose={() => {}}
        onComplete={() => {}}
      />,
    );
    expect(screen.getByRole("status").textContent).toContain("正在读取");
    expect(document.querySelector(".indeterminate")).not.toBeNull();
    expect(
      (screen.getByRole("button", { name: "一键配置" }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
    resolve(recommendation);
    await screen.findByText("AMD RX 7900 XT");
    expect(
      (screen.getByRole("button", { name: "一键配置" }) as HTMLButtonElement)
        .disabled,
    ).toBe(false);
  });
  it("画质直接配置模式一，帧率直接配置 XeFG，只有自定义显示清单", async () => {
    vi.mocked(rpc).mockImplementation(async (command) =>
      command === "recommend" ? recommendation : undefined,
    );
    const complete = vi.fn();
    render(
      <SmartDialog
        game={game}
        state={emptyState}
        onClose={() => {}}
        onComplete={complete}
      />,
    );
    await screen.findByText("AMD RX 7900 XT");
    expect(screen.queryAllByRole("radio")).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: "一键配置" }));
    await waitFor(() =>
      expect(rpc).toHaveBeenCalledWith("configure", {
        gameId: "test-game",
        mode: 0,
        vulkan: false,
        fgInput: "nofg",
      }),
    );
    await waitFor(() => expect(complete).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole("button", { name: /帧率优先/ }));
    fireEvent.click(screen.getByRole("button", { name: "一键配置" }));
    await waitFor(() =>
      expect(rpc).toHaveBeenCalledWith("configure", {
        gameId: "test-game",
        mode: 4,
        vulkan: false,
        fgInput: "nofg",
      }),
    );
    await waitFor(() => expect(complete).toHaveBeenCalledTimes(2));
    fireEvent.click(screen.getByRole("button", { name: /自定义/ }));
    expect(screen.getAllByRole("radio")).toHaveLength(3);
    fireEvent.click(screen.getByRole("radio", { name: /标准 OptiScaler/ }));
    fireEvent.click(screen.getByLabelText("Vulkan 接口"));
    fireEvent.change(screen.getByRole("combobox"), {
      target: { value: "fsrfg" },
    });
    fireEvent.click(screen.getByRole("button", { name: "一键配置" }));
    await waitFor(() =>
      expect(rpc).toHaveBeenCalledWith("configure", {
        gameId: "test-game",
        mode: 2,
        vulkan: true,
        fgInput: "fsrfg",
      }),
    );
  });
  it("配置出错保留弹窗与错误，不报告成功", async () => {
    vi.mocked(rpc).mockImplementation(async (command) => {
      if (command === "recommend") return recommendation;
      throw new Error("模型文件缺失");
    });
    const complete = vi.fn();
    render(
      <SmartDialog
        game={game}
        state={emptyState}
        onClose={() => {}}
        onComplete={complete}
      />,
    );
    await screen.findByText("AMD RX 7900 XT");
    fireEvent.click(screen.getByRole("button", { name: "一键配置" }));
    expect(await screen.findByRole("alert")).toHaveProperty(
      "textContent",
      "模型文件缺失",
    );
    expect(complete).not.toHaveBeenCalled();
  });
  it("社区入口按游戏列方案，选中后才应用对应分享", async () => {
    const item = { id: "preset-1", gameKey: "steam:123", gameName: "游戏",
      mode: 4, goal: "performance", vulkan: false, componentVersion: "v1",
      title: "我的顺滑配置", description: "XeFG 设置", author: "玩家 A",
      createdAt: 1, hasImage: false, isMine: false, settingCount: 1 };
    vi.mocked(rpc).mockImplementation(async (command) => {
      if (command === "recommend") return recommendation;
      if (command === "presets") return { items: [item], total: 1 };
      if (command === "preset") return { item, settings: [{ file: "OptiScaler.ini",
        section: "XeFG", key: "InterpolationCount", value: "1" }], imageData: null };
      return undefined;
    });
    const complete = vi.fn();
    render(<SmartDialog game={game} state={emptyState} onClose={() => {}} onComplete={complete} />);
    await screen.findByText("AMD RX 7900 XT");
    fireEvent.click(screen.getByRole("tab", { name: /玩家方案/ }));
    expect(screen.queryByRole("button", { name: "一键配置" })).toBeNull();
    fireEvent.click(await screen.findByRole("button", { name: /我的顺滑配置/ }));
    fireEvent.click(await screen.findByRole("button", { name: "一键使用这套方案" }));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("applyPreset",
      { gameId: "test-game", id: "preset-1" }));
    await waitFor(() => expect(complete).toHaveBeenCalledTimes(1));
  });
  it("从游戏卡片直达玩家方案时不先启动硬件检测", async () => {
    vi.mocked(rpc).mockImplementation(async (command) =>
      command === "presets" ? { items: [], total: 0 } : undefined,
    );
    render(<SmartDialog game={game} state={emptyState} initialTab="community"
      onClose={() => {}} onComplete={() => {}} />);
    await screen.findByText(/这款游戏还没有人分享配置/);
    expect(rpc).toHaveBeenCalledWith("presets", { gameId: "test-game" });
    expect(rpc).not.toHaveBeenCalledWith("recommend", { gameId: "test-game" });
    fireEvent.click(screen.getByRole("tab", { name: "智能推荐" }));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("recommend", { gameId: "test-game" }));
  });
});
