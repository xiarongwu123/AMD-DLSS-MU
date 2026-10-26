import { useEffect, useRef, useState } from "react";
import { Check, Copy, MessageCircle, X } from "lucide-react";
import "./contact-float.css";

const wechat = "xrwCoder";
export function ContactFloat() {
  const [open, setOpen] = useState(false);
  const [feedback, setFeedback] = useState("");
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.stopPropagation();
        setOpen(false);
        trigger.current?.focus();
      }
    };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape, true);
    return () => {
      document.removeEventListener("pointerdown", outside);
      document.removeEventListener("keydown", escape, true);
    };
  }, [open]);
  async function copy() {
    try {
      await navigator.clipboard.writeText(wechat);
      setFeedback("微信号已复制");
    } catch {
      setFeedback("复制失败，请选中微信号手动复制");
    }
  }
  return <div className="contact-float" ref={root}>
    {open && <section className="contact-card" id="mu-contact-card" aria-label="微信联系我">
      <header><div><MessageCircle size={21} /><strong>联系我</strong></div>
        <button className="icon-button" aria-label="关闭联系卡片" onClick={() => { setOpen(false); trigger.current?.focus(); }}><X size={18} /></button>
      </header>
      <img src="/artwork/wechat-xrwCoder.jpg" alt="添加作者微信的二维码" />
      <p className="contact-wechat">微信号 <strong>{wechat}</strong></p>
      <button className="button primary" onClick={copy}>{feedback === "微信号已复制" ? <Check size={17} /> : <Copy size={17} />}复制微信号</button>
      <p className="contact-hint" role="status">{feedback || "微信扫码或搜索微信号添加"}</p>
    </section>}
    <button ref={trigger} className="contact-trigger" aria-expanded={open} aria-controls="mu-contact-card"
      onClick={() => { setOpen(!open); setFeedback(""); }}><MessageCircle size={22} /><span>遇到问题了？联系我</span></button>
  </div>;
}
