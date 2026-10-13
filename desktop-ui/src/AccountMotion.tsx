import { lazy, Suspense, useEffect, useMemo, useState } from "react";
import { Settings } from "lucide-react";
import type { Account } from "./types";
import "./account-motion.css";
const Lanyard = lazy(() => import("./reactbits/Lanyard"));
const loadGridScan = () => import("./reactbits/GridScan");
const GridScan = lazy(loadGridScan);
export const prepareLoginMotion = () => { void loadGridScan().catch(() => {}); };
const escape = (s: string) => s.replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&apos;"}[c]!));
function badgeImage(account: Account, back = false) {
  const email = escape(account.email);
  const tier = account.membership.tier === "pro" ? "MU 会员" : "MU 玩家";
  const identity = escape(account.id.slice(0, 16));
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="600" height="900" viewBox="0 0 600 900">
    <defs><linearGradient id="bg" x2=".9" y2="1"><stop stop-color="#263b35"/><stop offset="1" stop-color="#111c22"/></linearGradient><pattern id="dots" width="18" height="18" patternUnits="userSpaceOnUse"><circle cx="2" cy="2" r="1" fill="#adff18" opacity=".1"/></pattern></defs>
    <rect width="600" height="900" rx="40" fill="url(#bg)"/><rect width="600" height="900" fill="url(#dots)"/>
    <rect x="250" y="25" width="100" height="18" rx="9" fill="#080e11"/>
    <text x="42" y="102" fill="#adff18" font-size="25" font-family="Microsoft YaHei, PingFang SC, Arial, sans-serif" font-weight="800">AMD DLSS MU</text><text x="555" y="102" text-anchor="end" fill="#9aaca4" font-size="16" font-family="Microsoft YaHei, PingFang SC, Arial, sans-serif">通行证</text>
    <path d="M42 130H558" stroke="#ffffff22"/>
    <text x="42" y="222" fill="#f0f8f3" font-size="58" font-weight="800" font-family="Microsoft YaHei, PingFang SC, Arial, sans-serif">${back ? "开始游戏" : tier}</text>
    <circle cx="300" cy="360" r="80" fill="#adff1810" stroke="#adff1870"/><text x="300" y="380" text-anchor="middle" fill="#adff18" font-size="65" font-weight="900" font-family="Microsoft YaHei, PingFang SC, Arial, sans-serif">MU</text>
    <text x="300" y="500" text-anchor="middle" fill="#e0eae5" font-size="23" font-family="Microsoft YaHei, PingFang SC, Arial, sans-serif" textLength="${Math.min(480,account.email.length*13)}" lengthAdjust="spacingAndGlyphs">${email}</text>
    <text x="300" y="545" text-anchor="middle" fill="#9baea6" font-size="18" font-family="Microsoft YaHei, PingFang SC, Arial, sans-serif">${back ? "你的游戏，你的配置" : "玩家编号 · " + identity}</text>
    <path d="M42 630H558" stroke="#ffffff22"/><text x="42" y="690" fill="#adff18" font-size="22" font-family="Microsoft YaHei, PingFang SC, Arial, sans-serif">${back ? "AMD DLSS MU" : "画质优先 / 帧率优先 / 自定义"}</text>
    <text x="42" y="752" fill="#9baca5" font-size="20" font-family="Microsoft YaHei, PingFang SC, Arial, sans-serif">${back ? "分享好方案，玩出你的风格" : "一键配置，畅快开玩"}</text>
    <g stroke="#cbd9cf" stroke-width="4">${Array.from({length:35},(_,i)=>`<path d="M${44+i*7} 802v${i%3===0?36:28}"/>`).join("")}</g><text x="555" y="835" text-anchor="end" fill="#d5e8d9" font-size="22" font-family="Microsoft YaHei, PingFang SC, Arial, sans-serif">MU</text>
  </svg>`;
  return "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);
}
export function AccountBadge({account, active, onClose, onSettings}: {account:Account; active:boolean; onClose:()=>void; onSettings:()=>void}) {
  const [unavailable,setUnavailable]=useState(false);
  const front=useMemo(()=>badgeImage(account),[account.id,account.email,account.membership.tier]);
  const back=useMemo(()=>badgeImage(account,true),[account.id,account.email,account.membership.tier]);
  useEffect(() => {
    const close = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [onClose]);
  return <>
    <button className={`account-badge-mask${active ? " animated" : ""}`} aria-label="收起账户工牌" onClick={onClose} />
    <aside className={`account-badge-drop${active ? " animated" : ""}`} aria-label="我的账户工牌">
    <button className="badge-settings" aria-label="账户设置" onClick={onSettings}><Settings size={17} /></button>
    <button className="badge-close" aria-label="收起工牌" onClick={onClose}>×</button>
    <div className="badge-stage" aria-label="可拖拽的 MU 账户工牌">
      {active && !unavailable ? <Suspense fallback={<img className="badge-static" src={front} alt="MU 账户工牌" />}>
        <Lanyard frontImage={front} backImage={back} cardColor="#182723" strapColor="#638a33" metal="graphite"
          finish="matte" size={.68} strapLength={.35} damping={.65} breeze={.12} intro={false} onUnsupported={()=>setUnavailable(true)} />
      </Suspense> : <img className="badge-static" src={front} alt="MU 账户工牌" />}
    </div>
  </aside></>;
}
export function LoginTransition({onComplete}: {onComplete:()=>void}) {
  useEffect(()=>{const timer=setTimeout(onComplete,1500);return()=>clearTimeout(timer);},[onComplete]);
  return <div className="login-warp" role="status" aria-label="登录成功，正在进入 MU">
    <Suspense fallback={<div className="login-warp-fallback" />}><GridScan linesColor="#48652b" scanColor="#adff18" cells={8} depth={4}
      scanDuration={.85} scanDelay={0} scanWidth={.35} glow={.5} trail={.35} autoScan intro={false}
      interactive={false} enableWebcam={false} enableGyro={false} dpr={1.5} /></Suspense>
    <div className="login-warp-label"><strong>登录成功</strong><span>进入 MU</span></div>
  </div>;
}
