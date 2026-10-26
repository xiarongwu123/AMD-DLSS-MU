import { ArrowRight, Check, Crown, Headset, Image, SlidersHorizontal, Sparkles, Zap } from "lucide-react";
import type { ReactNode } from "react";
import SpotlightCard from "./reactbits/SpotlightCard";
import ShinyText from "./reactbits/ShinyText";
import { useVisualEffects } from "./VisualEffects";
import type { Account, Plan } from "./types";
const featureNames: Record<string,string> = {
  "library.manage":"管理本地游戏", "dlss.configure":"DLSS 5 配置", "optiscaler.configure":"OptiScaler 配置",
  "magpie.launch":"大力喜鹊", "game.launch":"启动游戏", "game.restore":"恢复配置",
  "configuration.edit":"自定义配置", "diagnostics.use":"一键排查", "app.update":"软件更新",
  "compatibility.read":"兼容记录", "compatibility.submit":"提交实测反馈", "compatibility.public.read":"公开兼容记录",
  "games.supported.view":"100+ 实测支持游戏库",
  "schemes.premium.configure":"五类高级方案 · 作者调校 / 兼容 / 网游 / AMD大力喜鹊 / 老游戏",
  "schemes.game.view":"查看游戏专属推荐与配置参数"
};
export function PlanWall({plans,selected,onSelect,features,disabled}: {
  plans:Plan[];selected:string;onSelect:(id:string)=>void;features:Account["features"];disabled:boolean;
}) {
  const effects=useVisualEffects();
  const premium=features.filter(f=>f.enabled !== false && f.minimumTier==="pro" && f.key!=="services.remote");
  const hasCatalog=premium.some(f=>f.key==="games.supported.view");
  const basic=features.filter(f=>f.enabled !== false && f.minimumTier==="standard" && f.key!=="services.remote");
  return <div className="plan-wall" data-motion={effects.active ? "on" : "off"}>
    <div className="plan-wall-aura" aria-hidden="true"><i/><i/><i/></div>
    <div className="plan-wall-heading"><h2>少走弯路，把时间留给游戏</h2><p>多套路线，一键配置。MU 准备所需文件，不用到处找资源。</p></div>
    <div className="plan-route-story" aria-label="MU 配置路线">
      <div><Image size={20}/><span><strong>画质优先</strong><small>DLSS 5 神经渲染 · 更在意画面</small></span></div>
      <div><Zap size={20}/><span><strong>帧率优先</strong><small>DLSS 5 + XeFG · 更在意流畅</small></span></div>
      <div><SlidersHorizontal size={20}/><span><strong>自定义方案</strong><small>自己选路线，按喜好配置</small></span></div>
    </div>
    <p className="plan-route-note">配置路线按设备和游戏条件提供。高级会员额外解锁下方权益。</p>
    <div className="plan-options" role="radiogroup" aria-label="会员套餐">
      {plans.map((p,i)=>{
        const remoteOnly=p.grantsMembership===false,lifetime=!remoteOnly&&p.durationDays===0,assisted=!remoteOnly&&!lifetime&&(p.configurationSessions || 0)>0;
        const sessions=p.configurationSessions || 0;
        const price=(p.priceMinor/100).toFixed(p.priceMinor % 100 === 0 ? 0 : p.priceMinor % 10 === 0 ? 1 : 2);
        return <PlanVisual active={effects.active} lifetime={lifetime} selected={selected===p.id} key={p.id}><button type="button" role="radio" aria-checked={selected===p.id} tabIndex={selected===p.id ? 0 : -1} disabled={disabled}
          className={`plan-option ${selected===p.id ? "selected" : ""} ${lifetime ? "lifetime" : ""}`} key={p.id} onClick={()=>onSelect(p.id)}
          onKeyDown={e=>{if(["ArrowLeft","ArrowRight","ArrowUp","ArrowDown"].includes(e.key)){e.preventDefault();const step=e.key==="ArrowLeft"||e.key==="ArrowUp"?-1:1;const next=(i+step+plans.length)%plans.length;onSelect(plans[next].id);(e.currentTarget.closest('[role="radiogroup"]')?.querySelectorAll<HTMLElement>('[role="radio"]')[next])?.focus();}}}>
          {lifetime && <Crown className="plan-crown-watermark" size={155} strokeWidth={.7} aria-hidden="true"/>}
          <span className="plan-type">{assisted||remoteOnly ? <Headset size={22}/> : lifetime ? <Crown size={22}/> : <Sparkles size={22}/>}<span>{lifetime ? "终身通行证" : remoteOnly ? "单次预约" : assisted ? "配置有人帮" : "按月体验"}</span></span>
          <strong className="plan-name">{assisted ? "高级 Plus 会员" : lifetime && effects.active ? <ShinyText color="#f4d29b" shineColor="#fff7de" speed={2.4} delay={3}>{p.name}</ShinyText> : p.name}</strong>
          {lifetime && <span className="plan-exclusive-label">终身专属权益</span>}
          <span className="plan-price"><small>¥</small>{lifetime && effects.active ? <ShinyText color="#f4d29b" shineColor="#fff7de" speed={2.4} delay={3} shineWidth={20}>{price}</ShinyText> : price}<em>{remoteOnly ? "/ 次" : lifetime ? "/ 终身" : assisted ? "/ 套餐" : p.durationDays===30 ? "/ 月" : `/ ${p.durationDays} 天`}</em></span>
          <span className="plan-pitch">{lifetime ? sessions>0 ? `终身解锁，还有 ${sessions} 次远程指导` : "长期使用，一次开通就够" : remoteOnly ? "遇到配置难题，单次找我帮忙" : assisted ? "不会配置？我帮你完成" : "想玩之前，先看看能不能开"}</span>
          <span className="plan-duration">{remoteOnly ? "按次购买，不附带会员" : lifetime ? "终身高级会员，不再续费" : `${p.durationDays} 天${assisted ? "高级 Plus 会员" : "高级会员"}`}</span>
          <div className="plan-benefits">
            {!remoteOnly && <span className="plan-service"><Check size={15}/>{lifetime ? "终身享用所有付费软件功能" : "享用已开放的高级会员功能"}</span>}
            {!remoteOnly && hasCatalog && <>
              <span className="plan-service"><Check size={15}/>{lifetime ? "终身查看100+实测游戏库" : "解锁100+用户实测游戏"}</span>
              {!assisted && !lifetime && <span className="plan-service"><Check size={15}/> 按名称搜索，少走试错弯路</span>}
            </>}
            {lifetime && <>
              <span className="plan-service lifetime-benefit"><Check size={15}/> 专属功能定制</span>
              <span className="plan-service lifetime-benefit"><Check size={15}/> 专属优化功能</span>
            </>}
            {sessions>0 && <>
              <span className="plan-service highlight"><Check size={15}/> 专属一对一远程服务入口</span>
              <span className="plan-service highlight"><Check size={15}/> 含 {sessions} 次远程指导</span>
              <span className="plan-service"><Check size={15}/> 根据设备与游戏协助配置</span>
            </>}
          </div>
          <span className="plan-delivery">{lifetime ? "定制与优化、远程指导均通过微信预约；指导用完可按次购买" : remoteOnly ? "核对到账后微信预约；每次指导单独核销" : assisted ? "到账后联系微信，预约一对一指导" : "适合先体验 · 不含远程指导"}</span>
          <span className="plan-select">{selected===p.id ? <><Check size={17}/> 已选中</> : <>选择此套餐<ArrowRight size={17}/></>}</span>
        </button></PlanVisual>;
      })}
    </div>
    <section className="plan-permissions" aria-label="会员权限"><h3>三个会员套餐均可使用</h3><p className="plan-route-note">单次远程指导仅提供人工服务，不解锁下方会员功能。</p>
      <div className="plan-permission-list">{premium.map(f=><span key={f.key}><Check size={15}/>{featureNames[f.key] || "其他已开放高级会员功能"}</span>)}</div>
      {!premium.length && <p>当前没有已开放的高级会员专属权限，请先联系管理员确认。</p>}
      {!!basic.length && <details><summary>普通账户也能使用的基础功能</summary><div className="plan-permission-list">{basic.map(f=><span key={f.key}><Check size={15}/>{featureNames[f.key] || "其他已开放基础功能"}</span>)}</div></details>}
    </section>
  </div>;
}

function PlanVisual({active,lifetime,selected,children}: {active:boolean;lifetime:boolean;selected:boolean;children:ReactNode}) {
  const className=`plan-visual ${lifetime ? "featured" : ""} ${selected ? "is-selected" : ""}`;
  return active ? <SpotlightCard className={className} spotlightColor={lifetime ? "#f0c477" : "#adff18"} intensity={.18} borderGlow={.65} spotlightSize={300} flare={false} grain={0} theme="dark">{children}</SpotlightCard> : <div className={className}>{children}</div>;
}
