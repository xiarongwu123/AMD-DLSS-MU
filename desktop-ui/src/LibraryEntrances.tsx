import { useEffect, useState } from "react";
import { ArrowUpRight, Check, Copy, Crown, Gamepad2, Headset, LockKeyhole, Sparkles } from "lucide-react";
import { Button, Modal } from "./components";
import { Shine, useVisualEffects } from "./VisualEffects";
import { canUseRemoteService } from "./membership";
import { rpc } from "./bridge";
import type { State } from "./types";
import "./library-entrances.css";

export function LibraryEntrances({ state, supported, onSupported, onUpgrade }: {
  state: State; supported: boolean; onSupported: (value: boolean) => void; onUpgrade: (planId?: string) => void;
}) {
  const effects = useVisualEffects();
  const [remote, setRemote] = useState(false), [feedback, setFeedback] = useState("");
  const [checking, setChecking] = useState(false), [error, setError] = useState("");
  const [now, setNow] = useState(Date.now);
  const dates = [state.account?.membership.expiresAt, state.account?.membership.plusExpiresAt]
    .filter((value): value is string => !!value).map(Date.parse);
  const expires = dates.length ? Math.min(...dates) : null;
  const allowed = canUseRemoteService(state, now);
  useEffect(() => {
    if (expires === null || !Number.isFinite(expires) || expires <= now) return;
    const timer = setTimeout(() => setNow(Date.now()), Math.min(expires - now + 1, 2147483647));
    return () => clearTimeout(timer);
  }, [expires, now]);
  useEffect(() => { if (!allowed) setRemote(false); }, [allowed]);
  async function copy() {
    try { await navigator.clipboard.writeText("xrwCoder"); setFeedback("微信号已复制"); }
    catch { setFeedback("复制失败，请选中微信号手动复制"); }
  }
  return <>
    <nav className="library-entrances" aria-label="游戏库与会员服务">
      <Button className="library-mine" primary={!supported} aria-pressed={!supported} onClick={() => onSupported(false)}>
        <Gamepad2 size={22}/>我的游戏
      </Button>
      <button type="button" className="library-discover" aria-pressed={supported} onClick={() => onSupported(true)}>
        <span className="library-discover-icon"><Sparkles size={25}/></span>
        <span className="library-entrance-copy"><strong>支持游戏 <span className="library-count"><Shine active={effects.active}>100+</Shine></span></strong>
          <small>实测 DLSS 5 名单 · 高级会员</small></span>
        <ArrowUpRight size={20}/>
      </button>
      <Button className="library-remote" disabled={checking} onClick={async () => {
        if (!canUseRemoteService(state)) { onUpgrade("mu-remote-once"); return; }
        setFeedback(""); setError(""); setChecking(true);
        try { await rpc("remoteService"); setRemote(true); }
        catch (e) { setError((e as Error).message); }
        finally { setChecking(false); }
      }}>
        <Headset size={21}/><span className="library-entrance-copy"><strong>一对一远程服务</strong><small>{checking ? "正在验证指导权益…" : (state.account?.membership.remoteSessionsRemaining || 0)>0 ? `剩余 ${state.account!.membership.remoteSessionsRemaining} 次指导` : "联系作者，协助配置"}</small></span>
        {allowed ? <Crown size={16}/> : <LockKeyhole size={16}/>}
        <span className="library-remote-label">终身含3次 · 可按次购买</span>
      </Button>
    </nav>
    {error && <p className="error" role="alert">{error}</p>}
    {remote && allowed && <Modal title="一对一远程服务" className="remote-service-modal" onClose={() => setRemote(false)}>
      <div className="remote-service-contact">
        <span className="remote-service-symbol"><Headset size={30}/></span>
        <h2>添加作者微信</h2><p>发送你的 MU 账户、游戏名称和遇到的问题，联系我安排远程协助。</p>
        {typeof state.account?.membership.remoteSessionsRemaining === "number" && <p>可预约指导：{state.account.membership.remoteSessionsRemaining} 次；联系和查看微信不扣次数，实际指导完成后核销。</p>}
        <img src="/artwork/wechat-xrwCoder.jpg" alt="作者微信二维码"/>
        <p className="remote-service-wechat">微信号 <strong>xrwCoder</strong></p>
        <Button primary onClick={copy}>{feedback === "微信号已复制" ? <Check size={18}/> : <Copy size={18}/>}复制微信号</Button>
        <p role="status">{feedback || "微信扫码或搜索微信号添加"}</p>
      </div>
    </Modal>}
  </>;
}
