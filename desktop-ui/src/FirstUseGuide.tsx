import { useEffect, useRef, useState } from "react";
import { Button } from "./components";

const steps = [
  { title: "先把游戏加进来", text: "点击「添加游戏」选择游戏程序，或用「重新扫描」查找已安装的游戏。", target: '[data-guide="add-game"]' },
  { title: "从智能推荐开始", text: "选中游戏，再打开「智能推荐」。MU 会读取本机环境，帮你选择配置路线。", target: '[data-guide="game-smart"]' },
  { title: "只需选一个目标", text: "画质优先、帧率优先会自动选方案；只有「自定义」才需要手动选择。", target: '[data-guide="goals"]' },
  { title: "确认后，一键配置", text: "MU 会准备所需组件并配置游戏。完成后进入游戏，确认实际画面和流畅度。", target: '[data-guide="configure"]' },
  { title: "遇到问题，先一键排查", text: "检查显卡、驱动、组件与游戏文件。想撤销 MU 的改动，可使用「恢复配置」。", target: '[data-guide="game-support"]' },
  { title: "也可以看玩家方案", text: "这是独立入口，可以查看同一款游戏的分享设置，不影响智能推荐。", target: '[data-guide="community-tab"]' },
  { title: "喜欢自己的设置就分享", text: "配置完成并体验满意后，读取当前参数，填写标题、说明和截图即可分享。", target: '[data-guide="share"]' },
];

type Rect = { left: number; top: number; width: number; height: number };

export function FirstUseGuide({ step, hasGames, onStep, onFinish }: {
  step: number;
  hasGames: boolean;
  onStep: (step: number) => void;
  onFinish: () => void;
}) {
  const [rect, setRect] = useState<Rect | null>(null);
  const nextRef = useRef<HTMLButtonElement>(null);
  const current = steps[step];
  useEffect(() => {
    let frame = 0;
    const update = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        const selector = !hasGames && step === 1 ? '[data-guide="smart-header"]'
          : !hasGames && step === 4 ? '[data-guide="add-game"]' : current.target;
        const target = document.querySelector<HTMLElement>(selector);
        const bounds = target?.getBoundingClientRect();
        const next = bounds && bounds.width > 0 && bounds.height > 0
          ? { left: Math.max(8, bounds.left - 7), top: Math.max(8, bounds.top - 7),
              width: bounds.width + 14, height: bounds.height + 14 }
          : null;
        setRect(previous => JSON.stringify(previous) === JSON.stringify(next) ? previous : next);
      });
    };
    update();
    const observer = new MutationObserver(update);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener("resize", update);
    window.addEventListener("scroll", update, true);
    return () => {
      observer.disconnect();
      window.removeEventListener("resize", update);
      window.removeEventListener("scroll", update, true);
      cancelAnimationFrame(frame);
    };
  }, [step, hasGames, current.target]);
  useEffect(() => { nextRef.current?.focus(); }, [step]);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") { event.preventDefault(); onFinish(); }
      if (event.key === "ArrowRight") { event.preventDefault(); step === steps.length - 1 ? onFinish() : onStep(step + 1); }
      if (event.key === "ArrowLeft" && step > 0) { event.preventDefault(); onStep(step - 1); }
    };
    document.addEventListener("keydown", onKey, true);
    return () => document.removeEventListener("keydown", onKey, true);
  }, [step, onStep, onFinish]);
  const width = Math.min(360, window.innerWidth - 32);
  const cardLeft = rect ? Math.max(16, Math.min(window.innerWidth - width - 16, rect.left + rect.width / 2 - width / 2))
    : Math.max(16, (window.innerWidth - width) / 2);
  const cardTop = rect ? rect.top + rect.height + 210 < window.innerHeight
    ? rect.top + rect.height + 16 : Math.max(16, rect.top - 210)
    : Math.max(16, (window.innerHeight - 210) / 2);
  return <div className="first-use-guide">
    {rect && <div className="first-use-spotlight" style={rect} />}
    <section className="first-use-card" role="dialog" aria-modal="true" aria-label="新手引导"
      style={{ left: cardLeft, top: cardTop, width }}>
      <div className="first-use-count">{step + 1} / {steps.length} · 新手引导</div>
      <h2>{current.title}</h2>
      <p>{current.text}</p>
      {!hasGames && step > 0 && <div className="first-use-preview">
        {step === 2 ? "画质优先　·　帧率优先　·　自定义"
          : step === 5 ? "智能推荐　｜　玩家方案"
          : step === 6 ? "分享我的配置"
          : "添加游戏后即可使用此功能"}
      </div>}
      <div className="first-use-actions">
        <button className="first-use-skip" onClick={onFinish}>跳过引导</button>
        <div>
          <Button disabled={step === 0} onClick={() => onStep(step - 1)}>上一步</Button>
          <button ref={nextRef} className="button primary" onClick={() => step === steps.length - 1 ? onFinish() : onStep(step + 1)}>
            {step === steps.length - 1 ? "完成" : "下一步"}
          </button>
        </div>
      </div>
    </section>
  </div>;
}
