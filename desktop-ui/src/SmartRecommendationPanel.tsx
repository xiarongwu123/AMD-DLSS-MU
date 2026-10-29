import { useEffect, useRef, useState } from "react";
import { ArrowUpRight, Crown, LockKeyhole, RefreshCw, ShieldCheck } from "lucide-react";
import { Button } from "./components";
import { rpc } from "./bridge";
import type { State } from "./types";
import { canUseMemberFeature } from "./membership";
import "./smart-recommendation.css";

export type SmartContext = { confirmedApi: string; confirmedInput: string; windowed: string };
type Candidate = { id:string; scheme:string; name:string; state:string; score:number; level:string; automatic:boolean; premium:boolean; external:boolean;
  evidence:string; reasons:string[]; issues:{code:string;message:string;state:string}[];
  configuration:{backend:string;componentVersion:string;proxy:string;passes:number;graphicsApi:string;ini:Record<string,Record<string,string>>} };
type Result = {planId:string;gameName:string;goal:string;ruleRevision:string;ruleSource:string;expiresUtc:string;
  environment:{fields:Record<string,{value:string;confidence:string;source:string}>};recommended:Candidate|null;freeAlternative:Candidate|null;candidates:Candidate[];notices:string[]};
type Verification = {state:string;installed:boolean;loaded:boolean;active:boolean;experienceConfirmed:boolean;failureCodes:string[];evidence:string[]};
const fieldNames:Record<string,string>={gpuModel:"显卡型号",gpuVendor:"GPU厂商",deviceId:"PCI设备ID",architecture:"架构",driver:"Windows驱动版本",vramMb:"专用显存(MB)",vramBudgetMb:"显存预算(MB)",os:"系统",osBuild:"系统构建",gameId:"标准游戏ID",gameVersion:"游戏版本",gameBuild:"EXE构建摘要",bitness:"进程位数",api:"实际图形接口",upscalerInput:"超分输入",windowed:"窗口模式",resolution:"显示分辨率"};
const evidenceNames:Record<string,string>={verified:"已验证",community:"社区验证",experimental:"实验性",unverified:"未验证"};
const labels:Record<string,string> = {INSTALLED:"文件已部署，运行效果未验证",LOADED:"后端已加载，功能生效尚未确认",ACTIVE:"已观察到功能执行",EXPERIENCE_CONFIRMED:"体验已确认",FAILED:"检测到失败",UNKNOWN:"无法确认是否生效"};
export function SmartRecommendationPanel({gameId,gameTitle,state,disabled,selected,onUpgrade,onContextChange}: {
  gameId:string;gameTitle:string;state:State;disabled:boolean;selected:string|null;onUpgrade:()=>void;onContextChange?:(value:SmartContext)=>void;
}) {
  const [clock,setClock]=useState(Date.now);
  const expiry=state.account?.membership.expiresAt;
  const allowed=canUseMemberFeature(state,"schemes.game.view",clock);
  const mayConfigure=canUseMemberFeature(state,"schemes.premium.configure",clock);
  const [context,setContext]=useState<SmartContext>({confirmedApi:"",confirmedInput:"",windowed:""});
  const [goal,setGoal]=useState("stability"),[scope,setScope]=useState("");
  const [result,setResult]=useState<Result|null>(null),[error,setError]=useState(""),[busy,setBusy]=useState(false);
  const [others,setOthers]=useState(false),[details,setDetails]=useState<string|null>(null),[experimental,setExperimental]=useState(false);
  const [verification,setVerification]=useState<Verification|null>(null),[fallback,setFallback]=useState<{message:string;alternativeIds:string[]}|null>(null);
  const [applied,setApplied]=useState(false);
  const [feedback,setFeedback]=useState("unknown"),[consent,setConsent]=useState(false),[message,setMessage]=useState("");
  const generation=useRef(0);
  useEffect(()=>{if(!expiry||Date.parse(expiry)<=clock)return;const delay=Date.parse(expiry)-Date.now();if(!Number.isFinite(delay)||delay<=0){setClock(Date.now());return;}const timer=setTimeout(()=>setClock(Date.now()),Math.min(delay+1,2147483647));return()=>clearTimeout(timer);},[expiry,clock]);
  useEffect(()=>{generation.current++;setBusy(false);setApplied(false);setResult(null);setVerification(null);setError("");setMessage("");},[gameId,allowed,state.account?.id]);
  useEffect(()=>{generation.current++;setBusy(false);setResult(null);setExperimental(false);onContextChange?.(context);},[context,goal,onContextChange]);
  useEffect(()=>()=>{generation.current++;},[]);
  const args={gameId,...context,goal,scope};
  async function analyze(wantedScope="") {
    if(!allowed){onUpgrade();return;}
    const request=++generation.current;setBusy(true);setError("");setScope(wantedScope);setApplied(false);setVerification(null);setMessage("");
    try{
      const value=await rpc<Result|null>("gameRecommendation",{...args,scope:wantedScope});
      if(request!==generation.current)return;
      if(!value?.environment||!Array.isArray(value.candidates))throw new Error("未收到真实检测结果。请使用 Windows 客户端；预览不会模拟推荐成功。");
      const available = (candidate: Candidate | null) => candidate && !["compat", "amd-magpie", "legacy"].includes(candidate.scheme) ? candidate : null;
      setResult({...value,recommended:available(value.recommended),freeAlternative:available(value.freeAlternative),candidates:value.candidates.filter(candidate=>available(candidate)!==null)});
    }catch(e){if(request===generation.current)setError((e as Error).message);}
    finally{if(request===generation.current)setBusy(false);}
  }
  async function apply(candidate:Candidate) {
    if(candidate.premium&&!mayConfigure){onUpgrade();return;}
    if(!result)return;
    setBusy(true);setError("");setMessage("");
    try{
      const response=await rpc<{external:boolean;verification:Verification}|null>("recommendationApply",{...args,planId:result.planId,candidateId:candidate.id,confirmExperimental:experimental});
      if(response){setApplied(true);setVerification(response.verification);setMessage(response.external?"已交给外部工具管理；MU未将此操作记录为游戏配置成功。":"已执行安装检查。文件部署、后端加载和功能生效会分别显示。");}
    }catch(e){setError((e as Error).message);}finally{setBusy(false);}
  }
  async function verify() {
    setBusy(true);setError("");
    try{const response=await rpc<{verification:Verification;fallback:{message:string;alternativeIds:string[]}}>("recommendationVerify",{gameId,feedback});setVerification(response.verification);setFallback(response.fallback);}
    catch(e){setError((e as Error).message);}finally{setBusy(false);}
  }
  async function upload() {
    if(!consent)return;setBusy(true);setError("");
    try{await rpc("recommendationFeedback",{...args,feedback,consent});setMessage("已上传匿名验证记录。未知状态不会计为成功。");}
    catch(e){setError((e as Error).message);}finally{setBusy(false);}
  }
  function candidateView(candidate:Candidate) {
    const blocked=candidate.state==="BLOCKED"||candidate.state==="REQUIRE_ACTION";
    return <article className="smart-candidate" key={candidate.id}>
      <div className="smart-candidate-heading"><strong>{candidate.name}</strong><span>{evidenceNames[candidate.evidence]||"未验证"} · {candidate.level}{candidate.score>0?` · ${candidate.score}/100`:""}</span></div>
      <p>{candidate.configuration.backend} · {candidate.configuration.componentVersion} · {candidate.configuration.proxy} · {candidate.configuration.passes} Pass</p>
      <ul>{candidate.reasons.map((reason,i)=><li key={i}>{reason}</li>)}{candidate.issues.map(issue=><li key={issue.code+issue.message}><b>{issue.code}</b> · {issue.message}</li>)}</ul>
      <div className="actions"><Button disabled={busy||disabled} onClick={()=>setDetails(details===candidate.id?null:candidate.id)}>查看配置</Button>
        <Button primary disabled={busy||disabled||applied||blocked||!candidate.automatic&&!experimental} onClick={()=>void apply(candidate)}>{candidate.premium&&!mayConfigure?"开通后使用":candidate.external?"打开外部工具":candidate.automatic?"一键应用":"确认尝试实验配置"}</Button></div>
      {details===candidate.id&&<pre className="smart-config">{Object.entries(candidate.configuration.ini).map(([section,values])=>`[${section}]\n${Object.entries(values).map(([k,v])=>`${k}=${v}`).join("\n")}`).join("\n\n")||"此方案由外部工具管理，不直接写入上述配置。"}</pre>}
    </article>;
  }
  return <section className="game-specific-preset smart-recommendation" aria-label="当前游戏专属推荐">
    <div className="game-specific-heading"><div><span className="game-specific-kicker"><Crown size={14}/>游戏专属推荐</span><h4>专门针对「{gameTitle}」的配置</h4></div><span className="scheme-access is-premium">高级会员专享</span></div>
    <details className="smart-confirmation"><summary>确认游戏实际设置（静态检测不能代替实际运行接口）</summary>
      <div className="smart-inputs"><label>游戏实际图形接口<select aria-label="确认实际图形接口" value={context.confirmedApi} onChange={e=>setContext(v=>({...v,confirmedApi:e.target.value}))}>
        <option value="">未确认，保留未知/推断</option>{["dx8","dx9","dx11","dx12","vulkan","opengl"].map(v=><option key={v} value={v}>{v}</option>)}</select></label>
      <label>游戏支持的超分输入<select aria-label="确认游戏超分输入" value={context.confirmedInput} onChange={e=>setContext(v=>({...v,confirmedInput:e.target.value}))}><option value="">未确认</option>{["dlss","xess","fsr","none"].map(v=><option key={v} value={v}>{v}</option>)}</select></label>
      <label>窗口模式<select aria-label="确认窗口模式" value={context.windowed} onChange={e=>setContext(v=>({...v,windowed:e.target.value}))}><option value="">未确认</option><option value="true">已设为窗口模式</option><option value="false">非窗口模式</option></select></label></div>
    </details>
    <div className="smart-goal"><label className="smart-goal-field"><span>配置目标</span><select aria-label="智能推荐目标" value={goal} onChange={e=>setGoal(e.target.value)}><option value="stability">稳定优先</option><option value="framerate">高帧率</option><option value="quality">高画质</option></select></label>
      <Button className="smart-analyze" disabled={busy||disabled} onClick={()=>void analyze("")}>{allowed?<ArrowUpRight size={17}/>:<LockKeyhole size={17}/>} {allowed?"查看游戏专属方案":"开通会员 · 查看专属方案"}</Button>
      {allowed&&selected&&<Button disabled={busy||disabled} onClick={()=>void analyze(selected)}>仅评估当前方案</Button>}</div>
    {busy&&<p role="status">正在检测游戏与设备……检测 GPU、游戏版本、图形接口和已安装组件，并核对兼容规则。</p>}
    {error&&<p role="alert" className="error">{error}</p>}
    {result&&allowed&&<>
      <h4>{result.recommended?"已找到适合当前设备的配置":"当前游戏暂无已验证的自动配置"}</h4>
      <p>当前游戏：{result.gameName} · 当前显卡：{result.environment.fields.gpuModel?.value||"未知"}</p>
      <p className="muted">规则 {result.ruleRevision} · {result.ruleSource==="cache"?"本机有效加密缓存":result.ruleSource==="remote"?"已认证规则服务":"规则不可用"} · 不以 FPS 生成推荐</p>
      {result.notices.map((notice,i)=><p key={i} className="muted">{notice}</p>)}
      <details><summary>查看检测来源与未知项目</summary><dl className="smart-facts">{Object.entries(result.environment.fields).map(([key,fact])=><div key={key}><dt>{fieldNames[key]||key}</dt><dd>{fact.value} · {fact.confidence}<small>{fact.source}</small></dd></div>)}</dl></details>
      {result.recommended&&candidateView(result.recommended)}
      {result.freeAlternative&&result.freeAlternative.id!==result.recommended?.id&&<><h4>免费替代方案</h4>{candidateView(result.freeAlternative)}</>}
      <Button onClick={()=>setOthers(!others)}>{others?"收起其他方案":"查看其他方案与需要处理的条件"}</Button>
      {others&&<><label className="smart-consent"><input type="checkbox" checked={experimental} onChange={e=>setExperimental(e.target.checked)}/>我知悉实验候选未完成稳定性验证，愿意主动尝试；硬性限制仍不可跳过。</label>
        {result.candidates.filter(c=>c.id!==result.recommended?.id).map(candidateView)}</>}
    </>}
    {allowed&&<div className="smart-verification">
      <div className="actions"><Button disabled={busy||disabled} onClick={()=>rpc("launch",{gameId}).catch(e=>setError(e.message))}>启动游戏</Button><Button disabled={busy||disabled} onClick={()=>void verify()}><RefreshCw size={16}/>验证本次运行</Button>
      <Button disabled={busy||disabled} onClick={async()=>{setBusy(true);try{await rpc("restore",{gameId});setResult(null);setVerification(null);setMessage("恢复流程已结束，请重新检测；以现有恢复记录为准。");}catch(e){setError((e as Error).message);}finally{setBusy(false);}}}>恢复之前配置</Button></div>
      {verification&&<><p><ShieldCheck size={16}/> {labels[verification.state]||verification.state}</p><ul>{verification.evidence.map((s,i)=><li key={i}>{s}</li>)}</ul>{verification.failureCodes.map(code=><code key={code}>{code} </code>)}</>}
      {fallback&&<div><p>{fallback.message}</p>{fallback.alternativeIds.map(id=>{
      const alternative=result?.candidates.find(c=>c.id===id);return alternative&&<Button key={id} disabled={busy||disabled} onClick={async()=>{
        setBusy(true);setError("");try{await rpc("restore",{gameId});await analyze(alternative.scheme);setDetails(id);setMessage("已进入恢复与重新检测流程，请核对新的候选及阻断条件，再确认应用。");}
        catch(e){setError((e as Error).message);}finally{setBusy(false);}
      }}>恢复并检查「{alternative.name}」</Button>;
    })}</div>}
      <details><summary>反馈实际体验与匿名验证数据</summary><label>实际体验<select aria-label="实际体验反馈" value={feedback} onChange={e=>setFeedback(e.target.value)}><option value="unknown">尚未确认</option><option value="acceptable">体验可接受</option><option value="image_abnormal">画面异常</option><option value="crashed">发生崩溃</option></select></label>
        <p className="muted">仅上传匿名化设备标识、游戏构建摘要、显卡/驱动、方案参数版本和分阶段验证结果。不会上传完整路径、用户名或原始日志。用户反馈不替代功能执行证据。</p>
        <label className="smart-consent"><input type="checkbox" checked={consent} onChange={e=>setConsent(e.target.checked)}/>我同意本次上传，可随时取消；不会自动上传后续记录。</label><Button disabled={busy||!consent} onClick={()=>void upload()}>上传本次验证</Button>
      </details>
    </div>}
    {message&&<p role="status">{message}</p>}
  </section>;
}
