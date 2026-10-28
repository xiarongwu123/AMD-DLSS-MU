import { useState, useEffect, useCallback, useRef } from "react";
import {
  CheckCircle2,
  Cpu,
  UsersRound,
  ChevronDown,
  ChevronUp,
  SlidersHorizontal,
} from "lucide-react";
import { rpc, subscribe } from "./bridge";
import { Button, Modal, Loading, Progress, type CardOrigin } from "./components";
import type { Game, Goal, Recommendation, Check, State, NeuralReceipt, NeuralPlanResponse, NeuralOptions } from "./types";
import { CommunityPanel } from "./CommunityPanel";
import { NeuralSettings, defaultNeuralOptions } from "./NeuralSettings";
import { SchemeChoices, canUsePremium, schemes, type SchemeId } from "./SchemeChoices";
import type { SmartContext } from "./SmartRecommendationPanel";
import { PurchaseDialog } from "./AccountPage";
export const modeName = (mode: number) =>
  mode === 8 ? "老游戏神经渲染（ReShade 64位）" : mode === 6 ? "DLSS 5 神经渲染（多后端）" : mode === 5 ? "32位神经渲染桥接（实验性）" : mode === 0
    ? "DLSS 5 神经渲染（模式一）"
    : mode === 4
      ? "DLSS 5 + XeFG 2X"
      : "标准 OptiScaler";
