import { SmartRecommendationPanel, type SmartContext } from "./SmartRecommendationPanel";
import { ArrowUpRight, Crown, Gamepad2, LockKeyhole, Puzzle, ShieldCheck, Sparkles, UsersRound, WandSparkles, Zap } from "lucide-react";
import { Button } from "./components";
import { rpc } from "./bridge";
import type { State } from "./types";
import { canUseMemberFeature } from "./membership";
import "./scheme-choices.css";

export type SchemeId = "base" | "fps" | "compat" | "author" | "online" | "amd-magpie" | "legacy";
export const schemes = [
  { id: "base", name: "基础稳定版", detail: "Daniel 0.8 · 官方版入口", premium: false, icon: ShieldCheck },
  { id: "fps", name: "高帧率版", detail: "DLSS 5 + XeFG 2X", premium: false, icon: Zap },
  { id: "author", name: "作者调校版", detail: "Mochizuki · RX 9000 实验路线", premium: true, icon: Sparkles },
  { id: "compat", name: "兼容版", detail: "多后端 · 尝试其他加载路径", premium: true, icon: Puzzle },
  { id: "online", name: "网游适配版", detail: "按游戏配置 · 使用前确认风险", premium: true, icon: UsersRound },
  { id: "amd-magpie", name: "AMD 大力喜鹊", detail: "独立窗口增强 · RDNA3 实验版", premium: true, icon: WandSparkles },
  { id: "legacy", name: "老游戏专用版", detail: "ReShade 桥接 · 支持 32 位游戏", premium: true, icon: Gamepad2 },
] as const;
export function canUsePremium(state: State) {
  return canUseMemberFeature(state, "schemes.premium.configure");
}
export function SchemeChoices({ gameId, gameTitle, selected, state, disabled, onSelect, onUpgrade, onContextChange }: {
  gameId: string; gameTitle: string; selected: SchemeId | null; state: State; disabled: boolean;
  onSelect: (id: SchemeId) => void; onUpgrade: () => void;
  onContextChange?: (value: SmartContext) => void;
  onUseRecommendation?: (options: { backend: string; proxy: string; passes: number }) => void;
}) {
  const allowed = canUsePremium(state);
  return <section className="scheme-choices" aria-label="一键配置方案">
    <div className="scheme-choice-heading"><h4>选择一个方案，一键配置</h4><span>2 个免费方案 · 5 个高级方案</span></div>
    <div className="scheme-choice-grid">
      {schemes.map(s => <button type="button" key={s.id} disabled={disabled} aria-pressed={selected === s.id}
        className={`scheme-choice ${s.id === "author" ? "scheme-author-featured" : ""} ${selected === s.id ? "is-selected" : ""}`}
        onClick={() => onSelect(s.id)}>
        <s.icon size={24} />
        <span className="scheme-choice-copy"><strong>{s.name}</strong><small>{s.detail}</small></span>
        <span className={`scheme-access ${s.premium ? "is-premium" : ""}`}>
          {s.premium ? <>{allowed ? <Crown size={12}/> : <LockKeyhole size={12}/>}高级会员</> : "免费"}
        </span>
        {s.id === "author" && <span className="scheme-author-note">主推方案</span>}
      </button>)}
    </div>
    <SmartRecommendationPanel gameId={gameId} gameTitle={gameTitle} state={state} disabled={disabled} selected={selected} onUpgrade={onUpgrade} onContextChange={onContextChange}/>

  </section>;
}
