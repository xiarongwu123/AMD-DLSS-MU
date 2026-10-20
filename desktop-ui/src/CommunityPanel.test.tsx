// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { CommunityPanel } from "./CommunityPanel";
import { rpc } from "./bridge";
import type { Game } from "./types";
vi.mock("./bridge", () => ({ rpc: vi.fn(), isDesktop: true }));
const games = [{ id: "a", title: "游戏甲", steamAppId: "1" }, { id: "b", title: "游戏乙", steamAppId: "2" }] as Game[];
const item = { id: "shared", gameKey: "steam:1", gameName: "游戏甲", scope: "universal", mode: 4, goal: "performance", title: "玩家通用配置", description: "跨游戏复用", author: "玩家甲", componentVersion: "fixture", settingCount: 1, resourceUrl: "https://pan.quark.cn/s/fixture", resourceCode: "abcd", attachment: { name: "画质预设.ini", size: 12, sha256: "A".repeat(64) } };
afterEach(() => { cleanup(); vi.resetAllMocks(); });
it("无需选游戏即可浏览所有玩家分享，并能翻页", async () => {
 vi.mocked(rpc).mockResolvedValue({ items: [item], total: 31, page: 1 });
 render(<CommunityPanel games={[]} onApplied={() => {}}/>);
 await screen.findByText("玩家通用配置");
 expect(rpc).toHaveBeenCalledWith("presets", { page: 1 });
 expect(screen.queryByText(/只显示当前游戏/)).toBeNull();
 fireEvent.click(screen.getByRole("button", { name: "下一页" }));
 await waitFor(() => expect(rpc).toHaveBeenCalledWith("presets", { page: 2 }));
});
it("没有已配置游戏也能发布网盘链接和提取码，不调用配置读取", async () => {
 vi.mocked(rpc).mockResolvedValue({ items: [], total: 0 });
 render(<CommunityPanel games={[]} onApplied={() => {}}/>);
 fireEvent.click(await screen.findByRole("button", { name: "发布分享" }));
 fireEvent.change(screen.getByLabelText("标题"), { target: { value: "配套插件分享" } });
 fireEvent.change(screen.getByLabelText("说明"), { target: { value: "网盘中的画质预设" } });
 fireEvent.change(screen.getByLabelText("网盘链接（可选）"), { target: { value: item.resourceUrl } });
 fireEvent.change(screen.getByLabelText("提取码（可选）"), { target: { value: "abcd" } });
 fireEvent.click(screen.getByRole("button", { name: "发布分享" }));
 await waitFor(() => expect(rpc).toHaveBeenCalledWith("submitPreset", expect.objectContaining({ includeConfiguration: false, scope: "universal", resourceUrl: item.resourceUrl, resourceCode: "abcd" }), undefined));
 expect(rpc).not.toHaveBeenCalledWith("presetSnapshot", expect.anything());
});
it("附件作为真实文件交给Windows桥接，不拼接本地路径或自动安装", async () => {
 vi.mocked(rpc).mockResolvedValue({ items: [], total: 0 });
 render(<CommunityPanel games={[]} onApplied={() => {}}/>);
 fireEvent.click(await screen.findByRole("button", { name: "发布分享" }));
 fireEvent.change(screen.getByLabelText("标题"), { target: { value: "配置文件分享" } });
 fireEvent.change(screen.getByLabelText("说明"), { target: { value: "预设参数文件" } });
 const file = new File(["[Quality]\nValue=1"], "画质预设.ini");
 fireEvent.change(screen.getByLabelText("分享附件"), { target: { files: [file] } });
 fireEvent.click(screen.getByRole("button", { name: "发布分享" }));
 await waitFor(() => expect(rpc).toHaveBeenCalledWith("submitPreset", expect.objectContaining({ includeConfiguration: false }), [file]));
 expect(rpc).not.toHaveBeenCalledWith("applyPreset", expect.anything());
});
it("通用方案选择目标游戏后应用，附件和网盘分别操作", async () => {
 vi.mocked(rpc).mockImplementation(async command => command === "preset" ? { item, settings: [], imageData: null } : command === "presets" ? { items: [item], total: 1 } : true);
 render(<CommunityPanel games={games} onApplied={() => {}}/>);
 fireEvent.click(await screen.findByRole("button", { name: /玩家通用配置/ }));
 await screen.findByText("附件与配套资源");
 expect((screen.getByRole("button", { name: "一键使用这套方案" }) as HTMLButtonElement).disabled).toBe(true);
 fireEvent.change(screen.getByLabelText("应用到游戏"), { target: { value: "b" } });
 fireEvent.click(screen.getByRole("button", { name: "一键使用这套方案" }));
 await waitFor(() => expect(rpc).toHaveBeenCalledWith("applyPreset", { gameId: "b", id: "shared" }));
 fireEvent.click(screen.getByRole("button", { name: "下载附件" }));
 await waitFor(() => expect(rpc).toHaveBeenCalledWith("presetAttachment", { id: "shared" }));
 fireEvent.click(screen.getByRole("button", { name: "打开网盘" }));
 await waitFor(() => expect(rpc).toHaveBeenCalledWith("presetResource", { id: "shared", copy: false }));
});
