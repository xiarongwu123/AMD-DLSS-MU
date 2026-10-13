import type { State } from "./types";
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
      command === "configure" || command === "applyPreset" || command === "update" || command === "magpie"
        ? 3600000
        : 120000,
    );
    pending.set(id, { resolve: (value) => resolve(value as T), reject, timer });
    const host = window.chrome!.webview!;
    if (files) {
      if (!host.postMessageWithAdditionalObjects) {
        clearTimeout(timer);
        pending.delete(id);
        reject(new Error("当前 WebView2 不支持拖入文件，请用添加游戏按钮。"));
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
    features: [],
    paymentsEnabled: false,
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
  if (command === "checks")
    return [
      {
        title: "显卡",
        detail: "预览示例，不代表本机检测结果。",
        problem: false,
        unknown: true,
      },
    ];
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
