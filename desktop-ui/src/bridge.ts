import type { State, NeuralOptions } from "./types";
interface Envelope {
  id?: string;
  ok?: boolean;
  result?: unknown;
  error?: string;
  event?: string;
  data?: unknown;
}
interface Webview {
  postMessage: (value: unknown) => void;
  postMessageWithAdditionalObjects?: (value: unknown, files: File[]) => void;
  addEventListener: (
    kind: "message",
    callback: (event: { data: Envelope }) => void,
  ) => void;
}
declare global {
  interface Window {
    chrome?: { webview?: Webview };
  }
}
const pending = new Map<
  string,
  {
    resolve: (value: unknown) => void;
    reject: (error: Error) => void;
    timer: ReturnType<typeof setTimeout>;
  }
>();
const listeners = new Set<(event: string, data: unknown) => void>();
window.chrome?.webview?.addEventListener("message", ({ data }) => {
  if (data.event) {
    listeners.forEach((fn) => fn(data.event!, data.data));
    return;
  }
  const p = data.id && pending.get(data.id);
  if (!p) return;
  clearTimeout(p.timer);
  pending.delete(data.id!);
  if (data.ok) p.resolve(data.result);
  else p.reject(new Error(data.error || "操作未完成"));
});
export const isDesktop = !!window.chrome?.webview;
export const isPreview =
  !isDesktop && new URLSearchParams(location.search).get("preview") === "1";
