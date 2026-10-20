// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { NeuralSettings, defaultNeuralOptions } from "./NeuralSettings";
import { SmartDialog } from "./SmartDialog";
import { rpc, emptyState } from "./bridge";
vi.mock("./bridge", () => ({ rpc: vi.fn(), subscribe: () => () => {}, emptyState: { downloads: [], status: "", busy: false } }));
afterEach(() => { cleanup(); vi.resetAllMocks(); });
const game = { id: "nr-game", title: "游戏", cover: null, state: "Unconfigured", mode: null, steamAppId: null };
const receipt = { backend: "mochizuki", passes: 3, proxy: "dxgi.dll", graphicsApi: "dx12", statusLabel: "实验性", modeLabel: "高质量", options: { ...defaultNeuralOptions, backend: "mochizuki", passes: 3 } };
describe("神经渲染交互", () => {
  it("普通视图折叠技术参数，高级设置将 30 Pass 标为压力测试", async () => {
    vi.mocked(rpc).mockResolvedValue({ plan: { ...receipt, notices: [] }, missingResources: [], installed: null });
    function Harness() { const [options, setOptions] = useState({ ...defaultNeuralOptions, backend: "mochizuki" }); return <NeuralSettings gameId={game.id} options={options} onChange={setOptions} />; }
    render(<Harness />);
    expect(document.querySelector("details")?.open).toBe(false);
    fireEvent.click(screen.getByText("高级设置"));
    const passes = screen.getByLabelText("神经 Pass");
    expect(passes.textContent).toContain("实验 / 压力测试 · 30 Pass");
    fireEvent.change(passes, { target: { value: "30" } });
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("neuralPlan", expect.objectContaining({ neural: expect.objectContaining({ backend: "mochizuki", passes: 30 }) })));
    expect(screen.getByText(/额外 Pass 会增加耗时/)).toBeTruthy();
  });
  it("配置成功显示真实回执及四个后续入口，不提前关闭", async () => {
    vi.mocked(rpc).mockImplementation(async command => {
      if (command === "recommend") return { gpu: { name: "AMD RX 9070 XT", vramMb: 16384 }, width: 2560, height: 1440, recommendation: { qualityMode: 0, performanceMode: 4, availability: 0 }, hipSetupPending: false };
      if (command === "configure") return receipt;
      if (command === "neuralPlan") return { plan: { ...receipt, notices: [] }, missingResources: [], installed: null };
      return null;
    });
    const complete = vi.fn();
    render(<SmartDialog game={game} state={{...emptyState,account:{id:"a",email:"a@test",membership:{tier:"pro",expiresAt:null},paymentsEnabled:true,features:[{key:"schemes.premium.configure",allowed:true}]}}} onClose={() => {}} onComplete={complete} />);
    await screen.findByText("AMD RX 9070 XT");
    fireEvent.click(screen.getByText("原有目标与自定义设置"));
    fireEvent.click(screen.getByRole("button", { name: /自定义/ }));
    fireEvent.click(screen.getByRole("radio", { name: /多后端/ }));
    fireEvent.click(screen.getByRole("button", { name: "一键配置" }));
    await screen.findByText("DLSS 5 神经渲染已启用");
    expect(complete).not.toHaveBeenCalled();
    for (const name of ["启动游戏", "高级设置", "切换方案", "恢复之前配置"]) expect(screen.getByRole("button", { name })).toBeTruthy();
    expect(screen.getByText("Mochizuki")).toBeTruthy();
    expect(screen.getByText("高质量")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "启动游戏" }));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("launch", { gameId: game.id }));
    await waitFor(() => expect(complete).toHaveBeenCalledTimes(1));
  });
});
