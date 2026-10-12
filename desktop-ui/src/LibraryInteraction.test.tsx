// @vitest-environment jsdom
import { afterEach, describe, it, expect, vi } from "vitest";
import {
  render,
  screen,
  fireEvent,
  waitFor,
  cleanup,
  within,
} from "@testing-library/react";
import App from "./App";
import { DiagnosticsDialog } from "./SmartDialog";
import { rpc } from "./bridge";
import type { State } from "./types";
vi.mock("./bridge", () => ({
  rpc: vi.fn(),
  subscribe: () => () => {},
  isDesktop: true,
  isPreview: false,
  emptyState: { settings: { dark: true }, games: [], downloads: [] },
}));
const games = [
  {
    id: "new",
    title: "未配置游戏",
    cover: null,
    state: "Unconfigured",
    mode: null,
    steamAppId: null,
  },
  {
    id: "ready",
    title: "已配置游戏",
    cover: null,
    state: "Configured",
    mode: 0,
    steamAppId: null,
  },
];
function setup(page: "home" | "library") {
  let state = {
    page,
    games,
    settings: { dark: true },
    viewAvailable: true,
    downloads: [],
    online: true,
    status: "",
  } as unknown as State;
  vi.mocked(rpc).mockImplementation(async (command, args) => {
    if (command === "navigate")
      state = { ...state, page: args!.page as State["page"] };
    if (command === "removeGame")
      state = {
        ...state,
        games: state.games.filter((g) => g.id !== args!.gameId),
      };
    return state as never;
  });
  render(<App />);
}
afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});
describe("游戏库原交互", () => {
  it("首页下滑切换游戏库，不在首页追加游戏库区块", async () => {
    setup("home");
    await screen.findByRole("heading", { name: "我的游戏" });
    expect(screen.queryByRole("heading", { name: "游戏库" })).toBeNull();
    fireEvent.wheel(screen.getByRole("main"), { deltaY: 80 });
    await screen.findByRole("heading", { name: "游戏库" });
    expect(rpc).toHaveBeenCalledWith("navigate", { page: "library" });
  });
  it("开启与启动互斥、游戏库没有添加卡片、移除只提交游戏 ID", async () => {
    setup("library");
    await screen.findByRole("heading", { name: "游戏库" });
    const before = within(screen.getByRole("article", { name: "未配置游戏" }));
    const ready = within(screen.getByRole("article", { name: "已配置游戏" }));
    expect(before.getAllByRole("button", { name: "开启 DLSS 5" })).toHaveLength(
      1,
    );
    expect(before.queryByRole("button", { name: /启动游戏/ })).toBeNull();
    expect(ready.getAllByRole("button", { name: "启动游戏" })).toHaveLength(1);
    expect(ready.queryByRole("button", { name: /开启 DLSS/ })).toBeNull();
    expect(document.querySelector(".add-game-card")).toBeNull();
    fireEvent.click(
      before.getByRole("button", { name: "从游戏库移除 未配置游戏" }),
    );
    await waitFor(() =>
      expect(screen.queryByRole("article", { name: "未配置游戏" })).toBeNull(),
    );
    expect(rpc).toHaveBeenCalledWith("removeGame", { gameId: "new" });
  });
  it("一键排查仅保留关闭和继续配置的原路径", async () => {
    vi.mocked(rpc).mockResolvedValue([
      { title: "驱动", detail: "需进游戏确认", unknown: true, problem: false },
    ]);
    const next = vi.fn();
    render(
      <DiagnosticsDialog
        game={games[0]}
        onClose={() => {}}
        onConfigure={next}
      />,
    );
    await screen.findByText("基础检查已完成");
    expect(screen.queryByRole("button", { name: "刷新运行状态" })).toBeNull();
    expect(screen.queryByRole("button", { name: "诊断预览" })).toBeNull();
    expect(screen.queryByRole("button", { name: "F8 控制面板" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "继续配置" }));
    expect(next).toHaveBeenCalledOnce();
  });
});
