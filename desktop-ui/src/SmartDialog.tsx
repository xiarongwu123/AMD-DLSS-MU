import { useState, useEffect, useCallback } from "react";
import {
  CheckCircle2,
  Cpu,
  ChevronDown,
  ChevronUp,
  SlidersHorizontal,
} from "lucide-react";
import { rpc, subscribe } from "./bridge";
import { Button, Modal, Loading, Progress } from "./components";
import type { Game, Goal, Recommendation, Check, State } from "./types";
export const modeName = (mode: number) =>
  mode === 0
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
}: {
  game: Game;
  state: State;
  onClose: () => void;
  onComplete: () => void;
}) {
  const [goal, setGoal] = useState<Goal>("quality"),
    [custom, setCustom] = useState(0),
    [result, setResult] = useState<Recommendation | null>(null),
    [checking, setChecking] = useState(true),
    [stage, setStage] = useState("正在读取显卡与专用显存"),
    [error, setError] = useState(""),
    [working, setWorking] = useState(false),
    [vulkan, setVulkan] = useState(false),
    [fgInput, setFgInput] = useState("nofg");
  const mode = selectedMode(goal, custom);
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
  useEffect(() => {
    void detect();
    return subscribe((event, data) => {
      if (event === "detection") {
        const phase = data as { gameId: string; stage: string };
        if (phase.gameId === game.id) setStage(phase.stage);
      }
    });
  }, [detect, game.id]);
  const configure = async () => {
    setWorking(true);
    setError("");
    try {
      await rpc("configure", {
        gameId: game.id,
        mode,
        vulkan: mode === 2 && vulkan,
        fgInput: mode === 2 ? fgInput : "nofg",
      });
      onComplete();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
    }
  };
  const close = useCallback(() => {
    if (!working) onClose();
  }, [working, onClose]);
  return (
    <Modal
      wide
      title="智能推荐"
      onClose={close}
      footer={
        <>
          <p className="footer-note">
            {working
              ? "正在配置，请等待或到下载任务取消。"
              : "MU 配置所需文件；实际效果请进游戏确认。"}
          </p>
          <Button disabled={working} onClick={close}>
            返回游戏库
          </Button>
          <Button
            primary
            disabled={
              checking ||
              working ||
              game.state === "Configured" ||
              (!result && goal !== "custom")
            }
            onClick={configure}
          >
            {working
              ? "正在配置…"
              : game.state === "Configured"
                ? "请先恢复再切换"
                : "一键配置"}
          </Button>
        </>
      }
    >
      <div className="steps">
        <span className={checking ? "active" : ""}>1 检测</span>
        <i />
        <span className={!checking && !working ? "active" : ""}>
          2 选择目标
        </span>
        <i />
        <span className={working ? "active" : ""}>3 一键配置</span>
      </div>
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
          <h4 className="field-heading">你更在意什么？</h4>
          <div className="goals">
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
                onClick={() => setGoal(key)}
              >
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
          {goal === "custom" && (
            <section className="custom-options">
              <h4>选择方案</h4>
              {[0, 2, 4].map((m) => (
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
                        : m === 4
                          ? "神经渲染与 XeFG 帧生成，按 Insert 查看"
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
          {result && (
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
      {working && (
        <div className="operation">
          <p role="status">{state.status || "正在准备组件…"}</p>
          <Progress
            percent={state.downloads[0]?.percent || 0}
            unknown={
              !state.downloads[0] ||
              ["连接中", "校验中"].includes(state.downloads[0].state)
            }
          />
        </div>
      )}
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
    </Modal>
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
    [expanded, setExpanded] = useState<string[]>([]),
    [details, setDetails] = useState(""),
    [pending, setPending] = useState(false);
  useEffect(() => {
    let active = true;
    rpc<Check[]>("checks", { gameId: game.id })
      .then((v) => active && setChecks(v))
      .catch((e) => active && setError(e.message));
    return () => {
      active = false;
    };
  }, [game.id]);
  async function inspect(command: string) {
    setPending(true);
    try {
      const value = await rpc(command, { gameId: game.id });
      setDetails(
        typeof value === "string" ? value : JSON.stringify(value, null, 2),
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setPending(false);
    }
  }
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
          <Button primary onClick={onConfigure}>
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
                    ? "有项目需要处理"
                    : "基础检查已完成"}
              </h2>
              <p>{game.title}</p>
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
      <div className="actions">
        <Button disabled={pending} onClick={() => inspect("inspect")}>
          刷新运行状态
        </Button>
        <Button disabled={pending} onClick={() => inspect("exportDiagnostic")}>
          诊断预览
        </Button>
        <Button
          onClick={() =>
            rpc("panel", { gameId: game.id }).catch((e) => setError(e.message))
          }
        >
          F8 控制面板
        </Button>
      </div>
      {details && (
        <>
          <pre>{details}</pre>
          <Button
            onClick={() =>
              rpc("exportDiagnostic", { gameId: game.id, save: true }).catch(
                (e) => setError(e.message),
              )
            }
          >
            保存诊断 JSON
          </Button>
        </>
      )}
      {error && <p className="error">{error}</p>}
    </Modal>
  );
}
