// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import App from "./App";
import { SmartDialog } from "./SmartDialog";
import { emptyState, rpc, subscribe } from "./bridge";
import { defaultNeuralOptions } from "./NeuralSettings";
import { isDownloadComplete, magpieDownload } from "./downloadState";
import type { Download, State } from "./types";
vi.mock("./bridge", () => ({
  rpc: vi.fn(), subscribe: vi.fn(() => () => {}), isDesktop: true, isPreview: false,
  emptyState: { version: "test", page: "magpie", games: [], account: null, online: true,
    viewAvailable: true, busy: false, scanning: false, status: "", accountFeedback: "",
    settings: { dark: true, english: false, autoUpdate: true, presentMon: "" }, downloads: [], magpieStatus: "" },
}));
afterEach(() => { cleanup(); localStorage.clear(); vi.resetAllMocks(); });
const game = { id:"g", title:"测试游戏", cover:null, state:"Unconfigured", mode:null, steamAppId:null };
const account = { id:"a", email:"a@test", membership:{tier:"pro", expiresAt:null}, features:[{key:"schemes.premium.configure",allowed:true}] } as State["account"];
const download=(id:string,title:string,state:string,percent=50):Download=>({id,title,state,percent,size:100,detail:"",paused:false,independent:true});
it("已打开工具进入完成列表，失败与取消不算完成",()=>{
  expect(isDownloadComplete("已完成 · 已打开工具")).toBe(true);
  expect(isDownloadComplete("已完成 · 校验通过")).toBe(true);
  expect(isDownloadComplete("下载失败")).toBe(false);
  expect(isDownloadComplete("已取消")).toBe(false);
});
it("喜鹊进度只绑定对应品牌当前任务，不使用第一个下载",()=>{
  const rows=[download("other","其他游戏组件","下载中",90),download("old","AMD 大力喜鹊","已完成 · 已打开工具",100),download("a","AMD 大力喜鹊","下载中",28),download("n","大力喜鹊 · Magpie","已暂停",61)];
  expect(magpieDownload(rows,"amdMagpie")?.percent).toBe(28);
  expect(magpieDownload(rows,"magpie")?.percent).toBe(61);
  expect(magpieDownload([rows[0],rows[1]],"amdMagpie")).toBeUndefined();
});
it("真实下载页面会将AMD启动完成归入已完成页",async()=>{
  localStorage.setItem("mu-first-use-guide-v1","done");
  const state={...emptyState,page:"downloads",downloads:[download("a","AMD 大力喜鹊","已完成 · 已打开工具",100)]};
  vi.mocked(rpc).mockResolvedValue(state);
  render(<App/>);await screen.findByRole("heading",{name:"下载任务"});
  expect(screen.queryByRole("heading",{name:"AMD 大力喜鹊"})).toBeNull();
  fireEvent.click(screen.getByRole("button",{name:"已完成"}));
  expect(await screen.findByRole("heading",{name:"AMD 大力喜鹊"})).toBeTruthy();
});
it("显卡检测失败不阻断打开独立Daniel官方工具",async()=>{
  vi.mocked(rpc).mockImplementation(async command=>{ if(command==="recommend")throw new Error("检测失败");if(command==="daniel08")return true; });
  render(<SmartDialog game={game} state={emptyState} onClose={()=>{}} onComplete={()=>{}}/>);
  await screen.findByText("检测失败");
  const button=screen.getByRole("button",{name:"打开 Daniel 0.8 官方版"});expect((button as HTMLButtonElement).disabled).toBe(false);
  fireEvent.click(button);await waitFor(()=>expect(rpc).toHaveBeenCalledWith("daniel08",{gameId:"g"}));
});
it("已部署回执启动游戏期间禁止重复点击，且不会触发配置完成回调",async()=>{
  const receipt={backend:"mochizuki",passes:1,proxy:"dxgi.dll",graphicsApi:"dx12",statusLabel:"未验证",modeLabel:"标准",options:defaultNeuralOptions};
  let finish!:()=>void;const launch=new Promise<void>(r=>{finish=r;});
  vi.mocked(rpc).mockImplementation(async command=>{if(command==="neuralStatus")return receipt;if(command==="recommend")return {gpu:{name:"RX 7900 XT"},recommendation:{}};if(command==="launch")return launch;return null;});
  const complete=vi.fn(),close=vi.fn();
  render(<SmartDialog game={{...game,state:"Configured",mode:6}} state={{...emptyState,account}} onClose={close} onComplete={complete}/>);
  await screen.findByText("DLSS 5 神经渲染配置已部署");
  const button=screen.getByRole("button",{name:"启动游戏"});fireEvent.click(button);fireEvent.click(button);
  expect((button as HTMLButtonElement).disabled).toBe(true);
  expect(vi.mocked(rpc).mock.calls.filter(([c])=>c==="launch")).toHaveLength(1);
  finish();await waitFor(()=>expect(close).toHaveBeenCalledOnce());expect(complete).not.toHaveBeenCalled();
});

