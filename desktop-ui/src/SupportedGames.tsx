import { useEffect, useState } from "react";
import { ArrowUpRight, Check, Gamepad2, LockKeyhole, Search, Sparkles } from "lucide-react";
import { Button, Empty, Loading, Modal } from "./components";
import { rpc } from "./bridge";
import { Shine, useVisualEffects } from "./VisualEffects";
import type { State, SupportedCatalog, SupportedGame } from "./types";
import { canUseMemberFeature } from "./membership";
import "./supported-games.css";
export function SupportedGames({state,onUpgrade}: {state:State;onUpgrade:()=>void}) {
  const effects=useVisualEffects();
  const [search,setSearch]=useState(""),[page,setPage]=useState(1);
  const [catalog,setCatalog]=useState<SupportedCatalog|null>(null),[error,setError]=useState(""),[retry,setRetry]=useState(0);
  const [selected,setSelected]=useState<SupportedGame|null>(null),[failed,setFailed]=useState<string[]>([]);
  const [now,setNow]=useState(Date.now);
  const expiry=state.account?.membership.expiresAt;
  const expires=expiry ? Date.parse(expiry) : null;
  const canView=canUseMemberFeature(state,"games.supported.view",now);
  useEffect(()=>{
    if (expires === null || !Number.isFinite(expires) || expires <= now) return;
    const timer=setTimeout(()=>setNow(Date.now()),Math.min(expires-now+1,2147483647));
    return()=>clearTimeout(timer);
  },[expires,now]);
  useEffect(()=>{
    setCatalog(null);setSelected(null);setSearch("");setPage(1);setError("");
    if (!canView) return;
    let active=true;
    rpc<SupportedCatalog>("supportedGames").then(data=>{if(active)setCatalog(data);}).catch(e=>{if(active)setError(e.message);});
    return()=>{active=false;};
  },[canView,state.account?.id,expiry,retry]);
  if (!canView) return <section className="supported-games">
    <div className="page-heading"><div><h1>支持游戏</h1><p>高级会员专属游戏库</p></div></div>
    <div className="supported-paywall" data-motion={effects.active ? "on" : "off"}>
      <div className="supported-atmosphere" aria-hidden="true"><i/><i/><i/></div>
      <div className="supported-preview" aria-hidden="true">
        {/* Public decorative artwork only; the paid catalogue stays on the server. */}
        {["1091500","1245620","1551360","990080","1174180","2358720"].map((id,i)=><div className={`supported-poster poster-${i}`} key={id}>
          <img src={`https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/${id}/library_600x900.jpg`} alt="" loading="lazy" onError={e=>{e.currentTarget.style.visibility="hidden";}}/>
        </div>)}
      </div>
      <div className="supported-lock-copy">
        <span className="supported-lock-icon" aria-hidden="true"><LockKeyhole size={26}/><i/></span>
        <p className="supported-eyebrow"><Sparkles size={14}/> 高级会员专属</p>
        <h2 aria-label="开通高级会员查看100+实测游戏"><span className="supported-count"><Shine active={effects.active}>100+</Shine></span><span className="supported-title">实测游戏，等你探索</span></h2>
        <p className="supported-unlock-description">不用逐个试错，看看玩家验证过的 DLSS 5 游戏。</p>
        <Button primary charge className="supported-unlock-button" onClick={onUpgrade}>开通高级会员<ArrowUpRight size={21}/></Button>
        <span className="supported-unlock-footnote">解锁完整名单 · 支持游戏名称搜索</span>
      </div>
    </div>
  </section>;
  if(error) return <section className="supported-games"><h1>支持游戏</h1><p role="alert" className="error">{error}</p><Button onClick={()=>setRetry(r=>r+1)}>重新加载</Button></section>;
  if(!catalog) return <Loading text="正在验证会员权限并读取游戏名单"/>;
  const games=catalog.games.filter(g=>[g.title,g.canonicalName,...g.aliases].some(t=>t.toLowerCase().includes(search.trim().toLowerCase())));
  const pages=Math.max(1,Math.ceil(games.length/24));
  return <section className="supported-games">
    <div className="page-heading"><div><h1>支持游戏</h1><p>以下游戏已有用户反馈可开启 DLSS 5。</p></div>
      <label className="supported-search"><Search size={18}/><input type="search" aria-label="搜索支持游戏" placeholder="搜索游戏名称…" value={search} onChange={e=>{setSearch(e.target.value);setPage(1);}}/></label>
    </div>
    <p className="supported-meta" role="status">{games.length} 款游戏 · 名单更新于 {catalog.asOf}</p>
    <div className="supported-grid">{games.slice((page-1)*24,page*24).map(g=><button className="supported-card" key={g.canonicalName} onClick={()=>setSelected(g)}>
      <div className="supported-cover">{g.steamAppId && !failed.includes(g.steamAppId) ? <img src={`https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/${g.steamAppId}/header.jpg`} alt="" loading="lazy" onError={()=>setFailed(v=>[...v,g.steamAppId!])}/> : <Gamepad2 size={40}/>}</div>
      <div className="supported-copy"><strong>{g.title}</strong><span><Check size={14}/>用户实测可开启</span></div>
    </button>)}</div>
    {!games.length && <Empty title="没有找到这个游戏"/>}
    {pages>1 && <div className="supported-pagination"><Button disabled={page<=1} onClick={()=>setPage(p=>p-1)}>上一页</Button><span>{page} / {pages}</span><Button disabled={page>=pages} onClick={()=>setPage(p=>p+1)}>下一页</Button></div>}
    <p className="supported-note">实测名单来自用户确认；实际效果受游戏版本和配置影响。名称已合并整理，启动器及含义不明的条目待核对。</p>
    {selected && <Modal title={selected.title} onClose={()=>setSelected(null)} footer={<Button primary onClick={()=>setSelected(null)}>知道了</Button>}>
      <p className="supported-status"><Check size={18}/> 已有用户反馈可开启 DLSS 5</p>
      <h3>上报名称</h3><ul>{selected.aliases.map(alias=><li key={alias}>{alias}</li>)}</ul>
      <p>安装游戏后，在「我的游戏」中添加或扫描，再开启 DLSS 5。</p>
      <p className="supported-coming-soon" role="status"><Sparkles size={20}/>Steam 假入库功能正在路上，敬请期待。</p>
    </Modal>}
  </section>;
}
