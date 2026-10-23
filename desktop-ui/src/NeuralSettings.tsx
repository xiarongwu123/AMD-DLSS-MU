import { useEffect, useState } from "react";
import { ChevronDown, FolderInput, FlaskConical } from "lucide-react";
import { rpc } from "./bridge";
import { Button } from "./components";
import type { NeuralOptions, NeuralPlanResponse } from "./types";
import "./neural-settings.css";

export const defaultNeuralOptions: NeuralOptions = {
  backend: "auto", graphicsApi: "auto", proxy: "auto", passes: null,
  modelScale: 1, temporal: true, historyStrength: 1, applyModel: true, dynamicResolution: 1,
};
export function NeuralSettings({ gameId, options, onChange, installed = false, disabled = false }: {
  gameId: string; options: NeuralOptions; onChange: (options: NeuralOptions) => void;
  installed?: boolean; disabled?: boolean;
}) {
  const [result, setResult] = useState<NeuralPlanResponse | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [importing, setImporting] = useState(false);
  const [refresh, setRefresh] = useState(0);
  useEffect(() => {
    let active = true;
    setLoading(true); setResult(null); setError("");
    const timer = setTimeout(() => {
      rpc<NeuralPlanResponse>("neuralPlan", { gameId, neural: options })
        .then(value => { if (active) setResult(value); })
        .catch(e => { if (active) setError((e as Error).message); })
        .finally(() => { if (active) setLoading(false); });
    }, 350);
    return () => { active = false; clearTimeout(timer); };
  }, [gameId, options, refresh]);
  const set = <K extends keyof NeuralOptions>(key: K, value: NeuralOptions[K]) => onChange({ ...options, [key]: value });
  async function prepare() {
    setImporting(true); setError("");
    try { if (await rpc<boolean>("neuralResources")) setRefresh(n => n + 1); }
    catch (e) { setError((e as Error).message); }
    finally { setImporting(false); }
  }
  const mochi = options.backend === "mochizuki" || options.backend === "auto" && result?.plan.backend === "mochizuki";
  return <section className="neural-settings" aria-label="神经渲染设置">
    <div className="neural-summary">
      <span className="badge">{loading ? "正在匹配兼容规则…" : result?.plan.statusLabel || "待确认"}</span>
      <span>MU 自动匹配游戏与显卡；可在高级设置中调整。</span>
    </div>
    {result?.plan.notices.map((notice, index) => <p className="muted" key={index}>{notice}</p>)}
    {result?.missingResources.map(message => <p className="warning" key={message}>{message}</p>)}
    <details>
      <summary><ChevronDown size={17} />高级设置</summary>
      <fieldset disabled={disabled || importing}>
        <div className="neural-fields">
          <label>NR 后端<select aria-label="NR 后端" disabled={installed} value={options.backend} onChange={e => onChange({ ...options, backend: e.target.value, passes: e.target.value !== "mochizuki" && (options.passes ?? 1) > 3 ? null : options.passes })}>
            <option value="auto">自动选择（推荐）</option><option value="daniel">Daniel</option>
            <option value="lmxxf">lmxxf</option><option value="mochizuki">Mochizuki</option>
          </select></label>
          <label>游戏图形接口<select aria-label="游戏图形接口" disabled={installed} value={options.graphicsApi} onChange={e => set("graphicsApi", e.target.value)}>
            <option value="auto">自动检测</option><option value="dx11">DirectX 11</option><option value="dx12">DirectX 12</option><option value="vulkan">Vulkan</option>
          </select></label>
          <label>注入代理<select aria-label="注入代理" disabled={installed} value={options.proxy} onChange={e => set("proxy", e.target.value)}>
            <option value="auto">自动匹配</option>{["dxgi.dll", "winmm.dll", "d3d12.dll", "winhttp.dll", "wininet.dll", "dbghelp.dll"].map(p => <option key={p}>{p}</option>)}
          </select></label>
          <label>神经 Pass<select aria-label="神经 Pass" value={options.passes ?? "auto"} onChange={e => set("passes", e.target.value === "auto" ? null : Number(e.target.value))}>
            <option value="auto">自动匹配（默认 1 Pass）</option><option value="1">标准 · 1 Pass</option><option value="2">增强 · 2 Pass</option><option value="3">高质量 · 3 Pass</option>
            {mochi && Array.from({ length: 27 }, (_, n) => n + 4).map(n => <option key={n} value={n}>{n <= 8 ? "实验" : "实验 / 压力测试"} · {n} Pass</option>)}
          </select></label>
          {([ ["nrInline", "NR Inline（实验性）"], ["earlyAnyModuleWrap", "提前包装图形模块"], ["allowEnhancedBarriers", "增强屏障互操作"] ] as const).map(([key, label]) =>
            <label key={key}>{label}<select aria-label={label} value={options[key] == null ? "auto" : String(options[key])} onChange={e => set(key, e.target.value === "auto" ? null : e.target.value === "true")}>
              <option value="auto">遵循游戏兼容规则</option><option value="false">关闭</option><option value="true">开启（需重启游戏验证）</option>
            </select></label>)}
          {mochi && <>
            <label>模型分辨率比例<input aria-label="模型分辨率比例" type="number" min="0.25" max="1" step="0.05" value={options.modelScale} onChange={e => set("modelScale", Number(e.target.value))} /></label>
            <label>历史强度<input aria-label="历史强度" type="number" min="0" max="1" step="0.05" value={options.historyStrength} onChange={e => set("historyStrength", Number(e.target.value))} /></label>
            <label>动态分辨率<select aria-label="动态分辨率" value={options.dynamicResolution} onChange={e => set("dynamicResolution", Number(e.target.value))}>
              <option value="0">精确匹配</option><option value="1">自动分桶</option><option value="2">总是分桶</option>
            </select></label>
            <label className="neural-toggle"><input type="checkbox" checked={options.temporal} onChange={e => set("temporal", e.target.checked)} />使用时序历史</label>
            <label className="neural-toggle"><input type="checkbox" checked={options.applyModel} onChange={e => set("applyModel", e.target.checked)} />应用模型结果</label>
          </>}
        </div>
        {result && <p>当前匹配：{result.plan.backend} · {result.plan.graphicsApi} · {result.plan.proxy} · {result.plan.passes} Pass</p>}
        {(options.passes ?? 1) >= 4 && <p className="warning"><FlaskConical size={16} />额外 Pass 会增加耗时与显存占用。9–30 Pass 仅用于实验 / 压力测试，不是最高画质推荐。</p>}
        <p className="muted">Mochizuki 主要面向 RDNA4；RX 6000/7000 不作为稳定推荐。切换后端或代理需先恢复配置。</p>
        <p className="muted">MU 会从内置资源自动准备 310.8.0 DLL；Mochizuki 模型也会在首次配置时自动提取。Daniel 0.6.0 的 Runtime 与权重、lmxxf 的模型资源仍需另行准备。MU 不执行第三方 Setup。</p>
        <Button disabled={importing} onClick={prepare}><FolderInput size={18} />{importing ? "正在导入…" : "准备本机资源"}</Button>
      </fieldset>
    </details>
    {result?.missingResources.length ? <Button disabled={importing || disabled} onClick={prepare}>{importing ? "正在导入…" : "导入所需资源"}</Button> : null}
    {error && <p role="alert" className="error">{error}</p>}
  </section>;
}
