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
import type { Game, State } from "./types";
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
function setup(page: State["page"], libraryGames: Game[] = games, overrides: Partial<State> = {}) {
  let state = {
    page,
    games: libraryGames,
    settings: { dark: true },
    viewAvailable: true,
    downloads: [],
    online: true,
    status: "",
    ...overrides,
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
  localStorage.removeItem("mu-first-use-guide-v1");
});
describe("游戏库原交互", () => {
  it.each(["magpie", "amdMagpie"])("%s 下载未结束时仍可打开游戏配置、启动和排查", async command => {
    localStorage.setItem("mu-first-use-guide-v1", "done");
    setup("magpie", games, { account: { id: "a", email: "a@test", membership: { tier: "pro", expiresAt: null }, features: [{ key: "schemes.premium.configure", allowed: true }] } as State["account"] });
    await screen.findByRole("heading", { name: /一键开启大力喜鹊/ });
    const original = vi.mocked(rpc).getMockImplementation()!;
    let finish!: () => void;
    const download = new Promise<void>(resolve => { finish = resolve; });
    vi.mocked(rpc).mockImplementation(async (name, args) => {
      if (name === command) { await download; return true as never; }
      if (name === "recommend") return { gpu: { name: "RX 7900 XT", vramMb: 20480 }, width: 2560, height: 1440, recommendation: { qualityMode: 0, performanceMode: 4, availability: 0 }, hipSetupPending: false } as never;
      return original(name, args);
    });
    fireEvent.click(screen.getByRole("button", { name: command === "magpie" ? /N卡专用/ : /A卡专用/ }));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith(command, {}));
    expect((screen.getByRole("button", { name: /正在准备.*卡组件/ }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "游戏库" }));
    await screen.findByRole("heading", { name: "游戏库" });
    const before = within(screen.getByRole("article", { name: "未配置游戏" }));
    const ready = within(screen.getByRole("article", { name: "已配置游戏" }));
    expect((before.getByRole("button", { name: "开启 DLSS 5" }) as HTMLButtonElement).disabled).toBe(false);
    expect((ready.getByRole("button", { name: "启动游戏" }) as HTMLButtonElement).disabled).toBe(false);
    expect((before.getByRole("button", { name: "排查" }) as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(before.getByRole("button", { name: "开启 DLSS 5" }));
    await screen.findByRole("dialog", { name: "游戏图形方案" });
    finish();
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("state"));
  });
  it("原生喜鹊下载状态不锁游戏，真实游戏写入忙碌状态仍阻止冲突", async () => {
    setup("library", games, { magpiePreparing: true, busy: true });
    await screen.findByRole("heading", { name: "游戏库" });
    expect((screen.getByRole("button", { name: "开启 DLSS 5" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "启动游戏" }) as HTMLButtonElement).disabled).toBe(true);
  });
  it.each(["神力科莎", "Assetto Corsa", "244210"])("我的游戏实际页面可以用 %s 找到神力科莎卡片", async query => {
    setup("library", [...games, { id: "assetto", title: "assettocorsa", cover: null, state: "Unconfigured", mode: null,
      steamAppId: "244210", searchTerms: ["Assetto Corsa", "神力科莎"] }]);
    await screen.findByRole("heading", { name: "游戏库" });
    fireEvent.change(screen.getByPlaceholderText("搜索游戏 / Search games…"), { target: { value: query } });
    expect(screen.getByRole("article", { name: "assettocorsa" })).toBeTruthy();
    expect(screen.queryByRole("article", { name: "未配置游戏" })).toBeNull();
    expect(screen.queryByRole("article", { name: "已配置游戏" })).toBeNull();
  });
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
    expect(before.queryByRole("button", { name: "分享方案" })).toBeNull();
    expect(ready.getByRole("button", { name: "分享方案" })).toBeTruthy();
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
  it("已配置游戏直接读取当前配置并打开上传弹窗，不经过方案列表", async () => {
    setup("library");
    await screen.findByRole("heading", { name: "游戏库" });
    const original = vi.mocked(rpc).getMockImplementation()!;
    vi.mocked(rpc).mockImplementation(async (command, args) => command === "presetSnapshot"
      ? { mode: 0, componentVersion: "0.6.0", vulkan: false, gameName: "已配置游戏", settings: [] } as never
      : original(command, args));
    fireEvent.click(within(screen.getByRole("article", {name: "已配置游戏"})).getByRole("button", {name: "分享方案"}));
    await screen.findByRole("dialog", {name: "上传方案"});
    expect(rpc).toHaveBeenCalledWith("presetSnapshot", {gameId: "ready"});
    expect(vi.mocked(rpc).mock.calls.some(([command]) => command === "presets" || command === "launch" || command === "uploadPreset")).toBe(false);
    expect(screen.getByRole("button", {name: "发布分享"}).hasAttribute("disabled")).toBe(true);
  });
  it("配置读取失败保留原因，仍可在分享表单添加附件或网盘", async () => {
    setup("library");
    await screen.findByRole("heading", { name: "游戏库" });
    const original = vi.mocked(rpc).getMockImplementation()!;
    vi.mocked(rpc).mockImplementation(async (command, args) => {
      if (command === "presetSnapshot") throw new Error("当前游戏配置文件不存在");
      return original(command, args);
    });
    fireEvent.click(within(screen.getByRole("article", {name: "已配置游戏"})).getByRole("button", {name: "分享方案"}));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "当前游戏配置文件不存在");
    expect(screen.getByRole("dialog", {name: "上传方案"})).toBeTruthy();
    expect(screen.getByLabelText("网盘链接（可选）")).toBeTruthy();
    expect(screen.getByLabelText("分享附件")).toBeTruthy();
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