it("下载增量更新能显示新进度，省略字段不会清空页面状态",async()=>{
  localStorage.setItem("mu-first-use-guide-v1","done");
  let receive!:(event:string,data:unknown)=>void;
  vi.mocked(subscribe).mockImplementation(fn=>{receive=fn;return ()=>{};});
  vi.mocked(rpc).mockResolvedValue({...emptyState,page:"downloads",downloads:[]});
  render(<App/>);await screen.findByRole("heading",{name:"下载任务"});
  act(()=>receive("statePatch",{downloads:[download("new","测试组件下载","下载中",42)]}));
  expect(await screen.findByRole("heading",{name:"测试组件下载"})).toBeTruthy();
  act(()=>receive("statePatch",{status:"继续下载"}));
  expect(screen.getByRole("heading",{name:"测试组件下载"})).toBeTruthy();
});

it("确认实际图形接口后切换作者方案，资源检查保留该接口",async()=>{
  vi.mocked(rpc).mockImplementation(async command=>{
    if(command==="recommend")return {gpu:{name:"RX 9070 XT"},recommendation:{},is32Bit:false};
    if(command==="neuralPlan")return {plan:{backend:"mochizuki",notices:[],statusLabel:"实验性"},missingResources:[],resourcePreparation:[]};
    return null;
  });
  const allowed={...emptyState,account:{...account,features:[{key:"schemes.premium.configure",allowed:true},{key:"schemes.game.view",allowed:true}]}} as State;
  render(<SmartDialog game={game} state={allowed} onClose={()=>{}} onComplete={()=>{}}/>);
  fireEvent.change(await screen.findByLabelText("确认实际图形接口"),{target:{value:"dx12"}});
  fireEvent.click(screen.getByRole("button",{name:/作者调校版/}));
  await waitFor(()=>expect(rpc).toHaveBeenCalledWith("neuralPlan",expect.objectContaining({neural:expect.objectContaining({graphicsApi:"dx12",backend:"mochizuki"})})));
});

it("高级会员RX6000方案走固定0.6配置，不打开0.8且不请求多后端",async()=>{
  vi.mocked(rpc).mockImplementation(async command=>{
    if(command==="recommend")return {gpu:{name:"RX 6750 GRE"},recommendation:{},is32Bit:false};
    return null;
  });
  render(<SmartDialog game={game} state={{...emptyState,account}} onClose={()=>{}} onComplete={()=>{}}/>);
  await screen.findByText("RX 6750 GRE");
  fireEvent.click(screen.getByRole("button",{name:/60系显卡专用/}));
  expect(screen.getByText(/将打开 Daniel 0.6.0 安装窗口/)).toBeTruthy();
  fireEvent.change(screen.getByLabelText("60系显卡游戏图形接口"),{target:{value:"dx12"}});
  fireEvent.click(screen.getByRole("button",{name:"一键配置"}));
  await waitFor(()=>expect(rpc).toHaveBeenCalledWith("configure",expect.objectContaining({scheme:"rx6000",mode:0,confirmedApi:"dx12"})));
  expect(vi.mocked(rpc).mock.calls.some(([command])=>command==="daniel08"||command==="neuralPlan")).toBe(false);
});
it("普通账号不能选择RX6000会员方案进入安装",async()=>{
  vi.mocked(rpc).mockImplementation(async command=>command==="recommend"?{gpu:{name:"RX 6750 GRE"},recommendation:{}}:null);
  render(<SmartDialog game={game} state={emptyState} onClose={()=>{}} onComplete={()=>{}}/>);
  await screen.findByText("RX 6750 GRE");
  fireEvent.click(screen.getByRole("button",{name:/60系显卡专用/}));
  expect(screen.queryByLabelText("60系显卡游戏图形接口")).toBeNull();
  expect(vi.mocked(rpc).mock.calls.some(([command])=>command==="configure")).toBe(false);
});