export function selectedMode(goal: Goal, custom: number) {
  return goal === "quality" ? 0 : goal === "performance" ? 4 : custom;
}
export function SmartDialog({
  game,
  state,
  onClose,
  onComplete,
  initialTab = "recommend",
  initialUpload = false,
  cardOrigin,
  onStartAmdMagpie,
}: {
  game: Game;
  state: State;
  onClose: () => void;
  onComplete: () => void;
  initialTab?: "recommend" | "community";
  initialUpload?: boolean;
  cardOrigin?: CardOrigin | null;
  onStartAmdMagpie?: () => void;
}) {
  const [tab, setTab] = useState(initialTab),
    [goal, setGoal] = useState<Goal>("quality"),
    [custom, setCustom] = useState(0),
    [result, setResult] = useState<Recommendation | null>(null),
    [checking, setChecking] = useState(true),
    [stage, setStage] = useState("正在读取显卡与专用显存"),
    [error, setError] = useState(""),
    [working, setWorking] = useState(false),
    [vulkan, setVulkan] = useState(false),
    [fgInput, setFgInput] = useState("nofg");
  const [neural, setNeural] = useState(defaultNeuralOptions);
  const [resourceCheck, setResourceCheck] = useState<{ options: NeuralOptions; result: NeuralPlanResponse | null } | null>(null);
  const onResourceCheck = useCallback((result: NeuralPlanResponse | null) => setResourceCheck({ options: neural, result }), [neural]);
  const [completion, setCompletion] = useState<NeuralReceipt | null>(null);
  const [editAdvanced, setEditAdvanced] = useState(false);
  const [scheme, setScheme] = useState<SchemeId | null>("base");
  const [purchase, setPurchase] = useState(false);
  const [gamePreset, setGamePreset] = useState(false);
  const [info, setInfo] = useState("");
  const [smartContext,setSmartContext]=useState<SmartContext>({confirmedApi:"",confirmedInput:"",windowed:""});
  const confirmContext = useCallback((value: SmartContext) => {
    setSmartContext(value);
    if (["dx11", "dx12", "vulkan"].includes(value.confirmedApi))
      setNeural(previous => previous.graphicsApi === value.confirmedApi ? previous : { ...previous, graphicsApi: value.confirmedApi });
    if (["dx8", "dx9", "dx11", "opengl"].includes(value.confirmedApi)) setX86Api(value.confirmedApi);
  }, []);
  const pendingScheme = useRef<SchemeId | null>(null);
  const detectionStarted = useRef(false);
  useEffect(() => {
    let active = true;
    if (game.mode === 6 && game.state === "Configured" && initialTab === "recommend") {
      void rpc<NeuralReceipt | null>("neuralStatus", { gameId: game.id }).then(receipt => {
        if (active && receipt) { setCompletion(receipt); setNeural({ ...receipt.options, backend: receipt.backend, proxy: receipt.proxy, graphicsApi: receipt.graphicsApi }); }
      }).catch(e => { if (active) setError((e as Error).message); });
    }
    return () => { active = false; };
  }, [game.id, game.mode, game.state, initialTab]);
  const [x86Api,setX86Api]=useState("auto");
  const is32Bit=result?.is32Bit===true;
  const mode = scheme === "rx6000" ? 0 : scheme === "legacy" ? is32Bit ? 5 : 8 : scheme && !["base", "fps"].includes(scheme) ? 6 : selectedMode(goal, custom);
  const externalScheme = scheme === "base" || scheme === "amd-magpie";
  const launching = useRef(false);
  const missingNeuralResources = mode === 6 && scheme !== "amd-magpie" && resourceCheck?.options === neural && !!resourceCheck.result?.missingResources.length;
  const chooseScheme = useCallback((id: SchemeId) => {
    const item = schemes.find(s => s.id === id)!;
    if (item.premium && !canUsePremium(state)) { pendingScheme.current = id; setPurchase(true); return; }
    pendingScheme.current = null;
    setScheme(id); setGamePreset(false); setError("");
    if (id === "base" || id === "fps" || id === "rx6000") setGoal(id === "fps" ? "performance" : "quality");
    else {
      setGoal("custom"); setCustom(id === "legacy" ? is32Bit ? 5 : 8 : 6);
      setNeural({ ...defaultNeuralOptions, backend: id === "author" ? "mochizuki" : id === "compat" ? "auto" : "daniel",
        graphicsApi: ["dx11", "dx12", "vulkan"].includes(smartContext.confirmedApi) ? smartContext.confirmedApi : "auto" });
    }
  }, [state, is32Bit, smartContext.confirmedApi]);
  useEffect(() => {
    if (pendingScheme.current && canUsePremium(state)) { const next = pendingScheme.current; pendingScheme.current = null; setPurchase(false); chooseScheme(next); }
  }, [state, chooseScheme]);
  const visualMode = tab === "community" ? "community" : goal;
  const detect = useCallback(async () => {
    setChecking(true);
    setError("");
    setStage("正在读取显卡与专用显存");
    try {
      setResult(await rpc<Recommendation>("recommend", { gameId: game.id }));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setChecking(false);
    }
  }, [game.id]);
  useEffect(() => setTab(initialTab), [initialTab]);
  useEffect(() => {
    if (tab === "recommend" && !detectionStarted.current) {
      detectionStarted.current = true;
      void detect();
    }
  }, [tab, detect]);
  useEffect(() => {
    return subscribe((event, data) => {
      if (event === "detection") {
        const phase = data as { gameId: string; stage: string };
        if (phase.gameId === game.id) setStage(phase.stage);
      }
    });
  }, [game.id]);
  const configure = async () => {
    setWorking(true);
    setError("");
    setInfo("");
    try {
      if ((scheme === "rx6000" || [5, 6, 8].includes(mode) && scheme !== "base") && !canUsePremium(state)) { setPurchase(true); return; }
      if (scheme === "base") {
        if (!await rpc<boolean>("daniel08",{gameId:game.id})) return;
        setInfo("已打开 Daniel 0.8 官方安装器。请在 NeuralHub 中导入自己的 DLL 并配置游戏；此操作不会将游戏直接标记为已配置。");
        return;
      }
      if (scheme === "amd-magpie") {
        if (onStartAmdMagpie) { onStartAmdMagpie(); return; }
        if(await rpc<boolean>("amdMagpie"))setInfo("已打开 AMD 大力喜鹊，请在工具内选择效果并按其主页快捷键启用；游戏文件未修改，实际效果需在游戏中确认。");
        return;
      }
      const receipt = await rpc<NeuralReceipt | null>("configure", {
        gameId: game.id,
        ...smartContext,
        confirmedApi:[5,8].includes(mode)&&x86Api!=="auto"?x86Api:mode===6&&neural.graphicsApi!=="auto"?neural.graphicsApi:mode===2&&vulkan?"vulkan":smartContext.confirmedApi,
        mode,
        ...(scheme ? { scheme: gamePreset ? "game" : scheme } : {}),
        ...([5,8].includes(mode) ? {x86Api} : {}),
        ...(mode === 6 ? { neural } : {}),
        vulkan: mode === 2 && vulkan,
        fgInput: mode === 2 ? fgInput : "nofg",
      });
      if (mode === 6) {
        if (!receipt) throw new Error("未收到安装完成记录，请查看一键排查；未确认配置成功。");
        setCompletion(receipt);
        setNeural({ ...receipt.options, backend: receipt.backend, proxy: receipt.proxy, graphicsApi: receipt.graphicsApi });
      } else onComplete();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
    }
  };
  const updateAdvanced = async () => {
    setWorking(true); setError("");
    try { setCompletion(await rpc<NeuralReceipt>("neuralUpdate", { gameId: game.id, neural })); setEditAdvanced(false); }
    catch (e) { setError((e as Error).message); }
    finally { setWorking(false); }
  };
  const restoreNeural = async (switching = false) => {
    setWorking(true); setError("");
    try {
      await rpc("restore", { gameId: game.id });
      if (await rpc<NeuralReceipt | null>("neuralStatus", { gameId: game.id })) throw new Error("恢复尚未完成，当前方案仍保留。");
      setCompletion(null); setEditAdvanced(false); setGoal("custom"); setNeural(defaultNeuralOptions); if (!switching) onClose();
    } catch (e) { setError((e as Error).message); }
    finally { setWorking(false); }
  };
  const close = useCallback(() => onClose(), [onClose]);
  const activeDownload = working ? state.downloads.find(d => !d.independent && ["连接中", "下载中", "已暂停"].includes(d.state)) : undefined;
  const [downloadActionBusy, setDownloadActionBusy] = useState(false);
  async function controlDownload(action: "pause" | "cancel") {
    if (!activeDownload || downloadActionBusy) return;
    setDownloadActionBusy(true);
    try { await rpc("downloadAction", { id: activeDownload.id, action }); }
    catch (e) { setError((e as Error).message); }
    finally { setDownloadActionBusy(false); }
  }
  return (
    <><Modal
      wide
      title="游戏图形方案"
      cardOrigin={cardOrigin}
      className={`scheme-modal immersive-scheme scheme-catalog-modal scheme-${visualMode}`}
      backdrop={<div className="immersive-backdrop" aria-hidden="true">
        {game.cover && <>
          <img className="world-cover" src={game.cover} alt="" />
          <img className="world-focus" src={game.cover} alt="" />
        </>}
        <div className="world-shade" />
      </div>}
      onClose={close}
      footer={
        <>
          <p className="footer-note">
            {working
              ? "可收起窗口，任务在后台继续；下载时可暂停或取消。"
              : "MU 配置所需文件；实际效果请进游戏确认。"}
          </p>
          <Button onClick={close}>
            {working ? "收起并后台继续" : "返回游戏库"}
          </Button>
          {tab === "recommend" && !completion && <Button
            primary
            charge
            data-guide="configure"
            disabled={
              (!externalScheme && checking) ||
              working || state.busy ||
              missingNeuralResources ||
              (game.state === "Configured" && scheme !== "base" && scheme !== "amd-magpie") ||
              (!externalScheme && !result && goal !== "custom")
            }
            onClick={configure}
          >
            {working
              ? "正在配置…"
              : scheme === "base" ? "打开 Daniel 0.8 官方版"
              : scheme === "amd-magpie" ? "开启 AMD 大力喜鹊"
              : missingNeuralResources ? "需先补齐资源"
              : game.state === "Configured"
                ? "请先恢复再切换"
                : "一键配置"}
          </Button>}
        </>
      }
    >
      <div className="scheme-tabs" role="tablist" aria-label="配置方式">
        <button type="button" role="tab" aria-selected={tab === "recommend"}
          className={tab === "recommend" ? "active" : ""} onClick={() => setTab("recommend")}>智能推荐</button>
        <button type="button" role="tab" aria-selected={tab === "community"}
          className={`community-entry ${tab === "community" ? "active" : ""}`} data-guide="community-tab"
          onClick={() => setTab("community")}><UsersRound size={21} />玩家方案 <span>NEW</span></button>
      </div>
      {completion && tab === "recommend" ? <section className="neural-success">
        <CheckCircle2 size={42} className="accent" />
        <h2>DLSS 5 神经渲染配置已部署</h2>
        <span className="badge">{completion.statusLabel}</span>
        <dl className="neural-receipt">
          <div><dt>当前后端</dt><dd>{completion.backend === "daniel" ? "Daniel" : completion.backend === "mochizuki" ? "Mochizuki" : completion.backend === "lmxxf-rdna3" ? "lmxxf · AMDNR RDNA3" : "lmxxf"}</dd></div>
          <div><dt>当前模式</dt><dd>{completion.modeLabel}</dd></div>
          <div><dt>神经 Pass</dt><dd>{completion.passes}</dd></div>
          <div><dt>代理方式</dt><dd>{completion.proxy.replace(".dll", "").toUpperCase()}</dd></div>
        </dl>
        <p>文件已部署并校验；实际画质、性能与稳定性需进入游戏验证。</p>
        {completion.backend === "mochizuki" && <p className="muted">首次启动可能编译数分钟，请在游戏内按 Insert 查看进度。</p>}
        <div className="neural-success-actions">
          <Button primary disabled={working} onClick={async () => {
            if (launching.current) return;
            launching.current = true; setWorking(true); setError("");
            try { await rpc("launch", { gameId: game.id }); onClose(); }
            catch (e) { setError((e as Error).message); }
            finally { launching.current = false; setWorking(false); }
          }}>启动游戏</Button>
          <Button disabled={working} onClick={() => setEditAdvanced(v => !v)}>高级设置</Button>
          <Button disabled={working} onClick={() => restoreNeural(true)}>切换方案</Button>
          <Button disabled={working} onClick={() => restoreNeural()}>恢复之前配置</Button>
        </div>
        {editAdvanced && <><NeuralSettings gameId={game.id} options={neural} onChange={setNeural} installed disabled={working} /><Button primary disabled={working} onClick={updateAdvanced}>保存高级设置</Button></>}
      </section> : <div className={`mode-scene scene-${visualMode}`}>
      {tab === "community" ? <CommunityPanel game={game} games={state.games} onApplied={onComplete} initialUpload={initialUpload} /> : <>
      {checking ? (
        <Loading text={stage} />
      ) : (
        <>
          <div className="hardware">
            <Cpu />
            <div>
              <h3>{result?.gpu.name || "显卡未确认"}</h3>
              <p>
                {result
                  ? `${result.width} × ${result.height} · ${result.gpu.vramMb ? `${(result.gpu.vramMb / 1024).toFixed(1)} GB 显存` : "显存未确认"}`
                  : "检测未完成，仍可手动选择"}
              </p>
            </div>
            <span className="badge">{result ? "检测完成" : "待确认"}</span>
          </div>
          <SchemeChoices gameId={game.id} gameTitle={game.title} state={state} selected={scheme} disabled={working}
            onSelect={chooseScheme} onUpgrade={() => setPurchase(true)} onContextChange={confirmContext}
            onUseRecommendation={options => { setScheme(options.backend === "mochizuki" ? "author" : "compat"); setGamePreset(true); setGoal("custom"); setCustom(6); setNeural({ ...defaultNeuralOptions, ...options }); }} />
          {scheme && !["base", "fps"].includes(scheme) && <p className="scheme-paid-selection"><strong>{schemes.find(s => s.id === scheme)?.name}</strong> · {scheme === "rx6000" ? "Daniel 0.6.0 · AMD RX 6000（RDNA2）适用。受显卡性能限制，无法保证帧率表现和游戏兼容性。需 Windows 11、HIP 7.2 和64位 DX12 游戏。将打开 Daniel 0.6.0 安装窗口，请在窗口中确认游戏并完成安装；MU 随后校验文件，实际效果仍需进游戏验证。" : scheme === "author" ? "Mochizuki 实验配置；本游戏未发布实测预设时不承诺最佳效果。" : scheme === "online" ? "不绕过反作弊，继续配置前需要知悉风险。" : scheme === "amd-magpie" ? "首次自动下载完整包并打开独立工具，后续校验后直接打开；效果在 AMD 喜鹊内设置，不修改游戏文件。" : "具体兼容性与效果需进入游戏验证。"}</p>}
          {scheme === "rx6000" && <label className="smart-goal-field"><span>确认本游戏实际使用的图形接口</span><select aria-label="60系显卡游戏图形接口" value={smartContext.confirmedApi} disabled={working} onChange={e => confirmContext({ ...smartContext, confirmedApi: e.target.value })}><option value="">请选择实际接口</option><option value="dx12">DirectX 12</option></select></label>}
          {mode === 6 && scheme !== "amd-magpie" && canUsePremium(state) && <NeuralSettings gameId={game.id} options={neural} onChange={setNeural} onResourceCheck={onResourceCheck} disabled={working} />}
          <details className="scheme-custom-toggle"><summary>原有目标与自定义设置</summary>
          <h4 className="field-heading">你更在意什么？</h4>
          <div className="goals" data-guide="goals">
            {(
              [
                ["quality", "画质优先", "DLSS 5 神经渲染", "更清晰的画面"],
                ["performance", "帧率优先", "DLSS 5 + XeFG 2X", "更流畅的体验"],
                ["custom", "自定义", "", "自己选择方案"],
              ] as const
            ).map(([key, title, route, note]) => (
              <button
                key={key}
                className={`goal ${goal === key ? "selected" : ""}`}
                aria-pressed={goal === key}
                onClick={() => { setScheme(null); setGamePreset(false); setGoal(key); }}
              >
                <span className={`goal-art goal-art-${key}`} aria-hidden="true"><i /><i /><i /><b /></span>
                <strong>{title}</strong>
                <span>{note}</span>
                {route && <small>{route}</small>}
                {result &&
                  (key === "quality"
                    ? result.recommendation.qualityMode === 0
                    : key === "performance"
                      ? result.recommendation.performanceMode === 4
                      : false) && <em>推荐</em>}
              </button>
            ))}
          </div>
          </details>
          {[5,8].includes(mode) && <section className="custom-options">
            <p>{is32Bit ? "已识别32位游戏，使用实验性桥接。" : "老游戏使用64位 ReShade 插件。"}神经渲染效果与性能需进入游戏验证；当前不提供XeFG帧生成。</p>
            <label>游戏图形接口<select aria-label="32位游戏图形接口" value={x86Api} onChange={e=>setX86Api(e.target.value)}>
              <option value="auto">自动识别{result?.graphicsApi && result.graphicsApi!=="unknown" ? `（${result.graphicsApi}）` : "（未确认时请手动选择）"}</option>
              {is32Bit && <option value="dx8">DirectX 8</option>}<option value="dx9">DirectX 9</option><option value="dx11">DirectX 10 / 11</option><option value="opengl">OpenGL</option>
            </select></label>
          </section>}
          {goal === "custom" && !scheme && (
            <section className="custom-options">
              <h4>选择方案</h4>
              {[0, 2, 4, 6, is32Bit ? 5 : 8].map((m) => (
                <label key={m} className="radio-row">
                  <input
                    type="radio"
                    name="mode"
                    checked={custom === m}
                    onChange={() => setCustom(m)}
                  />
                  <div>
                    <strong>{modeName(m)}</strong>
                    <small>
                      {m === 0
                        ? "神经渲染，游戏内开启 FSR，按 End 查看"
                        : m === 6
                          ? "新增方案：自动匹配 Daniel / lmxxf / Mochizuki，按 Insert 查看"
                        : m === 4
                          ? "神经渲染与 XeFG 帧生成，按 Insert 查看"
                        : m === 5 || m === 8
                          ? "ReShade 神经渲染桥接，游戏内 Home 打开面板"
                          : "标准超分辨率与帧生成，按 Insert 查看"}
                    </small>
                  </div>
                </label>
              ))}
              {custom === 2 && (
                <div className="advanced-options">
                  <label>
                    <input
                      type="checkbox"
                      checked={vulkan}
                      onChange={(e) => setVulkan(e.target.checked)}
                    />{" "}
                    Vulkan 接口
                  </label>
                  <label>
                    帧生成入口
                    <select
                      value={fgInput}
                      onChange={(e) => setFgInput(e.target.value)}
                    >
                      <option value="nofg">不启用</option>
                      <option value="fsrfg">FSR 帧生成</option>
                      <option value="dlssg">DLSS 帧生成</option>
                    </select>
                  </label>
                </div>
              )}
            </section>
          )}
          {result && mode !== 6 && (
            <div className="advice">
              <p>
                {result.recommendation.availability === 0
                  ? "已匹配基础条件；游戏运行效果仍需确认。"
                  : goal === "performance"
                    ? result.recommendation.performanceReason
                    : result.recommendation.qualityReason}
              </p>
              {result.hipSetupPending && (
                <p>RX 6000 需要 HIP 7.2，配置时将引导安装。</p>
              )}
            </div>
          )}
          {!result && <Button onClick={detect}>重新检测</Button>}
        </>
      )}
      </>}
      </div>}
      {working && (
        <div className="operation">
          <p role="status">{state.status || "正在准备组件…"}</p>
          {activeDownload && <div className="actions">
            <Button disabled={downloadActionBusy} onClick={() => void controlDownload("pause")}>{activeDownload.paused ? "继续下载" : "暂停下载"}</Button>
            <Button disabled={downloadActionBusy} onClick={() => void controlDownload("cancel")}>取消下载</Button>
            <span role="status">{activeDownload.state}</span>
          </div>}
          <Progress
            percent={activeDownload?.percent || 0}
            unknown={
              !activeDownload ||
              ["连接中", "校验中"].includes(activeDownload.state)
            }
          />
        </div>
      )}
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {info && <p role="status" className="muted">{info}</p>}
    </Modal>
    {purchase && <PurchaseDialog state={state} onClose={() => setPurchase(false)} notify={message => setError(message)} />}</>
  );
}
export function DiagnosticsDialog({
  game,
  onClose,
  onConfigure,
}: {
  game: Game;
  onClose: () => void;
  onConfigure: () => void;
}) {
  const [checks, setChecks] = useState<Check[] | null>(null),
    [error, setError] = useState(""),
    [expanded, setExpanded] = useState<string[]>([]);
  useEffect(() => {
    let active = true;
    rpc<Check[]>("checks", { gameId: game.id })
      .then((v) => active && setChecks(v))
      .catch((e) => active && setError(e.message));
    return () => {
      active = false;
    };
  }, [game.id]);
  return (
    <Modal
      title="一键排查"
      onClose={onClose}
      wide
      footer={
        <>
          <span className="footer-note">
            文件齐全不代表已生效，请进游戏确认。
          </span>
          <Button onClick={onClose}>关闭</Button>
          <Button primary disabled={!!error} onClick={onConfigure}>
            继续配置
          </Button>
        </>
      }
    >
      {!checks && !error ? (
        <Loading text="正在检查显卡、驱动与安装文件" />
      ) : (
        <>
          <div className="diagnostic-heading">
            <CheckCircle2 />
            <div>
              <h2>
                {error
                  ? "检查未完成"
                  : checks?.some((x) => x.problem && !x.unknown)
                    ? `发现 ${checks.filter((x) => x.problem && !x.unknown).length} 项需要处理`
                    : checks?.some((x) => x.unknown)
                      ? "基础检查已完成"
                      : "基础检查通过"}
              </h2>
              <p>
                {checks?.some((x) => x.problem && !x.unknown)
                  ? "请查看未通过的项目，再继续配置。"
                  : checks?.some((x) => x.unknown)
                    ? `${checks.filter((x) => x.unknown).length} 项仍需在游戏中验证。`
                    : "当前环境已满足基础运行条件，可继续配置与验证。"}
              </p>
            </div>
          </div>
          {checks?.map((c) => (
            <div key={c.title} className="check-row">
              <button
                onClick={() =>
                  setExpanded((x) =>
                    x.includes(c.title)
                      ? x.filter((t) => t !== c.title)
                      : [...x, c.title],
                  )
                }
                aria-expanded={expanded.includes(c.title)}
              >
                <SlidersHorizontal size={24} />
                <div>
                  <strong>{c.title}</strong>
                  <p>{c.detail}</p>
                </div>
                <span
                  className={
                    c.problem && !c.unknown
                      ? "warning"
                      : c.unknown
                        ? "muted"
                        : "accent"
                  }
                >
                  {c.problem && !c.unknown
                    ? "需处理"
                    : c.unknown
                      ? "待验证"
                      : "已通过"}
                </span>
                {expanded.includes(c.title) ? (
                  <ChevronUp size={18} />
                ) : (
                  <ChevronDown size={18} />
                )}
              </button>
              {expanded.includes(c.title) && (
                <p className="check-detail">
                  {c.detail}
                  {c.unknown ? " 实际状态请启动游戏后确认。" : ""}
                </p>
              )}
            </div>
          ))}
        </>
      )}
      {error && <p className="error">{error}</p>}
    </Modal>
  );
}
