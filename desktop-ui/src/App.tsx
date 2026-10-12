import { useCallback, useEffect, useRef, useState } from "react";
import {
  Search,
  Sun,
  Moon,
  Settings,
  UserRound,
  Minus,
  Square,
  X,
  RefreshCw,
  Plus,
  ArrowUpRight,
  Play,
  RotateCcw,
  Sparkles,
  ScanLine,
  Download,
  Gamepad2,
  Monitor,
  ShieldCheck,
  Zap,
  Pause,
  Copy,
  Trash2,
  Check,
  UsersRound,
  CircleHelp,
} from "lucide-react";
import { rpc, subscribe, emptyState, isDesktop, isPreview } from "./bridge";
import { Button, Modal, Loading, Empty, Progress } from "./components";
import { SmartDialog, DiagnosticsDialog, modeName } from "./SmartDialog";
import { AccountPage } from "./AccountPage";
import { WishesPage } from "./WishesPage";
import { FirstUseGuide } from "./FirstUseGuide";
import type { State, Page, Game, Prompt } from "./types";
const nav: [Page, string, string][] = [
  ["home", "首页", "Home"],
  ["library", "游戏库", "Library"],
  ["magpie", "大力喜鹊", "Magpie"],
  ["wishes", "许愿池", "Wishes"],
  ["downloads", "下载任务", "Downloads"],
  ["help", "帮助与反馈", "Help"],
];
export default function App() {
  const [state, setState] = useState<State>(emptyState),
    [ready, setReady] = useState(false),
    [fatal, setFatal] = useState(""),
    [search, setSearch] = useState(""),
    [filter, setFilter] = useState("All"),
    [selected, setSelected] = useState<Game | null>(null),
    [dialog, setDialog] = useState<"smart" | "diagnostics" | null>(null),
    [smartTab, setSmartTab] = useState<"recommend" | "community">("recommend"),
    [guideStep, setGuideStep] = useState<number | null>(null),
    [guideRequested, setGuideRequested] = useState(false),
    [toast, setToast] = useState(""),
    [prompt, setPrompt] = useState<Prompt | null>(null),
    [notice, setNotice] = useState<{ title: string; text: string } | null>(
      null,
    ),
    [pending, setPending] = useState<string[]>([]),
    [doneTab, setDoneTab] = useState(false),
    [legal, setLegal] = useState(false);
  const guideChecked = useRef(false);
  const english = state.settings.english;
  const t = (cn: string, en: string) => (english ? en : cn);
  const notify = useCallback((text: string) => setToast(text), []);
  useEffect(() => {
    if (!toast) return;
    const timer = setTimeout(() => setToast(""), 5000);
    return () => clearTimeout(timer);
  }, [toast]);
  useEffect(() => {
    const unsub = subscribe((event, data) => {
      if (event === "state") {
        setState(data as State);
        setReady(true);
      }
      if (event === "prompt") setPrompt(data as Prompt);
      if (event === "toast") notify((data as { message: string }).message);
      if (event === "failure")
        setNotice(data as { title: string; text: string });
    });
    rpc<State>("state")
      .then((v) => {
        setState(v);
        setReady(true);
      })
      .catch((e) => setFatal(e.message));
    return unsub;
  }, [notify]);
  useEffect(() => {
    document.documentElement.dataset.theme = state.settings.dark
      ? "dark"
      : "light";
    document.documentElement.lang = english ? "en" : "zh-CN";
  }, [state.settings.dark, english]);
  const run = useCallback(
    async (command: string, args: Record<string, unknown> = {}) => {
      setPending((p) => [...p, command]);
      try {
        const result = await rpc(command, args);
        const latest = await rpc<State>("state");
        setState(latest);
        return result;
      } catch (e) {
        notify((e as Error).message);
        return undefined;
      } finally {
        setPending((p) => p.filter((x) => x !== command));
      }
    },
    [notify],
  );
  const navigate = useCallback(
    (page: Page) => {
      setSearch("");
      void run("navigate", { page });
    },
    [run],
  );
  const closeDialog = useCallback(() => setDialog(null), []);
  const show = (game: Game, kind: "smart" | "community" | "diagnostics") => {
    setSelected(game);
    if (kind === "diagnostics") setDialog("diagnostics");
    else {
      setSmartTab(kind === "community" ? "community" : "recommend");
      setDialog("smart");
    }
  };
  useEffect(() => {
    if (!ready || !state.viewAvailable || guideChecked.current) return;
    guideChecked.current = true;
    try {
      if (localStorage.getItem("mu-first-use-guide-v1") === "done") return;
    } catch { /* WebView storage may be unavailable; the guide can still run. */ }
    setGuideRequested(true);
  }, [ready, state.viewAvailable]);
  useEffect(() => {
    if (!guideRequested || !state.viewAvailable) return;
    if (state.page !== "library") {
      void run("navigate", { page: "library" });
      return;
    }
    if (state.scanning) return;
    setGuideRequested(false);
    setGuideStep(0);
  }, [guideRequested, state.viewAvailable, state.page, state.scanning, run]);
  const startGuide = () => {
    setDialog(null);
    setGuideRequested(true);
  };
  const finishGuide = () => {
    setGuideStep(null);
    try { localStorage.setItem("mu-first-use-guide-v1", "done"); } catch { /* optional */ }
  };
  const changeGuideStep = (step: number) => {
    const game = state.games[0];
    if (step === 2 || step === 3) {
      if (game) show(game, "smart");
      else setDialog(null);
    } else if (step === 5 || step === 6) {
      if (game) show(game, "community");
      else setDialog(null);
    } else setDialog(null);
    setGuideStep(step);
  };
  const reply = useCallback(
    (accepted: boolean) => {
      if (prompt) {
        void rpc("promptReply", { promptId: prompt.id, accepted });
        setPrompt(null);
      }
    },
    [prompt],
  );
  const count = (kind: string) =>
    state.games.filter((g) => kind === "All" || g.state === kind).length;
  const visible = state.games.filter(
    (g) =>
      g.title.toLowerCase().includes(search.toLowerCase()) &&
      (filter === "All" || g.state === filter),
  );
  const busy =
    state.busy ||
    pending.some((p) =>
      [
        "configure",
        "restore",
        "scan",
        "magpie",
        "update",
        "removeGame",
      ].includes(p),
    );
  const gameCard = (game: Game) => (
    <article
      key={game.id}
      className={`game-card ${selected?.id === game.id ? "chosen" : ""} ${state.page === "library" && state.games[0]?.id === game.id && (guideStep === 1 || guideStep === 4) ? "guide-active" : ""}`}
      tabIndex={0}
      aria-label={game.title}
      data-guide="game-smart"
      onClick={() => setSelected(game)}
      onKeyDown={(e) => {
        if (e.key === "Enter" && e.target === e.currentTarget)
          show(game, "smart");
      }}
    >
      <div className="game-cover">
        {state.page === "library" && (
          <button
            className="remove-game"
            aria-label={`从游戏库移除 ${game.title}`}
            title="从游戏库移除"
            disabled={busy || state.scanning}
            onClick={(e) => {
              e.stopPropagation();
              void run("removeGame", { gameId: game.id }).then((result) => {
                if (result !== undefined && selected?.id === game.id)
                  setSelected(null);
              });
            }}
          >
            <Trash2 size={17} />
          </button>
        )}
        {game.cover ? (
          <img
            src={game.cover}
            alt={game.title}
            loading="lazy"
            onError={(e) => (e.currentTarget.style.display = "none")}
          />
        ) : (
          <div className="cover-fallback">
            <Gamepad2 size={46} />
            <span>{game.title}</span>
          </div>
        )}
        <div className="game-overlay">
          <Button
            primary
            disabled={busy}
            onClick={() =>
              game.state === "Configured"
                ? run("launch", { gameId: game.id })
                : show(game, "smart")
            }
          >
            <Sparkles size={18} />
            {game.state === "Configured"
              ? t("启动游戏", "Launch game")
              : t("开启 DLSS 5", "Enable DLSS 5")}
          </Button>
          <div className="actions" data-guide="game-support">
            <Button disabled={busy} onClick={() => show(game, "diagnostics")}>
              <ScanLine size={17} />
              {t("排查", "Check")}
            </Button>
            <Button
              disabled={busy}
              onClick={() => run("restore", { gameId: game.id })}
            >
              <RotateCcw size={17} />
              {t("恢复", "Restore")}
            </Button>
          </div>
        </div>
      </div>
      <div className="game-caption">
        <strong title={game.title}>{game.title}</strong>
        <p className={game.state === "Configured" ? "accent" : "muted"}>
          {game.state === "Configured"
            ? t("已配置", "Configured")
            : game.state === "Unsupported"
              ? t("兼容性待确认", "Compatibility unconfirmed")
              : t("未配置", "Not configured")}
          {game.mode != null && <span> · {modeName(game.mode)}</span>}
        </p>
      </div>
    </article>
  );
  const gameGrid = (items: Game[], add = true) => (
    <div className="game-grid">
      {items.map(gameCard)}
      {add && (
        <button
          className="add-game-card"
          disabled={busy}
          onClick={() => run("addGame")}
        >
          <Plus size={42} />
          <strong>{t("添加游戏", "Add game")}</strong>
          <span>{t("选择本地游戏 · 加入游戏库", "Choose a local game")}</span>
        </button>
      )}
    </div>
  );
  const external = (url: string) => run("openLink", { url });
  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="header-first">
          <button className="brand" onClick={() => navigate("home")}>
            <img src="/artwork/amd_dlss_mu_logo.png" alt="MU" />
            <strong>AMD DLSS MU</strong>
          </button>
          <nav aria-label="主导航">
            {nav.map(([key, cn, en]) => (
              <button
                key={key}
                className={state.page === key ? "active" : ""}
                disabled={
                  !state.viewAvailable && !(key === "downloads" && state.busy)
                }
                onClick={() => navigate(key)}
              >
                {t(cn, en)}
              </button>
            ))}
          </nav>
          <div className="header-tools">
            <button
              className="icon-button"
              aria-label="切换主题"
              onClick={() => run("settings", { dark: !state.settings.dark })}
            >
              {state.settings.dark ? <Sun size={19} /> : <Moon size={19} />}
            </button>
            <button
              className="text-button"
              onClick={() => run("settings", { english: !english })}
            >
              {english ? "EN" : "ZH"}
            </button>
            <button
              className="account-shortcut"
              onClick={() => navigate("account")}
            >
              <UserRound size={17} />
              {state.account ? t("我的账户", "Account") : t("登录", "Sign in")}
            </button>
            <button
              className="icon-button"
              aria-label="设置"
              onClick={() => navigate("settings")}
            >
              <Settings size={18} />
            </button>
            <div className="window-buttons">
              <button
                aria-label="最小化"
                onClick={() => rpc("window", { action: "minimize" })}
              >
                <Minus size={16} />
              </button>
              <button
                aria-label="最大化"
                onClick={() => rpc("window", { action: "maximize" })}
              >
                <Square size={13} />
              </button>
              <button
                aria-label="关闭窗口"
                onClick={() => rpc("window", { action: "close" })}
              >
                <X size={17} />
              </button>
            </div>
          </div>
        </div>
        <div className="header-second">
          <div
            className="drag-space"
            onMouseDown={(e) => {
              if (e.button === 0 && isDesktop)
                void rpc("window", { action: "drag" });
            }}
            onDoubleClick={() =>
              isDesktop && rpc("window", { action: "maximize" })
            }
          />
          <label className="search">
            <Search size={18} />
            <input
              aria-label="搜索游戏"
              placeholder={t("搜索游戏 / Search games…", "Search games…")}
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                if (state.page !== "library")
                  void run("navigate", { page: "library" });
              }}
              disabled={!state.viewAvailable}
            />
          </label>
          <Button
            primary
            disabled={
              !state.viewAvailable || busy || pending.includes("update")
            }
            onClick={() => run("update")}
          >
            <RefreshCw
              size={18}
              className={pending.includes("update") ? "spin" : ""}
            />
            {t("检查更新", "Check updates")}
          </Button>
        </div>
      </header>
      {isPreview && (
        <div className="preview-label">
          React 界面预览 · 示例数据，真实操作请用 Windows 客户端
        </div>
      )}
      {!isDesktop && !isPreview && fatal ? (
        <Empty title="请在 MU 客户端中打开">{fatal}</Empty>
      ) : !ready ? (
        <Loading text={t("正在启动 MU…", "Starting MU…")} />
      ) : (
        <main
          className="page"
          key={state.page}
          onWheel={(e) => {
            if (
              state.page === "home" &&
              e.deltaY > 0 &&
              !dialog &&
              !prompt &&
              !pending.includes("navigate")
            )
              navigate("library");
          }}
          onDragOver={(e) => {
            if (state.page === "library" || state.page === "home") {
              e.preventDefault();
              e.dataTransfer.dropEffect = "copy";
            }
          }}
          onDrop={(e) => {
            e.preventDefault();
            if (busy || !state.viewAvailable) return;
            void rpc("dropGames", {}, Array.from(e.dataTransfer.files))
              .then(() => run("state"))
              .catch((e) => notify(e.message));
          }}
        >
          {state.page === "home" && (
            <>
              <section className="home-hero">
                <div>
                  <span className="eyebrow">AMD DLSS MU</span>
                  <h1>
                    一键配置 AMD <span>DLSS5</span>
                  </h1>
                  <div className="actions">
                    <Button primary onClick={() => navigate("library")}>
                      <Sparkles size={19} />
                      智能推荐
                    </Button>
                    <Button onClick={() => navigate("magpie")}>
                      开启大力喜鹊
                      <ArrowUpRight size={17} />
                    </Button>
                  </div>
                </div>
              </section>
              <div className="page-heading">
                <div>
                  <h2>{t("我的游戏", "My games")}</h2>
                  <p>
                    {t(
                      "已扫描到的本地游戏，选择并一键配置。",
                      "Choose a local game and configure it in one click.",
                    )}
                  </p>
                </div>
                <Button onClick={() => navigate("library")}>
                  {t("查看全部", "View all")}
                  <ArrowUpRight size={17} />
                </Button>
              </div>
              <div className="home-games">
                {gameGrid(state.games.slice(0, 3))}
              </div>
              <button
                className="scroll-library text-button"
                onClick={() => navigate("library")}
              >
                下滑查看全部游戏 ↓
              </button>
            </>
          )}
          {state.page === "library" && (
            <>
              <div className="page-heading">
                <h1>{t("游戏库", "Game library")}</h1>
                <div className="actions">
                  <Button data-guide="add-game" disabled={busy} onClick={() => run("addGame")}>
                    <Plus size={18} />
                    {t("添加游戏", "Add game")}
                  </Button>
                  <Button disabled={busy} onClick={() => run("scan")}>
                    <RefreshCw
                      className={state.scanning ? "spin" : ""}
                      size={18}
                    />
                    {state.scanning
                      ? t("扫描中…", "Scanning…")
                      : t("重新扫描", "Rescan")}
                  </Button>
                  <Button onClick={startGuide} title="重新查看软件使用引导">
                    <CircleHelp size={17} />新手引导
                  </Button>
                  <Button data-guide="community-header" disabled={busy || !state.games.length}
                    onClick={() => {
                      if (selected) show(selected, "community");
                      else notify("请先选择一款游戏，再查看玩家方案。");
                    }}>
                    <UsersRound size={17} />玩家方案 <span className="new-badge">NEW</span>
                  </Button>
                  <Button
                    primary
                    data-guide="smart-header"
                    disabled={busy || !state.games.length}
                    onClick={() => {
                      if (selected) show(selected, "smart");
                      else
                        notify(
                          t(
                            "请点击游戏封面上的“开启 DLSS 5”。",
                            "Choose Enable DLSS 5 on a game cover.",
                          ),
                        );
                    }}
                  >
                    <Sparkles size={18} />
                    {t("智能推荐", "Smart setup")}
                  </Button>
                </div>
              </div>
              <div className="tabs library-tabs">
                {[
                  ["All", "全部", "All"],
                  ["Configured", "已配置", "Configured"],
                  ["Unconfigured", "未配置", "Unconfigured"],
                  ["Unsupported", "待确认", "Unconfirmed"],
                ].map(([key, cn, en]) => (
                  <button
                    key={key}
                    className={filter === key ? "active" : ""}
                    onClick={() => setFilter(key)}
                  >
                    {t(cn, en)} {count(key)}
                  </button>
                ))}
              </div>
              {state.scanning && (
                <Loading text={state.status || "正在扫描游戏目录…"} />
              )}{" "}
              {!visible.length && search ? (
                <Empty title={t("没有匹配的游戏", "No matching games")} />
              ) : (
                gameGrid(visible, false)
              )}
            </>
          )}
          {state.page === "magpie" && (
            <>
              <section className="magpie-hero">
                <h1>
                  大力喜鹊 <span className="badge">推荐</span>
                </h1>
                <p>一键开启 Magpie · 独立窗口缩放</p>
              </section>
              <div className="magpie-layout">
                <section className="panel">
                  <h2>
                    <Zap size={26} />
                    一键开启大力喜鹊
                  </h2>
                  <Button
                    primary
                    className="large"
                    disabled={busy}
                    onClick={() => run("magpie")}
                  >
                    {pending.includes("magpie")
                      ? "正在准备组件…"
                      : "开启大力喜鹊"}
                  </Button>
                  <p role="status" className="muted">
                    {state.magpieStatus ||
                      "首次开启自动下载完整包，后续校验后直接启动。"}
                  </p>
                  {pending.includes("magpie") && (
                    <Progress
                      percent={state.downloads[0]?.percent || 0}
                      unknown={!state.downloads[0]}
                    />
                  )}
                  <div className="feature-grid">
                    {[
                      [Download, "自动下载依赖"],
                      [ShieldCheck, "校验组件"],
                      [Monitor, "独立窗口运行"],
                      [Gamepad2, "不改游戏文件"],
                    ].map(([Icon, text], i) => {
                      const I = Icon as typeof Download;
                      return (
                        <div className="feature" key={i}>
                          <I />
                          <strong>{String(text)}</strong>
                        </div>
                      );
                    })}
                  </div>
                </section>
                <aside className="panel">
                  <h3>使用说明</h3>
                  <ol>
                    <li>点击开启，等待组件准备完成</li>
                    <li>将游戏设置为窗口模式</li>
                    <li>切回游戏，按 Alt + Shift + A</li>
                  </ol>
                  <p className="muted">
                    实际效果取决于显卡与运行组件。请勿叠加多套补帧方案。
                  </p>
                </aside>
              </div>
              <div className="page-heading">
                <h2>已配置的游戏</h2>
              </div>
              {gameGrid(
                state.games.filter((g) => g.state === "Configured"),
                false,
              )}
              <h2>最近的游戏</h2>
              {gameGrid(state.games.slice(0, 6), false)}
            </>
          )}
          {state.page === "wishes" && <WishesPage notify={notify} />}
          {state.page === "downloads" && (
            <>
              <div className="page-heading">
                <div>
                  <h1>{t("下载任务", "Downloads")}</h1>
                  <p>
                    {t(
                      "组件下载进度与校验结果",
                      "Component downloads and verification",
                    )}
                  </p>
                </div>
              </div>
              <div className="tabs">
                <button
                  className={!doneTab ? "active" : ""}
                  onClick={() => setDoneTab(false)}
                >
                  进行中 / 未完成
                </button>
                <button
                  className={doneTab ? "active" : ""}
                  onClick={() => setDoneTab(true)}
                >
                  已完成
                </button>
              </div>
              {!state.downloads.some(
                (d) => doneTab === (d.state === "已完成 · 校验通过"),
              ) ? (
                <Empty title={doneTab ? "暂无完成记录" : "没有正在下载的任务"}>
                  选择游戏并配置方案后，所需组件会出现在这里。
                </Empty>
              ) : (
                state.downloads
                  .filter((d) => doneTab === (d.state === "已完成 · 校验通过"))
                  .map((d) => (
                    <section className="panel download-row" key={d.id}>
                      <div className="page-heading">
                        <h3>{d.title}</h3>
                        <span className="badge">{d.state}</span>
                      </div>
                      <p>
                        {d.percent}% · {(d.size / 1048576).toFixed(1)} MiB
                      </p>
                      <Progress
                        percent={d.percent}
                        unknown={d.state === "连接中"}
                      />
                      <p className="muted">{d.detail.split("\n")[0]}</p>
                      <div className="actions">
                        <Button
                          disabled={!["下载中", "已暂停"].includes(d.state)}
                          onClick={() =>
                            run("downloadAction", { id: d.id, action: "pause" })
                          }
                        >
                          <Pause size={16} />
                          {d.paused ? "继续" : "暂停"}
                        </Button>
                        <Button
                          disabled={
                            !["下载中", "已暂停", "连接中"].includes(d.state)
                          }
                          onClick={() =>
                            run("downloadAction", {
                              id: d.id,
                              action: "cancel",
                            })
                          }
                        >
                          取消
                        </Button>
                        {["下载失败", "已取消"].includes(d.state) && (
                          <Button
                            disabled={busy}
                            onClick={() =>
                              run("downloadAction", {
                                id: d.id,
                                action: "retry",
                              })
                            }
                          >
                            重试
                          </Button>
                        )}
                        <Button
                          onClick={() =>
                            setNotice({ title: d.title, text: d.detail })
                          }
                        >
                          详情
                        </Button>
                      </div>
                    </section>
                  ))
              )}
            </>
          )}
          {state.page === "settings" && (
            <>
              <h1>{t("设置", "Settings")}</h1>
              <section className="panel settings-list">
                <div>
                  <div>
                    <h3>外观</h3>
                    <p>选择适合你的界面</p>
                  </div>
                  <div className="segmented">
                    <button
                      className={state.settings.dark ? "active" : ""}
                      onClick={() => run("settings", { dark: true })}
                    >
                      夜间模式
                    </button>
                    <button
                      className={!state.settings.dark ? "active" : ""}
                      onClick={() => run("settings", { dark: false })}
                    >
                      日间模式
                    </button>
                  </div>
                </div>
                <div>
                  <h3>语言</h3>
                  <select
                    value={english ? "en" : "zh"}
                    onChange={(e) =>
                      run("settings", { english: e.target.value === "en" })
                    }
                  >
                    <option value="zh">中文</option>
                    <option value="en">English</option>
                  </select>
                </div>
                <div>
                  <h3>启动时自动检查更新</h3>
                  <input
                    aria-label="启动时自动检查更新"
                    type="checkbox"
                    checked={state.settings.autoUpdate}
                    onChange={(e) =>
                      run("settings", { autoUpdate: e.target.checked })
                    }
                  />
                </div>
                <div>
                  <div>
                    <h3>游戏运行监测</h3>
                    <p>{state.settings.presentMon || "未选择 PresentMon"}</p>
                  </div>
                  <Button onClick={() => run("presentMon")}>
                    选择 PresentMon
                  </Button>
                </div>
                <div>
                  <div>
                    <h3>监测数据</h3>
                    <p>只采集已配置游戏的运行状态</p>
                  </div>
                  <Button onClick={() => setLegal(true)}>
                    清除我的监测数据
                  </Button>
                </div>
                <div>
                  <div>
                    <h3>版本</h3>
                    <p>AMD DLSS MU v{state.version}</p>
                  </div>
                  <Button disabled={busy} onClick={() => run("update")}>
                    检查更新
                  </Button>
                </div>
                <div>
                  <div>
                    <h3>下载源</h3>
                    <p>自动选择与切换</p>
                  </div>
                  <Button onClick={() => navigate("about")}>关于 MU</Button>
                </div>
              </section>
            </>
          )}
          {state.page === "account" && (
            <AccountPage state={state} notify={notify} />
          )}
          {state.page === "help" && (
            <>
              <h1>帮助与反馈</h1>
              <p className="muted">先定位问题，不必反复重装。</p>
              <div className="help-grid">
                <section className="panel">
                  <h3>菜单没有反应？</h3>
                  <p>
                    模式一：开启游戏内 FSR 超分辨率，进入实际场景后按
                    End。模式二与 XeFG 方案按 Insert 查看。
                  </p>
                  <Button onClick={() => navigate("library")}>
                    选择游戏并排查
                  </Button>
                </section>
                <section className="panel">
                  <h3>游戏卡顿或闪退？</h3>
                  <p>退出游戏，在游戏库对应封面选择“恢复”，再验证原始状态。</p>
                  <Button onClick={() => navigate("library")}>
                    恢复游戏配置
                  </Button>
                </section>
                <section className="panel">
                  <h3>联系作者</h3>
                  <p>微信：xrwCoder · 添加后邀请入群</p>
                  <Button onClick={() => navigate("about")}>
                    查看联系信息
                  </Button>
                </section>
              </div>
            </>
          )}
          {state.page === "about" && (
            <>
              <h1>关于 AMD DLSS MU</h1>
              <section className="panel about">
                <img
                  className="about-logo"
                  src="/artwork/amd_dlss_mu_logo.png"
                  alt="MU"
                />
                <h2>AMD DLSS MU</h2>
                <p className="muted">v{state.version} · codeXia</p>
                <p>整合图形方案，简化游戏配置。</p>
                <div className="actions">
                  <Button
                    onClick={() =>
                      external("https://amd-dlss-mu.claude-api.cn/")
                    }
                  >
                    官方网站
                    <ArrowUpRight size={16} />
                  </Button>
                  <Button
                    onClick={() =>
                      external("https://github.com/xiarongwu123/AMD-DLSS-MU")
                    }
                  >
                    GitHub
                    <ArrowUpRight size={16} />
                  </Button>
                  <Button
                    onClick={() =>
                      rpc<string>("licenses")
                        .then((text) =>
                          setNotice({ title: "第三方组件与许可", text }),
                        )
                        .catch((e) => notify(e.message))
                    }
                  >
                    组件许可
                  </Button>
                </div>
                <img
                  className="community-qr"
                  src="/artwork/wechat-xrwCoder.jpg"
                  alt="作者微信二维码"
                />
                <p>微信：xrwCoder</p>
              </section>
            </>
          )}
        </main>
      )}
      {state.status && (
        <div className="status-bar" role="status">
          {state.busy && <span className="status-dot" />}
          {state.status}
        </div>
      )}
      {dialog === "smart" && selected && (
        <SmartDialog
          key={selected.id}
          game={state.games.find((g) => g.id === selected.id) || selected}
          state={state}
          initialTab={smartTab}
          onClose={closeDialog}
          onComplete={() => {
            setDialog(null);
            notify("配置完成，可以启动游戏");
            void run("state");
          }}
        />
      )}
      {dialog === "diagnostics" && selected && (
        <DiagnosticsDialog
          game={selected}
          onClose={closeDialog}
          onConfigure={() => setDialog("smart")}
        />
      )}
      {guideStep !== null && <FirstUseGuide step={guideStep} hasGames={state.games.length > 0}
        onStep={changeGuideStep} onFinish={finishGuide} />}
      {prompt && (
        <Modal
          title={prompt.title}
          onClose={() => reply(false)}
          footer={
            <>
              {prompt.confirm && (
                <Button onClick={() => reply(false)}>取消</Button>
              )}
              <Button primary onClick={() => reply(true)}>
                {prompt.confirm ? "确认" : "知道了"}
              </Button>
            </>
          }
        >
          <p className="preserve">{prompt.text}</p>
        </Modal>
      )}
      {notice && (
        <Modal
          title={notice.title}
          onClose={() => setNotice(null)}
          footer={
            <>
              <Button
                onClick={() =>
                  navigator.clipboard
                    .writeText(notice.text)
                    .then(() => notify("已复制"))
                    .catch(() => notify("复制失败，请选中文字复制"))
                }
              >
                <Copy size={16} />
                复制详情
              </Button>
              <Button primary onClick={() => setNotice(null)}>
                关闭
              </Button>
            </>
          }
        >
          <pre>{notice.text}</pre>
        </Modal>
      )}
      {legal && (
        <Modal
          title="清除监测数据"
          onClose={() => setLegal(false)}
          footer={
            <>
              <Button onClick={() => setLegal(false)}>取消</Button>
              <Button
                primary
                onClick={() => {
                  setLegal(false);
                  void run("eraseTelemetry").then(() =>
                    notify("清理请求已处理"),
                  );
                }}
              >
                确认清除
              </Button>
            </>
          }
        >
          <p>
            将删除此账户在本机待上传和服务器已保存的监测数据。已配置游戏下次运行会重新采集。
          </p>
        </Modal>
      )}
      {toast && (
        <div className="toast" role="status">
          <Check size={19} />
          {toast}
          <button
            aria-label="关闭提示"
            className="icon-button"
            onClick={() => setToast("")}
          >
            <X size={16} />
          </button>
        </div>
      )}
    </div>
  );
}
