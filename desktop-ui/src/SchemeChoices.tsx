import { SmartRecommendationPanel, type SmartContext } from "./SmartRecommendationPanel";
import { ArrowUpRight, Crown, LockKeyhole, ShieldCheck, Sparkles, UsersRound, Zap } from "lucide-react";
import { Button } from "./components";
import { rpc } from "./bridge";
import type { State } from "./types";
import { canUseMemberFeature } from "./membership";
import "./scheme-choices.css";

export type SchemeId = "high-quality" | "base" | "fps" | "author" | "online" | "rx6000";
export const schemes = [
  { id: "high-quality", name: "画质增强版", detail: "70系首选 · 支持 RX 7000 / 9000 · 模型已内置", premium: true, icon: Sparkles },
  { id: "base", name: "Daniel 0.8 官方入口", detail: "NeuralHub 独立程序，需在其中完成设置", premium: false, icon: ShieldCheck },
  { id: "fps", name: "高帧率版", detail: "DLSS 5 + XeFG 2X", premium: false, icon: Zap },
  { id: "author", name: "作者调校版", detail: "90系推荐 · Mochizuki 实验路线", premium: true, icon: Sparkles },
  { id: "online", name: "网游适配版", detail: "按游戏配置 · 使用前确认风险", premium: true, icon: UsersRound },
  { id: "rx6000", name: "60系显卡专用", detail: "Daniel 0.6.0 · 受显卡性能限制，无法保证帧率表现和游戏兼容性", premium: true, icon: ShieldCheck },
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
    <div className="scheme-choice-heading"><h4>选择一个图形方案</h4><span>2 个免费方案 · 4 个高级方案</span></div>
    <div className="scheme-choice-grid">
      {schemes.map(s => <button type="button" key={s.id} disabled={disabled} aria-pressed={selected === s.id}
        className={`scheme-choice ${s.id === "author" ? "scheme-author-featured" : ""} ${selected === s.id ? "is-selected" : ""}`}
        onClick={() => onSelect(s.id)}>
        <s.icon size={24} />
        <span className="scheme-choice-copy"><strong>{s.name}</strong><small>{s.detail}</small></span>
        <span className={`scheme-access ${s.premium ? "is-premium" : ""}`}>
          {s.premium ? <>{allowed ? <Crown size={12}/> : <LockKeyhole size={12}/>}高级会员</> : "免费"}
        </span>
        {s.id === "author" && <span className="scheme-author-note">90系推荐</span>}
      </button>)}
    </div>
    <SmartRecommendationPanel gameId={gameId} gameTitle={gameTitle} state={state} disabled={disabled} selected={selected} onUpgrade={onUpgrade} onContextChange={onContextChange}/>

  </section>;
}
