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
  it("6750 未确认游戏接口或窗口时仍直接打开 AMD 喜鹊，不执行游戏安装",async()=>{
    vi.mocked(rpc).mockImplementation(async command=>command==="recommend"?{...recommendation,gpu:{name:"AMD RX 6750 XT",vramMb:12288}}:command==="amdMagpie"?true:undefined);
    const complete=vi.fn();
    render(<SmartDialog game={game} state={{...emptyState,viewAvailable:true,account:{id:"a",email:"a@test",membership:{tier:"pro",expiresAt:null},paymentsEnabled:true,features:[{key:"schemes.premium.configure",allowed:true}]}}} onClose={()=>{}} onComplete={complete}/>);
    await screen.findByText("AMD RX 6750 XT");
    fireEvent.click(screen.getByRole("button",{name:/AMD 大力喜鹊.*高级会员/}));
    fireEvent.click(screen.getByRole("button",{name:"开启 AMD 大力喜鹊"}));
    await waitFor(()=>expect(rpc).toHaveBeenCalledWith("amdMagpie"));
    await screen.findByText(/已打开 AMD 大力喜鹊，请在工具内选择效果/);
    expect(rpc).not.toHaveBeenCalledWith("configure",expect.anything());
    expect(rpc).not.toHaveBeenCalledWith("gameRecommendation",expect.anything());
    expect(complete).not.toHaveBeenCalled();
    expect(screen.queryByText(/需自行提供与该版本匹配/)).toBeNull();
  });
  it("AMD 喜鹊取消准备时不会显示打开成功",async()=>{
    vi.mocked(rpc).mockImplementation(async command=>command==="recommend"?recommendation:command==="amdMagpie"?false:undefined);
    render(<SmartDialog game={game} state={{...emptyState,viewAvailable:true,account:{id:"a",email:"a@test",membership:{tier:"pro",expiresAt:null},paymentsEnabled:true,features:[{key:"schemes.premium.configure",allowed:true}]}}} onClose={()=>{}} onComplete={()=>{}}/>);
    await screen.findByText("AMD RX 7900 XT");
    fireEvent.click(screen.getByRole("button",{name:/AMD 大力喜鹊.*高级会员/}));
    fireEvent.click(screen.getByRole("button",{name:"开启 AMD 大力喜鹊"}));
    await waitFor(()=>expect(rpc).toHaveBeenCalledWith("amdMagpie"));
    expect(screen.queryByText(/已打开 AMD 大力喜鹊，请在工具内选择效果/)).toBeNull();
  });
  it("免费 Daniel 0.8 打开官方流程，不冒报游戏配置完成", async () => {
    vi.mocked(rpc).mockImplementation(async command => command === "recommend" ? recommendation : command === "daniel08" ? true : undefined);
    const complete = vi.fn();
    render(<SmartDialog game={game} state={emptyState} onClose={() => {}} onComplete={complete}/>);
    await screen.findByText("AMD RX 7900 XT");
    fireEvent.click(screen.getByRole("button", {name:"打开 Daniel 0.8 官方版"}));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("daniel08",{gameId:game.id}));
    await screen.findByText(/已打开 Daniel 0.8 官方安装器/);
    expect(rpc).not.toHaveBeenCalledWith("configure", expect.anything());
    expect(complete).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog", {name:"开通高级会员"})).toBeNull();
  });
  it("平铺的高帧率方案不要求会员，继续调用原有模式4", async () => {
    vi.mocked(rpc).mockImplementation(async command => command === "recommend" ? recommendation : undefined);
    render(<SmartDialog game={game} state={emptyState} onClose={() => {}} onComplete={() => {}}/>);
    await screen.findByText("AMD RX 7900 XT");
    fireEvent.click(screen.getByRole("button", {name:/高帧率版/}));
    fireEvent.click(screen.getByRole("button", {name:"一键配置"}));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("configure", {gameId:game.id,confirmedApi:"",confirmedInput:"",windowed:"",mode:4,scheme:"fps",vulkan:false,fgInput:"nofg"}));
    expect(screen.queryByRole("dialog", {name:"开通高级会员"})).toBeNull();
  });
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
      (screen.getByRole("button", { name: "打开 Daniel 0.8 官方版" }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
    resolve(recommendation);
    await screen.findByText("AMD RX 7900 XT");
    expect(
      (screen.getByRole("button", { name: "打开 Daniel 0.8 官方版" }) as HTMLButtonElement)
        .disabled,
    ).toBe(false);
  });
  it("画质和帧率保留原路线，新增方案只追加到自定义清单", async () => {
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
    fireEvent.click(screen.getByText("原有目标与自定义设置"));
    fireEvent.click(screen.getByRole("button", { name: /画质优先/ }));
    expect(screen.queryAllByRole("radio")).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: "一键配置" }));
    await waitFor(() =>
      expect(rpc).toHaveBeenCalledWith("configure", {
        gameId: "test-game",
        confirmedApi: "",confirmedInput:"",windowed:"",
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
        confirmedApi: "",confirmedInput:"",windowed:"",
        mode: 4,
        vulkan: false,
        fgInput: "nofg",
      }),
    );
    await waitFor(() => expect(complete).toHaveBeenCalledTimes(2));
    fireEvent.click(screen.getByRole("button", { name: /自定义/ }));
    expect(screen.getAllByRole("radio")).toHaveLength(5);
    fireEvent.click(screen.getByRole("radio", { name: /标准 OptiScaler/ }));
    fireEvent.click(screen.getByLabelText("Vulkan 接口"));
    fireEvent.change(screen.getByRole("combobox",{name:"帧生成入口"}), {
      target: { value: "fsrfg" },
    });
    fireEvent.click(screen.getByRole("button", { name: "一键配置" }));
    await waitFor(() =>
      expect(rpc).toHaveBeenCalledWith("configure", {
        gameId: "test-game",
        confirmedApi: "vulkan",confirmedInput:"",windowed:"",
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
    fireEvent.click(screen.getByText("原有目标与自定义设置"));
    fireEvent.click(screen.getByRole("button", { name: /画质优先/ }));
    fireEvent.click(screen.getByRole("button", { name: "一键配置" }));
    expect(await screen.findByRole("alert")).toHaveProperty(
      "textContent",
      "模型文件缺失",
    );
    expect(complete).not.toHaveBeenCalled();
  });
  it("社区入口列出全部分享，选中后才应用到目标游戏", async () => {
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
    await screen.findByText(/还没有玩家分享内容/);
    expect(rpc).toHaveBeenCalledWith("presets", { page: 1 });
    expect(rpc).not.toHaveBeenCalledWith("recommend", { gameId: "test-game" });
    fireEvent.click(screen.getByRole("tab", { name: "智能推荐" }));
    await waitFor(() => expect(rpc).toHaveBeenCalledWith("recommend", { gameId: "test-game" }));
  });
  it("32位不再禁用帧率或强制替换模式，仍可单独选择老游戏桥接",async()=>{
    vi.mocked(rpc).mockImplementation(async command=>command==="recommend" ? {...recommendation,is32Bit:true,graphicsApi:"dx9"} : undefined);
    render(<SmartDialog game={game} state={{...emptyState,account:{id:"a",email:"a@test",membership:{tier:"pro",expiresAt:null},paymentsEnabled:true,features:[{key:"schemes.premium.configure",allowed:true}]}}} onClose={()=>{}} onComplete={()=>{}}/>);
    await screen.findByText("AMD RX 7900 XT");
    fireEvent.click(screen.getByText("原有目标与自定义设置"));
    fireEvent.click(screen.getByRole("button",{name:/画质优先/}));
    fireEvent.click(screen.getByRole("button",{name:"一键配置"}));
    await waitFor(()=>expect(rpc).toHaveBeenCalledWith("configure",{gameId:game.id,confirmedApi:"",confirmedInput:"",windowed:"",mode:0,vulkan:false,fgInput:"nofg"}));
    fireEvent.click(screen.getByRole("button",{name:/帧率优先/}));
    expect((screen.getByRole("button",{name:"一键配置"}) as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(screen.getByRole("button",{name:"一键配置"}));
    await waitFor(()=>expect(rpc).toHaveBeenCalledWith("configure",expect.objectContaining({mode:4})));
    fireEvent.click(screen.getByRole("button",{name:/自定义/}));
    expect(screen.getAllByRole("radio")).toHaveLength(5);
    expect(screen.getByRole("radio",{name:/标准 OptiScaler/})).toBeTruthy();
    fireEvent.click(screen.getByRole("button",{name:/老游戏专用版/}));
    fireEvent.change(screen.getByRole("combobox",{name:"32位游戏图形接口"}),{target:{value:"opengl"}});
    fireEvent.click(screen.getByRole("button",{name:"一键配置"}));
    await waitFor(()=>expect(rpc).toHaveBeenCalledWith("configure",{gameId:game.id,confirmedApi:"opengl",confirmedInput:"",windowed:"",mode:5,scheme:"legacy",x86Api:"opengl",vulkan:false,fgInput:"nofg"}));
  });

});

it.each([['连接中',false],['已暂停',true]])('配置中可控制%s的下载，并收起窗口',async(state,paused)=>{
 let finish!:()=>void;
 vi.mocked(rpc).mockImplementation(async command=>{
   if(command==='recommend')return recommendation;
   if(command==='configure')return new Promise<void>(resolve=>{finish=resolve;});
   return undefined;
 });
 const close=vi.fn();
 render(<SmartDialog game={game} state={{...emptyState,downloads:[{id:'installation',title:'神经渲染组件',state,paused,percent:10,size:100,detail:'',independent:false}]}} onClose={close} onComplete={()=>{}}/>);
 await screen.findByText('AMD RX 7900 XT');
 fireEvent.click(screen.getByRole('button',{name:/高帧率版/}));fireEvent.click(screen.getByRole('button',{name:'一键配置'}));
 await screen.findByRole('button',{name:paused?'继续下载':'暂停下载'});
 fireEvent.click(screen.getByRole('button',{name:paused?'继续下载':'暂停下载'}));
 await waitFor(()=>expect(rpc).toHaveBeenCalledWith('downloadAction',{id:'installation',action:'pause'}));
 fireEvent.click(screen.getByRole('button',{name:'取消下载'}));
 await waitFor(()=>expect(rpc).toHaveBeenCalledWith('downloadAction',{id:'installation',action:'cancel'}));
 fireEvent.click(screen.getByRole('button',{name:'收起并后台继续'}));expect(close).toHaveBeenCalledOnce();
 finish();
});