export function subscribe(fn: (event: string, data: unknown) => void) {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}
export async function rpc<T = void>(
  command: string,
  args: Record<string, unknown> = {},
  files?: File[],
): Promise<T> {
  if (!isDesktop) {
    if (isPreview) return preview(command, args) as Promise<T>;
    throw new Error("请在 MU Windows 客户端中使用此功能。浏览器仅能查看界面。");
  }
  const id = crypto.randomUUID();
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(
      () => {
        pending.delete(id);
        reject(new Error("操作仍未完成，请查看下载任务或诊断状态。"));
      },
      command === "recommendationApply" || command === "gameRecommendation" || command === "recommendationFeedback" || command === "recommendationVerify" || command === "configure" || command === "applyPreset" || command === "update" || command === "magpie" || command === "amdMagpie" || command === "daniel08"
        ? 3600000
        : 120000,
    );
    pending.set(id, { resolve: (value) => resolve(value as T), reject, timer });
    const host = window.chrome!.webview!;
    if (files) {
      if (!host.postMessageWithAdditionalObjects) {
        clearTimeout(timer);
        pending.delete(id);
        reject(new Error("当前 WebView2 不支持传递文件，请更新 WebView2 后重试。"));
        return;
      }
      host.postMessageWithAdditionalObjects({ id, command, args }, files);
    } else host.postMessage({ id, command, args });
  });
}
export const emptyState: State = {
  version: "React 开发版",
  page: "account",
  games: [],
  account: null,
  online: false,
  viewAvailable: false,
  busy: false,
  scanning: false,
  status: "",
  accountFeedback: "",
  settings: { dark: true, english: false, autoUpdate: true, presentMon: "" },
  downloads: [],
  magpieStatus: "",
};
// Explicit browser preview fixtures never run in the packaged desktop host.
const previewState: State = {
  ...emptyState,
  page: "home",
  online: true,
  viewAvailable: true,
  version: "2.1.2 · React 预览",
  account: {
    id: "preview",
    email: "preview@mu.local",
    membership: { tier: "free", expiresAt: null },
    features: [{key:"games.supported.view",enabled:true,minimumTier:"pro",allowed:false},{key:"dlss.configure",enabled:true,minimumTier:"standard",allowed:true}],
    paymentsEnabled: true,
  },
  games: [
    {
      id: "preview-game",
      title: "我的游戏",
      cover: "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1091500/library_600x900.jpg",
      state: "Unconfigured",
      mode: null,
      steamAppId: null,
    },
  ],
};
async function preview(
  command: string,
  args: Record<string, unknown>,
): Promise<unknown> {
  if (command === "plans") return { paymentsEnabled:true,cdkPlanMatching:true,items:[
    {id:"mu-pro-monthly",name:"高级会员",durationDays:30,configurationSessions:0,priceMinor:990,currency:"CNY"},
    {id:"mu-pro-lifetime",name:"终身会员",durationDays:0,configurationSessions:3,priceMinor:8880,currency:"CNY"},
    {id:"mu-pro-assisted",name:"高级 Plus 会员",durationDays:30,configurationSessions:1,priceMinor:2990,currency:"CNY"},
    {id:"mu-remote-once",name:"远程指导服务",durationDays:0,grantsMembership:false,configurationSessions:1,priceMinor:3000,currency:"CNY"}
  ]};
  if (command === "orders") return {items:[],paymentsEnabled:true};
  if (command === "refreshAccount") return;
  if (command === "createOrder") throw new Error("浏览器仅预览套餐；请在 Windows 客户端创建真实付款申请。");
  if (command === "state") return structuredClone(previewState);
  if (command === "navigate") {
    previewState.page = args.page as State["page"];
    return;
  }
  if (command === "settings") {
    previewState.settings = { ...previewState.settings, ...args };
    listeners.forEach((fn) => fn("state", structuredClone(previewState)));
    return;
  }
  if (command === "recommend") {
    await new Promise((r) => setTimeout(r, 1300));
    return {
      gpu: { name: "AMD Radeon RX 7900 XT", vramMb: 20480 },
      width: 2560,
      height: 1440,
      recommendation: {
        qualityMode: 0,
        performanceMode: 4,
        qualityReason: "基础条件已匹配，实际画质请进游戏确认。",
        performanceReason: "DLSS 5 + XeFG，实际帧生成请进游戏确认。",
        notices: [],
        availability: 0,
      },
      hipSetupPending: false,
    };
  }
  if (["gameRecommendation","recommendationApply","recommendationVerify","recommendationFeedback"].includes(command)) throw new Error("智能推荐需要在 Windows 客户端进行真实检测，界面预览不模拟兼容结果。");
  if (command === "neuralStatus") return null;
  if (command === "neuralPlan") {
    const options = args.neural as NeuralOptions;
    const passes = options.passes ?? 1;
    return { plan: { backend: options.backend === "auto" ? "lmxxf-rdna3" : options.backend,
      graphicsApi: options.graphicsApi === "auto" ? "dx12" : options.graphicsApi,
      proxy: options.proxy === "auto" ? "dxgi.dll" : options.proxy,
      passes, modeLabel: passes === 1 ? "标准" : passes === 2 ? "增强" : passes === 3 ? "高质量" : "实验 / 压力测试",
      statusLabel: "未验证", options, notices: ["界面预览：此处为示例匹配，真实安装与检测请使用 Windows 客户端。"] }, missingResources: [], installed: null };
  }
  if (command === "checks")
    return [
      ["显卡", "示例：已识别 AMD Radeon RX 7900 XT。", false],
      ["驱动", "示例：已读到驱动版本，需在 AMD 软件中核对。", true],
      ["安装记录", "示例：已找到这款游戏的安装记录。", false],
      ["安装文件完整性", "示例：MU 安装的文件与记录一致。", false],
      ["帧率方案加载组件", "示例：已找到加载文件。", false],
      ["方案设置", "示例：已找到 OptiScaler 设置。", false],
      ["XeFG 组件", "示例：已找到帧生成组件。", false],
      ["AMD HIP", "示例：已找到 HIP 运行组件。", false],
      ["神经渲染模型组件", "示例：已找到模型组件。", false],
      ["神经渲染运行组件 1", "示例：已找到运行组件。", false],
      ["神经渲染运行组件 2", "示例：已找到运行组件。", false],
      ["神经渲染运行组件 3", "示例：已找到运行组件。", false],
      ["模型数据", "示例：已找到模型数据。", false],
      ["神经渲染模型版本", "示例：组件版本符合安装要求。", false],
      ["游戏加载组件", "示例：是否被游戏加载，需启动后验证。", true],
      ["游戏图形接口", "示例：实际游戏接口需启动后验证。", true],
    ].map(([title, detail, unknown]) => ({ title, detail, unknown, problem: false }));
  if (command === "supportedGames")
    return { asOf: "2026-10-14", games: [
      { title: "赛博朋克 2077", canonicalName: "Cyberpunk 2077", steamAppId: "1091500", aliases: ["Cyberpunk 2077", "Cyberpunk2077"] },
      { title: "极限竞速：地平线 5", canonicalName: "Forza Horizon 5", steamAppId: "1551360", aliases: ["ForzaHorizon5"] }
    ] };
  if (command === "wishes")
    return { items: [], total: 0, page: 1, pageSize: 20, counts: {} };
  if (command === "presets")
    return { items: [previewPreset], total: 1 };
  if (command === "preset")
    return { item: previewPreset, imageData: null, settings: [
      { file: "OptiScaler.ini", section: "DlssNr", key: "Quality", value: "1" },
      { file: "OptiScaler.ini", section: "XeFG", key: "InterpolationCount", value: "1" },
    ] };
  if (command === "presetSnapshot")
    return { mode: 4, componentVersion: "预览示例", vulkan: false, gameName: "我的游戏",
      settings: [{ file: "OptiScaler.ini", section: "XeFG", key: "InterpolationCount", value: "1" }] };
  throw new Error("这是界面预览；请在 Windows 客户端中测试真实功能。");
}
const previewPreset = {
  id: "preview-preset", gameKey: "title:PREVIEW", gameName: "我的游戏",
  mode: 4, goal: "performance", vulkan: false, componentVersion: "预览示例",
  title: "预览示例 · 流畅设置", description: "展示列表与详情的样式，非真实玩家方案。",
  author: "预览数据", createdAt: 0, hasImage: false, isMine: false, settingCount: 2,
};
