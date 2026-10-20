import { useCallback, useEffect, useRef, useState } from "react";
import { ArrowLeft, ChevronRight, ImagePlus, RefreshCw, UploadCloud, Paperclip, Link, Copy, Download, X } from "lucide-react";
import { isDesktop, rpc } from "./bridge";
import "./community-sharing.css";
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
  mode === -1 ? "资源分享" : mode === 0 || mode === 6 ? "DLSS 5 神经渲染" : mode === 4 ? "DLSS 5 + XeFG 2X" : "标准 OptiScaler";
const label = (goal: Goal) =>
  goal === "quality" ? "画质优先" : goal === "performance" ? "帧率优先" : "自定义";

export function CommunityPanel({ game, games, onApplied, initialUpload = false }: { game?: Game; games?: Game[]; onApplied: () => void; initialUpload?: boolean }) {
  const availableGames = games?.length ? games : (game ? [game] : []);
  const [targetId, setTargetId] = useState(game?.id ?? "");
  const target = availableGames.find(g => g.id === targetId);
  const [scope, setScope] = useState<"universal" | "game">("universal");
  const [includeConfiguration, setIncludeConfiguration] = useState(false);
  const [attachment, setAttachment] = useState<File | null>(null);
  const [resourceUrl, setResourceUrl] = useState(""), [resourceCode, setResourceCode] = useState("");
  const [page, setPage] = useState(1), [total, setTotal] = useState(0);
  const [message, setMessage] = useState("");
  const requestVersion = useRef(0);
  const [list, setList] = useState<CommunityPreset[]>([]);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState<CommunityPresetDetail | null>(null);
  const appliesToTarget = !!target && !!selected && (selected.item.scope === "universal" || selected.item.gameKey === `steam:${target.steamAppId}` || selected.item.gameName === target.title);
  const [view, setView] = useState<"list" | "upload">("list");
  const [snapshot, setSnapshot] = useState<CommunityPresetSnapshot | null>(null);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [goal, setGoal] = useState<Goal>("quality");
  const [image, setImage] = useState<string | null>(null);
  const [thumbnail, setThumbnail] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const submissionId = useRef(crypto.randomUUID());

  const refresh = useCallback(async (wantedPage = 1) => {
    const request = ++requestVersion.current;
    setLoading(true);
    setError("");
    try {
      const data = await rpc<CommunityPresetList>("presets", { page: wantedPage });
      if (request !== requestVersion.current) return;
      setList(data.items); setTotal(data.total); setPage(data.page ?? wantedPage);
    } catch (e) {
      if (request === requestVersion.current) setError((e as Error).message);
    } finally {
      if (request === requestVersion.current) setLoading(false);
    }
  }, []);
  const preparedGame = useRef<string | null>(null);
  useEffect(() => {
    setSelected(null); setList([]);
    if (initialUpload) {
      setLoading(false);
      if (preparedGame.current !== (game?.id ?? "global")) { preparedGame.current = game?.id ?? "global"; void prepareUpload(true); }
    } else void refresh();
    return () => { requestVersion.current++; };
  }, [refresh, initialUpload, game?.id]);

  async function detail(item: CommunityPreset) {
    setError("");
    setBusy(true);
    try { setSelected(await rpc<CommunityPresetDetail>("preset", { id: item.id })); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function prepareUpload(readConfiguration = false) {
    submissionId.current = crypto.randomUUID(); setError(""); setMessage("");
    setSnapshot(null); setIncludeConfiguration(false); setView("upload");
    if (readConfiguration && target) await capture();
  }
  async function capture() {
    if (!target) { setError("请先选择要读取配置的本地游戏。"); return; }
    setError(""); setBusy(true);
    try {
      const current = await rpc<CommunityPresetSnapshot>("presetSnapshot", { gameId: target.id });
      setSnapshot(current); setIncludeConfiguration(true);
      setGoal(current.mode === 0 || current.mode === 6 ? "quality" : current.mode === 4 ? "performance" : "custom");
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function resource(action: "download" | "open" | "copy") {
    if (!selected) return; setBusy(true); setError(""); setMessage("");
    try {
      if (action === "download") {
        const saved = await rpc<boolean>("presetAttachment", { id: selected.item.id });
        if (saved) setMessage("附件已保存，请按分享者的说明使用。");
      } else if (isDesktop) {
        await rpc("presetResource", { id: selected.item.id, copy: action === "copy" });
        if (action === "copy") setMessage("网盘链接和提取码已复制。");
      } else if (action === "copy") {
        await navigator.clipboard.writeText(selected.item.resourceUrl! + (selected.item.resourceCode ? `\n提取码：${selected.item.resourceCode}` : ""));
        setMessage("网盘链接和提取码已复制。");
      }
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  function chooseAttachment(file?: File) {
    if (!file) return;
    if (!/\.(zip|7z|rar|ini|json|txt|cfg|fx|fxh|preset)$/i.test(file.name) || file.size < 1 || file.size > 10 * 1024 * 1024) {
      setError("请选择10 MB以内的压缩包、配置或着色器文件；多个文件请先打包。"); return;
    }
    setAttachment(file); setError("");
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
      const small = document.createElement("canvas");
      const thumbScale = Math.min(1, 240 / Math.max(bitmap.width, bitmap.height));
      small.width = Math.max(1, Math.round(bitmap.width * thumbScale));
      small.height = Math.max(1, Math.round(bitmap.height * thumbScale));
      small.getContext("2d")!.drawImage(bitmap, 0, 0, small.width, small.height);
      bitmap.close();
      const data = canvas.toDataURL("image/jpeg", 0.65);
      if (data.length > 280000) throw new Error("截图压缩后仍超过 200 KB，请换一张截图。");
      const thumbnailData = small.toDataURL("image/jpeg", 0.5);
      if (thumbnailData.length > 42000) throw new Error("截图缩略图仍过大，请换一张截图。");
      setImage(data);
      setThumbnail(thumbnailData);
    } catch (e) { setError((e as Error).message); }
  }
  async function upload() {
    setError(""); setBusy(true);
    try {
      if (resourceUrl.trim()) {
        const url = new URL(resourceUrl.trim());
        if (url.protocol !== "https:") throw new Error("请填写完整的HTTPS网盘链接。");
      }
      await rpc("submitPreset", { gameId: target?.id, submissionId: submissionId.current,
        includeConfiguration, scope, resourceUrl: resourceUrl.trim(), resourceCode: resourceCode.trim(),
        goal, title: title.trim(), description: description.trim(), imageData: image, thumbnailData: thumbnail }, attachment ? [attachment] : undefined);
      submissionId.current = crypto.randomUUID();
      setView("list"); setTitle(""); setDescription(""); setImage(null); setThumbnail(null); setAttachment(null); setResourceUrl(""); setResourceCode(""); setSnapshot(null); setIncludeConfiguration(false);
      await refresh();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function apply() {
    if (!selected || !target) return;
    setError(""); setBusy(true);
    try {
      await rpc("applyPreset", { gameId: target.id, id: selected.item.id });
      onApplied();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  return <section className="community-panel" aria-label="朋友们的配置">
    {view === "list" && <div className="community-game-line">
      <span className="community-game-mark">MU</span>
      <div><strong>玩家方案</strong><small>所有玩家的配置、Mod 与配套资源</small></div>
      <em>{loading ? "读取中" : `${total} 条分享`}</em>
    </div>}
    {view === "list" ? selected ? <>
      <button className="community-back" onClick={() => setSelected(null)}><ArrowLeft size={16} /> 返回方案列表</button>
      <div className="community-detail">
        <div className="community-detail-head"><div><h3>{selected.item.title}</h3>
          <p>{selected.item.description}</p><small>{selected.item.author} · {route(selected.item.mode)}{selected.item.mode !== -1 ? ` · ${selected.item.componentVersion}` : ""} · {selected.item.scope === "universal" ? "通用分享" : `专用于 ${selected.item.gameName}`}</small></div></div>
        <div className="community-detail-layout">
          <div className="community-detail-main"><div className="community-meta">
            <div><small>目标</small><strong>{label(selected.item.goal)}</strong></div>
            <div><small>路线</small><strong>{route(selected.item.mode)}</strong></div>
            <div><small>接口</small><strong>{selected.item.vulkan ? "Vulkan" : "默认"}</strong></div>
          </div><h4>{selected.settings.length ? "将应用的参数" : "资源使用说明见上方分享介绍"}</h4>
          <div className="community-settings">{selected.settings.map((s) =>
            <div key={`${s.file}/${s.section}/${s.key}`}><span>{s.file} · [{s.section}] {s.key}</span><strong>{s.value}</strong></div>)}</div></div>
          <div className="community-detail-side">
            {selected.imageData ? <img src={selected.imageData} alt={`${selected.item.title} 的游戏截图`} />
              : <div className="community-no-image"><ImagePlus size={23} /><span>分享者未上传截图</span></div>}
            <p>截图和分享说明仅供参考，效果请以本机游戏实测为准。</p>
          </div>
        </div>
        {(selected.item.attachment || selected.item.resourceUrl) && <div className="community-resources">
          <h4>附件与配套资源</h4>
          {selected.item.attachment && <div className="community-resource-row"><Paperclip size={19}/><div><strong>{selected.item.attachment.name}</strong><small>{(selected.item.attachment.size / 1024 / 1024).toFixed(2)} MB · SHA256 下载校验</small></div>
            <Button disabled={busy} onClick={() => void resource("download")}><Download size={16}/>下载附件</Button></div>}
          {selected.item.resourceUrl && <div className="community-resource-row"><Link size={19}/><div><strong>网盘资源</strong><small>{selected.item.resourceUrl}</small>{selected.item.resourceCode && <small>提取码：{selected.item.resourceCode}</small>}</div>
            {isDesktop ? <Button disabled={busy} onClick={() => void resource("open")}>打开网盘</Button> : <a className="button" href={selected.item.resourceUrl} target="_blank" rel="noopener noreferrer">打开网盘</a>}
            <Button disabled={busy} onClick={() => void resource("copy")}><Copy size={15}/>复制链接</Button></div>}
          <p className="community-hint">附件单独下载，不会自动安装或运行；请按分享者的说明使用。</p>
        </div>}
        {selected.item.mode !== -1 && <div className="community-detail-action"><div><label className="community-target">应用到游戏<select aria-label="应用到游戏" value={targetId} disabled={busy} onChange={e => setTargetId(e.target.value)}><option value="">选择本地游戏</option>{availableGames.map(g => <option key={g.id} value={g.id}>{g.title}</option>)}</select></label><p className="community-hint">通用方案也需检查本机兼容条件；MU 会备份现有配置。</p></div>
          {!appliesToTarget && target && <p className="community-hint">此方案专用于「{selected.item.gameName}」，请选对应游戏。</p>}
          <Button primary disabled={busy || !appliesToTarget} onClick={apply}>{busy ? "正在配置…" : "一键使用这套方案"}</Button></div>}
        {message && <p role="status" className="community-hint">{message}</p>}
      </div>
      {error && <p role="alert" className="error">{error}</p>}
    </> : <>
      <div className="community-toolbar">
        <div><h3>看看朋友们怎么配</h3><p>浏览全部分享，找到好配置和配套资源。</p></div>
        <Button disabled={busy || loading} onClick={() => void refresh(page)} aria-label="刷新方案"><RefreshCw size={16} /></Button>
        <Button data-guide="share" primary disabled={busy} onClick={() => void prepareUpload()}><UploadCloud size={16} /> 发布分享</Button>
      </div>
      {(loading || (initialUpload && busy && !snapshot)) ? <Loading text={initialUpload && busy ? "正在读取当前配置…" : "正在读取方案…"} /> : list.length === 0
        ? <div className="community-empty">还没有玩家分享内容。发布你的配置、附件或网盘资源吧。</div>
        : <div className="community-list">{list.map((item) =>
          <button key={item.id} className="community-card" disabled={busy} onClick={() => detail(item)}>
            <span className="community-thumb">{item.thumbnailData
              ? <img src={item.thumbnailData} alt="" loading="lazy" />
              : <><ImagePlus size={20} /><small>{item.hasImage ? "查看截图" : "无截图"}</small></>}</span>
            <span className="community-card-copy"><strong>{item.title}</strong><span>{item.description}</span>
              <small>{item.scope === "universal" ? "通用分享" : item.gameName} · {route(item.mode)}{item.vulkan ? " · Vulkan" : ""}{item.settingCount ? ` · ${item.settingCount} 项参数` : ""}{item.attachment ? " · 含附件" : ""}{item.resourceUrl ? " · 含网盘链接" : ""} · {item.author}</small></span>
            <em>{label(item.goal)}</em><ChevronRight size={18} /></button>)}</div>}
      {total > 30 && <div className="community-pagination"><Button disabled={loading || page <= 1} onClick={() => void refresh(page - 1)}>上一页</Button><span>第 {page} / {Math.ceil(total / 30)} 页</span><Button disabled={loading || page * 30 >= total} onClick={() => void refresh(page + 1)}>下一页</Button></div>}
      {error && <p role="alert" className="error">{error}</p>}
    </> : <div className="community-upload-screen"><div className="community-upload" role="dialog" aria-modal="true" aria-label="上传方案">
      <div className="community-toolbar"><div><h3>发布分享</h3><p>分享配置、Mod 附件或网盘链接，可以单独发布资源。</p></div>
        <Button disabled={busy} onClick={() => { setView("list"); void refresh(); }}>返回列表</Button></div>
      <label>分享范围<select aria-label="分享范围" value={scope} onChange={e => setScope(e.target.value as "game" | "universal")}><option value="universal">通用分享 · 可用于不同游戏</option>{availableGames.length > 0 && <option value="game">当前游戏专用</option>}</select></label>
      {availableGames.length > 0 && <label>配置来源 / 适用游戏<select aria-label="配置来源游戏" value={targetId} disabled={busy} onChange={e => {setTargetId(e.target.value);setSnapshot(null);setIncludeConfiguration(false);}}><option value="">选择本地游戏</option>{availableGames.map(g => <option key={g.id} value={g.id}>{g.title}</option>)}</select></label>}
      <div className="community-capture"><Button disabled={busy || !target} onClick={() => void capture()}>读取我的配置</Button>{snapshot && <label><input type="checkbox" checked={includeConfiguration} onChange={e => setIncludeConfiguration(e.target.checked)}/>附带读取的配置参数</label>}</div>
      {snapshot && includeConfiguration && <div className="community-summary"><strong>{route(snapshot.mode)}</strong>
        <span>{snapshot.componentVersion}{snapshot.vulkan ? " · Vulkan" : ""} · 已读取 {snapshot.settings.length} 项参数</span></div>}
      {includeConfiguration && <label>方案类型<select value={goal} onChange={(e) => setGoal(e.target.value as Goal)}>
        {(snapshot?.mode === 0 || snapshot?.mode === 6) && <option value="quality">画质优先</option>}
        {snapshot?.mode === 4 && <option value="performance">帧率优先</option>}
        <option value="custom">自定义</option>
      </select></label>}
      <label>标题<input value={title} maxLength={50} onChange={(e) => setTitle(e.target.value)} placeholder="例如：2K 画质清晰设置" /></label>
      <label>说明<textarea value={description} maxLength={300} rows={3} onChange={(e) => setDescription(e.target.value)} placeholder="说说画面、流畅度和适合的场景" /></label>
      <label>网盘链接（可选）<input type="url" value={resourceUrl} maxLength={2048} placeholder="https://pan.quark.cn/s/…" onChange={e => setResourceUrl(e.target.value)}/></label>
      <label>提取码（可选）<input value={resourceCode} maxLength={64} placeholder="单独填写提取码" onChange={e => setResourceCode(e.target.value)}/></label>
      <label className="community-image"><Paperclip size={18}/>添加附件（可选，10 MB以内）<input type="file" aria-label="分享附件" accept=".zip,.7z,.rar,.ini,.json,.txt,.cfg,.fx,.fxh,.preset" onChange={e => { chooseAttachment(e.target.files?.[0]); e.target.value = ""; }}/></label>
      {attachment && <div className="community-attachment-selected"><span>{attachment.name} · {(attachment.size / 1024 / 1024).toFixed(2)} MB</span><Button aria-label="移除附件" disabled={busy} onClick={() => setAttachment(null)}><X size={15}/></Button></div>}
      <label className="community-image"><ImagePlus size={18} /> {image ? "更换截图" : "添加游戏截图（可选）"}
        <input type="file" accept="image/jpeg,image/png" onChange={(e) => void attach(e.target.files?.[0])} /></label>
      {image && <img className="community-upload-preview" src={image} alt="待上传截图预览" />}
      {error && <p role="alert" className="error">{error}</p>}
      <Button primary disabled={busy || title.trim().length < 3 || description.trim().length < 1 || (!includeConfiguration && !attachment && !resourceUrl.trim()) || (scope === "game" && !target)}
        onClick={upload}>{busy ? "正在上传…" : "发布分享"}</Button>
    </div></div>}
  </section>;
}
