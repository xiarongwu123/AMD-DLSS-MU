// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor, fireEvent } from "@testing-library/react";
import App from "./App";
import { emptyState, rpc } from "./bridge";

vi.mock("./bridge", () => ({
  rpc: vi.fn(),
  subscribe: () => () => {},
  isDesktop: true,
  isPreview: false,
  emptyState: {
    version: "test", page: "library", games: [], account: null, online: true,
    viewAvailable: true, busy: false, scanning: false, status: "", accountFeedback: "",
    settings: { dark: true, english: false, autoUpdate: true, presentMon: "" },
    downloads: [], magpieStatus: "",
  },
}));

afterEach(() => { cleanup(); localStorage.clear(); vi.resetAllMocks(); });

it("大力喜鹊页的 A卡按钮无需选择游戏即可直接打开独立工具",async()=>{
  localStorage.setItem("mu-first-use-guide-v1","done");
  const state={...emptyState,page:"magpie",account:{id:"a",email:"a@test",membership:{tier:"pro",expiresAt:null},features:[{key:"schemes.premium.configure",allowed:true}]}};
  vi.mocked(rpc).mockImplementation(async command=>command==="state"?state:command==="amdMagpie"?true:undefined);
  render(<App/>);
  fireEvent.click(await screen.findByRole("button",{name:/A卡专用/}));
  await waitFor(()=>expect(rpc).toHaveBeenCalledWith("amdMagpie",{}));
  expect(rpc).not.toHaveBeenCalledWith("configure",expect.anything());
  expect(rpc).not.toHaveBeenCalledWith("recommend",expect.anything());
  expect(screen.queryByRole("dialog",{name:"游戏图形方案"})).toBeNull();
});

it("首次进入游戏库自动引导，跳过后重新打开不会自动播放", async () => {
  vi.mocked(rpc).mockImplementation(async command =>
    command === "state" ? emptyState : undefined,
  );
  const view = render(<App />);
  expect(await screen.findByRole("dialog", { name: "新手引导" })).toHaveProperty(
    "textContent", expect.stringContaining("先把游戏加进来"),
  );
  fireEvent.click(screen.getByRole("button", { name: "跳过引导" }));
  expect(localStorage.getItem("mu-first-use-guide-v1")).toBe("done");
  view.unmount();
  render(<App />);
  await waitFor(() => expect(screen.getByRole("button", { name: /新手引导/ })).toBeTruthy());
  expect(screen.queryByRole("dialog", { name: "新手引导" })).toBeNull();
});

it("无需选择游戏即可从游戏库顶部打开全部玩家方案", async () => {
  localStorage.setItem("mu-first-use-guide-v1", "done");
  const state = { ...emptyState, games: [{ id: "game-1", title: "游戏一", cover: null,
    state: "Unconfigured", mode: null, steamAppId: null }] };
  vi.mocked(rpc).mockImplementation(async command => {
    if (command === "state") return state;
    if (command === "presets") return { items: [], total: 0 };
    return undefined;
  });
  render(<App />);
  await waitFor(() => expect(document.querySelector('[data-guide="community-header"]')).not.toBeNull());
  fireEvent.click(document.querySelector('[data-guide="community-header"]')!);
  expect(await screen.findByRole("dialog", { name: "玩家方案" })).toBeTruthy();
  expect(rpc).toHaveBeenCalledWith("presets", { page: 1 });
  expect(rpc).not.toHaveBeenCalledWith("recommend", { gameId: "game-1" });
});
