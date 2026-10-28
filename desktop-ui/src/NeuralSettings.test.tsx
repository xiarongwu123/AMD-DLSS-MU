// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { NeuralSettings, defaultNeuralOptions } from "./NeuralSettings";
import { SmartDialog } from "./SmartDialog";
import { rpc, emptyState } from "./bridge";
import type { NeuralOptions } from "./types";
vi.mock("./bridge", () => ({ rpc: vi.fn(), subscribe: () => () => {}, emptyState: { downloads: [], status: "", busy: false } }));
afterEach(() => { cleanup(); vi.resetAllMocks(); });
const game = { id: "nr-game", title: "游戏", cover: null, state: "Unconfigured", mode: null, steamAppId: null };
const receipt = { backend: "mochizuki", passes: 3, proxy: "dxgi.dll", graphicsApi: "dx12", statusLabel: "实验性", modeLabel: "高质量", options: { ...defaultNeuralOptions, backend: "mochizuki", passes: 3 } };
describe("神经渲染交互", () => {
  it("缺资源时直接显示缺项和导入入口，导入后重新检查", async () => {
    const missing = "Mochizuki 模型校验失败：需要匹配的 dlssnr.bin。";
    let imported = false;
    vi.mocked(rpc).mockImplementation(async command => {
      if (command === "neuralResources") { imported = true; return true; }
      return { plan: { ...receipt, notices: [] }, missingResources: imported ? [] : [missing], resourcePreparation: [], installed: null };
    });
    const checked = vi.fn();
    render(<NeuralSettings gameId={game.id} options={receipt.options} onChange={() => {}} onResourceCheck={checked} />);
    await screen.findByText("缺少必需资源，暂不能配置此方案");
    expect(screen.getByRole("alert").textContent).toContain(missing);
    expect(document.querySelector("details")?.open).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "导入所需资源" }));
    await screen.findByText("本机必需资源已检查");
    expect(checked).toHaveBeenLastCalledWith(expect.objectContaining({ missingResources: [] }));
  });
  it("模型可自动准备时明确解释首次下载，不伪装成已就绪", async () => {
    const preparation = "Mochizuki 模型尚未就绪，将从内置资源提取 dlssnr.bin。";
    vi.mocked(rpc).mockResolvedValue({ plan: { ...receipt, notices: [] }, missingResources: [], resourcePreparation: [preparation], installed: null });
    render(<NeuralSettings gameId={game.id} options={receipt.options} onChange={() => {}} />);
    await screen.findByText("首次使用需准备资源 · MU 自动处理");
    expect(screen.getByText(preparation)).toBeTruthy();
    expect(screen.queryByText("本机必需资源已检查")).toBeNull();
    expect(screen.queryByRole("button", { name: "导入所需资源" })).toBeNull();
  });
  it("缺模型时不允许提交配置，也不显示已匹配基础条件", async () => {
    vi.mocked(rpc).mockImplementation(async command => {
      if (command === "recommend") return { gpu: { name: "AMD RX 9070 XT", vramMb: 16384 }, width: 2560, height: 1440, recommendation: { qualityMode: 0, performanceMode: 4, availability: 0 }, hipSetupPending: false };
      if (command === "neuralPlan") return { plan: { ...receipt, notices: [] }, missingResources: ["缺少必需模型"], installed: null };
      return null;
    });
    render(<SmartDialog game={game} state={{...emptyState,account:{id:"a",email:"a@test",membership:{tier:"pro",expiresAt:null},paymentsEnabled:true,features:[{key:"schemes.premium.configure",allowed:true}]}}} onClose={() => {}} onComplete={() => {}} />);
    await screen.findByText("AMD RX 9070 XT");
    fireEvent.click(screen.getByRole("button", { name: /作者调校版/ }));
    const blocked = await screen.findByRole("button", { name: "需先补齐资源" });
    expect((blocked as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByText("已匹配基础条件；游戏运行效果仍需确认。")).toBeNull();
    fireEvent.click(blocked);
    expect(vi.mocked(rpc).mock.calls.some(([command]) => command === "configure")).toBe(false);
  });
  it("RX 7000 适配可选择，清除不属于该宿主的开关并限制为三 Pass", async () => {
    vi.mocked(rpc).mockResolvedValue({ plan: { ...receipt, backend: "lmxxf-rdna3", notices: [] }, missingResources: [], installed: null });
    function Harness() { const [options, setOptions] = useState<NeuralOptions>({ ...defaultNeuralOptions, backend: "mochizuki", passes: 30, nrInline: true, earlyAnyModuleWrap: true, modelScale: 0.5, temporal: false, historyStrength: 0.2 }); return <NeuralSettings gameId={game.id} options={options} onChange={setOptions} />; }
    render(<Harness />);
    fireEvent.change(screen.getByLabelText("NR 后端"), { target: { value: "lmxxf-rdna3" } });
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("neuralPlan", expect.objectContaining({ neural: expect.objectContaining({ backend: "lmxxf-rdna3", passes: null, nrInline: null, earlyAnyModuleWrap: null, modelScale: 1, temporal: true, historyStrength: 1 }) })));
    expect(screen.getByLabelText("神经 Pass").textContent).not.toContain("30 Pass");
    expect(screen.queryByLabelText("NR Inline（实验性）")).toBeNull();
    expect(screen.queryByRole("button", { name: "准备本机资源" })).toBeNull();
    expect(screen.getByText(/AMDNR by 3zwr1 · RX 7000 实验路线/)).toBeTruthy();
    fireEvent.click(screen.getByLabelText("RX 7000 Fast mode"));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("neuralPlan", expect.objectContaining({ neural: expect.objectContaining({ lmxxfFastMode: false }) })));
  });
  it("自动匹配 RX 7000 后展示专属设置，已安装后禁止直接切换运行库", async () => {
    vi.mocked(rpc).mockResolvedValue({ plan: { ...receipt, backend: "lmxxf-rdna3", notices: [] }, missingResources: [], installed: null });
    render(<NeuralSettings gameId={game.id} options={defaultNeuralOptions} onChange={() => {}} installed />);
    await screen.findByLabelText("RX 7000 Fast mode");
    expect((screen.getByLabelText("NR 后端") as HTMLSelectElement).disabled).toBe(true);
    expect(screen.queryByLabelText("增强屏障互操作")).toBeNull();
  });
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
    const complete = vi.fn(), close = vi.fn();
    render(<SmartDialog game={game} state={{...emptyState,account:{id:"a",email:"a@test",membership:{tier:"pro",expiresAt:null},paymentsEnabled:true,features:[{key:"schemes.premium.configure",allowed:true}]}}} onClose={close} onComplete={complete} />);
    await screen.findByText("AMD RX 9070 XT");
    fireEvent.click(screen.getByText("原有目标与自定义设置"));
    fireEvent.click(screen.getByRole("button", { name: /自定义/ }));
    fireEvent.click(screen.getByRole("radio", { name: /多后端/ }));
    fireEvent.click(screen.getByRole("button", { name: "一键配置" }));
    await screen.findByText("DLSS 5 神经渲染配置已部署");
    expect(screen.queryByText("DLSS 5 神经渲染已启用")).toBeNull();
    expect(complete).not.toHaveBeenCalled();
    for (const name of ["启动游戏", "高级设置", "切换方案", "恢复之前配置"]) expect(screen.getByRole("button", { name })).toBeTruthy();
    expect(screen.getByText("Mochizuki")).toBeTruthy();
    expect(screen.getByText("高质量")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "启动游戏" }));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("launch", { gameId: game.id }));
    await waitFor(() => expect(close).toHaveBeenCalledTimes(1));
    expect(complete).not.toHaveBeenCalled();
  });
});
