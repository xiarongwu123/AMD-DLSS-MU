import { useEffect, useState } from "react";
import { LockKeyhole } from "lucide-react";
import type { Order } from "./types";
export function PaymentCodes({payment,amountMinor}: {payment:NonNullable<Order["payment"]>;amountMinor:number}) {
  const channels=payment.channels?.length ? payment.channels : payment.qrImageUrl ? [{id:"manual",name:"扫码付款",qrImageUrl:payment.qrImageUrl}] : [];
  const [selected,setSelected]=useState(channels[0]?.id || ""),[failed,setFailed]=useState(false);
  const ids=channels.map(c=>c.id).join(",");
  useEffect(()=>{setSelected(channels[0]?.id || "");setFailed(false);},[ids]);
  const channel=channels.find(c=>c.id===selected) || channels[0];
  useEffect(()=>setFailed(false),[channel?.qrImageUrl]);
  if(!channel) return <p className="muted">收款码暂未提供，请联系微信 xrwCoder 核对收款方式。</p>;
  return <section className="payment-codes" aria-label="扫码付款">
    <div className="payment-methods" role="group" aria-label="支付方式">{channels.map(c=><button type="button" key={c.id} aria-pressed={channel.id===c.id} className={`${channel.id===c.id ? "active" : ""} ${c.id}`} onClick={()=>setSelected(c.id)}>{c.name}</button>)}</div>
    <div className="payment-code-content">
      <div className="payment-code-image">{failed ? <p role="alert">收款码加载失败，请刷新订单或联系微信 xrwCoder。</p> : <img src={channel.qrImageUrl} alt={`${channel.name}收款码`} onError={()=>setFailed(true)}/>}</div>
      <div className="payment-code-guide"><span className="eyebrow">{channel.name}</span><h3>扫码支付 ¥{(amountMinor/100).toFixed(2)}</h3>
        <ol><li>打开{channel.id==="alipay" ? "支付宝" : channel.id==="wechat" ? "微信" : "支付软件"}扫一扫。</li><li>填写上方金额，核对收款人后付款。</li><li>付款备注订单号；无法备注时，将凭证与订单号发给微信 xrwCoder。</li><li>点击「我已付款 · 提交核对」，等待人工开通。</li></ol>
        <p><LockKeyhole size={14}/> 这是固定收款码，不会自动填入金额或订单号。</p>
      </div>
    </div>
  </section>;
}
