import { useCallback, useEffect, useRef, useState } from "react";
import { ImagePlus, RefreshCw, UploadCloud } from "lucide-react";
import { rpc } from "./bridge";
import { Button, Loading } from "./components";
import type {
  CommunityPreset,
  CommunityPresetDetail,
  CommunityPresetList,
  CommunityPresetSnapshot,
  Game,
  Goal,
} from "./types";

const route = (mode: number) =>
  mode === 0 ? "DLSS 5 神经渲染" : mode === 4 ? "DLSS 5 + XeFG 2X" : "标准 OptiScaler";
const label = (goal: Goal) =>
  goal === "quality" ? "画质优先" : goal === "performance" ? "帧率优先" : "自定义";

export function CommunityPanel({ game, onApplied }: { game: Game; onApplied: () => void }) {
  const [list, setList] = useState<CommunityPreset[]>([]);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState<CommunityPresetDetail | null>(null);
  const [view, setView] = useState<"list" | "upload">("list");
  const [snapshot, setSnapshot] = useState<CommunityPresetSnapshot | null>(null);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [goal, setGoal] = useState<Goal>("quality");
  const [image, setImage] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const submissionId = useRef(crypto.randomUUID());

  const refresh = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const data = await rpc<CommunityPresetList>("presets", { gameId: game.id });
      setList(data.items);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [game.id]);
  useEffect(() => { setSelected(null); setList([]); void refresh(); }, [refresh]);

  async function detail(item: CommunityPreset) {
    setError("");
    setBusy(true);
    try { setSelected(await rpc<CommunityPresetDetail>("preset", { id: item.id })); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function prepareUpload() {
    setError("");
    setBusy(true);
    try {
      const current = await rpc<CommunityPresetSnapshot>("presetSnapshot", { gameId: game.id });
      setSnapshot(current);
      setGoal(current.mode === 0 ? "quality" : current.mode === 4 ? "performance" : "custom");
      setView("upload");
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function attach(file?: File) {
    if (!file) return;
    if (!["image/jpeg", "image/png"].includes(file.type) || file.size > 10 * 1024 * 1024) {
      setError("请选择 10 MB 以内的 JPG 或 PNG 截图。"); return;
    }
    try {
      const bitmap = await createImageBitmap(file);
      if (bitmap.width * bitmap.height > 40000000) { bitmap.close(); throw new Error("截图尺寸过大。"); }
      const canvas = document.createElement("canvas");
      const scale = Math.min(1, 800 / Math.max(bitmap.width, bitmap.height));
      canvas.width = Math.round(bitmap.width * scale);
      canvas.height = Math.round(bitmap.height * scale);
      canvas.getContext("2d")!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
      bitmap.close();
      const data = canvas.toDataURL("image/jpeg", 0.65);
      if (data.length > 280000) throw new Error("截图压缩后仍超过 200 KB，请换一张截图。");
      setImage(data);
    } catch (e) { setError((e as Error).message); }
  }
  async function upload() {
    setError(""); setBusy(true);
    try {
      await rpc("submitPreset", { gameId: game.id, submissionId: submissionId.current,
        goal, title: title.trim(), description: description.trim(), imageData: image });
      submissionId.current = crypto.randomUUID();
      setView("list"); setTitle(""); setDescription(""); setImage(null);
      await refresh();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function apply() {
    if (!selected) return;
    setError(""); setBusy(true);
    try {
      await rpc("applyPreset", { gameId: game.id, id: selected.item.id });
      onApplied();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  return <section className="community-panel" aria-label="朋友们的配置">
    {view === "list" ? <>
      <div className="community-toolbar">
        <div><h3>看看朋友们怎么配</h3><p>只显示这款游戏的分享方案，参数可先查看。</p></div>
        <Button disabled={busy || loading} onClick={refresh} aria-label="刷新方案"><RefreshCw size={16} /></Button>
        <Button disabled={busy} onClick={prepareUpload}><UploadCloud size={16} /> 分享我的配置</Button>
      </div>
      {loading ? <Loading text="正在读取方案…" /> : list.length === 0
        ? <div className="community-empty">这款游戏还没有人分享配置。你可以先分享自己的方案。</div>
        : <div className="community-list">{list.map((item) =>
          <button key={item.id} className={`community-card ${selected?.item.id === item.id ? "selected" : ""}`}
            disabled={busy} onClick={() => detail(item)}>
            <span className="community-card-top"><strong>{item.title}</strong><em>{label(item.goal)}</em></span>
            <span>{item.description}</span>
            <small>{route(item.mode)}{item.vulkan ? " · Vulkan" : ""} · {item.settingCount} 项参数 · {item.author}</small>
          </button>)}</div>}
      {selected && <div className="community-detail">
        <div className="community-detail-head"><div><h3>{selected.item.title}</h3>
          <p>{selected.item.description}</p><small>{route(selected.item.mode)} · {selected.item.componentVersion}</small></div>
          <Button primary disabled={busy} onClick={apply}>{busy ? "正在配置…" : "一键配置这套方案"}</Button></div>
        {selected.imageData && <img src={selected.imageData} alt={`${selected.item.title} 的游戏截图`} />}
        <details><summary>查看将应用的 {selected.settings.length} 项参数</summary>
          <div className="community-settings">{selected.settings.map((s) =>
            <div key={`${s.file}/${s.section}/${s.key}`}><span>{s.file} · [{s.section}] {s.key}</span><strong>{s.value}</strong></div>)}</div>
        </details>
        <p className="community-hint">MU 会先检查路线和版本，并备份已有配置；效果请在游戏中确认。</p>
      </div>}
      {error && <p role="alert" className="error">{error}</p>}
    </> : <div className="community-upload-screen"><div className="community-upload" role="dialog" aria-modal="true" aria-label="上传方案">
      <div className="community-toolbar"><div><h3>分享我的配置</h3><p>读取当前游戏的实际设置；只上传可复用参数。</p></div>
        <Button disabled={busy} onClick={() => setView("list")}>返回列表</Button></div>
      {snapshot && <div className="community-summary"><strong>{route(snapshot.mode)}</strong>
        <span>{snapshot.componentVersion}{snapshot.vulkan ? " · Vulkan" : ""} · 已读取 {snapshot.settings.length} 项参数</span></div>}
      <label>方案类型<select value={goal} onChange={(e) => setGoal(e.target.value as Goal)}>
        {snapshot?.mode === 0 && <option value="quality">画质优先</option>}
        {snapshot?.mode === 4 && <option value="performance">帧率优先</option>}
        <option value="custom">自定义</option>
      </select></label>
      <label>标题<input value={title} maxLength={50} onChange={(e) => setTitle(e.target.value)} placeholder="例如：2K 画质清晰设置" /></label>
      <label>说明<textarea value={description} maxLength={300} rows={3} onChange={(e) => setDescription(e.target.value)} placeholder="说说画面、流畅度和适合的场景" /></label>
      <label className="community-image"><ImagePlus size={18} /> {image ? "更换截图" : "添加游戏截图（可选）"}
        <input type="file" accept="image/jpeg,image/png" onChange={(e) => void attach(e.target.files?.[0])} /></label>
      {image && <img className="community-upload-preview" src={image} alt="待上传截图预览" />}
      {error && <p role="alert" className="error">{error}</p>}
      <Button primary disabled={busy || title.trim().length < 3 || description.trim().length < 1}
        onClick={upload}>{busy ? "正在上传…" : "一键上传方案"}</Button>
    </div></div>}
  </section>;
}
